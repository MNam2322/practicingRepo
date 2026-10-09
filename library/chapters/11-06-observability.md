---
id: "11-6"
number: "11.6"
title: "Observability"
part: 11
part_title: "Shipping software"
words: 12722
minutes: 55
---

# 11.6 Observability

> **Quick look.** Explaining production behaviour from what the system recorded: logs, metrics and traces.
>
> - Logs: structured events with message templates, levels and correlation ids; keep secrets and personal data out.
> - Metrics: cheap counters, gauges and histograms; keep label cardinality low (no user ids or raw URLs).
> - Use percentiles from histograms, never averages; averaging percentiles across instances is meaningless.
> - Traces follow a request across services as spans joined by the traceparent header; sample at high volume.
> - OpenTelemetry standardizes all three; in .NET: ILogger, ActivitySource and Meter.
> - Measure RED for services, USE for resources, plus business metrics; define SLOs and alert on burn rate.
> - Alert on user-facing symptoms with runbooks; put the trace id in every log line and error response.


### The big picture

On your laptop, when something goes wrong, you attach a debugger, set a breakpoint and look. In production you can't. The failure happened an hour ago, on one of twenty instances, in a request that passed through four services, for one customer out of a million, and it may not happen again. All you have is what the system recorded about itself at the time. Observability is the property of a system that lets you work out what happened, and why, from those records. That includes questions nobody thought to ask when the code was written.

This chapter covers the three kinds of record: logs, metrics and traces, what each is good at, and how they connect through shared ids. It explains how trace context travels between services, and OpenTelemetry, the standard way to produce all three that .NET builds in. It covers what to measure, why percentiles beat averages, and how to alert on what users feel rather than on everything that moves. The example runs two real services with structured logs, distributed traces and a latency histogram, using only Node's built-ins, so you can see the pieces an observability platform assembles for you. The chapter ends Part 11.

### Before you start

**Builds on:** 8.3 A modern .NET backend, 8.5 Validation and errors at the boundary, 10.5 Secrets and cryptography basics, 8.1 What a backend does, 8.7 Background work and queues, 10.1 Thinking like an attacker.

**Ideas explained later:**

- **Capacity planning** (12.4): Deciding how much of each resource a system needs for expected load, with margin.
- **Utilization** (12.4): The share of time a resource is busy.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Performance** (12.4): How quickly a system does its work at a given load.

**Tools and technologies named here:**

**The code uses:** methods and local functions, classes and properties, primary constructors, generic types, lambdas, null operators, tuples, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What are the three main signals of observability, and what is each best at?**

> Logs record individual events with details, for explaining what happened in a specific case. Metrics are numbers aggregated over time, for trends, dashboards and alerts. Traces follow one request across services, for finding where time went and where it failed.

**2. How does a trace follow a request from one service to another?**

> The caller sends the trace context in a header, the W3C `traceparent` header with the trace id and the caller's span id. The receiving service continues the same trace, recording its work as a child span.

**3. Why are percentiles better than averages for latency?**

> A few very slow requests barely move the average but make real users wait. Percentiles such as the 99th show the experience of the slowest requests directly.

**4. Why can't you average the p99 latencies of ten instances to get the service's p99?**

> Percentiles don't combine by averaging. You need the underlying distribution: record latencies in histograms, which can be added together, and compute percentiles from the combined histogram.

**5. What is high cardinality, and why does it matter for metrics?**

> A metric label with many possible values, such as a user id or booking reference. Every combination of labels becomes a separate time series, so such labels multiply storage and cost. Put them in logs and traces instead.

**6. What should an alert be based on?**

> Symptoms users feel, such as error rate or latency beyond an agreed objective, rather than causes such as high CPU. Every alert that wakes someone should need action and say what to do.

**7. What is OpenTelemetry?**

> A vendor-neutral standard and set of libraries for producing logs, metrics and traces, and sending them to any backend. .NET's own diagnostic APIs are its .NET implementation.

### Seeing inside a running system

**Monitoring** watches for known problems: is the service up, is the error rate above 1%, is the disk full. **Observability** goes further. It is being able to answer new questions about the system's behaviour from the data it already emits, without shipping new code to find out. An example is "why are bookings from the mobile app slow for passengers in Singapore since 14:00?" It comes from emitting rich, connected signals, and from tools that let you slice them in any direction.

### Logs

A log is a record of events, one per line, each with a time, a level and a message. **Logging** is producing them, and every backend does it. Logs are best for the details of individual cases: what exactly happened to booking 7f3a9c, and why the payment call failed.

Logs work well only when they are structured. The structured logging from A modern .NET backend (8.3) writes each event as named fields, typically one JSON object per line, as the example's services do. That lets a log platform filter and group by any field, such as `Flight`, `BookingId` or `traceId`, instead of searching free text. A **log level**, such as Debug, Information, Warning or Error, marks each event's importance, so production can keep Information and above while a developer turns on Debug for one component.

Every log line should carry the ids that connect it to everything else: the trace id, and business ids such as the booking reference. That is how a support ticket quoting a trace id, from Validation and errors at the boundary (8.5), leads straight to every line from every service involved.

Logs have costs and rules. Log platforms charge by the volume ingested and stored, so noisy logging becomes expensive fast: log events, not every loop iteration, and sample repetitive ones. Never log secrets, tokens or passwords, from Secrets and cryptography basics (10.5), and keep personal data such as passport numbers out, or masked, because logs are copied widely and kept for months.

### Metrics

A **metric** is a number measured repeatedly over time: requests per second, error count, queue length, response time. Each measurement is aggregated as it happens, so storing a metric costs the same whether it saw ten requests or ten million. That makes metrics the basis for dashboards, trends, capacity planning and alerts.

Metrics come in a few kinds. A **counter** only goes up, such as bookings created or errors seen; graphs show its rate of increase. A **gauge** is a current value that goes up and down, such as queue length, memory in use or open connections. A **histogram** counts measurements into buckets, such as how many requests took under 5 ms, under 10 ms, under 50 ms and so on. From it you can compute percentiles.

Metrics carry labels, also called attributes or dimensions: route, status code, region. Each distinct combination of label values is a separate time series. A label with many possible values, such as a user id or booking reference, has high **cardinality**. It creates millions of series, overwhelming storage and budgets. Keep metric labels to small, fixed sets of values, and put per-request detail in logs and traces.

### Percentiles, not averages

What a backend does (8.1) introduced percentiles and tail latency, and they matter most here. An average blends fast and slow requests into a number that describes nobody's experience. In the example, one run's average response time was 13 ms. But the 99th percentile was 55 ms, and one request took over 400 ms, because a small share of calls to the pricing service were slow. Measure and alert on p50, p95 and p99.

Percentiles have a trap: they can't be combined by averaging. The average of ten instances' p99 values isn't the service's p99. Record latencies in histograms, which can be added together across instances and time windows, and compute percentiles from the combined histogram. Observability platforms do this when latency is recorded as a histogram metric.

### Traces

A **trace** follows one request through the whole system, recording each piece of work along the way as a **trace span**. A span has a name, a start time, a duration, a status and attributes, plus its trace id and the id of its parent span. The spans of one request form a tree: the incoming request at the root, with the database queries, calls to other services and work inside those services as its descendants. Seeing that tree shows at a glance where the time went and which step failed. Following requests across service boundaries like this is **distributed tracing**.

For a trace to cross from one service to the next, each call must carry **trace context**, the trace id and the caller's span id, to the service it calls. The W3C Trace Context standard defines the `traceparent` header for this, with the form `00-<trace id>-<span id>-<flags>`, as the example builds by hand. HTTP client libraries add it automatically when tracing is on, and message brokers carry it in message headers, so traces continue through the queues from Background work and queues (8.7).

Recording every span of every request can be expensive at high volume, so traces are often sampled. Head sampling decides at the start of a request, such as keeping 10% of traces. Tail sampling decides at the end, and can keep every trace that was slow or failed along with a fraction of the rest. Tail sampling needs a collector that sees whole traces before choosing.

### Seeing it for real

This file starts two services: a bookings service and the pricing service it calls, one call in a hundred of which is very slow. It sends 200 requests through them. Each service records spans with W3C Trace Context ids, passes the `traceparent` header along, and writes structured logs with the trace id. At the end it prints the slowest request's trace as a tree, and latency percentiles from all 200 requests. It needs only Node, in a folder whose `package.json` says `"type": "module"`.

