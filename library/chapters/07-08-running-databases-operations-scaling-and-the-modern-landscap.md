---
id: "7-8"
number: "7.8"
title: "Running databases: operations, scaling and the modern landscape"
part: 7
part_title: "Storing data"
words: 19407
minutes: 84
---

# 7.8 Running databases: operations, scaling and the modern landscape

> **Quick look.** Running databases in production: backups, replication, scaling and today's options.
>
> - Replicas are not backups: a replica applies a bad DELETE in milliseconds. Test point-in-time restores.
> - Scale in order: query and index fixes, connection pooling, caching, read replicas, partitioning, sharding (most expensive).
> - Replica lag breaks read-your-writes: read from the primary briefly after a write.
> - Connection count, not query cost, is what usually brings a database down under load.
> - Postgres needs vacuum; transaction ID wraparound causes emergency outages.
> - Separate analytics from OLTP; partition tables that grow forever by time.
> - Default to PostgreSQL unless there is a specific reason; consider distributed SQL before hand-rolled sharding.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Restoring to one minute before a bad migration

A migration at 14:32 deleted the wrong rows. Last night's full backup is 14 hours old.

*Point-in-time restore*

```bash
# Azure SQL: restore a copy to just before the incident, then copy the rows back
az sql db restore --resource-group rg-prod --server sql-prod --name bookings \
  --dest-name bookings-restore-1431 --time "2026-10-01T14:31:00Z"
# PostgreSQL: recovery_target_time = '2026-10-01 14:31:00+00' with WAL archiving enabled
```

> **Lesson:** Replicas copy mistakes instantly. Point-in-time recovery, practised before you need it, is what undoes them.

#### Example 2: Connection pool exhausted by scale-out

During a sale, the team scales the API from 5 to 30 pods. The database starts refusing connections.

**What goes wrong.** Each pod's pool allows 100 connections: 30 × 100 = 3,000, far above the database's limit.

*Size pools for the whole fleet*

```text
database max connections:          800 (keep ~20% headroom -> 640 usable)
pods at peak:                        30
Max Pool Size per pod:   640 / 30 ≈ 20
connection string: ...;Max Pool Size=20;Timeout=15
or put a pooler (PgBouncer, RDS Proxy) in front of the database
```

> **Lesson:** Connection count, not query cost, often brings databases down. Size pools for the total number of instances.

### SQL

<p class="src">From <em>.NET Backend Deep Dive</em> · SQL</p>

<a id="dn-20"></a>

#### Efficient pagination

```sql
-- Slow: OFFSET must read and discard 100,000 rows
SELECT * FROM Orders ORDER BY Id OFFSET 100000 ROWS FETCH NEXT 50 ROWS ONLY;

-- Fast: keyset / seek pagination
SELECT TOP 50 * FROM Orders WHERE Id > @lastSeenId ORDER BY Id;
```

---

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a105"></a>

#### SQL Server and PostgreSQL, from a .NET perspective

|                   | SQL Server                                 | PostgreSQL                                               |
|-------------------|--------------------------------------------|----------------------------------------------------------|
| Driver            | `Microsoft.Data.SqlClient`                 | `Npgsql`                                                 |
| EF provider       | `UseSqlServer`                             | `UseNpgsql`                                              |
| Concurrency token | `rowversion`, first-class                  | `xmin` system column                                     |
| JSON              | `JSON_VALUE`, native JSON type in 2025     | `jsonb` with GIN indexes — excellent                     |
| Case sensitivity  | Collation-driven; usually case-insensitive | Case-sensitive by default — a classic migration surprise |
| Isolation default | Read Committed with locks (RCSI optional)  | Read Committed with MVCC — readers never block           |
| Arrays / enums    | No                                         | Native, and Npgsql maps them to C#                       |
| Full text         | Built in                                   | Built in, plus `pg_trgm` for fuzzy matching              |
| Cost              | Licensed per core                          | Free; managed offerings everywhere                       |

The practical differences that bite when porting: identifier case folding (unquoted names lowercase in Postgres), `GETDATE()` versus `now()`, `TOP` versus `LIMIT`, and `datetime2` versus `timestamptz`. If you use EF Core with no raw SQL, most of it is handled; every raw query is a porting task.

> **\[Note\]**
>
> **Npgsql and DateTime.** Npgsql 6+ maps `timestamptz` to a UTC `DateTime` and throws on a `DateTime` with `Kind.Local` or `Unspecified`. Use `DateTimeOffset` or always-UTC `DateTime` throughout your domain and this stops being a problem.

###### The differences that reach your C# code

| Concern                 | SQL Server                                              | PostgreSQL                                                                     |
|-------------------------|---------------------------------------------------------|--------------------------------------------------------------------------------|
| Provider                | `Microsoft.Data.SqlClient`                              | `Npgsql`                                                                       |
| Identifier case         | Case-insensitive by default                             | **Lower-cased unless quoted** — EF Core quotes, so `OrderId` stays `"OrderId"` |
| String comparison       | Collation-driven; often case-**insensitive**            | Case-**sensitive** by default                                                  |
| Concurrency token       | `rowversion` / `[Timestamp]`                            | `xmin` via `UseXminAsConcurrencyToken()`                                       |
| `Guid` key              | `uniqueidentifier` — do **not** cluster on a random one | `uuid`; heap table, so milder                                                  |
| JSON                    | `nvarchar` + `JSON_VALUE`; native type in 2025          | **`jsonb`** — indexable, far stronger                                          |
| Bulk insert             | `SqlBulkCopy`                                           | `COPY` via `NpgsqlBinaryImporter`                                              |
| Arrays                  | None — use a table                                      | Native, and mapped by Npgsql to `T[]`                                          |
| Case-insensitive column | Collation on the column                                 | `citext`, or an index on `lower(col)`                                          |
| Migrations              | Both fine                                               | Both fine — but the generated DDL differs                                      |

``` csharp
// the case-sensitivity difference is the one that bites during a port.
// this works on SQL Server (CI collation) and returns nothing on PostgreSQL:
db.Users.Where(u => u.Email == "Nam@Example.com");

// portable fix - and it needs an index to stay fast
db.Users.Where(u => u.Email.ToLower() == email.ToLower());
// PostgreSQL:  CREATE INDEX ix_users_email ON users (lower(email));
// or store a normalised column and compare that - which is what ASP.NET
// Core Identity does with NormalizedEmail, for exactly this reason.
```

``` csharp
// Npgsql maps things SQL Server cannot express - worth using when you can
public class Article
{
    public string[] Tags { get; set; } = [];            // text[] - native array
    public NpgsqlRange<DateTime> Period { get; set; }   // tstzrange
    public Dictionary<string, string> Meta { get; set; } = new();   // hstore/jsonb
}

db.Articles.Where(a => a.Tags.Contains("postgres"));    // translates to @>
// with:  CREATE INDEX ON articles USING gin (tags);

// and EF Core 8+ maps owned types into a single jsonb column
modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress, b => b.ToJson());
// one column, queryable, no join, no separate table.
```

The practical guidance for a new system: **PostgreSQL unless there is a specific reason otherwise** — no licence cost, a stronger JSON story, arrays and ranges, the extension ecosystem, and it runs everywhere. Choose SQL Server when you are already in a Microsoft estate with licences and expertise, when you need columnstore for hybrid analytics, or when Query Store's plan management is genuinely valuable to you. Both are excellent; the decision is usually organisational rather than technical.

<a id="be-a107"></a>

#### JSON columns and hybrid modelling

Modern relational engines store and index JSON well, which means you no longer have to choose between relational and document modelling. Keep the fields you query, join and constrain as real columns; put the variable, sparse or client-defined parts in JSON.

``` csharp
// EF Core owned entity mapped to a JSON column
b.Entity<Order>().OwnsOne(o => o.Metadata, m => m.ToJson());

public class OrderMetadata
{
    public string? Channel { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
}

// queryable, translated into JSON path expressions by the provider
var fromWeb = await db.Orders
    .Where(o => o.Metadata!.Channel == "web")
    .ToListAsync(ct);
```

**Put it in a column when**

- You filter, sort or join on it
- It needs a constraint or foreign key
- Every row has it
- Reporting tools must see it

**Put it in JSON when**

- It varies per tenant or per customer
- It is read as a whole blob
- The schema changes without a deploy
- It is an audit snapshot of something else

PostgreSQL `jsonb` with a GIN index is genuinely fast for containment queries and is often a better answer than adding a document database to your architecture. Consider the operational cost of a second store before you take on one.

###### The decision rule, and what a JSON column costs you

``` csharp
// the hybrid shape: columns for what you query, JSON for what varies
public class Order
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }         // filtered, joined  -> COLUMN
    public string Status { get; set; } = "";   // filtered, indexed -> COLUMN
    public decimal Total { get; set; }         // summed, compared  -> COLUMN
    public OrderMetadata Metadata { get; set; } = new();   // varies -> JSON
}

// EF Core 8+ maps an owned type to ONE json column
modelBuilder.Entity<Order>().OwnsOne(o => o.Metadata, b =>
{
    b.ToJson();
    b.OwnsMany(m => m.Attachments);      // nested collections work too
});

// and it is queryable - this translates, it does not load everything
var web = await db.Orders
    .Where(o => o.Metadata.Channel == "web")
    .ToListAsync(ct);
```

| Put it in a column when                     | Put it in JSON when                                |
|---------------------------------------------|----------------------------------------------------|
| You filter, sort, join or aggregate on it   | It is read as part of the whole record             |
| It needs a constraint or a foreign key      | The shape varies per tenant, category or version   |
| Every row has it                            | It is sparse — present on a minority of rows       |
| Reporting tools must see it                 | It is an immutable snapshot of an external payload |
| The optimiser must estimate its selectivity | You genuinely do not know the fields in advance    |

``` text
  THE COST PEOPLE UNDERESTIMATE: cardinality estimation

  the planner has NO per-key statistics inside a JSON document.
  a predicate on metadata->>'status' falls back to a hardcoded guess,
  often 0.5%.

    reality: 60% of rows match
    estimate: 0.5%
    -> the planner picks a nested loop expecting 5,000 rows
    -> it processes 6,000,000
    -> exactly the catastrophic plan from the optimisation part

  mitigation: an EXPRESSION INDEX, because PostgreSQL collects
  statistics on expression indexes.

    CREATE INDEX ix_orders_channel ON orders ((metadata->>'channel'));
    ANALYZE orders;
```

| Other cost             | Detail                                                                          |
|------------------------|---------------------------------------------------------------------------------|
| Whole-document rewrite | Updating one key rewrites the entire document and the whole row version         |
| TOAST / off-row        | Documents over ~2 KB move off-row, adding a read whenever the column is touched |
| No constraints         | Nothing prevents `{"qty": "banana"}` without a `CHECK`                          |
| Storage                | Keys repeat in every row                                                        |
| Migration              | There is no `ALTER` for a JSON shape — you write a data migration               |

> **\[Note\]**
>
> **This is usually the alternative to a second database.** A hybrid table — real columns for the queryable core, one `jsonb` column for the variable remainder — is very often a better answer than adding a document store. You keep transactions, joins, constraints and one operational surface, and you get the schema flexibility that motivated the document store in the first place.

<a id="be-a108"></a>

#### Scaling reads: replicas, partitioning, sharding

``` text
  vertical            bigger machine                easiest, has a ceiling, costs a lot at the top
  read replicas       one writer, N readers         great for read-heavy; replicas lag
  partitioning        one database, many partitions transparent to the app; prunes scans
  sharding            many databases by key          near-unlimited, and by far the most complex
```

``` csharp
// route reads to a replica with two contexts over the same model
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(writerCs));
builder.Services.AddDbContext<ReadOnlyDb>(o =>
    o.UseNpgsql(readerCs).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

> **\[Trap\]**
>
> **Common trap.** Replica lag makes read-after-write inconsistent: a user saves a form, is redirected, and sees the old value. Either read from the primary for a short window after a write (sticky reads), or return the saved state from the write response rather than re-fetching.

##### Partitioning

Splitting one logical table into physical partitions by date or tenant lets the engine skip everything irrelevant, and makes deleting old data an instant partition drop instead of a multi-hour `DELETE`. It is the highest value-to-effort scaling technique for time-series-shaped data.

##### Sharding

Once you shard, you lose cross-shard joins, cross-shard transactions and global auto-increment IDs, and you gain a routing layer and a rebalancing problem. Exhaust replicas, partitioning and caching first, and if you do shard, pick a key that is present on nearly every query — usually tenant ID.

###### Worked example

Partitioning a large audit table, and why it changes the economics of data retention.

``` sql
-- PostgreSQL declarative partitioning by month
CREATE TABLE audit_events (
    id          bigserial,
    occurred_at timestamptz NOT NULL,
    tenant_id   uuid        NOT NULL,
    payload     jsonb       NOT NULL
) PARTITION BY RANGE (occurred_at);

CREATE TABLE audit_events_2026_07 PARTITION OF audit_events
    FOR VALUES FROM ('2026-07-01') TO ('2026-08-01');
CREATE INDEX ON audit_events_2026_07 (tenant_id, occurred_at DESC);
```

``` text
  query: WHERE occurred_at >= '2026-07-01' AND tenant_id = @t

    unpartitioned:  index scan over 900 million rows' index
    partitioned:    PARTITION PRUNING - the planner touches ONE partition of 25 million

  retention: delete everything older than 24 months

    unpartitioned:  DELETE FROM audit_events WHERE occurred_at < ...
                    -> hours, massive WAL, table bloat, vacuum pressure, lock contention
    partitioned:    DROP TABLE audit_events_2024_07;
                    -> milliseconds, no WAL to speak of, no bloat
```

The retention difference is the one that surprises people. On a large table, deleting old rows is often the single most disruptive routine operation a system performs — and partitioning turns it into a metadata change. If you have any table that grows forever and has a retention policy, partitioning it is usually the highest-value database change available to you.

``` csharp
// EF Core maps to the parent table and is unaware of partitions - which is the point
b.Entity<AuditEvent>().ToTable("audit_events");

// just make sure the partition key is in the WHERE clause, or pruning cannot happen
var events = await db.AuditEvents
    .Where(e => e.TenantId == tenantId
             && e.OccurredAt >= from && e.OccurredAt < to)   // <- required for pruning
    .OrderByDescending(e => e.OccurredAt).Take(100)
    .ToListAsync(ct);
```

###### Read replicas: the consistency question, answered concretely

``` csharp
// route by intent, and make the intent explicit in the type system
public interface IReadDb  { IQueryable<T> Query<T>() where T : class; }
public interface IWriteDb { /* SaveChanges etc. */ }

