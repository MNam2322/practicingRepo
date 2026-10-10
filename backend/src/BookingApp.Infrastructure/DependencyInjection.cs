using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Identity;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the adapters behind the ports in BookingApp.Core.</summary>
    /// <param name="contentRoot">Relative SQLite file paths are resolved against this folder.</param>
    public static IServiceCollection AddBookingInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<VenueOptions>()
            .Bind(configuration.GetSection(VenueOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name), "Venue:Name is required.")
            .Validate(o => o.Resources.Count > 0, "Venue:Resources needs at least one resource.")
            .Validate(o => o.Resources.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == o.Resources.Count, "Resource ids must be unique.")
            .ValidateOnStart();

        services.AddSingleton(configuration.GetSection("Booking").Get<BookingPolicy>() ?? new BookingPolicy());

        var storage = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        switch (storage.Provider)
        {
            case StorageProvider.InMemory:
                services.AddSingleton<IResourceRepository, ConfigurationResourceRepository>();
                services.AddSingleton<IBookingRepository, InMemoryBookingRepository>();
                // Accounts only; one in-memory database per app instance.
                var name = $"accounts-{Guid.NewGuid():N}";
                services.AddDbContext<InMemoryBookingDbContext>(o => o.UseInMemoryDatabase(name));
                services.AddScoped<BookingDbContext>(sp => sp.GetRequiredService<InMemoryBookingDbContext>());
                services.AddHostedService<DatabaseInitializer>();
                break;
            case StorageProvider.Sqlite:
                var sqlite = SqliteConnectionString(configuration.GetConnectionString(ConnectionStrings.Sqlite), contentRoot);
                services.AddDbContext<SqliteBookingDbContext>(o => o.UseSqlite(sqlite));
                AddEfRepositories<SqliteBookingDbContext>(services);
                break;
            case StorageProvider.Postgres:
                var postgres = configuration.GetConnectionString(ConnectionStrings.Postgres)
                    ?? throw new InvalidOperationException("Storage:Provider is Postgres but ConnectionStrings:Postgres is not set.");
                // Overlapping inserts racing on the exclusion constraint can deadlock (SQLSTATE 40P01); PostgreSQL rolls
                // one back. The retrying execution strategy runs it again, and it then succeeds or gets 23P01 (slot_taken).
                // Library 7.7 "Concurrency and transactions": an execution strategy retries whole operations.
                services.AddDbContext<PostgresBookingDbContext>(o => o.UseNpgsql(postgres, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
                AddEfRepositories<PostgresBookingDbContext>(services);
                break;
            default:
                throw new InvalidOperationException($"Unknown Storage:Provider '{storage.Provider}'.");
        }

        AddAccounts(services);
        if (configuration.GetValue("Seed:DemoBookings", false)) services.AddHostedService<DemoSeeder>();
        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity over the booking database. Passwords: at least 10 characters and no forced character
    /// classes (length beats complexity rules); five wrong passwords lock the account for five minutes.
    /// The auth cookie itself is configured by the Api (it is an HTTP concern).
    /// </summary>
    private static void AddAccounts(IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BookingDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<AccountService>();
    }

    /// <summary>The EF Core repositories over whichever database was chosen; they depend only on the shared BookingDbContext.</summary>
    private static void AddEfRepositories<TContext>(IServiceCollection services) where TContext : BookingDbContext
    {
        services.AddScoped<BookingDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IBookingRepository, EfBookingRepository>();
        services.AddScoped<IResourceRepository, EfResourceRepository>();
        // First hosted service: the schema exists before anything else (such as the demo seeder) uses it.
        services.AddHostedService<DatabaseInitializer>();
    }

    /// <summary>"Data Source=.data/bookings.db" is relative to the API's folder; the folder is created if needed.</summary>
    private static string SqliteConnectionString(string? configured, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(configured ?? "Data Source=.data/bookings.db");
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
        if (Path.GetDirectoryName(builder.DataSource) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        return builder.ToString();
    }
}

/// <summary>
/// Adds a few bookings tomorrow so the availability grid is not empty on first run. Development only.
/// Repositories may be scoped (EF Core), so it works inside its own scope.
/// </summary>
internal sealed partial class DemoSeeder(IServiceScopeFactory scopes, TimeProvider time, ILogger<DemoSeeder> logger) : IHostedService
{
    private static readonly CustomerId DemoCustomer = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var catalog = await scope.ServiceProvider.GetRequiredService<IResourceRepository>().GetCatalogAsync(cancellationToken);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), catalog.Venue.TimeZone).DateTime).AddDays(1);
        var count = 0;
        foreach (var resource in catalog.Resources)
        {
            var hours = resource.HoursOn(tomorrow.DayOfWeek);
            if (hours is null) continue;
            foreach (var offset in new[] { 2, 5 })
            {
                var localStart = tomorrow.ToDateTime(hours.Open).AddMinutes(resource.SlotMinutes * offset);
                if (TimeOnly.FromDateTime(localStart.AddMinutes(resource.SlotMinutes)) > hours.Close) continue;
                var start = AvailabilityCalculator.ToUtc(localStart, catalog.Venue);
                var booking = Booking.Create(resource.Id, TimeRange.FromStart(start, resource.SlotLength), DemoCustomer, "Demo customer", time.GetUtcNow());
                if (await bookings.TryAddAsync(booking, cancellationToken)) count++;
            }
        }
        LogSeeded(count, tomorrow);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} demo bookings for {Day}")]
    private partial void LogSeeded(int count, DateOnly day);
}
