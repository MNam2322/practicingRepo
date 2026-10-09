---
id: "8-1"
number: "8.1"
title: "What a backend does"
part: 8
part_title: "Building a backend"
words: 5742
minutes: 25
---

# 8.1 What a backend does

> **Quick look.** What belongs on the server, and how one request travels through a backend.
>
> - The backend holds shared data, secrets and rules, because clients can't be trusted.
> - A request passes routing, authentication, validation, business logic, data access and response writing; each step can fail or add time.
> - Organize code in layers with dependencies pointing inward, in vertical slices by feature, or a mix.
> - Keep instances stateless, configured from the environment, with liveness and readiness checks.
> - Middleware order is the contract; graceful shutdown needs grace period > host timeout > longest request.
> - Give each request a time budget, a timeout on every outgoing call, and watch percentiles, not averages.


### The big picture

When a passenger taps "Book" in an airline's app, the app itself decides very little. It sends a request to software running on the airline's servers, which checks the seat is really free, applies the fare rules, charges the card, records the booking and replies. That server-side software is the backend. It holds the data and the rules, and every web page, mobile app, kiosk and partner system relies on it.

This chapter is the overview for Part 8. It explains what belongs on the server and why, follows a single request through a backend from arrival to response, and shows the usual ways of organizing backend code. It then covers the properties that let a backend run on many machines at once: statelessness, configuration from the environment, health checks and time budgets for every request. The example is a complete tiny backend that runs its own client, so you can watch requests and responses end to end.

### Before you start

**Builds on:** 6.6 HTTP, 4.5 Design principles that hold up, 4.4 Immutability and pure functions, 7.6 Non-relational stores, 1.7 The operating system, 4.3 Composition and dependency injection.

**Ideas explained later:**

- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Authentication** (10.2): Establishing who a caller is.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.

**Tools and technologies named here:**

- **Deploy** (11.4): To install a new version of software where it will run, such as on production servers.

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, null operators, switch expressions and patterns, tuples, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why must business rules like "no overbooking" be enforced on the server, even if the app checks them too?**

> The client is under the user's control. Anyone can change a web page's code, replay requests or write their own client. Checks in the client are for convenience; the server's checks are the ones that count.

**2. What does it mean for a backend service to be stateless?**

> It keeps no memory of individual users between requests in its own process. Anything that must persist lives in a database or cache. Any instance can then handle any request, so instances can be added, removed or restarted freely.

**3. What is the difference between a liveness check and a readiness check?**

> A liveness check says the process is working at all; failing it means "restart me". A readiness check says it can serve traffic right now, including its dependencies; failing it means "don't send me requests yet".

**4. What is tail latency, and why does it matter more when one request calls many services?**

> The response time of the slowest requests, such as the 99th percentile. A request that waits for ten services is as slow as the slowest of the ten, so rare slow responses in each become common in the combined request.

**5. What is the difference between a layered structure and vertical slices?**

> Layers split code by technical role: API, business logic, data access. Vertical slices split it by feature, with each feature's endpoint, logic and data access kept together. Many codebases combine the two.

**6. Why are sticky sessions a problem?**

> They send each user's requests to the same server because state lives in that server's memory. That server can't be restarted or removed without losing the state, and load can't be spread evenly.

### Clients, servers and backends

A **server** is a program that waits for requests and answers them, or the machine that runs it. A **client** is a program that sends those requests: a browser, a mobile app, a command-line tool or another server. The same program can be both, a server to its callers and a client of the database and services it calls in turn.

The **backend** is the part of a system that runs on servers the organization controls. It holds the data, enforces the rules and does work that needs secrets or heavy computing. The **frontend** is the part that runs on the user's device and presents the system to people: a web page in a browser, a mobile app, an airport kiosk. Part 9 covers frontends.

Most systems have several frontends for one backend, and often several backends talking to each other: bookings, loyalty, pricing, check-in, each run by a different team.

### What belongs on the server

Some responsibilities must live in the backend, whatever the frontend does.

**The rules that must hold.** A client can be modified, replayed or replaced by someone else's code. Any check a client makes, such as "this seat is free" or "this fare applies", is a convenience for the user. The server must check again, every time. This is often summed up as "never trust the client".

**Shared data.** Seat maps, bookings and balances are shared between users, so they live in one place that everyone's requests go through, as Part 7 described.

**Secrets.** Database passwords, payment provider keys and signing keys can't be shipped to devices, where anyone could extract them.

