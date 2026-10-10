namespace BookingApp.Core.Scheduling;

/// <summary>Read use cases for the venue: its resources, its booking rules and what day it is there.</summary>
public sealed class VenueService(IResourceRepository resources, TimeProvider time)
{
    /// <summary>The venue, its resources and its booking rules, read once for the request.</summary>
    public Task<ResourceCatalog> CatalogAsync(CancellationToken cancellationToken) => resources.GetCatalogAsync(cancellationToken);

    /// <summary>Today's date in the venue's time zone, not the server's.</summary>
    public DateOnly Today(Venue venue) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), venue.TimeZone).DateTime);
}
