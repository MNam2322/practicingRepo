---
id: "7-9"
number: "7.9"
title: "Database optimisation casebook"
part: 7
part_title: "Storing data"
words: 24908
minutes: 108
---

# 7.9 Database optimisation casebook

> **Quick look.** Eighteen slow or broken database workloads on SQL Server and PostgreSQL: the evidence, the fix, the numbers after, and the cost.
>
> - Find the query with the highest total cost, read the actual plan, and compare estimated with actual rows before adding anything.
> - Composite indexes: equality columns first, the range or sort column last.
> - Filtered and partial indexes only match a literal predicate; a parameter hides them on SQL Server.
> - Type mismatches (nvarchar parameter, varchar column), OR across columns, scalar UDFs and JSON lookups each defeat an index in their own way.
> - Exact counts for pagination are expensive; fetch one extra row, cap the count, or use an estimate.
> - Parameter sniffing and stale statistics after a bulk load cause most "same query, suddenly slow" incidents.
> - Delete in batches or drop whole partitions; update hot counters with one conditional statement; use RCSI, not NOLOCK.
> - Random GUID keys, bloat from non-HOT updates and unused indexes all slow writes; measure before you drop anything.


Chapter [7.3](07-03-indexes-and-query-plans.md) explains how indexes, statistics and the optimiser work. This chapter is the casebook: 18 slow or broken database workloads, each with the evidence that pointed at the cause, the fix, the numbers afterwards and what the fix costs. Where SQL Server and PostgreSQL behave differently, both are shown.

**The method behind every case**

1.  **Find the query that costs the most in total**, not the slowest single run: Query Store or `sys.dm_exec_query_stats` on SQL Server, `pg_stat_statements` on PostgreSQL. A 40 ms query run 2 million times a day matters more than one 30-second report.
2.  **Get the actual plan**, not the estimated one: an actual execution plan with `SET STATISTICS IO, TIME ON`, or `EXPLAIN (ANALYZE, BUFFERS)`.
3.  **Compare estimated rows with actual rows** at each step. A gap of 10× or more is the cause more often than a missing index.
4.  **Change one thing and measure again** on production-sized data. Logical reads or buffers are steadier than timings.
5.  **Write down what the fix costs**: extra writes, storage, maintenance, and the queries it might make worse.

**How each case is laid out:** the situation, the code before, what goes wrong, the evidence, the code after, why it works, the result, the costs and when not to use it, and a one-line lesson.

### Find the case for your problem

