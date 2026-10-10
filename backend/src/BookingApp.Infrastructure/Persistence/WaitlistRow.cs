using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Core.Waitlist;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>How a waitlist entry is stored.</summary>
internal sealed class WaitlistRow
{
    public Guid Id { get; set; }
    public string ResourceId { get; set; } = "";
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public WaitlistStatus Status { get; set; }
    public DateTimeOffset? NotifiedAt { get; set; }
    public int Version { get; set; }

    public static WaitlistRow From(WaitlistEntry e) => new()
    {
        Id = e.Id,
        ResourceId = e.ResourceId,
        StartUtc = e.Time.Start,
        EndUtc = e.Time.End,
        CustomerId = e.Customer.Value,
        CustomerName = e.CustomerName,
        Email = e.Email,
        CreatedAt = e.CreatedAt,
        Status = e.Status,
        NotifiedAt = e.NotifiedAt,
        Version = e.Version,
    };

    public WaitlistEntry ToDomain() => WaitlistEntry.Restore(Id, ResourceId, TimeRange.Create(StartUtc, EndUtc), new CustomerId(CustomerId),
        CustomerName, Email, CreatedAt, Status, NotifiedAt, Version);
}
