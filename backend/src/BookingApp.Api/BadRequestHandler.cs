using Microsoft.AspNetCore.Diagnostics;

namespace BookingApp.Api;

/// <summary>
/// Input that fails binding (an unparseable date, malformed JSON) is the client's mistake: answer 400 with the
/// reason, and don't log it as a server error.
/// </summary>
internal sealed class BadRequestHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad) return false;
        http.Response.StatusCode = bad.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Title = "The request could not be read.", Detail = bad.Message, Status = bad.StatusCode },
        });
    }
}
