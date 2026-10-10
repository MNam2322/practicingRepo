using BookingApp.Core.Scheduling;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>The one venue and its booking rules. The table holds a single row (id = 1, enforced by a CHECK).</summary>
internal sealed class VenueRow
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public int MaxDaysAhead { get; set; } = 30;
    public int MinLeadMinutes { get; set; } = 15;
    public double CancellationCutoffHours { get; set; } = 2;
    public int MaxActiveBookingsPerCustomer { get; set; } = 5;

    public Core.Bookings.BookingPolicy Policy() => new()
    {
        MaxDaysAhead = MaxDaysAhead,
        MinLeadMinutes = MinLeadMinutes,
        CancellationCutoffHours = CancellationCutoffHours,
        MaxActiveBookingsPerCustomer = MaxActiveBookingsPerCustomer,
    };

    public void SetPolicy(Core.Bookings.BookingPolicy p)
    {
        MaxDaysAhead = p.MaxDaysAhead;
        MinLeadMinutes = p.MinLeadMinutes;
        CancellationCutoffHours = p.CancellationCutoffHours;
        MaxActiveBookingsPerCustomer = p.MaxActiveBookingsPerCustomer;
    }
}

/// <summary>One audit log row. Append-only: a database trigger refuses UPDATE and DELETE.</summary>
internal sealed class AuditRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid ActorId { get; set; }
    public string ActorEmail { get; set; } = "";
    public string? RequestId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }

    public static AuditRow From(Core.Admin.AuditEntry e) => new()
    {
        Id = e.Id,
        OccurredAt = e.OccurredAt,
        ActorId = e.Actor.AccountId,
        ActorEmail = e.Actor.Email,
        RequestId = e.Actor.RequestId,
        Action = e.Action,
        Entity = e.Entity,
        EntityId = e.EntityId,
        Before = e.Before,
        After = e.After,
    };

    public Core.Admin.AuditEntry ToDomain() => new(Id, OccurredAt, new Core.Admin.Actor(ActorId, ActorEmail, RequestId), Action, Entity, EntityId, Before, After);
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