// then the read-after-write problem, handled honestly:
public async Task<IResult> Update(UpdateOrder cmd, CancellationToken ct)
{
    var updated = await _write.ApplyAsync(cmd, ct);

    // do NOT redirect to a GET that reads from a replica - it may show stale data.
    // return the new state directly from the write path instead.
    return Results.Ok(OrderResponse.From(updated));
}
```

| Technique                           | How it works                                                     | Cost                                                 |
|-------------------------------------|------------------------------------------------------------------|------------------------------------------------------|
| Return the written state            | The write path already has it; never re-read                     | Free — do this by default                            |
| Sticky reads after write            | Route this user to the primary for N seconds                     | Needs session state or a cookie                      |
| Wait for LSN / replication position | Write returns a position; read waits for the replica to reach it | Adds latency; supported by Postgres and some proxies |
| Accept it in the UI                 | Show ‘saved’ without re-fetching                                 | Free, and often the right product answer             |

Replica lag is usually milliseconds and occasionally seconds — during a large batch job, a long transaction on the primary, or a network hiccup, it can be minutes. Any design that assumes lag is small will fail during exactly the incident you least want it to.

<a id="be-a109"></a>

#### Connection resiliency, timeouts and pool sizing

Cloud databases fail transiently: failovers, throttling, maintenance windows. The app must expect it.

``` csharp
o.UseSqlServer(cs, sql =>
{
    sql.EnableRetryOnFailure(maxRetryCount: 5,
                             maxRetryDelay: TimeSpan.FromSeconds(10),
                             errorNumbersToAdd: null);
    sql.CommandTimeout(30);
});
```

| Setting            | Where                           | Guidance                                                  |
|--------------------|---------------------------------|-----------------------------------------------------------|
| `Max Pool Size`    | Connection string, default 100  | Total across replicas must be under the server's limit    |
| `Connect Timeout`  | Connection string, default 15 s | Lower it: a slow connect should fail fast                 |
| `CommandTimeout`   | EF / DbCommand, default 30 s    | Set per query; reports get their own longer value         |
| `Min Pool Size`    | Connection string, default 0    | Raise slightly to avoid cold-start connect storms         |
| `Application Name` | Connection string               | Set it — it is how the DBA identifies who is causing load |

> **\[Trap\]**
>
> **Common trap.** Execution strategies and manual transactions conflict: with retries enabled, EF refuses to retry a user-initiated transaction because it cannot know it is safe. Wrap the whole unit of work in `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` so the entire block is retried atomically.

Pool sizing arithmetic worth doing once: 20 pods times a max pool of 100 is 2,000 potential connections, and PostgreSQL defaults to 100. Either lower the per-pod max, or put PgBouncer in front — this is one of the most common ways a working service falls over the moment it is scaled out.

###### Sizing from arrival rate, and the cascade when you get it wrong

``` text
  SIZING, from Little's Law

    concurrent connections needed = arrival rate x average hold time

    400 requests/sec x 20 ms of database time = 8 concurrent
    -> a pool of 20-40 serves that comfortably, with headroom.
    -> a pool of 200 serves it WORSE: more context switching on the
       database, more memory per connection, and no more throughput.

  the counter-intuitive part: a database with 16 cores cannot usefully
  execute more than a few dozen queries at once. Extra connections are
  a queue with overhead, not extra capacity.

  so size the pool from CONCURRENCY, not from expected users. And when
  it is exhausted, the fix is usually FASTER QUERIES, not a bigger pool.
```

``` csharp
// the settings that turn a cascade into a degradation
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs, npg =>
{
    npg.EnableRetryOnFailure(
        maxRetryCount: 3,
        maxRetryDelay: TimeSpan.FromSeconds(5),
        errorCodesToAdd: null);
    npg.CommandTimeout(10);                     // per query
}));

// connection string
"...;Maximum Pool Size=25;Timeout=5;Connection Idle Lifetime=300;Connection Lifetime=1800"
//                        ^^^^^^^^^ FAIL FAST when the pool is empty.
//                        Waiting 30s just moves the failure later and
//                        holds a request thread while it waits.

// and a backstop at the database that the application cannot bypass
// ALTER ROLE app_service SET statement_timeout = '15s';
// ALTER ROLE app_service SET idle_in_transaction_session_timeout = '30s';
```

> **\[Trap\]**
>
> **Common trap.** `EnableRetryOnFailure` and user-initiated transactions do not mix. The execution strategy cannot retry a block it did not start, so EF Core throws *“The configured execution strategy does not support user-initiated transactions”*. You must wrap the whole unit of work in the strategy so it can retry the *entire* transaction — and that means the work inside must be idempotent, because it may genuinely run twice.

``` csharp
// the correct form when you need both retries and an explicit transaction
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Orders.AddAsync(order, ct);
    await db.SaveChangesAsync(ct);
    await outbox.PublishAsync(new OrderPlaced(order.Id), ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
// the ENTIRE lambda is retried on a transient failure - so everything in it
// must tolerate running more than once.
```

``` text
  THE CASCADE - why this looks like a database problem when it is not

   1  one slow query starts holding a connection for 5s instead of 20ms
   2  arrival rate is unchanged, so connections leave faster than they return
   3  the pool empties; new requests WAIT
   4  request threads block on pool acquisition
   5  the web server's thread pool fills with waiting requests
   6  health checks queue behind them and TIME OUT
   7  the load balancer marks the instance unhealthy and removes it
   8  its traffic moves to the remaining instances, already near the limit
   9  they fail the same way -> full outage

  the database was never the bottleneck. ONE slow query plus an
  UNBOUNDED WAIT produced a cascading failure. The bound is the fix.
```

<a id="be-a110"></a>

#### The database as an integration point

Two services reading and writing the same tables is the most tempting and most damaging shortcut in distributed .NET systems. It looks like reuse; it is a hidden, undocumented, untestable API with no version and no owner.

| What you lose                  | Why                                                                   |
|--------------------------------|-----------------------------------------------------------------------|
| Independent deployment         | A schema change must be coordinated across every reader               |
| Encapsulation                  | Business rules in service A are bypassed by service B's direct writes |
| Performance isolation          | One service's report locks the other's transactional path             |
| Any hope of a clean extraction | Nobody knows which columns are used by whom                           |

##### Legitimate exceptions

- **Read-only replicas for reporting and BI.** A separate physical replica, with a documented, versioned view layer — not the live tables.
- **Change Data Capture / logical replication** feeding events into a broker. This is a well-defined outbound integration, not shared ownership.
- **A deliberate transition period** during a strangler-fig migration, with a written end date.

###### The shared database, and how to get out of one

``` text
  the pattern: several applications read and write the SAME tables.

  why it happens: it is the fastest possible integration. No API, no
  contract, no versioning. Someone needs the data, and it is right there.

  what it costs
    the SCHEMA becomes the contract, but nobody wrote it down
    no application can change a column without breaking others
    no application knows who reads what, so nothing can be removed
    business rules are duplicated in every writer, and they disagree
    a bad query in ANY application degrades ALL of them
    you cannot upgrade or migrate one consumer independently

  and the specific failure that hurts most: an invariant enforced in
  application A's code is silently violated by application B, which
  wrote directly. The data is now inconsistent and A's assumptions
  are wrong.
```

| Escape route            | How                                                      | Fits                                                      |
|-------------------------|----------------------------------------------------------|-----------------------------------------------------------|
| **Views as a contract** | Consumers read views, never base tables                  | First step. Cheap, and it decouples the physical schema   |
| **Read-only replica**   | Reporting and analytics off the primary                  | Removes the load coupling, not the schema coupling        |
| **CDC to a read model** | Stream changes into a store the consumer owns            | Consumers stop touching your database at all              |
| **An API in front**     | One owner, everyone else calls it                        | The destination. Rules are enforced in one place          |
| **Outbox events**       | Publish business events; consumers build their own state | Best long-term; the contract is the event, not the schema |

``` sql
-- step one, and it costs an afternoon: give every consumer a VIEW,
-- and revoke direct table access.
CREATE VIEW reporting.v_orders AS
SELECT id, tenant_id, placed_at, status, total
FROM   app.orders
WHERE  deleted_at IS NULL;               -- the soft-delete rule, enforced once

REVOKE ALL ON app.orders FROM reporting_role;
GRANT SELECT ON reporting.v_orders TO reporting_role;

-- now you can rename app.orders.total, split the table, or partition it,
-- and the view absorbs the change. The consumers never notice.
-- this is the same argument as "an entity is not your API contract",
-- applied one layer down.
```

``` csharp
// and the outbox, which is how you publish without a distributed transaction
await using var tx = await db.Database.BeginTransactionAsync(ct);

order.MarkPlaced();
db.Outbox.Add(new OutboxMessage
{
    Id = Guid.CreateVersion7(),
    Type = nameof(OrderPlaced),
    Payload = JsonSerializer.Serialize(new OrderPlaced(order.Id, order.Total)),
    OccurredAt = timeProvider.GetUtcNow()
});
await db.SaveChangesAsync(ct);           // BOTH in one local transaction
await tx.CommitAsync(ct);

// a relay polls the outbox and publishes. If it crashes after publishing and
// before marking, the message is delivered twice - so consumers must be
// idempotent. That is the trade: at-least-once instead of 2PC, and it is
// almost always the right one.
```

> **\[Note\]**
>
> **The rule that prevents this happening again.** **One writer per table.** Everything else reads through a view, an API, or an event stream. It is a small constraint that preserves your ability to change the schema, and its absence is the single most common reason a legacy database cannot be modernised — not because the schema is bad, but because nobody knows who depends on it.

### Operations & Reliability

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Operations & Reliability</p>

<p class="pd">Keeping a database alive: backups you have actually tested, replication, failover, and the handful of metrics that tell you something is wrong before users do.</p>

<a id="be-d451"></a>

#### Backup types and what each protects against

| Type                                      | Contains                           | Restore speed             | Protects against                                     |
|-------------------------------------------|------------------------------------|---------------------------|------------------------------------------------------|
| **Logical dump** (`pg_dump`, `mysqldump`) | SQL statements or an archive       | Slow — replays everything | Corruption, version migration, single-table recovery |
| **Physical / base backup**                | The data files themselves          | Fast — copy back          | Full-instance loss                                   |
| **Incremental / differential**            | Changed blocks since the last full | Full + increments         | Reducing backup window and storage                   |
| **WAL / log archive**                     | Every change since the base backup | Replay to any instant     | Point-in-time recovery                               |
| **Snapshot** (storage or cloud)           | A block-level image                | Very fast                 | Instance loss — verify crash consistency             |
| **Replica**                               | A live second copy                 | Instant failover          | **Not a backup** — it replicates your mistakes       |

The last row is the one that causes real losses. A replica faithfully applies `DELETE FROM orders` within milliseconds. High availability and backup are different problems with different solutions, and having one does not give you the other.

``` text
# logical: portable, granular, slow to restore
pg_dump --format=custom --compress=9 --file=db.dump mydb
pg_restore --dbname=mydb --jobs=8 db.dump          # parallel restore

# physical + WAL archiving: the basis of point-in-time recovery
pg_basebackup -D /backup/base -Ft -z -Xs -P
archive_command = 'test ! -f /archive/%f && cp %p /archive/%f'

# and the tools you should actually use in production, which handle
# retention, verification, incremental and parallelism for you:
pgbackrest backup --stanza=main --type=incr
barman backup main
```

| Rule                                           | Reason                                                      |
|------------------------------------------------|-------------------------------------------------------------|
| 3-2-1: three copies, two media, one off-site   | Correlated failure is the norm, not the exception           |
| One copy immutable or in a separate account    | Ransomware and compromised credentials delete backups first |
| Encrypt at rest, and manage the key separately | A backup is a full copy of your data with no access control |
| Test the *restore*, on a schedule              | An untested backup is a hypothesis                          |
| Record how long a restore takes                | That number is your real RTO, whatever the plan says        |

> **\[Note\]**
>
> **The exercise that finds every gap.** Restore your production database into an empty environment, from backups only, following the written runbook, and time it. Almost every team that does this for the first time discovers something missing — a role, an extension, a sequence value, a certificate, or that the restore takes nine hours rather than one.

<a id="be-d452"></a>

#### Point-in-time recovery

The capability that matters after human error: not “restore last night's backup” but “restore to 14:32:57, immediately before the bad migration ran”.

``` text
  Sunday 02:00     base backup taken
  Sunday-Thursday  WAL segments archived continuously
  Thursday 14:33   someone runs UPDATE without a WHERE clause
  Thursday 14:50   discovered

  recovery:
    1. restore the base backup to a NEW instance (never over the live one)
    2. replay archived WAL up to recovery_target_time = '14:32:57'
    3. verify the data looks right BEFORE promoting
    4. either promote it, or extract just the affected rows and apply them

  RPO here is effectively zero (down to the last archived segment).
  RTO is however long the restore plus replay takes - which is why you measure it.
```

``` sql
# PostgreSQL recovery target options
restore_command = 'cp /archive/%f %p'
recovery_target_time = '2026-07-28 14:32:57+02'
recovery_target_action = 'pause'      # PAUSE so you can inspect before promoting
# alternatives: recovery_target_lsn, recovery_target_xid, recovery_target_name

# then, after verifying:
SELECT pg_wal_replay_resume();        # or pg_promote()
```

- **Restore to a new instance**, always. Recovering over the live database removes your remaining options if the target time was wrong.
- **Pause before promoting.** You will frequently need to adjust the target by a few seconds once you see the data.
- **Extract rather than switch, when you can.** If the damage is one table, restore aside and copy the good rows back — far less disruptive than rewinding the whole database.
- **Know your archive retention.** Point-in-time recovery only reaches as far back as your oldest base backup plus continuous WAL.
- **Test it with a real scenario.** “Recover the customers table to 10:00 yesterday” is a good drill.

<a id="be-d453"></a>

#### Replication topologies

| Type                     | Ships               | Standby is                            | Trade-off                                               |
|--------------------------|---------------------|---------------------------------------|---------------------------------------------------------|
| **Physical / streaming** | WAL records         | A byte-identical copy                 | All or nothing; same version and platform               |
| **Logical**              | Decoded row changes | Selected tables, possibly transformed | Flexible; more overhead; DDL is not replicated          |
| **Statement-based**      | The SQL text        | Re-executed                           | Non-deterministic functions diverge — largely abandoned |
| **Row-based**            | Before/after images | Applied directly                      | MySQL default; safe; larger log                         |

``` text
  synchronous vs asynchronous - the RPO/latency dial

  ASYNC     primary commits, then ships
              commit latency: unaffected
              RPO: whatever has not shipped yet (usually ms, occasionally minutes)

  SYNC      primary waits for the standby to confirm before COMMIT returns
              commit latency: + one network round trip
              RPO: zero
              and: if the standby dies, the PRIMARY STALLS unless you have
                   a second sync candidate or a fallback rule

  the common production shape: two synchronous candidates (any one suffices)
  plus asynchronous replicas for read scaling and a cross-region copy.
```

``` sql
-- PostgreSQL
synchronous_standby_names = 'ANY 1 (standby_a, standby_b)'   -- either confirms
-- 'FIRST 1 (...)' = priority order.  Empty = fully asynchronous.

-- logical replication: replicate selected tables, even across versions
CREATE PUBLICATION analytics FOR TABLE orders, customers;
CREATE SUBSCRIPTION analytics_sub
    CONNECTION 'host=primary dbname=app'
    PUBLICATION analytics;
```

> **\[Trap\]**
>
> **Common trap.** A synchronous standby that falls behind or dies takes the primary's write availability with it. Configure at least two candidates with `ANY 1`, monitor standby health as a primary-availability concern, and be certain the operations team knows how to demote to asynchronous under pressure — that decision should not be improvised at 3am.

<a id="be-d454"></a>

#### Replication lag and read-your-writes

Every asynchronous replica is behind. Usually by milliseconds, occasionally by minutes during a bulk load, a long transaction on the primary, or a network hiccup. Any design that assumes lag is always small will fail during exactly the incident you least want it to.

``` text
  the read-after-write problem

  user saves a form         -> PRIMARY   (committed)
  redirect to the detail page -> REPLICA  (lag 200 ms)
  the page shows the OLD value
  user saves again, or reports a bug

  the same problem appears as: a just-created record 404s, a list omits
  a new row, a counter appears not to increment.
```

| Technique                           | How                                                                  | Cost                                     |
|-------------------------------------|----------------------------------------------------------------------|------------------------------------------|
| Return the written state            | The write path already has it — never re-read                        | Free. Do this by default                 |
| Sticky reads after a write          | Route this user to the primary for N seconds                         | Session state or a cookie                |
| Wait for a replication position     | The write returns an LSN; the read waits for the replica to reach it | Added latency; needs support             |
| Read the primary for critical paths | Classify reads as tolerant or not                                    | Primary load; simple and explicit        |
| Accept it in the UI                 | Show “saved” without re-fetching                                     | Free, and often the right product answer |

``` sql
-- monitor lag in both time and bytes; time is what users feel
SELECT client_addr, state, sync_state,
       pg_wal_lsn_diff(sent_lsn, replay_lsn) AS replay_bytes_behind,
       replay_lag
FROM   pg_stat_replication;

-- on the replica itself
SELECT now() - pg_last_xact_replay_timestamp() AS lag;
```

> **\[Note\]**
>
> **Classify reads explicitly.** Two named connections — one to the primary, one to the replicas — and a deliberate decision per query, is far more maintainable than a clever proxy that guesses. Make “can this read tolerate being a second stale?” a question developers answer at the point of writing the query.

<a id="be-d455"></a>

#### Failover, split brain and quorum

Failover is the part of high availability that is easy to describe and hard to get right, because the failure modes are ambiguous: a primary that stops responding may be dead, or may be alive and unreachable.

``` text
  SPLIT BRAIN

  network partition between the primary and the monitor.
  the monitor cannot reach the primary -> assumes it is dead -> promotes a standby.
  but the primary is ALIVE and still accepting writes from clients on its side.

  now TWO primaries accept conflicting writes.
  when the partition heals, there is no correct way to merge them.
  this is the worst outcome in database operations - worse than downtime.

  PREVENTION
    quorum        an odd number of voters; promotion requires a majority
    fencing/STONITH   the old primary is forcibly isolated before promotion
    witness node  a third participant to break ties
    a VIP or a proxy that only ONE node can hold
```

| Component          | Role                                   | Examples                                                |
|--------------------|----------------------------------------|---------------------------------------------------------|
| Failure detector   | Decides the primary is gone            | Patroni, Orchestrator, cluster manager                  |
| Consensus store    | Prevents two promotions                | etcd, Consul, ZooKeeper — itself needing quorum         |
| Fencing            | Ensures the old primary cannot write   | STONITH, revoking a VIP, cutting storage access         |
| Client redirection | Points applications at the new primary | DNS (slow), a VIP, or a proxy such as PgBouncer/HAProxy |
| Standby promotion  | Makes a replica writable               | `pg_promote()`, engine-specific                         |

- **Automatic failover needs quorum.** A two-node cluster cannot distinguish a dead peer from a partition, so it either risks split brain or refuses to act.
- **Client redirection is usually the slow part.** A 300-second DNS TTL means a five-minute outage regardless of how quickly promotion happens. Use a proxy or a VIP.
- **Connection pools must be told.** Pooled connections to the old primary must be invalidated, or the application keeps trying to write to a demoted node.
- **Practise it.** An untested failover is a hypothesis. Run a drill in staging, and ideally in production during a maintenance window.
- **Consider whether you want it automatic at all.** For many systems a five-minute human-approved failover is preferable to the tail risk of an automatic one going wrong.

<a id="be-d456"></a>

#### Monitoring: what to actually watch

| Metric                      | Healthy                      | Means trouble when        | First action                                            |
|-----------------------------|------------------------------|---------------------------|---------------------------------------------------------|
| Cache hit ratio             | \> 99% (OLTP)                | Drops steadily            | Working set exceeds memory, or plans regressed to scans |
| Active connections          | Well under `max_connections` | Climbing toward the limit | Add a pooler; find the leak                             |
| `idle in transaction`       | Near zero                    | Anything sustained        | Blocks all MVCC cleanup — treat as urgent               |
| Longest transaction age     | Seconds                      | Minutes or hours          | Find and kill it; bloat is accruing                     |
| Replication lag             | \< 1 s                       | Growing monotonically     | Check standby I/O and long queries there                |
| Deadlocks per hour          | A few                        | Dozens                    | A lock-ordering problem in the application              |
| Dead tuple ratio            | \< 10%                       | \> 20%                    | Autovacuum is not keeping up — tune it                  |
| Checkpoint frequency        | Mostly timed                 | Mostly requested          | Increase `max_wal_size`                                 |
| Temp file bytes             | Low                          | Frequent spills           | `work_mem` too small, or missing indexes                |
| Disk free on the WAL volume | Ample                        | Falling                   | A stalled replication slot — the classic outage cause   |

``` sql
-- the single most valuable extension for query monitoring
CREATE EXTENSION pg_stat_statements;

SELECT calls, total_exec_time, mean_exec_time, rows,
       100.0 * shared_blks_hit / NULLIF(shared_blks_hit + shared_blks_read, 0) AS hit_pct,
       left(query, 100) AS query
FROM   pg_stat_statements
ORDER  BY total_exec_time DESC LIMIT 25;
-- order by TOTAL time, not mean: a 20 ms query called 2 million times an hour
-- costs far more than a 4-second report run twice a day.

-- SQL Server: Query Store is the equivalent, and is on by default in recent versions
```

> **\[Note\]**
>
> **Alert on symptoms, not causes.** High CPU is not an incident. Query latency above the SLO, a growing connection count, a stalled replication slot, or a transaction open for an hour — those are. Build the dashboard from this table, and set alerts only on the rows where a human needs to act tonight.

<a id="be-d457"></a>

#### Routine maintenance

Databases need housekeeping. Most of it is automatic and the automatic settings are frequently too conservative for a busy system.

| Task                          | Purpose                                        | Automatic? | When to intervene                                       |
|-------------------------------|------------------------------------------------|------------|---------------------------------------------------------|
| `VACUUM`                      | Reclaim dead tuples, update the visibility map | Autovacuum | Dead tuple ratio climbing; tune per table               |
| `ANALYZE`                     | Refresh statistics                             | Autovacuum | Immediately after a bulk load                           |
| Index rebuild                 | Restore page density                           | No         | When density drops below ~60%                           |
| `VACUUM FULL`                 | Rewrite the table compactly                    | No         | Rarely — it takes an exclusive lock. Prefer `pg_repack` |
| Partition management          | Create and drop time partitions                | No         | Automate it; a missing partition is an outage           |
| Statistics for the plan cache | Avoid stale plans                              | Partly     | After large data shape changes                          |

``` sql
-- autovacuum defaults scale by table size, which means huge tables are vacuumed
-- far too rarely. Override per table for the busy ones:
ALTER TABLE orders SET (
    autovacuum_vacuum_scale_factor = 0.02,     -- 2% instead of the 20% default
    autovacuum_analyze_scale_factor = 0.01,
    autovacuum_vacuum_cost_delay = 2           -- let it work faster
);

-- and always ANALYZE explicitly after a bulk load: autovacuum may not run for
-- minutes, and until it does, every plan is built on the pre-load statistics.
COPY orders FROM '/data/orders.csv' CSV;
ANALYZE orders;
```

> **\[Trap\]**
>
> **Common trap.** Never disable autovacuum to “improve performance”. Bloat grows without bound, plans degrade, and on PostgreSQL transaction-id wraparound protection will eventually force the database into a read-only emergency state that requires a lengthy offline vacuum to escape. If autovacuum is causing I/O pressure, tune its cost limits so it works more gently and more often — do not switch it off.

<a id="be-d458"></a>

#### Vacuum, bloat and transaction ID wraparound

The single most important PostgreSQL-specific operational topic, and the cause of more emergency outages than any other. It follows directly from MVCC: if old row versions are never cleaned up, the database eventually cannot function.

##### What VACUUM actually does

| Job                       | Detail                                                               |
|---------------------------|----------------------------------------------------------------------|
| Reclaim dead tuples       | Marks space reusable *within* the page — it does not shrink the file |
| Update the visibility map | Marks all-visible pages, which is what enables index-only scans      |
| Freeze old tuples         | Marks rows as permanently visible, retiring their transaction ids    |
| Update the free space map | So inserts know where there is room                                  |
| Update statistics         | Only `ANALYZE` does this; `VACUUM ANALYZE` does both                 |

``` text
  VACUUM vs VACUUM FULL - very different operations

  VACUUM         marks dead space reusable. Concurrent. Does NOT return space to the OS.
                 the table stays the same size on disk, and new rows fill the gaps.
                 -> this is the routine one. Autovacuum runs it.

  VACUUM FULL    rewrites the entire table compactly. Takes an ACCESS EXCLUSIVE lock:
                 no reads, no writes, for the duration. Needs 2x the disk space.
                 -> almost never the right answer on a live system.

  pg_repack      rewrites compactly WITHOUT the long exclusive lock. Use this instead.
```

##### Transaction ID wraparound

``` text
  transaction ids are 32-bit: about 4 billion, and they WRAP.

  MVCC visibility compares transaction ids, so a wrapped id would appear to be
  "in the future" and rows would vanish. PostgreSQL prevents this by FREEZING old
  rows - marking them as visible to everyone, so their id no longer matters.

  freezing happens during VACUUM. If VACUUM cannot keep up:

    age 200 million    autovacuum starts an anti-wraparound vacuum (autovacuum_freeze_max_age)
                       -> it will NOT back off for a lock conflict, unlike normal autovacuum
    age 10 million left  WARNING in the log, every commit
    age 1 million left   the database REFUSES ALL WRITES to protect itself
                       -> recovery requires a single-user vacuum, which can take HOURS

  what prevents freezing:
    a very long-running transaction         (holds back the horizon)
    an abandoned replication slot           (holds back the horizon)
    an unresolved prepared transaction       (holds back the horizon, forever)
    autovacuum disabled or badly starved
```

``` sql
-- the query to have on a dashboard. Alert well before 200 million.
SELECT datname,
       age(datfrozenxid) AS xid_age,
       round(100.0 * age(datfrozenxid) / 2147483648, 2) AS pct_to_wraparound
FROM   pg_database ORDER BY xid_age DESC;

-- per table, to find the one holding things back
SELECT relname, age(relfrozenxid) AS xid_age, n_dead_tup, last_autovacuum
FROM   pg_stat_user_tables t
JOIN   pg_class c ON c.relname = t.relname
ORDER  BY age(relfrozenxid) DESC LIMIT 20;

-- and the three things that block the horizon
SELECT pid, age(backend_xmin) AS xmin_age, state, left(query,60)
FROM   pg_stat_activity WHERE backend_xmin IS NOT NULL ORDER BY 2 DESC;

SELECT slot_name, active, pg_size_pretty(
         pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn)) AS wal_retained
