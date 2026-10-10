using BookingApp.Core.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// Deletes idempotency keys older than their 24-hour lifetime, once an hour, so the table stays small. Expired keys
/// are already ignored when claiming; this only reclaims space. Uses its own scope per run (library 4.3).
/// </summary>
internal sealed partial class IdempotencyKeyCleaner(IServiceScopeFactory scopes, TimeProvider time, ILogger<IdempotencyKeyCleaner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
                var cutoff = time.GetUtcNow() - Idempotency.KeepFor;
                var deleted = await db.IdempotencyKeys.Where(k => k.ClaimedAt < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) LogCleaned(deleted);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired idempotency keys")]
    private partial void LogCleaned(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning idempotency keys failed; will try again in an hour")]
    private partial void LogFailed(Exception error);
}
