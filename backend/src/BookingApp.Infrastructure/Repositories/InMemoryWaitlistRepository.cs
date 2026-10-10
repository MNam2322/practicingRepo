using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Waitlist;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Waitlist entries in memory, under one lock, with the database's guarantees: one waiting entry per customer and time,
/// a version check, and an entry marked told in the same step as its email goes into the outbox.
/// </summary>
internal sealed class InMemoryWaitlistRepository(InMemoryOutbox outbox) : IWaitlistRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, WaitlistRow> _rows = [];

    public Task<bool> TryAddAsync(WaitlistEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_rows.Values.Any(r => r.Status == WaitlistStatus.Waiting && r.ResourceId == entry.ResourceId && r.StartUtc == entry.Time.Start
                && r.EndUtc == entry.Time.End && r.CustomerId == entry.Customer.Value))
                return Task.FromResult(false);
            entry.MarkSaved();
            _rows[entry.Id] = WaitlistRow.From(entry);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateAsync(WaitlistEntry entry, CancellationToken cancellationToken) => Task.FromResult(Save(entry, null));

    public Task<bool> NotifyAsync(WaitlistEntry entry, OutboxMessage email, CancellationToken cancellationToken) => Task.FromResult(Save(entry, email));

    public Task<WaitlistEntry?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_rows.TryGetValue(id, out var row) ? row.ToDomain() : null);
    }

    public Task<IReadOnlyList<WaitlistEntry>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WaitlistEntry> found = [.. _rows.Values.Where(r => r.CustomerId == customer.Value).Select(r => r.ToDomain())];
            return Task.FromResult(found);
        }
    }

    public Task<IReadOnlyList<WaitlistEntry>> OpenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WaitlistEntry> open = [.. _rows.Values
                .Where(r => r.Status is WaitlistStatus.Waiting or WaitlistStatus.Notified && r.StartUtc > now)
                .OrderBy(r => r.CreatedAt).Select(r => r.ToDomain())];
            return Task.FromResult(open);
        }
    }

    public Task<int> ExpirePastAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var expired = 0;
            foreach (var row in _rows.Values.Where(r => r.Status is WaitlistStatus.Waiting or WaitlistStatus.Notified && r.StartUtc <= now))
            {
                row.Status = WaitlistStatus.Expired;
                row.Version++;
                expired++;
            }
            return Task.FromResult(expired);
        }
    }

    private bool Save(WaitlistEntry entry, OutboxMessage? email)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(entry.Id, out var stored) || stored.Version != entry.Version) return false;
            if (email is not null) outbox.Add([email]);
            entry.MarkSaved();
            _rows[entry.Id] = WaitlistRow.From(entry);
            return true;
        }
    }
}