```typescript
// observe.ts: run with: node observe.ts   (two services, logs, traces and metrics, using only Node's built-ins)
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { randomBytes } from "node:crypto";

// ---- A tiny tracer: spans with W3C Trace Context ids, collected in memory -------------------------
type Span = { traceId: string; spanId: string; parentId?: string; service: string; name: string; start: number; ms: number; status: "ok" | "error" };
const collected: Span[] = [];                                  // stands in for an OpenTelemetry collector
const hex = (bytes: number) => randomBytes(bytes).toString("hex");

function startSpan(service: string, name: string, parent?: { traceId: string; spanId: string }) {
  const span = { traceId: parent?.traceId ?? hex(16), spanId: hex(8), parentId: parent?.spanId, service, name, start: performance.now() };
  return {
    ...span,
    traceparent: `00-${span.traceId}-${span.spanId}-01`,       // version-traceid-spanid-flags, the W3C header format
    end(status: "ok" | "error" = "ok") { collected.push({ ...span, ms: performance.now() - span.start, status }); },
  };
}
const parseTraceparent = (header?: string) => {
  const m = header?.match(/^00-([0-9a-f]{32})-([0-9a-f]{16})-0[01]$/);
  return m ? { traceId: m[1]!, spanId: m[2]! } : undefined;
};
function log(service: string, level: string, message: string, fields: Record<string, unknown>) {   // structured: one JSON object per line
  if (process.env.SHOW_LOGS) console.log(JSON.stringify({ time: new Date().toISOString(), service, level, message, ...fields }));
}

// ---- Service 2: pricing, occasionally slow --------------------------------------------------------
let call = 0;
const pricing = createServer(async (req: IncomingMessage, res: ServerResponse) => {
  const span = startSpan("pricing", "GET /price", parseTraceparent(req.headers.traceparent as string | undefined));
  const n = ++call;
  const delay = n % 100 === 0 ? 400 : n % 25 === 0 ? 50 : 5;  // 1% very slow, 3% slow, the rest fast
  await new Promise(r => setTimeout(r, delay));
  log("pricing", "info", "Priced {Flight}", { Flight: "BA117", fare: 450, traceId: span.traceId, spanId: span.spanId });
  res.end(JSON.stringify({ fare: 450 }));
  span.end();
});

// ---- Service 1: bookings, which calls pricing --------------------------------------------------------
let pricingUrl = "";
const bookings = createServer(async (req: IncomingMessage, res: ServerResponse) => {
  const span = startSpan("bookings", `GET ${req.url}`, parseTraceparent(req.headers.traceparent as string | undefined));
  const client = startSpan("bookings", "HTTP GET pricing", span);   // a client span around the outgoing call
  const reply = await fetch(pricingUrl, { headers: { traceparent: client.traceparent } });   // the context travels in a header
  client.end(reply.ok ? "ok" : "error");
  log("bookings", "info", "Returned booking {BookingId}", { BookingId: 7, traceId: span.traceId, spanId: span.spanId });
  res.end(await reply.text());
  span.end();
});

const listen = (s: ReturnType<typeof createServer>) => new Promise<string>(r => s.listen(0, "127.0.0.1", () => r(`http://127.0.0.1:${(s.address() as { port: number }).port}`)));
pricingUrl = `${await listen(pricing)}/price`;
const bookingsUrl = await listen(bookings);

// ---- 200 requests, as a user or load test would make them ---------------------------------------------
const durations: number[] = [];
for (let i = 0; i < 200; i++) {
  const t = performance.now();
  await fetch(`${bookingsUrl}/bookings/7`);
  durations.push(performance.now() - t);
}

// ---- Traces: every span of one request, as a tree ---------------------------------------------------
const slowest = collected.filter(s => !s.parentId).sort((a, b) => b.ms - a.ms)[0]!;
const tree = (parentId: string | undefined, depth: number): void => {
  for (const s of collected.filter(x => x.traceId === slowest.traceId && x.parentId === parentId).sort((a, b) => a.start - b.start)) {
    console.log(`${"  ".repeat(depth)}${s.service.padEnd(9)} ${s.name.padEnd(20)} ${s.ms.toFixed(1).padStart(7)} ms`);
    tree(s.spanId, depth + 1);
  }
};
console.log(`slowest trace ${slowest.traceId}:`);
tree(undefined, 1);

// ---- Metrics: the average hides the slow requests; percentiles show them ------------------------------
const sorted = [...durations].sort((a, b) => a - b);
const pct = (p: number) => sorted[Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1)]!;
const mean = durations.reduce((a, b) => a + b, 0) / durations.length;
console.log(`requests: ${durations.length}, mean ${mean.toFixed(1)} ms, p50 ${pct(50).toFixed(1)} ms, p95 ${pct(95).toFixed(1)} ms, p99 ${pct(99).toFixed(1)} ms, max ${sorted.at(-1)!.toFixed(1)} ms`);
console.log(`spans collected: ${collected.length} (3 per request), traces: ${new Set(collected.map(s => s.traceId)).size}`);
bookings.close(); pricing.close();
```

One run's output:

```text
slowest trace 81f08dcaf9ec9ff19285ebea30bf5eba:
  bookings  GET /bookings/7        402.1 ms
    bookings  HTTP GET pricing       401.9 ms
      pricing   GET /price             401.2 ms
requests: 200, mean 13.0 ms, p50 6.9 ms, p95 16.7 ms, p99 55.3 ms, max 402.7 ms
spans collected: 600 (3 per request), traces: 200
```

With `SHOW_LOGS=1`, each service also prints its structured log lines. The two lines from one request share a trace id, which is what lets a log search find every line from every service for that request:

```text
{"time":"2026-09-30T15:13:04.976Z","service":"pricing","level":"info","message":"Priced {Flight}","Flight":"BA117","fare":450,"traceId":"6b143cfcca03ea129ccb96c1606561c5","spanId":"add87d6c0f73782a"}
{"time":"2026-09-30T15:13:04.989Z","service":"bookings","level":"info","message":"Returned booking {BookingId}","BookingId":7,"traceId":"6b143cfcca03ea129ccb96c1606561c5","spanId":"4a3ebd807a921b14"}
```

The trace answers the question the percentiles raise. The slowest request spent almost all of its 402 ms waiting for the pricing service, not in the bookings service's own code. The exact numbers change from run to run; the shape doesn't.

### OpenTelemetry

Every observability vendor once had its own libraries and formats, which tied applications to one vendor. **OpenTelemetry** is the vendor-neutral standard that replaced them. It defines APIs and libraries for producing logs, metrics and traces in many languages, a standard set of names for common attributes, such as `http.route` and `db.system`, and a wire protocol, OTLP, for sending them. The OpenTelemetry Collector receives, processes and forwards the data. Backends include Azure Monitor and Application Insights, Grafana's open-source stack of Prometheus, Loki and Tempo, and commercial services such as Datadog and Honeycomb. Changing backend means changing configuration, not code.

In .NET, the platform's own diagnostic APIs are OpenTelemetry's .NET API. `ILogger` produces logs. `System.Diagnostics.Activity` and `ActivitySource` produce spans. `System.Diagnostics.Metrics.Meter` produces counters, gauges and histograms. ASP.NET Core, `HttpClient` and many libraries already emit standard spans and metrics, such as `http.server.request.duration`. The OpenTelemetry packages collect it all and export it:

```csharp
// Program.cs, with the packages OpenTelemetry.Extensions.Hosting, OpenTelemetry.Instrumentation.AspNetCore,
// OpenTelemetry.Instrumentation.Http and OpenTelemetry.Exporter.OpenTelemetryProtocol
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("bookings-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()             // a span for every incoming request
        .AddHttpClientInstrumentation()             // a child span for every outgoing call, with traceparent added
        .AddSource("Bookings")                      // our own spans, from the ActivitySource below
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()             // request duration histograms, active requests and more
        .AddHttpClientInstrumentation()
        .AddMeter("Bookings")
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());   // logs carry the current trace and span ids
```

```csharp
public sealed class BookingService(ILogger<BookingService> log)
{
    static readonly ActivitySource Source = new("Bookings");
    static readonly Meter Meter = new("Bookings");
    static readonly Counter<long> Created = Meter.CreateCounter<long>("bookings.created", unit: "{booking}");

