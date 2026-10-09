---
id: "15-1"
number: "15.1"
title: "Cheat sheets and compressed summaries"
part: 15
part_title: "Reference"
words: 1673
minutes: 7
---

# 15.1 Cheat sheets and compressed summaries

> **Quick look.** Compressed summaries and the numbers worth memorizing.
>
> - Latency orders of magnitude: one Redis call is about 10,000 memory-cache reads; an N+1 over 100 rows wastes 100–1,000 ms.
> - Limits worth knowing: ~1 MB thread stack, 85,000-byte LOH threshold, 100 pooled connections per connection string, ~2,100 SQL Server parameters, 5-second default shutdown timeout, 5-minute JWT clock skew, 29-second API Gateway timeout.
> - Diagnostic command cheat sheet and a "which should I use?" decision index.
> - One-paragraph compressions of the types, APIs and caching guides.


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Using the latency table to estimate a design

Should a pricing page call Redis 40 times per request, or load one blob?

*Back-of-envelope*

```text
memory cache read      ~100 ns
Redis round trip       ~0.5–1 ms in the same zone
40 sequential Redis calls  = 20–40 ms per request
1 call with MGET / one hash = ~1 ms
-> batch the keys, or hold the data in an L1 memory cache with a short TTL
```

> **Lesson:** Orders of magnitude settle most design questions before any benchmark.

### Appendix

<p class="src">From <em>.NET Backend Deep Dive</em> · Appendix</p>

<a id="dn-239"></a>

#### A. Latency numbers worth memorizing (orders of magnitude)

| Operation | Time | Human-scale analogy |
|---|---|---|
| Reading a variable in L1 CPU cache | ~1 ns | 1 second |
| RAM access | ~100 ns | 1.5 minutes |
| `IMemoryCache` read (dictionary lookup) | ~50–100 ns | — |
| Uncontended lock | ~20 ns | — |
| Small object allocation (gen 0) | ~10–20 ns | — |
| Gen 0 GC | < 1 ms | — |
| Gen 2 GC (blocking) | 10–500 ms | **your p99 culprit** |
| Redis GET (same datacenter) | ~0.5–2 ms | — |
| Indexed SQL query, small result | ~1–10 ms | — |
| Full scan of a large table | 100 ms – seconds | — |
| Internal HTTP call | ~5–50 ms | — |
| Internet HTTP call | ~50–500 ms | — |
| .NET Lambda cold start (JIT) | 1–5 s | — |
| .NET Lambda cold start (AOT) | ~100–300 ms | — |

**What this means:** one Redis call ≈ 10,000 memory-cache reads. An N+1 over 100 rows wastes 100–1000ms. Micro-optimizing allocations is pointless while you still have one unnecessary query.

<a id="dn-240"></a>

#### B. Common allocation costs

| Code | Allocation |
|---|---|
| `s += x` in a loop of n | n intermediate strings — O(n²) bytes |
| `list.Where(x => x.Id == id)` with a closure | 1 closure object + 1 iterator |
| `items.Where(static x => x.IsActive)` | 1 iterator (no closure) |
| `string.Substring` | A new string |
| `str.AsSpan(0, 10)` | **Zero** |
| `await` on an already-completed task | **Zero** (fast path) |
| `await` on a task that truly suspends | ~1 boxed state machine + 1 Task |
| `ValueTask` completing synchronously | **Zero** |
| `new List<T>()` then 100 Adds | ~7 array resizes (4, 8, 16…128) → use `new List<T>(100)` |

<a id="dn-241"></a>

#### C. Diagnostic command cheat sheet

```bash
# .NET runtime
dotnet-counters monitor -p <pid> System.Runtime Microsoft.AspNetCore.Hosting
dotnet-counters monitor -p <pid> --counters System.Runtime[cpu-usage,gc-heap-size,threadpool-queue-length]
dotnet-gcdump collect -p <pid>
dotnet-dump collect -p <pid> && dotnet-dump analyze core_*
dotnet-trace collect -p <pid> --profile cpu-sampling --duration 00:00:30
dotnet-stack report -p <pid>

# Inside dotnet-dump analyze
> clrstack -all          # stacks for every thread
> dumpheap -stat         # heap statistics by type
> gcroot <addr>          # who is holding this object
> syncblk                # lock contention
> threadpool             # thread pool state

# Containers
kubectl top pod
kubectl logs <pod> --previous            # logs from the crashed instance
kubectl describe pod <pod>               # OOMKilled? restart count?
```

```sql
-- Quick SQL Server checks
EXEC sp_WhoIsActive;                                    -- (install it) what's running now
SELECT * FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;   -- who's blocked
DBCC SHOW_STATISTICS('Orders', 'IX_Orders_CustomerId');
SET STATISTICS IO, TIME ON;                             -- measure logical reads
```

<a id="dn-242"></a>

#### D. Limits and numbers worth memorizing

| Thing | Limit |
|---|---|
| Thread stack | ~1MB (1MB default, 8MB main thread on some Linux configs) |
| Large Object Heap threshold | 85,000 bytes |
| Single object maximum | 2GB (without `gcAllowVeryLargeObjects`) |
| Default ADO.NET connection pool | 100 per connection string |
| SQL Server parameters per query | ~2,100 (breaks large `Contains`) |
| SQL Server index key columns | 32 columns / 1,700 bytes (900 for clustered) |
| Thread pool thread injection rate | ~1–2 per second beyond minimum |
| Default `HostOptions.ShutdownTimeout` | 5 seconds |
| Default JWT `ClockSkew` | 5 minutes |
| AWS Lambda timeout / payload | 15 min / 6MB sync, 256KB async |
| API Gateway integration timeout / payload | 29s / 10MB (REST) |
| S3 single PUT / multipart part | 5GB / 5MB minimum part |
| Redis command execution | Single-threaded — one slow command blocks all |

