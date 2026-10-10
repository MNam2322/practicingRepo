using BookingApp.Core.Payments;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>How a deposit is stored. Amounts are integer minor units with their currency (library 4.9 Case 4).</summary>
internal sealed class PaymentRow
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "";
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    /// <summary>Optimistic concurrency token, as on bookings.</summary>
    public int Version { get; set; }

    public static PaymentRow From(Payment p) => new()
    {
        Id = p.Id,
        BookingId = p.BookingId,
        AmountMinor = p.Amount.Minor,
        Currency = p.Amount.Currency,
        Status = p.Status,
        ProviderReference = p.ProviderReference,
        FailureCode = p.FailureCode,
        CreatedAt = p.CreatedAt,
        PaidAt = p.PaidAt,
        RefundedAt = p.RefundedAt,
        Version = p.Version,
    };

    public Payment ToDomain() => Payment.Restore(Id, BookingId, new Money(AmountMinor, Currency), Status, ProviderReference, FailureCode,
        CreatedAt, PaidAt, RefundedAt, Version);
}

/// <summary>A webhook event received from the payment provider. The event id is the primary key: one row per event, ever.</summary>
internal sealed class PaymentEventRow
{
    public string EventId { get; set; } = "";
    public PaymentEventType Type { get; set; }
    public Guid PaymentId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    /// <summary>The raw body, kept for support and audits.</summary>
    public string Payload { get; set; } = "";
}
