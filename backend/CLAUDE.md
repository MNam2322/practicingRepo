# Backend: Booking API (.NET 10)

This is the C# half of the repo. Read the root `../CLAUDE.md` first: it explains how to use the library in `../library` before designing anything. The roadmap is in `TASKS.md`.

## Architecture

```
BookingApp.slnx
src/BookingApp.Core            domain + use cases. Base class library only: no ASP.NET Core, no EF Core, no I/O.
  Common/Result.cs             Result<T> and Error(kind, code, message): expected failures are results, not exceptions
  Scheduling/TimeRange.cs      half-open UTC range [Start, End); Overlaps
  Scheduling/Resource.cs       Venue (IANA time zone), Resource, OpeningHours, IResourceCatalog port
  Scheduling/Availability.cs   AvailabilityCalculator: pure, DST-safe slots for a local day
  Bookings/Booking.cs          Booking aggregate + BookingStatus + CustomerId
  Bookings/BookingPolicy.cs    rules as settings (lead time, days ahead, cutoff, max active) + Check()
  Bookings/BookingService.cs   use cases + IBookingStore port
src/BookingApp.Infrastructure  adapters: ConfigurationResourceCatalog, InMemoryBookingStore, DemoSeeder, DI
src/BookingApp.Api             HTTP: Program.cs (pipeline), Endpoints.cs, Contracts.cs, Customers.cs (cookie identity, problem mapping)
http/booking.http              one request per endpoint
```

Dependencies point inwards: Api → Core and Infrastructure; Infrastructure → Core; Core → nothing.

## Rules that must always hold

1. **No two blocking bookings for the same resource overlap.** This must be guaranteed by the store in one atomic step: a lock, a database constraint or a transaction. A "check, then insert" in application code is a race. The in-memory store does it under a lock; a database store must use a constraint or a serialisable transaction. There must be a test that fires many simultaneous requests at one slot and sees exactly one succeed.
2. **Times are UTC everywhere except at the edges.** Store and compare `DateTimeOffset` in UTC. Venue-local time is used only for opening hours, the slot grid and display. Never use `DateTime.Now`; use `TimeProvider`.
3. **State changes go through the aggregate.** `Booking` methods enforce the lifecycle. Endpoints never set fields directly.
4. **Errors are problem details with a stable `code`:**
   - 404 not found
   - 409 conflict, such as `slot_taken`
   - 422 rule broken, such as `too_late_to_cancel`
   - 403 not yours
   - 400 unreadable input

   The frontend switches on `code`.

## Conventions

- Use file-scoped namespaces, `sealed` classes, and records for contracts and value objects.
- Return `TypedResults` from endpoints, with `Results<...>` return types.
- Every async path takes a `CancellationToken`. Log with `[LoggerMessage]`.
- Options are bound from configuration and validated on start. Booking rules live in `appsettings.json` under `Booking`.
- NuGet versions go in `Directory.Packages.props` (central package management), never in a `.csproj`.
- Warnings are errors. Keep `dotnet format --verify-no-changes` clean.

## Commands (run from `backend/`)

- `dotnet build`
- `dotnet test` (from task 1)
- `dotnet format --verify-no-changes`
- `dotnet run --project src/BookingApp.Api`: http://localhost:5080. In Development it seeds demo bookings for tomorrow.
