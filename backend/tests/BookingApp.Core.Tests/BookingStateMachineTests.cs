using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

/// <summary>
/// The booking lifecycle. Every (status, trigger) pair is tried with its guards satisfied: exactly the transitions in
/// the table succeed, and every other one is refused with invalid_transition. Library: 12.6 "State machines for
/// aggregate lifecycles" ("exhaustive tests are generated, not written").
/// </summary>
public sealed class BookingStateMachineTests
{
    private static readonly CustomerId Owner = CustomerId.New();
    private static readonly BookingPolicy Policy = new() { CancellationCutoffHours = 2 };

    /// <summary>A booking from 10:00 to 11:00 tomorrow, in the given status.</summary>
    private static Booking InStatus(BookingStatus status) =>
        Booking.Restore(Guid.NewGuid(), "court-1", Hour(Tomorrow, 10), Owner, "Nam", status, Now, null, 1, holdExpiresAt: Now.AddMinutes(10));

    /// <summary>Fires a trigger at a moment when its guards hold: before the start for a cancel, during the booking otherwise.</summary>
    private static Result<Booking> Fire(Booking b, BookingTrigger trigger) => trigger switch
    {
        BookingTrigger.Cancel => b.CancelByStaff("Court flooded", b.Time.Start.AddHours(-1)),
        BookingTrigger.Complete => b.Complete(b.Time.Start.AddMinutes(30)),
        BookingTrigger.MarkNoShow => b.MarkNoShow(b.Time.Start.AddMinutes(30)),
        BookingTrigger.Confirm => b.Confirm(Owner, Now.AddMinutes(5)),          // before the hold expires
        _ => b.Expire(Now.AddMinutes(10)),                                       // once it is due
    };

    private static IEnumerable<(BookingStatus Status, BookingTrigger Trigger)> Pairs() =>
        from status in Enum.GetValues<BookingStatus>()
        from trigger in Enum.GetValues<BookingTrigger>()
        select (status, trigger);

