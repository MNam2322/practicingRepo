using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace BookingApp.Api.Tests;

/// <summary>Traces, metrics and health, as an operator sees them. Library: 11.6 Observability.</summary>
public abstract class ObservabilityTests(StorageProvider provider) : ApiTest(provider)
{
    private MetricCollector<long> Collect(string instrument) =>
        new(Factory.Services.GetRequiredService<IMeterFactory>(), Core.Common.Telemetry.Name, instrument);

    private static long Total(MetricCollector<long> collector) => collector.GetMeasurementSnapshot().Sum(m => m.Value);

    [Fact]
    public async Task The_business_counters_count()
    {
        using var created = Collect("bookingapp.bookings.created");
        using var conflicts = Collect("bookingapp.bookings.conflicts");
        using var confirmed = Collect("bookingapp.bookings.confirmed");
        using var cancelled = Collect("bookingapp.bookings.cancelled");
        using var expired = Collect("bookingapp.holds.expired");

        var booking = await BookOk(10);
        using var other = Factory.CreateClient();
        await AssertProblem(await Book(10, client: other), 409, "slot_taken");   // a double booking
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        await HoldOk(12);
        Factory.Time.Advance(TimeSpan.FromMinutes(10));
        await HoldOk(12, client: other);   // the expired hold is expired in passing, and counted

        Assert.Equal((3, 1, 1, 1, 1), (Total(created), Total(conflicts), Total(confirmed), Total(cancelled), Total(expired)));
        var conflict = Assert.Single(conflicts.GetMeasurementSnapshot());
        Assert.Equal("court-1", conflict.Tags["booking.resource"]);
        Assert.Equal("customer", Assert.Single(cancelled.GetMeasurementSnapshot()).Tags["cancelled.by"]);
    }

    [Fact]
    public async Task Liveness_never_asks_the_database_and_readiness_does()
    {
        foreach (var path in new[] { "/health", "/health/live", "/health/ready" })
        {
            var response = await Client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
        }
    }
}

public sealed class ObservabilityTestsInMemory() : ObservabilityTests(StorageProvider.InMemory);

public sealed class ObservabilityTestsSqlite() : ObservabilityTests(StorageProvider.Sqlite)
{
    [Fact]
    public async Task A_booking_is_one_trace_from_the_request_to_the_database()
    {
        var exported = new List<Activity>();
        await using var traced = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.ConfigureOpenTelemetryTracerProvider(t => t.AddInMemoryExporter(exported))));
        using var client = ApiFactory.BrowserClient(traced, new System.Net.CookieContainer());

        // Our own trace id (W3C traceparent), so spans from other tests running at the same time are told apart:
        // activity sources are process-wide.
        var traceId = ActivityTraceId.CreateRandom();
        using var request0 = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(10)) };
        request0.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var response = await client.SendAsync(request0, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // The request's own span ends just after the response reaches the client: wait for it to be exported.
        List<Activity> spans = [];
        for (var i = 0; i < 50; i++)
        {
            traced.Services.GetRequiredService<TracerProvider>().ForceFlush();
            // A copy: the background jobs keep exporting their own spans while the test reads.
            spans = [.. exported.ToArray().Where(s => s.TraceId == traceId)];
            if (spans.Any(s => s.Kind == ActivityKind.Server)) break;
            await Task.Delay(100, Ct);
        }

        var request = Assert.Single(spans, s => s.Kind == ActivityKind.Server && s.DisplayName == "POST api/bookings");
        var book = Assert.Single(spans, s => s.Source.Name == Core.Common.Telemetry.Name && s.DisplayName == "book");
        Assert.Equal(request.TraceId, book.TraceId);
        Assert.True(IsUnder(book, request, spans));
        Assert.Equal("ok", book.GetTagItem("booking.outcome"));

        // The SQL the booking ran: EF Core commands, in the same trace, under "book".
        var sql = spans.Where(s => s.Kind == ActivityKind.Client && s.TraceId == request.TraceId && IsUnder(s, book, spans)).ToList();
        Assert.NotEmpty(sql);
        Assert.Contains(sql, s => s.Tags.Any(t => t.Key.StartsWith("db.", StringComparison.Ordinal)));
    }

    private static bool IsUnder(Activity child, Activity ancestor, List<Activity> all)
    {
        for (var parent = child.ParentSpanId; parent != default;)
        {
            if (parent == ancestor.SpanId) return true;
            var next = all.FirstOrDefault(a => a.SpanId == parent);
            if (next is null) return false;
            parent = next.ParentSpanId;
        }
        return false;
    }
}

public sealed class ObservabilityTestsPostgres() : ObservabilityTests(StorageProvider.Postgres);
