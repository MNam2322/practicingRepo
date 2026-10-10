# ADR 0002: The database refuses overlapping bookings

**Status:** Accepted (October 2026, backend tasks 4, 6 and 11)

## Context

The one rule that must never break: two blocking bookings (confirmed, or a hold that has not expired) for the same
court must not overlap. Customers race for popular slots, and requests arrive at the same moment on different threads,
and later on different instances. A "check, then insert" in application code is a race however careful the check:
both requests see the slot free, and both insert (library 7.2 "Constraints: the integrity the application cannot
enforce", 7.4 "Optimistic and pessimistic concurrency").

## Decision

The storage layer makes the check and the insert one atomic step, and on PostgreSQL the database itself is the judge:

- **PostgreSQL:** a generated `period tstzrange(start_utc, end_utc, '[)')` column and
  `EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (status IN ('Confirmed', 'Pending'))`
  (`btree_gist`).
  - There is no application-side check; a violation (SQLSTATE 23P01) becomes 409 `slot_taken`.
  - Each insert first locks its resource's row (`FOR NO KEY UPDATE`), so inserts for one court queue instead of
    deadlocking on the constraint (40P01).
  - Holds that are due to expire are expired in the same transaction before the constraint looks.
- **SQLite:** the overlap check and the insert run in one `BEGIN IMMEDIATE` transaction, which takes the write lock
  before the check.
- **In memory:** the check and the insert run under one lock.
- Tests on every provider fire many simultaneous requests at one slot and expect exactly one success. A PostgreSQL test
  writes raw SQL, bypassing the app entirely, and the constraint still refuses it. The k6 race (docs/performance.md)
  gives 15 bookings for 15 slots out of 1,500 attempts.

## Alternatives

- **Check in the application, then insert.** Simple, and wrong under concurrency.
- **A unique index on (resource, start).** Catches identical starts only; a 10:00–12:00 and an 11:00–12:00 booking both
  get in.
- **Serialisable isolation everywhere, with retries.** Correct, but every conflicting transaction aborts and retries.
  It also depends on every code path remembering the isolation level.
- **Advisory locks or a lock table per resource.** Works, but only for writers that take the lock; a forgotten path or a
  script breaks the rule silently.
- **An optimistic version on the resource row.** Every booking on a court conflicts with every other, even when the
  times are far apart.

## Consequences

- **+** The guarantee holds for every writer: new endpoints, background jobs, admin scripts, a bug in a service.
- **+** It is measurable: one race test per provider, plus a load test.
- **−** PostgreSQL-specific DDL lives in a migration as raw SQL, and changing which statuses block means rebuilding the
  constraint (the Holds migration did).
- **−** Inserts for one court are serialised by design. 100 simultaneous customers on one court see p95 ≈ 1.4 s; normal
  traffic spreads over many courts and days.
- **−** Holds count as blocking, so expired holds must be flipped to Expired before an insert, and reads must treat a
  past-due hold as free (`BlocksAt(now)`).
