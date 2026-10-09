---
id: "12-1"
number: "12.1"
title: "Monoliths, modules and services"
part: 12
part_title: "Systems and architecture"
words: 17478
minutes: 76
---

# 12.1 Monoliths, modules and services

> **Quick look.** The real architectural decision is where boundaries go and how firmly they're kept, not how many services you run.
>
> - Monoliths are simple but decay into a big ball of mud without discipline.
> - A modular monolith keeps one deployment with enforced module boundaries and exclusive data ownership; it is often the destination, not a waypoint.
> - Microservices buy independent deployment, scaling and failure isolation, at the cost of networks, partial failure and eventual consistency.
> - Done badly you get a distributed monolith: all of the cost, none of the independence.
> - Draw boundaries around bounded contexts; each service owns its data and nobody else reads its tables.
> - Synchronous chains multiply latency and divide availability (three services at 99.9% give 99.7%).
> - Dependencies point inward to the domain (layered, clean, hexagonal); vertical slices organize by feature.
> - Change shape gradually with the strangler fig and anti-corruption layers; never a big-bang rewrite.


### The big picture

Every large system has to be divided somehow, because no one person can hold all of it in their head. For a decade, the industry's favourite answer was microservices: many small services, each deployed on its own. Many organizations then learned that splitting a system across a network brings every problem from Part 6, and more. They ended up with a "distributed monolith", with all the costs of both styles and the benefits of neither. The real question was never how many deployable pieces to have. It is where the boundaries go, and how well they are kept.

This chapter is about that question. It compares the three main shapes: a single monolith, a modular monolith with firm internal boundaries, and separately deployed services. It shows how to find good boundaries with ideas from domain-driven design, and why each part must own its own data. It covers how organizations' team structures shape their systems, the patterns for crossing boundaries and connecting to legacy systems, and how to move a system from one shape to another gradually. The example is an architecture checker. It enforces the rules that keep modules modular, and catches three violations in a small airline system.

### Before you start

**Builds on:** 7.4 Transactions and isolation, 11.3 Containers and Docker, 8.2 API design, 11.6 Observability, 4.5 Design principles that hold up, 8.1 What a backend does.

**Ideas explained later:**

- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Partial failure** (12.2): Some parts of a distributed system failing while others keep running.
- **Performance** (12.4): How quickly a system does its work at a given load.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a modular monolith?**

> A single deployable application divided internally into modules with explicit public APIs, their own data, and enforced boundaries. It keeps a monolith's simple deployment and transactions while getting most of the structure that services promise.

**2. What is a distributed monolith?**

> A system split into services that still can't change or deploy independently: they share a database, call each other synchronously for everything, or must be released together. It has the costs of distribution without the benefits.

**3. What is a bounded context?**

> A part of the domain within which each term has one precise meaning and model. "Booking" means something different to reservations, to loyalty and to revenue accounting; each is a separate context with its own model.

**4. Why should each service or module own its data exclusively?**

> Shared tables couple everything that touches them: nobody can change a schema without coordinating with every user of it. Exclusive ownership means other parts go through the owner's API or events, and the owner can change its storage freely.

**5. What does Conway's law say?**

> That organizations design systems that mirror their own communication structures. Team boundaries tend to become system boundaries, so the two should be designed together.

**6. What is the strangler fig pattern?**

> Replacing a legacy system gradually: new functionality is built alongside it, and traffic is routed piece by piece from the old system to the new, until the old one can be switched off.

**7. What does an anti-corruption layer protect?**

> Your model from another system's model. It translates between them at the boundary, so an old reservation system's formats and concepts don't leak into your code.

### Three shapes

A **monolith** is a system built and deployed as one unit: one codebase, one build, one process type, usually one database. That is not an insult. Monoliths are simple to develop, test, deploy and debug. Calls between parts are fast in-process method calls. A single database transaction can cover any change, as Transactions and isolation (7.4) showed. Refactoring across the whole system is one pull request. Many very large businesses run on monoliths.

The trouble is what monoliths tend to become without discipline: a big ball of mud, where every part reaches into every other part and every table is used from everywhere. Changes ripple unpredictably, builds and tests slow down, teams tread on each other, and the whole thing can only be scaled and released as a unit.

A **modular monolith** keeps the single deployment but divides the inside into modules: Bookings, Pricing, Inventory, Loyalty. Each has a small public API that others may call, internals nobody else may touch, and its own tables. The boundaries are enforced by the build, not just by good intentions, as the example shows. It keeps the monolith's simplicity while gaining most of the structure that services promise, and well-bounded modules can be extracted into services later, when there is a reason.

**Microservices** split a system into services that are deployed independently, each owning its data and running as its own process, usually in its own containers from Containers and Docker (11.3). Services talk over the network, through APIs from API design (8.2) or messages from Messaging and events (12.3).

### What services buy, and what they cost

Separate services buy real things. Each can be deployed on its own schedule, so teams don't wait for each other. Each can be scaled on its own: the search service may need twenty instances while refunds need two. Failures can be contained: a crash in recommendations need not take down check-in. And different services can use different technologies where that genuinely helps.

The price is high, and it is paid every day. Every call between services crosses a network, with latency, timeouts and partial failures, the problems of Distributed systems basics (12.2). There are no transactions across services, so consistency needs the outbox, events and sagas from Messaging and events (12.3). Debugging needs the distributed tracing from Observability (11.6). Every service needs its own pipeline, monitoring, security configuration, on-call ownership and API versioning. And a change that spans three services becomes three coordinated releases.

The worst outcome is the **distributed monolith**: services that share a database, call each other synchronously for everything, or can only be released together. It has all the costs of distribution and none of the independence. It usually comes from splitting along technical layers or along the wrong domain lines.

A reasonable default for most teams is a well-structured modular monolith, splitting out a service only for a concrete reason. That might be a part that must scale very differently, or has very different reliability or security needs. It might change on a very different schedule, or belong to a separate team that needs to release independently.

### Finding the boundaries

Good boundaries follow the business, not the technology. **Domain-driven design** (DDD), from Eric Evans's 2003 book of that name, gives the vocabulary.

Its central idea is the **bounded context**: a part of the domain within which every term has one precise meaning and one model. "Booking" means a reservation of seats to the reservations team, a source of points to the loyalty team, and a revenue item to finance. Forcing all three into one shared `Booking` class creates a model that serves nobody well and couples everyone to everyone. Instead, each context has its own model, and they exchange only what they need at their boundaries. Within a context, the team and the business use the same words for the same things, the ubiquitous language, and the code uses them too.

Boundaries drawn this way have high cohesion and low coupling, the principles from Design principles that hold up (4.5): things that change together live together, and each part depends on few others through narrow interfaces.

The most important rule is data ownership. Each module or service owns its tables exclusively, a rule often called **database per service**. Other parts never read or write those tables directly. They call the owner's API or react to its events. Shared tables are the strongest coupling there is: a column can't be renamed without finding and changing every piece of code, in every part of the system, that uses it. In a modular monolith the tables can live in one database, in separate schemas, as long as each module only touches its own.

### Teams and systems

In 1967 Melvin Conway observed that organizations produce designs that copy their own communication structures. **Conway's law** holds up remarkably well: if three teams build a compiler, it tends to have three phases. System boundaries drift towards team boundaries, and a boundary that cuts across a team, or a team that spans too many boundaries, tends to be worn away.

The practical lesson is to design teams and architecture together. Give each team clear ownership of one or a few bounded contexts, end to end. Some organizations deliberately shape teams to get the architecture they want, sometimes called the inverse Conway manoeuvre.

### Crossing boundaries

Parts of a system still need each other. The two basic styles, from What a backend does (8.1), apply between modules as well as services. A synchronous call through a public API is simple and couples availability: if pricing is down, booking waits. An asynchronous event, such as "booking created", decouples them in time: loyalty reacts when it can, at the cost of eventual consistency, as Messaging and events (12.3) describes.

When integrating with a system whose model you don't control, such as an older reservation system, a partner's API or a payment provider, put an **anti-corruption layer** at the boundary. It is a set of adapters, from Composition and dependency injection (4.3), that translate between the other system's concepts and formats and your own. Its quirks then stay in one place instead of spreading through your code.

At the edge of a service-based system, an **API gateway** gives clients one entry point. It routes requests to the right services and handles cross-cutting jobs such as authentication, rate limiting and TLS. Examples include Azure API Management and YARP, a reverse proxy library for .NET. A backend for frontend, from A Node and TypeScript backend (8.4), is a gateway shaped for one client. Between services, a **service mesh**, such as Istio or Linkerd, runs a small proxy next to each service. The proxies add mutual TLS, retries, timeouts and telemetry to every call without changes to the services' code, at the cost of more infrastructure to run.

### Seeing it for real

Boundaries that are only documented are soon broken. This script checks three rules for a small airline system: modules may use only each other's public APIs, every table has one owning module, and there are no dependency cycles between modules. It finds cycles with depth-first search, from Graphs (3.6).

```typescript
// modules.ts: run with: node modules.ts   (checks the boundaries of a modular monolith)
type Module = { publicApi: string[]; uses: string[]; tables: string[] };

// What each module offers, what it calls ("Module.Type"), and which tables its code touches
const system: Record<string, Module> = {
  Bookings: { publicApi: ["BookingsApi"], uses: ["Pricing.PricingApi", "Inventory.SeatsApi"], tables: ["bookings", "passengers"] },
  Pricing:  { publicApi: ["PricingApi"], uses: ["Inventory.SeatRepository"], tables: ["fares"] },
  Inventory:{ publicApi: ["SeatsApi"], uses: ["Loyalty.LoyaltyApi"], tables: ["seats", "flights"] },
  Loyalty:  { publicApi: ["LoyaltyApi"], uses: ["Bookings.BookingsApi"], tables: ["members", "bookings"] },
};

const problems: string[] = [];

// Rule 1: modules may only use each other's public API
for (const [name, m] of Object.entries(system))
  for (const ref of m.uses) {
    const [target, type] = ref.split(".") as [string, string];
    if (!system[target]!.publicApi.includes(type)) problems.push(`${name} uses ${ref}, which is internal to ${target}`);
  }

// Rule 2: every table has exactly one owning module
const owners = new Map<string, string[]>();
for (const [name, m] of Object.entries(system)) for (const t of m.tables) owners.set(t, [...(owners.get(t) ?? []), name]);
for (const [table, who] of owners) if (who.length > 1) problems.push(`table ${table} is used directly by ${who.join(" and ")}`);

// Rule 3: no dependency cycles between modules (depth-first search, as in 3.6)
const edges = Object.fromEntries(Object.entries(system).map(([n, m]) => [n, [...new Set(m.uses.map(r => r.split(".")[0]!))]]));
const state = new Map<string, "visiting" | "done">();
function visit(node: string, path: string[]): void {
  if (state.get(node) === "done") return;
  if (state.get(node) === "visiting") { problems.push(`cycle: ${[...path.slice(path.indexOf(node)), node].join(" -> ")}`); return; }
  state.set(node, "visiting");
  for (const next of edges[node]!) visit(next, [...path, node]);
  state.set(node, "done");
}
for (const node of Object.keys(system)) visit(node, []);

console.log(problems.length ? problems.map(p => `violation: ${p}`).join("\n") : "boundaries hold");
```

Its output:

```text
violation: Pricing uses Inventory.SeatRepository, which is internal to Inventory
violation: table bookings is used directly by Bookings and Loyalty
violation: cycle: Bookings -> Pricing -> Inventory -> Loyalty -> Bookings
```

Each violation is a real kind of decay. Pricing reaches into Inventory's internals, so Inventory can no longer change its repository freely. Loyalty reads the bookings table directly, so the Bookings team can't change its schema without breaking Loyalty. And the cycle means none of the four modules can be understood, tested or extracted on its own. The fixes are to call `Inventory.SeatsApi` instead, to have Loyalty keep its own data and learn about bookings through events, and to stop Inventory calling Loyalty. With them applied, the checker reports that the boundaries hold.

In .NET, the build can enforce the same rules. Put each module in its own projects, make internals `internal` so other projects can't see them, and expose only a small public API project. Then write **architecture tests** with libraries such as NetArchTest or ArchUnitNET, which fail the build when code breaks a dependency rule, just as the script does.

### Changing shape gradually

Rewriting a large system from scratch is famously risky: the old system keeps changing while the new one is built, and the switch-over is a single, enormous bet. The **strangler fig pattern**, named by Martin Fowler after vines that grow around a tree until they replace it, does it gradually instead. A routing layer, such as a gateway or reverse proxy, sits in front of the old system. New features are built in the new system. Existing features are moved one at a time, with routing switched over for each, and a way back if something goes wrong. The old system shrinks until it can be switched off.

Inside a codebase, the same idea extracts a module. Define an interface for the capability, route all callers through it, build the new implementation behind the interface, switch over and remove the old one. This is sometimes called branch by abstraction. Extracting a service from a modular monolith follows the same steps, and is much easier when the module already owns its data and has a narrow public API.

### In short

The key architectural decision is where boundaries go and how firmly they are kept, not how many deployable units there are. Monoliths are simple, but decay into a big ball of mud without discipline. Modular monoliths keep one deployment with enforced module boundaries, public APIs and exclusive data ownership. Microservices buy independent deployment, scaling and failure isolation at the cost of networks, partial failure, eventual consistency and operational overhead, and done badly they become distributed monoliths. Draw boundaries around bounded contexts, give each part exclusive ownership of its data, and align teams with boundaries, as Conway's law predicts. Translate foreign models at anti-corruption layers, and change shape gradually with the strangler fig.

