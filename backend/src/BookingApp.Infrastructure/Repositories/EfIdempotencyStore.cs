using BookingApp.Core.Common;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApp.Infrastructure.Repositories;

/// <summary>
/// Idempotency keys in the database. The claim is an INSERT on the primary key (scope, key), so the database decides
/// which of two simultaneous retries wins (library 8.2: "a unique constraint on the key, not a read-then-write").
/// Taking over an expired or abandoned claim is a conditional UPDATE on the claim time read, so only one retry can.
/// </summary>
internal sealed class EfIdempotencyStore(BookingDbContext db) : IIdempotencyStore
{
    public async Task<Claim> TryClaimAsync(string scope, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.IdempotencyKeys.Add(new IdempotencyRow { Scope = scope, Key = key, RequestHash = requestHash, ClaimedAt = now });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new Claim(ClaimOutcome.Claimed);
        }
        catch (DbUpdateException e) when (IsDuplicateKey(e))
        {
            // Someone used this key before: decide from what they left.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        var existing = await db.IdempotencyKeys.AsNoTracking().SingleAsync(r => r.Scope == scope && r.Key == key, cancellationToken);
        var expired = existing.ClaimedAt + Idempotency.KeepFor <= now;
        if (!expired)
        {
            if (existing.RequestHash != requestHash) return new Claim(ClaimOutcome.PayloadDiffers);
            if (existing.ResponseStatus is { } status)
                return new Claim(ClaimOutcome.Completed, new StoredResponse(status, existing.ResponseBody ?? "", existing.ResponseContentType ?? "application/json", existing.ResponseLocation));
            if (existing.ClaimedAt + Idempotency.AbandonedAfter > now) return new Claim(ClaimOutcome.InProgress);
        }

        // Expired, or abandoned by a request that died: take it over, unless another retry just did.
        var claimedAt = existing.ClaimedAt;
        var taken = await db.IdempotencyKeys
            .Where(r => r.Scope == scope && r.Key == key && r.ClaimedAt == claimedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RequestHash, requestHash)
                .SetProperty(r => r.ClaimedAt, now)
                .SetProperty(r => r.ResponseStatus, (int?)null)
                .SetProperty(r => r.ResponseBody, (string?)null)
                .SetProperty(r => r.ResponseContentType, (string?)null)
                .SetProperty(r => r.ResponseLocation, (string?)null), cancellationToken);
        return new Claim(taken == 1 ? ClaimOutcome.Claimed : ClaimOutcome.InProgress);
    }

    public Task CompleteAsync(string scope, string key, StoredResponse response, CancellationToken cancellationToken) =>
        db.IdempotencyKeys.Where(r => r.Scope == scope && r.Key == key).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.ResponseStatus, response.StatusCode)
            .SetProperty(r => r.ResponseBody, response.Body)
            .SetProperty(r => r.ResponseContentType, response.ContentType)
            .SetProperty(r => r.ResponseLocation, response.Location), cancellationToken);

    public Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken) =>
        db.IdempotencyKeys.Where(r => r.Scope == scope && r.Key == key).ExecuteDeleteAsync(cancellationToken);

    private static bool IsDuplicateKey(DbUpdateException e) => e.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,           // SQLITE_CONSTRAINT (primary key)
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };
}