    public async Task HoldSeatAsync(string flight, string cabin)
    {
        using Activity? activity = Source.StartActivity("Hold seat");        // a child span of the current request
        activity?.SetTag("flight.number", flight);
        await Task.Delay(10);                                                  // stands in for the real work
        Created.Add(1, new KeyValuePair<string, object?>("cabin", cabin));    // a small, fixed set of label values
        log.LogInformation("Held a seat on {Flight} in {Cabin}", flight, cabin);
    }
}
```

Locally, the Aspire dashboard, from A modern .NET backend (8.3), accepts OTLP and shows logs, traces and metrics side by side. The `dotnet-counters`, `dotnet-trace` and `dotnet-dump` tools inspect a running .NET process's metrics, traces and memory when you need to look deeper.

### What to measure

Two short checklists cover most of it. For every service, measure the **RED method**: the rate of requests, the errors among them, and their duration as a histogram. For every resource, such as a database, a queue, a machine or a connection pool, measure the **USE method**: utilization, how busy it is; saturation, how much work is waiting, such as queue length; and errors. Google's site reliability engineers describe four golden signals, latency, traffic, errors and saturation, which cover the same ground.

Add business metrics: bookings per minute, payment success rate, check-ins per flight. They often show a problem first, because a drop in bookings is visible even when every technical metric looks normal.

### Alerts

An **alert** is a notification that something needs human attention. Alerting is where observability most often goes wrong, in two opposite ways: missing real problems, and paging people so often that they stop paying attention, called alert fatigue.

Alert on symptoms that users feel: error rates, latency beyond an agreed target, work piling up in a queue, bookings dropping. Don't alert on causes that may or may not matter, such as CPU at 80%, which is often fine. Every alert that wakes someone must need action now. Anything that can wait belongs in a ticket or a dashboard. Each alert should link to a **runbook**, a short written guide saying what the alert means, how to check it and what to do. Agreeing targets for latency and errors, and alerting when they are at risk, is the subject of service level objectives in Reliability (12.5).

**Synthetic monitoring** runs scripted checks from outside the system on a schedule, such as searching for a flight or loading the booking page every minute from several regions. It catches failures that internal signals miss, such as DNS or certificate problems, and it notices outages even when no real users are active.

### Connecting the signals

The three signals are most useful together, and shared ids join them. A typical investigation starts from an alert on a metric: p99 latency is up. It moves to traces from that time and route, which show where the time goes, such as the pricing call. Then it moves to the logs for those trace ids, which show what the pricing service said. Many platforms link metrics to example traces directly, called exemplars. Security events belong in the same pipeline, as Thinking like an attacker (10.1) required, alongside the audit logs from Authentication and authorization (10.2).

### In short

Observability lets you explain production behaviour from what the system recorded. Logs record structured events with levels and connecting ids; keep secrets and personal data out and control volume. Metrics are cheap aggregated numbers, as counters, gauges and histograms; keep label cardinality low and compute percentiles from histograms, never averages. Traces follow requests across services as trees of spans, joined by W3C Trace Context in the `traceparent` header, with sampling at high volume. OpenTelemetry standardizes all three, and .NET's `ILogger`, `ActivitySource` and `Meter` implement it. Measure RED for services, USE for resources and business metrics. Alert on user-facing symptoms with runbooks, add synthetic checks, and move between signals by shared ids.

### What goes wrong

**Unstructured logs.** Free-text messages can't be filtered or grouped reliably. Log structured fields with message templates.

**Logs without ids.** Lines from different services can't be connected to one request. Include trace ids and business ids in every line.

**Secrets and personal data in logs.** Tokens and passport numbers spread to every log store and backup. Redact at the source.

**High-cardinality metric labels.** A booking id as a label creates millions of series and a huge bill. Keep labels to small, fixed sets.

**Averages on dashboards.** The mean looks healthy while one in a hundred users waits seconds. Show p95 and p99 from histograms.

**Broken trace context.** A queue or a custom HTTP call drops the `traceparent` header, and traces stop at that service. Propagate context through every hop.

**Alert fatigue.** Dozens of noisy, non-actionable alerts train people to ignore all of them. Alert on user-facing symptoms and fix or delete noisy alerts.

**Only internal checks.** Everything looks healthy from inside while users can't resolve the domain. Add synthetic monitoring from outside.

### Cost model

Emitting telemetry costs little per event: recording a span or incrementing a counter takes well under a microsecond, and disabled log levels cost almost nothing. The real costs are downstream. Log platforms charge per gigabyte ingested and stored, often the largest item in an observability bill. Metrics cost per active time series, which is why cardinality matters. Traces cost per span stored, which sampling controls. A service handling a thousand requests a second with three spans each produces over 250 million spans a day, so sampling 10%, plus every error and slow request, keeps both cost and insight.

### In other languages (optional)

**Node.js.** The OpenTelemetry JavaScript SDK instruments HTTP, Express, database drivers and more automatically; pino writes structured JSON logs quickly.

**Python.** OpenTelemetry's Python SDK and auto-instrumentation cover Django, FastAPI, requests and SQLAlchemy; `structlog` produces structured logs.

**Java.** The OpenTelemetry Java agent instruments applications without code changes; Micrometer is the common metrics API in Spring.

**Everywhere.** Because OpenTelemetry is a standard, a trace can start in a TypeScript frontend, pass through a .NET API and a Python service, and still be one trace.

### Key terms

- **Monitoring**: Watching a system for known problems, such as an error rate above a threshold.
- **Observability**: Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Logging**: Recording events as log lines, each with a time, level, message and fields.
- **Log level**: The importance of a log event, such as Debug, Information, Warning or Error.
- **Metric**: A number measured repeatedly over time and aggregated as it is recorded.
- **Counter**: A metric that only increases, such as requests served.
- **Gauge**: A metric holding a current value that rises and falls, such as queue length.
- **Histogram**: A metric that counts measurements into value ranges, from which percentiles can be computed.
- **Cardinality**: The number of distinct values a metric label can take, which multiplies the time series stored.
- **Trace**: The record of one request's path through a system, as a tree of spans.
- **Trace span**: One timed piece of work within a trace, with a name, duration, status, attributes and parent.
- **Distributed tracing**: Following requests across service boundaries by passing trace context between them.
- **Trace context**: The trace id and parent span id passed between services, in the W3C traceparent header.
- **OpenTelemetry**: The vendor-neutral standard and libraries for producing and exporting logs, metrics and traces.
- **RED method**: Measuring rate, errors and duration for every service.
- **USE method**: Measuring utilization, saturation and errors for every resource.
- **Alert**: A notification that something needs human attention.
- **Runbook**: A short guide saying what an alert means and what to do about it.
- **Synthetic monitoring**: Scripted checks run from outside the system on a schedule, such as a test search every minute.

### Where this leads

This ends Part 11. Part 12 turns to larger systems, starting with Monoliths, modules and services (12.1), where tracing across services becomes essential. Setting objectives for latency and errors, and alerting on error budgets, is in Reliability (12.5). Using measurements to find and fix bottlenecks is in Scaling and performance (12.4).

### Check yourself

1. In `observe.ts`, make one pricing call in fifty fail with a 500, record the error status on its spans, and print the error rate next to the percentiles.
2. Add a third service between bookings and pricing, and check that the slowest trace now has five spans in the right tree.
3. Change `pct` to compute percentiles from a histogram with fixed buckets instead of the sorted list, and compare the results.
4. Add the OpenTelemetry configuration above to an ASP.NET Core app, send it to the Aspire dashboard, and find the trace for one request.
5. List the alerts for a system you know. Mark each as symptom or cause, and as needing action now or not, and decide which to change.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Finding one customer's failed booking in 2 TB of logs

A customer quotes an error message, and the team greps text logs across 40 pods for an hour.

*Structured logs with a trace id*

```csharp
logger.LogWarning("Payment declined for booking {BookingId} with code {DeclineCode}", booking.Id, result.Code);
// stored as fields: BookingId=..., DeclineCode=..., TraceId=..., queryable in seconds

// and every error response carries the trace id (ProblemDetails "traceId"), so support can paste it
```

> **Lesson:** Log events with message templates, never interpolated strings, and put the trace id in logs and error responses.

#### Example 2: A metrics bill that tripled

Someone tagged a request-duration metric with `customer_id`. The metrics backend now stores 2 million time series.

*Low-cardinality labels*

```csharp
private static readonly Meter Meter = new("Bookings");
private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("booking.create.duration", unit: "ms");

Duration.Record(elapsedMs,
    new("market", market),            // ~20 values
    new("result", ok ? "ok" : "error"));   // 2 values
// customer ids belong in logs and traces, never in metric labels
```

> **Lesson:** Every distinct label combination is a separate time series. Keep labels to small, fixed sets.

### Logging & Observability

<p class="src">From <em>.NET Backend Deep Dive</em> · Logging & Observability</p>

<a id="dn-1"></a>

#### Structured Logging

##### What / Why
A traditional log line is **just text**:

```
[2026-08-23 10:00:01] Order 12345 failed for user 987 after 350ms
```

Machines can't read this. To find "all failed orders for user 987" you need regex — and that regex breaks the moment someone rewords the message.

A structured log is **an event with a schema**: a message template plus named, typed fields.

```json
{
  "timestamp": "2026-08-23T10:00:01Z",
  "level": "Error",
  "messageTemplate": "Order {OrderId} failed for user {UserId} after {ElapsedMs}ms",
  "OrderId": 12345,
  "UserId": 987,
  "ElapsedMs": 350,
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "Environment": "prod",
  "Service": "order-api"
}
```

Now you can query: `OrderId = 12345`, `ElapsedMs > 1000`, `level = Error AND Service = "order-api"`.

##### Practical

**Wrong — string interpolation destroys the structure:**

```csharp
_logger.LogError($"Order {orderId} failed for user {userId}");
// The template is burned into a single string.
// Every order produces a different template -> can't group, can't query.
```

**Right — message template:**

```csharp
_logger.LogError(ex,
    "Order {OrderId} failed for user {UserId} after {ElapsedMs}ms",
    orderId, userId, sw.ElapsedMilliseconds);
