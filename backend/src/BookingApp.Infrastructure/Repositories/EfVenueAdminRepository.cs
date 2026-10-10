using BookingApp.Core.Admin;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Venue set-up changes in the database. Each method adds the audit row to the same unit of work, so one SaveChanges
/// (one transaction) writes both the change and its record.
/// </summary>
internal sealed class EfVenueAdminRepository(BookingDbContext db) : IVenueAdminRepository
{
    public async Task<bool> AddResourceAsync(Resource resource, int sortOrder, AuditEntry audit, CancellationToken cancellationToken)
    {
        if (await db.Resources.AnyAsync(r => r.Id == resource.Id, cancellationToken)) return false;
        db.Resources.Add(ResourceRow.From(resource, sortOrder));
        return await SaveAsync(audit, cancellationToken);
    }

    public async Task<bool> UpdateResourceAsync(Resource resource, AuditEntry audit, CancellationToken cancellationToken)
    {
        var row = await db.Resources.SingleOrDefaultAsync(r => r.Id == resource.Id, cancellationToken);
        if (row is null) return false;
        row.Name = resource.Name;
        row.Kind = resource.Kind;
        row.Capacity = resource.Capacity;
        row.SlotMinutes = resource.SlotMinutes;
        row.MaxSlotsPerBooking = resource.MaxSlotsPerBooking;
        return await SaveAsync(audit, cancellationToken);
    }

    public async Task<bool> SetHoursAsync(string resourceId, IReadOnlyList<OpeningHours> hours, AuditEntry audit, CancellationToken cancellationToken)
    {
        var row = await db.Resources.Include(r => r.Hours).SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        if (row is null) return false;
        row.Hours.Clear();
        foreach (var h in hours) row.Hours.Add(new OpeningHoursRow { ResourceId = resourceId, Day = h.Day, Open = h.Open, Close = h.Close });
        return await SaveAsync(audit, cancellationToken);
    }

    public Task<bool> HasBookingsAsync(string resourceId, CancellationToken cancellationToken) =>
        db.Bookings.AnyAsync(b => b.ResourceId == resourceId, cancellationToken);

    public async Task<bool> DeleteResourceAsync(string resourceId, AuditEntry audit, CancellationToken cancellationToken)
    {
        var row = await db.Resources.SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        if (row is null) return false;
        db.Resources.Remove(row);   // opening hours and closures cascade; bookings would RESTRICT (checked first)
        return await SaveAsync(audit, cancellationToken);
    }

    public async Task AddClosureAsync(Closure closure, AuditEntry audit, CancellationToken cancellationToken)
    {
        db.Closures.Add(new ClosureRow
        {
            Id = closure.Id,
            ResourceId = closure.ResourceId,
            StartUtc = closure.Time.Start,
            EndUtc = closure.Time.End,
            Reason = closure.Reason,
            CreatedAt = audit.OccurredAt,
        });
        await SaveAsync(audit, cancellationToken);
    }

    public async Task<bool> DeleteClosureAsync(Guid closureId, AuditEntry audit, CancellationToken cancellationToken)
    {
        var row = await db.Closures.SingleOrDefaultAsync(c => c.Id == closureId, cancellationToken);
        if (row is null) return false;
        db.Closures.Remove(row);
        // Record what was removed.
        return await SaveAsync(audit with
        {
            Before = System.Text.Json.JsonSerializer.Serialize(new { row.ResourceId, Start = row.StartUtc, End = row.EndUtc, row.Reason }),
        }, cancellationToken);
    }

    public async Task SavePolicyAsync(BookingPolicy policy, AuditEntry audit, CancellationToken cancellationToken)
    {
        var venue = await db.Venue.SingleAsync(cancellationToken);
        venue.SetPolicy(policy);
        await SaveAsync(audit, cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(int take, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        var query = db.AuditLog.AsNoTracking();
        if (before is { } b) query = query.Where(a => a.OccurredAt < b);
        var rows = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(take).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    private async Task<bool> SaveAsync(AuditEntry audit, CancellationToken cancellationToken)
    {
        db.AuditLog.Add(AuditRow.From(audit));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
