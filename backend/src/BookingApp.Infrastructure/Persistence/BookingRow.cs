using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// How a booking is stored: a plain row. The domain <see cref="Booking"/> keeps its rules and has no public setters,
/// so the repository maps between the two with <see cref="Booking.Restore"/>; EF Core never constructs the aggregate.
/// </summary>
internal sealed class BookingRow
{
    public Guid Id { get; set; }
    public string ResourceId { get; set; } = "";
    /// <summary>UTC. On SQLite stored as integer ticks so it can be compared and indexed (BookingDbContext).</summary>
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public BookingStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    /// <summary>Why staff cancelled it, if they did.</summary>
    public string? CancellationReason { get; set; }
    /// <summary>When a hold stops counting; null for bookings that were never a hold.</summary>
    public DateTimeOffset? HoldExpiresAt { get; set; }
    /// <summary>Where notifications go; null when the customer gave none.</summary>
    public string? ContactEmail { get; set; }
    /// <summary>Optimistic concurrency token: every update checks it in its WHERE clause and increments it.</summary>
    public int Version { get; set; }

    public static BookingRow From(Booking b) => new()
    {
        Id = b.Id,
        ResourceId = b.ResourceId,
        StartUtc = b.Time.Start,
        EndUtc = b.Time.End,
        CustomerId = b.Customer.Value,
        CustomerName = b.CustomerName,
        Status = b.Status,
        CreatedAt = b.CreatedAt,
        CancelledAt = b.CancelledAt,
        CancellationReason = b.CancellationReason,
        HoldExpiresAt = b.HoldExpiresAt,
        ContactEmail = b.ContactEmail,
        Version = b.Version,
    };

    public Booking ToDomain() => Booking.Restore(Id, ResourceId, TimeRange.Create(StartUtc, EndUtc), new CustomerId(CustomerId),
        CustomerName, Status, CreatedAt, CancelledAt, Version, CancellationReason, HoldExpiresAt, ContactEmail);
}