**Heavy or slow work.** Generating documents, calling partners and processing payments happen on servers built and scaled for it.

Everything else is a design choice. Keeping presentation and immediate interaction in the frontend makes it fast. Keeping decisions in the backend keeps them consistent across every frontend.

### A request's journey

Follow one booking request through a typical backend.

It arrives over HTTPS, as HTTP (6.6) described, usually through a load balancer that terminated TLS and chose one of several instances. The web framework reads the request and routes it by method and path to the code that handles it. The request is authenticated, working out who is calling, and authorized, deciding whether they may do this, as Authentication and authorization (10.2) explains. The body is parsed and validated, as Validation and errors at the boundary (8.5) covers.

Then comes the part that is actually about the business, the **business logic**: is the seat free, which fare applies, what is the total. It reads and writes data through the database, as Part 7 covered, and may call other services or queue background work, as Background work and queues (8.7) shows. Finally, the result is turned into an HTTP response, usually JSON, and logs, metrics and traces are recorded along the way, as Observability (11.6) explains.

Each step can fail, and each adds time. A good backend makes every step explicit, fast and observable.

### Organizing the code

Two broad ways of organizing backend code are common, and most real codebases mix them.

A **layered architecture** splits code by technical role. An API layer turns HTTP into calls and results into responses. An application layer coordinates each use case, such as "book a seat". A domain layer holds the business rules and entities. An infrastructure layer talks to databases, queues, email and other services. Dependencies point inward: the domain knows nothing about HTTP or SQL, so it can be tested and changed on its own. This applies the dependency inversion principle from Design principles that hold up (4.5), and at its best it is the functional core and imperative shell from Immutability and pure functions (4.4).

**Vertical slices** split code by feature instead. Everything for "book a seat", its endpoint, request types, logic and data access, sits together in one folder. Adding a feature means adding a slice, not editing four layers. Shared rules still live in a shared domain model.

Layers make the separation of concerns clear. Slices keep related code together and changes local. A common combination is slices for features, with a small shared domain core and infrastructure behind interfaces.

### Stateless services

A backend that must stay up and handle growing traffic runs as several identical instances behind a load balancer: the horizontal scaling from Non-relational stores (7.6). That only works if any instance can handle any request.

So instances should be a **stateless service**: they keep nothing about individual users or conversations in their own memory between requests. Sessions, shopping baskets, half-finished workflows and uploaded files go into a database, a cache such as Redis, or object storage. Each request carries what it needs to identify its user, such as a token. Then instances can be added under load, removed when quiet, and restarted or replaced during deployments, with nobody noticing.

Keeping state in instance memory forces **sticky sessions**, where the load balancer sends each user back to the same instance. Load then spreads unevenly, and restarting an instance loses its users' state. In-memory caches of shared, re-creatable data are fine; the rule is about state that would be lost.

### Configuration from the environment

The same build of a backend runs on a developer's laptop, in test and in production. What differs is configuration: connection strings, service addresses, feature switches and limits. In 2011, engineers at the Heroku platform wrote down practices for services that run well in the cloud, the **twelve-factor app**. Its central ideas still apply. Keep configuration in the environment, not in the code, typically through environment variables, as The operating system (1.7) showed. Treat databases and queues as attached resources, swappable by configuration. Build once, and deploy the same build everywhere. Write logs to standard output and let the platform collect them. Make instances quick to start and safe to stop.

.NET's configuration system reads settings from `appsettings.json`, environment-specific files, environment variables and secret stores, in layers, and the options pattern from Composition and dependency injection (4.3) validates them at start-up. Secrets need special care, covered in Secrets and cryptography basics (10.5).

### Health checks

Load balancers and orchestrators such as Kubernetes need to know whether each instance is working. A backend answers with a **health check**: an endpoint, such as `/health`, that reports whether the instance is healthy.

Two kinds are worth separating. A liveness check answers "is this process working at all?". If it fails, the platform restarts the instance. It should be simple, checking only the process itself; if it checked the database, a database outage would make the platform restart every instance, which helps nobody. A **readiness check** answers "can this instance serve traffic right now?". It can check dependencies and warm-up. If it fails, the load balancer stops sending requests to the instance until it passes again.

### Budgets for time

A user waiting for a page doesn't care which service was slow. So each request has a **latency budget**: the total time it may take, split across the calls it makes. A 300 ms budget might allow 50 ms for authentication, 100 ms for the pricing service and 80 ms for the database, with room for the backend's own work.

