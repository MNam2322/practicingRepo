using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Api.Tests;

/// <summary>
/// Weekly series: each date booked on its own, failures reported per date, and the series confirmed or cancelled date by
/// date. Library: 8.2 "Bulk and batch operations", 1.2 "Recurring events".
/// </summary>
public abstract class SeriesTests(StorageProvider provider) : ApiTest(provider)
{
    private static readonly DateOnly Week2 = ApiFactory.Tomorrow.AddDays(7);
    private static readonly DateOnly Week3 = ApiFactory.Tomorrow.AddDays(14);

    private static object SeriesBody(int weeks, int hour = 10) =>
        new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, hour), weeks, name = "Nam" };

    private Task<HttpResponseMessage> PostSeries(int weeks, int hour = 10, HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync("/api/series", SeriesBody(weeks, hour), Ct);

    private async Task<SeriesResponse> SeriesOk(int weeks, int hour = 10, HttpClient? client = null)
    {
        var response = await PostSeries(weeks, hour, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesResponse>(Json, Ct))!;
    }

    private async Task<SeriesChangesResponse> Change(Guid seriesId, string action, string? paymentToken = "tok_visa")
    {
        var response = action == "confirm"
            ? await Client.PostAsJsonAsync($"/api/series/{seriesId}/confirm", new { paymentToken }, Ct)
            : await Client.PostAsync($"/api/series/{seriesId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeriesChangesResponse>(Json, Ct))!;
    }

    private async Task<List<BookingResponse>> Bookings(Guid seriesId) =>
        (await Client.GetFromJsonAsync<List<BookingResponse>>($"/api/series/{seriesId}", Json, Ct))!;

    [Fact]
    public async Task A_taken_date_is_reported_and_the_rest_are_held()
    {
        using var other = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/bookings", BookingBody(10, day: Week2), Ct)).StatusCode);

        var series = await SeriesOk(weeks: 4);

        Assert.Equal((3, 1), (series.Held, series.Failed));
        Assert.Equal(["2026-06-02", "2026-06-09", "2026-06-16", "2026-06-23"], series.Dates.Select(d => d.LocalDate));
        var taken = series.Dates[1];
        Assert.Equal((SeriesDateOutcome.Failed, "slot_taken"), (taken.Outcome, taken.Code));
        Assert.Null(taken.Booking);
        Assert.All(series.Dates.Where(d => d.Outcome == SeriesDateOutcome.Held), d =>
        {
            Assert.Equal(BookingStatus.Pending, d.Booking!.Status);
            Assert.Equal(series.SeriesId, d.Booking.SeriesId);
            Assert.Equal($"{d.LocalDate}T10:00", d.Booking.LocalStart);   // the same wall-clock time every week
        });
    }

    [Fact]
    public async Task A_closed_date_and_dates_past_the_booking_window_fail_with_their_reason()
    {
        var admin = await SignedInAs("Admin");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/closures",
            new { resourceId = "court-1", from = Week3, to = Week3, reason = "Tournament" }, Ct)).StatusCode);

        var series = await SeriesOk(weeks: 6);   // the sixth week is past the 30 days customers can book ahead

        Assert.Equal(["resource_closed", "too_far_ahead"], series.Dates.Where(d => d.Outcome == SeriesDateOutcome.Failed).Select(d => d.Code));
        Assert.Equal([2, 5], series.Dates.Where(d => d.Outcome == SeriesDateOutcome.Failed).Select(d => d.Index));
    }

    [Fact]
    public async Task A_series_is_2_to_12_weeks()
    {
        await AssertProblem(await PostSeries(weeks: 1), 400, "validation_failed");
        await AssertProblem(await PostSeries(weeks: 13), 400, "validation_failed");
    }

    [Fact]
    public async Task Confirming_the_series_pays_and_confirms_each_date()
    {
        var series = await SeriesOk(weeks: 3);
        var confirmed = await Change(series.SeriesId, "confirm");

        Assert.Equal((3, 0), (confirmed.Succeeded, confirmed.Failed));
        Assert.Equal(3, Factory.Payments.Charges.Count);
        Assert.All(await Bookings(series.SeriesId), b => Assert.Equal((BookingStatus.Confirmed, DepositStatus.Paid), (b.Status, b.Deposit!.Status)));
        Assert.Empty((await Change(series.SeriesId, "confirm")).Results);   // nothing left to confirm
    }

    [Fact]
    public async Task A_declined_card_fails_each_date_and_keeps_the_holds()
    {
        var series = await SeriesOk(weeks: 2);
        var result = await Change(series.SeriesId, "confirm", paymentToken: "tok_declined");
        Assert.Equal((0, 2), (result.Succeeded, result.Failed));
        Assert.All(result.Results, r => Assert.Equal("payment_declined", r.Code));
        Assert.All(await Bookings(series.SeriesId), b => Assert.Equal(BookingStatus.Pending, b.Status));
    }

    [Fact]
    public async Task Cancelling_the_series_cancels_and_refunds_each_date()
    {
        var series = await SeriesOk(weeks: 3);
        await Change(series.SeriesId, "confirm");
        var cancelled = await Change(series.SeriesId, "cancel");

        Assert.Equal((3, 0), (cancelled.Succeeded, cancelled.Failed));
        Assert.Equal(3, Factory.Payments.Refunds.Count);
        Assert.All(await Bookings(series.SeriesId), b => Assert.Equal((BookingStatus.Cancelled, DepositStatus.Refunded), (b.Status, b.Deposit!.Status)));
    }

    [Fact]
    public async Task One_date_can_be_cancelled_on_its_own()
    {
        var series = await SeriesOk(weeks: 3);
        await Change(series.SeriesId, "confirm");
        var second = series.Dates[1].Booking!;
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/bookings/{second.Id}/cancel", null, Ct)).StatusCode);
        Assert.Equal([BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Confirmed], (await Bookings(series.SeriesId)).Select(b => b.Status));
    }

    [Fact]
    public async Task A_date_inside_the_cut_off_stays_booked_when_the_series_is_cancelled()
    {
        var series = await SeriesOk(weeks: 3);
        await Change(series.SeriesId, "confirm");
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 9));   // an hour before the first date

        var cancelled = await Change(series.SeriesId, "cancel");
        Assert.Equal((2, 1), (cancelled.Succeeded, cancelled.Failed));
        Assert.Equal("too_late_to_cancel", cancelled.Results[0].Code);
        Assert.Equal([BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Cancelled], (await Bookings(series.SeriesId)).Select(b => b.Status));
    }

    [Fact]
    public async Task A_series_counts_once_toward_the_limit_of_upcoming_bookings()
    {
        foreach (var hour in new[] { 6, 7, 8, 9 }) await BookOk(hour);   // four of the five allowed
        var series = await SeriesOk(weeks: 4, hour: 14);
        Assert.Equal(4, series.Held);
        await AssertProblem(await Book(16), 422, "too_many_bookings");   // the series was the fifth
    }

    [Fact]
    public async Task Nobody_else_can_see_or_change_a_series()
    {
        var series = await SeriesOk(weeks: 2);
        using var other = Factory.CreateClient();
        await AssertProblem(await other.GetAsync($"/api/series/{series.SeriesId}", Ct), 404, "series_not_found");
        await AssertProblem(await other.PostAsJsonAsync($"/api/series/{series.SeriesId}/confirm", new { paymentToken = "tok_visa" }, Ct), 404, "series_not_found");
        await AssertProblem(await other.PostAsync($"/api/series/{series.SeriesId}/cancel", null, Ct), 404, "series_not_found");
    }

    [Fact]
    public async Task A_retried_series_request_books_once()
    {
        var key = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Send()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/series") { Content = JsonContent.Create(SeriesBody(3)) };
            request.Headers.Add("Idempotency-Key", key);
            return await Client.SendAsync(request, Ct);
        }
        var first = (await (await Send()).Content.ReadFromJsonAsync<SeriesResponse>(Json, Ct))!;
        var again = await Send();
        Assert.True(again.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(first.SeriesId, (await again.Content.ReadFromJsonAsync<SeriesResponse>(Json, Ct))!.SeriesId);
        Assert.Equal(3, (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!.Count);
    }
}

public sealed class SeriesTestsInMemory() : SeriesTests(StorageProvider.InMemory);

public sealed class SeriesTestsSqlite() : SeriesTests(StorageProvider.Sqlite);

public sealed class SeriesTestsPostgres() : SeriesTests(StorageProvider.Postgres);
