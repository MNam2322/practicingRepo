namespace BookingApp.Core.Payments;

/// <summary>Where a deposit is.</summary>
public enum PaymentStatus
{
    /// <summary>Sent to the provider; the outcome is not known yet (it timed out). Retrying reuses the same payment.</summary>
    Pending,
    /// <summary>The provider took the money.</summary>
    Paid,
    /// <summary>The provider refused (a declined card) or reported a failure. No money was taken.</summary>
    Failed,
    /// <summary>The money went back.</summary>
    Refunded,
}

/// <summary>
/// A deposit for one booking. Its <see cref="Id"/> is also the idempotency key sent to the provider, so however often a
/// charge or a refund is retried, the provider moves the money once (library 12.5 "Idempotency keys — preventing double
/// charges"). Providers repeat themselves (webhooks are delivered at least once), so each change below reports whether
/// it changed anything, and repeating one is harmless:
/// <code>
///   Pending --paid-----> Paid --refunded--> Refunded
///   Pending --failed---> Failed    (final: paying again starts a new payment)
/// </code>
/// A booking has at most one live payment (Pending or Paid), enforced by the repository.
/// </summary>
public sealed class Payment
{
    public const int MaxReferenceLength = 100;
    public const int MaxCodeLength = 64;

    private Payment(Guid id, Guid bookingId, Money amount, PaymentStatus status, string? providerReference, string? failureCode,
        DateTimeOffset createdAt, DateTimeOffset? paidAt, DateTimeOffset? refundedAt, int version)
    {
        Id = id;
        BookingId = bookingId;
        Amount = amount;
        Status = status;
        ProviderReference = providerReference;
        FailureCode = failureCode;
        CreatedAt = createdAt;
        PaidAt = paidAt;
        RefundedAt = refundedAt;
        Version = version;
    }

    public Guid Id { get; }
    public Guid BookingId { get; }
    public Money Amount { get; }
    public PaymentStatus Status { get; private set; }

    /// <summary>The provider's id for the charge. Needed to refund it.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Why it failed, in the provider's terms translated to ours (such as "insufficient_funds").</summary>
    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Incremented by the repository on every save. Used for optimistic concurrency.</summary>
    public int Version { get; private set; }

    /// <summary>A new deposit, about to be sent to the provider.</summary>
    public static Payment Start(Guid bookingId, Money amount, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), bookingId, amount, PaymentStatus.Pending, null, null, now, null, null, 0);

    /// <summary>For repositories rebuilding a payment they saved.</summary>
    public static Payment Restore(Guid id, Guid bookingId, Money amount, PaymentStatus status, string? providerReference, string? failureCode,
        DateTimeOffset createdAt, DateTimeOffset? paidAt, DateTimeOffset? refundedAt, int version) =>
        new(id, bookingId, amount, status, providerReference, failureCode, createdAt, paidAt, refundedAt, version);

    /// <summary>The provider took the money. False when that was already known, or the payment is settled otherwise.</summary>
    public bool MarkPaid(string providerReference, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) return false;
        Status = PaymentStatus.Paid;
        ProviderReference = Clip(providerReference, MaxReferenceLength);
        FailureCode = null;
        PaidAt = now;
        return true;
    }

    /// <summary>The provider refused or failed. Only a payment still waiting for its outcome can fail.</summary>
    public bool MarkFailed(string code)
    {
        if (Status != PaymentStatus.Pending) return false;
        Status = PaymentStatus.Failed;
        FailureCode = Clip(code, MaxCodeLength);
        return true;
    }

    /// <summary>The money went back. Only a paid deposit can be refunded.</summary>
    public bool MarkRefunded(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Paid) return false;
        Status = PaymentStatus.Refunded;
        RefundedAt = now;
        return true;
    }

    /// <summary>Called by a repository after a successful save.</summary>
    public void MarkSaved() => Version++;

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