### What goes wrong

**Microservices first.** A small team splits a young product into many services and spends its time on infrastructure and coordination. Start with a modular monolith and split for concrete reasons.

**Shared databases.** Several services or modules read and write the same tables, so none can change independently. Give each exclusive ownership of its data.

**Splitting by technical layer.** Separate "API", "logic" and "data" services must all change for every feature. Split by business capability.

**Chatty synchronous chains.** One request calls five services in sequence, multiplying latency and failure. Reshape boundaries, or use events and local copies of data.

**One shared model.** A single `Booking` class tries to serve every context and satisfies none. Give each bounded context its own model.

**Unenforced boundaries.** Rules written in a wiki are broken within months. Enforce them with project structure, `internal` visibility and architecture tests.

**The big-bang rewrite.** A two-year replacement project never catches up with the old system. Replace gradually with the strangler fig.

### Cost model

An in-process call between modules costs nanoseconds; the same call between services costs a network round trip, around a millisecond within a data centre, plus serialization, and it adds a new way to fail. A request that fans out through five sequential service calls pays that five times, and its availability is the product of theirs, as Reliability (12.5) shows. Each service adds a pipeline, dashboards, alerts, security settings and on-call duties, which is why the operational cost grows with the number of services faster than the code does. Enforcing module boundaries costs a few architecture tests; ignoring them costs slower changes every week after.

### In other languages (optional)

**Java.** Spring Modulith supports modular monoliths with verified module boundaries and in-process events; ArchUnit, from which ArchUnitNET is derived, writes architecture tests.

**TypeScript.** Monorepo tools such as Nx enforce module boundaries with lint rules that restrict which libraries may import which.

**Every stack.** Boundaries, data ownership and Conway's law are independent of language, which is why services in different languages can still form a well-structured system, and why services in one language can still form a distributed monolith.

### Key terms

- **Monolith**: A system built and deployed as one unit.
- **Modular monolith**: A single deployable system divided into modules with public APIs, private internals and their own data.
- **Microservices**: An architecture of independently deployed services, each owning its data and communicating over the network.
- **Distributed monolith**: Services that still can't change or deploy independently, combining the costs of both styles.
- **Domain-driven design**: An approach to software design built around the business domain and its language. Often shortened to DDD.
- **Bounded context**: A part of the domain within which each term has one precise meaning and model.
- **Database per service**: The rule that each service or module owns its data and nothing else touches it directly.
- **Conway's law**: Organizations design systems that mirror their own communication structures.
- **Anti-corruption layer**: Adapters at a boundary that translate another system's model into your own.
- **API gateway**: A single entry point that routes client requests to services and handles authentication, limits and TLS.
- **Service mesh**: Proxies beside each service that add encryption, retries and telemetry to calls between services.
- **Architecture test**: An automated test that fails when code breaks a structural rule, such as a forbidden dependency.
- **Strangler fig pattern**: Replacing a system gradually by routing features one by one from the old system to the new.

### Where this leads

The problems that appear as soon as parts talk over a network are in Distributed systems basics (12.2). Connecting boundaries with events, and keeping data consistent across them, is in Messaging and events (12.3). Scaling the parts that need it is in Scaling and performance (12.4), and containing failures between them is in Reliability (12.5).

### Check yourself

1. Add a fifth module, Payments, to `modules.ts` that Bookings uses and that uses nothing, and check the boundaries still hold. Then give it a call back to Bookings and read the cycle report.
2. Extend the checker with a rule that each module may use at most two other modules, and decide which module in the example would break it first.
3. List the bounded contexts in an airline's booking process, from search to check-in, and write what "booking" means in each.
4. Write an architecture test with NetArchTest or ArchUnitNET for a .NET solution you know, forbidding one project from referencing another.
5. Pick one feature of a legacy system you know and plan how to move it with the strangler fig pattern: where the routing layer sits, how traffic moves, and how to go back.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Module boundaries enforced by the build

A modular monolith's Loyalty module keeps reaching into Bookings' internal classes, and the boundary erodes.

*An architecture test*

```csharp
// NetArchTest.Rules
[Fact]
public void Loyalty_only_uses_Bookings_contracts()
{
    var result = Types.InAssembly(typeof(LoyaltyModule).Assembly)
        .ShouldNot().HaveDependencyOnAny("Bookings.Domain", "Bookings.Infrastructure")
        .GetResult();
    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
// Loyalty may reference Bookings.Contracts (events, public DTOs) and nothing else
```

> **Lesson:** Boundaries kept by review erode; boundaries checked by the build survive.

#### Example 2: Strangling a legacy endpoint

A .NET Framework monolith serves /api/fares. The new service is ready for one market first.

*YARP routes one capability at a time*

```json
{
  "ReverseProxy": {
    "Routes": {
      "fares-vn": { "ClusterId": "fares-new", "Match": { "Path": "/api/fares/{**rest}", "QueryParameters": [ { "Name": "market", "Values": [ "VN" ] } ] } },
      "everything-else": { "ClusterId": "legacy", "Match": { "Path": "{**catch-all}" } }
    },
    "Clusters": {
      "fares-new": { "Destinations": { "d1": { "Address": "http://fares-svc/" } } },
      "legacy":    { "Destinations": { "d1": { "Address": "http://legacy-iis/" } } }
    }
  }
}
```

> **Lesson:** Put a facade in front, move one capability at a time, and delete old code as traffic leaves it.

### Architecture: Layers, CQRS, Multi-tenancy

<p class="src">From <em>.NET Backend Deep Dive</em> · Architecture: Layers, CQRS, Multi-tenancy</p>

<a id="dn-131"></a>

#### Layering without ceremony

```
API          controllers/endpoints, DTOs, mapping, auth attributes
Application  use cases, orchestration, transactions, validation
Domain       entities, value objects, domain services, business rules
Infrastructure  EF, HTTP clients, message bus, cache, file storage
```

**The dependency rule:** dependencies point inward. Domain references nothing; infrastructure implements interfaces the inner layers define. That's what makes the domain testable without a database.

The failure mode is ceremony: five projects, an interface per class, and a mapper between four nearly identical models — for a CRUD service with three endpoints. Layers should be introduced when the business logic actually justifies them. A vertical-slice structure (a folder per feature containing its request, handler, validator, and endpoint) is usually easier to navigate than horizontal layers.

<a id="dn-135"></a>

#### When distributed is the wrong answer

Microservices trade in-process calls (nanoseconds, transactional, debuggable with one stack trace) for network calls (milliseconds, partially failing, requiring distributed tracing, retries, idempotency and sagas). That's worth it for **independent deployment and team autonomy at scale** — not for "clean code."

A modular monolith with clear internal boundaries gets most of the organizational benefit at a fraction of the operational cost, and can be split later along boundaries that have already proven themselves.

---

### Legacy & Migration Patterns

<p class="src">From <em>.NET Backend Deep Dive</em> · Legacy & Migration Patterns</p>

<a id="dn-194"></a>

#### Strangler fig

Most engineering happens on systems that already exist. The rewrite that replaces everything at once is the most reliably failed project in the industry — it competes with a moving target while delivering nothing until the end.

Put a facade in front of the legacy system and route capability by capability to the new one, retiring the old code as it's replaced.

```
        ┌── /orders/*    -> NEW service
Gateway ┼── /customers/* -> NEW service
        └── /*           -> LEGACY monolith
```

Each slice ships independently, is reversible by flipping a route, and delivers value before the whole is done. The two hard parts are **shared data** (below) and organizational patience — the facade will exist for longer than anyone estimates.

<a id="dn-195"></a>

#### Branch by abstraction (inside a codebase)

```csharp
public interface IPricingEngine { Task<Money> CalculateAsync(Cart c); }

// 1. Introduce the abstraction over the existing implementation
// 2. Build the new implementation behind the same interface
// 3. Route with a flag, compare in production (below)
// 4. Flip the default, keep the old path for a rollback window
// 5. Delete the old implementation AND the flag
```

No long-lived branch, no merge hell, every step is shippable.

<a id="dn-196"></a>

#### Verifying a replacement with real traffic

```csharp
var legacy = await _legacy.CalculateAsync(cart);

if (_flags.IsEnabled("shadow-pricing"))
{
    // Fire-and-forget, never let it affect the response
    _ = Task.Run(async () =>
    {
        try
        {
            var candidate = await _new.CalculateAsync(cart);
            if (candidate != legacy)
                _logger.LogWarning("Pricing mismatch {Cart}: {Old} vs {New}",
                    cart.Id, legacy, candidate);
        }
        catch (Exception ex) { _logger.LogError(ex, "Shadow failed"); }
    });
}
return legacy;   // the legacy result is still what ships
```

Shadow (dark) traffic gives you a correctness report from production data before anything depends on the new path. Constraints: the shadow path must be side-effect free (never let it send emails or charge cards), and you must budget the extra load.

<a id="dn-197"></a>

#### Moving the data

This is where migrations actually fail. Sequence that works:

```
1. New system READS from the old database (or a replica). Ship features immediately.
2. Dual write: write to both, old remains the source of truth.
   Compare continuously; reconcile the drift you WILL find.
3. Backfill history in batches, resumable, with a checkpoint.
4. Flip the source of truth. Keep dual writes for a rollback window.
5. Stop writing to the old store. Decommission only after a full retention cycle.
```

**Dual writes are not atomic** (Part 12.6) — the two stores will drift. Accept it, measure it with a continuous reconciliation job, and have a repair path. Pretending they'll stay in sync is how silent data loss happens.

**Backfills must be throttled.** A backfill that saturates the database causes the outage the migration was supposed to avoid. Batch it, sleep between batches, run it off-peak, and make it resumable from a checkpoint rather than restarting.

<a id="dn-198"></a>

#### Working in code you didn't write

- **Characterization tests first.** Before changing legacy behavior, write tests that capture what it *currently* does — bugs included. You cannot refactor safely without a definition of "unchanged."
- **Seams over rewrites.** Extract an interface at a boundary you control rather than restructuring inward.
- **Leave it better, in the file you were already in.** Opportunistic cleanup in the blast radius of your change is sustainable; a drive-by refactor of an unrelated module is a review burden and a risk.
- **Resist the rewrite instinct.** Code that looks terrible often encodes years of edge cases nobody wrote down. That weird conditional is usually a bug report from 2019.

---

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a63"></a>

#### Layered, clean and hexagonal architecture

All of these say the same thing in different vocabularies: *dependencies point inward, toward the domain*. The domain knows nothing about EF Core, HTTP or RabbitMQ; the outer layers depend on interfaces the domain owns.

``` text
       +---------------------------------------------+
       |  Api / Worker  (controllers, consumers, DI)  |   <- entry points
       |   +-------------------------------------+   |
       |   |  Application  (use cases, ports)     |   |   <- orchestration
       |   |    +-----------------------------+   |   |
       |   |    |  Domain (entities, rules)   |   |   |   <- no dependencies
       |   |    +-----------------------------+   |   |
       |   +-------------------------------------+   |
       |  Infrastructure (EF, HTTP, bus, files)      |   <- implements ports
       +---------------------------------------------+
             Infrastructure references Domain, never the reverse
```

Practically: `Domain` has zero NuGet packages. `Application` defines `IOrderRepository`. `Infrastructure` implements it with EF Core. `Api` wires them up in `Program.cs` — the only place that knows every concrete type.

> **\[Note\]**
>
> **Do not over-apply it.** A CRUD service with eight endpoints does not need four projects and a mediator. The cost of clean architecture is indirection; pay it where the business rules are genuinely complex.

``` csharp
// Domain — no NuGet packages, no framework types. Just the business.
namespace Shop.Domain;
public interface IOrderRepository                 // the PORT: domain owns it
{
    Task<Order?> FindAsync(OrderId id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
}
public sealed class Order { /* invariants live here */ }

// Application — orchestrates the domain, still framework-free
namespace Shop.Application;
public sealed class PlaceOrderHandler(IOrderRepository repo, IClock clock)
{
    public async Task<Result<OrderId>> HandleAsync(PlaceOrder cmd,
                                                    CancellationToken ct) { /* ... */ }
}

// Infrastructure — the ADAPTER: knows EF, implements the domain's port
namespace Shop.Infrastructure;
internal sealed class EfOrderRepository(AppDb db) : IOrderRepository { /* ... */ }

// Api — the only project that knows everything exists
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
```

``` text
<!-- the dependency rule, enforced by the compiler -->
<!-- Shop.Domain.csproj:         no ProjectReference at all       -->
<!-- Shop.Application.csproj:    → Domain                          -->
<!-- Shop.Infrastructure.csproj: → Application, Domain             -->
<!-- Shop.Api.csproj:            → Infrastructure, Application, Domain -->
```

> **\[Why it matters\]** The diagram is easy to agree with and easy to violate; project references make it structural. Once `Shop.Domain` has no reference to add EF to, the wrong dependency stops being a code-review argument and becomes a compiler error — which is the only form of architecture that survives staff turnover.

<a id="be-a68"></a>

#### The modular monolith in practice

One deployable, many enforced modules. You get the transactional simplicity and single-process debugging of a monolith, while keeping the boundaries that make a later split possible — and often unnecessary.

