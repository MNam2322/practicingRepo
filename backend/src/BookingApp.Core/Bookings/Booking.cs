using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>
/// Confirmed → Cancelled, or Confirmed → Completed / NoShow after it ends (staff actions, TASKS.md).
/// Library: 4.7 "Example 2: A booking lifecycle as a state machine", 12.6 "State machines for aggregate lifecycles".
/// </summary>
public enum BookingStatus { Confirmed, Cancelled, Completed, NoShow }

/// <summary>Identifies a customer. The starter uses an anonymous cookie; TASKS.md adds real accounts.</summary>
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
/// The booking aggregate. Every state change goes through a method here, so the rules live in one place
/// and a booking can never be in a state the rules do not allow.
/// </summary>
public sealed class Booking
{
    private Booking(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName,
        BookingStatus status, DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version)
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
    }

    public Guid Id { get; }
    public string ResourceId { get; }
    public TimeRange Time { get; }
    public CustomerId Customer { get; private set; }
    public string CustomerName { get; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Incremented by the repository on every save. Used for optimistic concurrency.</summary>
    public int Version { get; private set; }

    /// <summary>Holds a slot: only Confirmed bookings block other bookings.</summary>
    public bool BlocksSlot => Status == BookingStatus.Confirmed;

    public static Booking Create(string resourceId, TimeRange time, CustomerId customer, string customerName, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), resourceId, time, customer, customerName.Trim(), BookingStatus.Confirmed, now, null, 0);

    /// <summary>For stores rebuilding a booking they saved. Not for creating new bookings.</summary>
    public static Booking Restore(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName,
        BookingStatus status, DateTimeOffset createdAt, DateTimeOffset? cancelledAt, int version) =>
        new(id, resourceId, time, customer, customerName, status, createdAt, cancelledAt, version);

    /// <summary>Cancels the booking if the policy still allows it.</summary>
    public Result<Booking> Cancel(CustomerId by, DateTimeOffset now, BookingPolicy policy)
    {
        if (by != Customer) return new Error(ErrorKind.Forbidden, "not_your_booking", "You can only cancel your own bookings.");
        if (Status != BookingStatus.Confirmed)
            return new Error(ErrorKind.Conflict, "not_cancellable", $"This booking is {Status.ToString().ToLowerInvariant()} and cannot be cancelled.");
        if (Time.Start - now < policy.CancellationCutoff)
            return new Error(ErrorKind.RuleViolation, "too_late_to_cancel",
                $"Bookings can be cancelled up to {policy.CancellationCutoff.TotalHours:0.#} hours before they start.");
        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        return Result<Booking>.Ok(this);
    }

    /// <summary>
    /// Moves the booking to another customer: when someone who booked anonymously signs in, their bookings follow them
    /// to their account. Only the anonymous owner's bookings are ever moved (BookingService.ClaimAsync).
    /// </summary>
    public void TransferTo(CustomerId newOwner) => Customer = newOwner;

    /// <summary>Called by a repository after a successful save.</summary>
    public void MarkSaved() => Version++;
}
