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
    public const string InvalidTitle = "Some fields are missing or invalid.";
    public const string Code = "validation_failed";

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad) return false;
        http.Response.StatusCode = bad.StatusCode;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = { Title = Title, Detail = bad.Message, Status = bad.StatusCode, Extensions = { ["code"] = Code } },
        });
    }

    /// <summary>
    /// [ApiController] answers binding and validation failures itself, before the action runs: 400 with an
    /// <c>errors</c> map from field name (as in the JSON, e.g. "name") to messages, and code "validation_failed".
    /// Library: 8.5 "Validation in ASP.NET Core".
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        // A body or value that could not be parsed at all is unreadable; otherwise the fields failed their rules.
        var unreadable = context.ModelState.Values.SelectMany(v => v.Errors).Any(e => e.Exception is not null)
            || context.ModelState.ContainsKey("$");
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest,
            title: unreadable ? Title : InvalidTitle);
        problem.Extensions["code"] = Code;
        // Key errors by the JSON names clients send ("name", not "Name"), so a form can show each next to its input.
        var errors = problem.Errors.ToDictionary(e => JsonFieldName(e.Key), e => e.Value);
        problem.Errors.Clear();
        foreach (var (key, messages) in errors) problem.Errors[key] = messages;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>"Name" → "name", "Items[0].Name" → "items[0].name"; "$" (the whole body) stays as it is.</summary>
    private static string JsonFieldName(string key) =>
        string.Join(".", key.Split(".").Select(part => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(part)));
}
