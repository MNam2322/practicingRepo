using BookingApp.Core.Bookings;

namespace BookingApp.Core.Notifications;

public enum NotificationKind { BookingConfirmed, BookingCancelled, Reminder }

/// <summary>
/// An email that must go out, stored in the same transaction as the booking change that caused it, so the change and
/// the promise to tell the customer commit together or not at all (library 12.3 "Outbox + inbox patterns", 8.7
/// "Example 1: Confirmation emails lost on restart"). <see cref="Id"/> is also the email's message id on every attempt,
/// which lets the sender drop a repeat (library 14.2 "The outbox relay").
/// </summary>
/// <param name="DueAt">Not sent before this: now for confirmations and cancellations, 24 hours ahead for reminders.</param>
public sealed record OutboxMessage(Guid Id, NotificationKind Kind, Guid BookingId, string To, DateTimeOffset CreatedAt, DateTimeOffset DueAt)
{
    /// <summary>Failed sends so far.</summary>
    public int Attempts { get; init; }
}

/// <summary>Which emails a booking event means. Pure: the repositories call it while saving the booking.</summary>
public static class NotificationPlan
{
    /// <summary>How long before the start the reminder goes out.</summary>
    public static readonly TimeSpan ReminderBefore = TimeSpan.FromHours(24);

    /// <summary>
    /// The messages for everything that happened to <paramref name="booking"/> since it was loaded. None when there is
    /// no address. Confirming also schedules the reminder, unless the booking starts within 24 hours (the confirmation
    /// is reminder enough); the reminder is checked again when it is due, so a cancelled booking is not reminded.
    /// </summary>
    public static IReadOnlyList<OutboxMessage> For(Booking booking)
    {
        if (booking.ContactEmail is not { } to) return [];
        var messages = new List<OutboxMessage>();
        foreach (var e in booking.Events)
        {
            if (e.Kind == BookingEventKind.Confirmed)
            {
                messages.Add(new OutboxMessage(Guid.CreateVersion7(), NotificationKind.BookingConfirmed, booking.Id, to, e.At, e.At));
                var remindAt = booking.Time.Start - ReminderBefore;
                if (remindAt > e.At) messages.Add(new OutboxMessage(Guid.CreateVersion7(), NotificationKind.Reminder, booking.Id, to, e.At, remindAt));
            }
            else
            {
                messages.Add(new OutboxMessage(Guid.CreateVersion7(), NotificationKind.BookingCancelled, booking.Id, to, e.At, e.At));
            }
        }
        return messages;
    }
}
