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
    /// <summary>
    /// Saves the booking unless a booking that blocks at <c>booking.CreatedAt</c> (confirmed, or a hold that has not
    /// expired) overlaps it on the same resource. Holds past their time are expired first, in the same atomic step, so
    /// they never keep a slot. Returns false on a clash.
    /// </summary>
    Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken);

    /// <summary>Saves a changed booking. Returns false if someone else saved it first (version mismatch).</summary>
    Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken);

    Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Confirmed bookings and holds (Pending) for a resource that overlap a window. Callers check holds' expiry.</summary>
    Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken);

    Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    /// <summary>Every booking of every resource, in any status, that overlaps the window, ordered by start (staff schedule).</summary>
    Task<IReadOnlyList<Booking>> InWindowAsync(TimeRange window, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every hold past its time as Expired, in one conditional update (only rows still Pending and due), so it is
    /// safe to run twice and safe against a confirm racing it (library 7.9 "Atomic conditional update"). Returns how many.
    /// </summary>
    Task<int> ExpireDueHoldsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
