using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using BookingApp.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Tests;

/// <summary>The outbox commits with the booking change or not at all, and the relay's queries, on every provider.</summary>
public abstract class OutboxContract : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ten = new(2026, 6, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly CustomerId Alice = CustomerId.New();
    private int _hour;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected abstract (IBookingRepository Bookings, INotificationOutbox Outbox) New();

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<Booking> Hold()
    {
        var hold = Booking.CreateHold("court-1", TimeRange.FromStart(Ten.AddHours(_hour++), TimeSpan.FromHours(1)), Alice, "Alice", Now,
            TimeSpan.FromMinutes(10), "alice@example.com");
        Assert.True(await New().Bookings.TryAddAsync(hold, Ct));
        return hold;
    }

    [Fact]
    public async Task Confirming_writes_the_confirmation_and_the_reminder_with_the_change()
    {
        var hold = await Hold();
        var loaded = (await New().Bookings.FindAsync(hold.Id, Ct))!;
        loaded.Confirm(Alice, Now);
        Assert.True(await New().Bookings.UpdateAsync(loaded, Ct));

        var entries = await New().Outbox.ForBookingAsync(hold.Id, Ct);
        Assert.Equal([NotificationKind.BookingConfirmed, NotificationKind.Reminder], entries.Select(e => e.Message.Kind));
        Assert.All(entries, e => Assert.Null(e.Outcome));
        Assert.Equal("alice@example.com", (await New().Bookings.FindAsync(hold.Id, Ct))!.ContactEmail);
    }

    [Fact]
    public async Task A_change_that_is_not_saved_writes_no_email()
    {
        var hold = await Hold();
        var mine = (await New().Bookings.FindAsync(hold.Id, Ct))!;
        var theirs = (await New().Bookings.FindAsync(hold.Id, Ct))!;
        theirs.Cancel(Alice, Now, new BookingPolicy());
        Assert.True(await New().Bookings.UpdateAsync(theirs, Ct));

        mine.Confirm(Alice, Now);
        var saved = await New().Bookings.UpdateAsync(mine, Ct);
        if (!saved)   // a database refuses the stale copy, and its emails go with it
            Assert.Equal([NotificationKind.BookingCancelled], (await New().Outbox.ForBookingAsync(hold.Id, Ct)).Select(e => e.Message.Kind));
    }

    [Fact]
    public async Task Due_returns_what_is_due_oldest_first_and_done_messages_drop_out()
    {
        var first = await Hold();
        var second = await Hold();
        foreach (var b in new[] { first, second })
        {
            var loaded = (await New().Bookings.FindAsync(b.Id, Ct))!;
            loaded.Confirm(Alice, Now.AddMinutes(b == first ? 1 : 2));
            await New().Bookings.UpdateAsync(loaded, Ct);
        }

        var due = await New().Outbox.DueAsync(Now.AddMinutes(5), 10, Ct);
        Assert.Equal([first.Id, second.Id], due.Select(m => m.BookingId));   // the reminders are not due yet
        await New().Outbox.MarkDoneAsync(due[0].Id, DeliveryOutcome.Sent, Now.AddMinutes(5), Ct);
        await New().Outbox.MarkFailedAsync(due[1].Id, 1, Now.AddMinutes(10), "down", Now.AddMinutes(5), Ct);

        Assert.Empty(await New().Outbox.DueAsync(Now.AddMinutes(6), 10, Ct));
        var retry = Assert.Single(await New().Outbox.DueAsync(Now.AddMinutes(10), 10, Ct));
        Assert.Equal((second.Id, 1), (retry.BookingId, retry.Attempts));

        // Recording an outcome twice keeps the first.
        await New().Outbox.MarkDoneAsync(due[0].Id, DeliveryOutcome.Skipped, Now.AddMinutes(7), Ct);
        Assert.Equal(DeliveryOutcome.Sent, (await New().Outbox.ForBookingAsync(first.Id, Ct)).Single(e => e.Message.Id == due[0].Id).Outcome);

        var reminders = await New().Outbox.DueAsync(first.Time.Start.AddHours(-24), 10, Ct);
        Assert.Contains(reminders, m => m.Kind == NotificationKind.Reminder && m.BookingId == first.Id);
    }

    [Fact]
    public async Task A_dead_letter_is_done()
    {
        var hold = await Hold();
        var loaded = (await New().Bookings.FindAsync(hold.Id, Ct))!;
        loaded.Cancel(Alice, Now, new BookingPolicy());
        await New().Bookings.UpdateAsync(loaded, Ct);
        var message = Assert.Single(await New().Outbox.DueAsync(Now, 10, Ct));
        await New().Outbox.MarkFailedAsync(message.Id, 8, null, "gave up", Now, Ct);

        var entry = Assert.Single(await New().Outbox.ForBookingAsync(hold.Id, Ct));
        Assert.Equal((DeliveryOutcome.Dead, "gave up", 8), (entry.Outcome, entry.LastError, entry.Message.Attempts));
        Assert.Empty(await New().Outbox.DueAsync(Now.AddDays(1), 10, Ct));
    }
}

public sealed class InMemoryOutboxTests : OutboxContract
{
    private readonly InMemoryOutbox _outbox = new();
    private readonly InMemoryBookingRepository _bookings;

    public InMemoryOutboxTests() => _bookings = new InMemoryBookingRepository(_outbox);

    protected override (IBookingRepository, INotificationOutbox) New() => (_bookings, _outbox);
}

public sealed class SqliteOutboxTests : OutboxContract
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-outbox-{Guid.NewGuid():N}.db");
    private readonly List<BookingDbContext> _contexts = [];

    protected override (IBookingRepository, INotificationOutbox) New()
    {
        var context = new SqliteBookingDbContext(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        lock (_contexts) _contexts.Add(context);
        return (new EfBookingRepository(context), new EfNotificationOutbox(context));
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

public sealed class PostgresOutboxTests : OutboxContract
{
    private readonly List<BookingDbContext> _contexts = [];
    private string _connectionString = "";

    public override async ValueTask InitializeAsync() => _connectionString = await PostgresDatabase.CreateAsync();

    protected override (IBookingRepository, INotificationOutbox) New()
    {
        var context = PostgresDatabase.Context(_connectionString);
        lock (_contexts) _contexts.Add(context);
        return (new EfBookingRepository(context), new EfNotificationOutbox(context));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        PostgresTestServer.Release(_connectionString);
    }
}
