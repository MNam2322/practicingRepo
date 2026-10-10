using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>Where a booking is in its life. Transitions are in <see cref="Booking"/>'s table; anything not there is illegal.</summary>
public enum BookingStatus { Confirmed, Cancelled, Completed, NoShow, Pending, Expired }

/// <summary>What can happen to a booking.</summary>
public enum BookingTrigger { Cancel, Complete, MarkNoShow, Confirm, Expire }

/// <summary>Something that happened to a booking that the customer is told about (BookingApp.Core.Notifications).</summary>
public enum BookingEventKind { Confirmed, Cancelled }

/// <param name="At">When it happened.</param>
public sealed record BookingEvent(BookingEventKind Kind, DateTimeOffset At);

/// <summary>Identifies a customer: a signed-in account's id, or an anonymous browser cookie's.</summary>
public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.CreateVersion7());

    public static bool TryParse(string? value, out CustomerId id)
    {
        id = default;
        if (!Guid.TryParseExact(value, "N", out var g) || g == Guid.Empty) return false;
        id = new CustomerId(g);
        return true;
    }

    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// The booking aggregate. Every state change goes through a method here, so the rules live in one place and a booking
/// can never be in a state the rules do not allow. The lifecycle is an explicit transition table:
/// <code>
///   Pending   --Confirm----> Confirmed   (the customer, before the hold expires)
///   Pending   --Cancel-----> Cancelled   (the customer releases the hold, or staff cancel it)
///   Pending   --Expire-----> Expired     (the expiry job, once the hold is past its time)
///   Confirmed --Cancel-----> Cancelled   (customer before the cut-off; staff before the end, with a reason)
///   Confirmed --Complete---> Completed   (staff, once it has started)
///   Confirmed --MarkNoShow-> NoShow      (staff, once it has started)
///   Cancelled, Completed, NoShow, Expired: terminal
/// </code>
/// A hold (Pending) blocks the slot until it is confirmed, released or expires. Library: 4.7 "Example 2: A booking
/// lifecycle as a state machine", 12.6 "State machines for aggregate lifecycles", 7.9 Case 14 (held, confirmed,
/// released or expired).
/// </summary>
public sealed class Booking
{
    private static readonly Dictionary<(BookingStatus, BookingTrigger), BookingStatus> Transitions = new()
    {
        [(BookingStatus.Pending, BookingTrigger.Confirm)] = BookingStatus.Confirmed,
        [(BookingStatus.Pending, BookingTrigger.Cancel)] = BookingStatus.Cancelled,
        [(BookingStatus.Pending, BookingTrigger.Expire)] = BookingStatus.Expired,
        [(BookingStatus.Confirmed, BookingTrigger.Cancel)] = BookingStatus.Cancelled,
        [(BookingStatus.Confirmed, BookingTrigger.Complete)] = BookingStatus.Completed,
        [(BookingStatus.Confirmed, BookingTrigger.MarkNoShow)] = BookingStatus.NoShow,
    };

    public const int MaxReasonLength = 200;
    public const int MaxEmailLength = 254;

    private readonly List<BookingEvent> _events = [];

    private Booking(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName, BookingStatus status,
        DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version, string? cancellationReason, DateTimeOffset? holdExpiresAt,
        string? contactEmail)
    {
        Id = id;
        ResourceId = resourceId;
        Time = time;
        Customer = customer;
        CustomerName = customerName;
        Status = status;
        CreatedAt = createdAt;
        CancelledAt = cancelledAt;
        Version = version;
        CancellationReason = cancellationReason;
        HoldExpiresAt = holdExpiresAt;
        ContactEmail = contactEmail;
    }

