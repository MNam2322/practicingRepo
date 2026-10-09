namespace BookingApp.Core.Scheduling;

/// <summary>
/// A half-open span of time [Start, End), always stored in UTC. Half-open means a booking from
/// 10:00 to 11:00 and one from 11:00 to 12:00 touch but do not overlap.
/// Library: 1.2 "Intervals, overlaps, and scheduling" and "DateTime is broken; use DateTimeOffset".
/// </summary>
public readonly record struct TimeRange
{
    private TimeRange(DateTimeOffset start, DateTimeOffset end) { Start = start; End = end; }

    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public TimeSpan Duration => End - Start;

    /// <summary>Creates a range; both ends are converted to UTC. Throws if End is not after Start.</summary>
    public static TimeRange Create(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) throw new ArgumentException($"End ({end:O}) must be after start ({start:O}).", nameof(end));
        return new TimeRange(start.ToUniversalTime(), end.ToUniversalTime());
    }

    public static TimeRange FromStart(DateTimeOffset start, TimeSpan duration) => Create(start, start + duration);

    /// <summary>True when the two ranges share any instant. Touching ends do not count.</summary>
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(TimeRange other) => Start <= other.Start && other.End <= End;

    public override string ToString() => $"{Start:O} - {End:O}";
}
