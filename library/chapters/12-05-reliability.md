---
id: "12-5"
number: "12.5"
title: "Reliability"
part: 12
part_title: "Systems and architecture"
words: 12310
minutes: 54
---

# 12.5 Reliability

> **Quick look.** Reliability: serving users well while parts fail, and recovering fast when you can't.
>
> - Measure with SLIs from the user's side, set SLOs, manage the error budget and alert on burn rate.
> - Every call gets a timeout from the latency budget; retry only idempotent, transient failures, with backoff and jitter, at one layer.
> - Circuit breakers fail fast and need a fallback; bulkheads isolate pools per dependency; shed load past the knee.
> - Most incidents follow changes: release gradually.
> - Incidents: clear roles, mitigation before diagnosis, ask "what changed?", blameless postmortems.
> - RPO and RTO are business decisions; an untested backup is not a backup; keep immutable backups in a separate account.
> - Spread across zones and test failover; multi-region only when the business pays for it.


### The big picture

At scale, failure is normal. Disks die, instances are restarted, networks partition, dependencies slow down, a certificate expires, and someone deploys a bad change. A system with a hundred parts has something failing almost all the time. Reliability isn't the absence of failure. It is the system continuing to serve users well while parts of it fail, and recovering quickly when it can't. Aviation learned this long ago: aircraft are built with redundant systems, crews follow checklists for failures, and every incident is investigated to learn from it, not to blame. Software reliability engineering borrows heavily from that culture.

This chapter covers measuring reliability with service level indicators, objectives and error budgets, and the arithmetic of availability, where chained dependencies multiply their failures and redundancy divides them. It covers the patterns that stop one failing dependency from taking everything down: timeouts, careful retries, circuit breakers, fallbacks, bulkheads and load shedding. It then covers testing failure on purpose, handling incidents and learning from them, and recovering from disasters. The example computes what availability targets allow, then simulates a dependency outage under four strategies. It shows that retries alone quadruple the load on a failing service, while a circuit breaker with a fallback keeps every user served.

### Before you start

**Builds on:** 11.6 Observability, 12.1 Monoliths, modules and services, 11.5 Cloud basics, 8.1 What a backend does, 7.6 Non-relational stores, 12.2 Distributed systems basics.

**Tools and technologies named here:**

- **AI** (13.1): Artificial intelligence: software that does tasks associated with human intelligence, today mostly through machine learning.

**The code uses:** string interpolation, classes and properties, primary constructors, generic types, lambdas, switch expressions and patterns, tuples, exceptions, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between an SLI, an SLO and an SLA?**

> An SLI is a measurement, such as the share of requests that succeed within 300 ms. An SLO is the target for it, such as 99.9% over 30 days. An SLA is a contract with customers, with penalties if missed, usually looser than the internal SLO.

**2. How much failure does a 99.9% monthly target allow?**

> About 43 minutes of full outage in 30 days, or the equivalent in partial failures. That allowance is the error budget.

**3. A request passes through five services, each 99.9% available. How available is the whole?**

> About 99.5%: availabilities multiply when every part must work. Chains of synchronous dependencies are less reliable than any one of their parts.

**4. Why can retries make an outage worse?**

> Every failed call is repeated, multiplying the load on a service that is already struggling. Retries at several layers multiply again: three layers with three attempts each can send 27 calls for one request.

**5. What does a circuit breaker do?**

> It watches calls to a dependency, and when failures pass a threshold it opens: further calls fail immediately, without waiting, for a while. Then it lets a trial call through, and closes again if it succeeds.

**6. What is a bulkhead?**

> A separate, limited pool of resources, such as connections or concurrent calls, for each dependency, so that one slow dependency can use up only its own pool and not everything.

**7. What are RPO and RTO?**

> The recovery point objective is the most data you can afford to lose, measured in time. The recovery time objective is the longest the service may be down. Together they drive backup and failover design.

### Measuring reliability

**Reliability** is a system's ability to keep doing its job correctly for its users over time, despite failures in its parts. To manage it, you have to measure it from the users' point of view, and Google's site reliability engineering (SRE) practice gave the field its vocabulary.

A **service level indicator** (SLI) is a measurement of something users care about: the share of booking requests that succeed, the share of searches answered within 300 ms, the share of check-in messages processed within a minute. A **service level objective** (SLO) is a target for an SLI over a period: 99.9% of booking requests succeed within 300 ms over each 30 days. A **service level agreement** (SLA) is a contract, usually with customers or partners, that promises a level of service with penalties if it is missed. SLAs are typically looser than the internal SLOs, so the team notices problems before the contract is breached.

No real target is 100%. Users' own networks and devices fail more often than that, and each extra nine costs far more than the last. The gap between 100% and the SLO is the **error budget**: 0.1% of requests, or about 43 minutes a month at 99.9%. The budget turns reliability from an argument into a decision. While budget remains, the team can ship features and take risks. When it is spent, the priority shifts to reliability work until it recovers. Alerts, from Observability (11.6), are best based on **burn rate**, how fast the budget is being used: a rate that would spend a month's budget in hours pages someone, and a slow leak raises a ticket.

### The arithmetic of availability

Availability combines in simple and unforgiving ways, which the example calculates.

When a request needs several parts to work in sequence, their availabilities multiply. Five services at 99.9% each, all called for one request, give about 99.5%: more than seven times the downtime of any single one. This is the main cost of long synchronous chains between services, from Monoliths, modules and services (12.1).

When a request needs only one of several redundant copies, their failure probabilities multiply instead. Two independent copies at 99% each give 99.99% together, because both must fail at once. **Redundancy** means having more than one of anything whose failure would matter. A **single point of failure** is a component with no redundancy, whose failure alone brings the system down: one database server, one load balancer, one region, one person who knows how deployments work.

Redundancy only helps if failures are independent. Two instances in the same rack share its power supply; two regions running the same buggy release share the bug. That is why systems spread across availability zones, from Cloud basics (11.5), and why deployments go out gradually.

### Redundancy and failover

Stateless instances, from What a backend does (8.1), are the easiest to make redundant: run several behind a load balancer, and health checks remove failed ones. Data is harder. Databases run with replicas, from Non-relational stores (7.6), and switch to a replica when the primary fails, called **failover**. It is automatic in managed services, and it loses the most recent commits if replication was asynchronous.

Redundancy across regions can be active-passive, where a standby region takes over when the main one fails, or active-active, where both serve traffic all the time. Active-active recovers faster and needs the multi-region data consistency from Distributed systems basics (12.2). Either way, a failover that has never been tested probably doesn't work.

### When a dependency fails

Most outages that users see start with one dependency becoming slow or failing, and spread because callers aren't protected. Several patterns, most of them named in Michael Nygard's 2007 book *Release It!*, stop the spread.

**Timeouts** on every call to anything outside the process come first, set from the request's latency budget, from What a backend does (8.1). Without them, a hung dependency holds every calling request, and by Little's law, from Scaling and performance (12.4), the number of stuck requests grows until threads, connections or memory run out, and the caller fails too. The example's first strategy shows 500 requests stuck at once.

Retries hide brief failures, as Errors and exceptions (2.8) described, but only for transient errors and idempotent operations, and only with exponential backoff and jitter. Retries also multiply load on a service that is already failing, which the example shows directly, and retries at several layers multiply again. This **retry amplification** is how a small problem becomes an outage. Retry at one layer only, limit the attempts, and cap the total share of traffic that may be retries.

A **circuit breaker** stops calling a dependency that is clearly failing. Closed, it lets calls through and counts failures. When failures cross a threshold, it opens, and calls fail immediately without being attempted, giving the dependency room to recover and the caller an instant answer instead of a timeout. After a pause, it goes half-open and lets a trial call through. If it succeeds, the breaker closes; if not, it opens again.

A **fallback** decides what to do instead: return a cached fare, from Caching (8.6), with a note that it may be out of date, hide the recommendations panel, queue the request for later, or offer a read-only mode. That is **graceful degradation**: a reduced service instead of no service. Deciding the fallback for each dependency is a product decision as much as a technical one.

A **bulkhead**, named after the watertight compartments in a ship's hull, gives each dependency its own limited pool of resources, such as a maximum number of concurrent calls. A slow loyalty service can then use up only its own share, and bookings keep working.

**Load shedding** protects a service from its own overload. When it is past the knee of the latency curve from Scaling and performance (12.4), accepting more work makes every request slow. Rejecting some quickly, with 503 or 429 and a `Retry-After` header, keeps the accepted ones fast. It is backpressure, from The event loop (5.4), at the level of a service. Shed the least important work first: analytics before bookings.

### Seeing it for real

This script calculates what common availability targets allow, and how chains and redundancy combine. It then simulates a minute of traffic, 100 requests a second, to a service whose pricing dependency is down for 30 seconds in the middle. It compares four strategies: no timeout, a timeout, a timeout with retries, and a timeout with a circuit breaker and a cached-fare fallback.

