using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>Every booking route and error code, on each storage provider.</summary>
public abstract class BookingEndpointTests(StorageProvider provider) : ApiTest(provider)
{
    [Fact]
    public async Task Booking_returns_201_with_location_and_the_booking()
    {
        var response = await Book(10, slots: 2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        Assert.Equal($"/api/bookings/{booking.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("court-1", booking.ResourceId);
        Assert.Equal("Tennis court 1", booking.ResourceName);
        Assert.Equal("2026-06-02T10:00", booking.LocalStart);
        Assert.Equal("2026-06-02T12:00", booking.LocalEnd);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal("Nam", booking.CustomerName);
        Assert.True(booking.CanCancel);
    }

    [Fact]
    public async Task A_booked_slot_shows_as_booked_in_availability()
    {
        await BookOk(10);
        var availability = await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);
        Assert.Equal(SlotState.Booked, availability!.Slots.Single(s => s.LocalStart == "10:00").State);
    }

    [Fact]
    public async Task The_customer_cookie_is_issued_once_and_reused()
    {
        var first = await Client.GetAsync("/api/bookings/mine", Ct);
        var setCookie = Assert.Single(first.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{CurrentCustomer.CookieName}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);

        var booking = await BookOk(10);
        var again = await Client.GetAsync("/api/bookings/mine", Ct);
        Assert.False(again.Headers.Contains("Set-Cookie"));
        var mine = await again.Content.ReadFromJsonAsync<List<BookingResponse>>(Json, Ct);
        Assert.Equal(booking.Id, Assert.Single(mine!).Id);
    }

    [Fact]
    public async Task Another_customer_sees_none_of_my_bookings()
    {
        var booking = await BookOk(10);
        using var stranger = Factory.CreateClient();

        Assert.Empty((await stranger.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!);
        await AssertProblem(await stranger.GetAsync($"/api/bookings/{booking.Id}", Ct), 404, "booking_not_found");
    }

    [Fact]
    public async Task Get_one_booking_returns_it_to_its_owner()
    {
        var booking = await BookOk(10);
        var fetched = await Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{booking.Id}", Json, Ct);
        Assert.Equal(booking, fetched);
    }

    [Fact]
    public async Task Get_an_unknown_booking_is_404()
    {
        await AssertProblem(await Client.GetAsync($"/api/bookings/{Guid.NewGuid()}", Ct), 404, "booking_not_found");
    }

    [Fact]
    public async Task Booking_a_taken_slot_is_409_slot_taken()
    {
        await BookOk(10, slots: 2);
        using var other = Factory.CreateClient();
        await AssertProblem(await Book(11, client: other), 409, "slot_taken");
    }

    [Theory]
    [InlineData(31 * 24, 1, "too_far_ahead")]   // a month and a day ahead
    [InlineData(-18, 1, "too_soon")]            // 06:00 today, an hour before now
    [InlineData(10, 3, "too_long")]
    [InlineData(4, 1, "outside_opening_hours")]
    [InlineData(21, 2, "outside_opening_hours")]
    public async Task Rule_violations_are_422_with_a_code(int hoursFromTomorrowMidnight, int slots, string code)
    {
        var start = ApiFactory.At(ApiFactory.Tomorrow, 0).AddHours(hoursFromTomorrowMidnight);
        var response = await Client.PostAsJsonAsync("/api/bookings", new { resourceId = "court-1", start, slots, name = "Nam" }, Ct);
        await AssertProblem(response, 422, code);
    }

    [Fact]
    public async Task A_start_off_the_slot_grid_is_422_not_on_slot_grid()
    {
        var response = await Client.PostAsJsonAsync("/api/bookings",
            new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, 10, 30), slots = 1, name = "Nam" }, Ct);
        await AssertProblem(response, 422, "not_on_slot_grid");
    }

    [Fact]
    public async Task Booking_an_unknown_resource_is_404_resource_not_found()
    {
        await AssertProblem(await Book(10, resourceId: "nope"), 404, "resource_not_found");
    }

