---
id: "14-1"
number: "14.1"
title: "Recipes: code you will actually write"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 1607
minutes: 7
---

# 14.1 Recipes: code you will actually write

> **Quick look.** Ready-to-use recipes for the code you write most often.
>
> - Paginated list endpoint: tenant first in the predicate and index, half-open date ranges, a sort allow-list, a unique tiebreaker, projection to DTOs.
> - Idempotent command handler: look up the idempotency key, let a unique constraint decide concurrent duplicates, write the outbox in the same transaction.
> - Report queries: route them to a replica, keep them sargable, and bound their date range.
> - Related recipes live in their topic chapters: CSV import (8.9), resilient client (8.9), cached read (8.6), queue worker (8.7), file upload (8.9), concurrent-safe update (7.4).


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A filtered, sorted, paginated endpoint end to end

Agents need to list a tenant's bookings filtered by date range and status, sorted by a few columns, page by page.

*The handler*

```csharp
static readonly Dictionary<string, Expression<Func<Booking, object>>> Sorts = new()
{
    ["created"] = b => b.CreatedAt, ["total"] = b => b.Total,          // allow-list: each needs an index
};

app.MapGet("/bookings", async ([AsParameters] BookingQuery q, ITenant t, BookingDb db, CancellationToken ct) =>
{
    var query = db.Bookings.AsNoTracking().Where(b => b.TenantId == t.Id);
    if (q.From is { } from) query = query.Where(b => b.CreatedAt >= from);
    if (q.To is { } to)     query = query.Where(b => b.CreatedAt < to);        // half-open range
    if (q.Status is { } s)  query = query.Where(b => b.Status == s);
    var sort = Sorts.GetValueOrDefault(q.Sort ?? "created", Sorts["created"]);
    var page = await query.OrderByDescending(sort).ThenByDescending(b => b.Id)   // unique tiebreaker
        .Skip(q.Page * q.Size).Take(Math.Clamp(q.Size, 1, 100))
        .Select(b => new BookingRow(b.Id, b.Code, b.CreatedAt, b.Total, b.Status))
        .ToListAsync(ct);
    return Results.Ok(page);
});
// index: (TenantId, CreatedAt DESC, Id DESC) INCLUDE (Code, Total, Status)
```

> **Lesson:** Every choice traces to a rule: tenant first, sargable ranges, an allow-listed sort, a tiebreaker, a projection and a capped page.

### Recipes: code you will actually write

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Recipes: code you will actually write</p>

<a id="be-m285"></a>

#### Recipe: a filtered, sorted, paginated list endpoint

``` csharp
public sealed record OrderListRequest {
    public OrderStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Search { get; init; }
    public string Sort { get; init; } = "-createdAt";     // '-' prefix = descending
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed record OrderListResponse(IReadOnlyList<OrderRow> Items, string? NextCursor);
```

``` csharp
app.MapGet("/orders", async ([AsParameters] OrderListRequest req, ITenant tenant,
                             AppDb db, CancellationToken ct) => {
    if (req.Limit is < 1 or > 200)
        return Results.ValidationProblem(new() { ["limit"] = ["Must be 1-200"] });

    IQueryable<Order> q = db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenant.Id);              // ALWAYS first (§212)

    if (req.Status is { } s)  q = q.Where(o => o.Status == s);
    if (req.From is { } f)    q = q.Where(o => o.CreatedAt >= f.ToDateTime(TimeOnly.MinValue));
    if (req.To is { } t)      q = q.Where(o => o.CreatedAt <  t.AddDays(1).ToDateTime(TimeOnly.MinValue));
    if (!string.IsNullOrWhiteSpace(req.Search))
        q = q.Where(o => o.SearchKey.StartsWith(req.Search));   // seekable (§313)

    // Sort: allow-list only, mapped to indexed columns (§240).
    var desc = req.Sort.StartsWith('-');
    var field = desc ? req.Sort[1..] : req.Sort;
    if (!Sortable.Contains(field))
        return Results.ValidationProblem(new() { ["sort"] = ["Unsupported field"] });

    // Keyset: decode the cursor and seek past it (§296).
    if (Cursor.Decode(req.Cursor) is { } c)
        q = desc
            ? q.Where(o => o.CreatedAt < c.CreatedAt || (o.CreatedAt == c.CreatedAt && o.Id < c.Id))
            : q.Where(o => o.CreatedAt > c.CreatedAt || (o.CreatedAt == c.CreatedAt && o.Id > c.Id));

    q = desc
        ? q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
        : q.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);       // tiebreaker, always

    var page = await q.Take(req.Limit + 1)                     // +1 probes for "more"
        .Select(OrderRow.Projection)                           // shared expression
        .ToListAsync(ct);

    var hasMore = page.Count > req.Limit;
    if (hasMore) page.RemoveAt(req.Limit);

    return Results.Ok(new OrderListResponse(page,
        hasMore ? new Cursor(page[^1].CreatedAt, page[^1].Id).Encode() : null));
})
.RequireAuthorization("orders:read")
.CacheOutput(p => p.NoCache());                                // authenticated: never shared
```