FROM   pg_replication_slots ORDER BY 3 DESC;

SELECT gid, prepared, age(transaction) FROM pg_prepared_xacts;   -- almost always a bug
```

``` sql
-- tune autovacuum for large busy tables. The defaults scale by table size,
-- which means the biggest tables are vacuumed least often - exactly backwards.
ALTER TABLE orders SET (
    autovacuum_vacuum_scale_factor  = 0.02,   -- 2% dead, not the 20% default
    autovacuum_analyze_scale_factor = 0.01,
    autovacuum_vacuum_cost_delay    = 2,      -- work faster (ms of pause per cost unit)
    autovacuum_vacuum_cost_limit    = 2000    -- more work per cycle
);

-- and globally, if autovacuum is chronically behind
autovacuum_max_workers = 6
autovacuum_naptime = '15s'
maintenance_work_mem = '2GB'                  -- fewer index passes per vacuum
```

| Symptom                                    | Cause                              | Action                                        |
|--------------------------------------------|------------------------------------|-----------------------------------------------|
| Table grows though rows are deleted        | Bloat — vacuum not keeping up      | Tune per-table autovacuum settings            |
| Index-only scans stopped working           | Visibility map stale               | `VACUUM` the table                            |
| WAL disk filling                           | Abandoned replication slot         | Drop the slot                                 |
| `xid_age` climbing steadily                | Something holds the horizon        | Find it in the three queries above            |
| Autovacuum running constantly on one table | Anti-wraparound vacuum in progress | Let it finish; do not cancel it               |
| Sudden refusal of writes                   | Wraparound protection triggered    | Emergency single-user vacuum — plan for hours |

> **\[Trap\]**
>
> **Common trap.** Never cancel an anti-wraparound autovacuum to “reduce load”, and never disable autovacuum. Both convert a manageable background cost into an outage that requires downtime to resolve. If vacuum is causing I/O pressure, make it gentler and more frequent by lowering the cost delay and the scale factor — not by stopping it. Note also that SQL Server and Oracle avoid this specific failure mode entirely, using different MVCC implementations, so this is genuinely PostgreSQL-specific knowledge.

<a id="be-d459"></a>

#### Benchmarking, done fairly

Most database benchmarks measure the wrong thing and prove nothing. A useful benchmark answers a specific question about your workload, on production-like data, at a latency target you care about.

1.  **Write the question down first.** “Can this instance sustain 800 order inserts per second with p99 under 50 ms?” — not “is Postgres fast?”
2.  **Use production-shaped data at production scale.** A benchmark on 10,000 rows fits entirely in cache and measures nothing but CPU.
3.  **Model the real mix.** 95% reads and 5% writes behaves nothing like the reverse; a single-statement loop behaves nothing like real transactions.
4.  **Warm the cache before measuring**, then measure a steady state long enough to include a checkpoint and an autovacuum cycle.
5.  **Report percentiles, not averages.** The average hides the tail, and the tail is what users experience.
6.  **Find the knee, not the ceiling.** Throughput at an acceptable latency is the useful number; maximum throughput at 4-second latency is not.
7.  **Change one variable at a time**, and re-run the baseline afterwards to confirm nothing else drifted.

``` sql
# pgbench: built in, and the custom-script mode is the useful part
pgbench -i -s 500 mydb                       # initialise, scale 500 (~7.5 GB)

pgbench -c 32 -j 8 -T 600 -P 10 \\
        --progress-timestamp \\
        -f order_insert.sql@70 -f order_read.sql@30 \\
        mydb
#  -c clients, -j threads, -T seconds, -P progress interval
#  @70 / @30 = weighted mix of two scripts

# a custom script, with variables and a realistic transaction shape
cat > order_insert.sql <<'SQL'
\set customer random(1, 500000)
\set qty random(1, 5)
BEGIN;
INSERT INTO orders (customer_id, placed_at, total)
VALUES (:customer, now(), random()*500) RETURNING id \gset
INSERT INTO order_lines (order_id, sku, qty) VALUES (:id, 'SKU-1', :qty);
COMMIT;
SQL
```

| Tool                           | Workload                      | Use for                                                           |
|--------------------------------|-------------------------------|-------------------------------------------------------------------|
| `pgbench`                      | TPC-B-like, or custom scripts | PostgreSQL; the custom-script mode is genuinely good              |
| HammerDB                       | TPC-C and TPC-H               | Cross-engine comparison, OLTP and OLAP                            |
| sysbench                       | OLTP, configurable            | MySQL and PostgreSQL                                              |
| `ostinato` / `SQLQueryStress`  | Ad-hoc concurrency            | SQL Server                                                        |
| Your own application, replayed | The real workload             | **The most valuable option** — capture real traffic and replay it |

| Common benchmarking mistake            | Why it invalidates the result                          |
|----------------------------------------|--------------------------------------------------------|
| Measuring a cold cache                 | Every run measures disk, not the steady state          |
| Too little data                        | Everything is cached; index depth and I/O never matter |
| Single-threaded                        | Never exercises locking, latching or the pool          |
| No think time                          | Produces unrealistic contention on the same rows       |
| Averages only                          | A p99 of 8 s hides behind a mean of 40 ms              |
| Running against a shared or noisy host | Variance exceeds the effect you are measuring          |
| Comparing engines at default settings  | You are comparing default configurations, not engines  |
| Not repeating the baseline             | Drift in the environment is attributed to your change  |

> **\[Note\]**
>
> **The most useful benchmark you can build.** Capture a representative hour of production queries — from `pg_stat_statements`, the slow log, or a proxy — and replay it at 1×, 2× and 4× against a restored copy. That answers the question you actually have (“what happens when we grow?”) far better than any synthetic suite, and it doubles as your restore test.

<a id="be-d460"></a>

#### Modern observability tooling

The instrumentation inside the engine is only useful if something collects and presents it. The tooling has improved considerably and most teams use far less of it than is available.

| Layer                 | PostgreSQL                             | SQL Server                       | Purpose                                                       |
|-----------------------|----------------------------------------|----------------------------------|---------------------------------------------------------------|
| Query statistics      | `pg_stat_statements`                   | Query Store                      | Top queries by total time — the starting point for everything |
| Wait analysis         | `pg_wait_sampling`, `pg_stat_activity` | Wait statistics DMVs             | *Why* queries are slow, not just which                        |
| Plan history          | `auto_explain`                         | Query Store plan history         | Catching a plan regression after the fact                     |
| Log analysis          | pgBadger, pgAnalyze                    | Extended Events, Log Analytics   | Trends, error patterns, slow-query reports                    |
| Visual plan analysis  | explain.dalibo.com, pgMustard, PEV     | SSMS, Plan Explorer              | Reading a large plan without going cross-eyed                 |
| Continuous monitoring | pganalyze, Datadog DBM, Percona PMM    | Datadog, SQL Sentry, Query Store | Correlating database and application metrics                  |
| Index advice          | `hypopg`, `pg_qualstats`               | Missing index DMVs, DTA          | Hypothetical indexes tested without building them             |

``` text
-- auto_explain: log the plan for anything slow, automatically. Invaluable, because
-- the slow execution has already happened by the time you go looking for it.
shared_preload_libraries = 'pg_stat_statements,auto_explain'
auto_explain.log_min_duration = '500ms'
auto_explain.log_analyze = on          -- include ACTUAL rows and timings
auto_explain.log_buffers = on
auto_explain.log_nested_statements = on
auto_explain.log_format = json         -- so a log pipeline can parse it
auto_explain.sample_rate = 0.5         -- reduce overhead on very busy systems
```

``` sql
-- hypopg: does this index help, WITHOUT building it on a 400 GB table?
CREATE EXTENSION hypopg;

