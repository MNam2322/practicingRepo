# ADR 0005: Controllers → services → repositories, enforced by tests

**Status:** Accepted (October 2026, the switch to controllers and backend task 22)

## Context

The starter mapped endpoints with minimal APIs in one file. As the app grew (bookings, holds, payments, series,
waitlist, staff, admin), the team chose a conventional, predictable shape that any .NET developer recognises. They also
chose to keep business rules testable without a web server or a database. A layering that is only a convention erodes
within months (library 12.1 "Layers, and the dependency rule").

## Decision

- **Three projects, dependencies pointing inwards** (library 12.1, "inverted" layers):
  - `BookingApp.Core`: the domain (aggregates with explicit state machines), the use-case services, and the ports
    (`I…Repository`, `IPaymentGateway`, `IEmailSender` …). Base class library only.
  - `BookingApp.Infrastructure`: the adapters (EF Core repositories, the fake payment provider, the email sender,
    background jobs).
  - `BookingApp.Api`: HTTP. `[ApiController]` controllers bind the request, call services, and map results to problem
    details with a stable `code`.
- **Never minimal APIs for application endpoints.** Operational routes (health checks, the API reference, the web app's
  fallback) may be minimal.
- **Controllers never touch a repository or the DbContext.**
- **Enforced by `ArchitectureTests`** (backend/tests/BookingApp.Api.Tests):
  - Core's project file has no references.
  - Core's types use nothing outside the base class library.
  - Infrastructure never depends on Api.
  - Controllers depend on no storage port, repository, DbContext or EF Core.
  - Every `/api` endpoint is a controller action.
  - Nothing reads `DateTime.Now`/`UtcNow`: time comes from `TimeProvider`.

## Alternatives

- **Minimal APIs throughout.** Less ceremony and fast. The team preferred controllers' conventions (filters,
  attributes, one place per resource), and the rule keeps the code base uniform.
- **Vertical slices with a mediator (one handler per request).** Good for large teams. Here it adds indirection, and a
  package, for about 35 endpoints.
- **One project with folders.** Fine for a small service, but nothing stops a controller from reaching for the
  DbContext. Once it does, the domain can no longer be tested alone.

## Consequences

- **+** Rules are tested without HTTP or a database (Core tests run in milliseconds), and storage can change behind the
  ports. It has already changed three times: in-memory, then SQLite, then PostgreSQL.
- **+** Erosion fails the build: adding an EF Core reference to Core, or injecting a repository into a controller,
  breaks a test with a message that says why.
- **−** More files and mapping: request contracts become domain calls, and domain results become response contracts.
- **−** Identity lives in Infrastructure (it is EF Core-backed), so `AccountController` calls `AccountService` from
  Infrastructure. That is a service, not storage, and the tests allow it.
