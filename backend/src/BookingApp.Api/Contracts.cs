using System.ComponentModel.DataAnnotations;
using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Core.Scheduling;

namespace BookingApp.Api;

// Request and response shapes. Controllers never return domain objects, so the domain can change without
// breaking clients. The React app mirrors these in frontend/src/lib/api.ts. Keep the two in step.
// Times: "start"/"end" are UTC instants (ISO 8601 with Z); "localStart"/"localEnd" are venue wall-clock
// times without an offset, for display only.

/// <param name="Deposit">What confirming a booking costs now, or null when deposits are off.</param>
public sealed record VenueResponse(string Name, string TimeZone, PolicyResponse Policy, MoneyResponse? Deposit);

/// <summary>An amount in major units (100000 VND, 12.50 USD). Format it for display with the currency.</summary>
public sealed record MoneyResponse(decimal Amount, string Currency)
{
    public static MoneyResponse From(Money m) => new(m.Major, m.Currency);
}

/// <summary>Where a booking's deposit is.</summary>
public enum DepositStatus
{
    /// <summary>A hold that needs the deposit to be confirmed (after a declined card too: see failureCode).</summary>
    Due,
    /// <summary>Sent to the provider, outcome not known yet. Confirming again is safe and cannot charge twice.</summary>
    Pending,
    Paid,
    /// <summary>The hold ended without the deposit being paid.</summary>
    Failed,
    Refunded,
}

/// <param name="PaymentId">The payment, if one was started.</param>
/// <param name="FailureCode">Why the last attempt failed, such as insufficient_funds.</param>
public sealed record DepositResponse(Guid? PaymentId, decimal Amount, string Currency, DepositStatus Status, string? FailureCode)
{
    /// <summary>The deposit as the customer should see it, or null when the booking has none.</summary>
    public static DepositResponse? For(Booking b, Payment? payment, Money deposit, DateTimeOffset now)
    {
        var liveHold = b.StatusAt(now) == BookingStatus.Pending;
        if (payment is null)
            return liveHold && !deposit.IsZero ? new DepositResponse(null, deposit.Major, deposit.Currency, DepositStatus.Due, null) : null;
        var status = payment.Status switch
        {
            PaymentStatus.Pending => DepositStatus.Pending,
            PaymentStatus.Paid => DepositStatus.Paid,
            PaymentStatus.Refunded => DepositStatus.Refunded,
            _ => liveHold ? DepositStatus.Due : DepositStatus.Failed,
        };
        return new DepositResponse(payment.Id, payment.Amount.Major, payment.Amount.Currency, status, payment.FailureCode);
    }
}

/// <summary>Confirms a hold. The deposit is paid with a token from the payment provider's card form.</summary>
public sealed record ConfirmBookingRequest
{
    /// <summary>
    /// The provider's payment token. Required when the venue takes a deposit. With the fake provider: tok_visa (approved),
    /// tok_declined, tok_insufficient_funds, tok_expired_card, tok_unavailable, tok_timeout.
    /// </summary>
    [StringLength(200, ErrorMessage = "That payment token is too long.")]
    public string? PaymentToken { get; init; }
}

/// <summary>What the webhook endpoint answers: the event id, and whether it changed anything.</summary>
public sealed record WebhookAck(string EventId, WebhookOutcome Outcome);

public sealed record PolicyResponse(int MaxDaysAhead, int MinLeadMinutes, double CancellationCutoffHours, int MaxActiveBookingsPerCustomer)
{
    public static PolicyResponse From(BookingPolicy p) => new(p.MaxDaysAhead, p.MinLeadMinutes, p.CancellationCutoffHours, p.MaxActiveBookingsPerCustomer);
}

public sealed record OpeningHoursResponse(DayOfWeek Day, string Open, string Close);

public sealed record ResourceResponse(string Id, string Name, ResourceKind Kind, int Capacity, int SlotMinutes, int MaxSlotsPerBooking, IReadOnlyList<OpeningHoursResponse> Hours)
{
    public static ResourceResponse From(Resource r) => new(r.Id, r.Name, r.Kind, r.Capacity, r.SlotMinutes, r.MaxSlotsPerBooking,
        [.. r.Hours.OrderBy(h => ((int)h.Day + 6) % 7).Select(h => new OpeningHoursResponse(h.Day, h.Open.ToString("HH:mm"), h.Close.ToString("HH:mm")))]);
}

public sealed record SlotResponse(DateTimeOffset Start, DateTimeOffset End, string LocalStart, string LocalEnd, SlotState State)
{
    public static SlotResponse From(Slot s) => new(s.Time.Start, s.Time.End, s.LocalStart.ToString("HH:mm"), s.LocalEnd.ToString("HH:mm"), s.State);
}