```typescript
// resilience.ts: run with: node resilience.ts   (availability arithmetic, and one dependency failing four ways)

// 1. What an availability target allows, over 30 days
for (const slo of [99, 99.9, 99.95, 99.99]) {
  const minutes = 30 * 24 * 60 * (1 - slo / 100);
  console.log(`${String(slo).padEnd(6)}% allows ${minutes >= 60 ? `${(minutes / 60).toFixed(1)} hours` : `${minutes.toFixed(1)} minutes`} of failure a month`);
}
// 2. Chains multiply; redundancy helps
const chain = 0.999 ** 5, pair = 1 - (1 - 0.99) ** 2;
console.log(`five 99.9% services in a chain: ${(chain * 100).toFixed(2)}%; two 99% copies in parallel: ${(pair * 100).toFixed(2)}%`);

// 3. The pricing service is down from 10 s to 40 s of a 60 s run; 100 requests a second arrive
type Result = { ok: number; fallback: number; failed: number; calls: number; peakInFlight: number };
type Strategy = { timeoutMs: number; retries: number; breaker: boolean };
const HEALTHY_MS = 20, HANG_MS = 5000;
const down = (t: number) => t >= 10_000 && t < 40_000;

function run({ timeoutMs, retries, breaker }: Strategy): Result {
  const r: Result = { ok: 0, fallback: 0, failed: 0, calls: 0, peakInFlight: 0 };
  const busy: [number, number][] = [];                      // [start, end] of every request, to find the peak in flight
  let failuresInRow = 0, openUntil = -1;
  for (let t = 0; t < 60_000; t += 10) {
    if (breaker && t < openUntil) { r.fallback++; busy.push([t, t]); continue; }   // open circuit: answer at once from cache
    let elapsed = 0, success = false;
    for (let attempt = 0; attempt <= retries && !success; attempt++) {
      r.calls++;
      if (!down(t)) { elapsed += HEALTHY_MS; success = true; }
      else elapsed += Math.min(timeoutMs, HANG_MS);        // wait for the timeout, or until the hung call gives up
    }
    busy.push([t, t + elapsed]);
    if (success) { r.ok++; failuresInRow = 0; continue; }
    if (breaker) { r.fallback++; if (++failuresInRow >= 5) { openUntil = t + 5000; failuresInRow = 4; } }   // one failed probe reopens it
    else r.failed++;
  }
  const events = busy.flatMap(([s, e]) => [[s, 1], [e, -1]] as [number, number][]).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  let current = 0;
  for (const [, delta] of events) r.peakInFlight = Math.max(r.peakInFlight, (current += delta));
  return r;
}

console.log("\nstrategy                          answered  fallback  failed  calls to pricing  peak requests in flight");
for (const [name, s] of [
  ["no timeout", { timeoutMs: Infinity, retries: 0, breaker: false }],
  ["200 ms timeout", { timeoutMs: 200, retries: 0, breaker: false }],
  ["200 ms timeout, 3 retries", { timeoutMs: 200, retries: 3, breaker: false }],
  ["timeout, breaker and fallback", { timeoutMs: 200, retries: 0, breaker: true }],
] as const) {
  const x = run(s);
  console.log(`${name.padEnd(32)} ${String(x.ok).padStart(8)} ${String(x.fallback).padStart(9)} ${String(x.failed).padStart(7)} ${String(x.calls).padStart(17)} ${String(x.peakInFlight).padStart(24)}`);
}
```

Its output:

```text
99    % allows 7.2 hours of failure a month
99.9  % allows 43.2 minutes of failure a month
99.95 % allows 21.6 minutes of failure a month
99.99 % allows 4.3 minutes of failure a month
five 99.9% services in a chain: 99.50%; two 99% copies in parallel: 99.99%

strategy                          answered  fallback  failed  calls to pricing  peak requests in flight
no timeout                           3000         0    3000              6000                      500
200 ms timeout                       3000         0    3000              6000                       20
200 ms timeout, 3 retries            3000         0    3000             15000                       80
timeout, breaker and fallback        2996      3004       0              3006                        5
```

Read the columns together. Without a timeout, 500 requests are stuck at once, which is 100 a second times five seconds each, exactly as Little's law predicts, and enough to exhaust a typical thread or connection pool. A timeout cuts that to 20, but half of all users still get errors. Adding retries sends 15,000 calls instead of 6,000, quadrupling the load on the failing service during the outage, and saves nobody, because the failure isn't transient. The circuit breaker sends only about ten calls to the dead service, keeps just five requests in flight, and every user gets an answer, half of them from the cached fallback. A few requests just after recovery also get the fallback, until the breaker's next trial call finds the service healthy.

In .NET, the `Microsoft.Extensions.Http.Resilience` package, built on the Polly library, adds a standard pipeline of rate limiting, total timeout, retries with backoff and jitter, circuit breaker and per-attempt timeout to any `HttpClient`, from A modern .NET backend (8.3):

```csharp
builder.Services.AddHttpClient<PricingClient>(client => client.BaseAddress = new Uri("https://pricing.internal"))
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(200);   // each try
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);     // the whole call, retries included
        options.Retry.MaxRetryAttempts = 2;                                // exponential backoff with jitter by default
        options.CircuitBreaker.FailureRatio = 0.5;                         // open when half of calls fail...
        options.CircuitBreaker.MinimumThroughput = 20;                     // ...among at least 20 in the sampling window
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(5);
    });

public sealed class PricingClient(HttpClient http, IFareCache cache)
{
    public async Task<decimal> GetFareAsync(string route, CancellationToken ct)
    {
        try { return await http.GetFromJsonAsync<decimal>($"/fares/{route}", ct); }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            return await cache.GetLastKnownFareAsync(route, ct);          // degrade: an older price, shown as such
        }
    }
}
```

The same package's resilience pipelines protect calls that aren't HTTP, such as database or broker operations.

### Change causes most failures

Most incidents follow a change: a deployment, a configuration edit, a certificate rotation or a database migration. So the safest release practices from CI/CD pipelines (11.4) are also the main reliability tools: small changes, automated tests, gradual rollouts with canaries, feature flags that switch off quickly, and fast rollback. Treat configuration changes with the same care as code: review them, deploy them gradually and keep them versioned.

### Testing failure on purpose

Failure-handling code only runs during failures, so it is rarely tested and often broken. **Chaos engineering**, popularized by Netflix, whose Chaos Monkey began terminating production instances at random in 2011, tests it deliberately. It injects failures, such as killed instances, added latency, blocked dependencies or a lost zone, in a controlled way, while watching the SLIs, to confirm the system degrades as designed. Start in test environments, with small, reversible experiments and a way to stop immediately. Game days rehearse whole incidents, such as failing over a database or a region, with the people who would handle them for real. Azure Chaos Studio and similar services inject faults into cloud resources.

### When things go wrong

An **incident** is an unplanned disruption that affects users or threatens to. Handling one well is a skill, and it follows a pattern. Detect it quickly, through SLO alerts and synthetic checks from Observability (11.6). Assign roles: an incident commander who coordinates and decides, separate from the people investigating, and someone who keeps users and stakeholders informed. Mitigate first, then fix: roll back, switch off the flag, fail over or shed load to stop the harm, and only then find the root cause. Write down what happens as it happens.

Afterwards, a **blameless postmortem** records the timeline, the impact, the contributing causes and the actions that will prevent a repeat. It is blameless because people act reasonably on the information and tools they had; asking "how did the system let this happen?" produces fixes, while blame produces silence. This comes directly from aviation's safety culture, where "just culture" encourages open reporting of mistakes and near misses, and every incident is investigated to make the whole system safer.

### Disaster recovery

Some failures are too large for automatic handling: a region lost, data corrupted by a bug or deleted by an attacker, ransomware. **Disaster recovery** is the plan for those. Two numbers set its design. The **recovery point objective** (RPO) is the most data the business can afford to lose, measured in time: with a 15-minute RPO, backups or replication must never be more than 15 minutes behind. The **recovery time objective** (RTO) is the longest the service may be down while it is restored.

Backups, from Storage and files (1.6), must be copied off-site or to another region, protected from the same attacker as the live system, for example with immutable storage, and restored regularly as a test. An untested backup is a hope, not a plan. Point-in-time restore, from Why databases exist (7.1), handles mistaken deletes. Region failover handles lost regions, if the data is replicated there and the procedure has been rehearsed. Write the runbooks, from Observability (11.6), before they are needed.

### In short

