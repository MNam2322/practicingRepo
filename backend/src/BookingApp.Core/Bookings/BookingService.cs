using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>The booking use cases. Controllers call these; they never touch a repository directly.</summary>
public sealed class BookingService(IResourceRepository resources, IBookingRepository bookings, BookingPolicy policy, TimeProvider time)
{
    public Result<IReadOnlyList<Slot>> Availability(string resourceId, DateOnly localDay, IReadOnlyList<Booking> blocking)
    {
        var resource = resources.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        var notBefore = time.GetUtcNow().AddMinutes(policy.MinLeadMinutes);
        return Result<IReadOnlyList<Slot>>.Ok(AvailabilityCalculator.ForDay(resource, resources.Venue, localDay, blocking.Select(b => b.Time), notBefore));
    }

    public async Task<Result<IReadOnlyList<Slot>>> AvailabilityAsync(string resourceId, DateOnly localDay, CancellationToken cancellationToken)
    {
        if (resources.Find(resourceId) is null) return NotFound(resourceId);
        var window = AvailabilityCalculator.DayWindow(resources.Venue, localDay);
        var blocking = await bookings.BlockingAsync(resourceId, window, cancellationToken);
        return Availability(resourceId, localDay, blocking);
    }

    public async Task<Result<Booking>> BookAsync(CustomerId customer, string customerName, string resourceId, DateTimeOffset start, int slots, CancellationToken cancellationToken)
    {
        var resource = resources.Find(resourceId);
        if (resource is null) return NotFound(resourceId);
        if (slots < 1) return new Error(ErrorKind.RuleViolation, "not_whole_slots", "Book at least one slot.");
        if (string.IsNullOrWhiteSpace(customerName)) return new Error(ErrorKind.RuleViolation, "name_required", "Enter the name for the booking.");

        var now = time.GetUtcNow();
        var range = TimeRange.FromStart(start, resource.SlotLength * slots);
        if (policy.Check(resource, resources.Venue, range, now) is { } broken) return broken;

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

    public Task<IReadOnlyList<Booking>> MineAsync(CustomerId customer, CancellationToken cancellationToken) =>
        bookings.ForCustomerAsync(customer, cancellationToken);

    private static Error NotFound(string resourceId) => new(ErrorKind.NotFound, "resource_not_found", $"There is no resource '{resourceId}'.");
}
