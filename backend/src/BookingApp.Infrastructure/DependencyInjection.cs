using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
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
        services.AddSingleton<IResourceRepository, ConfigurationResourceRepository>();

        services.AddSingleton(configuration.GetSection("Booking").Get<BookingPolicy>() ?? new BookingPolicy());

        var storage = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        switch (storage.Provider)
        {
            case StorageProvider.InMemory:
                services.AddSingleton<IBookingRepository, InMemoryBookingRepository>();
                break;
            case StorageProvider.Sqlite:
                var connectionString = SqliteConnectionString(configuration.GetConnectionString(ConnectionStrings.Bookings), contentRoot);
                services.AddDbContext<BookingDbContext>(o => o.UseSqlite(connectionString));
                services.AddScoped<IBookingRepository, EfBookingRepository>();
                // First hosted service: the schema exists before anything else (such as the demo seeder) uses it.
                services.AddHostedService<DatabaseInitializer>();
                break;
            default:
                throw new InvalidOperationException($"Unknown Storage:Provider '{storage.Provider}'.");
        }

        if (configuration.GetValue("Seed:DemoBookings", false)) services.AddHostedService<DemoSeeder>();
        return services;
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
internal sealed partial class DemoSeeder(IServiceScopeFactory scopes, IResourceRepository resources, TimeProvider time, ILogger<DemoSeeder> logger) : IHostedService
{
    private static readonly CustomerId DemoCustomer = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), resources.Venue.TimeZone).DateTime).AddDays(1);
        var count = 0;
        foreach (var resource in resources.Resources)
        {
            var hours = resource.HoursOn(tomorrow.DayOfWeek);
            if (hours is null) continue;
            foreach (var offset in new[] { 2, 5 })
            {
                var localStart = tomorrow.ToDateTime(hours.Open).AddMinutes(resource.SlotMinutes * offset);
                if (TimeOnly.FromDateTime(localStart.AddMinutes(resource.SlotMinutes)) > hours.Close) continue;
                var start = AvailabilityCalculator.ToUtc(localStart, resources.Venue);
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
