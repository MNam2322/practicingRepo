# Backend: Booking API (.NET 10)

This is the C# half of the repo. Read the root `../CLAUDE.md` first: it explains how to use the library in `../library` before designing anything. The roadmap is in `TASKS.md`.

## Architecture

The API is layered **controller → service → repository**. Library: 8.3 "Endpoints: controllers", 8.2 "Minimal APIs vs controllers", 12.1 "Layering without ceremony".

```
BookingApp.slnx
src/BookingApp.Core            domain, services and repository ports. Base class library only: no ASP.NET Core, no EF Core, no I/O.
  Common/Result.cs             Result<T> and Error(kind, code, message): expected failures are results, not exceptions
  Scheduling/TimeRange.cs      half-open UTC range [Start, End); Overlaps
  Scheduling/Resource.cs       Venue (IANA time zone), Resource, OpeningHours, Closure, ResourceCatalog, IResourceRepository port
  Scheduling/Availability.cs   AvailabilityCalculator: pure, DST-safe slots for a local day (free, booked, past, closed)
  Scheduling/VenueService.cs   service: venue, resources, policy, the venue's today
  Bookings/Booking.cs          Booking aggregate: the lifecycle is a transition table (status, trigger) -> status, + CustomerId
  Bookings/BookingPolicy.cs    rules as settings (lead time, days ahead, cutoff, max active) + Check()
  Bookings/BookingService.cs   service: the booking use cases
  Bookings/IBookingRepository.cs  repository port; TryAddAsync is the atomic overlap check + insert
  Accounts/Account.cs          Account, Roles (Customer, Staff, Admin)
  Staff/StaffService.cs        service: day schedule, complete / no-show / staff cancel (through the aggregate), block time
  Admin/AdminService.cs        service: resources, hours, closures, booking rules; every change saved with an AuditEntry
  Admin/IVenueAdminRepository.cs  port: each write takes its audit entry and saves both in one transaction
src/BookingApp.Infrastructure  adapters, DemoSeeder, DI (Storage:Provider = InMemory | Sqlite | Postgres)
  Identity/                    AppUser, AccountService (sign up/in/out over ASP.NET Core Identity; moves anonymous bookings)
  Repositories/                EfBookingRepository, EfResourceRepository (Sqlite, Postgres); InMemoryBookingRepository, ConfigurationResourceRepository (InMemory)
  Persistence/                 BookingDbContext (shared model: tables, keys, FKs, CHECKs) with SqliteBookingDbContext and
                               PostgresBookingDbContext (tstzrange period + EXCLUDE constraint), BookingRow/ResourceRows, StorageOptions,
                               DatabaseInitializer (migrate, WAL, first-run seed of venue and resources from the Venue settings),
                               Migrations/Sqlite and Migrations/Postgres (generated per provider; read every one before committing)
src/BookingApp.Api             HTTP: Program.cs (pipeline), Authorization.cs (auth cookie, policies), Controllers/ (ApiControllerBase,
                               Venue, Resources, Bookings, Account, Staff, Admin),
                               Contracts.cs (request/response records), Customers.cs (cookie identity + model binder)
tests/                         Core.Tests (domain, services), Infrastructure.Tests (repository contract on every provider),
                               Api.Tests (WebApplicationFactory; booking and concurrency tests run on InMemory and Sqlite)
http/booking.http              one request per route
openapi/v1.json                the API contract, generated from the controllers; a test fails if it drifts
```

Dependencies point inwards: Api → Core and Infrastructure; Infrastructure → Core; Core → nothing.

- **Controllers** (`Api/Controllers/`) bind the request, call one or more **services**, and map the result to HTTP. They never touch a repository or a `DbContext`, and never hold business rules.
- **Services** (`Core`, e.g. `BookingService`, `VenueService`) hold the use cases. They call the domain and the **repositories** through the ports in Core.
- **Repositories** (`Infrastructure/Repositories/`) implement the ports: storage only, no business rules except the atomic overlap guarantee below.

## Rules that must always hold