/// <param name="Closures">Closures touching this day, so the UI can say why slots are closed.</param>
public sealed record AvailabilityResponse(string ResourceId, DateOnly Date, string TimeZone, IReadOnlyList<SlotResponse> Slots, IReadOnlyList<ClosureResponse> Closures);

/// <summary>A time a resource (or the whole venue) is closed. localStart/localEnd are venue wall-clock "yyyy-MM-ddTHH:mm".</summary>
public sealed record ClosureResponse(Guid Id, string? ResourceId, DateTimeOffset Start, DateTimeOffset End, string LocalStart, string LocalEnd, string Reason)
{
    public static ClosureResponse From(Closure c, Venue venue) => new(c.Id, c.ResourceId, c.Time.Start, c.Time.End,
        Local(c.Time.Start, venue), Local(c.Time.End, venue), c.Reason);

    internal static string Local(DateTimeOffset instant, Venue venue) => TimeZoneInfo.ConvertTime(instant, venue.TimeZone).ToString("yyyy-MM-ddTHH:mm");
}

/// <summary>
/// A booking request. The attributes check its shape at the boundary (400 with field errors); whether the booking is
/// allowed is a business rule checked in Core (409/422 with a code). Library: 8.5 "Three layers of validation".
/// </summary>
public sealed record CreateBookingRequest
{
    public const int MaxResourceIdLength = 64;
    public const int MaxSlots = 24;
    public const int MaxNameLength = 80;

    /// <summary>The resource to book, such as court-1.</summary>
    [Required(ErrorMessage = "Choose what to book.")]
    [StringLength(MaxResourceIdLength, ErrorMessage = "That resource id is too long.")]
    public string? ResourceId { get; init; }

    /// <summary>The UTC start of the first slot, exactly as returned by the availability endpoint.</summary>
    [Required(ErrorMessage = "Choose a start time.")]
    public DateTimeOffset? Start { get; init; }

    /// <summary>How many consecutive slots. Leave it out for one.</summary>
    [Range(1, MaxSlots, ErrorMessage = "Book between 1 and 24 slots.")]
    public int? Slots { get; init; }

    /// <summary>The name the booking is under.</summary>
    [Required(ErrorMessage = "Enter the name for the booking.")]
    [StringLength(MaxNameLength, ErrorMessage = "Keep the name to 80 characters or fewer.")]
    public string? Name { get; init; }

    /// <summary>Where the confirmation, a reminder the day before and any cancellation are emailed. Optional; signed-in
    /// customers get their account's address when this is left out.</summary>
    [EmailAddress(ErrorMessage = "Enter an email address such as name@example.com, or leave it empty.")]
    [StringLength(Booking.MaxEmailLength, ErrorMessage = "That email address is too long.")]
    public string? Email { get; init; }
}

public sealed record BookingResponse(
    Guid Id, string ResourceId, string ResourceName, DateTimeOffset Start, DateTimeOffset End,
    string LocalStart, string LocalEnd, BookingStatus Status, string CustomerName,
    DateTimeOffset CreatedAt, DateTimeOffset? CancelledAt, bool CanCancel, string? CancellationReason,
    DateTimeOffset? HoldExpiresAt, bool CanConfirm, DepositResponse? Deposit, string? Email)
{
    /// <param name="now">The moment the answers (canCancel, canConfirm) are worked out for.</param>
    /// <param name="payment">The booking's current payment (DepositService.Current), if any.</param>
    /// <param name="deposit">The deposit confirming takes now.</param>
    public static BookingResponse From(Booking b, ResourceCatalog catalog, BookingPolicy policy, DateTimeOffset now, Payment? payment, Money deposit)
    {
        var liveHold = b.Status == BookingStatus.Pending && now < b.HoldExpiresAt;
        return new(
            b.Id, b.ResourceId, catalog.Find(b.ResourceId)?.Name ?? b.ResourceId, b.Time.Start, b.Time.End,
            ClosureResponse.Local(b.Time.Start, catalog.Venue), ClosureResponse.Local(b.Time.End, catalog.Venue),
            b.StatusAt(now),
            b.CustomerName, b.CreatedAt, b.CancelledAt,
            // A live hold can always be released; a confirmed booking until the cut-off.
            liveHold || (b.Status == BookingStatus.Confirmed && b.Time.Start - now >= policy.CancellationCutoff),
            b.CancellationReason, b.HoldExpiresAt, liveHold, DepositResponse.For(b, payment, deposit, now), b.ContactEmail);
    }
}

