using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Deposits and webhook events in memory, under one lock: the same guarantees as the database (one live payment per
/// booking, one record per event, a version check on update). It keeps copies, so a stale copy is detected as in a
/// database. Lost on restart, like the in-memory bookings.
/// </summary>
internal sealed class InMemoryPaymentRepository(IBookingRepository bookings) : IPaymentRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Payment> _payments = [];
    private readonly HashSet<string> _events = [];

    public Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_payments.Values.Any(p => p.BookingId == payment.BookingId && p.Status is PaymentStatus.Pending or PaymentStatus.Paid))
                return Task.FromResult(false);
            payment.MarkSaved();
            _payments[payment.Id] = Copy(payment);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(Update(payment));
    }

    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_payments.TryGetValue(id, out var p) ? Copy(p) : null);
    }

    public Task<IReadOnlyList<Payment>> ForBookingsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Payment> found = [.. _payments.Values.Where(p => bookingIds.Contains(p.BookingId)).OrderBy(p => p.CreatedAt).Select(Copy)];
            return Task.FromResult(found);
        }
    }

    public async Task<IReadOnlyList<Payment>> AwaitingRefundAsync(DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        List<Payment> paid;
        lock (_gate) paid = [.. _payments.Values.Where(p => p.Status == PaymentStatus.Paid).Select(Copy)];
        var due = new List<Payment>();
        foreach (var payment in paid)
        {
            var booking = await bookings.FindAsync(payment.BookingId, cancellationToken);
            if (booking is not null && DepositService.RefundDue(booking, now)) due.Add(payment);
            if (due.Count == max) break;
        }
        return due;
    }

    public Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_events.Contains(eventId));
    }

    public Task<EventSave> RecordEventAsync(ReceivedPaymentEvent received, Payment? changed, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_events.Contains(received.EventId)) return Task.FromResult(EventSave.Duplicate);
            if (changed is not null && !Update(changed)) return Task.FromResult(EventSave.Conflict);
            _events.Add(received.EventId);
            return Task.FromResult(EventSave.Saved);
        }
    }

    /// <summary>Call under the lock.</summary>
    private bool Update(Payment payment)
    {
        if (!_payments.TryGetValue(payment.Id, out var stored) || stored.Version != payment.Version) return false;
        payment.MarkSaved();
        _payments[payment.Id] = Copy(payment);
        return true;
    }

    private static Payment Copy(Payment p) => Payment.Restore(p.Id, p.BookingId, p.Amount, p.Status, p.ProviderReference, p.FailureCode,
        p.CreatedAt, p.PaidAt, p.RefundedAt, p.Version);
}
