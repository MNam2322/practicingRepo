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

/// <summary>Reads the venue and its resources. The starter keeps them in configuration; TASKS.md moves them to the database.</summary>
public interface IResourceCatalog
{
    Venue Venue { get; }
    IReadOnlyList<Resource> Resources { get; }
    Resource? Find(string id);
}
