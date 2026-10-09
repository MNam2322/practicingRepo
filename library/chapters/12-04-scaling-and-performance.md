---
id: "12-4"
number: "12.4"
title: "Scaling and performance"
part: 12
part_title: "Systems and architecture"
words: 10476
minutes: 46
---

# 12.4 Scaling and performance

> **Quick look.** Performance and scalability: knowing where the time goes, and why latency suddenly explodes.
>
> - Measure before optimizing: profilers, BenchmarkDotNet, load tests and production telemetry.
> - Time usually goes, in order, to unnecessary queries, missing caches, serial I/O chains and blocking, before code.
> - Queueing: latency multiplies as utilization nears 100%, so plan capacity at acceptable latency with headroom.
> - Little's law, L = λW, links concurrency, throughput and latency.
> - Load test with realistic data volume and distribution; the output is the bottleneck, not a number.
> - Scale stateless tiers out; scale the database by doing less work, caching, replicas, read models and partitioning.
> - Rate limiting protects you from callers; queues level load; share as little as possible between nodes.


### The big picture

A booking site that answers in 200 milliseconds for one user may take ten seconds when a fare sale brings fifty thousand users at once, or fail entirely. Two separate questions are hiding there. Performance is about how fast the system is for a request. Scalability is about whether it stays fast as load grows, and what it takes to keep it so. Both are mostly won or lost by knowing where the time goes, rather than by clever code. The most important single idea in both is one that surprises almost everyone: response times don't grow steadily with load. They stay flat, then explode, as any resource approaches full use.

This chapter covers measuring before optimizing, with profilers, benchmarks and load tests. It covers where time usually goes, and the queueing mathematics that explains why busy systems suddenly slow down, with Little's law. It then covers capacity planning, the strategies for scaling each part of a system, with the database usually the hardest, and the .NET habits that keep hot paths fast. The example is a queueing simulation, whose results match the textbook formula. It shows latency multiplying as one server nears full use, and what adding a second server does.

### Before you start

**Builds on:** 6.1 Layers and packets, 8.1 What a backend does, 11.6 Observability, 3.1 Measuring cost with Big-O, 1.5 Memory, 7.3 Indexes and query plans.

**Ideas explained later:**

- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.

**The code uses:** variables and var, string interpolation, classes and properties, generic types, lambdas, LINQ, attributes. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between performance and scalability?**

> Performance is how fast the system handles work at a given load, such as the latency of one request. Scalability is how well it keeps that performance as load grows, and how much it costs to add capacity.

**2. A server is 90% busy. Roughly how much longer do requests take than at low load?**

> About ten times the service time, on average, for random arrivals: time in the system is roughly service time divided by one minus utilization. At 50% it is twice; at 99% it is a hundred times.

**3. What does Little's law say?**

> The average number of requests in a system equals the arrival rate times the average time each spends there: L = λW. At 100 requests a second taking 200 ms each, 20 requests are in progress on average.

**4. Why optimize only after measuring?**

> Intuition about where time goes is usually wrong. Profiles often show that most time is in one or two places, frequently waiting on I/O, and effort anywhere else changes nothing measurable.

**5. What is the difference between a benchmark and a load test?**

> A benchmark measures a small piece of code in isolation, such as two ways of parsing a string. A load test drives the whole system with realistic traffic, to find its capacity and how it fails.

**6. Why is the database usually the hardest part to scale?**

> Stateless application servers can be added freely, but data has one authoritative copy that must stay consistent. Scaling it needs indexes, caching, read replicas, read models or partitioning, each with trade-offs.

**7. What is queue-based load leveling?**

> Putting a queue between a bursty source of work and the service that does it, so the service works through spikes at a steady rate instead of being overwhelmed.

### Fast and scalable

**Performance** is how quickly a system does its work at a given load: the latency of a request, or the time a batch job takes. **Scalability** is how well a system keeps its performance as load grows, and how much capacity costs to add. They pull on different levers. A single request can be made faster by better indexes or fewer round trips. A system scales by being able to spread work across more resources without everything contending for one of them.

The measures come from earlier chapters. Latency is how long one request takes, and throughput is how many are handled per second, from Layers and packets (6.1). Latency is described by percentiles, with the tail mattering most, from What a backend does (8.1). And the goal is usually stated as both: for example, 1,000 requests a second with a p99 under 300 ms.

### Measure first

Intuition about performance is unreliable. Programmers routinely spend days speeding up code that accounts for 1% of the time, while the real cost sits in a query nobody looked at. Donald Knuth's often-quoted warning that "premature optimization is the root of all evil" was about exactly this: optimize the few places that measurably matter, and keep everything else simple.

Three kinds of measurement cover most needs. **Profiling** records where a running program spends its time or allocates its memory, function by function. In .NET, `dotnet-trace` and `dotnet-counters` work anywhere, including in containers, and Visual Studio, Rider and PerfView show the results as call trees and flame graphs. A **benchmark** measures a small piece of code in isolation, precisely and repeatably. BenchmarkDotNet is the standard .NET tool, and handles warm-up, JIT effects and statistics that naive stopwatch loops get wrong. A **load test** drives the whole system with realistic traffic, to find how much it can handle, where it breaks, and how it behaves when it does. k6, NBomber, JMeter and Azure Load Testing are common tools. Production telemetry, from Observability (11.6), then shows what real users experience.

This is what a BenchmarkDotNet benchmark looks like; run it in Release mode, never under a debugger:

```csharp
// Program.cs in a console project with the BenchmarkDotNet package; run with: dotnet run -c Release
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<ManifestBenchmarks>();

[MemoryDiagnoser]                                        // also report allocations per operation
public class ManifestBenchmarks
{
    private readonly string[] passengers = Enumerable.Range(1, 300).Select(i => $"PASSENGER {i}").ToArray();

    [Benchmark(Baseline = true)]
    public string Concatenate()
    {
        string manifest = "";
        foreach (string p in passengers) manifest += p + "\n";   // a new string every time round the loop
        return manifest;
    }

    [Benchmark]
    public string Builder()
    {
        var sb = new System.Text.StringBuilder();
        foreach (string p in passengers) sb.Append(p).Append('\n');
        return sb.ToString();
    }
}
```

It reports the mean time, spread and bytes allocated for each method, compared with the baseline. The builder version allocates a small fraction of the memory, because concatenation copies the whole growing string on every step, the O(n²) behaviour from Measuring cost with Big-O (3.1). Measure, though, rather than assume: for a handful of strings the difference is negligible.

### Where the time goes

When profiles of typical backends are examined, the same culprits appear again and again. The latency table from Memory (1.5) explains why: waiting dominates computing.

Database work usually comes first: missing indexes, from Indexes and query plans (7.3), N+1 query loops, from ORMs and EF Core (7.7), and fetching far more data than a screen shows. Network round trips come next: chatty APIs that need many calls per screen, and sequential calls to other services that could run in parallel, from Async and await (5.3). Then serialization of large payloads. Then genuinely CPU-heavy code on hot paths. Then memory allocation, which costs garbage collection time, from Memory management and garbage collection (2.7). Contention can be a hidden cause: threads waiting on a lock, from Threads and locks (5.2), or a thread pool starved by blocking calls, from Async and await (5.3).

The practical order of work follows: fix the queries and round trips first, then look at code.

### Why busy systems slow down

Every resource, a CPU, a database connection, a disk or a whole service, serves requests one at a time per unit of capacity, and requests that arrive while it is busy wait in a queue. Queueing theory describes what that does to response times, and its results are some of the most useful facts in this field.