    public static TheoryData<BookingStatus, BookingTrigger> EveryPair()
    {
        var data = new TheoryData<BookingStatus, BookingTrigger>();
        foreach (var (status, trigger) in Pairs()) data.Add(status, trigger);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_the_declared_transitions_are_permitted(BookingStatus status, BookingTrigger trigger)
    {
        var booking = InStatus(status);
        var result = Fire(booking, trigger);

        if (status == BookingStatus.Confirmed && trigger == BookingTrigger.Confirm)
        {
            // Not in the table on purpose: confirming a confirmed booking is a harmless no-op (idempotent confirm).
            Assert.True(result.IsSuccess);
            Assert.Equal(status, booking.Status);
        }
        else if (status == BookingStatus.Expired && trigger == BookingTrigger.Confirm)
        {
            // Refused like any undeclared pair, with a clearer code: the customer needs to know the hold ran out.
            Assert.Equal("hold_expired", result.Error?.Code);
            Assert.Equal(status, booking.Status);
        }
        else if (Booking.IsDeclared(status, trigger))
        {
            Assert.True(result.IsSuccess, $"{status} --{trigger}--> should be allowed");
            Assert.NotEqual(status, booking.Status);
        }
        else
        {
            Assert.Equal("invalid_transition", result.Error?.Code);
            Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
            Assert.Equal(status, booking.Status);
        }
    }

    [Fact]
    public void The_table_is_exactly_the_lifecycle()
    {
        var declared = Pairs().Where(p => Booking.IsDeclared(p.Status, p.Trigger));
        Assert.Equal(
            [
                (BookingStatus.Confirmed, BookingTrigger.Cancel), (BookingStatus.Confirmed, BookingTrigger.Complete), (BookingStatus.Confirmed, BookingTrigger.MarkNoShow),
                (BookingStatus.Pending, BookingTrigger.Cancel), (BookingStatus.Pending, BookingTrigger.Confirm), (BookingStatus.Pending, BookingTrigger.Expire),
            ],
            declared);
    }

    [Theory]
    [InlineData(BookingTrigger.Complete, BookingStatus.Completed)]
    [InlineData(BookingTrigger.MarkNoShow, BookingStatus.NoShow)]
    public void Outcomes_need_the_booking_to_have_started(BookingTrigger trigger, BookingStatus outcome)
    {
        var booking = InStatus(BookingStatus.Confirmed);
        var early = trigger == BookingTrigger.Complete ? booking.Complete(booking.Time.Start.AddMinutes(-1)) : booking.MarkNoShow(booking.Time.Start.AddMinutes(-1));
        Assert.Equal("not_started", early.Error?.Code);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        var onTime = trigger == BookingTrigger.Complete ? booking.Complete(booking.Time.Start) : booking.MarkNoShow(booking.Time.Start);
        Assert.True(onTime.IsSuccess);
        Assert.Equal(outcome, booking.Status);
        Assert.False(booking.BlocksAt(Now));
    }

    [Fact]
    public void Staff_cancel_needs_a_reason_and_records_it()
    {
        var booking = InStatus(BookingStatus.Confirmed);
        Assert.Equal("reason_required", booking.CancelByStaff("  ", Now).Error?.Code);

        Assert.True(booking.CancelByStaff("  Court flooded  ", Now).IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("Court flooded", booking.CancellationReason);
        Assert.Equal(Now, booking.CancelledAt);
    }

    [Fact]
    public void Staff_can_cancel_inside_the_customer_cut_off_but_not_after_the_end()
    {
        var late = InStatus(BookingStatus.Confirmed);
        Assert.True(late.CancelByStaff("Court flooded", late.Time.Start.AddMinutes(-5)).IsSuccess);

        var over = InStatus(BookingStatus.Confirmed);
        Assert.Equal("already_ended", over.CancelByStaff("Too late", over.Time.End).Error?.Code);
        Assert.Equal(BookingStatus.Confirmed, over.Status);
    }

    [Fact]
    public void A_customer_cancel_leaves_no_reason()
    {
        var booking = InStatus(BookingStatus.Confirmed);
        Assert.True(booking.Cancel(Owner, Now, Policy).IsSuccess);
        Assert.Null(booking.CancellationReason);
    }

    [Theory]
    [InlineData(-61, true, false)]   // an hour before: staff may cancel; outcomes not yet
    [InlineData(10, true, true)]     // during: both
    [InlineData(61, false, true)]    // after the end: outcomes only
    public void Staff_actions_reflect_the_table_and_the_guards(int minutesFromStart, bool cancel, bool outcomes)
    {
        var booking = InStatus(BookingStatus.Confirmed);
        var now = booking.Time.Start.AddMinutes(minutesFromStart);
        Assert.Equal(cancel, booking.CanStaff(BookingTrigger.Cancel, now));
        Assert.Equal(outcomes, booking.CanStaff(BookingTrigger.Complete, now));
        Assert.Equal(outcomes, booking.CanStaff(BookingTrigger.MarkNoShow, now));

        var done = InStatus(BookingStatus.Completed);
        Assert.False(done.CanStaff(BookingTrigger.Cancel, now) || done.CanStaff(BookingTrigger.Complete, now) || done.CanStaff(BookingTrigger.MarkNoShow, now));
    }

    [Fact]
    public void A_hold_blocks_until_it_expires_and_can_be_confirmed_until_then()
    {
        var hold = Booking.CreateHold("court-1", Hour(Tomorrow, 10), Owner, "Nam", Now, TimeSpan.FromMinutes(10));
        Assert.Equal(BookingStatus.Pending, hold.Status);
        Assert.True(hold.BlocksAt(Now.AddMinutes(9)));
        Assert.False(hold.BlocksAt(Now.AddMinutes(10)));

        Assert.Equal("not_your_booking", hold.Confirm(CustomerId.New(), Now).Error?.Code);
        Assert.Equal("hold_expired", hold.Confirm(Owner, Now.AddMinutes(10)).Error?.Code);
        Assert.Equal(BookingStatus.Pending, hold.Status);
        Assert.True(hold.Confirm(Owner, Now.AddMinutes(9)).IsSuccess);
        Assert.Equal(BookingStatus.Confirmed, hold.Status);
        Assert.True(hold.Confirm(Owner, Now.AddMinutes(30)).IsSuccess);   // again: harmless
    }

    [Fact]
    public void A_hold_cannot_expire_early_and_an_expired_one_cannot_be_confirmed()
    {
        var hold = Booking.CreateHold("court-1", Hour(Tomorrow, 10), Owner, "Nam", Now, TimeSpan.FromMinutes(10));
        Assert.Equal("hold_not_due", hold.Expire(Now.AddMinutes(5)).Error?.Code);
        Assert.True(hold.Expire(Now.AddMinutes(10)).IsSuccess);
        Assert.Equal(BookingStatus.Expired, hold.Status);
        Assert.Equal("hold_expired", hold.Confirm(Owner, Now.AddMinutes(11)).Error?.Code);
    }

    [Fact]
    public void A_hold_can_be_released_inside_the_cut_off()
    {
        var hold = Booking.CreateHold("court-1", Hour(Tomorrow, 10), Owner, "Nam", Now, TimeSpan.FromMinutes(10));
        Assert.True(hold.Cancel(Owner, hold.Time.Start.AddMinutes(-30), Policy).IsSuccess);
        Assert.Equal(BookingStatus.Cancelled, hold.Status);
    }
}