``` text
  src/
    Modules/
      Ordering/        Api  Application  Domain  Infrastructure   -> schema: ordering
      Billing/         Api  Application  Domain  Infrastructure   -> schema: billing
      Shipping/        Api  Application  Domain  Infrastructure   -> schema: shipping
    Shared/
      Kernel/          primitives only: Result, Money, DomainEvent
      Contracts/       the ONLY types modules may exchange
    Host/              references every module, wires DI, one Program.cs
```

1.  Each module owns its own schema; no cross-schema foreign keys or queries.
2.  Modules communicate through published contracts or an in-process event bus — never by referencing each other's internals.
3.  Enforce it mechanically: `InternalsVisibleTo` nowhere, plus an architecture test that fails the build on a forbidden reference.
4.  Each module registers itself with one extension method the host calls.
5.  When you extract a module to a service, the in-process bus becomes a real broker and almost nothing else changes.

``` csharp
// architecture tests turn a convention into a build failure
[Fact]
public void Ordering_must_not_reference_Billing_internals()
{
    var result = Types.InAssembly(typeof(OrderingModule).Assembly)
        .Should().NotHaveDependencyOn("Billing.Domain")
        .GetResult();

    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
```

> **\[Why it matters\]** The reason microservices projects fail is almost never the technology — it is that the boundaries were wrong. A modular monolith lets you discover the right boundaries while moving them is still cheap.

###### Enforcing module boundaries inside one solution

``` text
  the shape

    src/
      Modules/
        Orders/     Orders.Public/    <- the ONLY thing other modules reference
                    Orders.Core/      <- internal: domain, handlers, EF model
        Billing/    Billing.Public/
                    Billing.Core/
      Host/         references every Public and Core (composition only)

  rules
    Orders.Core -> Billing.Public      OK
    Orders.Core -> Billing.Core        FORBIDDEN
    Host        -> everything          it is the composition root

  the Public project holds contracts and integration events. Nothing else.
  once it starts holding entities, the boundary has already dissolved.
```

``` csharp
[Fact]
public void Modules_do_not_reference_each_other_internally()
{
    var modules = new[] { "Orders", "Billing", "Shipping" };
    foreach (var m in modules)
    {
        var others = modules.Where(x => x != m).Select(x => $"Acme.{x}.Core").ToArray();
        var result = Types.InAssembly(Assembly.Load($"Acme.{m}.Core"))
            .ShouldNot().HaveDependencyOnAny(others)
            .GetResult();
        Assert.True(result.IsSuccessful, $"{m} reaches into another module");
    }
}
```

``` sql
-- give each module its own SCHEMA in one database. This is what makes a
-- later extraction feasible rather than theoretical.
CREATE SCHEMA orders;
CREATE SCHEMA billing;

-- no foreign keys ACROSS schemas. Reference by id, exactly as you would
-- across services. If you cannot live without the FK, the boundary is wrong.
```

| Communication between modules      | When                                                     |
|------------------------------------|----------------------------------------------------------|
| A call on a `*.Public` interface   | Synchronous, needs a result, same transaction acceptable |
| An in-process notification         | Fire and forget within the same transaction              |
| An integration event via an outbox | Cross-module effects that must survive a crash           |
| A shared table                     | **Never.** This is the boundary dissolving               |

> **\[Note\]**
>
> **This is a destination, not a waypoint.** A modular monolith with enforced boundaries gives you most of what microservices offer — clear ownership, independent reasoning, the option to extract — without distributed transactions, network failure modes, or N pipelines. Extract a module only when it has a *specific* need: independent scaling, a different technology, or another team's release cadence. Until then, staying is the better engineering decision rather than the lazier one.

<a id="be-a71"></a>

#### Vertical slice architecture

Instead of organising by technical layer, organise by feature. Everything a use case needs — request, handler, validator, response, even the SQL — lives in one file or one folder.

``` text
  layered                          vertical slices

  Controllers/                     Features/
    OrdersController.cs              Orders/
  Services/                            PlaceOrder.cs      <- request+handler+validator+response
    OrderService.cs                    CancelOrder.cs
  Repositories/                        GetOrderSummary.cs
    OrderRepository.cs               Invoices/
  Models/                              IssueInvoice.cs
    Order.cs

  a change touches 4 folders        a change touches 1 file
```

``` csharp
public static class PlaceOrder
{
    public record Command(Guid CustomerId, IReadOnlyList<Line> Lines) : IRequest<Guid>;

    public class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(x => x.Lines).NotEmpty();
    }

    public class Handler(AppDb db, IPublisher bus) : IRequestHandler<Command, Guid>
    {
        public async Task<Guid> Handle(Command c, CancellationToken ct)
        {
            var order = Order.Place(c.CustomerId, c.Lines);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            await bus.Publish(new OrderPlaced(order.Id), ct);
            return order.Id;
        }
    }
}
```

**Slices are better at**

- Change locality — one feature, one place
- Letting each feature choose its own approach
- Onboarding: a new dev reads one file
- Deleting a feature cleanly

**Layers are better at**

- Enforcing a single shared domain model
- Preventing duplicated business rules
- Teams that need strong uniformity
- Deep domains where the model is the point

They combine well: vertical slices for the application layer, a shared domain project underneath for the rules that must not be duplicated. Reads can be blunt (project straight to a DTO with Dapper); writes go through the domain model.

###### What it looks like, and the tension with layers

``` text
  LAYERED                          VERTICAL SLICE

  Controllers/                     Features/
    OrdersController.cs              Orders/
  Services/                            CreateOrder/
    OrderService.cs                      Command.cs
  Repositories/                          Handler.cs
    OrderRepository.cs                   Validator.cs
  Models/                                Endpoint.cs
    Order.cs                           GetOrderById/
  DTOs/                                  Query.cs
    OrderDto.cs                          Handler.cs

  changing "create an order"       changing it touches ONE FOLDER
  touches five files in five
  folders

  the trade
    + change is local, and the blast radius is visible
    + each slice picks its own approach: this one needs a repository,
      that one is a three-line Dapper query. No forced uniformity.
    + deleting a feature means deleting a folder
    - deliberate duplication between slices
    - no single place enforcing a cross-cutting rule
    - "where does shared logic go?" recurs
```

``` csharp
public static class CreateOrder
{
    public sealed record Command(Guid CustomerId, IReadOnlyList<LineDto> Lines)
        : IRequest<Result<Guid>>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(x => x.Lines).NotEmpty();
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command c, CancellationToken ct)
        {
            var order = Order.Create(new CustomerId(c.CustomerId));
            foreach (var l in c.Lines) order.AddLine(l.ProductId, l.Qty, l.Price);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            return order.Id;
        }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", async (Command c, ISender s) =>
            (await s.Send(c)).ToHttpResult()).WithTags("Orders");
}
// note `internal` on the handler and validator: nothing outside the slice can
// reach them. That is the enforcement which makes the pattern hold.
```

> **\[Note\]**
>
> **They are not opposed.** The productive combination is vertical slices for the *application* layer — one folder per use case holding its command, handler, validator and endpoint — over a shared *domain* layer holding the rules. Slices stay independent; the invariants stay in one place. Choosing one exclusively is usually a false choice.

### Where .NET Is Heading

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Where .NET Is Heading</p>

<p class="pd">What has actually changed in the .NET ecosystem recently, what the community has converged on, and where to point your learning so it is still relevant in three years.</p>

<a id="be-a229"></a>

#### The modular monolith comeback

The industry has visibly cooled on microservices-by-default. Enough teams have now experienced the distributed monolith — all of the operational cost, none of the independence — that the mainstream advice has shifted to starting with a well-structured single deployable.

``` text
  2015-2020   "microservices or you are behind"
  2021-2023   "microservices, but we regret the boundaries"
  2024-2026   "modular monolith first; extract when a boundary proves itself"
```

This is not a retreat from the underlying ideas. Bounded contexts, explicit contracts, event-driven integration, independent data ownership and per-module observability are all still the right practices. What changed is the recognition that you can have all of them inside one process, and that the process boundary should be introduced when it solves a specific problem — independent scaling, independent deployment cadence, team autonomy — rather than by default.

> **\[Why it matters\]** For a legacy modernisation this is genuinely liberating. The target does not have to be twenty microservices. A clean modular .NET 9 monolith with real boundaries, good telemetry and a deployment pipeline is an enormous improvement and a far more achievable goal — and it leaves every extraction option open.

###### Why the pendulum swung, and what was learned

``` text
  the arc, roughly 2014-2026

  2014  microservices as the answer to monolith pain
        -> real problems: coupled releases, one huge codebase, no ownership

  2016-2020  widespread adoption, frequently premature
        -> teams with ONE team and EIGHT services
        -> distributed transactions, saga complexity, N pipelines,
           debugging across five sets of logs, local dev impossible
        -> having paid every technical cost for organisational benefits
           they did not need

  2020-2026  correction
        -> "you must be this tall": several teams, independent cadences
        -> modular monolith as the honest default
        -> and the realisation that the VALUABLE part was never the
           network boundary - it was the MODULE boundary. You can have
           that in one process.

  what was actually learned
    boundaries are the hard part, and you guess them badly UP FRONT
    a network call does not enforce a boundary; DISCIPLINE does
    you can always split later; merging back is much harder
    the operational cost of N services is permanent, not one-off
```

| Microservices bought   | Which you can also get from                                |
|------------------------|------------------------------------------------------------|
| Clear ownership        | Module boundaries with codeowners                          |
| Independent reasoning  | Enforced internal boundaries                               |
| Technology choice      | Rarely exercised in practice                               |
| Independent scaling    | **Only microservices** — a genuine reason                  |
| Independent deployment | **Only microservices** — the other genuine reason          |
| Fault isolation        | Partly — bulkheads and circuit breakers within one process |

The two rows in bold are the entire honest case. If neither independent scaling nor independent deployment is a stated need, the network boundary is buying you nothing that a module boundary does not, and it is charging you distributed transactions, distributed debugging and N pipelines for the privilege. That is the whole of the correction, and it is why .NET tooling — Aspire included — increasingly assumes a small number of well-structured services rather than dozens.

### Microservices

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Microservices</p>

<p class="pd">Splitting a system into independently deployable services — when it helps, what it costs, and how to get there from a monolith without a two-year rewrite that never ships.</p>

<a id="be-a165"></a>

#### Monolith or microservices: the honest trade

Microservices buy you independent deployment, independent scaling, technology freedom and fault isolation. They cost you distributed transactions, network failure modes, eventual consistency, harder debugging, and a platform team you may not have.

**A modular monolith is right when**

- One team, or a few closely aligned teams
- The domain is still being discovered
- Deployment is not the bottleneck
- You want refactoring to stay cheap

**Microservices are right when**

- Teams block each other on releases
- Parts of the system scale very differently
- Different components need different tech or SLAs
- You already have CI/CD, observability and on-call

The default recommendation from most practitioners today: **start with a monolith — see Terminology"\>modular monolith** with strict internal boundaries, and extract services only when a specific boundary proves painful. A well-modularised monolith can become microservices; a distributed big ball of mud cannot become anything.

> **\[Trap\]**
>
> **Common trap.** The **distributed monolith** is the worst outcome: services that must be deployed together, share a database, and call each other synchronously in long chains. You paid every microservices cost and got no benefit. The tell is ‘we need to release A, B and C at the same time’.

###### What you are actually buying, and what it costs

``` text
  the honest accounting

  WHAT MICROSERVICES BUY
    independent DEPLOYMENT       team A ships without team B
    independent SCALING          scale only the part that needs it
    fault isolation              one service down != all down
    technology choice per service
    clear ownership boundaries

  WHAT THEY COST - all of it permanent
    the network                  every in-process call becomes a
                                 potentially-failing, latent, retryable
                                 remote call
    distributed transactions     gone. Sagas and compensation instead.
    distributed debugging        one request across five services and
                                 five sets of logs
    operational surface          N pipelines, N dashboards, N on-call
                                 runbooks, N sets of dependencies
    data consistency             eventual, everywhere, by default
    local development            "run the whole system on a laptop"
                                 becomes a project of its own

  the observation that matters: MOST of the benefits are ORGANISATIONAL
  (independent deployment, ownership) and MOST of the costs are TECHNICAL.
  So the question is whether your organisational problem is big enough to
  justify taking on the technical one.

  with one team and eight services, you have bought nothing and paid
  everything.
```

| Signal you might need them                       | Signal you do not             |
|--------------------------------------------------|-------------------------------|
| Several teams blocked on one release train       | One team                      |
| One component needs 20× the capacity of the rest | Uniform load                  |
| Genuinely different technology requirements      | It is all C# and one database |
| Regulatory isolation of one data domain          | No such requirement           |
| A component with a very different change rate    | Everything changes together   |
| An acquisition with its own stack                | One codebase, one history     |

> **\[Note\]**
>
> **The modular monolith is the honest default.** Enforced module boundaries in one deployable give you ownership, clear interfaces and the *option* to extract, without distributed transactions or N pipelines. Extract a module when it has a specific, statable need — independent scaling, a different technology, another team's cadence. Starting with microservices means committing to the cost before you know where the boundaries actually are, and boundaries are the thing you are worst at guessing up front.

<a id="be-a166"></a>

#### Finding service boundaries

The boundary is not technical (Users service, Data service) and it is not entity-shaped (a service per table). It is a bounded context: a slice of the business with its own language, its own rules, and ideally its own team.