**Utilization** is the share of time a resource is busy: 90 requests a second at 10 ms each keeps one server 90% busy. For requests that arrive at random, the average time in the system, waiting plus being served, is roughly the service time divided by one minus the utilization. At 50% utilization, requests take twice their service time. At 90%, ten times. At 99%, a hundred times. The curve is flat for a long way, then turns sharply upwards near full use. That is why a system that is fine at 70% load can collapse at 95%, and why latency-sensitive resources are usually run well below full capacity.

**Little's law**, proved by John Little in 1961, connects the numbers. The average number of requests in a system, L, equals the arrival rate, λ, times the average time each spends inside, W: L = λW. It holds for almost any system: a server, a queue, a database connection pool, a whole airport. At 100 requests a second taking 200 ms each, 20 requests are in progress at any moment. So a pool of 20 connections is fully used, and any slowdown immediately queues more requests. The law works in every direction. From measured throughput and latency, it tells you how much concurrency you need, and a queue that is growing tells you that arrivals are outpacing service.

### Seeing it for real

This script simulates requests arriving at random at a server that takes 10 ms each on average, at increasing utilization. It compares the simulated times with the textbook formula, and checks Little's law. It uses a seeded random generator, so the numbers are identical on every run.

```typescript
// queueing.ts: run with: node queueing.ts   (why latency explodes as utilization approaches 100%)

// A seeded random generator, so every run gives the same numbers
function random(seed: number) {
  return () => { seed |= 0; seed = (seed + 0x6d2b79f5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const exponential = (rand: () => number, mean: number) => -Math.log(1 - rand()) * mean;   // random gaps and durations

// Requests arrive at random at `rate` per second; `servers` identical workers each take 10 ms on average
function simulate(rate: number, servers: number, requests = 200_000) {
  const rand = random(42);
  const freeAt = new Array<number>(servers).fill(0);                // when each worker next becomes free
  let now = 0, totalTime = 0;
  const times: number[] = [];
  for (let i = 0; i < requests; i++) {
    now += exponential(rand, 1000 / rate);                           // the next arrival, in ms
    const w = freeAt.indexOf(Math.min(...freeAt));                   // the worker that frees up first
    const start = Math.max(now, freeAt[w]!);                         // wait in the queue if it's busy
    freeAt[w] = start + exponential(rand, 10);
    const inSystem = freeAt[w]! - now;                               // waiting plus being served
    times.push(inSystem); totalTime += inSystem;
  }
  times.sort((a, b) => a - b);
  const mean = totalTime / requests;
  return { mean, p99: times[Math.floor(requests * 0.99)]!, inSystem: (rate / 1000) * mean };   // Little's law: L = λW
}

console.log("one server, 100 requests/s capacity");
console.log("utilization   mean      theory    p99       requests in the system (Little's law)");
for (const u of [0.5, 0.7, 0.8, 0.9, 0.95, 0.99]) {
  const r = simulate(100 * u, 1);
  const theory = 10 / (1 - u);                                       // the M/M/1 formula: W = service time / (1 - utilization)
  console.log(`${(u * 100).toFixed(0).padStart(6)}%   ${r.mean.toFixed(0).padStart(6)} ms  ${theory.toFixed(0).padStart(6)} ms  ${r.p99.toFixed(0).padStart(6)} ms   ${r.inSystem.toFixed(1)}`);
}

console.log("\n90 requests/s: one server versus two");
for (const servers of [1, 2]) {
  const r = simulate(90, servers);
  console.log(`${servers} server(s): utilization ${(90 / servers).toFixed(0)}%, mean ${r.mean.toFixed(1)} ms, p99 ${r.p99.toFixed(0)} ms`);
}
```

Its output:

```text
one server, 100 requests/s capacity
utilization   mean      theory    p99       requests in the system (Little's law)
    50%       20 ms      20 ms      92 ms   1.0
    70%       33 ms      33 ms     152 ms   2.3
    80%       50 ms      50 ms     234 ms   4.0
    90%      100 ms     100 ms     438 ms   9.0
    95%      199 ms     200 ms     910 ms   18.9
    99%      927 ms    1000 ms    2699 ms   91.8

90 requests/s: one server versus two
1 server(s): utilization 90%, mean 99.9 ms, p99 438 ms
2 server(s): utilization 45%, mean 12.5 ms, p99 54 ms
```

The simulation matches the formula to within a millisecond up to 95%. At 99%, it falls a little short, because so close to full use the queue takes an extremely long time to settle, which is itself a warning. Each request needs only 10 ms of work, yet at 90% utilization it takes 100 ms on average and 438 ms at the 99th percentile. The last two lines show the other side: adding a second server at the same traffic halves utilization and cuts average latency by a factor of eight.

Real traffic is burstier than random, and real service times vary more, which makes the curve worse, not better. Airline traffic is a good example, with fare sales, morning check-in peaks and disruptions that send thousands of passengers to rebook at once.

### Capacity planning

**Capacity planning** decides how much of each resource a system needs for expected load, with margin to spare. Measure, with load tests, how much one instance can handle while still meeting the latency goal: not the maximum throughput, which comes with terrible latency, but the throughput at acceptable latency. Leave **headroom**: spare capacity for spikes, for the loss of an instance or a zone, and for the latency curve. Many teams aim to keep latency-sensitive resources below about 60 to 70% utilization at normal peak.

Autoscaling, from Cloud basics (11.5), adds instances as load rises, but it reacts after the load arrives, and new instances take time to start and warm up. For known events, such as a fare sale at 09:00, scale up in advance.

### Scaling each part

Stateless application servers are the easy part. Because they keep no state, from What a backend does (8.1), more instances behind a load balancer add capacity almost linearly: horizontal scaling from Non-relational stores (7.6). Vertical scaling, a bigger machine, is simpler still, until it hits the largest size available.

The database is usually where scaling gets hard, because data has one authoritative copy that must stay consistent. The tools, roughly in the order to reach for them, come from Parts 7 and 8. First, do less work per request: indexes, projections and no N+1 loops. Then cache repeated reads, from Caching (8.6). Then send reads to read replicas, accepting replication lag, from Non-relational stores (7.6). Then serve queries from read models built for them, from Messaging and events (12.3). And finally, when writes themselves exceed what one database can take, partition the data across several, by a key that spreads load, from Non-relational stores (7.6).

Work that doesn't need an immediate answer can be moved off the critical path. **Queue-based load leveling** puts a queue between a bursty source and the service that handles it, from Background work and queues (8.7). A spike of ten thousand requests then becomes a queue that workers drain at a steady rate, instead of ten thousand simultaneous database transactions. Static content and cacheable responses belong at the edge, on a CDN, from HTTP (6.6). And less data is faster data: page results, from API design (8.2), compress responses and avoid sending fields nobody reads.

### Limits to scaling out

Adding machines stops helping at some point, and can even make things worse. Amdahl's law, from Parallelism (5.5), says the part of the work that can't run in parallel caps the speed-up. The Universal Scalability Law, developed by Neil Gunther, adds a second effect: coordination between nodes, such as locks, cache coherence and consensus from Distributed systems basics (12.2), grows with the number of nodes, so beyond some size throughput actually falls. Systems scale best when their parts share as little as possible: stateless servers, data partitioned by key, and work that doesn't need a global lock or a global order.

### .NET habits for hot paths

