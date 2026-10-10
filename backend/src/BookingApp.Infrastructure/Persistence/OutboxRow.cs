using BookingApp.Core.Notifications;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>A notification in the outbox, with what became of it. Outcome null: not done yet.</summary>
internal sealed class OutboxRow
{
    public const int MaxErrorLength = 500;

    public Guid Id { get; set; }
    public NotificationKind Kind { get; set; }
    public Guid? BookingId { get; set; }
    public string To { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public int Attempts { get; set; }
    /// <summary>After a failure: not tried again before this.</summary>
    public DateTimeOffset? RetryAt { get; set; }
    public DeliveryOutcome? Outcome { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Pre-rendered emails (waitlist) carry their text; the others are rendered from the booking when sent.</summary>
    public string? Subject { get; set; }
    public string? Body { get; set; }

    public static OutboxRow From(OutboxMessage m) => new()
    {
        Id = m.Id,
        Kind = m.Kind,
        BookingId = m.BookingId,
        To = m.To,
        CreatedAt = m.CreatedAt,
        DueAt = m.DueAt,
        Attempts = m.Attempts,
        Subject = m.Subject,
        Body = m.Body,
    };

    public OutboxMessage ToMessage() => new(Id, Kind, BookingId, To, CreatedAt, DueAt) { Attempts = Attempts, Subject = Subject, Body = Body };

    public OutboxEntry ToEntry() => new(ToMessage(), Outcome, DoneAt, LastError);
}