```

**Scopes — attach context to every log inside a block:**

```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["OrderId"] = orderId,
    ["TenantId"] = tenantId,
    ["CorrelationId"] = correlationId
}))
{
    _logger.LogInformation("Validating order");   // automatically carries OrderId, TenantId...
    await _payment.ChargeAsync(order);
    _logger.LogInformation("Order completed");
}
```

**Realistic Serilog setup:**

```csharp
builder.Host.UseSerilog((ctx, sp, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithProperty("Service", "order-api")
    .Enrich.With<TraceIdEnricher>()          // custom: attach the OTel TraceId
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.OpenTelemetry(o => o.Endpoint = otlpEndpoint));
```

##### Field rules

| Do | Don't |
|---|---|
| PascalCase property names (`OrderId`) | Log PII: passwords, national IDs, card numbers, tokens |
| Same field name for the same concept across all services | Log inside hot loops |
| Log at boundaries (request in/out, DB failure, external API) | `LogInformation` on every line of code |
| Correct levels: Warning = abnormal but handled; Error = request failed | Swallow an exception and log only the message (loses the stack trace) |

##### Interview answer
> "Structured logging emits events as key–value pairs instead of flat text. Use `ILogger` message templates, never string interpolation, so the sink preserves fields and you can query and aggregate them in Seq/Elastic/Loki. Add scopes to propagate correlation IDs."

---

<a id="dn-4"></a>

#### OpenTelemetry (OTel)

##### What / Why
OTel is the **vendor-neutral standard** for three signals: **traces, metrics, logs**. Before OTel, moving from App Insights to Datadog meant rewriting all instrumentation. With OTel the code stays the same and you only swap the **exporter**.

Core concepts:

- **Trace**: the full journey of one request across N services, identified by a `TraceId`.
- **Span**: one unit of work in that trace (one HTTP call, one SQL query). Has `SpanId`, `ParentSpanId`, start/end, **attributes**, events, status.
- **Context propagation**: passing the `traceparent` header between services (W3C Trace Context).
- **Resource**: process-level attributes (service.name, service.version, deployment.environment).
- **Collector**: a sidecar/gateway that receives OTLP, processes it (filter, sample, redact), and fans out to multiple backends.

In .NET, an OTel trace is a `System.Diagnostics.Activity` — it's in the BCL, not an exotic package.

##### Practical — setup

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("order-api", serviceVersion: "1.4.2")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName
        }))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o =>
        {
            o.RecordException = true;
            // Drop health checks to reduce noise
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
        .AddSource("OrderService")                      // your own spans
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://otel-collector:4317")))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()                    // GC, thread pool, exceptions
        .AddMeter("OrderService")
        .AddOtlpExporter());
```

##### Practical — custom spans

```csharp
public class OrderService
{
    private static readonly ActivitySource Source = new("OrderService");
    private static readonly Meter Meter = new("OrderService");
    private static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("orders.created");

    public async Task<Order> CreateAsync(CreateOrderCommand cmd)
    {
        using var activity = Source.StartActivity("CreateOrder", ActivityKind.Internal);
        activity?.SetTag("order.customer_id", cmd.CustomerId);
        activity?.SetTag("order.item_count", cmd.Items.Count);
        activity?.SetTag("order.total", cmd.Total);

        try
        {
            var order = await _repo.AddAsync(cmd.ToEntity());
            activity?.SetTag("order.id", order.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            OrdersCreated.Add(1, new KeyValuePair<string, object?>("channel", cmd.Channel));
            return order;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }
}
```

##### Linking logs to traces (the high-value trick)
When a log carries a `TraceId`, you can jump from a single error line straight to the waterfall for that request.

```csharp
public class TraceIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent e, ILogEventPropertyFactory f)
    {
        var a = Activity.Current;
        if (a is null) return;
        e.AddPropertyIfAbsent(f.CreateProperty("TraceId", a.TraceId.ToString()));
        e.AddPropertyIfAbsent(f.CreateProperty("SpanId",  a.SpanId.ToString()));
    }
}
```

##### Sampling (a cost decision)
100% tracing in production is an enormous bill.

```csharp
.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))  // 10%
```

Better: **tail sampling at the Collector** — keep 100% of traces that errored or were slow, and 1–5% of the rest.

##### Interview answer
> "OTel standardizes traces, metrics and logs; in .NET traces map onto `ActivitySource`/`Activity`. I enable auto-instrumentation for ASP.NET Core, HttpClient and EF Core, add custom spans for business operations, and export OTLP to a Collector that fans out to the backends. I enrich logs with TraceId to correlate them. In production I use parent-based plus tail sampling to control cost."

---

<a id="dn-5"></a>

#### Log filtering vs attribute redaction

Two different needs that get conflated:

##### (a) Filtering **log volume** by attribute (reduce noise and cost)

**Serilog — filter on properties:**

```csharp
.Filter.ByExcluding(e =>
    e.Properties.TryGetValue("RequestPath", out var p) &&
    p.ToString().Contains("/health"))
.Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Warning
    || e.Properties.ContainsKey("Audit"))
```

**Microsoft.Extensions.Logging — filter by category:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```

**OTel Collector — central filtering without redeploying the app:**

```yaml
processors:
  filter/health:
    traces:
      span:
        - 'attributes["http.route"] == "/health"'
  tail_sampling:
    policies:
      - name: errors, type: status_code, status_code: {status_codes: [ERROR]}
      - name: slow,   type: latency,     latency: {threshold_ms: 1000}
      - name: rest,   type: probabilistic, probabilistic: {sampling_percentage: 5}
```

##### (b) Stripping **sensitive values out of attributes** (redaction / masking)

This is the compliance-relevant one (PCI, GDPR).

**Serilog destructuring policy — mask by field name:**

```csharp
public class MaskingPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
        { "password", "token", "authorization", "cardNumber", "cvv", "ssn", "otp" };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory f,
        out LogEventPropertyValue result)
    {
        var props = value.GetType().GetProperties()
            .Select(p => new LogEventProperty(
                p.Name,
                Sensitive.Contains(p.Name)
                    ? new ScalarValue("***REDACTED***")
                    : f.CreatePropertyValue(p.GetValue(value), true)));

        result = new StructureValue(props);
        return true;
    }
}
// register: .Destructure.With<MaskingPolicy>()
```

**Partial masking, so the value is still useful for debugging:**

```csharp
static string MaskCard(string pan) =>
    pan.Length < 8 ? "****" : $"{pan[..6]}******{pan[^4..]}";   // 411111******1111

static string MaskEmail(string email)
{
    var at = email.IndexOf('@');
    return at <= 1 ? "***" : $"{email[0]}***{email[at..]}";      // a***@gmail.com
}
```

**OTel Collector — delete or hash attributes at the infrastructure layer:**

```yaml
processors:
  attributes/redact:
    actions:
      - key: http.request.header.authorization
        action: delete
      - key: db.statement
        action: delete          # avoid logging queries containing sensitive parameters
      - key: enduser.id
        action: hash
```

**The safest design is an allowlist, not a blocklist.** Log only fields you've approved; drop everything else by default. Blocklists are always incomplete — the day a developer adds a `pin` field, you leak it.

##### Interview answer
> "There are two layers: filtering to reduce log volume (levels, categories, Serilog filters, tail sampling at the Collector) and redaction to strip sensitive values from attributes (destructuring policies in the app, attribute processors in the Collector). The rule is to allowlist the fields you're permitted to log, and redact in both places for defense in depth."

---

### Metrics, SLOs & Cardinality

<p class="src">From <em>.NET Backend Deep Dive</em> · Metrics, SLOs & Cardinality</p>

<a id="dn-127"></a>

#### What to measure

**RED** for request-driven services: **R**ate, **E**rrors, **D**uration.
**USE** for resources: **U**tilization, **S**aturation, **E**rrors.

```csharp
private static readonly Meter Meter = new("OrderService", "1.0");

private static readonly Counter<long> Placed =
    Meter.CreateCounter<long>("orders.placed", unit: "{order}");

private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("orders.duration", unit: "ms");

// Observable gauge - polled, good for queue depth or pool usage
private static readonly ObservableGauge<int> QueueDepth =
    Meter.CreateObservableGauge("orders.queue.depth", () => _queue.Count);
```

<a id="dn-128"></a>

#### Cardinality — the mistake that costs real money

Every distinct combination of label values creates a separate time series.

```csharp
// CATASTROPHIC: one series per order, forever
Duration.Record(ms, new("order_id", orderId));

// Fine: bounded set of values
Duration.Record(ms, new("channel", channel), new("status", status));
```

User IDs, order IDs, raw URLs with path parameters, email addresses, and full error messages as labels are all cardinality bombs. They inflate your metrics bill, slow queries, and can take down the metrics backend itself.

**The rule: metrics answer "how many and how fast," traces answer "which one."** If you need per-order detail, that's a span attribute, not a metric label.

<a id="dn-129"></a>

#### Percentiles, not averages

An average latency of 200ms is compatible with 90% of requests at 50ms and 10% at 1.5 seconds. Users experience the tail. Track p50/p95/p99, and remember that **averaging percentiles across instances is mathematically meaningless** — aggregate the histogram buckets instead, which is why histogram metrics beat pre-computed summaries.

<a id="dn-130"></a>

#### SLIs and SLOs

```
SLI: proportion of requests served in under 300ms with a non-5xx status
SLO: 99.5% over a rolling 30 days
Error budget: 0.5% ≈ 3.6 hours of failure per month
```

The point of an error budget is that it converts reliability from an argument into arithmetic: budget remaining means you can ship risky changes; budget exhausted means you stop feature work and fix reliability. It also stops the pursuit of 100%, which is infinitely expensive and not what users need.

**Alert on symptoms, not causes.** Page on "error rate above SLO" or "burn rate is 14× budget," not on "CPU above 80%" — high CPU with healthy latency needs no human at 3am.

---

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a60"></a>

#### Logging that is actually useful

Log *structured events*, not formatted strings. A message template keeps the placeholders as queryable fields, so you can filter on `OrderId = 4711` in Seq, Kibana or Application Insights instead of grepping text.

``` csharp
// good: template + named properties
_log.LogInformation("Order {OrderId} shipped to {Country} in {Elapsed}ms",
                    order.Id, order.Country, sw.ElapsedMilliseconds);

// bad: interpolated, everything becomes one opaque string
_log.LogInformation($"Order {order.Id} shipped");

// zero-allocation, compile-time checked
[LoggerMessage(Level = LogLevel.Warning, Message = "Retry {Attempt} for {Url}")]
static partial void LogRetry(ILogger logger, int attempt, string url);

// correlate everything in a unit of work
using (_log.BeginScope(new Dictionary<string, object> { ["OrderId"] = id }))
{ ... }
```

| Level         | Means                                             | Alert on it?         |
|---------------|---------------------------------------------------|----------------------|
| Trace / Debug | Development detail                                | Off in production    |
| Information   | Business milestones: order placed, job finished   | No                   |
| Warning       | Recovered problem: retry, fallback, degraded mode | Trend, not per-event |
| Error         | This operation failed for a user                  | Yes                  |
| Critical      | The process or a dependency is down               | Page someone         |

> **\[Trap\]**
>
> **Common trap.** Never log passwords, tokens, full card numbers, or entire request bodies. Add a redaction step and assume logs will be readable by more people than you expect.

###### Structured logging, and the cost of getting it wrong

``` csharp
// interpolation destroys the structure. The message becomes an opaque string.
logger.LogInformation($"Order {orderId} failed for tenant {tenantId}");
// -> "Order 4471 failed for tenant acme"
//    unqueryable. To find every failure for acme you need a regex.
//    AND it formats the string even when Information is disabled.

// message TEMPLATE: the parameters survive as fields
logger.LogInformation("Order {OrderId} failed for tenant {TenantId}", orderId, tenantId);
// -> { "message": "Order 4471 failed...", "OrderId": 4471, "TenantId": "acme" }
//    now: TenantId = "acme" AND Level = Error  -> a query, not a grep

// arguments bind by POSITION, not by name. {TenantId} first and orderId first
// silently produces wrong fields with a correct-looking message.

