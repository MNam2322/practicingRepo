using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;
using BookingApp.Core.Waitlist;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using BookingApp.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Tests;

/// <summary>What every waitlist repository must guarantee, on each provider.</summary>
public abstract class WaitlistRepositoryContract : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeRange Ten = TimeRange.FromStart(new DateTimeOffset(2026, 6, 2, 3, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1));

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract (IWaitlistRepository Waitlist, INotificationOutbox Outbox) New();

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static WaitlistEntry Entry(CustomerId? customer = null, DateTimeOffset? at = null) =>
        WaitlistEntry.Join("court-1", Ten, customer ?? CustomerId.New(), "Wai", "wai@example.com", at ?? Now);

    [Fact]
    public async Task An_entry_round_trips_and_a_customer_waits_for_a_time_once()
    {
        var me = CustomerId.New();
        var entry = Entry(me);
        Assert.True(await New().Waitlist.TryAddAsync(entry, Ct));
        Assert.False(await New().Waitlist.TryAddAsync(Entry(me), Ct));

        var loaded = (await New().Waitlist.FindAsync(entry.Id, Ct))!;
        Assert.Equal((entry.ResourceId, entry.Time, me, "wai@example.com", WaitlistStatus.Waiting, 1),
            (loaded.ResourceId, loaded.Time, loaded.Customer, loaded.Email, loaded.Status, loaded.Version));

        // Once they have left, they can join again.
        loaded.Leave();
        Assert.True(await New().Waitlist.UpdateAsync(loaded, Ct));
        Assert.True(await New().Waitlist.TryAddAsync(Entry(me), Ct));
    }

    [Fact]
    public async Task Telling_someone_saves_the_entry_and_the_email_together_once()
    {
        var entry = Entry();
        await New().Waitlist.TryAddAsync(entry, Ct);
        var mine = (await New().Waitlist.FindAsync(entry.Id, Ct))!;
        var theirs = (await New().Waitlist.FindAsync(entry.Id, Ct))!;

        Assert.True(await New().Waitlist.NotifyAsync(mine, mine.Notify(Now, "Free again", "Body"), Ct));
        Assert.False(await New().Waitlist.NotifyAsync(theirs, theirs.Notify(Now, "Free again", "Body"), Ct));   // a second sweep

        var due = await New().Outbox.DueAsync(Now, 10, Ct);
        var email = Assert.Single(due);
        Assert.Equal((NotificationKind.WaitlistSlotFree, (Guid?)null, "wai@example.com", "Free again"), (email.Kind, email.BookingId, email.To, email.Subject));
        Assert.Equal(WaitlistStatus.Notified, (await New().Waitlist.FindAsync(entry.Id, Ct))!.Status);
    }

    [Fact]
    public async Task Open_lists_entries_in_joining_order_and_past_ones_expire()
    {
        var late = Entry(at: Now.AddMinutes(5));
        var early = Entry(at: Now);
        await New().Waitlist.TryAddAsync(late, Ct);
        await New().Waitlist.TryAddAsync(early, Ct);
        Assert.Equal([early.Id, late.Id], (await New().Waitlist.OpenAsync(Now, Ct)).Select(e => e.Id));

        Assert.Equal(2, await New().Waitlist.ExpirePastAsync(Ten.Start, Ct));
        Assert.Empty(await New().Waitlist.OpenAsync(Ten.Start.AddMinutes(-1), Ct));
        Assert.Equal(WaitlistStatus.Expired, (await New().Waitlist.FindAsync(early.Id, Ct))!.Status);
    }
}

public sealed class InMemoryWaitlistRepositoryTests : WaitlistRepositoryContract
{
    private readonly InMemoryOutbox _outbox = new();
    private readonly InMemoryWaitlistRepository _waitlist;

    public InMemoryWaitlistRepositoryTests() => _waitlist = new InMemoryWaitlistRepository(_outbox);

    protected override (IWaitlistRepository, INotificationOutbox) New() => (_waitlist, _outbox);
}

public sealed class SqliteWaitlistRepositoryTests : WaitlistRepositoryContract
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-wait-{Guid.NewGuid():N}.db");
    private readonly List<BookingDbContext> _contexts = [];

    protected override (IWaitlistRepository, INotificationOutbox) New()
    {
        var context = new SqliteBookingDbContext(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        lock (_contexts) _contexts.Add(context);
        return (new EfWaitlistRepository(context), new EfNotificationOutbox(context));
    }

    public override async ValueTask InitializeAsync()
    {
        await using var db = new SqliteBookingDbContext(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        await db.Database.MigrateAsync(Ct);
        await TestDatabase.SeedAsync(db);
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp file must not fail the test.
        }
    }
}

public sealed class PostgresWaitlistRepositoryTests : WaitlistRepositoryContract
{
    private readonly List<BookingDbContext> _contexts = [];
    private string _connectionString = "";

    public override async ValueTask InitializeAsync() => _connectionString = await PostgresDatabase.CreateAsync();

    protected override (IWaitlistRepository, INotificationOutbox) New()
    {
        var context = PostgresDatabase.Context(_connectionString);
        lock (_contexts) _contexts.Add(context);
        return (new EfWaitlistRepository(context), new EfNotificationOutbox(context));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        PostgresTestServer.Release(_connectionString);
    }
}
