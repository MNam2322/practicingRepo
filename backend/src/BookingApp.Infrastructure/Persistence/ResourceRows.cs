using BookingApp.Core.Scheduling;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>The one venue. The table holds a single row (id = 1, enforced by a CHECK).</summary>
internal sealed class VenueRow
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
}

internal sealed class ResourceRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ResourceKind Kind { get; set; }
    public int Capacity { get; set; }
    public int SlotMinutes { get; set; }
    public int MaxSlotsPerBooking { get; set; }
    /// <summary>Display order on the resources page.</summary>
    public int SortOrder { get; set; }
    public List<OpeningHoursRow> Hours { get; set; } = [];

    public Resource ToDomain() => new(Id, Name, Kind, Capacity, SlotMinutes, MaxSlotsPerBooking,
        [.. Hours.OrderBy(h => h.Day).Select(h => new OpeningHours(h.Day, h.Open, h.Close))]);

    public static ResourceRow From(Resource r, int sortOrder) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Kind = r.Kind,
        Capacity = r.Capacity,
        SlotMinutes = r.SlotMinutes,
        MaxSlotsPerBooking = r.MaxSlotsPerBooking,
        SortOrder = sortOrder,
        Hours = [.. r.Hours.Select(h => new OpeningHoursRow { ResourceId = r.Id, Day = h.Day, Open = h.Open, Close = h.Close })],
    };
}

/// <summary>One day's opening hours for a resource, in venue local time. At most one row per resource and day.</summary>
internal sealed class OpeningHoursRow
{
    public string ResourceId { get; set; } = "";
    public DayOfWeek Day { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}

internal sealed class ClosureRow
{
    public Guid Id { get; set; }
    /// <summary>Null: the whole venue is closed.</summary>
    public string? ResourceId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public Closure ToDomain() => new(Id, ResourceId, TimeRange.Create(StartUtc, EndUtc), Reason);
}
