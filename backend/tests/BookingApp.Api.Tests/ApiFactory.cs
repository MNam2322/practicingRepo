using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace BookingApp.Api.Tests;

/// <summary>
/// The real app in-process. Only time is replaced, so tests can move the clock. Library: 4.6 "Integration testing an API"
/// ("replace as little as possible") and "TimeProvider — testing time-dependent code".
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Monday 1 June 2026, 07:00 at the venue (Asia/Ho_Chi_Minh, UTC+7).</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 2 June 2026, tomorrow at the venue.</summary>
    public static readonly DateOnly Tomorrow = new(2026, 6, 2);

    public FakeTimeProvider Time { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: no demo bookings, so every test starts from an empty store.
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    /// <summary>Venue-local time on a day, as the UTC instant the API expects.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7)).ToUniversalTime();
}

/// <summary>A fresh app per test, so tests never share bookings (library 4.6: "Tests must not share state").</summary>
public abstract class ApiTest : IDisposable
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    protected ApiTest()
    {
        Factory = new ApiFactory();
        Client = Factory.CreateClient();
    }

    protected ApiFactory Factory { get; }

    /// <summary>A client that keeps cookies, like a browser.</summary>
    protected HttpClient Client { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static object BookingBody(int hour, int slots = 1, string resourceId = "court-1", string name = "Nam", DateOnly? day = null) =>
        new { resourceId, start = ApiFactory.At(day ?? ApiFactory.Tomorrow, hour), slots, name };

    protected Task<HttpResponseMessage> Book(int hour, int slots = 1, string resourceId = "court-1", HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync("/api/bookings", BookingBody(hour, slots, resourceId), Ct);

    protected async Task<BookingResponse> BookOk(int hour, int slots = 1, HttpClient? client = null)
    {
        var response = await Book(hour, slots, client: client);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
    }

    /// <summary>Asserts an RFC 9457 problem response with a status and a stable code.</summary>
    protected static async Task AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