    public Guid Id { get; }
    public string ResourceId { get; }
    public TimeRange Time { get; }
    public CustomerId Customer { get; private set; }
    public string CustomerName { get; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Why staff cancelled it. Null when the customer cancelled, or it was not cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>When a hold (Pending) stops counting. Null for bookings that were never a hold.</summary>
    public DateTimeOffset? HoldExpiresAt { get; }

    /// <summary>Where confirmations, reminders and cancellations go. Null: the customer is not emailed.</summary>
    public string? ContactEmail { get; }

    /// <summary>
    /// What happened since the booking was loaded, for the repository to turn into notifications in the same save
    /// (the outbox, library 12.3). Cleared once saved.
    /// </summary>
    public IReadOnlyList<BookingEvent> Events => _events;

    /// <summary>Incremented by the repository on every save. Used for optimistic concurrency.</summary>
    public int Version { get; private set; }

    /// <summary>Holds the slot at this moment: a confirmed booking, or a hold that has not expired yet.</summary>
    public bool BlocksAt(DateTimeOffset now) =>
        Status == BookingStatus.Confirmed || (Status == BookingStatus.Pending && now < HoldExpiresAt);

    /// <summary>The status as people should see it now: a hold past its time is expired, even before the expiry job runs.</summary>
    public BookingStatus StatusAt(DateTimeOffset now) =>
        Status == BookingStatus.Pending && now >= HoldExpiresAt ? BookingStatus.Expired : Status;

    /// <summary>True when the table has a transition for this status and trigger (guards aside).</summary>
    public static bool IsDeclared(BookingStatus status, BookingTrigger trigger) => Transitions.ContainsKey((status, trigger));

    /// <summary>
    /// What staff can do to this booking now, guards included. The staff UI shows exactly these actions, so it can
    /// never offer something the domain will refuse.
    /// </summary>
    public bool CanStaff(BookingTrigger trigger, DateTimeOffset now) => trigger switch
    {
        BookingTrigger.Cancel => IsDeclared(Status, trigger) && now < Time.End,
        BookingTrigger.Complete or BookingTrigger.MarkNoShow => IsDeclared(Status, trigger) && now >= Time.Start,
        _ => false,
    };

    /// <summary>A confirmed booking straight away (staff and demo data; customers start with a hold).</summary>
    public static Booking Create(string resourceId, TimeRange time, CustomerId customer, string customerName, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), resourceId, time, customer, customerName.Trim(), BookingStatus.Confirmed, now, null, 0, null, null, null);

    /// <summary>A hold: it blocks the slot for <paramref name="holdFor"/>, until it is confirmed or released.</summary>
    public static Booking CreateHold(string resourceId, TimeRange time, CustomerId customer, string customerName, DateTimeOffset now, TimeSpan holdFor,
        string? contactEmail = null) =>
        new(Guid.CreateVersion7(), resourceId, time, customer, customerName.Trim(), BookingStatus.Pending, now, null, 0, null, now + holdFor,
            string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim());

    /// <summary>For repositories rebuilding a booking they saved. Not for creating new bookings.</summary>
    public static Booking Restore(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName,
        BookingStatus status, DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version, string? cancellationReason = null,
        DateTimeOffset? holdExpiresAt = null, string? contactEmail = null) =>
        new(id, resourceId, time, customer, customerName, status, createdAt, cancelledAt, version, cancellationReason, holdExpiresAt, contactEmail);

    /// <summary>
    /// The customer confirms their hold before it expires. Confirming a booking that is already confirmed changes nothing
    /// and succeeds, so a retried confirm is harmless (library 7.9: "idempotent confirm").
    /// </summary>
    public Result<Booking> Confirm(CustomerId by, DateTimeOffset now)
    {
        if (CheckConfirm(by, now) is { } refused) return refused;
        if (Status == BookingStatus.Confirmed) return Result<Booking>.Ok(this);
        return Fire(BookingTrigger.Confirm, now);
    }

    /// <summary>
    /// Why <see cref="Confirm"/> would be refused now, or null if it would succeed (including "already confirmed").
    /// Lets a caller check before doing something it cannot undo cheaply, such as taking a deposit.
    /// </summary>
    public Error? CheckConfirm(CustomerId by, DateTimeOffset now)
    {
        if (by != Customer) return new Error(ErrorKind.Forbidden, "not_your_booking", "You can only confirm your own bookings.");
        if (Status == BookingStatus.Confirmed) return null;
        if (Status == BookingStatus.Expired || (Status == BookingStatus.Pending && now >= HoldExpiresAt)) return HoldExpired;
        return IsDeclared(Status, BookingTrigger.Confirm)
            ? null
            : new Error(ErrorKind.Conflict, "invalid_transition", $"A booking that is {Describe(Status)} cannot be confirmed.");
    }

    /// <summary>The expiry job ends a hold that is past its time.</summary>
    public Result<Booking> Expire(DateTimeOffset now)
    {
        if (Status == BookingStatus.Pending && now < HoldExpiresAt)
            return new Error(ErrorKind.Conflict, "hold_not_due", "This hold has not expired yet.");
        return Fire(BookingTrigger.Expire, now);
    }

