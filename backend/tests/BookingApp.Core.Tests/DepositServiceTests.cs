using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Infrastructure.Payments;
using BookingApp.Infrastructure.Repositories;
using Microsoft.Extensions.Time.Testing;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

/// <summary>Deposits with a fake clock, the in-memory repositories and the fake provider.</summary>
public sealed class DepositServiceTests
{
    private static readonly CustomerId Alice = CustomerId.New();

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryBookingRepository _bookings = new();
    private readonly FakePaymentGateway _gateway = new();
    private readonly BookingService _bookingService;

    public DepositServiceTests()
    {
        var resources = new TestResources(Court("court-1")) { Policy = new BookingPolicy { MaxDaysAhead = 30, MinLeadMinutes = 15, CancellationCutoffHours = 2 } };
        _bookingService = new BookingService(resources, _bookings, new HoldOptions { Minutes = 10 }, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DepositService Deposits(decimal deposit = 100_000) =>
        new(_bookings, new InMemoryPaymentRepository(_bookings), _gateway, _bookingService, new PaymentOptions { Deposit = deposit }, _time);

    private async Task<Booking> Hold() => (await _bookingService.BookAsync(Alice, "Alice", "court-1", At(Tomorrow, 10), 1, Ct)).Value!;

    [Fact]
    public async Task With_deposits_off_confirming_needs_no_payment()
    {
        var hold = await Hold();
        var confirmed = await Deposits(deposit: 0).ConfirmAsync(Alice, hold.Id, paymentToken: null, Ct);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Value!.Status);
        Assert.Empty(_gateway.Charges);
    }

    [Fact]
    public async Task An_expired_or_foreign_hold_is_never_charged()
    {
        var deposits = Deposits();
        var hold = await Hold();
        Assert.Equal("not_your_booking", (await deposits.ConfirmAsync(CustomerId.New(), hold.Id, "tok_visa", Ct)).Error?.Code);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("hold_expired", (await deposits.ConfirmAsync(Alice, hold.Id, "tok_visa", Ct)).Error?.Code);
        Assert.Empty(_gateway.Charges);
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled, true)]
    [InlineData(BookingStatus.Expired, true)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Completed, false)]
    [InlineData(BookingStatus.NoShow, false)]
    public void Refunds_follow_the_cancellation_rules(BookingStatus status, bool refund)
    {
        var booking = Booking.Restore(Guid.NewGuid(), "court-1", Hour(Tomorrow, 10), Alice, "Alice", status, Now, null, 1);
        Assert.Equal(refund, DepositService.RefundDue(booking, Now));
    }

    [Fact]
    public void A_hold_past_its_time_is_due_a_refund_even_before_the_expiry_job()
    {
        var hold = Booking.CreateHold("court-1", Hour(Tomorrow, 10), Alice, "Alice", Now, TimeSpan.FromMinutes(10));
        Assert.False(DepositService.RefundDue(hold, Now.AddMinutes(9)));
        Assert.True(DepositService.RefundDue(hold, Now.AddMinutes(10)));
    }

    [Fact]
    public void The_current_payment_prefers_money_held_over_attempts()
    {
        var bookingId = Guid.NewGuid();
        var failed = Payment.Start(bookingId, new Money(1, "VND"), Now);
        failed.MarkFailed("card_declined");
        var paid = Payment.Start(bookingId, new Money(1, "VND"), Now.AddMinutes(1));
        paid.MarkPaid("ch", Now);
        Assert.Same(paid, DepositService.Current([failed, paid]));
        Assert.Same(failed, DepositService.Current([failed]));
        Assert.Null(DepositService.Current([]));
    }
}
