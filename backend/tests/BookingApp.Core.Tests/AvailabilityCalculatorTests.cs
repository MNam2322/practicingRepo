using BookingApp.Core.Scheduling;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

public sealed class AvailabilityCalculatorTests
{
    private static readonly Resource Court = TestData.Court();

    [Fact]
    public void A_normal_day_has_one_slot_per_hour_of_opening()
    {
        var slots = AvailabilityCalculator.ForDay(Court, Club, Tomorrow, [], Now);

        Assert.Equal(16, slots.Count);
        Assert.Equal(new DateTime(2026, 6, 2, 6, 0, 0), slots[0].LocalStart);
        Assert.Equal(At(Tomorrow, 6), slots[0].Time.Start);
        Assert.Equal(new DateTime(2026, 6, 2, 22, 0, 0), slots[^1].LocalEnd);
        Assert.All(slots, s => Assert.Equal(SlotState.Free, s.State));
    }

    [Fact]
    public void A_closed_day_has_no_slots()
    {
        var mondaysOnly = Court with { Hours = [new OpeningHours(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(22, 0))] };
        Assert.Empty(AvailabilityCalculator.ForDay(mondaysOnly, Club, Tomorrow, [], Now));
    }

    [Fact]
    public void Slots_overlapping_a_busy_range_are_booked_and_touching_ones_are_free()
    {
        var busy = TimeRange.FromStart(At(Tomorrow, 10), TimeSpan.FromHours(2));
        var slots = AvailabilityCalculator.ForDay(Court, Club, Tomorrow, [busy], Now);

        Assert.Equal(SlotState.Free, StateAt(slots, 9));
        Assert.Equal(SlotState.Booked, StateAt(slots, 10));
        Assert.Equal(SlotState.Booked, StateAt(slots, 11));
        Assert.Equal(SlotState.Free, StateAt(slots, 12));
    }

    [Fact]
    public void Slots_starting_before_not_before_are_past_even_when_booked()
    {
        var notBefore = At(Tomorrow, 12);
        var busy = Hour(Tomorrow, 8);
        var slots = AvailabilityCalculator.ForDay(Court, Club, Tomorrow, [busy], notBefore);

        Assert.Equal(SlotState.Past, StateAt(slots, 8));
        Assert.Equal(SlotState.Past, StateAt(slots, 11));
        Assert.Equal(SlotState.Free, StateAt(slots, 12));
    }

    [Fact]
    public void Closed_beats_booked_and_past_beats_closed()
    {
        var closed = TimeRange.FromStart(At(Tomorrow, 9), TimeSpan.FromHours(3));
        var busy = Hour(Tomorrow, 10);
        var slots = AvailabilityCalculator.ForDay(Court, Club, Tomorrow, [busy], At(Tomorrow, 10), [closed]);

        Assert.Equal(SlotState.Past, StateAt(slots, 9));
        Assert.Equal(SlotState.Closed, StateAt(slots, 10));
        Assert.Equal(SlotState.Closed, StateAt(slots, 11));
        Assert.Equal(SlotState.Free, StateAt(slots, 12));
    }

    [Fact]
    public void Half_hour_slots_follow_the_resource_grid()
    {
        var room = TestData.Court("room-a", slotMinutes: 30, maxSlots: 6);
        var slots = AvailabilityCalculator.ForDay(room, Club, Tomorrow, [], Now);

        Assert.Equal(32, slots.Count);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.Time.Duration));
    }

    [Fact]
    public void The_day_window_covers_the_local_day_in_utc()
    {
        var window = AvailabilityCalculator.DayWindow(Club, Tomorrow);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero), window.Start);
        Assert.Equal(TimeSpan.FromHours(24), window.Duration);
    }

    private static SlotState StateAt(IReadOnlyList<Slot> slots, int localHour) => slots.Single(s => s.LocalStart.Hour == localHour).State;
}