``` sql
-- The index this endpoint requires. Without it, none of the above matters.
CREATE INDEX IX_Orders_Tenant_Created
  ON Orders (TenantId, CreatedAt DESC, Id DESC)
  INCLUDE (CustomerId, Status, Total);        -- covering (§197)
```

> **\[Tip\]**
>
> **Every design decision here is traceable to a section.** Tenant first in the predicate and in the index; half-open date range so it stays sargable; a sort allow-list because each option is an index commitment; a unique tiebreaker so pages cannot repeat; a projection so `Notes` is never fetched; `Take(limit + 1)` so there is no `COUNT(*)`.

<a id="be-m286"></a>

#### Recipe: an idempotent command handler

``` csharp
public sealed class PlaceOrderHandler(AppDb db, IStockService stock, TimeProvider time) {

    public async Task<Result<OrderDto>> HandleAsync(
            PlaceOrder cmd, string idempotencyKey, CancellationToken ct) {

        // 1. Already done? Return the SAME response, not a new order.
        var prior = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.TenantId == cmd.TenantId, ct);
        if (prior is not null)
            return Result.Ok(JsonSerializer.Deserialize<OrderDto>(prior.Response)!);

        // 2. Cross-aggregate check, before we take any locks (§258).
        if (!await stock.IsAvailableAsync(cmd.Lines, ct))
            return Result.Conflict("Insufficient stock");

        // 3. Domain decides. Invariants live in the aggregate (§254).
        var order = Order.Place(cmd.TenantId, cmd.CustomerId,
                                cmd.Lines.Select(l => new OrderLine(l.Sku, l.Qty, l.UnitPrice)),
                                time.GetUtcNow());
        db.Orders.Add(order);

        var dto = OrderDto.From(order);

        // 4. Order, idempotency record, and outbox message: ONE transaction
        //    (§250).
        db.IdempotencyRecords.Add(new IdempotencyRecord {
            Key = idempotencyKey, TenantId = cmd.TenantId,
            Response = JsonSerializer.Serialize(dto),
            CreatedAt = time.GetUtcNow()
        });
        db.Outbox.Add(OutboxMessage.For(new OrderPlaced(
            order.Id, order.CustomerId, order.Total, time.GetUtcNow())));

        try {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "UX_Idempotency_Key")) {
            // 5. Two concurrent requests with the same key. The other one won.
            //    Re-read its response rather than failing (§193).
            var winner = await db.IdempotencyRecords.AsNoTracking()
                .FirstAsync(r => r.Key == idempotencyKey && r.TenantId == cmd.TenantId, ct);
            return Result.Ok(JsonSerializer.Deserialize<OrderDto>(winner.Response)!);
        }

        return Result.Ok(dto);
    }
}
```

``` sql
-- The unique constraint is what makes step 5 correct. A check-then-insert
-- without it is a race (§188).
CREATE UNIQUE INDEX UX_Idempotency_Key ON IdempotencyRecords (TenantId, [Key]);
-- Plus a retention job: these are only useful for hours (§305).
```

