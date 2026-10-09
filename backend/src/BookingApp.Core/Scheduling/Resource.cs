namespace BookingApp.Core.Scheduling;

/// <summary>The place that owns the resources. All opening hours are in its time zone.</summary>
/// <param name="TimeZoneId">An IANA id such as "Asia/Ho_Chi_Minh". .NET resolves IANA ids on Windows, macOS and Linux.</param>
public sealed record Venue(string Name, string TimeZoneId)
{
    public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}

/// <summary>Opening hours for one day of the week, in venue local time. Close must be after Open (no overnight opening yet).</summary>
public sealed record OpeningHours(DayOfWeek Day, TimeOnly Open, TimeOnly Close);

public enum ResourceKind { Court, Room, Table, Other }

/// <summary>Something people book by the slot: a court, a meeting room, a table.</summary>
/// <param name="Id">A readable slug such as "court-1"; used in URLs.</param>
/// <param name="SlotMinutes">Bookings start on this grid and last a whole number of slots.</param>
/// <param name="MaxSlotsPerBooking">The longest booking allowed, in slots.</param>
public sealed record Resource(
    string Id,
    string Name,
    ResourceKind Kind,
    int Capacity,
    int SlotMinutes,
    int MaxSlotsPerBooking,
    IReadOnlyList<OpeningHours> Hours)
{
    public TimeSpan SlotLength => TimeSpan.FromMinutes(SlotMinutes);

    public OpeningHours? HoursOn(DayOfWeek day) => Hours.FirstOrDefault(h => h.Day == day);
}

/// <summary>
/// A time when a resource cannot be booked: a holiday, maintenance, an event. The range is UTC, like bookings.
/// </summary>
/// <param name="ResourceId">The closed resource, or null when the whole venue is closed.</param>
/// <param name="Reason">Shown to customers, such as "Court resurfacing".</param>
public sealed record Closure(Guid Id, string? ResourceId, TimeRange Time, string Reason)
{
    public bool AppliesTo(string resourceId) => ResourceId is null || string.Equals(ResourceId, resourceId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The venue and its resources as read at one moment: one load per request, then plain lookups.</summary>
public sealed class ResourceCatalog(Venue venue, IReadOnlyList<Resource> resources)
{
    private readonly Dictionary<string, Resource> _byId = resources.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    public Venue Venue { get; } = venue;
    public IReadOnlyList<Resource> Resources { get; } = resources;
    public Resource? Find(string id) => _byId.GetValueOrDefault(id);
}

/// <summary>Reads the venue, its resources and their closures. Stored in the database (EfResourceRepository) or in configuration (tests, InMemory).</summary>
public interface IResourceRepository
{
    Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Closures for this resource, or for the whole venue, that overlap the window, ordered by start.</summary>
    Task<IReadOnlyList<Closure>> ClosuresAsync(string resourceId, TimeRange window, CancellationToken cancellationToken);

    Task AddClosureAsync(Closure closure, CancellationToken cancellationToken);
}