// zero-allocation logging for hot paths - source generated, no boxing
[LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
    Message = "Order {OrderId} failed: {Reason}")]
static partial void OrderFailed(ILogger logger, long orderId, string reason);
// the generated code checks IsEnabled FIRST and skips everything if disabled.
```

``` csharp
// scopes: attach context to every line inside a block, automatically
using (logger.BeginScope(new Dictionary<string, object>
{
    ["CorrelationId"] = correlationId,
    ["TenantId"] = tenantId,
}))
{
    logger.LogInformation("Starting import");   // carries both
    await ProcessAsync();                       // so does everything inside
}
// this is what makes "every log line for this user's request" a query
// rather than an archaeology exercise.
```

| Level         | Means                      | Test                                                      |
|---------------|----------------------------|-----------------------------------------------------------|
| `Trace`       | Diagnostic detail          | Never on in production                                    |
| `Debug`       | Developer interest         | Off by default; on temporarily                            |
| `Information` | Notable business events    | Would you want this for every request? If yes, reconsider |
| `Warning`     | Unexpected, but handled    | Something recovered and someone should know               |
| `Error`       | **A human must look**      | If errors are routine noise, nobody reads any of them     |
| `Critical`    | The system cannot function | Rare. Page-worthy                                         |

> **\[Trap\]**
>
> **Common trap.** Never log request bodies, tokens, connection strings, or anything typed into a password field. Logs are widely readable, retained for months, shipped to third-party vendors, and are frequently the least access-controlled store you own. One `LogDebug("payload: {Body}", body)` left enabled turns your logging platform into an unencrypted copy of customer data — found by an auditor, not by you.

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a194"></a>

#### Observability: logs, metrics and traces

Monitoring tells you a known thing is broken. Observability lets you ask a question you did not anticipate. You need all three signals, correlated by a trace ID that flows through every hop.

| Signal  | Answers                                  | Cardinality                             |
|---------|------------------------------------------|-----------------------------------------|
| Metrics | Is something wrong, and how much?        | Low — aggregate counters and histograms |
| Traces  | Where in the call graph did the time go? | High — one span tree per request        |
| Logs    | What exactly happened in this case?      | Highest — sample or you go bankrupt     |

``` csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-api", serviceVersion: version))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Orders")
        .AddOtlpExporter());

// your own domain metrics
private static readonly Counter<long> Placed =
    new Meter("Orders").CreateCounter<long>("orders.placed");
Placed.Add(1, new KeyValuePair<string, object?>("channel", channel));
```

OpenTelemetry is vendor-neutral: instrument once, export to Jaeger, Prometheus, Grafana, Application Insights, Datadog or Honeycomb by changing configuration. Context propagation via the `traceparent` header is automatic for HttpClient and ASP.NET Core, and most message libraries propagate it too.

###### Worked example

Correlation in practice. A user reports ‘my export failed at about 2pm’, and you have four services, a queue and a database. Here is what makes that a two-minute investigation instead of an afternoon.

``` text
  the chain that must be unbroken

  browser sends       traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
                                      ^^ trace id (same for the whole journey)
    gateway           span: GET /exports          parent: none
      orders-api      span: POST /exports         parent: gateway span
        db            span: INSERT exports        parent: orders-api span
        outbox        the trace id is stored ON THE MESSAGE
          worker      span: consume ExportRequested   parent: the producing span
            blob      span: PUT export.csv
            email     span: POST /send            <- failed here, 502

  one search on the trace id shows all of it, in order, with durations.
```

``` csharp
// the two things that break the chain, and how to fix them

// 1. a message crossing a broker loses context unless it is propagated
public async Task PublishAsync<T>(T message, CancellationToken ct)
{
    var props = new Dictionary<string, object?>();
    Propagators.DefaultTextMapPropagator.Inject(
        new PropagationContext(Activity.Current!.Context, Baggage.Current),
        props, (carrier, k, v) => carrier[k] = v);          // writes traceparent into headers

    await _bus.SendAsync(message, props, ct);
}

// consumer side
var parent = Propagators.DefaultTextMapPropagator.Extract(
    default, headers, (carrier, k) => carrier.TryGetValue(k, out var v) ? [v] : []);

using var activity = ActivitySource.StartActivity(
    "consume " + typeof(T).Name, ActivityKind.Consumer, parent.ActivityContext);

// 2. background work started with Task.Run loses Activity.Current
var captured = Activity.Current;                            // capture it
_ = Task.Run(async () =>
{
    using var a = _source.StartActivity("background", ActivityKind.Internal, captured?.Context ?? default);
    ...
});
```

Add the trace id to your error responses and your log lines, and the support workflow becomes: user pastes the id from the error page, you paste it into the trace search, you see the failing span with its exception. That single change removes more investigation time than any amount of additional logging.

``` csharp
// put it in every ProblemDetails response
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString());
```

###### Sampling, cardinality and cost control

Full-fidelity tracing at scale is unaffordable, and full-fidelity metrics with unbounded labels will take down your metrics backend before it takes your budget.

| Control            | Approach                                                              | Note                                                        |
|--------------------|-----------------------------------------------------------------------|-------------------------------------------------------------|
| Trace sampling     | Head-based: sample 1–10% of traces at the root                        | Cheap, simple, but you sample away the rare failures        |
|                    | Tail-based: buffer the whole trace, keep it if it errored or was slow | Keeps what matters; needs a collector that can buffer       |
|                    | Always sample errors, sample successes at 1%                          | The pragmatic default                                       |
| Metric cardinality | Never put user id, order id, or a raw URL path in a label             | Each unique combination is a separate time series           |
|                    | Use the route template (`/orders/{id}`), not the path                 | ASP.NET Core instrumentation does this correctly by default |
| Log volume         | Sample debug logs; drop health-check and static-file requests         | Health checks alone are often 40% of log lines              |

``` csharp
// drop the noise before it is exported
.WithTracing(t => t.AddAspNetCoreInstrumentation(o =>
{
    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")
                   && !ctx.Request.Path.StartsWithSegments("/metrics");
}))

// keep errors, sample the rest
.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.05)))
```

A cardinality mistake is worth understanding concretely: a counter labelled with `customer_id` across 50,000 customers and 5 status values creates 250,000 time series from one metric. Prometheus will happily accept them and then run out of memory. The rule is that label values must come from a small, bounded set that you can enumerate.

<a id="be-a195"></a>

#### What to measure and when to wake someone up

Alert on symptoms users feel, not on causes. High CPU is not an incident; a 5% error rate is.

| Framework           | Signals                                     |
|---------------------|---------------------------------------------|
| RED (services)      | **R**ate, **E**rrors, **D**uration          |
| USE (resources)     | **U**tilisation, **S**aturation, **E**rrors |
| Four golden signals | Latency, traffic, errors, saturation        |

##### .NET-specific counters worth a dashboard

- Thread-pool queue length — sustained growth means blocking on async code somewhere.
- Gen 2 collection rate and pause time — the usual cause of periodic latency spikes.
- `Exception` throw rate — a rising count of caught-and-swallowed exceptions is a hidden failure.
- HTTP connection pool: queued requests and connections established per second.
- Database connection pool wait time — the first thing to saturate under real load.

##### Define an SLO, then alert on burn rate

“99.9% of requests succeed in under 300ms over 30 days” gives you an error budget of about 43 minutes. Alert when you are burning that budget fast enough to exhaust it — that filters out the noise of individual blips while still catching a real degradation within minutes.

``` text
dotnet-counters monitor -n MyApi
dotnet-dump collect -p 1234        # then analyse with dotnet-dump analyze
dotnet-trace collect -p 1234 --profile cpu-sampling
dotnet-gcdump collect -p 1234      # heap contents without a full dump
```

###### SLO burn rate, and alerts people trust

``` text
  THRESHOLD ALERTING vs BURN-RATE ALERTING

  threshold   "alert if error rate > 1%"
              fires at 3am for a 90-second blip that consumed 0.01% of
              the monthly budget. Nobody needed to wake up.
              -> people start ignoring it. Then they ignore the real one.

  burn rate   "alert if we are consuming the error budget fast enough
               to exhaust it in under N hours"

    SLO 99.9% over 30 days  ->  error budget = 43 minutes

    burn rate 14.4x  -> budget gone in ~2 days   -> PAGE
    burn rate 6x     -> budget gone in ~5 days   -> PAGE (slower window)
    burn rate 1x     -> exactly on budget        -> ticket
    a 90-second blip -> negligible burn          -> nothing

  this ties alert URGENCY to actual USER IMPACT, which is the only thing
  that justifies waking someone.
```

``` csharp
// the four signals worth instrumenting first, in .NET
var meter = new Meter("Acme.Orders");

// RATE, ERRORS, DURATION come free from ASP.NET Core instrumentation:
//   http.server.request.duration  (a histogram, with status code as a tag)

// SATURATION is the one you must add - and it is the leading indicator
meter.CreateObservableGauge("db.pool.in_use", () => pool.InUse);
meter.CreateObservableGauge("queue.depth",    () => queue.Depth);
meter.CreateObservableGauge("queue.oldest_message_age_seconds",
                            () => queue.OldestAge.TotalSeconds);

