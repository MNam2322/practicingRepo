using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>
/// The rule that matters most: two blocking bookings for one resource never overlap, even when requests arrive together.
/// Library: 7.4 "Optimistic and pessimistic concurrency", 7.9 "Atomic conditional update".
/// </summary>
public abstract class ConcurrencyTests(StorageProvider provider) : ApiTest(provider)
{
    [Fact]
    public async Task Fifty_parallel_bookings_for_one_slot_give_exactly_one_201()
    {
        const int requests = 50;
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var start = new ManualResetEventSlim();

        // Fifty different customers, so neither the per-customer limit nor the rate limiter gets in the way.
        var tasks = Enumerable.Range(0, requests).Select(_ => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(10)) };
            request.Headers.Add("Cookie", $"{CurrentCustomer.CookieName}={CustomerId.New()}");
            start.Wait(Ct);
            using var response = await client.SendAsync(request, Ct);
            return response.StatusCode;
        }, Ct)).ToList();
        start.Set();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(requests - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Parallel_overlapping_bookings_of_different_lengths_never_double_book()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // 10:00-12:00, 11:00-12:00, 11:00-13:00 and 12:00-13:00 requested at once, many times each.
        var shapes = new[] { (10, 2), (11, 1), (11, 2), (12, 1) };
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var (hour, slots) = shapes[i % shapes.Length];
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(hour, slots)) };
            request.Headers.Add("Cookie", $"{CurrentCustomer.CookieName}={CustomerId.New()}");
            using var response = await client.SendAsync(request, Ct);
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, $"Unexpected {response.StatusCode}");
        }, Ct));
        await Task.WhenAll(tasks);

        // Read what was actually saved: at least one booking won, and no two saved bookings overlap.
        using var scope = Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var day = TimeRange.Create(ApiFactory.At(ApiFactory.Tomorrow, 0), ApiFactory.At(ApiFactory.Tomorrow.AddDays(1), 0));
        var saved = await repository.BlockingAsync("court-1", day, Ct);
        Assert.NotEmpty(saved);
        for (var i = 0; i < saved.Count; i++)
            for (var j = i + 1; j < saved.Count; j++)
                Assert.False(saved[i].Time.Overlaps(saved[j].Time), $"{saved[i].Time} overlaps {saved[j].Time}");
    }
}

public sealed class ConcurrencyTestsInMemory() : ConcurrencyTests(StorageProvider.InMemory);

/// <summary>On SQLite the guard is BEGIN IMMEDIATE in EfBookingRepository; the requests really do contend for the file lock.</summary>
public sealed class ConcurrencyTestsSqlite() : ConcurrencyTests(StorageProvider.Sqlite);

/// <summary>On PostgreSQL the guard is the exclusion constraint: the repository does no overlap check of its own.</summary>
public sealed class ConcurrencyTestsPostgres() : ConcurrencyTests(StorageProvider.Postgres);
