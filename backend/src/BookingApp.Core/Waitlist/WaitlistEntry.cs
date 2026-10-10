using BookingApp.Core.Bookings;
using BookingApp.Core.Notifications;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Waitlist;

public enum WaitlistStatus
{
    /// <summary>In line for the time.</summary>
    Waiting,
    /// <summary>Told the time is free (first come, first served).</summary>
    Notified,
    /// <summary>Left the line.</summary>
    Left,
    /// <summary>The time has passed.</summary>
    Expired,
}

/// <summary>Someone waiting for a time that is taken: one resource, one exact range, in the order they joined.</summary>
public sealed class WaitlistEntry
{
    private WaitlistEntry(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName, string email,
        DateTimeOffset createdAt, WaitlistStatus status, DateTimeOffset? notifiedAt, int version)
    {
        Id = id;
        ResourceId = resourceId;
        Time = time;
        Customer = customer;
        CustomerName = customerName;
        Email = email;
        CreatedAt = createdAt;
        Status = status;
        NotifiedAt = notifiedAt;
        Version = version;
    }

    public Guid Id { get; }
    public string ResourceId { get; }
    public TimeRange Time { get; }
    public CustomerId Customer { get; }
    public string CustomerName { get; }

    /// <summary>Where the "it is free" email goes. Required: a waitlist that cannot tell you is no use.</summary>
    public string Email { get; }

    /// <summary>The place in line: earlier is first.</summary>
    public DateTimeOffset CreatedAt { get; }

    public WaitlistStatus Status { get; private set; }
    public DateTimeOffset? NotifiedAt { get; private set; }
    public int Version { get; private set; }

    public static WaitlistEntry Join(string resourceId, TimeRange time, CustomerId customer, string customerName, string email, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), resourceId, time, customer, customerName.Trim(), email.Trim(), now, WaitlistStatus.Waiting, null, 0);

    public static WaitlistEntry Restore(Guid id, string resourceId, TimeRange time, CustomerId customer, string customerName, string email,
        DateTimeOffset createdAt, WaitlistStatus status, DateTimeOffset? notifiedAt, int version) =>
        new(id, resourceId, time, customer, customerName, email, createdAt, status, notifiedAt, version);

    /// <summary>Leaves the line. False when the entry is no longer waiting.</summary>
    public bool Leave()
    {
        if (Status != WaitlistStatus.Waiting) return false;
        Status = WaitlistStatus.Left;
        return true;
    }

    /// <summary>Marks the entry told, with the email that tells it (saved together: <see cref="IWaitlistRepository.NotifyAsync"/>).</summary>
    public OutboxMessage Notify(DateTimeOffset now, string subject, string body)
    {
        if (Status != WaitlistStatus.Waiting) throw new InvalidOperationException("Only a waiting entry can be notified.");
        Status = WaitlistStatus.Notified;
        NotifiedAt = now;
        return new OutboxMessage(Guid.CreateVersion7(), NotificationKind.WaitlistSlotFree, null, Email, now, now) { Subject = subject, Body = body };
    }

    public void MarkSaved() => Version++;
}

/// <summary>The waitlist's storage. Implemented in Infrastructure.</summary>
public interface IWaitlistRepository
{
    /// <summary>Adds an entry. False when this customer is already waiting for this time (a partial unique index).</summary>
    Task<bool> TryAddAsync(WaitlistEntry entry, CancellationToken cancellationToken);

    /// <summary>Saves a change if nobody saved the entry since it was read (version check).</summary>
    Task<bool> UpdateAsync(WaitlistEntry entry, CancellationToken cancellationToken);

    Task<WaitlistEntry?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<WaitlistEntry>> ForCustomerAsync(CustomerId customer, CancellationToken cancellationToken);

    /// <summary>Entries waiting or told, for times that have not started, oldest first.</summary>
    Task<IReadOnlyList<WaitlistEntry>> OpenAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the entry as told and its email in the outbox, in one transaction, if nobody changed the entry since it was
    /// read. False when someone did (it is not told twice).
    /// </summary>
    Task<bool> NotifyAsync(WaitlistEntry entry, OutboxMessage email, CancellationToken cancellationToken);

    /// <summary>Entries for times that have started are over. Returns how many.</summary>
    Task<int> ExpirePastAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
