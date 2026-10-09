---
id: "14-3"
number: "14.3"
title: "Worked cases from production"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 11333
minutes: 49
---

# 14.3 Worked cases from production

> **Quick look.** Real production incidents worked through: symptom, diagnosis, fix and lesson.
>
> - Order list 80 ms → 4 s: 101 queries for 50 rows (lazy-loading N+1). Count commands per request.
> - Wrong report totals: a one-to-many join multiplied parent amounts before summing.
> - Page 500 takes 8 s: OFFSET paging cost plus a non-unique sort that skipped and repeated rows; keyset paging fixed both.
> - Dashboard melts the database at 9 am: synchronized cache expiry under concurrency; one query plus stampede protection and jitter.
> - Four-hour import: a list search inside a loop, SaveChanges per row and 1.2 M tracked entities.
> - Wrong tenant's data in a cache: a mis-scoped query poisoned a cache entry with no expiry.
> - Sale-day collapse: three small faults compounding; adding pods made connection limits worse.
> - Others: the service split along entities (N+1 across services), an ARM-only race, parameter sniffing with data skew, a non-reversible rename, an outbox losing 0.2% of messages.
> - Order service design: 5,000 orders/minute is about 83 requests per second, which fits a few pods and one database. Do the arithmetic first.


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Turning an incident into a guardrail

After the "order list went from 80 ms to 4 s" incident (lazy-loading N+1), the team wants it never to happen again.

*Three guardrails*

```text
1. Lazy loading disabled project-wide (no UseLazyLoadingProxies)
2. Command-count interceptor logs any request issuing more than 10 queries (warning in production)
3. Integration test per list endpoint asserting a fixed number of queries
Postmortem action items are code, not reminders.
```

> **Lesson:** Every production case should end with a mechanism that makes the same mistake impossible or loud.

### A Worked Design: Order Service End to End

<p class="src">From <em>.NET Backend Deep Dive</em> · A Worked Design: Order Service End to End</p>

<a id="dn-164"></a>

#### The write path

Tying the parts together. Requirements: 5,000 orders/minute at peak, p99 under 300ms, payments must never double-charge, and downstream systems (inventory, email, analytics) must be notified.

```
POST /v1/orders        Idempotency-Key: <client guid>
  ├─ rate limiter, per user                                   (23.4)
  ├─ FluentValidation filter -> 400 + ProblemDetails          (34.1)
  ├─ idempotency check: unique index, replay stored result    (12.3)
  ├─ resource authorization: is this THEIR cart?              (23.2)
  ├─ ONE transaction:
  │    ├─ insert Order (aggregate enforces its invariants)    (34.3)
  │    ├─ insert OutboxMessage(OrderPlaced)                   (12.6)
  │    └─ SaveChanges -> interceptor stamps audit columns     (2.1)
  ├─ commit
  └─ 201 Created + Location
```

Key decisions and why:

- **The payment call is not in the transaction.** An external call inside a transaction holds a database connection for the duration of someone else's network latency — the fastest route to pool exhaustion (13.D). Payment happens in the saga, driven from the outbox.
- **The outbox, not a direct publish.** A publish after commit can fail and silently lose the event (12.6).
- **Idempotency before anything expensive**, so a client retry costs one indexed lookup.
- **A DTO, never the entity**, in both directions (23.3).

<a id="dn-165"></a>

#### The async continuation

```
OutboxPublisher (BackgroundService + IServiceScopeFactory)   (3.3)
  └─> OrderPlaced -> topic
        ├─ PaymentConsumer    -> charge, idempotent, retry + circuit breaker  (12.1/12.2)
        ├─ InventoryConsumer  -> reserve stock
        ├─ EmailConsumer      -> confirmation
        └─ AnalyticsConsumer  -> warehouse
```

Every consumer writes to an **inbox** table with a unique `MessageId` before doing work (12.6), because delivery is at-least-once. Payment failure triggers a **compensating action** that cancels the order (12.5) — and that compensation is itself idempotent and retried forever, with a DLQ and a human process behind it.

Ordering matters for a customer's events, so the **partition key is the customer id** (31.1).

<a id="dn-166"></a>

#### The read path

```
GET /v1/orders?cursor=...&limit=50
  ├─ HybridCache: L1 memory + L2 Redis, tag "orders:{customerId}"   (6.3)
  └─ miss -> EF projection to DTO, AsNoTracking, keyset paging      (2.2)
```

Cache invalidation is by **tag on write**, backed by a Redis backplane so every pod's L1 drops the entry (16.5). TTL stays short anyway, because invalidation is the hard part (6.4).

The index that makes it work: `(CustomerId, CreatedAt DESC) INCLUDE (Total, Status) WHERE IsDeleted = 0` — covering, filtered, and left-most-prefix aligned with the query (5.1, 2.1).

<a id="dn-167"></a>

#### Capacity check

5,000/minute ≈ **83 rps** sustained, perhaps 250 rps at peak. That is genuinely modest, and the honest conclusion is that this fits comfortably on a handful of pods against one well-indexed database. **Do the arithmetic before designing for scale you don't have** — most "we need Kafka and sharding" conversations end here.

Budget within the 300ms p99: ~5ms validation and auth, ~20ms for the transaction, ~2ms Redis, leaving substantial headroom. The things that would actually break it are an N+1 (2.2.1), a blocking call starving the pool (8.2), or a gen 2 GC pause (20.3) — which is exactly what to instrument.

<a id="dn-168"></a>

#### Operating it

| Concern | Mechanism |
|---|---|
| Tracing | OTel spans through API → outbox → consumers, `traceId` in every log and error response (1.3, 22.3) |
| SLO | 99.5% under 300ms; alert on burn rate, not CPU (28.4) |
| Health | Liveness = process only; readiness = DB + Redis (27.1) |
| Deploys | Rolling, expand-and-contract migrations (14.7) |
| Failure | Circuit breaker on payment with a degraded path; DLQ with replay (12.2) |
| Diagnosis | Command-count test guards N+1 in CI (17.2); the Part 30 table for triage |

<a id="dn-169"></a>

#### What the interviewer is checking

Not whether you named the most technologies. Typically: do you put the external call outside the transaction, do you reach for an outbox when asked how the event survives a crash, do you say "at-least-once, so consumers are idempotent" without prompting, and do you compute the load before proposing an architecture for it.

Saying "83 requests per second doesn't need Kafka, but we'd want a durable queue for the payment step" is a stronger answer than any diagram.

---

### Worked cases: the read path

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Worked cases: the read path</p>

<a id="be-m293"></a>

#### The system we will be fixing

Every case that follows works on one system, so the code compounds instead of resetting. It is a multi-tenant order management platform: ASP.NET Core, EF Core over SQL Server, Redis for caching, a nightly importer, and a reporting screen that nobody loves.

> **\[Why it matters\]**
>
> **On the numbers.** These are composite scenarios built from the failure modes in this guide, with figures representative of systems of this shape and size — not measurements taken from one specific application. Treat the *ratios* and the diagnostic path as the content; treat the absolute milliseconds as illustration.

``` csharp
public class Tenant   { public int Id; public string Name = ""; }

public class Customer {
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string? Notes { get; set; }                 // nvarchar(max), rarely read
    public List<Order> Orders { get; set; } = [];
}

public class Order {
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }                  // decimal(18,4)
    public DateTimeOffset CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<OrderLine> Lines { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public class OrderLine {
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Sku { get; set; } = "";              // varchar(50), not Unicode
    public int Qty { get; set; }
    public decimal UnitPriceAtSale { get; set; }        // frozen at sale time
}

public class Payment {
    public long Id { get; set; }
    public long OrderId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset SettledAt { get; set; }
}

public enum OrderStatus { Draft = 0, Open = 1, Shipped = 2, Closed = 3, Cancelled = 4 }
```

``` sql
-- The schema as configured. Note what is deliberate:
CREATE TABLE Orders (
  Id          bigint IDENTITY PRIMARY KEY,          -- sequential clustered key
  TenantId    int NOT NULL,
  CustomerId  int NOT NULL REFERENCES Customers(Id),
  Status      tinyint NOT NULL,
  Total       decimal(18,4) NOT NULL,               -- 4dp, not the 18,2 default
  CreatedAt   datetimeoffset NOT NULL,
  RowVersion  rowversion
);
CREATE INDEX IX_Orders_Tenant_Created
  ON Orders (TenantId, CreatedAt DESC) INCLUDE (CustomerId, Status, Total);
CREATE INDEX IX_OrderLines_OrderId ON OrderLines (OrderId) INCLUDE (Sku, Qty);
```

Scale, for calibration: 40 tenants, 2 million orders, 9 million order lines, the largest tenant holding roughly 60% of the rows. That skew matters in three separate cases below.

<a id="be-m294"></a>

#### Case: the order list that went from 80ms to 4s

Symptom

The main order grid was fine at launch and is now four seconds for the largest tenant. CPU on the web tier is low; the database is not obviously struggling. Latency scales with the number of rows on screen.

What the instrumentation showed

An EF command-counting interceptor logged **101 database commands for one request** that renders 50 rows. That single number identifies the problem before anyone opens a profiler.

``` csharp
// A 25-line interceptor worth adding to every EF project.
public sealed class CommandCounter : DbCommandInterceptor {
    public static readonly AsyncLocal<int> Count = new();
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand cmd, CommandEventData e, InterceptionResult<DbDataReader> r) {
        Count.Value++; return r;
    }
}
// In a middleware, after the response:
if (CommandCounter.Count.Value > 10)
    logger.LogWarning("{Path} issued {N} DB commands", ctx.Request.Path, CommandCounter.Count.Value);
```