Reliability means serving users well despite failing parts. Measure it with SLIs from the users' side, set SLOs, and manage the error budget, alerting on burn rate; SLAs are looser contracts. Chained dependencies multiply failure, while independent redundancy removes single points of failure; spread across zones and test failover. Protect every call: timeouts from the latency budget, limited retries with backoff at one layer only, circuit breakers, fallbacks for graceful degradation, bulkheads per dependency, and load shedding past the knee. Most incidents follow changes, so release gradually. Test failure with chaos experiments and game days, handle incidents with clear roles and mitigation first, learn with blameless postmortems, and plan disaster recovery around tested backups, RPO and RTO.

### What goes wrong

**No timeouts.** One hung dependency fills every thread and connection, and the caller fails too. Set a timeout on every outgoing call.

**Retries everywhere.** Retries at every layer multiply load on a failing service and turn a blip into an outage. Retry at one layer, with limits, backoff and jitter.

**No fallback decided.** When pricing fails, the whole booking page fails. Decide in advance what each feature does when each dependency is down.

**Shared pools.** One slow dependency uses every connection and blocks unrelated work. Give each dependency its own limits.

**Accepting all work under overload.** Every request becomes slow, and eventually all time out. Shed load early with 503 and `Retry-After`.

**Hidden single points of failure.** One database, one person, one certificate, one DNS provider. Find them and add redundancy or a plan.

**Untested backups and failover.** The restore fails during the real disaster. Restore and fail over regularly as rehearsals.

**Blame after incidents.** People hide mistakes, and the same failure recurs. Run blameless postmortems and fix the system.

### Cost model

Each extra nine costs roughly ten times more: moving from 99.9% to 99.99% cuts allowed failure from 43 minutes to 4 a month, and usually needs multi-zone or multi-region redundancy, automated failover and far more testing. Redundant capacity costs money every hour; active-active across regions roughly doubles infrastructure and adds data consistency work. Resilience patterns themselves cost little: a timeout or breaker check is a few microseconds per call. Retries cost load, which the example shows can quadruple during outages. The costs of downtime, lost bookings, stranded passengers and penalties under SLAs, usually decide how many nines are worth buying.

### In other languages (optional)

**Java.** Resilience4j provides circuit breakers, retries, bulkheads, rate limiters and time limiters, and Spring integrates them.

**Node.js.** Libraries such as opossum implement circuit breakers; `AbortSignal.timeout`, from Talking to the backend (9.7), gives every fetch a timeout.

**Service meshes.** Istio and Linkerd, from Monoliths, modules and services (12.1), apply timeouts, retries, circuit breaking and outlier detection to calls between services in any language, without code changes.

**Everywhere.** Google's free Site Reliability Engineering books describe SLOs, error budgets, incident management and postmortems in depth.

### Key terms

- **Reliability**: A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Service level indicator**: A measurement of service quality that users care about, such as the share of fast, successful requests. Often shortened to SLI.
- **Service level objective**: A target for a service level indicator over a period, such as 99.9% over 30 days. Often shortened to SLO.
- **Service level agreement**: A contract promising a level of service, with penalties if it is missed. Often shortened to SLA.
- **Error budget**: The failure an SLO allows, such as 0.1% of requests, spent on change and risk.
- **Burn rate**: How fast an error budget is being used, the basis for reliability alerts.
- **Redundancy**: Having more than one of any component whose failure would matter.
- **Single point of failure**: A component whose failure alone brings the system down.
- **Failover**: Switching work from a failed component to a standby or replica.
- **Timeout**: A limit on how long to wait for an operation before giving up.
- **Retry amplification**: Retries multiplying the load on a failing service, worse when several layers retry.
- **Circuit breaker**: A guard that stops calling a failing dependency for a while, failing fast instead.
- **Fallback**: What to do instead when a dependency fails, such as using cached data.
- **Graceful degradation**: Offering a reduced service instead of failing completely.
- **Bulkhead**: A separate, limited pool of resources per dependency, so one can't exhaust everything.
- **Load shedding**: Rejecting some work quickly under overload so the rest stays fast.
- **Chaos engineering**: Deliberately injecting failures, in a controlled way, to test that a system copes.
- **Incident**: An unplanned disruption that affects users or threatens to.
- **Blameless postmortem**: A review after an incident that looks for system causes and fixes, not people to blame.
- **Disaster recovery**: The plans and tools for restoring service after a large failure, such as a lost region.
- **Recovery point objective**: The most data, measured in time, that the business can afford to lose. Often shortened to RPO.
- **Recovery time objective**: The longest a service may be down while it is restored. Often shortened to RTO.

### Where this leads

This ends Part 12. Part 13 turns to working with AI coding assistants, starting with How coding assistants work (13.1). The measurements behind SLOs are in Observability (11.6), the release practices that make change safe are in CI/CD pipelines (11.4), and the capacity limits that load shedding protects are in Scaling and performance (12.4).

### Check yourself

1. In `resilience.ts`, add a strategy with a timeout, one retry and the circuit breaker, and compare its calls to pricing and fallbacks with the others.
2. Make the outage partial, with 30% of calls failing instead of all of them, and see which strategies now benefit from retries.
3. Calculate the availability of a booking request that needs the API, the database and a payment provider in sequence at 99.95%, 99.99% and 99.9%, and what adding a second, independent payment provider would do.
4. Write the SLIs and SLOs for online check-in: what is measured, over what period, with what target, and what the error budget allows.
5. Write a one-page disaster recovery plan for a system you know, with its RPO and RTO, and check when its backups were last restored.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A retry storm that took down a recovering service

Pricing restarts. 2,000 clients retry immediately and in sync, and it falls over again.

*Backoff with jitter, a breaker and a budget*

```csharp
builder.Services.AddHttpClient<PricingClient>()
    .AddResilienceHandler("pricing", b => b
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,                             // spreads retries out
            Delay = TimeSpan.FromMilliseconds(200),
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5, MinimumThroughput = 20, BreakDuration = TimeSpan.FromSeconds(15),
        })
        .AddTimeout(TimeSpan.FromMilliseconds(800)));
```

> **Lesson:** Retries without jitter synchronize clients. Back off, add jitter, and stop calling a dependency that is down.

#### Example 2: Degrading instead of failing

When the recommendations service is down, the whole booking page returns 500.

*A decided fallback*

```csharp
IReadOnlyList<Offer> offers;
try
{
    offers = await recommendations.GetAsync(customerId, ct).WaitAsync(TimeSpan.FromMilliseconds(300), ct);
}
catch (Exception ex) when (ex is TimeoutException or HttpRequestException or BrokenCircuitException)
{
    logger.LogWarning(ex, "Recommendations unavailable, using defaults");
    offers = DefaultOffers.ForMarket(market);     // the page still works
}
```

> **Lesson:** Decide in advance what still works when a dependency fails, and keep optional features optional.

### Resilience & Distributed Systems Patterns

<p class="src">From <em>.NET Backend Deep Dive</em> · Resilience & Distributed Systems Patterns</p>

<a id="dn-47"></a>

#### Retries — doing them wrong is worse than not doing them

**Three rules:**

1. **Only retry idempotent operations.** Retrying `POST /payments` charges twice. Retrying a `GET` is safe.
2. **Only retry transient failures.** 500/503/timeout/network → retry. 400/401/404/422 → retrying is pointless and just burns capacity.
3. **Always use exponential backoff with jitter.** Without jitter, 1,000 clients all retry at the two-second mark → a **retry storm** that kills the service just as it recovers.

```csharp
// .NET 8+ Polly v8 via Microsoft.Extensions.Http.Resilience
builder.Services.AddHttpClient<IPaymentApi, PaymentApi>()
    .AddResilienceHandler("payment", b =>
    {
        b.AddTimeout(TimeSpan.FromSeconds(3));              // per-attempt timeout

        b.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,                               // essential
            Delay = TimeSpan.FromMilliseconds(200),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError
                                || r.StatusCode == HttpStatusCode.TooManyRequests)
        });

        b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,                             // 50% failures
            MinimumThroughput = 20,                         // across at least 20 requests
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15)
        });

        b.AddTimeout(TimeSpan.FromSeconds(10));             // total pipeline timeout
    });
```

**Timeouts must be hierarchical**, or retries never get a chance to run:

```
Client timeout (30s)  >  API Gateway (29s)  >  App total (10s)  >  per attempt (3s) x 3
```

If each attempt gets 10s and the total budget is also 10s, the retry never happens.

**Retry amplification:** three service tiers each retrying three times means the bottom tier sees **27 requests** per original request. Retry at **one** tier only (usually the one closest to the backend, or at the edge), or apply a **retry budget** (cap retries at, say, 10% of traffic).

<a id="dn-48"></a>

#### Circuit breaker — the states

```
CLOSED  ──(failure ratio exceeded)──>  OPEN
   ▲                                    │
   │                              (after BreakDuration)
   │                                    ▼
   └──(trial succeeds)──────────── HALF-OPEN ──(trial fails)──> OPEN
```

- **Closed**: traffic flows normally while failures are counted.
- **Open**: **fail fast immediately**, don't call the backend. That's the whole value — it gives the backend room to recover and gives the caller a fast answer instead of a 30-second hang.
- **Half-open**: let a few trial requests through.

