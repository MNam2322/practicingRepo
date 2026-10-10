using System.ComponentModel.DataAnnotations;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Api;

// Request and response shapes. Controllers never return domain objects, so the domain can change without
// breaking clients. The React app mirrors these in frontend/src/lib/api.ts. Keep the two in step.
// Times: "start"/"end" are UTC instants (ISO 8601 with Z); "localStart"/"localEnd" are venue wall-clock
// times without an offset, for display only.

public sealed record VenueResponse(string Name, string TimeZone, PolicyResponse Policy);

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
}

public sealed record BookingResponse(
    Guid Id, string ResourceId, string ResourceName, DateTimeOffset Start, DateTimeOffset End,
    string LocalStart, string LocalEnd, BookingStatus Status, string CustomerName,
    DateTimeOffset CreatedAt, DateTimeOffset? CancelledAt, bool CanCancel, string? CancellationReason)
{
    public static BookingResponse From(Booking b, ResourceCatalog catalog, BookingPolicy policy, DateTimeOffset now) => new(
        b.Id, b.ResourceId, catalog.Find(b.ResourceId)?.Name ?? b.ResourceId, b.Time.Start, b.Time.End,
        ClosureResponse.Local(b.Time.Start, catalog.Venue), ClosureResponse.Local(b.Time.End, catalog.Venue),
        b.Status, b.CustomerName, b.CreatedAt, b.CancelledAt,
        b.Status == BookingStatus.Confirmed && b.Time.Start - now >= policy.CancellationCutoff,
        b.CancellationReason);
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
        b.Status, b.CustomerName, b.CancellationReason,
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
