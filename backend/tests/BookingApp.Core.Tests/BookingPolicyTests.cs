using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

public sealed class BookingPolicyTests
{
    private readonly BookingPolicy _policy = new() { MaxDaysAhead = 30, MinLeadMinutes = 15, CancellationCutoffHours = 2, MaxActiveBookingsPerCustomer = 5 };
    private readonly Resource _court = Court();

    private string? Check(TimeRange time) => _policy.Check(_court, Club, time, Now)?.Code;

    [Fact]
    public void A_whole_slot_inside_opening_hours_is_allowed() => Assert.Null(Check(Hour(Tomorrow, 10)));

    [Fact]
    public void Starting_within_the_lead_time_is_too_soon() =>
        Assert.Equal("too_soon", Check(TimeRange.FromStart(Now.AddMinutes(10), TimeSpan.FromHours(1))));

    [Fact]
    public void Starting_in_the_past_is_too_soon() =>
        Assert.Equal("too_soon", Check(TimeRange.FromStart(Now.AddHours(-2), TimeSpan.FromHours(1))));

    [Fact]
    public void Starting_beyond_max_days_ahead_is_too_far_ahead() => Assert.Equal("too_far_ahead", Check(Hour(Tomorrow.AddDays(31), 10)));

    [Fact]
    public void Exactly_max_days_ahead_is_allowed() => Assert.Null(Check(TimeRange.FromStart(Now.AddDays(30), TimeSpan.FromHours(1))));

    [Fact]
    public void A_part_slot_is_not_whole_slots() =>
        Assert.Equal("not_whole_slots", Check(TimeRange.FromStart(At(Tomorrow, 10), TimeSpan.FromMinutes(90))));

    [Fact]
    public void More_slots_than_the_resource_allows_is_too_long() =>
        Assert.Equal("too_long", Check(TimeRange.FromStart(At(Tomorrow, 10), TimeSpan.FromHours(3))));

    [Fact]
    public void Starting_before_opening_is_outside_opening_hours() => Assert.Equal("outside_opening_hours", Check(Hour(Tomorrow, 5)));

    [Fact]
    public void Ending_after_closing_is_outside_opening_hours() =>
        Assert.Equal("outside_opening_hours", Check(TimeRange.FromStart(At(Tomorrow, 21), TimeSpan.FromHours(2))));

    [Fact]
    public void A_closed_day_is_outside_opening_hours()
    {
        var mondaysOnly = _court with { Hours = [new OpeningHours(DayOfWeek.Monday, new TimeOnly(6, 0), new TimeOnly(22, 0))] };
        // Tomorrow is a Tuesday.
        Assert.Equal("outside_opening_hours", _policy.Check(mondaysOnly, Club, Hour(Tomorrow, 10), Now)?.Code);
    }

    [Fact]
    public void Starting_off_the_slot_grid_is_refused() =>
        Assert.Equal("not_on_slot_grid", Check(TimeRange.FromStart(At(Tomorrow, 10, 30), TimeSpan.FromHours(1))));

    [Fact]
    public void The_grid_is_measured_from_opening_time()
    {
        var halfPast = _court with { Hours = EveryDay(new TimeOnly(6, 30), new TimeOnly(21, 30)) };
        Assert.Null(_policy.Check(halfPast, Club, TimeRange.FromStart(At(Tomorrow, 10, 30), TimeSpan.FromHours(1)), Now));
        Assert.Equal("not_on_slot_grid", _policy.Check(halfPast, Club, Hour(Tomorrow, 10), Now)?.Code);
    }
}
