using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BookingApp.Api;

/// <summary>
/// The customer making the request, from the "bk-customer" cookie; a new anonymous id is issued when there is
/// none. Endpoints take it as a parameter and minimal APIs call <see cref="BindAsync"/>.
/// TASKS.md replaces this with real accounts (and keeps anonymous bookings on sign-in).
/// </summary>
public sealed record CurrentCustomer(CustomerId Id)
{
    public const string CookieName = "bk-customer";

    public static ValueTask<CurrentCustomer?> BindAsync(HttpContext context)
    {
        if (CustomerId.TryParse(context.Request.Cookies[CookieName], out var existing))
            return ValueTask.FromResult<CurrentCustomer?>(new CurrentCustomer(existing));

        var id = CustomerId.New();
        context.Response.Cookies.Append(CookieName, id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
        });
        return ValueTask.FromResult<CurrentCustomer?>(new CurrentCustomer(id));
    }
}

/// <summary>Turns a domain <see cref="Error"/> into an RFC 9457 problem response with a stable "code".</summary>
internal static class Problems
{
    public static ProblemHttpResult From(Error error) => TypedResults.Problem(
        title: error.Message,
        statusCode: error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
