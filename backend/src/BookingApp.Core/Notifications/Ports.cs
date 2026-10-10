namespace BookingApp.Core.Notifications;

/// <summary>The relay's view of the outbox: what is due, and what became of each attempt. Messages are added by the
/// booking repositories, in the same save as the change.</summary>
public interface INotificationOutbox
{
    /// <summary>Unsent messages that are due and not waiting for a retry, oldest first.</summary>
    Task<IReadOnlyList<OutboxMessage>> DueAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    /// <summary>Done: sent, or skipped as no longer true (a reminder for a cancelled booking).</summary>
    Task MarkDoneAsync(Guid id, DeliveryOutcome outcome, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>A failed attempt: try again at <paramref name="retryAt"/>, or never (null: dead-lettered, for a person to look at).</summary>
    Task MarkFailedAsync(Guid id, int attempts, DateTimeOffset? retryAt, string error, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Every message for a booking, with what became of it. For support and tests.</summary>
    Task<IReadOnlyList<OutboxEntry>> ForBookingAsync(Guid bookingId, CancellationToken cancellationToken);
}

public enum DeliveryOutcome { Sent, Skipped, Dead }

/// <summary>A message and what became of it.</summary>
public sealed record OutboxEntry(OutboxMessage Message, DeliveryOutcome? Outcome, DateTimeOffset? DoneAt, string? LastError);

/// <summary>
/// Sends one email. <see cref="EmailMessage.Id"/> is the same on every attempt for one notification, and a sender must
/// not deliver the same id twice: a relay that crashed after sending but before recording it will send it again
/// (library 14.2: "use the outbox row's id as the message id").
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <param name="Date">When it was due to go out (stable across attempts); the email's Date header.</param>
public sealed record EmailMessage(Guid Id, string To, string Subject, string Body, DateTimeOffset Date);