Averages hide the problem cases. Latency is described by **percentiles**: the 50th percentile, or median, is the time half of requests beat; the 99th percentile is the time all but 1% beat. The slowest requests are the **tail latency**, and they matter more than they seem. A request that calls ten services in parallel waits for the slowest of the ten. If each service has a 1% chance of a slow response, about 10% of the combined requests hit at least one.

Budgets become timeouts on every outgoing call, set shorter than the caller's own timeout, as HTTP (6.6) advised. The calls a request makes should run in parallel where they are independent, as Async and await (5.3) showed, and anything that doesn't need to finish before the user gets an answer should move to background work.

### A tiny complete backend

This program is a real ASP.NET Core backend in one file. It starts itself, then acts as its own client, so you can see requests and responses end to end. The first line tells `dotnet run` to use the web SDK. It has a health check, a read endpoint, and a booking endpoint whose business logic refuses overbooking even when requests arrive at once. A modern .NET backend (8.3) explains every part in detail.

```csharp
#:sdk Microsoft.NET.Sdk.Web
// backend.cs — run with: dotnet run backend.cs
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();                            // keep the console output readable
builder.WebHost.UseUrls("http://127.0.0.1:5057");
builder.Services.AddSingleton<FlightStore>();                // persistence: an in-memory stand-in for a database
builder.Services.AddSingleton<BookingService>();             // business logic
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/flights/{number}", (string number, FlightStore store) =>
    store.Find(number) is { } flight ? Results.Ok(flight) : Results.NotFound());
app.MapPost("/flights/{number}/bookings", (string number, BookingRequest request, BookingService bookings) =>
    bookings.Book(number, request.Passenger) switch
    {
        BookingResult.Booked b => Results.Created($"/flights/{number}/bookings/{b.Seat}", b),
        BookingResult.NoSuchFlight => Results.NotFound(),
        BookingResult.Full => Results.Conflict(new { error = "flight is full" }),
        _ => Results.StatusCode(500),
    });

await app.StartAsync();                                      // the server now runs in the background

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5057") };   // and we become its client
Console.WriteLine(await http.GetStringAsync("/health"));     // {"status":"healthy"}
Console.WriteLine(await http.GetStringAsync("/flights/BA117"));   // {"number":"BA117","seats":2,"booked":0}
for (int i = 0; i < 3; i++)
{
    using var response = await http.PostAsJsonAsync("/flights/BA117/bookings", new { passenger = $"passenger {i}" });
    Console.WriteLine($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}
// 201 {"seat":1,"passenger":"passenger 0"}
// 201 {"seat":2,"passenger":"passenger 1"}
// 409 {"error":"flight is full"}
Console.WriteLine((int)(await http.GetAsync("/flights/XX999")).StatusCode);   // 404
await app.StopAsync();

record Flight(string Number, int Seats, int Booked);
record BookingRequest(string Passenger);

abstract record BookingResult
{
    public sealed record Booked(int Seat, string Passenger) : BookingResult;
    public sealed record NoSuchFlight : BookingResult;
    public sealed record Full : BookingResult;
}

sealed class FlightStore
{
    readonly ConcurrentDictionary<string, Flight> flights = new() { ["BA117"] = new Flight("BA117", Seats: 2, Booked: 0) };
    public Flight? Find(string number) => flights.TryGetValue(number, out var f) ? f : null;
    public bool TryUpdate(Flight seen, Flight updated) => flights.TryUpdate(seen.Number, updated, seen);   // compare-and-swap, as in 5.2
}

sealed class BookingService(FlightStore store)
{
    public BookingResult Book(string number, string passenger)
    {
        while (true)                                          // retry if another request changed the flight meanwhile
        {
            if (store.Find(number) is not { } flight) return new BookingResult.NoSuchFlight();
            if (flight.Booked >= flight.Seats) return new BookingResult.Full();
            var updated = flight with { Booked = flight.Booked + 1 };
            if (store.TryUpdate(flight, updated)) return new BookingResult.Booked(updated.Booked, passenger);
        }
    }
}
```

The booking result is a small closed set of cases, as in Types (2.3), so the endpoint's `switch` maps each business outcome to exactly one HTTP status. In a real backend the store would be a database, and the compare-and-swap would be a conditional `UPDATE` or a concurrency token, as Transactions and isolation (7.4) described.

