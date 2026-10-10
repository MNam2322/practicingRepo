using BookingApp.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApp.Infrastructure.Tests;

/// <summary>
/// The audit log is append-only in the database itself: even raw SQL, bypassing the app, cannot change or delete an
/// entry (library 10.6 "Prove the audit log has not been altered").
/// </summary>
public sealed class AuditLogTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-audit-{Guid.NewGuid():N}.db");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Insert =
        "INSERT INTO audit_log (id, occurred_at, actor_id, actor_email, action, entity, entity_id) " +
        "VALUES ('6D1F1C3E-0000-4000-8000-00000000A001', 1, '6D1F1C3E-0000-4000-8000-00000000A002', 'admin@example.com', 'policy.updated', 'policy', 'booking')";

    [Fact]
    public async Task Sqlite_refuses_update_and_delete()
    {
        await using var db = TestDatabase.Context(_file);
        await db.Database.MigrateAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(Insert, Ct);

        var update = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit_log SET actor_email = 'someone@else.com'", Ct));
        Assert.Contains("append-only", update.Message);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_log", Ct));
        Assert.Equal(1, await db.AuditLog.CountAsync(Ct));
    }

    [Fact]
    public async Task Postgres_refuses_update_delete_and_truncate()
    {
        var connectionString = await BookingApp.Tests.Shared.PostgresTestServer.NewDatabaseAsync();
        await using var db = PostgresDatabase.Context(connectionString);
        await db.Database.MigrateAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(Insert.Replace("VALUES ('6D1F1C3E-0000-4000-8000-00000000A001', 1,", "VALUES ('6D1F1C3E-0000-4000-8000-00000000A001', now(),", StringComparison.Ordinal), Ct);

        foreach (var sql in new[] { "UPDATE audit_log SET actor_email = 'someone@else.com'", "DELETE FROM audit_log", "TRUNCATE audit_log" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Ct));
            Assert.Contains("append-only", error.MessageText);
        }
        Assert.Equal(1, await db.AuditLog.CountAsync(Ct));
        BookingApp.Tests.Shared.PostgresTestServer.Release(connectionString);
    }

    public void Dispose() => TestDatabase.Delete(_file);
}
