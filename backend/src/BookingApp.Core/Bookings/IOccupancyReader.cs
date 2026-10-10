using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>A time something occupies: a confirmed booking (Until null) or a hold that stops counting at Until.</summary>
public sealed record Occupied(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset? Until)
{
    public TimeRange Time => TimeRange.Create(Start, End);

    /// <summary>Occupies the time at this moment (a hold past its time does not).</summary>
    public bool At(DateTimeOffset now) => Until is null || now < Until;

    public static Occupied From(Booking b) => new(b.Time.Start, b.Time.End, b.Status == BookingStatus.Pending ? b.HoldExpiresAt : null);
}

/// <summary>
/// What occupies a resource in a window, for showing availability. It may be served from a cache, but never stale after
/// a booking change: the booking repositories invalidate it on every write. Checks that must be exact (the overlap
/// guarantee, "bookings in the way") read the repository instead.
/// </summary>
public interface IOccupancyReader
{
    Task<IReadOnlyList<Occupied>> OccupiedAsync(string resourceId, TimeRange window, CancellationToken cancellationToken);
}

/// <summary>Reads the repository every time: for tests and anything that wants no cache.</summary>
public sealed class DirectOccupancyReader(IBookingRepository bookings) : IOccupancyReader
{
    public async Task<IReadOnlyList<Occupied>> OccupiedAsync(string resourceId, TimeRange window, CancellationToken cancellationToken) =>
        [.. (await bookings.BlockingAsync(resourceId, window, cancellationToken)).Select(Occupied.From)];
}
