---
id: "14-5"
number: "14.5"
title: "What actually goes wrong: field guide and checklists"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 12127
minutes: 53
---

# 14.5 What actually goes wrong: field guide and checklists

> **Quick look.** What actually goes wrong in production, ranked, plus review checklists.
>
> - The top ten by frequency × damage: N+1 queries, sync-over-async, per-instance caches after scaling out, unbounded growth, non-idempotent message handling, missing ownership checks (IDOR), check-then-act races, breaking schema changes in rolling deploys, database checks in liveness probes, untested backups.
> - Almost all appear only under conditions missing on a laptop: concurrency, scale, many instances, real data volume, partial failure, time.
> - Three questions before merging: What if this runs twice? What if there are a hundred at once? What if the thing I call is down or slow?
> - PR checklist: no .Result or .Wait(), tokens flow to every call, no async void, bounded queries, DTOs at the boundary.
> - A trap index collects all 214 traps flagged across the library, each linked to its explanation.


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The three questions on a real pull request

A PR adds a "resend ticket" button that emails the ticket and records the action.

*Asking them*

```text
Runs twice?        double-click sends two emails -> idempotency key per click, button disabled while pending
A hundred at once? support bulk-resends 500 tickets -> queue the emails, bounded worker, no HTTP calls inside a loop
Dependency down?   email provider times out -> outbox row in the same transaction, retry with backoff, DLQ alert
```

> **Lesson:** Run, repeat, concurrency and failure: three questions catch most production bugs before merge.

### What Actually Goes Wrong: A Field Guide

<p class="src">From <em>.NET Backend Deep Dive</em> · What Actually Goes Wrong: A Field Guide</p>

<a id="dn-236"></a>

#### The top ten, by frequency × damage

A consolidated frequency list. If you retain nothing else from this document, retain this ordering — it reflects what causes real production pain, weighted by how often it happens.

**1. N+1 queries.** The single most common performance defect in any ORM codebase. Invisible in development with ten rows; catastrophic with ten thousand. *Guard it with a command-counting test (17.2), not vigilance.*

**2. Sync-over-async.** One `.Result` in a hot path starves the thread pool under load. The symptom — low CPU, queued requests — is one almost nobody diagnoses correctly the first time. *Ban it with analyzer `CA1849` as an error (33.2).*

**3. Per-instance caching after scaling out.** Works perfectly on one pod, produces "the data is wrong sometimes" on three. *The bug report is always vague, which is why it survives for months (6.1).*

**4. Unbounded growth.** A static dictionary, an unsubscribed event, a captive `DbContext`, an unbounded channel or queue. All present identically: memory climbs, pod restarts, nobody knows why. *`gcroot` answers it in one command (13.B).*

**5. Non-idempotent message handling.** At-least-once delivery is a guarantee, not an edge case. Duplicate emails are embarrassing; duplicate charges are a refund process and a trust problem. *(4.4, 12.3)*

**6. Missing authorization on a resource.** IDOR — role checks that never verify ownership. The most common *serious* API vulnerability, and no framework prevents it for you. *(23.2)*

**7. Check-then-act races.** "Verify then insert", "check stock then decrement". Passes every test, fails under concurrency. *The constraint is the correctness mechanism (42.1).*

**8. Breaking schema changes under rolling deploys.** Renaming a column takes production down because both versions run simultaneously for ninety seconds. *Expand-and-contract is mandatory, not a nicety (14.7).*

**9. Database checks in a liveness probe.** A brief DB blip restarts every pod at once; they return with cold caches and finish off the recovering database. *A dependency hiccup becomes a full outage (27.1).*

**10. Untested backups.** Discovered at the worst possible moment, and the failures are mundane — wrong database, missing key, fourteen-hour restore nobody had timed. *(54.2)*

<a id="dn-237"></a>

#### The pattern behind the pattern

Look at that list again. Almost none of these are failures of knowledge — they're failures that **only appear under conditions absent from the developer's machine**: concurrency, scale, multiple instances, real data volume, partial failure, and time.

That observation is the most useful thing in this document, because it tells you where to spend effort:

| Condition missing locally | How you catch it anyway |
|---|---|
| Concurrency | Load tests; a deliberate parallel test; constraints in the database |
| Multiple instances | Run two locally; ask "what if this state isn't shared?" of every cache |
| Data volume | Seed realistically, with realistic distribution (38.2) |
| Partial failure | Chaos: kill a dependency in staging and watch |
| Time | `TimeProvider` and explicit clock tests (32.1) |
| Rolling deploys | Assert backward compatibility in CI, not in review |

**The meta-rule: make the failure mode impossible or automatically detected, rather than relying on someone remembering.** A unique index beats a code review comment. A command-counting test beats a style guide. An analyzer-as-error beats a wiki page. Every item in the list above has a mechanical guard, and the mechanical guard is always cheaper than the incident.

<a id="dn-238"></a>

#### The three questions that catch most of it

Before merging anything that touches data or concurrency:

1. **What happens if this runs twice?** (idempotency, at-least-once, retries, double-submit)
2. **What happens if there are a hundred of these at once?** (races, pool exhaustion, N+1, lock contention)
3. **What happens if the thing I'm calling is down or slow?** (timeout, retry, circuit breaker, fallback, partial state)

Most of the fifty-seven parts of this document are elaborations of those three questions.

---

### Code Review Checklist & Anti-pattern Table

<p class="src">From <em>.NET Backend Deep Dive</em> · Code Review Checklist & Anti-pattern Table</p>

<a id="dn-88"></a>

#### Backend .NET PR checklist

**Async**

