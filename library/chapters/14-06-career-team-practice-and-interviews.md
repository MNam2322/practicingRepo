---
id: "14-6"
number: "14.6"
title: "Career, team practice and interviews"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 3405
minutes: 15
---

# 14.6 Career, team practice and interviews

> **Quick look.** Study priorities, team practices and interview preparation.
>
> - Study first: N+1, DbContext thread safety, BackgroundService scopes, async state machine, memory cache vs Redis.
> - 50 short interview answers cover async, EF Core, DI, GC, caching, messaging, security and cloud.
> - System design: clarify requirements, compute the load, then design; "this fits on three pods and one Postgres" is often the right answer.
> - Debugging questions test method: narrate the funnel and name real tools.
> - Behavioral answers: situation, your action, measured result; have a real failure ready.
> - Team practices: ADRs record alternatives and consequences; reviews focus on design and risk; pay tech debt alongside feature work.


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Answering "the API got slow, what do you do?"

A common senior interview question that tests method rather than trivia.

*A strong answer, out loud*

```text
"First I check what changed: deploy, config, traffic, data volume.
Then where the time goes: traces for the slow requests, p50 vs p99.
If p99 is bad and CPU is low, I suspect blocking: thread-pool queue length in dotnet-counters.
If the database is the span, I read the actual plan and look for scans or estimate mismatches.
I mitigate first — roll back or scale — then fix the cause and add a guard so it can't recur."
```

> **Lesson:** Narrate a method that terminates and name real tools; that is what interviewers listen for.

### Study Order by Priority

<p class="src">From <em>.NET Backend Deep Dive</em> · Study Order by Priority</p>

<a id="dn-2"></a>

**Tier 1 — near-certain to be asked, and it changes your daily code**

1. N+1: detection and fixes
2. DbContext isn't thread-safe + `IDbContextFactory`
3. BackgroundService + `IServiceScopeFactory` + captive dependencies
4. The async/await state machine and why `.Result` is bad
5. IMemoryCache vs Redis once you scale past one instance

**Tier 2 — shows depth**

6. Interceptors + soft delete + filtered indexes
7. Cache stampede + HybridCache
8. Boxing and `ValueType.Equals`
9. Task vs ValueTask
10. Structured logging + OTel + TraceId correlation

**Tier 3 — architecture and cloud**

11. Queue vs bus, outbox, idempotent consumers
12. Presigned URLs + S3 security
13. Lambda cold starts, API Gateway limits
14. SSO back-channel logout
15. GC tuning, Server GC, measuring with dotnet-counters

**The most effective way to learn this:** build a small API (Orders + Items + Customers) and then **deliberately** create an N+1, a cartesian explosion, a shared `DbContext` in `Task.WhenAll`, a `.Result` deadlock, and a cache stampede. Turn on EF logging, dotnet-counters and OTel so you can **watch** each failure happen, then fix it. You'll remember it because you saw it, not because you read it.

---

### 50 Interview Questions with Short Answers

<p class="src">From <em>.NET Backend Deep Dive</em> · 50 Interview Questions with Short Answers</p>

<a id="dn-61"></a>

**Async & threading**

1. Does `await` create a thread? → No. It registers a continuation and returns the thread to the pool.
2. Why does `.Result` deadlock? → The blocked thread holds the SynchronizationContext the continuation needs to resume on.
3. Does that happen in ASP.NET Core? → No (no SyncContext), but you get thread pool starvation instead.
4. `Task` vs `ValueTask`? → Task is a class and always allocates; ValueTask is a struct with no allocation on synchronous completion, and may be awaited only once.
5. What is `ConfigureAwait(false)` for? → Don't capture the context; avoids deadlocks and reduces overhead. Needed in libraries.
6. Why is `async void` dangerous? → Exceptions can't be caught and crash the process; you can't await it.
7. Is `Task.Run` good for I/O? → No. It just burns an extra thread; I/O is already async.
8. How do you bound concurrency? → `Parallel.ForEachAsync` with `MaxDegreeOfParallelism`, or a `SemaphoreSlim`.
9. Why can't you `lock` in async code? → Monitor has thread affinity. Use `SemaphoreSlim(1,1)`.
10. How does `Task.WhenAll` surface exceptions? → It waits for all tasks; `await` throws only the first, and the rest are in `task.Exception.InnerExceptions`.