    /// <summary>
    /// The customer cancels their own booking, up to the policy's cut-off before the start. A hold can always be
    /// released: it is not a booking yet.
    /// </summary>
    public Result<Booking> Cancel(CustomerId by, DateTimeOffset now, BookingPolicy policy)
    {
        if (by != Customer) return new Error(ErrorKind.Forbidden, "not_your_booking", "You can only cancel your own bookings.");
        if (!IsDeclared(Status, BookingTrigger.Cancel))
            return new Error(ErrorKind.Conflict, "not_cancellable", $"This booking is {Describe(Status)} and cannot be cancelled.");
        if (Status == BookingStatus.Confirmed && Time.Start - now < policy.CancellationCutoff)
            return new Error(ErrorKind.RuleViolation, "too_late_to_cancel",
                $"Bookings can be cancelled up to {policy.CancellationCutoff.TotalHours:0.#} hours before they start.");
        return Fire(BookingTrigger.Cancel, now);
    }

    /// <summary>Staff cancel any booking that has not ended yet, saying why (the customer sees the reason).</summary>
    public Result<Booking> CancelByStaff(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) return new Error(ErrorKind.RuleViolation, "reason_required", "Say why the booking is cancelled.");
        if (IsDeclared(Status, BookingTrigger.Cancel) && now >= Time.End)
            return new Error(ErrorKind.RuleViolation, "already_ended", "This booking has already ended. Mark it completed or a no-show instead.");
        var result = Fire(BookingTrigger.Cancel, now);
        if (result.IsSuccess) CancellationReason = reason.Trim()[..Math.Min(reason.Trim().Length, MaxReasonLength)];
        return result;
    }

    /// <summary>Staff record that the booking took place. Only once it has started.</summary>
    public Result<Booking> Complete(DateTimeOffset now) => StaffOutcome(BookingTrigger.Complete, now);

    /// <summary>Staff record that nobody came. Only once it has started.</summary>
    public Result<Booking> MarkNoShow(DateTimeOffset now) => StaffOutcome(BookingTrigger.MarkNoShow, now);

    /// <summary>
    /// Moves the booking to another customer: when someone who booked anonymously signs in, their bookings follow them
    /// to their account. Only the anonymous owner's bookings are ever moved (BookingService.ClaimAsync).
    /// </summary>
    public void TransferTo(CustomerId newOwner) => Customer = newOwner;

    /// <summary>Called by a repository after a successful save: the version moves on and the events are dealt with.</summary>
    public void MarkSaved()
    {
        Version++;
        _events.Clear();
    }

    public static readonly Error HoldExpired =
        new(ErrorKind.Conflict, "hold_expired", "This hold has expired and the time was released. Choose a time again.");

    private Result<Booking> StaffOutcome(BookingTrigger trigger, DateTimeOffset now)
    {
        if (IsDeclared(Status, trigger) && now < Time.Start)
            return new Error(ErrorKind.RuleViolation, "not_started", "This booking has not started yet.");
        return Fire(trigger, now);
    }

    private Result<Booking> Fire(BookingTrigger trigger, DateTimeOffset now)
    {
        if (!Transitions.TryGetValue((Status, trigger), out var next))
            return new Error(ErrorKind.Conflict, "invalid_transition", $"A booking that is {Describe(Status)} cannot be {Describe(trigger)}.");
        Status = next;
        if (next == BookingStatus.Cancelled) CancelledAt = now;
        if (next is BookingStatus.Confirmed or BookingStatus.Cancelled)
            _events.Add(new BookingEvent(next == BookingStatus.Confirmed ? BookingEventKind.Confirmed : BookingEventKind.Cancelled, now));
        return Result<Booking>.Ok(this);
    }

    private static string Describe(BookingStatus status) => status switch
    {
        BookingStatus.NoShow => "a no-show",
        BookingStatus.Pending => "a hold",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string Describe(BookingTrigger trigger) => trigger switch
    {
        BookingTrigger.Cancel => "cancelled",
        BookingTrigger.Complete => "marked completed",
        BookingTrigger.Confirm => "confirmed",
        BookingTrigger.Expire => "expired",
        _ => "marked a no-show",
    };
}
