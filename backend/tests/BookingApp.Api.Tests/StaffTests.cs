using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Identity;
using BookingApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>Staff tools: who may use them, the day schedule, booking outcomes, staff cancel with a reason, and blocking time.</summary>
public abstract class StaffTests(StorageProvider provider) : ApiTest(provider)
{

    private static string Day => $"{ApiFactory.Tomorrow:yyyy-MM-dd}";

    [Fact]
    public async Task Anonymous_callers_get_401_and_customers_get_403()
    {
        await AssertProblem(await Client.GetAsync($"/api/staff/schedule?date={Day}", Ct), 401, "unauthenticated");
        using var customer = await SignedInAs(null);
        await AssertProblem(await customer.GetAsync($"/api/staff/schedule?date={Day}", Ct), 403, "forbidden");
        await AssertProblem(await customer.PostAsync($"/api/staff/bookings/{Guid.NewGuid()}/complete", null, Ct), 403, "forbidden");
    }

    [Fact]
    public async Task Admins_can_use_staff_tools_too()
    {
        using var admin = await SignedInAs("Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/staff/schedule?date={Day}", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_schedule_shows_every_resource_with_its_bookings_and_closures()
    {
        var booked = await BookOk(10);
        var cancelled = await BookOk(12);
        await Client.PostAsync($"/api/bookings/{cancelled.Id}/cancel", null, Ct);
        using var staff = await SignedInAs("Staff");
        await staff.PostAsJsonAsync("/api/staff/closures", new { resourceId = "court-2", start = ApiFactory.At(ApiFactory.Tomorrow, 9), end = ApiFactory.At(ApiFactory.Tomorrow, 11), reason = "Coaching" }, Ct);

        var schedule = (await staff.GetFromJsonAsync<ScheduleResponse>($"/api/staff/schedule?date={Day}", Json, Ct))!;
        Assert.Equal(["court-1", "court-2", "room-a"], schedule.Resources.Select(r => r.ResourceId));
        var court1 = schedule.Resources[0];
        Assert.Equal("06:00", court1.Open);
        Assert.Equal([BookingStatus.Confirmed, BookingStatus.Cancelled], court1.Bookings.Select(b => b.Status));
        Assert.Equal("Nam", court1.Bookings[0].CustomerName);
        Assert.Equal(booked.Id, court1.Bookings[0].Id);
        // It is the day before: staff may cancel, but outcomes must wait until it starts.
        Assert.Equal(new StaffActions(Complete: false, NoShow: false, Cancel: true), court1.Bookings[0].Actions);
        Assert.Equal("Coaching", Assert.Single(schedule.Resources[1].Closures).Reason);
    }

    [Fact]
    public async Task Outcomes_can_be_recorded_once_the_booking_has_started()
    {
        var first = await BookOk(10);
        var second = await BookOk(12);
        using var staff = await SignedInAs("Staff");

        await AssertProblem(await staff.PostAsync($"/api/staff/bookings/{first.Id}/complete", null, Ct), 422, "not_started");

        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 12, 30));
        var completed = (await (await staff.PostAsync($"/api/staff/bookings/{first.Id}/complete", null, Ct)).Content.ReadFromJsonAsync<StaffBookingResponse>(Json, Ct))!;
        Assert.Equal(BookingStatus.Completed, completed.Status);
        Assert.Equal(new StaffActions(false, false, false), completed.Actions);
        var noShow = (await (await staff.PostAsync($"/api/staff/bookings/{second.Id}/no-show", null, Ct)).Content.ReadFromJsonAsync<StaffBookingResponse>(Json, Ct))!;
        Assert.Equal(BookingStatus.NoShow, noShow.Status);

        // Terminal: nothing more can happen to them.
        await AssertProblem(await staff.PostAsync($"/api/staff/bookings/{first.Id}/no-show", null, Ct), 409, "invalid_transition");
        await AssertProblem(await staff.PostAsJsonAsync($"/api/staff/bookings/{second.Id}/cancel", new { reason = "Oops" }, Ct), 409, "invalid_transition");
        // And the customer sees the outcome.
        var mine = (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!;
        Assert.Equal([BookingStatus.Completed, BookingStatus.NoShow], mine.Select(b => b.Status));
    }

    [Fact]
    public async Task Staff_cancel_inside_the_cut_off_frees_the_slot_and_the_customer_sees_why()
    {
        var booked = await BookOk(10);
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 9, 30));   // too late for the customer
        await AssertProblem(await Client.PostAsync($"/api/bookings/{booked.Id}/cancel", null, Ct), 422, "too_late_to_cancel");

        using var staff = await SignedInAs("Staff");
        var empty = await staff.PostAsJsonAsync($"/api/staff/bookings/{booked.Id}/cancel", new { reason = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var cancelled = (await (await staff.PostAsJsonAsync($"/api/staff/bookings/{booked.Id}/cancel", new { reason = "Court flooded" }, Ct))
            .Content.ReadFromJsonAsync<StaffBookingResponse>(Json, Ct))!;
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);

        var mine = (await Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{booked.Id}", Json, Ct))!;
        Assert.Equal("Court flooded", mine.CancellationReason);
        var availability = (await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={Day}", Json, Ct))!;
        Assert.Equal(SlotState.Free, availability.Slots.Single(s => s.LocalStart == "10:00").State);
    }

    [Fact]
    public async Task Blocked_time_cannot_be_booked_and_cannot_cover_a_booking()
    {
        using var staff = await SignedInAs("Staff");
        var response = await staff.PostAsJsonAsync("/api/staff/closures",
            new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 14), end = ApiFactory.At(ApiFactory.Tomorrow, 15), reason = "Net repair" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var closure = (await response.Content.ReadFromJsonAsync<ClosureResponse>(Json, Ct))!;
        Assert.Equal("2026-06-02T14:00", closure.LocalStart);
        await AssertProblem(await Book(14), 422, "resource_closed");

        await BookOk(16);
        await AssertProblem(await staff.PostAsJsonAsync("/api/staff/closures",
            new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 16), end = ApiFactory.At(ApiFactory.Tomorrow, 18), reason = "Event" }, Ct), 409, "bookings_in_the_way");
        await AssertProblem(await staff.PostAsJsonAsync("/api/staff/closures",
            new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 18), end = ApiFactory.At(ApiFactory.Tomorrow, 17), reason = "Backwards" }, Ct), 422, "end_before_start");
    }
}

public sealed class StaffTestsInMemory() : StaffTests(StorageProvider.InMemory);

public sealed class StaffTestsSqlite() : StaffTests(StorageProvider.Sqlite);

public sealed class StaffTestsPostgres() : StaffTests(StorageProvider.Postgres);
