using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingApp.Core.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;

namespace BookingApp.Api;

/// <summary>
/// Makes a POST safe to retry: a client sends an <c>Idempotency-Key</c> header, generated once per operation and reused
/// on every retry of it. The first request with a key runs; later ones get the same response again (with
/// <c>Idempotency-Replayed: true</c>) instead of running twice. The same key with a different body is 422
/// idempotency_key_reused; the same key while the first is still running is 409 request_in_progress.
/// Without the header nothing changes. Library: 8.2 "Idempotent HTTP APIs and safe retries", 12.5.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";
    public const int MaxKeyLength = 100;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (key.Length == 0)
        {
            await next();
            return;
        }
        if (key.Length > MaxKeyLength || key.Any(c => c < 0x21 || c > 0x7E))
        {
            context.Result = Problem(http, StatusCodes.Status400BadRequest, "invalid_idempotency_key",
                $"The {Header} header must be 1 to {MaxKeyLength} visible ASCII characters, such as a GUID.");
            return;
        }

        // Keys are per caller: the signed-in account or the anonymous customer cookie (both bound as CurrentCustomer).
        var scope = context.ActionArguments.Values.OfType<CurrentCustomer>().FirstOrDefault()?.Id.ToString() ?? "anonymous";
        var services = http.RequestServices;
        var store = services.GetRequiredService<IIdempotencyStore>();
        var json = services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var ct = http.RequestAborted;

        var claim = await store.TryClaimAsync(scope, key, Fingerprint(context, json), now, ct);
        switch (claim.Outcome)
        {
            case ClaimOutcome.Completed:
                context.Result = Replay(http, claim.Response!);
                return;
            case ClaimOutcome.InProgress:
                context.Result = Problem(http, StatusCodes.Status409Conflict, "request_in_progress",
                    "A request with this Idempotency-Key is still being handled. Try again in a moment.");
                return;
            case ClaimOutcome.PayloadDiffers:
                context.Result = Problem(http, StatusCodes.Status422UnprocessableEntity, "idempotency_key_reused",
                    "This Idempotency-Key was already used for a different request. Use a new key for a new booking.");
                return;
        }

        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            // Nothing reliable to remember: let a retry try again.
            await store.ReleaseAsync(scope, key, CancellationToken.None);
            return;
        }
        if (executed.Result is not ObjectResult result || (result.StatusCode ?? 200) >= 500)
        {
            await store.ReleaseAsync(scope, key, CancellationToken.None);
            return;
        }

        // Store before responding, so a retry after a lost response finds it (library 8.2).
        var stored = new StoredResponse(
            result.StatusCode ?? StatusCodes.Status200OK,
            JsonSerializer.Serialize(result.Value, result.Value?.GetType() ?? typeof(object), json),
            result.Value is ProblemDetails ? "application/problem+json" : "application/json",
            (result as CreatedResult)?.Location);
        await store.CompleteAsync(scope, key, stored, CancellationToken.None);
    }

    /// <summary>The request's method, route and body: the same key may only be reused for the same request.</summary>
    private static string Fingerprint(ActionExecutingContext context, JsonSerializerOptions json)
    {
        var body = context.ActionArguments
            .Where(a => a.Value is not CurrentCustomer and not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => $"{a.Key}={JsonSerializer.Serialize(a.Value, a.Value?.GetType() ?? typeof(object), json)}");
        var text = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}\n{string.Join("\n", body)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static ContentResult Replay(HttpContext http, StoredResponse response)
    {
        http.Response.Headers[ReplayedHeader] = "true";
        if (response.Location is not null) http.Response.Headers.Location = response.Location;
        return new ContentResult { StatusCode = response.StatusCode, Content = response.Body, ContentType = response.ContentType };
    }

    private static ObjectResult Problem(HttpContext http, int status, string code, string title)
    {
        var problem = http.RequestServices.GetRequiredService<ProblemDetailsFactory>().CreateProblemDetails(http, status, title);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