SELECT * FROM hypopg_create_index(
  'CREATE INDEX ON orders (tenant_id, status, placed_at DESC)');

EXPLAIN SELECT * FROM orders
WHERE tenant_id = :t AND status = 'Open' ORDER BY placed_at DESC LIMIT 50;
-- if the plan now shows an Index Scan on <13234>btree_orders, the index would be used.

SELECT hypopg_reset();
-- this turns "let's try an index and see" from a multi-hour maintenance window
-- into a ten-second experiment.
```

``` sql
-- newer EXPLAIN options worth knowing
EXPLAIN (ANALYZE, BUFFERS, SETTINGS, WAL) SELECT ...;
--   SETTINGS  shows non-default planner settings in effect - explains "works on my machine"
--   WAL       shows WAL generated, for write-heavy statements
EXPLAIN (GENERIC_PLAN) SELECT * FROM orders WHERE id = $1;
--   plan a parameterised statement WITHOUT supplying values - the generic plan
--   the server would cache. Directly useful for diagnosing sniffing-style problems.
```

> **\[Note\]**
>
> **The three-item minimum.** If a database has nothing else, it should have `pg_stat_statements` (or Query Store), `auto_explain` at a sensible threshold, and an alert on the longest-running transaction. Those three answer most incidents, and each takes minutes to enable.

### Scaling

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Scaling</p>

<p class="pd">Growing past one machine. Read scaling, partitioning, sharding, pooling and caching — roughly in the order you should attempt them, because each step costs considerably more complexity than the last.</p>

<a id="be-d461"></a>

#### The order to scale in

``` text
  attempt these in order. Most systems never need to reach the bottom.

  1  FIX THE QUERIES        the top 10 queries by total time. Indexes, rewrites.
                            frequently a 10-100x win for a day's work.
  2  TUNE THE INSTANCE      memory, checkpoints, autovacuum, connection pooling.
  3  SCALE UP               a bigger machine. Boring, effective, has a ceiling.
  4  CACHE                  move repeated reads off the database entirely.
  5  READ REPLICAS          one writer, N readers. Great for read-heavy loads.
  6  PARTITION              one database, many physical partitions. Transparent.
  7  SHARD                  many databases by key. Powerful, and much harder.
  8  SEPARATE OLAP          move analytics to a warehouse or a columnar store.

  teams routinely jump to 7 when 1 would have sufficed. Sharding a system whose
  real problem is a missing index means you now have that problem N times.
```

| Step              | Effort | Typical gain              | Complexity added                                     |
|-------------------|--------|---------------------------|------------------------------------------------------|
| Fix queries       | Days   | 2–100×                    | None                                                 |
| Tune the instance | Days   | 1.2–3×                    | None                                                 |
| Scale up          | Hours  | 2–10×                     | None — and a hard ceiling                            |
| Cache             | Weeks  | 2–50× on reads            | Invalidation correctness                             |
| Read replicas     | Weeks  | N× on reads               | Lag, read-your-writes                                |
| Partition         | Weeks  | Large on time-ranged data | Partition management                                 |
| Shard             | Months | Near-linear               | **Very high** — no cross-shard joins or transactions |

> **\[Why it matters\]** Each step is roughly an order of magnitude more expensive to operate than the one above it, and the gains are not cumulative in the way people hope. The discipline is to exhaust the cheap steps and to be able to prove you have.

<a id="be-d462"></a>

#### Connection pooling

The most common cause of a database that falls over under load is not query cost. It is connection count.

``` text
  each PostgreSQL connection is a separate OS PROCESS
    ~5-10 MB of memory
    plus per-connection work_mem allocations during queries

  20 application pods x a pool of 100 = 2,000 connections
  against a database with max_connections = 200

  what actually happens: connection errors, then a thundering herd of retries,
  then the database spends its CPU on context switching rather than on queries.

  and the arithmetic that surprises people: a database with 16 cores cannot
  usefully execute more than a few dozen queries at once. 2,000 connections
  do not give you 2,000x the throughput - they give you a queue with overhead.
```

| Layer            | Tool                                   | Note                                                           |
|------------------|----------------------------------------|----------------------------------------------------------------|
| Application pool | Built into the driver                  | Size it per instance: concurrent queries, not concurrent users |
| External pooler  | PgBouncer, pgpool, ProxySQL, RDS Proxy | Multiplexes many client connections onto few server ones       |
| Server limit     | `max_connections`                      | A ceiling, not a target                                        |

``` text
# PgBouncer in transaction mode: the highest-value single change for many systems
[databases]
app = host=primary port=5432 dbname=app

[pgbouncer]
pool_mode = transaction        # a server connection is held only for the transaction
max_client_conn = 5000         # what applications may open
default_pool_size = 40         # what actually reaches PostgreSQL
reserve_pool_size = 10
server_idle_timeout = 60
```

| Pool mode     | Server connection held for  | Breaks                                                                   |
|---------------|-----------------------------|--------------------------------------------------------------------------|
| `session`     | The whole client connection | Nothing — and gives little benefit                                       |
| `transaction` | One transaction             | Session state: `SET`, advisory locks, `LISTEN`, some prepared statements |
| `statement`   | One statement               | Multi-statement transactions entirely                                    |

Sizing rule of thumb, from Little's Law: concurrent queries needed = arrival rate × average query time. 400 queries per second at 20 ms each is 8 concurrent queries. A pool of 20–40 server connections serves that comfortably; a pool of 2,000 serves it worse.

> **\[Trap\]**
>
> **Common trap.** `transaction` mode is where the benefit is, and it silently breaks anything relying on session state — session-level `SET`, temporary tables, advisory locks, `LISTEN/NOTIFY`, and server-side prepared statements unless the driver and pooler both support it. Test these specifically before switching.

<a id="be-d463"></a>

#### Caching, and where to put it

``` text
  browser -> CDN -> application cache -> distributed cache -> DATABASE
                      (in-process)          (Redis)

  closer to the user: faster, cheaper, staler, harder to invalidate.

  and the layer people forget: the database's OWN cache.
  a 99% buffer-pool hit ratio means most reads never touch disk already.
  adding Redis in front of a well-cached database buys latency and
  offloads CPU - not I/O.
```

| Cache                           | Good for                                     | Invalidation                                  |
|---------------------------------|----------------------------------------------|-----------------------------------------------|
| Materialised view               | Expensive aggregates over slow-changing data | Scheduled or incremental refresh              |
| Summary table                   | The same, with full control                  | Triggers, or a batch job                      |
| Distributed cache               | Hot rows read far more than written          | TTL, plus eviction on write                   |
| In-process cache                | Reference data, config, lookups              | Short TTL — it cannot be invalidated remotely |
| Prepared statement / plan cache | Automatic                                    | The engine handles it                         |
| HTTP / CDN                      | Public, immutable responses                  | Cache-Control and versioned URLs              |

``` sql
-- materialised views: precomputed, refreshable, and indexable
CREATE MATERIALIZED VIEW mv_daily_revenue AS
SELECT date_trunc('day', placed_at) AS day, country, sum(total) AS revenue,
       count(*) AS orders
FROM   orders GROUP BY 1, 2;

CREATE UNIQUE INDEX ux_mv_daily ON mv_daily_revenue (day, country);  -- required below
REFRESH MATERIALIZED VIEW CONCURRENTLY mv_daily_revenue;   -- no read lock; needs the index

-- a 40-second aggregate becomes a 3 ms indexed lookup, at the cost of
-- being as stale as your refresh interval.
```

| Cache problem            | Symptom                                                     | Mitigation                                     |
|--------------------------|-------------------------------------------------------------|------------------------------------------------|
| Stampede                 | The entry expires and 500 requests hit the database at once | A lock per key, or stale-while-revalidate      |
| Stale data               | A user updates something and sees the old value             | Evict on write; short TTLs; version the key    |
| Unbounded growth         | Memory climbs until eviction or OOM                         | Size limits and an explicit eviction policy    |
| Key collisions           | The wrong tenant's data returned                            | Include tenant and schema version in every key |
| Cache as source of truth | Data lost on restart                                        | Never — the cache must always be rebuildable   |

<a id="be-d464"></a>

#### Read replicas

One writer, several readers. The cheapest real scaling step for a read-heavy workload, and the one with the most subtle application consequences.

``` text
                    writes
  application  --------------->  PRIMARY
       |                            | async WAL stream
       |  reads                     v
       +----------------->  REPLICA 1   REPLICA 2   REPLICA 3
                           (reporting)  (app reads) (cross-region)

  the primary still handles every write, so this scales READS only.
  a write-bound system gains nothing from replicas.
```

| Route to a replica      | Keep on the primary                                 |
|-------------------------|-----------------------------------------------------|
| Reports and analytics   | Anything read immediately after writing it          |
| Search and list views   | Balance checks and limit validations before a write |
| Exports and batch reads | Anything inside a read-write transaction            |
| Dashboards              | Uniqueness checks that guard an insert              |
| Read-only API endpoints | Sequence or counter reads used for a decision       |

``` text
-- dedicate a replica to reporting so long analytics queries cannot evict the
-- OLTP working set from the primary's buffer pool, and configure it to tolerate
-- them without cancelling:
hot_standby_feedback = on        -- tell the primary not to vacuum rows we still need
max_standby_streaming_delay = 30s  -- how long replay may wait for a running query
-- the trade: hot_standby_feedback lets a long replica query cause bloat on the PRIMARY.
-- pick one: bloat on the primary, or queries cancelled on the replica.
```

> **\[Trap\]**
>
> **Common trap.** A long-running query on a replica conflicts with replay of a vacuum that removes rows it still needs. Either the query is cancelled (`ERROR: canceling statement due to conflict with recovery`) or replay stalls and lag grows. There is no configuration that avoids both — which is the real reason reporting deserves its own dedicated replica with settings tuned for it.

<a id="be-d465"></a>

#### Partitioning

Splitting one logical table into several physical ones, transparently to queries. The highest value-to-effort scaling technique for any table that grows forever and is queried by a range.

``` text
  query: WHERE occurred_at >= '2026-07-01' AND tenant_id = :t

    unpartitioned   index scan over 900 million rows' worth of index
    partitioned     PARTITION PRUNING - the planner touches ONE partition of 25M

  retention: delete everything older than 24 months

    unpartitioned   DELETE FROM audit_events WHERE occurred_at < ...
                    -> hours, huge WAL volume, table bloat, vacuum pressure, lock contention
    partitioned     DROP TABLE audit_events_2024_07;
                    -> milliseconds, almost no WAL, no bloat
```

| Strategy  | Partition by           | Fits                                               |
|-----------|------------------------|----------------------------------------------------|
| Range     | A date, or an id range | Time-series, audit logs, events — the common case  |
| List      | An explicit value set  | Region, country, tenant tier                       |
| Hash      | A hash of the key      | Even distribution when there is no natural range   |
| Composite | Range then hash        | Time plus tenant, for very large multi-tenant data |

- **The partition key must appear in the `WHERE` clause**, or pruning cannot happen and every partition is scanned.
- **The partition key must be part of any unique constraint.** A global unique index across partitions is not generally possible.
- **Automate partition creation.** A missing future partition means failed inserts — use `pg_partman` or a scheduled job, and monitor that it ran.
- **Too many partitions hurts.** Planning time grows with partition count; thousands is usually too many.
- **Indexes are per partition** in PostgreSQL, created automatically when defined on the parent.

> **\[Note\]**
>
> **The retention argument alone justifies it.** On a large table, purging old rows is often the single most disruptive routine operation a system performs. Partitioning turns it into a metadata change. If you have any table that grows forever with a retention policy, this is probably the highest-value database change available to you.

<a id="be-d466"></a>

#### Sharding

Splitting data across independent databases by a key. It scales writes and storage near-linearly, and it costs more than every other option combined.

``` text
  shard 1            shard 2            shard 3
  tenants A-H        tenants I-P        tenants Q-Z

  what you GAIN
    writes scale with shard count
    storage and working set per shard stay manageable
    a failure affects a subset of tenants, not everyone

  what you LOSE
    cross-shard JOINs             -> application-side joins, or a query router
    cross-shard TRANSACTIONS      -> sagas, or 2PC
    global uniqueness             -> UUIDs, or a central sequence service
    global ORDER BY / aggregate   -> scatter-gather and merge
    simple migrations             -> N schema changes, coordinated
    simple backups                -> N backups, restored to a consistent point
    rebalancing                   -> the hardest ongoing problem
```

| Shard key choice      | Verdict                                                  |
|-----------------------|----------------------------------------------------------|
| Tenant / customer id  | Usually best — most queries already filter by it         |
| Geographic region     | Good when data residency is a requirement anyway         |
| Hash of the entity id | Even distribution, but every cross-entity query fans out |
| Time                  | **Bad** — the current shard takes all the writes         |
| Sequential id ranges  | **Bad** — the same hot-shard problem                     |

The key must appear in nearly every query, or you scatter-gather across all shards and lose the benefit. This is why tenant id is the usual answer in B2B systems and why sharding a consumer social graph is genuinely hard.

| Approach                  | Examples                               | Note                                        |
|---------------------------|----------------------------------------|---------------------------------------------|
| Application-level routing | A shard map in your code               | Total control, total responsibility         |
| Proxy / coordinator       | Citus, Vitess, ShardingSphere          | Presents one logical database               |
| Distributed SQL           | CockroachDB, YugabyteDB, TiDB, Spanner | Sharding is the engine's problem, not yours |
| Managed                   | Azure SQL Hyperscale, Aurora Limitless | Reduced operational burden                  |

> **\[Note\]**
>
> **Consider distributed SQL before hand-rolling.** If you have concluded you need sharding, evaluate a distributed SQL engine first. They give you horizontal writes while keeping SQL, transactions and joins — at the cost of higher latency per operation and a less mature ecosystem. That is frequently a better trade than reimplementing a query router and a rebalancer yourself.

<a id="be-d467"></a>

#### Separating OLTP and analytics

The two workloads are mechanically incompatible on one instance: OLTP wants many small indexed reads with a hot cache; analytics wants full scans of a few columns over everything. Run both and the scans evict the working set, and every transactional query slows down.

|               | OLTP                             | OLAP                                         |
|---------------|----------------------------------|----------------------------------------------|
| Query shape   | Point reads and writes, few rows | Scans and aggregates, many rows, few columns |
| Concurrency   | Thousands of short queries       | Few long queries                             |
| Storage       | Row store                        | Column store                                 |
| Normalisation | 3NF                              | Star or snowflake schema                     |
| Freshness     | Immediate                        | Minutes to hours is usually fine             |
| Indexes       | Many, narrow                     | Few — scans dominate                         |

``` text
  the progression, cheapest first

  1  a reporting REPLICA                    minutes of work, solves most cases
  2  materialised views / summary tables    precomputed aggregates
  3  a COLUMNSTORE index on the same table  SQL Server; hybrid, one system
  4  a separate WAREHOUSE                   Snowflake, BigQuery, Redshift, ClickHouse
       fed by CDC or scheduled ETL
  5  a full star schema with dimensions     when BI is a product, not a side concern
```

``` sql
-- a star schema, for reference: one wide fact table, narrow dimensions
CREATE TABLE fact_sales (
    date_key     int    NOT NULL REFERENCES dim_date(date_key),
    product_key  int    NOT NULL REFERENCES dim_product(product_key),
    customer_key int    NOT NULL REFERENCES dim_customer(customer_key),
    quantity     int    NOT NULL,
    net_amount   numeric(19,4) NOT NULL
);
-- dimensions are deliberately DENORMALISED (dim_product holds category, brand,
-- supplier as columns, not as joins) because a star schema optimises for scan-and-group,
-- not for update anomalies. The rules from Part 2 are relaxed on purpose here.

