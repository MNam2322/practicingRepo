using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using BookingApp.Core.Admin;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api.Controllers;

/// <summary>
/// Venue set-up: resources, opening hours, closures and the booking rules. Admin only. Every change is recorded in the
/// audit log with the admin who made it.
/// </summary>
[Route("api/admin")]
[Authorize(Policy = Policies.ManageVenue)]
[EnableRateLimiting(BookingsController.WritesPolicy)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson, Description = "unauthenticated")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, ProblemJson, Description = "forbidden")]
public sealed class AdminController(AdminService admin, VenueService venue) : ApiControllerBase
{
    /// <summary>Adds a court or room. It has no opening hours (so it is closed) until they are set.</summary>
    [HttpPost("resources")]
    [EndpointName("CreateResource")]
    [Consumes(Json)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "resource_exists")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "invalid_resource_id")]
    public async Task<ActionResult<ResourceResponse>> CreateResource(CreateResourceRequest request, CancellationToken ct)
    {
        var draft = new ResourceDraft(request.Id!, request.Name!, request.Kind!.Value, request.Capacity!.Value, request.SlotMinutes!.Value, request.MaxSlotsPerBooking!.Value);
        var result = await admin.CreateResourceAsync(draft, Actor(), ct);
        return result.IsSuccess ? Created($"/api/resources/{result.Value!.Id}/availability", ResourceResponse.From(result.Value)) : Failure(result.Error!);
    }

    /// <summary>Changes a resource's name, kind, capacity, slot length or longest booking.</summary>
    /// <param name="id">The resource id.</param>
    [HttpPut("resources/{id}")]
    [EndpointName("UpdateResource")]
    [Consumes(Json)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "invalid_hours (the hours no longer fit the slot length)")]
    public async Task<ActionResult<ResourceResponse>> UpdateResource(string id, UpdateResourceRequest request, CancellationToken ct)
    {
        var draft = new ResourceDraft(id, request.Name!, request.Kind!.Value, request.Capacity!.Value, request.SlotMinutes!.Value, request.MaxSlotsPerBooking!.Value);
        var result = await admin.UpdateResourceAsync(id, draft, Actor(), ct);
        return result.IsSuccess ? ResourceResponse.From(result.Value!) : Failure(result.Error!);
    }

    /// <summary>Replaces a resource's weekly opening hours. Days left out are closed.</summary>
    /// <param name="id">The resource id.</param>
    [HttpPut("resources/{id}/hours")]
    [EndpointName("SetOpeningHours")]
    [Consumes(Json)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "invalid_hours")]
    public async Task<ActionResult<ResourceResponse>> SetHours(string id, SetHoursRequest request, CancellationToken ct)
    {
        var hours = request.Hours!.Select(h => new OpeningHours(h.Day!.Value, ParseTime(h.Open!), ParseTime(h.Close!))).ToList();
        var result = await admin.SetHoursAsync(id, hours, Actor(), ct);
        return result.IsSuccess ? ResourceResponse.From(result.Value!) : Failure(result.Error!);
    }

    /// <summary>Deletes a resource that has never been booked. Otherwise close it instead.</summary>
    /// <param name="id">The resource id.</param>
    [HttpDelete("resources/{id}")]
    [EndpointName("DeleteResource")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "resource_in_use")]
    public async Task<IActionResult> DeleteResource(string id, CancellationToken ct)
    {
        var result = await admin.DeleteResourceAsync(id, Actor(), ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    /// <summary>Closures that overlap a range of venue-local days, venue-wide ones included.</summary>
    /// <param name="from">First day (yyyy-MM-dd); default today.</param>
    /// <param name="to">Last day, inclusive; default 60 days after from.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    [HttpGet("closures")]
    [EndpointName("ListClosures")]
    [ProducesResponseType<List<ClosureResponse>>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<List<ClosureResponse>>> Closures(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var catalog = await venue.CatalogAsync(ct);
        var first = from ?? venue.Today(catalog.Venue);
        var last = to ?? first.AddDays(60);
        var window = TimeRange.Create(AvailabilityCalculator.DayWindow(catalog.Venue, first).Start, AvailabilityCalculator.DayWindow(catalog.Venue, last < first ? first : last).End);
        return (await admin.ClosuresAsync(window, ct)).Select(c => ClosureResponse.From(c, catalog.Venue)).ToList();
    }

    /// <summary>Closes one resource, or every resource (leave out resourceId), for whole days, such as a holiday.</summary>
    [HttpPost("closures")]
    [EndpointName("AddClosure")]
    [Consumes(Json)]
    [ProducesResponseType<ClosureResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson, Description = "bookings_in_the_way")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "end_before_start, reason_required")]
    public async Task<ActionResult<ClosureResponse>> AddClosure(AdminClosureRequest request, CancellationToken ct)
    {
        var resourceId = string.IsNullOrWhiteSpace(request.ResourceId) ? null : request.ResourceId;
        var result = await admin.AddClosureAsync(resourceId, request.From!.Value, request.To!.Value, request.Reason!, Actor(), ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var body = ClosureResponse.From(result.Value!, (await venue.CatalogAsync(ct)).Venue);
        return Created($"/api/admin/closures?from={body.LocalStart[..10]}", body);
    }

    /// <summary>Removes a closure: that time can be booked again.</summary>
    /// <param name="id">The closure id.</param>
    [HttpDelete("closures/{id:guid}")]
    [EndpointName("DeleteClosure")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "closure_not_found")]
    public async Task<IActionResult> DeleteClosure(Guid id, CancellationToken ct)
    {
        var result = await admin.DeleteClosureAsync(id, Actor(), ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    /// <summary>The booking rules.</summary>
    [HttpGet("policy")]
    [EndpointName("GetPolicy")]
    [ProducesResponseType<PolicyResponse>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<PolicyResponse>> Policy(CancellationToken ct) => PolicyResponse.From((await venue.CatalogAsync(ct)).Policy);

    /// <summary>Changes the booking rules. They apply to new bookings and cancellations from now on.</summary>
    [HttpPut("policy")]
    [EndpointName("UpdatePolicy")]
    [Consumes(Json)]
    [ProducesResponseType<PolicyResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed")]
    public async Task<ActionResult<PolicyResponse>> UpdatePolicy(PolicyRequest request, CancellationToken ct)
    {
        var policy = new BookingPolicy
        {
            MaxDaysAhead = request.MaxDaysAhead!.Value,
            MinLeadMinutes = request.MinLeadMinutes!.Value,
            CancellationCutoffHours = request.CancellationCutoffHours!.Value,
            MaxActiveBookingsPerCustomer = request.MaxActiveBookingsPerCustomer!.Value,
        };
        var result = await admin.UpdatePolicyAsync(policy, Actor(), ct);
        return result.IsSuccess ? PolicyResponse.From(result.Value!) : Failure(result.Error!);
    }

    /// <summary>The audit log, newest first: who changed what, and when.</summary>
    /// <param name="take">How many entries (1 to 200; default 50).</param>
    /// <param name="before">Only entries before this instant, to page back.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    [HttpGet("audit")]
    [EndpointName("GetAuditLog")]
    [ProducesResponseType<List<AuditEntryResponse>>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<List<AuditEntryResponse>>> Audit(int? take, DateTimeOffset? before, CancellationToken ct) =>
        (await admin.AuditAsync(take ?? 50, before, ct)).Select(AuditEntryResponse.From).ToList();

    /// <summary>The signed-in admin, and this request's trace id to match the audit entry with the server logs.</summary>
    private Actor Actor() => new(
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
        User.Identity?.Name ?? "",
        Activity.Current?.Id ?? HttpContext.TraceIdentifier);

    private static TimeOnly ParseTime(string hhmm) => TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);
}
