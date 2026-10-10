using System.Globalization;
using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Waitlist;

/// <summary>The "Waitlist" settings.</summary>
public sealed class WaitlistOptions
{
    public const string Section = "Waitlist";

    /// <summary>How often freed times are looked for.</summary>
    public int SweepSeconds { get; set; } = 10;

    /// <summary>How long the person told first has before the next in line is told too (while the time is still free).</summary>
    public int OfferMinutes { get; set; } = 30;
}

/// <summary>An entry and its place in line (1 is next; 0 once it is no longer waiting).</summary>
public sealed record WaitlistPlace(WaitlistEntry Entry, int Position);

/// <summary>
/// Waiting lists for taken times. Join a time someone else has; when it frees up (a cancellation, a released or expired
/// hold), the first person in line is emailed through the outbox, in the same transaction as their entry is marked told,
/// so nobody is told twice or forgotten. If the time is still free after <see cref="WaitlistOptions.OfferMinutes"/>, the
/// next person is told too. The freed times are found by a sweep (and straight after a cancel), so every way a time can
/// free up is covered without each one having to remember the waitlist.
/// </summary>
public sealed class WaitlistService(
    IWaitlistRepository waitlist, IBookingRepository bookings, IResourceRepository resources,
    WaitlistOptions options, NotificationOptions notifications, TimeProvider time)
{
    public async Task<Result<WaitlistPlace>> JoinAsync(CustomerId customer, string customerName, string? email, string resourceId,
        DateTimeOffset start, int slots, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var resource = catalog.Find(resourceId);
        if (resource is null) return new Error(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");
        if (string.IsNullOrWhiteSpace(customerName)) return new Error(ErrorKind.RuleViolation, "name_required", "Enter the name for the booking.");
        if (string.IsNullOrWhiteSpace(email))
            return new Error(ErrorKind.RuleViolation, "email_required", "Enter an email address, so we can tell you when the time frees up.");

        var now = time.GetUtcNow();
        var range = TimeRange.FromStart(start, resource.SlotLength * Math.Max(slots, 1));
        // Only a time that could be booked is worth waiting for: the same rules as booking.
        if (catalog.Policy.Check(resource, catalog.Venue, range, now) is { } broken) return broken;
        if ((await resources.ClosuresAsync(resource.Id, range, cancellationToken)).Count > 0)
            return new Error(ErrorKind.RuleViolation, "resource_closed", $"{resource.Name} is closed at that time.");
        if (!await TakenAsync(resource.Id, range, now, cancellationToken))
            return new Error(ErrorKind.Conflict, "slot_free", "That time is free: book it instead.");
        var mine = await bookings.ForCustomerAsync(customer, cancellationToken);
        if (mine.Any(b => b.ResourceId == resource.Id && b.BlocksAt(now) && b.Time.Overlaps(range)))
            return new Error(ErrorKind.Conflict, "already_yours", "You have booked that time already.");

        var entry = WaitlistEntry.Join(resource.Id, range, customer, customerName, email, now);
        if (!await waitlist.TryAddAsync(entry, cancellationToken))
            return new Error(ErrorKind.Conflict, "already_waiting", "You are already on the waitlist for that time.");
        return Result<WaitlistPlace>.Ok(await PlaceAsync(entry, cancellationToken));
    }

    /// <summary>The customer's entries for times still to come, with their places in line.</summary>
    public async Task<IReadOnlyList<WaitlistPlace>> MineAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var open = await waitlist.OpenAsync(now, cancellationToken);
        return [.. (await waitlist.ForCustomerAsync(customer, cancellationToken))
            .Where(e => e.Time.Start > now)
            .OrderBy(e => e.Time.Start)
            .Select(e => new WaitlistPlace(e, Position(e, open)))];
    }

    public async Task<Result<WaitlistPlace>> LeaveAsync(CustomerId customer, Guid id, CancellationToken cancellationToken)
    {
        var entry = await waitlist.FindAsync(id, cancellationToken);
        if (entry is null || entry.Customer != customer) return new Error(ErrorKind.NotFound, "waitlist_entry_not_found", "There is no such waitlist entry.");
        if (entry.Leave() && !await waitlist.UpdateAsync(entry, cancellationToken))
            return new Error(ErrorKind.Conflict, "changed_meanwhile", "This entry was changed at the same time. Reload and try again.");
        return Result<WaitlistPlace>.Ok(new WaitlistPlace(entry, 0));
    }

    /// <summary>
    /// Tells the first person in line for each time that is free again (and the next, once the first has had
    /// <see cref="WaitlistOptions.OfferMinutes"/>). Returns how many were told. Safe to run at any time and twice: an
    /// entry is marked told with its email in one transaction, only if it is still waiting.
    /// </summary>
    public async Task<int> OfferFreedTimesAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await waitlist.ExpirePastAsync(now, cancellationToken);
        var open = await waitlist.OpenAsync(now, cancellationToken);
        if (open.Count == 0) return 0;
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var told = 0;
        foreach (var line in open.GroupBy(e => (e.ResourceId, e.Time)))
        {
            // Someone was told recently: give them their turn.
            if (line.Any(e => e.Status == WaitlistStatus.Notified && e.NotifiedAt > now.AddMinutes(-options.OfferMinutes))) continue;
            var next = line.Where(e => e.Status == WaitlistStatus.Waiting).OrderBy(e => e.CreatedAt).FirstOrDefault();
            if (next is null || catalog.Find(next.ResourceId) is not { } resource) continue;
            if (await TakenAsync(resource.Id, next.Time, now, cancellationToken)) continue;
            if ((await resources.ClosuresAsync(resource.Id, next.Time, cancellationToken)).Count > 0) continue;

            var (subject, body) = Render(next, resource, catalog);
            if (await waitlist.NotifyAsync(next, next.Notify(now, subject, body), cancellationToken)) told++;
        }
        return told;
    }

    private async Task<bool> TakenAsync(string resourceId, TimeRange range, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await bookings.BlockingAsync(resourceId, range, cancellationToken)).Any(b => b.BlocksAt(now));

    private async Task<WaitlistPlace> PlaceAsync(WaitlistEntry entry, CancellationToken cancellationToken) =>
        new(entry, Position(entry, await waitlist.OpenAsync(time.GetUtcNow(), cancellationToken)));

    private static int Position(WaitlistEntry entry, IReadOnlyList<WaitlistEntry> open) =>
        entry.Status != WaitlistStatus.Waiting ? 0
            : 1 + open.Count(e => e.Status == WaitlistStatus.Waiting && e.ResourceId == entry.ResourceId && e.Time == entry.Time
                && (e.CreatedAt < entry.CreatedAt || (e.CreatedAt == entry.CreatedAt && e.Id.CompareTo(entry.Id) < 0)));

    private (string Subject, string Body) Render(WaitlistEntry entry, Resource resource, ResourceCatalog catalog)
    {
        var venue = catalog.Venue;
        var culture = CultureInfo.InvariantCulture;
        var start = TimeZoneInfo.ConvertTime(entry.Time.Start, venue.TimeZone);
        var end = TimeZoneInfo.ConvertTime(entry.Time.End, venue.TimeZone);
        var link = $"{notifications.AppUrl.TrimEnd('/')}/#/r/{resource.Id}/{start.ToString("yyyy-MM-dd", culture)}?at={Uri.EscapeDataString(entry.Time.Start.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", culture))}";
        var subject = $"Free again: {resource.Name}, {start.ToString("ddd d MMM", culture)} {start.ToString("HH:mm", culture)}";
        var body = string.Join("\r\n",
            $"Hello {entry.CustomerName},",
            "",
            "Good news: a time you were waiting for is free again.",
            "",
            $"  {resource.Name}, {venue.Name}",
            $"  {start.ToString("dddd d MMMM yyyy", culture)}, {start.ToString("HH:mm", culture)}–{end.ToString("HH:mm", culture)} ({venue.TimeZoneId.Replace('_', ' ')} time)",
            "",
            "You were first in line, but it is first come, first served, so book it soon:",
            link,
            "",
            venue.Name);
        return (subject, body);
    }
}