##### Signals you have found a real boundary

- The same word means different things on each side — *Customer* in Sales is a lead, in Billing an account, in Support a ticket owner.
- Change requests arrive from different stakeholders and on different cadences.
- Load profiles differ by an order of magnitude.
- You can describe the interface as a handful of business events, not a hundred CRUD calls.

``` text
  bad: horizontal slices               good: vertical slices

  [ API layer service   ]              [ Ordering ]  [ Billing ]  [ Shipping ]
  [ Business layer svc  ]                UI + API      UI + API     UI + API
  [ Data layer service  ]                logic         logic        logic
  every feature touches all 3            own store     own store    own store
```

A useful heuristic: if implementing a typical feature requires changing more than one service, your boundaries are wrong. Redraw them along the seams where features actually land.

###### Worked example

Event storming, compressed into something you can actually run in an afternoon. This is the most reliable technique for finding boundaries, and it needs a wall and sticky notes rather than a tool.

1.  **Write every domain event** in past tense on orange notes, in rough time order: *OrderPlaced, PaymentAuthorised, StockReserved, InvoiceIssued, ShipmentDispatched, DeliveryConfirmed*. Do not organise yet.
2.  **Add the commands** that cause them on blue notes, and the actor who issues each.
3.  **Mark the pain points** in red: where people wait, where things go wrong, where two departments argue about what a word means.
4.  **Look for clusters** where the same nouns, actors and vocabulary recur. Those are candidate bounded contexts — the boundaries fall in the gaps between clusters, not through the middle of them.
5.  **Test each boundary** with the question: can this cluster make its decisions using only its own data plus events it has already received? If it constantly needs to ask another cluster a synchronous question, the line is in the wrong place.

``` text
  the clusters that emerge, and the vocabulary that identifies them

  SALES            "quote"  "opportunity"  "customer" = a prospect
                   OrderPlaced, QuoteAccepted, DiscountApplied

  FULFILMENT       "pick"  "pack"  "customer" = a delivery address
                   StockReserved, ShipmentDispatched, DeliveryConfirmed

  FINANCE          "ledger"  "receivable"  "customer" = an account with a credit limit
                   InvoiceIssued, PaymentReceived, CreditNoteRaised

  three different meanings of "customer" -> three contexts, each with its own model.
  the integration between them is events, and an anti-corruption layer at each edge.
```

The vocabulary test is the most practical signal in the whole exercise. When the same word means three different things to three groups of people, forcing them into one shared `Customer` class produces a type with forty properties, most of which are null in most contexts — and that class then couples every service that touches it.

###### Boundary smells, and what each one is telling you

| Smell                                                             | Diagnosis                                                                             |
|-------------------------------------------------------------------|---------------------------------------------------------------------------------------|
| Every feature needs changes in three services                     | The boundaries cut across features instead of along them                              |
| Service A calls B, which calls C, synchronously, on every request | These are one service pretending to be three                                          |
| A shared library contains the domain model                        | You have distributed a monolith and added deployment coupling                         |
| Two services always deploy together                               | There is one boundary here, not two                                                   |
| A service has no data of its own                                  | It is a function, not a service — fold it into a caller                               |
| One service is called by everything                               | Either it is genuinely infrastructure, or it is a god service to be split             |
| Cross-service transactions keep being needed                      | The consistency boundary and the service boundary disagree; move the service boundary |

The last one is the most useful diagnostic in the list. A requirement for a distributed transaction is almost always evidence that two things that must be consistent have been placed in different services. Before building a saga, seriously consider merging the two services — sagas are for genuinely independent business steps, not for undoing a modelling mistake.

<a id="be-a167"></a>

#### A database per service

This is the rule that makes microservices real, and the one teams break first. If two services share tables, they cannot be deployed, scaled, or schema-migrated independently — they are one service wearing two processes.

``` text
  shared database (not microservices)      database per service

  svc A --\                                svc A -> [ db A ]
           +--> [ one schema ]              svc B -> [ db B ]
  svc B --/                                       events / APIs between them
  a column rename breaks both               each owns its schema completely
```

The consequence is that joins across service boundaries are gone. Your options, in order of preference: keep a local read-model of the data you need, kept current by events; call the owning service and cache; or build a dedicated read-side projection that subscribes to several services' events.

``` csharp
// Shipping keeps a slim local copy of what it needs about a customer
public class CustomerAddressProjection(ShippingDb db) : IConsumer<CustomerAddressChanged>
{
    public async Task Consume(ConsumeContext<CustomerAddressChanged> ctx)
    {
        var m = ctx.Message;
        await db.CustomerAddresses.Upsert(m.CustomerId, m.Address, m.Version);
        await db.SaveChangesAsync(ctx.CancellationToken);
    }
}
```

> **\[Note\]**
>
> **Eventual consistency is a product decision, not just a technical one.** “The address updates within a few seconds” needs to be acceptable to the business, and the UI has to tell the truth about it. Get that agreement before you split the data.

###### The rule, and the queries it breaks

``` text
  the rule: a service owns its data. No other service touches its tables.

  what it buys
    the service can change its schema without coordination
    the service can choose its own store
    a bad query in one service cannot degrade another
    the boundary is enforced, not merely intended

  what it breaks, immediately
    JOINS across services       -> now an API call per row, or a projection
    reports spanning services   -> a warehouse, or a read model
    foreign keys                -> gone. Referential integrity is your problem.
    "just add a column"         -> if two services need the same fact,
                                   one of them owns it and publishes it
    transactions across services-> sagas

  the intermediate step that is often correct:
    ONE DATABASE, SCHEMA PER SERVICE, no cross-schema foreign keys and
    no cross-schema queries, enforced by GRANTs. You get the boundary
    discipline without the operational multiplication - and extraction
    later is genuinely feasible because the coupling never formed.
```

``` sql
-- enforce the boundary with permissions, not with a convention document
CREATE SCHEMA orders;  CREATE SCHEMA billing;

CREATE ROLE orders_svc;  CREATE ROLE billing_svc;
GRANT USAGE ON SCHEMA orders TO orders_svc;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA orders TO orders_svc;
-- and CRUCIALLY: billing_svc is granted NOTHING on schema orders.
-- the boundary is now enforced by the database, and a developer cannot
-- accidentally cross it - the query simply fails.

-- publish what others need, deliberately
CREATE VIEW orders.v_public_orders AS
SELECT id, tenant_id, status, placed_at FROM orders.orders WHERE deleted_at IS NULL;
GRANT SELECT ON orders.v_public_orders TO reporting_role;
```

| Cross-service need                   | Answer                                                            |
|--------------------------------------|-------------------------------------------------------------------|
| Read one entity from another service | An API call, cached with a short TTL                              |
| Join across services for a list view | A **read model** built from events — do not call per row          |
| Reporting across everything          | A warehouse fed by CDC or events                                  |
| A fact both services need            | One owns it; the other keeps a replicated copy, updated by events |
| Referential integrity                | Eventual, verified by a reconciliation job that reports orphans   |
| A transaction across two services    | A saga with compensation — or move the boundary                   |

The last row deserves emphasis. A frequent need for a transaction spanning two services is strong evidence that the boundary is in the wrong place. Before building a saga, seriously consider merging the two services. Consistency requirements are a boundary-finding tool, not just a constraint to engineer around.

<a id="be-a168"></a>

#### Synchronous or asynchronous communication

Every synchronous call couples your availability to someone else's. Three services at 99.9% chained synchronously give you 99.7% — and that is before timeouts and retries amplify the failure.

| Pattern                      | Coupling                     | Use when                                  |
|------------------------------|------------------------------|-------------------------------------------|
| HTTP / gRPC request-response | Temporal — both must be up   | The caller genuinely needs the answer now |
| Event published to a broker  | Loose — consumer can be down | Something happened others may care about  |
| Command sent to a queue      | Loose, but a known recipient | Do this work, not necessarily right now   |
| Local read-model from events | None at request time         | You need someone's data on your hot path  |

The design move that removes most cascading failures: **on the request path, only talk to your own database.** Everything you need from other services is already replicated locally by events. Synchronous calls are reserved for things that are genuinely someone else's decision to make right now, like a payment authorisation.

``` text
  chained (fragile)                    choreographed (resilient)

  API -> A -> B -> C -> D              API -> A -> [ event ]
  latency adds up                                     |-> B (own pace)
  any failure fails all                               |-> C
  timeout budgets nest                                |-> D
```

###### The availability arithmetic that decides it

``` text
  SYNCHRONOUS CHAINS MULTIPLY FAILURE

    order-api -> customer-api -> credit-api -> external bureau
      99.9%        99.9%          99.9%          99.5%

    combined availability = 0.999 x 0.999 x 0.999 x 0.995 = 99.2%
    -> ~6 hours of downtime a year, caused mostly by OTHER services.

    and LATENCY ADDS: 40ms + 60ms + 200ms = 300ms, every request,
    with the slowest link setting the floor.

  ASYNCHRONOUS DECOUPLES

    order-api -> [queue] -> ... eventually
      99.9%       the queue absorbs downstream failure.
                  order-api's availability is its OWN, plus the broker's.

    the trade: the caller does not know the outcome. You now need a way
    to report success later, and a way to handle failure after the fact.
```

| Use synchronous when                       | Use asynchronous when                        |
|--------------------------------------------|----------------------------------------------|
| The caller **needs the answer** to proceed | The caller only needs it *accepted*          |
| A user is waiting on the result            | The work can complete later                  |
| The operation is a query                   | The operation is a command with side effects |
| Failure should be visible immediately      | Failure should be retried automatically      |
| It is a validation the answer depends on   | It is a notification or a downstream effect  |

``` csharp
// the hybrid that covers most real cases: ACCEPT synchronously,
// PROCESS asynchronously, and give the caller a way to follow up.
[HttpPost("/orders")]
public async Task<IResult> Place(CreateOrder cmd, CancellationToken ct)
{
    // validate synchronously - the caller must know if the request is wrong
    var validation = await validator.ValidateAsync(cmd, ct);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

    // persist + enqueue in ONE transaction (the outbox)
    var id = await orders.AcceptAsync(cmd, ct);

    // 202, with somewhere to look
    return Results.Accepted($"/orders/{id}", new { id, status = "Accepted" });
}
// the caller gets immediate feedback on VALIDITY, and the expensive,
// failure-prone downstream work happens where it can be retried.
```

> **\[Trap\]**
>
> **Common trap.** A synchronous call with no timeout is the mechanism by which one slow service takes down every service above it: threads pile up waiting, the pool exhausts, health checks queue, and the load balancer removes an instance that was merely *waiting*. Every outbound call needs a timeout shorter than its caller's, all the way down the chain — and almost every client library defaults to infinite or to something absurd like 100 seconds.

<a id="be-a169"></a>

#### API gateway and backend-for-frontend

A gateway is the single entry point for external clients. It handles cross-cutting concerns once — TLS, authentication, rate limiting, request logging, routing — so that twenty services do not each implement them differently.

| Concern                  | Gateway                    | Service                              |
|--------------------------|----------------------------|--------------------------------------|
| TLS termination, WAF     | Yes                        | No                                   |
| Token validation         | Yes (and pass claims down) | Re-validate; never trust the network |
| Rate limiting per client | Yes                        | Optional, per-tenant                 |
| Routing and versioning   | Yes                        | No                                   |
| Business logic           | **Never**                  | Yes                                  |

A backend-for-frontend goes further: one gateway per client type. The mobile BFF returns small aggregated payloads tuned for a phone; the web BFF returns richer ones. It stops every service from being distorted by the needs of one UI.

> **\[Trap\]**
>
> **Common trap.** Gateways accumulate logic. Once orchestration, mapping and business rules move in, the gateway becomes a shared component every team must change to ship anything — the coupling you split up to avoid. Keep it dumb, keep it declarative.

###### Two different patterns, frequently conflated

``` text
  API GATEWAY                        BACKEND FOR FRONTEND

  one entry point for everyone       one backend PER CLIENT TYPE

    web ---+                           web  -> web-bff  --+
    mobile-+--> gateway --> services    mobile -> mobile-bff-+--> services
    partner+                           partner -> partner-api-+

  concerns: routing, TLS, rate         concerns: SHAPING the response for
  limiting, auth, one place to         ONE client. Aggregating several calls
  observe traffic                      into one round trip. Client-specific
                                       auth and versioning.

  owned by: the platform team          owned by: THE FRONTEND TEAM
  changes: rarely                      changes: with the client

  they compose. A gateway in front of several BFFs is a normal shape.
  the mistake is one gateway trying to be a BFF for three clients -
  it accumulates client-specific transformation logic in YAML and
  becomes the bottleneck every team waits on.
```

``` csharp
// a BFF's actual job: turn N calls into 1, shaped for this screen
[HttpGet("/mobile/order-summary/{id}")]
public async Task<OrderSummaryDto> Get(Guid id, CancellationToken ct)
{
    // parallel, not sequential - this is the whole point
    var orderTask    = orders.GetAsync(id, ct);
    var shipmentTask = shipping.GetForOrderAsync(id, ct);
    var invoiceTask  = billing.GetInvoiceAsync(id, ct);
    await Task.WhenAll(orderTask, shipmentTask, invoiceTask);

    // and shape it for a MOBILE screen: 6 fields, not 60
    return new OrderSummaryDto(
        orderTask.Result.Id,
        orderTask.Result.Total,
        shipmentTask.Result?.EstimatedArrival,
        invoiceTask.Result?.Status);
}
// the mobile client made ONE request over a high-latency link instead of
// three. That is the entire value proposition, and it is a large one on
// a cellular connection.
```

