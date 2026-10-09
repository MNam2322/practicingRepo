# Backend: Booking API (.NET 10)

This is the C# half of the repo. Read the root `../CLAUDE.md` first: it explains how to use the library in `../library` before designing anything. The roadmap is in `TASKS.md`.

## Architecture

The API is layered **controller → service → repository**. Library: 8.3 "Endpoints: controllers", 8.2 "Minimal APIs vs controllers", 12.1 "Layering without ceremony".

```
BookingApp.slnx
src/BookingApp.Core            domain, services and repository ports. Base class library only: no ASP.NET Core, no EF Core, no I/O.
  Common/Result.cs             Result<T> and Error(kind, code, message): expected failures are results, not exceptions
  Scheduling/TimeRange.cs      half-open UTC range [Start, End); Overlaps
  Scheduling/Resource.cs       Venue (IANA time zone), Resource, OpeningHours, IResourceRepository port
  Scheduling/Availability.cs   AvailabilityCalculator: pure, DST-safe slots for a local day
  Scheduling/VenueService.cs   service: venue, resources, policy, the venue's today
  Bookings/Booking.cs          Booking aggregate + BookingStatus + CustomerId
  Bookings/BookingPolicy.cs    rules as settings (lead time, days ahead, cutoff, max active) + Check()
  Bookings/BookingService.cs   service: the booking use cases
  Bookings/IBookingRepository.cs  repository port; TryAddAsync is the atomic overlap check + insert
src/BookingApp.Infrastructure  adapters: Repositories/ (ConfigurationResourceRepository, InMemoryBookingRepository), DemoSeeder, DI
src/BookingApp.Api             HTTP: Program.cs (pipeline), Controllers/ (ApiControllerBase, Venue, Resources, Bookings),
                               Contracts.cs (request/response records), Customers.cs (cookie identity + model binder)
http/booking.http              one request per route
openapi/v1.json                the API contract, generated from the controllers; a test fails if it drifts
```

Dependencies point inwards: Api → Core and Infrastructure; Infrastructure → Core; Core → nothing.

- **Controllers** (`Api/Controllers/`) bind the request, call one or more **services**, and map the result to HTTP. They never touch a repository or a `DbContext`, and never hold business rules.
- **Services** (`Core`, e.g. `BookingService`, `VenueService`) hold the use cases. They call the domain and the **repositories** through the ports in Core.
- **Repositories** (`Infrastructure/Repositories/`) implement the ports: storage only, no business rules except the atomic overlap guarantee below.

## Rules that must always hold

1. **No two blocking bookings for the same resource overlap.** This must be guaranteed by the booking repository in one atomic step: a lock, a database constraint or a transaction. A "check, then insert" in application code is a race. The in-memory repository does it under a lock; a database repository must use a constraint or a serialisable transaction. There must be a test that fires many simultaneous requests at one slot and sees exactly one succeed.
2. **Times are UTC everywhere except at the edges.** Store and compare `DateTimeOffset` in UTC. Venue-local time is used only for opening hours, the slot grid and display. Never use `DateTime.Now`; use `TimeProvider`.
3. **State changes go through the aggregate.** `Booking` methods enforce the lifecycle. Controllers and services never set fields directly.
4. **Errors are problem details with a stable `code`:**
   - 404 not found
   - 409 conflict, such as `slot_taken`
   - 422 rule broken, such as `too_late_to_cancel`
   - 403 not yours
   - 400 unreadable or invalid input: code `validation_failed`, with an `errors` map keyed by JSON field name

   The frontend switches on `code`. Controllers return `Failure(error)` from `ApiControllerBase` for a domain `Error`.

## Conventions

- **Never use minimal APIs for application endpoints.** Every application route is an action on an `[ApiController]` controller that derives from `ApiControllerBase`, mapped with `MapControllers()`. Only operational routes (`/health`, the `/` info route) stay in `Program.cs`.
- Actions return `ActionResult<T>` (or `Task<ActionResult<T>>`), use attribute routes (`[Route]`, `[HttpGet]`, …), and put rate limits on writes with `[EnableRateLimiting]`.
- Request bodies are records with init properties and DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`) for shape only; `[ApiController]` answers 400 before the action runs. Business rules stay in Core (409/422 with a code). Library: 8.5.
- Every action has `[EndpointName]`, an XML `<summary>`, and `[ProducesResponseType]` for its success (`Json`) and each error (`ProblemJson`, with the codes in `Description`).
- New use cases go in a service in Core; new storage goes behind a repository interface in Core with its implementation in `Infrastructure/Repositories/`. Name them `XxxService`, `IXxxRepository`, `XxxRepository`.
- Use file-scoped namespaces, `sealed` classes, and records for contracts and value objects.
- Every async path takes a `CancellationToken`. Log with `[LoggerMessage]`.
- Options are bound from configuration and validated on start. Booking rules live in `appsettings.json` under `Booking`.
- NuGet versions go in `Directory.Packages.props` (central package management), never in a `.csproj`.
- Warnings are errors. Keep `dotnet format --verify-no-changes` clean.

## Commands (run from `backend/`)

- `dotnet build`
- `dotnet test` (from task 1). After an intended API contract change: `UPDATE_OPENAPI=1 dotnet test`, then commit `openapi/v1.json`.
- `dotnet format --verify-no-changes`
- `dotnet run --project src/BookingApp.Api`: http://localhost:5080. In Development it seeds demo bookings for tomorrow, and serves the API reference at http://localhost:5080/scalar and the document at `/openapi/v1.json`.