-- slowly changing dimension type 2: keep history by versioning dimension rows
CREATE TABLE dim_product (
    product_key  int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,  -- surrogate, per version
    product_id   bigint NOT NULL,                               -- the stable business id
    name         text NOT NULL,
    category     text NOT NULL,
    valid_from   date NOT NULL,
    valid_to     date,
    is_current   boolean NOT NULL
);
-- so a fact row from 2024 joins to the 2024 version of the product,
-- and a rename in 2026 does not rewrite historic reports.
```

> **\[Note\]**
>
> **Start with a replica.** Adding a dedicated reporting replica takes an afternoon and resolves the majority of “analytics is slowing down the application” problems. A warehouse is the right answer when data volume, query complexity or the need to join across several source systems genuinely demands it — not as the first move.

### Specialised & Advanced

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Specialised & Advanced</p>

<p class="pd">Capabilities beyond ordinary CRUD, plus the engineering practice around databases: procedural code, search, spatial and time-series data, change data capture, migrations, and a method for tuning.</p>

<a id="be-d474"></a>

#### Stored procedures, functions and triggers

Code that runs inside the database. Powerful, and the source of some of the most enduring architectural arguments in the industry.

| Object            | Returns                    | Can commit?            | Typical use                                              |
|-------------------|----------------------------|------------------------|----------------------------------------------------------|
| Scalar function   | One value                  | No                     | Reusable computation — watch the per-row cost            |
| Table function    | A result set               | No                     | Parameterised views, generators                          |
| Procedure         | Nothing, or out parameters | Yes                    | Batch jobs, multi-step maintenance                       |
| Trigger           | —                          | Within the transaction | Enforcing invariants, auditing, maintaining derived data |
| View              | A query                    | —                      | Encapsulation, security boundary                         |
| Materialised view | A stored result            | —                      | Precomputed aggregates                                   |

**Good reasons to put logic in the database**

- Set-based operations over large volumes — avoids moving data
- Invariants that must hold for every writer
- Auditing that must not be bypassable
- Maintaining derived columns correctly
- Batch maintenance close to the data

**Good reasons not to**

- Version control, testing and code review are weaker
- Debugging and profiling are harder
- Logic split across two languages and two repositories
- Difficult to scale horizontally — it runs on the database's CPU
- Triggers are invisible: nothing in the calling code mentions them

``` sql
-- the trigger trade-off, concretely
CREATE FUNCTION audit_orders() RETURNS trigger AS $$
BEGIN
    INSERT INTO audit_log (actor, action, entity, entity_id, before, after)
    VALUES (current_setting('app.actor', true), TG_OP, 'order',
            COALESCE(NEW.id, OLD.id)::text, to_jsonb(OLD), to_jsonb(NEW));
    RETURN NULL;                       -- AFTER trigger: the return value is ignored
END $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_audit_orders
AFTER INSERT OR UPDATE OR DELETE ON orders
FOR EACH ROW EXECUTE FUNCTION audit_orders();

-- upside:  EVERY writer is audited - the application, a migration, a console session.
-- downside: nobody reading the application code knows this happens, and a bulk
--           update of 5 million rows now writes 5 million audit rows inside the
--           same transaction.
```

> **\[Trap\]**
>
> **Common trap.** A row-level trigger on a bulk operation multiplies its cost invisibly. A 5-million-row update becomes 5 million function invocations plus 5 million inserts, in one transaction, holding one enormous set of locks. Use statement-level triggers where the semantics allow, disable triggers deliberately for controlled bulk loads, and always test bulk paths against a table that has its production triggers.

<a id="be-d475"></a>

#### Views and encapsulation

A view is a stored query. Used well it is a stable interface over a schema you want to change; used carelessly it is a performance trap that hides its own cost.

| Kind                                           | Stored                      | Updatable | Cost                                           |
|------------------------------------------------|-----------------------------|-----------|------------------------------------------------|
| Simple view                                    | The query text              | Often yes | Inlined into the calling query — usually free  |
| Complex view (aggregates, `DISTINCT`, windows) | The query text              | No        | May block predicate pushdown                   |
| Materialised view                              | The result set              | No        | Cheap reads, stale data, refresh cost          |
| Indexed view                                   | The result, maintained live | —         | SQL Server; write amplification on base tables |

``` sql
-- views as a stable contract, so the physical schema can change underneath
CREATE VIEW v_customer_summary AS
SELECT c.id, c.name, c.country,
       count(o.id)            AS order_count,
       COALESCE(sum(o.total), 0) AS lifetime_value,
       max(o.placed_at)       AS last_order_at
FROM   customers c
LEFT   JOIN orders o ON o.customer_id = c.id AND o.status <> 'Cancelled'
GROUP  BY c.id, c.name, c.country;

-- reporting tools and read-only roles are granted access to VIEWS only.
-- the base tables can then be split, renamed or partitioned without breaking them.
GRANT SELECT ON v_customer_summary TO analyst;
```

> **\[Trap\]**
>
> **Common trap.** Views stacked on views are the classic hidden performance problem: a five-level stack looks like `SELECT * FROM v_final WHERE id = 5` and expands into a query joining fourteen tables and computing three aggregates that are then discarded. Always `EXPLAIN` a query against a view before assuming it is cheap, and be suspicious of any view whose definition references another view.

<a id="be-d476"></a>

#### Spatial data

Geography is a first-class relational capability, and it is dramatically better than storing latitude and longitude as two numbers.

``` sql
CREATE EXTENSION postgis;

CREATE TABLE stores (
    id       bigint PRIMARY KEY,
    name     text NOT NULL,
    location geography(Point, 4326) NOT NULL     -- 4326 = WGS84 lat/long
);
CREATE INDEX ix_stores_location ON stores USING gist (location);

-- "the 10 nearest stores within 5 km" - one indexed query
SELECT id, name,
       ST_Distance(location, :point) AS metres
FROM   stores
WHERE  ST_DWithin(location, :point, 5000)        -- uses the GiST index
ORDER  BY location <-> :point                    -- KNN operator, also indexed
LIMIT  10;

-- and the things that are painful without a spatial type
SELECT * FROM delivery_zones WHERE ST_Contains(area, :point);      -- point in polygon
SELECT ST_Area(area::geography) / 1e6 AS sq_km FROM delivery_zones;
SELECT ST_Intersection(a.area, b.area) FROM zones a, zones b WHERE ST_Intersects(a.area, b.area);
```

| Type        | Model                | Use                                                          |
|-------------|----------------------|--------------------------------------------------------------|
| `geometry`  | Flat Cartesian plane | Fast; correct only over small areas or with a projected SRID |
| `geography` | Spherical earth      | Correct distances globally; slower                           |

The naive alternative — a bounding-box query on separate latitude and longitude columns — is approximately right near the equator and increasingly wrong toward the poles, cannot express containment or intersection, and cannot be indexed for nearest-neighbour ordering. Every one of those is a GiST index away.

<a id="be-d477"></a>

#### Time-series data in a relational database

Metrics, sensor readings, prices, event counts. The shape is distinctive: append-only, ordered by time, written far more often than updated, queried in ranges, and almost never needed at full resolution once it is old.

``` sql
-- Partition by time so old data can be dropped in O(1) and queries touch
-- only the partitions in range.
CREATE PARTITION FUNCTION pfMonthly (datetime2) AS RANGE RIGHT
  FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01');

CREATE TABLE Readings (
  SensorId int NOT NULL,
  At datetime2 NOT NULL,
  Value float NOT NULL,
  CONSTRAINT PK_Readings PRIMARY KEY (SensorId, At)   -- key order matters: see below
) ON psMonthly(At);
```

**Key order is the design decision.** `(SensorId, At)` makes "this sensor over a range" a single seek — the common query. `(At, SensorId)` makes "everything in this hour" fast and makes per-sensor history a scan. Choose by the query you run most; you cannot have both from one index without paying for a second.

``` sql
-- Downsampling: keep raw data briefly, rolled-up data for a long time.
INSERT INTO ReadingsHourly (SensorId, Hour, Avg, Min, Max, Count)
SELECT SensorId, DATETRUNC(hour, At), AVG(Value), MIN(Value), MAX(Value), COUNT(*)
FROM Readings
WHERE At >= @from AND At < @to
GROUP BY SensorId, DATETRUNC(hour, At);

-- Retention becomes a partition switch, not a DELETE:
ALTER TABLE Readings SWITCH PARTITION 1 TO ReadingsArchive PARTITION 1;
```

``` sql
-- Gap filling and rate-of-change are window-function work (§185):
SELECT SensorId, At, Value,
       Value - LAG(Value) OVER (PARTITION BY SensorId ORDER BY At)      AS Delta,
       DATEDIFF(second, LAG(At) OVER (PARTITION BY SensorId ORDER BY At), At) AS GapSeconds,
       AVG(Value) OVER (PARTITION BY SensorId ORDER BY At
                        ROWS BETWEEN 9 PRECEDING AND CURRENT ROW)       AS Sma10
FROM Readings WHERE At >= @from;
```

> **\[Tip\]**
>
> **Store aggregates, not raw data, past a short window.** Nobody queries second-resolution data from eight months ago, but everyone keeps it "just in case" until the table is the largest object in the database. Decide the retention ladder up front — raw for 7 days, hourly for 90, daily for 5 years — and implement it as a job on day one, not after the disk alert ([Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)).

> **\[Trap\]**
>
> **Approximation is usually acceptable here, and it is worth using.** Percentiles from a merged histogram, distinct counts from HyperLogLog, and heavy hitters from a sketch all give bounded-memory answers over arbitrary ranges, which exact computation cannot at this volume ([Quantiles and histograms](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m130), [Approximate distinct counts](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m127)). A purpose-built time-series store does all of this for you; adopt one when partitioning plus rollups stops being enough.

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

<a id="be-d478"></a>

#### Change data capture

Reading the transaction log turns every committed change into an event stream, with no application modification at all. It is the pragmatic way to get data out of a system you cannot change.

``` text
  application -> database -> WAL / transaction log
                                |
                        [ logical decoding / CDC ]
                                |
                          a stream of row changes
                                |
                +---------------+---------------+
                v               v               v
           search index    read model     data warehouse

  the application knows nothing about any of it.
```

``` sql
-- PostgreSQL logical replication is CDC with no extra infrastructure
CREATE PUBLICATION cdc_orders FOR TABLE orders, order_lines;

-- consumers subscribe, or a connector (Debezium) reads the replication slot
-- and publishes to Kafka.

-- REPLICA IDENTITY controls how much the log records for UPDATE and DELETE
ALTER TABLE orders REPLICA IDENTITY FULL;   -- log the whole OLD row, not just the key
-- default is the primary key only, which means consumers see the new values
-- and the key, but not what the previous values were.
```

**CDC**

- No application change — works on legacy
- Cannot miss a change; the log is authoritative
- Captures before and after images
- **Exposes your schema as the contract**
- Events are row-shaped, not business-shaped

**An application outbox**

- Events are intentional and business-shaped
- The schema stays private
- Requires modifying the application
- You control the payload and its versioning
- The right choice for systems you own

| Constraint                       | Detail                                                                    |
|----------------------------------|---------------------------------------------------------------------------|
| Schema is the contract           | A column rename breaks every consumer — mitigate with a translation layer |
| Bulk operations flood the stream | A one-line `UPDATE` touching 5M rows emits 5M events                      |
| Initial snapshot                 | The first run reads the whole table; plan for it                          |
| Slot retention                   | An unconsumed replication slot prevents WAL cleanup until the disk fills  |
| DDL is not replicated            | Schema changes must be coordinated out of band                            |
| No business intent               | You get ‘status changed 2 → 3’, not ‘order shipped’                       |

> **\[Trap\]**
>
> **Common trap.** The stalled replication slot is one of the most common ways a healthy PostgreSQL instance goes down: a consumer stops, WAL accumulates because the slot still needs it, and the disk fills. Monitor every slot's lag, alert on growth, and drop slots nobody is reading.

<a id="be-d481"></a>

#### Vector search in a relational database

Embedding models turn text, images or audio into vectors where geometric closeness means semantic similarity. Since `pgvector` became mature, doing this inside PostgreSQL is the default starting point rather than a compromise — and it keeps the vectors transactionally consistent with the rows they describe.

``` sql
CREATE EXTENSION vector;

CREATE TABLE doc_chunks (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id bigint NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    tenant_id   uuid   NOT NULL,                 -- a real column: it is a SECURITY filter
    chunk_no    int    NOT NULL,
    content     text   NOT NULL,
    embedding   vector(1536) NOT NULL,           -- dimension is part of the type
    UNIQUE (document_id, chunk_no)
);

-- HNSW: the index you almost always want. Fast, high recall, incremental.
CREATE INDEX ix_chunks_hnsw ON doc_chunks
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- and index whatever you filter on, because the filter runs alongside the search
CREATE INDEX ix_chunks_tenant ON doc_chunks (tenant_id);
```

| Index        | Build                          | Recall                 | Memory | Use                                                       |
|--------------|--------------------------------|------------------------|--------|-----------------------------------------------------------|
| **HNSW**     | Slow, incremental              | High                   | Higher | The default. Handles inserts well                         |
| **IVFFlat**  | Fast, needs data present first | Good, tuning-sensitive | Lower  | Very large static corpora; must be rebuilt as data shifts |
| None (exact) | —                              | Perfect                | None   | Under ~50k rows, a brute-force scan is genuinely fine     |

``` csharp
-- the distance operators. The index is built for ONE of them - match them or it is unused.
SELECT id, content, embedding <=> :query_vec AS distance   -- <=> cosine
FROM   doc_chunks
WHERE  tenant_id = :tenant                                  -- filter, not optional
ORDER  BY embedding <=> :query_vec                          -- must match the index opclass
LIMIT  10;

--  <->  L2 / Euclidean      (vector_l2_ops)
--  <=>  cosine distance     (vector_cosine_ops)      <- usual for text embeddings
--  <#>  negative inner product (vector_ip_ops)

-- recall/speed dial at query time
SET LOCAL hnsw.ef_search = 100;      -- higher = better recall, slower. Default 40.
```

###### Filtered vector search: the part that bites

``` text
  the problem with combining a filter and an ANN index

  approximate nearest neighbour indexes search the GRAPH, then the filter is applied.
  ask for the 10 nearest, filter to one tenant, and you may get 0 rows back -
  because all 10 global nearest belonged to other tenants.

  three strategies

  1  OVER-FETCH then filter        ORDER BY ... LIMIT 200, filter in an outer query
                                   simple; recall degrades badly for selective filters

  2  PARTITIONED / partial indexes  one HNSW index per tenant, or per partition
                                    excellent recall; impractical past a few hundred tenants

  3  ITERATIVE scan (pgvector 0.8+)  the index keeps producing candidates until
                                     enough pass the filter
                                     SET hnsw.iterative_scan = strict_order;
                                     -> the correct answer in most cases
```

``` csharp
-- strategy 3, which is what you generally want
SET LOCAL hnsw.iterative_scan = strict_order;
SET LOCAL hnsw.max_scan_tuples = 20000;        -- bound the work

SELECT id, content
FROM   doc_chunks
WHERE  tenant_id = :tenant AND document_id = ANY(:allowed_docs)
ORDER  BY embedding <=> :query_vec
LIMIT  10;

-- strategy 2, for a small number of large tenants: partition, and index each partition
CREATE TABLE doc_chunks (...) PARTITION BY LIST (tenant_id);
```

> **\[Trap\]**
>
> **Common trap.** The tenant filter in a vector query is a **security control**, not an optimisation. Vector search returns the *nearest* chunks, so without a filter the nearest chunk to a question may belong to another customer — and in a retrieval-augmented generation pipeline that text is then quoted verbatim in an answer. Treat the vector table exactly like any other multi-tenant table: enforce the filter, ideally with row-level security, and write a test that proves it.

###### Hybrid search, and operational realities

``` csharp
-- pure vector search is surprisingly bad at exact identifiers, product codes and names -
-- precisely what users search for. Combine it with keyword search and re-rank.
WITH semantic AS (
    SELECT id, row_number() OVER (ORDER BY embedding <=> :qv) AS rank
    FROM   doc_chunks WHERE tenant_id = :t
    ORDER  BY embedding <=> :qv LIMIT 50
),
keyword AS (
    SELECT id, row_number() OVER (ORDER BY ts_rank(search_vector, q) DESC) AS rank
    FROM   doc_chunks, websearch_to_tsquery('english', :query) q
    WHERE  tenant_id = :t AND search_vector @@ q
    ORDER  BY ts_rank(search_vector, q) DESC LIMIT 50
)
SELECT c.id, c.content,
       COALESCE(1.0/(60 + s.rank), 0) + COALESCE(1.0/(60 + k.rank), 0) AS rrf_score
