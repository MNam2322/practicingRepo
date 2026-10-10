using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>
/// Stores bookings. The one rule a repository must guarantee itself is that two blocking bookings for the same
/// resource never overlap, even when two requests arrive at the same moment. Checking first and then saving
/// is a race; the check and the save must be one atomic step.
/// Library: 7.4 "Optimistic and pessimistic concurrency", 7.9 "Atomic conditional update" and "Atomic upsert".
/// </summary>
public interface IBookingRepository
{
    /// <summary>Saves the booking unless a blocking booking on the same resource overlaps it. Returns false if one does.</summary>
    Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken);

    /// <summary>Saves a changed booking. Returns false if someone else saved it first (version mismatch).</summary>
    Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken);

    Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Blocking bookings for a resource that overlap a window.</summary>
    Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    /// <summary>Every booking of every resource, in any status, that overlaps the window, ordered by start (staff schedule).</summary>
    Task<IReadOnlyList<Booking>> InWindowAsync(TimeRange window, CancellationToken cancellationToken);
}
