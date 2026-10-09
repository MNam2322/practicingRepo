using BookingApp.Core.Common;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

/// <summary>
/// Base for every API controller. Controllers stay thin: bind the request, call a service, map the result to HTTP.
/// They call services (BookingService, VenueService) and never touch a repository. Library: 8.3 "Endpoints: controllers", 12.1 "Layering without ceremony".
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Turns a domain <see cref="Error"/> into an RFC 9457 problem response with a stable "code".</summary>
    protected ObjectResult Failure(Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: status, title: error.Message);
        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