| Concern                    | Gateway  | BFF                         |
|----------------------------|----------|-----------------------------|
| TLS termination            | **Yes**  | No                          |
| Coarse rate limiting       | **Yes**  | No                          |
| Token validation           | **Yes**  | Reads claims                |
| Business authorisation     | No       | No — belongs in the service |
| Response shaping           | No       | **Yes**                     |
| Aggregating calls          | No       | **Yes**                     |
| Client-specific versioning | No       | **Yes**                     |
| Owned by                   | Platform | The client team             |

The ownership row is the one that determines whether this works. A BFF owned by the frontend team can change as fast as the client does, which is the point. A BFF owned by a backend team becomes a queue that the frontend team waits in — at which point it is a gateway with extra steps, and the pattern has failed.

<a id="be-a170"></a>

#### Consistency without distributed transactions

Two-phase commit across services is not available to you in practice: it needs a coordinator, it holds locks across the network, and one slow participant blocks everyone. The alternative is to accept that the system is consistent *eventually* and design for the window.

1.  Make each step a local ACID transaction inside one service.
2.  Emit an event in that same transaction, via the outbox.
3.  Let the next service react and do its own local transaction.
4.  Define a compensating action for each step that can fail after a later one has committed.
5.  Add a monitor for sagas that have been in flight too long — stuck processes are the real failure mode.

Where you truly cannot tolerate eventual consistency — typically money and stock levels at the moment of commitment — that is strong evidence those two things belong in the same service and the same transaction. Consistency requirements are a boundary-finding tool.

###### Building the outbox, and making consumers idempotent

``` sql
-- the outbox table. Note the partial index: it stays tiny forever,
-- because it only indexes unpublished rows.
CREATE TABLE outbox (
    id            uuid PRIMARY KEY,
    type          text NOT NULL,
    payload       jsonb NOT NULL,
    occurred_at   timestamptz NOT NULL DEFAULT now(),
    published_at  timestamptz,
    attempts      int NOT NULL DEFAULT 0
);
CREATE INDEX ix_outbox_pending ON outbox (occurred_at)
    WHERE published_at IS NULL;
```

``` csharp
// the write side: ONE local transaction. No 2PC, nothing to go wrong.
await using var tx = await db.Database.BeginTransactionAsync(ct);

order.MarkPlaced();
db.Outbox.Add(new OutboxMessage
{
    Id = Guid.CreateVersion7(),
    Type = nameof(OrderPlaced),
    Payload = JsonSerializer.SerializeToDocument(new OrderPlaced(order.Id, order.Total)),
});
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
// either BOTH the state change and the message exist, or NEITHER does.
// that atomicity is the entire point.

// the relay: claim a batch with SKIP LOCKED so several relays can run
var batch = await db.Database.SqlQuery<OutboxMessage>($@"
    SELECT * FROM outbox
    WHERE published_at IS NULL
    ORDER BY occurred_at
    LIMIT 100
    FOR UPDATE SKIP LOCKED").ToListAsync(ct);

foreach (var m in batch)
{
    await bus.PublishAsync(m.Type, m.Payload, ct);
    m.PublishedAt = timeProvider.GetUtcNow();
}
await db.SaveChangesAsync(ct);
// if the relay crashes AFTER publishing and BEFORE marking, the message is
// sent twice. That is at-least-once, and it is why the next bit exists.
```

``` text
  AT-LEAST-ONCE + IDEMPOTENT CONSUMER = the observable effect of exactly-once

  exactly-once DELIVERY is not achievable across a network.
  exactly-once PROCESSING is - by deduplicating at the consumer.

  and the deduplication must be in the SAME TRANSACTION as the work,
  or you have moved the race rather than removed it:

    BEGIN
      INSERT INTO processed (message_id) VALUES (:id) ON CONFLICT DO NOTHING
      -- 0 rows = already handled. ROLLBACK, ack, done.
      UPDATE orders SET status = 'Shipped' WHERE id = :order_id
    COMMIT

  now redelivery is harmless, and the guarantee does not depend on the
  broker's promises at all.
```

| Alternative                  | When                                                                              |
|------------------------------|-----------------------------------------------------------------------------------|
| **Transactional outbox**     | The default. Works with any broker and any database                               |
| **CDC on the outbox table**  | Removes the polling relay entirely — Debezium reads the WAL                       |
| **Listen-to-yourself**       | Publish first, consume your own event to do the work                              |
| **Saga with compensation**   | Multi-step processes across services                                              |
| **2PC / XA**                 | Almost never. Blocking protocol, in-doubt transactions, poor support              |
| **Put them in one database** | **Consider this first.** The requirement may be telling you the boundary is wrong |

<a id="be-a171"></a>

#### Strangler fig: getting out of the monolith

Named after the vine that grows around a tree and eventually replaces it. You put a facade in front of the legacy system, route one capability at a time to new code, and delete the old path once traffic has moved. The monolith keeps running and shrinking; there is no big-bang cutover.

``` text
  phase 1                 phase 2                       phase 3

  client                  client                        client
    |                       |                             |
  [ legacy ]            [ YARP facade ]              [ YARP facade ]
                          /        \                    /        \
                   [ legacy ]  [ Orders svc ]    [ legacy (thin) ] [ 6 services ]
```

1.  **Put the facade in first**, routing 100% to legacy. Prove it changes nothing.
2.  **Pick a low-risk, high-churn capability** — something you keep changing and that few things depend on.
3.  **Extract the data it owns**, usually the hardest part. Dual-write or replicate until the new service owns it.
4.  **Route a percentage of traffic** to the new service; compare results against legacy in shadow mode.
5.  **Flip fully, then delete the legacy code path.** Deleting is the step teams skip, and it is what makes the next extraction cheaper.

> **\[Trap\]**
>
> **Common trap.** Do not start with the hardest, most central domain object to ‘prove the approach’. Start with something that will succeed, so the team learns the mechanics on easy ground.

###### Worked example

A concrete first extraction, with the actual sequence of deployments. The target is a ‘document generation’ capability: high churn, well-defined inputs, few dependents.

``` text
  deploy 1   YARP in front of the legacy app, routing 100% to legacy.
             Nothing else changes. Verify: identical behaviour, latency +1-2 ms.
             This deploy is the risk you take once, not every time.

  deploy 2   New service exists and is deployed, but receives no production traffic.
             YARP mirrors 5% of requests to it (fire and forget, response discarded).
             Compare outputs offline. Fix discrepancies. This can run for weeks.

  deploy 3   Route 5% of real traffic to the new service, by header or user cohort.
             Watch error rate, latency, and business metrics side by side.

  deploy 4   Ramp 5% -> 25% -> 50% -> 100% over days, with an instant rollback
             (a config change, not a deploy).

  deploy 5   DELETE the legacy code path and its tests. This is the step teams skip,
             and skipping it means the next extraction is just as expensive as this one.
```

``` text
// YARP with traffic splitting by header, so rollback is a config change
{
  "ReverseProxy": {
    "Routes": {
      "documents-new": {
        "ClusterId": "documents-svc",
        "Match": { "Path": "/api/documents/{**rest}", "Headers": [
          { "Name": "X-Canary", "Values": ["true"], "Mode": "ExactHeader" } ] }
      },
      "documents-legacy": {
        "ClusterId": "legacy",
        "Match": { "Path": "/api/documents/{**rest}" }
      },
      "everything-else": { "ClusterId": "legacy", "Match": { "Path": "{**catch-all}" } }
    }
  }
}
```

``` csharp
// shadow comparison middleware: run both, serve legacy, report differences
app.Use(async (ctx, next) =>
{
    if (!ShouldShadow(ctx)) { await next(ctx); return; }

    ctx.Request.EnableBuffering();
    var body = await ReadAndRewindAsync(ctx.Request);

    await next(ctx);                                   // legacy serves the real response

    _ = Task.Run(async () =>                           // shadow, off the request path
    {
        try
        {
            var shadow = await _newService.CallAsync(body);
            if (!Compare(shadow, capturedLegacyResponse))
                _log.LogWarning("Shadow mismatch on {Path}: {Diff}", ctx.Request.Path, Diff(...));
        }
        catch (Exception ex) { _log.LogWarning(ex, "Shadow call failed"); }
    });
});
```

> **\[Trap\]**
>
> **Common trap.** Shadow traffic must be side-effect free. Mirroring a `POST /payments` to a new service that actually charges the card is a very expensive way to learn this. Either shadow read-only endpoints, or run the new service against a copy of the database with writes redirected to a no-op.

###### Extracting the data, which is the hard half

1.  **Map the reads.** Enable query logging with `Application Name` set per component, and collect a week. You will find consumers nobody remembered — a reporting job, an Excel connection, an old integration.
2.  **Choose a direction.** Either the new service owns the data and the legacy app reads it via API, or the legacy app keeps ownership and the new service maintains a replica from events. The second is usually less risky first.
3.  **Dual-write with a reconciliation job** during the transition, and alert on divergence rather than assuming it will not happen.
4.  **Flip the source of truth** in one deploy, with the reverse path still present.
5.  **Remove the old table** only after a full retention cycle has passed and nothing has read it — verified with the same query logging, not by inspection.

<a id="be-a172"></a>

#### The actor model: Akka.NET and Orleans

An actor is a single-threaded unit of state and behaviour that communicates only by messages. Because only one message is processed at a time, the actor's state needs no locks — concurrency is handled by the model rather than by you.

**Akka.NET**

- Explicit actor hierarchies and supervision
- You control lifecycle, routing, sharding
- Powerful, and a lot of concepts to hold
- Port of the JVM Akka model

**Microsoft Orleans**

- ‘Virtual actors’ (grains) always exist logically
- Runtime activates and deactivates them for you
- Much less ceremony; feels like calling an interface
- First-class .NET, integrates with the generic host

``` csharp
public interface IOrderGrain : IGrainWithGuidKey
{
    Task<OrderState> PlaceAsync(PlaceOrder cmd);
}

public class OrderGrain : Grain, IOrderGrain
{
    private OrderState _state = new();      // safe: one call at a time per grain
    public Task<OrderState> PlaceAsync(PlaceOrder cmd) { _state = _state.Place(cmd); return Task.FromResult(_state); }
}
```

Actors shine where you have many independent stateful entities with high contention on each one — devices, sessions, game objects, per-customer workflows. They are a poor fit for set-based queries and reporting; keep a database alongside them for that.

###### What actors actually solve, and when they are overkill

``` text
  the actor model in one idea

    an ACTOR owns some state.
    nobody else can touch that state.
    all interaction is by MESSAGE, processed ONE AT A TIME.

    -> no locks. No race conditions on that state. Ever.
    -> the actor is a unit of concurrency AND of distribution:
       it can live on any node, and move.

  what this genuinely solves
    per-entity concurrency      one actor per order, per device, per game
                                session. Concurrent requests for the SAME
                                entity queue naturally; different entities
                                run in parallel.
    stateful workflows          the actor holds the state machine
    fan-out to many entities    100,000 device actors, each cheap

  what it does NOT solve
    the state still has to be PERSISTED somewhere
    a cluster still has to be operated
    debugging is now message-flow archaeology
```

``` csharp
// Orleans - "virtual actors": you never create or destroy a grain.
// you address one by id and the runtime activates it on demand,
// on some node, and deactivates it when idle.
public interface IOrderGrain : IGrainWithGuidKey
{
    Task<Result> AddLineAsync(LineDto line);
    Task<OrderState> GetAsync();
}

public class OrderGrain([PersistentState("order")] IPersistentState<OrderState> state)
    : Grain, IOrderGrain
{
    public async Task<Result> AddLineAsync(LineDto line)
    {
        // NO LOCKING NEEDED. Orleans guarantees single-threaded execution
        // per grain, so two concurrent callers for THIS order are serialised
        // and two callers for DIFFERENT orders run in parallel.
        if (state.State.Status != "Draft") return Error.Conflict();
        state.State.Lines.Add(line);
        await state.WriteStateAsync();
        return Result.Ok();
    }
}

// calling it - the location is the runtime's problem
var grain = grainFactory.GetGrain<IOrderGrain>(orderId);
await grain.AddLineAsync(line);
```

**Akka.NET**

- Explicit actor lifecycle — you create and supervise
- Supervision hierarchies and failure strategies
- Streams, persistence, clustering as modules
- More control, more concepts to learn
- Closer to the original Erlang/Akka model

**Orleans**

- Virtual actors — always addressable, activated on demand
- No lifecycle management at all
- Persistence and placement are built in
- Much gentler learning curve
- The pragmatic choice for most .NET teams

| Actors fit                                           | Actors are overkill                             |
|------------------------------------------------------|-------------------------------------------------|
| High contention on individual entities               | Contention handled fine by a database row lock  |
| Many small stateful entities (IoT, gaming, sessions) | Stateless request/response CRUD                 |
| Long-lived in-memory state worth keeping hot         | State that must be durable and queryable anyway |
| Complex per-entity state machines                    | A status column and a few transitions           |
| You need location transparency across a cluster      | One process is enough                           |

