using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Api.Tests;

/// <summary>Admin set-up: resources, opening hours, closures, booking rules, and the audit log of who changed what.</summary>
public abstract class AdminTests(StorageProvider provider) : ApiTest(provider)
{
    private static string Tomorrow => $"{ApiFactory.Tomorrow:yyyy-MM-dd}";

    private static object NewResource(string id = "court-3") =>
        new { id, name = "Tennis court 3", kind = "court", capacity = 4, slotMinutes = 60, maxSlotsPerBooking = 2 };

    private static object EveryDay(string open, string close) =>
        new { hours = Enum.GetValues<DayOfWeek>().Select(d => new { day = d.ToString().ToLowerInvariant(), open, close }) };

    [Fact]
    public async Task Only_admins_may_change_the_venue()
    {
        await AssertProblem(await Client.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct), 401, "unauthenticated");
        using var staff = await SignedInAs("Staff");
        await AssertProblem(await staff.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct), 403, "forbidden");
        await AssertProblem(await staff.GetAsync("/api/admin/audit", Ct), 403, "forbidden");
    }

    [Fact]
    public async Task A_new_resource_with_hours_can_be_booked()
    {
        using var admin = await SignedInAs("Admin");
        var created = await admin.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertProblem(await admin.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct), 409, "resource_exists");

        // No hours yet: listed, but closed.
        var resources = (await Client.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", Json, Ct))!;
        Assert.Equal("court-3", resources[^1].Id);
        Assert.Empty((await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-3/availability?date={Tomorrow}", Json, Ct))!.Slots);

        var hours = await admin.PutAsJsonAsync("/api/admin/resources/court-3/hours", EveryDay("08:00", "12:00"), Ct);
        Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
        var slots = (await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-3/availability?date={Tomorrow}", Json, Ct))!.Slots;
        Assert.Equal(["08:00", "09:00", "10:00", "11:00"], slots.Select(s => s.LocalStart));
        await BookOk(9, client: null, resourceId: "court-3");
    }

    [Fact]
    public async Task Bad_input_is_400_and_broken_hours_are_422()
    {
        using var admin = await SignedInAs("Admin");
        var badId = await admin.PostAsJsonAsync("/api/admin/resources", NewResource("Court 3!"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.True((await badId.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("id", out _));

        await AssertProblem(await admin.PutAsJsonAsync("/api/admin/resources/court-1/hours", EveryDay("12:00", "08:00"), Ct), 422, "invalid_hours");
        await AssertProblem(await admin.PutAsJsonAsync("/api/admin/resources/court-1/hours", EveryDay("08:00", "09:30"), Ct), 422, "invalid_hours");
        // 45-minute slots do not fit court-1's 06:00-22:00 hours.
        await AssertProblem(await admin.PutAsJsonAsync("/api/admin/resources/court-1",
            new { name = "Tennis court 1", kind = "court", capacity = 4, slotMinutes = 45, maxSlotsPerBooking = 2 }, Ct), 422, "invalid_hours");
        await AssertProblem(await admin.PutAsJsonAsync("/api/admin/resources/nope/hours", EveryDay("08:00", "12:00"), Ct), 404, "resource_not_found");
    }

    [Fact]
    public async Task A_resource_with_bookings_cannot_be_deleted_but_an_unused_one_can()
    {
        using var admin = await SignedInAs("Admin");
        await BookOk(10);
        await AssertProblem(await admin.DeleteAsync("/api/admin/resources/court-1", Ct), 409, "resource_in_use");

        await admin.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/admin/resources/court-3", Ct)).StatusCode);
        await AssertProblem(await Client.GetAsync($"/api/resources/court-3/availability?date={Tomorrow}", Ct), 404, "resource_not_found");
    }

    [Fact]
    public async Task A_holiday_closes_every_resource_until_it_is_removed()
    {
        using var admin = await SignedInAs("Admin");
        var response = await admin.PostAsJsonAsync("/api/admin/closures", new { from = Tomorrow, to = Tomorrow, reason = "Public holiday" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var closure = (await response.Content.ReadFromJsonAsync<ClosureResponse>(Json, Ct))!;
        Assert.Null(closure.ResourceId);

        foreach (var id in new[] { "court-1", "court-2", "room-a" })
        {
            var slots = (await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/{id}/availability?date={Tomorrow}", Json, Ct))!.Slots;
            Assert.All(slots, s => Assert.Equal(SlotState.Closed, s.State));
        }
        Assert.Single((await admin.GetFromJsonAsync<List<ClosureResponse>>($"/api/admin/closures?from={Tomorrow}&to={Tomorrow}", Json, Ct))!);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/closures/{closure.Id}", Ct)).StatusCode);
        await BookOk(10);
        await AssertProblem(await admin.DeleteAsync($"/api/admin/closures/{closure.Id}", Ct), 404, "closure_not_found");
    }

    [Fact]
    public async Task A_closure_over_confirmed_bookings_is_refused()
    {
        using var admin = await SignedInAs("Admin");
        await BookOk(10);
        await AssertProblem(await admin.PostAsJsonAsync("/api/admin/closures", new { resourceId = "court-1", from = Tomorrow, to = Tomorrow, reason = "Resurfacing" }, Ct),
            409, "bookings_in_the_way");
        // Another court that day is fine.
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/admin/closures", new { resourceId = "court-2", from = Tomorrow, to = Tomorrow, reason = "Resurfacing" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Changed_booking_rules_apply_at_once()
    {
        using var admin = await SignedInAs("Admin");
        var policy = (await admin.GetFromJsonAsync<PolicyResponse>("/api/admin/policy", Json, Ct))!;
        Assert.Equal(30, policy.MaxDaysAhead);

        var updated = await admin.PutAsJsonAsync("/api/admin/policy",
            new { maxDaysAhead = 3, minLeadMinutes = 15, cancellationCutoffHours = 48, maxActiveBookingsPerCustomer = 5 }, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(3, (await Client.GetFromJsonAsync<VenueResponse>("/api/venue", Json, Ct))!.Policy.MaxDaysAhead);

        var fiveDaysOut = await Client.PostAsJsonAsync("/api/bookings", BookingBody(10, day: ApiFactory.Tomorrow.AddDays(4)), Ct);
        await AssertProblem(fiveDaysOut, 422, "too_far_ahead");
        // A 48-hour cut-off: tomorrow 10:00 (27 hours away) is now too late to cancel.
        var booked = await BookOk(10);
        Assert.False(booked.CanCancel);

        var invalid = await admin.PutAsJsonAsync("/api/admin/policy",
            new { maxDaysAhead = 0, minLeadMinutes = 15, cancellationCutoffHours = 2, maxActiveBookingsPerCustomer = 5 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Every_change_is_in_the_audit_log_with_who_made_it()
    {
        using var admin = await SignedInAs("Admin");
        var me = (await admin.GetFromJsonAsync<AccountResponse>("/api/me", Json, Ct))!;
        await admin.PostAsJsonAsync("/api/admin/resources", NewResource(), Ct);
        await admin.PutAsJsonAsync("/api/admin/resources/court-3/hours", EveryDay("08:00", "12:00"), Ct);
        await admin.PutAsJsonAsync("/api/admin/policy", new { maxDaysAhead = 14, minLeadMinutes = 15, cancellationCutoffHours = 2, maxActiveBookingsPerCustomer = 5 }, Ct);
        // A refused change leaves no entry.
        await admin.PutAsJsonAsync("/api/admin/resources/court-1/hours", EveryDay("12:00", "08:00"), Ct);

        var audit = (await admin.GetFromJsonAsync<List<AuditEntryResponse>>("/api/admin/audit", Json, Ct))!;
        Assert.Equal(["policy.updated", "resource.hours_set", "resource.created"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal(me.Email, a.ActorEmail));
        Assert.All(audit, a => Assert.Equal(me.Id, a.ActorId));
        Assert.All(audit, a => Assert.False(string.IsNullOrEmpty(a.RequestId)));
        var policy = audit[0];
        Assert.Contains("\"maxDaysAhead\":30", policy.Before);
        Assert.Contains("\"maxDaysAhead\":14", policy.After);
        Assert.Contains("\"open\":\"08:00\"", audit[1].After);
        Assert.Null(audit[2].Before);
        Assert.Contains("court-3", audit[2].After);
    }
}

public sealed class AdminTestsInMemory() : AdminTests(StorageProvider.InMemory);

public sealed class AdminTestsSqlite() : AdminTests(StorageProvider.Sqlite);

public sealed class AdminTestsPostgres() : AdminTests(StorageProvider.Postgres);
