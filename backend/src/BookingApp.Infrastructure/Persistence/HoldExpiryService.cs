using BookingApp.Core.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// Releases holds past their time, every few seconds (Holds:SweepSeconds). The work is in the database, so a restart
/// loses nothing: the next sweep finds them. Errors are logged and the loop carries on (library 8.7 "Background work
/// inside the process"). A hold never waits for this job to stop blocking: every insert expires due holds first.
/// </summary>
internal sealed partial class HoldExpiryService(IServiceScopeFactory scopes, HoldOptions options, TimeProvider time, ILogger<HoldExpiryService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SweepSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<BookingService>().ExpireDueHoldsAsync(stoppingToken);
                if (expired > 0) LogExpired(expired);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} expired hold(s)")]
    private partial void LogExpired(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Releasing expired holds failed; trying again at the next sweep")]
    private partial void LogFailed(Exception error);
}
