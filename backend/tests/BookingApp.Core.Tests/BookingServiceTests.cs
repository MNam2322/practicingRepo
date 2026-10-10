using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Repositories;
using Microsoft.Extensions.Time.Testing;
using static BookingApp.Core.Tests.TestData;

namespace BookingApp.Core.Tests;

/// <summary>The use cases with a fake clock and the real in-memory store. Library: 4.6 "TimeProvider — testing time-dependent code".</summary>
public sealed class BookingServiceTests
{
    private static readonly CustomerId Alice = CustomerId.New();
    private static readonly CustomerId Bob = CustomerId.New();

    private readonly FakeTimeProvider _time = new(Now);
    private readonly BookingPolicy _policy = new() { MaxDaysAhead = 30, MinLeadMinutes = 15, CancellationCutoffHours = 2, MaxActiveBookingsPerCustomer = 3 };
    private readonly TestResources _resources = new(Court("court-1"), Court("court-2"));
    private readonly BookingService _service;

    public BookingServiceTests()
    {
        _resources.Policy = _policy;
        _service = new BookingService(_resources, new InMemoryBookingRepository(), new HoldOptions { Minutes = 10 }, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<Booking>> Book(CustomerId who, int hour, int slots = 1, string resource = "court-1") =>
        _service.BookAsync(who, "Alice", null, resource, At(Tomorrow, hour), slots, Ct);

    [Fact]
    public async Task Booking_a_free_slot_succeeds_and_shows_as_booked()
    {
        var result = await Book(Alice, 10, slots: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromHours(2), result.Value!.Time.Duration);
        var slots = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!.Slots;
        Assert.Equal(SlotState.Booked, slots.Single(s => s.LocalStart.Hour == 10).State);
        Assert.Equal(SlotState.Booked, slots.Single(s => s.LocalStart.Hour == 11).State);
        Assert.Equal(SlotState.Free, slots.Single(s => s.LocalStart.Hour == 12).State);
    }

    [Fact]
    public async Task An_overlapping_booking_is_slot_taken()
    {
        await Book(Alice, 10, slots: 2);
        var result = await Book(Bob, 11);
        Assert.Equal("slot_taken", result.Error?.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task Touching_bookings_are_both_allowed()
    {
        Assert.True((await Book(Alice, 10)).IsSuccess);
        Assert.True((await Book(Bob, 11)).IsSuccess);
        Assert.True((await Book(Bob, 9)).IsSuccess);
    }

    [Fact]
    public async Task The_same_time_on_another_resource_is_allowed()
    {
        await Book(Alice, 10);
        Assert.True((await Book(Bob, 10, resource: "court-2")).IsSuccess);
    }

    [Fact]
    public async Task A_cancelled_slot_can_be_booked_again()
    {
        var first = (await Book(Alice, 10)).Value!;
        Assert.True((await _service.CancelAsync(Alice, first.Id, Ct)).IsSuccess);
        Assert.True((await Book(Bob, 10)).IsSuccess);
    }

    [Fact]
    public async Task An_unknown_resource_is_not_found()
    {
        Assert.Equal("resource_not_found", (await Book(Alice, 10, resource: "nope")).Error?.Code);
        Assert.Equal("resource_not_found", (await _service.AvailabilityAsync("nope", Tomorrow, Ct)).Error?.Code);
    }

    [Fact]
    public async Task A_blank_name_is_required()
    {
        var result = await _service.BookAsync(Alice, "  ", null, "court-1", At(Tomorrow, 10), 1, Ct);
        Assert.Equal("name_required", result.Error?.Code);
    }

    [Fact]
    public async Task Zero_slots_is_not_whole_slots()
    {
        Assert.Equal("not_whole_slots", (await Book(Alice, 10, slots: 0)).Error?.Code);
    }

    [Fact]
    public async Task Policy_rules_are_applied()
    {
        Assert.Equal("too_soon", (await _service.BookAsync(Alice, "Alice", null, "court-1", Now.AddMinutes(5), 1, Ct)).Error?.Code);
        Assert.Equal("too_long", (await Book(Alice, 10, slots: 3)).Error?.Code);
    }

    [Fact]
    public async Task A_customer_cannot_hold_more_than_the_maximum_active_bookings()
    {
        for (var hour = 8; hour < 11; hour++) Assert.True((await Book(Alice, hour)).IsSuccess);

        var result = await Book(Alice, 12);
        Assert.Equal("too_many_bookings", result.Error?.Code);
        Assert.True((await Book(Bob, 12)).IsSuccess);
    }

    [Fact]
    public async Task Past_and_cancelled_bookings_do_not_count_towards_the_maximum()
    {
        var cancelled = (await Book(Alice, 8)).Value!;
        await _service.CancelAsync(Alice, cancelled.Id, Ct);
        await Book(Alice, 9);
        await Book(Alice, 10);
        Assert.True((await Book(Alice, 11)).IsSuccess);
    }

    [Fact]
    public async Task Cancelling_close_to_the_start_is_too_late()
    {
        var booking = (await Book(Alice, 10)).Value!;
        Assert.True((await _service.ConfirmAsync(Alice, booking.Id, Ct)).IsSuccess);
        _time.SetUtcNow(booking.Time.Start.AddHours(-1));

        var result = await _service.CancelAsync(Alice, booking.Id, Ct);
        Assert.Equal("too_late_to_cancel", result.Error?.Code);
    }

    [Fact]
    public async Task Cancelling_someone_elses_booking_is_forbidden()
    {
        var booking = (await Book(Alice, 10)).Value!;
        Assert.Equal("not_your_booking", (await _service.CancelAsync(Bob, booking.Id, Ct)).Error?.Code);
    }

    [Fact]
    public async Task Cancelling_an_unknown_booking_is_not_found()
    {
        Assert.Equal("booking_not_found", (await _service.CancelAsync(Alice, Guid.NewGuid(), Ct)).Error?.Code);
    }

    [Fact]
    public async Task Customers_only_see_their_own_bookings()
    {
        var booking = (await Book(Alice, 10)).Value!;
        await Book(Bob, 12);

        Assert.True((await _service.GetAsync(Alice, booking.Id, Ct)).IsSuccess);
        Assert.Equal("booking_not_found", (await _service.GetAsync(Bob, booking.Id, Ct)).Error?.Code);
        Assert.Single(await _service.MineAsync(Alice, Ct));
    }

    [Fact]
    public async Task Slots_inside_the_lead_time_show_as_past()
    {
        _time.SetUtcNow(At(Tomorrow, 9, 50));
        var slots = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!.Slots;
        Assert.Equal(SlotState.Past, slots.Single(s => s.LocalStart.Hour == 10).State);
        Assert.Equal(SlotState.Free, slots.Single(s => s.LocalStart.Hour == 11).State);
    }

    [Fact]
    public async Task Booking_into_a_resource_closure_is_resource_closed()
    {
        await _resources.AddClosureAsync(new Closure(Guid.NewGuid(), "court-1", TimeRange.FromStart(At(Tomorrow, 9), TimeSpan.FromHours(3)), "Resurfacing"), Ct);

        var result = await Book(Alice, 11);
        Assert.Equal("resource_closed", result.Error?.Code);
        Assert.Contains("Resurfacing", result.Error!.Message);
        Assert.True((await Book(Alice, 12)).IsSuccess);
        Assert.True((await Book(Bob, 11, resource: "court-2")).IsSuccess);
    }

    [Fact]
    public async Task A_venue_wide_closure_closes_every_resource()
    {
        await _resources.AddClosureAsync(new Closure(Guid.NewGuid(), null, TimeRange.Create(At(Tomorrow, 0), At(Tomorrow.AddDays(1), 0)), "Public holiday"), Ct);

        Assert.Equal("resource_closed", (await Book(Alice, 10)).Error?.Code);
        Assert.Equal("resource_closed", (await Book(Alice, 10, resource: "court-2")).Error?.Code);
        var day = (await _service.AvailabilityAsync("court-2", Tomorrow, Ct)).Value!;
        Assert.All(day.Slots, s => Assert.Equal(SlotState.Closed, s.State));
        Assert.Equal("Public holiday", Assert.Single(day.Closures).Reason);
    }

    [Fact]
    public async Task Availability_marks_closed_slots_and_lists_the_closure()
    {
        await _resources.AddClosureAsync(new Closure(Guid.NewGuid(), "court-1", TimeRange.FromStart(At(Tomorrow, 9), TimeSpan.FromHours(2)), "Coaching"), Ct);
        await Book(Bob, 12);

        var day = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!;
        Assert.Equal(SlotState.Free, day.Slots.Single(s => s.LocalStart.Hour == 8).State);
        Assert.Equal(SlotState.Closed, day.Slots.Single(s => s.LocalStart.Hour == 9).State);
        Assert.Equal(SlotState.Closed, day.Slots.Single(s => s.LocalStart.Hour == 10).State);
        Assert.Equal(SlotState.Free, day.Slots.Single(s => s.LocalStart.Hour == 11).State);
        Assert.Equal(SlotState.Booked, day.Slots.Single(s => s.LocalStart.Hour == 12).State);
        Assert.Equal("Coaching", Assert.Single(day.Closures).Reason);
    }

    [Fact]
    public async Task Booking_makes_a_hold_that_blocks_until_confirmed()
    {
        var hold = (await Book(Alice, 10)).Value!;
        Assert.Equal(BookingStatus.Pending, hold.Status);
        Assert.Equal(Now.AddMinutes(10), hold.HoldExpiresAt);
        Assert.Equal("slot_taken", (await Book(Bob, 10)).Error?.Code);

        var confirmed = await _service.ConfirmAsync(Alice, hold.Id, Ct);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Value!.Status);
        Assert.Equal("not_your_booking", (await _service.ConfirmAsync(Bob, hold.Id, Ct)).Error?.Code);
    }

    [Fact]
    public async Task An_expired_hold_frees_the_slot_even_before_the_job_runs()
    {
        var hold = (await Book(Alice, 10)).Value!;
        _time.Advance(TimeSpan.FromMinutes(11));

        var slots = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!.Slots;
        Assert.Equal(SlotState.Free, slots.Single(s => s.LocalStart.Hour == 10).State);
        Assert.True((await Book(Bob, 10)).IsSuccess);
        Assert.Equal("hold_expired", (await _service.ConfirmAsync(Alice, hold.Id, Ct)).Error?.Code);
    }

    [Fact]
    public async Task The_expiry_job_releases_due_holds_only()
    {
        await Book(Alice, 10);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Book(Alice, 12);
        _time.Advance(TimeSpan.FromMinutes(6));   // the first is 11 minutes old, the second 6

        Assert.Equal(1, await _service.ExpireDueHoldsAsync(Ct));
        Assert.Equal(0, await _service.ExpireDueHoldsAsync(Ct));   // safe to run twice
        var mine = await _service.MineAsync(Alice, Ct);
        Assert.Equal([BookingStatus.Expired, BookingStatus.Pending], mine.Select(b => b.Status));
    }
}