Most code doesn't need tuning. On the paths that profiles show are hot, a few .NET habits help. Keep I/O asynchronous end to end, from Async and await (5.3). Reuse `HttpClient` through the factory, from TCP and UDP (6.3). Use no-tracking queries and projections, from ORMs and EF Core (7.7). Avoid allocations in tight loops with `Span<T>`, pooled buffers from `ArrayPool<T>` and `StringBuilder`. Use the JSON source generator for serialization-heavy APIs. Keep Server GC for services, the default in ASP.NET Core. Let the runtime's dynamic profile-guided optimization, on by default, do its work. And consider Native AOT where start-up time matters, from Compilers, interpreters and JIT (2.2). Each claim should be checked with a benchmark or a profile on your own code.

### In short

Performance is speed at a given load; scalability is keeping it as load grows. Measure before optimizing, with profilers for where time goes, BenchmarkDotNet for small pieces of code, load tests for whole systems and telemetry for real users. Time usually goes to database work and round trips before code. Queueing explains sudden slowdowns: time in the system is about service time divided by one minus utilization, so latency multiplies as utilization nears 100%. Little's law, L = λW, links concurrency, throughput and latency. Plan capacity at acceptable latency with headroom, and pre-scale for known peaks. Scale stateless tiers out, and the database with less work, caching, replicas, read models and partitioning, and level load with queues. Share as little as possible between nodes.

### What goes wrong

**Optimizing by guesswork.** Weeks go into code that accounts for a sliver of the time. Profile first, and fix the largest cost.

**Benchmarks done badly.** A stopwatch around a loop in a Debug build measures the JIT and the debugger. Use BenchmarkDotNet in Release.

**Planning for maximum throughput.** Capacity is sized at the throughput where latency is already terrible. Plan at the throughput that meets the latency goal, with headroom.

**Running hot.** A service kept at 90% utilization to save money has ten times its natural latency and no room for spikes. Keep latency-sensitive resources well below full use.

**Scaling the wrong tier.** More web servers are added while the database is the bottleneck, and things get slower. Find the saturated resource first.

**No load testing before big events.** A fare sale finds the system's limit in production. Load test at and beyond expected peaks, and pre-scale.

**Unbounded concurrency.** Every request opens its own database connection or thread, and the system collapses under contention. Bound concurrency with pools and queues.

**Global coordination.** A single lock, counter or ordered log that every request touches caps the whole system. Partition by key.

### Cost model

The formulas in this chapter are the cost model. Time in the system is roughly service time divided by one minus utilization, so going from 70% to 90% utilization more than triples average latency. Little's law gives concurrency: rate times latency. A stateless tier's capacity grows almost linearly with instances; a database's grows with the tools above, each costing complexity, staleness or money. Adding one instance to a pool of N at the same load lowers utilization by a factor of N over N plus one. Near the knee of the curve, that cuts latency far more than proportionally, as the two-server line in the example showed.

### In other languages (optional)

**Node.js.** `clinic.js` and the built-in `--prof` and `--cpu-prof` options profile Node; each process uses one core for JavaScript, so capacity grows by adding processes, and the same queueing curves apply to its event loop.

**Java.** Async-profiler and Java Flight Recorder profile the JVM; JMH is the benchmark harness, the counterpart of BenchmarkDotNet.

**Load testing.** k6 scripts in JavaScript, Locust in Python, Gatling in Scala and NBomber in C# all drive HTTP systems with realistic traffic.

**Everywhere.** Queueing, Little's law and Amdahl's law describe any system that serves requests, whatever it is written in.

### Key terms

- **Performance**: How quickly a system does its work at a given load.
- **Scalability**: How well a system keeps its performance as load grows, and what adding capacity costs.
- **Profiling**: Recording where a running program spends its time or allocates memory.
- **Benchmark**: A precise, repeatable measurement of a small piece of code in isolation.
- **BenchmarkDotNet**: The standard .NET library for writing and running benchmarks.
- **Load test**: Driving a whole system with realistic traffic to measure its capacity and behaviour under load.
- **Utilization**: The share of time a resource is busy.
- **Little's law**: The average number of items in a system equals the arrival rate times the average time each spends there.
- **Capacity planning**: Deciding how much of each resource a system needs for expected load, with margin.
- **Headroom**: Spare capacity kept for spikes, failures and latency.
- **Queue-based load leveling**: Putting a queue in front of a service so bursts of work are handled at a steady rate.
- **Universal Scalability Law**: A model in which coordination costs make throughput level off, then fall, as nodes are added.

### Where this leads

What happens when load exceeds capacity anyway, and how to fail gracefully, is in Reliability (12.5). The measurements that capacity planning needs come from Observability (11.6), and the data-tier tools from Part 7, starting with Indexes and query plans (7.3).

### Check yourself

1. In `queueing.ts`, keep the average service time at 10 ms but make it more variable, for example 5 ms nine times in ten and 55 ms the tenth. Compare the table with the original.
2. Use Little's law: an API handles 400 requests a second at a p50 of 50 ms. How many requests are in progress, and how big must a database connection pool be if each request holds a connection for 20 ms?
3. Add three and four servers to the second part of the script at 90 requests a second, and find where adding servers stops helping noticeably.
4. Write a BenchmarkDotNet benchmark for two ways of doing something in a codebase you know, run it in Release, and read the allocation column.
5. For a system you know, find the resource most likely to saturate first at twice today's peak, and the evidence for it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Doing the arithmetic before the architecture

A team proposes Kafka and sharding for "5,000 orders a minute at peak".

*Little's law and a quick estimate*

```text
5,000 orders/min = 83 orders/s
each order: ~4 queries at ~3 ms + one payment call at ~250 ms
in-flight requests L = λ × W = 83/s × 0.3 s ≈ 25 concurrent requests
-> a few pods and one well-indexed database handle this; keep 50% headroom for spikes
```

> **Lesson:** Compute load and concurrency first; most systems are smaller than the architecture proposed for them.

#### Example 2: Load testing with realistic data

A load test reports 2,000 requests per second, but production falls over at 400.

**What goes wrong.** The test used an empty database and every virtual user requested the same booking, so everything was cached.

*k6 with varied data*

```javascript
import http from "k6/http";
import { SharedArray } from "k6/data";
const ids = new SharedArray("ids", () => JSON.parse(open("./booking-ids.json")));   // 50,000 real-shaped ids

export const options = { stages: [{ duration: "2m", target: 200 }, { duration: "5m", target: 600 }],
  thresholds: { http_req_duration: ["p(99)<800"] } };

export default function () {
  const id = ids[Math.floor(Math.random() * ids.length)];
  http.get(`${__ENV.BASE}/bookings/${id}`, { headers: { Authorization: `Bearer ${__ENV.TOKEN}` } });
}
```

> **Lesson:** Load tests need production-like volume, distribution and warm-up, or they measure your cache.

### Performance Engineering Method

<p class="src">From <em>.NET Backend Deep Dive</em> · Performance Engineering Method</p>

<a id="dn-157"></a>

#### The loop

The tools appear in Part 13; this is the process that makes them useful.

```
1. Define the target      p99 < 300ms at 500 rps. Without a number you can't finish.
2. Measure the baseline   Load test the current system. Never optimize from intuition.
3. Find the bottleneck    Profile. The cause is almost never where you assumed.
4. Fix ONE thing
5. Re-measure             Confirm the fix, and check nothing else regressed.
6. Repeat or stop         Stop when you hit the target. Optimization is not a hobby.
```

