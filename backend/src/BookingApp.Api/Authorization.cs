using BookingApp.Core.Accounts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace BookingApp.Api;

/// <summary>
/// Capabilities, not job titles: code asks for a policy; which roles grant it is decided here, once.
/// Library: 10.2 "Authorization policies that scale".
/// </summary>
public static class Policies
{
    /// <summary>See and manage every booking on the day schedule (staff features).</summary>
    public const string ViewSchedule = "ViewSchedule";

    /// <summary>Change resources, opening hours, closures and booking rules (admin features).</summary>
    public const string ManageVenue = "ManageVenue";
}

internal static class AuthorizationSetup
{
    public const string AuthCookieName = "bk-auth";

    public static IServiceCollection AddBookingAuth(this IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            // Library 10.2 "Staying signed in": HttpOnly so scripts cannot read it, SameSite=Lax against cross-site
            // posts, Secure whenever the request is HTTPS, a sliding two-week lifetime.
            o.Cookie.Name = AuthCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            // An API answers 401/403 with problem details; it never redirects to a login page.
            o.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => WriteProblem(context.HttpContext, StatusCodes.Status401Unauthorized, "unauthenticated", "Sign in to do that."),
                OnRedirectToAccessDenied = context => WriteProblem(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "Your account is not allowed to do that."),
            };
        });

        // Deny by default (library 10.2 "Example 2: Deny by default") is a global [Authorize] filter on every controller
        // (Program.cs, AddControllers): an action needs a signed-in user unless it says [AllowAnonymous], so a new
        // endpoint can never be public by accident. A fallback policy would also turn every unknown URL into a 401.
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ViewSchedule, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.ManageVenue, p => p.RequireRole(Roles.Admin));
        return services;
    }

    private static async Task WriteProblem(HttpContext http, int status, string code, string title)
    {
        http.Response.StatusCode = status;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = { Status = status, Title = title, Extensions = { ["code"] = code } },
        });
    }
}
