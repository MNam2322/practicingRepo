using System.Net;
using System.Net.Http.Json;

namespace BookingApp.Api.Tests;

public sealed class VenueEndpointTests : ApiTest
{
    [Fact]
    public async Task Health_is_healthy()
    {
        var response = await Client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Venue_returns_name_time_zone_and_policy()
    {
        var venue = await Client.GetFromJsonAsync<VenueResponse>("/api/venue", Json, Ct);

        Assert.Equal("Riverside Sports Club", venue!.Name);
        Assert.Equal("Asia/Ho_Chi_Minh", venue.TimeZone);
        Assert.Equal(30, venue.Policy.MaxDaysAhead);
        Assert.Equal(2, venue.Policy.CancellationCutoffHours);
    }

    [Fact]
    public async Task Resources_lists_every_resource_with_hours_from_monday()
    {
        var resources = (await Client.GetFromJsonAsync<List<ResourceResponse>>("/api/resources", Json, Ct))!;

        Assert.Equal(["court-1", "court-2", "room-a"], resources.Select(r => r.Id));
        var court = resources[0];
        Assert.Equal(60, court.SlotMinutes);
        Assert.Equal(DayOfWeek.Monday, court.Hours[0].Day);
        Assert.Equal("06:00", court.Hours[0].Open);
        Assert.Equal(7, court.Hours.Count);
    }

    [Fact]
    public async Task Availability_returns_the_days_slots_in_utc_and_local_time()
    {
        var availability = await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);

        Assert.Equal(ApiFactory.Tomorrow, availability!.Date);
        Assert.Equal("Asia/Ho_Chi_Minh", availability.TimeZone);
        Assert.Equal(16, availability.Slots.Count);
        var first = availability.Slots[0];
        Assert.Equal("06:00", first.LocalStart);
        Assert.Equal(ApiFactory.At(ApiFactory.Tomorrow, 6), first.Start);
        Assert.Equal(TimeSpan.Zero, first.Start.Offset);
    }

    [Fact]
    public async Task Availability_defaults_to_today_at_the_venue()
    {
        var availability = await Client.GetFromJsonAsync<AvailabilityResponse>("/api/resources/court-1/availability", Json, Ct);
        Assert.Equal(new DateOnly(2026, 6, 1), availability!.Date);
        // Now is 07:00 local, so 06:00 and 07:00 are past (07:00 is inside the 15-minute lead time).
        Assert.Equal(Core.Scheduling.SlotState.Past, availability.Slots[1].State);
        Assert.Equal(Core.Scheduling.SlotState.Free, availability.Slots[2].State);
    }

    [Fact]
    public async Task Availability_for_an_unknown_resource_is_404_resource_not_found()
    {
        await AssertProblem(await Client.GetAsync("/api/resources/nope/availability", Ct), 404, "resource_not_found");
    }

    [Fact]
    public async Task Availability_with_an_unreadable_date_is_400()
    {
        var response = await Client.GetAsync("/api/resources/court-1/availability?date=tomorrow", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
