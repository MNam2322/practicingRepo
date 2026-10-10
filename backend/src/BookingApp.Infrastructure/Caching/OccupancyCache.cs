using System.Collections.Concurrent;
using System.Globalization;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.Extensions.Caching.Hybrid;

namespace BookingApp.Infrastructure.Caching;

/// <summary>
/// Per resource and UTC date, a version that moves on with every booking change. The booking repositories call
/// <see cref="Changed"/> after a write commits; the cache puts the versions in its keys (library 8.6 "Invalidation":
/// versioned keys). A reader that loaded old data before a write and stores it after the write stores it under the old
/// version's key, which no later reader asks for: the ordering race that leaves plain invalidation stale cannot happen.
/// In memory, for one instance; with a distributed cache (L2) the versions would have to live there too.
/// </summary>
public sealed class OccupancyVersions
{
    private readonly ConcurrentDictionary<(string Resource, DateOnly Date), long> _versions = new();

    /// <summary>A booking for <paramref name="resourceId"/> over <paramref name="time"/> was added or changed.</summary>
    public void Changed(string resourceId, TimeRange time)
    {
        foreach (var date in Dates(time)) _versions.AddOrUpdate((Key(resourceId), date), 1, (_, v) => v + 1);
    }

    public long Of(string resourceId, DateOnly date) => _versions.GetValueOrDefault((Key(resourceId), date));

    private long _loads;

    /// <summary>How many times the cache asked the database (tests: proof that the cache is used and refreshed).</summary>
    public long Loads => Interlocked.Read(ref _loads);

    internal void Loaded() => Interlocked.Increment(ref _loads);

    /// <summary>The UTC dates a range touches: a cached window and a booking that overlap always share one.</summary>
    public static IEnumerable<DateOnly> Dates(TimeRange time)
    {
        var last = DateOnly.FromDateTime(time.End.UtcDateTime.AddTicks(-1));
        for (var d = DateOnly.FromDateTime(time.Start.UtcDateTime); d <= last; d = d.AddDays(1)) yield return d;
    }

    private static string Key(string resourceId) => resourceId.ToLowerInvariant();
}

/// <summary>
/// Availability's read of what occupies a resource, through HybridCache: request coalescing (one database query for many
/// simultaneous readers) and a size bound, with a short expiry as the last line of defence (library 8.6 "HybridCache",
/// 14.2 "Fix 3 — cache last, with invalidation designed in"). Entries are plain values, so no request can change what
/// another sees; a hold's expiry travels with it, so an expired hold stops counting without any invalidation.
/// </summary>
public sealed class CachedOccupancyReader(IBookingRepository bookings, HybridCache cache, OccupancyVersions versions) : IOccupancyReader
{
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1) };

    public async Task<IReadOnlyList<Occupied>> OccupiedAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
    {
        var dates = OccupancyVersions.Dates(window).ToList();
        var culture = CultureInfo.InvariantCulture;
        var key = $"occupancy:{resourceId.ToLowerInvariant()}:{window.Start.UtcTicks}:{window.End.UtcTicks}:"
            + string.Join('.', dates.Select(d => versions.Of(resourceId, d).ToString(culture)));
        return await cache.GetOrCreateAsync(key, (bookings, resourceId, window, versions), static async (state, ct) =>
        {
            state.versions.Loaded();
            var found = await state.bookings.BlockingAsync(state.resourceId, state.window, ct);
            return found.Select(Occupied.From).ToArray();
        }, Options, cancellationToken: cancellationToken);
    }
}
