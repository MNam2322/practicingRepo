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

public sealed record AvailabilityResponse(string ResourceId, DateOnly Date, string TimeZone, IReadOnlyList<SlotResponse> Slots);

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
    DateTimeOffset CreatedAt, DateTimeOffset? CancelledAt, bool CanCancel)
{
    public static BookingResponse From(Booking b, VenueService venue, DateTimeOffset now)
    {
        var tz = venue.Venue.TimeZone;
        return new BookingResponse(
            b.Id, b.ResourceId, venue.Find(b.ResourceId)?.Name ?? b.ResourceId, b.Time.Start, b.Time.End,
            TimeZoneInfo.ConvertTime(b.Time.Start, tz).ToString("yyyy-MM-ddTHH:mm"),
            TimeZoneInfo.ConvertTime(b.Time.End, tz).ToString("yyyy-MM-ddTHH:mm"),
            b.Status, b.CustomerName, b.CreatedAt, b.CancelledAt,
            b.Status == BookingStatus.Confirmed && b.Time.Start - now >= venue.Policy.CancellationCutoff);
    }
}