Always pair it with a **fallback**: stale cache, a default value, or a degraded feature. An open circuit shouldn't just become a 500 for the user.

<a id="dn-49"></a>

#### Idempotency keys — preventing double charges

```csharp
[HttpPost("payments")]
public async Task<IActionResult> Pay(
    [FromHeader(Name = "Idempotency-Key")] string key,
    PaymentRequest req, CancellationToken ct)
{
    if (string.IsNullOrEmpty(key)) return BadRequest("Idempotency-Key required");

    // Insert with a UNIQUE INDEX on (Key, UserId) -> the database makes this atomic
    var existing = await _store.TryBeginAsync(key, _user.Id, Hash(req), ct);

    if (existing is { Status: "completed" })
        return Ok(existing.Response);                  // return the old result, don't re-charge
    if (existing is { Status: "in_progress" })
        return StatusCode(409, "Request in progress"); // the client can retry later
    if (existing is not null && existing.RequestHash != Hash(req))
        return Conflict("Key reused with different payload");

    var result = await _payment.ChargeAsync(req, ct);
    await _store.CompleteAsync(key, result, ct);
    return Ok(result);
}
```

The key should be **client-generated** (a GUID) with a 24-hour TTL. This is how Stripe does it.

<a id="dn-53"></a>

#### Graceful shutdown

When Kubernetes sends SIGTERM, an app that exits immediately drops in-flight requests and loses partially processed messages.

```csharp
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));   // default is only 5 seconds

// In a BackgroundService: honor stoppingToken but finish the work in hand
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var batch = await DequeueAsync(stoppingToken);
        // Don't pass stoppingToken here - let the batch finish
        await ProcessAsync(batch, CancellationToken.None);
    }
}
```

In Kubernetes, add `preStop: sleep 5` so the load balancer removes the pod from its endpoints before the app shuts down; otherwise requests still route to a terminating pod.

---

### Incident Response & On-Call

<p class="src">From <em>.NET Backend Deep Dive</em> · Incident Response & On-Call</p>

<a id="dn-190"></a>

#### Roles during an incident

Even with three people, separate the roles explicitly — the most common failure in a live incident is four engineers debugging the same thing while nobody talks to the business.

| Role | Does | Does not |
|---|---|---|
| **Incident commander** | Coordinates, decides, tracks the timeline | Debug |
| **Operations** | Investigates and applies fixes | Communicate externally |
| **Comms** | Updates the status page and stakeholders on a fixed cadence | Interrupt ops for details |

<a id="dn-191"></a>

#### Sequence

```
1. Declare it.      A named incident beats "is anyone else seeing this?"
2. Assess severity. Who is affected, how badly, is it getting worse?
3. MITIGATE.        Restore service. Diagnosis comes later.
4. Communicate.     Fixed cadence, even with nothing new: "still investigating, next update 20:30".
5. Resolve + monitor.
6. Postmortem within a few days, while memory is fresh.
```

**Mitigation before diagnosis** is the rule people violate most. Roll back, fail over, disable the feature flag, scale out, shed load — then find out why. An hour spent understanding the root cause while users are down is an hour of outage you chose to take.

The highest-value question early on is always: **"what changed?"** Deploy, config, feature flag, traffic pattern, an upstream provider, a certificate, a scheduled job. Most incidents are correlated with a change, which is precisely why deploy markers on your dashboards pay for themselves.

| Severity | Rough meaning | Response |
|---|---|---|
| SEV1 | Core function down or data loss | Page immediately, all hands, status page |
| SEV2 | Major degradation or a key feature broken | Page, work it now |
| SEV3 | Minor or with a workaround | Next business day |

<a id="dn-192"></a>

#### Runbooks

A runbook is written **before** the incident, for a tired person at 3am with no context:

```markdown
## Alert: OrderApi p99 > 1s
Impact:  checkout feels slow; 5xx if it exceeds the gateway timeout
Dashboard: <link>   Traces: <link>   Deploy history: <link>

Checks, in order
1. Recent deploy? -> roll back first, investigate after
2. DB: blocking sessions, slow queries        <query link>
3. Thread pool queue length rising?           -> sync-over-async regression
4. Redis reachable? Hit rate collapsed?       -> cold cache after restart
5. Upstream payment provider status           <link>

Mitigations
- Roll back:   <command>
- Scale out:   <command>
- Disable recommendations feature flag: <link>

Escalate to: #team-orders, then <name>
```

The test of a runbook is whether someone outside the team can follow it. If every step ends in "ask Minh," it isn't a runbook.

<a id="dn-193"></a>

#### Blameless postmortems

Blameless is not a politeness convention — it's the only way to get accurate information. If naming the cause gets someone punished, people stop volunteering what actually happened, and you lose the ability to fix the system.

The framing that works: **"how did the system allow a reasonable person to cause this?"** A deploy that took production down is a missing gate, not a careless engineer.

```markdown
Summary      One paragraph a non-engineer can follow
Impact       Duration, users affected, orders lost, in numbers
Timeline     First symptom -> detection -> mitigation -> resolution, with times
Root cause   Usually multiple contributing factors, not one
What went well
What was hard  (Detection took 40 min. Why? The alert didn't exist.)
Action items  Owner + date + ticket. Anything without all three won't happen.
```

Track **time to detect** separately from **time to mitigate**. Long detection is an observability problem; long mitigation is a tooling and runbook problem. They have completely different fixes, and averaging them hides both.

Fix the class, not the instance. "Added a null check" is a symptom fix; "added a contract test so this breaks in CI" addresses the category.

---

### Backup, Restore & Disaster Recovery

<p class="src">From <em>.NET Backend Deep Dive</em> · Backup, Restore & Disaster Recovery</p>

<a id="dn-224"></a>

#### Two numbers define everything

```
RPO  Recovery Point Objective  - how much data may we lose?   -> backup frequency
RTO  Recovery Time Objective   - how long may we be down?     -> restore mechanism
```

Both are **business decisions**, not technical ones, and they should be written down. "As little as possible" is not an answer — it's how you end up paying for a multi-region active-active setup to protect a system nobody would notice being down for an hour.

| RPO/RTO | Mechanism | Relative cost |
|---|---|---|
| 24h / 24h | Nightly backup, manual restore | Very low |
| 1h / 4h | Point-in-time restore from transaction logs | Low |
| 5m / 1h | Continuous replication, promote a replica | Medium |
| ~0 / minutes | Multi-region with automated failover | Very high |

<a id="dn-225"></a>

#### An untested backup is not a backup

This is the section's entire point. Backups fail silently, and the failure is discovered at exactly the worst moment. The failure modes are mundane: the job has been erroring for three months and nobody read the alert, the backup is of the wrong database, it's encrypted with a key stored only in the system that just died, or it restores fine but takes fourteen hours — which nobody knew because nobody timed it.

```
Quarterly, at minimum:
  1. Restore to a scratch environment from the actual backup artifact
  2. TIME it -> this number IS your RTO, not the one in the document
  3. Verify integrity: row counts, a checksum, a real query
  4. Confirm the restored system starts and serves traffic
```

Also verify **what's not in the backup**: blob storage, secrets and key material, message queue contents, configuration, and DNS. A restored database with no S3 objects and no Key Vault access is not a restored system.

<a id="dn-226"></a>

#### Ransomware and the immutability requirement

Modern threats delete backups first. Defenses that matter: **immutable/WORM storage** (S3 Object Lock, Part 9.4), backups in a **separate account** with separate credentials, MFA delete, and at least one copy offline or logically air-gapped. The old 3-2-1 rule still holds — three copies, two media, one off-site.

Backups reachable with the same credentials as production are not a recovery plan.

<a id="dn-227"></a>

#### Application-level resilience

Database recovery is only part of it:

- **Replay:** an outbox or event log lets you rebuild read models and search indexes without a database restore (Part 12.6).
- **Idempotency** makes replaying a queue after failover safe rather than a duplicate-charge event (Part 12.3).
- **Graceful degradation:** what still works when the database is read-only during a failover? Being able to serve cached reads while writes are down is often the difference between "degraded" and "down."
- **Practice it.** A failover procedure first executed during a real incident is a hypothesis, not a plan. Game days are how you find that the runbook references a server decommissioned last year.

---

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a134"></a>

#### Resilience: retries, circuit breakers and bulkheads

In a distributed system, every remote call fails eventually. Resilience policies decide whether that failure stays local or takes down the cluster.