| Technique | The problem | Group |
|---|---|---|
| Composite index column order | [Payment-retry query reading 2.3M index rows to return 9,214](#case-index-equality-before-range) | Indexes |
| Filtered and partial indexes | [Job workers scanning 60M rows to find 3,000 pending jobs](#case-filtered-index-job-queue) | Indexes |
| Implicit conversion | [Marketplace webhooks scanning 18M rows to find one order](#case-implicit-conversion-varchar) | Indexes |
| Indexing a JSON property | [Support searching 22M orders by a value buried in JSON](#case-json-property-index) | Indexes |
| OR to UNION rewrite | [Support search box that scans 9M orders for one match](#case-or-across-columns-union) | Query shape |
| Capped and estimated counts | [An admin grid that counts 20 million rows on every page view](#case-count-for-pagination-totals) | Query shape |
| Scalar UDF to inline TVF | [A customer report calling a scalar function 412,388 times](#case-scalar-udf-per-row) | Query shape |
| Atomic upsert | [Concurrent webhooks creating the same customer twice](#case-upsert-race-conditions) | Query shape |
| Sort spills and memory grants | [A nightly revenue report that spends 14 minutes spilling a sort](#case-sort-spill-memory-grant) | Query shape |
| Parameter sniffing | [One tenant with 40% of the orders gets a 45-second plan](#case-parameter-sniffing-tenant-skew) | Plans and statistics |
| Statistics update after load | [Morning report estimates 1 row for 400,000 freshly loaded sales](#case-stale-statistics-after-bulk-load) | Plans and statistics |
| Batched delete | [A retention DELETE that blocked every audit write for three hours](#case-batched-retention-delete) | Writes and locking |
| Partitioned retention | [Dropping a month of audit events instead of deleting it](#case-partition-retention-drop) | Writes and locking |
| Atomic conditional update | [A 500-unit flash sale that managed 30 orders a second](#case-hot-row-inventory) | Writes and locking |
| Read committed snapshot | [Month-end reports that froze checkout, and the NOLOCK that lied](#case-readers-block-writers-rcsi) | Writes and locking |
| Sequential keys | [Random GUID keys that left a 200M-row table a third empty](#case-random-guid-clustered-key) | Storage and schema |
| HOT updates and fillfactor | [Heartbeat updates that grew a 2M-row table to 38 GB](#case-postgres-bloat-hot-updates) | Storage and schema |
| Index consolidation | [23 indexes on Orders and a 35 ms insert](#case-too-many-indexes) | Storage and schema |

### Indexes

Getting an index the optimiser can actually use for the query you run.

<a id="case-index-equality-before-range"></a>

#### Case 1 · Composite index column order: Payment-retry query reading 2.3M index rows to return 9,214

*SQL Server 2022 · PostgreSQL 17 · EF Core 10*

**The situation.** A multi-tenant billing service keeps 120M rows in `dbo.Invoices`. The payment-retry screen and the retry worker both ask for one tenant's invoices in one status since a date, newest first. For the largest tenant (9.6M invoices) the query takes about 230 ms, and Query Store ranks it first by total logical reads, although the index already has all three columns.

**Before**

*Before: the retry query*

```csharp
// PaymentRetryQueries.cs
var since = clock.UtcNow.AddMonths(-12);

var rows = await db.Invoices
    .Where(i => i.TenantId == tenantId
             && i.Status == status              // equality
             && i.IssuedAt >= since)            // range
    .OrderByDescending(i => i.IssuedAt)
    .Select(i => new RetryRow(i.Id, i.Number, i.CustomerId, i.Total, i.IssuedAt))
    .ToListAsync(ct);
```

*Before: the existing index*

```sql
-- Created two years ago for the "invoices this month" screen
CREATE INDEX IX_Invoices_Tenant_IssuedAt_Status
    ON dbo.Invoices (TenantId, IssuedAt, Status)      -- (1) range column in the middle
    INCLUDE (Number, CustomerId, Total);
```

**What goes wrong.** The index has the right columns in the wrong order (1). A B-tree seek can use equality on the leading columns and then one range. After the range, the remaining key columns are no longer in a useful order, so they can only be checked row by row. SQL Server seeks to `TenantId = @tenantId AND IssuedAt >= @since`, reads every invoice the tenant issued in the last 12 months, and tests `Status` on each one as a residual predicate.

For the largest tenant that range is 2,316,402 rows (about 16,700 pages) to return 9,214. At a glance the plan looks healthy: an ordered Index Seek, no Sort, no lookups. The clue is the gap between Number of Rows Read and Actual Number of Rows.

**Evidence**

*SQL Server before: actual plan and STATISTICS IO, TIME*

```text
Index Seek (NonClustered) [Invoices].[IX_Invoices_Tenant_IssuedAt_Status]
  Seek Keys[1]: Prefix: TenantId = @tenantId, Start: IssuedAt >= @since
  Predicate:    Status = @status
  Ordered: True   Scan Direction: BACKWARD
  Number of Rows Read:     2,316,402
  Actual Number of Rows:       9,214

Table 'Invoices'. Scan count 1, logical reads 16712, physical reads 0, read-ahead reads 0, ...
 SQL Server Execution Times:
   CPU time = 219 ms,  elapsed time = 231 ms.
```

*PostgreSQL before: status is listed in Index Cond anyway*

```text
Index Scan Backward using invoices_tenant_issued_status on invoices
    (actual time=0.061..138.402 rows=9214 loops=1)
  Index Cond: ((tenant_id = 42) AND (issued_at >= '2025-10-02 00:00:00+00'::timestamp with time zone)
               AND (status = 4))
  Buffers: shared hit=24118 read=2253
Execution Time: 139.871 ms

-- All three columns appear under Index Cond with either index. Only the leading
-- equality and the range bound the scan; status is checked on every entry in it.
-- The Buffers line is where the difference shows.
```

*SQL Server after*

```text
Index Seek (NonClustered) [Invoices].[IX_Invoices_Tenant_Status_IssuedAt]
  Seek Keys[1]: Prefix: TenantId, Status = @tenantId, @status, Start: IssuedAt >= @since
  Ordered: True   Scan Direction: FORWARD
  Number of Rows Read:     9,214
  Actual Number of Rows:   9,214

Table 'Invoices'. Scan count 1, logical reads 71, physical reads 0, read-ahead reads 0, ...
 SQL Server Execution Times:
   CPU time = 3 ms,  elapsed time = 14 ms.
```

**After**

*After: equality columns first, range last*

```sql
-- SQL Server 2022 (ONLINE = ON needs Enterprise edition or Azure SQL)
CREATE INDEX IX_Invoices_Tenant_Status_IssuedAt
    ON dbo.Invoices (TenantId, Status, IssuedAt DESC)
    INCLUDE (Number, CustomerId, Total)
    WITH (ONLINE = ON);

-- PostgreSQL 17
CREATE INDEX CONCURRENTLY invoices_tenant_status_issued
    ON invoices (tenant_id, status, issued_at DESC)
    INCLUDE (number, customer_id, total);
```

*After: the same index in the EF Core model*

```csharp
modelBuilder.Entity<Invoice>(b =>
{
    b.HasIndex(i => new { i.TenantId, i.Status, i.IssuedAt })
     .IsDescending(false, false, true)
     .IncludeProperties(i => new { i.Number, i.CustomerId, i.Total })
     .HasDatabaseName("IX_Invoices_Tenant_Status_IssuedAt");
});
```

*Before dropping the old index*

```sql
-- SQL Server: is anything still using it? (counters reset when the instance restarts)
SELECT s.user_seeks, s.user_scans, s.user_lookups, s.user_updates, s.last_user_seek
FROM sys.dm_db_index_usage_stats AS s
JOIN sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE s.database_id = DB_ID()
  AND i.name = N'IX_Invoices_Tenant_IssuedAt_Status';

-- Which queries? Plans in Query Store that mention it
SELECT DISTINCT p.query_id
FROM sys.query_store_plan AS p
WHERE p.query_plan LIKE N'%IX_Invoices_Tenant_IssuedAt_Status%';

-- PostgreSQL 17
SELECT idx_scan, last_idx_scan
FROM pg_stat_user_indexes
WHERE indexrelname = 'invoices_tenant_issued_status';
```

**Why it works.** B-tree entries are sorted by the first key column, then by the second within it, and so on. With `(TenantId, Status, IssuedAt DESC)`, all of one tenant's PaymentFailed invoices sit next to each other, already in IssuedAt order. The seek lands on the newest one, reads exactly the 9,214 rows it returns, and the ORDER BY needs no Sort because the index supplies that order. (DESC is optional for a single-column sort, since both engines can read an index backwards, but it states the intent.)

The rule: columns compared with equality first, then the one range or sort column. A range in the middle turns every column after it into a filter, because within `IssuedAt >= @since` the Status values are scattered across the whole range. The order among the equality columns matters much less.

**Result.** For the largest tenant, logical reads fell from 16,712 to 71 and the query from 231 ms to 14 ms, and it dropped out of Query Store's top 50. On the PostgreSQL deployment buffers went from 26,371 to 9,286; nearly all of the remaining ones are heap pages, one per row returned.

> **Costs, and when not to.** The new index is about the same size as the old one, roughly 8 GB at 120M rows, most of it INCLUDE columns. Status is a key column in both, so a status change was already a key update and replacing one index with the other costs nothing extra on writes. Keeping both doubles the storage and the index maintenance on every insert and every status change.
>
> So don't drop the old index on reflex, and don't keep it on reflex either. `(TenantId, IssuedAt, Status)` is still the right shape for "all of a tenant's invoices this month, any status", and the new index can't serve that query: neither SQL Server 2022 nor PostgreSQL 17 can skip over a leading Status column, so it would read every status and sort. Check usage over a full business cycle including month-end, find the queries in Query Store, and drop the old index only when nothing needs it.

> **Lesson:** Put equality columns first and the single range or sort column last; any key column after a range is only a filter.

<a id="case-filtered-index-job-queue"></a>

#### Case 2 · Filtered and partial indexes: Job workers scanning 60M rows to find 3,000 pending jobs

*SQL Server 2022 · PostgreSQL 17 · EF Core 10*

**The situation.** `dbo.Jobs` backs the background job system: invoice emails, PDF renders, partner webhooks. It holds 60M rows because completed jobs are kept for 90 days, but only about 3,000 are Pending at any moment. Three worker instances poll every second, and database CPU sits at 70% on a 16-core server even when the queue is empty. Last sprint someone added a filtered index on pending jobs; the plan did not change.

**Before**

*Before: the filtered index that was added*

```sql
-- Status is an int column mapped from enum JobStatus (Pending = 0, Running = 1, ...)
CREATE INDEX IX_Jobs_Pending
    ON dbo.Jobs (RunAfter)
    INCLUDE (Type, Attempts)
    WHERE Status = 0;              -- about 3,000 of 60M rows
```

*Before: the worker's poll*

```csharp
// JobWorker.PollAsync: runs every second on each of 3 instances
var pending = JobStatus.Pending;               // (1) captured variable: sent as a parameter
var now = DateTime.UtcNow;

var batch = await db.Jobs
    .Where(j => j.Status == pending && j.RunAfter <= now)
    .OrderBy(j => j.RunAfter)
    .Take(20)
    .ToListAsync(ct);

foreach (var job in batch)                      // (2) read, then claim: two workers can take the same job
{
    job.Status = JobStatus.Running;
    job.LockedBy = workerId;
    job.LockedUntil = now.AddMinutes(5);
}
await db.SaveChangesAsync(ct);
```

**What goes wrong.** EF Core turns the captured variable (1) into a parameter, `WHERE [j].[Status] = @pending`. SQL Server caches one plan for that query text and reuses it for any value of @pending, so it can't choose an index whose filter is `Status = 0`: the next execution might pass 2. The filtered index is skipped and every poll scans the clustered index, all 60M rows. The plan says so in a warning that is easy to miss, Unmatched index.

The read-then-claim (2) is a second bug. Two workers that poll at the same moment read the same 20 rows, both mark them Running, and both run them. Customers got duplicate invoice emails a few times a week.

**Evidence**

*SQL Server before: actual plan*

```text
SELECT   Warnings: Unmatched index: [Ops].[dbo].[Jobs].[IX_Jobs_Pending]
  Top (20)
    Parallelism (Gather Streams)   Order By: [j].[RunAfter] ASC
      Sort (Top N Sort)
        Clustered Index Scan (Clustered) [Jobs].[PK_Jobs]
          Predicate: [j].[Status] = [@pending] AND [j].[RunAfter] <= [@now]
          Number of Rows Read:    60,214,377
          Actual Number of Rows:       2,961

Showplan XML:
  <UnmatchedIndexes>
    <Parameterization>
      <Object Database="[Ops]" Schema="[dbo]" Table="[Jobs]" Index="[IX_Jobs_Pending]" />
    </Parameterization>
  </UnmatchedIndexes>
```

*Query Store, one hour, before and after*

```text
                        before (LINQ poll)    after (dequeue)
executions                     5,046              10,712
avg duration                1,142 ms              1.9 ms
avg CPU                     7,904 ms              0.6 ms     (before: parallel, DOP 8)
avg logical reads          1,412,330                  38

After, one claim of 20 jobs (SET STATISTICS IO, TIME ON):
  Index Seek (NonClustered) [Jobs].[IX_Jobs_Pending]  Seek Keys[1]: End: RunAfter <= sysutcdatetime()
  Table 'Jobs'. Scan count 1, logical reads 118, physical reads 0, ...
   CPU time = 0 ms,  elapsed time = 2 ms.
```

**After**

*After: a literal for the filter, one statement for the claim*

```csharp
// Read-only queries (dashboard, "when is the next job due" for the idle back-off):
// write the enum constant in the lambda. EF Core inlines constants as literals.
var nextDue = await db.Jobs
    .Where(j => j.Status == JobStatus.Pending)       // SQL: WHERE [j].[Status] = 0
    .MinAsync(j => (DateTime?)j.RunAfter, ct);
// If the value must come from a variable: j.Status == EF.Constant(status)

// The claim: one statement. Interpolated {values} become parameters,
// so the status literal goes in the SQL text, not in a {placeholder}.
var claimed = await db.Database.SqlQuery<ClaimedJob>($"""
    WITH next AS (
        SELECT TOP (20) Id, Type, Payload, Status, LockedBy, LockedUntil, Attempts
        FROM dbo.Jobs WITH (READPAST, UPDLOCK, ROWLOCK)
        WHERE Status = 0
          AND RunAfter <= SYSUTCDATETIME()
        ORDER BY RunAfter
    )
    UPDATE next
    SET Status = 1,
        LockedBy = {workerId},
        LockedUntil = DATEADD(minute, 5, SYSUTCDATETIME()),
        Attempts = Attempts + 1
    OUTPUT inserted.Id, inserted.Type, inserted.Payload, inserted.Attempts;
    """).ToListAsync(ct);                           // not composed, so EF sends it as written
```

*After: the PostgreSQL version*

```sql
CREATE INDEX CONCURRENTLY jobs_pending_run_after
    ON jobs (run_after)
    WHERE status = 'pending';

WITH next AS (
    SELECT id
    FROM jobs
    WHERE status = 'pending'            -- literal: matches the partial index
      AND run_after <= now()
    ORDER BY run_after
    LIMIT 20
    FOR UPDATE SKIP LOCKED
)
UPDATE jobs AS j
SET status = 'running',
    locked_by = $1,
    locked_until = now() + interval '5 minutes',
    attempts = j.attempts + 1
FROM next
WHERE j.id = next.id
RETURNING j.id, j.type, j.payload, j.attempts;
```

**Why it works.** A filtered index holds only the rows that satisfy its WHERE clause, so this one is a few dozen pages instead of 60M entries. The optimiser can use it only when it can prove at compile time that the query's predicate implies the filter. `Status = 0` in the SQL text proves it; `Status = @pending` can't, because the cached plan has to be valid for every value. `OPTION (RECOMPILE)` also works, by compiling for the actual value each time, but then every poll pays for a compile. EF Core decides from the expression tree: a constant in the lambda becomes a literal, a captured variable becomes a parameter, and `EF.Constant()` forces a literal.

The claim has to be a single statement. UPDLOCK makes the read take update locks that are held until the statement commits, READPAST skips rows another worker has locked instead of waiting for them, and ROWLOCK keeps it to row locks. `FOR UPDATE SKIP LOCKED` does the same in PostgreSQL. PostgreSQL has the same partial-index rule, but only for generic plans: `status = $1` can use the partial index in a custom plan and not in a generic one. Npgsql doesn't prepare statements unless asked, so it rarely bites, but with a literal there is nothing to think about.

**Result.** Polls went from 1.14 s and 1.4M logical reads to about 2 ms, and database CPU with an empty queue fell from 70% to 6%. In the four weeks since, no job has been claimed twice.

> **Costs, and when not to.** The filtered index is tiny, but every job passes through it: the insert adds an entry and the claim removes it, so it takes as many writes as the queue has jobs. That is cheap here. On PostgreSQL the claims also leave dead tuples behind; a queue table this busy needs per-table autovacuum settings such as a low `autovacuum_vacuum_scale_factor`, because the default 20% of 60M rows means vacuum almost never runs. The real long-term fix is to move completed jobs out of the hot table.
>
> Once a filtered index exists, INSERT, UPDATE and DELETE on the table fail with error 1934 in any session with `QUOTED_IDENTIFIER OFF`, which is the default for sqlcmd without `-I`. And the trick only works when you control the query text. If an ORM or the database's forced parameterisation turns every literal into a parameter, use an ordinary index on `(Status, RunAfter)` instead.

> **Lesson:** A filtered or partial index is used only when the query's predicate is a literal that matches the filter; a parameter hides it.

<a id="case-implicit-conversion-varchar"></a>

#### Case 3 · Implicit conversion: Marketplace webhooks scanning 18M rows to find one order

*SQL Server 2022 · EF Core 10 · PostgreSQL 17*

**The situation.** `dbo.Orders` (18.2M rows) predates the .NET service: a DBA created it with `ExternalRef varchar(32)` and a unique index for the marketplace's order reference. The database uses `SQL_Latin1_General_CP1_CI_AS`, the setup default for US English installations and for Azure SQL Database. The webhook handler looks orders up by ExternalRef about 5,400 times an hour, and each lookup takes about 350 ms and 2.4 s of CPU.

**Before**

*Before: the entity and the lookup*

```csharp
public sealed class Order
{
    public long Id { get; set; }
    public string ExternalRef { get; set; } = "";   // (1) not mapped: EF Core assumes nvarchar
    public OrderStatus Status { get; set; }
    // ...
}

// MarketplaceWebhookHandler
var orderRef = evt.OrderRef;
var order = await db.Orders.SingleOrDefaultAsync(o => o.ExternalRef == orderRef, ct);
```

*Before: what EF Core sends*

```sql
-- Orders.ExternalRef is varchar(32) NOT NULL, with UX_Orders_ExternalRef (ExternalRef)
exec sp_executesql N'SELECT TOP(2) [o].[Id], [o].[ExternalRef], [o].[Status], ...
FROM [Orders] AS [o]
WHERE [o].[ExternalRef] = @orderRef',
N'@orderRef nvarchar(4000)',                        -- (2) Unicode parameter, varchar column
@orderRef = N'402-7781234-5566012';
```

**What goes wrong.** EF Core only knows what the model tells it. With no mapping for ExternalRef (1) it treats the property as `nvarchar(max)` and sends the value as `nvarchar(4000)` (2). nvarchar has higher data type precedence than varchar, so SQL Server converts the column, not the parameter: the predicate becomes `CONVERT_IMPLICIT(nvarchar(32), ExternalRef, 0) = @orderRef`, evaluated for every row.

What happens next depends on the collation. Under a Windows collation such as `Latin1_General_CI_AS`, varchar and nvarchar compare by the same rules, so SQL Server can still seek: it computes a range with `GetRangeThroughConvert` and adds a residual predicate, which costs a little more and blurs the estimates. Under a SQL collation such as `SQL_Latin1_General_CP1_CI_AS`, varchar and nvarchar use different comparison rules, no range can be derived, and the plan scans the whole index. This database is the second case.

**Evidence**

*SQL Server before: actual plan*

```text
SELECT  Warnings:
  Type conversion in expression (CONVERT_IMPLICIT(nvarchar(32),[o].[ExternalRef],0)) may affect
  "CardinalityEstimate" in query plan choice, Type conversion in expression
  (CONVERT_IMPLICIT(nvarchar(32),[o].[ExternalRef],0)=[@orderRef]) may affect "SeekPlan" in query
  plan choice

Index Scan (NonClustered) [Orders].[UX_Orders_ExternalRef]
  Predicate: CONVERT_IMPLICIT(nvarchar(32),[o].[ExternalRef],0)=[@orderRef]
  Number of Rows Read:   18,204,551
  Actual Number of Rows:          1
Key Lookup (Clustered) [Orders].[PK_Orders]   Actual Number of Rows: 1
```

*Query Store, one hour, before and after*

```text
                       before         after
executions              5,412         5,388
avg duration           348 ms        0.4 ms
avg CPU              2,406 ms        0.1 ms    (before: parallel scan)
avg logical reads      88,190             7
```

*PostgreSQL analogue: bigint column, numeric parameter*

```text
-- shipments.order_id is bigint and indexed; Dapper passed a C# decimal, so the parameter is numeric
Gather (actual time=1102.4..1109.8 rows=1 loops=1)
  Workers Launched: 2
  ->  Parallel Seq Scan on shipments (actual time=731.0..1099.1 rows=0 loops=3)
        Filter: ((order_id)::numeric = '81726354'::numeric)
        Rows Removed by Filter: 6666667
Execution Time: 1109.9 ms

-- the same query with a long (bigint) parameter
Index Scan using shipments_order_id_idx on shipments (actual time=0.041..0.042 rows=1 loops=1)
  Index Cond: (order_id = '81726354'::bigint)
Execution Time: 0.06 ms
```

**After**

*After: tell EF Core the real column type*

```csharp
modelBuilder.Entity<Order>(b =>
{
    b.Property(o => o.ExternalRef)
     .IsUnicode(false)
     .HasMaxLength(32);                 // or .HasColumnType("varchar(32)")
});

// Now sent as @orderRef varchar(32). EF Core's log shows:
//   @orderRef='402-7781234-5566012' (Size = 32) (DbType = AnsiString)

// Only if every string column in this legacy schema really is varchar:
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    => configurationBuilder.Properties<string>().AreUnicode(false);
```

*Finding the others: Query Store plans with a seek-blocking conversion*

```sql
WITH hits AS (
    SELECT p.query_id,
           SUM(rs.count_executions)                          AS executions,
           SUM(rs.count_executions * rs.avg_cpu_time) / 1000.0 AS total_cpu_ms   -- avg_cpu_time is in microseconds
    FROM sys.query_store_plan AS p
    JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
    WHERE p.query_plan LIKE N'%ConvertIssue="Seek Plan"%'
    GROUP BY p.query_id
)
SELECT TOP (20) h.query_id, h.executions, h.total_cpu_ms, qt.query_sql_text
FROM hits AS h
JOIN sys.query_store_query AS q ON q.query_id = h.query_id
JOIN sys.query_store_query_text AS qt ON qt.query_text_id = q.query_text_id
ORDER BY h.total_cpu_ms DESC;
```

**Why it works.** With the property mapped as `varchar(32)`, EF Core sends a varchar parameter, both sides of the comparison have the same type, and the predicate is an ordinary seek on `UX_Orders_ExternalRef`: a few index pages and one key lookup. Nothing changes in the database. The model should describe the real column; scaffolding (`dotnet ef dbcontext scaffold`) writes these mappings for you, while a hand-written model over an existing schema silently defaults to nvarchar. In the plan XML, a `PlanAffectingConvert` with `ConvertIssue="Seek Plan"` means a conversion stopped a seek; one with only `"Cardinality Estimate"` is often harmless, for example a conversion in the SELECT list.

PostgreSQL has the same trap with different types. Text types are safe, because varchar to text is a free relabel and an index on a varchar column still works, and an int column compared with a bigint parameter is fine thanks to cross-type operators. But a bigint column compared with a numeric parameter is cast to numeric on every row, and the index is ignored. It appears when raw SQL or Dapper is handed a C# decimal; EF Core LINQ types the parameter from the property, so it only happens there if you cast the column.

**Result.** Lookups went from 348 ms, 2.4 s of CPU and 88,190 logical reads to under a millisecond and 7 logical reads, and the server lost about 3.5 cores of steady load. The Query Store search found nine more queries with the same warning, all on varchar columns mapped as plain strings; mapping them fixed all nine.

> **Costs, and when not to.** The mapping costs nothing at run time; the risk is getting it wrong the other way. If you mark a property non-Unicode and the column really is nvarchar, the seek still works (the parameter is the side that gets converted), but any character outside the varchar code page is replaced with `?` before it reaches the server and the lookup silently misses. Map exactly what the column is.
>
> Don't fix it by changing the column to nvarchar unless the data needs Unicode. On 18M rows that is a size-of-data change that rewrites the table, the index on the column has to be dropped and rebuilt around it, and the keys double in size. If the column needs Unicode later, change the column and the mapping together.

> **Lesson:** When a column is varchar, the model has to say so, or every lookup converts the column instead of the parameter.

<a id="case-json-property-index"></a>

#### Case 4 · Indexing a JSON property: Support searching 22M orders by a value buried in JSON

*SQL Server 2022 · PostgreSQL 17 · EF Core 10*

**The situation.** Marketplace orders carry a JSON `Metadata` column written by the channel connector: channel name, the channel's own order id, gift options, marketplace tracking. When a customer calls with an Amazon or eBay order number, support searches for that `channelOrderId` inside the JSON. `Orders` has 22M rows, about 60% of them with a channelOrderId. Searches take 20 to 30 seconds, and about one in seven hits the default 30-second command timeout.

**Before**

*Before: searching inside the JSON*

```csharp
// SQL Server: Metadata is nvarchar(max) holding JSON, mapped as a plain string
var orderIds = await db.Orders
    .FromSql($"""
        SELECT * FROM dbo.Orders
        WHERE JSON_VALUE(Metadata, '$.channelOrderId') = {channelOrderId}
        """)                                       // (1) computed from every row's JSON
    .Select(o => o.Id)
    .ToListAsync(ct);

// PostgreSQL deployment, metadata is jsonb:
//   SELECT id FROM orders WHERE metadata->>'channelOrderId' = $1;     (1) same problem
```

**What goes wrong.** Neither engine can seek on a value it has to compute from each row (1). With no index on the expression, SQL Server reads every row of the clustered index, 22M rows with their JSON, and parses each document to extract one property. PostgreSQL does the same with a sequential scan of the jsonb. The table is about 35 GB with the JSON stored in-row, so much of it comes from disk while support waits on the phone.

**Evidence**

*SQL Server before: actual plan and Query Store*

```text
Parallelism (Gather Streams)
  Clustered Index Scan (Clustered) [Orders].[PK_Orders]
    Predicate: json_value([o].[Metadata],N'$.channelOrderId')=[@p0]
    Number of Rows Read:    22,180,404
    Actual Number of Rows:           1

Query Store, last 7 days: executions 2,914 (412 aborted at the 30 s client timeout)
  avg duration 27,410 ms   avg CPU 96,200 ms   avg logical reads 4,412,870   avg physical reads 1,209,331
```

*PostgreSQL before*

```text
Gather (actual time=11204.1..11233.6 rows=1 loops=1)
  Workers Launched: 2
  Buffers: shared hit=40211 read=3261190
  ->  Parallel Seq Scan on orders (actual time=7391.2..11198.0 rows=0 loops=3)
        Filter: ((metadata ->> 'channelOrderId'::text) = '204-5571204-8836190'::text)
        Rows Removed by Filter: 7393468
Execution Time: 11233.7 ms
```

*After, both engines*

```text
SQL Server:
  Index Seek (NonClustered) [Orders].[IX_Orders_ChannelOrderId]
    Seek Keys[1]: Prefix: ChannelOrderId = @channelOrderId    Actual Number of Rows: 1
  Table 'Orders'. Scan count 1, logical reads 4, physical reads 0, ...
   CPU time = 0 ms,  elapsed time = 1 ms.

PostgreSQL:
  Index Scan using orders_channel_order_id on orders (actual time=0.031..0.032 rows=1 loops=1)
    Index Cond: ((metadata ->> 'channelOrderId'::text) = '204-5571204-8836190'::text)
    Buffers: shared hit=5
  Execution Time: 0.058 ms
```

**After**

*After: SQL Server, an indexed computed column*

```sql
-- Not persisted: a metadata-only change that takes no space in the table
ALTER TABLE dbo.Orders
    ADD ChannelOrderId AS JSON_VALUE(Metadata, '$.channelOrderId');

-- Run this, and every later INSERT/UPDATE/DELETE on Orders, with ANSI_NULLS, ANSI_PADDING,
-- ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON, NUMERIC_ROUNDABORT OFF
CREATE INDEX IX_Orders_ChannelOrderId
    ON dbo.Orders (ChannelOrderId)
    WITH (ONLINE = ON);
-- Warning! The maximum key length for a nonclustered index is 1700 bytes. The index
-- 'IX_Orders_ChannelOrderId' has maximum length of 8000 bytes. ...   (JSON_VALUE is nvarchar(4000))

SELECT COLUMNPROPERTY(OBJECT_ID(N'dbo.Orders'), N'ChannelOrderId', 'IsDeterministic') AS is_deterministic,
       COLUMNPROPERTY(OBJECT_ID(N'dbo.Orders'), N'ChannelOrderId', 'IsPrecise')       AS is_precise,
       COLUMNPROPERTY(OBJECT_ID(N'dbo.Orders'), N'ChannelOrderId', 'IsIndexable')     AS is_indexable;
```

*After: EF Core, query the mapped column*

```csharp
// Order has: public string? ChannelOrderId { get; private set; }
modelBuilder.Entity<Order>(b =>
{
    b.Property(o => o.ChannelOrderId)
     .HasComputedColumnSql("JSON_VALUE([Metadata], '$.channelOrderId')", stored: false);
    b.HasIndex(o => o.ChannelOrderId);
});

var orderIds = await db.Orders
    .Where(o => o.ChannelOrderId == channelOrderId)    // WHERE [o].[ChannelOrderId] = @channelOrderId
    .Select(o => o.Id)
    .ToListAsync(ct);
```

*After: PostgreSQL, two ways to index jsonb*

```sql
-- (a) B-tree on the one expression support searches by: small, supports =, ranges, ORDER BY
CREATE INDEX CONCURRENTLY orders_channel_order_id
    ON orders ((metadata->>'channelOrderId'))
    WHERE metadata->>'channelOrderId' IS NOT NULL;      -- leaves out the 40% without one

SELECT id FROM orders WHERE metadata->>'channelOrderId' = $1;          -- uses (a)

-- (b) GIN over whole documents: any key, but only containment and jsonpath
CREATE INDEX CONCURRENTLY orders_metadata_path_ops
    ON orders USING gin (metadata jsonb_path_ops);

SELECT id FROM orders WHERE metadata @> '{"channelOrderId": "204-5571204-8836190"}';      -- uses (b)
SELECT id FROM orders WHERE metadata @? '$.channelOrderId ? (@ == "204-5571204-8836190")'; -- uses (b)

-- (b) can't serve metadata->>'channelOrderId' = $1, and jsonb_path_ops can't serve metadata ? 'key'.
-- (a) can't serve @>. The query has to be written in the form its index supports.
```

**Why it works.** An index can only seek on values it stores. The computed column gives SQL Server a name for the expression, and indexing it stores the extracted value for every row, so the search becomes a seek on a short string. The requirements are that the expression is deterministic and precise (JSON_VALUE is both) and that the SET options above are on when the index is built and whenever the table is modified. PERSISTED is optional for a deterministic, precise expression; you would need it only for an imprecise result such as float. The optimiser also matches the same expression in a query to the computed column, but mapping the column in EF Core and querying it by name doesn't depend on anyone spelling the expression exactly. If you map Metadata with EF Core 10's JSON support (`ComplexProperty(o => o.Metadata, m => m.ToJson())`), EF writes the JSON_VALUE path itself, so check it with `ToQueryString()`: the computed column only matches the identical expression, including the case of the key.

In PostgreSQL the choice is between the two indexes. The expression index stores one value per row and supports exactly the queries that repeat the expression. The GIN `jsonb_path_ops` index stores hashes of every path and value in every document, so it supports containment (`@>`) and jsonpath (`@?`, `@@`) on any key, at several times the size. The `IS NOT NULL` predicate on (a) is safe even with parameters, because `=` can never be true for NULL, so the planner can always prove it.

**Result.** On SQL Server, searches went from 27.4 s and 4.4M logical reads to about 1 ms and 4 logical reads, with no timeouts since. On PostgreSQL, 11.2 s became 0.06 ms. The partial expression index is 0.5 GB; the jsonb_path_ops index we tried on a copy was 2.9 GB and took 9 minutes to build, so we kept the B-tree.

> **Costs, and when not to.** Every order insert, and every update that touches Metadata, now evaluates the expression and maintains one more index: 0.9 GB in SQL Server, 0.5 GB in PostgreSQL. Building it reads all 22M rows; use `ONLINE = ON` (Enterprise edition) or `CREATE INDEX CONCURRENTLY`. Once the index exists, any session with the wrong SET options fails every INSERT and UPDATE on Orders with error 1934, so test old sqlcmd scripts as well as the application. JSON_VALUE returns nvarchar(4000), so an id longer than 1,700 bytes would make the insert fail; casting to a short type avoids the warning but changes the expression, and then queries have to use the column by name.
>
> If support searches a JSON field often enough to need an index, ask whether it should be a real column. A plain `ChannelOrderId varchar(40)` written by the connector is smaller, typed, can have a unique constraint per channel, and doesn't rely on queries matching an expression. Keep JSON for the data you only display.

> **Lesson:** To index a JSON property, index the exact expression the queries use, and make sure they keep using that expression.

### Query shape

The same result, written so the engine does far less work.

<a id="case-or-across-columns-union"></a>

#### Case 5 · OR to UNION rewrite: Support search box that scans 9M orders for one match

*SQL Server 2022 · PostgreSQL 17 · EF Core 10*

**The situation.** Support staff paste whatever the customer gives them, an email, a phone number or an order number, into one search box. The query matches all three at once across `Customers` (2.1M rows) and `Orders` (9M rows). Each of the three columns has its own index, yet the search takes 2.4 s and support runs it about 18,000 times a day.

**Before**

*Before: one query, three ORs across two tables*

```csharp
var q = term.Trim();

var hits = await db.Orders
    .Where(o => o.Customer.Email == q          // (1) Customers column
             || o.Customer.Phone == q          //     Customers column
             || o.OrderNumber == q)            //     Orders column
    .OrderByDescending(o => o.PlacedAt)
    .Take(50)
    .Select(o => new SupportHit(o.Id, o.OrderNumber, o.PlacedAt, o.Customer.Name, o.Customer.Email))
    .ToListAsync(ct);
```

*Before: the SQL it generates*

```sql
SELECT TOP(50) [o].[Id], [o].[OrderNumber], [o].[PlacedAt], [c].[Name], [c].[Email]
FROM [Orders] AS [o]
INNER JOIN [Customers] AS [c] ON [o].[CustomerId] = [c].[Id]
WHERE [c].[Email] = @q OR [c].[Phone] = @q OR [o].[OrderNumber] = @q
ORDER BY [o].[PlacedAt] DESC

-- Indexes: Customers(Email) unique, Customers(Phone), Orders(OrderNumber) unique, Orders(CustomerId)
```

**What goes wrong.** Each branch of the OR is selective on its own, but the OR (1) mixes columns from two tables. No single index can answer it, and the predicate can only be evaluated once a customer row and an order row have been joined. So the optimiser joins everything and filters afterwards: it scans all 9M orders and all 2.1M customers, hash joins them, and applies the OR as a residual. None of the three indexes is used, because each one finds only part of the answer.

**Evidence**

*SQL Server before: actual plan and Query Store*

```text
Top (50)
  Parallelism (Gather Streams)
    Sort (Top N Sort)  Order By: [o].[PlacedAt] DESC
      Hash Match (Inner Join)  Hash Keys Build: [c].[Id]  Hash Keys Probe: [o].[CustomerId]
        Probe Residual: [o].[CustomerId]=[c].[Id] AND
                        ([c].[Email]=[@q] OR [c].[Phone]=[@q] OR [o].[OrderNumber]=[@q])
        Actual Number of Rows: 1
        Clustered Index Scan [Customers].[PK_Customers]   Actual Number of Rows: 2,104,880
        Clustered Index Scan [Orders].[PK_Orders]         Actual Number of Rows: 9,012,447

Query Store, last 24 hours: executions 18,240, avg duration 2,382 ms, avg CPU 9,710 ms,
                            avg logical reads 214,615
```

*PostgreSQL before: the cross-table OR is no better*

```text
Limit (actual time=2140.6..2163.9 rows=1 loops=1)
  ->  Gather Merge (actual time=2140.6..2163.9 rows=1 loops=1)
        Workers Launched: 2
        ->  Sort (actual time=2122.3..2122.3 rows=0 loops=3)
              Sort Key: o.placed_at DESC
              ->  Parallel Hash Join (actual time=2071.5..2122.0 rows=0 loops=3)
                    Hash Cond: (o.customer_id = c.id)
                    Join Filter: ((c.email = '447700900123'::text) OR (c.phone = '447700900123'::text)
                                  OR (o.order_number = '447700900123'::text))
                    Rows Removed by Join Filter: 3004149
                    ->  Parallel Seq Scan on orders o (actual rows=3004149 loops=3)
                    ->  Parallel Hash (actual rows=701627 loops=3)
                          ->  Parallel Seq Scan on customers c (actual rows=701627 loops=3)
Execution Time: 2164.2 ms
```

*PostgreSQL: an OR within one table becomes a BitmapOr*

```text
EXPLAIN (ANALYZE, BUFFERS)
SELECT id FROM customers WHERE email = '447700900123' OR phone = '447700900123';

Bitmap Heap Scan on customers (actual time=0.041..0.042 rows=1 loops=1)
  Recheck Cond: ((email = '447700900123'::text) OR (phone = '447700900123'::text))
  Heap Blocks: exact=1
  Buffers: shared hit=7
  ->  BitmapOr (actual time=0.035..0.036 rows=0 loops=1)
        ->  Bitmap Index Scan on customers_email_idx (actual time=0.019..0.019 rows=0 loops=1)
              Index Cond: (email = '447700900123'::text)
        ->  Bitmap Index Scan on customers_phone_idx (actual time=0.015..0.015 rows=1 loops=1)
              Index Cond: (phone = '447700900123'::text)
Execution Time: 0.061 ms
```

**After**

*After: three seekable branches (SQL Server)*

```sql
SELECT TOP (50) o.Id, o.OrderNumber, o.PlacedAt, c.Name, c.Email
FROM (
    SELECT o.Id
    FROM dbo.Orders AS o JOIN dbo.Customers AS c ON c.Id = o.CustomerId
    WHERE c.Email = @q                         -- seek Customers(Email), then Orders(CustomerId)
    UNION
    SELECT o.Id
    FROM dbo.Orders AS o JOIN dbo.Customers AS c ON c.Id = o.CustomerId
    WHERE c.Phone = @q                         -- seek Customers(Phone), then Orders(CustomerId)
    UNION
    SELECT o.Id
    FROM dbo.Orders AS o
    WHERE o.OrderNumber = @q                   -- seek Orders(OrderNumber)
) AS hit
JOIN dbo.Orders AS o ON o.Id = hit.Id
JOIN dbo.Customers AS c ON c.Id = o.CustomerId
ORDER BY o.PlacedAt DESC;

-- Table 'Customers'. Scan count 1, logical reads 12, ...
-- Table 'Orders'. Scan count 2, logical reads 26, ...
--  CPU time = 0 ms,  elapsed time = 2 ms.
```

*After: the same shape in EF Core with Concat*

```csharp
var q = term.Trim();

IQueryable<long> ids =
    db.Orders.Where(o => o.Customer.Email == q).Select(o => o.Id)
      .Concat(db.Orders.Where(o => o.Customer.Phone == q).Select(o => o.Id))
      .Concat(db.Orders.Where(o => o.OrderNumber == q).Select(o => o.Id));     // UNION ALL

var hits = await db.Orders
    .Where(o => ids.Contains(o.Id))            // WHERE [o].[Id] IN (... UNION ALL ...)
    .OrderByDescending(o => o.PlacedAt)
    .Take(50)
    .Select(o => new SupportHit(o.Id, o.OrderNumber, o.PlacedAt, o.Customer.Name, o.Customer.Email))
    .ToListAsync(ct);
```

*After: PostgreSQL needs only two branches*

```sql
SELECT o.id, o.order_number, o.placed_at, c.name, c.email
FROM orders o
JOIN customers c ON c.id = o.customer_id
WHERE o.id IN (
    SELECT o1.id
    FROM orders o1 JOIN customers c1 ON c1.id = o1.customer_id
    WHERE c1.email = $1 OR c1.phone = $1       -- one table: a BitmapOr handles this OR
    UNION ALL
    SELECT o2.id FROM orders o2 WHERE o2.order_number = $1)
ORDER BY o.placed_at DESC
LIMIT 50;
```

**Why it works.** Each branch has a predicate on one table that one index can answer, so each becomes a seek: Customers by Email, Customers by Phone, Orders by OrderNumber, then Orders(CustomerId) to find a matched customer's orders. The few ids that come out are joined back for the columns the screen shows, so the work is proportional to the matches, not to the size of the tables.

The fix depends on the engine and on where the OR is. Across a join neither engine helps: PostgreSQL also joins everything and filters. Within one table PostgreSQL combines indexes on its own with a BitmapOr, so there only the cross-table part needs splitting. SQL Server can also combine seeks for a single-table OR (an index union), but it's a cost-based choice, so confirm it in the actual plan before you rely on it.

**Result.** On SQL Server the search went from 2.4 s and 214,615 logical reads to 2 ms and 38, all seeks, and dropped out of Query Store's top CPU list. The PostgreSQL deployment, with the two-branch version, went from 2.16 s to 0.4 ms.

> **Costs, and when not to.** UNION removes duplicates with a sort or hash over everything the branches return; UNION ALL only appends. When branches can overlap (a phone number that is also an order number, a customer whose email and phone both match), UNION returns each order once and UNION ALL may return it twice. Here the de-duplication is over a handful of ids and costs nothing, so the T-SQL uses UNION. The EF Core version uses Concat (UNION ALL) on purpose: it feeds `IN (...)`, a semi-join, where duplicates in the list can't duplicate the output. If branches can return thousands of rows each, the de-duplication is real work and you need to know which one you want.
>
> The rewrite is longer, and every new search field adds a branch that also needs an index. It pays off when the OR spans tables or the plan shows the engine failing to combine indexes. A single-table OR on PostgreSQL usually needs nothing, and a branch that isn't selective (a search on country, say) returns most of the table through its seek, so splitting it out gains nothing.

> **Lesson:** An OR across columns of different tables can't use any of their indexes; split it into one seekable branch per table and union the keys.

<a id="case-count-for-pagination-totals"></a>

#### Case 6 · Capped and estimated counts: An admin grid that counts 20 million rows on every page view

*PostgreSQL 17 · SQL Server 2022 · EF Core 10*

**The situation.** The operations admin grid lists shipments from the last 24 months, newest first, 25 per page, with optional carrier and status filters. Its header says "Page 1 of 812,344". The page query takes 160 ms, but the `COUNT(*)` behind that header runs with the same filters on every page view and takes 2.8 s of the 3 s the page needs. In pg_stat_statements it is the top statement by total time.

**Before**

*Before: count, then page*

```csharp
public async Task<ShipmentPage> SearchAsync(ShipmentFilter f, int page, CancellationToken ct)
{
    var query = db.Shipments.Where(s => s.CreatedAt >= f.From);
    if (f.Carrier is { } carrier) query = query.Where(s => s.Carrier == carrier);
    if (f.Status is { } status)   query = query.Where(s => s.Status == status);

    var total = await query.CountAsync(ct);                  // (1) every page view, every matching row

    var rows = await query
        .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
        .Skip(page * PageSize).Take(PageSize)
        .Select(s => new ShipmentRow(s.Id, s.TrackingNo, s.Carrier, s.Status, s.CreatedAt))
        .ToListAsync(ct);

    return new ShipmentPage(rows, page + 1, (total + PageSize - 1) / PageSize);   // (2) "of 812,344"
}
```

**What goes wrong.** A count can't stop early. To say 20,308,577, the database has to visit every matching row and, in PostgreSQL, check that each one is visible to the current transaction. With the default filter that is 20.3M of the table's 26M rows, every time anyone opens or pages through the grid (1). The page query, by contrast, reads 25 entries from an index in CreatedAt order and stops.

And the number (2) isn't used for anything. Nobody has ever gone to page 812,344, and with about 40 shipments created per second the total is out of date before the page renders.

**Evidence**

*pg_stat_statements, last 24 hours*

```text
query                                                        calls   mean_exec_time   total_exec_time
SELECT count(*)::int FROM shipments AS s WHERE s.created_at… 18,420         2,812 ms           14.4 h
SELECT s.id, s.tracking_no, s.carrier, … ORDER BY s.created… 18,420           161 ms            0.8 h
```

*EXPLAIN (ANALYZE, BUFFERS) of the count*

```text
Finalize Aggregate (actual time=2798.4..2811.9 rows=1 loops=1)
  Buffers: shared hit=171402 read=96811
  ->  Gather (actual time=2797.9..2811.8 rows=3 loops=1)
        Workers Planned: 2
        Workers Launched: 2
        ->  Partial Aggregate (actual time=2781.2..2781.3 rows=1 loops=3)
              ->  Parallel Seq Scan on shipments s (actual time=0.031..2402.7 rows=6769526 loops=3)
                    Filter: (created_at >= '2024-10-02 00:00:00+00'::timestamp with time zone)
                    Rows Removed by Filter: 1897141
Execution Time: 2812.3 ms
```

*After: the capped count*

```text
Aggregate (actual time=5.912..5.913 rows=1 loops=1)
  Buffers: shared hit=178
  ->  Limit (actual time=0.029..5.204 rows=10001 loops=1)
        ->  Index Only Scan using shipments_created_at_id_idx on shipments s
              (actual time=0.028..4.511 rows=10001 loops=1)
              Index Cond: (created_at >= '2024-10-02 00:00:00+00'::timestamp with time zone)
              Heap Fetches: 6214
Execution Time: 5.961 ms
```

**After**

*After: n+1 for paging, a capped count for the header*

```csharp
const int PageSize = 25;
const int CountCap = 10_000;

var ordered = query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);

var rows = await ordered
    .Skip(page * PageSize).Take(PageSize + 1)        // one extra row answers "is there a next page?"
    .Select(s => new ShipmentRow(s.Id, s.TrackingNo, s.Carrier, s.Status, s.CreatedAt))
    .ToListAsync(ct);

var hasNext = rows.Count > PageSize;
if (hasNext) rows.RemoveAt(PageSize);

// First page only, or when the filters change. Reads at most 10,001 index entries:
//   PostgreSQL: SELECT count(*)::int FROM (SELECT 1 ... ORDER BY ... LIMIT 10001) AS s0
//   SQL Server: SELECT COUNT(*) FROM (SELECT TOP(10001) 1 ... ORDER BY ...) AS s0
// Keeping the ORDER BY makes it read the newest entries of the CreatedAt index.
int? capped = page == 0 ? await ordered.Take(CountCap + 1).CountAsync(ct) : null;

var totalLabel = capped switch
{
    null => null,
    > CountCap => "More than 10,000 shipments",
    int n => $"{n:N0} shipments",
};
```

*Estimates, when a rough number is enough*

```sql
-- PostgreSQL: whole-table total, maintained by VACUUM and ANALYZE
SELECT reltuples::bigint AS approx_rows
FROM pg_class
WHERE oid = 'public.shipments'::regclass;

-- PostgreSQL: the planner's row estimate for a filtered query. The query text must come
-- from the application's own list of filters, never from user input.
CREATE FUNCTION count_estimate(query text) RETURNS bigint
LANGUAGE plpgsql AS $$
DECLARE plan jsonb;
BEGIN
    EXECUTE 'EXPLAIN (FORMAT JSON) ' || query INTO plan;
    RETURN (plan -> 0 -> 'Plan' ->> 'Plan Rows')::bigint;
END $$;

-- SQL Server: unfiltered total from metadata, no scan
SELECT SUM(ps.row_count) AS approx_rows
FROM sys.dm_db_partition_stats AS ps
WHERE ps.object_id = OBJECT_ID(N'dbo.Shipments')
  AND ps.index_id IN (0, 1);            -- the heap or the clustered index only
```

**Why it works.** Each option answers a smaller question than "exactly how many?". One extra row answers "is there a next page?" for the price of one row. A capped count reads at most 10,001 entries and answers "how many, if it's a manageable number?". The planner's estimate costs one planning call and answers "roughly how many?", as well as the statistics allow; `reltuples` and `sys.dm_db_partition_stats` give the whole-table figure from metadata without touching the rows.

The product conversation came first. We asked the ops leads what they do with the total. Nobody jumps to the last page: they narrow the filters until the list is short. The number they do care about, exceptions for one carrier this week, is usually a few hundred and so exact under the cap. The monthly volume figure comes from the warehouse, not from this grid. So the grid now shows "More than 10,000 shipments" with Next and Previous, and an exact count whenever the filtered set is small.

**Result.** Page views went from 3.0 s to about 0.2 s, and the count's share of database time from 14.4 hours a day to under two minutes. Nobody has asked for the exact total back.

> **Costs, and when not to.** You lose the exact total and the "jump to page N" control. That is a product decision, not a technical one, so make it with the people who use the screen. Estimates can be far off for combined filters, because the planner assumes carrier and status are independent; label them as approximate and never feed them into anything that has to add up. `reltuples` is only as fresh as the last VACUUM or ANALYZE, and it is -1 for a table that has never been analysed.
>
> If someone really needs an exact count over millions of rows, give it its own path: a button that runs the count once on request, or a counter table maintained by the writes, which adds a counter update to every insert and needs care under contention. What you shouldn't do is pay for it on every page view.

> **Lesson:** Don't count millions of rows to draw a pager: fetch n+1 for Next, cap the count, and estimate when a rough number will do.

<a id="case-scalar-udf-per-row"></a>

#### Case 7 · Scalar UDF to inline TVF: A customer report calling a scalar function 412,388 times

*SQL Server 2022 · PostgreSQL 17*

**The situation.** Account managers download a report of active customers with their tier (Gold, Silver or Bronze), based on revenue over the last 12 months. The tier comes from `dbo.fn_CustomerTier`, a scalar UDF written years ago and called from a dozen places. The report returns 412,388 rows and takes 75 seconds. The database is on SQL Server 2022 at compatibility level 160, so the team assumed scalar UDF inlining had taken care of it.

**Before**

*Before: the function*

```sql
CREATE FUNCTION dbo.fn_CustomerTier (@CustomerId int)
RETURNS varchar(10)
AS
BEGIN
    DECLARE @revenue decimal(19, 2);

    SELECT @revenue = SUM(o.Total)
    FROM dbo.Orders AS o
    WHERE o.CustomerId = @CustomerId
      AND o.PlacedAt >= DATEADD(month, -12, SYSUTCDATETIME());    -- (2) time-dependent

    RETURN CASE WHEN @revenue >= 10000 THEN 'Gold'
                WHEN @revenue >= 2000  THEN 'Silver'
                ELSE 'Bronze' END;
END;
```

*Before: the report*

```sql
SELECT c.CustomerId, c.Name, c.Country, c.AccountManagerId,
       dbo.fn_CustomerTier(c.CustomerId) AS Tier                  -- (1) once per row
FROM dbo.Customers AS c
WHERE c.IsActive = 1
ORDER BY c.Country, c.Name;
```

**What goes wrong.** A scalar UDF that isn't inlined runs once per row (1): 412,388 separate executions of a query against Orders, each with its own call overhead. The optimiser can't see inside it, so it can't turn those lookups into one join; it costs the function as nearly free; and a plan that calls it can't go parallel. STATISTICS IO doesn't report the function's reads either, so the report looks like a cheap scan of Customers that is mysteriously slow.

Inlining didn't happen because of (2). SQL Server 2019 and later, at compatibility level 150 or higher, can rewrite a scalar UDF into the calling query, but only if it meets a documented list of requirements, and one is that it calls no time-dependent functions such as GETDATE() or SYSUTCDATETIME(). `sys.sql_modules.is_inlineable` was 0.

**Evidence**

*Before: what each tool shows*

```text
SET STATISTICS IO, TIME ON:
  Table 'Customers'. Scan count 1, logical reads 9214, physical reads 0, ...
   SQL Server Execution Times:
     CPU time = 71406 ms,  elapsed time = 74912 ms.          -- no line for Orders at all

Actual plan:
  SELECT  NonParallelPlanReason: TSQLUserDefinedFunctionsNotParallelizable
    Compute Scalar  [Expr1002] = [dbo].[fn_CustomerTier]([c].[CustomerId])   (cost: 0%)

sys.dm_exec_function_stats, dbo.fn_CustomerTier, after one run:
  execution_count     412,388
  total_worker_time   69,804,112 (microseconds)
  total_logical_reads  1,649,612

sys.sql_modules.is_inlineable for dbo.fn_CustomerTier: 0
```

*After: STATISTICS TIME*

```text
Inline TVF with CROSS APPLY (parallel plan, DOP 8, no NonParallelPlanReason):
   CPU time = 6203 ms,  elapsed time = 1288 ms.

Join to the pre-aggregated set:
   CPU time = 5512 ms,  elapsed time = 1104 ms.
```

**After**

*After: an inline table-valued function*

```sql
CREATE OR ALTER FUNCTION dbo.tvf_CustomerTier (@CustomerId int, @AsOf datetime2(0))
RETURNS TABLE
AS RETURN
    SELECT CASE WHEN r.Revenue >= 10000 THEN 'Gold'
                WHEN r.Revenue >= 2000  THEN 'Silver'
                ELSE 'Bronze' END AS Tier
    FROM (SELECT SUM(o.Total) AS Revenue            -- no GROUP BY: always exactly one row
          FROM dbo.Orders AS o
          WHERE o.CustomerId = @CustomerId
            AND o.PlacedAt >= DATEADD(month, -12, @AsOf)) AS r;
GO

DECLARE @asOf datetime2(0) = SYSUTCDATETIME();     -- the caller supplies "now"

SELECT c.CustomerId, c.Name, c.Country, c.AccountManagerId, t.Tier
FROM dbo.Customers AS c
CROSS APPLY dbo.tvf_CustomerTier(c.CustomerId, @asOf) AS t
WHERE c.IsActive = 1
ORDER BY c.Country, c.Name;
```

*After: or a join to a pre-aggregated set*

```sql
DECLARE @from datetime2(0) = DATEADD(month, -12, SYSUTCDATETIME());

SELECT c.CustomerId, c.Name, c.Country, c.AccountManagerId,
       CASE WHEN r.Revenue >= 10000 THEN 'Gold'
            WHEN r.Revenue >= 2000  THEN 'Silver'
            ELSE 'Bronze' END AS Tier
FROM dbo.Customers AS c
LEFT JOIN (SELECT o.CustomerId, SUM(o.Total) AS Revenue
           FROM dbo.Orders AS o
           WHERE o.PlacedAt >= @from
           GROUP BY o.CustomerId) AS r
       ON r.CustomerId = c.CustomerId
WHERE c.IsActive = 1
ORDER BY c.Country, c.Name;
```

*PostgreSQL: a SQL function the planner can inline*

```sql
-- A plpgsql function, or a LANGUAGE sql one left VOLATILE (the default), runs as a
-- black box once per row: EXPLAIN shows "Function Scan on customer_tier", or nothing.
-- A scalar SQL function is inlined only if its body is one expression with no FROM.
CREATE OR REPLACE FUNCTION customer_tier(p_customer_id int, p_as_of timestamptz)
RETURNS TABLE (tier text)
LANGUAGE sql
STABLE                                    -- reads tables, changes nothing; allows inlining
AS $$
    SELECT CASE WHEN coalesce(sum(o.total), 0) >= 10000 THEN 'gold'
                WHEN coalesce(sum(o.total), 0) >= 2000  THEN 'silver'
                ELSE 'bronze' END
    FROM orders o
    WHERE o.customer_id = p_customer_id
      AND o.placed_at >= p_as_of - interval '12 months'
$$;

SELECT c.id, c.name, t.tier
FROM customers c
CROSS JOIN LATERAL customer_tier(c.id, now()) AS t   -- inlined: the plan shows the scan of orders
WHERE c.is_active;
```

**Why it works.** An inline TVF is a single SELECT with no BEGIN...END, and SQL Server expands it into the calling query like a view with parameters. The optimiser can see the Orders access, choose between seeking per customer and aggregating once with a hash join, cost it properly, and go parallel. The aggregate without GROUP BY always returns exactly one row, so CROSS APPLY keeps customers with no orders (they get Bronze); the join to a pre-aggregated set is the same idea written out. Inlining would have done this automatically if the function had qualified, so check `is_inlineable` for your hot functions, but don't count on it: the restrictions are long (no time-dependent functions, no table variables, not called from ORDER BY or GROUP BY, among others), they have changed between cumulative updates, and an eligible function still isn't guaranteed to be inlined.

PostgreSQL works the same way in its own terms. plpgsql functions are never inlined. A SQL function is inlined only when it is simple enough: a scalar one must be a single expression with no FROM, and a set-returning one must be a single SELECT that isn't VOLATILE, STRICT or SECURITY DEFINER. Declaring it STABLE, which is accurate for a function that reads tables and changes nothing, is what lets the planner fold it into the query.

**Result.** The report went from 75 s to 1.3 s with the inline TVF (1.1 s with the explicit join) and now runs at DOP 8. The other call sites moved to the TVF over two sprints; the scalar function survives in one legacy stored procedure.

> **Costs, and when not to.** Every caller has to change: a scalar call becomes CROSS APPLY, and an inline TVF can't be used in a computed column or a CHECK constraint. Because its body becomes part of each calling query, a change to the function changes every plan that uses it, and large inline TVFs nested inside each other can be slow to compile. Keep them small.
>
> Don't bother when the function is cheap arithmetic on its arguments with no data access, or when it runs over a few hundred rows; the per-call cost matters at scale. If many reports need the tier, consider computing it nightly into a column. Twelve-month revenue doesn't change between two page loads.

> **Lesson:** A scalar UDF that isn't inlined runs once per row, hides its cost and blocks parallelism; express data access as an inline TVF or a join.

<a id="case-upsert-race-conditions"></a>

#### Case 8 · Atomic upsert: Concurrent webhooks creating the same customer twice

*PostgreSQL 17 · SQL Server 2022 · .NET 10*

**The situation.** Payment and storefront webhooks both call `GetOrCreateCustomerAsync(email)`, with the email already normalised to lower case. When a new customer checks out, three events arrive within about 50 ms and land on different instances. The SQL Server database has a unique index on Email and logs about 400 duplicate-key errors a day, each a failed webhook that the provider retries. The PostgreSQL database for the EU region never got that index and has 1,184 email addresses with two or more customer rows.

**Before**

*Before: check, then insert*

```csharp
public async Task<long> GetOrCreateCustomerAsync(string email, string name, CancellationToken ct)
{
    var id = await db.Customers
        .Where(c => c.Email == email)
        .Select(c => (long?)c.Id)
        .FirstOrDefaultAsync(ct);                     // (1) both requests see "no row"
    if (id is not null) return id.Value;

    var customer = new Customer { Email = email, Name = name, CreatedAt = clock.UtcNow };
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);                    // (2) both insert
    return customer.Id;
}
```

**What goes wrong.** Between the SELECT (1) and the INSERT (2) there are a few milliseconds in which another request can run the same SELECT and also find nothing. Read committed doesn't help, because there is no row to lock yet and nothing stops the second request. With a unique index the second INSERT fails with a duplicate key error; without one, both succeed and the customer exists twice, each copy with some of the orders.

Wrapping both statements in a transaction doesn't fix it at the default isolation level. The SELECT takes no lock that would block another session's SELECT or INSERT of the same email.

**Evidence**

*Before: logs and data*

```text
SQL Server, application log, one day: 412 occurrences of
  Microsoft.Data.SqlClient.SqlException (0x80131904): Cannot insert duplicate key row in object
  'dbo.Customers' with unique index 'UX_Customers_Email'. The duplicate key value is
  (ana.silva@example.com).   Number: 2601

One of them, traced:
  request A  09:14:03.112  SELECT ... WHERE Email = 'ana.silva@example.com'   -> 0 rows
  request B  09:14:03.118  SELECT ... WHERE Email = 'ana.silva@example.com'   -> 0 rows
  request A  09:14:03.131  INSERT INTO Customers ...                          -> Id 4410391
  request B  09:14:03.135  INSERT INTO Customers ...                          -> error 2601

PostgreSQL (EU), no unique index:
  SELECT email, count(*) FROM customers GROUP BY email HAVING count(*) > 1;
  (1184 rows)
```

**After**

*After: PostgreSQL, one atomic statement*

```sql
-- The index the EU database never had (added after merging the duplicates)
CREATE UNIQUE INDEX CONCURRENTLY customers_email_key ON customers (email);
ALTER TABLE customers ADD CONSTRAINT customers_email_key UNIQUE USING INDEX customers_email_key;

-- Get or create, returning the id either way
INSERT INTO customers (email, name, created_at, last_seen_at)
VALUES ($1, $2, now(), now())
ON CONFLICT (email) DO UPDATE
    SET last_seen_at = EXCLUDED.last_seen_at        -- writes a new row version on every call
RETURNING id;

-- Alternative with no write when the row exists: two statements
INSERT INTO customers (email, name, created_at, last_seen_at)
VALUES ($1, $2, now(), now())
ON CONFLICT (email) DO NOTHING
RETURNING id;                                       -- returns nothing if the email exists
-- only if nothing came back, as a separate statement:
SELECT id FROM customers WHERE email = $1;
```

*After: SQL Server, MERGE with HOLDLOCK or UPDATE then INSERT*

```sql
-- MERGE is not atomic on its own: without HOLDLOCK two sessions can both take
-- the NOT MATCHED branch, and one fails with 2601/2627.
MERGE dbo.Customers WITH (HOLDLOCK) AS t
USING (VALUES (@Email, @Name)) AS s (Email, Name) ON t.Email = s.Email
WHEN MATCHED THEN UPDATE SET LastSeenAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (Email, Name, CreatedAt, LastSeenAt)
                      VALUES (s.Email, s.Name, SYSUTCDATETIME(), SYSUTCDATETIME())
OUTPUT inserted.Id;

-- The pattern many teams prefer: update under a key-range lock, insert if nothing matched
SET XACT_ABORT ON;
DECLARE @ids TABLE (Id bigint);

BEGIN TRANSACTION;

UPDATE dbo.Customers WITH (UPDLOCK, SERIALIZABLE)
SET    LastSeenAt = SYSUTCDATETIME()
OUTPUT inserted.Id INTO @ids
WHERE  Email = @Email;

IF @@ROWCOUNT = 0
    INSERT dbo.Customers (Email, Name, CreatedAt, LastSeenAt)
    OUTPUT inserted.Id INTO @ids
    VALUES (@Email, @Name, SYSUTCDATETIME(), SYSUTCDATETIME());

COMMIT TRANSACTION;

SELECT Id FROM @ids;
```

*After: calling it, with a last line of defence*

```csharp
public async Task<long> GetOrCreateCustomerAsync(string email, string name, CancellationToken ct)
{
    await using var cmd = dataSource.CreateCommand(UpsertSql);   // the ON CONFLICT ... RETURNING id statement
    cmd.Parameters.Add(new NpgsqlParameter { Value = email });
    cmd.Parameters.Add(new NpgsqlParameter { Value = name });
    return (long)(await cmd.ExecuteScalarAsync(ct))!;
}

// Insert paths that can still race (an EF Core SaveChanges elsewhere) treat a unique
// violation as "someone else created it first" and read the winner's row.
static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
{
    SqlException { Number: 2627 or 2601 } => true,                                // constraint / unique index
    PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,     // SQLSTATE 23505
    _ => false,
};

try
{
    db.Customers.Add(customer);
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateException ex) when (IsUniqueViolation(ex))
{
    db.ChangeTracker.Clear();
    customer = await db.Customers.SingleAsync(c => c.Email == customer.Email, ct);
}
```

**Why it works.** Both fixes move the decision to where the database can serialise it. `ON CONFLICT` checks the unique index as part of the insert: if another transaction has inserted the same email and not yet committed, this one waits for it and then takes the conflict branch, and `RETURNING id` returns the id whether the row was inserted or updated. With DO NOTHING, an existing row returns nothing, and the follow-up SELECT must be a separate statement: in read committed each statement takes a fresh snapshot and sees the row the other transaction just committed, while a SELECT in the same statement (a CTE) could miss it.

In SQL Server, SERIALIZABLE on the UPDATE takes a key-range lock on that email in the unique index even when no row exists, and UPDLOCK makes it an update lock, which two sessions can't hold at once. The second request waits until the first commits, then its UPDATE finds the row. MERGE needs HOLDLOCK (the same as SERIALIZABLE) for the same reason, and because MERGE has a long history of bugs, many teams use the UPDATE-then-INSERT form instead. Catching 2627/2601 or SQLSTATE 23505 stays as the last line of defence for any path that doesn't go through the upsert.

**Result.** Duplicate-key errors went from about 400 a day to none in the 30 days since, and the webhook retries they caused stopped. In the EU database the 1,184 duplicated emails were merged, with orders re-pointed to the oldest customer row, before the unique index went on.

> **Costs, and when not to.** The upsert needs a unique index or constraint on the key: without one there is nothing for ON CONFLICT to detect or for the range lock to protect. On a table that already has duplicates, adding it means a data clean-up first, and that is usually the real work. In SQL Server the key-range lock serialises concurrent upserts of the same email, and of other new emails that fall into the same gap in the index, so keep the transaction to these two statements and never call another service inside it.
>
> `DO UPDATE` writes a new row version on every call even when nothing changed: WAL, a dead tuple for vacuum, and update triggers firing. If most calls find an existing customer, DO NOTHING plus a SELECT is cheaper. Both forms draw an identity value for every attempt, so ids get gaps, which only matters if someone assumes they are contiguous.

> **Lesson:** Get-or-create must be one atomic operation against a unique index; a SELECT followed by an INSERT is a race.

<a id="case-sort-spill-memory-grant"></a>

#### Case 9 · Sort spills and memory grants: A nightly revenue report that spends 14 minutes spilling a sort

*SQL Server 2022 · PostgreSQL 17*

**The situation.** Every night a job builds the "top 500 customers by revenue, last 12 months" report for account management. It was written to stream every order in the period, sorted by customer, and add up the totals in C#: 30.4M rows. The job takes 14 minutes, and the DBA's tempdb alert fires every night at 02:10.

**Before**

*Before: the job's query*

```sql
SELECT o.CustomerId, c.Name, c.Email, c.Country,
       o.OrderId, o.PlacedAt, o.Total
FROM dbo.Orders AS o
JOIN dbo.Customers AS c ON c.CustomerId = o.CustomerId
WHERE o.PlacedAt >= @from AND o.PlacedAt < @to       -- 30.4M of 41M orders
ORDER BY o.CustomerId, o.PlacedAt;                    -- (1) sorts every row, wide columns included
```

*Before: the aggregation in C#*

```csharp
var totals = new List<CustomerTotal>();
CustomerTotal? current = null;

await foreach (var row in ReadOrdersSortedByCustomerAsync(from, to, ct))   // 30.4M rows
{
    if (current?.CustomerId != row.CustomerId)
    {
        if (current is not null) totals.Add(current);
        current = new CustomerTotal(row.CustomerId, row.Name, row.Email, row.Country);
    }
    current.Add(row.Total, row.PlacedAt);
}
// ...
var top = totals.OrderByDescending(t => t.Revenue).Take(500).ToList();     // (2) keeps 500 of 1.9M
```

**What goes wrong.** The database is asked to sort 30.4M rows (1), each carrying name, email and country, so that the C# loop sees each customer's orders together. A sort finishes in one pass only if its whole input fits in its memory grant, and SQL Server sizes the grant before the query starts and never grows it while it runs. Here the optimiser wanted about 10.7 GB (it assumes variable-length columns are half full), but no single query may take more than a set share of query memory, so the grant was capped at 2.7 GB. The sort wrote the rest to tempdb and read it back, and it can't return its first row until all of that is done.

Then 30.4M rows travel to the job, which keeps 500 of the 1.9M customer totals it builds (2). The plan's wait stats split the 14 minutes between tempdb I/O for the spill and ASYNC_NETWORK_IO while the job consumes the rows.

**Evidence**

*SQL Server before: actual plan and Query Store*

```text
Sort  Order By: [o].[CustomerId] ASC, [o].[PlacedAt] ASC
  Warning: Operator used tempdb to spill data during execution with spill level 1 and 8 spilled
  thread(s), Sort wrote 451204 pages to and read 451204 pages from tempdb with granted memory
  2844160KB and used memory 2844160KB
  Estimated Number of Rows: 30,377,000   Actual Number of Rows: 30,412,775

MemoryGrantInfo (KB): DesiredMemory 11240512, RequestedMemory 2844160, GrantedMemory 2844160,
                      MaxUsedMemory 2844160, MaxQueryMemory 2844160
WaitStats (top two):  IO_COMPLETION 2,971,004 ms (summed over 8 threads), ASYNC_NETWORK_IO 418,226 ms

Query Store, last 30 runs: avg duration 838 s, avg tempdb space used 451,880 pages
```

*PostgreSQL before: same query, work_mem = 64MB*

```text
Sort (actual time=141204.6..187513.0 rows=30412775 loops=1)
  Sort Key: o.customer_id, o.placed_at
  Sort Method: external merge  Disk: 2201384kB
  Buffers: shared hit=118420 read=486211, temp read=275173 written=275173
  ->  Hash Join (actual time=3904.1..58220.7 rows=30412775 loops=1)
        Hash Cond: (o.customer_id = c.id)
        ->  Seq Scan on orders o (actual time=0.041..21980.3 rows=30412775 loops=1)
              Filter: ((placed_at >= '2025-10-01 00:00:00+00'::timestamp with time zone)
                       AND (placed_at < '2026-10-01 00:00:00+00'::timestamp with time zone))
              Rows Removed by Filter: 10587225
        ->  Hash (actual time=3880.2..3880.2 rows=2310004 loops=1)
              ->  Seq Scan on customers c (actual time=0.012..1204.7 rows=2310004 loops=1)
Execution Time: 191880.4 ms           -- 2.1 GB on disk, before a single row is sent
```

*After, both engines*

```text
SQL Server:
  Sort (Top N Sort)       Actual Number of Rows: 500       (no warnings)
    Stream Aggregate      Group By: [o].[CustomerId]      Actual Number of Rows: 1,912,406
      Index Scan [Orders].[IX_Orders_Customer_PlacedAt]
        Predicate: [o].[PlacedAt] >= @from AND [o].[PlacedAt] < @to
        Number of Rows Read: 41,000,000   Actual Number of Rows: 30,412,775
  MemoryGrantInfo (KB): GrantedMemory 61440, MaxUsedMemory 52112
   CPU time = 74102 ms,  elapsed time = 20912 ms.

PostgreSQL, default work_mem (4MB):
  Limit (actual time=13981.9..13982.3 rows=500 loops=1)
    ->  Sort (actual time=13981.9..13982.1 rows=500 loops=1)
          Sort Key: (sum(o.total)) DESC
          Sort Method: top-N heapsort  Memory: 71kB
          ->  GroupAggregate (actual time=0.07..13204.6 rows=1912406 loops=1)
                Group Key: o.customer_id
                ->  Index Only Scan using orders_customer_placed on orders o (rows=30412775 loops=1)
                      Heap Fetches: 18402
  Execution Time: 13991.0 ms
```

**After**

*After: aggregate first, top N, join last*

```sql
WITH top_customers AS (
    SELECT TOP (500)
           o.CustomerId,
           SUM(o.Total)    AS Revenue,
           COUNT_BIG(*)    AS Orders,
           MAX(o.PlacedAt) AS LastOrderAt
    FROM dbo.Orders AS o
    WHERE o.PlacedAt >= @from AND o.PlacedAt < @to
    GROUP BY o.CustomerId                    -- 30.4M rows in, 1.9M narrow rows out
    ORDER BY Revenue DESC                    -- Top N Sort over the totals, not the orders
)
SELECT t.CustomerId, c.Name, c.Email, c.Country, t.Revenue, t.Orders, t.LastOrderAt
FROM top_customers AS t
JOIN dbo.Customers AS c ON c.CustomerId = t.CustomerId    -- 500 lookups
ORDER BY t.Revenue DESC;

-- PostgreSQL: the same query with LIMIT 500 instead of TOP (500)
```

*After: an index that delivers rows grouped by customer*

```sql
-- Rows arrive in CustomerId order: Stream Aggregate (GroupAggregate in PostgreSQL),
-- with no hash table and no sort, so no memory grant for either.
-- If the "my orders" index on (CustomerId, PlacedAt DESC) exists, add Total to its
-- INCLUDE list instead of creating a second index.
CREATE INDEX IX_Orders_Customer_PlacedAt
    ON dbo.Orders (CustomerId, PlacedAt)
    INCLUDE (Total)
    WITH (ONLINE = ON);

-- PostgreSQL 17
CREATE INDEX CONCURRENTLY orders_customer_placed
    ON orders (customer_id, placed_at) INCLUDE (total);
```

*Only then: memory, for this job only*

```sql
-- PostgreSQL: raise work_mem for the report's session or role, never globally.
-- work_mem applies per sort or hash node, per parallel worker, per connection.
SET work_mem = '256MB';                              -- this session only
ALTER ROLE reporting_job SET work_mem = '256MB';     -- every new session of the job's login

-- SQL Server: see what the grant did on recent runs (Query Store memory is in 8 KB pages)
SELECT TOP (5) rs.last_execution_time,
       rs.avg_query_max_used_memory * 8 AS avg_max_used_kb,
       rs.avg_tempdb_space_used * 8     AS avg_tempdb_kb
FROM sys.query_store_runtime_stats AS rs
JOIN sys.query_store_plan AS p ON p.plan_id = rs.plan_id
JOIN sys.query_store_query AS q ON q.query_id = p.query_id
WHERE q.object_id = OBJECT_ID(N'dbo.Report_TopCustomers')
ORDER BY rs.last_execution_time DESC;
```

**Why it works.** Each step removes work before the next is considered. Aggregating in the database turns 30.4M wide rows into 1.9M rows of four narrow columns before anything is sorted, and the job receives 500 rows instead of 30.4M. With the index, rows arrive already grouped by customer, so the aggregate streams and needs no memory at all. The final TOP (500) is a Top N sort: PostgreSQL's top-N heapsort keeps only 500 rows in memory. SQL Server uses its small Top N algorithm only up to TOP (100) and a normal sort above that, which is fine here because its input is now 1.9M narrow rows, about 60 MB.

Memory comes last because it doesn't remove work, it only gives it more room, and that room is shared. In PostgreSQL `work_mem` is a limit per sort or hash node, per parallel worker, per connection (hash nodes may use `hash_mem_multiplier` times it, 2 by default), so it has to be set for the session or role that needs it. In SQL Server, memory grant feedback (row mode at compatibility level 150+; SQL Server 2022 persists it in Query Store and uses a percentile of recent runs) corrects grants between executions, but not on the first run and never above the per-query maximum that capped this sort.

**Result.** The report now runs in 21 seconds with no spill and a 60 MB grant, and sends 500 rows to the job instead of 30.4M; the nightly tempdb alert has stopped. On PostgreSQL the same rewrite takes 14 s at the default work_mem, with `Sort Method: top-N heapsort  Memory: 71kB`.

> **Costs, and when not to.** The aggregation moves CPU from the job's server to the database, as a 20-second burst instead of 14 minutes of tempdb traffic; on a server that is busy at night, schedule it. The index is another 1.1 GB and another write on every order insert. It reads all 41M entries because PlacedAt isn't the leading column, which is acceptable only because three quarters of the orders fall inside the window; for a one-month report, an index led by PlacedAt is the better shape.
>
> Raising memory is the wrong first move. A global `work_mem = '256MB'` looks harmless until 200 connections each run a query with three sort or hash nodes and parallel workers, and the server runs out of memory. In SQL Server, raising the per-query cap for everyone (a Resource Governor workload group setting) or adding MIN_GRANT_PERCENT hints makes concurrent queries queue for memory with RESOURCE_SEMAPHORE waits. Use them for a query that genuinely has to sort a lot, after its shape is right.

> **Lesson:** When a sort spills, first shrink what reaches it (aggregate first, use an index's order, TOP N); raise memory last, and only for that query.

### Plans and statistics

When the SQL is fine but the optimiser picked the wrong plan.

<a id="case-parameter-sniffing-tenant-skew"></a>

#### Case 10 · Parameter sniffing: One tenant with 40% of the orders gets a 45-second plan

*SQL Server 2022 · EF Core 10 · PostgreSQL 17*

**The situation.** A multi-tenant order platform keeps 50M rows in `dbo.Orders`. Tenant 17, a national retailer, owns 20M of them; the median tenant has about 3,000, and there are 4,800 tenants. The "top customers, last 90 days" tile on the tenant dashboard runs about 2M times a day. Every few days it either takes 45 s for tenant 17 (the reporting context has a 60 s command timeout) or 2.3 s for everybody, and the on-call engineer "fixes" it by clearing the plan cache.

**Before**

*Before: one query text for every tenant*

```csharp
public async Task<List<CustomerRevenue>> TopCustomersAsync(
    int tenantId, DateTime from, DateTime to, CancellationToken ct)
{
    var rows = await db.Orders
        .Where(o => o.TenantId == tenantId                  // (1) 300 rows or 2.2M rows, one plan
                 && o.CreatedAt >= from && o.CreatedAt < to)
        .GroupBy(o => new { o.CustomerId, o.Customer.Name })
        .Select(g => new { g.Key.CustomerId, g.Key.Name,
                           Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
        .OrderByDescending(x => x.Revenue)
        .Take(50)
        .ToListAsync(ct);                                   // (2) sp_executesql, cached plan

    return rows.ConvertAll(x => new CustomerRevenue(x.CustomerId, x.Name, x.Orders, x.Revenue));
}
```

*Before: what SQL Server runs (simplified)*

```sql
-- Sent through sp_executesql: the first caller after a recompile shapes the plan
SELECT TOP (50) o.CustomerId, c.Name, COUNT(*) AS Orders, SUM(o.Total) AS Revenue
FROM dbo.Orders AS o
JOIN dbo.Customers AS c ON c.Id = o.CustomerId
WHERE o.TenantId = @tenantId
  AND o.CreatedAt >= @from AND o.CreatedAt < @to
GROUP BY o.CustomerId, c.Name
ORDER BY Revenue DESC;

-- The index it can use, without INCLUDE columns:
-- CREATE INDEX IX_Orders_TenantId_CreatedAt ON dbo.Orders (TenantId, CreatedAt);
```

**What goes wrong.** SQL Server compiles the statement once, with the parameter values of whichever call arrives first after a recompile (a statistics update, a failover, a cache clear), and reuses that plan for every tenant (1, 2). With this skew no single plan is acceptable for both ends:

- Compiled for a small tenant, the estimate is about 300 rows: an index seek, a key lookup per row for `CustomerId` and `Total`, and a nested loop into `Customers`. For tenant 17 that is 2.2M key lookups and 2.2M customer seeks: 45 s.
- Compiled for tenant 17, the estimate is 2.2M rows: a parallel scan of all 50M orders and hash joins. Tenant 17 gets 2.3 s, and so does a tenant with 300 rows, on 8 cores each. The one time this plan stayed cached for 40 minutes, CPU sat at 100% and the whole dashboard timed out.

**Evidence**

*Query Store: one query, two plans (last 7 days)*

```text
-- sys.query_store_plan + sys.query_store_runtime_stats, query_id 4182
-- "compiled for" is ParameterCompiledValue from each plan's XML
plan_id  compiled for      executions  avg_duration_ms  max_duration_ms  avg_logical_reads
311      @tenantId = 17        55,200            2,310            4,080          1,012,400
327      @tenantId = 2291  12,412,800               45           46,900              5,560

-- Regressed Queries report: query 4182 flagged after the Sunday statistics update,
-- when plan 327 replaced plan 311
```

*Actual plan: tenant 17 on the plan compiled for tenant 2291*

```text
Operator                                      Est rows   Actual rows   Executions
Top                                                 50            50            1
Sort (Top N Sort)                                  287            50            1
Hash Match (Aggregate)                             287       186,430            1   spill level 1
Nested Loops (Inner Join)                          312     2,184,906            1
  Nested Loops (Inner Join)                        312     2,184,906            1
    Index Seek  IX_Orders_TenantId_CreatedAt       312     2,184,906            1
    Key Lookup  PK_Orders                            1     2,184,906    2,184,906
  Clustered Index Seek  PK_Customers                 1     2,184,906    2,184,906

ParameterList: @tenantId  ParameterCompiledValue 2291  ParameterRuntimeValue 17
Table 'Orders'. Scan count 1, logical reads 8749950, physical reads 38210, read-ahead reads 297640
Table 'Customers'. Scan count 0, logical reads 6554718
SQL Server Execution Times: CPU time = 21406 ms, elapsed time = 45212 ms.
```

*PostgreSQL: the same query switches to a generic plan (reproduced with PREPARE)*

```text
-- EXECUTE top_customers(...) five times for small tenants, then once for tenant 17
SELECT name, generic_plans, custom_plans FROM pg_prepared_statements;
     name      | generic_plans | custom_plans
---------------+---------------+--------------
 top_customers |             1 |            5

EXPLAIN (ANALYZE, BUFFERS, TIMING OFF) EXECUTE top_customers(17, ...);
Limit  (cost=61.42..61.44 rows=5 width=63) (actual rows=50 loops=1)
  ->  Sort  (cost=61.42..61.43 rows=5 width=63) (actual rows=50 loops=1)
        Sort Key: (sum(o.total)) DESC
        Sort Method: top-N heapsort  Memory: 33kB
        ->  GroupAggregate  (cost=61.24..61.36 rows=5 width=63) (actual rows=186430 loops=1)
              ->  Sort  (cost=61.24..61.25 rows=5 width=29) (actual rows=2184906 loops=1)
                    Sort Method: external merge  Disk: 86720kB
                    ->  Nested Loop  (cost=0.85..61.18 rows=5 width=29) (actual rows=2184906 loops=1)
                          ->  Index Scan using ix_orders_tenant_created on orders o  (cost=0.43..18.98 rows=5 width=14) (actual rows=2184906 loops=1)
                                Index Cond: ((tenant_id = $1) AND (created_at >= $2) AND (created_at < $3))
                          ->  Index Scan using customers_pkey on customers c  (cost=0.42..8.44 rows=1 width=23) (actual rows=1 loops=2184906)
Execution Time: 14862.317 ms

-- With plan_cache_mode = force_custom_plan, the same call gets a parallel hash join:
-- Bitmap Index Scan rows=2201442 (actual rows=2184906), Execution Time: 2118.540 ms
```

**After**

*After: large tenants get their own query text, and recompile*

```csharp
public async Task<List<CustomerRevenue>> TopCustomersAsync(
    Tenant tenant, DateTime from, DateTime to, CancellationToken ct)
{
    var orders = db.Orders.Where(o => o.TenantId == tenant.Id
                                   && o.CreatedAt >= from && o.CreatedAt < to);

    // 11 tenants above 500k orders (flag recomputed nightly). A different SQL text is a
    // different plan cache entry, and these calls also get OPTION (RECOMPILE).
    if (tenant.SizeClass == TenantSize.Large)
        orders = orders.TagWith(RecompileInterceptor.Tag);

    var rows = await orders
        .GroupBy(o => new { o.CustomerId, o.Customer.Name })
        .Select(g => new { g.Key.CustomerId, g.Key.Name,
                           Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
        .OrderByDescending(x => x.Revenue)
        .Take(50)
        .ToListAsync(ct);

    return rows.ConvertAll(x => new CustomerRevenue(x.CustomerId, x.Name, x.Orders, x.Revenue));
}

public sealed class RecompileInterceptor : DbCommandInterceptor
{
    public const string Tag = "recompile";

    // This path is async only, so the sync ReaderExecuting is not overridden
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("-- " + Tag, StringComparison.Ordinal))
            command.CommandText += Environment.NewLine + "OPTION (RECOMPILE)";
        return ValueTask.FromResult(result);
    }
}
// options.UseSqlServer(connectionString).AddInterceptors(new RecompileInterceptor());
```

*The other SQL Server options, and how to check PSP*

```sql
-- 1. Plan every call with its real values: ~9 ms CPU per call for this query
SELECT ... OPTION (RECOMPILE);

-- 2. Plan for the average tenant (density vector), the same plan for everyone
SELECT ... OPTION (OPTIMIZE FOR (@tenantId UNKNOWN));

-- 3. Pin one plan for all values (one tenant group stays the victim), or attach a
--    hint to the query without a deploy (Query Store hints, SQL Server 2022)
EXEC sys.sp_query_store_force_plan @query_id = 4182, @plan_id = 327;
EXEC sys.sp_query_store_set_hints @query_id = 4182, @query_hints = N'OPTION (RECOMPILE)';

-- 4. Parameter Sensitive Plan optimisation: compatibility level 160 only
ALTER DATABASE Shop SET COMPATIBILITY_LEVEL = 160;
ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SENSITIVE_PLAN_OPTIMIZATION = ON;  -- default

-- Did it apply to this query? Variants appear here; no rows means it did not.
-- The parameter_sensitive_plan_optimization_skipped_reason extended event says why.
SELECT v.parent_query_id, v.query_variant_query_id, v.dispatcher_plan_id
FROM sys.query_store_query_variant AS v
WHERE v.parent_query_id = 4182;
```

*PostgreSQL and Npgsql*

```sql
-- A server-side prepared statement is planned with the real values (custom plans)
-- for its first five executions. After that the planner compares a generic plan,
-- built without values, with the average custom plan, and keeps the generic one
-- if it does not look more expensive. Here it estimated 5 rows for any tenant.
EXPLAIN (GENERIC_PLAN)            -- PostgreSQL 16+: show the generic plan
SELECT ... WHERE o.tenant_id = $1 AND o.created_at >= $2 AND o.created_at < $3 ...;

-- Only prepared statements are affected: SQL PREPARE, NpgsqlCommand.Prepare(),
-- or Npgsql auto-prepare (Max Auto Prepare > 0; it prepares a statement after
-- Auto Prepare Min Usages = 5 uses on a connection). Npgsql's default,
-- Max Auto Prepare = 0, sends unnamed statements that are planned on every call.

-- Fix for the reporting role only: always plan with the real values
ALTER ROLE reporting_app SET plan_cache_mode = force_custom_plan;

-- or only around this one query
BEGIN;
SET LOCAL plan_cache_mode = force_custom_plan;
-- ... the top customers query ...
COMMIT;
```

**Why it works.** Each option changes when, and with which values, the plan is chosen:

- `OPTION (RECOMPILE)` plans every call with its real values. The price is a compile per call, about 9 ms of CPU for this query.
- `OPTIMIZE FOR UNKNOWN` plans with the density vector: 50M orders over 4,800 tenants is about 10,400 rows per tenant. The plan stops flipping, but it is chosen for an average tenant that does not exist. Here it picked the seek and lookup plan, so tenant 17 still took 45 s.
- Query Store plan forcing pins one plan for all values. It ends the flip and makes one side the permanent victim.
- Parameter Sensitive Plan (PSP) optimisation looks at the histogram behind a parameterised equality predicate at compile time. If the distribution is skewed enough, it builds a dispatcher plan that sorts each call into a low, medium or high cardinality bucket, each with its own plan (a query variant). It is a heuristic: it may or may not apply to a given query, and the only way to know is to look for variants.
- A separate code path uses what you know and the optimiser does not: which tenants are big. Small tenants share one cached seek plan; large tenants get their own text and recompile.

The database was at compatibility level 150. In staging at 160, PSP did create variants for this query and tenant 17 got its own plan, but moving production to 160 changes estimates for every query and was planned for the next quarter, and PSP's bucket boundaries belong to the optimiser. PostgreSQL has the same trade-off in another place: the generic plan for `tenant_id = $1` with two range parameters estimated 5 rows, so it looked cheaper than the custom plans, and from then on every tenant got it. `force_custom_plan` restores planning with real values, at about 1.7 ms of planning per call here.

**Result.** Tenant 17's tile now takes 2.1 to 2.4 s on its own parallel plan, small tenants stay at 30 to 40 ms on the shared seek plan, and the plan has not flipped in three months. The large path recompiles about 1,500 times a day, roughly 14 s of CPU a day.

> **Costs, and when not to.** The split path needs a size flag that someone maintains. A tenant that grows past the threshold keeps the small-tenant plan until it is reclassified, so the flag is recomputed nightly from order counts. Two query texts also mean two plans to watch in Query Store.
>
> `OPTION (RECOMPILE)` on every call would also have worked: about 5 CPU-hours a day at 2M calls, and 9 ms added to a 38 ms query for small tenants. On a query that runs thousands of times a second it is not affordable. Do none of this when the skew is mild: if the largest tenant is a few times the median, one plan is fine. A covering index would remove the key lookups but not the choice between nested loops and hash joins. In PostgreSQL, `force_custom_plan` for a whole role gives up the planning savings that prepared statements exist for, so scope it to the role or transaction that needs it.

> **Lesson:** When one parameter value matches a thousand times more rows than another, a single cached plan is wrong for someone, so decide deliberately which calls share a plan.

<a id="case-stale-statistics-after-bulk-load"></a>

#### Case 11 · Statistics update after load: Morning report estimates 1 row for 400,000 freshly loaded sales

*SQL Server 2022 · PostgreSQL 17*

**The situation.** A retail reporting database keeps 90M rows in `dbo.Sales`, clustered on `(SaleDate, SaleId)`. The SalesNightly job inserts about 400,000 rows for the previous day at 02:00, and the report pack runs at 06:00 for that day. On Sundays the pack takes 50 s; on the other six days it takes 9 to 12 minutes. On Sunday the pack runs three hours after the weekly maintenance job updates statistics at 03:00.

**Before**

*Before: the nightly load, with no statistics step*

```sql
-- SalesNightly, step 2 (02:00). @LoadDate is yesterday.
INSERT INTO dbo.Sales (SaleDate, SaleId, StoreId, ProductId, Quantity, Amount)
SELECT s.SaleDate, s.SaleId, s.StoreId, s.ProductId, s.Quantity, s.Amount
FROM staging.Sales AS s
WHERE s.SaleDate = @LoadDate;             -- about 400,000 rows
-- (1) the job ends here; statistics wait for the Sunday 03:00 maintenance job
```

*Before: one of the report queries*

```sql
CREATE OR ALTER PROCEDURE dbo.DailyStoreSales @SaleDate date
AS
SELECT st.Region, st.StoreName, p.Category,
       SUM(s.Quantity) AS Units, SUM(s.Amount) AS Revenue
FROM dbo.Sales AS s
JOIN dbo.Stores AS st ON st.StoreId = s.StoreId
JOIN dbo.Products AS p ON p.ProductId = s.ProductId
WHERE s.SaleDate = @SaleDate              -- (2) newer than the histogram's last step
GROUP BY st.Region, st.StoreName, p.Category
ORDER BY st.Region, st.StoreName, p.Category
OPTION (RECOMPILE);                       -- (3) a fresh compile from the same stale statistics
```

**What goes wrong.** The optimiser estimates how many rows match `SaleDate = @SaleDate` (2) from the histogram on the leading column of `CIX_Sales`. The histogram was built on Sunday, so its last step is Saturday. Every later date lies past the last step, and the legacy cardinality estimator, which this database uses because it still runs at compatibility level 110, estimates 1 row for such a value. This is the ascending-key problem: new keys always arrive beyond the histogram, and the load (1) never refreshes it.

With 1 row expected, nested loops into `Stores` and `Products` look free and the sort gets the minimum memory grant. With 400,118 actual rows the loops do 400,118 seeks each and the sort spills to tempdb twice over. `OPTION (RECOMPILE)` (3), added years ago for a parameter sniffing problem, does not help: a fresh compile from stale statistics gives the same estimate.

**Evidence**

*Statistics on Thursday 06:05*

```text
-- sys.dm_db_stats_properties(OBJECT_ID('dbo.Sales'), 1)
stats      last_updated         rows        rows_sampled  modification_counter
CIX_Sales  2026-09-27 03:14:52  89,610,402  412,880       1,603,961

-- DBCC SHOW_STATISTICS ('dbo.Sales', CIX_Sales) WITH HISTOGRAM, last two steps
RANGE_HI_KEY  RANGE_ROWS  EQ_ROWS  DISTINCT_RANGE_ROWS  AVG_RANGE_ROWS
2026-09-24    398,112     401,006  1                    398,112
2026-09-26    396,551     402,117  1                    396,551

-- Four nightly loads since the last update: 1.6M changes. The compatibility level 110
-- threshold is 500 + 20% of rows = 17.9M, about 40 more nightly loads away.
```

*Actual plan: EXEC dbo.DailyStoreSales @SaleDate = '2026-09-30'*

```text
Operator                                  Est rows  Actual rows  Executions
Sort (ORDER BY)                                  1        6,412           1
Stream Aggregate                                 1        6,412           1
Sort                                             1      400,118           1   spill level 2
Nested Loops (Inner Join)                        1      400,118           1
  Nested Loops (Inner Join)                      1      400,118           1
    Clustered Index Seek  CIX_Sales              1      400,118           1
    Clustered Index Seek  PK_Stores              1      400,118     400,118
  Clustered Index Seek    PK_Products            1      400,118     400,118

Warning: Operator used tempdb to spill data during execution with spill level 2
         and 1 spilled thread(s)
Granted memory: 1,024 KB
Table 'Products'. Scan count 0, logical reads 1200354
Table 'Stores'. Scan count 0, logical reads 800236
Table 'Sales'. Scan count 1, logical reads 4521
SQL Server Execution Times: CPU time = 4812 ms, elapsed time = 58940 ms.
```

*After the statistics step: Friday's run*

```text
-- sys.dm_db_stats_properties after step 3 (02:41)
stats      last_updated         rows        rows_sampled  modification_counter
CIX_Sales  2026-10-02 02:41:07  91,615,740  91,615,740    0

Operator                                  Est rows  Actual rows  Executions
Sort (ORDER BY)                              6,380        6,401           1
Hash Match (Aggregate)                       6,380        6,401           1
Hash Match (Inner Join)                    401,377      401,377           1
  Clustered Index Scan  PK_Stores              400          400           1
  Hash Match (Inner Join)                  401,377      401,377           1
    Clustered Index Scan  PK_Products       20,000       20,000           1
    Clustered Index Seek  CIX_Sales        401,377      401,377           1
Granted memory: 52,416 KB, no spills
SQL Server Execution Times: CPU time = 610 ms, elapsed time = 702 ms.
```

*PostgreSQL: the same load, before ANALYZE*

```text
SELECT n_live_tup, n_mod_since_analyze, last_autoanalyze
FROM pg_stat_user_tables WHERE relname = 'sales';
 n_live_tup | n_mod_since_analyze |    last_autoanalyze
------------+---------------------+------------------------
   91214363 |             1603961 | 2026-09-27 04:12:09+00
-- autovacuum analyses after 50 + 0.1 x reltuples, about 9M changes

-- Filter on sold_at; the only index containing it is (store_id, sold_at)
Nested Loop  (cost=0.86..3402.62 rows=1 width=32) (actual rows=400118 loops=1)
  ->  Nested Loop  (cost=0.57..3394.31 rows=1 width=26) (actual rows=400118 loops=1)
        ->  Seq Scan on stores st  (cost=0.00..8.00 rows=400 width=14) (actual rows=400 loops=1)
        ->  Index Scan using ix_sales_store_sold_at on sales s  (cost=0.57..8.47 rows=1 width=16) (actual rows=1000 loops=400)
              Index Cond: ((store_id = st.store_id) AND (sold_at >= '2026-09-30 00:00:00+00'::timestamptz) AND (sold_at < '2026-10-01 00:00:00+00'::timestamptz))
  ->  Index Scan using products_pkey on products p  (cost=0.29..8.30 rows=1 width=10) (actual rows=1 loops=400118)
Sort Method: external merge  Disk: 18320kB

-- After ANALYZE sales, the same filter is estimated at rows=403301
```

**After**

*After: statistics are part of the load (SQL Server)*

```sql
-- SalesNightly, step 2: the same insert of about 400,000 rows
-- SalesNightly, step 3: refresh the statistics the morning queries depend on
UPDATE STATISTICS dbo.Sales CIX_Sales WITH FULLSCAN;     -- reads the whole table, ~70 s

-- Cheaper, if the batch window is tight; the sample rate is kept for auto-updates too:
-- UPDATE STATISTICS dbo.Sales CIX_Sales WITH SAMPLE 5 PERCENT, PERSIST_SAMPLE_PERCENT = ON;

-- Last job step: fail loudly if the statistics did not move
IF EXISTS (SELECT 1
           FROM sys.stats AS s
           CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) AS sp
           WHERE s.object_id = OBJECT_ID('dbo.Sales') AND s.name = 'CIX_Sales'
             AND sp.last_updated < DATEADD(hour, -2, SYSDATETIME()))
    THROW 50002, 'CIX_Sales statistics were not refreshed by SalesNightly.', 1;
```

*After: PostgreSQL*

```sql
-- End of the nightly load
INSERT INTO sales (sale_date, sale_id, store_id, product_id, sold_at, quantity, amount)
SELECT sale_date, sale_id, store_id, product_id, sold_at, quantity, amount
FROM staging_sales
WHERE sale_date = $1;

ANALYZE sales;   -- samples 300 x default_statistics_target = 30,000 rows: seconds, not minutes

-- Between loads, let autovacuum analyse this table after ~190k changes instead of ~9M
ALTER TABLE sales SET (autovacuum_analyze_scale_factor = 0.002,
                       autovacuum_analyze_threshold   = 10000);
```

**Why it works.** A histogram only knows the values that existed when it was built. A query for a newer value gets a guess, and the legacy estimator's guess is 1 row. Updating the statistics as the last step of the load puts yesterday into the histogram before anyone queries it, so the 06:00 compile sees 401,377 rows and picks hash joins with a proper memory grant.

Auto-update did not help because its threshold was not reached:

- At compatibility level 110, SQL Server waits for 500 + 20% of the rows to change: 17.9M here.
- At level 130 and above the threshold is dynamic, about sqrt(1000 x rows), roughly 300,000 for 90M rows, so this load would cross it every night. Even then the update fires only when a query next compiles against the statistic. Here that is the report itself, which would wait for a sampled update in the middle of its compile.

Putting the update in the job decides when it runs and how much it reads. PostgreSQL has the same threshold logic (`autovacuum_analyze_threshold + autovacuum_analyze_scale_factor x reltuples`) and is less exposed to this exact shape: for `sale_date = $1` it estimates from `n_distinct`, about 400,000 rows per date, and for a range past the last histogram bound it reads the real maximum from a B-tree whose leading column is the filtered column. Without such an index, as for `sold_at` here, the range estimate drops to 1 row.

**Result.** Since the statistics step went in, the report pack takes 40 to 60 s every morning, whatever the day of the week, and the actual plans show no spill warnings. The step adds 70 s to the 02:00 job.

> **Costs, and when not to.** `FULLSCAN` reads the whole 90M-row table every night, 70 s of I/O that competes with anything else in the batch window. A sample is cheaper and usually enough when one date holds 400,000 rows. Updating statistics also invalidates the cached plans that depend on them, so expect a burst of recompiles after the job; here that is the point, on a busy OLTP table it is a cost.
>
> Do not add `UPDATE STATISTICS` for every table after every load: refresh the statistics whose new values the next queries filter on. Raising the compatibility level would bring the dynamic threshold and the newer estimator, which assumes some rows exist past the last step instead of 1, but it changes estimates for every query in the database and needs its own project with Query Store baselines. In PostgreSQL, a low `autovacuum_analyze_scale_factor` on a table that changes all day means frequent analyses and constant background I/O; set it per table, not globally.

> **Lesson:** Statistics describe the data when they were built, so a job that loads new key values should refresh them before anyone queries those values.

### Writes and locking

Deletes, counters and readers that stall everyone else.

<a id="case-batched-retention-delete"></a>

#### Case 12 · Batched delete: A retention DELETE that blocked every audit write for three hours

*SQL Server 2022 · PostgreSQL 17*

**The situation.** `dbo.AuditEvents` in the `Shop` database holds 640M rows: every change to an order, invoice or subscription writes one. Policy keeps 18 months. The retention job had been broken for a year, so its first run after the fix had 180M rows to remove. It started at 01:00 as a single statement. By 03:00 the on-call engineer had been paged for blocked checkouts, an Availability Group secondary 40 minutes behind and a transaction log at 400 GB.

**Before**

*Before: one statement for the whole backlog*

```sql
-- RetentionJob (SQL Server), monthly at 01:00
DECLARE @cutoff datetime2(3) = DATEADD(month, -18, SYSUTCDATETIME());

DELETE FROM dbo.AuditEvents          -- (1) one statement, one transaction, 180M rows
WHERE CreatedAt < @cutoff;           -- (2) no index on CreatedAt: scans 640M rows
```

**What goes wrong.** One statement is one transaction (1), and everything about it scales with 180M rows:

- **Log.** The log cannot be truncated past the oldest active transaction, so log backups freed nothing. Each deleted row is logged once per index, and the log grew to 400 GB.
- **Locks.** Once the statement held about 5,000 locks on the table, SQL Server escalated to an exclusive table lock. From a few seconds after 01:00, every insert into `AuditEvents`, and so every checkout that writes an audit row, waited on `LCK_M_IX`.
- **Replica.** The secondary has to redo the same 400 GB. Redo fell 40 minutes behind, so readable-secondary reports were 40 minutes stale and a failover would have needed 40 minutes of redo first.
- **Cancel.** Killing the session at 03:10 released nothing. Rollback undoes every row, single-threaded, and took 61 minutes with the table lock still held.

The scan (2) made each part slower, but even with an index the single transaction is the problem.

**Evidence**

*At 02:55, before the kill*

```text
-- sys.databases and DBCC SQLPERF(LOGSPACE)
name  log_reuse_wait_desc   Log Size (MB)  Log Space Used (%)
Shop  ACTIVE_TRANSACTION    401,520        97.6

-- sys.dm_tran_locks, resource OBJECT_ID('dbo.AuditEvents')
request_session_id  resource_type  request_mode  request_status
87                  OBJECT         X             GRANT
(140 sessions)      OBJECT         IX            WAIT

-- sys.dm_hadr_database_replica_states, the secondary
redo_queue_size (KB)  redo_rate (KB/s)  behind
61,804,320            25,600            ~40 min

-- KILL 87, then KILL 87 WITH STATUSONLY at 03:33
SPID 87: transaction rollback in progress. Estimated rollback completion: 38%.
Estimated time remaining: 2210 seconds.
```

**After**

*After: small batches, each its own transaction (SQL Server)*

```sql
-- One-off, before the first run (ONLINE = ON needs Enterprise edition)
CREATE INDEX IX_AuditEvents_CreatedAt ON dbo.AuditEvents (CreatedAt)
    WITH (ONLINE = ON);

-- The job step runs in autocommit mode: each DELETE commits on its own
SET DEADLOCK_PRIORITY LOW;           -- if it collides with checkout, it loses
DECLARE @cutoff datetime2(3) = DATEADD(month, -18, SYSUTCDATETIME());
DECLARE @rows int = 1;

WHILE @rows > 0
BEGIN
    DELETE TOP (4000) FROM dbo.AuditEvents    -- below the ~5,000-lock escalation point
    WHERE CreatedAt < @cutoff;                -- can seek IX_AuditEvents_CreatedAt
    SET @rows = @@ROWCOUNT;

    -- Back off while the secondary is more than 2 GB behind
    WHILE EXISTS (SELECT 1 FROM sys.dm_hadr_database_replica_states
                  WHERE database_id = DB_ID() AND is_local = 0
                    AND redo_queue_size > 2097152)     -- KB
        WAITFOR DELAY '00:00:10';

    WAITFOR DELAY '00:00:00.100';             -- room for log backups and other writers
END;
```

*After: PostgreSQL*

```sql
CREATE INDEX CONCURRENTLY ix_audit_events_created_at ON audit_events (created_at);

CREATE OR REPLACE PROCEDURE purge_audit_events(cutoff timestamptz, batch_size int DEFAULT 5000)
LANGUAGE plpgsql AS $$
DECLARE
    n int;
BEGIN
    LOOP
        DELETE FROM audit_events
        WHERE id IN (SELECT id FROM audit_events
                     WHERE created_at < cutoff
                     ORDER BY created_at
                     LIMIT batch_size);
        GET DIAGNOSTICS n = ROW_COUNT;
        EXIT WHEN n = 0;
        COMMIT;                     -- one transaction per batch (procedures, PostgreSQL 11+)
        PERFORM pg_sleep(0.1);
    END LOOP;
END $$;

-- Call it outside an explicit transaction, or COMMIT fails with
-- "invalid transaction termination"
CALL purge_audit_events(now() - interval '18 months');

-- Watch the replicas while it runs
SELECT application_name, replay_lag FROM pg_stat_replication;

-- Afterwards: make the space reusable (the file does not shrink)
VACUUM (VERBOSE) audit_events;
```

**Why it works.** Each batch is a short transaction: 4,000 rows, about 9 MB of log, a few milliseconds of locks. When it commits, its log can be backed up and reused, its locks are released long before escalation, and the secondary redoes it in a fraction of a second. Escalation is attempted once a statement holds about 5,000 locks on one table or index, so the batch stays below that. The pause and the redo-queue check let log backups, redo and normal writers keep up. The index on `CreatedAt` lets every batch find its rows directly instead of scanning, so the last batch costs about the same as the first.

A cancel now rolls back at most one batch, and a restart simply carries on, because the loop has no state beyond the cutoff.

PostgreSQL differs in two ways. There is no lock escalation, and a rollback is instant because there is no undo: the rows simply stay. A single huge delete still floods the replicas with WAL and, while it runs, holds back the xmin horizon, so VACUUM cannot remove rows that become dead after it started, in any table of the database. After the purge the 180M row versions are dead tuples until VACUUM removes them. VACUUM makes the space reusable for new rows but returns nothing to the operating system, because the empty pages are at the start of the table, not the end; for that you need `VACUUM FULL` (an exclusive lock for the whole rewrite) or pg_repack.

**Result.** The catch-up run removed 180M rows in 9 h 40 min, at about 5,200 rows/s, during business hours and without blocking: checkout p99 stayed at 180 ms, the log peaked at 31 GB with 15-minute log backups, and the secondary's redo queue never passed 2 GB. The monthly run now deletes about 16M rows in 52 minutes.

> **Costs, and when not to.** Batching does not reduce the total work. The 180M rows still produced about 400 GB of log, now spread over ten hours and through the log backups, so backup storage and the link to the secondary had to absorb it. The job takes far longer in wall-clock time, and the new index on `CreatedAt` is one more index every audit insert maintains.
>
> Batching is the right tool for a one-off cleanup or a modest monthly volume. If you delete a large slice of a table every month, deleting rows is the wrong design: partition by month and drop whole partitions (the next case). If you are deleting most of a table, copying the rows you keep into a new table and swapping names can be cheaper than deleting the rest.

> **Lesson:** Delete in small, separately committed batches through an index, and throttle on log and replica health, so no single transaction is big enough to hurt.

<a id="case-partition-retention-drop"></a>

#### Case 13 · Partitioned retention: Dropping a month of audit events instead of deleting it

*SQL Server 2022 · PostgreSQL 17*

**The situation.** After the batched delete (previous case), `AuditEvents` retention still took 52 minutes and about 36 GB of log every month to remove roughly 16M rows, and the volume keeps growing with the business. The team rebuilt the table with monthly partitions on `CreatedAt`, so that retention removes a whole month without touching its rows.

**Before**

*Before: the monthly batched delete*

```sql
-- Correct and safe, but its cost is proportional to the rows
WHILE @rows > 0
BEGIN
    DELETE TOP (4000) FROM dbo.AuditEvents
    WHERE CreatedAt < @cutoff;              -- (1) 16M rows located and deleted one by one
    SET @rows = @@ROWCOUNT;
    WAITFOR DELAY '00:00:00.100';
END;
-- (2) every deleted row is logged in each of 5 indexes, shipped to the secondary,
--     and later removed from its page by ghost cleanup
```

**What goes wrong.** The batched delete is safe, but its cost grows with the number of rows (1). Every month 16M rows are found through the index, deleted, logged once per index (2), redone on the secondary and cleaned up by ghost cleanup afterwards. Next year's months are bigger, so the job and its log volume grow with them, and the deletes leave half-empty pages scattered through the indexes.

**Evidence**

*The same job, before and after partitioning*

```text
-- September, batched delete
rows deleted       16,284,117
duration           52 min 40 s
log generated      35.8 GB   (log backups taken during the run)
redo queue peak    1.9 GB

-- October, TRUNCATE TABLE ... WITH (PARTITIONS (2))
rows removed       16,612,940
duration           0.4 s     (Sch-M lock on the table for that time)
log generated      a few MB  (page and extent deallocations, not rows)

-- sys.partitions (index_id = 1) before the October run
partition  lower bound   rows
1          (none)                 0
2          2025-03-01    16,612,940
3          2025-04-01    16,904,212
...
21         2026-10-01       831,406    (October so far)
22         2026-11-01             0    (empty, ready to split)
```

**After**

*After: monthly partitions (SQL Server)*

```sql
CREATE PARTITION FUNCTION pf_AuditMonth (datetime2(3))
    AS RANGE RIGHT FOR VALUES ('2025-03-01', '2025-04-01', /* ... */ '2026-11-01');
CREATE PARTITION SCHEME ps_AuditMonth
    AS PARTITION pf_AuditMonth ALL TO ([PRIMARY]);

CREATE TABLE dbo.AuditEvents
(
    AuditEventId bigint IDENTITY NOT NULL,
    CreatedAt    datetime2(3)    NOT NULL,
    TenantId     int             NOT NULL,
    EntityType   varchar(40)     NOT NULL,
    EntityId     bigint          NOT NULL,
    Action       varchar(40)     NOT NULL,
    Payload      nvarchar(max)   NULL,
    CONSTRAINT PK_AuditEvents
        PRIMARY KEY CLUSTERED (CreatedAt, AuditEventId)   -- must contain CreatedAt
) ON ps_AuditMonth (CreatedAt);

-- Every index aligned on the same scheme, or TRUNCATE and SWITCH by partition fail
CREATE INDEX IX_AuditEvents_Entity
    ON dbo.AuditEvents (EntityType, EntityId, CreatedAt)
    ON ps_AuditMonth (CreatedAt);
```

*After: the monthly job (SQL Server)*

```sql
-- 0. Refuse to split a partition that holds rows: that would move them
IF EXISTS (SELECT 1 FROM sys.partitions
           WHERE object_id = OBJECT_ID('dbo.AuditEvents') AND index_id = 1
             AND partition_number = $PARTITION.pf_AuditMonth('2026-12-01')
             AND rows > 0)
    THROW 50001, 'The partition to split is not empty; splitting it would move rows.', 1;

-- 1. Split the empty last partition so next month has a home: metadata only
ALTER PARTITION SCHEME ps_AuditMonth NEXT USED [PRIMARY];
ALTER PARTITION FUNCTION pf_AuditMonth() SPLIT RANGE ('2026-12-01');

-- 2. Empty the oldest month (March 2025, partition 2): metadata only
DECLARE @p int = $PARTITION.pf_AuditMonth('2025-03-01');
DECLARE @sql nvarchar(200) =
    N'TRUNCATE TABLE dbo.AuditEvents WITH (PARTITIONS (' + CAST(@p AS nvarchar(10)) + N'));';
EXEC sys.sp_executesql @sql;
-- To archive instead: ALTER TABLE dbo.AuditEvents SWITCH PARTITION 2 TO dbo.AuditEvents_Out;
-- (an empty table with the same columns and indexes, on the same filegroup)

-- 3. Remove the boundary between two empty partitions: metadata only
ALTER PARTITION FUNCTION pf_AuditMonth() MERGE RANGE ('2025-03-01');
```

*After: PostgreSQL declarative partitioning*

```sql
CREATE TABLE audit_events (
    id          bigint GENERATED ALWAYS AS IDENTITY,
    created_at  timestamptz NOT NULL,
    tenant_id   int         NOT NULL,
    entity_type text        NOT NULL,
    entity_id   bigint      NOT NULL,
    action      text        NOT NULL,
    payload     jsonb,
    PRIMARY KEY (id, created_at)       -- a unique constraint must include the partition key
) PARTITION BY RANGE (created_at);

CREATE INDEX ON audit_events (entity_type, entity_id, created_at);  -- on every partition

CREATE TABLE audit_events_2026_10 PARTITION OF audit_events
    FOR VALUES FROM ('2026-10-01 00:00+00') TO ('2026-11-01 00:00+00');

-- Monthly job (pg_cron, or pg_partman with a premake of a few months)
CREATE TABLE IF NOT EXISTS audit_events_2026_12 PARTITION OF audit_events
    FOR VALUES FROM ('2026-12-01 00:00+00') TO ('2027-01-01 00:00+00');

-- PostgreSQL 14+: reads and writes on the parent continue. Not allowed in a transaction
-- block, or when the table has a DEFAULT partition.
ALTER TABLE audit_events DETACH PARTITION audit_events_2025_03 CONCURRENTLY;
DROP TABLE audit_events_2025_03;
```

**Why it works.** A partition is a separate B-tree in SQL Server and a separate table in PostgreSQL. Removing a month removes that structure: SQL Server deallocates the partition's pages and logs the deallocations, not 16M row deletes; PostgreSQL drops the partition's files. Nothing is shipped row by row to the replicas, and there is nothing left for ghost cleanup or VACUUM.

The rolling window only stays cheap if the boundary operations stay metadata-only. `SPLIT` of an empty partition and `MERGE` of two empty partitions only change the function. `SPLIT` of a partition that holds rows moves every row on the far side of the new boundary, fully logged, under a schema modification lock that blocks every query on the table. That is why the job splits next month's partition while it is still empty and refuses to split otherwise. In PostgreSQL the failure is louder: an insert for a range with no partition fails with "no partition of relation found for row", so partitions are created months ahead, with pg_partman or a scheduled job. Timestamp bounds are written with an explicit `+00` so that the session's time zone does not move them.

**Result.** The October run removed March 2025 (16.6M rows) in 0.4 s with a few MB of log, against 52 minutes and 35.8 GB for the batched delete. Queries that filter on `CreatedAt`, such as the audit screen's "last 30 days", now read one or two partitions.

> **Costs, and when not to.** Partitioning is a manageability feature, not a speed-up. Queries that do not filter on the partition key touch every partition: looking up one event by `AuditEventId` alone now needs an aligned index on it and probes every partition, about 60 logical reads instead of 4, and the "all events for order 123" query had to gain a date range. Unique constraints and primary keys must include the partition key, so the database no longer enforces `AuditEventId` alone as unique; the identity makes that acceptable here. In SQL Server every index must be aligned for `TRUNCATE` and `SWITCH` by partition, and the split-ahead job becomes critical: if it fails for two months, rows pile into the last partition and the next split has to move them.
>
> Converting the existing table was a project of its own: a new partitioned table, dual writes, a backfill and a cut-over. Do not partition for retention when a batched delete takes minutes, or when rows do not leave by the partition key, for example when retention is per tenant.

> **Lesson:** If data leaves a table by age in large chunks, make the age the partition key and drop partitions: deleting rows costs work per row, dropping a partition does not.

<a id="case-hot-row-inventory"></a>

#### Case 14 · Atomic conditional update: A 500-unit flash sale that managed 30 orders a second

*C# · EF Core 10 · SQL Server 2022 · PostgreSQL 17*

**The situation.** A consumer electronics shop sells 500 units of one pair of headphones at 19:00. About 40,000 people are waiting, and checkout receives around 2,500 order attempts a second in the first minute. Successful orders peaked at 31 a second, the last unit sold after 38 s, checkout p99 reached 11 s, and 1,140 customers were charged for orders that were then rolled back.

**Before**

*Before: read, check, pay, update, all in one transaction*

```csharp
public async Task<OrderResult> PlaceOrderAsync(PlaceOrder cmd, CancellationToken ct)
{
    await using var tx = await db.Database.BeginTransactionAsync(
        IsolationLevel.RepeatableRead, ct);                             // (1)

    var stock = await db.Stock.SingleAsync(s => s.Sku == cmd.Sku, ct);  // (2) S lock until commit
    if (stock.Available < cmd.Quantity)
        return OrderResult.SoldOut;

    var payment = await payments.ChargeAsync(                           // (3) 400-900 ms,
        cmd.PaymentToken, cmd.Amount, ct);                              //     locks still held
    if (!payment.Succeeded)
        return OrderResult.PaymentFailed;

    stock.Available -= cmd.Quantity;                                    // (4) S to X: waits,
    db.Orders.Add(Order.Create(cmd, payment.PaymentId));                //     then deadlocks
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return OrderResult.Placed;
}
// PostgreSQL version of the same bug: SELECT ... FOR UPDATE before the payment call.
// No deadlocks, but every buyer queues behind the row lock for the payment latency.
```

**What goes wrong.** Under `REPEATABLE READ` (1), the read (2) takes a shared lock on the one `Stock` row and keeps it until commit. Hundreds of requests hold that shared lock at the same time, all see `Available = 500`, and all go to the payment provider (3) with the lock held. When they come back, each must convert its shared lock to exclusive (4), and none can while others hold shared locks. Pairs of them wait on each other: a conversion deadlock. The deadlock monitor kills victims a few at a time, the rest wait on `LCK_M_X`, and the victims' customers have already been charged.

With `SELECT ... FOR UPDATE` in PostgreSQL there is no deadlock, but buyers are served one at a time, each holding the lock for a whole payment call: at 600 ms that is under 2 orders a second. Either way, one row and one slow external call bound the whole sale.

**Evidence**

*The first minute of the sale*

```text
-- 19:00:00 to 19:01:00, sys.dm_os_wait_stats delta
wait_type   waiting_tasks_count  wait_time_ms  avg_wait_ms
LCK_M_X                  10,512    24,703,200        2,350
LCK_M_S                   1,204       810,300          673

-- 19:00:20, sys.dm_exec_requests
blocked sessions  wait_type  wait_resource
             412  LCK_M_X    KEY: 7:72057594046185472 (8a2f61c3b1e4)   -- the one Stock row

-- SQLServer:Locks, Number of Deadlocks/sec
19:00:10  42     19:00:30  57     19:00:50  61

-- Checkout service, first two minutes
orders placed: peak 31/s    p99: 11.2 s    SqlException 1205 (deadlock victim): 3,388
payments captured for orders that rolled back: 1,140
```

**After**

*After: reserve with one conditional update, pay outside the transaction*

```csharp
public async Task<OrderResult> PlaceOrderAsync(PlaceOrder cmd, CancellationToken ct)
{
    // 1. Reserve. Default READ COMMITTED; the row lock is held for about a millisecond.
    await using (var tx = await db.Database.BeginTransactionAsync(ct))
    {
        var reserved = await db.Stock
            .Where(s => s.Sku == cmd.Sku && s.Available >= cmd.Quantity)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Available, s => s.Available - cmd.Quantity)
                .SetProperty(s => s.Reserved, s => s.Reserved + cmd.Quantity), ct);

        if (reserved == 0)
            return OrderResult.SoldOut;                  // nothing to undo

        db.Reservations.Add(new Reservation(cmd.OrderId, cmd.Sku, cmd.Quantity,
            expiresAt: clock.UtcNow.AddMinutes(10)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // 2. Pay with no transaction and no lock open. The order id is the idempotency key.
    var payment = await payments.ChargeAsync(
        cmd.PaymentToken, cmd.Amount, idempotencyKey: cmd.OrderId.ToString(), ct);

    // 3. Confirm or release; both only act on a reservation that is still Held
    return payment.Succeeded
        ? await reservations.ConfirmAsync(cmd.OrderId, payment.PaymentId, ct)
        : await reservations.ReleaseAsync(cmd.OrderId, ct);
}
```

*After: the statement EF sends, and the release (SQL Server)*

```sql
-- The reservation, roughly as EF Core sends it
UPDATE [s]
SET [s].[Available] = [s].[Available] - @qty,
    [s].[Reserved]  = [s].[Reserved] + @qty
FROM [Stock] AS [s]
WHERE [s].[Sku] = @sku AND [s].[Available] >= @qty;     -- 0 rows affected = sold out

-- Release (payment failed, or the expiry job found it past ExpiresAt).
-- The status check makes it safe to run twice and safe against a racing confirm.
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE dbo.Reservations SET Status = 'Released'
WHERE OrderId = @orderId AND Status = 'Held';
IF @@ROWCOUNT = 1
    UPDATE dbo.Stock
    SET Available = Available + @qty, Reserved = Reserved - @qty
    WHERE Sku = @sku;
COMMIT;
```

*For extreme contention: split the counter (PostgreSQL)*

```sql
-- 500 units over 16 rows: buyers lock one of them, not all the same one
CREATE TABLE stock_slots (
    sku       text     NOT NULL,
    slot      smallint NOT NULL,
    available int      NOT NULL CHECK (available >= 0),
    PRIMARY KEY (sku, slot)
);

UPDATE stock_slots s
SET available = s.available - $2
FROM (SELECT sku, slot
      FROM stock_slots
      WHERE sku = $1 AND available >= $2
      ORDER BY random()
      LIMIT 1
      FOR UPDATE SKIP LOCKED) AS pick        -- skip slots another buyer holds right now
WHERE s.sku = pick.sku AND s.slot = pick.slot
RETURNING s.slot;
-- No row back does not always mean sold out: every slot with stock may be locked
-- at this instant. Retry a few times, then check SUM(available) before saying so.
```

**Why it works.** The conditional update moves the check into the statement that changes the row. The database evaluates `Available >= @qty` and decrements under the same exclusive row lock, so two buyers can never both take the last unit: the second waits, then sees the new value and matches no row. No isolation level above READ COMMITTED is needed, and the affected-row count tells the caller what happened. The lock now lasts one short transaction, about a millisecond, instead of a payment call, so one row can serve several hundred reservations a second.

The payment runs after the commit, so a slow provider holds nothing in the database. The reservation with an expiry is what makes that safe: if the process dies between reserving and paying, the expiry job returns the units. When one row still cannot keep up, splitting it into N slot rows divides the contention by N, and `SKIP LOCKED` sends each buyer to a slot nobody is holding. An append-only reservations ledger goes further and removes the shared counter: each buyer inserts a reservation, and the limit is enforced by claiming one of 500 pre-created unit rows with `SKIP LOCKED`.

**Result.** The next sale, 800 units, sold out in 1.9 s. Reservation p99 was 24 ms, `LCK_M_X` waits averaged 3 ms, there were no deadlocks, and no customer was charged without an order. The single-row version was enough; the slot table was tested for Black Friday and not needed.

> **Costs, and when not to.** The checkout is now a small state machine: held, confirmed, released or expired. It needs the expiry job, idempotent confirm and release, and a rule for a payment that succeeds after its reservation expired (here: reserve again if stock remains, otherwise refund automatically). The expiry must be longer than the payment provider's timeout.
>
> Slot rows make reads dearer (stock is a `SUM`), can say "sold out" while one slot still has units, and need rebalancing as slots drain. Do not build them, or a ledger, until one conditional `UPDATE` per order has been measured as the bottleneck; for normal product traffic it never is.

> **Lesson:** Make the check and the change one conditional statement, read the affected-row count, and never hold a row lock across a call to another system.

<a id="case-readers-block-writers-rcsi"></a>

#### Case 15 · Read committed snapshot: Month-end reports that froze checkout, and the NOLOCK that lied

*SQL Server 2022 · PostgreSQL 17*

**The situation.** The shop's `Shop` database runs SQL Server's default, locking read committed. On the last two working days of each month, finance runs about 30 reports against `Orders` and `OrderLines` (40M and 180M rows) on the primary. During those days checkout p99 rose from 150 ms to 8 s. A developer added `WITH (NOLOCK)` to the reports; the next month checkout was fine, and the revenue report disagreed with the payment provider's settlement by £48,210.

**Before**

*Before: a month-end report under locking read committed*

```sql
-- Revenue by day and country, run by finance on the primary
SELECT CAST(o.PlacedAt AS date) AS Day, o.Country,
       COUNT(DISTINCT o.OrderId) AS Orders,
       SUM(l.Quantity * l.UnitPrice) AS Revenue
FROM dbo.Orders AS o
JOIN dbo.OrderLines AS l ON l.OrderId = o.OrderId
WHERE o.PlacedAt >= @monthStart AND o.PlacedAt < @nextMonth   -- (1) the pages checkout writes
  AND o.Status <> 'Cancelled'
GROUP BY CAST(o.PlacedAt AS date), o.Country;
-- (2) each row read needs a shared lock: the report waits for open checkout
--     transactions, and checkouts queue behind its waiting lock request
```

*Before: the quick fix*

```sql
FROM dbo.Orders AS o WITH (NOLOCK)                     -- (3) READ UNCOMMITTED
JOIN dbo.OrderLines AS l WITH (NOLOCK) ON l.OrderId = o.OrderId
-- Reads rows from transactions that later roll back, and an allocation-order scan
-- can read a row twice, or miss it, when a page split moves it during the scan
```

**What goes wrong.** Under locking read committed, a reader takes a shared lock on each row or page as it reads it (2). The current month's orders are exactly where checkout is writing (1): new orders, status changes, payment confirmations. The report waits for each open checkout transaction that holds a row it needs (`LCK_M_S`). While its request waits, later checkouts that need an incompatible lock on the same page queue behind it (`LCK_M_IX`, `LCK_M_X`), because SQL Server grants waiting lock requests in order. Thirty reports scanning the same hot pages turn each collision into a queue, and checkout p99 reached 8 s.

`NOLOCK` (3) removes the waiting by removing the guarantees. The report counted orders whose payment later failed and rolled back, and during page splits it read some rows twice and skipped others. Running it twice in a row gave two different totals.

**Evidence**

*Month-end, and the NOLOCK month*

```text
-- 10:00 to 11:00 on the last working day, sys.dm_os_wait_stats delta
wait_type  waiting_tasks_count  wait_time_ms  avg_wait_ms
LCK_M_S                 21,840    13,977,600          640
LCK_M_IX                 9,377    13,596,650        1,450
LCK_M_X                  4,112     4,029,760          980

-- 10:42:15, sys.dm_exec_requests
session  program          wait_type  wait_ms  blocked_by  wait_resource
    214  FinanceReports   LCK_M_S        610         402  PAGE: 7:1:48211907
    517  Checkout         LCK_M_IX       580         214  PAGE: 7:1:48211907
    521  Checkout         LCK_M_IX       544         214  PAGE: 7:1:48211907
    530  Checkout         LCK_M_IX       512         214  PAGE: 7:1:48211907
    402  Checkout         (running INSERT INTO dbo.OrderLines, transaction open 640 ms)
-- at this moment: 31 report sessions and 58 checkout sessions blocked

-- The NOLOCK month
revenue report                 4,812,330.17
provider settlement            4,764,120.17
difference                        48,210.00
```

**After**

*After: read committed snapshot for the database*

```sql
-- Only this connection may be in the database while it runs. WITH ROLLBACK IMMEDIATE
-- rolls back other sessions' open transactions instead of waiting for them.
ALTER DATABASE Shop SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;

SELECT name, is_read_committed_snapshot_on, is_accelerated_database_recovery_on
FROM sys.databases
WHERE name = N'Shop';

-- The reports lose their hints and stay at plain READ COMMITTED. Each statement now
-- reads the last committed version of every row as of the moment it started.

-- Watch the version store: tempdb, or the persistent version store when ADR is on
SELECT DB_NAME(database_id) AS db, reserved_space_kb
FROM sys.dm_tran_version_store_space_usage;

SELECT persistent_version_store_size_kb
FROM sys.dm_tran_persistent_version_store_stats
WHERE database_id = DB_ID(N'Shop');

-- What keeps old versions alive
SELECT TOP (5) session_id, elapsed_time_seconds
FROM sys.dm_tran_active_snapshot_database_transactions
ORDER BY elapsed_time_seconds DESC;
```

*After: code that relied on reads waiting*

```sql
-- Delivery slot capacity check inside the booking transaction. Under locking read
-- committed the COUNT waited for other in-flight bookings, then counted them. Under
-- RCSI it reads the last committed rows and misses them: two orders get the last place.
SET XACT_ABORT ON;
BEGIN TRAN;
INSERT INTO dbo.DeliverySlotBookings (SlotId, OrderId) VALUES (@slotId, @orderId);

IF (SELECT COUNT(*)
    FROM dbo.DeliverySlotBookings WITH (READCOMMITTEDLOCK)   -- the old locking read, here only
    WHERE SlotId = @slotId) > @capacity
BEGIN
    ROLLBACK;
    THROW 50010, 'Delivery slot is full.', 1;
END;
COMMIT;
-- Better long term: a per-slot counter with a conditional UPDATE (see the flash-sale case)
```

**Why it works.** With `READ_COMMITTED_SNAPSHOT` on, a read committed statement does not take shared locks on the rows it reads. When it meets a row that a writer has changed and not yet committed, it reads the previous committed version from the version store instead of waiting. The report sees the data as of the moment its statement started, checkout never waits for a report and a report never waits for checkout. Writers still block writers, so hot-row problems like the flash sale remain.

It is also correct, unlike `NOLOCK`: no uncommitted data, and no rows read twice or skipped, because the statement reads one consistent point in time.

PostgreSQL has always worked this way: every read uses an MVCC snapshot, so readers never block writers and writers never block readers. Its version store is the table itself, which is why PostgreSQL needs VACUUM (see the heartbeat bloat case).

**Result.** At the next month-end the same 30 reports ran on the primary and checkout p99 stayed at 160 ms. `LCK_M_S` and `LCK_M_IX` waits in the busiest hour fell from 27.6M ms to under 60,000 ms. The version store peaked at 6.4 GB in tempdb during the longest report (24 minutes), and the revenue report matched the provider's settlement to the penny.

> **Costs, and when not to.** Every update and delete now writes the old row version to the version store, in tempdb, or inside the database as the persistent version store when Accelerated Database Recovery is on. Modified rows also grow by 14 bytes for the version pointer, which caused a burst of page splits in the first days. A long-running statement or a forgotten open transaction keeps every version since it started and can fill tempdb, so alert on version store size and the oldest snapshot transaction. Switching it on needs a moment of exclusive access to the database.
>
> Behaviour changes for code that relied on reads waiting for in-flight changes. A check-then-insert that used to see other sessions' uncommitted rows (after waiting for them) now reads around them, so triggers, capacity checks and read-then-update logic must be reviewed and given `READCOMMITTEDLOCK`, `UPDLOCK` or a conditional update where they need that. Moving the reports to a readable secondary, which reads under snapshot isolation, would also have worked, with slightly stale data. Do not turn on RCSI and walk away from a database full of such logic, and do not use `NOLOCK` for anything whose numbers matter.

> **Lesson:** Under locking read committed, readers and writers queue for each other; RCSI removes the queue with row versions, NOLOCK removes it by returning wrong answers.

### Storage and schema

Keys, page layout and index counts that decide how fast a table can be written.

<a id="case-random-guid-clustered-key"></a>

#### Case 16 · Sequential keys: Random GUID keys that left a 200M-row table a third empty

*C# · EF Core 10 · SQL Server 2022 · PostgreSQL 17*

**The situation.** `dbo.ShipmentEvents` stores carrier tracking events: 200M rows, growing by about 22M a month, with `Id uniqueidentifier` as the clustered primary key. The nightly carrier import ran at 2,900 rows/s in January, when the table had 20M rows; by September it managed 610 rows/s and ran into the morning. The server has 64 GB of memory, and the table alone is 54 GB.

**Before**

*Before: the entity assigns its own random key*

```csharp
public class ShipmentEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();     // (1) random, set by us
    public Guid ShipmentId { get; private set; }
    public string CarrierCode { get; private set; } = "";
    public string EventType { get; private set; } = "";
    public DateTime OccurredAt { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public string? Payload { get; private set; }
    // ...
}

// By convention Id is the primary key, and on SQL Server the clustered index (2)
modelBuilder.Entity<ShipmentEvent>().HasKey(e => e.Id);
```

*Before: the table EF created*

```sql
CREATE TABLE dbo.ShipmentEvents (
    Id          uniqueidentifier NOT NULL,
    ShipmentId  uniqueidentifier NOT NULL,
    CarrierCode varchar(10)      NOT NULL,
    EventType   varchar(30)      NOT NULL,
    OccurredAt  datetime2(3)     NOT NULL,
    ReceivedAt  datetime2(3)     NOT NULL,
    Payload     nvarchar(1000)   NULL,
    CONSTRAINT PK_ShipmentEvents PRIMARY KEY CLUSTERED (Id)       -- (2)
);
CREATE INDEX IX_ShipmentEvents_ShipmentId ON dbo.ShipmentEvents (ShipmentId);
```

**What goes wrong.** Because the constructor sets `Id` (1), EF Core sees a value that is not `Guid.Empty` and inserts it as it is. Each new row lands at a random position in the clustered index (2). The target page is usually full, so SQL Server splits it: it allocates a new page, moves about half the rows and logs all of it. Random inserts settle at pages about 69% full, so nearly a third of the table is empty space, and every page cached in memory carries it.

While the table fitted in memory this cost little. At 54 GB on a 64 GB server, about a third of the clustered index is not in memory at any moment, so many inserts land on a page that must first be read from disk (`PAGEIOLATCH_EX`), and the insert rate fell as the table grew. The nonclustered index also carries the 16-byte key in every row.

**Evidence**

*Fragmentation, buffer pool and waits*

```text
-- sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('dbo.ShipmentEvents'), NULL, NULL, 'SAMPLED')
index                           avg_fragmentation_in_percent  avg_page_space_used_in_percent  page_count
PK_ShipmentEvents (clustered)   99.2                          68.6                            6,942,118
IX_ShipmentEvents_ShipmentId    98.7                          70.3                            1,286,540

-- sys.dm_os_buffer_descriptors, clustered index only
cached pages  cached MB  free_space_in_bytes in those pages
4,610,233     36,017     11,165 MB (31%)

-- Nightly import, 02:00 to 06:40
rows/s 610    Page Splits/sec 28 (about one per 22 rows)
waits: PAGEIOLATCH_EX 61% (avg 4.1 ms), WRITELOG 17%
```

*PostgreSQL check: 2M inserts into a uuid primary key*

```text
SELECT pg_size_pretty(pg_relation_size('t_rand_pkey')), (pgstatindex('t_rand_pkey')).avg_leaf_density;
-- gen_random_uuid()            78 MB   avg_leaf_density 69.82
-- time-ordered v7 values       60 MB   avg_leaf_density 90.04   (90 = B-tree default fillfactor)
```

**After**

*After: let EF Core generate the key*

```csharp
public class ShipmentEvent
{
    public Guid Id { get; private set; }      // left as Guid.Empty: EF Core fills it on Add
    // ...
}

// For a Guid key on SQL Server, EF Core's convention is client-side generation with
// SequentialGuidValueGenerator, whose values increase in SQL Server's uniqueidentifier
// order. It only runs when the property still holds Guid.Empty.

// Not a fix on SQL Server: Guid.CreateVersion7() (.NET 9+) puts its timestamp in the
// first bytes, but SQL Server compares uniqueidentifier starting with the LAST 6 bytes,
// which are random in a v7 value. In SQL Server's order it is as random as NewGuid().

// Database-side alternative, sequential in SQL Server's order. NEWSEQUENTIALID() is
// only allowed in a DEFAULT, and EF reads the value back after the insert.
modelBuilder.Entity<ShipmentEvent>()
    .Property(e => e.Id)
    .HasDefaultValueSql("NEWSEQUENTIALID()");
```

*After: narrow clustered key, GUID as the public id (the rebuild)*

```sql
CREATE TABLE dbo.ShipmentEvents_v2 (
    EventNo     bigint IDENTITY  NOT NULL,
    Id          uniqueidentifier NOT NULL,      -- still the id the API exposes
    ShipmentId  uniqueidentifier NOT NULL,
    CarrierCode varchar(10)      NOT NULL,
    EventType   varchar(30)      NOT NULL,
    OccurredAt  datetime2(3)     NOT NULL,
    ReceivedAt  datetime2(3)     NOT NULL,
    Payload     nvarchar(1000)   NULL,
    CONSTRAINT PK_ShipmentEvents_v2 PRIMARY KEY CLUSTERED (EventNo),  -- appends at the end
    CONSTRAINT UQ_ShipmentEvents_v2_Id UNIQUE NONCLUSTERED (Id)       -- 16 + 8 bytes a row
);
CREATE INDEX IX_ShipmentEvents_v2_ShipmentId ON dbo.ShipmentEvents_v2 (ShipmentId);

-- Backfilled in batches ordered by ReceivedAt, with dual writes, then swapped with sp_rename.
-- EF Core: HasKey(e => e.EventNo); HasIndex(e => e.Id).IsUnique();
```

*PostgreSQL: UUIDv7 works there*

```csharp
// PostgreSQL compares uuid values byte by byte from the first byte, and Npgsql writes
// a Guid in RFC 4122 byte order, the order of its string form. The 48-bit timestamp
// at the front of a version 7 value therefore sends new keys to the right edge of the
// B-tree. (Values created in the same millisecond are in random order among
// themselves, which costs nothing measurable.)
var shipmentEvent = new ShipmentEvent(id: Guid.CreateVersion7(), /* ... */);

// PostgreSQL 18 can generate them in the database:
//     id uuid PRIMARY KEY DEFAULT uuidv7()
// PostgreSQL 17 has no built-in v7 function, so generate them in the application.
```

**Why it works.** An index on an increasing key only inserts on its last page. When that page fills, SQL Server starts a new one without moving rows, so pages end up full and the page being written stays in memory. A random key spreads inserts over every page, which costs splits, log, fragmentation and, once the table outgrows memory, a physical read per insert.

What counts as increasing is the database's comparison, not .NET's. SQL Server compares `uniqueidentifier` in byte groups, starting with the last 6 bytes, the last block of the string form. EF Core's `SequentialGuidValueGenerator` and `NEWSEQUENTIALID()` put the changing part there; `Guid.CreateVersion7()` puts its timestamp at the front, so SQL Server sees it as random. PostgreSQL compares `uuid` bytewise from the front, so on PostgreSQL UUIDv7 is the right choice.

The bigint clustered key goes further: 8 bytes instead of 16 in the clustered index and in every nonclustered index, which all carry the clustered key as their row locator.

**Result.** After the rebuild, the clustered index is 5.0M pages at 99.1% page density, 39 GB instead of 54 GB, and the import runs at 3,300 rows/s with `PAGEIOLATCH_EX` under 5% of waits. The unique index on `Id` is still random-insert, but its rows are about 33 bytes, so at 9.3 GB it stays mostly in memory. Entities in newer services simply stopped assigning `Guid.NewGuid()`.

> **Costs, and when not to.** Rebuilding 200M rows took a weekend of backfill, a period of dual writes and a careful rename; on a small table the same change takes minutes. Sequential keys concentrate inserts on the last page, which at tens of thousands of inserts a second from many threads becomes a latch hotspot (`PAGELATCH_EX`); SQL Server 2019 and later have the `OPTIMIZE_FOR_SEQUENTIAL_KEY` index option for that. Sequential and time-ordered ids also reveal creation order and roughly the time, so a public id that must not leak that should stay random, in a narrow secondary index as here.
>
> `NEWSEQUENTIALID()` can start from a lower range after the server restarts, and EF Core's generator orders values within each application process, so several app instances insert at a few points rather than one. Both are still far better than random. Do not rebuild a table for this when it fits comfortably in memory and its insert rate is low: then the fragmentation costs disk space, not time.

> **Lesson:** A GUID key is only insert-friendly if it increases in the database's own sort order: let EF Core or NEWSEQUENTIALID() generate it on SQL Server, use UUIDv7 on PostgreSQL, and never assign Guid.NewGuid() to a clustered key yourself.

<a id="case-postgres-bloat-hot-updates"></a>

#### Case 17 · HOT updates and fillfactor: Heartbeat updates that grew a 2M-row table to 38 GB

*PostgreSQL 17 · Npgsql*

**The situation.** A job scheduler keeps one row per job run in `job_runs`: 2M live rows, about 40,000 of them running at any moment. Each running job's worker updates `last_seen_at` every 8 seconds, about 5,000 updates a second in total. Two months after launch the table was 38 GB, its indexes another 19 GB, and the scheduler's "find stale workers" query had gone from 15 ms to 2.4 s.

**Before**

*Before: the table, the heartbeat and the stale check*

```sql
CREATE TABLE job_runs (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    job_name     text        NOT NULL,
    worker_id    text        NOT NULL,
    status       text        NOT NULL,          -- queued, running, done, failed
    started_at   timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    progress     int         NOT NULL DEFAULT 0,
    payload      jsonb
);
CREATE INDEX ix_job_runs_status       ON job_runs (status);
CREATE INDEX ix_job_runs_worker       ON job_runs (worker_id);
CREATE INDEX ix_job_runs_last_seen_at ON job_runs (last_seen_at);   -- (1) for the stale check

-- Every 8 s per running job, about 5,000 a second in total
UPDATE job_runs SET last_seen_at = now(), progress = $2 WHERE id = $1;   -- (2)

-- Every 10 s: workers that stopped sending heartbeats
SELECT id, worker_id
FROM job_runs
WHERE status = 'running' AND last_seen_at < now() - interval '1 minute';
```

**What goes wrong.** PostgreSQL never updates a row in place. An update writes a new row version and leaves the old one behind as a dead tuple. If no indexed column changes and the page has room, the update is HOT (heap-only tuple): the new version goes on the same page, no index is touched, and the next access to the page can prune the old version without VACUUM. Here the heartbeat (2) changes `last_seen_at`, which is indexed (1), so not one of the 5,000 updates a second was HOT. Each wrote a new heap tuple wherever free space could be found and a new entry in all four indexes.

Autovacuum fell behind and stayed behind. With the default `autovacuum_vacuum_scale_factor` of 0.2 it started after about 400,000 dead tuples, but each run had to scan the whole heap and every index at the default cost limit, and each run took longer than the last because the table it scanned kept growing. By the second month a run took 2 to 3 hours, while 40M to 50M new dead tuples arrived. New versions could not find free space, so the file grew, and VACUUM only makes space reusable; it never shrinks the file.

**Evidence**

*pg_stat_user_tables and pgstattuple*

```text
SELECT n_live_tup, n_dead_tup, n_tup_upd, n_tup_hot_upd, last_autovacuum, autovacuum_count
FROM pg_stat_user_tables WHERE relname = 'job_runs';
 n_live_tup | n_dead_tup |  n_tup_upd  | n_tup_hot_upd |        last_autovacuum        | autovacuum_count
------------+------------+-------------+---------------+-------------------------------+------------------
    2004118 |   38412906 | 26118440211 |             0 | 2026-09-29 03:41:12.801146+00 |              512

-- pgstattuple reads the whole table: run it off-peak
SELECT * FROM pgstattuple('job_runs');
  table_len  | tuple_count | tuple_len | tuple_percent | dead_tuple_count | dead_tuple_len | dead_tuple_percent | free_space  | free_percent
-------------+-------------+-----------+---------------+------------------+----------------+--------------------+-------------+--------------
 40802189312 |     2004118 | 491008910 |           1.2 |         38412906 |     9411161970 |              23.07 | 30356000000 |         74.4
```

*The stale-worker query*

```text
EXPLAIN (ANALYZE, BUFFERS) SELECT id, worker_id FROM job_runs
WHERE status = 'running' AND last_seen_at < now() - interval '1 minute';

Bitmap Heap Scan on job_runs  (cost=30512.40..2618301.77 rows=812 width=21) (actual time=2391.420..2412.833 rows=37 loops=1)
  Recheck Cond: (status = 'running'::text)
  Filter: (last_seen_at < (now() - '00:01:00'::interval))
  Rows Removed by Filter: 39963
  Heap Blocks: exact=301245
  Buffers: shared hit=118220 read=190311
  ->  Bitmap Index Scan on ix_job_runs_status  (cost=0.00..30512.20 rows=1630188 width=0) (actual time=402.118..402.118 rows=1630041 loops=1)
Execution Time: 2413.106 ms
-- 1.63M index entries for 40,000 running rows: the rest point at dead versions
```

**After**

*After: no index on the heartbeat column, room on every page*

```sql
-- 1. The stale check reads the 40,000 running rows through the status index instead
DROP INDEX CONCURRENTLY ix_job_runs_last_seen_at;

-- 2. Leave 30% of each page free so the new version fits next to the old one
ALTER TABLE job_runs SET (fillfactor = 70);

-- 3. Vacuum this table early and with a bigger I/O budget
ALTER TABLE job_runs SET (
    autovacuum_vacuum_scale_factor = 0.01,   -- 50 + 1% of 2M: ~20,000 dead tuples
    autovacuum_vacuum_cost_limit   = 2000    -- ten times the default of 200
);

-- 4. Verify: take two samples a few minutes apart and compare the deltas
SELECT n_tup_upd, n_tup_hot_upd, n_dead_tup, last_autovacuum
FROM pg_stat_user_tables
WHERE relname = 'job_runs';

-- The other classic cause: something holding back the xmin horizon, so VACUUM runs
-- but cannot remove anything newer than the oldest snapshot
SELECT pid, state, xact_start, age(backend_xmin) AS xmin_age
FROM pg_stat_activity
WHERE backend_xmin IS NOT NULL
ORDER BY age(backend_xmin) DESC
LIMIT 5;
SELECT slot_name, active, age(xmin) AS xmin_age FROM pg_replication_slots;
```

*After: rewrite the bloated table online*

```bash
# VACUUM FULL would also rewrite it, but holds an ACCESS EXCLUSIVE lock for the whole
# rewrite: no heartbeats and no reads. pg_repack copies the live rows into a new table
# (with the table's fillfactor), replays the changes made meanwhile, and takes the
# exclusive lock only briefly at the start and to swap the files at the end.
psql -d scheduler -c "CREATE EXTENSION IF NOT EXISTS pg_repack;"
pg_repack --dbname=scheduler --table=public.job_runs --no-order --jobs=2
```

*Verification, 10 minutes apart*

```text
-- pg_stat_user_tables for job_runs, a few hours after the change
            n_tup_upd   n_tup_hot_upd   n_dead_tup
10:00  26,231,872,040     104,210,577       41,206
10:10  26,234,872,412     107,156,723       53,818
delta       3,000,372       2,946,146                (98.2% HOT, about 5,000 updates/s)
```

**Why it works.** With the index on `last_seen_at` gone, the heartbeat changes only unindexed columns, and with 30% free on every page the new version fits next to the old one. That is a HOT update: one heap write, no index writes, and the old version is pruned the next time the page is accessed, with no VACUUM involved. `n_tup_hot_upd` against `n_tup_upd` shows it directly.

The stale-worker check still works without its index: it reads the 40,000 `running` rows through the status index and filters on `last_seen_at`. The per-table autovacuum settings start VACUUM after about 20,000 dead tuples with ten times the I/O budget, so the occasional non-HOT update is cleaned up quickly. The pg_repack rewrite gives back the 37 GB that VACUUM can only mark as reusable.

The other classic cause looks the same from outside: VACUUM runs but removes almost nothing, because a long-running or idle-in-transaction session, an abandoned replication slot, or `hot_standby_feedback` from a replica running a long query holds back the xmin horizon. Check that before tuning anything.

**Result.** After the change and one pg_repack run (11 minutes, with the exclusive lock held for under a second at each end), the table is 690 MB and its indexes 92 MB. 98.2% of updates are HOT, `n_dead_tup` stays under 60,000, and the stale-worker query takes 9 ms. The table has stayed the same size for three months.

> **Costs, and when not to.** `fillfactor = 70` makes the table about 40% larger than a packed one, and every sequential scan reads that much more; it pays only on tables whose rows are updated often. Dropping an index costs whichever query used it: the stale check went from an index range scan to filtering 40,000 rows, which is fine at this size and would not be with 4M running jobs. pg_repack needs free disk space for a copy of the live table, the extension installed in the database, a primary key or unique index, and brief exclusive locks that wait behind long transactions.
>
> Aggressive per-table autovacuum settings cost I/O; give them to the few hot tables, not to the whole cluster. If you do need an index on the heartbeat column, move the heartbeat into its own narrow table (`job_run_id`, `last_seen_at`) so that only that small table churns.

> **Lesson:** In PostgreSQL every update writes a new row version, so keep frequently updated columns out of indexes and leave free space on the page so the updates are HOT, then check `n_tup_hot_upd`.

<a id="case-too-many-indexes"></a>

#### Case 18 · Index consolidation: 23 indexes on Orders and a 35 ms insert

*SQL Server 2022 · PostgreSQL 17*

**The situation.** `dbo.Orders` (60M rows) had 23 nonclustered indexes, most of them added over three years from missing index suggestions in execution plans and DMVs. A single order insert averaged 35 ms, and the nightly import of 1.2M marketplace orders took 2 h 50 min. Nobody knew which indexes were still used, so nobody dared to drop any.

**Before**

*Before: a selection of the 23*

```sql
CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId);                     -- (1)
CREATE INDEX IX_Orders_CustomerId_PlacedAt ON dbo.Orders (CustomerId, PlacedAt);  -- (1)
CREATE INDEX IX_Orders_missing_812 ON dbo.Orders (CustomerId, PlacedAt)
    INCLUDE (Status, Total);                                                      -- (2)
CREATE INDEX IX_Orders_missing_1407 ON dbo.Orders (CustomerId, PlacedAt)
    INCLUDE (Status, Total, CurrencyCode);                                        -- (2)
CREATE INDEX IX_Orders_Status ON dbo.Orders (Status);                             -- (3)
CREATE INDEX IX_Orders_missing_95 ON dbo.Orders (Status) INCLUDE (PlacedAt);      -- (3)
CREATE INDEX IX_Orders_ShippingPostcode ON dbo.Orders (ShippingPostcode);         -- (4)
CREATE INDEX IX_Orders_GiftWrapCode ON dbo.Orders (GiftWrapCode);                 -- (4)
CREATE INDEX IX_Orders_PromoCode ON dbo.Orders (PromoCode) INCLUDE (Total);       -- (5)
CREATE UNIQUE INDEX UX_Orders_ExternalRef ON dbo.Orders (Channel, ExternalRef);   -- (6)
CREATE INDEX IX_Orders_PlacedAt_Country ON dbo.Orders (PlacedAt, Country)
    INCLUDE (Total, CurrencyCode);                                                -- (7)
-- ... 12 more
```

**What goes wrong.** Every insert writes the clustered index and all 23 nonclustered indexes, and every update of an indexed column writes each index that contains it. Most of these keys are effectively random (customer, postcode, promo code), so one insert touches about 24 pages in different parts of the files, and with the table larger than memory several of them are physical reads.

Many were redundant. `IX_Orders_CustomerId` (1) is a left prefix of `IX_Orders_CustomerId_PlacedAt`, which has the same keys as the two suggestions (2) that differ by one included column: four indexes for one access path. The `Status` pair (3) is the same story. Others (4) served a feature removed two years ago. Missing index suggestions are made per query: the optimiser never says "widen the index you already have".

**Evidence**

*Index usage on the primary since the last restart*

```text
-- sys.dm_db_index_usage_stats; sqlserver_start_time 2026-07-12 (82 days, July to
-- September month-ends, but not the quarterly VAT run)
index                           user_seeks   user_scans  user_updates   size_mb
IX_Orders_CustomerId_PlacedAt   88,120,412            0   118,402,330     2,210
IX_Orders_missing_1407          41,330,218            3   118,402,330     3,020
IX_Orders_missing_812                   12            0   118,402,330     2,890
IX_Orders_CustomerId                     0            0   118,402,330     1,410
IX_Orders_Status                 2,410,331            0   164,881,204       980
IX_Orders_missing_95                     0            0   164,881,204     1,460
IX_Orders_ShippingPostcode               0            0   118,402,330     1,690
IX_Orders_GiftWrapCode                   0            0   118,402,330     1,120
IX_Orders_PromoCode                      0            0   118,402,330     1,250
UX_Orders_ExternalRef                    0            0   118,402,330     2,050
IX_Orders_PlacedAt_Country               0            0   118,402,330     1,820

-- The same DMV on the readable secondary (marketing dashboard)
IX_Orders_PromoCode              3,112,904            0             0

-- Query Store, INSERT INTO dbo.Orders, last 7 days
avg_duration 35.2 ms   avg_logical_io_reads 131   avg_physical_io_reads 7.6
avg_log_bytes_used 11,840
-- Import waits: PAGEIOLATCH_EX 58%, WRITELOG 19%
```

**After**

*After: reads against writes per index (SQL Server)*

```sql
-- Run on every replica: reads on a readable secondary are only counted there
SELECT i.name, i.is_unique, i.is_primary_key,
       ISNULL(u.user_seeks + u.user_scans + u.user_lookups, 0) AS reads,
       ISNULL(u.user_updates, 0)                                AS writes,
       u.last_user_seek, u.last_user_scan,
       (SELECT sqlserver_start_time FROM sys.dm_os_sys_info)   AS counting_since
FROM sys.indexes AS i
LEFT JOIN sys.dm_db_index_usage_stats AS u
       ON u.object_id = i.object_id
      AND u.index_id = i.index_id
      AND u.database_id = DB_ID()
WHERE i.object_id = OBJECT_ID('dbo.Orders')
  AND i.index_id > 1                       -- nonclustered only
ORDER BY reads, writes DESC;
```

*After: find prefixes, widen one, disable the rest*

```sql
-- a's keys are a left prefix of b's keys (then compare their INCLUDE lists by hand)
WITH k AS (
    SELECT i.index_id, i.name, i.is_unique,
           STRING_AGG(CASE WHEN ic.is_included_column = 0 THEN c.name END, ',')
               WITHIN GROUP (ORDER BY ic.key_ordinal) AS keys
    FROM sys.indexes AS i
    JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID('dbo.Orders') AND i.index_id > 1
    GROUP BY i.index_id, i.name, i.is_unique
)
SELECT a.name AS redundant, b.name AS covered_by, a.keys, b.keys AS wider_keys
FROM k AS a
JOIN k AS b ON a.index_id <> b.index_id
WHERE a.is_unique = 0
  AND b.keys + ',' LIKE a.keys + ',%';

-- One index for the customer access path, with the union of the INCLUDE lists
CREATE INDEX IX_Orders_CustomerId_PlacedAt ON dbo.Orders (CustomerId, PlacedAt)
    INCLUDE (Status, Total, CurrencyCode)
    WITH (DROP_EXISTING = ON, ONLINE = ON);          -- ONLINE needs Enterprise edition

-- Disable rather than drop: the definition stays, the data and its write cost go
ALTER INDEX IX_Orders_missing_1407 ON dbo.Orders DISABLE;
ALTER INDEX IX_Orders_missing_812  ON dbo.Orders DISABLE;
ALTER INDEX IX_Orders_CustomerId   ON dbo.Orders DISABLE;
-- Regression? ALTER INDEX IX_Orders_CustomerId ON dbo.Orders REBUILD;
-- A full business cycle later with no regressions: DROP INDEX.
```

*After: PostgreSQL*

```sql
SELECT s.indexrelname, s.idx_scan, s.last_idx_scan,       -- last_idx_scan: PostgreSQL 16+
       i.indisunique, i.indisprimary,
       pg_size_pretty(pg_relation_size(s.indexrelid)) AS size
FROM pg_stat_user_indexes AS s
JOIN pg_index AS i ON i.indexrelid = s.indexrelid
WHERE s.relname = 'orders'
ORDER BY s.idx_scan, pg_relation_size(s.indexrelid) DESC;
-- Counts run from pg_stat_database.stats_reset, are lost after a crash, and are
-- kept per server: a replica counts its own scans.

CREATE INDEX CONCURRENTLY ix_orders_customer_placed_v2
    ON orders (customer_id, placed_at) INCLUDE (status, total, currency_code);

-- No DISABLE in PostgreSQL: keep the CREATE statements in the migration for the way back
DROP INDEX CONCURRENTLY ix_orders_customer_placed;
DROP INDEX CONCURRENTLY ix_orders_customer_id;
```

**Why it works.** An index pays on every write and earns only on the reads that use it. The usage DMV and `pg_stat_user_indexes` give both numbers per index, so the candidates are indexes with many writes and no reads, and indexes whose keys are a left prefix of another's: any seek on `(CustomerId)` can use `(CustomerId, PlacedAt)` instead. Near-identical suggestions collapse into one index with the union of their INCLUDE columns, so every query that used one of them keeps a covering index.

Disabling keeps the index definition in the catalogue and throws away its data, so writes stop paying for it at once, and a regression is fixed with one `REBUILD` instead of a hunt for the original script. Only disable nonclustered indexes: a disabled clustered index makes the table unreadable, and disabling a unique index that a foreign key references disables that foreign key. PostgreSQL has no disable, so the migration that drops an index keeps its `CREATE INDEX CONCURRENTLY` statement as the way back.

**Result.** Eleven indexes went: seven unused, three folded into the widened customer index and the status index, and one exact duplicate. The insert now averages 11 ms with 64 logical reads and 2.1 physical reads, log per insert fell from 11.8 KB to 5.6 KB, the import takes 58 minutes, and 19 GB of disk and buffer pool came back. Nothing regressed through the October month-end or the Q3 VAT report.

> **Costs, and when not to.** `sys.dm_db_index_usage_stats` starts from zero at every restart and failover, and reads on a readable secondary are counted only on that secondary. An index used once a quarter looks unused for most of the quarter: the VAT report's index (7) showed no reads since the July restart and would have been dropped a week before the Q3 run, and the promo index (5) is read only on the secondary. Collect over a full business cycle, including month-end, quarter-end and year-end jobs, and ask the reporting teams before dropping anything.
>
> Some indexes stay whatever the numbers say. A unique index enforces a rule, not a query: `UX_Orders_ExternalRef` (6) stops the import creating duplicate marketplace orders. An index that supports a foreign key keeps deletes of parent rows from scanning this table. A widened index is bigger, so queries that used the narrow one read a little more. Do not prune indexes by usage counts alone on a database whose workload you do not know.

> **Lesson:** Every index is a cost on every write: measure reads against writes over a whole business cycle, fold duplicates into one index, and disable before you drop.