### Backends talking to backends

As systems grow, backends split into several services that call each other. They talk in two broad ways.

Synchronous calls, usually HTTP or gRPC, wait for an answer: the booking service asks the pricing service for a fare and uses the reply. They are simple, and they couple the services' availability: if pricing is down, booking can't finish. API design (8.2) covers designing these interfaces.

Asynchronous messages go through a queue or broker, and the sender doesn't wait: the booking service announces "booking created", and loyalty, email and analytics react in their own time. Each service keeps working when another is down, at the cost of eventual consistency, as Non-relational stores (7.6) described. Background work and queues (8.7) and Messaging and events (12.3) cover these.

How far to split a system into services, and when not to, is the subject of Monoliths, modules and services (12.1).

### In short

The backend runs on servers you control, holds shared data and secrets, and enforces the rules, because clients can't be trusted. A request passes through routing, authentication, validation, business logic, data access and response writing, and every step can fail or add time. Organize code in layers with dependencies pointing inward, in vertical slices by feature, or a mix. Keep instances stateless, configured from the environment, and observable through liveness and readiness checks. Give each request a time budget, set timeouts on every call, and watch tail latency, not just averages.

### What goes wrong

**Trusting the client.** A price, discount or permission check done only in the app is bypassed by editing a request. Enforce every rule on the server.

**State in instance memory.** Sessions or baskets kept in memory vanish when an instance restarts, and force sticky sessions. Keep state in a shared store.

**Configuration baked into the build.** A connection string in code means a new build for every environment, and secrets in source control. Read configuration from the environment and a secret store.

**Health checks that test everything.** A liveness check that queries the database restarts every instance during a database outage. Keep liveness simple and put dependency checks in readiness.

**No timeouts on outgoing calls.** One slow dependency ties up every request thread and takes the whole backend down with it. Give every call a timeout within the request's budget.

**Averages instead of percentiles.** An average of 80 ms hides that 2% of users wait five seconds. Measure and alert on the 95th and 99th percentiles.

**Business logic in the endpoint.** Rules written directly in HTTP handlers are hard to test and get copied into other endpoints. Keep handlers thin and put rules where they can be reused and tested.

### Cost model

A modern web framework adds only tens of microseconds per request for routing, parsing and serialization. So a well-written backend spends its time waiting. Database queries take about a millisecond each, calls to other services a few milliseconds each within a data centre, and then there is the network to the user. One instance can typically handle thousands of simple requests per second. Each added synchronous dependency adds its latency and its failure rate to every request that uses it, while stateless instances add capacity roughly linearly until a shared dependency, usually the database, becomes the limit.

### In other languages (optional)

**JavaScript and TypeScript.** Node.js backends with Express, Fastify or NestJS follow the same request journey, on an event loop, as A Node and TypeScript backend (8.4) shows.

**Python.** FastAPI, Django and Flask are the common frameworks. FastAPI is async and validates requests from type hints.

**Java and Kotlin.** Spring Boot dominates, with the same layers, dependency injection and health endpoints, the last through Spring Boot Actuator.

**Go.** The standard `net/http` package is enough for many services, and small, statically compiled binaries start quickly, which suits containers.

### Key terms

- **Server**: A program, or the machine running it, that waits for requests and answers them.
- **Client**: A program that sends requests to a server.
- **Backend**: The part of a system that runs on the organization's servers, holding data and enforcing rules.
- **Frontend**: The part of a system that runs on the user's device and presents it to people.
- **Business logic**: The rules and calculations that are actually about the business, separate from HTTP and storage.
- **Layered architecture**: Organizing code by technical role, such as API, application, domain and infrastructure layers.
- **Vertical slice**: Organizing code by feature, keeping each feature's endpoint, logic and data access together.
- **Stateless service**: A service whose instances keep no per-user state between requests, so any instance can serve any request.
- **Sticky session**: Routing each user's requests to the same instance because their state lives in its memory.
- **Twelve-factor app**: A set of practices for services that run well in the cloud, such as keeping configuration in the environment.
- **Health check**: An endpoint that reports whether an instance is working, for load balancers and orchestrators.
- **Readiness check**: A health check that says whether an instance can serve traffic right now.
- **Latency budget**: The total time a request may take, divided among the work and calls it makes.
- **Percentile**: The value below which a given share of measurements fall, such as the 99th percentile response time.
- **Tail latency**: The response time of the slowest requests, described by high percentiles.