| Strategy                    | Protects against                           | Watch out for                                |
|-----------------------------|--------------------------------------------|----------------------------------------------|
| Timeout                     | Hanging calls holding resources            | Must be shorter than the caller's timeout    |
| Retry with jittered backoff | Transient blips                            | Only for idempotent calls; never retry a 400 |
| Circuit breaker             | Hammering a service that is already down   | Needs a half-open probe to recover           |
| Bulkhead                    | One slow dependency starving the whole app | Sizing is empirical                          |
| Fallback                    | Total failure of a non-critical dependency | Degrade honestly, do not fake data           |
| Hedging                     | Tail latency                               | Multiplies load; idempotent reads only       |

``` csharp
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true                       // avoids synchronised retry storms
    })
    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5, MinimumThroughput = 20,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15)
    })
    .AddTimeout(TimeSpan.FromSeconds(5))
    .Build();
```

``` text
  circuit breaker states

   CLOSED  --failures exceed threshold-->  OPEN  --break duration elapses-->  HALF-OPEN
     ^                                      |                                    |
     |                                 fail fast,                          one trial call
     +---------- trial succeeds ------------+------- trial fails ----------------+
```

> **\[Trap\]**
>
> **Common trap.** Retries at three layers multiply: 3 × 3 × 3 = 27 requests for one user action. Decide on one layer that retries — usually the outermost client — and make the others fail fast.

###### Worked example

Why unjittered retries make outages worse, with numbers. A downstream service has a two-second blip. Two thousand in-flight requests hit it.

``` text
  WITHOUT jitter: every client retries at exactly t+1s, t+2s, t+4s

    t=0.0s   2,000 requests fail
    t=1.0s   2,000 retries arrive SIMULTANEOUSLY  -> service, now recovering, is
                                                     crushed again and fails all of them
    t=2.0s   2,000 retries arrive simultaneously  -> same
    t=4.0s   2,000 retries arrive simultaneously  -> same

    the downstream service never gets a quiet moment to recover.
    a 2-second blip becomes a 10-minute outage. This is a "retry storm".

  WITH full jitter: delay = random(0, base * 2^attempt)

    t=0.0-1.0s   retries spread evenly across a full second -> ~2,000 rps smoothed
    t=0.0-2.0s   next wave spread across two seconds
    the service sees a gentle ramp and recovers on the first attempt.
```

``` csharp
.AddRetry(new HttpRetryStrategyOptions
{
    MaxRetryAttempts = 3,
    BackoffType = DelayBackoffType.Exponential,
    UseJitter = true,                                  // never turn this off
    Delay = TimeSpan.FromMilliseconds(200),
    // retry only what is safe and worth retrying
    ShouldHandle = args => ValueTask.FromResult(
        args.Outcome.Result is { StatusCode: HttpStatusCode.RequestTimeout
                                           or HttpStatusCode.TooManyRequests
                                           or >= HttpStatusCode.InternalServerError }
        || args.Outcome.Exception is HttpRequestException or TimeoutRejectedException),
    OnRetry = args =>
    {
        _log.LogWarning("Retry {Attempt} after {Delay}ms for {Url}",
            args.AttemptNumber, args.RetryDelay.TotalMilliseconds, args.Context.OperationKey);
        return default;
    }
})
```

> **\[Trap\]**
>
> **Common trap.** Never retry a 400, 401, 403, 404, 409 or 422. They will fail identically every time, and retrying them turns one bad request into four, plus three times the log noise and latency. Retry 408, 429, 502, 503, 504 and transport exceptions — and honour `Retry-After` when the server sends it.

###### Timeout budgets, and why they must decrease inward

``` text
  browser              30 s
    gateway            25 s        <- must be less, or the gateway keeps working
      orders-api       10 s           on a request nobody is waiting for
        billing-api     4 s
          database      2 s

  each layer's timeout is strictly less than its caller's, with headroom for
  retries: if orders-api retries billing 3 times at 4 s, that is 12 s of budget
  against a 10 s limit -> the retry policy must fit INSIDE the timeout, not outside.

  correct nesting:  total timeout ( retry ( attempt timeout ) )
```

``` csharp
// Polly orders strategies outside-in, so this is: total -> retry -> per-attempt
var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddTimeout(TimeSpan.FromSeconds(10))          // OUTERMOST: total budget
    .AddRetry(new() { MaxRetryAttempts = 2, Delay = TimeSpan.FromMilliseconds(200),
                      BackoffType = DelayBackoffType.Exponential, UseJitter = true })
    .AddTimeout(TimeSpan.FromSeconds(3))           // INNERMOST: per attempt
    .Build();
// worst case: 3 + 0.2 + 3 + 0.4 + 3 = 9.6 s, safely inside the 10 s total.
```

Getting this backwards — a per-attempt timeout longer than the total, or a caller whose timeout is shorter than its callee's — produces the most confusing failure mode in distributed systems: work that completes successfully downstream while the caller has already given up, so the user sees an error and the side effect still happened.

###### Circuit breaker tuning, with the reasoning

| Setting             | Too low                                    | Too high                             | Reasonable start           |
|---------------------|--------------------------------------------|--------------------------------------|----------------------------|
| `FailureRatio`      | Trips on normal error noise                | Never trips during a partial outage  | 0.5                        |
| `MinimumThroughput` | Trips on 2 failures at 3am with no traffic | Never trips on a low-traffic service | 20–100 per sampling window |
| `SamplingDuration`  | Noisy, flaps open and closed               | Slow to notice a real outage         | 30 s                       |
| `BreakDuration`     | Hammers a recovering service               | Stays down long after recovery       | 15–30 s                    |

The most common misconfiguration is a minimum throughput that a low-traffic endpoint never reaches, so the breaker is decorative. The second most common is one breaker shared across all downstream hosts, so a single bad replica opens the circuit for the healthy ones — partition the breaker per host, which the standard resilience handler does by default.

### Integration & Distributed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Integration & Distributed</p>

<p class="pd">Patterns for systems made of parts that can fail independently. These postdate the GoF catalogue entirely, and they are where most modern design difficulty actually lives.</p>

<a id="be-h771"></a>

#### Retry, timeout, circuit breaker and bulkhead

Four resilience patterns that are almost always used together, and are wrong when used separately.

``` csharp
// .NET 8+ ships these as a standard pipeline
builder.Services.AddHttpClient<IBillingClient, BillingClient>(c =>
{
    c.BaseAddress = new Uri(cfg["Billing:Url"]!);
    c.Timeout = TimeSpan.FromSeconds(10);        // ALWAYS. Default is 100 seconds.
})
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.Retry.UseJitter = true;                    // <- see below
    o.CircuitBreaker.FailureRatio = 0.5;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);       // PER ATTEMPT
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10); // ALL attempts
});
// the two timeouts must be consistent: attempts x per-attempt must not
// exceed the total, or the total fires mid-retry and the policy is incoherent.
```

``` text
  WHY JITTER, and it is not a detail

    100 clients call a service. It fails.
    all 100 retry after exactly 1s   -> 100 simultaneous requests
    all 100 retry after exactly 2s   -> 100 simultaneous requests

    the retry policy has converted a brief outage into a synchronised
    hammer that prevents recovery. This is a RETRY STORM, and it is
    self-inflicted.

    with jitter, the 100 retries spread across the window and the
    service can actually come back.

  CIRCUIT BREAKER states

    CLOSED     normal. Count failures.
      | failure ratio exceeded
      v
    OPEN       FAIL FAST. Do not call at all. The dependency gets room
      |        to recover, and YOUR threads are not held waiting.
      | after a break duration
      v
    HALF-OPEN  let ONE request through.
                 succeeds -> CLOSED
                 fails    -> OPEN again

    the point is not protecting THEM. It is protecting YOU: an open
    circuit returns immediately instead of holding a thread for 3 seconds.
```

**Bulkhead** is the one most often omitted. It limits how many concurrent calls one dependency may consume, so a slow dependency cannot exhaust your entire thread pool or connection pool. Without it, one slow downstream service takes down a process that had four other perfectly healthy dependencies.

> **\[Trap\]**
>
> **Common trap.** Retrying a non-idempotent operation duplicates its effect. A retried `POST /charge` can charge twice — and the request that “timed out” may well have succeeded. Only retry reads, or writes carrying an idempotency key. Retry policies applied blanketly to every HTTP call are a common and expensive mistake.

###### The retry storm, and why jitter is not optional

``` text
  A RETRY STORM, step by step

    t=0     a dependency has a 2-second blip.
            500 in-flight requests fail simultaneously.
    t=1s    ALL 500 retry. The dependency, now recovering, receives
            500 requests in one instant and fails again.
    t=2s    all 500 retry again. And again at t=4s.

    the dependency CANNOT RECOVER, because every recovery window is
    immediately saturated by synchronised retries.

    a 2-second blip has become a 10-minute outage, and the cause is
    the retry policy - not the original fault.

  WITH JITTER

    each client waits base x random(0.5, 1.5).
    the 500 retries spread across a window. The dependency serves
    them at its own pace and recovers.

  AND THE SECOND CONTROL, which is less well known: A RETRY BUDGET.

    cap retries as a FRACTION of total traffic - say 10%.
    if more than 10% of requests are retries, STOP RETRYING.

    without it, a partial outage becomes total: every failing request
    triples the load, so a dependency at 90% capacity failing 20% of
    requests receives 140% of normal traffic and fails completely.
```

