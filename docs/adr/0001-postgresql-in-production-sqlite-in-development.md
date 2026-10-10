# ADR 0001: PostgreSQL in production, SQLite in development, one EF Core model

**Status:** Accepted (October 2026, backend tasks 4–6)

## Context

The starter kept bookings in memory. The app needs a real database: bookings must survive restarts, and "no two
bookings overlap" has to hold under concurrent requests (ADR 0002). It also needs a first run without setup: a newcomer
should be able to clone the repository, run `dotnet run`, and book. Tests must stay fast, yet prove behaviour against the
database the app runs on in production.

## Decision

- **Production: PostgreSQL 17.** Its exclusion constraint over a `tstzrange` refuses overlapping bookings in the
  database itself (ADR 0002). It runs in docker-compose locally and is the database in the Docker image's compose file.
- **Development default: SQLite**, a file under `backend/src/BookingApp.Api/.data`, created and migrated on start. No
  Docker needed. `--launch-profile http-postgres` switches to PostgreSQL.
- **Fast tests: an in-memory provider**, behind the same repository ports.
- **One EF Core model** (`BookingDbContext`) with a provider-specific subclass each (`SqliteBookingDbContext`,
  `PostgresBookingDbContext`) and **migrations generated per provider**. Constraints that EF Core cannot express (the
  exclusion constraint, append-only triggers) are written as SQL in the migration.
- Every API and repository test runs on **all three providers**; PostgreSQL runs in Testcontainers, and
  `REQUIRE_DOCKER=1` turns a skipped PostgreSQL test into a failure.

## Alternatives

- **PostgreSQL only.** One engine, no differences to manage. Lost because every newcomer would need Docker before the
  first run.
- **SQLite only.** Zero setup, but it has one writer at a time and no exclusion constraint. Overlap safety would rest on
  a transaction lock alone, and it is not what a club would run for a busy booking site.
- **SQL Server.** Mature and well supported by EF Core, but it has no exclusion constraint, so overlaps need serialisable
  transactions or application locks, and its licensing gets in the way for small deployments.
- **A document database.** It has no constraints and no transactions across the rows the overlap rule spans, so the
  guarantee would move into application code, which is where races live.

## Consequences

- **+** `git clone`, `dotnet run`, and it works. Production gets constraints the database enforces whatever writes to
  it.
- **+** The repository contract tests show the same behaviour on all three providers.
- **−** Two migration sets to keep in step: every model change is generated twice and both files are read before
  committing (backend/CLAUDE.md).
- **−** Real differences to respect:
  - SQLite stores times as UTC ticks (it cannot compare `DateTimeOffset`).
  - Overlap enforcement differs per provider (ADR 0002).
  - The in-memory provider keeps no history across restarts.
- **−** CI must run Docker for the PostgreSQL tests, or it proves only the easy providers.
