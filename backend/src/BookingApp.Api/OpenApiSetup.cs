using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace BookingApp.Api;

/// <summary>
/// The OpenAPI document (/openapi/v1.json) and the Scalar reference page (/scalar), in Development only.
/// The document is the contract: it is committed as backend/openapi/v1.json and a test fails when it drifts.
/// Library: 8.2 "Describing the contract", "OpenAPI, versioning and contracts".
/// </summary>
internal static class OpenApiSetup
{
    public static IServiceCollection AddBookingOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Booking API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "Courts and rooms at the club: availability, booking and cancelling. Times named start/end are UTC instants; " +
                    "localStart/localEnd are venue wall-clock times for display. Errors are RFC 9457 problem details with a stable `code`.";
                // The server URL depends on where the API runs; leave it out so the committed document is stable.
                document.Servers = [];
                return Task.CompletedTask;
            });
            o.AddSchemaTransformer((schema, context, _) =>
            {
                // Every problem response from this API carries a stable machine-readable code (Controllers/ApiControllerBase.cs).
                if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)) || context.JsonTypeInfo.Type.IsAssignableTo(typeof(HttpValidationProblemDetails)))
                {
                    schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                    schema.Properties["code"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Stable error code such as slot_taken or too_late_to_cancel. Clients switch on this, not on title.",
                    };
                }
                return Task.CompletedTask;
            });
        });

    public static WebApplication MapBookingApiDocs(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;
        app.MapOpenApi();
        app.MapScalarApiReference(o => o.WithTitle("Booking API"));
        return app;
    }
}
