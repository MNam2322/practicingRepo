using BookingApp.Core.Waitlist;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookingApp.Infrastructure.Notifications;

/// <summary>
/// Looks for waited-for times that are free again every Waitlist:SweepSeconds and tells the first person in line
/// (through the outbox). Catches every way a time frees up, including holds that simply expire.
/// </summary>
internal sealed partial class WaitlistSweep(IServiceScopeFactory scopes, WaitlistOptions options, TimeProvider time, ILogger<WaitlistSweep> logger)
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
                var told = await scope.ServiceProvider.GetRequiredService<WaitlistService>().OfferFreedTimesAsync(stoppingToken);
                if (told > 0) LogTold(told);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Told {Count} waiting customer(s) that their time is free")]
    private partial void LogTold(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Offering freed times failed; trying again at the next sweep")]
    private partial void LogFailed(Exception error);
}