Before

``` csharp
public async Task<List<OrderRow>> GetOrders(int tenantId, int page) {
    var orders = await _db.Orders
        .Where(o => o.TenantId == tenantId)
        .OrderByDescending(o => o.CreatedAt)
        .Skip(page * 50).Take(50)
        .ToListAsync();

    var rows = new List<OrderRow>();
    foreach (var o in orders) {
        rows.Add(new OrderRow {
            Id         = o.Id,
            Customer   = o.Customer.Name,              // lazy load -> 1 query each
            LineCount  = o.Lines.Count,                // lazy load -> 1 query each
            Total      = o.Total,
            Paid       = o.Payments.Sum(p => p.Amount) // lazy load -> 1 query each
        });
    }
    return rows;
}
```

Three things are wrong and they compound. Every navigation access is a round trip ([N+1 and how to see it](07-07-orms-and-ef-core.md#be-m156)). Every order is materialised as a tracked entity with a change-tracking snapshot ([Tracking, no-tracking, and the identity map](07-07-orms-and-ef-core.md#be-m158)). And `SELECT *` pulls `Customer.Notes`, an `nvarchar(max)` nobody displays.

After

``` csharp
public async Task<List<OrderRow>> GetOrders(int tenantId, int page) =>
    await _db.Orders
        .AsNoTracking()
        .Where(o => o.TenantId == tenantId)
        .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)  // tiebreaker
        .Skip(page * 50).Take(50)
        .Select(o => new OrderRow {
            Id        = o.Id,
            Customer  = o.Customer.Name,               // becomes a JOIN
            LineCount = o.Lines.Count(),               // becomes a correlated COUNT
            Total     = o.Total,
            Paid      = o.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
        })
        .ToListAsync();
```

``` sql
-- One command. The subqueries are cheap because both FKs are indexed.
SELECT o.Id, c.Name,
       (SELECT COUNT(*) FROM OrderLines l WHERE l.OrderId = o.Id),
       o.Total,
       (SELECT SUM(p.Amount) FROM Payments p WHERE p.OrderId = o.Id)
FROM Orders o JOIN Customers c ON c.Id = o.CustomerId
WHERE o.TenantId = @tenantId
ORDER BY o.CreatedAt DESC, o.Id DESC
OFFSET @skip ROWS FETCH NEXT 50 ROWS ONLY;
```

Result

|                          | Before                    | After        |
|--------------------------|---------------------------|--------------|
| DB commands per request  | 101                       | 1            |
| Rows materialised        | 50 orders + ~700 children | 50 flat rows |
| Columns fetched          | all, incl. `Notes`        | 5            |
| Latency (largest tenant) | ~4,000 ms                 | ~35 ms       |

> **\[Tip\]**
>
> **Takeaway.** The fix was not clever — it was projecting instead of fetching ([Projection: Select before materialize](07-07-orms-and-ef-core.md#be-m157)). The valuable part is the interceptor: "commands per request" is a single number that turns an invisible architectural problem into an alert. Add it before you need it. Note also the `(decimal?)` cast: `SUM` over zero payments returns `NULL`, not `0` ([NULL handling in SQL](07-02-the-relational-model-and-sql.md#be-m187)).

<a id="be-m295"></a>

#### Case: the report whose totals were wrong

Symptom

Finance reports that revenue by country is roughly 30% too high, but only for some countries, and only since a feature that let orders carry several payments. Nothing errors. The number is simply wrong, which is the worst kind of bug.

Before

``` csharp
var revenue = await _db.Orders
    .Where(o => o.TenantId == tenantId && o.CreatedAt >= from)
    .SelectMany(o => o.Payments, (o, p) => new { o.Customer.Country, o.Total })
    .GroupBy(x => x.Country)
    .Select(g => new { Country = g.Key, Revenue = g.Sum(x => x.Total) })
    .ToListAsync();
```

``` sql
-- What that generates, and why it is wrong:
SELECT c.Country, SUM(o.Total)          -- o.Total, once per PAYMENT row
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
JOIN Payments p ON p.OrderId = o.Id     -- fan-out: 1 order x 3 payments = 3 rows
GROUP BY c.Country;
```

Diagnosis

The join multiplies rows, and the aggregate then sums the *parent's* value once per child row. An order with three partial payments contributes its total three times. Countries whose customers pay in instalments are inflated; countries that pay once are correct — which is exactly the "only some countries" pattern that made it hard to spot.

``` sql
-- The one-line check that proves it, before changing any code:
SELECT COUNT(*) AS JoinedRows, COUNT(DISTINCT o.Id) AS Orders
FROM Orders o JOIN Payments p ON p.OrderId = o.Id
WHERE o.TenantId = @t;
-- JoinedRows > Orders means every parent-level aggregate in this query is wrong.
```

After

``` csharp
// The payments were never needed for this figure at all:
var revenue = await _db.Orders
    .Where(o => o.TenantId == tenantId && o.CreatedAt >= from)
    .GroupBy(o => o.Customer.Country)
    .Select(g => new { Country = g.Key, Revenue = g.Sum(o => o.Total) })
    .ToListAsync();

// And when you genuinely need both grains, aggregate each independently:
var byCountry = await _db.Orders
    .Where(o => o.TenantId == tenantId && o.CreatedAt >= from)
    .GroupBy(o => o.Customer.Country)
    .Select(g => new {
        Country  = g.Key,
        Ordered  = g.Sum(o => o.Total),                            // order grain
        Settled  = g.Sum(o => o.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) // child grain
    })
    .ToListAsync();
```

> **\[Trap\]** **The general rule.** Never aggregate a parent column across a join to a child table. Either aggregate the child in a subquery or a separate query, or aggregate the parent first and join to the result. In SQL, a window function expresses it directly:
>
> ``` sql
> SELECT DISTINCT c.Country,
>        SUM(o.Total) OVER (PARTITION BY c.Country) AS Revenue
> FROM Orders o JOIN Customers c ON c.Id = o.CustomerId;   -- no fan-out to Payments
> ```

> **\[Tip\]**
>
> **Prevention.** Add a reconciliation test that computes the same figure two different ways and asserts they agree. Fan-out bugs never announce themselves, so the only reliable defence is a second, structurally different computation. See [Join types and what they really mean](07-02-the-relational-model-and-sql.md#be-m181) and [Window functions](07-02-the-relational-model-and-sql.md#be-d414).

<a id="be-m296"></a>

#### Case: page 500 takes eight seconds

Symptom

The order grid is fast on page 1 and unusable past page 200. Support also has two open tickets claiming rows are "missing" and one claiming a row "appeared twice".

Diagnosis

Two distinct bugs wearing one costume.

``` csharp
-- 1. OFFSET must read and discard everything before the page.
--    Page 500 of 50 reads 25,000 rows to return 50.
ORDER BY o.CreatedAt DESC OFFSET 25000 ROWS FETCH NEXT 50 ROWS ONLY;

-- 2. CreatedAt is not unique. Bulk-imported orders share a timestamp to the
--    second, so SQL is free to order ties differently between the two queries
--    that produce page 3 and page 4 - hence rows both skipped and duplicated.
```

After

``` csharp
public sealed record Cursor(DateTimeOffset CreatedAt, long Id) {
    public string Encode() => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{CreatedAt:O}|{Id}"));
    public static Cursor? Decode(string? s) {
        if (string.IsNullOrEmpty(s)) return null;
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split('|');
        return parts is [var d, var i]
            ? new Cursor(DateTimeOffset.Parse(d), long.Parse(i)) : null;
    }
}

public async Task<(List<OrderRow> Rows, string? Next)> GetPage(
        int tenantId, string? cursor, int size = 50) {
    var c = Cursor.Decode(cursor);
    var q = _db.Orders.AsNoTracking().Where(o => o.TenantId == tenantId);

    if (c is not null)
        q = q.Where(o => o.CreatedAt < c.CreatedAt
                      || (o.CreatedAt == c.CreatedAt && o.Id < c.Id));

    var page = await q
        .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
        .Take(size + 1)                                   // +1 probes for a next page
        .Select(o => new OrderRow { Id = o.Id, Total = o.Total,
                                     CreatedAt = o.CreatedAt })
        .ToListAsync();

    var hasMore = page.Count > size;
    if (hasMore) page.RemoveAt(size);
    var next = hasMore
        ? new Cursor(page[^1].CreatedAt, page[^1].Id).Encode() : null;
    return (page, next);
}
```

``` csharp
-- The generated predicate is a range seek on IX_Orders_Tenant_Created:
WHERE TenantId = @t AND (CreatedAt < @c OR (CreatedAt = @c AND Id < @i))
ORDER BY CreatedAt DESC, Id DESC
OFFSET 0 ROWS FETCH NEXT 51 ROWS ONLY;

-- EF Core 7+ can also express this as a tuple comparison, which some providers
-- translate into a single row-value predicate:
--   .Where(o => ValueTuple.Create(o.CreatedAt, o.Id)
--             < ValueTuple.Create(c.CreatedAt, c.Id))
```

Result

|                                | Offset    | Keyset |
|--------------------------------|-----------|--------|
| Rows read for page 1           | 50        | 51     |
| Rows read for page 500         | 25,050    | 51     |
| Latency, page 500              | ~8,000 ms | ~12 ms |
| Stable while rows are inserted | no        | yes    |

> **\[Trap\]**
>
> **What you give up.** Keyset pagination cannot jump to an arbitrary page number, and it does not give you a total count. If the UI truly needs "page 47 of 812", keep offset paging but cap it — most products that add a cursor also discover that nobody used page 47. Also: **the tiebreaker fixes a correctness bug independently of the pagination style**. Add `ThenBy(o => o.Id)` to every paged query today, even if you never switch to cursors ([Comparison: IComparable, IComparer, stable ordering](02-03-types.md#be-m57), [Pagination: OFFSET vs keyset](07-07-orms-and-ef-core.md#be-m161)).

Recipe: [Recipe: a filtered, sorted, paginated list endpoint](14-01-recipes-code-you-will-actually-write.md#be-m285)

<a id="be-m297"></a>

#### Case: the dashboard that melts the database at 9am

Symptom

Every weekday between 08:55 and 09:10, database CPU pins at 100% and unrelated requests time out. The rest of the day is quiet. The dashboard itself is "only" 900 ms when tested alone.

Diagnosis

Two problems, and the second one only appears under concurrency.

``` csharp
// Problem 1: six sequential queries over the same rows.
var total     = await q.CountAsync();
var open      = await q.CountAsync(o => o.Status == OrderStatus.Open);
var shipped   = await q.CountAsync(o => o.Status == OrderStatus.Shipped);
var revenue   = await q.SumAsync(o => o.Total);
var openValue = await q.Where(o => o.Status == OrderStatus.Open).SumAsync(o => o.Total);
var biggest   = await q.MaxAsync(o => o.Total);
// Six scans of the same range, six round trips.

// Problem 2: the cache, which looks correct.
var cached = await _cache.GetStringAsync(key);
if (cached is null) {
    var data = await BuildDashboard(tenantId);          // 900ms
    await _cache.SetStringAsync(key, Serialize(data),
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
}
// At 09:00 the 15-minute entry expires. 200 users hit it within the same second.
// All 200 miss. All 200 run BuildDashboard concurrently. That is the stampede.
```

After — one query

``` csharp
var stats = await _db.Orders.AsNoTracking()
    .Where(o => o.TenantId == tenantId && o.CreatedAt >= from)
    .GroupBy(o => 1)                                   // single group = whole set
    .Select(g => new DashboardStats {
        Total      = g.Count(),
        Open       = g.Count(o => o.Status == OrderStatus.Open),
        Shipped    = g.Count(o => o.Status == OrderStatus.Shipped),
        Revenue    = g.Sum(o => o.Total),
        OpenValue  = g.Sum(o => o.Status == OrderStatus.Open ? o.Total : 0m),
        Biggest    = g.Max(o => o.Total)
    })
    .SingleAsync();
```

``` sql
-- Conditional aggregation: one pass, one round trip.
SELECT COUNT(*),
       COUNT(CASE WHEN Status = 1 THEN 1 END),
       COUNT(CASE WHEN Status = 2 THEN 1 END),
       SUM(Total),
       SUM(CASE WHEN Status = 1 THEN Total ELSE 0 END),
       MAX(Total)
FROM Orders WHERE TenantId = @t AND CreatedAt >= @from;
```

After — stampede protection

``` csharp
// .NET 9: HybridCache does this for you - L1 in-process, L2 distributed,
// and it collapses concurrent misses for the same key into one factory call.
var stats = await _hybrid.GetOrCreateAsync(
    key,
    async ct => await BuildDashboard(tenantId, ct),
    new HybridCacheEntryOptions {
        Expiration      = TimeSpan.FromMinutes(15),     // L2
        LocalCacheExpiration = TimeSpan.FromMinutes(2)  // L1
    });

// On older runtimes, collapse the misses yourself with a Lazy per key:
private static readonly ConcurrentDictionary<string, Lazy<Task<DashboardStats>>> _inflight = new();

async Task<DashboardStats> GetAsync(string key, Func<Task<DashboardStats>> factory) {
    var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<DashboardStats>>(factory));
    try { return await lazy.Value; }
    finally { _inflight.TryRemove(key, out _); }        // let the next miss rebuild
}
```

> **\[Tip\]**
>
> **Jitter the expiry too.** Fixed 15-minute TTLs across 40 tenants make all 40 expire in lockstep after a deploy. Randomising by ±10% (`TimeSpan.FromMinutes(15 * (0.9 + Random.Shared.NextDouble() * 0.2))`) spreads the rebuild load across a window instead of a spike. See [Caching layers](08-06-caching.md#be-m282) and [Aggregation: GROUP BY, HAVING, GROUPING SETS](07-02-the-relational-model-and-sql.md#be-d411).

> **\[Why it matters\]**
>
> **Why the isolated test lied.** 900 ms measured alone told you nothing about 200 concurrent copies of the same 900 ms query. Load characteristics do not compose linearly, and cache expiry synchronises load in a way no single-user test can reveal. When a problem is time-of-day shaped, look for something with a fixed TTL or a fixed schedule.

<a id="be-m298"></a>

#### Case: the search filter that stopped using indexes

Symptom

An advanced search screen is fast for most filter combinations and pathologically slow for a few. The plan cache shows thousands of near-identical single-use plans for this query shape.

Before

``` csharp
public async Task<List<OrderRow>> Search(SearchRequest r) {
    IEnumerable<Order> q = _db.Orders;                  // (1) typed as IEnumerable

    if (r.Status is not null)   q = q.Where(o => o.Status == r.Status);
    if (r.From is not null)     q = q.Where(o => o.CreatedAt.Date >= r.From.Value.Date);  // (2)
    if (r.Sku is not null)      q = q.Where(o => o.Lines.Any(l => l.Sku.ToLower() == r.Sku.ToLower())); // (3)
    if (r.CustomerIds is { Count: > 0 })
                                q = q.Where(o => r.CustomerIds.Contains(o.CustomerId));   // (4)
    return q.Select(Map).Take(200).ToList();
}
```

Four defects

1.  **The variable is typed `IEnumerable<Order>`.** Every `Where` binds to `Enumerable.Where`, not `Queryable.Where`. The entire `Orders` table is streamed to the web server and filtered in memory. This is the whole bug, and it is a one-word mistake ([IEnumerable vs IQueryable](03-10-collections-and-linq-in-net.md#be-b295)).
2.  **`.Date` on the column** becomes `CAST(CreatedAt AS date) >= @p` — non-sargable, so the index on `CreatedAt` can be scanned but not seeked ([Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160)).
3.  **`ToLower()` on both sides** is a function on the column *and* an unnecessary one: `Sku` is stored in a case-insensitive collation already ([String comparison: ordinal vs culture](01-03-text-is-numbers-too.md#be-m80), [Column types, sizing, and collation](07-02-the-relational-model-and-sql.md#be-m190)).
4.  **`Contains` over a variable-length list** produced a different SQL string per list length on the EF version in use, filling the plan cache ([IN lists, Contains, and parameter blowup](07-07-orms-and-ef-core.md#be-m164)).

After

``` csharp
public async Task<List<OrderRow>> Search(SearchRequest r, CancellationToken ct) {
    IQueryable<Order> q = _db.Orders.AsNoTracking()
        .Where(o => o.TenantId == r.TenantId);          // always scope the tenant first

    if (r.Status is not null)
        q = q.Where(o => o.Status == r.Status);

    if (r.From is not null) {                            // half-open range: sargable
        var from = r.From.Value.Date;
        q = q.Where(o => o.CreatedAt >= from);
    }
    if (r.To is not null) {
        var toExclusive = r.To.Value.Date.AddDays(1);
        q = q.Where(o => o.CreatedAt < toExclusive);
    }
    if (r.Sku is not null)
        q = q.Where(o => o.Lines.Any(l => l.Sku == r.Sku));   // EXISTS, collation does the rest

    if (r.CustomerIds is { Count: > 0 })
        q = q.Where(o => r.CustomerIds.Contains(o.CustomerId)); // EF 8+: one OPENJSON param

    return await q
        .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
        .Take(200)
        .Select(OrderRow.Projection)                     // shared, translatable projection
        .ToListAsync(ct);
}
```

``` csharp
// The shared projection, defined once as an expression so it still reaches SQL:
public sealed record OrderRow(long Id, string Customer, decimal Total, DateTimeOffset CreatedAt) {
    public static readonly Expression<Func<Order, OrderRow>> Projection =
        o => new OrderRow(o.Id, o.Customer.Name, o.Total, o.CreatedAt);
}
```

> **\[Tip\]**
>
> **Guardrail.** Composing an `IQueryable` with `if` statements is the correct pattern — it needs no `PredicateBuilder` and no expression rewriting ([Expression trees: how IQueryable works](03-10-collections-and-linq-in-net.md#be-m151)). The failure mode is purely the declared variable type, so make it impossible: have repository methods accept and return `IQueryable<T>` explicitly, and add an analyzer rule or code-review checklist item for `IEnumerable` variables assigned from a `DbSet`.

<a id="be-m299"></a>

#### Case: the export that timed out at 50,000 rows

Symptom

A CSV export works for small tenants and fails for large ones with a request timeout, and the pod is OOM-killed shortly after. Memory climbs to about 2 GB before the crash.

Before

``` csharp
public async Task<IActionResult> Export(int tenantId) {
    var orders = await _db.Orders
        .Include(o => o.Customer)
        .Include(o => o.Lines)
        .Where(o => o.TenantId == tenantId)
        .ToListAsync();                                  // 500k orders, tracked, in memory

    var sb = new StringBuilder();
    sb.AppendLine("OrderId,Customer,Sku,Qty,Total");
    foreach (var o in orders)
        foreach (var l in o.Lines)
            sb.AppendLine($"{o.Id},{o.Customer.Name},{l.Sku},{l.Qty},{o.Total}");

    return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "orders.csv");
}
```

Everything here materialises at once: the entity graph, the change-tracker snapshots, the `StringBuilder` chunks, the final string (UTF-16, so double the byte count), and then a full UTF-8 copy of it. Peak memory is roughly four times the payload, and the client sees nothing until the last row is formatted.

After

``` csharp
public async Task Export(int tenantId, CancellationToken ct) {
    Response.ContentType = "text/csv; charset=utf-8";
    Response.Headers.ContentDisposition = "attachment; filename=orders.csv";

    await using var writer = new StreamWriter(
        Response.Body, new UTF8Encoding(false), bufferSize: 64 * 1024);
    await writer.WriteLineAsync("OrderId,Customer,Sku,Qty,Total");

    var rows = _db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenantId)
        .OrderBy(o => o.Id)
        .SelectMany(o => o.Lines, (o, l) => new {          // flatten in SQL
            o.Id, Customer = o.Customer.Name, l.Sku, l.Qty, o.Total })
        .AsAsyncEnumerable();

    var buffer = new StringBuilder(256);
    await foreach (var r in rows.WithCancellation(ct)) {
        buffer.Clear()
            .Append(r.Id).Append(',')
            .Append(Csv(r.Customer)).Append(',')
            .Append(r.Sku).Append(',')
            .Append(r.Qty).Append(',')
            .Append(r.Total.ToString(CultureInfo.InvariantCulture));
        await writer.WriteLineAsync(buffer, ct);           // StringBuilder overload: no ToString()
    }
    await writer.FlushAsync(ct);
}

static string Csv(string v) =>
    v.AsSpan().IndexOfAny(',', '"', '\n') >= 0
        ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
```

Result

|                         | Before          | After     |
|-------------------------|-----------------|-----------|
| Peak managed memory     | ~2 GB           | ~8 MB     |
| Time to first byte      | never (timeout) | ~40 ms    |
| Objects on the LOH      | many, multi-MB  | none      |
| Scales with tenant size | memory          | time only |

> **\[Trap\]**
>
> **The cost of streaming.** The `DbDataReader` stays open for the whole export, holding a pooled connection and, on SQL Server, potentially shared locks depending on isolation level ([IAsyncEnumerable and await foreach](03-10-collections-and-linq-in-net.md#be-m154)). For a long export, that is a real resource commitment: use `AsNoTracking`, keep per-row work minimal, and consider running exports against a read replica or with snapshot isolation so they never block writers ([Transactions and isolation levels](07-04-transactions-and-isolation.md#be-a100)).

> **\[Tip\]**
>
> **Also worth knowing:** `SelectMany` flattens the parent-child shape in SQL, which is what lets the whole thing stream. Trying to stream `Include(o => o.Lines)` does not work as well — EF must buffer each parent's children to build the graph.

### Worked cases: writes, memory, infrastructure

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Worked cases: writes, memory, infrastructure</p>

<a id="be-m300"></a>

#### Case: the nightly import that took four hours

Symptom

A nightly job imports ~180,000 order rows from a supplier file. It started at 20 minutes a year ago and now runs past the morning batch window. Memory grows steadily through the run; the process never recovers it.

Before

``` csharp
public async Task Import(Stream csv, int tenantId) {
    var existing = await _db.Orders
        .Where(o => o.TenantId == tenantId)
        .ToListAsync();                                   // 1.2M tracked entities

    foreach (var line in ReadLines(csv)) {
        var row = Parse(line);
        var match = existing.FirstOrDefault(o => o.ExternalRef == row.Ref);  // O(n) each
        if (match is null)
            _db.Orders.Add(new Order { /* ... */ });
        else
            match.Total = row.Total;
        await _db.SaveChangesAsync();                     // one round trip PER ROW
    }
}
```

Diagnosis

| Defect                                        | Cost                                                     |
|-----------------------------------------------|----------------------------------------------------------|
| `FirstOrDefault` over a `List`, inside a loop | 180,000 × 1.2M ≈ 2×10<sup>11</sup> comparisons           |
| `SaveChanges` per row                         | 180,000 round trips, 180,000 transactions                |
| 1.2M tracked entities                         | snapshots + identity map; every `SaveChanges` walks them |
| One long-lived `DbContext`                    | memory never released until the job ends                 |

Note the interaction: `SaveChanges` gets *slower as the run progresses*, because change detection is proportional to the number of tracked entities. That is why the job degraded over a year rather than being slow from day one.

After

``` csharp
public async Task Import(Stream csv, int tenantId, CancellationToken ct) {
    // 1. Load only the keys you need to decide insert-vs-update, into a hash lookup.
    var index = await _db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenantId)
        .Select(o => new { o.ExternalRef, o.Id })
        .ToDictionaryAsync(x => x.ExternalRef, x => x.Id, StringComparer.Ordinal, ct);
    //    1.2M small entries instead of 1.2M entity graphs. O(1) probes instead of O(n).

    // 2. Stream the file; never hold the whole thing.
    await foreach (var batch in ReadRowsAsync(csv, ct).Chunk(1000).WithCancellation(ct)) {
        // 3. A fresh context per batch: bounded tracking, bounded memory.
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var inserts = new List<Order>();
        var updates = new List<(long Id, decimal Total)>();

        foreach (var row in batch) {
            if (index.TryGetValue(row.Ref, out var id)) updates.Add((id, row.Total));
            else inserts.Add(new Order { TenantId = tenantId, ExternalRef = row.Ref,
                                          Total = row.Total, CreatedAt = row.CreatedAt });
        }

        if (inserts.Count > 0) {
            db.Orders.AddRange(inserts);
            await db.SaveChangesAsync(ct);                // ONE batched command set
            foreach (var o in inserts) index[o.ExternalRef] = o.Id;
        }

        foreach (var group in updates.GroupBy(u => u.Total))   // few distinct totals
            await db.Orders
                .Where(o => group.Select(g => g.Id).Contains(o.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Total, group.Key), ct);
    }
}
```

Result

|                 | Before             | After         |
|-----------------|--------------------|---------------|
| Key comparisons | ~2×10<sup>11</sup> | 180,000       |
| Round trips     | ~180,000           | ~400          |
| Peak memory     | ~6 GB, growing     | ~200 MB, flat |
| Wall clock      | ~4 h               | ~3 min        |

> **\[Tip\]**
>
> **If inserts dominate, go further.** `SqlBulkCopy` writes hundreds of thousands of rows in seconds by bypassing the change tracker and the statement pipeline entirely. Stage into a temp table, then `MERGE` or an `UPDATE ... FROM` into the target — that is the set-based version of this whole job ([Set-based thinking, temp tables, and TVPs](07-02-the-relational-model-and-sql.md#be-m191), [Upsert: MERGE and the alternatives](07-02-the-relational-model-and-sql.md#be-m193)).

> **\[Trap\]**
>
> **Batch size is a tradeoff, not a constant.** Larger batches mean fewer round trips but longer transactions, more log growth, and a higher chance of lock escalation past ~5,000 locks on a table ([Locking, blocking, and deadlocks](07-04-transactions-and-isolation.md#be-m192)). 500–2,000 rows is the usual sweet spot. Measure at your row width rather than copying a number.

Recipe: [Recipe: import a large CSV into SQL Server](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m284)

<a id="be-m301"></a>

#### Case: the parser that allocated four gigabytes

Symptom

The importer's parsing stage is only 15% of wall-clock time but `dotnet-counters` shows gen-0 collections firing continuously and 12% of CPU spent in GC. Allocation rate is roughly 700 MB/s.

Before

``` csharp
record ImportRow(string Ref, decimal Total, DateTimeOffset CreatedAt, string Sku);

static ImportRow Parse(string line) {
    var parts = line.Split(',');                      // 1 array + 4 substrings
    return new ImportRow(
        parts[0].Trim(),                              // + 1 more string
        decimal.Parse(parts[1], CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(parts[2], CultureInfo.InvariantCulture),
        parts[3].ToUpper());                          // + 1 more string
}
// ~7 allocations per row x 180,000 rows x ~24 bytes minimum each,
// plus the line string itself from ReadLine. It adds up fast.
```

After

``` csharp
readonly record struct ImportRow(string Ref, decimal Total, DateTimeOffset CreatedAt, string Sku);

static bool TryParse(ReadOnlySpan<char> line, out ImportRow row) {
    row = default;
    var e = new SpanSplitter(line, ',');

    if (!e.TryNext(out var refSpan)) return false;
    if (!e.TryNext(out var totalSpan) ||
        !decimal.TryParse(totalSpan, NumberStyles.Number,
                          CultureInfo.InvariantCulture, out var total)) return false;
    if (!e.TryNext(out var dateSpan) ||
        !DateTimeOffset.TryParse(dateSpan, CultureInfo.InvariantCulture,
                                 DateTimeStyles.AssumeUniversal, out var created)) return false;
    if (!e.TryNext(out var skuSpan)) return false;

    row = new ImportRow(Intern(refSpan.Trim()), total, created, Upper(skuSpan));
    return true;
}

// A tiny allocation-free splitter over a span.
ref struct SpanSplitter {
    private ReadOnlySpan<char> _rest; private readonly char _sep;
    public SpanSplitter(ReadOnlySpan<char> s, char sep) { _rest = s; _sep = sep; }
    public bool TryNext(out ReadOnlySpan<char> field) {
        if (_rest.IsEmpty) { field = default; return false; }
        int i = _rest.IndexOf(_sep);
        if (i < 0) { field = _rest; _rest = default; }
        else { field = _rest[..i]; _rest = _rest[(i + 1)..]; }
        return true;
    }
}
```

``` csharp
// The two strings we genuinely must keep are deduplicated, because SKUs and
// refs repeat heavily across the file. This is the single largest win here.
private readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);

private string Intern(ReadOnlySpan<char> s) {
    if (_pool.GetAlternateLookup<ReadOnlySpan<char>>()      // .NET 9: probe without allocating
            .TryGetValue(s, out var hit)) return hit;
    var created = s.ToString();
    _pool[created] = created;
    return created;
}
// Pre-.NET 9, hash the span yourself or accept one ToString() on the miss path only.
```

``` csharp
// Read the file as spans, not strings, with a pooled buffer:
static async IAsyncEnumerable<ImportRow> ReadRowsAsync(
        Stream s, [EnumeratorCancellation] CancellationToken ct = default) {
    using var reader = new StreamReader(s, Encoding.UTF8, bufferSize: 64 * 1024);
    await reader.ReadLineAsync(ct);                       // header
    while (await reader.ReadLineAsync(ct) is { } line)
        if (TryParse(line, out var row)) yield return row;
}
```

Result

|                            | Before     | After                   |
|----------------------------|------------|-------------------------|
| Allocations per row        | ~7 objects | 0–2 (dedup misses only) |
| Total allocated, full file | ~4 GB      | ~90 MB                  |
| CPU in GC                  | ~12%       | \<1%                    |
| Parse stage wall clock     | baseline   | ~2.6× faster            |

> **\[Why it matters\]**
>
> **Was it worth it?** Here, yes — a batch job over a large file is exactly the regime where allocation rate becomes the bottleneck ([Span and Memory](02-07-memory-management-and-garbage-collection.md#be-b273)). The same rewrite applied to a method that runs twice per HTTP request would be strictly worse: harder to read, harder to change, and unmeasurable. The deciding question is never "can this be made allocation-free?" but "is allocation showing up in the profile?" ([Measure first: tools and method](12-04-scaling-and-performance.md#be-m275)).

> **\[Trap\]**
>
> **Constraints you inherit with `ref struct`.** `SpanSplitter` cannot cross an `await`, cannot be captured in a lambda, and cannot be a field. That is why the parsing is a synchronous method called from the async reader, rather than the two being fused ([Managed pointers: ref, in, and ref struct](01-05-memory.md#be-m35)).

<a id="be-m302"></a>

#### Case: the lookup that got slower every week

Symptom

A pricing service keeps a warm in-memory dictionary of roughly 400,000 SKU price rules. Lookups were sub-microsecond at launch. Six months later a hot path is spending most of its time in `Dictionary.FindValue`, and a few specific SKUs never resolve at all — they are re-added on every request, so the dictionary also grows without bound.

Before

``` csharp
public class PriceKey {
    public string Sku { get; set; } = "";
    public string Region { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }

    public override bool Equals(object? o) =>
        o is PriceKey k && k.Sku == Sku && k.Region == Region
                       && k.EffectiveFrom == EffectiveFrom;

    public override int GetHashCode() => Sku.Length ^ Region.Length;   // (1)
}

var rules = new Dictionary<PriceKey, PriceRule>();
// ...
key.EffectiveFrom = newDate;                                          // (2)
```

Two distinct bugs

1.  **A hash function with almost no entropy.** `Sku.Length ^ Region.Length` produces maybe 60 distinct values for 400,000 entries. Every bucket holds thousands of colliding entries, and each lookup walks that chain calling `Equals`. The dictionary has degraded from O(1) to O(n/60) — and it degrades *further* as the catalogue grows, which is exactly the "slower every week" symptom ([Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)).
2.  **A mutable key.** Mutating `EffectiveFrom` after insertion changes the hash. The entry is now sitting in the bucket for its *old* hash, so no lookup will ever find it and `Remove` will not remove it. It is a permanent leak and a permanent miss ([Equality: ==, Equals, GetHashCode, IEquatable](02-03-types.md#be-m56)).

After

``` csharp
// Immutable by construction, structural equality generated, no boxing on lookup.
public readonly record struct PriceKey(string Sku, string Region, DateOnly EffectiveFrom);
// record struct gives: IEquatable<PriceKey>, field-wise Equals,
// and a HashCode.Combine-based GetHashCode over all three fields.

// Built once at load, then read-only for its lifetime:
private static FrozenDictionary<PriceKey, PriceRule> _rules =
    LoadRules().ToFrozenDictionary();

public bool TryGetPrice(in PriceKey key, out PriceRule rule)
    => _rules.TryGetValue(key, out rule!);

// Publishing a new catalogue is a single reference assignment - atomic for readers,
// no lock, no torn state. The old dictionary is collected once readers drain.
public static void Publish(IEnumerable<PriceRule> fresh) =>
    Volatile.Write(ref _rules, fresh.ToFrozenDictionary(r => r.Key));
```

How it was actually found

``` csharp
// A 10-line diagnostic that turns "it feels slow" into a number.
static double CollisionRatio<K, V>(Dictionary<K, V> d) where K : notnull {
    var buckets = new HashSet<int>();
    foreach (var k in d.Keys) buckets.Add(k.GetHashCode());
    return (double)d.Count / Math.Max(1, buckets.Count);   // ideal is ~1.0
}
// Result here: 400,000 / 61 = 6,557 entries per distinct hash. Diagnosis complete.
```

> **\[Tip\]**
>
> **Rules that would have prevented both bugs.** Always implement `GetHashCode` with `HashCode.Combine` over exactly the fields `Equals` uses — never by hand ([Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)). Always make dictionary keys immutable; `readonly record struct` gives you immutability, correct equality, `IEquatable<T>`, and no boxing in one line ([records, readonly struct, ref struct](02-03-types.md#be-m54)). And for a build-once/read-forever table, `FrozenDictionary` is measurably faster than `Dictionary` ([FrozenDictionary and FrozenSet](03-02-arrays-and-lists.md#be-m108)).

<a id="be-m303"></a>

#### Case: the deadlock at month-end close

Symptom

On the last working day of each month, a batch of "close open orders" operations produces a burst of SQL Server error 1205 (deadlock victim). Users see intermittent failures; retrying by hand usually works. No code changed.

What the deadlock graph showed

``` sql
-- From the always-on system_health session - no reproduction needed:
SELECT CAST(event_data AS xml) FROM sys.fn_xe_file_target_read_file(
    'system_health*.xel', null, null, null)
WHERE object_name = 'xml_deadlock_report';

-- Two processes, each holding an exclusive key lock the other wants:
--   spid 71: holds Orders(Id=8814), waiting for Orders(Id=8790)
--   spid 93: holds Orders(Id=8790), waiting for Orders(Id=8814)
```

Before

``` csharp
public async Task CloseOrders(IEnumerable<long> orderIds, CancellationToken ct) {
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    foreach (var id in orderIds) {                        // arbitrary caller-supplied order
        var order = await _db.Orders.FindAsync([id], ct);
        order!.Status = OrderStatus.Closed;
        await _db.SaveChangesAsync(ct);
    }

    await _notifier.PublishAsync(new OrdersClosed(orderIds), ct);   // network call, IN the tx
    await tx.CommitAsync(ct);
}
```

Three causes, all fixable

1.  **Inconsistent lock ordering.** Two concurrent calls with overlapping, differently-ordered id lists take the same locks in opposite sequence. This is the deadlock, exactly.
2.  **A network call inside the transaction.** The publish holds every lock for the duration of an external round trip, widening the window from microseconds to hundreds of milliseconds.
3.  **No retry.** Deadlocks are transient by definition; treating them as fatal makes a recoverable condition into a user-visible failure.

After

``` csharp
public async Task CloseOrders(IReadOnlyCollection<long> orderIds, CancellationToken ct) {
    // 1. Deterministic order. This alone eliminates the deadlock cycle.
    var ids = orderIds.Distinct().Order().ToArray();

    // 2. Retry through EF's execution strategy so it composes with transactions.
    var strategy = _db.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async ct2 => {
        await using var tx = await _db.Database.BeginTransactionAsync(ct2);

        // 3. One set-based statement instead of N read-modify-write pairs.
        //    Fewer locks, held for less time, taken in index order.
        foreach (var chunk in ids.Chunk(500))
            await _db.Orders
                .Where(o => chunk.Contains(o.Id) && o.Status == OrderStatus.Open)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Closed), ct2);

        // 4. The notification becomes an outbox row in the same transaction.
        _db.Outbox.AddRange(ids.Select(id => OutboxMessage.For(new OrderClosed(id))));
        await _db.SaveChangesAsync(ct2);

        await tx.CommitAsync(ct2);
    }, ct);
    // A background dispatcher publishes outbox rows after commit, at least once.
}
```

``` csharp
// EF's retry needs to know deadlocks are transient. SQL Server's provider
// handles 1205 when configured:
options.UseSqlServer(cs, o => o.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(5),
    errorNumbersToAdd: [1205]));
```

> **\[Trap\]**
>
> **`ExecuteUpdate` bypasses the change tracker.** No `SaveChanges` interceptors run, no audit fields are stamped, no concurrency token is checked, and any already-tracked `Order` in this context is now stale ([Bulk operations and batching](07-07-orms-and-ef-core.md#be-m166)). Here that is acceptable because the outbox row carries the audit intent and the context is per-request. In a codebase where `SaveChanges` is doing important work, moving to `ExecuteUpdate` silently removes it — check first.

> **\[Tip\]**
>
> **The general prescription for deadlocks,** in order: sort your resource access, shorten the transaction (no I/O inside it, ever), fix the missing index that turned a seek into a lock-hungry scan, enable `READ_COMMITTED_SNAPSHOT` so readers stop blocking writers, and retry with jitter on top ([Locking, blocking, and deadlocks](07-04-transactions-and-isolation.md#be-m192), [Consistency across stores](07-06-non-relational-stores.md#be-m210)).

<a id="be-m304"></a>

#### Case: the cache that served the wrong tenant

Symptom

A customer reports seeing another company's order in a summary panel. It is intermittent, unreproducible on demand, and it is the most serious class of bug this system can have.

Before

``` csharp
public async Task<OrderSummary> GetSummary(long orderId) {
    var key = $"order-summary:{orderId}";                 // (1) no tenant, no version
    var json = await _cache.GetStringAsync(key);
    if (json is not null) return JsonSerializer.Deserialize<OrderSummary>(json)!;

    var summary = await Build(orderId);                   // (2) also unscoped
    await _cache.SetStringAsync(key, JsonSerializer.Serialize(summary));  // (3) no TTL
    return summary;
}
```

Diagnosis

Order ids are globally unique here, so the key collision was not the direct cause. The actual sequence was worse and more instructive:

1.  A support tool briefly ran with a mis-scoped query that returned an order outside the caller's tenant.
2.  That result was written to a cache entry **with no expiry**.
3.  The query bug was fixed and deployed. The poisoned cache entry survived the deploy and kept being served — for weeks.

A transient bug became permanent because the cache had no TTL and no version in the key. This is the ordinary shape of cache incidents: the cache does not cause the bug, it *preserves* it.

After

``` csharp
// Key format: app : schema-version : tenant : entity : id
private static string SummaryKey(int tenantId, long orderId)
    => $"the legacy system:v4:t{tenantId}:order-summary:{orderId}";

public async Task<OrderSummary> GetSummary(int tenantId, long orderId, CancellationToken ct) {
    var key = SummaryKey(tenantId, orderId);
    return await _hybrid.GetOrCreateAsync(key, async token => {
        var summary = await Build(tenantId, orderId, token);   // tenant is a query predicate
        return summary;
    },
    new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) },
    cancellationToken: ct);
}
```

``` csharp
// Defence in depth: make the wrong query impossible, not just uncached.
// A global query filter means every Orders query is tenant-scoped by construction.
modelBuilder.Entity<Order>()
    .HasQueryFilter(o => o.TenantId == _tenantContext.CurrentTenantId);

// And assert on the way out, so a leak fails loudly instead of rendering:
if (summary.TenantId != tenantId)
    throw new InvalidOperationException("Tenant scope violation");
```

> **\[Trap\]**
>
> **Related outage, same system.** While investigating, someone ran `KEYS the legacy system:*` against production Redis to find the bad entries. Redis executes commands on a single thread, so an O(n) scan over several million keys blocked every other client for seconds and took the site down. Use `SCAN` with a cursor, or — better — make the key version bump the eviction mechanism, so you never need to enumerate keys at all ([Redis: the data structures](07-06-non-relational-stores.md#be-m206)).

> **\[Tip\]**
>
> **Three rules that make this class of bug structurally impossible.** Put the tenant in every cache key. Put a schema version in every cache key, and bump it on any shape change — it makes both bad data and deserialization breakage evaporate on deploy. And give every entry a TTL, always: an entry with no expiry is a permanent commitment to a value you computed with code that no longer exists ([Cache key design and value format](07-06-non-relational-stores.md#be-m207)).

<a id="be-m305"></a>

#### Case: the audit trail that killed write throughput

Symptom

Order updates were 15 ms; after an audit feature shipped they are 400 ms and degrade further as the table grows. The audit table is now the largest object in the database and reads against it time out.

Before

``` csharp
public override async Task<int> SaveChangesAsync(CancellationToken ct = default) {
    foreach (var entry in ChangeTracker.Entries()
                 .Where(e => e.State is EntityState.Modified or EntityState.Added)) {
        AuditEntries.Add(new AuditEntry {
            EntityName = entry.Entity.GetType().Name,
            Before = JsonSerializer.Serialize(entry.OriginalValues.ToObject()),  // (1)
            After  = JsonSerializer.Serialize(entry.CurrentValues.ToObject()),
            At     = DateTimeOffset.UtcNow
        });
    }
    return await base.SaveChangesAsync(ct);
}
```

``` sql
-- The audit table as created:
CREATE TABLE AuditEntries (
  Id bigint IDENTITY PRIMARY KEY,
  EntityName nvarchar(max),          -- (2) unbounded, unindexable
  Before nvarchar(max), After nvarchar(max),
  At datetimeoffset
);
CREATE INDEX IX_Audit_Entity ON AuditEntries (EntityName);   -- (3) on nvarchar(max)? no.
```

Four compounding problems

1.  **Reflection plus serialization per entity, per save.** `ToObject()` materialises a boxed property bag; `JsonSerializer` is then invoked with default options constructed implicitly. Two full JSON documents per changed row ([Source generation vs reflection](02-02-compilers-interpreters-and-jit.md#be-m64), [System.Text.Json: shape and cost](08-08-serialization-and-data-formats.md#be-m208)).
2.  **Every string column is `nvarchar(max)`** because none was configured. Values over 8,000 bytes go off-row, so a single audit row can cost several extra page reads ([Type mapping: CLR to SQL](07-07-orms-and-ef-core.md#be-m170), [Column types, sizing, and collation](07-02-the-relational-model-and-sql.md#be-m190)).
3.  **The audit write is inside the business transaction,** so it extends every lock's lifetime.
4.  **No retention policy.** The table grows forever, so index maintenance, statistics, and backups all degrade continuously.

After

``` csharp
// 1. Capture only what changed, without serializing whole entities.
private static readonly JsonSerializerOptions AuditJson = new() {   // cached, not per call
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

public override async Task<int> SaveChangesAsync(CancellationToken ct = default) {
    var audits = new List<AuditEntry>();
    foreach (var entry in ChangeTracker.Entries<IAuditable>()) {
        if (entry.State is not (EntityState.Modified or EntityState.Added)) continue;

        var changes = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties) {
            if (!prop.IsModified) continue;                       // changed fields only
            changes[prop.Metadata.Name] = prop.CurrentValue;
        }
        if (changes.Count == 0) continue;

        audits.Add(new AuditEntry {
            EntityName = entry.Metadata.ShortName(),
            EntityId   = entry.Properties.First(p => p.Metadata.IsPrimaryKey()).CurrentValue?.ToString(),
            Changes    = JsonSerializer.Serialize(changes, AuditJson),
            At         = DateTimeOffset.UtcNow
        });
    }
    AuditEntries.AddRange(audits);
    return await base.SaveChangesAsync(ct);
}
```

``` csharp
// 2. Configure the columns. This is the change with the largest single effect.
modelBuilder.Entity<AuditEntry>(e => {
    e.Property(a => a.EntityName).HasMaxLength(64).IsUnicode(false).IsRequired();
    e.Property(a => a.EntityId).HasMaxLength(64).IsUnicode(false);
    e.Property(a => a.Changes).HasMaxLength(4000);      // spill to max only when needed
    e.HasIndex(a => new { a.EntityName, a.EntityId, a.At });
    e.ToTable(t => t.HasComment("Retention: 90 days, partitioned monthly"));
});
```

``` sql
-- 3. Retention, batched so it never escalates to a table lock.
WHILE 1 = 1
BEGIN
    DELETE TOP (2000) FROM AuditEntries WHERE At < DATEADD(day, -90, SYSUTCDATETIME());
    IF @@ROWCOUNT < 2000 BREAK;
    WAITFOR DELAY '00:00:00.100';
END
-- Better still on a partitioned table: SWITCH the oldest partition out. O(1).
```

Result

|                               | Before              | After                    |
|-------------------------------|---------------------|--------------------------|
| Serialized bytes per update   | 2 full entities     | changed fields only      |
| `JsonSerializerOptions` built | per call            | once                     |
| Audit row width               | off-row, multi-page | in-row for typical edits |
| Update latency                | ~400 ms, growing    | ~22 ms, stable           |
| Table size trend              | unbounded           | bounded at 90 days       |

> **\[Why it matters\]**
>
> **The architectural alternative.** If auditing must not affect write latency at all, take it out of the write path entirely: SQL Server temporal tables or change data capture derive the history from committed changes with no application code, and a downstream consumer can project it wherever you want it ([Consistency across stores](07-06-non-relational-stores.md#be-m210)). Doing it in `SaveChanges` is the pragmatic option, not the scalable one — and it is worth knowing which one you chose.

### Worked cases: systems and delivery

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Worked cases: systems and delivery</p>

<a id="be-m307"></a>

#### Case: the API that fell over at 09:00 on sale day

Symptom

At the sale opening, p99 went from 90 ms to 30 seconds within ninety seconds, then the pods began failing readiness and restarting. CPU on both the web tier and the database stayed under 40%. Adding instances made it *worse*.

What the metrics showed

``` text
threadpool-queue-length      0  ─►  4,200        rising
threadpool-thread-count     32  ─►  180          slowly climbing
db connections in use      18/100 ─► 100/100     pinned
"Timeout expired ... obtaining a connection from the pool"  thousands
GC % time                    2%  ─►  3%          normal
SQL Server CPU              22%  ─►  35%         idle
```

Diagnosis: three faults compounding

``` csharp
// 1. One sync-over-async call on the hot path, added months earlier.
var config = _configService.GetAsync(tenantId).Result;      // blocks a pool thread
// Harmless at 50 rps. At 2,000 rps, every request parks a thread for the
// duration of a network call (§136).

// 2. Thread starvation delayed the RELEASE of database connections, because
//    the continuation that disposes the scope needs a thread to run on
//    (§233). Connections were held far longer than the
//    queries took - so the pool exhausted while the database sat idle.

// 3. The product cache had a fixed 10-minute TTL set at deploy time, so every
//    instance expired within the same second and all of them rebuilt at once
//    (§297).
```

**Why adding instances made it worse:** each new pod brought its own pool of 100 connections. The database's connection limit became the next bottleneck, and the extra pods spent their first seconds JIT-compiling cold paths ([The JIT: tiers, OSR, and dynamic PGO](02-02-compilers-interpreters-and-jit.md#be-m40)).

Fixes, in the order applied

| Change                                                 | Effect                           |
|--------------------------------------------------------|----------------------------------|
| Remove the `.Result`; `await` to the entry point       | queue length back to ~0          |
| Jitter cache TTL by ±10%, add stampede collapsing      | rebuild spread over a minute     |
| Cap pool at 40/instance; size against the server limit | database stops being the ceiling |
| Add per-tenant rate limiting with `429`                | degrades instead of collapsing   |
| Warm the JIT and the cache in a startup task           | no cold-start cliff on scale-out |

> **\[Why it matters\]**
>
> **The lesson is about compounding, not any single bug.** Each of the three faults was survivable alone and had been for months. Under load they amplified each other, and the visible symptom — connection pool timeouts — pointed at the component that was *least* at fault. When a system fails only under load, look for a resource being held longer than the work takes, not for the work being slow ([Load testing and capacity](12-04-scaling-and-performance.md#be-m272)).

<a id="be-m308"></a>

#### Case: the service split that made everything slower

Symptom

Six months after extracting Customers, Pricing, and Inventory from the monolith, the order-detail page went from 120 ms to 900 ms, deploys required coordination across three teams, and a Customers outage took down checkout.

What the trace showed

``` text
GET /orders/1042                                       880 ms
├── SELECT Orders + Lines                                12 ms
├── HTTP GET customers/9                                 95 ms
├── HTTP GET pricing/rules?sku=A-1                       88 ms
├── HTTP GET pricing/rules?sku=B-2                       91 ms      ← per line
├── HTTP GET pricing/rules?sku=C-3                       87 ms      ← per line
├── HTTP GET inventory/stock?sku=A-1                     84 ms
├── ... 3 more                                          260 ms
└── HTTP GET customers/9/address                         89 ms      ← again
```

**It is N+1, at the service layer** ([N+1 and how to see it](07-07-orms-and-ef-core.md#be-m156)). The same bug the ORM taught everyone to avoid, reintroduced across a network where each hop costs 100× a database round trip ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705)).

Root cause

The split was made along *entity* lines, not bounded contexts ([Decomposition: contexts and data ownership](12-01-monoliths-modules-and-services.md#be-m247)). An order genuinely needs customer, price, and stock facts together, so every boundary crossing was on the critical path of a single page.

``` text
// What was done                    What it should have been
Customer service                    ORDERS context owns the order, and keeps
Pricing service                     the customer name, the price at sale, and
Inventory service                   the reserved quantity as ITS OWN facts,
Orders service                      fed by events (§249).
```

The fix, without re-merging

``` csharp
// 1. Batch what must stay synchronous - one call, not N.
var prices = await _pricing.GetManyAsync(order.Lines.Select(l => l.Sku), ct);

// 2. Denormalise the facts the order needs at the time it needs them.
//    UnitPriceAtSale was ALREADY the right model (§189) -
//    it just was not being used, and pricing was re-fetched on every read.

// 3. Keep a local read model of customer display data, updated by events.
public sealed class CustomerSnapshotConsumer : IConsumer<CustomerRenamed> {
    public Task Consume(ConsumeContext<CustomerRenamed> ctx) =>
        _db.CustomerSnapshots.Where(c => c.Id == ctx.Message.CustomerId)
           .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, ctx.Message.Name));
}
// The order page now reads one database. 880 ms -> 40 ms, and a Customers
// outage no longer affects checkout (§246).
```

> **\[Trap\]**
>
> **The tell that the boundary was wrong** was there from the start: every feature touched three services, and the three always deployed together ([Decomposition: contexts and data ownership](12-01-monoliths-modules-and-services.md#be-m247)). That is a distributed monolith, and no amount of caching or protocol tuning fixes it — the coupling is in the model, not the transport.

<a id="be-m309"></a>

#### Case: the bug that only appeared on ARM

Symptom

After moving from x64 to Graviton instances to cut costs, roughly one request in 50,000 threw a `NullReferenceException` inside a config lookup that had been stable for three years. It never reproduced locally, never in staging, and the stack trace pointed at a line that could not be null.

The code

``` csharp
public sealed class ConfigCache {
    private Dictionary<string, Rule>? _rules;
    private bool _loaded;

    public Rule Get(string key) {
        if (!_loaded) {                       // (2) reads _loaded
            _rules = Load();                  // (1a)
            _loaded = true;                   // (1b)
        }
        return _rules![key];                  // (3) NRE here, occasionally
    }
}
```

Diagnosis

Nothing orders the two writes. The compiler, the JIT, or the CPU may make `_loaded = true` visible to another thread *before* `_rules` is visible. A second thread then sees `_loaded == true`, skips initialisation, and dereferences a null `_rules`.

``` csharp
// x86-64 has a strongly ordered memory model: stores become visible in program
// order, so this code was accidentally correct for three years.
// ARM64 is weakly ordered. The same IL, the same JIT, different guarantees
// (§131).
//
// It is not a compiler bug and not an ARM bug. The code was always broken;
// x64 was hiding it.
```

Fix

``` csharp
// Option 1 - let the runtime do it. Static initialisation is guaranteed
// once, with the right barriers, at no cost (§43).
private static readonly Dictionary<string, Rule> Rules = Load();

// Option 2 - Lazy<T>, when it must be per-instance or deferred.
private readonly Lazy<Dictionary<string, Rule>> _rules = new(Load);
public Rule Get(string key) => _rules.Value[key];

// Option 3 - publish the whole thing with ONE reference write, which is
// atomic, and read it with Volatile.Read (§134).
private Dictionary<string, Rule>? _rules;
public Rule Get(string key) {
    var snapshot = Volatile.Read(ref _rules);
    if (snapshot is null) {
        snapshot = Load();
        Volatile.Write(ref _rules, snapshot);   // fully built BEFORE published
    }
    return snapshot[key];
}
```

> **\[Why it matters\]**
>
> **Why this case is worth knowing even if you never see it.** An entire class of latent concurrency bug is masked by x86's memory model and unmasked by ARM — and ARM is now the default on Apple hardware, on Graviton, and on much of the cloud. A codebase written and tested only on x64 has never had these bugs tested for. The audit is mechanical: find every field written by one thread and read by another without a lock, `Interlocked`, or `Volatile` ([Interlocked, volatile, and what lock guarantees](05-02-threads-and-locks.md#be-m132)).

<a id="be-m310"></a>

#### Case: right in staging, wrong in production

Symptom

A tenant-scoped report ran in 200 ms in staging with a full production data copy, and in 45 seconds in production — intermittently. Some mornings it was fast. After a failover it was reliably slow for hours, then recovered on its own.

Diagnosis

``` sql
-- The query, unchanged for two years:
SELECT ... FROM Orders o WHERE o.TenantId = @tenantId AND o.CreatedAt >= @from

-- Two plans existed in the cache, for the same query text:
--   Plan A: Index Seek on IX_Orders_Tenant_Created, nested loops
--           - compiled for @tenantId = 7   (400 rows)
--   Plan B: Clustered Index Scan, hash match
--           - compiled for @tenantId = 3   (1.2 MILLION rows, 60% of the table)

-- Whichever tenant ran the report FIRST after a restart or a statistics update
-- got a plan compiled for their data. Everyone else inherited it.
-- Plan A applied to tenant 3: 1.2M nested-loop iterations.  (§199)
```

This is **parameter sniffing** meeting extreme data skew ([Statistics and cardinality estimation](07-03-indexes-and-query-plans.md#be-d436)). Staging never reproduced it because staging ran the report as one tenant, so it only ever had one plan — and by luck, the right one.

Options considered

| Approach                            | Effect                          | Cost                       |
|-------------------------------------|---------------------------------|----------------------------|
| `OPTION (RECOMPILE)`                | always optimal for this value   | compile on every execution |
| `OPTIMIZE FOR UNKNOWN`              | an average plan, never terrible | never optimal either       |
| Split the query by tenant size      | optimal for both                | two code paths to maintain |
| A filtered index for the big tenant | optimal, no code change         | one index per outlier      |
| Force a plan via Query Store        | stops the flipping              | freezes it; may age badly  |

``` csharp
// Chosen: RECOMPILE, because the report runs a few hundred times a day and a
// ~2 ms compile is irrelevant next to a 45 s worst case.
var rows = await db.Database
    .SqlQuery<ReportRow>($"...  OPTION (RECOMPILE)")
    .ToListAsync(ct);

// Plus a Query Store alert on plan regression, so the next one is noticed
// rather than reported by a user (§226).
```

> **\[Trap\]**
>
> **Skew invalidates "we tested with production data".** A copy of production has the same *rows* but not the same *access pattern*, and plan choice depends on which parameter arrives first. Any multi-tenant system with one dominant tenant should test the report as the largest tenant, the smallest, and in both orders ([Load testing and capacity](12-04-scaling-and-performance.md#be-m272)).

<a id="be-m311"></a>

#### Case: the migration that could not be rolled back

Symptom

A release renamed `Orders.Total` to `Orders.GrossTotal` and changed its meaning to include tax. Deployed at 14:00. By 14:03, half the instances were throwing `SqlException: Invalid column name 'Total'`. The rollback made it worse.

Timeline

``` text
14:00  migration runs: DROP COLUMN Total; ADD GrossTotal (populated from Total * 1.1)
14:00  rolling deploy begins - 4 of 8 instances updated
14:01  the 4 OLD instances query [Total]. It no longer exists. 500s.
14:03  decision to roll back
14:04  code rolled back on all 8 - now ALL instances query [Total]. Still gone.
14:06  DOWN COMPLETELY. The schema cannot be un-migrated: the original Total
       values were overwritten by a lossy transform. Restore from backup is
       the only option, and it costs 40 minutes of writes.
```

Root cause

A single destructive migration deployed alongside dependent code, on a rolling deploy that by definition runs two versions at once ([Deployment strategies and rollback](11-04-ci-cd-pipelines.md#be-a220)). Two independent errors compounded: the schema was not backward compatible, and the data transform was not reversible.

What it should have been

``` sql
-- Release 1 (additive, old code unaffected, instantly revertible)
ALTER TABLE Orders ADD GrossTotal decimal(18,4) NULL;
-- code: writes BOTH Total and GrossTotal; reads Total

-- Release 2 (backfill, batched and restartable) (§221)
UPDATE TOP (5000) Orders SET GrossTotal = Total * 1.1 WHERE GrossTotal IS NULL;
-- code: writes both; reads GrossTotal with a fallback to Total

-- Release 3 (only once no instance reads Total, verified by telemetry)
ALTER TABLE Orders ALTER COLUMN GrossTotal decimal(18,4) NOT NULL;
ALTER TABLE Orders DROP COLUMN Total;
```

> **\[Why it matters\]**
>
> **Two separate lessons, both worth stating.** First, expand/contract is not ceremony — it is what makes each step independently revertible ([Schema evolution: expand and contract](07-05-schema-design-and-migrations.md#be-m220)). Second, **renaming a field and changing its meaning in the same release is never acceptable**, even with expand/contract: `GrossTotal` was a new fact, so it deserved a new column and a new name, and `Total` should have been retired separately once nothing read it ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223)).

> **\[Tip\]**
>
> **The telemetry step is the one people skip.** "No instance reads `Total` any more" is a claim you can verify — log every read of the legacy path for a week and check the count is zero. Without it, release 3 is a guess, and it is the guess that causes the incident.

<a id="be-m312"></a>

#### Case: the outbox that lost messages

Symptom

Finance reported that roughly 0.2% of confirmed orders had no invoice. No errors anywhere. The orders were correct; the downstream effect simply never happened. It had been running for eight months.

The dispatcher

``` csharp
protected override async Task ExecuteAsync(CancellationToken ct) {
    while (!ct.IsCancellationRequested) {
        var batch = await _db.Outbox.Where(m => !m.Sent)
                                    .OrderBy(m => m.Id).Take(100).ToListAsync(ct);
        foreach (var m in batch) {
            await _bus.PublishAsync(m.Payload, ct);      // (1)
            m.Sent = true;                                // (2)
        }
        await _db.SaveChangesAsync(ct);                   // (3)
        await Task.Delay(1000, ct);
    }
}
```

Three defects

1.  **Publish before persisting the flag, then save once at the end.** If the process is killed between (1) and (3) — every deploy, every scale-in — messages were published but not marked sent, so they republish. That is at-least-once and is fine. But the reverse also happened: `SaveChanges` succeeded for a batch where a later publish threw, marking *unpublished* messages as sent. Those were lost permanently.
2.  **No claim step.** Two dispatcher instances after a scale-out read the same batch and both published it.
3.  **The consumer was not idempotent**, so the duplicates from (2) created duplicate invoices — which someone had "fixed" months earlier by adding a de-duplication filter that also silently dropped legitimate retries.

Fix

``` csharp
// Claim, then publish, then mark - one message at a time, each step durable.
var claimed = await _db.Outbox
    .Where(m => m.Status == Pending && m.NextAttemptUtc <= now)
    .OrderBy(m => m.Id).Take(100)
    .ExecuteUpdateAsync(s => s
        .SetProperty(m => m.Status, Claimed)
        .SetProperty(m => m.ClaimedBy, _instanceId)
        .SetProperty(m => m.ClaimExpiresUtc, now.AddMinutes(5)), ct);
// A claim with an EXPIRY, so a dead instance's work is picked up again.

foreach (var m in await LoadClaimedAsync(ct)) {
    try {
        await _bus.PublishAsync(m.Payload, m.Id, ct);   // message id = idempotency key
        await MarkSentAsync(m.Id, ct);                  // its own transaction
    } catch (Exception ex) {
        await ScheduleRetryAsync(m.Id, backoffWithJitter, ex, ct);   // never lose it
    }
}
```

> **\[Trap\]**
>
> **At-least-once plus a non-idempotent consumer equals corruption, and the "fix" is usually worse.** Deduplicating by content or by a time window drops real retries as well as duplicates — which is how a duplicate-invoice bug becomes a missing-invoice bug that takes eight months to notice. Idempotency belongs in the consumer, keyed on a stable message id ([Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249), [Consistency across stores](07-06-non-relational-stores.md#be-m210)).

> **\[Tip\]**
>
> **Alert on the queue, not on errors.** This failure produced no errors at all. What would have caught it on day one: an `outbox.pending` gauge with an alert on age, and a daily reconciliation asserting *confirmed orders == invoices created*. Silent divergence needs a check that compares two independently-derived numbers ([Metrics: what to measure](11-06-observability.md#be-e540), [Case: the report whose totals were wrong](#be-m295)).

<a id="be-m313"></a>

#### Case: the search that degraded as data grew

Symptom

Product search was 30 ms at launch and 6 seconds two years later. Latency tracked catalogue size almost exactly linearly, which is the signature of a scan.

Before

``` csharp
var results = await db.Products
    .Where(p => p.TenantId == tenantId &&
                (p.Name.ToLower().Contains(term.ToLower()) ||
                 p.Description.ToLower().Contains(term.ToLower()) ||
                 p.Sku.ToLower().Contains(term.ToLower())))
    .OrderBy(p => p.Name)
    .Take(50).ToListAsync(ct);
```

``` text
-- Generates three LIKE '%...%' with LOWER() on each column:
WHERE LOWER([Name]) LIKE '%' + @term + '%' OR LOWER([Description]) LIKE ...
-- Leading wildcard: no index can seek (§200).
-- LOWER() on the column: non-sargable even for a prefix (§160).
-- Description is nvarchar(max), stored off-row: every row costs extra pages
--   (§194).
-- Result: full scan of the table, plus an off-row fetch per row, per search.
```

After — three layers, cheapest first

``` csharp
// LAYER 1: an indexed, folded search key. Handles exact and prefix matching,
// which is most real traffic (§86).
ALTER TABLE Products ADD SearchKey AS (LOWER(CONCAT(Sku,' ',Name))) PERSISTED;
CREATE INDEX IX_Products_Search ON Products (TenantId, SearchKey) INCLUDE (Name, Price);

db.Products.Where(p => p.TenantId == tenantId && p.SearchKey.StartsWith(term))
// -> LIKE 'term%' : SEEKABLE. This alone handled ~70% of queries in 4 ms.

// LAYER 2: full-text for word search across the description
// (§216).
CREATE FULLTEXT INDEX ON Products (Name, Description) KEY INDEX PK_Products;
// CONTAINS/FREETEXT with RANK, instead of three LIKEs.

// LAYER 3: a dedicated engine, only if relevance tuning, facets, or typo
// tolerance are actually required - with a sync pipeline and a staleness
// answer, which layers 1 and 2 do not need.
```

Result

|                            | Before               | After                           |
|----------------------------|----------------------|---------------------------------|
| Plan                       | clustered index scan | index seek                      |
| Pages read (200k products) | ~180,000             | ~40                             |
| Latency                    | ~6,000 ms            | ~4 ms prefix / ~35 ms full-text |
| Scales with catalogue size | linearly             | logarithmically                 |

> **\[Why it matters\]**
>
> **The general shape.** Anything whose latency grows linearly with table size is scanning, and the question is always the same: *can this predicate seek?* A leading wildcard cannot, a function on the column cannot, and an implicit type conversion cannot ([Seeks, scans, and residual predicates](07-03-indexes-and-query-plans.md#be-m200)). Reach for a search engine when you need relevance, not when you need speed — speed is usually an index away, and a second system is a permanent operational cost.

> **\[Tip\]**
>
> **Solve for the traffic you have.** Instrumenting the search box showed most queries were short prefixes of a SKU or product name — which layer 1 handles entirely. Building layer 3 first would have been months of work to serve the 5% of queries that layers 1 and 2 could not.
