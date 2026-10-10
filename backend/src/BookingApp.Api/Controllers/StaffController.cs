using BookingApp.Core.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

/// <summary>Staff tools: the day schedule and what happens to bookings on the day. Staff and Admin only.</summary>
[Route("api/staff")]
[Authorize(Policy = Policies.ViewSchedule)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson, Description = "unauthenticated")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson, Description = "forbidden")]
public sealed class StaffController(StaffService staff, Core.Payments.DepositService deposits, Core.Scheduling.VenueService venue) : ApiControllerBase
{
    /// <summary>Every resource's bookings (any status) and closures for one venue-local day, with the actions allowed on each booking.</summary>
    /// <param name="date">A venue-local day (yyyy-MM-dd). Leave it out for today at the venue.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    [HttpGet("schedule")]
    [EndpointName("GetSchedule")]
    [ProducesResponseType<ScheduleResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    public async Task<ActionResult<ScheduleResponse>> Schedule(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? staff.Today((await venue.CatalogAsync(ct)).Venue);
        return ScheduleResponse.From(await staff.ScheduleAsync(day, ct), staff.Now);
    }

    /// <summary>Records that a booking took place. Only once it has started.</summary>
    /// <param name="id">The booking id.</param>
    [HttpPost("bookings/{id:guid}/complete")]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("CompleteBooking")]
    [ProducesResponseType<StaffBookingResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "booking_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "invalid_transition, changed_meanwhile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "not_started")]
    public Task<ActionResult<StaffBookingResponse>> Complete(Guid id, CancellationToken ct) => Respond(staff.CompleteAsync(id, ct), ct);

    /// <summary>Records that nobody came. Only once it has started.</summary>
    /// <param name="id">The booking id.</param>
    [HttpPost("bookings/{id:guid}/no-show")]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("MarkNoShow")]
    [ProducesResponseType<StaffBookingResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "booking_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "invalid_transition, changed_meanwhile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "not_started")]
    public Task<ActionResult<StaffBookingResponse>> NoShow(Guid id, CancellationToken ct) => Respond(staff.MarkNoShowAsync(id, ct), ct);

    /// <summary>Cancels any booking that has not ended, with a reason the customer sees. Frees the slot and refunds the deposit.</summary>
    /// <param name="id">The booking id.</param>
    [HttpPost("bookings/{id:guid}/cancel")]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("StaffCancelBooking")]
    [Consumes(Json)]
    [ProducesResponseType<StaffBookingResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed (errors.reason)")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "booking_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "invalid_transition, changed_meanwhile")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "already_ended, reason_required")]
    public async Task<ActionResult<StaffBookingResponse>> Cancel(Guid id, StaffCancelRequest request, CancellationToken ct)
    {
        var response = await Respond(staff.CancelAsync(id, request.Reason!, ct), ct);
        // The club cancelled: the customer's deposit goes back (the refund job retries if the provider is down).
        if (response.Value is not null) await deposits.SettleAsync(id, ct);
        return response;
    }

    /// <summary>Blocks time on a resource (maintenance, an event) so customers cannot book it.</summary>
    [HttpPost("closures")]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("BlockTime")]
    [Consumes(Json)]
    [ProducesResponseType<ClosureResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "bookings_in_the_way")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "end_before_start, reason_required")]
    public async Task<ActionResult<ClosureResponse>> Block(BlockTimeRequest request, CancellationToken ct)
    {
        var result = await staff.BlockAsync(request.ResourceId!, request.Start!.Value, request.End!.Value, request.Reason!, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var body = ClosureResponse.From(result.Value!, (await venue.CatalogAsync(ct)).Venue);
        return Created($"/api/staff/schedule?date={body.LocalStart[..10]}", body);
    }

    private async Task<ActionResult<StaffBookingResponse>> Respond(Task<Core.Common.Result<Core.Bookings.Booking>> change, CancellationToken ct)
    {
        var result = await change;
        if (!result.IsSuccess) return Failure(result.Error!);
        return StaffBookingResponse.From(result.Value!, (await venue.CatalogAsync(ct)).Venue, staff.Now);
    }
}
