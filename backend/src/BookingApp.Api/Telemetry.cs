using BookingApp.Core.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BookingApp.Api;

/// <summary>
/// Traces, metrics and logs through OpenTelemetry, one pipeline (library 11.6 "OpenTelemetry: traces, metrics and logs
/// from one pipeline"): the HTTP request, the app's own spans (book, confirm, cancel …) and every EF Core command land in
/// one trace; logs carry the trace id. Exported over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (for example to the
/// Aspire dashboard: docker compose --profile observability up), and not at all otherwise.
/// </summary>
public static class BookingTelemetry
{
    public static WebApplicationBuilder AddBookingTelemetry(this WebApplicationBuilder builder)
    {
        var export = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "booking-api"))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = http => !http.Request.Path.StartsWithSegments("/health");   // probes are noise
                })
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource(Telemetry.Name);
                if (export) t.AddOtlpExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(Telemetry.Name);
                if (export) m.AddOtlpExporter();
            });
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;
            o.IncludeFormattedMessage = true;
            if (export) o.AddOtlpExporter();
        });
        return builder;
    }

    /// <summary>
    /// /health and /health/live: is the process up? (Never the database: a database blip must not get every instance
    /// restarted.) /health/ready: can it serve, which needs the database. Library 11.6 "Health checks".
    /// </summary>
    public static void MapBookingHealth(this WebApplication app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false };
        app.MapHealthChecks("/health", live);
        app.MapHealthChecks("/health/live", live);
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
    }
}