> **\[Why it matters\]**
>
> **Three failure modes are covered and each needed a different tool.** A client retry after a lost response — the lookup at step 1. Two concurrent identical requests — the unique constraint at step 5. A crash between saving the order and publishing the event — the outbox at step 4. Handling only the first is the common case, and it is the one that produces duplicate orders under load ([Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312)).

<a id="be-m291"></a>

#### Recipe: a report query that stays fast

``` csharp
// Requirement: revenue and order count by month and country, for a date range,
// with the top 5 products per country. The naive version is four queries and
// a fan-out bug (§295).

public async Task<RevenueReport> BuildAsync(
        int tenantId, DateOnly from, DateOnly to, CancellationToken ct) {

    var fromUtc = from.ToDateTime(TimeOnly.MinValue);
    var toUtc   = to.AddDays(1).ToDateTime(TimeOnly.MinValue);   // half-open

    // ONE pass, conditional aggregation, order grain only - no join to Lines
    // (§186).
    var summary = await db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenantId
                 && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                 && o.Status != OrderStatus.Cancelled)
        .GroupBy(o => new { Country = o.Customer.Country, Month = o.CreatedAt.Month })
        .Select(g => new MonthlyRow {
            Country   = g.Key.Country,
            Month     = g.Key.Month,
            Orders    = g.Count(),
            Revenue   = g.Sum(o => o.Total),
            Shipped   = g.Sum(o => o.Status == OrderStatus.Shipped ? o.Total : 0m),
            AvgOrder  = g.Average(o => o.Total),
            Biggest   = g.Max(o => o.Total)
        })
        .ToListAsync(ct);

    // Top-N per group: a window function via raw SQL, because EF cannot
    // express it (§185, §167).
    var top = await db.Database.SqlQuery<TopProductRow>($"""
        WITH ranked AS (
            SELECT c.Country, l.Sku,
                   SUM(l.Qty * l.UnitPriceAtSale) AS Revenue,
                   ROW_NUMBER() OVER (PARTITION BY c.Country
                                      ORDER BY SUM(l.Qty * l.UnitPriceAtSale) DESC) AS rn
            FROM Orders o
            JOIN Customers  c ON c.Id = o.CustomerId
            JOIN OrderLines l ON l.OrderId = o.Id
            WHERE o.TenantId = {tenantId}
              AND o.CreatedAt >= {fromUtc} AND o.CreatedAt < {toUtc}
              AND o.Status <> 4
            GROUP BY c.Country, l.Sku)
        SELECT Country, Sku, Revenue FROM ranked WHERE rn <= 5
        """).ToListAsync(ct);

    return new RevenueReport(summary, top.ToLookup(t => t.Country));
}
```

``` sql
-- Two indexes make both queries seek rather than scan.
CREATE INDEX IX_Orders_Report ON Orders (TenantId, CreatedAt)
  INCLUDE (CustomerId, Status, Total)  WHERE Status <> 4;   -- filtered
CREATE INDEX IX_OrderLines_Order ON OrderLines (OrderId) INCLUDE (Sku, Qty, UnitPriceAtSale);
```

> **\[Tip\]**
>
> **Report queries belong on a replica.** They scan more than OLTP queries do and hold reads longer, competing for exactly the buffer pool your transactional workload needs ([Analytical modeling: grain, facts, dimensions](07-05-schema-design-and-migrations.md#be-m215)). Route them to a read replica, or at minimum run them under snapshot isolation so they never block writers ([Read replicas and read/write splitting](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m264)).

> **\[Why it matters\]**
>
> **Note the two deliberate boundaries.** The summary is LINQ because it translates cleanly and stays refactorable; the top-N-per-group is raw SQL because EF cannot express a window function, and forcing it would mean pulling every line into memory. Mixing the two on purpose — LINQ by default, SQL where it is genuinely better — is a healthy architecture, not a compromise.
