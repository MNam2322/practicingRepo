using System.Data;
using System.Data.Common;
using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Caching;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Bookings in a relational database through EF Core: named query methods on one focused class, no generic repository
/// (library 7.7 "Where data access code belongs"). Reads are no-tracking; writes are guarded as described on each method.
/// </summary>
/// <param name="occupancy">Told about every committed change, so cached availability is never stale (null in tests that do not cache).</param>
/// <param name="metrics">Counts expired holds, wherever they expire (the job, or in passing before an insert).</param>
internal sealed class EfBookingRepository(BookingDbContext db, OccupancyVersions? occupancy = null, IBookingMetrics? metrics = null) : IBookingRepository
{
    /// <summary>
    /// The overlap check and the insert must be one atomic step; a read-then-insert would let two requests both see
    /// the slot free. How depends on the database:
    /// <list type="bullet">
    /// <item>PostgreSQL: no check here at all. The insert either succeeds or breaks the exclusion constraint
    /// (SQLSTATE 23P01), and the database decides (library 7.2: "any rule of the form no two rows may... belongs in a
    /// constraint").</item>
    /// <item>SQLite, which has no such constraint: the check and the insert run in one transaction that starts with
    /// <c>BEGIN IMMEDIATE</c>, taking the write lock before the check, so a second writer waits and then sees the first
    /// row (library 7.4: a multi-row invariant needs serialisable work or a lock).</item>
    /// </list>
    /// </summary>
    public Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken) =>
        db.Database.IsNpgsql() ? InsertGuardedByConstraintAsync(booking, cancellationToken) : CheckAndInsertUnderLockAsync(booking, cancellationToken);

    /// <summary>
    /// PostgreSQL: the exclusion constraint decides. Inserts for the same resource first take a lock on that resource's
    /// row, so they queue rather than deadlock: two overlapping inserts that race on the constraint otherwise wait on
    /// each other and PostgreSQL kills one (40P01). Library 7.4: "a multi-row invariant: serialisable, or a lock on a
    /// designated parent row". The lock only orders the inserts; without the constraint they would all succeed.
    /// The whole attempt runs inside the retrying execution strategy, as a transaction must (library 7.7).
    /// </summary>
    private async Task<bool> InsertGuardedByConstraintAsync(Booking booking, CancellationToken cancellationToken)
    {
        var saved = await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM resources WHERE id = {booking.ResourceId} FOR NO KEY UPDATE", ct);
            // Holds past their time must not keep the slot: release them before the constraint looks.
            await ExpireDueHoldsAsync(booking.ResourceId, booking.CreatedAt, ct);
            var row = BookingRow.From(booking);
            row.Version = booking.Version + 1;
            db.Bookings.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
            {
                return false;
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }, cancellationToken);
        if (saved)
        {
            booking.MarkSaved();
            occupancy?.Changed(booking.ResourceId, booking.Time);
        }
        return saved;
    }

    private async Task<bool> CheckAndInsertUnderLockAsync(Booking booking, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Disposing an uncommitted transaction rolls it back, so an early return releases the lock.
            await using var transaction = await BeginWriteTransactionAsync(cancellationToken);
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            await ExpireDueHoldsAsync(booking.ResourceId, booking.CreatedAt, cancellationToken);
            var clash = await BookingQueries.Blocking(db, booking.ResourceId, booking.Time).AnyAsync(cancellationToken);
            if (clash) return false;

            var row = BookingRow.From(booking);
            row.Version = booking.Version + 1;   // the version MarkSaved gives the domain object below
            db.Bookings.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            booking.MarkSaved();
            occupancy?.Changed(booking.ResourceId, booking.Time);
            return true;
        }
        finally
        {
            await db.Database.UseTransactionAsync(null, CancellationToken.None);
            db.ChangeTracker.Clear();
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Saves the booking's changed state if nobody else saved it since it was read: the version it was read with is
    /// checked in the UPDATE's WHERE clause (EF Core concurrency token). Zero rows changed means someone won; return false.
    /// The notifications its events call for go into the outbox in the same SaveChanges, so they commit with the change
    /// or not at all (library 12.3 "Outbox + inbox patterns").
    /// </summary>
    public async Task<bool> UpdateAsync(Booking booking, CancellationToken cancellationToken)
    {
        var row = BookingRow.From(booking);
        var entry = db.Bookings.Attach(row);
        entry.Property(r => r.Version).OriginalValue = booking.Version;
        row.Version = booking.Version + 1;
        entry.Property(r => r.Status).IsModified = true;
        entry.Property(r => r.CancelledAt).IsModified = true;
        entry.Property(r => r.CustomerName).IsModified = true;
        entry.Property(r => r.CustomerId).IsModified = true;
        entry.Property(r => r.CancellationReason).IsModified = true;
        db.Outbox.AddRange(NotificationPlan.For(booking).Select(OutboxRow.From));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            booking.MarkSaved();
            occupancy?.Changed(booking.ResourceId, booking.Time);
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

    public async Task<Booking?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Booking>> BlockingAsync(string resourceId, TimeRange window, CancellationToken cancellationToken)
    {
        var rows = await BookingQueries.Blocking(db, resourceId, window).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public Task<int> ExpireDueHoldsAsync(DateTimeOffset now, CancellationToken cancellationToken) => ExpireDueHoldsAsync(null, now, cancellationToken);

    /// <summary>
    /// One conditional UPDATE: only holds that are still Pending and past their time change, so it is safe to run twice
    /// and a confirm racing it either wins (the row is no longer Pending) or loses (its version check fails).
    /// </summary>
    private async Task<int> ExpireDueHoldsAsync(string? resourceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending && b.HoldExpiresAt <= now && (resourceId == null || b.ResourceId == resourceId))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired).SetProperty(b => b.Version, b => b.Version + 1), cancellationToken);
        if (expired > 0) metrics?.HoldsExpired(expired);
        return expired;
    }

    public async Task<IReadOnlyList<Booking>> InWindowAsync(TimeRange window, CancellationToken cancellationToken)
    {
        var rows = await BookingQueries.InWindow(db, window).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        var rows = await BookingQueries.ForCustomer(db, customer.Value).ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    /// <summary>A transaction that holds the write lock from its first statement.</summary>
    private async Task<DbTransaction> BeginWriteTransactionAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        // deferred: false makes Microsoft.Data.Sqlite issue BEGIN IMMEDIATE rather than a deferred BEGIN. While another
        // writer holds the lock, it waits (retrying SQLITE_BUSY) up to the command timeout.
        if (connection is SqliteConnection sqlite) return sqlite.BeginTransaction(IsolationLevel.Serializable, deferred: false);
        return await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }
}