FROM   doc_chunks c
LEFT   JOIN semantic s ON s.id = c.id
LEFT   JOIN keyword  k ON k.id = c.id
WHERE  s.id IS NOT NULL OR k.id IS NOT NULL
ORDER  BY rrf_score DESC LIMIT 10;
-- reciprocal rank fusion: no score normalisation needed, and it works well in practice.
```

| Operational fact                           | Consequence                                                                                       |
|--------------------------------------------|---------------------------------------------------------------------------------------------------|
| The embedding model is part of your schema | Change models and every stored vector is meaningless. Version the collection by model and reindex |
| 1536 dimensions is ~6 KB per row           | A million chunks is ~6 GB of vectors before indexing. Consider `halfvec` or dimension reduction   |
| HNSW index build is slow                   | Set `maintenance_work_mem` high; build with parallel workers; expect hours on large tables        |
| Index must fit in memory for good latency  | If it spills, query time rises by an order of magnitude                                           |
| Recall is not correctness                  | ANN is approximate by definition. Measure recall against exact search on a sample                 |
| Chunking strategy dominates quality        | More than the index or the model. Overlap chunks; keep them semantically whole                    |

> **\[Note\]**
>
> **Start here, move later.** `pgvector` in your existing PostgreSQL handles corpora into the millions of chunks with good latency, keeps vectors consistent with their source rows in one transaction, and lets you filter and join relationally. Move to Qdrant, Milvus or a managed vector service when you have measured a specific limit — not because a dedicated store sounds more appropriate.

<a id="be-d482"></a>

#### The extension ecosystem

Extensibility is arguably the main reason PostgreSQL displaced its competitors: capabilities that would be a separate product elsewhere are an `CREATE EXTENSION` away, inside the same transaction boundary and the same backup.

| Extension            | Adds                                            | Replaces                                |
|----------------------|-------------------------------------------------|-----------------------------------------|
| `pg_stat_statements` | Query-level statistics                          | Guesswork                               |
| `auto_explain`       | Automatic plan logging for slow queries         | Reproducing the problem manually        |
| `pg_trgm`            | Trigram similarity and indexable `LIKE '%x%'`   | A search engine, for fuzzy matching     |
| `PostGIS`            | Full geospatial types, indexes and functions    | A dedicated GIS database                |
| `pgvector`           | Vector types and ANN indexes                    | A vector database                       |
| `TimescaleDB`        | Hypertables, continuous aggregates, compression | A time-series database                  |
| `pg_partman`         | Automated partition creation and retention      | A cron job you forgot to write          |
| `pg_repack`          | Compact a table without a long exclusive lock   | `VACUUM FULL` downtime                  |
| `pg_cron`            | In-database scheduling                          | An external scheduler for database jobs |
| `hypopg`             | Hypothetical indexes                            | Building a 400 GB index to find out     |
| `Citus`              | Sharding and distributed query                  | A separate sharding layer               |
| `postgres_fdw`       | Query a remote PostgreSQL as if local           | An ETL job, sometimes                   |
| `pgcrypto`           | Encryption and hashing functions                | Application-side crypto, sometimes      |
| `pgaudit`            | Configurable audit logging                      | Logging every statement                 |

``` sql
-- foreign data wrappers: query another system as if it were a local table
CREATE EXTENSION postgres_fdw;
CREATE SERVER analytics FOREIGN DATA WRAPPER postgres_fdw
    OPTIONS (host 'warehouse.internal', dbname 'analytics');
CREATE USER MAPPING FOR app_service SERVER analytics
    OPTIONS (user 'reader', password '...');
IMPORT FOREIGN SCHEMA public LIMIT TO (dim_product) FROM SERVER analytics INTO ext;

SELECT o.id, p.category
FROM   orders o JOIN ext.dim_product p ON p.product_id = o.product_id;
-- predicates and joins are pushed to the remote server where possible.
-- there are also FDWs for MySQL, Oracle, SQL Server, S3/Parquet and CSV files.
```

``` sql
-- pg_cron: schedule maintenance where the data is
SELECT cron.schedule('nightly-analyze', '30 3 * * *', 'ANALYZE');
SELECT cron.schedule('purge-audit', '0 4 * * *', $$
    DELETE FROM audit_log WHERE occurred_at < now() - interval '2 years'
$$);
SELECT cron.schedule('refresh-mv', '*/15 * * * *',
    'REFRESH MATERIALIZED VIEW CONCURRENTLY mv_daily_revenue');

SELECT * FROM cron.job_run_details ORDER BY start_time DESC LIMIT 20;   -- and monitor it
```

###### Choosing extensions responsibly

| Question                                    | Why it matters                                                                           |
|---------------------------------------------|------------------------------------------------------------------------------------------|
| Is it available on my managed platform?     | RDS, Azure and Cloud SQL each allow a specific list. Check *before* designing around one |
| Who maintains it, and how actively?         | An abandoned extension can block a major-version upgrade for years                       |
| Does it require `shared_preload_libraries`? | That means a restart to add or remove it                                                 |
| What is its upgrade story?                  | Some extensions must be updated in lockstep with the server version                      |
| What is the licence?                        | TimescaleDB's advanced features and some others are not open source                      |
| Does it change the on-disk format?          | If so, removing it later is not simply a `DROP`                                          |

``` sql
-- what is installed, what is available, and what needs updating
SELECT extname, extversion FROM pg_extension ORDER BY 1;
SELECT name, default_version, installed_version, comment
FROM   pg_available_extensions WHERE installed_version IS NOT NULL;
ALTER EXTENSION pgvector UPDATE;
```

> **\[Trap\]**
>
> **Common trap.** Extension availability is the most common reason a design that works locally cannot be deployed. A developer builds on `pgvector` and `pg_cron` in Docker, and the managed instance permits neither — or permits an older version without the feature relied upon. Verify the platform's extension list and versions at design time, not at deployment.

<a id="be-d483"></a>

#### A tuning methodology

A repeatable order of operations, so that tuning is investigation rather than guesswork.

1.  **Define the problem numerically.** “The database is slow” is not actionable. “p99 on the order list endpoint is 4.2 s against a 300 ms target” is.
2.  **Find the top queries by TOTAL time.** Not by mean: a 20 ms query called two million times an hour costs far more than a four-second nightly report.
3.  **Get the plan** for the worst offender with `EXPLAIN (ANALYZE, BUFFERS)`.
4.  **Compare estimated to actual rows at every node.** The lowest sharp divergence is the root cause.
5.  **Fix in this order:** statistics, then the query, then the index, then the schema, then the configuration, then the hardware. The cost of each step rises sharply.
6.  **Measure again** against the metric from step 1 — not just the query's own timing.
7.  **Write down what you found.** The next person to hit it will be you, in eighteen months, having forgotten.

| Symptom                        | Look at first                                                |
|--------------------------------|--------------------------------------------------------------|
| One query slow, always         | Its plan — index or rewrite                                  |
| One query slow sometimes       | Parameter sniffing, or lock waits                            |
| Everything slow at once        | Locks, a long transaction, or saturated I/O                  |
| Slow only under load           | Connection pool exhaustion, lock contention                  |
| Gradually degrading over weeks | Bloat, stale statistics, unbounded growth                    |
| Slow after a deploy            | A new query, a changed plan, a missing index on a new column |
| Slow at a fixed time daily     | A batch job evicting the buffer pool                         |

> **\[Note\]**
>
> **The habit that matters most.** Look at the plan before forming a theory. Most time lost to database performance work is spent testing hypotheses that thirty seconds of `EXPLAIN ANALYZE` would have eliminated.

### The Modern Landscape

<p class="src">From <em>Backend Engineering</em> · Relational Databases · The Modern Landscape</p>

<p class="pd">How databases are actually run in 2026. Managed services, separated storage and compute, serverless and branching, distributed SQL, and the analytics stack that has moved out from under the OLTP database.</p>

<a id="be-d484"></a>

#### Managed services: what changes and what does not

Most databases now run as a managed service. That changes the operational surface substantially and changes the fundamentals not at all — which is the point worth being precise about, because both halves get overstated.

| Concern                         | Self-managed                       | Managed                                            |
|---------------------------------|------------------------------------|----------------------------------------------------|
| Patching and minor upgrades     | Yours                              | Provider's, in a maintenance window you choose     |
| Major version upgrades          | Yours, with full control           | Provider-assisted; you still test and schedule it  |
| Backups and PITR                | You configure and verify           | Configured for you — **still verify the restore**  |
| High availability and failover  | You build it (Patroni, clustering) | A checkbox, with the provider's semantics          |
| Replication                     | You configure                      | Managed read replicas, often cross-region          |
| Configuration                   | Everything                         | A permitted subset — parameter groups              |
| OS access                       | Yes                                | **No** — no shell, no `perf`, no log files on disk |
| Superuser                       | Yes                                | **No** — a semi-privileged role instead            |
| Extensions                      | Anything you compile               | An allow-list                                      |
| Schema, queries, indexes, plans | Yours                              | **Still entirely yours**                           |

That last row is the important one. Managed services remove operational toil; they do not fix a missing index, an N+1 query, a lock-ordering deadlock or a table with no partitioning strategy. Every part of this guide from schema design through query optimisation applies unchanged — and matters *more*, because you can no longer compensate with a kernel tweak or a custom extension.

###### What you lose, and how to work around it

| Missing capability                    | Workaround                                                                                 |
|---------------------------------------|--------------------------------------------------------------------------------------------|
| No OS access for diagnostics          | Rely on `pg_stat_*` views, Performance Insights, Query Store                               |
| No superuser                          | Most tasks have a granted alternative (`rds_superuser`, etc.); some are simply unavailable |
| Cannot read log files directly        | Ship logs to the provider's log service and query them there                               |
| Restricted `shared_preload_libraries` | Check the allow-list before designing around an extension                                  |
| No control over the OS page cache     | Size the instance so the buffer pool dominates                                             |
| Failover semantics are the provider's | Test them — trigger a failover in staging and time it                                      |
| Version lag behind upstream           | Plan features against what your provider offers, not the release notes                     |

- **Verify backups yourself.** The provider guarantees the backup exists, not that your restore procedure works or that the restored instance has the roles, extensions and sequence values you need.
- **Test the failover.** A managed failover still drops connections, so your pool must handle it. Trigger one deliberately and watch what the application does.
- **Read the parameter group.** Provider defaults are conservative and generic; `work_mem`, autovacuum settings and connection limits usually need adjusting for your workload.
- **Watch the connection limit.** It is often derived from instance size, and it is lower than people expect. A pooler is not optional.
- **Budget for I/O, not just compute.** Provisioned IOPS and throughput limits cause latency cliffs that look exactly like query problems.

<a id="be-d485"></a>

#### Separated storage and compute

The architectural change behind Aurora, AlloyDB, Neon, Azure SQL Hyperscale and Cloud Spanner: the storage layer becomes a distributed, multi-tenant service, and the database engine becomes a stateless-ish compute node on top of it.

``` text
  CLASSIC                              SEPARATED

  +---------------------+              +----------+  +----------+  +----------+
  |  engine             |              | writer   |  | reader   |  | reader   |
  |  buffer pool        |              | compute  |  | compute  |  | compute  |
  |  WAL + data files   |              +-----+----+  +-----+----+  +-----+----+
  +----------+----------+                    |             |             |
             |                               +------+------+------+------+
        local disk                                  |
                                        +-----------v------------+
                                        |  distributed storage    |
                                        |  6 copies / 3 AZs       |
                                        |  applies the log itself |
                                        +-------------------------+

  the writer ships LOG RECORDS, not pages. Storage nodes apply them.
  replicas read the same storage, so replica creation is near-instant
  and replication lag is milliseconds rather than a stream to catch up on.
```

| Consequence                         | Effect                                                                            |
|-------------------------------------|-----------------------------------------------------------------------------------|
| Replicas share storage              | Adding a read replica takes minutes and costs no extra storage                    |
| Failover is fast                    | The new writer attaches to existing storage — no data to copy                     |
| Storage grows automatically         | No volume resizing; you pay per GB used                                           |
| Backups are continuous              | PITR to any second, implemented in the storage layer                              |
| Write amplification is lower        | Only log records cross the network, not full pages                                |
| **Latency per operation is higher** | Storage is a network hop — commit latency is a few milliseconds, not microseconds |
| Some knobs disappear                | Checkpoint tuning largely stops being your concern                                |

The trade to internalise: these systems are dramatically better at *availability, elasticity and operational simplicity*, and they are not faster per transaction than a well-tuned instance with local NVMe. For a workload dominated by tiny low-latency transactions, a single well-provisioned machine can still win on raw latency. For almost everything else, the operational properties dominate.

###### What it means for how you write code

- **Commit latency is a network round trip.** Chatty transactions with many small commits suffer more than on local storage — batch where you can.
- **Read replicas become cheap and plentiful**, so routing reads deliberately pays off more than it used to.
- **Replica lag is usually milliseconds**, which makes read-your-writes less painful — but it is still not zero, so the patterns from the replication section still apply.
- **Cold caches are more expensive.** A newly promoted or newly scaled compute node has an empty buffer pool and every miss is a network read. Warm-up matters more.
- **Sequential-scan-heavy analytics is penalised differently.** Some platforms parallelise scans into the storage layer, which is a genuine advantage; others do not.

<a id="be-d486"></a>

#### Serverless databases and branching

Two related developments that have changed the development workflow more than the production one: compute that scales to zero and bills by usage, and copy-on-write branches of a whole database.

| Platform               | Model                                            | Notable for                                              |
|------------------------|--------------------------------------------------|----------------------------------------------------------|
| Aurora Serverless v2   | Scales capacity units in seconds                 | Mature; scales up and down without disconnecting         |
| Neon                   | PostgreSQL, storage/compute split, scale to zero | Instant database branching                               |
| PlanetScale            | MySQL/Vitess                                     | Branching plus deploy requests with schema-change review |
| Azure SQL Serverless   | Auto-pause and resume                            | Integrated with the Azure ecosystem                      |
| CockroachDB Serverless | Distributed SQL, usage-based                     | Horizontal scale without managing nodes                  |
| Supabase / Xata        | Managed PostgreSQL plus an API layer             | Application-oriented developer experience                |

``` text
  DATABASE BRANCHING - the workflow change that matters

  main  ----o-------------o------------o---->   production data
             \             \
   feature-a   o--o--o       \               a branch: copy-on-write, created in seconds
   feature-b                  o--o--o        with PRODUCTION-SHAPED data, not a seed script

  each pull request gets a database branch:
    migrations run against realistic data volumes
    integration tests hit a real schema with real cardinalities
    a bad migration destroys a branch, not an environment
    the branch is deleted when the PR merges

  this is the ephemeral-environment pattern, applied to the hardest part of it.
```

``` yaml
# a CI job per pull request, with a real database branch
- name: Create database branch
  run: |
    BRANCH=$(neon branches create --name "pr-${{ github.event.number }}" \
             --parent main --output json | jq -r '.branch.id')
    echo "DATABASE_URL=$(neon connection-string $BRANCH)" >> $GITHUB_ENV

- name: Run migrations against production-shaped data
  run: flyway migrate                 # a lock-heavy migration FAILS here, not in production

- name: Integration tests
  run: dotnet test

- name: Delete branch
  if: always()
  run: neon branches delete "pr-${{ github.event.number }}"
