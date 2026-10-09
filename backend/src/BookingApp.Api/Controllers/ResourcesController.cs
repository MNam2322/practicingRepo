using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

[Route("api/resources")]
public sealed class ResourcesController(VenueService venue, BookingService bookings) : ApiControllerBase
{
    /// <summary>Courts and rooms, with opening hours.</summary>
    [HttpGet]
    [EndpointName("GetResources")]
    [ProducesResponseType<List<ResourceResponse>>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<List<ResourceResponse>>> List(CancellationToken ct) =>
        (await venue.CatalogAsync(ct)).Resources.Select(ResourceResponse.From).ToList();

    /// <summary>A resource's slots for one venue-local day.</summary>
    /// <param name="id">The resource id, such as court-1.</param>
    /// <param name="date">A venue-local day (yyyy-MM-dd). Leave it out for today at the venue.</param>
    /// <param name="ct">Cancelled when the client goes away.</param>
    [HttpGet("{id}/availability")]
    [EndpointName("GetAvailability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson, Description = "validation_failed: the date could not be read")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson, Description = "resource_not_found")]
    public async Task<ActionResult<AvailabilityResponse>> Availability(string id, DateOnly? date, CancellationToken ct)
    {
        var day = date ?? venue.Today((await venue.CatalogAsync(ct)).Venue);
        var result = await bookings.AvailabilityAsync(id, day, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        var (place, slots, closures) = result.Value!;
        return new AvailabilityResponse(id, day, place.TimeZoneId, [.. slots.Select(SlotResponse.From)], [.. closures.Select(c => ClosureResponse.From(c, place))]);
    }
}
