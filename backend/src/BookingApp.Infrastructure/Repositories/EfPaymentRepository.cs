using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Deposits and received webhook events in the database. The rules that keep money safe are constraints, not checks:
/// one live (pending or paid) payment per booking (partial unique index), one row per webhook event (primary key on the event id),
/// and a version check on every update.
/// </summary>
internal sealed class EfPaymentRepository(BookingDbContext db) : IPaymentRepository
{
    public async Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        var row = PaymentRow.From(payment);
        row.Version = payment.Version + 1;   // the version MarkSaved gives the domain object below
        db.Payments.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            payment.MarkSaved();
            return true;
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
        {
            return false;   // this booking already has a live payment
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<bool> UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        Track(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            payment.MarkSaved();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Payment>> ForBookingsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken)
    {
        var rows = await db.Payments.AsNoTracking()
            .Where(p => bookingIds.Contains(p.BookingId))
            .ToListAsync(cancellationToken);
        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset, and the list per booking is short.
        return [.. rows.OrderBy(r => r.CreatedAt).Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<Payment>> AwaitingRefundAsync(DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        var rows = await (
                from p in db.Payments.AsNoTracking()
                join b in db.Bookings.AsNoTracking() on p.BookingId equals b.Id
                where p.Status == PaymentStatus.Paid
                    && (b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Expired
                        || (b.Status == BookingStatus.Pending && b.HoldExpiresAt <= now))
                select p)
            .Take(max)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public Task<bool> EventSeenAsync(string eventId, CancellationToken cancellationToken) =>
        db.PaymentEvents.AnyAsync(e => e.EventId == eventId, cancellationToken);

    /// <summary>One SaveChanges, so one transaction: the event row and the payment change are saved together or not at all.</summary>
    public async Task<EventSave> RecordEventAsync(ReceivedPaymentEvent received, Payment? changed, CancellationToken cancellationToken)
    {
        db.PaymentEvents.Add(new PaymentEventRow
        {
            EventId = received.EventId,
            Type = received.Type,
            PaymentId = received.PaymentId,
            ReceivedAt = received.ReceivedAt,
            Payload = received.Payload,
        });
        if (changed is not null) Track(changed);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            changed?.MarkSaved();
            return EventSave.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EventSave.Conflict;
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
        {
            return EventSave.Duplicate;   // the same event, delivered twice at the same moment: the other one saved it
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Attaches the payment's new state, with the version it was read at checked in the UPDATE's WHERE clause.</summary>
    private void Track(Payment payment)
    {
        var row = PaymentRow.From(payment);
        var entry = db.Payments.Attach(row);
        entry.Property(r => r.Version).OriginalValue = payment.Version;
        row.Version = payment.Version + 1;
        entry.Property(r => r.Status).IsModified = true;
        entry.Property(r => r.ProviderReference).IsModified = true;
        entry.Property(r => r.FailureCode).IsModified = true;
        entry.Property(r => r.PaidAt).IsModified = true;
        entry.Property(r => r.RefundedAt).IsModified = true;
    }
}
