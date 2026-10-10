using BookingApp.Core.Common;
using BookingApp.Core.Payments;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>One date of a weekly series and what became of it: held (Booking) or not (Error).</summary>
/// <param name="Index">0 for the first week.</param>
/// <param name="Start">The UTC start, worked out from the venue-local time for that date.</param>
public sealed record SeriesDate(int Index, DateOnly LocalDate, DateTimeOffset? Start, Booking? Booking, Error? Error);

/// <summary>What a series request came to, date by date.</summary>
public sealed record SeriesResult(Guid SeriesId, IReadOnlyList<SeriesDate> Dates);

/// <summary>Confirming or cancelling a series: the outcome for each of its bookings.</summary>
public sealed record SeriesChange(Booking Booking, Error? Error);

/// <summary>
/// Weekly series ("every Tuesday 18:00 for 8 weeks"). Each date is booked on its own, through the same rules as a
/// single booking, and the answer says which dates were held and why the others were not: one taken date does not
/// cancel the rest (library 8.2 "Bulk and batch operations": best effort, a result per item, a capped size). A series
/// is short and bounded, so its dates are booked straight away rather than kept as a rule (library 1.2 "Recurring
/// events"). Confirming and cancelling the series act on each date the same way.
/// </summary>
public sealed class SeriesService(
    BookingService bookingService, DepositService deposits, IResourceRepository resources, IBookingRepository bookings, TimeProvider time)
{
    public const int MinWeeks = 2;
    public const int MaxWeeks = 12;

    /// <summary>
    /// The UTC starts of a weekly series from <paramref name="firstStart"/>. Each week keeps the same venue wall-clock
    /// time: weeks are added in local time and each date is converted on its own, so a DST change does not move the
    /// booking by an hour (library 1.2: "the offset must be recomputed per occurrence"). A time that does not exist on a
    /// date (inside a DST gap) is null.
    /// </summary>
    public static IReadOnlyList<(DateOnly LocalDate, DateTimeOffset? Start)> WeeklyStarts(Venue venue, DateTimeOffset firstStart, int weeks)
    {
        var local = TimeZoneInfo.ConvertTime(firstStart, venue.TimeZone).DateTime;
        return [.. Enumerable.Range(0, weeks).Select(i =>
        {
            var date = local.AddDays(7 * i);
            DateTimeOffset? start = venue.TimeZone.IsInvalidTime(date) ? null : AvailabilityCalculator.ToUtc(date, venue);
            return (DateOnly.FromDateTime(date), start);
        })];
    }

    public async Task<Result<SeriesResult>> BookWeeklyAsync(CustomerId customer, string customerName, string? contactEmail, string resourceId,
        DateTimeOffset firstStart, int slots, int weeks, CancellationToken cancellationToken)
    {
        if (weeks is < MinWeeks or > MaxWeeks)
            return new Error(ErrorKind.RuleViolation, "invalid_weeks", $"A series is {MinWeeks} to {MaxWeeks} weeks.");
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        if (catalog.Find(resourceId) is null) return new Error(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");

        var seriesId = Guid.CreateVersion7();
        var dates = new List<SeriesDate>();
        foreach (var (localDate, start) in WeeklyStarts(catalog.Venue, firstStart, weeks))
        {
            if (start is not { } at)
            {
                dates.Add(new SeriesDate(dates.Count, localDate, null, null,
                    new Error(ErrorKind.RuleViolation, "time_does_not_exist", "The clocks change that day: this time does not exist.")));
                continue;
            }
            var booked = await bookingService.BookAsync(customer, customerName, contactEmail, resourceId, at, slots, seriesId, cancellationToken);
            dates.Add(new SeriesDate(dates.Count, localDate, at, booked.Value, booked.Error));
        }
        return Result<SeriesResult>.Ok(new SeriesResult(seriesId, dates));
    }

    /// <summary>The customer's bookings in a series, in date order. Not found for anyone else.</summary>
    public async Task<Result<IReadOnlyList<Booking>>> GetAsync(CustomerId customer, Guid seriesId, CancellationToken cancellationToken)
    {
        var found = (await bookings.ForCustomerAsync(customer, cancellationToken)).Where(b => b.SeriesId == seriesId).OrderBy(b => b.Time.Start).ToList();
        return found.Count == 0
            ? new Error(ErrorKind.NotFound, "series_not_found", "There is no such series.")
            : Result<IReadOnlyList<Booking>>.Ok(found);
    }

    /// <summary>Pays the deposit for and confirms every date still held. Each date succeeds or fails on its own.</summary>
    public Task<Result<IReadOnlyList<SeriesChange>>> ConfirmAsync(CustomerId customer, Guid seriesId, string? paymentToken, CancellationToken cancellationToken) =>
        ForEachAsync(customer, seriesId, b => b.StatusAt(time.GetUtcNow()) == BookingStatus.Pending,
            b => deposits.ConfirmAsync(customer, b.Id, paymentToken, cancellationToken), cancellationToken);

    /// <summary>
    /// Cancels every date that is still on (held or confirmed and not started), refunding deposits as the rules say. A date
    /// past the cancellation cut-off stays booked and is reported as too late.
    /// </summary>
    public Task<Result<IReadOnlyList<SeriesChange>>> CancelAsync(CustomerId customer, Guid seriesId, CancellationToken cancellationToken) =>
        ForEachAsync(customer, seriesId,
            b => b.StatusAt(time.GetUtcNow()) is BookingStatus.Pending or BookingStatus.Confirmed && b.Time.Start > time.GetUtcNow(),
            async b =>
            {
                var cancelled = await bookingService.CancelAsync(customer, b.Id, cancellationToken);
                if (cancelled.IsSuccess) await deposits.SettleAsync(b.Id, cancellationToken);
                return cancelled;
            }, cancellationToken);

    private async Task<Result<IReadOnlyList<SeriesChange>>> ForEachAsync(CustomerId customer, Guid seriesId, Func<Booking, bool> applies,
        Func<Booking, Task<Result<Booking>>> change, CancellationToken cancellationToken)
    {
        var series = await GetAsync(customer, seriesId, cancellationToken);
        if (!series.IsSuccess) return series.Error!;
        var changes = new List<SeriesChange>();
        foreach (var booking in series.Value!.Where(applies))
        {
            var result = await change(booking);
            changes.Add(new SeriesChange(result.Value ?? (await bookings.FindAsync(booking.Id, cancellationToken))!, result.Error));
        }
        return Result<IReadOnlyList<SeriesChange>>.Ok(changes);
    }
}
