using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>
/// Booking makes a hold that blocks the slot for 10 minutes; the customer confirms it, releases it, or it expires and the
/// slot is free again. Library: 7.9 Case 14, 4.7 "Example 2: A booking lifecycle as a state machine".
/// </summary>
public abstract class HoldTests(StorageProvider provider) : ApiTest(provider)
{
    private async Task<SlotState> StateAt(int hour)
    {
        var availability = await Client.GetFromJsonAsync<AvailabilityResponse>(
            $"/api/resources/court-1/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);
        return availability!.Slots.Single(s => s.LocalStart == $"{hour}:00").State;
    }

    private async Task<BookingStatus?> StoredStatus(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IBookingRepository>().FindAsync(id, Ct))?.Status;
    }

    [Fact]
    public async Task A_hold_blocks_the_slot_for_everyone_else()
    {
        await HoldOk(10);
        Assert.Equal(SlotState.Booked, await StateAt(10));
        using var other = Factory.CreateClient();
        await AssertProblem(await Book(10, client: other), 409, "slot_taken");
    }

    [Fact]
    public async Task Confirming_makes_it_a_booking_and_a_retry_is_harmless()
    {
        var hold = await HoldOk(10);
        Factory.Time.Advance(TimeSpan.FromMinutes(9));

        var confirmed = await ConfirmOk(hold);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        Assert.False(confirmed.CanConfirm);
        Assert.Equal(BookingStatus.Confirmed, (await ConfirmOk(hold)).Status);

        Factory.Time.Advance(TimeSpan.FromMinutes(30));   // long past the hold: a confirmed booking stays
        Assert.Equal(SlotState.Booked, await StateAt(10));
    }

    [Fact]
    public async Task Only_the_customer_who_holds_it_can_confirm()
    {
        var hold = await HoldOk(10);
        using var other = Factory.CreateClient();
        await AssertProblem(await Confirm(hold.Id, other), 403, "not_your_booking");
        await AssertProblem(await Confirm(Guid.NewGuid()), 404, "booking_not_found");
    }

    [Fact]
    public async Task An_expired_hold_frees_the_slot_and_cannot_be_confirmed()
    {
        var hold = await HoldOk(10);
        Factory.Time.Advance(TimeSpan.FromMinutes(10));

        // Free at once, before any job has run.
        Assert.Equal(SlotState.Free, await StateAt(10));
        await AssertProblem(await Confirm(hold.Id), 409, "hold_expired");
        var mine = (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!;
        var expired = Assert.Single(mine);
        Assert.Equal(BookingStatus.Expired, expired.Status);
        Assert.False(expired.CanConfirm);
        Assert.False(expired.CanCancel);

        using var other = Factory.CreateClient();
        Assert.Equal(BookingStatus.Pending, (await HoldOk(10, client: other)).Status);
        Assert.Equal(BookingStatus.Expired, await StoredStatus(hold.Id));   // the insert expired it first
        await AssertProblem(await Confirm(hold.Id), 409, "hold_expired");
    }

    [Fact]
    public async Task The_expiry_job_releases_holds_in_the_background()
    {
        var hold = await HoldOk(10);
        Assert.Equal(BookingStatus.Pending, await StoredStatus(hold.Id));

        // Past the hold and past a sweep: the job (on the fake clock) wakes up and expires it in the database.
        Factory.Time.Advance(TimeSpan.FromMinutes(11));
        for (var i = 0; i < 100 && await StoredStatus(hold.Id) != BookingStatus.Expired; i++)
            await Task.Delay(50, Ct);
        Assert.Equal(BookingStatus.Expired, await StoredStatus(hold.Id));
    }

    [Fact]
    public async Task Releasing_a_hold_frees_the_slot_even_inside_the_cut_off()
    {
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 9));   // an hour before: too late to cancel a booking
        var hold = await HoldOk(10);
        var released = await Client.PostAsync($"/api/bookings/{hold.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(SlotState.Free, await StateAt(10));
        await AssertProblem(await Confirm(hold.Id), 409, "invalid_transition");
    }

    [Fact]
    public async Task A_live_hold_is_in_the_way_of_blocking_time_and_an_expired_one_is_not()
    {
        await HoldOk(10);
        var staff = await SignedInAs("Staff");
        object Closure() => new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 9), end = ApiFactory.At(ApiFactory.Tomorrow, 12), reason = "Net repair" };

        await AssertProblem(await staff.PostAsJsonAsync("/api/staff/closures", Closure(), Ct), 409, "bookings_in_the_way");
        Factory.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(HttpStatusCode.Created, (await staff.PostAsJsonAsync("/api/staff/closures", Closure(), Ct)).StatusCode);
    }
}

public sealed class HoldTestsInMemory() : HoldTests(StorageProvider.InMemory);

public sealed class HoldTestsSqlite() : HoldTests(StorageProvider.Sqlite);

public sealed class HoldTestsPostgres() : HoldTests(StorageProvider.Postgres);
