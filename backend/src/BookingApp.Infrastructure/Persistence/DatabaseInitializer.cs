using BookingApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date before the app serves requests: applies migrations, turns on WAL on SQLite so
/// readers don't wait for the writer, and on first run seeds the venue and resources from the "Venue" settings.
/// Registered before any other hosted service, so the demo seeder runs after it.
/// The DbContext is scoped, so this singleton creates its own scope (library 4.3 "Scoped services inside a background worker").
/// </summary>
internal sealed partial class DatabaseInitializer(IServiceScopeFactory scopes, IOptions<StorageOptions> options, IOptions<VenueOptions> venue,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        if (options.Value.MigrateOnStartup)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await db.Database.MigrateAsync(cancellationToken);
            if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            LogMigrated(pending.Count, string.Join(", ", pending));
        }
        await SeedVenueAsync(db, cancellationToken);
    }

    /// <summary>
    /// First run only (no venue row yet): copy the venue and resources from configuration. After that the database is
    /// the source. A resource row may already exist as a placeholder made by a migration for older bookings; it is
    /// overwritten with the configured values.
    /// </summary>
    private async Task SeedVenueAsync(BookingDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Venue.AnyAsync(cancellationToken)) return;
        var catalog = ConfigurationResourceRepository.BuildCatalog(venue.Value);
        db.Venue.Add(new VenueRow { Name = catalog.Venue.Name, TimeZoneId = catalog.Venue.TimeZoneId });
        var existing = await db.Resources.Include(r => r.Hours).ToDictionaryAsync(r => r.Id, cancellationToken);
        foreach (var (resource, order) in catalog.Resources.Select((r, i) => (r, i)))
        {
            var configured = ResourceRow.From(resource, order);
            if (existing.TryGetValue(resource.Id, out var row))
            {
                db.Entry(row).CurrentValues.SetValues(configured);
                row.Hours.Clear();
                row.Hours.AddRange(configured.Hours);
            }
            else db.Resources.Add(configured);
        }
        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(catalog.Resources.Count, catalog.Venue.Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database ready; applied {Count} migration(s): {Migrations}")]
    private partial void LogMigrated(int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the venue {Venue} with {Count} resources from configuration")]
    private partial void LogSeeded(int count, string venue);
}
