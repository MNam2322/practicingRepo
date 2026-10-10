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
  Bookings/SeriesService.cs    service: weekly series (each date booked on its own, per-date results; confirm/cancel per date)
  Bookings/IBookingRepository.cs  repository port; TryAddAsync is the atomic overlap check + insert
  Bookings/IOccupancyReader.cs    port for availability's read of what occupies a resource (cached in Infrastructure)
  Bookings/HoldOptions.cs      Holds settings: how long a hold lasts (Minutes), how often expired holds are swept
  Payments/                    Money (minor units + currency exponent), Payment (deposit lifecycle), DepositService
                               (confirm takes the deposit, refunds, webhook events), ports IPaymentGateway,
                               IPaymentWebhookReader, IPaymentRepository; PaymentOptions (Payments: Currency, Deposit)
  Notifications/               OutboxMessage + NotificationPlan (booking events -> emails, reminder 24 h before), ports
                               INotificationOutbox and IEmailSender, NotificationService (the relay: send, back off, dead-letter)
                               and EmailTemplates
  Waitlist/                    WaitlistEntry (in line for one taken time), IWaitlistRepository, WaitlistService (join,
                               place in line, leave, offer freed times)
  Accounts/Account.cs          Account, Roles (Customer, Staff, Admin)
  Staff/StaffService.cs        service: day schedule, complete / no-show / staff cancel (through the aggregate), block time
  Admin/AdminService.cs        service: resources, hours, closures, booking rules; every change saved with an AuditEntry
  Admin/IVenueAdminRepository.cs  port: each write takes its audit entry and saves both in one transaction
src/BookingApp.Infrastructure  adapters, DemoSeeder, DI (Storage:Provider = InMemory | Sqlite | Postgres)
  Identity/                    AppUser, AccountService (sign up/in/out over ASP.NET Core Identity; moves anonymous bookings)
  Caching/                     OccupancyVersions + CachedOccupancyReader: availability's occupancy through HybridCache,
                               versioned keys bumped by the booking repositories on every write
  Notifications/               FileEmailSender (.eml files, idempotent by message id), NotificationRelay and WaitlistSweep
                               (background services)
  Payments/                    the fake provider: FakePaymentGateway (deterministic by token, idempotent per payment id),
                               FakePaymentWebhookReader (HMAC signature, 5-minute window), RefundService (retries refunds)
  Repositories/                EfBookingRepository, EfResourceRepository, EfPaymentRepository (Sqlite, Postgres); InMemoryBookingRepository,
                               ConfigurationResourceRepository, InMemoryPaymentRepository (InMemory)
  Persistence/                 BookingDbContext (shared model: tables, keys, FKs, CHECKs) with SqliteBookingDbContext and
                               PostgresBookingDbContext (tstzrange period + EXCLUDE constraint), BookingRow/ResourceRows, StorageOptions,
                               DatabaseInitializer (migrate, WAL, first-run seed of venue and resources from the Venue settings),
                               HoldExpiryService and IdempotencyKeyCleaner (background jobs on TimeProvider),
                               Migrations/Sqlite and Migrations/Postgres (generated per provider; read every one before committing)
src/BookingApp.Api             HTTP: Program.cs (pipeline), Authorization.cs (auth cookie, policies), Controllers/ (ApiControllerBase,
                               Venue, Resources, Bookings, Series, Waitlist, PaymentWebhook, Account, Staff, Admin),
                               Contracts.cs (request/response records), Customers.cs (cookie identity + model binder)
tests/                         Core.Tests (domain, services), Infrastructure.Tests (repository contract on every provider),
                               Api.Tests (WebApplicationFactory; API tests run on InMemory, Sqlite and Postgres)
