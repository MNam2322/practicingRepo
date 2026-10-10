using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>Where a booking is in its life. Transitions are in <see cref="Booking"/>'s table; anything not there is illegal.</summary>
public enum BookingStatus { Confirmed, Cancelled, Completed, NoShow }

/// <summary>What can happen to a booking.</summary>
public enum BookingTrigger { Cancel, Complete, MarkNoShow }

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
///   Confirmed --Cancel-----> Cancelled   (customer before the cut-off; staff before the end, with a reason)
///   Confirmed --Complete---> Completed   (staff, once it has started)
///   Confirmed --MarkNoShow-> NoShow      (staff, once it has started)
///   Cancelled, Completed, NoShow: terminal
/// </code>
/// Library: 4.7 "Example 2: A booking lifecycle as a state machine", 12.6 "State machines for aggregate lifecycles".
/// </summary>
public sealed class Booking
{
    private static readonly Dictionary<(BookingStatus, BookingTrigger), BookingStatus> Transitions = new()
    {
        [(BookingStatus.Confirmed, BookingTrigger.Cancel)] = BookingStatus.Cancelled,
        [(BookingStatus.Confirmed, BookingTrigger.Complete)] = BookingStatus.Completed,
        [(BookingStatus.Confirmed, BookingTrigger.MarkNoShow)] = BookingStatus.NoShow,
    };

    public const int MaxReasonLength = 200;

    private Booking(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName,
        BookingStatus status, DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version, string? cancellationReason)
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

    /// <summary>Incremented by the repository on every save. Used for optimistic concurrency.</summary>
    public int Version { get; private set; }

    /// <summary>Holds a slot: only Confirmed bookings block other bookings.</summary>
    public bool BlocksSlot => Status == BookingStatus.Confirmed;

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

    public static Booking Create(string resourceId, TimeRange time, CustomerId customer, string customerName, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), resourceId, time, customer, customerName.Trim(), BookingStatus.Confirmed, now, null, 0, null);

    /// <summary>For repositories rebuilding a booking they saved. Not for creating new bookings.</summary>
    public static Booking Restore(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName,
        BookingStatus status, DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version, string? cancellationReason = null) =>
        new(id, resourceId, time, customer, customerName, status, createdAt, cancelledAt, version, cancellationReason);

    /// <summary>The customer cancels their own booking, up to the policy's cut-off before the start.</summary>
    public Result<Booking> Cancel(CustomerId by, DateTimeOffset now, BookingPolicy policy)
    {
        if (by != Customer) return new Error(ErrorKind.Forbidden, "not_your_booking", "You can only cancel your own bookings.");
        if (!IsDeclared(Status, BookingTrigger.Cancel))
            return new Error(ErrorKind.Conflict, "not_cancellable", $"This booking is {Describe(Status)} and cannot be cancelled.");
        if (Time.Start - now < policy.CancellationCutoff)
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

    /// <summary>Called by a repository after a successful save.</summary>
    public void MarkSaved() => Version++;

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
        return Result<Booking>.Ok(this);
    }

    private static string Describe(BookingStatus status) => status switch
    {
        BookingStatus.NoShow => "a no-show",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static string Describe(BookingTrigger trigger) => trigger switch
    {
        BookingTrigger.Cancel => "cancelled",
        BookingTrigger.Complete => "marked completed",
        _ => "marked a no-show",
    };
}
