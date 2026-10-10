using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>One resource-day of availability: its slots, the closures that touch it, and the venue it is in.</summary>
public sealed record DayAvailability(Venue Venue, IReadOnlyList<Slot> Slots, IReadOnlyList<Closure> Closures);

/// <summary>The booking use cases. Controllers call these; they never touch a repository directly.</summary>
public sealed class BookingService(IResourceRepository resources, IBookingRepository bookings, IOccupancyReader occupancy, HoldOptions holds,
    TimeProvider time, IBookingMetrics? metrics = null)
{
    private readonly IBookingMetrics _metrics = metrics ?? NoBookingMetrics.Instance;

    public async Task<Result<DayAvailability>> AvailabilityAsync(string resourceId, DateOnly localDay, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var resource = catalog.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        var window = AvailabilityCalculator.DayWindow(catalog.Venue, localDay);
        var now = time.GetUtcNow();
        // Holds past their time no longer count, even before the expiry job has marked them. (A cached read: the
        // booking repositories invalidate it on every change.)
        var blocking = (await occupancy.OccupiedAsync(resource.Id, window, cancellationToken)).Where(o => o.At(now));
        var closures = await resources.ClosuresAsync(resource.Id, window, cancellationToken);
        var notBefore = now.AddMinutes(catalog.Policy.MinLeadMinutes);
        var slots = AvailabilityCalculator.ForDay(resource, catalog.Venue, localDay, blocking.Select(b => b.Time), notBefore, closures.Select(c => c.Time));
        return Result<DayAvailability>.Ok(new DayAvailability(catalog.Venue, slots, closures));
    }

    /// <param name="contactEmail">Where confirmations and reminders go; null for none.</param>
    public Task<Result<Booking>> BookAsync(CustomerId customer, string customerName, string? contactEmail, string resourceId, DateTimeOffset start, int slots,
        CancellationToken cancellationToken) =>
        BookAsync(customer, customerName, contactEmail, resourceId, start, slots, seriesId: null, cancellationToken);

    /// <summary>Holds a time, as one date of a weekly series when <paramref name="seriesId"/> is given (SeriesService).</summary>
    public async Task<Result<Booking>> BookAsync(CustomerId customer, string customerName, string? contactEmail, string resourceId, DateTimeOffset start, int slots,
        Guid? seriesId, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity("book");
        activity?.SetTag("booking.resource", resourceId);
        if (seriesId is { } series) activity?.SetTag("booking.series", series);
        var result = await HoldAsync(customer, customerName, contactEmail, resourceId, start, slots, seriesId, cancellationToken);
        Telemetry.Outcome(activity, result);
        if (result.IsSuccess) _metrics.Held(resourceId);
        else if (result.Error!.Code == "slot_taken") _metrics.Conflict(resourceId);
        return result;
    }

    private async Task<Result<Booking>> HoldAsync(CustomerId customer, string customerName, string? contactEmail, string resourceId, DateTimeOffset start,
        int slots, Guid? seriesId, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var resource = catalog.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        if (slots < 1) return new Error(ErrorKind.RuleViolation, "not_whole_slots", "Book at least one slot.");
        if (string.IsNullOrWhiteSpace(customerName)) return new Error(ErrorKind.RuleViolation, "name_required", "Enter the name for the booking.");

        var policy = catalog.Policy;
        var now = time.GetUtcNow();
        var range = TimeRange.FromStart(start, resource.SlotLength * slots);
        if (policy.Check(resource, catalog.Venue, range, now) is { } broken) return broken;

        var closures = await resources.ClosuresAsync(resource.Id, range, cancellationToken);
        if (closures.Count > 0)
            return new Error(ErrorKind.RuleViolation, "resource_closed", $"{resource.Name} is closed at that time: {closures[0].Reason}.");

        // A weekly series is one commitment: its dates count once toward the limit.
        var active = (await bookings.ForCustomerAsync(customer, cancellationToken))
            .Where(b => b.BlocksAt(now) && b.Time.End > now).Select(b => b.SeriesId ?? b.Id).ToHashSet();
        if (active.Count >= policy.MaxActiveBookingsPerCustomer && !(seriesId is { } series && active.Contains(series)))
            return new Error(ErrorKind.RuleViolation, "too_many_bookings", $"You can hold up to {policy.MaxActiveBookingsPerCustomer} upcoming bookings.");

        // A hold: it blocks the slot until the customer confirms, releases it, or it expires.
        var booking = Booking.CreateHold(resource.Id, range, customer, customerName, now, holds.HoldFor, contactEmail, seriesId);
        return await bookings.TryAddAsync(booking, cancellationToken)
            ? Result<Booking>.Ok(booking)
            : new Error(ErrorKind.Conflict, "slot_taken", "Someone has just booked that time. Pick another slot.");
    }

    public async Task<Result<Booking>> CancelAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity("cancel");
        activity?.SetTag("booking.id", bookingId);
        var result = await CancelCoreAsync(customer, bookingId, cancellationToken);
        Telemetry.Outcome(activity, result);
        if (result.IsSuccess) _metrics.Cancelled(result.Value!.ResourceId, "customer");
        return result;
    }

    private async Task<Result<Booking>> CancelCoreAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        var policy = (await resources.GetCatalogAsync(cancellationToken)).Policy;
        var result = booking.Cancel(customer, time.GetUtcNow(), policy);
        if (!result.IsSuccess) return result;
        return await bookings.UpdateAsync(booking, cancellationToken)
            ? result
            : new Error(ErrorKind.Conflict, "changed_meanwhile", "This booking was changed at the same time. Reload and try again.");
    }

    /// <summary>Turns the customer's hold into a booking. Safe to repeat: confirming a confirmed booking returns it.</summary>
    public async Task<Result<Booking>> ConfirmAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity("confirm");
        activity?.SetTag("booking.id", bookingId);
        var result = await ConfirmCoreAsync(customer, bookingId, cancellationToken);
        Telemetry.Outcome(activity, result);
        return result;
    }

    private async Task<Result<Booking>> ConfirmCoreAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        var wasConfirmed = booking.Status == BookingStatus.Confirmed;
        var result = booking.Confirm(customer, time.GetUtcNow());
        if (!result.IsSuccess || wasConfirmed) return result;
        if (await bookings.UpdateAsync(booking, cancellationToken))
        {
            _metrics.Confirmed(booking.ResourceId);
            return result;
        }
        // Someone changed it at the same moment: most likely the expiry job. Say which.
        var current = await bookings.FindAsync(bookingId, cancellationToken);
        return current?.Status switch
        {
            BookingStatus.Expired => Booking.HoldExpired,
            BookingStatus.Confirmed => Result<Booking>.Ok(current),
            _ => new Error(ErrorKind.Conflict, "changed_meanwhile", "This booking was changed at the same time. Reload and try again."),
        };
    }

    /// <summary>Releases every hold past its time (the expiry job). Returns how many.</summary>
    public Task<int> ExpireDueHoldsAsync(CancellationToken cancellationToken) => bookings.ExpireDueHoldsAsync(time.GetUtcNow(), cancellationToken);

    public async Task<Result<Booking>> GetAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null || booking.Customer != customer) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        return Result<Booking>.Ok(booking);
    }

    /// <summary>
    /// Moves every booking made under an anonymous customer id to a signed-in account, one booking at a time through
    /// the aggregate. Returns how many moved. A booking changed meanwhile is left where it is rather than overwritten.
    /// </summary>
    public async Task<int> ClaimAsync(CustomerId anonymous, CustomerId account, CancellationToken cancellationToken)
    {
        if (anonymous == account) return 0;
        var moved = 0;
        foreach (var booking in await bookings.ForCustomerAsync(anonymous, cancellationToken))
        {
            booking.TransferTo(account);
            if (await bookings.UpdateAsync(booking, cancellationToken)) moved++;
        }
        return moved;
    }

    public Task<IReadOnlyList<Booking>> MineAsync(CustomerId customer, CancellationToken cancellationToken) =>
        bookings.ForCustomerAsync(customer, cancellationToken);

    private static Error NotFound(string resourceId) => new(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");
}
