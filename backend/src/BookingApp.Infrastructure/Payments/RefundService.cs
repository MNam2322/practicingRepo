using BookingApp.Core.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure.Payments;

/// <summary>
/// Gives back deposits that are due a refund but did not get one yet: the provider was down when the booking was
/// cancelled, or a paid hold expired. Runs every Payments:SweepSeconds. The work is found in the database each time,
/// so a restart loses nothing, and refunds carry the payment id as the idempotency key, so a refund is never paid twice
/// (library 8.7 "Background work inside the process", 12.5).
/// </summary>
internal sealed partial class RefundService(IServiceScopeFactory scopes, PaymentOptions options, TimeProvider time, ILogger<RefundService> logger)
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
                var refunded = await scope.ServiceProvider.GetRequiredService<DepositService>().RefundDueAsync(stoppingToken);
                if (refunded > 0) LogRefunded(refunded);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refunded {Count} deposit(s)")]
    private partial void LogRefunded(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refunding deposits failed; trying again at the next sweep")]
    private partial void LogFailed(Exception error);
}