/// <summary>
/// Daylight saving in Europe/London: on 29 March 2026 clocks jump from 01:00 to 02:00, on 25 October 2026 they go
/// back from 02:00 to 01:00. Library: 1.2 "Time zones and DST in practice".
/// </summary>
public sealed class AvailabilityDstTests
{
    private static readonly Venue London = new("London club", "Europe/London");
    private static readonly DateOnly SpringForward = new(2026, 3, 29);
    private static readonly DateOnly FallBack = new(2026, 10, 25);
    private static readonly DateTimeOffset LongAgo = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Resource Open(int fromHour, int toHour) =>
        TestData.Court() with { Hours = EveryDay(new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0)) };

    [Fact]
    public void Spring_forward_day_uses_summer_time_offsets()
    {
        var slots = AvailabilityCalculator.ForDay(Open(6, 22), London, SpringForward, [], LongAgo);

        Assert.Equal(16, slots.Count);
        // 06:00 BST is 05:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 5, 0, 0, TimeSpan.Zero), slots[0].Time.Start);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromHours(1), s.Time.Duration));
    }

    [Fact]
    public void Spring_forward_day_is_23_hours_long()
    {
        Assert.Equal(TimeSpan.FromHours(23), AvailabilityCalculator.DayWindow(London, SpringForward).Duration);
    }

    [Fact]
    public void Spring_forward_never_offers_a_slot_in_the_skipped_hour()
    {
        var slots = AvailabilityCalculator.ForDay(Open(0, 4), London, SpringForward, [], LongAgo);

        Assert.DoesNotContain(slots, s => s.LocalStart.Hour == 1);
        var twoAm = Assert.Single(slots, s => s.LocalStart.Hour == 2);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), twoAm.Time.Start);
        Assert.All(slots, s => Assert.False(London.TimeZone.IsInvalidTime(s.LocalStart)));
    }

    [Fact]
    public void Fall_back_day_uses_winter_time_offsets()
    {
        var slots = AvailabilityCalculator.ForDay(Open(6, 22), London, FallBack, [], LongAgo);

        Assert.Equal(16, slots.Count);
        // 06:00 GMT is 06:00 UTC; the day before, 06:00 BST was 05:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 6, 0, 0, TimeSpan.Zero), slots[0].Time.Start);
        var dayBefore = AvailabilityCalculator.ForDay(Open(6, 22), London, FallBack.AddDays(-1), [], LongAgo);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 5, 0, 0, TimeSpan.Zero), dayBefore[0].Time.Start);
    }

    [Fact]
    public void Fall_back_day_is_25_hours_long()
    {
        Assert.Equal(TimeSpan.FromHours(25), AvailabilityCalculator.DayWindow(London, FallBack).Duration);
    }

    [Fact]
    public void Fall_back_repeated_hour_gives_one_slot_and_no_overlaps()
    {
        var slots = AvailabilityCalculator.ForDay(Open(0, 4), London, FallBack, [], LongAgo);

        Assert.Single(slots, s => s.LocalStart.Hour == 1);
        for (var i = 1; i < slots.Count; i++)
        {
            Assert.False(slots[i - 1].Time.Overlaps(slots[i].Time), $"{slots[i - 1].Time} overlaps {slots[i].Time}");
            Assert.True(slots[i - 1].Time.End <= slots[i].Time.Start);
        }
    }

    [Fact]
    public void A_booking_in_the_repeated_hour_blocks_only_its_own_slot()
    {
        // 01:00 local is ambiguous; it resolves to standard time, 01:00 UTC.
        var busy = TimeRange.FromStart(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(1));
        var slots = AvailabilityCalculator.ForDay(Open(0, 4), London, FallBack, [busy], LongAgo);

        Assert.Equal(SlotState.Booked, slots.Single(s => s.LocalStart.Hour == 1).State);
        Assert.Equal(SlotState.Free, slots.Single(s => s.LocalStart.Hour == 2).State);
    }
}
