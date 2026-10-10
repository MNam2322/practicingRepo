using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookingApp.Infrastructure.Identity;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Tests.Shared;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace BookingApp.Api.Tests;

/// <summary>
/// The real app in-process. Only time is replaced, so tests can move the clock. Library: 4.6 "Integration testing an API"
/// ("replace as little as possible") and "TimeProvider — testing time-dependent code".
/// </summary>
/// <param name="postgres">For the Postgres provider: a connection string to the test database (PostgresTestServer).</param>
public sealed class ApiFactory(StorageProvider provider = StorageProvider.InMemory, string? databaseFile = null, string? postgres = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Monday 1 June 2026, 07:00 at the venue (Asia/Ho_Chi_Minh, UTC+7).</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 2 June 2026, tomorrow at the venue.</summary>
    public static readonly DateOnly Tomorrow = new(2026, 6, 2);

    public FakeTimeProvider Time { get; } = new(Now);

    public StorageProvider Provider => provider;

    /// <summary>The SQLite file for this app: a fresh temp file unless a test passes one to share between two apps.</summary>
    public string DatabaseFile { get; } = databaseFile ?? Path.Combine(Path.GetTempPath(), $"booking-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: no demo bookings, so every test starts from an empty database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:Provider", provider.ToString());
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={DatabaseFile}");
        if (postgres is not null) builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            // The auth cookie's expiry is read by the HTTP client on the real clock, so the cookie scheme keeps real
            // time; the domain (slots, cut-offs, lockout) runs on the fake clock.
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = TimeProvider.System);
        });
    }

    /// <summary>
    /// Deletes the SQLite file and its WAL files. A connection can still be on its way back to the pool when a test
    /// ends, so this retries briefly and then leaves the temp file behind rather than failing the test.
    /// </summary>
    public static void DeleteDatabase(string file)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                foreach (var path in new[] { file, file + "-wal", file + "-shm" })
                    if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Venue-local time on a day, as the UTC instant the API expects.</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7)).ToUniversalTime();
}

/// <summary>
/// A fresh app per test, so tests never share bookings (library 4.6: "Tests must not share state"). On Postgres each
/// test also gets its own database. Created in InitializeAsync, because that may wait for the database server.
/// </summary>
public abstract class ApiTest(StorageProvider provider = StorageProvider.InMemory) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private string? _postgres;

    public async ValueTask InitializeAsync()
    {
        _postgres = provider == StorageProvider.Postgres ? await PostgresTestServer.NewDatabaseAsync() : null;
        Factory = new ApiFactory(provider, postgres: _postgres);
        Client = Factory.CreateClient();
    }

    protected ApiFactory Factory { get; private set; } = null!;

    /// <summary>A client that keeps cookies, like a browser.</summary>
    protected HttpClient Client { get; private set; } = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected const string Password = "correct horse battery";

    /// <summary>A browser signed in as a new account with the given role (the role is added, then the person signs in again).</summary>
    protected async Task<HttpClient> SignedInAs(string? role)
    {
        var client = Factory.CreateClient();
        var email = $"{role ?? "customer"}.{Guid.NewGuid():N}@example.com";
        Assert.Equal(System.Net.HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/account/signup", new { email, password = Password, name = role ?? "Customer" }, Ct)).StatusCode);
        if (role is null) return client;
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, role);
        }
        // Roles travel in the auth cookie, so sign in again to pick up the new one.
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/account/signin", new { email, password = Password }, Ct)).StatusCode);
        return client;
    }

    protected static object BookingBody(int hour, int slots = 1, string resourceId = "court-1", string name = "Nam", DateOnly? day = null) =>
        new { resourceId, start = ApiFactory.At(day ?? ApiFactory.Tomorrow, hour), slots, name };

    protected Task<HttpResponseMessage> Book(int hour, int slots = 1, string resourceId = "court-1", HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync("/api/bookings", BookingBody(hour, slots, resourceId), Ct);

    protected async Task<BookingResponse> BookOk(int hour, int slots = 1, HttpClient? client = null, string resourceId = "court-1")
    {
        var response = await Book(hour, slots, resourceId, client);
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

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is null) return;
        await Factory.DisposeAsync();
        if (Factory.Provider == StorageProvider.Sqlite) ApiFactory.DeleteDatabase(Factory.DatabaseFile);
        PostgresTestServer.Release(_postgres);
        GC.SuppressFinalize(this);
    }
}
