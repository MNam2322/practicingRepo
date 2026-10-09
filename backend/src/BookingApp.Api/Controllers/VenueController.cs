using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

[Route("api/venue")]
public sealed class VenueController(VenueService venue) : ApiControllerBase
{
    /// <summary>The venue, its time zone and the booking rules.</summary>
    [HttpGet]
    [EndpointName("GetVenue")]
    [ProducesResponseType<VenueResponse>(StatusCodes.Status200OK, Json)]
    public ActionResult<VenueResponse> Get() =>
        new VenueResponse(venue.Venue.Name, venue.Venue.TimeZoneId, PolicyResponse.From(venue.Policy));
}