``` csharp
// Polly v8 - and note UseJitter is NOT the default in hand-rolled policies
new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,                          // <- the important one
        Delay = TimeSpan.FromSeconds(1),
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError)
            .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .Handle<HttpRequestException>(),
        // NOTE: 4xx other than 429 is NOT retried. The request is wrong;
        // it will be wrong again.
    })
    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 20,          // do not trip on 2 failures out of 3
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15),
    })
    .AddConcurrencyLimiter(permitLimit: 50, queueLimit: 0)   // BULKHEAD
    .Build();
```

| Control         | Protects                  | Without it                                  |
|-----------------|---------------------------|---------------------------------------------|
| Timeout         | Your threads              | One slow dependency holds every worker      |
| Retry           | Against transient faults  | A blip becomes a user-visible error         |
| **Jitter**      | The dependency's recovery | **Synchronised retry storms**               |
| Circuit breaker | Both sides                | You keep paying the timeout on every call   |
| **Bulkhead**    | Your other dependencies   | One slow dependency exhausts the whole pool |
| Retry budget    | Against amplification     | A partial outage becomes total              |

### The data layer as an architecture

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The data layer as an architecture</p>

<a id="be-m263"></a>

#### Resilience: retries, breakers, and timeouts

Transient failures are normal in any networked system: a failover, a brief packet loss, a deadlock victim, a throttled managed database. Handling them is a design decision, not an afterthought.

``` csharp
// EF's built-in strategy handles the provider's known-transient error list.
options.UseSqlServer(cs, o => o.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(10),
    errorNumbersToAdd: [1205]));                 // add deadlock victim

// It is incompatible with a user-initiated transaction unless the whole unit
// of work goes through the strategy - otherwise a retry replays only part of it:
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async ct => {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // ... all the work ...
    await tx.CommitAsync(ct);
}, ct);
```

| Policy                                  | Protects against                         | Danger                                   |
|-----------------------------------------|------------------------------------------|------------------------------------------|
| Retry with exponential backoff + jitter | brief transient faults                   | amplifying load during an outage         |
| Circuit breaker                         | a dependency that is genuinely down      | tripping on normal error rates           |
| Timeout                                 | unbounded waits                          | too short → retry storms                 |
| Bulkhead (bounded concurrency)          | one slow dependency consuming everything | rejecting work that would have succeeded |
| Fallback                                | degraded but useful responses            | hiding real failures from monitoring     |

``` csharp
// Microsoft.Extensions.Resilience (Polly v8) - the standard pipeline shape:
var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(5))                        // per attempt
    .AddRetry(new RetryStrategyOptions {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,                                       // essential - see below
        ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient)
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions {
        FailureRatio = 0.5, SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 20, BreakDuration = TimeSpan.FromSeconds(15)
    })
    .Build();
```

> **\[Trap\]**
>
> **Retries without jitter synchronise your clients.** After a brief outage, every caller retries at the same backoff intervals, producing a thundering herd that re-breaks the recovering service — the same lockstep failure as unjittered cache TTLs ([Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)). Jitter is not a refinement; it is the part that makes retry safe.