// business metrics matter more than technical ones for detecting outages:
var placed = meter.CreateCounter<long>("orders.placed");
// "orders placed per minute dropped to zero" detects far more real
// incidents than any CPU alert, and it has no false positives.
```

| Alert on                                                | Not on                             |
|---------------------------------------------------------|------------------------------------|
| Symptoms users feel — error rate, latency, availability | Causes — CPU high, memory high     |
| SLO burn rate                                           | Every threshold crossing           |
| Sustained conditions                                    | Momentary spikes                   |
| Absence of expected work — a job that did not run       | Every completed job                |
| Saturation approaching a hard limit                     | Every capacity metric              |
| Things a human must act on **now**                      | Things that can wait until morning |

| Every alert needs                       | Because                                              |
|-----------------------------------------|------------------------------------------------------|
| A title stating **user impact**         | “Checkout failing for EU users”, not “HighErrorRate” |
| A link to the relevant dashboard        | Saves minutes at the worst possible time             |
| A runbook link                          | The responder may not be whoever built it            |
| A severity that determines the response | Page, ticket, or dashboard-only                      |
| An owner                                | An alert nobody owns is ignored, then deleted        |

> **\[Note\]**
>
> **The monthly test for an alerting system.** For every page in the last month, ask: did a human need to do something immediately? If the honest answer is no more than about half the time, your alerting is training people to ignore it. Delete or downgrade aggressively — a small set of trusted alerts detects more real incidents than a large set of ignored ones.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a211"></a>

#### The observability platform itself

The application side of observability — OpenTelemetry, structured logs, health checks — is only half the system. The other half is where that data goes, how much it costs, and whether it is still there when you need it.

``` text
  application (OTLP)  ->  OpenTelemetry Collector  ->  backends
                             |
                             |  this is the control point. Put one in.
                             |
                             +- processors:  batch, memory_limiter, tail sampling,
                             |               attribute redaction, cardinality limits
                             +- exporters:   traces  -> Tempo / Jaeger / vendor
                                             metrics -> Prometheus / Mimir
                                             logs    -> Loki / Elasticsearch

  without a collector, every service ships directly to every backend:
    changing vendor means redeploying every service
    sampling and redaction policy lives in application code
    a backend outage backs up into your application's memory
```

``` text
# collector config: the policies that keep cost and cardinality under control
processors:
  memory_limiter:
    limit_percentage: 75
    spike_limit_percentage: 20

  tail_sampling:                      # decide AFTER seeing the whole trace
    decision_wait: 10s
    policies:
      - name: keep-all-errors
        type: status_code
        status_code: { status_codes: [ERROR] }
      - name: keep-slow
        type: latency
        latency: { threshold_ms: 1000 }
      - name: sample-the-rest
        type: probabilistic
        probabilistic: { sampling_percentage: 2 }

  attributes/redact:                  # PII must not reach the backend
    actions:
      - { key: http.request.header.authorization, action: delete }
      - { key: user.email, action: delete }
      - { key: db.statement, action: hash }

  filter/drop-noise:
    traces:
      span:
        - 'attributes["http.route"] == "/health/live"'
```

| Signal   | Typical retention           | Cost driver                      | Control                                                    |
|----------|-----------------------------|----------------------------------|------------------------------------------------------------|
| Metrics  | 13 months                   | Active time series (cardinality) | Label discipline; recording rules for long-term aggregates |
| Traces   | 7–30 days                   | Spans ingested                   | Tail sampling; drop health checks and static assets        |
| Logs     | 7–30 days hot, then archive | Bytes ingested                   | Level discipline; sampling; structured fields not prose    |
| Profiles | 7 days                      | Continuous profiling overhead    | Sample at 1–5%; enable per service                         |

###### What to build before the first incident

- **One dashboard per service**, identical in layout: RED metrics, dependency latency, saturation, error log rate, and the current version. Identical layout matters more than perfect metrics, because it makes services comparable under pressure.
- **Trace-to-log correlation.** Clicking a span must reach the log lines for that trace. Without it you have two disconnected tools and you will use neither well.
- **Exemplars** linking a metric spike to a specific trace. This is the shortest path from ‘p99 is bad’ to ‘here is a bad request’.
- **SLOs with burn-rate alerts**, not threshold alerts. Alert when the error budget is being consumed fast enough to matter, so blips stay quiet and real degradation pages immediately.
- **A single runbook link on every alert.** An alert without a runbook is a notification, not an alert.
- **Continuous profiling** in production (Pyroscope, Parca, or a vendor). Finding a CPU regression from a profile you already have beats reproducing it locally.

A useful test of whether the platform works: pick a request from a week ago, at random, and try to reconstruct exactly what it did — which services, which queries, how long each took, and what it logged. If you cannot, the gap you hit is the next thing to fix, and it is better to find it on a Tuesday than during an outage.

### Observability

<p class="src">From <em>Backend Engineering</em> · APIs · Observability</p>

<p class="pd">You cannot operate what you cannot see, and &quot;add more logging&quot; is not a strategy.</p>

<a id="be-e540"></a>

#### What to measure

``` csharp
private static readonly Meter Meter = new("MyApp.Orders", "1.0");

private static readonly Counter<long> Placed =
    Meter.CreateCounter<long>("orders.placed", "orders");
private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("orders.place.duration", "ms");
private static readonly UpDownCounter<long> Queued =
    Meter.CreateUpDownCounter<long>("outbox.pending", "messages");

Placed.Add(1, new KeyValuePair<string, object?>("result", "ok"));
Duration.Record(sw.Elapsed.TotalMilliseconds);
```

**The four signals worth having on every service**, in this order:

1.  **Rate** — requests per second, by endpoint. Tells you whether a change in anything else is caused by load.
2.  **Errors** — failures per second, and the ratio. The ratio is what you alert on, because rate alone moves with traffic.
3.  **Duration** — as a histogram, so percentiles are computable and mergeable ([Quantiles and histograms](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m130)). Never as an average.
4.  **Saturation** — how full the constrained resource is: thread pool queue, connection pool, outbox depth, CPU.

``` csharp
// Saturation metrics are the ones that predict rather than report. Queue depth
// rising is a leading indicator; latency rising is a lagging one. Instrument:
//   thread pool queue length      (§136)
//   connection pool in-use count  (§262)
//   channel/outbox backlog        (§135)
//   GC % time and gen 2 count     (§34)
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddMeter("MyApp.Orders")
    .AddAspNetCoreInstrumentation()
    .AddRuntimeInstrumentation()
    .AddOtlpExporter());
```

> **\[Trap\]**
>
> **Tag cardinality is the operational cost of metrics.** Every distinct combination of tag values is a separate time series. Tagging by endpoint (dozens) is fine; by tenant (thousands) is expensive; by user id, order id, or SQL text is how you take down a metrics backend or receive a memorable invoice ([Observing the data layer](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m265)).

<a id="be-e541"></a>

#### Structured logging

``` csharp
_logger.LogInformation("Order created {OrderId} for {CustomerId} in {ElapsedMs}ms",
    order.Id, customerId, sw.ElapsedMilliseconds);
```

Not string interpolation — named properties, so they're queryable. `orderId:42` finds it; grepping a formatted sentence does not.

Log at boundaries: request in, request out, external call, error. Not every line of business logic.

**One trace ID through everything.** `Activity`/OpenTelemetry propagates `traceparent` across services and flows across awaits automatically via `AsyncLocal`. Include it in error responses so a user can hand it to support and you can find the exact request.

<a id="be-e542"></a>

#### Distributed tracing

Logs tell you what happened in one place; traces tell you where the time went across all of them. A **trace** is a tree of **spans**, each with a duration, linked by a trace id that propagates across process boundaries.

``` text
trace 4bf92f3577b34da6
├── POST /orders                                    142 ms
│   ├── auth: validate token                          2 ms
│   ├── SELECT Customers WHERE Id = @p                8 ms
│   ├── HTTP POST stock-service/reserve              95 ms   ← there it is
│   │   └── SELECT Inventory ...                     71 ms
│   ├── INSERT Orders; INSERT OrderLines             11 ms
│   └── INSERT Outbox                                 3 ms
```

``` csharp
builder.Services.AddOpenTelemetry().WithTracing(t => t
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation()          // propagates the trace id automatically
    .AddEntityFrameworkCoreInstrumentation()
    .AddSource("MyApp.Orders")
    .SetSampler(new TraceIdRatioBasedSampler(0.1))   // 10% - full tracing is expensive
    .AddOtlpExporter());

// Your own spans, for work the instrumentation cannot see:
private static readonly ActivitySource Source = new("MyApp.Orders");
using var activity = Source.StartActivity("PriceOrder");
activity?.SetTag("order.line_count", lines.Count);
```

**Propagation is what makes it distributed.** The W3C `traceparent` header carries the trace and span id across HTTP calls; `HttpClient` instrumentation adds it automatically. For message-based hops you must copy it into the message headers yourself, or the trace ends at the broker ([Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249)).

> **\[Tip\]**
>
> **Put the trace id in every log line and every error response.** That is the join key between the three signals: a user quotes the id from an error page, you find the trace, the trace names the slow span, and the logs for that span tell you why ([Errors: ProblemDetails and failure contracts](08-02-api-design.md#be-m242)). Without it, correlating three telemetry systems is manual archaeology.

> **\[Trap\]**
>
> **Sample, but sample consistently.** If each service decides independently, you get fragments of traces — the most useless possible output. Sampling decisions must be made once, at the entry point, and propagated. And always sample errors and slow requests at 100% regardless of the base rate; those are the ones you will want.

<a id="be-e543"></a>

#### Health checks

``` csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDb>("database", tags: ["ready"])
    .AddCheck<OutboxBacklogCheck>("outbox", tags: ["ready"])
    .AddUrlGroup(new Uri(cfg["Stock:BaseUrl"] + "/health/live"), "stock", tags: ["deps"]);

app.MapHealthChecks("/health/live",  new() { Predicate = _ => false });          // liveness
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

| Probe         | Asks                          | Failure means              | Must not check        |
|---------------|-------------------------------|----------------------------|-----------------------|
| **Liveness**  | is the process wedged?        | restart me                 | dependencies — ever   |
| **Readiness** | can I serve traffic?          | take me out of rotation    | optional dependencies |
| **Startup**   | have I finished initialising? | wait longer before probing | —                     |

