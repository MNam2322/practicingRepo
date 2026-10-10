using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using BookingApp.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApp.Infrastructure.Tests;

internal static class PostgresDatabase
{
    public static PostgresBookingDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<PostgresBookingDbContext>().UseNpgsql(connectionString).Options);

    /// <summary>A new migrated database with the test venue and courts.</summary>
    public static async Task<string> CreateAsync()
    {
        var connectionString = await PostgresTestServer.NewDatabaseAsync();
        await using var db = Context(connectionString);
        await db.Database.MigrateAsync();
        await TestDatabase.SeedAsync(db);
        return connectionString;
    }
}

/// <summary>The same repository contract as the other providers, on a real PostgreSQL 17 (Testcontainers).</summary>
public sealed class PostgresBookingRepositoryTests : BookingRepositoryContract
{
    private readonly List<BookingDbContext> _contexts = [];
    private string _connectionString = "";

    public override async ValueTask InitializeAsync() => _connectionString = await PostgresDatabase.CreateAsync();

    protected override IBookingRepository NewRepository()
    {
        var context = PostgresDatabase.Context(_connectionString);
        lock (_contexts) _contexts.Add(context);
        return new EfBookingRepository(context);
    }

    [Fact]
    public async Task Saving_a_stale_copy_is_refused()
    {
        var booking = NewBooking();
        await NewRepository().TryAddAsync(booking, Ct);
        var mine = (await NewRepository().FindAsync(booking.Id, Ct))!;
        var theirs = (await NewRepository().FindAsync(booking.Id, Ct))!;
        mine.Cancel(mine.Customer, Now, new BookingPolicy());
        Assert.True(await NewRepository().UpdateAsync(mine, Ct));
        theirs.Cancel(theirs.Customer, Now, new BookingPolicy());
        Assert.False(await NewRepository().UpdateAsync(theirs, Ct));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        PostgresTestServer.Release(_connectionString);
    }
}

/// <summary>
/// Proof that PostgreSQL itself refuses double bookings: these tests write raw SQL, bypassing the repository and every
/// line of application code, and the exclusion constraint still says no (SQLSTATE 23P01).
/// </summary>
public sealed class PostgresExclusionConstraintTests : IAsyncLifetime
{
    private string _connectionString = "";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _connectionString = await PostgresDatabase.CreateAsync();

    private async Task InsertAsync(string resource, string start, string end, string status = "Confirmed")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "INSERT INTO bookings (id, resource_id, start_utc, end_utc, customer_id, customer_name, status, created_at, version) " +
            "VALUES (@id, @resource, @start::timestamptz, @end::timestamptz, @customer, 'Raw SQL', @status, now(), 1)", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("resource", resource);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.AddWithValue("customer", Guid.NewGuid());
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task An_overlapping_confirmed_booking_is_refused_by_the_database()
    {
        await InsertAsync("court-1", "2026-06-02 10:00Z", "2026-06-02 12:00Z");

        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync("court-1", "2026-06-02 11:00Z", "2026-06-02 12:00Z"));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, error.SqlState);
        Assert.Equal(PostgresBookingDbContext.NoOverlapConstraint, error.ConstraintName);
    }

    [Fact]
    public async Task Touching_bookings_other_resources_and_cancelled_rows_are_allowed()
    {
        await InsertAsync("court-1", "2026-06-02 10:00Z", "2026-06-02 11:00Z");
        await InsertAsync("court-1", "2026-06-02 11:00Z", "2026-06-02 12:00Z");   // [10,11) and [11,12) only touch
        await InsertAsync("court-2", "2026-06-02 10:00Z", "2026-06-02 11:00Z");   // another resource
        await InsertAsync("court-1", "2026-06-02 10:00Z", "2026-06-02 11:00Z", "Cancelled");   // not blocking
    }

    [Fact]
    public async Task Fifty_parallel_inserts_for_one_slot_leave_exactly_one_row()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            try
            {
                await InsertAsync("court-1", "2026-06-02 10:00Z", "2026-06-02 11:00Z");
                return true;
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ExclusionViolation)
            {
                return false;
            }
        }, Ct)));
        Assert.Equal(1, results.Count(r => r));
    }

    public ValueTask DisposeAsync()
    {
        PostgresTestServer.Release(_connectionString);
        return ValueTask.CompletedTask;
    }
}