Two rules that save the most time: **profile before optimizing** (developers guess wrong most of the time, which is why profilers exist), and **change one thing at a time** (two simultaneous changes and you can't attribute the result, including when it gets worse).

<a id="dn-158"></a>

#### Load testing

```js
// k6 - realistic shape beats a flat hammer
export const options = {
  stages: [
    { duration: '2m', target: 100 },   // ramp
    { duration: '5m', target: 100 },   // steady state - this is the measurement
    { duration: '2m', target: 500 },   // find the knee
    { duration: '2m', target: 0 },     // ramp down - watch recovery
  ],
  thresholds: {
    http_req_duration: ['p(99)<300'],
    http_req_failed:   ['rate<0.01'],
  },
};
```

Test-design mistakes that invalidate results:

- **Testing against an empty database.** A query that's fast over 1,000 rows says nothing about 10 million. Seed realistic volume *and* realistic distribution (some customers have 10,000 orders).
- **Every virtual user hitting the same id.** That's a 100% cache hit rate you won't see in production.
- **No warm-up.** The first minute measures JIT tier-0 and cold caches (Part 8.6).
- **Ignoring the ramp-down.** How fast the system recovers is as informative as how it behaves at peak.
- **Load generator saturation.** Check that the bottleneck isn't your test client.

**Find the knee, not just the average.** Systems degrade non-linearly: fine at 400 rps, collapsing at 450. The interesting number is where the curve bends and what breaks first — CPU, connection pool, thread pool, or a downstream dependency.

<a id="dn-159"></a>

#### Profiling

```bash
dotnet-trace collect -p <pid> --profile cpu-sampling --duration 00:00:30
dotnet-trace collect -p <pid> --providers Microsoft-DotNETCore-SampleProfiler
# open the .nettrace in PerfView, Visual Studio, or speedscope for a flame graph
```

Reading a flame graph: **width is total time, not depth.** Look for the widest frames, then ask whether the work is necessary at all before trying to make it faster.

```csharp
[MemoryDiagnoser]                         // Allocated is as important as Mean
[SimpleJob(RuntimeMoniker.Net90)]
public class Bench { [Benchmark] public void Method() { ... } }
```

<a id="dn-160"></a>

#### Where the time actually goes

Before micro-optimizing, check this order — it's roughly the frequency with which each is the real answer:

1. **An unnecessary query** (N+1, missing index, loading whole entities) — usually milliseconds to seconds
2. **A missing cache** on an expensive, repeated computation
3. **A serial chain** of independent I/O calls that could overlap
4. **Blocking** (`.Result`, lock contention) starving the thread pool
5. **GC pressure** from excessive allocation
6. **Algorithmic complexity** — an O(n²) in-memory join
7. Everything else

Steps 1–3 typically yield 10–100×. Steps 5–6 yield 2–5×. Anything below that is usually noise next to a single round trip you didn't need to make.

---

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a222"></a>

#### Performance: measure, then fix

The rule is unchanged and constantly ignored: profile before optimising. The bottleneck is almost never where it feels like it is.

| Symptom               | Likely cause                                          | Tool                                  |
|-----------------------|-------------------------------------------------------|---------------------------------------|
| High latency, low CPU | Waiting on I/O, locks, or thread-pool starvation      | `dotnet-trace`, distributed traces    |
| High CPU              | Hot loop, serialization, regex, excessive logging     | `dotnet-trace` cpu-sampling, PerfView |
| Memory climbing       | Rooted references, unbounded cache, LOH fragmentation | `dotnet-gcdump`, dotMemory            |
| Periodic pauses       | Gen 2 / LOH collections                               | GC counters, `dotnet-counters`        |
| Slow under load only  | Connection or thread-pool exhaustion                  | Pool counters, `ss -s`                |
| One endpoint slow     | N+1 query or a missing index                          | SQL execution plan, EF logging        |

``` csharp
[MemoryDiagnoser]
public class SerializerBenchmarks
{
    private readonly Order _order = OrderBuilder.Sample();

    [Benchmark(Baseline = true)]
    public string Reflection() => JsonSerializer.Serialize(_order);

    [Benchmark]
    public string SourceGenerated() => JsonSerializer.Serialize(_order, Ctx.Default.Order);
}
```

BenchmarkDotNet handles warm-up, statistical significance and allocation counting properly. Micro-benchmarks written with a `Stopwatch` and a loop are usually measuring the JIT.

> **\[Note\]**
>
> **The cheapest wins, in order.** Add the missing database index. Remove the N+1. Cache the thing that never changes. Stop serializing fields nobody reads. Only then start tuning C#.

###### Worked example

An end-to-end investigation, in the order the evidence actually arrives. The complaint: ‘the dashboard is slow, sometimes.’

1.  **Quantify it.** The trace histogram shows p50 = 180 ms, p99 = 4.2 s. So it is not slow — it is *occasionally* very slow, which is a completely different investigation.
2.  **Find the slow traces.** Filter traces above 2 s. All of them share one span: `SELECT ... FROM audit_events`, taking 3–4 s while p50 for that span is 12 ms.
3.  **Look at the parameters.** The slow ones all have a date range covering more than 90 days. Short ranges are fast.
4.  **Get the plan.** Short range: index seek. Long range: the optimiser switches to a clustered index scan because it estimates too many rows to make seeks worthwhile.
5.  **Decide the fix.** Not an index — the index is correct and the optimiser's choice is defensible. The real problem is that the endpoint allows an unbounded date range on a 900-million-row table.
6.  **Fix it at the right level.** Cap the range at 31 days in the API, page the results, and offer a background export for anything larger. Latency p99 drops to 240 ms.
7.  **Prevent recurrence.** Add an alert on that span's p99, and a test asserting the endpoint rejects ranges over the cap.

The general lesson is that the fastest fix is frequently not a technical optimisation but a constraint. Unbounded inputs — date ranges, page sizes, result sets, upload sizes, batch counts — are where most tail latency lives, and capping them is cheaper and more reliable than making the unbounded case fast.

###### A benchmark you can trust

``` csharp
[MemoryDiagnoser]                       // allocations are usually the real story
[SimpleJob(RuntimeMoniker.Net80)]       // compare runtimes directly
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
public class ParsingBenchmarks
{
    private string _line = null!;

    [GlobalSetup] public void Setup() => _line = File.ReadLines("sample.csv").First();

    [Benchmark] public Reading Split()  { var p = _line.Split(','); return new(...); }
    [Benchmark] public Reading Spans()  { ReadOnlySpan<char> s = _line; ... }
    [Benchmark] public Reading Utf8()   { ... Utf8Parser.TryParse(...); ... }
}
```

- **Never benchmark in a loop with a Stopwatch.** You will measure JIT warm-up, GC timing and the loop itself. BenchmarkDotNet handles warm-up, iteration count, statistical significance and outlier detection.
- **Return a value from the benchmark method.** Otherwise the JIT may eliminate the work entirely as dead code, and you will report an impossible result.
- **Look at the allocation column first.** A change that halves allocations usually helps a real server more than one that shaves 5 ns off a method, because it reduces GC pressure across the whole process.
- **Benchmark the realistic input.** A parser tuned on 40-character lines can be slower on the 4,000-character lines production actually sends.
- **Micro-benchmarks do not compose.** A 30% win on a method that is 2% of request time is a 0.6% win. Always check the profile first to find out whether the method matters at all.

### Performance & Bottlenecks

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Performance & Bottlenecks</p>

<p class="pd">Finding out why something is slow, rather than guessing. Where time actually goes, a method that terminates, and load testing that produces a usable answer.</p>

<a id="be-g705"></a>

#### Where the time actually goes

Before optimising anything, account for the time. Almost every performance investigation that goes badly begins with a theory instead of a measurement, and the measurement is usually thirty seconds of work.

``` bash
# curl reports every phase of a request separately. This is the cheapest
# diagnostic in existence and it is consistently skipped.
cat > curl-format.txt <<'EOF'
     dns:  %{time_namelookup}s
 connect:  %{time_connect}s
     tls:  %{time_appconnect}s
    ttfb:  %{time_starttransfer}s
   total:  %{time_total}s
EOF
curl -w '@curl-format.txt' -o /dev/null -s https://api.example.com/orders

#   dns                 resolver, or a cold cache
#   connect - dns       TCP handshake: one network round trip
#   tls - connect       TLS handshake: another round trip plus crypto
#   ttfb - tls          YOUR APPLICATION. The only part your code controls.
#   total - ttfb        response transfer: payload size and bandwidth
```

``` text
  A LATENCY BUDGET - write it down before optimising

  target: p99 under 300 ms for GET /orders

    dns                      0 ms    warm, or connection reused
    tcp + tls                0 ms    connection REUSED (keep-alive)
    load balancer            2 ms
    application, own work   15 ms
    database query          40 ms
    external pricing API   120 ms    <- the dominant term
    serialisation            8 ms
    response transfer       15 ms
    -------------------------------
    total                  200 ms    100 ms of headroom

  now the conversation is arithmetic rather than opinion. Two facts fall out:

    optimising the 15 ms of application code cannot save more than 15 ms.
    the external API is 60% of the budget. Cache it, or call it in
    parallel, or make it optional - and everything else is noise.

  the frequent surprise: ttfb is 20 ms and total is 900 ms, because the
  response is 4 MB. No query tuning fixes that; pagination does.
```

| Budget item       | Typical          | Reduce by                                                       |
|-------------------|------------------|-----------------------------------------------------------------|
| DNS               | 0–50 ms          | Caching; connection reuse                                       |
| TCP handshake     | 1 RTT            | Keep-alive, connection pooling                                  |
| TLS handshake     | 1 RTT (TLS 1.3)  | Session resumption; reuse                                       |
| Application work  | Yours            | Profiling, and the other guides                                 |
| Database          | 1–1000 ms        | **Usually the largest single term.** Indexes, fewer round trips |
| External calls    | 10–1000 ms       | Cache, parallelise, or make optional                            |
| Response transfer | size / bandwidth | Compression, pagination, fewer fields                           |

> **\[Note\]**
>
> **Two rules of thumb worth carrying.** A request crossing a region boundary pays that round trip for *every* round trip — so ten sequential calls cost ten times the distance, and batching or parallelising is worth more than any code change. And compression is nearly always worth it above about 1 KB: the CPU cost is small and JSON compresses roughly eight to ten times.

###### Instrumenting the budget, so the argument becomes arithmetic

``` csharp
// make each phase measurable, so "it's slow" becomes a number per stage
using var activity = ActivitySource.StartActivity("PlaceOrder");
activity?.SetTag("tenant.id", tenantId);

using (var _ = ActivitySource.StartActivity("db.load-customer"))
    customer = await _repo.ByIdAsync(id, ct);

using (var _ = ActivitySource.StartActivity("pricing.external"))
    price = await _pricing.QuoteAsync(order, ct);

// with OpenTelemetry exporting to any backend, a trace waterfall then
// shows the WIDTH of each span - and "why is this slow" is answered by
// looking, not by adding logging and redeploying.
```

``` text
  A LATENCY BUDGET, written down BEFORE optimising

    target: p99 under 300 ms for GET /orders

      dns                      0 ms    warm, connection reused
      tcp + tls                0 ms    keep-alive
      load balancer            2 ms
      application, own work   15 ms
      database query          40 ms
      external pricing API   120 ms    <- 60% of the budget
      serialisation            8 ms
      response transfer       15 ms
      ------------------------------
      total                  200 ms    100 ms of headroom

    two facts fall out immediately, and neither is a matter of opinion:

      optimising the 15 ms of application code CANNOT save more than
      15 ms. Any effort there is capped.

      the external API is the budget. Cache it, call it in parallel,
      or make it optional. Everything else is noise.

  the frequent surprise: ttfb is 20 ms and total is 900 ms - because
  the response is 4 MB. No query tuning fixes that. Pagination does.
```

| Latency fact                | Consequence                                         |
|-----------------------------|-----------------------------------------------------|
| Same-AZ round trip ~0.5 ms  | 10 sequential calls = 5 ms. Fine                    |
| Cross-AZ ~1–2 ms            | 10 sequential = 20 ms. Noticeable                   |
| Cross-region ~50–150 ms     | 10 sequential = **1.5 s**. Batch or parallelise     |
| TLS handshake = 1 RTT (1.3) | Connection reuse is worth more than any code change |
| Compression above ~1 KB     | JSON compresses 8–10×; CPU cost is small            |
| Disk read ~0.1–1 ms SSD     | Rarely the bottleneck; the query plan usually is    |

The rule that follows from the cross-region row: **a chatty protocol across a distance is unfixable by optimisation**. Ten sequential calls pay the distance ten times, and no amount of tuning at either end changes that. Either batch them into one call, issue them in parallel, or move the caller closer — those are the only three options.

<a id="be-g706"></a>

#### Finding the bottleneck: a method that terminates

A sequence that ends, rather than a search that continues until someone gives up. Each step either finds the answer or eliminates a layer.

1.  **State the problem as a number.** “p99 on `GET /orders` is 4.2 s against a 300 ms target.” Not “it feels slow”.
2.  **Is it slow for everyone, or for some?** One endpoint or all? One instance or all? One region or all? One tenant or all? This single question usually halves the search space.
3.  **Split client from server.** Compare the `curl` timing above from inside the network with the browser's own timing. If TTFB is fine, the problem is payload, rendering or the client's network.
4.  **Account for the time server-side.** A trace, or timers around each phase. Find the widest span. Do not guess which it is.
5.  **Check saturation before optimising code.** CPU, memory, disk `await`, connection pool in use, queue depth, thread pool. A saturated resource explains everything above it.
6.  **Now go one layer down** into whatever the widest span was — the query plan, the remote service, the disk. Repeat from step 4.
7.  **Change one thing, measure, and keep or revert.** Two simultaneous changes tell you nothing about either.

| Observation                           | Almost always                                                |
|---------------------------------------|--------------------------------------------------------------|
| Slow for everyone, always             | A missing index, an N+1, or an unbounded query               |
| Slow only under load                  | Saturation — a pool, a thread limit, or a lock               |
| Slow for one tenant                   | Data volume skew — their table is a hundred times larger     |
| Slow at a fixed time daily            | A batch job or report evicting the cache                     |
| Slow after a deploy                   | A new query, a changed plan, a missing index on a new column |
| High load, low CPU                    | I/O wait or lock contention, not compute                     |
| Latency high, CPU under the limit     | cgroup CPU throttling                                        |
| First request slow, rest fast         | Cold start — JIT, cache, connection, or DNS                  |
| Gradually worse over weeks            | Growth: data volume, bloat, or an unbounded table            |
| Fast in isolation, slow in production | Contention, or production data volume you did not replicate  |

``` text
  THE SATURATION CHECK, in one screen

    CPU        top          %us user  %sy system  %wa IO-WAIT  %st STEAL
    memory     free -h      'available' is the number; swapping is fatal
    disk       iostat -xz   r_await / w_await in ms. >10 ms = saturated
    network    ss -s        and the app's own connection counts
    pool       app metrics  in-use vs max. >80% sustained = the bottleneck
    threads    app metrics  queue length, or thread pool starvation
    queue      broker       depth, AND age of the oldest message

  find the saturated one FIRST. Optimising code that is waiting on a
  saturated resource changes nothing, and it is where most wasted
  performance work goes.
```

> **\[Note\]**
>
> **Measure before theorising.** The single highest-return habit in this whole guide: get a trace, a plan or a profile *before* forming an opinion about the cause. Most time lost to performance work is spent testing hypotheses that thirty seconds of measurement would have eliminated — and the hypothesis that feels most plausible is very often the one that is wrong.

###### The USE method, and a saturation check you can run in sixty seconds

``` text
  THE USE METHOD - for every resource, check three things, in order

    UTILISATION   how busy is it, as a percentage of time
    SATURATION    how much queued work is waiting for it
    ERRORS        is it failing

  SATURATION IS THE ONE THAT PREDICTS. Utilisation tells you the
  present; saturation tells you what happens if load rises 20%.

    CPU at 70% with a run queue of 0    -> headroom
    CPU at 70% with a run queue of 12   -> already saturated. Requests
                                           are waiting for a core.

  the resources worth walking, with the saturation signal for each:

    CPU        run queue length      (load average vs core count)
    memory     swapping, OOM kills   (si/so in vmstat - ANY is bad)
    disk       await, queue depth    (iostat -xz, r_await/w_await in ms)
    network    dropped packets, retransmits
    conn pool  in-use vs max         (application metric)
    threads    queue length          (application metric)
    broker     consumer lag and age
```

``` csharp
#!/usr/bin/env bash
# sixty-second saturation check. Run this BEFORE forming any theory.
echo "== load (compare to $(nproc) cores)"; uptime
echo "== cpu breakdown"; vmstat 1 5
#   %wa high  -> waiting on I/O, not compute
#   %st  > 0  -> the HYPERVISOR is stealing CPU. Not your problem to fix.
#   si/so > 0 -> SWAPPING. Stop here; nothing else matters.
echo "== disk"; iostat -xz 1 3
#   r_await / w_await in ms. Over ~10 ms on SSD = saturated.
#   %util is misleading on SSDs - ignore it, read await.
echo "== memory"; free -h
#   read 'available', not 'free'. Cache is not a leak.
echo "== sockets"; ss -s
echo "== top consumers"; ps aux --sort=-%cpu | head -8
echo "== recent kills"; dmesg -T | grep -i -E 'oom|killed' | tail -5
```

| Reading                         | Almost always means                               |
|---------------------------------|---------------------------------------------------|
| Load average \> cores, low CPU% | Processes blocked on I/O, not compute             |
| `%st` above ~2                  | Noisy neighbour on shared infrastructure          |
| Any `si`/`so`                   | Swapping — latency is already ruined              |
| `await` 50 ms, low IOPS         | The storage tier is throttling you                |
| High CPU%, low throughput       | Lock contention, or GC                            |
| Latency in multiples of 100 ms  | **cgroup CPU throttling** — a container CPU limit |
| Everything idle, requests slow  | Waiting on a downstream service                   |

The last row is the one that ends most investigations on the wrong machine. If every local resource is idle and requests are still slow, the bottleneck is *elsewhere* — go to the trace, find the widest span, and move to that system. Continuing to profile a machine that is doing nothing is the single most common way performance work is wasted.

<a id="be-g707"></a>

#### Load testing that tells you something

Most load tests produce a number that cannot be acted on. A useful one produces a *bottleneck* and a *curve*.

1.  **Write the question first.** “Can this sustain 800 orders per second with p99 under 200 ms?” — not “how fast is it?”
2.  **Use production-shaped data at production scale.** A test against 10,000 rows fits entirely in cache and measures nothing that matters.
3.  **Model the real mix.** 95% reads behaves nothing like 95% writes, and one endpoint hammered behaves nothing like your actual traffic.
4.  **Warm up, then measure a steady state** long enough to include a checkpoint, a GC cycle and a cache expiry.
5.  **Vary the input.** The same id every request is served entirely from cache — you tested the cache.
6.  **Ramp until something breaks**, and record precisely *what* broke first.
7.  **Report percentiles and the knee**, not the maximum.

``` text
  THE KNEE, which is the actual answer

    latency
      |                                    .
      |                                  .
      |                               .
      |                          . <---- the KNEE: throughput is still
      |                    .              rising but latency has started
      |        . . . . .                  to climb steeply
      | . . .
      +-------------------------------------- throughput
              ^                    ^
         comfortable          "maximum throughput"
         capacity              - at 4 s latency, which
                                serves nobody

    quote the throughput AT YOUR LATENCY TARGET. "We handle 2,400 rps"
    is meaningless without saying at what p99 - and past the knee the
    number is real and useless.
```

| Mistake                      | Why the result is invalid                                         |
|------------------------------|-------------------------------------------------------------------|
| No warm-up                   | You measured cold caches and unoptimised code paths               |
| Too little data              | Everything cached; index depth and I/O never mattered             |
| Reporting the mean           | A p99 of 8 s hides comfortably behind a mean of 40 ms             |
| Same input each request      | Every layer's cache answered. You tested the cache                |
| No think time                | Unrealistic contention on the same rows                           |
| One load generator           | It saturated before the system did — you measured your own client |
| Testing a shared environment | Variance exceeds the effect you are measuring                     |
| Not naming what broke        | The number is unusable; the bottleneck is the deliverable         |

> **\[Note\]**
>
> **The output is a sentence, not a number.** “We handle 1,200 requests per second” is nearly useless. “At 1,200 rps the database connection pool saturates, p99 rises to 4 s, and adding application instances makes it worse because they compete for the same pool” tells you what to fix, what will happen at double the load, and which change would be wasted effort. Run the test until something gives way, then identify exactly what gave way.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a213"></a>

#### Capacity planning and performance budgets

The performance section covers diagnosing what is slow. This is the question asked earlier and answered far less often: how much of everything do we need, and how do we know before the traffic arrives?

##### Start from Little's Law

``` text
  L = lambda x W        concurrency = arrival rate x average service time

  400 requests/second, 120 ms average  ->  L = 400 x 0.12 = 48 concurrent requests

  which sizes everything downstream:

    threads          async I/O, so ~48 in flight, NOT 48 threads. A handful suffices.
    db connections   48 x (40 ms db time / 120 ms total) = 16 concurrent queries
                     -> a pool of ~25 across the whole deployment, with headroom
    replicas         if one pod handles 150 rps comfortably, 400/150 = 3, plus
                     headroom and N+1 for a node failure -> 5
```

The reason to do this arithmetic is that the intuitive answers are wrong in both directions. Teams provision connection pools of 100 per pod across 20 pods against a database allowing 200 connections; and they provision twelve replicas for load two could handle because CPU graphs are reassuring.

| Resource          | Sizing rule                                                              | Common mistake                                              |
|-------------------|--------------------------------------------------------------------------|-------------------------------------------------------------|
| Replicas          | Peak rps ÷ measured per-pod capacity, × headroom, plus one for node loss | Sizing for average, not peak                                |
| CPU requests      | Observed p95 usage under load, not the limit                             | Setting requests equal to peak, wasting most of the cluster |
| Memory limit      | p99 working set + GC headroom (~30%)                                     | Forgetting server GC grows the heap toward the limit        |
| DB pool (per pod) | Concurrent queries per pod + small buffer                                | The default of 100, multiplied by replica count             |
| Broker consumers  | Backlog drain target ÷ per-consumer throughput                           | Scaling consumers past the partition count                  |
| Cache size        | Working-set size × average entry size, with an eviction policy           | No bound, then OOM kills                                    |

###### Establishing the per-pod number, which is the only input that matters

1.  **Deploy one replica**, isolated, with production-like data volume. Small datasets give meaningless numbers because everything fits in cache.
2.  **Ramp load** until p99 latency crosses your target — not until errors appear. The interesting number is throughput at an acceptable latency, and it is usually 40–60% of the throughput at which it falls over.
3.  **Record the constraint.** CPU-bound, connection-pool-bound, or downstream-bound? This determines whether adding replicas helps at all. If one pod saturates a shared database, ten pods will saturate it ten times faster.
4.  **Convert to a budget.** “150 rps per pod at p99 \< 300 ms, CPU-bound at 0.8 cores.” That sentence is your capacity model.
5.  **Re-measure per release**, in CI if you can. A regression from 150 to 90 rps is invisible on a functional test suite and doubles your infrastructure bill.

``` bash
# a throughput regression gate in CI
- name: Load test
  run: |
    dotnet run --project tests/LoadTests -- \
      --target-rps 150 --duration 3m --p99-ms 300 \
      --fail-if-below-rps 135        # 10% regression tolerance
```

| Headroom for                         | Typical allowance                       |
|--------------------------------------|-----------------------------------------|
| Traffic variance within the hour     | +30% over measured peak                 |
| Losing one node or availability zone | N+1, or N+1 per zone                    |
| Deployment surge                     | `maxSurge: 1` worth of spare capacity   |
| Growth before the next review        | One or two quarters of projected growth |
| Cold-start and warm-up               | New pods serve less for the first ~30 s |

###### The failure mode capacity planning exists to prevent

``` text
  the queueing cliff

  utilisation    30%    50%    70%    80%    90%    95%
  queue wait     0.4x   1.0x   2.3x   4.0x   9.0x   19.0x   (of service time)

  latency does not degrade linearly with load. It is roughly u/(1-u).
  a system comfortable at 70% utilisation is in trouble at 90% -
  which is a 28% increase in traffic, i.e. one good marketing email.

  this is why "we have headroom, CPU is only at 70%" is a dangerous sentence,
  and why autoscaling thresholds belong at 60-70%, not 85%.
```

Two practical consequences. First, target utilisation well below saturation — the cost of the extra replica is far less than the cost of the incident. Second, alert on *saturation* (queue depth, pool wait time, thread-pool queue length) rather than on utilisation, because saturation is what actually predicts the cliff.

- Publish the capacity model next to the dashboard: expected peak, measured per-pod capacity, current replica count, current headroom.
- Re-derive it whenever the workload changes shape — a new endpoint, a bigger payload, an added downstream call.
- Include cost in the model. “Peak needs 12 replicas at €38/month each” turns a capacity question into a business decision.
- Load-test the *whole* path including the database and the broker. A per-pod number measured against a stubbed backend tells you about your stubs.
- Know your hard ceilings: database max connections, broker partition count, third-party rate limits, and provisioned throughput. These do not autoscale, and hitting one is a wall rather than a slope.

### Observability, testing, and delivery

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Observability, testing, and delivery</p>

<a id="be-m272"></a>

#### Load testing and capacity

Functional tests answer "is it correct?". Load tests answer a different question: *at what point does it stop being correct?* Those are different failures and the second is only visible under concurrency.

``` csharp
// NBomber, k6, or Locust - the tool matters less than the shape of the test.
var scenario = Scenario.Create("place_order", async ctx => {
        var res = await client.PostAsJsonAsync("/orders", NewOrder());
        return res.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
    })
    .WithLoadSimulations(
        Simulation.RampingInject(rate: 200, interval: TimeSpan.FromSeconds(1),
                                 during: TimeSpan.FromMinutes(5)));
```

| Test kind | Answers                                                          |
|-----------|------------------------------------------------------------------|
| Baseline  | latency and throughput at expected load                          |
| Stress    | where does it break, and *how* — gracefully or catastrophically? |
| Soak      | does anything leak over hours? ([The GC in one page](02-07-memory-management-and-garbage-collection.md#be-b239))  |
| Spike     | does it recover after a sudden burst, or stay broken?            |

``` text
// The failures load testing finds, which nothing else does:
//   connection pool exhaustion       (§262)
//   thread pool starvation           (§136)
//   cache stampede at TTL expiry     (§297)
//   lock contention and deadlocks    (§303)
//   ephemeral port exhaustion        (§19)
//   gen 2 growth from a leak         (§24)
// Every one is invisible at one request at a time.
```

> **\[Trap\]**
>
> **Load testing against a small dataset measures nothing useful.** Query plans change with data volume ([Statistics and cardinality estimation](07-03-indexes-and-query-plans.md#be-d436)), a scan over 1,000 rows is fast and over 10 million is not, and cache hit rates are meaningless when everything fits. Seed production-shaped volume *and* production-shaped skew — one tenant with 60% of the rows behaves nothing like forty even tenants.

> **\[Tip\]**
>
> **Test the failure mode, not just the ceiling.** A system that sheds load cleanly at 120% — 429s, fast failures, a stable queue — is far healthier than one that accepts everything and degrades into timeouts for every user. Rate limiting and bounded queues are what convert the second into the first ([Auth, rate limiting, and output caching](08-03-a-modern-net-backend.md#be-m243), [Channels and async pipelines](05-05-parallelism.md#be-m135)).

Worked example: [Case: the API that fell over at 09:00 on sale day](14-03-worked-cases-from-production.md#be-m307)

### Optimization in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Optimization in practice</p>

<a id="be-m275"></a>

#### Measure first: tools and method

The order of operations is fixed: *reproduce → measure → find the dominant cost → change one thing → measure again*. Skipping straight to the change is how people spend a week making a method 30% faster inside a request that spends 95% of its time waiting on SQL.

``` csharp
[MemoryDiagnoser]                    // reports allocations - use this always
[SimpleJob(RuntimeMoniker.Net80)]
public class Bench {
    private List<int> _data = Enumerable.Range(0, 10_000).ToList();

    [Benchmark(Baseline = true)] public int Linq() => _data.Where(x => x % 2 == 0).Sum();
    [Benchmark] public int Loop() { int s = 0; foreach (var x in _data) if (x % 2 == 0) s += x; return s; }
}
```

| Question                                     | Tool                                                                 |
|----------------------------------------------|----------------------------------------------------------------------|
| Which method is faster?                      | BenchmarkDotNet                                                      |
| Where does wall-clock time go in production? | OpenTelemetry / Application Insights traces                          |
| What is allocating?                          | `dotnet-counters`, then a memory profiler (dotMemory, PerfView, VS)  |
| Why is memory growing?                       | two heap snapshots, compare retained sets                            |
| What SQL actually ran?                       | EF logging, MiniProfiler, Query Store                                |
| Is the GC the problem?                       | `dotnet-counters monitor System.Runtime` — % time in GC, gen 2 count |
| Where is CPU going?                          | `dotnet-trace` + SpeedScope, or PerfView                             |

> **\[Trap\]**
>
> **Never benchmark in a loop in `Main` with a `Stopwatch`.** You will measure JIT warmup, tiered compilation transitions, and dead-code elimination of your entire method. BenchmarkDotNet handles all of that; the difference between it and a naive harness is routinely an order of magnitude in the wrong direction.

Worked example: [The review checklist these cases produce](14-05-what-actually-goes-wrong-field-guide-and-checklists.md#be-m306)

Applied in: [Load testing and capacity](#be-m272)

<a id="be-m276"></a>

#### Rules of thumb, ranked

Ordered by how much they usually matter, largest first:

1.  **Remove the round trip.** A network call is 0.5–50 ms. Anything in-process is nanoseconds to microseconds. N+1, chatty service calls, and per-item cache lookups dwarf every other consideration.
2.  **Fix the algorithm.** An accidental O(n²) — usually a `Contains` inside a loop — beats every micro-optimisation combined.
3.  **Fetch less.** Fewer columns, fewer rows, fewer entities tracked. Projection and pagination.
4.  **Cache what is stable.** A correct cache is the largest single-step win available, and the hardest to keep correct.
5.  **Do it once instead of n times.** Hoist invariant work out of loops, materialise deferred queries you enumerate twice, pre-size collections.
6.  **Reduce allocation in hot paths.** Structs, spans, pooling, `static` lambdas. Only after the above.
7.  **Micro-optimise.** Devirtualisation, bounds-check elimination, SIMD. Real, occasionally worth a lot, almost never where you should start.

> **\[Why it matters\]**
>
> **Corollary.** Most "slow C#" is not slow C#. It is a missing index, a query returning 50,000 rows to display 20, a cache that was never added, or a synchronous call blocking a thread pool thread. Look there first, every time.

First principles: [Where the time actually goes](#be-g705)

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a131"></a>

#### Rate limiting, quotas and backpressure

Retries, circuit breakers and bulkheads protect *you* from a slow dependency. Rate limiting is the mirror image: it protects your service from its callers — a runaway client loop, a scraper, an over-eager retry policy, or simply more legitimate traffic than the database can absorb. Since .NET 7 it is built in.

| Algorithm      | Behaviour                                            | Use for                                                  |
|----------------|------------------------------------------------------|----------------------------------------------------------|
| Fixed window   | N per interval, counter resets on the boundary       | Simple quotas; tolerates a burst at the seam             |
| Sliding window | Fixed window split into segments that roll           | Smoother quotas without the boundary burst               |
| Token bucket   | Tokens refill at a steady rate, bucket holds a burst | The best default: sustained rate plus headroom           |
| Concurrency    | N in flight at once, regardless of rate              | Protecting a bounded resource — a pool, a CPU-bound path |

``` csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // partition per authenticated tenant, falling back to IP for anonymous —
    // a global limit punishes everyone for one noisy caller
    o.AddPolicy("per-tenant", ctx => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: ctx.User.FindFirstValue("tenant_id")
                      ?? ctx.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous",
        factory: _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,                            // burst capacity
            TokensPerPeriod = 20,                        // sustained: 20/s
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,                              // reject, don't queue
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));

    // tell the client WHEN to come back — without this they retry immediately
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((int)retry.TotalSeconds).ToString();
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 429, Title = "Too many requests",
        }, ct);
    };
});

app.UseRateLimiter();                        // AFTER UseAuthentication
app.MapGet("/reports", GetReports).RequireRateLimiting("per-tenant");
```

###### Where the limit belongs

A limiter in the process protects that process only: run five replicas and the effective limit is five times what you configured. For per-instance protection — guarding a thread pool or a connection pool — that is exactly right. For a contractual quota (“1,000 requests per hour on the free tier”) the counter has to be shared, which means the gateway, an ingress-level limiter, or a Redis-backed counter. Most systems end up with both: a coarse quota at the edge and a fine concurrency limit next to the resource that actually saturates.

Backpressure is the same idea one level down. A queue with no bound converts an overload into an out-of-memory crash; a bounded `Channel` with `BoundedChannelFullMode.Wait` makes the producer slow down instead, which is almost always the behaviour you want.

> **\[Trap\]**
>
> **Common trap.** Rate limiting by IP alone breaks on the first proxy, NAT gateway or mobile network: thousands of users share one address and one user can rotate through many. Partition by the strongest identity available — API key, tenant, then user, and IP only as a last resort — and configure `ForwardedHeaders` properly, or you will be limiting your own load balancer.

> **\[Why it matters\]** A single misbehaving client can take down a service that is otherwise healthy, and the failure looks exactly like a capacity problem — so teams scale up, pay more, and get taken down again. A limiter turns that incident into a 429 for one caller, and the `Retry-After` header turns the caller’s retry storm into an orderly wait.

### Performance

<p class="src">From <em>Backend Engineering</em> · APIs · Performance</p>

<a id="be-e527"></a>

#### Caching

The cheapest request is one you never make.

``` yaml
Cache-Control: public, max-age=3600            cacheable by anyone, 1 hour
Cache-Control: private, max-age=60             browser only
Cache-Control: no-store                        never persist (auth, PII)
Cache-Control: no-cache                        may store, must revalidate
Cache-Control: max-age=60, stale-while-revalidate=300   serve stale, refresh behind
```

`no-cache` does **not** mean "don't cache." It means "check with me first." `no-store` is the one that means don't keep it.

**Conditional requests** turn a full response into a 304 with no body:

``` yaml
Response:  ETag: "v3-abc123"
Next req:  If-None-Match: "v3-abc123"
Response:  304 Not Modified          ← no body, tiny
```

`ETag` is a version fingerprint (a hash or version number). `Last-Modified`/`If-Modified-Since` is the weaker, second-resolution equivalent.

**Cache invalidation** is genuinely hard. The tractable strategies: short TTLs and accept staleness; event-driven purging; or version the cache key so a change makes old entries unreachable rather than requiring deletion.

This section is the API-facing summary. For the full model — freshness vs validation, `Vary`, CDN behaviour, browser layers, and every failure mode — see the caching reference, Part VII onward.

<a id="be-e528"></a>

#### The N+1 problem

``` text
GET /orders           → 100 orders
GET /orders/1/customer
GET /orders/2/customer
...                   → 100 more round trips
```

At 50 ms each, that's 5 seconds for what should be one call. It's the single most common API performance failure.

Fixes: allow embedding (`GET /orders?include=customer`), provide a batch endpoint (`GET /customers?ids=1,2,3`), or denormalize the common case into the list response. GraphQL exposes this problem to the *server* instead, where DataLoader-style batching is the standard mitigation.

<a id="be-e529"></a>

#### Payload size

- **Compression:** `Accept-Encoding: gzip, br`. Brotli beats gzip on text by ~15–20%. Typically 70–90% reduction on JSON. Nearly free.
- **Sparse fieldsets:** `?fields=id,name` when clients need three columns of forty.
- **Don't over-optimize the body.** At small sizes, latency dominates: one 100 KB response beats ten 10 KB ones, every time.
- **Stream large responses** rather than buffering. In .NET, `IAsyncEnumerable<T>` from a controller streams the JSON.

<a id="be-e530"></a>

#### Connection reuse

From 0.4: an HTTPS handshake is 2–3 round trips. Reusing connections eliminates it.

- Reuse a single `HttpClient` (via `IHttpClientFactory`), never one per request.
- Set `PooledConnectionLifetime` (~2 minutes) so DNS changes are picked up.
- HTTP/2 multiplexes many concurrent requests over one connection — a large win for chatty internal traffic.

<a id="be-e531"></a>

#### Async operations

Some work doesn't fit a request/response window. Don't hold the connection open for three minutes:

``` text
POST /reports          → 202 Accepted, Location: /jobs/abc
GET  /jobs/abc         → { "status": "running", "progress": 0.4 }
GET  /jobs/abc         → { "status": "done", "result_url": "/reports/xyz" }
```

Better than polling, when you can: a webhook on completion. Best of both: return a job ID *and* accept a callback URL.

------------------------------------------------------------------------