### Where this leads

Designing the HTTP interface a backend offers is in API design (8.2). Building one properly in .NET is in A modern .NET backend (8.3), and in Node.js in A Node and TypeScript backend (8.4). Checking what comes in and reporting what went wrong is in Validation and errors at the boundary (8.5). Making reads fast is in Caching (8.6), and moving slow work out of requests is in Background work and queues (8.7).

### Check yourself

1. In `backend.cs`, fire 20 booking requests at once with `Task.WhenAll` against a flight with 5 seats. Count the 201 and 409 responses.
2. Add a readiness endpoint, `/ready`, that returns 503 until a flag is set a few seconds after start-up.
3. Move the seat count for BA117 into configuration, read from an environment variable, and check that changing it needs no code change.
4. Draw the journey of one request through a backend you know. Mark every network call and estimate its share of the time.
5. Find where a system you know keeps per-user state. Decide whether its instances could be restarted at any moment without users noticing.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A discount the client calculated

The mobile app sends `{ price: 120 }` after applying a promo code locally. A tester changes it to 1.

*The server decides prices*

```csharp
public sealed record CreateBookingRequest(Guid FlightId, string FareClass, string? PromoCode);   // no price field

var fare = await fares.GetAsync(req.FlightId, req.FareClass, ct);
var total = promos.Apply(fare, req.PromoCode, customer);   // computed server-side from trusted data
```

> **Lesson:** The client can't be trusted with rules, prices or permissions. The backend computes and enforces them.

#### Example 2: A request budget across four calls

The booking endpoint must answer in 2 seconds, but each of its four downstream calls has a 30-second default timeout.

*Derive timeouts from the budget*

```csharp
builder.Services.AddHttpClient<InventoryClient>()
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(400);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(900);   // includes one retry
    });
// the endpoint also passes HttpContext.RequestAborted so abandoned requests stop work
```

> **Lesson:** Give every request a time budget and set each outbound timeout from it.

### Under the Hood

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Under the Hood</p>

<p class="pd">What actually happens between dotnet run and a byte on the wire. Assembly loading, the JIT, the thread pool, Kestrel, routing, model binding, filters and the DI container — the machinery that makes everything above it feel like magic until it breaks.</p>

<a id="be-a49"></a>

#### The full request lifecycle, end to end

Putting the previous sections together for a single authenticated `POST /orders` that writes to a database and publishes an event.

``` text
   1  TCP accept, TLS handshake (ALPN picks h2)
   2  Kestrel parses headers from the pipe; HttpContext rented from the pool
   3  An IServiceScope is created for this request
   4  Middleware: exception handler -> forwarded headers -> routing
   5  Routing matches POST /orders, sets the endpoint and its metadata
   6  Authentication middleware validates the JWT against cached JWKS -> ClaimsPrincipal
   7  Authorization reads the endpoint's policy metadata and evaluates it
   8  Rate limiter checks the partition for this client
   9  Endpoint invoked: model binding deserializes the body, validation runs
  10  Your handler resolves a SCOPED DbContext from the request scope
  11  EF builds the query/command, opens a POOLED connection, runs it in a transaction
  12  The outbox row is written in the same transaction; commit
  13  Result executed: serialized by System.Text.Json to the PipeWriter
  14  Response flushed; Activity ends and the span is exported to the collector
  15  Scope disposed -> DbContext disposed -> connection returned to the pool
  16  HttpContext reset and returned to the pool; connection kept alive

   ...meanwhile, a BackgroundService relay picks up the outbox row and publishes it
```

> **\[Why it matters\]** Every layer in this list is somewhere a production problem hides: TLS at 1, header limits at 2, a leaked scope at 3, middleware order at 4–8, a captive DbContext at 10, connection pool exhaustion at 11, and a serialization cycle at 13. Being able to name the step is most of the diagnosis.

###### A traced request, with where the time actually goes

