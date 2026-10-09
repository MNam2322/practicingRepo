using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BookingApp.Api;

/// <summary>
/// Input that fails binding (an unparseable date, malformed JSON) is the client's mistake: answer 400 with the
/// reason, and don't log it as a server error.
/// </summary>
internal sealed class BadRequestHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public const string Title = "The request could not be read.";

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad) return false;
        http.Response.StatusCode = bad.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Title = Title, Detail = bad.Message, Status = bad.StatusCode },
        });
    }

    /// <summary>
    /// [ApiController] answers model binding failures itself, before the action runs. Keep the same title as above,
    /// and list the fields that could not be read.
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest, title: Title);
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}