```

###### The constraints of serverless compute

| Constraint                 | Detail                                                                        | Mitigation                                                                           |
|----------------------------|-------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| **Cold start**             | A scaled-to-zero instance takes hundreds of milliseconds to seconds to resume | Keep a minimum capacity for latency-sensitive workloads                              |
| **Connection storms**      | Serverless *application* functions each open connections                      | A pooler, a data proxy, or an HTTP data API                                          |
| **Cost unpredictability**  | A runaway query or a scan-heavy report bills directly                         | Alerts on spend; `statement_timeout`; capacity ceilings                              |
| **Scaling is not instant** | Capacity ramps over seconds                                                   | A traffic spike may be throttled before it scales                                    |
| **Vendor coupling**        | Branching, autoscaling and data APIs are proprietary                          | Keep the SQL standard; treat the platform features as operational, not architectural |

The serverless-application-plus-database connection problem deserves emphasis because it is so common: a thousand concurrent function invocations each opening a connection will exhaust any database. The answers are an external pooler in transaction mode, a provider-supplied proxy, or an HTTP-based data API that multiplexes for you. Application-level pooling does not help, because each function instance has its own pool of one.

<a id="be-d487"></a>

#### Distributed SQL

A category that barely existed a decade ago and is now a mainstream choice: horizontal write scaling and multi-region availability while keeping SQL, joins and ACID transactions. The cost is latency and a genuinely different performance model.

| System                              | Lineage                                           | Notes                                               |
|-------------------------------------|---------------------------------------------------|-----------------------------------------------------|
| Google Spanner                      | The original                                      | TrueTime with atomic clocks; external consistency   |
| CockroachDB                         | Spanner-inspired, PostgreSQL wire protocol        | Hybrid logical clocks; strong serialisable default  |
| YugabyteDB                          | PostgreSQL query layer over a distributed store   | High PostgreSQL compatibility, including extensions |
| TiDB                                | MySQL protocol, with a columnar replica (TiFlash) | Genuine HTAP — OLTP and OLAP on one system          |
| Vitess / PlanetScale                | MySQL sharding layer                              | Battle-tested at very large scale                   |
| Citus                               | PostgreSQL extension                              | Sharding inside PostgreSQL; strong for multi-tenant |
| Aurora Limitless / Azure Hyperscale | Managed sharding                                  | Cloud-native, reduced operational burden            |

``` text
  HOW A DISTRIBUTED TRANSACTION COMMITS

  data is split into RANGES, each replicated (usually 3 or 5 copies) via RAFT.
  each range has a leader; writes go to the leader and are acknowledged by a QUORUM.

  a transaction touching ranges A and B:

    1. write intents on the rows in both ranges          (provisional)
    2. each intent replicated to a quorum of its range   (a network round trip each)
    3. a transaction record is committed atomically      (another round trip)
    4. intents are resolved asynchronously into real values

  so a multi-range transaction costs SEVERAL consensus round trips.
  same-datacentre: ~1-3 ms.  Cross-region: 50-150 ms PER ROUND TRIP.

  -> the performance model is dominated by how many ranges you touch
     and how far apart their leaders are.
```

| What changes about how you design          | Why                                                                                                           |
|--------------------------------------------|---------------------------------------------------------------------------------------------------------------|
| **Locality is a schema decision**          | Co-locate rows accessed together in the same range — interleave child tables under the parent                 |
| **Sequential keys are an anti-pattern**    | A monotonic key makes one range the write hotspot for the entire cluster. Use UUIDs or hash-sharded sequences |
| **Cross-range transactions are expensive** | Design so a typical transaction touches one range, usually one tenant                                         |
| **Follower reads are a feature**           | Slightly stale reads served locally, avoiding a cross-region hop                                              |
| **Retries are mandatory**                  | Serialisable isolation aborts on conflict; every transaction needs a retry loop                               |
| **Regional tables and row-level homing**   | Pin data to a region for latency and for residency compliance                                                 |

``` sql
-- CockroachDB: co-locate children with their parent so a per-order transaction
-- stays within one range
CREATE TABLE orders (
    tenant_id uuid, id uuid DEFAULT gen_random_uuid(),
    PRIMARY KEY (tenant_id, id)                  -- tenant first: co-locates a tenant's data
);
CREATE TABLE order_lines (
    tenant_id uuid, order_id uuid, line_no int,
    PRIMARY KEY (tenant_id, order_id, line_no),
    CONSTRAINT fk FOREIGN KEY (tenant_id, order_id) REFERENCES orders (tenant_id, id)
) INTERLEAVE IN PARENT orders (tenant_id, order_id);

-- avoid a monotonic hotspot on a sequence
CREATE TABLE events (
    id bigint DEFAULT unique_rowid(),            -- not monotonic across nodes
    ...
);
-- or hash-shard an index that would otherwise be append-only
CREATE INDEX ON events (occurred_at) USING HASH WITH (bucket_count = 16);

-- stale-but-local reads, avoiding a cross-region consensus hop
SELECT * FROM orders AS OF SYSTEM TIME follower_read_timestamp() WHERE tenant_id = $1;
```

###### When distributed SQL is and is not the answer

**Choose it when**

- Writes genuinely exceed one machine
- Multi-region availability is a requirement
- Data residency demands per-region homing
- You would otherwise hand-roll sharding
- Zero-downtime for zonal failure is required

**Stay single-node when**

- Reads dominate — replicas are simpler and faster
- Per-transaction latency is critical
- The data fits comfortably on one machine (which is a lot — tens of terabytes)
- Your workload is join-heavy analytics
- The team has no appetite for a new performance model

A single modern instance with fast NVMe handles a very large workload — tens of thousands of transactions per second and many terabytes. Most systems that adopt distributed SQL do so for availability, geography or regulatory reasons rather than because one node could not cope. Being clear about which reason applies to you is the difference between a good decision and cargo-culting Spanner.

> **\[Trap\]**
>
> **Common trap.** Distributed SQL does not make bad schemas fast. A missing index is still a missing index, and it is now a missing index that requires a distributed scan across every node. The fundamentals matter more here, not less — and diagnosing them is harder because a plan spans machines.

<a id="be-d488"></a>

#### The analytics stack has moved out

A decade ago analytics ran on a replica of the transactional database. Now there is a distinct stack, and knowing its shape matters because the boundary is where your data engineering obligations begin.

``` text
  the modern shape

  OLTP databases            ingestion              storage              query
  ---------------           ---------              -------              -----
  PostgreSQL      --CDC-->  Debezium/Kafka  -->  object storage  -->  DuckDB
  SQL Server      --ELT-->  Fivetran/dbt          + Iceberg /         ClickHouse
  APIs, events              Airbyte               Delta Lake          Snowflake
                                                                      BigQuery
                                                                      Trino

  the significant change: STORAGE IS NOW A FILE FORMAT IN OBJECT STORAGE,
  not something owned by a query engine. Several engines read the same tables.
```

| Component                       | What it is                                                       | Why it matters                                                       |
|---------------------------------|------------------------------------------------------------------|----------------------------------------------------------------------|
| **Parquet**                     | Columnar file format                                             | The de facto interchange format for analytical data                  |
| **Apache Iceberg** / Delta Lake | A table format over Parquet: schema evolution, ACID, time travel | Gives object storage database-like semantics                         |
| **DuckDB**                      | An embedded analytical engine                                    | Queries Parquet and Iceberg locally at remarkable speed — no cluster |
| **ClickHouse**                  | Columnar OLTP-speed analytics                                    | Extremely fast aggregation on very large tables                      |
| **dbt**                         | SQL transformation with tests and lineage                        | Turns ETL scripts into reviewed, tested, versioned code              |
| **Trino / Presto**              | Federated query engine                                           | One SQL surface over several sources                                 |

``` sql
-- DuckDB is worth knowing even if you never adopt a warehouse. It queries
-- Parquet directly, in-process, and it is genuinely fast.
INSTALL httpfs; LOAD httpfs;

SELECT country, date_trunc('month', placed_at) AS month, sum(total) AS revenue
FROM   read_parquet('s3://lake/orders/year=2026/month=*/*.parquet')
WHERE  placed_at >= '2026-01-01'
GROUP  BY 1, 2 ORDER BY 1, 2;
-- no cluster, no loading step, columnar and vectorised. For ad-hoc analysis over
-- exported data this replaces a warehouse for a lot of teams.

-- and it reads directly from PostgreSQL too, which makes it a very convenient
-- way to run a heavy aggregate without loading the OLTP instance
ATTACH 'postgres://reader@replica/app' AS pg (TYPE postgres, READ_ONLY);
SELECT count(*) FROM pg.public.orders;
```

###### Where the OLTP database's responsibility ends

1.  **Own the source of truth** and its constraints. Everything downstream is derived and rebuildable.
2.  **Publish a deliberate contract** — CDC events, an outbox, or a documented set of views. Do not let the warehouse read your base tables directly, or every schema change becomes their outage.
3.  **Keep analytics off the primary.** A replica at minimum; ideally a separate stack entirely.
4.  **Version the contract** and treat a breaking change to it as you would a breaking API change — because it is one.
5.  **Agree who owns transformation.** If the OLTP team ships denormalised views for the warehouse's convenience, the OLTP schema slowly becomes a reporting schema.
6.  **Propagate erasure.** A deletion request must reach the lake, the warehouse and the exports. This is the compliance obligation most often missed at this boundary.

| Anti-pattern at the boundary                    | Consequence                                                       |
|-------------------------------------------------|-------------------------------------------------------------------|
| The warehouse reads production tables directly  | Every schema change breaks reporting; every heavy query hits OLTP |
| Reporting logic duplicated in both              | Two definitions of “revenue”, and they disagree                   |
| No CDC, so nightly full extracts                | Hours of load on the primary; a day of staleness                  |
| Analytics tables in the OLTP database           | Scans evict the transactional working set                         |
| Personal data copied without a retention policy | A compliance exposure with no owner                               |

> **\[Note\]**
>
> **The pragmatic sequence.** A reporting replica handles most needs. Add materialised views or summary tables next. Move to a lake or warehouse when data volume, query complexity, or the need to join across several source systems genuinely demands it — and when someone owns it. A warehouse nobody maintains is a second source of truth that is quietly wrong.

<a id="be-d489"></a>

#### Choosing an engine, and what to learn next

The honest short answer for most new systems: **PostgreSQL, unless there is a specific reason otherwise** — and the specific reasons are worth knowing.

| Choose                               | When                                                                                                                                    |
|--------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------|
| **PostgreSQL**                       | The default. Best extension ecosystem, excellent standards compliance, strong JSON, spatial and vector support, available everywhere    |
| **SQL Server**                       | A Microsoft estate; strong tooling and Query Store; columnstore for hybrid workloads; the licensing cost is the trade                   |
| **MySQL / MariaDB**                  | Existing expertise, or a platform built on it; Vitess for very large sharded deployments                                                |
| **Oracle**                           | Existing investment, or specific enterprise features; rarely a new choice                                                               |
| **SQLite**                           | Embedded, edge, on-device, testing — and far more capable than its reputation. Increasingly viable server-side with Litestream or Turso |
| **CockroachDB / Yugabyte / Spanner** | Multi-region writes, data residency, or genuine horizontal write scale                                                                  |
| **ClickHouse / DuckDB**              | Analytics, as a complement rather than a replacement                                                                                    |
| **TimescaleDB**                      | Time-series at volume, while staying PostgreSQL                                                                                         |

##### Where to go deeper

- **Read your engine's documentation end to end, once.** PostgreSQL's and SQL Server's are excellent, and most engineers have only ever read fragments arrived at via search.
- **Run `EXPLAIN ANALYZE` habitually**, not only when something is slow. Building an intuition for the plans your queries produce is the highest-return practice in this guide.
- **Load ten million rows into something and see what breaks.** Almost every lesson here is invisible at a thousand rows and obvious at ten million.
- **Break things deliberately in a scratch instance:** fill the WAL disk, stall a replication slot, force a deadlock, leave a transaction open for an hour, trigger a failover. Each turns a paragraph into experience.
- **Read the standard texts** — Kleppmann's *Designing Data-Intensive Applications* for distributed thinking, Winand's *SQL Performance Explained* for indexing, Karwin's *SQL Antipatterns* for modelling, and Celko for advanced SQL.
- **Follow one engine's release notes.** The pace is high — PostgreSQL alone has added JSON path, MERGE, iterative vector scans, logical replication improvements and parallelism gains in recent versions.

> **\[Note\]**
>
> **The through-line.** Everything here reduces to one idea: *a database is a physical system — pages, trees, logs, locks, network round trips — behind a declarative interface that hides it.* SQL lets you ignore that reality until the data grows or the concurrency rises, at which point every problem you meet is a place where the abstraction stopped being free. Managed services and distributed engines move which physical details you are responsible for; they do not remove the fact that there are some. Knowing what is underneath is what turns those problems from mysteries into diagnoses.

### The data layer as an architecture

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The data layer as an architecture</p>

<a id="be-m264"></a>

#### Read replicas and read/write splitting

Sending reads to a replica removes reporting and dashboard load from the primary. The cost is **replication lag** — typically milliseconds, occasionally seconds, unbounded during a problem.

``` csharp
// Two contexts over two connection strings; the read one is never tracked.
builder.Services.AddDbContext<WriteDb>(o => o.UseSqlServer(primary));
builder.Services.AddDbContext<ReadDb>(o => o
    .UseSqlServer(replica, s => s.EnableRetryOnFailure())
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
// ApplicationIntent=ReadOnly in the connection string routes to a readable
// secondary in an availability group automatically.
```

**The read-your-writes problem** is the one that produces bug reports: a user saves a record, is redirected, the read hits a replica that has not caught up, and the record "did not save".

``` csharp
// Strategies, cheapest first:
// 1. After a write, read from the primary for a short window (sticky session).
// 2. Return the saved entity from the write itself - do not re-read at all.
// 3. Carry the write's LSN / commit token and wait for the replica to reach it.
// 4. Route by operation: user-facing reads to primary, reports to replica.

private bool RecentlyWrote => _session.LastWriteUtc is { } t
    && DateTimeOffset.UtcNow - t < TimeSpan.FromSeconds(10);

public IQueryable<Order> Orders => RecentlyWrote ? _write.Orders : _read.Orders;
```

> **\[Tip\]**
>
> **Option 2 is underrated.** Most read-after-write problems exist because the code writes, then queries for what it just wrote. Returning the entity you already have in memory removes the round trip *and* the consistency question at once. Re-read only when the database computed something you do not have — an identity value, a trigger result, a computed column.

> **\[Trap\]**
>
> **Sharding is a much larger commitment than replication.** Splitting data across shards by a partition key means cross-shard queries, cross-shard transactions, and re-sharding all become application problems. Before sharding, exhaust: indexes, read replicas, caching, archiving old data, and table partitioning within one database — which gives you partition elimination and fast archival with none of the distribution ([Indexes for LINQ writers](07-03-indexes-and-query-plans.md#be-m171)).

Goes deeper: [Consistent hashing and partition assignment](03-03-hash-tables.md#be-m77)

Goes further: [Decomposition: contexts and data ownership](12-01-monoliths-modules-and-services.md#be-m247)

<a id="be-m265"></a>

#### Observing the data layer

Three signals answer almost every data-layer question, and they are cheap to emit.

``` csharp
// 1. Duration, as a histogram - never as an average (§130).
private static readonly Histogram<double> QueryDuration =
    Meter.CreateHistogram<double>("db.query.duration", "ms");

// 2. Commands per logical operation - catches N+1 the day it appears.
private static readonly Histogram<int> CommandsPerRequest =
    Meter.CreateHistogram<int>("db.commands_per_request");

// 3. Rows returned - catches "someone removed the Take()".
private static readonly Histogram<int> RowsReturned =
    Meter.CreateHistogram<int>("db.rows_returned");
```

``` csharp
// EF emits DiagnosticSource events; an interceptor is the simplest hook.
public sealed class MetricsInterceptor : DbCommandInterceptor {
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand cmd, CommandExecutedEventData e, DbDataReader result,
            CancellationToken ct = default) {
        QueryDuration.Record(e.Duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("db.operation", OperationOf(cmd)));
        return ValueTask.FromResult(result);
    }
}

// OpenTelemetry wires EF, SqlClient, and HTTP into one trace automatically:
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                       .AddSqlClientInstrumentation())
    .WithMetrics(m => m.AddMeter("MyApp.Data").AddRuntimeInstrumentation());
