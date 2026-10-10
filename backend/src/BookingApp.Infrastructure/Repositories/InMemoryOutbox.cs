using BookingApp.Core.Notifications;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// The outbox for the in-memory provider. <see cref="InMemoryBookingRepository"/> adds messages under its own lock in
/// the same step as the booking change, which is what a transaction gives the database providers. Lost on restart,
/// like everything in memory.
/// </summary>
internal sealed class InMemoryOutbox : INotificationOutbox
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, OutboxRow> _rows = [];

    /// <summary>Called by the booking repository while it holds its lock and saves the change.</summary>
    public void Add(IEnumerable<OutboxMessage> messages)
    {
        lock (_gate)
            foreach (var m in messages) _rows[m.Id] = OutboxRow.From(m);
    }

    public Task<IReadOnlyList<OutboxMessage>> DueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<OutboxMessage> due = [.. _rows.Values
                .Where(o => o.Outcome == null && o.DueAt <= now && (o.RetryAt == null || o.RetryAt <= now))
                .OrderBy(o => o.DueAt).Take(max).Select(o => o.ToMessage())];
            return Task.FromResult(due);
        }
    }

    public Task MarkDoneAsync(Guid id, DeliveryOutcome outcome, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(id, out var row) && row.Outcome is null)
            {
                row.Outcome = outcome;
                row.DoneAt = at;
            }
        }
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(Guid id, int attempts, DateTimeOffset? retryAt, string error, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(id, out var row) && row.Outcome is null)
            {
                row.Attempts = attempts;
                row.RetryAt = retryAt;
                row.LastError = error.Length <= OutboxRow.MaxErrorLength ? error : error[..OutboxRow.MaxErrorLength];
                if (retryAt is null)
                {
                    row.Outcome = DeliveryOutcome.Dead;
                    row.DoneAt = at;
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxEntry>> ForBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<OutboxEntry> found = [.. _rows.Values.Where(o => o.BookingId == bookingId).OrderBy(o => o.DueAt).ThenBy(o => o.Id).Select(o => o.ToEntry())];
            return Task.FromResult(found);
        }
    }
}
