using System.Collections.Concurrent;
using System.Net;
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
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace BookingApp.Api.Tests;

/// <summary>
/// The real app in-process. Only time is replaced, so tests can move the clock. Library: 4.6 "Integration testing an API"
/// ("replace as little as possible") and "TimeProvider — testing time-dependent code".
/// </summary>
/// <param name="postgres">For the Postgres provider: a connection string to the test database (PostgresTestServer).</param>
/// <param name="settings">Extra configuration for this app, such as a slower poll for a background service.</param>
public sealed class ApiFactory(StorageProvider provider = StorageProvider.InMemory, string? databaseFile = null, string? postgres = null,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Monday 1 June 2026, 07:00 at the venue (Asia/Ho_Chi_Minh, UTC+7).</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Tuesday 2 June 2026, tomorrow at the venue.</summary>
    public static readonly DateOnly Tomorrow = new(2026, 6, 2);

    public FakeTimeProvider Time { get; } = new(Now);

    /// <summary>The fake payment provider's webhook secret in tests.</summary>
    public const string WebhookSecret = "whsec_test_secret_0123456789";

    /// <summary>The fake payment provider the app uses: tests read what it charged and make it fail.</summary>
    public BookingApp.Infrastructure.Payments.FakePaymentGateway Payments => Services.GetRequiredService<BookingApp.Infrastructure.Payments.FakePaymentGateway>();

    /// <summary>Errors the app logged while the test ran, so a failing test can say what went wrong on the server.</summary>
    public ConcurrentQueue<string> ServerErrors { get; } = new();

    public StorageProvider Provider => provider;

    /// <summary>Where this app's emails go (.eml files): a fresh temp folder per app.</summary>
    public string EmailFolder { get; } = Path.Combine(Path.GetTempPath(), $"booking-emails-{Guid.NewGuid():N}");

    /// <summary>The emails sent so far, oldest first.</summary>
    public IReadOnlyList<string> Emails() => Directory.Exists(EmailFolder)
        ? [.. Directory.GetFiles(EmailFolder, "*.eml").Order().Select(File.ReadAllText)]
        : [];

    /// <summary>The SQLite file for this app: a fresh temp file unless a test passes one to share between two apps.</summary>
    public string DatabaseFile { get; } = databaseFile ?? Path.Combine(Path.GetTempPath(), $"booking-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: no demo bookings, so every test starts from an empty database.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:Provider", provider.ToString());
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={DatabaseFile}");
        if (postgres is not null) builder.UseSetting("ConnectionStrings:Postgres", postgres);
        builder.UseSetting("Payments:Fake:WebhookSecret", WebhookSecret);
        builder.UseSetting("Email:Folder", EmailFolder);
        // Rate limits out of the way (every test client shares one "IP"); RateLimitTests put the real ones back.
        builder.UseSetting("RateLimits:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimits:WritesPerMinutePerAccount", "100000");
        builder.UseSetting("RateLimits:WritesPerMinutePerIp", "100000");
        builder.UseSetting("RateLimits:SignInsPerMinutePerIp", "100000");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging => logging.AddProvider(new ErrorCollector(ServerErrors)));
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

    /// <summary>A client that behaves like the web app: keeps cookies and sends the antiforgery token on writes.</summary>
    public new HttpClient CreateClient() => BrowserClient(this, new CookieContainer());

    /// <summary>Like <see cref="CreateClient()"/>, with cookies the test controls (such as a chosen customer cookie).</summary>
    public HttpClient CreateClient(CookieContainer cookies) => BrowserClient(this, cookies);

    /// <summary>A browser-like client for any factory (also one made with WithWebHostBuilder).</summary>
    public static HttpClient BrowserClient(WebApplicationFactory<Program> factory, CookieContainer cookies) =>
        factory.CreateDefaultClient(new Uri("http://localhost"), new XsrfHandler(cookies), new CookieContainerHandler(cookies));

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
        Factory = new ApiFactory(provider, postgres: _postgres, settings: Settings);
        Client = Factory.CreateClient();
    }

    protected ApiFactory Factory { get; private set; } = null!;

    /// <summary>Configuration a test class needs on top of the defaults.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>();

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

    protected Task<HttpResponseMessage> Book(int hour, int slots = 1, string resourceId = "court-1", HttpClient? client = null, string? email = null) =>
        (client ?? Client).PostAsJsonAsync("/api/bookings", new { resourceId, start = ApiFactory.At(ApiFactory.Tomorrow, hour), slots, name = "Nam", email }, Ct);

    /// <summary>Holds a slot and confirms it, as the booking page does: a confirmed booking.</summary>
    protected async Task<BookingResponse> BookOk(int hour, int slots = 1, HttpClient? client = null, string resourceId = "court-1", string? email = null) =>
        await ConfirmOk(await HoldOk(hour, slots, client, resourceId, email), client);

    /// <summary>Holds a slot without confirming it.</summary>
    protected async Task<BookingResponse> HoldOk(int hour, int slots = 1, HttpClient? client = null, string resourceId = "court-1", string? email = null)
    {
        var response = await Book(hour, slots, resourceId, client, email);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
    }

    /// <summary>Confirms a hold, paying the deposit with a fake provider token (tok_visa is approved).</summary>
    protected Task<HttpResponseMessage> Confirm(Guid id, HttpClient? client = null, string? paymentToken = "tok_visa") =>
        (client ?? Client).PostAsJsonAsync($"/api/bookings/{id}/confirm", new { paymentToken }, Ct);

    protected async Task<BookingResponse> ConfirmOk(BookingResponse hold, HttpClient? client = null, string paymentToken = "tok_visa")
    {
        var response = await Confirm(hold.Id, client, paymentToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
    }

    /// <summary>
    /// For tests that write the Cookie header themselves: an anonymous antiforgery pair (the cookie to send, and the token
    /// for the X-XSRF-TOKEN header). Anonymous tokens are not tied to a customer, so one pair serves many customers.
    /// </summary>
    protected static async Task<(string Cookie, string Token)> AnonymousAntiforgeryAsync(HttpClient raw)
    {
        using var response = await raw.GetAsync("/api/antiforgery", Ct);
        var cookies = response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).ToList();
        var token = cookies.Single(c => c.StartsWith(BookingSecurity.TokenCookie + "=", StringComparison.Ordinal))[(BookingSecurity.TokenCookie.Length + 1)..];
        return (string.Join("; ", cookies.Where(c => !c.StartsWith(BookingSecurity.TokenCookie + "=", StringComparison.Ordinal))), Uri.UnescapeDataString(token));
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
        if (Directory.Exists(Factory.EmailFolder)) Directory.Delete(Factory.EmailFolder, recursive: true);
        PostgresTestServer.Release(_postgres);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// What the web app does (frontend/src/lib/api.ts): before a write, make sure there is an XSRF-TOKEN cookie (GET
/// /api/antiforgery), then echo it in the X-XSRF-TOKEN header.
/// </summary>
internal sealed class XsrfHandler(CookieContainer cookies) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && request.RequestUri is { } uri)
        {
            if (Token(uri) is null)
                using (await base.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(uri, "/api/antiforgery")), cancellationToken)) { }
            if (Token(uri) is { } token && !request.Headers.Contains(BookingSecurity.HeaderName)) request.Headers.Add(BookingSecurity.HeaderName, token);
        }
        return await base.SendAsync(request, cancellationToken);
    }

    private string? Token(Uri uri) => cookies.GetCookies(uri)[BookingSecurity.TokenCookie]?.Value;
}

/// <summary>Keeps every Error-level log line (with its exception) from the app under test.</summary>
internal sealed class ErrorCollector(ConcurrentQueue<string> errors) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, errors);

    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<string> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) errors.Enqueue($"{category}: {formatter(state, exception)} {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
