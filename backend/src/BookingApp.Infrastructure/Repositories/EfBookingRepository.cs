using System.Data;
using System.Data.Common;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Bookings in a relational database through EF Core: named query methods on one focused class, no generic repository
/// (library 7.7 "Where data access code belongs"). Reads are no-tracking; writes are guarded as described on each method.
/// </summary>
internal sealed class EfBookingRepository(BookingDbContext db) : IBookingRepository
{
    /// <summary>
    /// The overlap check and the insert must be one atomic step. On SQLite the transaction starts with
    /// <c>BEGIN IMMEDIATE</c>, which takes the database's write lock before the check runs, so a second writer waits
    /// until the first has committed and then sees its row (library 7.4: a multi-row invariant needs serialisable
    /// work or a lock). A plain read-then-insert would let two requests both see the slot free.
    /// </summary>
    public async Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Disposing an uncommitted transaction rolls it back, so an early return releases the lock.
            await using var transaction = await BeginWriteTransactionAsync(cancellationToken);
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var start = booking.Time.Start;
            var end = booking.Time.End;
            var clash = await db.Bookings.AnyAsync(b => b.ResourceId == booking.ResourceId && b.Status == BookingStatus.Confirmed
                && b.StartUtc < end && start < b.EndUtc, cancellationToken);
            if (clash) return false;

            var row = BookingRow.From(booking);
            row.Version = booking.Version + 1;   // the version MarkSaved gives the domain object below
            db.Bookings.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            booking.MarkSaved();
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
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            booking.MarkSaved();
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
        var start = window.Start;
        var end = window.End;
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.ResourceId == resourceId && b.Status == BookingStatus.Confirmed && b.StartUtc < end && start < b.EndUtc)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => r.ToDomain())];
    }

    public async Task<IReadOnlyList<Booking>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken)
    {
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.CustomerId == customer.Value)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(cancellationToken);
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