/// <summary>Create an account. The password needs at least 10 characters; no other rules.</summary>
public sealed record SignUpRequest
{
    /// <summary>The email to sign in with.</summary>
    [Required(ErrorMessage = "Enter your email.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(254, ErrorMessage = "That email is too long.")]
    public string? Email { get; init; }

    /// <summary>At least 10 characters.</summary>
    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Use at least 10 characters.")]
    public string? Password { get; init; }

    /// <summary>Your name, shown in the account menu and suggested for bookings.</summary>
    [Required(ErrorMessage = "Enter your name.")]
    [StringLength(CreateBookingRequest.MaxNameLength, ErrorMessage = "Keep the name to 80 characters or fewer.")]
    public string? Name { get; init; }
}

public sealed record SignInRequest
{
    [Required(ErrorMessage = "Enter your email.")]
    [StringLength(254)]
    public string? Email { get; init; }

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(128)]
    public string? Password { get; init; }
}

/// <summary>The signed-in account.</summary>
/// <param name="Roles">Customer, and Staff or Admin where granted.</param>
public sealed record AccountResponse(Guid Id, string Email, string Name, IReadOnlyList<string> Roles)
{
    public static AccountResponse From(BookingApp.Core.Accounts.Account a) => new(a.Id, a.Email, a.Name, a.Roles);
}

/// <param name="MovedBookings">Bookings made before signing in (on this browser) that now belong to the account.</param>
public sealed record SignedInResponse(AccountResponse Account, int MovedBookings);

/// <summary>What staff may do to a booking right now (the domain's transition table plus its guards).</summary>
public sealed record StaffActions(bool Complete, bool NoShow, bool Cancel);

/// <summary>A booking as staff see it on the schedule.</summary>
public sealed record StaffBookingResponse(
    Guid Id, string ResourceId, DateTimeOffset Start, DateTimeOffset End, string LocalStart, string LocalEnd,
    BookingStatus Status, string CustomerName, string? CancellationReason, StaffActions Actions)
{
    public static StaffBookingResponse From(Booking b, Venue venue, DateTimeOffset now) => new(
        b.Id, b.ResourceId, b.Time.Start, b.Time.End, ClosureResponse.Local(b.Time.Start, venue), ClosureResponse.Local(b.Time.End, venue),
        b.StatusAt(now), b.CustomerName, b.CancellationReason,
        new StaffActions(b.CanStaff(BookingTrigger.Complete, now), b.CanStaff(BookingTrigger.MarkNoShow, now), b.CanStaff(BookingTrigger.Cancel, now)));
}

/// <param name="Open">Venue-local "HH:mm", or null when closed all day.</param>
public sealed record ScheduleColumnResponse(string ResourceId, string ResourceName, int SlotMinutes, string? Open, string? Close,
    IReadOnlyList<StaffBookingResponse> Bookings, IReadOnlyList<ClosureResponse> Closures);

/// <summary>The day schedule: one column per resource.</summary>
public sealed record ScheduleResponse(DateOnly Date, string TimeZone, IReadOnlyList<ScheduleColumnResponse> Resources)
{
    public static ScheduleResponse From(BookingApp.Core.Staff.DaySchedule s, DateTimeOffset now) => new(s.Date, s.Venue.TimeZoneId,
        [.. s.Columns.Select(c => new ScheduleColumnResponse(c.Resource.Id, c.Resource.Name, c.Resource.SlotMinutes,
            c.Hours?.Open.ToString("HH:mm"), c.Hours?.Close.ToString("HH:mm"),
            [.. c.Bookings.Select(b => StaffBookingResponse.From(b, s.Venue, now))],
            [.. c.Closures.Select(x => ClosureResponse.From(x, s.Venue))]))]);
}

/// <summary>Cancel a booking as staff. The customer sees the reason.</summary>
public sealed record StaffCancelRequest
{
    [Required(ErrorMessage = "Say why the booking is cancelled.")]
    [StringLength(Booking.MaxReasonLength, ErrorMessage = "Keep the reason to 200 characters or fewer.")]
    public string? Reason { get; init; }
}

/// <summary>Block time on a resource: start and end are UTC instants, such as slot starts from availability.</summary>
public sealed record BlockTimeRequest
{
    [Required(ErrorMessage = "Choose what to block.")]
    [StringLength(CreateBookingRequest.MaxResourceIdLength)]
    public string? ResourceId { get; init; }

    [Required(ErrorMessage = "Choose a start time.")]
    public DateTimeOffset? Start { get; init; }

    [Required(ErrorMessage = "Choose an end time.")]
    public DateTimeOffset? End { get; init; }

    [Required(ErrorMessage = "Say why the time is blocked.")]
    [StringLength(200, ErrorMessage = "Keep the reason to 200 characters or fewer.")]
    public string? Reason { get; init; }
}

// ---- admin ----

/// <summary>A new resource. The id becomes part of URLs and cannot change later.</summary>
public sealed record CreateResourceRequest
{
    /// <summary>Lowercase letters, digits and dashes, such as court-3.</summary>
    [Required(ErrorMessage = "Give the resource an id, such as court-3.")]
    [RegularExpression("^[a-z0-9][a-z0-9-]{1,62}$", ErrorMessage = "Use 2 to 63 lowercase letters, digits and dashes, such as court-3.")]
    public string? Id { get; init; }

