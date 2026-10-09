using BookingApp.Core.Bookings;

namespace BookingApp.Core.Scheduling;

/// <summary>Read use cases for the venue: its resources, its booking rules and what day it is there.</summary>
public sealed class VenueService(IResourceRepository resources, BookingPolicy policy, TimeProvider time)
{
    public BookingPolicy Policy => policy;

    /// <summary>The venue and its resources, read once for the request.</summary>
    public Task<ResourceCatalog> CatalogAsync(CancellationToken cancellationToken) => resources.GetCatalogAsync(cancellationToken);

    /// <summary>Today's date in the venue's time zone, not the server's.</summary>
    public DateOnly Today(Venue venue) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), venue.TimeZone).DateTime);
}
