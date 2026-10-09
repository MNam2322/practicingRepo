using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// Brings the database up to date before the app serves requests: applies migrations and, on SQLite, turns on WAL so
/// readers don't wait for the writer. Registered before any other hosted service, so seeding runs after it.
/// The DbContext is scoped, so this singleton creates its own scope (library 4.3 "Scoped services inside a background worker").
/// </summary>
internal sealed partial class DatabaseInitializer(IServiceScopeFactory scopes, IOptions<StorageOptions> options, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await db.Database.MigrateAsync(cancellationToken);
        if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        LogMigrated(pending.Count, string.Join(", ", pending));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database ready; applied {Count} migration(s): {Migrations}")]
    private partial void LogMigrated(int count, string migrations);
}
