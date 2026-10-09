using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Bookings;

/// <summary>Business rules that are settings, not code. Bound from the "Booking" configuration section.</summary>
public sealed class BookingPolicy
{
    /// <summary>How far ahead customers may book.</summary>
    public int MaxDaysAhead { get; set; } = 30;

    /// <summary>Bookings must start at least this many minutes from now.</summary>
    public int MinLeadMinutes { get; set; } = 15;

    /// <summary>Cancelling is allowed until this many hours before the start.</summary>
    public double CancellationCutoffHours { get; set; } = 2;

    /// <summary>The most active (future, confirmed) bookings one customer may hold.</summary>
    public int MaxActiveBookingsPerCustomer { get; set; } = 5;

    public TimeSpan CancellationCutoff => TimeSpan.FromHours(CancellationCutoffHours);

    /// <summary>
    /// Checks a requested booking against the resource and the policy. Everything except "is the slot free",
    /// which only the repository can answer atomically.
    /// </summary>
    public Error? Check(Resource resource, Venue venue, TimeRange time, DateTimeOffset now)
    {
        if (time.Start < now.AddMinutes(MinLeadMinutes))
            return new Error(ErrorKind.RuleViolation, "too_soon", $"Bookings must start at least {MinLeadMinutes} minutes from now.");
        if (time.Start > now.AddDays(MaxDaysAhead))
            return new Error(ErrorKind.RuleViolation, "too_far_ahead", $"You can book up to {MaxDaysAhead} days ahead.");

        var slots = time.Duration / resource.SlotLength;
        if (slots != Math.Floor(slots) || slots < 1)
            return new Error(ErrorKind.RuleViolation, "not_whole_slots", $"Bookings last a whole number of {resource.SlotMinutes}-minute slots.");
        if (slots > resource.MaxSlotsPerBooking)
            return new Error(ErrorKind.RuleViolation, "too_long", $"The longest booking for {resource.Name} is {resource.MaxSlotsPerBooking} slots.");

        // Opening hours and the slot grid are defined in venue local time, so check in local time.
        var localStart = TimeZoneInfo.ConvertTime(time.Start, venue.TimeZone);
        var localEnd = TimeZoneInfo.ConvertTime(time.End, venue.TimeZone);
        var hours = resource.HoursOn(localStart.DayOfWeek);
        if (hours is null || localEnd.Date != localStart.Date
            || TimeOnly.FromDateTime(localStart.DateTime) < hours.Open
            || TimeOnly.FromDateTime(localEnd.DateTime) > hours.Close)
            return new Error(ErrorKind.RuleViolation, "outside_opening_hours", $"{resource.Name} is not open for that whole time.");
        var minutesFromOpen = (TimeOnly.FromDateTime(localStart.DateTime) - hours.Open).TotalMinutes;
        if (minutesFromOpen % resource.SlotMinutes != 0)
            return new Error(ErrorKind.RuleViolation, "not_on_slot_grid", $"Bookings start on the {resource.SlotMinutes}-minute grid from {hours.Open:HH\\:mm}.");

        return null;
    }
}
