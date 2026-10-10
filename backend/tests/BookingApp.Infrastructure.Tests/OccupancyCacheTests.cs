using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Infrastructure.Tests;

/// <summary>
/// The race that leaves plainly-invalidated caches stale (library 8.6 "Invalidation"): a reader loads old data, a write
/// invalidates, then the reader stores the old data. With versioned keys the old data lands under a key nobody reads.
/// </summary>
public sealed class OccupancyCacheTests
{
    private static readonly TimeRange Day = TimeRange.FromStart(new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1));
    private static readonly TimeRange Ten = TimeRange.FromStart(new DateTimeOffset(2026, 6, 2, 3, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_slow_reader_that_started_before_a_write_cannot_leave_the_cache_stale()
    {
        var store = new SlowBookings();
        var versions = new OccupancyVersions();
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var reader = new CachedOccupancyReader(store, cache, versions);

        // Reader A starts and reads "nothing booked"; it is slow to finish.
        store.Release = new TaskCompletionSource();
        var slow = reader.OccupiedAsync("court-1", Day, Ct);
        await store.Started.Task;

        // Meanwhile a booking commits and invalidates the day.
        store.Booked.Add(Booking.Create("court-1", Ten, CustomerId.New(), "Nam", Ten.Start.AddDays(-1)));
        versions.Changed("court-1", Ten);

        // A finishes and stores what it read (stale), but under the old version's key.
        store.Release.SetResult();
        Assert.Empty(await slow);

        store.Release = null;
        var fresh = await reader.OccupiedAsync("court-1", Day, Ct);
        Assert.Equal(Ten, Assert.Single(fresh).Time);
        Assert.Equal(2, versions.Loads);
    }

    [Fact]
    public void A_change_touches_the_utc_dates_of_its_range_only()
    {
        var versions = new OccupancyVersions();
        versions.Changed("court-1", TimeRange.Create(new DateTimeOffset(2026, 6, 1, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 2, 1, 0, 0, TimeSpan.Zero)));
        Assert.Equal(1, versions.Of("court-1", new DateOnly(2026, 6, 1)));
        Assert.Equal(1, versions.Of("COURT-1", new DateOnly(2026, 6, 2)));
        Assert.Equal(0, versions.Of("court-1", new DateOnly(2026, 6, 3)));
        Assert.Equal(0, versions.Of("court-2", new DateOnly(2026, 6, 1)));
        // Ending exactly at midnight does not touch the next date.
        Assert.Equal([new DateOnly(2026, 6, 2)], OccupancyVersions.Dates(TimeRange.Create(new DateTimeOffset(2026, 6, 2, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero))));
    }

    /// <summary>A booking store whose blocking query can be held open, to line the race up.</summary>
    private sealed class SlowBookings : IBookingRepository
    {
        public List<Booking> Booked { get; } = [];
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource? Release { get; set; }

        public async Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
        {
            var snapshot = Booked.Where(b => b.Time.Overlaps(window)).ToList();   // read now...
            Started.TrySetResult();
            if (Release is { } release) await release.Task;                       // ...answer later
            return snapshot;
        }

        public Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Booking>> InWindowAsync(TimeRange window, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ExpireDueHoldsAsync(DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