**CLR & memory**

11. What are gen 0/1/2? → Buckets by object age; higher generations are collected less often but more expensively.
12. What is the LOH? → The heap for objects ≥85KB, collected with gen 2 and not compacted by default → fragmentation.
13. Server GC vs Workstation GC? → Server uses per-core heaps and threads for throughput at higher memory cost; it's the ASP.NET Core default.
14. Where does boxing occur? → Struct to object/interface, non-generic collections, `params object[]`, default `ValueType.Equals`.
15. Do records reduce GC pressure? → No. A `record class` allocates like a class, and `with` allocates more.
16. When should you use a struct? → Small (≤16 bytes), immutable, short-lived, and never boxed.
17. Should you write finalizers? → Almost never; use `SafeHandle`. A finalizer keeps the object alive an extra GC cycle.
18. `IDisposable` vs `IAsyncDisposable`? → The latter is for cleanup involving I/O (flushing a stream) without blocking a thread.
19. Can .NET leak memory? → Yes: static collections, unsubscribed event handlers, captive dependencies.
20. How do you diagnose it? → `dotnet-counters` for heap size, two `dotnet-gcdump` snapshots to compare, then `gcroot` to find the holder.

**EF Core**

21. What is N+1 and how do you spot it? → One query plus N follow-ups; enable EF command logging or count commands in a test.
22. What does `AsSplitQuery` solve? → Cartesian explosion when including multiple collections.
23. What does `AsNoTracking` gain you? → No snapshots → faster with fewer allocations, but no object deduplication.
24. How do you implement soft delete? → A `SaveChangesInterceptor` flipping Deleted→Modified, plus global query filters and a filtered index.
25. Does `ExecuteDelete` go through interceptors? → **No** — it breaks soft delete and auditing.
26. Is `DbContext` thread-safe? → No, one operation at a time. Use `IDbContextFactory` for parallel work.
27. What's the catch with `AddDbContextPool`? → Instances are reused, so the context must not hold per-request state.
28. Optimistic concurrency in EF? → A `[Timestamp]`/RowVersion column → `DbUpdateConcurrencyException`.
29. TPH vs TPT? → TPH is one table with a discriminator (fast); TPT spreads across tables and joins (slower).
30. How do you do zero-downtime migrations? → Expand and contract: add nullable, backfill, switch reads, drop the old column.

**DI & architecture**

31. What is a captive dependency? → A scoped service held by a singleton — it lives forever, leaks, and serves stale data. Enable `ValidateScopes`.
32. How does a BackgroundService use a DbContext? → Inject `IServiceScopeFactory` and call `CreateAsyncScope()` each iteration.
33. `IServiceScopeFactory` vs `IServiceProviderFactory`? → The first creates scopes at runtime; the second replaces the DI container entirely.
34. How do you choose a lifetime? → By state and construction cost; DbContext scoped, caches singleton.
35. Queue vs bus? → A queue is point-to-point with one consumer (commands); a bus is pub-sub with many subscribers (events).
36. What's the dual-write problem? → A DB commit and a message publish aren't atomic → use an outbox.
37. Why must consumers be idempotent? → Brokers only guarantee at-least-once delivery.
38. What is a saga? → A chain of local transactions with compensating actions, replacing two-phase commit.
39. How does a circuit breaker differ from a retry? → A retry tries again; a breaker **stops trying** so the backend can recover, and fails fast.
40. How does an idempotency key work? → The client sends a key, the server stores it uniquely and replays the stored result on a repeat.

**Caching, SQL, cloud**

