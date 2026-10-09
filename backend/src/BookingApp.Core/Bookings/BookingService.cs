using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>
/// Stores bookings. The one rule a store must guarantee itself is that two blocking bookings for the same
/// resource never overlap, even when two requests arrive at the same moment. Checking first and then saving
/// is a race; the check and the save must be one atomic step.
/// Library: 7.4 "Optimistic and pessimistic concurrency", 7.9 "Atomic conditional update" and "Atomic upsert".
/// </summary>
public interface IBookingStore
{
    /// <summary>Saves the booking unless a blocking booking on the same resource overlaps it. Returns false if one does.</summary>
    Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken);

    /// <summary>Saves a changed booking. Returns false if someone else saved it first (version mismatch).</summary>
    Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken);

    Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Blocking bookings for a resource that overlap a window.</summary>
    Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);
}

/// <summary>The booking use cases. Endpoints call these; they never touch the store directly.</summary>
public sealed class BookingService(IResourceCatalog catalog, IBookingStore store, BookingPolicy policy, TimeProvider time)
{
    public Result<IReadOnlyList<Slot>> Availability(string resourceId, DateOnly localDay, IReadOnlyList<Booking> blocking)
    {
        var resource = catalog.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        var notBefore = time.GetUtcNow().AddMinutes(policy.MinLeadMinutes);
        return Result<IReadOnlyList<Slot>>.Ok(AvailabilityCalculator.ForDay(resource, catalog.Venue, localDay, blocking.Select(b => b.Time), notBefore));
    }

    public async Task<Result<IReadOnlyList<Slot>>> AvailabilityAsync(string resourceId, DateOnly localDay, CancellationToken cancellationToken)
    {
        if (catalog.Find(resourceId) is null) return NotFound(resourceId);
        var window = AvailabilityCalculator.DayWindow(catalog.Venue, localDay);
        var blocking = await store.BlockingAsync(resourceId, window, cancellationToken);
        return Availability(resourceId, localDay, blocking);
    }

    public async Task<Result<Booking>> BookAsync(CustomerId customer, string customerName, string resourceId, DateTimeOffset start, int slots, CancellationToken cancellationToken)
    {
        var resource = catalog.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        if (slots < 1) return new Error(ErrorKind.RuleViolation, "not_whole_slots", "Book at least one slot.");
        if (string.IsNullOrWhiteSpace(customerName)) return new Error(ErrorKind.RuleViolation, "name_required", "Enter the name for the booking.");

        var now = time.GetUtcNow();
        var range = TimeRange.FromStart(start, resource.SlotLength * slots);
        if (policy.Check(resource, catalog.Venue, range, now) is { } broken) return broken;

        var active = (await store.ForCustomerAsync(customer, cancellationToken)).Count(b => b.BlocksSlot && b.Time.End > now);
        if (active >= policy.MaxActiveBookingsPerCustomer)
            return new Error(ErrorKind.RuleViolation, "too_many_bookings", $"You can hold up to {policy.MaxActiveBookingsPerCustomer} upcoming bookings.");

        var booking = Booking.Create(resource.Id, range, customer, customerName, now);
        return await store.TryAddAsync(booking, cancellationToken)
            ? Result<Booking>.Ok(booking)
            : new Error(ErrorKind.Conflict, "slot_taken", "Someone has just booked that time. Pick another slot.");
    }

    public async Task<Result<Booking>> CancelAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await store.FindAsync(bookingId, cancellationToken);
        if (booking is null) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        var result = booking.Cancel(customer, time.GetUtcNow(), policy);
        if (!result.IsSuccess) return result;
        return await store.UpdateAsync(booking, cancellationToken)
            ? result
            : new Error(ErrorKind.Conflict, "changed_meanwhile", "This booking was changed at the same time. Reload and try again.");
    }

    public async Task<Result<Booking>> GetAsync(CustomerId customer, Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await store.FindAsync(bookingId, cancellationToken);
        if (booking is null || booking.Customer != customer) return new Error(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
        return Result<Booking>.Ok(booking);
    }

    public Task<IReadOnlyList<Booking>> MineAsync(CustomerId customer, CancellationToken cancellationToken) =>
        store.ForCustomerAsync(customer, cancellationToken);

    private static Error NotFound(string resourceId) => new(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");
}
