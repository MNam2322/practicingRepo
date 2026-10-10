using BookingApp.Core.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure.Notifications;

/// <summary>
/// Sends due notifications every Notifications:PollSeconds. A hosted service is a singleton and the outbox and
/// repositories are scoped, so each pass works in its own scope (library 4.3 "Example 2: Scoped services inside a
/// background worker", 8.7). The work is in the database, so stopping the app at any point loses nothing.
/// </summary>
internal sealed partial class NotificationRelay(IServiceScopeFactory scopes, NotificationOptions options, TimeProvider time, ILogger<NotificationRelay> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<NotificationService>().SendDueAsync(stoppingToken);
                if (sent > 0) LogSent(sent);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {Count} notification(s)")]
    private partial void LogSent(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending notifications failed; trying again at the next poll")]
    private partial void LogFailed(Exception error);
}
