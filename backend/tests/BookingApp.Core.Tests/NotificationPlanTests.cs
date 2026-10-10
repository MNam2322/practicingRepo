using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

/// <summary>Which emails each booking event means.</summary>
public class NotificationPlanTests
{
    private static readonly CustomerId Alice = CustomerId.New();

    private static Booking Hold(DateOnly day, int hour, string? email = "alice@example.com") =>
        Booking.CreateHold("court-1", Hour(day, hour), Alice, "Alice", Now, TimeSpan.FromMinutes(10), email);

    [Fact]
    public void Confirming_means_a_confirmation_now_and_a_reminder_24_hours_before()
    {
        var booking = Hold(Tomorrow, 10);
        booking.Confirm(Alice, Now.AddMinutes(1));

        var plan = NotificationPlan.For(booking);
        Assert.Equal([NotificationKind.BookingConfirmed, NotificationKind.Reminder], plan.Select(m => m.Kind));
        Assert.Equal(Now.AddMinutes(1), plan[0].DueAt);
        Assert.Equal(booking.Time.Start.AddHours(-24), plan[1].DueAt);
        Assert.All(plan, m => Assert.Equal(("alice@example.com", booking.Id), (m.To, m.BookingId)));
        Assert.NotEqual(plan[0].Id, plan[1].Id);
    }

    [Fact]
    public void A_booking_within_24_hours_gets_no_reminder()
    {
        var soon = Booking.CreateHold("court-1", TimeRange.FromStart(Now.AddHours(5), TimeSpan.FromHours(1)), Alice, "Alice", Now,
            TimeSpan.FromMinutes(10), "alice@example.com");
        soon.Confirm(Alice, Now);
        Assert.Equal([NotificationKind.BookingConfirmed], NotificationPlan.For(soon).Select(m => m.Kind));
    }

    [Fact]
    public void Cancelling_means_a_cancellation_and_releasing_a_hold_too()
    {
        var booking = Hold(Tomorrow, 10);
        booking.Cancel(Alice, Now, new BookingPolicy());
        Assert.Equal([NotificationKind.BookingCancelled], NotificationPlan.For(booking).Select(m => m.Kind));
    }

    [Fact]
    public void No_address_no_emails_and_other_changes_mean_none()
    {
        var anonymous = Hold(Tomorrow, 10, email: null);
        anonymous.Confirm(Alice, Now);
        Assert.Empty(NotificationPlan.For(anonymous));

        var played = Booking.Restore(Guid.NewGuid(), "court-1", Hour(Tomorrow, 10), Alice, "Alice", BookingStatus.Confirmed, Now, null, 1,
            contactEmail: "alice@example.com");
        played.Complete(played.Time.Start.AddMinutes(5));
        Assert.Empty(NotificationPlan.For(played));
    }

    [Fact]
    public void Events_are_cleared_once_saved()
    {
        var booking = Hold(Tomorrow, 10);
        booking.Confirm(Alice, Now);
        Assert.Single(booking.Events);
        booking.MarkSaved();
        Assert.Empty(booking.Events);
        Assert.Empty(NotificationPlan.For(booking));
    }
}
