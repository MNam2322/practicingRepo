using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

/// <summary>Where the web app gets its antiforgery token before its first write (and again after an antiforgery_failed).</summary>
[AllowAnonymous]
[Route("api/antiforgery")]
public sealed class AntiforgeryController : ApiControllerBase
{
    /// <summary>Sets the XSRF-TOKEN cookie; send its value in the X-XSRF-TOKEN header on every write.</summary>
    /// <remarks>
    /// Every POST, PUT, PATCH and DELETE (except the payment provider's signed webhook) needs it: a page on another site
    /// can make the browser send this site's cookies, but cannot read this one to copy it into a header. The token
    /// belongs to whoever is signed in; signing in, up or out returns a new one.
    /// </remarks>
    [HttpGet]
    [EndpointName("GetAntiforgeryToken")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Get()
    {
        BookingSecurity.IssueAntiforgeryToken(HttpContext);
        return NoContent();
    }
}
