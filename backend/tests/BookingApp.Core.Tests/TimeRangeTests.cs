using BookingApp.Core.Scheduling;

namespace BookingApp.Core.Tests;

public sealed class TimeRangeTests
{
    private static readonly DateTimeOffset T = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static TimeRange Range(int fromHour, int toHour) => TimeRange.Create(T.AddHours(fromHour), T.AddHours(toHour));

    [Fact]
    public void Touching_ranges_do_not_overlap()
    {
        Assert.False(Range(0, 1).Overlaps(Range(1, 2)));
        Assert.False(Range(1, 2).Overlaps(Range(0, 1)));
    }

    [Fact]
    public void Ranges_that_share_time_overlap_both_ways()
    {
        Assert.True(Range(0, 2).Overlaps(Range(1, 3)));
        Assert.True(Range(1, 3).Overlaps(Range(0, 2)));
    }

    [Fact]
    public void A_range_inside_another_overlaps_it()
    {
        Assert.True(Range(0, 4).Overlaps(Range(1, 2)));
        Assert.True(Range(1, 2).Overlaps(Range(0, 4)));
    }

    [Fact]
    public void Identical_ranges_overlap() => Assert.True(Range(0, 1).Overlaps(Range(0, 1)));

    [Fact]
    public void Separate_ranges_do_not_overlap()
    {
        Assert.False(Range(0, 1).Overlaps(Range(2, 3)));
        Assert.False(Range(2, 3).Overlaps(Range(0, 1)));
    }

    [Fact]
    public void End_must_be_after_start()
    {
        Assert.Throws<ArgumentException>(() => TimeRange.Create(T, T));
        Assert.Throws<ArgumentException>(() => TimeRange.Create(T, T.AddMinutes(-1)));
    }

    [Fact]
    public void Ends_are_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.FromHours(7));
        var range = TimeRange.FromStart(local, TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.Zero, range.Start.Offset);
        Assert.Equal(T, range.Start);
        Assert.Equal(TimeSpan.FromHours(1), range.Duration);
    }

    [Fact]
    public void Contains_includes_its_own_ends()
    {
        Assert.True(Range(0, 3).Contains(Range(0, 3)));
        Assert.True(Range(0, 3).Contains(Range(1, 2)));
        Assert.False(Range(0, 3).Contains(Range(2, 4)));
    }
}
