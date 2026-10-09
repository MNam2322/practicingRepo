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
- [ ] `tests/BookingApp.Core.Tests` (xUnit), covering:
  - `TimeRange` (touching ranges don't overlap)
  - `BookingPolicy.Check`, one test per rule
  - `AvailabilityCalculator`, including a DST day: use `Europe/London` on its spring-forward and fall-back dates
  - `Booking.Cancel` for every rule
  - `BookingService` with a `FakeTimeProvider` and the in-memory repository
- [ ] `tests/BookingApp.Api.Tests` (WebApplicationFactory), covering:
  - every controller action's status code and shape
  - each error code
  - the cookie being issued and reused
  - **50 parallel bookings for one slot give exactly one 201**
- [ ] Add the test projects to `BookingApp.slnx`.

**Done when:** `dotnet test` passes, and deliberately breaking `TimeRange.Overlaps` makes tests fail.

### 2. OpenAPI and API docs
Read first: 8.2 API design.
- [ ] Add `Microsoft.AspNetCore.OpenApi` (`AddOpenApi`, `MapOpenApi`) and Scalar (`MapScalarApiReference`), both in Development only.
- [ ] Give every controller action a name, a summary (XML comments or `[EndpointSummary]`) and its error responses (`[ProducesResponseType]` with `ProblemDetails`).

**Done when:** `/scalar` lists every route with schemas, and `/openapi/v1.json` is served. (Unblocks frontend task 5.)

### 3. Validation
Read first: 8.5 "Validation in ASP.NET Core".
- [ ] Validate request shapes at the boundary: name length, slots range, a missing resource id. Use DataAnnotations on the request records (checked automatically by `[ApiController]`) or FluentValidation. (`AddValidation()` is for minimal APIs, which this project does not use.)
- [ ] Return 400 with field errors. Business rules stay in Core and keep returning 422 with codes.

## Phase 2: A real database

### 4. EF Core with SQLite
Read first: 7.7 ORMs and EF Core, 7.5 Schema design and migrations, 7.4 "Optimistic and pessimistic concurrency".
- [ ] Add `BookingDbContext`:
  - a `Bookings` table with an index for the availability query (resource, start)
  - a concurrency token
  - migrations
- [ ] Implement `EfBookingRepository : IBookingRepository` in `Infrastructure/Repositories/`. `TryAddAsync` must be atomic: on SQLite, run the overlap check and the insert in one transaction that takes the write lock first (`BEGIN IMMEDIATE`). `UpdateAsync` uses the concurrency token.
- [ ] Choose the repository through configuration (`Storage:Provider` = `InMemory` | `Sqlite`). The tests run against both.

**Done when:** bookings survive a restart, and the 50-parallel-requests test passes on SQLite.

### 5. Resources and opening hours in the database
Read first: 7.2 The relational model and SQL, 7.2 "Constraints: the integrity the application cannot enforce".
- [ ] Move venue, resources and opening hours from `appsettings.json` into tables behind `IResourceRepository` (an `EfResourceRepository`), seeded from the current settings on first run.
- [ ] Add **closures**: dates or ranges when a resource is closed, such as holidays or maintenance. Availability and booking respect them.

### 6. PostgreSQL with an exclusion constraint
Read first: 7.3 "Specialised index types", 7.9 "Atomic conditional update", 7.8 Running databases.
- [ ] Add a PostgreSQL provider (Npgsql) and `docker-compose.yml` with PostgreSQL 17. You need Docker Desktop.
- [ ] Prevent overlaps **in the database**: a `tstzrange` column with `EXCLUDE USING gist (resource_id WITH =, time WITH &&) WHERE (status = 'confirmed')`, using the `btree_gist` extension. Map the constraint violation (SQLSTATE 23P01) to `slot_taken`.
- [ ] Run the integration tests against PostgreSQL with Testcontainers.

**Done when:** the parallel test passes on PostgreSQL, with the constraint (not app code) doing the blocking.

## Phase 3: Accounts, staff and admin

### 7. Accounts
Read first: 10.2 Authentication and authorization, 10.2 "Authorization policies that scale".
- [ ] Add ASP.NET Core Identity with cookie auth, and roles `Customer`, `Staff` and `Admin`.
- [ ] Add an `AccountController` with sign up, sign in, sign out and `GET /api/me`, using `UserManager` and `SignInManager`. Do not use `MapIdentityApi` (it is minimal APIs).
- [ ] On sign-in, anonymous cookie bookings move to the account.

**Unblocks frontend 6.**

### 8. Staff features
Read first: 4.7 "Example 2: A booking lifecycle as a state machine", 12.6 "State machines for aggregate lifecycles".
- [ ] A staff-only `StaffController` (authorization policy, not role checks in code), backed by a service:
  - the day schedule across all resources
  - mark a booking completed or no-show, allowed only after it starts
  - cancel any booking with a reason
  - block time for maintenance
- [ ] Extend the state machine and test every allowed and disallowed transition.

**Unblocks frontend 7.**

### 9. Admin features
- [ ] An admin-only `AdminController`, backed by a service, to manage resources, opening hours, closures and booking policy, with an audit log of who changed what.

**Unblocks frontend 8.**

## Phase 4: Real-world booking features

### 10. Idempotent booking requests
Read first: 12.5 "Idempotency keys — preventing double charges", 8.2 "Idempotent HTTP APIs and safe retries".
- [ ] `POST /api/bookings` accepts an `Idempotency-Key` header. A repeat of the same key returns the original response. The same key with a different body gives 422.

### 11. Holds that expire
Read first: 8.7 "Background work inside the process", 7.9 "Atomic conditional update".
- [ ] Booking becomes two steps:
  - a 10-minute hold, with status `Pending`, which blocks the slot
  - then confirm
- [ ] A background job releases expired holds.

**Unblocks frontend 9.**

### 12. Payments (deposit) with a fake provider
Read first: 4.9 the Adapter case, 8.9 "Example 2: Receiving a payment webhook safely", 14.2 "Receiving a webhook: verify, store, then work".
- [ ] Put an `IPaymentGateway` port in front of a fake provider that you can drive from tests.
- [ ] Confirming a hold takes a deposit.
- [ ] Add a webhook controller action that verifies the signature and is idempotent.
- [ ] Refunds follow the cancellation rules.

### 13. Notifications with an outbox
Read first: 12.3 "Outbox + inbox patterns", 8.7 "Example 1: Confirmation emails lost on restart", 4.3 "Example 2: Scoped services inside a background worker".
- [ ] Booking confirmed, booking cancelled, and a reminder 24 hours before. Write them to an outbox table in the same transaction as the change.
- [ ] A worker sends them through an `IEmailSender`. A fake sender writes `.eml` files to a folder.

### 14. Recurring bookings
- [ ] "Every Tuesday 18:00 for 8 weeks" creates a series. Report which dates failed (taken, closed) rather than failing everything. Cancel one occurrence or the whole series.

**Unblocks frontend 10.**

### 15. Waitlist
- [ ] Join a waitlist for a taken slot. When it frees up, notify the first person in line through the outbox.

## Phase 5: Quality

### 16. Caching availability
Read first: 8.6 Caching, 8.6 "Invalidation".
- [ ] Cache availability per resource-day with `HybridCache`. Invalidate it on every booking change for that resource and day. Prove with a test that it is never stale after a booking.

### 17. Observability
Read first: 11.6 Observability.
- [ ] OpenTelemetry tracing, metrics and logs. Custom metrics: bookings created, conflicts (`slot_taken`), cancellations, and hold expiries.
- [ ] Add a database health check.

### 18. Performance
Read first: 7.9 Database optimisation casebook, 7.3 Indexes and query plans, 12.4 Scaling and performance.
- [ ] Seed one year of bookings. Measure the availability and my-bookings queries, read their plans, fix them with the right indexes, and record before and after numbers.
- [ ] Load test the booking race with k6 or NBomber.

### 19. Security hardening
Read first: 10.4 Common web vulnerabilities, 12.5 "Rate limiting".
- [ ] Security headers, antiforgery for cookie-authenticated writes, rate limits per account, and secrets in user-secrets.

## Phase 6: Delivery

### 20. Docker
Read first: 11.3 Containers and Docker.
- [ ] Build one image that contains the API and the built frontend (`UseStaticFiles` + `MapFallbackToFile`).
- [ ] Use `docker-compose.yml` to run the app with PostgreSQL.

### 21. CI (when a GitHub remote exists)
Read first: 11.4 CI/CD pipelines.
- [ ] A GitHub Actions workflow that builds, tests (including PostgreSQL through Testcontainers), runs the format check, and builds the image.

### 22. Architecture guard rails
Read first: 12.1 Monoliths, modules and services, 12.6 Domain modeling and application architecture.
- [ ] Architecture tests: Core references nothing, controllers never touch a repository or the DbContext, and there are no minimal-API application endpoints.
- [ ] `docs/adr/` with one decision record per big choice (database, overlap enforcement, identity, payments).

## Changelog

- Starter: domain, availability, booking, cancel, in-memory store with atomic overlap check, problem codes, rate limits, demo seed.
- Switched the API from minimal APIs to `[ApiController]` controllers (controller → service → repository): `IBookingStore` → `IBookingRepository`, `IResourceCatalog` → `IResourceRepository`, new `VenueService`; same routes, codes and rate limits.