> **\[Note\]**
>
> **Ask what the database already gives you.** A great deal of what actors provide — serialised access to one entity, consistent state, durability — a relational database provides with a row lock or an optimistic concurrency token, with no new runtime to operate. Actors earn their place when the entity count is very large, the state is hot and expensive to reload, or the per-entity logic is a genuine state machine. For a legacy platform already using Akka.NET, the useful question is which of those three still applies — often only one does, and the rest could be a table.

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Application Architecture</p>

<p class="pd">Patterns at the scale of a whole application: how the layers are arranged, which direction dependencies point, and whether reads and writes share a model.</p>

<a id="be-h764"></a>

#### Layers, and the dependency rule

Layering is the oldest architectural pattern and the one every other in this part is a variation of. What distinguishes the variations is **which way the dependencies point**.

``` text
  CLASSIC LAYERS - dependencies point DOWN

    Presentation  ---> Application ---> Domain ---> Data Access
                                                        |
                                                        v
                                                    database

    the domain depends on the data layer. Changing the database changes
    the domain. The domain cannot be tested without it.

  INVERTED (hexagonal / clean / onion) - dependencies point INWARD

    Presentation  ---+
                     +---> Application ---> Domain
    Infrastructure ---+                       ^
                                              |
                     Domain DEFINES the interfaces;
                     Infrastructure IMPLEMENTS them.

    the compile-time arrow now runs from infrastructure TO domain.
    the domain project references nothing.

  the mechanical test, and it is the only one that matters:
    open Domain.csproj. If it references Infrastructure, or any database
    or web package, the inversion did not happen.
```

``` csharp
// enforce it in a test, or it erodes within a quarter
[Fact]
public void Domain_depends_on_nothing()
{
    var result = Types.InAssembly(typeof(Order).Assembly)
        .ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "System.Net.Http", "Acme.Infrastructure")
        .GetResult();
    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
```

**The cost is real**

- More projects and more indirection
- An interface and an implementation for things used once
- Mapping between entity, domain model and DTO
- One feature spans four folders
- Genuine overhead on a small application

**The benefit is specific**

- The domain is testable with no infrastructure
- Swapping a data store is contained
- Business rules survive a framework rewrite
- Boundaries enable parallel work
- It pays off proportionally to how long the system lives

> **\[Note\]**
>
> **Choose by lifespan and team size.** For a service with two endpoints and one developer, a single project with folders is correct and Clean Architecture is costume. For a system several teams maintain for a decade, the indirection buys real optionality. The failure mode is the ceremony *without* the enforcement — four projects with `DbContext` referenced from all of them, which is strictly worse than one project.

###### Enforcing it mechanically, and the three ways it leaks

``` csharp
// an architecture test that runs in CI - convention alone lasts a quarter
using NetArchTest.Rules;

[Fact]
public void Domain_references_nothing_external()
{
    var result = Types.InAssembly(typeof(Order).Assembly)
        .ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "System.Net.Http", "Newtonsoft", "Acme.Infrastructure")
        .GetResult();
    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}

[Fact]
public void Handlers_are_sealed_and_internal()
{
    var result = Types.InAssembly(typeof(PlaceOrderHandler).Assembly)
        .That().ImplementInterface(typeof(IRequestHandler<,>))
        .Should().BeSealed().And().NotBePublic()
        .GetResult();
    Assert.True(result.IsSuccessful);
}
```

``` text
  THE THREE LEAKS, all of which compile and pass review

  1  THE INTERFACE EXPOSES THE IMPLEMENTATION

       public interface IOrderRepository { IQueryable<Order> Query(); }

     IQueryable IS Entity Framework - translated by it, failing at
     runtime when it cannot translate, and impossible to implement
     over Dapper. The dependency was renamed, not inverted.

  2  A "SHARED" OR "COMMON" PROJECT

       Domain -> Common -> EntityFrameworkCore

     the arrow still reaches the database. Shared projects are where
     layering goes to die, because nobody audits them and everyone
     adds to them.
     -> the architecture test must check Common too.

  3  ATTRIBUTES

       [Table("orders")] [Column("total")] public class Order

     the domain now needs the EF package for its attributes.
     -> use IEntityTypeConfiguration<T> in the infrastructure project.
        the domain class stays clean, and the mapping lives with the
        thing that does the mapping.

  the mechanical test that catches all three:
    open Domain.csproj. Any PackageReference beyond the BCL is a leak.
```

| Rule to enforce in CI                        | Catches                              |
|----------------------------------------------|--------------------------------------|
| Domain references no external packages       | All three leaks above                |
| Application does not reference Api           | Inverted layering                    |
| Infrastructure types are `internal`          | Consumers binding to implementations |
| No module references another module's `Core` | Modular monolith boundary erosion    |
| Handlers are sealed and internal             | Accidental public surface            |
| No `DateTime.Now` outside an adapter         | Untestable ambient time              |

The last row is worth adding early. Banning `DateTime.Now` and `DateTime.UtcNow` outside a single time adapter, enforced by an analyser or an architecture test, removes an entire category of untestable code — and `TimeProvider` makes the replacement free.

<a id="be-h765"></a>

#### Hexagonal, Clean and Onion: the same idea, drawn three ways

``` text
  they differ in vocabulary and diagram, not in substance.

  HEXAGONAL (ports and adapters), Cockburn 2005
    the application sits inside. PORTS are interfaces it defines;
    ADAPTERS are implementations at the edge.
      driving adapters   (HTTP, CLI, tests)      -> call INTO ports
      driven adapters    (database, queue, SMTP) -> called BY ports
    the useful contribution: symmetry. A test harness and a web
    controller are the SAME KIND OF THING.

  ONION, Palermo 2008
    concentric rings, dependencies point inward.
      domain model -> domain services -> application services -> infrastructure
    the useful contribution: it names the rings.

  CLEAN, Martin 2012
    concentric rings again, plus a rule about crossing them.
      entities -> use cases -> interface adapters -> frameworks
    the useful contribution: the DEPENDENCY RULE, stated as a
    one-liner, and the insistence that frameworks are a detail.

  ALL THREE SAY: the domain defines its own interfaces, and everything
  else depends on it. If you have that, you have all three.
```

``` csharp
// ports, as the domain defines them - note the vocabulary is the DOMAIN's
namespace Acme.Domain.Ports;

public interface IOrderRepository                       // driven port
{
    Task<Order?> ByIdAsync(OrderId id, CancellationToken ct);
    Task SaveAsync(Order order, CancellationToken ct);
}
public interface INotifyCustomer                        // driven port
{
    Task OrderConfirmedAsync(CustomerId id, OrderId order, CancellationToken ct);
}

// adapters implement them, and live at the edge
namespace Acme.Infrastructure.Adapters;
internal sealed class SqlOrderRepository(AppDbContext db) : IOrderRepository { }
internal sealed class SendGridNotifier(ISendGridClient c) : INotifyCustomer { }
internal sealed class NoOpNotifier : INotifyCustomer { }   // for tests, and for
                                                            // the environment where
                                                            // email is disabled
```

The symmetry point is worth dwelling on because it is hexagonal's genuine contribution. If a web controller and an integration test are both just *driving adapters*, then testing the application without HTTP is not a special arrangement — it is the ordinary case, using a different adapter. That reframing is what makes the architecture testable rather than merely tidy.

> **\[Why it matters\]** The three names describe one idea, and treating them as competing architectures produces meetings that cannot conclude. Recognising the shared shape — dependencies point inward, the outside world is adapters — lets a team adopt the substance without arguing about whose diagram to draw.

<a id="be-h766"></a>

#### Vertical Slice architecture

``` text
  LAYERED                          VERTICAL SLICE

  Controllers/                     Features/Orders/
    OrdersController.cs              CreateOrder/
  Services/                            Command.cs
    OrderService.cs                    Handler.cs
  Repositories/                        Validator.cs
    OrderRepository.cs                 Endpoint.cs
  Models/                            GetOrderById/
    Order.cs                           Query.cs
  DTOs/                                Handler.cs
    OrderDto.cs                        Response.cs

  changing "create an order"       changing it touches ONE FOLDER
  touches five files in five
  folders

  + change is LOCAL, and the blast radius is visible in the directory tree
  + each slice picks its own approach: this one needs a repository,
    that one is a three-line Dapper query. No forced uniformity.
  + deleting a feature means deleting a folder
  - deliberate duplication between slices
  - no single place enforcing a cross-cutting rule
  - "where does shared logic go?" recurs, and needs an answer
```

``` csharp
public static class CreateOrder
{
    public sealed record Command(Guid CustomerId, IReadOnlyList<LineDto> Lines)
        : IRequest<Result<Guid>>;

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(x => x.Lines).NotEmpty();
    }

    internal sealed class Handler(AppDbContext db) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command c, CancellationToken ct) { ... }
    }

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", async (Command c, ISender s) =>
            (await s.Send(c)).ToHttpResult());
}
// `internal` on the handler and validator is the ENFORCEMENT: nothing
// outside the slice can reach them, so slices cannot quietly couple.
```

> **\[Note\]**
>
> **They are not opposed.** The productive combination is vertical slices for the *application* layer — one folder per use case — over a shared *domain* layer holding the rules. Slices stay independent and duplicable; the invariants stay in one place. Choosing one exclusively is usually a false choice.

###### A complete slice, front to back

``` csharp
// Features/Orders/PlaceOrder.cs - the ENTIRE feature, in one file
public static class PlaceOrder
{
    // ---- the request ----------------------------------------------------
    public sealed record Command(Guid CustomerId, IReadOnlyList<Line> Lines,
                                 string IdempotencyKey) : IRequest<Result<Guid>>
    {
        public sealed record Line(Guid ProductId, int Quantity);
    }

    // ---- validation, scoped to this slice -------------------------------
    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.CustomerId).NotEmpty();
            RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line");
            RuleForEach(x => x.Lines).ChildRules(l =>
                l.RuleFor(x => x.Quantity).InclusiveBetween(1, 9_999));
            RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
        }
    }

    // ---- the handler -----------------------------------------------------
    internal sealed class Handler(
        AppDbContext db, IPricingEngine pricing, IInventoryService inventory,
        TimeProvider clock) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command cmd, CancellationToken ct)
        {
            var customer = await db.Customers
                .FirstOrDefaultAsync(c => c.Id == cmd.CustomerId, ct);
            if (customer is null) return Error.NotFound("Customer", cmd.CustomerId);
            if (customer.OnCreditHold) return Error.Conflict("Customer is on credit hold");

            var productIds = cmd.Lines.Select(l => l.ProductId).Distinct().ToArray();

            // ONE query for all products - not one per line
            var products = await db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            var missing = productIds.Except(products.Keys).ToArray();
            if (missing.Length > 0) return Error.NotFound("Product", missing);

            var order = Order.Create(customer.Id, clock.GetUtcNow());
            foreach (var line in cmd.Lines)
            {
                var product = products[line.ProductId];
                order.AddLine(product.Id, new Quantity(line.Quantity), product.ListPrice);
            }

            var priced = pricing.Price(new PricingContext(order, customer,
                                       DateOnly.FromDateTime(clock.GetUtcNow().Date)));
            order.ApplyPricing(priced);

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            return order.Id.Value;
        }
    }

    // ---- the endpoint ----------------------------------------------------
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/orders", async (Command cmd, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(cmd, ct);
                return result.Match(
                    ok:   id => Results.Created($"/orders/{id}", new { id }),
                    fail: e  => e.ToProblemDetails());
            })
            .WithName("PlaceOrder")
            .WithTags("Orders")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization("orders:write");
}
```

``` csharp
// and the endpoint registration, discovered by convention so adding a
// slice never means editing Program.cs
public static class EndpointRegistration
{
    public static void MapFeatureEndpoints(this IEndpointRouteBuilder app)
    {
        var mappers = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsAbstract && t.IsSealed)          // static classes
            .Select(t => t.GetMethod("Map", BindingFlags.Public | BindingFlags.Static))
            .Where(m => m is not null &&
                        m.GetParameters() is [{ ParameterType.Name: nameof(IEndpointRouteBuilder) }]);

        foreach (var map in mappers) map!.Invoke(null, [app]);
    }
}
// note: reflection at startup. If you need Native AOT, replace this with
// a source generator or explicit calls - the convention is a convenience,
// not a requirement of the pattern.
```

| What lives in the slice                | What does not                              |
|----------------------------------------|--------------------------------------------|
| The command, query and response shapes | The `Order` aggregate — shared domain      |
| Validation for *this* request          | `Money`, `Quantity` — shared value objects |
| The handler, `internal` and `sealed`   | `IPricingEngine` — shared capability       |
| The endpoint and its metadata          | Cross-cutting behaviours — the pipeline    |
| Slice-specific projections             | The `DbContext` configuration              |

The `internal sealed` on the handler and validator is the enforcement that makes this hold. Without it, another slice can reference `PlaceOrder.Handler` directly, and within a quarter the slices are coupled — at which point you have layered architecture with an unusual folder structure.

### Modules, services, and how they talk

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Modules, services, and how they talk</p>

<a id="be-m244"></a>

#### The real question: coupling and cadence

"Monolith or microservices" is the wrong framing, because it conflates two independent decisions:

|             | Logical modularity             | Physical distribution                           |
|-------------|--------------------------------|-------------------------------------------------|
| Question    | are the boundaries clear?      | do they deploy separately?                      |
| Buys you    | comprehensibility, safe change | independent deploy and scale, fault isolation   |
| Costs you   | discipline                     | network, partial failure, ops, distributed data |
| Reversible? | yes, cheaply                   | expensively                                     |

