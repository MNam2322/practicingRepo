using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Tests;

/// <summary>Weekly dates keep their wall-clock time across a DST change (library 1.2 "Recurring events").</summary>
public class SeriesTests
{
    private static readonly Venue London = new("London club", "Europe/London");

    [Fact]
    public void Every_week_keeps_the_local_time_across_the_clock_change()
    {
        // 18:00 on Tuesday 24 March 2026 (GMT); the clocks go forward on Sunday 29 March.
        var first = new DateTimeOffset(2026, 3, 24, 18, 0, 0, TimeSpan.Zero);
        var starts = SeriesService.WeeklyStarts(London, first, 3);

        Assert.Equal([new DateOnly(2026, 3, 24), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 7)], starts.Select(s => s.LocalDate));
        Assert.All(starts, s => Assert.Equal(18, TimeZoneInfo.ConvertTime(s.Start!.Value, London.TimeZone).Hour));
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 17, 0, 0, TimeSpan.Zero), starts[1].Start);   // 18:00 BST is 17:00 UTC
    }

    [Fact]
    public void A_time_that_does_not_exist_on_a_date_is_null()
    {
        // 01:30 on Sunday 22 March; on 29 March the clocks jump from 01:00 to 02:00.
        var starts = SeriesService.WeeklyStarts(London, new DateTimeOffset(2026, 3, 22, 1, 30, 0, TimeSpan.Zero), 2);
        Assert.NotNull(starts[0].Start);
        Assert.Null(starts[1].Start);
    }
}