41. Memory cache vs distributed cache? → Process RAM (fast, inconsistent at scale) vs Redis (slower, consistent).
42. What is a cache stampede? → A key expires and a flood of requests all miss at once — fix with per-key locks, TTL jitter, or HybridCache.
43. What does HybridCache give you? → L1+L2, stampede protection, tag invalidation, built into .NET 9.
44. What's a good invalidation strategy? → Prefer short TTLs; use explicit invalidation only where you truly must.
45. Does composite index column order matter? → Very much; the left-most prefix rule.
46. What does SARGable mean? → A predicate that can use an index; don't wrap columns in functions.
47. Keyset paging vs OFFSET? → OFFSET scans and discards n rows; keyset uses `WHERE Id > @last` and stays fast.
48. What is parameter sniffing? → A plan cached for the first parameter values, wrong for others; use `OPTION (RECOMPILE)` or Query Store.
49. How do you cut .NET Lambda cold starts? → ReadyToRun/Native AOT, provisioned concurrency, more memory, initialize clients statically.
50. What makes a presigned URL safe? → Authorize before signing, short TTL, server-generated per-user keys, POST policy limiting size, and confirming via S3 events.

---

### Team Practices That Compound

<p class="src">From <em>.NET Backend Deep Dive</em> · Team Practices That Compound</p>

<a id="dn-202"></a>

#### Architecture Decision Records

A one-page record per significant decision, committed alongside the code:

```markdown
# ADR-014: Use an outbox for order events
Status: Accepted (2026-03-12)
Context: Publishing after SaveChanges loses events when the broker is unavailable.
Decision: Write events to an Outbox table inside the same transaction; a worker publishes.
Consequences:
  + No lost events; survives broker outages
  - Publish latency of up to one polling interval
  - An extra table and worker to operate
Alternatives: 2PC (rejected: unsupported by the broker), direct publish with retry
              (rejected: still loses events on process crash)
```