> **\[Trap\]**
>
> **Only retry idempotent operations.** A timeout tells you nothing about whether the write committed — the response may simply have been lost. Retrying a non-idempotent insert double-charges someone. Give writes a client-supplied idempotency key and make the handler converge on repeat ([Consistency across stores](07-06-non-relational-stores.md#be-m210), [Upsert: MERGE and the alternatives](07-02-the-relational-model-and-sql.md#be-m193)).

Worked example: [Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303)

First principles: [TCP: reliability built on unreliability](06-03-tcp-and-udp.md#be-m14)

Goes further: [Microservices: what you buy and pay](12-01-monoliths-modules-and-services.md#be-m246)

Applied in: [Health checks and readiness](11-06-observability.md#be-e543)

Recipe: [Recipe: a resilient outbound API client](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m287)

### Reliability & Operations

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Reliability & Operations</p>

<p class="pd">Knowing the system is healthy, finding out quickly when it is not, and having practised what you will do.</p>

<a id="be-g710"></a>

#### Capacity, incidents and disaster recovery

##### Capacity planning

``` text
# the arithmetic, done before you need it
#
#   peak requests/sec  x  average CPU-seconds per request  =  cores needed
#   400 rps  x  0.015 CPU-s  =  6 cores of actual work
#   at a 60% utilisation target -> 10 cores -> 3 instances of 4 cores
#
# then ask the questions the number does not answer:
#   what is the growth rate, and when does this become insufficient?
#   what happens if one availability zone is lost? (capacity must survive N-1)
#   how long does scaling take? (autoscaling is not instant)
#   what breaks FIRST - the app, the database, the connection pool, a quota?
```

The last question is the useful one. Load testing to find *which* component saturates first is far more valuable than a headline requests-per-second number, because that component is the one to fix or to scale.

##### Incident response

1.  **Declare it.** An unnamed incident has no owner and no timeline.
2.  **Assign roles** — someone commanding, someone investigating, someone communicating. One person doing all three does none well.
3.  **Mitigate before diagnosing.** Roll back, disable the flag, shed load, fail over. Understanding can wait; the outage cannot.
4.  **Communicate on a schedule**, even when there is nothing new. Silence is interpreted as absence.
5.  **Keep a timeline as you go.** Reconstructing it afterwards from memory is unreliable and slow.
6.  **Review blamelessly.** The output is systemic changes with owners and dates — not a name.

##### Disaster recovery

| Term         | Means                      | Determines                            |
|--------------|----------------------------|---------------------------------------|
| **RTO**      | How long recovery may take | Your HA architecture                  |
| **RPO**      | How much data you may lose | Your backup and replication frequency |
| **Failover** | Switching to the standby   | Must be tested to be real             |
| **Failback** | Returning afterwards       | **The step everyone forgets to plan** |

- **The full restore drill**: rebuild production into an empty environment from backups only, following the written runbook, and time it. Almost every team discovers something missing the first time — a role, an extension, a certificate, a sequence value, or that it takes nine hours rather than one.
- **Test failover deliberately**, on a schedule, not during an incident.
- **Game days**: inject a failure in a controlled window and see what happens. Kill an instance, block a dependency, fill a disk.
- **Runbooks must be executable by someone who did not build it** — that is the person who will be on call.
- **Keep the runbook somewhere reachable when your systems are down.** A runbook in a wiki hosted on the failed cluster is not a runbook.

###### Capacity from arrival rate, and the restore drill

``` text
  LITTLE'S LAW, which turns capacity from opinion into arithmetic

      concurrency = arrival rate  x  average time in system

    400 requests/sec x 50 ms = 20 concurrent requests
      -> ~20-40 worker threads, and a pool of ~20-40 connections.
      -> NOT 400. Sizing from "requests per second" directly is the
         most common capacity error there is.

  and it works backwards, which is more useful:

    a pool of 20 connections, each held 50 ms
      -> maximum throughput = 20 / 0.05 = 400 requests/sec.
      -> if a query slows to 200 ms, capacity drops to 100/sec.
         SAME POOL. FOUR TIMES LESS THROUGHPUT.

    which is why "one slow query" is a capacity incident, not a
    latency inconvenience - and why the fix is usually the query,
    not a bigger pool.

  headroom: size for peak x 2. Peak is not average, and the 20%
  headroom people quote disappears the moment one instance is
  restarting during a deploy.
```

1.  **Write down the scenario.** “The primary database region is unavailable”, or “a migration corrupted the orders table at 14:32”.
2.  **Restore into an empty environment**, from backups only. Never over the live system — that removes your remaining options if the target time was wrong.
3.  **Have someone who did not write the runbook execute it.** Every step they cannot follow is a gap.
4.  **Time it.** That number is your real RTO, whatever the plan claims.
5.  **Verify the data**, not just that the service starts. Row counts, a known record, a recent transaction.
6.  **Record what was missing**, assign owners, and schedule the next drill.

| What a first restore drill finds             |                                               |
|----------------------------------------------|-----------------------------------------------|
| A role or permission that was never scripted | The application connects but cannot read      |
| A missing extension or collation             | Migrations fail on the restored instance      |
| Sequences not restored correctly             | Duplicate key violations on the first insert  |
| Secrets that live only in the old vault      | Nothing can authenticate                      |
| A certificate someone installed by hand      | TLS fails                                     |
| It takes nine hours, not one                 | The stated RTO was never achievable           |
| Nobody knows who declares a disaster         | The organisational gap, and often the largest |

> **\[Note\]**
>
> **An untested backup is a hypothesis.** The usual outcome of a first drill is not that the backups were missing — they exist. It is that the *procedure* around them was never complete, and the gaps only become visible when someone follows it under time pressure. Schedule it, rotate who runs it, and treat every gap as a bug with an owner and a date.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a204"></a>

#### Backups, disaster recovery and resilience testing

| Term | Question it answers                                                          |
|------|------------------------------------------------------------------------------|
| RPO  | How much data can we afford to lose? → backup frequency and replication mode |
| RTO  | How long can we be down? → failover architecture                             |
| MTTR | How fast do we typically recover? → tooling and runbook quality              |

1.  Decide RPO and RTO per system *with the business*. A five-minute RPO and a one-hour RTO cost very different amounts from twenty-four hours of each.
2.  Automate backups, and separately automate a scheduled **restore** into a scratch environment. An untested backup is a rumour.
3.  Store backups in a different failure domain — different region, and ideally a different account, so a compromised credential cannot delete them.
4.  Write the failover runbook, then execute it on a calm afternoon while someone times it.
5.  Include the things people forget: DNS, certificates, secrets, message-broker state, and the pipeline that deploys the app.

##### Chaos, in moderation

You do not need a chaos platform to get most of the benefit. Kill a pod during a load test. Block the database port for thirty seconds and watch whether the circuit breaker behaves. Fill a disk. Expire a token. Each experiment turns an assumption into a fact, and the ones that surprise you are the ones worth automating.

> **\[Note\]**
>
> **The single most valuable exercise.** Restore your production database into an empty environment, from backups only, with the runbook, timed. Nearly every team that tries this for the first time discovers something missing — and discovers it on a Tuesday rather than during an outage.

###### The restore drill, and what it always finds

1.  **Pick a scenario and write it down** — “the primary database region is unavailable”, or “a migration corrupted the orders table at 14:32”.
2.  **Restore into an empty environment**, from backups only. Never over the live system — that removes your remaining options if the target time was wrong.
3.  **Follow the written runbook exactly**, and have it executed by someone who did not write it. Every step they cannot follow is a gap.
4.  **Time it.** That number is your real RTO, whatever the plan claims.
5.  **Verify the data**, not just that the service starts. Row counts, a known record, a recent transaction.
6.  **Write down what was missing** — and something always is.

| What a first restore drill typically uncovers         |                                               |
|-------------------------------------------------------|-----------------------------------------------|
| A database role or permission that was never scripted | The application connects but cannot read      |
| A missing extension or collation                      | Migrations fail on the restored instance      |
| Sequence values not restored correctly                | Duplicate key violations on the first insert  |
| Secrets that live only in the old Key Vault           | Nothing can authenticate                      |
| A certificate nobody knew was manually installed      | TLS fails                                     |
| It takes nine hours, not one                          | The stated RTO was never achievable           |
| Nobody knows who is authorised to declare a disaster  | The organisational gap, and often the biggest |

``` csharp
// resilience testing beyond restores - inject failure deliberately
// Simmy (Polly) lets you fault a dependency from CONFIGURATION,
// so a chaos experiment does not need a code change.
services.AddHttpClient<IBillingClient>()
    .AddResilienceHandler("chaos", b => b
        .AddChaosFault(new ChaosFaultStrategyOptions
        {
            InjectionRate = 0.05,                       // 5% of calls
            FaultGenerator = new FaultGenerator()
                .AddException(() => new HttpRequestException("chaos")),
            Enabled = cfg.GetValue<bool>("Chaos:Enabled"),   // off by default
        }));
// run it in a non-production environment first, then in production during
// a declared window. What you learn is whether your RETRY and CIRCUIT
// BREAKER settings do what you assumed - which is almost never obvious.
```

> **\[Note\]**
>
> **An untested backup is a hypothesis.** The most common outcome of a first restore drill is not that the backups were missing — they usually exist. It is that the *procedure* around them was never complete, and the gap only becomes visible when someone tries to follow it under time pressure. Schedule it, rotate who runs it, and treat every gap found as a bug with an owner.

<a id="be-a206"></a>

#### Multi-region and active-active

Backups protect you from data loss. Multi-region protects you from a region being unavailable, which is a different failure and needs a different design. The decision is driven entirely by what the business will pay for, expressed as RTO and RPO.

| Topology                | RTO      | RPO              | Cost  | Application impact                                          |
|-------------------------|----------|------------------|-------|-------------------------------------------------------------|
| Single region + backups | Hours    | Minutes to hours | 1×    | None                                                        |
| Warm standby (passive)  | Minutes  | Seconds          | ~1.4× | Small — must tolerate a failover                            |
| Active-passive (hot)    | \< 1 min | Near zero        | ~1.8× | Read-only mode during failover                              |
| Active-active           | Seconds  | Near zero        | ~2.2× | **Large** — conflict resolution, global IDs, data residency |

``` text
  active-passive: the pragmatic choice for most systems

    Traffic Manager / Front Door / Route 53
        |                          |
        v (100%)                   v (0%, health-checked)
    region A  ---- async replication ---->  region B
      app + db primary                        app + db replica (read-only)

    failover: promote B's database, flip DNS/traffic weight, accept the replication gap.
    the hard part is not the compute - it is the database promotion and the DNS TTL.

  active-active: both regions serve writes

    the question you must answer: what happens when the same order is modified
    in both regions during a partition?
      - partition by tenant/region so a given record is only ever written in one place  <- do this
      - or last-write-wins with vector clocks and accept lost updates
      - or CRDTs, if the data model permits it
```

``` csharp
// what the application must actually do differently

// 1. region-aware health so the traffic manager fails over on the right signal
app.MapHealthChecks("/health/region", new HealthCheckOptions
{
    // fail this if the LOCAL database primary is unreachable, so traffic leaves the region
    Predicate = c => c.Tags.Contains("region-critical")
});

// 2. a read-only mode for the passive side and for failover windows
public sealed class ReadOnlyGuard(IOptionsMonitor<RegionOptions> region) : IPipelineBehavior<...>
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        if (region.CurrentValue.ReadOnly && req is ICommand)
            throw new ServiceUnavailableException("region.read_only",
                "This region is currently read-only. Please retry shortly.");
        return await next();
    }
}

// 3. globally-unique, sortable identifiers so two regions cannot collide
var id = Guid.CreateVersion7();       // time-ordered, no coordination needed
```

###### The parts people forget until the drill

- **DNS TTL.** A 300-second TTL means a five-minute RTO floor no matter how fast your failover is. Lower it to 30–60 seconds *before* you need it.
- **Certificates and secrets in both regions.** A vault that only exists in region A makes region B useless.
- **The message broker.** In-flight messages in region A are not in region B. Decide whether they are lost, replicated, or replayed from an event log.
- **Scheduled jobs.** Both regions running the nightly invoice job means duplicate invoices. Leadership must be global, not per region.
- **The deployment pipeline.** If CI can only deploy to region A, you cannot patch during a region A outage.
- **Data residency.** Failing EU traffic over to a US region may be a compliance violation, which makes it a legal constraint on your topology, not an engineering choice.
- **Cost of egress.** Cross-region replication and chatty cross-region calls are billed per gigabyte and add up faster than the compute.

The most valuable thing you can do here is far cheaper than building active-active: run a documented, timed failover drill in a lower environment, quarterly. Every team that does this discovers something missing from the list above, and discovers it at a convenient time.

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a223"></a>

#### Running it: incidents, runbooks and on-call

Production reliability is mostly preparation. When something breaks at 03:00 the useful artefacts are the ones written at 14:00 on a calm Tuesday.

1.  **Acknowledge and assess.** What is the user-visible impact, and how many people? Impact drives urgency, not root cause.
2.  **Stop the bleeding.** Roll back, flip the feature flag, scale up, shed load. Diagnosis comes after the bleeding stops.
3.  **Communicate.** A status update every 30 minutes, even when the update is ‘still investigating’.
4.  **Diagnose with data.** Traces for where, metrics for how much, logs for exactly what. Recent deploys and config changes first — most incidents are self-inflicted and recent.
5.  **Write it up blamelessly.** Timeline, contributing factors, what made detection slow, and one or two concrete actions with owners.

##### What a runbook contains

- What this service does, who owns it, and what depends on it.
- The dashboard and log query links — actual links, not descriptions.
- How to scale it, restart it, and roll it back, as copy-pasteable commands.
- The three failures that have actually happened, and what fixed them.
- Who to escalate to for each downstream dependency.

> **\[Note\]**
>
> **Test your assumptions.** Restore a database backup on a schedule — an untested backup is a hope. Kill a pod in staging during a load test. Expire a certificate in a lower environment on purpose. Everything you have not exercised is an assumption.

###### A runbook someone else can follow at 3am

``` yaml
# Runbook: Orders API — elevated 5xx

## Is this the right runbook?
Alert: `OrdersApiErrorBudgetBurn`. Dashboard: <link>. Owner: #team-orders.

## Assess (2 minutes)
1. Dashboard <link> — is it ALL endpoints or one? All instances or one?
2. Recent deploys: `gh run list --workflow deploy.yml --limit 5`
3. Dependency health: <status page links>

## Mitigate FIRST — understanding can wait
- Deployed in the last hour?   -> ROLL BACK: `<exact command>`
- One instance unhealthy?      -> restart it: `<exact command>`
- A feature flag changed?      -> turn it off: <link to flag>
- Database slow?               -> check blocking: `<exact query>`
- Overloaded?                  -> scale out: `<exact command>`

## Escalate
No improvement in 15 minutes -> page <secondary>. Customer impact -> notify <channel>.

## Only then, diagnose
Traces <link> · Logs <query> · Recent changes <link>
```

| A runbook works when                         | Test                                            |
|----------------------------------------------|-------------------------------------------------|
| It states what alert it answers              | Can the responder tell they have the right one? |
| Mitigation comes before diagnosis            | Is the first action something that helps users? |
| Commands are copy-pasteable                  | No “check the database” without the query       |
| It says when to escalate, and to whom        | A named rota, not “ask someone”                 |
| It is reachable when systems are down        | **Not in a wiki hosted on the failed cluster**  |
| Someone who did not write it has followed it | The only test that matters                      |

1.  **Declare the incident.** An unnamed problem has no owner and no timeline.
2.  **Assign roles** — commander, investigator, communicator. One person doing all three does none well.
3.  **Mitigate before diagnosing.** Roll back, flag off, shed load, fail over.
4.  **Communicate on a schedule**, even with nothing new. Silence reads as absence.
5.  **Keep a timeline as you go.** Reconstructing it afterwards is unreliable.
6.  **Review blamelessly**, and produce systemic changes with owners and dates — not a name.

> **\[Note\]**
>
> **On-call is a system, not a rota.** Sustainable on-call needs a realistic alert volume, runbooks that work, permission to fix the causes of repeat pages, and time off after a bad night. A rota over an unmaintained alerting system produces burnout and then attrition — and the person who leaves takes the undocumented knowledge with them, which makes the next rotation worse.

### Reliability

<p class="src">From <em>Backend Engineering</em> · APIs · Reliability</p>

<p class="pd">This is all downstream of Fact 2: the channel can fail ambiguously.</p>

<a id="be-e520"></a>

#### Idempotency

**The most important operational property in API design.** If an operation is idempotent, a client that times out can simply retry. If not, retrying might charge a card twice.

`GET`, `PUT`, `DELETE` are idempotent by definition. `POST` is not — and `POST` is where the money usually is.

**Idempotency keys** fix this:

``` yaml
POST /payments
Idempotency-Key: 7f3a9c2e-...

→ server: if this key was seen, return the ORIGINAL response; do not re-execute
```

Implementation notes that matter:

- Store the key with the **response**, not just a "seen" flag, so retries get the same answer.
- Scope keys per-client so one tenant can't collide with another.
- Give them a TTL (24 hours is typical).
- Handle the concurrent case: two identical requests arriving simultaneously. A unique index on the key plus catching the constraint violation is the simplest correct approach.
- Ideally, hash the request body and reject a reused key with a *different* body — that's a client bug you want surfaced.

Stripe's implementation is the de facto reference design.

<a id="be-e521"></a>

#### Retries

**Only retry what's safe to retry.** Idempotent methods, or non-idempotent ones carrying an idempotency key.

**Only retry what might succeed:** 408, 429, 502, 503, 504, and connection-level failures. Never 4xx (except 429) — the request is wrong and will stay wrong.

**Exponential backoff with jitter:**

``` text
delay = min(cap, base × 2^attempt) × random(0.5, 1.5)
```

Jitter is not optional. Without it, every client that failed during an outage retries at the same instant, and your service dies again the moment it recovers. This is the **thundering herd**, and it's how brief outages become long ones.

**Cap total attempts and total time.** Three attempts, or a hard deadline. Retries multiply load exactly when the system is least able to take it.

**Retry budgets** are the mature version: allow retries only up to ~10% of total request volume. Beyond that, fail fast — if 30% of calls are failing, retrying is making it worse.

<a id="be-e522"></a>

#### Circuit breakers

When a dependency is down, continuing to call it wastes resources on both sides and delays recovery.

``` text
CLOSED  → normal; count failures
        → threshold exceeded →
OPEN    → fail immediately, no calls; after a cooldown →
HALF-OPEN → let one request through
        → success → CLOSED  |  failure → OPEN
```

The point isn't protecting yourself; it's giving the struggling dependency room to recover, and failing fast instead of tying up your own threads waiting on something that won't answer.

In .NET: `Microsoft.Extensions.Http.Resilience` (Polly v8) gives you retry, breaker, timeout, and hedging as one configured pipeline. Don't hand-roll this.

<a id="be-e523"></a>

#### Rate limiting

Protects you from abuse, runaway loops, and one tenant starving another.

| Algorithm         | Behavior                                                  |
|-------------------|-----------------------------------------------------------|
| Fixed window      | simple; allows 2× burst at the boundary                   |
| Sliding window    | smoother, more state                                      |
| Token bucket      | allows controlled bursts; usually the right default       |
| Leaky bucket      | perfectly smooth output                                   |
| Concurrency limit | caps in-flight, not rate — best for protecting a resource |

Always tell the client what happened:

``` yaml
HTTP/1.1 429 Too Many Requests
Retry-After: 30
RateLimit-Limit: 100
RateLimit-Remaining: 0
RateLimit-Reset: 1755432030
```

A 429 without `Retry-After` guarantees clients retry immediately and make it worse.

<a id="be-e524"></a>

#### Timeouts

Covered in depth in the async reference, Part V; the API-specific points:

- **Every network call needs an explicit timeout.** Defaults are usually absurd (`HttpClient` is 100 seconds).
- **Timeouts must decrease as you go deeper.** If your caller allows 10 s, your call to a dependency cannot allow 10 s — you need room to respond.
- **Propagate deadlines, not durations.** Otherwise each hop restarts the clock and downstream work continues long after the caller gave up. gRPC does this natively.

<a id="be-e525"></a>

#### Concurrency and consistency

Two clients editing the same resource is not an edge case; it's Tuesday.

**Lost updates.** A reads `{name:"Nam", email:"a@x"}`, B reads the same, A writes a name change, B writes an email change. B's `PUT` contains A's old name, so A's edit is silently gone. Nobody sees an error.

**Optimistic concurrency** fixes it with the ETag you already have:

``` yaml
GET /users/42            → 200, ETag: "v3"
PUT /users/42
If-Match: "v3"
                         → 200 if still v3
                         → 412 Precondition Failed if someone else wrote
```

The client then re-fetches, re-applies, and retries. This is the correct default for HTTP APIs: no locks held across a network, no server state, and the failure is explicit rather than silent.

**Pessimistic locking** — an explicit lock endpoint with a lease — is occasionally right for long human edits (a document someone has open for twenty minutes). It requires timeouts, because clients disappear, and it doesn't scale. Prefer optimistic unless you have a specific reason.

**`PATCH` reduces the problem** by only sending changed fields — A and B touching different fields no longer conflict. It doesn't eliminate it, since both may touch the same field.

**Multi-resource operations and the transaction question.** A single request that must update two services cannot use a database transaction, because there's no shared database. Two-phase commit exists and is almost always the wrong answer at this layer — it holds locks across a network and blocks if the coordinator dies.

The practical alternatives:

- **Saga** — a sequence of local transactions, each with a compensating action. Order placed → payment taken → stock reserved; if stock fails, refund the payment. Compensation is not rollback: the payment *happened* and was then reversed, and both appear in the ledger.
- **Outbox pattern** — write the state change and an outbox row in the *same* local transaction, then publish from the outbox asynchronously. This is the standard solution to "I updated the database but the message never sent" (or vice versa), and it's worth knowing because that failure mode is otherwise unavoidable.
- **Eventual consistency** — accept the window, design the UI for it, and make operations idempotent so replays converge.

**Read-your-own-writes.** A client `POST`s, immediately `GET`s, and reads a stale replica. Options: route reads to the primary for a short window after a write, return the created representation in the `POST` response (so no follow-up `GET` is needed), or pass a version token the read layer waits for. The second is free and solves most cases — return `201` *with a body*, not just a `Location`.

<a id="be-e526"></a>

#### Graceful degradation

The systems that survive are the ones with a defined behavior for "dependency unavailable":

- Serve stale cache with an indication of staleness
- Return partial results with the failed section marked
- Fall back to a default (empty recommendations rather than a 500)
- Queue the write and confirm asynchronously

Decide this per endpoint, deliberately. The default — propagate the error — is sometimes right and often not.

------------------------------------------------------------------------
