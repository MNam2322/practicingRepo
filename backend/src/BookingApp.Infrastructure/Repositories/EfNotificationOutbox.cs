using BookingApp.Core.Notifications;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// The relay's side of the outbox table. Messages are added by <see cref="EfBookingRepository"/> in the same SaveChanges
/// as the booking change. Each update here is one conditional UPDATE on a message that is not done yet, so two relays
/// (or a relay and a restart) cannot both record an outcome.
/// </summary>
internal sealed class EfNotificationOutbox(BookingDbContext db) : INotificationOutbox
{
    public async Task<IReadOnlyList<OutboxMessage>> DueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        var rows = await db.Outbox.AsNoTracking()
            .Where(o => o.Outcome == null && o.DueAt <= now && (o.RetryAt == null || o.RetryAt <= now))
            .OrderBy(o => o.DueAt)
            .Take(max)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToMessage())];
    }

    public Task MarkDoneAsync(Guid id, DeliveryOutcome outcome, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.Outbox.Where(o => o.Id == id && o.Outcome == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Outcome, outcome).SetProperty(o => o.DoneAt, at), cancellationToken);

    public Task MarkFailedAsync(Guid id, int attempts, DateTimeOffset? retryAt, string error, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var message = error.Length <= OutboxRow.MaxErrorLength ? error : error[..OutboxRow.MaxErrorLength];
        DeliveryOutcome? outcome = retryAt is null ? DeliveryOutcome.Dead : null;
        DateTimeOffset? doneAt = retryAt is null ? at : null;
        return db.Outbox.Where(o => o.Id == id && o.Outcome == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Attempts, attempts)
                .SetProperty(o => o.RetryAt, retryAt)
                .SetProperty(o => o.LastError, message)
                .SetProperty(o => o.Outcome, outcome)
                .SetProperty(o => o.DoneAt, doneAt), cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxEntry>> ForBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var rows = await db.Outbox.AsNoTracking().Where(o => o.BookingId == bookingId).ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.DueAt).ThenBy(r => r.Id).Select(r => r.ToEntry())];
    }
}