``` text
  GET /api/orders/4471   -   a real trace, annotated

  0.00 ms   kernel accepts the connection, hands the socket to Kestrel
  0.05      Kestrel parses request line and headers  (no allocation for common ones)
  0.10      HttpContext created from a POOLED object - not a new allocation
  0.12      middleware: exception handler, HSTS, HTTPS redirect
  0.20      UseRouting        -> Endpoint selected, metadata attached
  0.25      UseAuthentication -> JWT validated (signature cached, ~50us)
  0.30      UseAuthorization  -> reads [Authorize] metadata, evaluates policy
  0.40      endpoint executes -> a DI SCOPE is created here
  0.45      scoped services resolved: DbContext, repositories
  0.60      model binding + validation
  1.20      >>> DbContext query: SQL generated, connection taken from pool
  8.90      >>> database round trip - THE DOMINANT COST, 7.7 ms
  9.10      change tracker materialises entities
  9.30      mapping entity -> DTO
  9.50      JSON serialisation to the response body
  9.70      response written; SCOPE DISPOSED -> DbContext returned/disposed
  9.80      connection returned to the pool

  the shape is almost always this: ONE dependency call dominates, and
  everything the framework does is noise beside it. Which is why optimising
  middleware before measuring is nearly always wasted effort.
```

| Layer                 | Typical cost  | When it becomes the problem                             |
|-----------------------|---------------|---------------------------------------------------------|
| Kestrel parsing       | \< 0.1 ms     | Essentially never                                       |
| Middleware pipeline   | 0.1–0.5 ms    | A badly written custom middleware doing I/O             |
| Authentication        | 0.05–5 ms     | Fetching signing keys per request instead of caching    |
| DI scope + resolution | 0.05–1 ms     | Hundreds of registrations, or a factory doing real work |
| Model binding         | \< 0.5 ms     | Very large request bodies                               |
| **Database**          | **1–1000 ms** | **Usually. Start here**                                 |
| Serialisation         | 0.1–10 ms     | Large payloads — then paginate rather than optimise     |
| Outbound HTTP         | 10–1000 ms    | No timeout, no pooling, or sequential calls             |

``` csharp
// instrument the layers, so the trace above is something you can see
builder.Services.AddOpenTelemetry().WithTracing(t => t
    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
    .AddHttpClientInstrumentation()
    .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
    .AddOtlpExporter());

// and one custom span where your own logic is non-trivial
using var activity = ActivitySource.StartActivity("PriceCalculation");
activity?.SetTag("order.lines", lines.Count);
```

> **\[Note\]**
>
> **The diagnostic order.** When a request is slow: get a trace first, find the single widest span, and work on that. In the large majority of cases it is one database call, one outbound HTTP call, or a sequential loop of either. Framework-level tuning — middleware order, serializer settings, DI lifetimes — matters only after those are dealt with, and usually not even then.

### How an application is wired

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How an application is wired</p>

<a id="be-m228"></a>

#### The host: what runs before your code

A modern .NET application is a **host** containing a service container, a configuration system, a logging pipeline, and a set of hosted services with a shared lifetime. A web application is that plus an HTTP server.

``` csharp
var builder = WebApplication.CreateBuilder(args);

// PHASE 1 - registration. Nothing is constructed; you are describing a plan.
builder.Configuration.AddJsonFile("appsettings.json");
builder.Services.AddDbContext<AppDb>(o => o.UseSqlServer(cs));
builder.Services.AddScoped<IOrderQueries, OrderQueries>();
builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();      // ── the container is BUILT here. Registration
                                //    after this point has no effect.

// PHASE 2 - the pipeline. Also just description, executed per request later.
app.UseExceptionHandler("/error");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();           // ── start the server, start hosted services,
                                //    block until shutdown is signalled
```

The two-phase shape explains a class of confusing bugs: **anything that reads configuration or resolves a service during phase 1 sees an incomplete world.** Registration order does not matter for services (the container resolves lazily), but it matters absolutely for middleware, which is composed in the order you call it.

``` text
// Startup order, in full:
//   1. host builder configured; configuration providers layered
//   2. Build(): service descriptors frozen into an IServiceProvider
//   3. middleware pipeline composed into a single nested delegate
//   4. IHostedService.StartAsync called, in registration order
//   5. Kestrel binds and begins accepting
//   6. ... requests ...
//   7. SIGTERM -> StopAsync on hosted services, in REVERSE order
//   8. root provider disposed, disposing every singleton it created
```

