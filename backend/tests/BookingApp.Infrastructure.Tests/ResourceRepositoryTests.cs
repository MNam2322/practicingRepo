using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace BookingApp.Infrastructure.Tests;

/// <summary>A migrated SQLite file with the venue and two courts, for repository tests.</summary>
internal static class TestDatabase
{
    public static SqliteBookingDbContext Context(string file) =>
        new(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite($"Data Source={file}").Options);

    public static async Task SeedAsync(BookingDbContext db)
    {
        var hours = Enum.GetValues<DayOfWeek>().Select(d => new OpeningHours(d, new TimeOnly(6, 0), new TimeOnly(22, 0))).ToList();
        db.Venue.Add(new VenueRow { Name = "Test club", TimeZoneId = "Asia/Ho_Chi_Minh" });
        db.Resources.Add(ResourceRow.From(new Resource("court-2", "Court 2", ResourceKind.Court, 4, 60, 2, hours), 1));
        db.Resources.Add(ResourceRow.From(new Resource("court-1", "Court 1", ResourceKind.Court, 4, 60, 2, hours), 0));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static void Delete(string file)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                foreach (var path in new[] { file, file + "-wal", file + "-shm" })
                    if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }
}

public sealed class EfResourceRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Day = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-resources-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(Day);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EfResourceRepository Repository(BookingDbContext db) => new(db, _time);

    public async ValueTask InitializeAsync()
    {
        await using var db = TestDatabase.Context(_file);
        await db.Database.MigrateAsync(Ct);
        await TestDatabase.SeedAsync(db);
    }

    [Fact]
    public async Task The_catalog_has_the_venue_and_resources_in_display_order_with_hours()
    {
        await using var db = TestDatabase.Context(_file);
        var catalog = await Repository(db).GetCatalogAsync(Ct);

        Assert.Equal("Test club", catalog.Venue.Name);
        Assert.Equal(["court-1", "court-2"], catalog.Resources.Select(r => r.Id));
        var hours = catalog.Find("COURT-1")!.HoursOn(DayOfWeek.Monday)!;
        Assert.Equal(new TimeOnly(6, 0), hours.Open);
        Assert.Equal(new TimeOnly(22, 0), hours.Close);
        Assert.Equal(7, catalog.Resources[0].Hours.Count);
    }

    [Fact]
    public async Task Closures_include_the_resource_and_the_whole_venue_but_not_other_resources()
    {
        await using var db = TestDatabase.Context(_file);
        var repo = Repository(db);
        var court1 = new Closure(Guid.NewGuid(), "court-1", TimeRange.FromStart(Day.AddHours(2), TimeSpan.FromHours(2)), "Resurfacing");
        var venue = new Closure(Guid.NewGuid(), null, TimeRange.FromStart(Day.AddHours(1), TimeSpan.FromHours(1)), "Fire drill");
        var court2 = new Closure(Guid.NewGuid(), "court-2", TimeRange.FromStart(Day, TimeSpan.FromHours(8)), "Event");
        var later = new Closure(Guid.NewGuid(), "court-1", TimeRange.FromStart(Day.AddDays(3), TimeSpan.FromHours(1)), "Later");
        foreach (var c in new[] { court1, venue, court2, later }) await repo.AddClosureAsync(c, Ct);

        var found = await repo.ClosuresAsync("court-1", TimeRange.FromStart(Day, TimeSpan.FromHours(24)), Ct);
        Assert.Equal([venue.Id, court1.Id], found.Select(c => c.Id));
        Assert.Equal(court1.Time, found[1].Time);
        Assert.Equal("Resurfacing", found[1].Reason);
    }

    [Fact]
    public async Task The_database_rejects_rows_that_break_its_constraints()
    {
        await using var db = TestDatabase.Context(_file);
        // A booking for a resource that does not exist (foreign key).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO bookings (id, resource_id, start_utc, end_utc, customer_id, customer_name, status, created_at, version) VALUES ('b1', 'nope', 1, 2, 'c', 'n', 'Confirmed', 0, 1)"));
        // A resource with no capacity (CHECK).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO resources (id, name, kind, capacity, slot_minutes, max_slots_per_booking, sort_order) VALUES ('x', 'X', 'Court', 0, 60, 1, 9)"));
        // An unknown kind (CHECK).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO resources (id, name, kind, capacity, slot_minutes, max_slots_per_booking, sort_order) VALUES ('y', 'Y', 'Pool', 2, 60, 1, 9)"));
        // Opening hours that close before they open (CHECK).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO opening_hours (resource_id, day_of_week, open_time, close_time) VALUES ('court-1', 9, '08:00:00', '07:00:00')"));
        // A second venue row (CHECK id = 1).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO venue (id, name, time_zone_id) VALUES (2, 'Other', 'UTC')"));
        // A closure that ends before it starts (CHECK).
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(db, "INSERT INTO closures (id, resource_id, start_utc, end_utc, reason, created_at) VALUES ('c1', 'court-1', 5, 4, 'r', 0)"));
    }

    [Fact]
    public async Task A_resource_with_bookings_cannot_be_deleted()
    {
        await using (var db = TestDatabase.Context(_file))
        {
            var booking = Core.Bookings.Booking.Create("court-1", TimeRange.FromStart(Day.AddHours(3), TimeSpan.FromHours(1)), Core.Bookings.CustomerId.New(), "Nam", Day);
            Assert.True(await new EfBookingRepository(db).TryAddAsync(booking, Ct));
        }
        await using var again = TestDatabase.Context(_file);
        await Assert.ThrowsAsync<DbUpdateException>(() => Insert(again, "DELETE FROM resources WHERE id = 'court-1'"));
    }

    /// <summary>Raw SQL so the test exercises the database's rules, not EF Core's or the domain's.</summary>
    private static async Task Insert(BookingDbContext db, string sql)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, Ct);
        }
        catch (SqliteException e)
        {
            throw new DbUpdateException(e.Message, e);
        }
    }

    public ValueTask DisposeAsync()
    {
        TestDatabase.Delete(_file);
        return ValueTask.CompletedTask;
    }
}
