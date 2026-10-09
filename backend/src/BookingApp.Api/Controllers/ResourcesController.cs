using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

[Route("api/resources")]
public sealed class ResourcesController(VenueService venue, BookingService bookings) : ApiControllerBase
{
    /// <summary>Courts and rooms, with opening hours.</summary>
    [HttpGet]
    public ActionResult<List<ResourceResponse>> List() => venue.Resources.Select(ResourceResponse.From).ToList();

    /// <summary>A resource's slots for one venue-local day (yyyy-MM-dd); defaults to today at the venue.</summary>
    [HttpGet("{id}/availability")]
    public async Task<ActionResult<AvailabilityResponse>> Availability(string id, DateOnly? date, CancellationToken ct)
    {
        var day = date ?? venue.Today();
        var result = await bookings.AvailabilityAsync(id, day, ct);
        return result.IsSuccess
            ? new AvailabilityResponse(id, day, venue.Venue.TimeZoneId, [.. result.Value!.Select(SlotResponse.From)])
            : Failure(result.Error!);
    }
}
