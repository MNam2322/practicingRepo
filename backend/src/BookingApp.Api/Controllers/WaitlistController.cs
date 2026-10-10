using BookingApp.Core.Scheduling;
using BookingApp.Core.Waitlist;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

/// <summary>The current customer's waitlist entries for taken times.</summary>
[AllowAnonymous]
[Route("api/waitlist")]
public sealed class WaitlistController(WaitlistService waitlist, VenueService venue) : ApiControllerBase
{
    /// <summary>Joins the waitlist for a taken time. When it frees up, the first in line is emailed.</summary>
    /// <remarks>
    /// The time must be one that could be booked, but is taken now (409 slot_free if it is free: book it). Emails go to
    /// "email", or the signed-in account's address. When the time frees up, the first person in line is told (first come,
    /// first served); if it is still free 30 minutes later, the next person is told too.
    /// </remarks>
    /// <param name="request">The taken time.</param>
    /// <param name="customer">Bound from the auth or customer cookie.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    [HttpPost]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("JoinWaitlist")]
    [Consumes(Json)]
    [ProducesResponseType<WaitlistEntryResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "slot_free, already_waiting, already_yours")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson,
        Description = "email_required, name_required, too_soon, too_far_ahead, outside_opening_hours, not_on_slot_grid, too_long, resource_closed")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<WaitlistEntryResponse>> Join(JoinWaitlistRequest request, CurrentCustomer customer, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? customer.AccountEmail : request.Email;
        var result = await waitlist.JoinAsync(customer.Id, request.Name!, email, request.ResourceId!, request.Start!.Value, request.Slots ?? 1, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var body = WaitlistEntryResponse.From(result.Value!, await venue.CatalogAsync(ct));
        return Created("/api/waitlist/mine", body);
    }

    /// <summary>The current customer's waitlist entries for times still to come, with their places in line.</summary>
    [HttpGet("mine")]
    [EndpointName("GetMyWaitlist")]
    [ProducesResponseType<List<WaitlistEntryResponse>>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<List<WaitlistEntryResponse>>> Mine(CurrentCustomer customer, CancellationToken ct)
    {
        var catalog = await venue.CatalogAsync(ct);
        return (await waitlist.MineAsync(customer.Id, ct)).Select(p => WaitlistEntryResponse.From(p, catalog)).ToList();
    }

    /// <summary>Leaves the waitlist for a time.</summary>
    /// <param name="id">The waitlist entry id.</param>
    [HttpPost("{id:guid}/leave")]
    [EnableRateLimiting(BookingsController.WritesPolicy)]
    [EndpointName("LeaveWaitlist")]
    [ProducesResponseType<WaitlistEntryResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "waitlist_entry_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "changed_meanwhile")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<WaitlistEntryResponse>> Leave(Guid id, CurrentCustomer customer, CancellationToken ct)
    {
        var result = await waitlist.LeaveAsync(customer.Id, id, ct);
        return result.IsSuccess ? WaitlistEntryResponse.From(result.Value!, await venue.CatalogAsync(ct)) : Failure(result.Error!);
    }
}