```

> **\[Trap\]**
>
> **Never use raw SQL text as a metric dimension.** Parameterised SQL is fine; SQL with inlined literals produces unbounded cardinality and will take down your metrics backend or your bill ([Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160)). Tag with a stable operation name you choose — `orders.list`, `orders.detail` — not with the statement. The same applies to tenant ids on high-cardinality metrics: sample or aggregate instead.

> **\[Tip\]**
>
> **Alert on shape, not just on time.** "Commands per request exceeded 10" and "rows returned exceeded 10,000" catch the regressions that cause slowness *before* they are slow enough to breach a latency alert — and they point straight at the cause, which a latency alert never does ([Query and performance regression gates](04-06-testing.md#be-m226)).

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)

Applied in: [Distributed tracing and correlation](11-06-observability.md#be-e542)

### Database Servers as Infrastructure

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Database Servers as Infrastructure</p>

<p class="pd">The machine underneath the database. Sizing, storage layout, memory, connections and HA topology — the infrastructure decisions that determine whether query tuning is even the right conversation.</p>

<a id="be-g670"></a>

#### Sizing a database server

Databases have a different resource profile from application servers, and provisioning them like one is a common and expensive error.

``` text
  the priority order for a transactional database

  1  MEMORY      the single most important resource.
                 goal: the WORKING SET (hot data + indexes) fits in RAM.
                 a cache hit is ~100ns; a miss is ~100us on SSD. 1000x.

  2  STORAGE     low-latency, high-IOPS. Random 8k reads and writes.
                 latency matters more than throughput for OLTP.

  3  CPU         needed for parsing, planning, sorting, joining, compression.
                 rarely the first bottleneck on a well-indexed OLTP system.
                 IS the bottleneck for analytics.

  4  NETWORK     rarely limiting, until replication or backups saturate it.

  the usual mistake: provisioning by CPU count because that is how
  application servers are sized, then wondering why it is slow.
```

| Question           | How to answer it                                                                                               |
|--------------------|----------------------------------------------------------------------------------------------------------------|
| How much RAM?      | Database size, or the hot subset of it, plus overhead. If the whole database fits in RAM, buy that             |
| How many cores?    | Roughly: peak concurrent *active* queries. Not concurrent connections — most are idle                          |
| What IOPS?         | Measure at peak: `iostat` on the current system, then add headroom for growth and for rebuild/backup           |
| What disk size?    | Current + growth + **free space for maintenance**. Index rebuilds and `VACUUM FULL` may need a full extra copy |
| How much headroom? | Target ~60% steady-state utilisation. A database at 90% has no capacity to absorb a spike or a bad plan        |

``` sql
-- how big is the working set, really?
SELECT pg_size_pretty(pg_database_size(current_database()));
SELECT relname, pg_size_pretty(pg_total_relation_size(relid)) AS total
FROM   pg_catalog.pg_statio_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 20;

-- and is current memory sufficient? The hit ratio answers it directly.
SELECT sum(heap_blks_hit) * 100.0 / NULLIF(sum(heap_blks_hit + heap_blks_read), 0) AS hit_pct
FROM pg_statio_user_tables;
-- OLTP target: >99%. Sustained below ~95% usually means more RAM, not more CPU.
```

> **\[Note\]**
>
> **Buy memory before anything else.** Doubling RAM so the working set fits in cache routinely produces a larger improvement than doubling CPU or moving to faster disks, because it removes I/O from the path entirely rather than making it quicker. It is also the cheapest change per unit of improvement, and it requires no application changes.

<a id="be-g671"></a>

#### Storage layout and OS configuration for a database

``` csharp
# separate volumes, for genuinely different reasons - not tidiness

/var/lib/postgresql/data    data files      random I/O, needs IOPS
/var/lib/postgresql/wal     write-ahead log SEQUENTIAL, latency-critical
                                            -> commit latency is bounded by
                                               how fast this can fsync
/backup                     backups         throughput, cheap, and MUST NOT
                                            share a device with the data
/var/log                    logs            so a runaway log cannot fill
                                            the data volume and stop the database

# the last one is a real outage cause: log volume fills the same disk as
# the data files, and the database halts because it cannot write.
```

| OS setting             | Value                          | Why                                                                            |
|------------------------|--------------------------------|--------------------------------------------------------------------------------|
| Filesystem             | ext4 or XFS                    | Both fine. XFS handles large files and parallelism slightly better             |
| Mount options          | `noatime`                      | Stops a metadata write on every read                                           |
| `vm.swappiness`        | `1` (not 0)                    | A swapped-out database page is catastrophic for latency                        |
| Transparent Huge Pages | **Disabled**                   | Causes latency spikes and stalls. Every major database recommends disabling it |
| Huge pages (explicit)  | Enabled and sized              | Reduces page-table overhead for a large buffer pool                            |
| I/O scheduler          | `none` / `mq-deadline` on NVMe | The device reorders better than the kernel can                                 |
| `vm.dirty_ratio`       | Lowered                        | Prevents huge writeback stalls at checkpoint                                   |
| NUMA                   | Interleave, or pin             | On large multi-socket machines, remote memory access is measurably slower      |

``` text
# disable transparent huge pages - the single most common Linux database misconfiguration
echo never > /sys/kernel/mm/transparent_hugepage/enabled
echo never > /sys/kernel/mm/transparent_hugepage/defrag
# and persist it via a systemd unit or kernel parameter, or it returns on reboot

# check what is actually set
cat /sys/kernel/mm/transparent_hugepage/enabled
sysctl vm.swappiness vm.dirty_ratio vm.dirty_background_ratio
mount | grep -E 'data|pgdata'
```

> **\[Trap\]**
>
> **Common trap.** Filling the disk stops a database, and recovery is not always simply deleting a file — some engines need free space to start up and to clean up. Monitor free space with a threshold that leaves time to react (alert at 20% free, not 5%), keep logs and backups off the data volume, and know in advance what you would delete first. On PostgreSQL specifically, a stalled replication slot filling the WAL volume is the most common version of this incident.

<a id="be-g672"></a>

#### Connections: the infrastructure view

``` text
  the arithmetic nobody does until it breaks

  20 application pods x connection pool of 100 = 2,000 connections
  database max_connections = 200

  and even if it accepted 2,000:
    PostgreSQL: one PROCESS per connection, ~5-10 MB each = 10-20 GB of RAM
                spent on connections rather than on caching data
    a 16-core machine cannot usefully run more than a few dozen queries at once
    -> 2,000 connections is a queue with overhead, not 2,000x the throughput

  the fix is a POOLER, and it belongs in the infrastructure, not the app.
```

| Layer          | Component                      | Sizing                                                               |
|----------------|--------------------------------|----------------------------------------------------------------------|
| Application    | Driver pool                    | Small — concurrent *queries* per instance, not per user              |
| Infrastructure | PgBouncer, ProxySQL, RDS Proxy | Multiplexes thousands of client connections onto tens of server ones |
| Database       | `max_connections`              | A ceiling that protects the server, not a target                     |

``` text
# PgBouncer in transaction mode: usually the highest-value single change
[pgbouncer]
pool_mode = transaction        # a server connection is held only for a transaction
max_client_conn = 5000         # what applications may open
default_pool_size = 40         # what actually reaches PostgreSQL
reserve_pool_size = 10
server_idle_timeout = 60

# sizing, from Little's Law:
#   concurrent queries needed = arrival rate x average query duration
#   400 qps x 20ms = 8 concurrent. A pool of 20-40 serves that comfortably.

# transaction mode breaks session state: SET, advisory locks, LISTEN/NOTIFY,
# temp tables, and server-side prepared statements unless both ends support it.
# Test those specifically before switching.
```

| Connection concern                          | Infrastructure answer                                          |
|---------------------------------------------|----------------------------------------------------------------|
| Serverless functions exhausting connections | A proxy that pools on the database side, or an HTTP data API   |
| Deploy causes a connection spike            | Stagger pod startup; set a sane pool minimum                   |
| A failover leaves stale connections         | Short DNS TTL, and a driver that re-resolves; test it          |
| One noisy service starves others            | Separate pools or separate database users with per-user limits |
| Idle connections holding resources          | Idle timeouts at both the pool and the database                |

###### What actually happens when the pool is exhausted

``` text
  the cascade, which is why this looks like a database problem
  when it is a pool problem

  1  a slow query holds a connection for 5s instead of 20ms
  2  arrival rate is unchanged, so connections are checked out
     faster than they are returned
  3  the pool empties. New requests WAIT for a connection.
  4  request threads block on pool acquisition
  5  the web server's thread/task pool fills with waiting requests
  6  health checks now queue behind them and time out
  7  the load balancer marks the instance unhealthy and removes it
  8  its traffic goes to the remaining instances, which were already
     near the limit -> they fail the same way
  9  full outage

  the database was never the bottleneck. ONE slow query plus an
  unbounded wait produced a cascading failure.
```

``` sql
# the settings that turn a cascade into a degradation
# .NET / Npgsql connection string
Maximum Pool Size=20;          # bounded, and sized from Little's Law
Timeout=5;                     # FAIL FAST on pool acquisition, do not queue forever
Command Timeout=10;            # no single query may run away
Connection Idle Lifetime=300;
Connection Lifetime=1800;      # recycle, so DNS/failover changes are picked up

# and at the database, a backstop the application cannot bypass
ALTER ROLE app_service SET statement_timeout = '10s';
ALTER ROLE app_service SET idle_in_transaction_session_timeout = '30s';
```

| Metric to expose            | Alert when                                                |
|-----------------------------|-----------------------------------------------------------|
| Pool size in use            | Sustained above ~80% of maximum                           |
| Pool wait time              | p99 above a few milliseconds — it should normally be zero |
| Pool acquisition timeouts   | Any at all                                                |
| Longest-running query       | Above your statement timeout                              |
| `idle in transaction` count | Anything sustained                                        |

The general lesson generalises beyond connection pools: **every queue needs a bound and a timeout.** Unbounded waiting converts a slowdown in one component into an outage in every component above it, and the failure appears at the top of the stack where the cause is not.

<a id="be-g673"></a>

#### High availability topologies

``` text
  what the infrastructure must provide, per topology

  SINGLE INSTANCE + BACKUPS
    RTO: restore time (hours).  RPO: last backup (minutes to hours).
    needs: backups, tested restore, monitoring.
    entirely adequate for a great many systems, and honest about it.

  PRIMARY + STANDBY (same region, sync or async)
    RTO: seconds to minutes.  RPO: zero (sync) or ~seconds (async).
    needs: a failure detector, a consensus store to prevent two primaries,
           FENCING of the old primary, and client redirection.

  PRIMARY + STANDBY + READ REPLICAS
    adds read scale. Same failover machinery.

  MULTI-REGION
    RTO/RPO depend on sync vs async across the WAN.
    synchronous across regions adds the inter-region RTT to EVERY commit.
    needs: a decision about data residency, and a tested failover runbook.
```

| Component            | Job                                   | Note                                                         |
|----------------------|---------------------------------------|--------------------------------------------------------------|
| Failure detector     | Decide the primary is gone            | Patroni, Orchestrator, or the managed service                |
| Consensus store      | Prevent two primaries                 | etcd, Consul — and it needs its own quorum                   |
| Fencing              | Ensure the old primary cannot write   | The step most often skipped, and the cause of split brain    |
| Client redirection   | Point applications at the new primary | A proxy or VIP. DNS TTL makes DNS the slow option            |
| Witness / third node | Break ties                            | A two-node cluster **cannot** safely fail over automatically |

``` text
  SPLIT BRAIN - the worst outcome in database operations

  a network partition separates the monitor from the primary.
  monitor cannot reach primary -> assumes dead -> promotes the standby.
  but the primary is ALIVE, still serving clients on its side.

  now two primaries accept conflicting writes. When the partition heals
  there is NO correct way to merge them. This is worse than downtime.

  prevention: quorum (an odd number of voters), fencing before promotion,
  and a VIP or proxy that only one node can hold.
```

- **Two nodes cannot safely fail over automatically.** They cannot distinguish a dead peer from a partition. Add a witness, or accept manual failover.
- **Client redirection is usually the slow part.** A 300-second DNS TTL means a five-minute outage regardless of how fast promotion is.
- **Connection pools must be invalidated**, or the application keeps writing to a demoted node.
- **Test it.** An untested failover is a hypothesis. Run a drill in staging, and ideally in production during a window.
- **Consider whether you want it automatic.** For many systems a five-minute human-approved failover beats the tail risk of an automatic one going wrong at 3am.

###### Failover, and the two ways it loses data

``` text
  ASYNCHRONOUS REPLICATION - the default, and it can lose committed data

    primary commits  ->  acknowledges the client  ->  sends to replica
                              ^
                         the client is told "saved" HERE

    if the primary dies before the replica receives the change, that
    transaction is GONE, and the client was told it succeeded.

    the exposure is measured by REPLICATION LAG:
      lag 200 ms -> up to 200 ms of committed transactions at risk

  SYNCHRONOUS REPLICATION - no loss, at a cost

    primary commits -> WAITS for the replica -> acknowledges the client

    + zero data loss on failover
    - every write pays the round trip to the replica. Cross-AZ that is
      1-2 ms added to EVERY commit.
    - and if the replica is DOWN, writes BLOCK entirely, unless you
      configure a fallback - at which point you are asynchronous again
      at exactly the moment it matters.

  the middle ground most systems land on:
    synchronous to ONE local replica, asynchronous to remote ones.
    (PostgreSQL: synchronous_standby_names with ANY 1 (...))
```

| Topology                          | RPO                | RTO      | Cost                                         |
|-----------------------------------|--------------------|----------|----------------------------------------------|
| Backups only                      | Hours              | Hours    | Lowest                                       |
| Async replica, manual failover    | Seconds            | Minutes  | Low                                          |
| Async replica, automatic failover | Seconds            | \< 1 min | Needs a witness to avoid split brain         |
| **Sync replica**                  | **Zero**           | \< 1 min | Latency on every write                       |
| Multi-region async                | Seconds to minutes | Minutes  | Egress cost, and much more to operate        |
| Multi-primary                     | Zero               | Zero     | **Conflict resolution becomes your problem** |

**RPO** is how much data you can afford to lose; **RTO** is how long you can be down. They are separate numbers, they are business decisions rather than technical ones, and asking for both to be zero is asking for multi-primary with conflict resolution — which trades a rare, understood failure for a constant, subtle one.

> **\[Trap\]**
>
> **Common trap.** Automatic failover needs an odd number of voters, and a two-node cluster cannot provide one. With only a primary and a replica, neither can distinguish “the other is dead” from “I am partitioned” — so either failover is manual, or you add a third lightweight *witness* whose only job is to break the tie. Skipping the witness is how you get two primaries and a divergence no one can merge.

<a id="be-g674"></a>

#### Managed or self-hosted, operationally

**Managed (RDS, Azure SQL, Cloud SQL)**

- Patching, backups and failover configured for you
- Replicas and PITR are a checkbox
- No OS access, no superuser
- An allow-list of extensions and versions
- Higher unit cost; far lower operational cost

**Self-hosted**

- Full control: any extension, any version, any tuning
- Local NVMe, custom kernel settings, huge pages
- You build HA, backups, patching and monitoring
- Needs someone whose job this genuinely is
- Lower unit cost; substantial operational cost

| Consideration          | Detail                                                                                             |
|------------------------|----------------------------------------------------------------------------------------------------|
| Version lag            | Managed services trail upstream. Check the available version before designing around a new feature |
| Extension availability | The commonest reason a design that works locally cannot be deployed                                |
| Parameter access       | You get a parameter group, not `postgresql.conf`. Some settings are simply unavailable             |
| Maintenance windows    | The provider will restart your database. Choose the window, and be able to survive it              |
| Backup verification    | The provider guarantees the backup exists — not that *your* restore procedure works                |
| Cost model             | Instance + storage + IOPS + backup + cross-AZ traffic. The traffic line surprises people           |
| Escape cost            | Moving off a managed service later is a migration project, not a configuration change              |

> **\[Note\]**
>
> **The honest default.** Use the managed service unless you have a specific, stated reason not to — a required extension, a performance profile that needs local NVMe, a licensing constraint, or a genuine existing operations team. “We can run it ourselves” is true and usually not the question; the question is whether running it is the best use of the time of the people who would have to.
