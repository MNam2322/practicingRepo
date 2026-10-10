using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingApp.Api.Tests;

/// <summary>Retrying a booking with the same Idempotency-Key is harmless: one booking, the same answer every time.</summary>
public abstract class IdempotencyTests(StorageProvider provider) : ApiTest(provider)
{
    private Task<HttpResponseMessage> BookWithKey(string key, int hour = 10, string name = "Nam", HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(hour, name: name)) };
        request.Headers.Add("Idempotency-Key", key);
        return (client ?? Client).SendAsync(request, Ct);
    }

    private async Task<int> MyBookingCount(HttpClient? client = null) =>
        (await (client ?? Client).GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!.Count;

    [Fact]
    public async Task The_same_key_twice_books_once_and_answers_the_same()
    {
        var key = Guid.NewGuid().ToString();
        var first = await BookWithKey(key);
        var second = await BookWithKey(key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        var a = (await first.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        var b = (await second.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await MyBookingCount());
    }

    [Fact]
    public async Task The_same_key_with_a_different_booking_is_422()
    {
        var key = Guid.NewGuid().ToString();
        await BookWithKey(key, hour: 10);
        await AssertProblem(await BookWithKey(key, hour: 12), 422, "idempotency_key_reused");
        Assert.Equal(1, await MyBookingCount());
    }

    [Fact]
    public async Task A_refusal_is_remembered_too()
    {
        using var other = Factory.CreateClient();
        await BookOk(10, client: other);
        var key = Guid.NewGuid().ToString();

        await AssertProblem(await BookWithKey(key), 409, "slot_taken");
        var again = await BookWithKey(key);
        await AssertProblem(again, 409, "slot_taken");
        Assert.True(again.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Keys_belong_to_one_caller()
    {
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await BookWithKey(key, hour: 10)).StatusCode);
        using var someoneElse = Factory.CreateClient();
        var theirs = await BookWithKey(key, hour: 12, client: someoneElse);
        Assert.Equal(HttpStatusCode.Created, theirs.StatusCode);
        Assert.False(theirs.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(1, await MyBookingCount(someoneElse));
    }

    [Fact]
    public async Task Without_a_key_every_request_counts()
    {
        Assert.Equal(HttpStatusCode.Created, (await Book(10)).StatusCode);
        await AssertProblem(await Book(10), 409, "slot_taken");
    }

    [Fact]
    public async Task A_bad_key_is_400()
    {
        await AssertProblem(await BookWithKey(new string('k', 101)), 400, "invalid_idempotency_key");
        await AssertProblem(await BookWithKey("has spaces"), 400, "invalid_idempotency_key");
    }

    [Fact]
    public async Task An_invalid_request_does_not_use_up_the_key()
    {
        var key = Guid.NewGuid().ToString();
        var invalid = await BookWithKey(key, name: "");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await BookWithKey(key)).StatusCode);
    }

    [Fact]
    public async Task Ten_simultaneous_retries_make_one_booking()
    {
        var key = Guid.NewGuid().ToString();
        // One customer (one cookie) retrying the same booking ten times at once.
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (antiforgery, token) = await AnonymousAntiforgeryAsync(client);
        var cookie = $"{CurrentCustomer.CookieName}={Core.Bookings.CustomerId.New()}; {antiforgery}";
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(10)) };
            request.Headers.Add("Idempotency-Key", key);
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add(BookingSecurity.HeaderName, token);
            using var response = await client.SendAsync(request, Ct);
            var code = response.StatusCode == HttpStatusCode.Conflict
                ? (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString()
                : null;
            return (response.StatusCode, code);
        }, Ct)));

        // Each one either booked, got the stored answer, or was told the first is still running. Never slot_taken:
        // that would mean a second attempt actually ran.
        Assert.All(responses, r => Assert.True(r.StatusCode == HttpStatusCode.Created || r.code == "request_in_progress", $"{r.StatusCode} {r.code}"));
        using var check = new HttpRequestMessage(HttpMethod.Get, "/api/bookings/mine");
        check.Headers.Add("Cookie", cookie);
        var mine = (await (await client.SendAsync(check, Ct)).Content.ReadFromJsonAsync<List<BookingResponse>>(Json, Ct))!;
        Assert.Single(mine);
    }
}

public sealed class IdempotencyTestsInMemory() : IdempotencyTests(StorageProvider.InMemory);

public sealed class IdempotencyTestsSqlite() : IdempotencyTests(StorageProvider.Sqlite);

public sealed class IdempotencyTestsPostgres() : IdempotencyTests(StorageProvider.Postgres);
