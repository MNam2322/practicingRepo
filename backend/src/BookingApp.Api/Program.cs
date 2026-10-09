using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BookingApp.Api;
using BookingApp.Api.Controllers;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---- application and infrastructure ----
builder.Services.AddBookingInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<VenueService>();

// ---- HTTP concerns ----
builder.Services.AddControllers(o =>
    {
        // Request shapes are checked with explicit attributes on the request records (Contracts.cs), not implied by
        // nullability; business rules are checked in Core and answered with 422 and a code.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        // Numbers are numbers: "slots": "2" is a 400, and the contract types them as integers only.
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = BadRequestHandler.InvalidModelState);
// Problem details written outside MVC (status code pages, the exception handler) use the same JSON settings.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddBookingOpenApi();
builder.Services.AddExceptionHandler<BadRequestHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Writes are limited per customer cookie (or IP when there is none): 20 a minute is plenty for a person.
    o.AddPolicy(BookingsController.WritesPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Request.Cookies[CurrentCustomer.CookieName] ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
// The React app calls the API through the Vite proxy in development, so CORS only matters when it is
// served from another origin. Origins come from configuration ("Cors:AllowedOrigins").
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

// Operational routes only; application endpoints are controllers (Controllers/).
app.MapGet("/", () => TypedResults.Ok(new { name = "Booking API", requests = "see backend/http/booking.http", health = "/health", docs = "/scalar (Development)" }))
    .ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapBookingApiDocs();
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