They combine into four real options, and only one is uniformly bad:

``` text
                 │  one deployable        │  many deployables
─────────────────┼────────────────────────┼──────────────────────────
 clear           │  MODULAR MONOLITH      │  MICROSERVICES
 boundaries      │  the sensible default  │  when you need the
                 │                        │  independence
─────────────────┼────────────────────────┼──────────────────────────
 unclear         │  big ball of mud       │  DISTRIBUTED MONOLITH
 boundaries      │  slow, but recoverable │  all the costs, none of
                 │                        │  the benefits
```

**The bottom-right quadrant is the one to fear**, and it is where most failed microservice migrations land: services that must be deployed together, that share a database, that break in lockstep — now with network calls between them. Splitting a system whose boundaries you have not established converts every method call into a distributed call, and every refactor into a coordinated release.

> **\[Why it matters\]**
>
> **The honest test** is not scale or team size in the abstract; it is: *do two parts of this system change for different reasons, on different schedules, at different risk levels?* If billing changes monthly under audit and the catalogue changes daily, separating them buys real independence. If everything changes together in the same sprint, separation buys deployment ceremony and latency ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705)).

> **\[Tip\]**
>
> **Start modular, distribute later.** Getting boundaries right inside one process is cheap to correct — a bad boundary is a refactor. Getting them wrong across a network is expensive — a bad boundary is a migration, a data move, and a coordinated release ([The migration path, in order](#be-m252)).

<a id="be-m246"></a>

#### Microservices: what you buy and pay

| You buy                | You pay                                                                                                          |
|------------------------|------------------------------------------------------------------------------------------------------------------|
| Independent deployment | versioned contracts between every pair ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223))                                   |
| Independent scaling    | network latency on every hop ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705))                                          |
| Fault isolation        | partial failure as the normal case                                                                               |
| Technology freedom     | N build pipelines, N runtimes, N sets of dependencies                                                            |
| Team autonomy          | distributed tracing to answer "where did the time go?"                                                           |
| Bounded blast radius   | **no distributed transactions** — sagas instead ([Transactions across services: sagas and the outbox](#be-m250)) |

``` text
// The cost that surprises people is availability MULTIPLICATION.
// A request touching 5 services, each 99.9% available:
//   0.999^5 = 99.5%  ->  ~3.6 hours of downtime per month instead of 43 minutes.
//
// Which is why retries, timeouts, circuit breakers, and fallbacks are not
// optional extras in a distributed system - they are what restores the
// availability the decomposition spent (§263).
```

**Size is not the point.** "Micro" has caused more damage than any other word in this discussion: a service should be as large as its bounded context, and a context that owns orders, order lines, payments, and shipments is a perfectly good service. Splitting by entity — an Order service, an OrderLine service — guarantees chatty calls and distributed transactions.

> **\[Tip\]**
>
> **The prerequisites, honestly.** Before splitting: automated deployment per service, centralised structured logging with correlation ids, distributed tracing, per-service health checks and dashboards, a contract-testing story, and a way to run the system locally. Without those, a distributed system is not just harder to operate — it is harder to *understand*, and debugging becomes archaeology ([Observing the data layer](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m265)).

<a id="be-m247"></a>

#### Decomposition: contexts and data ownership

Boundaries follow the business, not the schema. The useful unit is a **bounded context**: a region within which a word has one meaning.

``` text
"Customer" is not one thing:
  Sales     → a lead with a pipeline stage and an owner
  Billing   → a legal entity with a tax id and payment terms
  Support   → a contact with a history of tickets
  Shipping  → an address and a set of delivery preferences

// Forcing these into one shared Customer table produces a wide table nobody
// owns, where every team's change risks every other team (§189).
// Each context keeps its own model, correlated by a shared CustomerId.
```

**The rule that makes decomposition real: one service owns each piece of data, and nobody else reads its tables.** Direct database access from another service is the single most common way a microservice architecture becomes a distributed monolith — because now the schema is a public contract that cannot be changed independently.

| Symptom                                 | What it means                      |
|-----------------------------------------|------------------------------------|
| Two services always deploy together     | the boundary is in the wrong place |
| A feature always touches three services | you split along the wrong axis     |
| One service reads another's tables      | there is no boundary at all        |
| A shared library holds domain logic     | coupling moved, it did not go away |
| A change needs a coordinated release    | you have a distributed monolith    |

> **\[Tip\]**
>
> **Data duplication across contexts is correct, not a smell.** Shipping keeping its own copy of the delivery address is not denormalisation gone wrong — it is Shipping owning the fact it needs, in the shape it needs, immune to changes elsewhere. Keep it fresh with events, and accept that it lags ([Asynchronous communication: events and commands](#be-m249), [Consistency across stores](07-06-non-relational-stores.md#be-m210)).

Worked example: [Case: the service split that made everything slower](14-03-worked-cases-from-production.md#be-m308)

<a id="be-m248"></a>

#### Synchronous communication and its coupling

``` csharp
// The obvious approach, and the one that creates the tightest coupling:
var customer = await _customerApi.GetAsync(order.CustomerId, ct);   // HTTP or gRPC
// Now: if Customers is down, Orders is down.
//      if Customers is slow, Orders is slow.
//      if Customers changes its contract, Orders breaks.
// You have a runtime dependency as strong as a compile-time one, plus latency.
```

|                      | HTTP/JSON              | gRPC                                            |
|----------------------|------------------------|-------------------------------------------------|
| Contract             | OpenAPI, by convention | `.proto`, enforced at build                     |
| Payload              | larger, readable       | compact binary                                  |
| Streaming            | awkward                | first-class, both directions                    |
| Debuggability        | curl, browser, proxy   | needs tooling                                   |
| Deadline propagation | manual                 | built in ([Protocol Buffers and gRPC](08-08-serialization-and-data-formats.md#be-m93)) |

``` csharp
// If you must call synchronously, make failure a designed behaviour:
builder.Services.AddHttpClient<ICustomerApi, CustomerApi>(c => {
        c.BaseAddress = new Uri(cfg["Customers:BaseUrl"]!);
        c.Timeout = TimeSpan.FromSeconds(3);          // ALWAYS shorter than yours
    })
    .AddStandardResilienceHandler()                   // retry + breaker + timeout
    .SetHandlerLifetime(TimeSpan.FromMinutes(2));     // re-resolve DNS (§16)

// And decide what happens when it fails - before it does:
//   fail the request? serve stale data? omit the section? queue for later?
// "It'll be fine" is a decision too, just an unexamined one.
```

> **\[Trap\]**
>
> **Synchronous chains multiply latency and divide availability.** A → B → C → D at 10 ms each is 40 ms of pure network before any work happens, and one slow link stalls everything upstream. If a call chain is more than two deep, that is a decomposition problem, not a performance problem — the boundary is wrong ([Decomposition: contexts and data ownership](#be-m247)).

> **\[Tip\]**
>
> **Ask whether the caller needs the answer *now*.** Most cross-service calls are gathering data for a response that could have been assembled from a local read model, or triggering work that could have been an event. Genuine synchronous need — a payment authorisation, a stock reservation — is rarer than the code suggests ([Asynchronous communication: events and commands](#be-m249)).

Worked example: [Case: the service split that made everything slower](14-03-worked-cases-from-production.md#be-m308)

Recipe: [Recipe: a resilient outbound API client](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m287)

<a id="be-m249"></a>

#### Asynchronous communication: events and commands

Two message kinds, and confusing them is the most common messaging design error.

|                          | Event                             | Command                                       |
|--------------------------|-----------------------------------|-----------------------------------------------|
| Says                     | "this happened"                   | "do this"                                     |
| Named                    | past tense: `OrderPlaced`         | imperative: `ReserveStock`                    |
| Recipients               | any number, unknown to the sender | exactly one, known                            |
| Sender knows the effect? | no — and must not care            | yes                                           |
| Coupling                 | loose                             | tighter — the sender knows the handler exists |

``` csharp
// Event: a fact. Carry enough for consumers to act without calling back.
public sealed record OrderPlaced(
    long OrderId, long CustomerId, decimal Total, string Currency,
    DateTimeOffset OccurredAt, IReadOnlyList<LineSnapshot> Lines);
// Too thin ("OrderId only") forces every consumer into a synchronous callback,
// which reintroduces exactly the coupling the event removed.
// Too fat leaks your internal model into everyone else's code. Aim for the
// facts a consumer plausibly needs, and version it (§223).
```

``` csharp
public sealed class OrderPlacedConsumer(IStockService stock) : IConsumer<OrderPlaced> {
    public async Task Consume(ConsumeContext<OrderPlaced> ctx) {
        // At-least-once delivery is the norm: this WILL run twice sometimes.
        // Idempotency is the consumer's responsibility, always.
        await stock.ReserveAsync(ctx.Message.OrderId, ctx.Message.Lines,
                                 idempotencyKey: ctx.MessageId!.Value, ctx.CancellationToken);
    }
}
```

| Broker            | Model              | Ordering          | Replay                  |
|-------------------|--------------------|-------------------|-------------------------|
| RabbitMQ          | queues + exchanges | per queue         | no — consumed is gone   |
| Azure Service Bus | queues + topics    | per session       | limited                 |
| Kafka             | partitioned log    | per partition key | yes — retention-bounded |

> **\[Trap\]**
>
> **Ordering is per partition, never global.** If `OrderPlaced` and `OrderCancelled` for the same order can land on different partitions, they can be processed out of order. Partition by the entity key so one entity's messages are ordered, and design handlers to tolerate out-of-order arrival anyway — check the current state rather than assuming a sequence ([Consistency across stores](07-06-non-relational-stores.md#be-m210)).

Worked example: [Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312)

<a id="be-m250"></a>

#### Transactions across services: sagas and the outbox

There is no distributed transaction. Two-phase commit across services is available in theory and a bad idea in practice — it couples availability, holds locks across the network, and does not survive a coordinator failure. What replaces it is a **saga**: a sequence of local transactions, each with a compensating action.

``` csharp
Happy path:
  1. Orders:   create order (pending)          local transaction
  2. Stock:    reserve items                   local transaction
  3. Payments: charge the card                 local transaction
  4. Orders:   confirm order                   local transaction

Payment fails at step 3:
  3'. Stock:   RELEASE the reservation         compensation
  4'. Orders:  mark the order failed           compensation

// Compensations are not rollbacks. The intermediate states WERE visible;
// a refund is not the same as a charge never happening. The business must
// accept that, and usually already does.
```

|                        | Choreography                  | Orchestration                         |
|------------------------|-------------------------------|---------------------------------------|
| How                    | each service reacts to events | one coordinator issues commands       |
| Coupling               | loosest                       | coordinator knows all steps           |
| Understanding the flow | no single place shows it      | one readable state machine            |
| Best for               | 2–3 steps                     | anything longer, or with compensation |

``` csharp
// The mechanism that makes any of this reliable: the transactional outbox.
// Write the business change and the message in ONE local transaction.
await using var tx = await db.Database.BeginTransactionAsync(ct);
db.Orders.Add(order);
db.Outbox.Add(OutboxMessage.For(new OrderPlaced(order.Id, ...)));
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
// A background dispatcher publishes unsent rows and marks them sent.
// The message is now exactly as durable as the order (§210).
```

> **\[Trap\]**
>
> **Without an outbox you have a lost-update window.** Save then publish loses the message if the process dies between them; publish then save publishes an event for something that never happened. Neither is acceptable for money, stock, or anything auditable — and both fail rarely enough to survive testing and show up in production ([Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303)).

Worked example: [Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312)

Recipe: [Recipe: an idempotent command handler](14-01-recipes-code-you-will-actually-write.md#be-m286)

<a id="be-m251"></a>

#### Gateways, BFFs, and service discovery

``` text
  browser ─┐
  mobile  ─┼──► API gateway ──┬──► Orders
  partner ─┘   (or a BFF      ├──► Customers
                per client)   └──► Billing
```

| Component     | Owns                                                            | Should not                                             |
|---------------|-----------------------------------------------------------------|--------------------------------------------------------|
| **Gateway**   | routing, TLS termination, authn, rate limiting, correlation ids | hold business logic — it becomes a shared bottleneck   |
| **BFF**       | aggregating and shaping for *one* client                        | be shared between clients — then it is a gateway again |
| **Discovery** | turning a service name into an address                          | be hand-maintained config                              |

``` csharp
// YARP - a reverse proxy as a .NET library, so gateway policy is code you test.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(b => b.AddRequestTransform(async ctx => {
        ctx.ProxyRequest.Headers.Add("X-Correlation-Id",
            ctx.HttpContext.TraceIdentifier);
    }));
app.MapReverseProxy();
```

``` text
// Discovery in practice is usually DNS you did not write:
//   Kubernetes   orders.default.svc.cluster.local  -> a stable virtual IP
//   Service mesh sidecar proxies handle routing, retries, mTLS
//   .NET Aspire  service discovery + config wiring for local and cloud
// Which is why §16 matters more in a distributed system than a
// single-service one - stale resolution now breaks service-to-service calls.
```

> **\[Why it matters\]**
>
> **A BFF exists to stop clients from being chatty.** A mobile screen needing data from four services should make one call, not four over a high-latency link ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705)). The BFF fans out server-side where the calls are cheap. The moment two different clients share a BFF, it starts accumulating conditionals for both and becomes the shared component the pattern was meant to avoid.

> **\[Trap\]**
>
> **A gateway is a single point of failure and a shared deployment.** Everything routes through it, so its outage is total and every change to it risks every service. Keep it thin, keep its logic declarative, and make sure it is not where authorisation decisions *only* live — services must still authorise, because anything inside the perimeter can otherwise call anything ([Auth, rate limiting, and output caching](08-03-a-modern-net-backend.md#be-m243)).

<a id="be-m252"></a>

#### The migration path, in order

Every step below is independently valuable and independently revertible. That property is what makes it a plan rather than a gamble — and most systems should stop before the last step.

``` text
 1. MEASURE. Which parts change together? Which change for different reasons?
    Commit history answers this better than opinion: files that always change
    in the same PR belong in the same module.

 2. SEPARATE SCHEMAS inside one database. Give each candidate module its own
    schema and stop cross-schema joins. Painful, revealing, and reversible.

 3. ENFORCE MODULE BOUNDARIES in code. Public contracts, internal everything
    else, architecture tests in CI (§245).

 4. ROUTE CROSS-MODULE CALLS THROUGH INTERFACES. In-process still, but now
    every crossing is a named, countable, mockable call.

 5. MAKE THE CROSSINGS ASYNCHRONOUS where the semantics allow. Add the outbox
    now, while it is still one database and a transaction is available
    (§250).

 6. EXTRACT ONE MODULE. Pick the one with the fewest inbound crossings and the
    most independent cadence. Move its schema; replace the interface with an
    HTTP or message client. Everything else is unchanged.

 7. STOP unless the next extraction has a reason as good as the first.
```

**The strangler pattern** is how step 6 happens without a rewrite: put a facade in front of the old behaviour, route one operation at a time to the new service, and keep both running until traffic has fully moved.

``` csharp
// Route by flag, verify by comparing, then cut over:
app.MapPost("/orders", async (PlaceOrderCommand cmd, IFeatureManager flags, ...) =>
    await flags.IsEnabledAsync("orders-service")
        ? await _newServiceClient.PlaceAsync(cmd, ct)
        : await _legacyHandler.HandleAsync(cmd, ct));

// For read paths, run BOTH and compare before switching - a mismatch report
// is far better evidence than a test suite (§295).
```

> **\[Why it matters\]**
>
> **Steps 1–5 deliver most of the benefit.** Comprehensible boundaries, safe independent change, and clear data ownership are what teams actually wanted; distribution was the assumed means. Many systems that complete step 5 discover the pain has gone and step 6 has no business case left — which is a successful outcome, not a failed migration.

> **\[Trap\]**
>
> **Do not extract and rewrite simultaneously.** Moving code across a boundary and changing its behaviour at the same time makes every bug ambiguous: is this new logic, or a move that lost something? Extract first with identical behaviour, verify, then change ([Schema evolution: expand and contract](07-05-schema-design-and-migrations.md#be-m220)).

### Integration & Distributed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Integration & Distributed</p>

<p class="pd">Patterns for systems made of parts that can fail independently. These postdate the GoF catalogue entirely, and they are where most modern design difficulty actually lives.</p>

<a id="be-h774"></a>

#### Anti-corruption layer, Gateway and BFF

##### Anti-corruption layer

**Intent:** translate between your model and an external one, so the external model cannot leak inward. It is Adapter applied to an entire integration, and its real function is *containment* rather than translation.

``` csharp
// what the legacy system gives you, and you cannot change it
public class LegacyCustomerRecord
{
    public string CUST_ID { get; set; }
    public string CUST_NM { get; set; }
    public string STAT_CD { get; set; }      // "A", "I", "P", "X"
    public string CRT_DT  { get; set; }      // "20260807" as a string
}

// your domain, which owes it nothing
public sealed record Customer(CustomerId Id, string Name,
                              CustomerStatus Status, DateOnly CreatedOn);

// the ACL - the ONLY place that knows CUST_NM exists or that "A" means Active
public sealed class LegacyCustomerAdapter(ILegacyClient legacy) : ICustomerSource
{
    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct)
    {
        var r = await legacy.FetchAsync(id.Value, ct);
        return r is null ? null : new Customer(
            new CustomerId(r.CUST_ID), r.CUST_NM.Trim(),
            r.STAT_CD switch { "A" => CustomerStatus.Active,
                               "I" => CustomerStatus.Inactive, _ => CustomerStatus.Unknown },
            DateOnly.ParseExact(r.CRT_DT, "yyyyMMdd", CultureInfo.InvariantCulture));
    }
}
// when the legacy system is replaced, ONE class changes.
// without it, STAT_CD == "A" appears in forty places.
```

##### Gateway and Backend for Frontend

``` text
  two patterns, frequently conflated

  API GATEWAY                        BACKEND FOR FRONTEND

  ONE entry point for everyone       one backend PER CLIENT TYPE

  concerns: routing, TLS, rate       concerns: SHAPING the response for
  limiting, token validation,        ONE client. Aggregating several
  one place to observe traffic       calls into one round trip.

  owned by: the PLATFORM team        owned by: THE CLIENT TEAM
  changes: rarely                    changes: with the client

  they COMPOSE. A gateway in front of several BFFs is normal.
  the mistake is one gateway trying to be a BFF for three clients -
  it accumulates client-specific transformation and becomes the
  bottleneck every team waits on.

  and the ownership row is what decides whether a BFF works. A BFF
  owned by a backend team is a queue the frontend team waits in - at
  which point it is a gateway with extra steps.
```

###### What an ACL contains, beyond translation

``` text
  an ACL is usually described as "translation". In practice it contains
  five things, and four of them are not translation at all.

  1  TYPE TRANSLATION
       "20260807" -> DateOnly.  "A" -> CustomerStatus.Active.

  2  VOCABULARY TRANSLATION
       CUST_NM -> Name.  their "Account" -> your "Customer".
       this is where a bounded context boundary actually lives.

  3  ERROR TRANSLATION
       their 200-with-an-error-body -> your exception.
       their 500 -> a transient failure you may retry.
       their 404 -> null, not an exception.
       -> the domain must be able to distinguish RETRYABLE from
          PERMANENT, and legacy systems rarely make that easy.

  4  SEMANTIC DEFENCE
       their "quantity" is sometimes negative to mean a return.
       their date is in an unstated timezone.
       their "unique" id is not unique across regions.
       -> the ACL asserts your invariants and REJECTS what violates
          them, loudly, at the boundary - rather than letting bad data
          flow inward and surface three services later.

  5  RESILIENCE
       timeouts, retries, a circuit breaker. The legacy system's
       availability must not become your availability.
```

``` csharp
public sealed class LegacyCustomerAcl(ILegacyClient legacy, ILogger<LegacyCustomerAcl> log)
    : ICustomerSource
{
    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct)
    {
        LegacyCustomerRecord? r;
        try { r = await legacy.FetchAsync(id.Value, ct); }
        catch (LegacyTimeoutException ex)
        {
            // 3. ERROR TRANSLATION: their failure becomes YOUR vocabulary
            throw new UpstreamUnavailableException("customer-master", ex);
        }
        if (r is null) return null;

        // 4. SEMANTIC DEFENCE - fail loudly at the boundary
        if (string.IsNullOrWhiteSpace(r.CUST_NM))
            throw new UpstreamDataException($"Customer {id} has no name in the legacy system");

        if (!DateOnly.TryParseExact(r.CRT_DT, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            throw new UpstreamDataException($"Unparseable CRT_DT '{r.CRT_DT}' for {id}");

        // 1 + 2. translation
        return new Customer(id, r.CUST_NM.Trim(), MapStatus(r.STAT_CD), created);
    }

    private CustomerStatus MapStatus(string code) => code switch
    {
        "A" => CustomerStatus.Active,
        "I" => CustomerStatus.Inactive,
        "P" => CustomerStatus.Pending,
        _   => LogAndDefault(code),        // an UNKNOWN code is a signal that
                                            // the legacy system changed. Log it;
                                            // do not silently map to Unknown.
    };
}
```

Point 4 is the one that distinguishes a real ACL from a mapper. A mapper converts whatever it is given; an ACL *refuses* data that would violate your domain's assumptions, at the boundary, where the error message can name the upstream system and the offending record. Without it, bad legacy data flows inward and fails somewhere with no context — and the investigation starts in the wrong codebase.

<a id="be-h775"></a>

#### Strangler Fig, Sidecar and Claim Check

##### Strangler fig

**Intent:** replace a system incrementally by routing individual capabilities to the new implementation, while the old one keeps serving everything else. Named after the plant that grows around a tree and eventually replaces it.

``` text
// a proxy routes by path. Moving a route is a CONFIGURATION change.
{
  "ReverseProxy": {
    "Routes": {
      "orders-new":     { "ClusterId": "new",    "Match": { "Path": "/orders/{**r}" } },
      "everything-else":{ "ClusterId": "legacy", "Match": { "Path": "{**catch-all}" },
                          "Order": 100 }
    },
    "Clusters": {
      "new":    { "Destinations": { "d1": { "Address": "http://orders-api:8080/" } } },
      "legacy": { "Destinations": { "d1": { "Address": "http://legacy-iis/" } } }
    }
  }
}
// rolling back is the same change, reversed, in seconds. That
// reversibility is what makes each increment genuinely safe.
```

``` text
  THE RULE THAT KEEPS IT HONEST

    every increment must SHIP, and the legacy path must keep working
    throughout.

    if a step requires BOTH systems to be finished before anything can
    go live, it is a rewrite wearing a migration's clothes - and it
    will be cancelled at 70% when priorities change.

  the usual sequencing
    1  put the proxy in front, routing EVERYTHING to legacy. Ship it.
       (this step alone is valuable: you now have a seam)
    2  move ONE low-risk route. Ship it. Watch it.
    3  repeat, most-valuable-first
    4  when the last route moves, delete the legacy system
```

##### Sidecar and Claim Check

| Pattern         | Intent                                                                | Example                                                |
|-----------------|-----------------------------------------------------------------------|--------------------------------------------------------|
| **Sidecar**     | Run a helper process alongside the application, sharing its lifecycle | A service-mesh proxy, a log shipper, a secrets agent   |
| **Ambassador**  | A sidecar that specifically proxies *outbound* calls                  | Adding retries and mTLS without touching the app       |
| **Claim check** | Store a large payload; send only a reference                          | A message bus with a 256 KB limit and an 8 MB document |

``` csharp
// claim check: write the payload FIRST, then the message referencing it.
// this order is not optional - a consumer must never receive a reference
// to a blob that does not exist yet.
var key = $"events/{date:yyyy/MM/dd}/{evt.Id}.json";
await blobs.UploadAsync(key, payload, ct);                       // 1. payload
await bus.PublishAsync(new ClaimCheck(evt.Id, key, hash), ct);   // 2. reference

// and the operational consequences people meet later:
//   BLOB RETENTION must exceed queue retention plus dead-letter time
//   orphaned blobs need a lifecycle rule (the message failed to publish)
//   replaying an old message needs the blob to still exist
```

###### Sequencing a strangler migration, and the trap in the middle

``` text
  THE SEQUENCE THAT WORKS

  0  PUT THE PROXY IN FRONT, ROUTING 100% TO LEGACY.
       nothing has moved. Ship it anyway.
       you now have a SEAM, and every later step is a config change.
       -> teams that skip this step end up doing a big-bang cutover,
          because they never created the place to switch.

  1  MOVE A READ-ONLY, LOW-RISK ROUTE.
       a "get status" endpoint. No writes, easy to verify, trivial to
       revert. This proves the machinery, not the migration.

  2  MOVE THE HIGHEST-VALUE ROUTE THAT IS SELF-CONTAINED.
       value first, so the project survives a priority change.

  3  REPEAT, and delete legacy code as each route moves.
       DELETING IS PART OF THE STEP. Otherwise you maintain both.

  4  THE LAST ROUTE MOVES -> decommission the legacy system.

  THE TRAP IN THE MIDDLE, and it is where these projects die:

    SHARED STATE. Both systems write the same tables.

    new writes to `orders`. Legacy writes to `orders`.
    -> both must honour every invariant the other assumes
    -> a schema change must be compatible with BOTH
    -> and you cannot change the schema to suit the new design,
       which was frequently the point of the migration

    the resolutions, in order of preference:
      move a whole DATA-OWNING capability at once, not an endpoint
      have the new system own the table and expose an API the legacy
        system calls
      accept dual-write with an ACL and a reconciliation job - and
        treat it as temporary, with a date
```

| Route selection criterion          | Why                                                    |
|------------------------------------|--------------------------------------------------------|
| Self-contained data                | Avoids the shared-state trap                           |
| Low traffic first                  | A smaller blast radius while the machinery is unproven |
| High value early                   | The project survives a change in priorities            |
| Clear success metric               | You can tell whether it worked                         |
| Reversible in one config change    | Rollback is not a deploy                               |
| **Legacy code deleted at the end** | Otherwise you maintain two systems indefinitely        |

> **\[Note\]**
>
> **Every increment must ship.** If a step requires both systems to be finished before anything can go live, it is a rewrite wearing a migration's clothes — and it will be cancelled at 70% when priorities change, leaving you with two half-systems. The strangler fig's entire value is that it is a sequence of individually valuable, individually reversible steps.