    [Required(ErrorMessage = "Give the resource a name.")]
    [StringLength(100, ErrorMessage = "Keep the name to 100 characters or fewer.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Choose what kind of resource it is.")]
    public ResourceKind? Kind { get; init; }

    [Required, Range(1, 1000, ErrorMessage = "Capacity is between 1 and 1000 people.")]
    public int? Capacity { get; init; }

    [Required, Range(5, 240, ErrorMessage = "Slots last between 5 and 240 minutes.")]
    public int? SlotMinutes { get; init; }

    [Required, Range(1, 48, ErrorMessage = "The longest booking is between 1 and 48 slots.")]
    public int? MaxSlotsPerBooking { get; init; }
}

/// <summary>Changes to a resource. The id stays the same.</summary>
public sealed record UpdateResourceRequest
{
    [Required(ErrorMessage = "Give the resource a name.")]
    [StringLength(100, ErrorMessage = "Keep the name to 100 characters or fewer.")]
    public string? Name { get; init; }

    [Required(ErrorMessage = "Choose what kind of resource it is.")]
    public ResourceKind? Kind { get; init; }

    [Required, Range(1, 1000, ErrorMessage = "Capacity is between 1 and 1000 people.")]
    public int? Capacity { get; init; }

    [Required, Range(5, 240, ErrorMessage = "Slots last between 5 and 240 minutes.")]
    public int? SlotMinutes { get; init; }

    [Required, Range(1, 48, ErrorMessage = "The longest booking is between 1 and 48 slots.")]
    public int? MaxSlotsPerBooking { get; init; }
}

/// <summary>One day's opening hours, in venue-local time.</summary>
public sealed record OpeningHoursRequest
{
    [Required(ErrorMessage = "Choose the day.")]
    public DayOfWeek? Day { get; init; }

    /// <summary>Venue-local "HH:mm".</summary>
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Use HH:mm, such as 07:00.")]
    public string? Open { get; init; }

    /// <summary>Venue-local "HH:mm", after Open.</summary>
    [Required, RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Use HH:mm, such as 21:00.")]
    public string? Close { get; init; }
}

/// <summary>The whole week. Days that are left out are closed.</summary>
public sealed record SetHoursRequest
{
    [Required, MaxLength(7, ErrorMessage = "There are only seven days.")]
    public List<OpeningHoursRequest>? Hours { get; init; }
}

/// <summary>Close one resource, or the whole venue when resourceId is left out, for whole venue-local days.</summary>
public sealed record AdminClosureRequest
{
    /// <summary>Leave out to close every resource (a holiday).</summary>
    [StringLength(CreateBookingRequest.MaxResourceIdLength)]
    public string? ResourceId { get; init; }

    /// <summary>The first closed day (venue-local yyyy-MM-dd).</summary>
    [Required(ErrorMessage = "Choose the first day.")]
    public DateOnly? From { get; init; }

    /// <summary>The last closed day, inclusive.</summary>
    [Required(ErrorMessage = "Choose the last day.")]
    public DateOnly? To { get; init; }

    [Required(ErrorMessage = "Say why it is closed.")]
    [StringLength(200, ErrorMessage = "Keep the reason to 200 characters or fewer.")]
    public string? Reason { get; init; }
}

/// <summary>The booking rules.</summary>
public sealed record PolicyRequest
{
    [Required, Range(1, 365, ErrorMessage = "Between 1 and 365 days.")]
    public int? MaxDaysAhead { get; init; }

    [Required, Range(0, 1440, ErrorMessage = "Between 0 and 1440 minutes.")]
    public int? MinLeadMinutes { get; init; }

    [Required, Range(0, 168, ErrorMessage = "Between 0 and 168 hours.")]
    public double? CancellationCutoffHours { get; init; }

    [Required, Range(1, 100, ErrorMessage = "Between 1 and 100 bookings.")]
    public int? MaxActiveBookingsPerCustomer { get; init; }
}

/// <summary>One audit log entry: who changed what, and what it was before and after (JSON).</summary>
public sealed record AuditEntryResponse(Guid Id, DateTimeOffset OccurredAt, Guid ActorId, string ActorEmail, string? RequestId,
    string Action, string Entity, string EntityId, string? Before, string? After)
{
    public static AuditEntryResponse From(BookingApp.Core.Admin.AuditEntry e) =>
        new(e.Id, e.OccurredAt, e.Actor.AccountId, e.Actor.Email, e.Actor.RequestId, e.Action, e.Entity, e.EntityId, e.Before, e.After);
}