http/booking.http              one request per route
tools/dev-secrets.mjs          puts this machine's development secrets into dotnet user-secrets (run once)
tools/payment-webhook.mjs      sends a signed webhook as the fake provider (node tools/payment-webhook.mjs <type> <paymentId>)
tools/BookingApp.Perf          seeds a year of bookings into PostgreSQL and measures the hot queries (plans, timings)
perf/booking-race.js           k6 load test: many customers race for the same slots; exactly one wins each
docs/performance.md            the measurements, what was changed and why, and how to reproduce them
openapi/v1.json                the API contract, generated from the controllers; a test fails if it drifts
```

Dependencies point inwards: Api → Core and Infrastructure; Infrastructure → Core; Core → nothing. `ArchitectureTests` (Api.Tests) enforce it: Core's project has no references and its types use only the BCL, Infrastructure never uses Api, controllers use no storage port, repository, DbContext or EF Core, every `/api` endpoint is a controller action, and nothing reads `DateTime.Now`/`UtcNow`. Why it is built this way: `../docs/adr/` (one record per big choice; add one when you make a new one).

- **Controllers** (`Api/Controllers/`) bind the request, call one or more **services**, and map the result to HTTP. They never touch a repository or a `DbContext`, and never hold business rules.
- **Services** (`Core`, e.g. `BookingService`, `VenueService`) hold the use cases. They call the domain and the **repositories** through the ports in Core.
- **Repositories** (`Infrastructure/Repositories/`) implement the ports: storage only, no business rules except the atomic overlap guarantee below.

## Rules that must always hold

1. **No two blocking bookings for the same resource overlap.** Blocking means confirmed, or a hold (`Pending`) that has not expired. This must be guaranteed by the booking repository in one atomic step: a lock, a database constraint or a transaction. A "check, then insert" in application code is a race. The in-memory repository does it under a lock; `EfBookingRepository` on SQLite runs the check and the insert in one `BEGIN IMMEDIATE` transaction; on PostgreSQL it does no check at all and the `ex_bookings_no_overlap` exclusion constraint refuses the insert (SQLSTATE 23P01 → `slot_taken`); a database repository must use a constraint or a serialisable transaction. There must be a test that fires many simultaneous requests at one slot and sees exactly one succeed.
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
- **Holds:** `POST /api/bookings` makes a hold (`Pending`, `holdExpiresAt` = now + Holds:Minutes) that blocks the slot; `POST /api/bookings/{id}/confirm` confirms it (idempotent; 409 `hold_expired` once it ran out). A hold stops blocking the moment it expires, without waiting for any job: reads check `BlocksAt(now)` / `StatusAt(now)`, and every insert first expires due holds for its resource in the same transaction (one conditional UPDATE). `HoldExpiryService` sweeps every Holds:SweepSeconds so the stored status catches up; a confirm racing it loses on the version check. Staff and demo bookings are created confirmed (`Booking.Create`).
- **Security:** every write to a controller needs the antiforgery token (`AntiforgeryFilter`, Security.cs): the API sets the JS-readable `XSRF-TOKEN` cookie (`GET /api/antiforgery`, and again on sign-in, sign-up and sign-out, because tokens belong to the signed-in user) and the client echoes it in `X-XSRF-TOKEN`; failures are 400 `antiforgery_failed`. Only server-to-server endpoints with their own proof (the signed payment webhook) take `[IgnoreAntiforgeryToken]`. Security headers on every response (strict CSP and no-store on /api), no Server header, 1 MB body limit, HSTS outside Development. Rate limits (RateLimits section): every /api request per caller, writes per account (signed in) or per IP (anonymous: the customer cookie is client-sent, so never a partition key), sign-ins per IP; 429s are problem details with Retry-After and are logged. Secrets never in the repository: Development reads them from dotnet user-secrets (`node tools/dev-secrets.mjs`), elsewhere from environment variables.
- **Observability:** OpenTelemetry (Api/Telemetry.cs) traces ASP.NET Core, HttpClient and EF Core commands plus the app's `BookingApp` ActivitySource (Core/Common/Telemetry.cs: a span per use case with `booking.outcome`), exports metrics (ASP.NET Core, runtime, and the `BookingApp` meter) and logs, over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set. Business counters go through the `IBookingMetrics` port (Infrastructure/Observability/BookingMetrics.cs, IMeterFactory): bookings created, conflicts (slot_taken), confirmed, cancelled (by customer or staff), holds expired (counted by the repositories wherever holds expire). Tag by resource, never by customer. Liveness never checks dependencies; readiness checks the database.
- **Query shape:** the hot reads live in `BookingQueries` (measured by tools/BookingApp.Perf). Overlap queries add `start_utc > windowStart − Booking.MaxLength` (24 h, enforced by BookingPolicy) so a B-tree seeks a bounded range; keep that bound in any new overlap query. Changing a hot query or its index: measure before and after (docs/performance.md) rather than guess.
- **Caching:** availability reads what occupies a resource-day through `IOccupancyReader` (HybridCache, 1-minute expiry). Both booking repositories call `OccupancyVersions.Changed` after every committed add or update, and the cache key carries those versions, so availability is never stale after a booking change, not even for a slow reader racing a write (library 8.6 "Invalidation": versioned keys). Holds carry their expiry, so an expired hold stops counting without invalidation. Anything that must be exact (the overlap guarantee, "bookings in the way") reads the repository, never the cache. A new write path to bookings must call `Changed` too.
- **Series:** `POST /api/series` books the same venue-local time weekly (2–12 weeks): weeks are added in local time and converted one by one (DST-safe), and each date goes through `BookingService.BookAsync` with the series id, so every rule applies per date and the answer lists each date as held or failed with its code (best effort, library 8.2). A series counts once toward the limit of upcoming bookings. Confirming and cancelling a series act on each date and report each outcome.
- **Waitlist:** customers join the line for a taken time (`/api/waitlist`; one waiting entry per customer and time, a partial unique index). `WaitlistSweep` (Waitlist:SweepSeconds) finds waited-for times that are free again, whatever freed them (cancel, release, expired hold), and tells the first in line: the entry is marked Notified and its pre-rendered email goes into the outbox in one transaction, only if the entry is unchanged, so nobody is told twice. If the time is still free after Waitlist:OfferMinutes, the next in line is told. Entries for past times expire.
- **Notifications:** a booking records events (Confirmed, Cancelled) when its status changes; both booking repositories turn them into outbox messages (`NotificationPlan`) in the same save as the change, so an email can never be promised without the change or the other way round. `NotificationRelay` sends due messages every Notifications:PollSeconds; a failure backs off (10 s doubling, up to an hour) and is dead-lettered after Notifications:MaxAttempts. The outbox id is the message id on every attempt and the sender drops repeats, so a crash between sending and recording never sends twice. A reminder is re-checked when due (cancelled: skipped). Emails go to the booking's contact email (the request's `email`, or the account's); none without one.
- **Payments:** confirming a hold takes the deposit (Payments:Deposit in Payments:Currency; 0 turns it off) through `IPaymentGateway`. Money is `Money` (integer minor units), never a bare decimal. Every charge and refund carries the payment id as the provider's idempotency key, and the database allows one live (pending or paid) payment per booking, so nothing is ever charged twice. A declined card is 402 `payment_declined` (the hold stays); no answer is 503 `payment_unavailable` (retry reuses the payment). Refunds follow the cancellation rules: whatever the rules let be cancelled (customer before the cut-off, staff any time) or a paid hold that lapsed is refunded; the refund is tried at once and `RefundService` retries. Webhooks (`POST /api/payments/webhook`): the adapter verifies the HMAC over the raw body first, then the event id is recorded with its change in one transaction, so a redelivery changes nothing. Deviation from library 14.2 ("work later"): the work is one local row update, done in the same transaction; anything slow (refunds) is left to the refund job.
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
- PostgreSQL: `docker compose up -d postgres` (repo root), then `dotnet run --project src/BookingApp.Api --launch-profile http-postgres`. Tests start their own PostgreSQL with Testcontainers; without Docker those tests are skipped, and `REQUIRE_DOCKER=1 dotnet test` makes that a failure (use it in CI).
- Traces, metrics and logs locally: `docker compose --profile observability up -d aspire-dashboard` (repo root), then run the API with `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` (PowerShell: `$env:OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"`) and open http://localhost:18888: Traces shows each request with its `book`/`confirm`/`cancel` span and the SQL under it; Metrics → BookingApp has the business counters. Without the variable nothing is exported.
- Health: `/health` and `/health/live` (the process only), `/health/ready` (also the database).
- The whole app as one image: `docker compose up --build` (repo root) → http://localhost:8080. The root `Dockerfile` builds the web app (Node), publishes the API (SDK) and runs both on the chiseled ASP.NET runtime as a non-root user; the API serves `wwwroot` (`WebApp.cs`: static files, `index.html` fallback, unknown `/api` paths stay 404 problems). Configuration comes from the environment (docker-compose.yml): connection string, webhook secret, `DataProtection__KeysPath` and `Email__Folder` on the `/data` volume, so sign-ins survive a restart. Never set `ASPNETCORE_ENVIRONMENT=Development` in an image.
- First time on a machine: `node tools/dev-secrets.mjs` (the webhook secret and the PostgreSQL connection string, into dotnet user-secrets; `dotnet user-secrets list --project src/BookingApp.Api` shows them).
- `dotnet run --project src/BookingApp.Api`: http://localhost:5080. Bookings are stored in `src/BookingApp.Api/.data/bookings.db` (SQLite, git-ignored); delete that folder to start empty. In Development it seeds demo bookings for tomorrow, and serves the API reference at http://localhost:5080/scalar and the document at `/openapi/v1.json`.