    [Fact]
    public async Task More_than_the_maximum_active_bookings_is_422_too_many_bookings()
    {
        for (var hour = 8; hour < 13; hour++) await BookOk(hour);
        await AssertProblem(await Book(14), 422, "too_many_bookings");
    }

    [Fact]
    public async Task Malformed_json_is_400()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/bookings", content, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task AddClosure(string? resourceId, DateTimeOffset start, TimeSpan length, string reason)
    {
        using var scope = Factory.Services.CreateScope();
        var resources = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        await resources.AddClosureAsync(new Closure(Guid.NewGuid(), resourceId, TimeRange.FromStart(start, length), reason), Ct);
    }

    [Fact]
    public async Task A_closed_day_shows_no_free_slots_and_lists_the_closure()
    {
        await AddClosure("court-1", ApiFactory.At(ApiFactory.Tomorrow, 0), TimeSpan.FromDays(1), "Court resurfacing");

        var availability = await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-1/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);
        Assert.All(availability!.Slots, slot => Assert.Equal(SlotState.Closed, slot.State));
        var closure = Assert.Single(availability.Closures);
        Assert.Equal("Court resurfacing", closure.Reason);
        Assert.Equal("2026-06-02T00:00", closure.LocalStart);

        var other = await Client.GetFromJsonAsync<AvailabilityResponse>($"/api/resources/court-2/availability?date={ApiFactory.Tomorrow:yyyy-MM-dd}", Json, Ct);
        Assert.DoesNotContain(other!.Slots, slot => slot.State == SlotState.Closed);
    }

    [Fact]
    public async Task Booking_into_a_closure_is_422_resource_closed()
    {
        await AddClosure("court-1", ApiFactory.At(ApiFactory.Tomorrow, 9), TimeSpan.FromHours(3), "Coaching");
        await AssertProblem(await Book(10), 422, "resource_closed");
        await AssertProblem(await Book(8, slots: 2), 422, "resource_closed");
        await BookOk(12);
    }

    [Fact]
    public async Task A_venue_wide_closure_closes_every_resource()
    {
        await AddClosure(null, ApiFactory.At(ApiFactory.Tomorrow, 0), TimeSpan.FromDays(1), "Public holiday");
        await AssertProblem(await Book(10, resourceId: "court-2"), 422, "resource_closed");
        await AssertProblem(await Book(10, resourceId: "room-a"), 422, "resource_closed");
    }

    [Fact]
    public async Task Cancel_returns_the_cancelled_booking_and_frees_the_slot()
    {
        var booking = await BookOk(10);

        var response = await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        Assert.Equal(ApiFactory.Now, cancelled.CancelledAt);
        Assert.False(cancelled.CanCancel);

        await BookOk(10);
    }

    [Fact]
    public async Task Cancelling_someone_elses_booking_is_403_not_your_booking()
    {
        var booking = await BookOk(10);
        using var stranger = Factory.CreateClient();
        await AssertProblem(await stranger.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct), 403, "not_your_booking");
    }

    [Fact]
    public async Task Cancelling_twice_is_409_not_cancellable()
    {
        var booking = await BookOk(10);
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        await AssertProblem(await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct), 409, "not_cancellable");
    }

    [Fact]
    public async Task Cancelling_after_the_cutoff_is_422_too_late_to_cancel()
    {
        var booking = await BookOk(10);
        Factory.Time.SetUtcNow(booking.Start.AddHours(-1));

        var mine = await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct);
        Assert.False(Assert.Single(mine!).CanCancel);
        await AssertProblem(await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct), 422, "too_late_to_cancel");
    }

    [Fact]
    public async Task Cancelling_an_unknown_booking_is_404()
    {
        await AssertProblem(await Client.PostAsync($"/api/bookings/{Guid.NewGuid()}/cancel", null, Ct), 404, "booking_not_found");
    }
}

public sealed class BookingEndpointTestsInMemory() : BookingEndpointTests(StorageProvider.InMemory);

public sealed class BookingEndpointTestsSqlite() : BookingEndpointTests(StorageProvider.Sqlite);
