using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Staff;

/// <summary>One resource on the day schedule: its hours that day, every booking (any status) and its closures.</summary>
public sealed record ScheduleColumn(Resource Resource, OpeningHours? Hours, IReadOnlyList<Booking> Bookings, IReadOnlyList<Closure> Closures);

/// <summary>The whole venue for one local day, resources as columns.</summary>
public sealed record DaySchedule(Venue Venue, DateOnly Date, IReadOnlyList<ScheduleColumn> Columns);

/// <summary>
/// Staff use cases: the day schedule, the outcome of a booking (completed or no-show), cancelling any booking with a
/// reason, and blocking time. Every state change goes through the Booking aggregate's transition table.
/// Callers must hold the ViewSchedule policy (checked by the controller).
/// </summary>
public sealed class StaffService(IResourceRepository resources, IBookingRepository bookings, TimeProvider time)
{
    public async Task<DaySchedule> ScheduleAsync(DateOnly localDay, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var window = AvailabilityCalculator.DayWindow(catalog.Venue, localDay);
        var dayBookings = await bookings.InWindowAsync(window, cancellationToken);
        var closures = await resources.AllClosuresAsync(window, cancellationToken);
        var columns = catalog.Resources.Select(r => new ScheduleColumn(
            r,
            r.HoursOn(localDay.DayOfWeek),
            [.. dayBookings.Where(b => string.Equals(b.ResourceId, r.Id, StringComparison.OrdinalIgnoreCase))],
            [.. closures.Where(c => c.AppliesTo(r.Id))])).ToList();
        return new DaySchedule(catalog.Venue, localDay, columns);
    }

    public DateOnly Today(Venue venue) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), venue.TimeZone).DateTime);

    public DateTimeOffset Now => time.GetUtcNow();

    public Task<Result<Booking>> CompleteAsync(Guid bookingId, CancellationToken cancellationToken) =>
        ChangeAsync(bookingId, b => b.Complete(time.GetUtcNow()), cancellationToken);

    public Task<Result<Booking>> MarkNoShowAsync(Guid bookingId, CancellationToken cancellationToken) =>
        ChangeAsync(bookingId, b => b.MarkNoShow(time.GetUtcNow()), cancellationToken);

    public Task<Result<Booking>> CancelAsync(Guid bookingId, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(bookingId, b => b.CancelByStaff(reason, time.GetUtcNow()), cancellationToken);

    /// <summary>
    /// Blocks time on a resource (maintenance, coaching) as a closure, so customers cannot book it. Refused while
    /// confirmed bookings overlap: staff must cancel those first, with a reason the customer will see.
    /// </summary>
    public async Task<Result<Closure>> BlockAsync(string resourceId, DateTimeOffset start, DateTimeOffset end, string reason, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var resource = catalog.Find(resourceId);
        if (resource is null) return new Error(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");
        if (end <= start) return new Error(ErrorKind.RuleViolation, "end_before_start", "The end must be after the start.");
        if (string.IsNullOrWhiteSpace(reason)) return new Error(ErrorKind.RuleViolation, "reason_required", "Say why the time is blocked.");
        var range = TimeRange.Create(start, end);
        var now = time.GetUtcNow();
        var inTheWay = (await bookings.BlockingAsync(resource.Id, range, cancellationToken)).Count(b => b.BlocksAt(now));
        if (inTheWay > 0)
            return new Error(ErrorKind.Conflict, "bookings_in_the_way",
                $"{inTheWay} booking(s) or hold(s) overlap that time. Cancel them first, with a reason, then block the time.");
        var closure = new Closure(Guid.CreateVersion7(), resource.Id, range, reason.Trim());
        await resources.AddClosureAsync(closure, cancellationToken);
        return Result<Closure>.Ok(closure);
    }

    private async Task<Result<Booking>> ChangeAsync(Guid bookingId, Func<Booking, Result<Booking>> change, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        var result = change(booking);
        if (!result.IsSuccess) return result;
        return await bookings.UpdateAsync(booking, cancellationToken)
            ? result
            : new Error(ErrorKind.Conflict, "changed_meanwhile", "This booking was changed at the same time. Reload and try again.");
    }
}
