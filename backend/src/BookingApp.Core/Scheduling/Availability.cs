namespace BookingApp.Core.Scheduling;

public enum SlotState { Free, Booked, Past }

/// <param name="Time">The slot in UTC; send <c>Time.Start</c> back to book it.</param>
/// <param name="LocalStart">The same start in venue local time, for display.</param>
public sealed record Slot(TimeRange Time, DateTime LocalStart, DateTime LocalEnd, SlotState State);

/// <summary>
/// Works out a resource's slots for one local day. Pure: no clock, no storage, so it is easy to test,
/// including daylight-saving days. Library: 1.2 "Time zones and DST in practice", 4.4 (pure functions).
/// </summary>
public static class AvailabilityCalculator
{
    /// <param name="busy">Ranges already taken on that day (confirmed bookings).</param>
    /// <param name="notBefore">Slots starting before this instant are reported as Past.</param>
    public static IReadOnlyList<Slot> ForDay(Resource resource, Venue venue, DateOnly localDay, IEnumerable<TimeRange> busy, DateTimeOffset notBefore)
    {
        var hours = resource.HoursOn(localDay.DayOfWeek);
        if (hours is null) return [];

        var taken = busy.ToList();
        var slots = new List<Slot>();
        for (var local = localDay.ToDateTime(hours.Open); local.AddMinutes(resource.SlotMinutes) <= localDay.ToDateTime(hours.Close); local = local.AddMinutes(resource.SlotMinutes))
        {
            var localEnd = local.AddMinutes(resource.SlotMinutes);
            // A local time that does not exist (skipped by a DST jump) cannot be booked.
            if (venue.TimeZone.IsInvalidTime(local) || venue.TimeZone.IsInvalidTime(localEnd)) continue;
            var range = TimeRange.Create(ToUtc(local, venue), ToUtc(localEnd, venue));
            var state = range.Start < notBefore ? SlotState.Past
                : taken.Any(t => t.Overlaps(range)) ? SlotState.Booked
                : SlotState.Free;
            slots.Add(new Slot(range, local, localEnd, state));
        }
        return slots;
    }

    /// <summary>The UTC range covering a whole local day, for loading that day's bookings.</summary>
    public static TimeRange DayWindow(Venue venue, DateOnly localDay) =>
        TimeRange.Create(ToUtc(localDay.ToDateTime(TimeOnly.MinValue), venue), ToUtc(localDay.AddDays(1).ToDateTime(TimeOnly.MinValue), venue));

    /// <summary>Venue local wall-clock time to UTC. Ambiguous times (DST fall-back) resolve to the standard-time offset.</summary>
    public static DateTimeOffset ToUtc(DateTime local, Venue venue) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), venue.TimeZone), TimeSpan.Zero);
}
