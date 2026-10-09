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
    private readonly BookingService _service;

    public BookingServiceTests() =>
        _service = new BookingService(new TestResources(Court("court-1"), Court("court-2")), new InMemoryBookingRepository(), _policy, _time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<Booking>> Book(CustomerId who, int hour, int slots = 1, string resource = "court-1") =>
        _service.BookAsync(who, "Alice", resource, At(Tomorrow, hour), slots, Ct);

    [Fact]
    public async Task Booking_a_free_slot_succeeds_and_shows_as_booked()
    {
        var result = await Book(Alice, 10, slots: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromHours(2), result.Value!.Time.Duration);
        var slots = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!;
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
        var result = await _service.BookAsync(Alice, "  ", "court-1", At(Tomorrow, 10), 1, Ct);
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
        Assert.Equal("too_soon", (await _service.BookAsync(Alice, "Alice", "court-1", Now.AddMinutes(5), 1, Ct)).Error?.Code);
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
        var slots = (await _service.AvailabilityAsync("court-1", Tomorrow, Ct)).Value!;
        Assert.Equal(SlotState.Past, slots.Single(s => s.LocalStart.Hour == 10).State);
        Assert.Equal(SlotState.Free, slots.Single(s => s.LocalStart.Hour == 11).State);
    }
}
