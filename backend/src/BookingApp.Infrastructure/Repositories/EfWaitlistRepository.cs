using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Waitlist;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>Waitlist entries in the database. Telling someone is the entry change and its outbox email in one SaveChanges.</summary>
internal sealed class EfWaitlistRepository(BookingDbContext db) : IWaitlistRepository
{
    public async Task<bool> TryAddAsync(WaitlistEntry entry, CancellationToken cancellationToken)
    {
        var row = WaitlistRow.From(entry);
        row.Version = entry.Version + 1;
        db.Waitlist.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            entry.MarkSaved();
            return true;
        }
        catch (DbUpdateException e) when (DbErrors.IsUniqueViolation(e))
        {
            return false;   // already waiting for this time
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public Task<bool> UpdateAsync(WaitlistEntry entry, CancellationToken cancellationToken) => SaveAsync(entry, null, cancellationToken);

    public Task<bool> NotifyAsync(WaitlistEntry entry, OutboxMessage email, CancellationToken cancellationToken) => SaveAsync(entry, email, cancellationToken);

    public async Task<WaitlistEntry?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Waitlist.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<WaitlistEntry>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        var rows = await db.Waitlist.AsNoTracking().Where(w => w.CustomerId == customer.Value).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<WaitlistEntry>> OpenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await db.Waitlist.AsNoTracking()
            .Where(w => (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Notified) && w.StartUtc > now)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public Task<int> ExpirePastAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Waitlist
            .Where(w => (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.Notified) && w.StartUtc <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Expired).SetProperty(w => w.Version, w => w.Version + 1), cancellationToken);

    /// <summary>The entry's new state, checked against the version it was read at, plus the email if any: one transaction.</summary>
    private async Task<bool> SaveAsync(WaitlistEntry entry, OutboxMessage? email, CancellationToken cancellationToken)
    {
        var row = WaitlistRow.From(entry);
        var tracked = db.Waitlist.Attach(row);
        tracked.Property(r => r.Version).OriginalValue = entry.Version;
        row.Version = entry.Version + 1;
        tracked.Property(r => r.Status).IsModified = true;
        tracked.Property(r => r.NotifiedAt).IsModified = true;
        if (email is not null) db.Outbox.Add(OutboxRow.From(email));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            entry.MarkSaved();
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
}
