using System.Net.Http.Json;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Caching;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>
/// Availability is cached, and never stale after a booking change: every write invalidates the resource-day it touches.
/// Library: 8.6 "Invalidation".
/// </summary>
public abstract class AvailabilityCacheTests(StorageProvider provider) : ApiTest(provider)
{
    private long Loads => Factory.Services.GetRequiredService<OccupancyVersions>().Loads;

    private async Task<SlotState> StateAt(int hour, HttpClient? client = null)
    {
        var availability = await (client ?? Client).GetFromJsonAsync<AvailabilityResponse>(
            $"/api/resources/court-1/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);
        return availability!.Slots.Single(s => s.LocalStart == $"{hour}:00").State;
    }

    [Fact]
    public async Task Repeated_reads_come_from_the_cache()
    {
        await StateAt(10);
        var loads = Loads;
        for (var i = 0; i < 5; i++) await StateAt(10);
        Assert.Equal(loads, Loads);
    }

    [Fact]
    public async Task Availability_is_never_stale_after_booking_or_cancelling()
    {
        using var other = Factory.CreateClient();
        Assert.Equal(SlotState.Free, await StateAt(10, other));   // cached now

        var hold = await HoldOk(10);
        Assert.Equal(SlotState.Booked, await StateAt(10, other));
        var booking = await ConfirmOk(hold);
        Assert.Equal(SlotState.Booked, await StateAt(10, other));
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Assert.Equal(SlotState.Free, await StateAt(10, other));

        // Staff changes too.
        var again = await BookOk(10);
        Assert.Equal(SlotState.Booked, await StateAt(10, other));
        var staff = await SignedInAs("Staff");
        await staff.PostAsJsonAsync($"/api/staff/bookings/{again.Id}/cancel", new { reason = "Court flooded" }, Ct);
        Assert.Equal(SlotState.Free, await StateAt(10, other));
    }

    [Fact]
    public async Task A_change_on_another_day_or_resource_keeps_the_cached_day()
    {
        await StateAt(10);
        var loads = Loads;
        await Client.PostAsJsonAsync("/api/bookings", BookingBody(10, resourceId: "court-2"), Ct);
        await Client.PostAsJsonAsync("/api/bookings", BookingBody(10, day: ApiFactory.Tomorrow.AddDays(3)), Ct);
        await StateAt(10);
        Assert.Equal(loads, Loads);
    }

    [Fact]
    public async Task An_expired_hold_frees_the_slot_without_reloading()
    {
        await HoldOk(10);
        Assert.Equal(SlotState.Booked, await StateAt(10));
        var loads = Loads;
        Factory.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(SlotState.Free, await StateAt(10));
        Assert.Equal(loads, Loads);   // the hold's expiry is in the cached entry
    }

    [Fact]
    public async Task A_weekly_series_shows_on_every_cached_day()
    {
        var days = new[] { ApiFactory.Tomorrow, ApiFactory.Tomorrow.AddDays(7), ApiFactory.Tomorrow.AddDays(14) };
        foreach (var day in days)
            await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={day:yyyy-MM-dd}", Json, Ct);

        await Client.PostAsJsonAsync("/api/series", new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 10), weeks = 3, name = "Nam" }, Ct);

        foreach (var day in days)
        {
            var slots = (await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={day:yyyy-MM-dd}", Json, Ct))!.Slots;
            Assert.Equal(SlotState.Booked, slots.Single(s => s.LocalStart == "10:00").State);
        }
    }
}

public sealed class AvailabilityCacheTestsInMemory() : AvailabilityCacheTests(StorageProvider.InMemory);

public sealed class AvailabilityCacheTestsSqlite() : AvailabilityCacheTests(StorageProvider.Sqlite);

public sealed class AvailabilityCacheTestsPostgres() : AvailabilityCacheTests(StorageProvider.Postgres);
