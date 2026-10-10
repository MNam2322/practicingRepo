using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>The venue, resources, opening hours and closures from the database. Reads are no-tracking.</summary>
internal sealed class EfResourceRepository(BookingDbContext db, TimeProvider time) : IResourceRepository
{
    public async Task<ResourceCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var venue = await db.Venue.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The venue has not been set up. The database is seeded from the Venue settings on first start.");
        var resources = await db.Resources.AsNoTracking().Include(r => r.Hours)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        return new ResourceCatalog(new Venue(venue.Name, venue.TimeZoneId), [.. resources.Select(r => r.ToDomain())]);
    }

    public async Task<IReadOnlyList<Closure>> ClosuresAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
    {
        var start = window.Start;
        var end = window.End;
        var rows = await db.Closures.AsNoTracking()
            .Where(c => (c.ResourceId == null || c.ResourceId == resourceId) && c.StartUtc < end && start < c.EndUtc)
            .OrderBy(c => c.StartUtc)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<Closure>> AllClosuresAsync(TimeRange window, CancellationToken cancellationToken)
    {
        var start = window.Start;
        var end = window.End;
        var rows = await db.Closures.AsNoTracking().Where(c => c.StartUtc < end && start < c.EndUtc).OrderBy(c => c.StartUtc).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task AddClosureAsync(Closure closure, CancellationToken cancellationToken)
    {
        db.Closures.Add(new ClosureRow
        {
            Id = closure.Id,
            ResourceId = closure.ResourceId,
            StartUtc = closure.Time.Start,
            EndUtc = closure.Time.End,
            Reason = closure.Reason,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }
}
