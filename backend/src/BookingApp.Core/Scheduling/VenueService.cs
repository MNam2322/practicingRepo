using BookingApp.Core.Bookings;

namespace BookingApp.Core.Scheduling;

/// <summary>Read use cases for the venue: its resources, its booking rules and what day it is there.</summary>
public sealed class VenueService(IResourceRepository resources, BookingPolicy policy, TimeProvider time)
{
    public Venue Venue => resources.Venue;

    public IReadOnlyList<Resource> Resources => resources.Resources;

    public BookingPolicy Policy => policy;

    public Resource? Find(string id) => resources.Find(id);

    /// <summary>Today's date in the venue's time zone, not the server's.</summary>
    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), resources.Venue.TimeZone).DateTime);
}
