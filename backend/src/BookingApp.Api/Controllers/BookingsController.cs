using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

/// <summary>The current customer's bookings. The customer is identified by the bk-customer cookie, issued on first call.</summary>
[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings, VenueService venue, TimeProvider time) : ApiControllerBase
{
    /// <summary>Rate limit for writes, per customer cookie (Program.cs).</summary>
    public const string WritesPolicy = "booking-writes";

    /// <summary>Books one or more consecutive slots.</summary>
    /// <remarks>Send a slot's start exactly as the availability response gave it. Someone may book the same slot first: then the answer is 409 slot_taken.</remarks>
    [HttpPost]
    [EnableRateLimiting(WritesPolicy)]
    [EndpointName("CreateBooking")]
    [Consumes(Json)]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "The request body could not be read.")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "slot_taken")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson,
        Description = "too_soon, too_far_ahead, not_whole_slots, too_long, outside_opening_hours, not_on_slot_grid, name_required, too_many_bookings")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.BookAsync(customer.Id, request.Name ?? "", request.ResourceId ?? "", request.Start, request.Slots ?? 1, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var body = ToResponse(result.Value!);
        return Created($"/api/bookings/{body.Id}", body);
    }

    /// <summary>The current customer's bookings, oldest first.</summary>
    [HttpGet("mine")]
    [EndpointName("GetMyBookings")]
    [ProducesResponseType<List<BookingResponse>>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<List<BookingResponse>>> Mine(CurrentCustomer customer, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var list = await bookings.MineAsync(customer.Id, ct);
        return list.Select(b => BookingResponse.From(b, venue, now)).ToList();
    }

    /// <summary>One of the current customer's bookings.</summary>
    /// <param name="id">The booking id.</param>
    [HttpGet("{id:guid}")]
    [EndpointName("GetBooking")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "booking_not_found")]
    public async Task<ActionResult<BookingResponse>> Get(Guid id, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.GetAsync(customer.Id, id, ct);
        return result.IsSuccess ? ToResponse(result.Value!) : Failure(result.Error!);
    }

    /// <summary>Cancels a booking while the cancellation rules allow it.</summary>
    /// <param name="id">The booking id.</param>
    [HttpPost("{id:guid}/cancel")]
    [EnableRateLimiting(WritesPolicy)]
    [EndpointName("CancelBooking")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson, Description = "not_your_booking")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "booking_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "not_cancellable, changed_meanwhile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "too_late_to_cancel")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BookingResponse>> Cancel(Guid id, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await bookings.CancelAsync(customer.Id, id, ct);
        return result.IsSuccess ? ToResponse(result.Value!) : Failure(result.Error!);
    }

    private BookingResponse ToResponse(Booking booking) => BookingResponse.From(booking, venue, time.GetUtcNow());
}
