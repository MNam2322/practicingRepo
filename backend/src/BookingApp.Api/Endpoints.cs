using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BookingApp.Api;

public static class Endpoints
{
    public const string BookingWritesPolicy = "booking-writes";

    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/venue", (IResourceCatalog catalog, BookingPolicy policy) =>
            TypedResults.Ok(new VenueResponse(catalog.Venue.Name, catalog.Venue.TimeZoneId, PolicyResponse.From(policy))))
            .WithTags("Venue");

        api.MapGet("/resources", (IResourceCatalog catalog) =>
            TypedResults.Ok(catalog.Resources.Select(ResourceResponse.From).ToList()))
            .WithTags("Venue");

        // date is a venue-local day (yyyy-MM-dd); defaults to today at the venue.
        api.MapGet("/resources/{id}/availability", async Task<Results<Ok<AvailabilityResponse>, ProblemHttpResult>> (
            string id, DateOnly? date, IResourceCatalog catalog, BookingService bookings, TimeProvider time, CancellationToken ct) =>
        {
            var day = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), catalog.Venue.TimeZone).DateTime);
            var result = await bookings.AvailabilityAsync(id, day, ct);
            return result.IsSuccess
                ? TypedResults.Ok(new AvailabilityResponse(id, day, catalog.Venue.TimeZoneId, [.. result.Value!.Select(SlotResponse.From)]))
                : Problems.From(result.Error!);
        }).WithTags("Availability");

        var mine = api.MapGroup("/bookings").WithTags("Bookings");

        mine.MapPost("/", async Task<Results<Created<BookingResponse>, ProblemHttpResult>> (
            CreateBookingRequest request, CurrentCustomer customer, BookingService bookings, IResourceCatalog catalog,
            BookingPolicy policy, TimeProvider time, CancellationToken ct) =>
        {
            var result = await bookings.BookAsync(customer.Id, request.Name ?? "", request.ResourceId ?? "", request.Start, request.Slots ?? 1, ct);
            if (!result.IsSuccess) return Problems.From(result.Error!);
            var body = BookingResponse.From(result.Value!, catalog, policy, time.GetUtcNow());
            return TypedResults.Created($"/api/bookings/{body.Id}", body);
        }).RequireRateLimiting(BookingWritesPolicy);

        mine.MapGet("/mine", async (CurrentCustomer customer, BookingService bookings, IResourceCatalog catalog, BookingPolicy policy, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            var list = await bookings.MineAsync(customer.Id, ct);
            return TypedResults.Ok(list.Select(b => BookingResponse.From(b, catalog, policy, now)).ToList());
        });

        mine.MapGet("/{id:guid}", async Task<Results<Ok<BookingResponse>, ProblemHttpResult>> (
            Guid id, CurrentCustomer customer, BookingService bookings, IResourceCatalog catalog, BookingPolicy policy, TimeProvider time, CancellationToken ct) =>
        {
            var result = await bookings.GetAsync(customer.Id, id, ct);
            return result.IsSuccess ? TypedResults.Ok(BookingResponse.From(result.Value!, catalog, policy, time.GetUtcNow())) : Problems.From(result.Error!);
        });

        mine.MapPost("/{id:guid}/cancel", async Task<Results<Ok<BookingResponse>, ProblemHttpResult>> (
            Guid id, CurrentCustomer customer, BookingService bookings, IResourceCatalog catalog, BookingPolicy policy, TimeProvider time, CancellationToken ct) =>
        {
            var result = await bookings.CancelAsync(customer.Id, id, ct);
            return result.IsSuccess ? TypedResults.Ok(BookingResponse.From(result.Value!, catalog, policy, time.GetUtcNow())) : Problems.From(result.Error!);
        }).RequireRateLimiting(BookingWritesPolicy);

        return app;
    }
}
