using System.Security.Claims;
using System.Threading.RateLimiting;
using BookingApp.Api.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingApp.Api;

/// <summary>The "RateLimits" settings. Per minute; generous for a person, tight for a script.</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Every /api request, per account (signed in) or per IP (anonymous).</summary>
    public int RequestsPerMinute { get; set; } = 300;

    /// <summary>Booking writes per signed-in account.</summary>
    public int WritesPerMinutePerAccount { get; set; } = 20;

    /// <summary>Booking writes per IP for anonymous callers (a shared Wi-Fi is several people, so more than one account).</summary>
    public int WritesPerMinutePerIp { get; set; } = 60;

    /// <summary>Sign-up and sign-in attempts per IP (lockout also stops guessing per account).</summary>
    public int SignInsPerMinutePerIp { get; set; } = 10;
}

/// <summary>
/// Hardening that is not a feature: antiforgery for cookie-authenticated writes, security headers, rate limits per
/// account, and request size limits. Library: 10.4 "Cross-site request forgery", "Misconfiguration and headers",
/// "Baseline hardening", "Rate limiting (.NET 7+, built in)"; 12.5 "Rate limiting".
/// </summary>
public static class BookingSecurity
{
    /// <summary>The cookie the SPA reads (it is not HttpOnly) and echoes in <see cref="HeaderName"/> on every write.</summary>
    public const string TokenCookie = "XSRF-TOKEN";

    public const string HeaderName = "X-XSRF-TOKEN";

    public static WebApplicationBuilder AddBookingSecurity(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.AddServerHeader = false;                       // do not advertise the server
            o.Limits.MaxRequestBodySize = 1024 * 1024;       // JSON bodies here are small; the webhook caps itself lower
        });

        // Antiforgery (the "synchronizer token" pattern for an SPA): the cookie the attacker's page cannot read decides.
        // A cross-site page can make the browser send our cookies, but it cannot read XSRF-TOKEN to put it in a header.
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = HeaderName;
            o.Cookie.Name = "bk-antiforgery";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var limits = builder.Configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Every API request, per caller: a scraper or a runaway client cannot take the whole API.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => http.Request.Path.StartsWithSegments("/api")
                ? RateLimitPartition.GetTokenBucketLimiter(Caller(http), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.RequestsPerMinute,
                    TokensPerPeriod = limits.RequestsPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter("not-api"));
            // Writes: per account when signed in. Anonymous callers are limited per IP, not per customer cookie: the
            // client sends that cookie, so a script could mint a fresh one, and a fresh allowance, with every request.
            o.AddPolicy(BookingsController.WritesPolicy, http => CurrentCustomer.AccountId(http) is { } account
                ? Window($"account:{account}", limits.WritesPerMinutePerAccount)
                : Window($"ip:{Ip(http)}", limits.WritesPerMinutePerIp));
            o.AddPolicy(AccountController.SignInPolicy, http => Window($"ip:{Ip(http)}", limits.SignInsPerMinutePerIp));
            o.OnRejected = async (context, token) =>
            {
                var http = context.HttpContext;
                // Rate-limit hits are an attack signal: log them (who, where), never the body.
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BookingApp.RateLimit")
                    .LogWarning("Rate limit hit by {Caller} on {Method} {Path}", Caller(http), http.Request.Method, http.Request.Path);
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var problem = http.RequestServices.GetRequiredService<ProblemDetailsFactory>()
                    .CreateProblemDetails(http, StatusCodes.Status429TooManyRequests, "Too many requests. Wait a moment and try again.");
                problem.Extensions["code"] = "rate_limited";
                await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", token);
            };
        });
        return builder;
    }

    /// <summary>
    /// Security headers on every response (library 10.4 "Baseline hardening"). API responses get the strictest
    /// policy: they are data, never a page to run. Anything else (the API reference in Development, the web app once the
    /// image serves it) may load its own scripts and styles, and nothing from elsewhere.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        http.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            if (http.Request.Path.StartsWithSegments("/api"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                headers.CacheControl = "no-store";   // bookings and accounts are personal: no copies in caches
            }
            else if (!http.Request.Path.StartsWithSegments("/scalar"))
            {
                headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            }
            return Task.CompletedTask;
        });
        await next();
    });

    /// <summary>
    /// Gives the caller a fresh antiforgery token for whoever is signed in on <paramref name="user"/> (tokens are bound to
    /// the user, so sign-in and sign-out must issue a new one).
    /// </summary>
    public static void IssueAntiforgeryToken(HttpContext http, ClaimsPrincipal? user = null)
    {
        if (user is not null) http.User = user;
        var tokens = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        http.Response.Cookies.Append(TokenCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,                 // the SPA must read it: that is the point
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            IsEssential = true,
            Path = "/",
        });
    }

    private static string Caller(HttpContext http) => CurrentCustomer.AccountId(http) is { } account ? $"account:{account}" : $"ip:{Ip(http)}";

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> Window(string key, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}

/// <summary>
/// Every write (POST, PUT, PATCH, DELETE) to a controller needs a valid antiforgery token, unless the action or its
/// controller says [IgnoreAntiforgeryToken]. Refused like any other error: problem details with a stable code, so the
/// SPA can fetch a fresh token and try once more. (MVC's own AutoValidateAntiforgeryToken needs the Razor view
/// services, which an API does not register.)
/// </summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>().Any()) return;
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            var problem = http.RequestServices.GetRequiredService<ProblemDetailsFactory>().CreateProblemDetails(http, StatusCodes.Status400BadRequest,
                "This request did not come with a valid antiforgery token. Reload the page and try again.");
            problem.Extensions["code"] = "antiforgery_failed";
            context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { "application/problem+json" } };
        }
    }
}
