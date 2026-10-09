---
id: "4-8"
number: "4.8"
title: "Anti-patterns"
part: 4
part_title: "Designing programs"
words: 6730
minutes: 29
---

# 4.8 Anti-patterns

> **Quick look.** Recurring mistakes, used as a review checklist.
>
> - Structural: god classes, feature envy, classes that should be split by field usage.
> - Behavioural: boolean parameters (use named arguments or enums), hidden temporal coupling.
> - Abstraction: layers and interfaces added for their own sake, often by experienced teams.
> - Data and distributed: the shared database between services, chatty calls, distributed monoliths.
> - The .NET catalogue: sync-over-async, N+1, captive dependencies, new HttpClient per call, unbounded caches.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A god service with 40 dependencies

`BookingService` has 4,000 lines and a constructor with 23 parameters; every change conflicts with someone else's.

*Split by use case*

```csharp
// Before: BookingService.Create/Change/Cancel/Refund/Rebook/Upgrade/... all in one class
public sealed class CancelBooking(BookingDb db, IRefunds refunds, IOutbox outbox)
{
    public async Task HandleAsync(CancelBookingCommand cmd, CancellationToken ct) { /* ... */ }
}
public sealed class ChangeFlight(BookingDb db, IFareRules rules) { /* ... */ }
// each handler has 2–4 dependencies and one reason to change
```

> **Lesson:** A huge constructor signals a class doing too much. Split by use case so each piece has few dependencies.

#### Example 2: Two services sharing one database table

Loyalty reads the Bookings table directly. A column rename in Bookings breaks Loyalty in production.

*Publish facts instead of sharing tables*

```csharp
// Bookings owns its table and publishes an event through its outbox:
public sealed record BookingCompleted(Guid BookingId, Guid CustomerId, decimal Amount, DateTimeOffset At);

// Loyalty consumes the event and keeps its own copy of what it needs:
public sealed class AwardPoints(LoyaltyDb db) : IConsumer<BookingCompleted>
{
    public async Task Consume(ConsumeContext<BookingCompleted> ctx) { /* idempotent by BookingId */ }
}
```

> **Lesson:** A shared table is a hidden, unversioned API. Each service owns its data and others get it through contracts.

### Code Review Checklist & Anti-pattern Table

<p class="src">From <em>.NET Backend Deep Dive</em> · Code Review Checklist & Anti-pattern Table</p>

<a id="dn-89"></a>

#### Anti-pattern quick reference

| Anti-pattern | Consequence | Use instead |
|---|---|---|
| `.Result` in ASP.NET Core | Thread pool starvation | `await` |
| `new HttpClient()` per call | Socket exhaustion | `IHttpClientFactory` |
| `IMemoryCache` across many pods | Inconsistent data | `HybridCache` / Redis |
| Singleton `DbContext` | Memory leak, races | Scoped / `IDbContextFactory` |
| Stacked collection `Include`s | Cartesian explosion | `AsSplitQuery` / projection |
| Query inside `foreach` | N+1 | `Include` / `ToLookup` |
| `catch (Exception) { }` | Silent failure | Log and rethrow, or handle specifically |
| `throw ex;` | **Loses the stack trace** | `throw;` |
| String-concatenated SQL | SQL injection | Parameters / EF |
| `DateTime.Now` | Timezone bugs, untestable | `TimeProvider` + UTC |
| Retry without jitter | Retry storms | Exponential backoff + jitter |
| `KEYS *` on Redis | Stalls the whole server | `SCAN` |
| `SELECT *` | Breaks covering indexes | List the columns |
| `float`/`double` for money | Rounding errors | `decimal` |
| Business logic in controllers | Untestable, unreusable | A service/handler layer |
| Returning EF entities from an API | Field leakage, JSON cycles, breaking changes | DTOs |

---

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a83"></a>

#### The anti-pattern catalogue

