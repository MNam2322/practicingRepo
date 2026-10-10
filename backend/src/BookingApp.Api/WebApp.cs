using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace BookingApp.Api;

/// <summary>
/// What running as one deployable needs (the Docker image, library 11.3): the built web app served from wwwroot, Data
/// Protection keys that survive a restart, and client addresses from a trusted proxy only.
/// </summary>
public static class WebApp
{
    public static WebApplicationBuilder AddBookingHosting(this WebApplicationBuilder builder)
    {
        // Cookies (sign-in) and antiforgery tokens are protected with these keys. Kept in a volume so a new container
        // still reads the cookies the last one issued; without a path (development) the default per-user folder is used.
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName("booking-app");
        if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keys)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keys));

        // Behind a reverse proxy the client's address arrives in X-Forwarded-For. That header is client input unless it
        // comes from a proxy we know (library 6.7 "Rate limits bypassed with a forged header"): only listed proxies.
        var proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var proxy in proxies) o.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        return builder;
    }

    /// <summary>Before anything that reads the client address or scheme (rate limits, HTTPS, logs).</summary>
    public static void UseBookingForwardedHeaders(this WebApplication app)
    {
        if (app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() is { Length: > 0 })
            app.UseForwardedHeaders();
    }

    /// <summary>
    /// The web app (wwwroot, put there by the Docker build). Hashed files under /assets never change, so browsers keep
    /// them for a year; index.html is asked for again every time, so a new release shows up at once. Any other path that is
    /// not the API gets index.html (the app routes after the #). An unknown /api path stays a 404 problem.
    /// </summary>
    public static void MapBookingWebApp(this WebApplication app)
    {
        if (!Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
        {
            // Development: Vite serves the web app, so / says what this is instead.
            app.MapGet("/", () => TypedResults.Ok(new { name = "Booking API", requests = "see backend/http/booking.http", health = "/health/live, /health/ready", docs = "/scalar (Development)" }))
                .ExcludeFromDescription();
            return;
        }
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                headers[HeaderNames.CacheControl] = context.Context.Request.Path.StartsWithSegments("/assets")
                    ? "public, max-age=31536000, immutable"
                    : "no-cache";
            },
        });
        app.MapFallback("/api/{**path}", (HttpContext http) =>
        {
            var problem = http.RequestServices.GetRequiredService<ProblemDetailsFactory>()
                .CreateProblemDetails(http, StatusCodes.Status404NotFound, "There is no such API endpoint.");
            problem.Extensions["code"] = "not_found";
            return Results.Problem(problem);
        });
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
        });
    }
}