The value is almost entirely in the **alternatives and consequences** sections. Eighteen months later, the question is never "what did we do" (that's visible in the code) but "did they consider X, and what did it cost us?" ADRs are what stop teams re-litigating settled decisions and, equally, what lets them revisit one when the context genuinely changed.

<a id="dn-203"></a>

#### Code review that's worth the time

Reviews should be about **design, correctness and risk**. Formatting and style belong to analyzers and `dotnet format` in CI (Part 33.2) — if a human is commenting on brace placement, the tooling is missing.

What to actually look for, roughly in order of value: does this handle failure; is there a concurrency or N+1 problem; does it leak data across users or tenants; will this be understandable in a year; is it tested at the level that matters.

Two habits that change the tone materially: **distinguish blocking from non-blocking comments** ("nit:" vs "this is a data leak"), and **ask rather than assert** when you're unsure ("what happens if this runs twice?" surfaces the bug without an argument). And keep PRs small — review quality falls off a cliff past a few hundred lines, so a 2,000-line PR gets a rubber stamp, which is worse than no review.

<a id="dn-204"></a>

#### Tech debt, honestly categorized

Not all debt is equal, and treating it as one bucket is why it never gets paid:

| Type | Example | Priority |
|---|---|---|
| **Contagious** | A bad pattern being copied into new code | **Fix now** — the cost compounds |
| **Blocking** | Slows every change in an active area | High — pays back immediately |
| **Risky** | Unpatched dependency, no rollback path, untested money path | High — it's a latent incident |
| **Ugly but stable** | Awkward code in a module nobody touches | **Leave it.** Rewriting it is cost with no return |

The way debt actually gets paid is **attached to feature work in the area** ("we're changing pricing anyway, so extract the interface first"), not in a "refactoring sprint" that gets cut the moment a deadline appears.

<a id="dn-205"></a>

#### Documentation that survives

Documentation rots in proportion to how far it sits from the code. In rough order of durability: **executable tests** (can't lie, CI enforces it) → **types and names** → **ADRs** (dated decisions, not current state) → **runbooks** (validated every incident) → **README** → a wiki nobody opens.

Write down the things code can't express: *why* this design, what was tried and rejected, what will break if you change this, and how to run it locally. Don't document *what* the code does — that's what the code is for, and the duplicate will drift.

---

### The Senior Interview Playbook

<p class="src">From <em>.NET Backend Deep Dive</em> · The Senior Interview Playbook</p>

<a id="dn-206"></a>

#### System design: the sequence

The mistake is jumping to a diagram. The order that signals seniority:

```
1. Clarify      Who uses it? What's the read/write ratio? What's the actual scale?
2. Do the math  "5,000/min is 83 rps" changes the design completely. (40.4)
3. Define the API  Endpoints and contracts before boxes.
4. Data model   Access patterns first, then storage choice. (43.4)
5. Draw the happy path  Simply. Then scale ONLY what the numbers demand.
6. Break it     "What if the payment provider is down? If this pod dies mid-write?"
7. Operate it   How do you know it's broken? How do you deploy it? Roll it back?
```

Step 2 is the differentiator. Most candidates design for imaginary scale; computing the load and then saying *"this fits on three pods and one Postgres instance"* is a stronger answer than any diagram — and it's usually the truth.

Step 6 is the second differentiator. Anyone can draw the happy path. **Volunteer the failure modes before you're asked**: at-least-once delivery means idempotent consumers; the external call stays outside the transaction; the cache can be stale and here's for how long.

<a id="dn-207"></a>

#### Phrases that signal experience

Not because they're magic words, but because each implies you've been burned:

- "Let me check the numbers before I pick an architecture."
- "That's at-least-once, so the consumer has to be idempotent."
- "I'd keep the HTTP call outside the transaction."
- "This is eventually consistent — is stale data acceptable here, and for how long?"
- "How would we detect this failing in production?"
- "That's a trade-off: we'd gain X and pay Y. Which matters more here?"
- "I don't know — here's how I'd find out."

That last one more than any of the others. Confident invention is the fastest way to fail a senior interview; a clear method for resolving uncertainty is the thing being tested.

<a id="dn-208"></a>

#### Debugging questions

When asked "the API got slow, what do you do?", they're testing method, not knowledge. Narrate the funnel:

```
What changed?           deploy, config, flag, traffic, upstream   (46.2)
Where is the time?      traces first, not guesses                 (13.A)
Narrow it               one endpoint or all? one pod or all? reads or writes?
Form a hypothesis, test it against data, then fix ONE thing       (38.1)
Mitigate first if users are affected. Diagnose after.             (46.2)
```

Name specific tools and signals — `dotnet-counters`, thread pool queue length, `% time in GC`, `gcroot` — because that's what separates having read about it from having done it.

<a id="dn-209"></a>

#### Behavioral answers

Use situation → action → result, weighted heavily toward **your** actions and a **measured** result ("p99 from 2.1s to 180ms", "incidents from six a month to one"). Vague outcomes read as secondhand stories.

Have a genuine failure ready, told without defensiveness, ending in what you changed structurally. "I shipped a migration that locked the orders table for eleven minutes; now every migration over a threshold requires a batched script and a review" is a strong answer. "My weakness is caring too much" is not.

<a id="dn-210"></a>

#### What to ask them

Questions reveal what you'll be like to work with:

- "What does the on-call rotation look like, and how many pages a week?"
- "How long from merge to production?"
- "What's the most painful part of the codebase right now?"
- "How do you decide what gets built?"
- "What happened in your last incident?"

The answers also tell you whether the things in this document exist there — and whether the job is engineering or firefighting.

---

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a224"></a>

#### A study plan that fits around a job

Given where you are — maintaining a .NET Framework platform while learning modern stacks — the highest-leverage order is roughly this. Each block is a few weeks of evenings, and each ends with something running that you can point at.

| Block | Focus                             | Build this                                                                             |
|-------|-----------------------------------|----------------------------------------------------------------------------------------|
| 1     | Modern C# and .NET 9 fundamentals | Port one small legacy utility to a .NET 9 console app                                  |
| 2     | ASP.NET Core, DI, config, EF Core | A minimal-API CRUD service with Postgres in Docker                                     |
| 3     | Testing                           | Add xUnit unit tests plus Testcontainers integration tests to it                       |
| 4     | Containers and CI/CD              | Dockerfile plus a GitHub Actions pipeline that builds, tests and pushes                |
| 5     | Integration and resilience        | Add a typed HttpClient with Polly and a RabbitMQ consumer                              |
| 6     | Observability                     | OpenTelemetry to a local Jaeger and Grafana stack                                      |
| 7     | Kubernetes                        | Deploy the service to kind or k3d with probes and an HPA                               |
| 8     | Microservices patterns            | Split it in two, add the outbox pattern and a saga                                     |
| 9     | Bring it home                     | Put YARP in front of a copy of a legacy app and route one endpoint to your new service |

##### How to study so it sticks

- **Build, do not watch.** A tutorial you followed teaches you the tutorial. Break it deliberately and fix it; that teaches you the system.
- **Read the SQL, the HTTP and the IL.** Whenever a framework does something for you, look once at what it actually emitted.
- **Keep a decisions file** in each practice repo: what you chose, what you rejected, and why. It is the fastest interview preparation you will ever do.
- **Apply one thing a week at work.** Structured logging, a health endpoint, a test for a bug you just fixed. Small changes to the legacy platform teach more than a greenfield side project, because the constraints are real.
- **Explain it to someone.** If you cannot describe why the outbox pattern exists in three sentences, you know the shape of it but not the reason.

> **\[Note\]**
>
> **The through-line.** Nearly everything in this guide is one idea in different clothes: *make the boundaries explicit, and make failure at those boundaries something you designed for rather than something that surprises you.* Type boundaries, transaction boundaries, service boundaries, network boundaries, deployment boundaries. Get those right and the rest is detail.

###### What actually works, and what does not

| Works                                                 | Does not                                   |
|-------------------------------------------------------|--------------------------------------------|
| **Building something small and real**                 | Reading a book end to end                  |
| Reading only what you cannot currently explain        | Comprehensive coverage of a large document |
| Investigating one thing properly when it breaks       | Watching a course at 2× speed              |
| Writing down what you found, afterwards               | Assuming you will remember                 |
| Explaining it to a colleague                          | Nodding along in a talk                    |
| Deliberately breaking things in a scratch environment | Reading about failure modes                |
| Twenty minutes most days                              | Four hours one Sunday a month              |

``` text
  a realistic weekly shape, around a job

  MON-FRI   ~20 min       read ONE section of a reference on something you
                          touched that day. Not a chapter - a section.

  ONE evening  ~90 min    BUILD. Reproduce something from what you read:
                          cause thread-pool starvation and watch throughput
                          collapse; make a write skew happen in two psql
                          sessions; fill a disk and watch what breaks.

  MONTHLY   ~2 hours      read one public incident report (Cloudflare,
                          GitLab, AWS). Ask: would we survive this?
                          Then fix the one thing where the answer is no.

  the ratio is the point. Reading without building produces familiarity
  that collapses under pressure. Building without reading produces habits
  with no explanation behind them.
```

On this guide specifically, and the others alongside it: they are **references, not curricula**. Five hundred sections read cover to cover produces recognition without capability. The intended use is to skim the titles, read the handful you could not currently explain aloud, build something using them, and return by search when something breaks. The sidebar filter and browser find are the primary interface — not the scrollbar.

> **\[Note\]**
>
> **The one habit with the best return.** Run `EXPLAIN ANALYZE`, or open a profiler, or read a trace — *before* forming a theory about why something is slow. Most time lost to performance work is spent testing hypotheses that thirty seconds of measurement would have eliminated. That habit generalises to every layer in these guides.
