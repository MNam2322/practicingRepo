using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>One resource-day of availability: its slots, the closures that touch it, and the venue it is in.</summary>
public sealed record DayAvailability(Venue Venue, IReadOnlyList<Slot> Slots, IReadOnlyList<Closure> Closures);

/// <summary>The booking use cases. Controllers call these; they never touch a repository directly.</summary>
public sealed class BookingService(IResourceRepository resources, IBookingRepository bookings, TimeProvider time)
{
    public async Task<Result<DayAvailability>> AvailabilityAsync(string resourceId, DateOnly localDay, CancellationToken cancellationToken)
    {
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var resource = catalog.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        var window = AvailabilityCalculator.DayWindow(catalog.Venue, localDay);
        var blocking = await bookings.BlockingAsync(resource.Id, window, cancellationToken);
        var closures = await resources.ClosuresAsync(resource.Id, window, cancellationToken);
        var notBefore = time.GetUtcNow().AddMinutes(catalog.Policy.MinLeadMinutes);
        var slots = AvailabilityCalculator.ForDay(resource, catalog.Venue, localDay, blocking.Select(b => b.Time), notBefore, closures.Select(c => c.Time));
        return Result<DayAvailability>.Ok(new DayAvailability(catalog.Venue, slots, closures));
    }

    public async Task<Result<Booking>> BookAsync(CustomerId customer, string customerName, string resourceId, DateTimeOffset start, int slots, CancellationToken cancellationToken)
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

        var active = (await bookings.ForCustomerAsync(customer, cancellationToken)).Count(b => b.BlocksSlot && b.Time.End > now);
        if (active >= policy.MaxActiveBookingsPerCustomer)
            return new Error(ErrorKind.RuleViolation, "too_many_bookings", $"You can hold up to {policy.MaxActiveBookingsPerCustomer} upcoming bookings.");

        var booking = Booking.Create(resource.Id, range, customer, customerName, now);
        return await bookings.TryAddAsync(booking, cancellationToken)
            ? Result<Booking>.Ok(booking)
            : new Error(ErrorKind.Conflict, "slot_taken", "Someone has just booked that time. Pick another slot.");
    }

    public async Task<Result<Booking>> CancelAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
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
