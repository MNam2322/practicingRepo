using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using BookingApp.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingApp.Infrastructure.Tests;

/// <summary>What every payment repository must guarantee, run against each provider.</summary>
public abstract class PaymentRepositoryContract : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ten = new(2026, 6, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly Money Deposit = new(100_000, "VND");
    private int _hour;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A booking repository and a payment repository over the same storage, as one request would have.</summary>
    protected abstract (IBookingRepository Bookings, IPaymentRepository Payments) New();

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<Booking> AddBooking(bool hold = false)
    {
        var time = TimeRange.FromStart(Ten.AddHours(_hour++), TimeSpan.FromHours(1));
        var booking = hold
            ? Booking.CreateHold("court-1", time, CustomerId.New(), "Nam", Now, TimeSpan.FromMinutes(10))
            : Booking.Create("court-1", time, CustomerId.New(), "Nam", Now);
        Assert.True(await New().Bookings.TryAddAsync(booking, Ct));
        return booking;
    }

    private async Task<Payment> AddPaid(Guid bookingId)
    {
        var payment = Payment.Start(bookingId, Deposit, Now);
        Assert.True(await New().Payments.TryAddAsync(payment, Ct));
        payment.MarkPaid("ch_1", Now);
        Assert.True(await New().Payments.UpdateAsync(payment, Ct));
        return payment;
    }

    [Fact]
    public async Task A_payment_round_trips_with_every_field()
    {
        var booking = await AddBooking();
        var payment = await AddPaid(booking.Id);
        var loaded = (await New().Payments.FindAsync(payment.Id, Ct))!;

        Assert.Equal((payment.Id, booking.Id, Deposit, PaymentStatus.Paid, "ch_1", Now, 2),
            (loaded.Id, loaded.BookingId, loaded.Amount, loaded.Status, loaded.ProviderReference, loaded.PaidAt!.Value, loaded.Version));
        Assert.Equal(payment.Id, Assert.Single(await New().Payments.ForBookingsAsync([booking.Id], Ct)).Id);
    }

    [Fact]
    public async Task A_booking_has_at_most_one_live_payment()
    {
        var booking = await AddBooking();
        var first = Payment.Start(booking.Id, Deposit, Now);
        Assert.True(await New().Payments.TryAddAsync(first, Ct));
        Assert.False(await New().Payments.TryAddAsync(Payment.Start(booking.Id, Deposit, Now), Ct));   // pending

        first.MarkPaid("ch_1", Now);
        await New().Payments.UpdateAsync(first, Ct);
        Assert.False(await New().Payments.TryAddAsync(Payment.Start(booking.Id, Deposit, Now), Ct));   // paid

        // A failed attempt does not count: the customer can try another card.
        var other = await AddBooking();
        var declined = Payment.Start(other.Id, Deposit, Now);
        await New().Payments.TryAddAsync(declined, Ct);
        declined.MarkFailed("card_declined");
        await New().Payments.UpdateAsync(declined, Ct);
        Assert.True(await New().Payments.TryAddAsync(Payment.Start(other.Id, Deposit, Now.AddMinutes(1)), Ct));
    }

    [Fact]
    public async Task Saving_a_stale_copy_is_refused()
    {
        var booking = await AddBooking();
        var payment = await AddPaid(booking.Id);
        var mine = (await New().Payments.FindAsync(payment.Id, Ct))!;
        var theirs = (await New().Payments.FindAsync(payment.Id, Ct))!;
        mine.MarkRefunded(Now);
        Assert.True(await New().Payments.UpdateAsync(mine, Ct));
        theirs.MarkRefunded(Now);
        Assert.False(await New().Payments.UpdateAsync(theirs, Ct));
    }

    [Fact]
    public async Task An_event_is_recorded_once_with_its_change()
    {
        var booking = await AddBooking();
        var payment = Payment.Start(booking.Id, Deposit, Now);
        await New().Payments.TryAddAsync(payment, Ct);
        var received = new ReceivedPaymentEvent("evt_1", PaymentEventType.PaymentSucceeded, payment.Id, Now, "{}");

        Assert.False(await New().Payments.EventSeenAsync("evt_1", Ct));
        payment.MarkPaid("ch_1", Now);
        Assert.Equal(EventSave.Saved, await New().Payments.RecordEventAsync(received, payment, Ct));
        Assert.True(await New().Payments.EventSeenAsync("evt_1", Ct));
        Assert.Equal(PaymentStatus.Paid, (await New().Payments.FindAsync(payment.Id, Ct))!.Status);

        var again = (await New().Payments.FindAsync(payment.Id, Ct))!;
        again.MarkRefunded(Now);
        Assert.Equal(EventSave.Duplicate, await New().Payments.RecordEventAsync(received, again, Ct));
        Assert.Equal(PaymentStatus.Paid, (await New().Payments.FindAsync(payment.Id, Ct))!.Status);   // nothing saved
    }

    [Fact]
    public async Task An_event_with_a_stale_payment_saves_nothing()
    {
        var booking = await AddBooking();
        var payment = await AddPaid(booking.Id);
        var stale = (await New().Payments.FindAsync(payment.Id, Ct))!;
        var fresh = (await New().Payments.FindAsync(payment.Id, Ct))!;
        fresh.MarkRefunded(Now);
        await New().Payments.UpdateAsync(fresh, Ct);

        stale.MarkRefunded(Now);
        var received = new ReceivedPaymentEvent("evt_2", PaymentEventType.RefundSucceeded, payment.Id, Now, "{}");
        Assert.Equal(EventSave.Conflict, await New().Payments.RecordEventAsync(received, stale, Ct));
        Assert.False(await New().Payments.EventSeenAsync("evt_2", Ct));   // the provider will deliver it again
    }

    [Fact]
    public async Task Awaiting_refund_finds_paid_deposits_of_bookings_that_no_longer_happen()
    {
        var cancelled = await AddBooking();
        var confirmed = await AddBooking();
        var lapsedHold = await AddBooking(hold: true);
        var refunded = await AddBooking();
        var cancelledPayment = await AddPaid(cancelled.Id);
        await AddPaid(confirmed.Id);
        var holdPayment = await AddPaid(lapsedHold.Id);
        var refundedPayment = await AddPaid(refunded.Id);
        refundedPayment.MarkRefunded(Now);
        await New().Payments.UpdateAsync(refundedPayment, Ct);
        foreach (var b in new[] { cancelled, refunded })
        {
            var loaded = (await New().Bookings.FindAsync(b.Id, Ct))!;
            loaded.Cancel(loaded.Customer, Now, new BookingPolicy());
            Assert.True(await New().Bookings.UpdateAsync(loaded, Ct));
        }

        Assert.Equal([cancelledPayment.Id], (await New().Payments.AwaitingRefundAsync(Now.AddMinutes(5), 50, Ct)).Select(p => p.Id));
        // Once the paid hold is past its time (not yet swept), it is due too.
        Assert.Equal(new[] { cancelledPayment.Id, holdPayment.Id }.Order(),
            (await New().Payments.AwaitingRefundAsync(Now.AddMinutes(10), 50, Ct)).Select(p => p.Id).Order());
    }
}