1. **No two blocking bookings for the same resource overlap.** This must be guaranteed by the booking repository in one atomic step: a lock, a database constraint or a transaction. A "check, then insert" in application code is a race. The in-memory repository does it under a lock; `EfBookingRepository` on SQLite runs the check and the insert in one `BEGIN IMMEDIATE` transaction; on PostgreSQL it does no check at all and the `ex_bookings_no_overlap` exclusion constraint refuses the insert (SQLSTATE 23P01 → `slot_taken`); a database repository must use a constraint or a serialisable transaction. There must be a test that fires many simultaneous requests at one slot and sees exactly one succeed.
2. **Times are UTC everywhere except at the edges.** Store and compare `DateTimeOffset` in UTC. Venue-local time is used only for opening hours, the slot grid and display. Never use `DateTime.Now`; use `TimeProvider`.
3. **State changes go through the aggregate.** `Booking` methods enforce the lifecycle through its transition table; anything not in the table is refused (`invalid_transition`). Adding a status or trigger means adding rows, and the exhaustive test in `BookingStateMachineTests` covers every pair. Controllers and services never set fields directly.
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
- **Authorization:** every controller action needs a signed-in user (a global `[Authorize]` filter) unless it says `[AllowAnonymous]`. Staff and admin actions use a capability policy, `[Authorize(Policy = Policies.ViewSchedule)]` or `Policies.ManageVenue`; roles map to policies only in `Authorization.cs`. Never check roles in code. `CurrentCustomer` is the account id when signed in, else the anonymous cookie id.
- Every action has `[EndpointName]`, an XML `<summary>`, and `[ProducesResponseType]` for its success (`Json`) and each error (`ProblemJson`, with the codes in `Description`).
- New use cases go in a service in Core; new storage goes behind a repository interface in Core with its implementation in `Infrastructure/Repositories/`. Name them `XxxService`, `IXxxRepository`, `XxxRepository`.
- Use file-scoped namespaces, `sealed` classes, and records for contracts and value objects.
- Every async path takes a `CancellationToken`. Log with `[LoggerMessage]`.
- Options are bound from configuration and validated on start. Booking rules live in `appsettings.json` under `Booking`. The `Venue` and `Booking` sections only seed the database on first run; after that the venue, its booking rules (`ResourceCatalog.Policy`), resources, opening hours and closures live in the database and admins change them.
- **Idempotency:** a POST that must be safe to retry gets `[Idempotent]` (Api/Idempotent.cs): an `Idempotency-Key` header is claimed atomically per caller in `idempotency_keys`, the response is stored before it is sent, and a retry gets it back with `Idempotency-Replayed: true`. Known limit: the stored response is not in the same transaction as the change; a claim abandoned by a crash can be taken over after 2 minutes.
- **Audit:** every admin change is written with an `AuditEntry` (actor = the signed-in account, request id, before/after JSON) in the same transaction. `audit_log` is append-only in the database (triggers on SQLite and PostgreSQL).
- Rules any writer must obey are database constraints (keys, foreign keys, CHECKs, NOT NULL) as well as code. Migrations must upgrade a database that already has data: test the upgrade path, not just an empty database.
- NuGet versions go in `Directory.Packages.props` (central package management), never in a `.csproj`.
- Warnings are errors. Keep `dotnet format --verify-no-changes` clean.

## Commands (run from `backend/`)

- `dotnet build`
- `dotnet test` (from task 1). After an intended API contract change: `UPDATE_OPENAPI=1 dotnet test`, then commit `openapi/v1.json`.
- `dotnet format --verify-no-changes`
- New migration (after changing the model), once per provider (`dotnet tool restore` first):
  - `dotnet ef migrations add <Name> --context SqliteBookingDbContext --project src/BookingApp.Infrastructure --startup-project src/BookingApp.Infrastructure --output-dir Persistence/Migrations/Sqlite`
  - the same with `--context PostgresBookingDbContext` and `--output-dir Persistence/Migrations/Postgres`
  - read both generated files before committing; things EF Core cannot express (like `EXCLUDE`) go in as `migrationBuilder.Sql`.
- PostgreSQL: `docker compose up -d` (repo root), then `dotnet run --project src/BookingApp.Api --launch-profile http-postgres`. Tests start their own PostgreSQL with Testcontainers; without Docker those tests are skipped, and `REQUIRE_DOCKER=1 dotnet test` makes that a failure (use it in CI).
- `dotnet run --project src/BookingApp.Api`: http://localhost:5080. Bookings are stored in `src/BookingApp.Api/.data/bookings.db` (SQLite, git-ignored); delete that folder to start empty. In Development it seeds demo bookings for tomorrow, and serves the API reference at http://localhost:5080/scalar and the document at `/openapi/v1.json`.
