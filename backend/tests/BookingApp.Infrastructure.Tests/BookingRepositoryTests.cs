using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Tests;

/// <summary>What every IBookingRepository must do, run against each implementation.</summary>
public abstract class BookingRepositoryContract : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    protected static readonly DateTimeOffset Ten = new(2026, 6, 2, 3, 0, 0, TimeSpan.Zero);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A repository over the shared store; EF repositories each get their own DbContext, like separate requests.</summary>
    protected abstract IBookingRepository NewRepository();

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected static Booking NewBooking(int hourOffset = 0, int hours = 1, string resource = "court-1", CustomerId? customer = null) =>
        Booking.Create(resource, TimeRange.FromStart(Ten.AddHours(hourOffset), TimeSpan.FromHours(hours)), customer ?? CustomerId.New(), "Nam", Now);

    [Fact]
    public async Task An_added_booking_can_be_found_with_every_field()
    {
        var booking = NewBooking();
        Assert.True(await NewRepository().TryAddAsync(booking, Ct));

        var found = await NewRepository().FindAsync(booking.Id, Ct);
        Assert.NotNull(found);
        Assert.Equal(booking.ResourceId, found.ResourceId);
        Assert.Equal(booking.Time, found.Time);
        Assert.Equal(TimeSpan.Zero, found.Time.Start.Offset);
        Assert.Equal(booking.Customer, found.Customer);
        Assert.Equal(booking.CustomerName, found.CustomerName);
        Assert.Equal(BookingStatus.Confirmed, found.Status);
        Assert.Equal(booking.CreatedAt, found.CreatedAt);
        Assert.Null(found.CancelledAt);
        Assert.Equal(1, found.Version);
    }

    [Fact]
    public async Task An_overlapping_booking_is_refused_and_a_touching_one_is_not()
    {
        Assert.True(await NewRepository().TryAddAsync(NewBooking(0, 2), Ct));
        Assert.False(await NewRepository().TryAddAsync(NewBooking(1), Ct));
        Assert.False(await NewRepository().TryAddAsync(NewBooking(-1, 2), Ct));
        Assert.True(await NewRepository().TryAddAsync(NewBooking(2), Ct));
        Assert.True(await NewRepository().TryAddAsync(NewBooking(-1), Ct));
        Assert.True(await NewRepository().TryAddAsync(NewBooking(0, 1, "court-2"), Ct));
    }

    [Fact]
    public async Task A_cancelled_booking_no_longer_blocks()
    {
        var booking = NewBooking();
        await NewRepository().TryAddAsync(booking, Ct);
        var loaded = (await NewRepository().FindAsync(booking.Id, Ct))!;
        Assert.True(loaded.Cancel(loaded.Customer, Now, new BookingPolicy()).IsSuccess);
        Assert.True(await NewRepository().UpdateAsync(loaded, Ct));

        var reloaded = (await NewRepository().FindAsync(booking.Id, Ct))!;
        Assert.Equal(BookingStatus.Cancelled, reloaded.Status);
        Assert.Equal(Now, reloaded.CancelledAt);
        Assert.True(await NewRepository().TryAddAsync(NewBooking(), Ct));
    }

    [Fact]
    public async Task Blocking_returns_confirmed_overlaps_in_start_order()
    {
        var late = NewBooking(3);
        var early = NewBooking(0);
        var cancelled = NewBooking(1);
        foreach (var b in new[] { late, early, cancelled }) await NewRepository().TryAddAsync(b, Ct);
        var c = (await NewRepository().FindAsync(cancelled.Id, Ct))!;
        c.Cancel(c.Customer, Now, new BookingPolicy());
        await NewRepository().UpdateAsync(c, Ct);
        await NewRepository().TryAddAsync(NewBooking(0, 1, "court-2"), Ct);

        var found = await NewRepository().BlockingAsync("court-1", TimeRange.FromStart(Ten, TimeSpan.FromHours(10)), Ct);
        Assert.Equal([early.Id, late.Id], found.Select(b => b.Id));
        var outside = await NewRepository().BlockingAsync("court-1", TimeRange.FromStart(Ten.AddHours(-5), TimeSpan.FromHours(5)), Ct);
        Assert.Empty(outside);
    }

    [Fact]
    public async Task For_customer_returns_only_theirs_in_start_order()
    {
        var me = CustomerId.New();
        var second = NewBooking(2, customer: me);
        var first = NewBooking(0, customer: me);
        await NewRepository().TryAddAsync(second, Ct);
        await NewRepository().TryAddAsync(first, Ct);
        await NewRepository().TryAddAsync(NewBooking(4), Ct);

        Assert.Equal([first.Id, second.Id], (await NewRepository().ForCustomerAsync(me, Ct)).Select(b => b.Id));
    }

    [Fact]
    public async Task Parallel_adds_for_one_slot_let_exactly_one_through()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => NewRepository().TryAddAsync(NewBooking(), Ct), Ct)));
        Assert.Equal(1, results.Count(r => r));
    }
}

public sealed class InMemoryBookingRepositoryTests : BookingRepositoryContract
{
    private readonly InMemoryBookingRepository _repository = new();
    protected override IBookingRepository NewRepository() => _repository;
}

/// <summary>EF Core on a SQLite file: each repository has its own DbContext and connection, as separate requests would.</summary>
public sealed class SqliteBookingRepositoryTests : BookingRepositoryContract
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-repo-{Guid.NewGuid():N}.db");
    private readonly List<BookingDbContext> _contexts = [];

    private BookingDbContext NewContext()
    {
        var context = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        lock (_contexts) _contexts.Add(context);
        return context;
    }

    protected override IBookingRepository NewRepository() => new EfBookingRepository(NewContext());

    public override async ValueTask InitializeAsync()
    {
        await using var db = new BookingDbContext(new DbContextOptionsBuilder<BookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        await db.Database.MigrateAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", Ct);
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

        // "theirs" was read at version 1; the row is now at version 2, so the conditional UPDATE matches nothing.
        theirs.Cancel(theirs.Customer, Now, new BookingPolicy());
        Assert.False(await NewRepository().UpdateAsync(theirs, Ct));
        Assert.Equal(2, (await NewRepository().FindAsync(booking.Id, Ct))!.Version);
    }

    [Fact]
    public async Task Times_are_stored_as_utc_ticks()
    {
        var booking = NewBooking();
        await NewRepository().TryAddAsync(booking, Ct);
        await using var connection = new SqliteConnection($"Data Source={_file}");
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(start_utc), start_utc FROM bookings";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        Assert.True(await reader.ReadAsync(Ct));
        Assert.Equal("integer", reader.GetString(0));
        Assert.Equal(booking.Time.Start.UtcTicks, reader.GetInt64(1));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        // Best effort: a connection may still be closing; a leftover temp file must not fail the test.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                foreach (var path in new[] { _file, _file + "-wal", _file + "-shm" })
                    if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }
}
