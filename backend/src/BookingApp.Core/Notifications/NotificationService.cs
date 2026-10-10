using System.Globalization;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Notifications;

/// <summary>The "Notifications" settings.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>How often the relay looks for due emails.</summary>
    public int PollSeconds { get; set; } = 5;

    /// <summary>The web app's address, for links in emails.</summary>
    public string AppUrl { get; set; } = "http://localhost:5173";

    /// <summary>Failed sends before a message is dead-lettered (left for a person to look at).</summary>
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>
/// The relay: sends due outbox messages through <see cref="IEmailSender"/> and records what became of each. Nothing is
/// lost (a message stays until it is recorded as done) and nothing is sent twice on purpose: a crash between sending
/// and recording sends it again with the same id, and the sender drops the repeat. A failure waits longer each time
/// (10 s, 20 s, 40 s … up to an hour) and is dead-lettered after <see cref="NotificationOptions.MaxAttempts"/>.
/// Library: 12.3 "Outbox + inbox patterns", 14.2 "The outbox relay: publishing without losing or duplicating".
/// </summary>
public sealed class NotificationService(
    INotificationOutbox outbox, IBookingRepository bookings, IResourceRepository resources, IEmailSender sender,
    NotificationOptions options, TimeProvider time)
{
    /// <summary>Sends what is due. Returns how many were sent.</summary>
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await outbox.DueAsync(now, 20, cancellationToken);
        if (due.Count == 0) return 0;
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var sent = 0;
        foreach (var message in due)
        {
            EmailMessage email;
            if (message is { Subject: { } subject, Body: { } body })
            {
                email = new EmailMessage(message.Id, message.To, subject, body, message.DueAt);
            }
            else
            {
                var booking = message.BookingId is { } id ? await bookings.FindAsync(id, cancellationToken) : null;
                // A reminder is only true while the booking still stands.
                if (booking is null || (message.Kind == NotificationKind.Reminder && booking.StatusAt(now) != BookingStatus.Confirmed))
                {
                    await outbox.MarkDoneAsync(message.Id, DeliveryOutcome.Skipped, now, cancellationToken);
                    continue;
                }
                email = EmailTemplates.Render(message, booking, catalog, options.AppUrl);
            }
            try
            {
                await sender.SendAsync(email, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var attempts = message.Attempts + 1;
                DateTimeOffset? retryAt = attempts >= options.MaxAttempts ? null : now + Backoff(attempts);
                await outbox.MarkFailedAsync(message.Id, attempts, retryAt, e.Message, now, cancellationToken);
                continue;
            }
            await outbox.MarkDoneAsync(message.Id, DeliveryOutcome.Sent, time.GetUtcNow(), cancellationToken);
            sent++;
        }
        return sent;
    }

    /// <summary>The wait after the nth failure: 10 s doubling, at most an hour.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(10 * Math.Pow(2, attempts - 1), 3600));
}

/// <summary>The emails' wording. Plain text; times in the venue's time zone, as everywhere customers see them.</summary>
public static class EmailTemplates
{
    public static EmailMessage Render(OutboxMessage message, Booking b, ResourceCatalog catalog, string appUrl)
    {
        var venue = catalog.Venue;
        var resource = catalog.Find(b.ResourceId)?.Name ?? b.ResourceId;
        var start = TimeZoneInfo.ConvertTime(b.Time.Start, venue.TimeZone);
        var end = TimeZoneInfo.ConvertTime(b.Time.End, venue.TimeZone);
        var culture = CultureInfo.InvariantCulture;
        var day = start.ToString("dddd d MMMM yyyy", culture);
        var times = $"{start.ToString("HH:mm", culture)}–{end.ToString("HH:mm", culture)}";
        var link = $"{appUrl.TrimEnd('/')}/#/b/{b.Id}";

        var (subject, lines) = message.Kind switch
        {
            NotificationKind.BookingConfirmed => (
                $"Booking confirmed: {resource}, {start.ToString("ddd d MMM", culture)} {start.ToString("HH:mm", culture)}",
                new[]
                {
                    "Your booking is confirmed.",
                    "",
                    $"Free cancellation up to {catalog.Policy.CancellationCutoffHours.ToString("0.#", culture)} hours before the start; the deposit is then refunded.",
                }),
            NotificationKind.Reminder => (
                $"Reminder: {resource} tomorrow at {start.ToString("HH:mm", culture)}",
                new[] { "A reminder of your booking tomorrow. See you there!" }),
            _ => (
                $"Booking cancelled: {resource}, {start.ToString("ddd d MMM", culture)} {start.ToString("HH:mm", culture)}",
                b.CancellationReason is { } reason
                    ? new[] { "We are sorry: the club had to cancel your booking.", $"Reason: {reason}", "", "Any deposit you paid is refunded to your card." }
                    : new[] { "Your booking is cancelled, as you asked.", "", "Any deposit you paid is refunded to your card." }),
        };

        var body = string.Join("\r\n", [
            $"Hello {b.CustomerName},",
            "",
            .. lines,
            "",
            $"  {resource}, {venue.Name}",
            $"  {day}, {times} ({venue.TimeZoneId.Replace('_', ' ')} time)",
            "",
            $"Your booking: {link}",
            "",
            venue.Name,
        ]);
        return new EmailMessage(message.Id, message.To, subject, body, message.DueAt);
    }
}