> **\[Tip\]** **Turn on validation in development.** `ValidateOnBuild` catches missing registrations at startup rather than on the first request that needs them, and `ValidateScopes` catches captive dependencies ([Service lifetimes and the captive dependency](04-03-composition-and-dependency-injection.md#be-a58)). Both are on by default in the Development environment and both are worth enabling everywhere.
>
> ``` csharp
> builder.Host.UseDefaultServiceProvider((ctx, o) => {
>     o.ValidateScopes = true; o.ValidateOnBuild = true;
> });
> ```

<a id="be-m231"></a>

#### Middleware: the pipeline as nested delegates

The pipeline is not a list checked per request. At startup, each registration wraps the next into a single composed delegate; a request calls that one delegate, and everything else is nesting.

``` csharp
app.Use(async (ctx, next) => {
    var sw = Stopwatch.StartNew();
    await next(ctx);                    // everything downstream happens HERE
    _log.LogInformation("{Path} {Status} in {Ms}ms",
        ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
});
```

``` csharp
Request  ──► Exception handler ──► HTTPS ──► Static files ──► Routing
                                                                  │
                                              Auth ──► Authz ──► Endpoint
                                                                  │
Response ◄── (each middleware resumes AFTER its await next) ◄──────┘

// The code after 'await next' runs on the way OUT, in reverse order.
// That is why response headers must be set BEFORE next: by the time you
// resume, the response may already have started (§299).
```

**Order is the contract.** Authentication before authorization. Exception handling first, so it wraps everything. Routing before anything that needs to know the endpoint. Response compression before whatever writes the body. Getting this wrong produces behaviour that looks like a framework bug.

``` csharp
// Class-based middleware is instantiated ONCE, at startup - it is effectively
// a singleton. Constructor-inject only singletons; take scoped services as
// parameters of InvokeAsync, which the framework resolves per request:
public class TenantMiddleware(RequestDelegate next) {
    public async Task InvokeAsync(HttpContext ctx, ITenantResolver resolver) {
        ctx.Items["TenantId"] = await resolver.ResolveAsync(ctx);
        await next(ctx);
    }
}
// Injecting a scoped service into the CONSTRUCTOR here is the captive
// dependency of §230, in its most common disguise.
```

> **\[Trap\]**
>
> **Short-circuiting means not calling `next`.** That is correct and intended — a rate limiter or auth failure should stop the pipeline. What is not correct is writing to the response *and* calling `next`: once the response has started, downstream middleware that tries to set a status or a header throws `InvalidOperationException`. Check `ctx.Response.HasStarted` before touching the response late in the pipeline.

Goes further: [Auth, rate limiting, and output caching](08-03-a-modern-net-backend.md#be-m243)

<a id="be-m235"></a>

#### Shutdown and graceful drain

Deployments, autoscaling, and node maintenance all terminate processes routinely. What happens between the signal and the exit determines whether users see errors.

``` csharp
// SIGTERM (or Ctrl+C) triggers:
//   1. IHostApplicationLifetime.ApplicationStopping fires
//   2. Kestrel stops ACCEPTING new connections; in-flight requests continue
//   3. IHostedService.StopAsync on each service, in REVERSE registration order
//   4. after HostOptions.ShutdownTimeout, remaining work is abandoned
//   5. the root provider is disposed - every singleton IDisposable disposed
//   6. process exits

builder.Services.Configure<HostOptions>(o => {
    o.ShutdownTimeout = TimeSpan.FromSeconds(30);   // must exceed your longest request
});
```

``` csharp
public class Worker(IHostApplicationLifetime lifetime) : IHostedService {
    public Task StartAsync(CancellationToken ct) {
        lifetime.ApplicationStopping.Register(() => _log.LogInformation("Draining"));
        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken ct) {
        // ct is already the shutdown deadline - finish the current unit of
        // work and stop. Do not start anything new.
        await _channel.Writer.CompleteAsync();       // let consumers drain
        await _drained.WaitAsync(ct);
    }
}
```

> **\[Trap\]**
>
> **The orchestrator's grace period must exceed the host's shutdown timeout, which must exceed your longest request.** Kubernetes sends SIGTERM and then SIGKILL after `terminationGracePeriodSeconds` (30 by default); if your shutdown timeout is longer, the process is killed mid-drain and in-flight work is lost regardless of how careful the code is. These three numbers must be set as a set, not independently.

> **\[Tip\]**
>
> **Also remove yourself from the load balancer first.** Between SIGTERM and the LB noticing, new requests still arrive at a server that has stopped accepting them. A readiness probe that starts failing on `ApplicationStopping`, plus a short sleep before the server actually stops accepting, closes that window — and it is the difference between a silent deploy and a burst of 502s on every release.

Applied in: [Health checks and readiness](11-06-observability.md#be-e543)
