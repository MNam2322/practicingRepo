using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

public sealed class BookingTests
{
    private static readonly CustomerId Owner = CustomerId.New();
    private readonly BookingPolicy _policy = new() { CancellationCutoffHours = 2 };

    private static Booking NewBooking() => Booking.Create("court-1", Hour(Tomorrow, 10), Owner, "  Nam  ", Now);

    [Fact]
    public void A_new_booking_is_confirmed_and_blocks_its_slot()
    {
        var booking = NewBooking();
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.True(booking.BlocksAt(Now));
        Assert.Equal("Nam", booking.CustomerName);
    }

    [Fact]
    public void The_owner_can_cancel_before_the_cutoff()
    {
        var booking = NewBooking();
        var result = booking.Cancel(Owner, Now, _policy);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(Now, booking.CancelledAt);
        Assert.False(booking.BlocksAt(Now));
    }

    [Fact]
    public void Cancelling_exactly_at_the_cutoff_is_allowed()
    {
        var booking = NewBooking();
        Assert.True(booking.Cancel(Owner, booking.Time.Start - _policy.CancellationCutoff, _policy).IsSuccess);
    }

    [Fact]
    public void Someone_else_cannot_cancel()
    {
        var booking = NewBooking();
        AssertError(booking.Cancel(CustomerId.New(), Now, _policy), ErrorKind.Forbidden, "not_your_booking");
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public void A_cancelled_booking_cannot_be_cancelled_again()
    {
        var booking = NewBooking();
        booking.Cancel(Owner, Now, _policy);
        AssertError(booking.Cancel(Owner, Now, _policy), ErrorKind.Conflict, "not_cancellable");
    }

    [Fact]
    public void A_completed_booking_cannot_be_cancelled()
    {
        var booking = Booking.Restore(Guid.NewGuid(), "court-1", Hour(Tomorrow, 10), Owner, "Nam", BookingStatus.Completed, Now, null, 1);
        AssertError(booking.Cancel(Owner, Now, _policy), ErrorKind.Conflict, "not_cancellable");
    }

    [Fact]
    public void Cancelling_after_the_cutoff_is_too_late()
    {
        var booking = NewBooking();
        var justAfterCutoff = booking.Time.Start - _policy.CancellationCutoff + TimeSpan.FromMinutes(1);

        AssertError(booking.Cancel(Owner, justAfterCutoff, _policy), ErrorKind.RuleViolation, "too_late_to_cancel");
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    private static void AssertError<T>(Result<T> result, ErrorKind kind, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.Equal(code, result.Error.Code);
    }
}
