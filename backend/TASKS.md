# Backend tasks

Do these one at a time, in the order `../PLAYBOOK.md` gives. Before you plan a task:

1. Read its **Read first** sections with `node ../library/show.mjs`.
2. Search for anything else it needs with `node ../library/find.mjs`.

A task is finished when its **Done when** holds and `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` all pass.

## Done (the starter)

- [x] Domain:
  - a `TimeRange` with overlap checks
  - a venue in an IANA time zone, with resources, opening hours and a slot grid
  - a pure, DST-safe availability calculator
  - a `Booking` aggregate with cancellation rules
  - `BookingPolicy` settings
- [x] Controllers for the venue, resources, a day's availability, booking, my bookings, one booking, and cancel (controller → service → repository).
- [x] The in-memory booking repository refuses overlapping bookings atomically: 30 simultaneous requests for one slot give 1×201 and 29×409.
- [x] Problem details with stable codes, a customer cookie, rate limits on writes, CORS for the frontend, a health check and demo seed data.

## Phase 1: Safety net

### 1. Tests
Read first: 4.6 "API integration tests", 4.6 "TimeProvider — testing time-dependent code (.NET 8+)", 1.2 "Time zones and DST in practice".
- [x] `tests/BookingApp.Core.Tests` (xUnit), covering:
  - `TimeRange` (touching ranges don't overlap)
  - `BookingPolicy.Check`, one test per rule
  - `AvailabilityCalculator`, including a DST day: use `Europe/London` on its spring-forward and fall-back dates
  - `Booking.Cancel` for every rule
  - `BookingService` with a `FakeTimeProvider` and the in-memory repository
- [x] `tests/BookingApp.Api.Tests` (WebApplicationFactory), covering:
  - every controller action's status code and shape
  - each error code
  - the cookie being issued and reused
  - **50 parallel bookings for one slot give exactly one 201**
- [x] Add the test projects to `BookingApp.slnx`.

**Done when:** `dotnet test` passes, and deliberately breaking `TimeRange.Overlaps` makes tests fail.

### 2. OpenAPI and API docs
Read first: 8.2 API design.
- [x] Add `Microsoft.AspNetCore.OpenApi` (`AddOpenApi`, `MapOpenApi`) and Scalar (`MapScalarApiReference`), both in Development only.
- [x] Give every controller action a name, a summary (XML comments or `[EndpointSummary]`) and its error responses (`[ProducesResponseType]` with `ProblemDetails`).

**Done when:** `/scalar` lists every route with schemas, and `/openapi/v1.json` is served. (Unblocks frontend task 5.)

### 3. Validation
Read first: 8.5 "Validation in ASP.NET Core".
- [x] Validate request shapes at the boundary: name length, slots range, a missing resource id. Use DataAnnotations on the request records (checked automatically by `[ApiController]`) or FluentValidation. (`AddValidation()` is for minimal APIs, which this project does not use.)
- [x] Return 400 with field errors. Business rules stay in Core and keep returning 422 with codes.

## Phase 2: A real database

### 4. EF Core with SQLite
Read first: 7.7 ORMs and EF Core, 7.5 Schema design and migrations, 7.4 "Optimistic and pessimistic concurrency".
- [x] Add `BookingDbContext`:
  - a `Bookings` table with an index for the availability query (resource, start)
  - a concurrency token
  - migrations
- [x] Implement `EfBookingRepository : IBookingRepository` in `Infrastructure/Repositories/`. `TryAddAsync` must be atomic: on SQLite, run the overlap check and the insert in one transaction that takes the write lock first (`BEGIN IMMEDIATE`). `UpdateAsync` uses the concurrency token.
- [x] Choose the repository through configuration (`Storage:Provider` = `InMemory` | `Sqlite`). The tests run against both.

**Done when:** bookings survive a restart, and the 50-parallel-requests test passes on SQLite.

### 5. Resources and opening hours in the database
Read first: 7.2 The relational model and SQL, 7.2 "Constraints: the integrity the application cannot enforce".
- [x] Move venue, resources and opening hours from `appsettings.json` into tables behind `IResourceRepository` (an `EfResourceRepository`), seeded from the current settings on first run.
- [x] Add **closures**: dates or ranges when a resource is closed, such as holidays or maintenance. Availability and booking respect them.

### 6. PostgreSQL with an exclusion constraint
Read first: 7.3 "Specialised index types", 7.9 "Atomic conditional update", 7.8 Running databases.
- [x] Add a PostgreSQL provider (Npgsql) and `docker-compose.yml` with PostgreSQL 17. You need Docker Desktop.
- [x] Prevent overlaps **in the database**: a `tstzrange` column with `EXCLUDE USING gist (resource_id WITH =, time WITH &&) WHERE (status = 'confirmed')`, using the `btree_gist` extension. Map the constraint violation (SQLSTATE 23P01) to `slot_taken`.
- [x] Run the integration tests against PostgreSQL with Testcontainers.

**Done when:** the parallel test passes on PostgreSQL, with the constraint (not app code) doing the blocking.

## Phase 3: Accounts, staff and admin

### 7. Accounts
Read first: 10.2 Authentication and authorization, 10.2 "Authorization policies that scale".
- [x] Add ASP.NET Core Identity with cookie auth, and roles `Customer`, `Staff` and `Admin`.
- [x] Add an `AccountController` with sign up, sign in, sign out and `GET /api/me`, using `UserManager` and `SignInManager`. Do not use `MapIdentityApi` (it is minimal APIs).
- [x] On sign-in, anonymous cookie bookings move to the account.

**Unblocks frontend 6.**

### 8. Staff features
Read first: 4.7 "Example 2: A booking lifecycle as a state machine", 12.6 "State machines for aggregate lifecycles".
- [x] A staff-only `StaffController` (authorization policy, not role checks in code), backed by a service:
  - the day schedule across all resources
  - mark a booking completed or no-show, allowed only after it starts
  - cancel any booking with a reason
  - block time for maintenance
- [x] Extend the state machine and test every allowed and disallowed transition.

**Unblocks frontend 7.**

### 9. Admin features
- [x] An admin-only `AdminController`, backed by a service, to manage resources, opening hours, closures and booking policy, with an audit log of who changed what.

**Unblocks frontend 8.**

## Phase 4: Real-world booking features

### 10. Idempotent booking requests
Read first: 12.5 "Idempotency keys — preventing double charges", 8.2 "Idempotent HTTP APIs and safe retries".
- [x] `POST /api/bookings` accepts an `Idempotency-Key` header. A repeat of the same key returns the original response. The same key with a different body gives 422.

### 11. Holds that expire
Read first: 8.7 "Background work inside the process", 7.9 "Atomic conditional update".
- [x] Booking becomes two steps:
  - a 10-minute hold, with status `Pending`, which blocks the slot
  - then confirm
- [x] A background job releases expired holds.

**Unblocks frontend 9.**

### 12. Payments (deposit) with a fake provider
Read first: 4.9 the Adapter case, 8.9 "Example 2: Receiving a payment webhook safely", 14.2 "Receiving a webhook: verify, store, then work".
- [x] Put an `IPaymentGateway` port in front of a fake provider that you can drive from tests.
- [x] Confirming a hold takes a deposit.
- [x] Add a webhook controller action that verifies the signature and is idempotent.
- [x] Refunds follow the cancellation rules.

### 13. Notifications with an outbox
Read first: 12.3 "Outbox + inbox patterns", 8.7 "Example 1: Confirmation emails lost on restart", 4.3 "Example 2: Scoped services inside a background worker".
- [x] Booking confirmed, booking cancelled, and a reminder 24 hours before. Write them to an outbox table in the same transaction as the change.
- [x] A worker sends them through an `IEmailSender`. A fake sender writes `.eml` files to a folder.

### 14. Recurring bookings
- [x] "Every Tuesday 18:00 for 8 weeks" creates a series. Report which dates failed (taken, closed) rather than failing everything. Cancel one occurrence or the whole series.

**Unblocks frontend 10.**

### 15. Waitlist
- [x] Join a waitlist for a taken slot. When it frees up, notify the first person in line through the outbox.

## Phase 5: Quality

### 16. Caching availability
Read first: 8.6 Caching, 8.6 "Invalidation".
- [x] Cache availability per resource-day with `HybridCache`. Invalidate it on every booking change for that resource and day. Prove with a test that it is never stale after a booking.

### 17. Observability
Read first: 11.6 Observability.
- [x] OpenTelemetry tracing, metrics and logs. Custom metrics: bookings created, conflicts (`slot_taken`), cancellations, and hold expiries.
- [x] Add a database health check.

### 18. Performance
Read first: 7.9 Database optimisation casebook, 7.3 Indexes and query plans, 12.4 Scaling and performance.
- [x] Seed one year of bookings. Measure the availability and my-bookings queries, read their plans, fix them with the right indexes, and record before and after numbers.
- [x] Load test the booking race with k6 or NBomber.

### 19. Security hardening
Read first: 10.4 Common web vulnerabilities, 12.5 "Rate limiting".
- [x] Security headers, antiforgery for cookie-authenticated writes, rate limits per account, and secrets in user-secrets.

## Phase 6: Delivery

### 20. Docker
Read first: 11.3 Containers and Docker.
- [x] Build one image that contains the API and the built frontend (`UseStaticFiles` + `MapFallbackToFile`).
- [x] Use `docker-compose.yml` to run the app with PostgreSQL.

### 21. CI (when a GitHub remote exists)
Read first: 11.4 CI/CD pipelines.
- [x] A GitHub Actions workflow that builds, tests (including PostgreSQL through Testcontainers), runs the format check, and builds the image.

### 22. Architecture guard rails
Read first: 12.1 Monoliths, modules and services, 12.6 Domain modeling and application architecture.
- [x] Architecture tests: Core references nothing, controllers never touch a repository or the DbContext, and there are no minimal-API application endpoints.
- [x] `docs/adr/` with one decision record per big choice (database, overlap enforcement, identity, payments).

## Changelog

- Starter: domain, availability, booking, cancel, in-memory store with atomic overlap check, problem codes, rate limits, demo seed.
- Switched the API from minimal APIs to `[ApiController]` controllers (controller → service → repository): `IBookingStore` → `IBookingRepository`, `IResourceCatalog` → `IResourceRepository`, new `VenueService`; same routes, codes and rate limits.
- Task 1: xUnit tests — Core (TimeRange, policy rules, availability incl. Europe/London DST days, cancel rules, BookingService with FakeTimeProvider) and API (every route, every error code, cookie, 50 parallel bookings → one 201).
- Task 2: OpenAPI (`/openapi/v1.json`) and Scalar (`/scalar`) in Development; every action named, summarised, with its error codes; `ProblemDetails.code` in the schema; contract committed as `backend/openapi/v1.json` with a drift test.
- Task 3: request validation with DataAnnotations on `CreateBookingRequest` (resourceId, start, slots 1–24, name ≤ 80); 400 `validation_failed` with camelCase field errors; business rules still 422; strict JSON numbers.
- Task 4: EF Core with SQLite: `BookingDbContext` (bookings table, (resource, start) and customer indexes, `version` concurrency token, UTC ticks on SQLite), migrations applied at start-up, `EfBookingRepository` with `BEGIN IMMEDIATE` for the atomic insert; `Storage:Provider` InMemory | Sqlite; API booking/concurrency tests run on both.
- Task 5: venue, resources, opening hours and closures in the database (FKs, CHECKs), seeded from the Venue settings on first run; closures (per resource or venue-wide) make slots `closed` and bookings 422 `resource_closed`; the migration upgrades databases that already hold bookings.
- Task 6: PostgreSQL 17 (`docker-compose.yml`, `http-postgres` profile) with a generated `tstzrange` period and `EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE status = Confirmed`; 23P01 → `slot_taken`, no app-side check; per-provider contexts and migrations; Testcontainers tests on PostgreSQL (raw-SQL proof that the database blocks overlaps).
- Task 7: ASP.NET Core Identity (cookie `bk-auth`, roles Customer/Staff/Admin, capability policies, deny-by-default filter); `AccountController` sign up/in/out and `GET /api/me`; anonymous bookings move to the account through the aggregate; lockout and per-IP sign-in rate limit. PostgreSQL inserts now lock the resource row first (no exclusion-check deadlocks) inside a retrying execution strategy.
- Task 8: staff tools under the ViewSchedule policy: day schedule (all resources, any status, allowed actions per booking), complete / no-show after start, staff cancel with a reason the customer sees, block time (refused over confirmed bookings); the booking lifecycle is a transition table with an exhaustive test; Accounts:Bootstrap grants Staff/Admin by email.
- Task 9: admin API (ManageVenue policy): resources create/update/delete, weekly hours, whole-day closures incl. venue-wide holidays, booking rules moved into the database; every change saved with an audit entry (actor, request id, before/after) in the same transaction; `audit_log` append-only by database triggers.
- Task 10: `Idempotency-Key` on `POST /api/bookings`: atomic per-caller claim in `idempotency_keys`, response stored before it is sent and replayed with `Idempotency-Replayed: true`; 422 for a reused key with another body, 409 while in progress; hourly cleanup of keys older than 24 h; the frontend sends one key per booking attempt.
- Task 11: holds — booking makes a `Pending` hold (Holds:Minutes, default 10) that blocks the slot until `POST /api/bookings/{id}/confirm` (idempotent; 409 `hold_expired`) or release; expired holds stop blocking at once (lazy check on read, conditional UPDATE before each insert) and `HoldExpiryService` sweeps them to `Expired`; the PostgreSQL exclusion constraint now covers `Pending` too; the frontend holds and confirms in one go until frontend task 9.
- Task 12: deposits through an `IPaymentGateway` port and a fake provider (deterministic test tokens, idempotent per payment id): confirming a hold charges `Payments:Deposit` (402 `payment_declined` keeps the hold, 503 `payment_unavailable` retries the same payment); one live payment per booking (partial unique index); refunds whenever the cancellation rules allow a cancel (customer before the cut-off, staff) or a paid hold lapses, retried by `RefundService`; `POST /api/payments/webhook` verifies an HMAC over the raw body (5-minute window) and records each event id once with its change; `tools/payment-webhook.mjs` sends signed events.
- Task 13: notifications through an outbox: the booking aggregate records Confirmed/Cancelled events and the repositories write `notification_outbox` rows (confirmation, reminder 24 h before, cancellation with the staff reason) in the same save; `NotificationRelay` sends due rows through `IEmailSender` with backoff and dead-lettering, skips reminders for bookings no longer confirmed; `FileEmailSender` writes `.eml` files named by message id (a repeat after a crash is dropped); bookings take an optional `email` (signed-in customers default to their account address).
- Task 14: weekly series (`/api/series`, 2–12 weeks, idempotent): each date is booked on its own at the same venue-local time (DST-safe expansion) and reported as held or failed with its code; `bookings.series_id`; confirm (pay each date) and cancel (refund each date the rules allow) report per date; one date can still be cancelled alone; a series counts once toward the upcoming-bookings limit.
- Task 15: waitlist (`/api/waitlist`: join a taken time, mine with places in line, leave): `waitlist` table with one waiting entry per customer and time; `WaitlistSweep` tells the first in line when the time is free again (any cause), marking the entry and writing its email to the outbox in one transaction; the next in line is told after 30 minutes if it is still free; outbox messages can carry pre-rendered text and no booking.
- Task 16: availability's occupancy read is cached per resource-day in HybridCache (`IOccupancyReader` / `CachedOccupancyReader`); the booking repositories bump per resource-day versions after every committed write and the versions are in the cache key, so a read after a booking change is never stale (tests on every provider, plus the slow-reader race); holds carry their expiry; exact checks bypass the cache.
- Task 17: OpenTelemetry traces (ASP.NET Core, HttpClient, EF Core, and `BookingApp` spans per use case), metrics (ASP.NET Core, runtime, `BookingApp` counters: created, conflicts, confirmed, cancelled, holds expired) and logs, exported over OTLP when configured; Aspire dashboard in docker-compose (`observability` profile); `/health/live` and `/health/ready` (database); tests prove one trace from request to SQL and the counters.
- Task 18: `tools/BookingApp.Perf` seeds a year (114k bookings, 30 courts) and measures `BookingQueries`; overlap queries bounded by the 24-hour maximum booking length (now a rule); `ix_bookings_resource_start` partial on blocking statuses, new `ix_bookings_start`: availability reads 10 rows instead of 501 (1.25 → 0.06 ms in the database), the staff schedule an index range instead of a 114k-row scan (p50 14.1 → 2.8 ms); a (customer, start) index measured and rejected; k6 race (100 customers, 15 slots): exactly 15 bookings, 1,485 slot_taken, 0 errors; Npgsql pool capped below max_connections. Details: docs/performance.md.
- Task 19: antiforgery on every controller write (XSRF-TOKEN cookie + X-XSRF-TOKEN header, `GET /api/antiforgery`, new token on sign-in/up/out, 400 `antiforgery_failed`; the signed webhook exempt); security headers (strict CSP and no-store on /api, nosniff, DENY framing, no-referrer, no Server header, HSTS outside Development, 1 MB body limit); rate limits per account or per IP (never per client-sent cookie), a global per-caller limit, 429 problem details with Retry-After, logged; development secrets moved to dotnet user-secrets (`tools/dev-secrets.mjs`).
- Task 20: one image (root `Dockerfile`): the web app built with Node, the API published with the SDK, run on `aspnet:10.0.12-noble-chiseled-extra` as a non-root user; the API serves `wwwroot` (hashed assets cached for a year, `index.html` no-cache, an `index.html` fallback, unknown `/api` paths still 404 problems); `docker compose up --build` runs it on :8080 with PostgreSQL; secrets and paths from the environment, Data Protection keys and emails on a `/data` volume (sign-ins survive a restart); forwarded headers trusted only from configured proxies; SQL command logging off outside Development.
- Task 22: `ArchitectureTests` (NetArchTest + Mono.Cecil): Core's project file has no references and its types use only the BCL; Infrastructure never depends on Api; controllers use no storage port, repository, DbContext or EF Core; every `/api` endpoint is a controller action (checked on the running app); no `DateTime.Now`/`UtcNow` in the app (IL scan). Checked: an EF Core reference added to Core fails the test. `docs/adr/` 0001–0005: database, overlap enforcement, identity, payments, layering, each with alternatives and consequences.
- Task 21: GitHub Actions CI (`.github/workflows/ci.yml`) on pushes to `main` and `dev` and on pull requests, failing fast in cost order:
  - backend: restore (NuGet cached), build with warnings as errors, format check, Core tests, then the Infrastructure and API tests on every provider with `REQUIRE_DOCKER=1`
  - frontend: lint, type check and build, component tests
  - image (after both pass): built once with the commit SHA as its tag and the GitHub Actions cache, started with PostgreSQL through docker-compose, waited on `/health/ready`, then the Playwright suite run against it
  Failure reports are uploaded as artifacts; the workflow passes actionlint. The `dev` branch is created from `main`, and the root CLAUDE.md records the remote and branch rules.