<a id="dn-243"></a>

#### E. Decision index — "which should I use?"

| Question | Answer | Part |
|---|---|---|
| Sequential or parallel DB calls? | Sequential unless each query is >100ms and traffic is low | 3.1 |
| `Include` or `Select`? | `Select` for reads; `Include` only when you need the entity | 2.2 |
| Single or split query? | Split for multiple/large collections; single for one small one | 2.2.2 |
| `Task` or `ValueTask`? | `Task` by default; `ValueTask` only on hot paths that usually complete synchronously | 8.3 |
| `class` or `record`? | `record` for data identified by its values; `class` for entities and services | 7.2 |
| `record` or `readonly record struct`? | The struct only for small immutable values where allocation matters | 7.2 |
| Memory or distributed cache? | Distributed (or hybrid) the moment you have more than one instance | 6.1 |
| Optimistic or pessimistic locking? | Optimistic unless the row is genuinely contended | 42.2 |
| Queue or topic? | Queue for commands (one handler); topic for events (many) | 4.2 |
| Kafka, RabbitMQ or SQS? | Replay/volume → Kafka; complex routing → Rabbit; simple durability on AWS → SQS | 31 |
| Relational or document store? | Relational unless you have one narrow access pattern at very high scale | 43.4 |
| Serverless or containers? | Serverless for spiky/idle; containers above a few hundred sustained rps | 9.2 |
| Minimal API or controllers? | Organizational choice; minimal for small services and AOT | 24.4 |
| REST, gRPC or GraphQL? | REST public; gRPC internal and chatty; GraphQL for heterogeneous clients | 29.4 |
| AOT or ReadyToRun? | AOT for Lambda/CLI; R2R for long-running services | 39.1 |
| `IOptions`, `Snapshot` or `Monitor`? | `Monitor` in singletons and background services; `Snapshot` in scoped | 21.2 |
| Liveness or readiness check? | Process-only in liveness; dependencies in readiness | 27.1 |
| `decimal` or minor-unit integers? | Either, consistently. Never `double` | 55.1 |
| Soft or hard delete? | Soft for recoverability; hard (or crypto-shred) when erasure is required | 53.1 |
| Microservices or modular monolith? | Monolith until independent deployment is the actual constraint | 29.5 |

### The whole thing, compressed

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The whole thing, compressed</p>

<a id="be-b302"></a>

#### The whole thing, compressed

**A type is a contract about memory**, and the value/reference distinction decides copying, mutation, equality, and lifetime. Get that wrong and you get silent bugs: discarded struct mutations, unreachable dictionary entries, reference equality where you wanted value equality.

**Every data structure buys one operation's speed with another's**, so choosing one is choosing what you care about. `Dictionary` buys lookup with memory and unordered iteration. `SortedList` buys memory and locality with insert cost. `LinkedList` buys insertion with cache misses — a bad trade almost always.

**And measure**, because at the sizes most code actually runs at, the constant factors decide the winner and the asymptotics don't.

------------------------------------------------------------------------

*Caveats: code is illustrative and uncompiled; the from-scratch implementations are simplified for clarity and omit edge cases the BCL handles. Content reflects knowledge through May 2026 — verify .NET 9/10-era APIs against current documentation. Sizes and layout figures assume 64-bit; 32-bit halves the pointer-sized ones.*

<div class="also">

async / await → APIs → Caching → all references →

</div>

### The whole thing, compressed

<p class="src">From <em>Backend Engineering</em> · APIs · The whole thing, compressed</p>

<a id="be-e558"></a>

#### The whole thing, compressed

An API is **a contract between programs that cannot see inside each other, carried over a channel that fails ambiguously.**

The contract half gives you resources, schemas, status codes, serialization, versioning, and documentation — everything about *agreeing on meaning*.

The unreliable-channel half gives you idempotency, retries, backoff, timeouts, circuit breakers, and rate limits — everything about *not making failure worse*.

When a design decision seems arbitrary, ask which half it belongs to. It's almost always one or the other.

------------------------------------------------------------------------

*Caveats: code and examples are illustrative and uncompiled. Content reflects knowledge through May 2026 — verify RFC numbers, library APIs, and .NET specifics against current sources. Several sections (versioning strategy, 422 vs 400, envelope shape, REST purism) encode judgment calls where informed people genuinely disagree; the reasoning is given so you can decide differently.*

<div class="also">

async / await → C# types → Caching → all references →

</div>

### The whole thing, compressed

<p class="src">From <em>Backend Engineering</em> · Caching · The whole thing, compressed</p>

<a id="be-f625"></a>

#### The whole thing, compressed

A cache is **a second copy of data placed somewhere cheaper to reach.**

It works only because access costs and access frequencies are both wildly uneven — so the work is maximizing hit rate: key design, eviction, sizing, layering.

It is dangerous only because a copy can disagree with the original — so the work is bounding the divergence window: TTLs, invalidation, versioned keys, and the failure modes that appear when many copies go wrong at once.

Every technique here is one or the other. And when invalidation gets hard, the reliable move is almost always the same: **stop invalidating and start versioning** — change the key, whether that's `user:42:v7`, `main.a1b2c3.js`, or a surrogate-key purge. Immutable data cannot go stale.

------------------------------------------------------------------------

*Caveats: code is illustrative and uncompiled. Content reflects knowledge through May 2026 — verify library APIs, Redis defaults, and framework specifics against current documentation. Latency figures are order-of-magnitude, not benchmarks. Several areas (Redlock's safety, normalized vs document caching, when a cache is worth its complexity) are genuinely contested; the reasoning is given so you can decide differently.*

<div class="also">

async / await → APIs → C# types → all references →

</div>
