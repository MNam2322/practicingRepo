using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Keeps bookings in memory: lost on restart, fine for the starter and for tests.
/// The overlap check and the insert run under one lock, so two simultaneous requests for the same slot
/// cannot both succeed. A database repository must give the same guarantee with a constraint or a transaction,
/// not with a check-then-insert in application code (TASKS.md, task 4).
/// </summary>
internal sealed class InMemoryBookingRepository : IBookingRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Booking> _bookings = [];

    public Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var clash = _bookings.Values.Any(b => b.ResourceId == booking.ResourceId && b.BlocksSlot && b.Time.Overlaps(booking.Time));
            if (clash) return Task.FromResult(false);
            booking.MarkSaved();
            _bookings[booking.Id] = booking;
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // In memory the stored object is the same instance, so there is no stale copy to detect.
            // A database repository compares the version it loaded with the one in the row.
            if (!_bookings.ContainsKey(booking.Id)) return Task.FromResult(false);
            booking.MarkSaved();
            return Task.FromResult(true);
        }
    }

    public Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_bookings.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Booking> found = [.. _bookings.Values
                .Where(b => b.ResourceId == resourceId && b.BlocksSlot && b.Time.Overlaps(window))
                .OrderBy(b => b.Time.Start)];
            return Task.FromResult(found);
        }
    }

    public Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Booking> found = [.. _bookings.Values.Where(b => b.Customer == customer).OrderBy(b => b.Time.Start)];
            return Task.FromResult(found);
        }
    }
}