| Anti-pattern                                      | Fix                                      | §                                                                                                        |
|---------------------------------------------------|------------------------------------------|----------------------------------------------------------------------------------------------------------|
| `list.Contains(x)` inside a loop                  | `HashSet`                                | [HashSet and set operations](03-03-hash-tables.md#be-m102)                                                                   |
| `Count() > 0`                                     | `Any()`                                  | [First, Single, Any, Count: the ones people get wrong](03-10-collections-and-linq-in-net.md#be-m144)                                         |
| `ToList()` then `Where()` on a DbSet              | filter on the server                     | [IEnumerable vs IQueryable](03-10-collections-and-linq-in-net.md#be-b295)                                                                    |
| enumerating a deferred query twice                | materialise once                         | [Deferred execution and multiple enumeration](03-10-collections-and-linq-in-net.md#be-m139)                                                  |
| string `+=` in a loop                             | `StringBuilder`                          | [Concatenation, StringBuilder, interpolation](01-03-text-is-numbers-too.md#be-m81)                                                   |
| `ToUpper()` to compare                            | `OrdinalIgnoreCase`                      | [String comparison: ordinal vs culture](01-03-text-is-numbers-too.md#be-m80)                                                         |
| lazy loading inside a `foreach`                   | projection or `Include`                  | [N+1 and how to see it](07-07-orms-and-ef-core.md#be-m156)                                                                        |
| fetching entities to map to a DTO                 | `Select` in the query                    | [Projection: Select before materialize](07-07-orms-and-ef-core.md#be-m157)                                                        |
| tracking a read-only query                        | `AsNoTracking` / projection              | [Tracking, no-tracking, and the identity map](07-07-orms-and-ef-core.md#be-m158)                                                  |
| two collection `Include`s                         | `AsSplitQuery`                           | [Include, split queries, and cartesian explosion](07-07-orms-and-ef-core.md#be-m159)                                              |
| `Skip(n).Take()` at deep pages                    | keyset pagination                        | [Pagination: OFFSET vs keyset](07-07-orms-and-ef-core.md#be-m161)                                                                 |
| `ORDER BY` without a unique tiebreaker            | `ThenBy(x => x.Id)`                      | [Comparison: IComparable, IComparer, stable ordering](02-03-types.md#be-m57), [Pagination: OFFSET vs keyset](07-07-orms-and-ef-core.md#be-m161) |
| `WHERE CAST(col AS date) = @d`                    | a half-open range                        | [Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160)                                              |
| `WITH (NOLOCK)` as a perf fix                     | `READ_COMMITTED_SNAPSHOT`                | [Transactions and isolation levels](07-04-transactions-and-isolation.md#be-a100)                                                            |
| unconfigured `decimal` / `string`                 | `HasPrecision` / `HasMaxLength`          | [Type mapping: CLR to SQL](07-07-orms-and-ef-core.md#be-m170)                                                                     |
| a long-lived `DbContext`                          | one per unit of work                     | [Tracking, no-tracking, and the identity map](07-07-orms-and-ef-core.md#be-m158)                                                  |
| loading rows to update them                       | `ExecuteUpdate`                          | [Bulk operations and batching](07-07-orms-and-ef-core.md#be-m166)                                                                 |
| growing collections without capacity              | pre-size                                 | [Capacity, pre-sizing, and ArrayPool](02-07-memory-management-and-garbage-collection.md#be-m113)                                                          |
| mutable struct                                    | `readonly record struct`                 | [Value types vs reference types](02-04-values-variables-and-references.md#be-c306), [records, readonly struct, ref struct](02-03-types.md#be-m54)              |
| mutating a key already in a hash set              | immutable keys                           | [Equality: ==, Equals, GetHashCode, IEquatable](02-03-types.md#be-m56)                                                 |
| persisting `GetHashCode()`                        | a stable hash you control                | [Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)                                                            |
| `.Result` / `.Wait()` on async                    | `await` all the way                      | —                                                                                                        |
| PLINQ on a web server                             | let the server parallelise requests      | [PLINQ: when parallel helps](05-05-parallelism.md#be-m153)                                                                   |
| unbounded `IMemoryCache`                          | `SizeLimit` + entry sizes                | [Caching layers](08-06-caching.md#be-m282)                                                                               |
| per-item service call in a loop                   | batch, or own the fact locally           | [Case: the service split that made everything slower](14-03-worked-cases-from-production.md#be-m308)                                          |
| lazy init without a barrier                       | `Lazy<T>` or `static readonly`           | [Case: the bug that only appeared on ARM](14-03-worked-cases-from-production.md#be-m309)                                                      |
| testing a report as one tenant only               | largest, smallest, both orders           | [Case: right in staging, wrong in production](14-03-worked-cases-from-production.md#be-m310)                                                  |
| rename + meaning change in one release            | a new column with a new name             | [Case: the migration that could not be rolled back](14-03-worked-cases-from-production.md#be-m311)                                            |
| outbox batch marked sent in one save              | claim, publish, mark - per message       | [Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312)                                                          |
| alerting only on errors                           | alert on queue age and reconciliation    | [Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312)                                                          |
| a search engine to fix a missing index            | indexed search key first                 | [Case: the search that degraded as data grew](14-03-worked-cases-from-production.md#be-m313)                                                  |
| an aggregate large enough to contend              | smallest boundary the invariants allow   | [Aggregates: the transaction boundary](12-06-domain-modeling-and-application-architecture.md#be-m253)                                                         |
| navigation property to another aggregate          | reference by id                          | [Aggregates: the transaction boundary](12-06-domain-modeling-and-application-architecture.md#be-m253)                                                         |
| rich model with public setters                    | private setters + factory methods        | [Rich and anemic models, honestly](12-06-domain-modeling-and-application-architecture.md#be-m255)                                                             |
| I/O inside a domain event handler                 | outbox row, dispatch after commit        | [Domain events](12-06-domain-modeling-and-application-architecture.md#be-m256)                                                                                |
| `SaveChanges` inside a repository                 | one commit per use case                  | [Application services and use cases](12-06-domain-modeling-and-application-architecture.md#be-m258)                                                           |
| string-interpolated log messages                  | message templates with named fields      | [Logging that is actually queryable](11-06-observability.md#be-a60)                                                            |
| expected failures logged as errors                | reserve `Error` for the unexpected       | [Logging that is actually queryable](11-06-observability.md#be-a60)                                                            |
| high-cardinality metric tags                      | tag by endpoint, not by id               | [Metrics: what to measure](11-06-observability.md#be-e540)                                                                     |
| liveness probe that checks the database           | liveness = process only                  | [Health checks and readiness](11-06-observability.md#be-e543)                                                                  |
| load testing against a tiny dataset               | production volume and skew               | [Load testing and capacity](12-04-scaling-and-performance.md#be-m272)                                                                    |
| release toggles that are never deleted            | set an expiry when you create one        | [Feature flags and progressive delivery](11-04-ci-cd-pipelines.md#be-m273)                                                       |
| assuming rollback works after a migration         | expand/contract, or it is one-way        | [Deployment strategies and rollback](11-04-ci-cd-pipelines.md#be-a220)                                                           |
| `db.Update(entity)` on a client DTO               | load, then apply changed fields          | [The change tracker and disconnected graphs](07-07-orms-and-ef-core.md#be-m174)                                                   |
| TPT with many subtypes                            | TPH unless subtypes are disjoint         | [Inheritance mapping: TPH, TPT, TPC](07-07-orms-and-ef-core.md#be-m173)                                                           |
| per-request state in a pooled `DbContext`         | don't pool, or inject the state          | [Compiled queries, compiled models, and pooling](07-07-orms-and-ef-core.md#be-m175)                                               |
| audit interceptor bypassed by `ExecuteUpdate`     | stamp explicitly on the bulk path        | [Interceptors and diagnostics](07-07-orms-and-ef-core.md#be-m177)                                                                 |
| returning entities from an API                    | a DTO per contract                       | [Request and response contracts](08-02-api-design.md#be-m239)                                                               |
| binding to a type with unsettable fields          | a command type with only settable ones   | [Request and response contracts](08-02-api-design.md#be-m239)                                                               |
| `200 OK` with `{"success": false}`                | real status codes + ProblemDetails       | [Errors: ProblemDetails and failure contracts](08-02-api-design.md#be-m242)                                                 |
| exception details in an API response              | a correlation id; log the rest           | [Errors: ProblemDetails and failure contracts](08-02-api-design.md#be-m242)                                                 |
| sortable fields with no index behind them         | an allow-list of indexed columns         | [Pagination, filtering, and sorting contracts](08-02-api-design.md#be-a130)                                                 |
| caching an authenticated response globally        | vary the key on the user                 | [Auth, rate limiting, and output caching](08-03-a-modern-net-backend.md#be-m243)                                                      |
| copying a controller to `V2` and diverging        | one implementation, versioned edge       | [Versioning an API](08-02-api-design.md#be-m241)                                                                            |
| splitting services before boundaries exist        | modular monolith first                   | [The real question: coupling and cadence](12-01-monoliths-modules-and-services.md#be-m244)                                                      |
| one service reading another's tables              | own your data; publish events            | [Decomposition: contexts and data ownership](12-01-monoliths-modules-and-services.md#be-m247)                                                   |
| a synchronous call chain three deep               | a decomposition problem, not a perf one  | [Synchronous communication and its coupling](12-01-monoliths-modules-and-services.md#be-m248)                                                   |
| publish-after-save without an outbox              | one local transaction for both           | [Transactions across services: sagas and the outbox](12-01-monoliths-modules-and-services.md#be-m250)                                           |
| business logic in the API gateway                 | keep it thin and declarative             | [Gateways, BFFs, and service discovery](12-01-monoliths-modules-and-services.md#be-m251)                                                        |
| extracting and rewriting at once                  | extract identically, verify, then change | [The migration path, in order](12-01-monoliths-modules-and-services.md#be-m252)                                                                 |
| `new HttpClient()` per request                    | `IHttpClientFactory`                     | [Sockets, ports, and connection state](06-03-tcp-and-udp.md#be-m19)                                                          |
| singleton `HttpClient` that never re-resolves DNS | set `PooledConnectionLifetime`           | [DNS: the lookup you never see](06-04-dns.md#be-m16)                                                                 |
| accepting any TLS certificate                     | fix the cert or trust the CA             | [TLS: what the handshake establishes](06-05-tls.md#be-m17)                                                           |
| unbuffered byte-at-a-time file I/O                | buffer — one syscall per chunk           | [System calls: crossing into the kernel](01-07-the-operating-system.md#be-m8)                                                         |
| a thread per concurrent I/O operation             | async; the device works alone anyway     | [Devices, interrupts, and why I/O is different](01-07-the-operating-system.md#be-m10)                                                 |
| assuming a write is durable on return             | `Flush(flushToDisk: true)`               | [Files, handles, and the page cache](01-06-storage-and-files.md#be-m9)                                                             |
| compressing to fix too many round trips           | batch; latency ≠ bandwidth               | [Where the time actually goes](12-04-scaling-and-performance.md#be-g705)                                                                 |
| work in a static field initializer                | explicit init, or `Lazy<T>`              | [Type loading, statics, and initialization order](02-02-compilers-interpreters-and-jit.md#be-m43)                                               |
| exceptions for expected failures                  | the `Try` pattern                        | [Exceptions: what they actually cost](02-08-errors-and-exceptions.md#be-m44)                                                           |
| `throw ex;`                                       | `throw;` — preserves the stack           | [Exceptions: what they actually cost](02-08-errors-and-exceptions.md#be-m44)                                                           |
| inlining everything into one big method           | small methods; let the JIT flatten       | [Inlining and what stops it](02-02-compilers-interpreters-and-jit.md#be-m41)                                                                    |
| suppressing trim/AOT warnings                     | source generators, or don't trim         | [AOT, ReadyToRun, and trimming](02-02-compilers-interpreters-and-jit.md#be-m42)                                                                 |
| scoped service in a singleton constructor         | `IServiceScopeFactory`                   | [Service lifetimes and the captive dependency](04-03-composition-and-dependency-injection.md#be-a58)                                                  |
| scoped service in middleware constructor          | parameter of `InvokeAsync`               | [Middleware: the pipeline as nested delegates](08-01-what-a-backend-does.md#be-m231)                                                 |
| `IOptionsSnapshot` in a singleton                 | `IOptionsMonitor`                        | [Configuration and options](08-03-a-modern-net-backend.md#be-a59)                                                                     |
| transient `IDisposable` from the root provider    | make it scoped                           | [Dependency injection: the container and resolution](04-03-composition-and-dependency-injection.md#be-m229)                                           |
| fire-and-forget from a request handler            | a channel or an outbox row               | [A request, end to end](05-03-async-and-await.md#be-c328)                                                                        |
| background service without a scope per iteration  | `CreateAsyncScope()` each loop           | [Background services and scopes outside a request](08-07-background-work-and-queues.md#be-m234)                                             |
| ignoring `stoppingToken`                          | observe it; shutdown hangs otherwise     | [Shutdown and graceful drain](08-01-what-a-backend-does.md#be-m235)                                                                  |
| secrets in `appsettings.json`                     | env vars or a secret store               | [Configuration and options](08-03-a-modern-net-backend.md#be-a59)                                                                     |
| grace period shorter than shutdown timeout        | set the three timeouts as a set          | [Shutdown and graceful drain](08-01-what-a-backend-does.md#be-m235)                                                                  |
| random Guid as a clustered key                    | identity, or `Guid.CreateVersion7()`     | [B-tree anatomy: levels, splits, and fill factor](07-03-indexes-and-query-plans.md#be-m196)                                              |
| index that needs a lookup per row                 | `INCLUDE` the returned columns           | [Nonclustered indexes, lookups, and the tipping point](07-03-indexes-and-query-plans.md#be-m197)                                         |
| range column before equality in an index key      | equality first, range last               | [Seeks, scans, and residual predicates](07-03-indexes-and-query-plans.md#be-m200)                                                        |
| trusting a plan with a bad row estimate           | fix statistics or the predicate first    | [Statistics and cardinality estimation](07-03-indexes-and-query-plans.md#be-d436)                                                        |
| hinting a join type to fix a slow plan            | fix the cardinality estimate             | [Join algorithms: loops, hash, and merge](07-03-indexes-and-query-plans.md#be-m199)                                                      |
| columnstore on a transactional table              | rowstore for OLTP, columnstore for facts | [Columnstore and compression](07-03-indexes-and-query-plans.md#be-m201)                                                                  |
| `OrderBy(_ => Guid.NewGuid())` to shuffle         | `Random.Shared.Shuffle`                  | [Shuffling and sampling correctly](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m74)                                                              |
| `rng.Next() % n`                                  | `Next(0, n)` — unbiased                  | [Random: what it is and what it is not](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m73)                                                         |
| `GetHashCode()` for sharding or cache keys        | `XxHash64` — stable across processes     | [Non-cryptographic hashing](03-03-hash-tables.md#be-m75)                                                                     |
| `hash % N` for node assignment                    | rendezvous hashing or fixed slots        | [Consistent hashing and partition assignment](03-03-hash-tables.md#be-m77)                                                   |
| a natural key as the primary key                  | surrogate key + unique constraint        | [Generating identifiers](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)                                                                        |
| sequential ids exposed in public URLs             | an opaque public identifier              | [Generating identifiers](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)                                                                        |
| reusing a retired protobuf field number           | `reserved`                               | [Protocol Buffers and gRPC](08-08-serialization-and-data-formats.md#be-m93)                                                                     |
| proto3 scalar zero meaning "not supplied"         | `optional` or a wrapper type             | [Protocol Buffers and gRPC](08-08-serialization-and-data-formats.md#be-m93)                                                                     |
| raw `IntPtr` for a native handle                  | `SafeHandle`                             | [Handles and lifetimes across the boundary](02-02-compilers-interpreters-and-jit.md#be-m96)                                                     |
| native code retaining a managed buffer            | pin explicitly, or allocate unmanaged    | [Marshalling to native code](02-02-compilers-interpreters-and-jit.md#be-m95)                                                                    |
| `Parse`/`ToString` without a format provider      | `InvariantCulture` at machine boundaries | [Formatting and parsing across cultures](01-03-text-is-numbers-too.md#be-m97)                                                        |
| `ToString("C")` for a known currency              | format from the currency code            | [Formatting and parsing across cultures](01-03-text-is-numbers-too.md#be-m97)                                                        |
| catastrophic regex backtracking                   | `NonBacktracking` + a timeout            | [Regex: backtracking, ReDoS, and source generation](01-03-text-is-numbers-too.md#be-m84)                                             |
| `SearchValues.Create` inside the loop             | `static readonly`                        | [SearchValues and vectorized searching](01-03-text-is-numbers-too.md#be-m85)                                                         |
| `BitConverter` for wire formats                   | `BinaryPrimitives`, explicit endianness  | [Binary layout: endianness and BinaryPrimitives](01-06-storage-and-files.md#be-m88)                                                |
| async file I/O without `FileOptions.Asynchronous` | open the handle async                    | [Streams, buffering, and file I/O](01-06-storage-and-files.md#be-m89)                                                              |
| decompressing untrusted input unbounded           | cap the decompressed length              | [Compression: when and what](01-06-storage-and-files.md#be-m91)                                                                    |
| sorting a stream to take the top 10               | bounded min-heap                         | [Top-K without sorting](03-05-heaps-and-priority-queues.md#be-m124)                                                                        |
| averaging p99s across servers                     | merge histograms, then quantile          | [Quantiles and histograms](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m130)                                                                     |
| `E[x²] - E[x]²` for variance                      | Welford                                  | [Streaming aggregates done accurately](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m126)                                                         |
| storing a future event as a UTC instant           | local time + IANA zone id                | [Time zones and DST in practice](01-02-numbers-inside-the-machine.md#be-m65)                                                                |
| one offset reused across a recurrence             | resolve per occurrence                   | [Recurring events](01-02-numbers-inside-the-machine.md#be-m67)                                                                              |
| `DateTime.UtcNow` in testable logic               | inject `TimeProvider`                    | [Testing time: TimeProvider](04-06-testing.md#be-m68)                                                                    |
| splitting money with naive rounding               | largest remainder allocation             | [Allocating amounts without losing cents](01-02-numbers-inside-the-machine.md#be-m70)                                                       |
| rounding net and tax independently                | derive one by subtraction                | [Percentages, tax, and order of operations](01-02-numbers-inside-the-machine.md#be-m71)                                                     |
| bare primitives for ids and amounts               | `readonly record struct` wrappers        | [Value objects and parsing at the boundary](08-05-validation-and-errors-at-the-boundary.md#be-m266)                                                    |
| soft delete without a filtered unique index       | filter the index, or archive             | [Soft delete: costs and alternatives](07-05-schema-design-and-migrations.md#be-m211)                                                          |
| query filter treated as a security boundary       | row-level security + assertions          | [Multi-tenancy: three strategies](07-05-schema-design-and-migrations.md#be-m212)                                                              |
| event-sourcing the whole system                   | only where history is the domain         | [Append-only and event sourcing](07-05-schema-design-and-migrations.md#be-m214)                                                               |
| summing a non-additive measure                    | declare the grain, classify measures     | [Analytical modeling: grain, facts, dimensions](07-05-schema-design-and-migrations.md#be-m215)                                                |
| `Point(lat, lng)`                                 | X is longitude — use a named factory     | [Geospatial data](07-06-non-relational-stores.md#be-m217)                                                                              |
| raw SQL text as a metric dimension                | a stable operation name                  | [Observing the data layer](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m265)                                                                     |
| raising Max Pool Size to fix timeouts             | find the connection held too long        | [Connection pooling and exhaustion](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462)                                                            |
| retry without jitter                              | exponential backoff + jitter             | [Resilience: retries, breakers, and timeouts](12-05-reliability.md#be-m263)                                                  |
| hand-rolled `Split(',')` CSV parsing              | a real parser; guard formula injection   | [Tabular interchange: CSV and Excel](08-08-serialization-and-data-formats.md#be-m219)                                                           |
| generic `IRepository<T>` leaking `IQueryable`     | named query methods                      | [Where data access code belongs](07-07-orms-and-ef-core.md#be-m260)                                                               |
| blocking on async with `.Result`                  | `await` to the entry point               | [Async data access: starvation and deadlock](05-03-async-and-await.md#be-m136)                                                   |
| unbounded `Channel` or queue                      | bound it; backpressure                   | [Channels and async pipelines](05-05-parallelism.md#be-m135)                                                                 |
| `await` inside a `lock`                           | `SemaphoreSlim(1,1)`                     | [Interlocked, volatile, and what lock guarantees](05-02-threads-and-locks.md#be-m132)                                              |
| `ReaderWriterLockSlim` for read-mostly data       | publish an immutable snapshot            | [Immutable snapshot publishing](04-04-immutability-and-pure-functions.md#be-m134)                                                                |
| non-atomic `_counter++` across threads            | `Interlocked.Increment`                  | [Interlocked, volatile, and what lock guarantees](05-02-threads-and-locks.md#be-m132)                                              |
| accepting a `CancellationToken` and ignoring it   | pass it all the way down                 | [Cancellation and timeouts](05-03-async-and-await.md#be-m137)                                                                    |
| one-shot `UPDATE` over a huge table               | keyset-driven batches                    | [Backfills without downtime](07-05-schema-design-and-migrations.md#be-m221)                                                                   |
| `Database.Migrate()` at startup                   | idempotent script, separate step         | [EF migrations in practice](07-05-schema-design-and-migrations.md#be-m222)                                                                    |
| rename-in-one-migration on a live system          | expand / migrate / contract              | [Schema evolution: expand and contract](07-05-schema-design-and-migrations.md#be-m220)                                                        |
| testing queries on the InMemory provider          | SQLite or Testcontainers                 | [Testing data access](04-06-testing.md#be-m224)                                                                          |
| hand-written `GetHashCode`, untested              | contract test + `HashCode.Combine`       | [Contract tests for equality and comparers](04-06-testing.md#be-m225)                                                    |
| `EnableSensitiveDataLogging` in production        | development only                         | [Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)                                                                      |
| fast hash (SHA256) for passwords                  | PBKDF2 / Argon2 + constant-time compare  | [Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)                                                                      |
| recursive traversal over unbounded depth          | explicit `Stack<T>`                      | [Graphs: representation and topological sort](03-06-graphs.md#be-m122)                                                  |
| `BinarySearch` on an unsorted list                | sort first, or use a set                 | [Binary search and its variants](03-07-searching-and-sorting.md#be-m115)                                                               |
| `NOT IN` over a nullable subquery                 | `NOT EXISTS`                             | [Subqueries, derived tables, and correlation](07-02-the-relational-model-and-sql.md#be-d412)                                                  |
| a cursor or row-by-row loop                       | one set-based statement                  | [Set-based thinking, temp tables, and TVPs](07-02-the-relational-model-and-sql.md#be-m191)                                                    |
| condition on the right table in `WHERE`           | move it into `ON`                        | [Join types and what they really mean](07-02-the-relational-model-and-sql.md#be-m181)                                                         |
| `UNION` where `UNION ALL` is correct              | drop the dedupe                          | [Set operations: UNION, INTERSECT, EXCEPT](07-02-the-relational-model-and-sql.md#be-m182)                                                     |
| updating two systems without an outbox            | transactional outbox + idempotency       | [Consistency across stores](07-06-non-relational-stores.md#be-m210)                                                                    |
| raising RavenDB's per-session request cap         | `Include`, batched load, or streaming    | [RavenDB: session, unit of work, request budget](07-06-non-relational-stores.md#be-m204)                                               |
| `WaitForNonStaleResults` in production            | design around eventual consistency       | [RavenDB: indexes and eventual consistency](07-06-non-relational-stores.md#be-m205)                                                    |
| `KEYS *` on Redis                                 | `SCAN`                                   | [Redis: the data structures](07-06-non-relational-stores.md#be-m206)                                                                   |
| unversioned, untenanted cache keys                | `app:v:tenant:entity:id`                 | [Cache key design and value format](07-06-non-relational-stores.md#be-m207)                                                            |
| new `JsonSerializerOptions` per call              | cache one static instance                | [System.Text.Json: shape and cost](08-08-serialization-and-data-formats.md#be-m208)                                                             |
| reflection inside a per-row loop                  | cache delegates, or source-generate      | [Source generation vs reflection](02-02-compilers-interpreters-and-jit.md#be-m64)                                                               |
| `FromSqlRaw` with interpolation                   | `FromSql` (interpolated handler)         | [Raw SQL, FromSql, and stored procedures](07-07-orms-and-ef-core.md#be-m167)                                                      |

> **\[Why it matters\]**
>
> **How to use this table.** These are not laws — each one has a legitimate exception. They are a review checklist: when something is slow, walk the list and check which apply before opening a profiler. In most .NET codebases, three or four of these account for the large majority of the available wins.

Worked example: [The review checklist these cases produce](14-05-what-actually-goes-wrong-field-guide-and-checklists.md#be-m306)

Applied in: [How to use this guide](00-02-backend-development-in-one-sitting.md#be-m1)

### Anti-Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Anti-Patterns</p>

<p class="pd">Recurring solutions that make things worse. Each one has a name, a plausible reason it was adopted, and a recognisable smell — which is what makes them worth cataloguing.</p>

<a id="be-h780"></a>

#### Structural anti-patterns

| Anti-pattern             | Looks like                                              | Why it happens                                | Exit                                                  |
|--------------------------|---------------------------------------------------------|-----------------------------------------------|-------------------------------------------------------|
| **God object**           | One class of 3,000 lines that everything depends on     | Every new feature had somewhere obvious to go | Extract by responsibility, one collaborator at a time |
| **Anaemic domain model** | Data classes plus `*Service` classes holding all logic  | It maps trivially to an ORM                   | Move invariants onto the type that owns the data      |
| **Big ball of mud**      | No discernible structure; everything reaches everything | No boundaries were ever enforced              | Introduce a seam, then strangle inward                |
| **Lava flow**            | Dead code nobody dares delete                           | It might be used somewhere                    | Instrument it; delete what never runs                 |
| **Poltergeist**          | A class that exists only to call another                | A layer was added without a purpose           | Delete it; call the target                            |
| **Golden hammer**        | Every problem solved with the same tool                 | It worked once, memorably                     | Name the force; check the tool addresses it           |

``` text
  THE GOD OBJECT, and how it actually forms - because nobody sets out
  to write one

    week 1    OrderService.PlaceOrder()          reasonable
    week 3    + CalculatePricing()               related enough
    week 6    + SendConfirmationEmail()          it has the order...
    week 9    + GenerateInvoicePdf()             ...and the customer
    week 14   + SyncToWarehouse()                and the lines
    week 20   + ExportForAccounting()
    ...
    month 9   2,400 lines, 14 injected dependencies, and every feature
              touches it, so every feature conflicts in version control

  each individual step was DEFENSIBLE. The failure was that nobody
  asked "does this belong here?" - they asked "where is the order?"

  the early warning, and it is measurable:
    the constructor parameter count. Past about five, cohesion is
    already gone. Past eight, split it today.
```

###### Splitting a god object without a rewrite

``` text
  THE GROUPS ARE ALREADY VISIBLE IN THE CONSTRUCTOR

    public class OrderService(
        AppDbContext db, IMapper mapper, ICacheService cache,     <- data
        IEmailSender email, IPdfGenerator pdf,                     <- outbound
        IWarehouseClient warehouse,                                <- outbound
        IPricingEngine pricing, IEventPublisher events,            <- domain
        ILogger<OrderService> log, IConfiguration config)          <- ambient

    three cohesive groups plus ambient concerns. That is three classes,
    and you did not have to read a single method to find them.

  THE SPLIT, in shippable steps

    1  create the new class, EMPTY, registered in DI.
    2  MOVE ONE METHOD to it, and have the god object DELEGATE:

         public Task SendConfirmationAsync(Order o, CancellationToken ct)
             => _notifier.SendConfirmationAsync(o, ct);   // forwards

       every caller still compiles. Ship it.
    3  repeat until the group is moved.
    4  update CALLERS to use the new class directly, one at a time.
    5  delete the forwarding methods, and the now-unused dependencies.

  at no point is anything broken, and you can stop at any step.
```

| Metric                          | Threshold | Meaning                                   |
|---------------------------------|-----------|-------------------------------------------|
| Constructor parameters          | \> 5      | Cohesion is already gone                  |
| Lines in a class                | \> 500    | Worth looking at                          |
| Public methods                  | \> 15     | Probably several responsibilities         |
| Methods using no instance state | Any       | Those are static helpers or another class |
| Number of distinct callers      | \> 20     | A shotgun-surgery risk                    |
| Fields used by only one method  | Any       | That method belongs elsewhere             |

The last row is the most actionable and the least known. Compute, for each field, which methods use it. If a cluster of fields is used by exactly one cluster of methods and nothing else touches them, that is a class waiting to be extracted — and the boundary is determined by the data rather than by anyone's opinion. This is *cohesion analysis*, and several static-analysis tools compute it as LCOM.

<a id="be-h781"></a>

#### Behavioural anti-patterns

| Anti-pattern                  | The problem                                                                           |
|-------------------------------|---------------------------------------------------------------------------------------|
| **Service locator**           | `container.Resolve<IFoo>()` mid-method — the dependency is invisible in the signature |
| **Singleton abuse**           | Global mutable state, untestable, order-dependent tests                               |
| **Ambient context**           | `DateTime.Now`, `HttpContext.Current` — hidden inputs that cannot be substituted      |
| **Sequential coupling**       | “Call `Init()` before `Run()`”, enforced by nothing                                   |
| **Magic strings and numbers** | `if (status == "A")` — meaning lives in someone's head                                |
| **Boolean trap**              | `Process(order, true, false, true)`                                                   |
| **Exception as control flow** | Throwing for an expected outcome, at 5,000× the cost of a return                      |
| **Swallowing exceptions**     | `catch { }` — failure becomes invisible                                               |

``` csharp
// SERVICE LOCATOR - and why it is worse than it looks
public class OrderService(IServiceProvider services)
{
    public async Task PlaceAsync(Order o)
    {
        var repo = services.GetRequiredService<IOrderRepository>();   // hidden
        var mail = services.GetRequiredService<IEmailSender>();       // hidden
    }
}
// the constructor says this class needs IServiceProvider. It actually
// needs two specific things, and you find out by reading every line.
// worse: a missing registration now fails at RUNTIME, on the one code
// path that needs it, instead of at startup.

// AMBIENT CONTEXT - the same problem, differently disguised
public bool IsExpired(Token t) => t.ExpiresAt <= DateTime.UtcNow;   // hidden input
// untestable at the boundary, and it is not obvious the method depends
// on anything at all.

public class TokenService(TimeProvider clock)                       // explicit
{
    public bool IsExpired(Token t) => t.ExpiresAt <= clock.GetUtcNow();
}
```

> **\[Trap\]**
>
> **Common trap.** The boolean trap is worth a specific mention because it is so easy to fix. `Process(order, true, false)` is unreadable at the call site and the compiler cannot help. Use named arguments (`Process(order, validate: true, notify: false)`), an enum, or separate methods. The cost of fixing it is minutes; the cost of leaving it is every future reader navigating to the signature.

###### Ambient context, and why TimeProvider mattered

``` csharp
// AMBIENT CONTEXT - a hidden input the signature does not mention
public bool IsExpired(Token t) => t.ExpiresAt <= DateTime.UtcNow;
// this method depends on the clock, and nothing says so.
// you cannot test the boundary. You cannot test "one second before".

// the historic workaround, in every codebase for fifteen years
public interface IClock { DateTimeOffset UtcNow { get; } }

// and since .NET 8, the BCL has it, so there is no excuse
public class TokenService(TimeProvider clock)
{
    public bool IsExpired(Token t) => t.ExpiresAt <= clock.GetUtcNow();
}

// in a test
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
var svc = new TokenService(clock);
Assert.False(svc.IsExpired(token));
clock.Advance(TimeSpan.FromMinutes(16));      // TIME TRAVEL, deterministically
Assert.True(svc.IsExpired(token));
```

``` text
  TimeProvider ALSO ABSTRACTS TIMERS AND DELAYS, which is the part
  people miss and which makes it far more valuable than IClock ever was.

    await Task.Delay(TimeSpan.FromMinutes(5), ct);
      -> a test of this genuinely waits five minutes, or you refactor
         the delay out and stop testing it.

    await Task.Delay(TimeSpan.FromMinutes(5), clock, ct);
      -> in a test, FakeTimeProvider.Advance(5 minutes) completes it
         INSTANTLY, and the code under test is unchanged.

    clock.CreateTimer(...)   -> same, for periodic work.

  so retry backoff, circuit-breaker windows, cache expiry, polling
  loops and scheduled work all become testable in milliseconds -
  and those are precisely the behaviours that were previously
  untested because testing them was impractical.

  OTHER AMBIENT CONTEXTS worth removing the same way:
    DateTime.Now / UtcNow          -> TimeProvider
    Guid.NewGuid()                 -> an IIdGenerator, for deterministic tests
    Random.Shared                  -> an injected Random with a seed
    Environment.MachineName        -> configuration
    HttpContext.Current            -> IHttpContextAccessor, injected
    Thread.CurrentPrincipal        -> ICurrentUser
    CultureInfo.CurrentCulture     -> explicit, at the boundary
```

| Ambient thing                      | Why it hurts                                                          |
|------------------------------------|-----------------------------------------------------------------------|
| Static mutable state               | Tests share it, so test order matters                                 |
| `DateTime.Now`                     | Untestable boundaries; also machine-timezone dependent                |
| `Guid.NewGuid()`                   | Non-deterministic assertions                                          |
| `HttpContext.Current`              | Does not exist in a background thread — and did not port to .NET Core |
| Service locator                    | The dependency is invisible in the signature                          |
| Thread-local or `AsyncLocal` state | Flows in ways nobody can predict                                      |

<a id="be-h782"></a>

#### Abstraction anti-patterns

The failure modes that come from applying patterns rather than from ignoring them — and these are the ones that afflict experienced teams rather than inexperienced ones.

| Anti-pattern                 | Symptom                                          | Underlying error                      |
|------------------------------|--------------------------------------------------|---------------------------------------|
| **Premature abstraction**    | An interface with one implementation, forever    | Abstracting before seeing what varies |
| **Pattern fever**            | Seven patterns in a CRUD screen                  | The pattern was the goal              |
| **Speculative generality**   | Extension points nothing extends                 | “We might need it”                    |
| **Leaky abstraction**        | `IRepository` exposing `IQueryable`              | The abstraction did not abstract      |
| **Wrapper for its own sake** | `ILoggerWrapper` around `ILogger`                | Wrapping something already abstract   |
| **Inner-platform effect**    | A configurable rules engine nobody can configure | Rebuilding the language, badly        |
| **Architecture astronaut**   | Six layers, each forwarding                      | Structure without a force             |

``` text
  THE LEAKY ABSTRACTION, worked - because it is the subtle one

    public interface IRepository<T>
    {
        IQueryable<T> Query();          // <- the leak
    }

    what this promises: "you do not depend on the data layer"
    what is true:
      callers write LINQ against the ORM's model
      an unsupported expression throws at RUNTIME, not compile time
      lazy evaluation means the query runs OUTSIDE the repository,
        possibly after the DbContext is disposed
      swapping to Dapper is impossible - IQueryable is the ORM

    you have all the ceremony of an abstraction and none of the benefit.

  the test for any abstraction:
    "if I replaced the implementation with a completely different
     technology, would the interface still make sense?"

    IQueryable<T>  -> no. It IS the technology.
    Task<Order?> ByIdAsync(OrderId id)  -> yes.
```

> **\[Note\]**
>
> **The rule of three, restated as a defence.** First occurrence: write it directly. Second: notice, and copy it. Third: now you have *seen* what varies, so abstract along that axis. Abstracting at the first or second occurrence means guessing — and a wrong abstraction is materially harder to remove than duplication, because callers have already built on it.

###### The one-implementation interface debate, honestly

``` text
  the most common design argument in .NET codebases, and both sides
  have a real point.

  FOR: "extract an interface for everything"
    - required for mocking with most frameworks
    - documents the contract separately from the implementation
    - a seam exists if you ever need one
    - the team applies it uniformly, so nobody argues per class

  AGAINST: "only extract when there are two implementations"
    - IFoo/Foo pairs double the file count with no decision content
    - "go to definition" lands on the interface, not the code
    - it is not really an abstraction: the interface is the class's
      public surface, renamed
    - a real second implementation usually needs a DIFFERENT shape
      anyway, so the seam is in the wrong place
```

| Extract an interface when                                      | Do not when                                |
|----------------------------------------------------------------|--------------------------------------------|
| A second implementation exists, or is scheduled                | It is speculative                          |
| It crosses an architectural boundary (domain → infrastructure) | Both sides are in the same project         |
| It represents a *domain concept* (`IPaymentGateway`)           | It mirrors one class (`IOrderServiceImpl`) |
| It wraps something external you do not control                 | It wraps something already abstract        |
| It is a decoration point                                       | Nothing decorates it                       |
| A test genuinely cannot use the real thing                     | The real thing is fast and deterministic   |

``` csharp
// the middle position that resolves most of it:
// extract interfaces at BOUNDARIES, use concrete types WITHIN a layer.

// BOUNDARY - interface. Domain defines it; infrastructure implements it.
public interface IOrderRepository { ... }          // domain -> infrastructure
public interface IPaymentGateway  { ... }          // domain -> external world
public interface IClock           { ... }          // domain -> the machine

// WITHIN a layer - concrete. Nothing crosses; nothing is substituted.
public sealed class OrderPricingCalculator { }     // pure, fast, deterministic
public sealed class OrderValidator { }
// test these DIRECTLY. There is nothing to mock - and a test that
// constructs the real calculator tests more than one asserting on a mock.

// and note: modern test frameworks and TimeProvider have removed several
// of the historic reasons for a mock-only interface.
```

> **\[Note\]**
>
> **The question that settles it.** “If I replaced the implementation with a completely different technology, would this interface still make sense?” `IOrderRepository` with `ByIdAsync` survives a move from EF Core to Dapper to a document store. `IOrderService` that mirrors `OrderService` method-for-method does not survive anything, because it was never an abstraction — it was a copy.

<a id="be-h783"></a>

#### Data and distributed anti-patterns

| Anti-pattern               | The problem                                                                        |
|----------------------------|------------------------------------------------------------------------------------|
| **N+1 queries**            | One query for the list, one per item. 50 orders = 51 queries                       |
| **Shared database**        | Several services writing the same tables; the schema becomes an unwritten contract |
| **Entity as API contract** | Renaming a column breaks every consumer                                            |
| **EAV**                    | `(entity, attribute, value)` — no types, no constraints, unqueryable               |
| **Chatty interface**       | Ten round trips where one would do                                                 |
| **Distributed monolith**   | Microservices that must deploy together                                            |
| **Dual write**             | Write the database, then publish — either can fail alone                           |
| **Nano-services**          | Services so small the coordination exceeds the work                                |

``` text
  THE DISTRIBUTED MONOLITH - the worst outcome available, because it
  has every cost and no benefit

    you split into services. Then:

      service A cannot deploy without B deploying too
      a schema change requires coordinating four teams
      the integration test suite needs all of them running
      a failure in one cascades to all of them
      local development requires running the whole system

    you now have:
      the network, the serialisation, N pipelines, N dashboards,
      distributed debugging, eventual consistency
    and you have NOT got:
      independent deployment, independent scaling, fault isolation

    the cause is almost always that the BOUNDARIES were drawn wrong -
    usually along technical layers (a "data service", an "API service")
    rather than along business capabilities.

    the test: can service A ship a change on Tuesday without asking
    anyone? If no, it is not an independent service.
```

The honest note on the shared database: it is the fastest possible integration, which is exactly why it happens. Someone needs the data and it is right there. The cost arrives later, when nobody can change a column because nobody knows who reads it — and that is the single most common reason a legacy database cannot be modernised. The rule that prevents it is **one writer per table**; everything else reads through a view, an API, or an event stream.

###### Detecting an N+1 automatically, rather than by reading code

``` csharp
// an EF Core interceptor that COUNTS queries per request and fails
// the build when a threshold is crossed. Turn it on in tests.
public sealed class QueryCountInterceptor(QueryCounter counter) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        counter.Increment(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

[Fact]
public async Task Order_list_page_issues_at_most_three_queries()
{
    var counter = new QueryCounter();
    var response = await _client.GetAsync("/orders?page=1");

    Assert.True(counter.Count <= 3,
        $"Expected <= 3 queries, got {counter.Count}:\n{counter.Summary}");
    // an N+1 introduced later FAILS THE BUILD, with the offending SQL
    // printed. This is the only reliable way to keep N+1s out - code
    // review does not catch them, because nothing in the C# looks wrong.
}
```

``` text
  THE THREE WAYS AN N+1 GETS IN, and none of them look wrong in review

  1  LAZY LOADING
       foreach (var o in orders) Print(o.Customer.Name);
       a property read that is a query.

  2  A MAPPER TOUCHING A NAVIGATION
       _mapper.Map<OrderDto>(order)
       AutoMapper reads order.Customer.Name -> a query, inside the mapper,
       invisible at the call site.

  3  A LOOP CALLING A SERVICE THAT QUERIES
       foreach (var o in orders) await _pricing.GetAsync(o.ProductId);
       not EF's fault at all, and the same shape. Also applies to HTTP
       calls, cache lookups and message publishes in a loop.

  the general form: ANY per-item remote call inside a loop.
  the general fix: fetch the SET in one call, then join in memory.

    var ids = orders.Select(o => o.ProductId).Distinct().ToArray();
    var prices = await _pricing.GetManyAsync(ids, ct);      // ONE call
    var lookup = prices.ToDictionary(p => p.ProductId);
```

| Anti-pattern           | Detect it by                                         |
|------------------------|------------------------------------------------------|
| N+1                    | A query-count assertion in tests                     |
| Shared database        | Auditing which roles have write access to each table |
| Entity as API contract | Diffing the generated OpenAPI document in CI         |
| Distributed monolith   | “Can service A deploy alone on a Tuesday?”           |
| Chatty interface       | Counting round trips for one user action             |
| Dual write             | Searching for a publish adjacent to a `SaveChanges`  |

The pattern across that table: **these are all detectable mechanically, and none of them are reliably detectable by reading code**. That is why they persist in codebases with good review culture — a reviewer sees a property access and a foreach, both of which look fine. A test that counts queries does not have that blind spot.
