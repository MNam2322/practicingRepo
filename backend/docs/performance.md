# Performance: the hot queries and the booking race

Measured on 10 October 2026, on PostgreSQL 17 (docker-compose) on a developer laptop. Library: 7.3 "Reading a query plan",
7.9 Case 1 (rows read vs rows returned), 1.2 "Intervals, overlaps, and scheduling", 12.4 "Load testing with realistic
data".

## The data

`tools/BookingApp.Perf seed` builds a separate database, `booking_perf`, with a year shaped like the real thing:

- **Resources:** 30 courts, open 06:00–22:00 every day.
- **Dates:** 270 days of history and 95 days ahead. The past is 75% booked; the future fills up near today and thins out.
- **Bookings:** 114,407.
  - Past: 85% completed, 12% cancelled, 3% no-shows.
  - Future: 88% confirmed, 12% cancelled.
- **Customers:** 4,000 regular customers, skewed (a few book often), plus 10 coaches who hold 15% of all bookings (about 1,750 each).

Then `measure` prints `EXPLAIN (ANALYZE, BUFFERS)` for each query, and times 100–400 calls with varied parameters after a warm-up. The queries are `BookingQueries`, the ones the app runs.

## What changed

1. **Availability** (one resource, one day) and the **staff schedule** (every resource, one day) both test overlap as `start_utc < @end AND @start < end_utc`. That is two open-ended ranges, and a B-tree seeks only one. So the database read every booking that started before the window's end, then filtered.
   - The fix is a third, redundant predicate: no booking is longer than 24 hours (`Booking.MaxLength`, now enforced by `BookingPolicy`), so an overlapping booking starts after `@start − 24h`. With `start_utc > @start − 24h` the search is a bounded range.
2. **Indexes** (migration `QueryIndexes`):
   - `ix_bookings_resource_start (resource_id, start_utc)` is now **partial**, `WHERE status IN ('Confirmed', 'Pending')`. It holds only bookings that block, which is exactly what availability and the overlap check ask for.
   - New: `ix_bookings_start (start_utc)`, for the staff schedule.
3. **Rejected:** a `(customer_id, start_utc)` index for My bookings. Measured, it bought nothing. The planner still chose a bitmap scan and a sort, because the cost is fetching a customer's rows from the heap, not finding them. The single-column index stays.

## Plans

| Query | Before | After |
|---|---|---|
| Availability, court-01, one day | Bitmap scan on the exclusion constraint's GiST index (resource only): **501 rows read for 5**, 115 buffers, 1.25 ms | Index scan on `ix_bookings_resource_start`, both bounds in the index condition: **10 rows read for 5**, 4 buffers, 0.06–0.10 ms |
| Staff schedule, one day | **Seq scan of all 114,407 rows** for 139, 2,388 buffers, 8.7 ms | Index scan on `ix_bookings_start`: 265 rows read for 139, 99 buffers, 0.13–0.17 ms |
| My bookings, regular customer (22 rows) | Bitmap scan on `ix_bookings_customer` + sort, 0.3 ms | unchanged |
| My bookings, a coach (1,765 rows) | Bitmap scan + sort, 1,253 heap pages, 2.6 ms | unchanged |

The before plan for availability:

```text
Sort (actual rows=5 loops=1)
  ->  Bitmap Heap Scan on bookings b (actual rows=5 loops=1)
        Filter: ((start_utc < '2026-12-09 17:00:00+00') AND ('2026-12-08 17:00:00+00' < end_utc))
        Rows Removed by Filter: 496
        Buffers: shared hit=112
        ->  Bitmap Index Scan on ex_bookings_no_overlap (actual rows=501 loops=1)
              Index Cond: ((resource_id)::text = 'court-01'::text)
Execution Time: 1.252 ms
```

And after:

```text
Index Scan using ix_bookings_resource_start on bookings b (actual rows=5 loops=1)
  Index Cond: ((resource_id = 'court-01') AND (start_utc > '2026-12-07 17:00:00+00') AND (start_utc < '2026-12-09 17:00:00+00'))
  Filter: ('2026-12-08 17:00:00+00' < end_utc)
  Rows Removed by Filter: 5
  Buffers: shared hit=4
Execution Time: 0.059 ms
```

## Timings (end to end through EF Core, milliseconds)

| Query | calls | before p50 | before p95 | after p50 | after p95 |
|---|---:|---:|---:|---:|---:|
| Availability, random resource and day | 400 | 2.08 | 3.38 | 1.74 | 2.41 |
| My bookings, regular customer | 400 | 1.72 | 2.50 | 1.65 | 2.09 |
| My bookings, a coach | 100 | 10.63 | 18.33 | 15.68 | 19.16 |
| Staff schedule, random day | 100 | 14.12 | 18.61 | **2.79** | **4.06** |

What these numbers mean:

- **Availability:** after the fix, the time is mostly the round trip and EF Core materialising rows. The query itself went from 1.25 ms to under 0.1 ms, and its cost no longer grows with history: before, every booking the court ever had was read.
- **Staff schedule:** it no longer scans the whole table; it was getting slower every day.
- **My bookings:** cheap for ordinary customers. A coach's list costs what it returns, 1,765 rows. The fix there is not an index but returning fewer rows. Paging, or upcoming plus the last 90 days by default, is the next step if coaches' pages feel slow. (The 314 ms before-maximum was one cold outlier.)

## The booking race under load

`perf/booking-race.js` is a k6 test. 100 customers, each with their own cookie, try to book the same 15 slots in random order, all at once. The API was on PostgreSQL (`--launch-profile http-postgres`):

```text
docker run --rm -i -e BASE=http://host.docker.internal:5080 -v "<backend>/perf:/scripts" grafana/k6:1.8.1 run /scripts/booking-race.js
```

| Run | booked (201) | slot_taken (409) | unexpected | p50 | p95 |
|---|---:|---:|---:|---:|---:|
| Pool of up to 100 connections | **15** | 1,485 | 0 | 1.15 s | 1.34 s |
| Pool capped at 40 | **15** | 1,485 | 0 | 1.16 s | 1.40 s |

- **Exactly one success per slot.** The thresholds `booked count==15` and `unexpected count==0` pass. Afterwards, the database holds exactly one active booking for each raced slot (`GROUP BY resource_id, start_utc HAVING count(*) > 1` returns nothing).
- **Latency is deliberate.** All 1,500 attempts hit three resources at once, and inserts for one resource queue on that resource's row lock. That lock is what keeps them from deadlocking on the exclusion constraint (EfBookingRepository). A real club spreads its traffic over many courts and days.
- **Connection limit (found by this test).** The first run grew Npgsql's pool to its default of 100, which equals PostgreSQL's `max_connections`, so an admin `psql` was refused ("too many clients").
  - Development now caps the pool: `Maximum Pool Size=40` in the connection string. The second run shows the same throughput with 40, and an admin connection gets in.
  - In production, size the pool per instance so that instances × pool stays below `max_connections` minus a reserve.

## Reproduce

```text
docker compose up -d                                                   # repo root
cd backend
dotnet run -c Release --project tools/BookingApp.Perf -- seed         # about 10 s
dotnet run -c Release --project tools/BookingApp.Perf -- measure --label now
# rate limits raised: all the virtual users share one IP
RateLimits__WritesPerMinutePerIp=100000 RateLimits__RequestsPerMinute=100000 dotnet run --project src/BookingApp.Api --launch-profile http-postgres
# in another terminal, from backend/:
docker run --rm -i -e BASE=http://host.docker.internal:5080 -v "$PWD/perf:/scripts" grafana/k6:1.8.1 run /scripts/booking-race.js
```
