namespace BookingApp.Core.Payments;

/// <summary>Deposits, and the webhook events already received. Implemented in Infrastructure.</summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Adds a payment. False when the booking already has a live (pending or paid) payment: two confirms raced, and the
    /// database lets only one of them pay (a partial unique index, library 7.2).
    /// </summary>
    Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken);

    /// <summary>Saves a change if nobody saved the payment since it was read (version check). False when someone did.</summary>
    Task<bool> UpdateAsync(Payment payment, CancellationToken cancellationToken);

    Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every payment for these bookings, oldest first.</summary>
    Task<IReadOnlyList<Payment>> ForBookingsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken);

    /// <summary>
    /// Paid deposits whose booking no longer happens: cancelled, expired, or a hold past its time. The refund job's query.
    /// </summary>
    Task<IReadOnlyList<Payment>> AwaitingRefundAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a webhook event and, in the same transaction, the payment change it caused (if any). The event id is the
    /// primary key, so a second delivery of the same event saves nothing.
    /// </summary>
    Task<EventSave> RecordEventAsync(ReceivedPaymentEvent received, Payment? changed, CancellationToken cancellationToken);
}

/// <summary>A webhook event as it is kept: what came in, and when.</summary>
public sealed record ReceivedPaymentEvent(string EventId, PaymentEventType Type, Guid PaymentId, DateTimeOffset ReceivedAt, string Payload);

public enum EventSave
{
    Saved,
    /// <summary>Received before. Nothing was saved.</summary>
    Duplicate,
    /// <summary>The payment changed at the same moment. Nothing was saved; the provider will deliver it again.</summary>
    Conflict,
}