> **\[Trap\]**
>
> **A liveness check that touches the database is a cascading-failure generator.** The database blips, every instance reports unhealthy, the orchestrator restarts all of them simultaneously, and they all reconnect at once — turning a five-second blip into an outage. Liveness answers "is this process alive?" and nothing more. Dependency health belongs in readiness, and even there, only for dependencies without which you genuinely cannot serve anything ([Resilience: retries, breakers, and timeouts](12-05-reliability.md#be-m263)).

> **\[Tip\]**
>
> **Readiness should fail on shutdown, before the server stops accepting.** Registering a callback on `ApplicationStopping` that flips readiness to unhealthy gives the load balancer time to notice before connections start being refused — the difference between a silent deploy and a burst of 502s ([Shutdown and graceful drain](08-01-what-a-backend-does.md#be-m235)).

### Reliability & Operations

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Reliability & Operations</p>

<p class="pd">Knowing the system is healthy, finding out quickly when it is not, and having practised what you will do.</p>

<a id="be-g708"></a>

#### Observability: logs, metrics and traces

| Signal       | Answers                                  | Cost                    | Cardinality               |
|--------------|------------------------------------------|-------------------------|---------------------------|
| **Metrics**  | Is something wrong? How much, over time? | Very cheap              | Low — keep labels bounded |
| **Logs**     | What exactly happened in this case?      | Expensive at volume     | Unlimited                 |
| **Traces**   | Where did the time go, across services?  | Moderate, with sampling | High                      |
| **Profiles** | Which code consumed the CPU or memory?   | Moderate                | —                         |

``` text
  how they are used together, in an incident

  1  an ALERT fires on a METRIC          "p99 latency above SLO"
  2  DASHBOARDS narrow it                "only the checkout endpoint, only EU"
  3  a TRACE shows where the time went   "94% in one database call"
  4  LOGS give the detail                "lock wait on orders, tenant 4471"
  5  a PROFILE if it is CPU-bound        "serialisation, in this method"

  metrics tell you THAT. traces tell you WHERE. logs tell you WHY.
  a system with only logs makes step 1 impossible, and step 2 slow.
```

``` text
# structured logs, always. A formatted sentence cannot be queried.
{"ts":"2026-08-07T10:22:31Z","level":"error","msg":"order failed",
 "trace_id":"4bf92f...","order_id":"4471","tenant":"acme","duration_ms":8241}

# NOT: log.Error($"Order {id} failed after {ms}ms")
# -> unqueryable, unaggregatable, and it will be parsed with a regex by
#    someone at 3am who is not having a good time.

# and the field that ties everything together:
#   trace_id, propagated through EVERY service and EVERY log line.
#   without it, correlating a user's request across five services is manual.
```

| Practice                       | Detail                                                                                                              |
|--------------------------------|---------------------------------------------------------------------------------------------------------------------|
| OpenTelemetry                  | Vendor-neutral. Instrument once; auto-instrumentation covers HTTP, database and queue clients with almost no code   |
| Sample traces, not logs        | Head sampling for volume; tail sampling to keep all the slow and failed ones                                        |
| Bound metric cardinality       | A label containing a user id or a URL with an id creates millions of series and will take down your metrics backend |
| Log levels that mean something | `ERROR` = someone must look. If errors are routine noise, nobody looks at any of them                               |
| Never log secrets or PII       | Logs are widely readable, long-retained, and shipped to third parties                                               |
| Retention by value             | Full detail for days, aggregates for months, archives for years                                                     |

###### Cardinality, sampling and what observability actually costs

``` text
  METRIC CARDINALITY - the mistake that takes down monitoring systems

  a time series exists per UNIQUE COMBINATION of label values.

    http_requests_total{method, status, endpoint}
      5 methods x 8 statuses x 40 endpoints  =  1,600 series      fine

    add user_id (100,000 users)
      -> 160,000,000 series
      -> your Prometheus runs out of memory and dies, and takes your
         visibility with it during the incident it was meant to reveal

  NEVER put in a label: user id, order id, session id, email, full URL
                        with an id in it, trace id, timestamp, IP address

  those belong in LOGS and TRACES, which are built for high cardinality.
  metrics answer "how many"; logs and traces answer "which one".
```

``` text
# normalise the endpoint label at the source
/orders/4471   ->  /orders/{id}
/users/abc/orders/99  ->  /users/{id}/orders/{id}

# most auto-instrumentation does this. Verify it, because a framework that
# reports the raw path will silently produce unbounded cardinality.

# tail sampling: keep everything interesting, discard the routine
processors:
  tail_sampling:
    policies:
      - { name: errors,  type: status_code, status_code: {status_codes: [ERROR]} }
      - { name: slow,    type: latency, latency: {threshold_ms: 1000} }
      - { name: sample,  type: probabilistic, probabilistic: {sampling_percentage: 1} }
# -> 100% of errors and slow requests, 1% of normal ones.
#    Head sampling would discard 99% of your errors too.
```

| Cost driver        | Control                                                               |
|--------------------|-----------------------------------------------------------------------|
| Log volume         | Level discipline; sample repetitive lines; drop health-check logs     |
| Log retention      | Days at full fidelity, then aggregate, then archive to object storage |
| Metric cardinality | Bound labels; review new metrics in code review                       |
| Trace volume       | Tail sampling                                                         |
| Egress to a vendor | A collector that filters and aggregates before shipping               |

A practical observation: observability spend frequently exceeds the compute spend of the system being observed, and almost always because of debug logging left on, health-check requests being traced, or one high-cardinality label. All three are found in an afternoon, and none is found without looking.

###### Aggregation, retention and cost

Logs on a machine are useless the moment you have more than one machine, and dangerous once disks fill. The aggregation pipeline — agent, collector, store, query layer — is what makes them searchable across instances, but it is also where observability spend concentrates. The lever is not sampling everything more aggressively; it is choosing the right signal for the question: metrics for “is it healthy”, traces for “where did the time go”, logs for “what exactly happened to this one request”.

| Tier                      | Retention  | Contents                                                     |
|---------------------------|------------|--------------------------------------------------------------|
| Hot (indexed, searchable) | 7–14 days  | Everything at INFO and above — what incidents actually query |
| Warm (queryable, slower)  | 30–90 days | Trend analysis, post-incident review                         |
| Cold (object storage)     | 1–7 years  | Compliance and audit; restored on demand, rarely read        |

> **\[Trap\]**
>
> **Common trap.** Log volume grows with traffic, so a per-request DEBUG line is a bill that scales with your success — and the log that finally overwhelms the pipeline usually arrives during the incident you needed it for. Sample debug output, cap per-request lines, and alert on ingestion rate as well as on errors.

<a id="be-g709"></a>

#### Alerting that people trust

The failure mode of alerting is not missing an incident. It is alert fatigue — enough noise that the real alert is dismissed with everything else.

| Alert on                                                | Not on                               |
|---------------------------------------------------------|--------------------------------------|
| Symptoms users feel — error rate, latency, availability | Causes — CPU high, memory high       |
| SLO burn rate                                           | Every individual threshold crossing  |
| Sustained conditions                                    | Momentary spikes                     |
| Things a human must act on *now*                        | Things that can wait for the morning |
| Absence of expected work — a job that did not run       | Every completed job                  |
| Certificate expiry, disk filling, quota nearing         | Every capacity metric                |

``` text
  SLO-BASED ALERTING, and why it beats thresholds

  threshold:  "alert if error rate > 1%"
              fires at 3am for a 90-second blip that consumed 0.01% of
              the monthly budget. Nobody needed to wake up.

  burn rate:  "alert if we are consuming the error budget fast enough
               to exhaust it in under N hours"
              -> a fast burn pages immediately
              -> a slow burn creates a ticket
              -> a blip does neither

  this ties alert urgency to actual user impact, which is the only
  thing that justifies waking someone.
```

| Every alert needs                       | Because                                              |
|-----------------------------------------|------------------------------------------------------|
| A clear title stating user impact       | “Checkout failing for EU users”, not “HighErrorRate” |
| A link to the relevant dashboard        | Saves minutes at the worst time                      |
| A runbook link                          | The responder may not be the person who built it     |
| A severity that determines the response | Page, ticket, or dashboard-only                      |
| An owner                                | An alert nobody owns is deleted or ignored           |

> **\[Note\]**
>
> **The test for an alerting system.** For every page in the last month, ask: did a human need to do something immediately? If the honest answer is no more than about half the time, the alerting is training people to ignore it. Delete or downgrade alerts aggressively — a smaller set of trusted alerts detects more real incidents than a large set of ignored ones.

###### The arithmetic, and burn-rate alerts that people trust

``` text
  THE ERROR BUDGET, computed

    SLO 99.9% over 30 days
      30 days = 43,200 minutes
      allowed failure = 0.1% = 43.2 minutes per month

    that 43 minutes is a BUDGET. Spending it is not a failure - it is
    the point. An SLO met with 100% of the budget unspent means the
    target is too conservative and you are over-investing in reliability
    at the expense of features.

  BURN RATE = how fast you are spending it

    burn rate 1x    -> exactly on budget. Fine.
    burn rate 14.4x -> the whole month's budget gone in ~2 days.

  ALERTING ON BURN RATE, not on threshold crossings:

    fast burn   14.4x over 1 hour   -> PAGE. 2% of budget in an hour.
    slow burn   6x over 6 hours     -> PAGE, lower urgency.
    trickle     1x over 3 days      -> ticket, not a page.

    a 90-second blip consumes ~0.03% of the budget -> NOTHING FIRES.

  this is why burn-rate alerting produces alerts people trust: urgency
  is tied to user impact, not to a threshold being momentarily crossed.
```

| Availability | Per month | Per week | Realistic for                             |
|--------------|-----------|----------|-------------------------------------------|
| 99%          | 7.2 h     | 1.7 h    | Internal tools                            |
| 99.9%        | 43 min    | 10 min   | **Most business services**                |
| 99.95%       | 22 min    | 5 min    | Revenue-critical                          |
| 99.99%       | 4.3 min   | 1 min    | Needs multi-region and serious investment |
| 99.999%      | 26 s      | 6 s      | Almost never a real requirement           |

Each additional nine roughly multiplies cost by ten, and the failure modes change in kind rather than degree — at 99.99% a single rolling deploy consuming four minutes has spent the entire monthly budget. Note also that your SLO cannot meaningfully exceed your *dependencies'*: a service on one cloud database with a 99.99% SLA cannot credibly promise 99.999%.

> **\[Note\]**
>
> **The test of an alerting system.** For every page in the last month, ask whether a human needed to act immediately. If the answer is no more than about half the time, the system is training people to ignore it — and the next real alert will be ignored too. Delete and downgrade aggressively; a small set of trusted alerts detects more real incidents than a large set of ignored ones.

### Logging and observability, practically

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Logging and observability, practically</p>

<p class="pd">Structured logs, scopes and attribute filtering, OpenTelemetry, and the specific case of auditing an SSO logout request end to end.</p>

<a id="be-q1"></a>

#### Structured logging: the difference that matters

A traditional log line is a sentence. A structured log line is a **sentence plus a dictionary**: the message template stays constant, and the values become queryable fields. That single change is what turns logs from something you read into something you query.

``` csharp
// ✗ string interpolation — the values are baked into the text
_logger.LogInformation($"Order {order.Id} placed by {user.Id} for {total:C}");
// stored as: "Order 8f2c… placed by 41 for $99.00"
// → to find all orders over $500 you are now writing a regex over text

// ✓ message template — the values stay separate and typed
_logger.LogInformation("Order {OrderId} placed by {UserId} for {Total}",
                       order.Id, user.Id, order.Total);
// stored as:
// {
//   "message":  "Order 8f2c placed by 41 for 99.00",
//   "template": "Order {OrderId} placed by {UserId} for {Total}",
//   "OrderId":  "8f2c…", "UserId": 41, "Total": 99.00,
//   "TraceId":  "0af7651916cd43dd8448eb211c80319c"
// }
// → OrderId:"8f2c*" | Total > 500 | count by UserId  … all become queries
```

Two refinements make it production-grade. **Scopes** attach context to every log written inside a block, so you do not repeat yourself. **Source-generated loggers** remove the boxing and the format cost when the level is disabled.

``` csharp
// scope: every log inside this block carries these fields automatically
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["TenantId"] = tenant.Id,
    ["OrderId"]  = order.Id,
}))
{
    _logger.LogInformation("Reserving stock");     // has TenantId + OrderId
    await _inventory.ReserveAsync(order, ct);
    _logger.LogInformation("Stock reserved");      // and so does this
}

// source-generated: no boxing, no allocation when Information is disabled
public static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Order {OrderId} placed by {UserId} for {Total}")]
    public static partial void OrderPlaced(ILogger logger,
        Guid orderId, int userId, decimal total);
}
Log.OrderPlaced(_logger, order.Id, user.Id, order.Total);
```

> **\[Trap\]**
>
> **Common trap.** Never interpolate into the template (`$"..."`) — you lose every field *and* pay the formatting cost even when the level is disabled. And never log the whole object (`LogInformation("User {@User}", user)`) without checking what is on it: password hashes, tokens and personal data end up in a system your whole company can search.

<a id="be-q2"></a>

#### Filtering by attribute: scopes, enrichers and log levels

“Filter logs by attribute” happens at three different places, and knowing which one you need is most of the work.

| Where                     | Mechanism                      | Use it for                                                      |
|---------------------------|--------------------------------|-----------------------------------------------------------------|
| Before the log is written | `LogLevel` config per category | Turning down noisy libraries (EF Core SQL, HttpClient)          |
| As it is written          | Scopes and enrichers           | Attaching TenantId, UserId, TraceId, MachineName to every event |
| In the backend            | Query by field                 | Investigating: `TenantId=42 AND Level=Error`                    |

``` text
// appsettings.json — category-based level filtering
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "MyApp.Payments": "Debug"        // one namespace turned up
    }
  }
}
```

``` csharp
// middleware that puts request-wide attributes on EVERY log in the request
app.Use(async (ctx, next) =>
{
    var scope = new Dictionary<string, object?>
    {
        ["TraceId"]  = Activity.Current?.TraceId.ToString(),
        ["TenantId"] = ctx.User.FindFirst("tenant_id")?.Value,
        ["UserId"]   = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
        ["Path"]     = ctx.Request.Path.Value,
    };
    using (ctx.RequestServices.GetRequiredService<ILogger<Program>>()
                              .BeginScope(scope))
    {
        await next(ctx);
    }
});

// a custom filter: drop health-check noise entirely, keep everything else
builder.Logging.AddFilter((provider, category, level) =>
{
    if (category?.Contains("HealthChecks") == true && level < LogLevel.Warning)
        return false;
    return level >= LogLevel.Information;
});
```

> **\[Trap\]**
>
> **Common trap.** Scopes only flow to loggers resolved *inside* the scope on the same async flow — they ride on `AsyncLocal`. Fire-and-forget work started with `Task.Run` inherits the scope at the moment it starts, but a message pulled off a queue later does not. Re-establish the scope in the consumer from the message’s own correlation id.

<a id="be-q4"></a>

#### OpenTelemetry: traces, metrics and logs from one pipeline

OpenTelemetry is a vendor-neutral standard for emitting three signals. In .NET, traces are `Activity`, metrics are `Meter`, and logs are the `ILogger` you already use — OTel just exports them somewhere.

``` csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-api", serviceVersion: "2.4.1"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o =>
        {
            o.RecordException = true;
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()          // outbound calls
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
        .AddSource("MyApp.Orders")               // your own spans
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()             // GC, thread pool, exceptions
        .AddMeter("MyApp.Orders")
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeScopes = true;                      // ← your scopes become attributes
    o.AddOtlpExporter();
});
```

``` csharp
// your own span and metric — the two lines that make a workflow visible
private static readonly ActivitySource Source = new("MyApp.Orders");
private static readonly Meter Meter = new("MyApp.Orders");
private static readonly Counter<long> Placed = Meter.CreateCounter<long>("orders.placed");
private static readonly Histogram<double> Duration =
    Meter.CreateHistogram<double>("orders.place.duration", unit: "ms");

public async Task<Order> PlaceAsync(Cart cart, CancellationToken ct)
{
    using var activity = Source.StartActivity("place-order");
    activity?.SetTag("order.items", cart.Items.Count);
    activity?.SetTag("tenant.id", _tenant.Id);

    var sw = Stopwatch.StartNew();
    try
    {
        var order = await _repo.SaveAsync(cart, ct);
        activity?.SetTag("order.id", order.Id);
        Placed.Add(1, new KeyValuePair<string, object?>("result", "ok"));
        return order;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Placed.Add(1, new KeyValuePair<string, object?>("result", "error"));
        throw;
    }
    finally { Duration.Record(sw.Elapsed.TotalMilliseconds); }
}
```

The payoff is correlation: because the log records carry the same `TraceId` as the span, one click moves from “this request took 4 seconds” to the exact SQL statement and the log lines around it — across services.

> **\[Trap\]**
>
> **Common trap.** Traces are sampled; metrics are not. If you compute a rate from traces you get the rate *of the sample*. Count with a metric, investigate with a trace, and read the detail in logs — using the wrong signal for the question is the most common way teams end up with expensive telemetry that answers nothing.

### Shipping it

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Shipping it</p>

<p class="pd">The artefacts that turn working code into a running service: the container, the health endpoints, the observability wiring and the deployment manifest.</p>

<a id="be-w17"></a>

#### The observability wiring that makes the next incident short

``` csharp
// one request, one identity, across logs, traces and metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString(),
        serviceInstanceId: Environment.MachineName))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o =>
        {
            o.RecordException = true;
            o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
            o.EnrichWithHttpRequest = (a, r) =>
                a.SetTag("tenant.id", r.HttpContext.User.TenantId());
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("Orders.*")
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation().AddRuntimeInstrumentation()
        .AddMeter("Orders.*").AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o => { o.IncludeScopes = true; o.AddOtlpExporter(); });

// always sample what went wrong, whatever the ratio says
public sealed class ErrorBiasedSampler(Sampler inner) : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters p) =>
        p.Tags?.Any(t => t.Key == "error") == true
            ? new SamplingResult(SamplingDecision.RecordAndSample)
            : inner.ShouldSample(in p);
}
```

``` text
// the four alerts that are worth being woken for — symptoms, not causes
// 1. availability:  rate(http_requests{status=~"5.."}[5m]) / rate(http_requests[5m]) > 0.01
// 2. latency:       histogram_quantile(0.95, http_request_duration) > 1s for 10m
// 3. saturation:    dotnet_threadpool_queue_length > 100 for 5m
// 4. work stopped:  rate(orders_placed_total[15m]) == 0 during business hours
//
// note #4: the only one that catches "deploy succeeded, feature silently broken".
// Health checks are green, latency is fine, error rate is zero — because nothing
// is happening at all.
```

> **\[Why it matters\]** Every one of these takes minutes to add and pays for itself the first time something breaks at 2am. The trace tells you *which hop*, the metric tells you *how many users*, the log tells you *what exactly* — and because they share a trace id, you move between them by clicking rather than by guessing.