public sealed class InMemoryPaymentRepositoryTests : PaymentRepositoryContract
{
    private readonly InMemoryBookingRepository _bookings = new();
    private readonly InMemoryPaymentRepository _payments;

    public InMemoryPaymentRepositoryTests() => _payments = new InMemoryPaymentRepository(_bookings);

    protected override (IBookingRepository, IPaymentRepository) New() => (_bookings, _payments);
}

public sealed class SqlitePaymentRepositoryTests : PaymentRepositoryContract
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-pay-{Guid.NewGuid():N}.db");
    private readonly List<BookingDbContext> _contexts = [];

    protected override (IBookingRepository, IPaymentRepository) New()
    {
        var context = new SqliteBookingDbContext(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite($"Data Source={_file}").Options);
        lock (_contexts) _contexts.Add(context);
        return (new EfBookingRepository(context), new EfPaymentRepository(context));
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

public sealed class PostgresPaymentRepositoryTests : PaymentRepositoryContract
{
    private readonly List<BookingDbContext> _contexts = [];
    private string _connectionString = "";

    public override async ValueTask InitializeAsync() => _connectionString = await PostgresDatabase.CreateAsync();

    protected override (IBookingRepository, IPaymentRepository) New()
    {
        var context = PostgresDatabase.Context(_connectionString);
        lock (_contexts) _contexts.Add(context);
        return (new EfBookingRepository(context), new EfPaymentRepository(context));
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        PostgresTestServer.Release(_connectionString);
    }
}
