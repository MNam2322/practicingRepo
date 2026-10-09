using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings, VenueService venue, TimeProvider time) : ApiControllerBase
{
    /// <summary>Rate limit for writes, per customer cookie (Program.cs).</summary>
    public const string WritesPolicy = "booking-writes";

    /// <summary>Books one or more consecutive slots. 409 slot_taken if someone got there first.</summary>
    [HttpPost]
    [EnableRateLimiting(WritesPolicy)]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.BookAsync(customer.Id, request.Name ?? "", request.ResourceId ?? "", request.Start, request.Slots ?? 1, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var body = ToResponse(result.Value!);
        return Created($"/api/bookings/{body.Id}", body);
    }

    /// <summary>The current customer's bookings, oldest first.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<BookingResponse>>> Mine(CurrentCustomer customer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var list = await bookings.MineAsync(customer.Id, ct);
        return list.Select(b => BookingResponse.From(b, venue, now)).ToList();
    }

    /// <summary>One of the current customer's bookings.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingResponse>> Get(Guid id, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.GetAsync(customer.Id, id, ct);
        return result.IsSuccess ? ToResponse(result.Value!) : Failure(result.Error!);
    }

    /// <summary>Cancels a booking while the cancellation rules allow it.</summary>
    [HttpPost("{id:guid}/cancel")]
    [EnableRateLimiting(WritesPolicy)]
    public async Task<ActionResult<BookingResponse>> Cancel(Guid id, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.CancelAsync(customer.Id, id, ct);
        return result.IsSuccess ? ToResponse(result.Value!) : Failure(result.Error!);
    }

    private BookingResponse ToResponse(Booking booking) => BookingResponse.From(booking, venue, time.GetUtcNow());
}
