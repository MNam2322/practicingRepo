using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

[AllowAnonymous]
[Route("api/venue")]
public sealed class VenueController(VenueService venue) : ApiControllerBase
{
    /// <summary>The venue, its time zone and the booking rules.</summary>
    [HttpGet]
    [EndpointName("GetVenue")]
    [ProducesResponseType<VenueResponse>(StatusCodes.Status200OK, Json)]
    public async Task<ActionResult<VenueResponse>> Get(CancellationToken ct)
    {
        var catalog = await venue.CatalogAsync(ct);
        return new VenueResponse(catalog.Venue.Name, catalog.Venue.TimeZoneId, PolicyResponse.From(catalog.Policy));
    }
}