- [ ] No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`
- [ ] `CancellationToken` flows all the way to the DB/HTTP call
- [ ] No `async void` (except event handlers)
- [ ] Libraries use `ConfigureAwait(false)`

**EF Core**

- [ ] Read-only queries use `AsNoTracking()`
- [ ] No queries inside loops (N+1)
- [ ] Projections instead of loading whole entities for a few columns
- [ ] Multiple collection `Include`s use `AsSplitQuery()`
- [ ] No `ExecuteDeleteAsync` on soft-deletable entities
- [ ] Paging has a deterministic `OrderBy` (without one, order is undefined)

**DI**

- [ ] No scoped services injected into singletons
- [ ] `BackgroundService` creates a scope per iteration
- [ ] `DbContext` is never shared across `Task.WhenAll`

**Security**

- [ ] No PII or secrets in logs
- [ ] Authorization happens before issuing a presigned URL or fetching a resource by ID
- [ ] Input is validated; no string-concatenated SQL
- [ ] Cache keys are scoped per user/tenant for private data

**Operability**

- [ ] Logs carry enough context to debug (IDs, tenant, correlation)
- [ ] External calls have timeouts, retries and a circuit breaker
- [ ] Message consumers are idempotent
- [ ] Long-running work isn't inside a transaction

### Worked cases: systems and delivery

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Worked cases: systems and delivery</p>

<a id="be-m314"></a>

#### Trap index

The 214 traps flagged throughout the guide, collected for a review pass. Each links back to the section that explains it.

| §                | Trap                                                                                                                                                                                                                                                                                           |
|------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [§1](00-02-backend-development-in-one-sitting.md#be-m1)     | **Where this guide will go out of date fastest:** EF Core behaviour that changed between major versions, .NET APIs introduced in 8 and 9, and anything about a specific database engine's optimiser. The reasoning…                                                                            |
| [§6](01-07-the-operating-system.md#be-m6)     | **Container memory limits kill, they do not slow** A process exceeding its cgroup limit is terminated by the OOM killer with no exception, no stack trace, and nothing in your application logs — the process simply…                                                                          |
| [§9](01-06-storage-and-files.md#be-m9)     | **Handle exhaustion looks like something else** Leaking file or socket handles produces "too many open files" or a failure to connect — often reported far from the leak, in unrelated code that simply happened to…                                                                           |
| [§13](06-02-ip-addresses-and-routing.md#be-m13)   | **MTU and fragmentation** Most links carry at most ~1500 bytes per packet. A larger IP packet must be fragmented and reassembled, and if any fragment is lost the whole packet is lost. Worse,…                                                                                                |
| [§14](06-03-tcp-and-udp.md#be-m14)   | **Head-of-line blocking** Because TCP guarantees order, one lost packet stalls everything behind it on that connection until it is retransmitted — even data that already arrived intact. On a…                                                                                                |
| [§16](06-04-dns.md#be-m16)   | **The classic .NET DNS bug** A static HttpClient is correct advice — it avoids socket exhaustion — but a long-lived HttpClient keeps its connections open indefinitely and therefore never…                                                                                                    |
| [§17](06-05-tls.md#be-m17)   | **Never do this, including in staging:** handler.ServerCertificateCustomValidationCallback = (\_, \_, \_, \_) =\> true; It reliably survives into production, because it makes the error go away and nothing later…                                                                            |
| [§19](06-03-tcp-and-udp.md#be-m19)   | **A "connection" in a pool is not free while idle** Each holds a socket, a file descriptor (§9), kernel send and receive buffers, and on the server side a matching set. A pool of 100 connections across 20 service…                                                                          |
| [§23](01-05-memory.md#be-m23)   | **This is why recursion over data of unbounded shape is dangerous** A recursive walk of a 50,000-node category tree is a crash, not an exception you can recover from. Any traversal whose depth is driven by data rather than by code…                                                        |
| [§24](02-07-memory-management-and-garbage-collection.md#be-m24)   | **IDisposable is unrelated to memory** The GC handles memory; Dispose handles everything else — file handles, sockets, database connections, OS locks. Those are scarce, unmanaged resources the collector…                                                                                    |
| [§27](02-04-values-variables-and-references.md#be-c306)  | **Trap** A mutable struct exposed through a property returns a copy. obj.Position.X = 5; does not compile for a property (good), but list\[0\].X = 5 on a List\<struct\> also…                                                                                                                 |
| [§29](01-05-memory.md#be-m29)   | **Which is why x?.y?.z is not the cost you think** Each ?. adds a real compare-and-branch, unlike an ordinary dereference — but branches are cheap and well predicted. The genuine cost of a long navigation chain is…                                                                         |
| [§31](01-05-memory.md#be-m31)   | **Trap** Large structs are expensive to copy. Past roughly 16–24 bytes, passing by value starts to lose to passing a reference. Use in parameters (in Matrix4x4 m) to pass a…                                                                                                                  |
| [§32](01-04-the-processor.md#be-c303)  | **new in a generic method with where T : new() is not this fast** For reference types the runtime goes through Activator.CreateInstance, roughly an order of magnitude slower than a direct new, and it shows up immediately in a…                                                             |
| [§36](01-05-memory.md#be-m36)   | **Two measurement traps** GC.GetTotalMemory(forceFullCollection: true) triggers a blocking full collection — useful in a diagnostic console app, never in a running service. And measuring…                                                                                                    |
| [§38](02-02-compilers-interpreters-and-jit.md#be-m38)   | **The version in metadata is not the file name** Assembly identity is name + version + culture + public key, and a binding failure reports the requested identity, not the file you think you shipped. On a legacy…                                                                            |
| [§39](02-02-compilers-interpreters-and-jit.md#be-m39)   | **Diamond dependency conflicts are resolved by unification, not by magic** If two packages want different versions of the same library, the build picks one and binds both to it. That is usually fine and occasionally produces a…                                                            |
| [§40](02-02-compilers-interpreters-and-jit.md#be-m40)   | **This is why naive benchmarking lies** A Stopwatch loop measures tier 0 code, then the transition, then tier 1 — blended into one number that describes nothing. BenchmarkDotNet warms up until the code…                                                                                     |
| [§42](02-02-compilers-interpreters-and-jit.md#be-m42)   | **Trim and AOT warnings are not advisory** IL2026/IL3050 mean "this will break at runtime and the build cannot tell you where". Enable \<IsAotCompatible\>true\</IsAotCompatible\> on libraries so the analyzers…                                                                              |
| [§43](02-02-compilers-interpreters-and-jit.md#be-m43)   | **Static initialisers deadlock** If A's initialiser touches B and B's touches A, and two threads enter from opposite sides, both block forever holding the other's type-initialisation lock. There is…                                                                                         |
| [§45](01-02-numbers-inside-the-machine.md#be-m45)   | **Trap** The classic overflow bug is (low + high) / 2 in a binary search. Write low + (high - low) / 2. The second classic is multiplying two ints that represent sizes: int…                                                                                                                  |
| [§46](01-02-numbers-inside-the-machine.md#be-m46)   | **Trap** A NaN key in a Dictionary\<double, T\> can be stored but never found by == semantics — though it is found by the dictionary, because dictionaries use Equals. Sorting…                                                                                                                |
| [§48](01-03-text-is-numbers-too.md#be-m48)   | **Trap** Truncating a string to N characters with Substring(0, n) can split a surrogate pair and produce invalid UTF-16, or split a combining sequence and move an accent…                                                                                                                     |
| [§49](02-03-types.md#be-m49)   | **Trap** Enums are not validated. (Status)200 compiles and runs. Any value that came from a database, a query string, or JSON must be checked with Enum.IsDefined — and note…                                                                                                                  |
| [§51](02-03-types.md#be-m51)   | **Trap in a legacy codebase** Enabling NRT project-wide on an old solution produces thousands of warnings and tempts blanket ! suppression, which is worse than not enabling it. Enable per-file…                                                                                              |
| [§52](02-03-types.md#be-m52)   | **Trap** Storing local times in a database is a data-loss bug: during a DST fall-back, one local hour occurs twice and you cannot recover which. Store UTC (or timestamptz),…                                                                                                                  |
| [§53](02-03-types.md#be-m53)   | **Trap** SQL Server's uniqueidentifier sort order is not byte order — it compares the last 6 bytes first. So a Guid sequence that looks sorted in .NET may not be sorted in…                                                                                                                   |
| [§54](02-03-types.md#be-m54)   | **Trap** A record's generated equality compares all fields, including collections — by reference. Two records holding equal-but-distinct List\<T\>s are not equal. Override…                                                                                                                   |
| [§56](02-03-types.md#be-m56)   | **The mutable-key bug** Put an object in a HashSet, mutate a field used in its hash, and the object is now in the wrong bucket. It becomes unfindable and un-removable, and it will…                                                                                                           |
| [§59](02-03-types.md#be-m59)   | **new() is a trap in hot paths** For reference types the runtime calls Activator.CreateInstance, which is roughly an order of magnitude slower than a direct new. Pass a Func\<T\> factory instead, or…                                                                                        |
| [§60](02-03-types.md#be-m60)   | **Variance only applies to reference conversions** IEnumerable\<int\> is not an IEnumerable\<object\>, because that would require boxing each element, which is a representation change rather than a reinterpretation. Use…                                                                   |
| [§61](02-03-types.md#be-m61)   | **Cost note** Because each value-type instantiation compiles separately, a generic-math helper used over eight numeric types produces eight copies of the code. Fine for a few…                                                                                                                |
| [§63](02-03-types.md#be-m63)   | **Not translatable to SQL** A switch expression inside a Where on an IQueryable will not translate. Simple ternaries and ?: chains usually do (they become CASE WHEN); switch expressions…                                                                                                     |
| [§65](01-02-numbers-inside-the-machine.md#be-m65)   | **Do not store future events as UTC instants** "The weekly meeting at 09:00 Ho Chi Minh time" converted to UTC once and stored is wrong the moment a government changes its DST rules or offset — which happens…                                                                               |
| [§67](01-02-numbers-inside-the-machine.md#be-m67)   | **The offset must be recomputed per occurrence** A weekly 09:00 meeting spans a DST transition twice a year; computing one offset at the series start and adding seven days repeatedly drifts the wall-clock time by…                                                                          |
| [§68](04-06-testing.md#be-m68)   | **Wall clock versus monotonic** GetUtcNow() can jump backwards — NTP correction, a VM resuming from a snapshot, a manual clock change. Any measurement of elapsed time must use…                                                                                                               |
| [§69](01-02-numbers-inside-the-machine.md#be-m69)   | **Rounding at the wrong level changes the total** Rounding each line and summing does not equal summing then rounding — on a 200-line invoice the two can differ by more than a cent, and both are defensible. Tax…                                                                            |
| [§71](01-02-numbers-inside-the-machine.md#be-m71)   | **Deriving both parts by rounding independently breaks the identity** If you compute net = Round(gross / 1.1) and vat = Round(gross \* 0.1 / 1.1), then net + vat can differ from gross by a cent. Always compute one component and obtain…                                                    |
| [§71](01-02-numbers-inside-the-machine.md#be-m71)   | **Multi-rate baskets need per-rate subtotals** Applying an invoice-level discount to a basket containing 10% and 5% VAT lines requires allocating the discount across lines first (§70), then taxing each group at…                                                                            |
| [§72](01-02-numbers-inside-the-machine.md#be-m72)   | **Watch the type behind the unit** A readonly record struct wrapping a decimal is free and exact; wrapping a double inherits every floating-point trap (§46), and making it a class adds an allocation…                                                                                        |
| [§73](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m73)   | **Modulo introduces bias** rng.Next() % 100 is not uniform: int.MaxValue is not a multiple of 100, so the low values occur slightly more often. The bias is small for small ranges and large…                                                                                                  |
| [§74](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m74)   | **A "random" order that must be stable is not a shuffle** Randomising search results per request means a user paging through them sees duplicates and gaps — the same class of bug as a non-deterministic ORDER BY (§161).…                                                                    |
| [§75](03-03-hash-tables.md#be-m75)   | **Non-cryptographic means "an attacker can construct collisions"** xxHash is superb for distribution and useless as a security primitive. If an adversary chooses the input — a filename, an ETag, an integrity check on an upload —…                                                          |
| [§76](03-03-hash-tables.md#be-m76)   | **Do not use a checksum where you need a version** A hash tells you two things differ; it cannot tell you which is newer. Concurrency control needs an ordering — a rowversion, a sequence, or a timestamp with a…                                                                             |
| [§77](03-03-hash-tables.md#be-m77)   | **Balanced keys are not balanced load** Consistent hashing distributes keys evenly; it says nothing about traffic. One celebrity tenant, one hot product, one runaway API consumer will concentrate on a…                                                                                      |
| [§78](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)   | **Sequential ids leak information** Exposing /orders/1042 tells any customer roughly how many orders you have processed, and lets them enumerate resources by incrementing — which is only stopped by…                                                                                         |
| [§78](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)   | **Natural keys change** Email addresses, SKUs, national ids, ISBNs, tax numbers — every one of them is "guaranteed stable" until the day it is not, and by then it is a foreign key in…                                                                                                        |
| [§79](01-03-text-is-numbers-too.md#be-m79)   | **Do not intern at runtime as a memory optimisation** The intern pool is rooted for the life of the process — interned strings never get collected. For deduplicating strings read from a database or a file, use your own…                                                                    |
| [§80](01-03-text-is-numbers-too.md#be-m80)   | **The Turkish-I bug** In tr-TR, "i".ToUpper() is "İ", not "I". Code that upper-cases a header name or a config key to compare it breaks on a Turkish-locale server. Case-insensitive…                                                                                                          |
| [§82](01-03-text-is-numbers-too.md#be-m82)   | **Constraint** Span\<T\> is a ref struct. You cannot use it across an await, in an iterator, in a lambda, or as a class field. Use Memory\<T\> / ReadOnlyMemory\<T\> for those cases and…                                                                                                      |
| [§83](01-03-text-is-numbers-too.md#be-m83)   | **Legacy trap** Code pages beyond the built-in set (Windows-1252, Shift-JIS) require System.Text.Encoding.CodePages and…                                                                                                                                                                       |
| [§84](01-03-text-is-numbers-too.md#be-m84)   | **The static Regex.IsMatch(input, pattern) overloads** use a small internal cache keyed by the pattern (15 entries by default). Exceed it with dynamically-built patterns and every call re-parses. Either use instance…                                                                       |
| [§85](01-03-text-is-numbers-too.md#be-m85)   | **Construct it once** SearchValues.Create is deliberately expensive — it analyses the set to pick an implementation. Calling it inside the method that searches makes the code slower than…                                                                                                    |
| [§86](01-03-text-is-numbers-too.md#be-m86)   | **Diacritic stripping is not universal** Vietnamese đ/Đ is a distinct letter, not d plus a mark, so decomposition leaves it untouched — you need an explicit mapping. German ß folds to ss only under…                                                                                         |
| [§87](01-03-text-is-numbers-too.md#be-m87)   | **It does not scale by itself** Comparing every pair in a 100,000-row table is 5&times;109 distance computations. Always block first: only compare records that share a cheap key — same first two…                                                                                            |
| [§88](01-06-storage-and-files.md#be-m88)   | **Do not blit a struct to the wire** MemoryMarshal.AsBytes over a struct is fast and correct only when both ends share the same architecture, the same padding, and the same field order — and the CLR…                                                                                        |
| [§89](01-06-storage-and-files.md#be-m89)   | **FileOptions.Asynchronous is not the default,** and without it ReadAsync on a FileStream performs synchronous I/O on a thread pool thread. Under load that is thread pool starvation with an async-looking call…                                                                              |
| [§91](01-06-storage-and-files.md#be-m91)   | **Two things not to do** Do not compress data that is already compressed — images, video, archives, encrypted blobs — you spend CPU to grow the payload slightly. And do not decompress…                                                                                                       |
| [§93](08-08-serialization-and-data-formats.md#be-m93)   | **proto3 has no null for scalars** An unset int64 and a set-to-zero int64 are indistinguishable unless you mark the field optional or use a wrapper type. Modelling "quantity not supplied" as 0 is a…                                                                                         |
| [§94](08-08-serialization-and-data-formats.md#be-m94)   | **The registry becomes a hard dependency** If consumers cannot reach it, they cannot decode. Cache schemas aggressively and locally, treat a registry outage as a degraded-but-running condition, and never…                                                                                   |
| [§95](02-02-compilers-interpreters-and-jit.md#be-m95)   | **The GC moves objects; native code does not know that** Passing a managed array by pointer works only because the marshaller pins it for the duration of the call. Store that pointer natively and use it after the call…                                                                     |
| [§97](01-03-text-is-numbers-too.md#be-m97)   | **Currency formatting does not convert currency** ToString("C") applies the culture's symbol, so a USD amount rendered under vi-VN displays as đồng — the same number with the wrong unit, which is worse than an…                                                                             |
| [§99](03-02-arrays-and-lists.md#be-m99)   | **Trap** Removing items inside a foreach throws InvalidOperationException. Removing inside a forward for loop silently skips elements. Iterate backwards, or use…                                                                                                                              |
| [§104](03-05-heaps-and-priority-queues.md#be-b280) | **Trap** PriorityQueue is not stable — equal priorities come out in arbitrary order. If insertion order matters for ties, make the priority a tuple: (priority,…                                                                                                                               |
| [§106](05-02-threads-and-locks.md#be-b282) | **Traps** Count on a ConcurrentDictionary takes all the locks — do not call it in a hot loop. Enumeration gives a moment-in-time snapshot that may include or omit concurrent…                                                                                                                 |
| [§107](03-02-arrays-and-lists.md#be-m107) | **Trap** ImmutableArray\<T\> is a struct wrapping an array, so default(ImmutableArray\<T\>) has a null inner array and throws on almost any use. Check IsDefault or always…                                                                                                                    |
| [§109](03-02-arrays-and-lists.md#be-m109) | **Trap** IReadOnlyList\<T\> is not a defensive copy. If you return the same List\<T\> you hold, the caller can cast it back to List\<T\> and mutate your state. For true isolation…                                                                                                            |
| [§113](02-07-memory-management-and-garbage-collection.md#be-m113) | **Traps** Rent returns an array of at least the requested size — never use buffer.Length as your data length. Always Return in a finally. Never keep a reference after…                                                                                                                        |
| [§114](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m114) | **The bar is high, and it is correctness, not cleverness** A custom structure needs the equality contract right (§56), the growth policy right, the enumerator right, and thread-safety documented. Dictionary\<K,V\> has had two…                                                             |
| [§115](03-07-searching-and-sorting.md#be-m115) | **Three ways to get this wrong** (lo + hi) / 2 overflows past ~1 billion elements (§45). BinarySearch on an unsorted list returns garbage silently — no exception. And the comparer used to search…                                                                                            |
| [§116](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m116) | **Not thread-safe** Concurrent Add corrupts \_head and \_count independently, producing torn state that no exception reports. For a multi-producer or multi-consumer version, use…                                                                                                             |
| [§118](03-04-trees.md#be-m118) | **Memory is the catch** A node per character with a Dictionary in each is enormously wasteful — a naive trie over a million words can cost hundreds of megabytes. Real implementations…                                                                                                        |
| [§119](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m119) | **Constraints to accept up front** You cannot remove an item (clearing bits would break other members — use a counting Bloom filter if you must). You cannot enumerate it. You cannot resize it; sizing…                                                                                       |
| [§121](01-01-bits-and-bytes.md#be-m121) | **Sparse data defeats it** A bitset over ten million possible ids costs 1.25 MB whether it holds 10 members or 10 million. If the set is sparse relative to its universe, HashSet\<int\> or a…                                                                                                 |
| [§122](03-06-graphs.md#be-m122) | **Recursion depth** A recursive depth-first traversal over a deep chain — a 50,000-node linked category list, for instance — overflows the 1 MB stack and takes the process down with a…                                                                                                       |
| [§124](03-05-heaps-and-priority-queues.md#be-m124) | **PriorityQueue is not stable** (§104). Equal scores come out in arbitrary order, so a "top 10" over ties is non-deterministic between runs. If that matters — and for anything a user sees, it does…                                                                                          |
| [§126](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m126) | **None of this applies to money** decimal is exact for decimal fractions, so compensated summation is unnecessary and misleading there (§47). These techniques are for double-valued measurements —…                                                                                           |
| [§127](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m127) | **It only estimates cardinality** You cannot ask whether a specific item is in the set (use a Bloom filter — §119), you cannot remove items, and you cannot enumerate it. And because it depends on…                                                                                           |
| [§128](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m128) | **Rare keys are the least accurate** The error bound is proportional to the total stream count, so a key seen 3 times in a stream of 10 million with &epsilon;=0.001 may be reported as 10,000. The…                                                                                           |
| [§129](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m129) | **Random is not thread-safe** and a shared instance used concurrently silently returns correlated or repeated values — which quietly destroys the uniformity you are relying on. Use Random.Shared…                                                                                            |
| [§130](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m130) | **Percentiles do not average** The p99 of the union of two servers' data is not the mean of their two p99s — that computation is meaningless and it is what most naive dashboards display. You must…                                                                                           |
| [§131](05-02-threads-and-locks.md#be-m131) | **Torn reads** Reads and writes are atomic only for types up to the platform word size — int, bool, references, and long/double on 64-bit. A decimal, a DateTime, a Guid, or any…                                                                                                              |
| [§132](05-02-threads-and-locks.md#be-m132) | **Never lock on these:** this (callers can lock your instance and deadlock you), a Type object (shared across the whole AppDomain), a string (interned literals are shared process-wide), or…                                                                                                  |
| [§132](05-02-threads-and-locks.md#be-m132) | **Never await inside a lock** It does not compile with the Lock type and it is a bug with Monitor: the lock is thread-affine, the continuation may resume on a different thread, and you get an…                                                                                               |
| [§133](05-02-threads-and-locks.md#be-m133) | **ReaderWriterLockSlim is almost never the right answer** It is several times more expensive to acquire than a plain lock even in the uncontended read case, it has recursion and upgrade rules that produce deadlocks, and it…                                                                |
| [§134](04-04-immutability-and-pure-functions.md#be-m134) | **Consistency across two snapshots** Reading two independently-published tables gives you no guarantee they are from the same generation. If several tables must agree, put them in one immutable object…                                                                                      |
| [§135](05-05-parallelism.md#be-m135) | **Always complete the writer, including on failure** Complete(exception) makes every consumer's ReadAllAsync throw that exception. Forgetting it leaves consumers awaiting forever — a hang with no stack trace and no…                                                                        |
| [§136](05-03-async-and-await.md#be-m136) | **Async over sync is not a fix** Wrapping a blocking call in Task.Run does not create asynchrony — it moves the blocking onto a pool thread, which is strictly worse under load because you have…                                                                                              |
| [§137](05-03-async-and-await.md#be-m137) | **Cancelling a database call does not undo it** Cancellation aborts the command and, on most providers, tears down the connection — but a write that already committed stays committed, and an aborted write inside…                                                                           |
| [§139](03-10-collections-and-linq-in-net.md#be-m139) | **The worst version of this** is a deferred query over a database context that is disposed before enumeration — you get ObjectDisposedException from a line nowhere near the query. Any method…                                                                                                |
| [§140](03-10-collections-and-linq-in-net.md#be-b295) | **The single most expensive LINQ mistake:** crossing the boundary accidentally and pulling a whole table into memory.                                                                                                                                                                          |
| [§143](03-10-collections-and-linq-in-net.md#be-m143) | **The deferred-GroupBy tax** GroupBy is deferred, so enumerating the result twice rebuilds the entire lookup. If you iterate groups more than once — common when you compute a count and then…                                                                                                 |
| [§144](03-10-collections-and-linq-in-net.md#be-m144) | **Trap on value types** FirstOrDefault() on an IEnumerable\<int\> returns 0 for "not found" — indistinguishable from a real zero. Same for DateTime (returns 0001-01-01) and any struct. Use…                                                                                                  |
| [§145](03-10-collections-and-linq-in-net.md#be-m145) | **Trap** A key selector with side effects or non-determinism (a Random, a mutable field, DateTime.Now) violates the comparison contract and can throw…                                                                                                                                         |
| [§146](03-10-collections-and-linq-in-net.md#be-m146) | **Cartesian trap** SelectMany over two unrelated sequences produces a cross product: 1,000 × 1,000 is a million rows. In EF this is exactly how an unfiltered join or a multi-Include…                                                                                                         |
| [§151](03-10-collections-and-linq-in-net.md#be-m151) | **Cost** Building and compiling expression trees at runtime (e.Compile()) is slow — hundreds of microseconds. Cache compiled delegates. EF caches query plans keyed by the…                                                                                                                    |
| [§153](05-05-parallelism.md#be-m153) | **On a web server, PLINQ is usually wrong** ASP.NET already uses every core to serve concurrent requests. Parallelising within a request steals threads from other requests and generally reduces total…                                                                                       |
| [§154](03-10-collections-and-linq-in-net.md#be-m154) | **Trap** Streaming from EF holds the DbDataReader — and therefore the connection — open for the entire duration of your processing. If you do slow per-row work, you are…                                                                                                                      |
| [§156](07-07-orms-and-ef-core.md#be-m156) | **Hidden N+1** It also appears without lazy loading: a foreach that calls a repository method per item, a Select that awaits a per-item lookup, or an AutoMapper projection…                                                                                                                   |
| [§158](07-07-orms-and-ef-core.md#be-m158) | **Trap** A long-lived DbContext accumulates tracked entities forever — memory grows and SaveChanges gets slower because it walks every tracked entry. DbContext is designed…                                                                                                                   |
| [§159](07-07-orms-and-ef-core.md#be-m159) | **Split-query consistency** Between the parent query and the child query another transaction can commit. If that matters, wrap both in an explicit transaction with an appropriate isolation…                                                                                                  |
| [§161](07-07-orms-and-ef-core.md#be-m161) | **Non-deterministic sort** Any pagination whose ORDER BY is not unique can repeat or skip rows between pages, because SQL is free to break ties differently each time. Always append the…                                                                                                      |
| [§162](07-07-orms-and-ef-core.md#be-m162) | **The big one:** selecting the elements of a group does not translate to SQL, because SQL GROUP BY produces aggregates, not row sets. EF Core 3+ throws; before that it silently…                                                                                                              |
| [§164](07-07-orms-and-ef-core.md#be-m164) | **Trap** Contains over a list of strings can defeat an index if the parameter type does not match the column type — the classic being an nvarchar parameter against a varchar…                                                                                                                 |
| [§166](07-07-orms-and-ef-core.md#be-m166) | **These bypass the change tracker entirely** No SaveChanges interceptors, no audit fields set by SaveChanges overrides, no domain events, no optimistic concurrency check, and already-tracked entities in the…                                                                                |
| [§167](07-07-orms-and-ef-core.md#be-m167) | **SQL injection** FromSql with an interpolated string is safe — EF converts each hole into a parameter. FromSqlRaw(\$"... {id}") is not, because the interpolation happens before EF…                                                                                                          |
| [§168](07-04-transactions-and-isolation.md#be-a100) | **WITH (NOLOCK) is not "make it faster"** It is ReadUncommitted: you can read rows twice, miss rows entirely, or read a row mid-update — even rows that get rolled back. It is acceptable only for approximate…                                                                                |
| [§169](07-04-transactions-and-isolation.md#be-d449) | **Trap** Concurrency tokens only protect the row. If your invariant spans rows ("total of all lines must not exceed the credit limit"), a token on each row does not protect…                                                                                                                  |
| [§170](07-07-orms-and-ef-core.md#be-m170) | **The two defaults that cost real money:** an unconfigured decimal becomes decimal(18,2) and silently truncates anything needing more scale (unit prices, FX rates, tax rates); an unconfigured string becomes…                                                                                |
| [§173](07-07-orms-and-ef-core.md#be-m173) | **TPT is the one that surprises people in production** Querying the base type generates a LEFT JOIN to every subtype table, so adding the fifth payment type silently adds a fifth join to every existing query. Costs grow…                                                                   |
| [§174](07-07-orms-and-ef-core.md#be-m174) | **DetectChanges is O(entities &times; properties)** and runs on SaveChanges, on most queries, and on Entries(). With 50 tracked entities it is invisible; with 50,000 it dominates the whole operation, and it gets…                                                                           |
| [§175](07-07-orms-and-ef-core.md#be-m175) | **Pooled contexts are reset, not recreated** Any state you put in a field on your DbContext subclass — a tenant id, a current user, a cached lookup — survives into the next request that gets that instance.…                                                                                 |
| [§177](07-07-orms-and-ef-core.md#be-m177) | **Interceptors do not see ExecuteUpdate/ExecuteDelete as entity changes** — those bypass the change tracker entirely, so audit stamps, domain events, and outbox rows silently stop happening for them (§166). Either stamp explicitly in the…                                                 |
| [§178](07-07-orms-and-ef-core.md#be-m178) | **Gaps are normal and are not a bug** Identity and sequence values are consumed outside the transaction so that concurrent inserts do not serialise; a rollback does not return the number. Any…                                                                                               |
| [§181](07-02-the-relational-model-and-sql.md#be-m181) | **The classic left-join bug** Putting a condition on the right table in WHERE instead of ON silently converts a left join into an inner join, because NULL \<\> 'x' is unknown and the row is…                                                                                                 |
| [§182](07-02-the-relational-model-and-sql.md#be-m182) | **Set operators compare whole rows, and they treat NULLs as equal** — unlike =. So EXCEPT and INTERSECT give you the null-safe comparison that a join does not, which is occasionally exactly what you want for reconciling two tables.…                                                       |
| [§183](07-02-the-relational-model-and-sql.md#be-d412) | **NOT IN with NULLs returns nothing** If the subquery yields even one NULL, x NOT IN (subquery) is never true for any row, because x \<\> NULL is unknown. This produces a silently empty result set with no…                                                                                  |
| [§184](07-02-the-relational-model-and-sql.md#be-d413) | **Cycles will hang or error** If the data can contain a loop (A parents B parents A), a recursive CTE runs until MAXRECURSION stops it. Carry a path column and exclude already-visited ids, or…                                                                                               |
| [§185](07-02-the-relational-model-and-sql.md#be-d414) | **LAST_VALUE surprises everyone** With an ORDER BY in the window, the default frame is RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW — so LAST_VALUE returns the current row, not the group's…                                                                                             |
| [§187](07-02-the-relational-model-and-sql.md#be-m187) | **Every row of that table is a different rule** Nulls being equal for GROUP BY and DISTINCT but unequal for = and JOIN is not an inconsistency you can reason your way around — it has to be memorised. The…                                                                                   |
| [§188](07-02-the-relational-model-and-sql.md#be-m188) | **Untrusted constraints** After a bulk insert with WITH (CHECK_CONSTRAINTS) disabled, or after re-enabling with WITH NOCHECK, SQL Server marks the constraint untrusted and stops using it for…                                                                                                |
| [§190](07-02-the-relational-model-and-sql.md#be-m190) | **Collation mismatches are a silent performance killer** Joining or comparing two columns with different collations forces an implicit conversion, which makes the predicate non-sargable and turns a seek into a scan. It…                                                                    |
| [§192](07-04-transactions-and-isolation.md#be-m192) | **Lock escalation** Past roughly 5,000 locks on one table, SQL Server escalates row locks to a table lock, and unrelated work stops. This is why bulk updates should be batched — DELETE…                                                                                                      |
| [§193](07-02-the-relational-model-and-sql.md#be-m193) | **MERGE on SQL Server has a long history of bugs and concurrency surprises** — it is not automatically atomic against concurrent inserts unless the target key is protected by a unique index and the statement holds the right locks (HOLDLOCK…                                               |
| [§194](07-03-indexes-and-query-plans.md#be-m194) | **Rows cannot span pages** (in SQL Server: 8,060 bytes of in-row data). Exceed it and variable-length columns are pushed off-row into ROW_OVERFLOW or LOB pages, and every read of that column…                                                                                                |
| [§195](07-03-indexes-and-query-plans.md#be-d421) | **Heaps leave forwarding pointers** Update a row in a heap so it no longer fits its page and the engine moves it, leaving a pointer behind — and every index lookup then costs two page reads instead of…                                                                                      |
| [§197](07-03-indexes-and-query-plans.md#be-m197) | **Covering has a cost you pay on every write** An index with five included columns is a second copy of those columns: it doubles their storage and must be maintained on every insert, delete, and update that…                                                                                |
| [§200](07-03-indexes-and-query-plans.md#be-m200) | **A scan is not automatically wrong** Reading a 500-row lookup table is one page; seeking it is also one page. And past the tipping point, a scan genuinely is the cheaper plan (§197). Judge by pages…                                                                                        |
| [§201](07-03-indexes-and-query-plans.md#be-m201) | **Columnstore is hostile to single-row work** Point lookups, small updates, and deletes are slow: small inserts land in a rowstore delta store until a rowgroup fills, and deletes are logical, marked in a delete…                                                                            |
| [§203](07-06-non-relational-stores.md#be-m203) | **The unbounded-array failure** Embedding a collection that grows forever — audit entries, messages, event history — produces a document that grows until reads and writes both become expensive,…                                                                                             |
| [§204](07-06-non-relational-stores.md#be-m204) | **Do not raise MaxNumberOfRequestsPerSession to make an error go away** It is the smoke alarm, not the fire. Hitting it means a loop is issuing per-item queries; the fix is Include, a batched LoadAsync, or a projection. For genuinely…                                                     |
| [§206](07-06-non-relational-stores.md#be-m206) | **Redis executes commands one at a time on a single thread** One O(n) command over a large key blocks every other client. KEYS \* is the classic outage (use SCAN, which is cursor-based). So are SMEMBERS on a million-member…                                                                |
| [§207](07-06-non-relational-stores.md#be-m207) | **Do not cache DbContext-tracked entities** They carry lazy-loading proxies and navigation references that either fail to serialize or drag half the object graph in with them. Cache DTOs — flat, versioned,…                                                                                 |
| [§208](08-08-serialization-and-data-formats.md#be-m208) | **Differences from Newtonsoft that bite on migration:** System.Text.Json is case-sensitive by default (set PropertyNameCaseInsensitive), does not serialize fields unless told to, is strict about numbers-as-strings, does…                                                                   |
| [§209](07-06-non-relational-stores.md#be-m209) | **What you give up** No NOT NULL, no CHECK, no foreign keys, no column statistics — so the optimiser estimates blindly. Filtering on a JSON path is a computed expression, which means a…                                                                                                      |
| [§212](07-05-schema-design-and-migrations.md#be-m212) | **A global query filter is not a security boundary** It is applied by EF, so it is absent from raw SQL, from ExecuteUpdate, from IgnoreQueryFilters(), from anything Dapper runs, and from every other process touching…                                                                       |
| [§213](07-05-schema-design-and-migrations.md#be-m213) | **History tables grow without bound** A temporal table on a hot row updated every minute produces half a million history rows a year per row. Partition the history table by time and switch out old…                                                                                          |
| [§214](07-05-schema-design-and-migrations.md#be-m214) | **Do not event-source the whole system** The pattern earns its cost where history is the domain — ledgers, order lifecycles, inventory movement, approval workflows, anything audited. For a settings table…                                                                                   |
| [§215](07-05-schema-design-and-migrations.md#be-m215) | **Do not run analytics on the OLTP database** A columnstore scan over a fact table competes for exactly the buffer pool and CPU your transactional workload needs, and analytical queries hold their reads for a…                                                                              |
| [§216](07-06-non-relational-stores.md#be-m216) | **Language configuration is not a detail** The stemmer and stop-word list are chosen per index and per query, and a mismatch silently returns nothing. Vietnamese in particular is not well served by most…                                                                                    |
| [§217](07-06-non-relational-stores.md#be-m217) | **Coordinate order is the classic bug** Point(x, y) means Point(longitude, latitude) — the reverse of how humans say and write coordinates, and the reverse of GeoJSON's own display convention in many…                                                                                       |
| [§218](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d477) | **Approximation is usually acceptable here, and it is worth using** Percentiles from a merged histogram, distinct counts from HyperLogLog, and heavy hitters from a sketch all give bounded-memory answers over arbitrary ranges, which…                                                       |
| [§219](08-08-serialization-and-data-formats.md#be-m219) | **CSV formula injection** A field beginning with =, +, -, or @ is interpreted as a formula when the file is opened in a spreadsheet — and formulas can invoke external commands. If you export…                                                                                                |
| [§220](07-05-schema-design-and-migrations.md#be-m220) | **Operations that lock a large table** and will take an outage if run naively: adding a NOT NULL column with a default on older SQL Server versions, changing a column's type or width, adding a foreign…                                                                                      |
| [§221](07-05-schema-design-and-migrations.md#be-m221) | **Keyset, not offset** A backfill driven by Skip(n).Take(batch) degrades quadratically — each batch scans past everything already done (§161). Worse, as rows stop matching the filter the…                                                                                                    |
| [§222](07-05-schema-design-and-migrations.md#be-m222) | **Do not call Database.Migrate() at application startup in production** With multiple instances, several race to apply the same migration; the losers fail or, worse, partially apply. It also means your application's database user needs…                                                   |
| [§224](04-06-testing.md#be-m224) | **The InMemory provider is not a database** — Microsoft's own guidance now says not to use it for testing query behaviour. It has no relational semantics: no foreign keys, no unique constraints, no NOT NULL…                                                                                |
| [§227](10-06-privacy-compliance-and-the-supply-chain.md#be-m227) | **Encrypted columns cannot be searched, sorted, or indexed meaningfully** — deterministic encryption allows equality lookups but leaks which rows share a value, which for something like a national ID is often enough to identify people.…                                                   |
| [§227](10-06-privacy-compliance-and-the-supply-chain.md#be-m227) | **Logs are the most common leak** EnableSensitiveDataLogging() puts every parameter value into your logs — it is a development-only setting and it must never reach production config. Structured…                                                                                             |
| [§229](04-03-composition-and-dependency-injection.md#be-m229) | **Transient disposables are captured by the resolving scope** A transient IDisposable resolved from the root provider is held until the application shuts down — so a transient HttpClient or DbContext resolved outside a scope…                                                              |
| [§230](04-03-composition-and-dependency-injection.md#be-a58)  | **Transient is not "safe by default"** A transient injected into a singleton is also captured for the process lifetime — the lifetime of an injected dependency is bounded by its consumer, not by its…                                                                                        |
| [§231](08-01-what-a-backend-does.md#be-m231) | **Short-circuiting means not calling next** That is correct and intended — a rate limiter or auth failure should stop the pipeline. What is not correct is writing to the response and calling next: once the…                                                                                 |
| [§232](08-03-a-modern-net-backend.md#be-a59)  | **IOptionsSnapshot cannot be injected into a singleton** — it is scoped, so it is the captive dependency again (§230). Singletons that need reloadable configuration must use IOptionsMonitor. This is one of the most common…                                                                 |
| [§232](08-03-a-modern-net-backend.md#be-a59)  | **Connection strings and secrets do not belong in appsettings.json** They end up in source control, in container images, and in build logs. Use environment variables, user secrets in development, or a secret store — and remember that…                                                     |
| [§233](05-03-async-and-await.md#be-c328) | **Never start unawaited work from a request handler** \_ = ProcessAsync(order); continues after the scope is disposed, so its DbContext is dead, its HttpContext is recycled, and an unhandled exception has nowhere to go.…                                                                   |
| [§234](08-07-background-work-and-queues.md#be-m234) | **Three ways background services fail quietly** Not creating a scope per iteration, so one DbContext accumulates tracked entities forever. Not observing stoppingToken, so shutdown hangs until it is force-killed.…                                                                           |
| [§235](08-01-what-a-backend-does.md#be-m235) | **The orchestrator's grace period must exceed the host's shutdown timeout, which must exceed your longest request** Kubernetes sends SIGTERM and then SIGKILL after terminationGracePeriodSeconds (30 by default); if your shutdown timeout is longer, the process is killed mid-drain…        |
| [§237](08-03-a-modern-net-backend.md#be-a127) | **Keep handlers thin, whichever you choose** An endpoint's job is to bind, validate, call one thing, and map the result to a status code. Business logic in an endpoint cannot be tested without HTTP, cannot be…                                                                              |
| [§238](08-02-api-design.md#be-m238) | **Status codes carry contract meaning; do not improvise** 400 the request is malformed. 401 not authenticated. 403 authenticated but not permitted. 404 not found — also the right answer for "exists but you may not know it…                                                                 |
| [§239](08-02-api-design.md#be-m239) | **Model binding is an attack surface** Binding directly to a type with more properties than the client should set — IsAdmin, TenantId, Status — is over-posting: the client sets it and you save it. A…                                                                                        |
| [§240](08-02-api-design.md#be-a130) | **Every sortable field is an index commitment** Allowing sort by six columns means either six indexes or five slow queries, and clients will find the slow ones. Publish only what you have indexed, and treat…                                                                                |
| [§241](08-02-api-design.md#be-m241) | **Version the contract, not the implementation** Copying a controller to V2 and diverging the logic produces two codebases that drift. Keep one implementation and map to the versioned shape at the edge — usually a…                                                                         |
| [§242](08-02-api-design.md#be-m242) | **Never return exception details to a caller** A stack trace names your types, file paths, framework versions, and often connection strings or SQL. Log the detail with a correlation id and return the id — the…                                                                              |
| [§243](08-03-a-modern-net-backend.md#be-m243) | **Never cache a response that varies by user without varying the key on the user** — it is the tenant-leak bug at the HTTP layer, and a shared proxy will happily serve one customer's data to another (§304). Authenticated endpoints should generally…                                       |
| [§245](12-01-monoliths-modules-and-services.md#be-a68)  | **Boundaries decay silently without enforcement** The first cross-module using is added under deadline pressure, compiles fine, and is never reviewed as an architectural decision. A single architecture test that…                                                                           |
| [§248](12-01-monoliths-modules-and-services.md#be-m248) | **Synchronous chains multiply latency and divide availability** A → B → C → D at 10 ms each is 40 ms of pure network before any work happens, and one slow link stalls everything upstream. If a call chain is more than two deep,…                                                            |
| [§249](12-01-monoliths-modules-and-services.md#be-m249) | **Ordering is per partition, never global** If OrderPlaced and OrderCancelled for the same order can land on different partitions, they can be processed out of order. Partition by the entity key so one…                                                                                     |
| [§250](12-01-monoliths-modules-and-services.md#be-m250) | **Without an outbox you have a lost-update window** Save then publish loses the message if the process dies between them; publish then save publishes an event for something that never happened. Neither is acceptable…                                                                       |
| [§251](12-01-monoliths-modules-and-services.md#be-m251) | **A gateway is a single point of failure and a shared deployment** Everything routes through it, so its outage is total and every change to it risks every service. Keep it thin, keep its logic declarative, and make sure it is not…                                                         |
| [§252](12-01-monoliths-modules-and-services.md#be-m252) | **Do not extract and rewrite simultaneously** Moving code across a boundary and changing its behaviour at the same time makes every bug ambiguous: is this new logic, or a move that lost something? Extract first…                                                                            |
| [§253](12-06-domain-modeling-and-application-architecture.md#be-m253) | **Large aggregates are a contention problem, not a design preference** If Customer contains every order, then two concurrent orders for one customer contend on the same root — and your optimistic concurrency token rejects one of them…                                                     |
| [§254](12-06-domain-modeling-and-application-architecture.md#be-m254) | **Do not enforce a cross-aggregate invariant inside a setter** A check that requires querying the database ("is this SKU still in stock?") inside a domain method turns every property assignment into I/O, and it is a lie anyway…                                                            |
| [§255](12-06-domain-modeling-and-application-architecture.md#be-m255) | **A rich model with public setters is neither** If order.Status = Closed compiles from anywhere, the methods on Order are suggestions and the invariants are not enforced. Private setters and a private…                                                                                      |
| [§256](12-06-domain-modeling-and-application-architecture.md#be-m256) | **Do not send an email, call an API, or publish to a broker from a domain event handler running inside the transaction** It holds locks across a network call (§303), and if the transaction later rolls back you have already sent the email. Write an outbox row inside the transaction and… |
| [§258](12-06-domain-modeling-and-application-architecture.md#be-m258) | **Transaction scope belongs here, not lower** If a repository or a domain method calls SaveChanges, no caller can compose two operations atomically, and every use case that needs to becomes a workaround. One…                                                                               |
| [§259](12-06-domain-modeling-and-application-architecture.md#be-m259) | **Where EF genuinely does constrain you:** an aggregate must be loadable as a graph, so an aggregate with thousands of children is expensive to load and save whole — which is a real argument for smaller…                                                                                    |
| [§261](07-07-orms-and-ef-core.md#be-m261) | **Dapper gives up EF's protections along with its overhead** No change tracking means no automatic concurrency token check; no model means no HasMaxLength or HasPrecision guarding your parameters; and every query is a string,…                                                             |
| [§262](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462) | **Raising Max Pool Size is almost never the fix** The pool limit is protecting the database, which has its own worker and memory limits; a bigger pool converts a client-side queue into server-side contention, which…                                                                        |
| [§263](12-05-reliability.md#be-m263) | **Retries without jitter synchronise your clients** After a brief outage, every caller retries at the same backoff intervals, producing a thundering herd that re-breaks the recovering service — the same lockstep…                                                                           |
| [§263](12-05-reliability.md#be-m263) | **Only retry idempotent operations** A timeout tells you nothing about whether the write committed — the response may simply have been lost. Retrying a non-idempotent insert double-charges someone.…                                                                                         |
| [§264](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m264) | **Sharding is a much larger commitment than replication** Splitting data across shards by a partition key means cross-shard queries, cross-shard transactions, and re-sharding all become application problems. Before…                                                                        |
| [§265](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m265) | **Never use raw SQL text as a metric dimension** Parameterised SQL is fine; SQL with inlined literals produces unbounded cardinality and will take down your metrics backend or your bill (§160). Tag with a stable…                                                                           |
| [§266](08-05-validation-and-errors-at-the-boundary.md#be-m266) | **Keep them small and readonly** A value object wrapping one primitive costs nothing when it is a readonly record struct. Make it a class and you have added an allocation and a null check to every…                                                                                          |
| [§267](11-06-observability.md#be-a60)  | **Logging expected failures as errors destroys your error rate** A 404, a validation failure, or a deliberate concurrency conflict is a normal outcome (§242). If they are logged at Error, real incidents drown in noise and the…                                                             |
| [§267](11-06-observability.md#be-a60)  | **Never log a whole entity or request object** It captures every field including the ones you must not persist, it is enormous, and it is the most common accidental data leak in a codebase (§227). Log the…                                                                                  |
| [§268](11-06-observability.md#be-e540) | **Tag cardinality is the operational cost of metrics** Every distinct combination of tag values is a separate time series. Tagging by endpoint (dozens) is fine; by tenant (thousands) is expensive; by user id, order id,…                                                                    |
| [§269](11-06-observability.md#be-e542) | **Sample, but sample consistently** If each service decides independently, you get fragments of traces — the most useless possible output. Sampling decisions must be made once, at the entry point, and…                                                                                      |
| [§270](11-06-observability.md#be-e543) | **A liveness check that touches the database is a cascading-failure generator** The database blips, every instance reports unhealthy, the orchestrator restarts all of them simultaneously, and they all reconnect at once — turning a five-second…                                            |
| [§271](04-06-testing.md#be-m271) | **Tests must not share state** Either a fresh database per test class, or wrap each test in a transaction that is rolled back. Tests that pass alone and fail in a suite — or pass in one order and…                                                                                           |
| [§272](12-04-scaling-and-performance.md#be-m272) | **Load testing against a small dataset measures nothing useful** Query plans change with data volume (§198), a scan over 1,000 rows is fast and over 10 million is not, and cache hit rates are meaningless when everything fits.…                                                             |
| [§273](11-04-ci-cd-pipelines.md#be-m273) | **Flags multiply combinatorially** Ten independent flags is 1,024 possible configurations, and you have tested a handful. Keep the number of simultaneously-live release toggles small, set an expiry…                                                                                         |
| [§274](11-04-ci-cd-pipelines.md#be-a220) | **"We can roll back" is usually false once data has been written** Code rolls back; a migrated table does not, and neither does a message already consumed or a cache already poisoned. Rollback is only real if every change since the…                                                       |
| [§275](12-04-scaling-and-performance.md#be-m275) | **Never benchmark in a loop in Main with a Stopwatch** You will measure JIT warmup, tiered compilation transitions, and dead-code elimination of your entire method. BenchmarkDotNet handles all of that; the difference…                                                                      |
| [§278](02-07-memory-management-and-garbage-collection.md#be-a89)  | **ValueTask rules** Await it exactly once. Never await it twice, never store it, never call .Result on an incomplete one, and never pass the same instance to Task.WhenAll. If you need…                                                                                                       |
| [§279](02-03-types.md#be-b242) | **The ways structs lose** Copying a large struct on every call, boxing it into an interface or an object collection, defensive copies from a non-readonly struct in an in parameter or…                                                                                                        |
| [§280](02-05-control-flow-and-functions.md#be-m280) | **Accidental capture of this** Referring to any instance field or method inside a lambda captures the entire enclosing object, not just that field. In a long-lived subscription (an event handler,…                                                                                           |
| [§281](01-04-the-processor.md#be-m281) | **False sharing** Two threads writing to different variables that share a cache line will ping-pong that line between cores and destroy throughput. Pad hot per-thread counters to 64…                                                                                                         |
| [§282](08-06-caching.md#be-m282) | **Cache what is expensive and stable, not what is convenient** Caching a cheap query saves nothing and adds a coherence bug. Caching mutable data without an invalidation path produces the worst class of production issue:…                                                                  |
| [§284](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m284) | **Collect errors, do not throw on the first bad row** A 200,000-row import that fails at row 3 because of one bad date is unusable operationally. Reject rows, cap the error list so a fully-malformed file cannot exhaust…                                                                    |
| [§287](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m287) | **The per-attempt timeout must be well under the total** With a 10-second client timeout and a 10-second attempt timeout, the retry policy never gets to run — the whole request is cancelled during the first attempt.…                                                                       |
| [§288](08-06-caching.md#be-m288) | **Cache DTOs, never entities** A tracked entity carries navigation references and possibly a proxy; serializing it either fails or drags half the object graph across the wire and into Redis…                                                                                                 |
| [§289](08-07-background-work-and-queues.md#be-m289) | **Claims must expire** If an instance is killed while holding a claim with no expiry, those items are stranded permanently and nobody notices — exactly the silent failure in §312. An…                                                                                                        |
| [§290](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m290) | **Four upload traps, all common** Reading into a byte\[\] first (a 20 MB array is on the LOH, and ten concurrent uploads is 200 MB — §34). Trusting the extension or the Content-Type header, both…                                                                                            |
| [§292](07-04-transactions-and-isolation.md#be-m292) | **Without step 2, the concurrency token does nothing** EF compares against the value it loaded a moment ago, which always matches — so the last writer silently wins and the token creates a false sense of safety. The…                                                                       |
| [§295](14-03-worked-cases-from-production.md#be-m295) | **The general rule** Never aggregate a parent column across a join to a child table. Either aggregate the child in a subquery or a separate query, or aggregate the parent first and join…                                                                                                     |
| [§296](14-03-worked-cases-from-production.md#be-m296) | **What you give up** Keyset pagination cannot jump to an arbitrary page number, and it does not give you a total count. If the UI truly needs "page 47 of 812", keep offset paging but…                                                                                                        |
| [§299](14-03-worked-cases-from-production.md#be-m299) | **The cost of streaming** The DbDataReader stays open for the whole export, holding a pooled connection and, on SQL Server, potentially shared locks depending on isolation level (§154). For…                                                                                                 |
| [§300](14-03-worked-cases-from-production.md#be-m300) | **Batch size is a tradeoff, not a constant** Larger batches mean fewer round trips but longer transactions, more log growth, and a higher chance of lock escalation past ~5,000 locks on a table (§192).…                                                                                      |
| [§301](14-03-worked-cases-from-production.md#be-m301) | **Constraints you inherit with ref struct** SpanSplitter cannot cross an await, cannot be captured in a lambda, and cannot be a field. That is why the parsing is a synchronous method called from the async…                                                                                  |
| [§303](14-03-worked-cases-from-production.md#be-m303) | **ExecuteUpdate bypasses the change tracker** No SaveChanges interceptors run, no audit fields are stamped, no concurrency token is checked, and any already-tracked Order in this context is now stale (§166).…                                                                               |
| [§304](14-03-worked-cases-from-production.md#be-m304) | **Related outage, same system** While investigating, someone ran KEYS the legacy system:\* against production Redis to find the bad entries. Redis executes commands on a single thread, so an O(n) scan over…                                                                                 |
| [§308](14-03-worked-cases-from-production.md#be-m308) | **The tell that the boundary was wrong** was there from the start: every feature touched three services, and the three always deployed together (§247). That is a distributed monolith, and no amount of…                                                                                      |
| [§310](14-03-worked-cases-from-production.md#be-m310) | **Skew invalidates "we tested with production data"** A copy of production has the same rows but not the same access pattern, and plan choice depends on which parameter arrives first. Any multi-tenant system with one…                                                                      |
| [§312](14-03-worked-cases-from-production.md#be-m312) | **At-least-once plus a non-idempotent consumer equals corruption, and the "fix" is usually worse** Deduplicating by content or by a time window drops real retries as well as duplicates — which is how a duplicate-invoice bug becomes a missing-invoice bug that…                            |

### Checklist

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Checklist</p>

<p class="pd">Types  
 
 Structs are small (≤16 bytes), immutable, and  readonly  
  readonly record struct  for small value types;  record  for DTOs 
  IEquatable&lt;T&gt;  implemented wherever  Equals  is 
  GetHashCode  uses  HashCode.Combine , never XOR 
 Nothing mutable is used as a dictionary key 
 Enum values are explicit and never renumbered 
  decimal  for money;  DateTimeOffset  for timestamps</p>

<a id="be-b300"></a>

#### Code review — red flags

``` bash
grep -rn "ArrayList\|Hashtable"                     # non-generic, boxes everything
grep -rn "\.ToList()\.\|\.ToArray()\."              # materializing mid-pipeline
grep -rn "\.Count() > 0"                            # should be .Any()
grep -rn "AsEnumerable()"                           # IQueryable → in-memory
grep -rn "new List<.*>()" | grep -i "capacity\|size" # sizes known but not pre-set
grep -rn "\.Insert(0,\|RemoveAt(0)"                 # O(n) in a loop?
grep -rn "GetHashCode() \^"                         # XOR — collides on reordering
grep -rn "ToUpper()\|ToLower()"                     # culture-sensitive comparison
grep -rn "public .* struct" | head                  # mutable structs?
grep -rn "GetOrAdd(.*=>"                            # factory may run more than once
grep -rn "stackalloc"                               # inside a loop? over 1 KB?
```

Read for:

- A `struct` with a settable property or a mutating method
- A class used as a dictionary key with no `Equals`/`GetHashCode`
- `List.Contains` inside a loop over another collection (accidental O(n²))
- `string +=` inside a loop
- A public method returning a mutable private collection
- `Substring` in a parsing hot path where a span would do
- `foreach` over a parameter typed `IEnumerable<T>` that gets enumerated twice

------------------------------------------------------------------------

### The Decision Card

<p class="src">From <em>Backend Engineering</em> · async / await · The Decision Card</p>

<p class="pd">The scan-while-writing-code version. Everything here is derived in the parts above. 
  Which return type?  
  
 
 
 Situation 
 Use 
 
 
 
 
 Normal async method 
  Task  /  Task&lt;T&gt;  
 
 
 Hot path, usually completes synchronously, measured 
  ValueTask&lt;T&gt;  
 
 
 Nothing to await — you already have the value 
  Task.FromResult(x) , no  async  
 
 
 Streaming results over time 
  IAsyn</p>

<a id="be-c381"></a>

#### Code review — greppable red flags

Fastest audit of an unfamiliar codebase, roughly in order of severity:

``` bash
grep -rn "\.Result\b\|\.Wait()\|GetAwaiter().GetResult()"   # sync-over-async
grep -rn "async void"                                        # process killers
grep -rn "Task.Factory.StartNew"                             # wrong defaults
grep -rn "\.ForEach(async\|ForEach(async"                    # async void in disguise
grep -rn "CreateUnbounded"                                   # no backpressure
grep -rn "new HttpClient()"                                  # socket exhaustion
grep -rn "Task.Run(() =>" --include=*.cs                     # possible fake async
grep -rn "\.Wait(\|\.WaitOne(\|Thread.Sleep"                 # blocking in async paths
grep -rn "ConfigureAwait(false)" src/Web src/Api             # dangerous in Framework app layer
grep -rn "async Task[^<]" | grep -v "CancellationToken"      # missing cancellation
```

Then read for the things grep can't see:

- A `using`/`await using` scope containing an await that doesn't need the resource
- `WhenAll` over a collection whose size is caller-controlled
- An `IAsyncEnumerable` parameter list without `[EnumeratorCancellation]`
- `SemaphoreSlim.WaitAsync` without a `finally { Release(); }`
- A `catch (OperationCanceledException)` with no `when` filter
- A `DbContext` or ORM session touched from more than one concurrent branch
- An interface declaring `void` where implementations obviously want to be async

------------------------------------------------------------------------

### The Checklist

<p class="src">From <em>Backend Engineering</em> · APIs · The Checklist</p>

<p class="pd">Design  
 
 Nouns in paths, verbs as methods 
 Consistent naming, casing, and envelope shape 
 Every collection paginated, with a documented maximum 
 Filtering and sorting validated against an allow-list 
 ISO 8601 UTC dates; integer minor units for money; IDs as strings 
 
  Correctness  
 
 Correct status codes, especially 401 vs 403 and 4xx vs 5xx 
 Problem Details error bodies with a stable</p>

<a id="be-e556"></a>

#### Code review — red flags

**Greppable:**

``` bash
grep -rn "new HttpClient()"                      # socket exhaustion
grep -rn "\.Result\|\.Wait()"                    # sync-over-async
grep -rn "ToListAsync()" | grep -v "Take\|Skip"  # unbounded queries
grep -rn "FromBody.*Entity\|FromBody.*Model"     # mass assignment risk
grep -rn "catch.*{ }"                            # swallowed errors
grep -rn "ex.ToString()\|ex.StackTrace"          # leaking internals
grep -rn "\$\"SELECT\|string.Format.*SELECT"     # SQL injection
grep -rn "== signature\|signature =="            # non-constant-time compare
grep -rn "api_key=\|token=" --include=*.cs       # secrets in URLs
```

**Read for:**

- An endpoint that loads a record by ID without checking ownership
- A `POST` that mutates money or state with no idempotency key
- A list endpoint with no `limit` cap, or a cap the client controls
- A `sort`/`filter` parameter reaching the query without an allow-list
- A `catch` returning 500 for what is clearly a 400
- A DTO that is actually the database entity
- An external call with no timeout, no retry policy, or a retry on a non-idempotent operation
- A response returning the entity's every property
- A webhook handler with no signature check, or doing slow work before returning 2xx

------------------------------------------------------------------------

### Checklist

<p class="src">From <em>Backend Engineering</em> · Caching · Checklist</p>

<p class="pd">Design  
 
 Every cache has a stated purpose, a measured hit rate, and an owner 
 Keys include every varying dimension — user, tenant, locale, version 
 Keys exclude anything that doesn&#x27;t vary the value 
 TTLs are chosen per data type, not globally 
 All TTLs are jittered 
 Every cache has a size bound and an eviction policy 
 
  Correctness  
 
 Stampede protection on expensive keys (coalescing</p>

<a id="be-f623"></a>

#### Code review — red flags

``` bash
grep -rn "GetOrCreate"                      # is it coalesced? is Size set?
grep -rn "SizeLimit"                        # any entry missing Size is silently uncached
grep -rn "TimeSpan.FromMinutes(5)"          # one TTL everywhere = no jitter, no thought
grep -rn "SlidingExpiration"                # unbounded staleness on hot keys
grep -rn "KEYS \|\.Keys()"                  # blocks Redis entirely
grep -rn "new ConnectionMultiplexer"        # should exist exactly once
grep -rn "Cache-Control.*public"            # is the response actually public?
grep -rn "no-cache"                         # did they mean no-store?
grep -rn "localStorage.setItem.*token"      # XSS-readable credentials
grep -rn "max-age=31536000"                 # only valid on content-hashed URLs
```

Read for:

- A cache key built from a method's parameters, missing the current user
- An invalidation that removes one key where three representations exist
- A cache read with no try/catch — a Redis blip becomes a 500
- A cached entity rather than a DTO
- `staleTime` left at 0 on slow-changing data
- A service worker with no versioned cache name or cleanup step
- A long `max-age` on anything without a content hash in the filename

------------------------------------------------------------------------

### Worked cases: writes, memory, infrastructure

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Worked cases: writes, memory, infrastructure</p>

<a id="be-m306"></a>

#### The review checklist these cases produce

Every case above was found by asking a small number of questions in a fixed order. This is that order, as a checklist you can run against any slow or wrong piece of data code before opening a profiler.

Correctness first

1.  Does any aggregate cross a one-to-many join? If yes, the number is probably wrong ([Case: the report whose totals were wrong](14-03-worked-cases-from-production.md#be-m295)).
2.  Is every `ORDER BY` that feeds pagination deterministic — unique key appended? ([Case: page 500 takes eight seconds](14-03-worked-cases-from-production.md#be-m296))
3.  Is the tenant (or equivalent scope) present in every query predicate *and* every cache key? ([Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304))
4.  Are there nullable columns in a `NOT IN`, a join key, or an aggregate? ([NULL handling in SQL](07-02-the-relational-model-and-sql.md#be-m187))
5.  Is any dictionary or set key mutable after insertion? ([Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302))

Then shape

1.  How many database commands does one request issue? Count it; do not estimate ([Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)).
2.  Is the query projecting to a DTO, or materialising entities it will discard? ([Projection: Select before materialize](07-07-orms-and-ef-core.md#be-m157))
3.  Is anything tracked that will never be saved? ([Tracking, no-tracking, and the identity map](07-07-orms-and-ef-core.md#be-m158))
4.  Is a `Contains`, `Any`, or `FirstOrDefault` running inside a loop? ([Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300))
5.  Is a filter wrapping a column in a function or a cast? ([Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298))
6.  Does the result set size grow with the data, or is it bounded? ([Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299))

Then concurrency and lifetime

1.  Is there any I/O inside a transaction? ([Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303))
2.  Are resources locked in a deterministic order?
3.  Does every cache entry have a TTL and a schema version? ([Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304))
4.  Does a cache miss under load collapse into one rebuild, or N? ([Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297))
5.  Is anything unbounded — a table with no retention, a cache with no size limit, a context with no scope, an embedded array with no cap? ([Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305))

Only then, allocation

1.  Does a profile actually show GC or allocation as a cost? If not, stop here ([Measure first: tools and method](12-04-scaling-and-performance.md#be-m275)).
2.  Is the hot path per-row or per-request? Optimise allocation only in the former ([Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)).

> **\[Why it matters\]**
>
> **Why this order.** A wrong answer delivered quickly is worse than a slow correct one, so correctness comes first. Shape problems — round trips, over-fetching, accidental O(n²) — are usually one or two orders of magnitude and are cheap to fix. Concurrency problems are intermittent and therefore expensive to diagnose later, so they are worth checking proactively. Allocation is last because it is almost never the largest term, and the code it produces is the hardest to maintain ([Rules of thumb, ranked](12-04-scaling-and-performance.md#be-m276)).

Applied in: [How to use this guide](00-02-backend-development-in-one-sitting.md#be-m1)
