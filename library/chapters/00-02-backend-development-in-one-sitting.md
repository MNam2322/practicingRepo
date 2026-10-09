---
id: "0-2"
number: "0.2"
title: "Backend development in one sitting"
part: 0
part_title: "Start here"
words: 16590
minutes: 72
---

# 0.2 Backend development in one sitting

> **Quick look.** The whole of backend work in one sitting: a few ideas, ten recurring problems, and the rest is vocabulary.
>
> - Almost all cost, failure and design effort is spent crossing boundaries: process, network, database.
> - Shared mutable state is what makes systems hard; consistency is a set of per-operation dials, not one switch.
> - A remote call has three outcomes: success, failure, or success you never heard about. Retries and idempotency keys exist for the third.
> - Every framework does the same steps: listen, route, run middleware, bind input, inject dependencies, talk to the database.
> - N+1 queries cause more slow endpoints than anything else; turn on ORM query logging before you need it.
> - Optimistic concurrency (a version column checked on update) is the default for user-editable data.
> - Default architecture: a modular monolith with boundaries enforced in the build.
> - Mock at the boundary (payment provider, clock), not inside your own code; put a trace id on every log line and error response.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: One request, every boundary it crosses

"Place order" takes 1.8 seconds in production but 40 ms on a laptop. You trace the request and count boundary crossings.

*What the trace showed*

```text
POST /orders                      total 1,800 ms
  auth: validate JWT                   1 ms   (in-process)
  load customer        SQL             4 ms
  load 30 products     SQL x 30      120 ms   <- N+1: one query per line item
  price check          HTTP to pricing service x 30   1,500 ms   <- sequential calls
  save order           SQL             8 ms
  publish event        broker          6 ms
```

*The fix: cross each boundary once*

```csharp
var ids = request.Lines.Select(l => l.ProductId).ToList();
var products = await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(ct);   // 1 query
var prices = await pricing.GetPricesAsync(ids, ct);                                // 1 batched HTTP call
```

> **Lesson:** Cost lives at boundaries. Count network and database round trips per request before you profile any code.

#### Example 2: The retry that charged a customer twice

A mobile client times out after 10 seconds and retries the payment. The first request had actually succeeded after 11 seconds.

**What goes wrong.** A remote call has three outcomes: success, failure, or success you never heard about. The retry assumed failure.

*The fix: an idempotency key*

```csharp
// Client sends the same Idempotency-Key header on every retry of one logical payment.
app.MapPost("/payments", async ([FromHeader(Name = "Idempotency-Key")] Guid key,
                                PaymentRequest req, PayDb db, CancellationToken ct) =>
{
    var existing = await db.Payments.SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
    if (existing is not null) return Results.Ok(existing.ToDto());   // replay the stored result

    var payment = Payment.Create(key, req);
    db.Payments.Add(payment);
    await db.SaveChangesAsync(ct);   // unique index on IdempotencyKey stops two concurrent firsts
    return Results.Created($"/payments/{payment.Id}", payment.ToDto());
});
```

> **Lesson:** Any operation a client may retry needs a way to recognize the repeat. Design for the "succeeded but you didn't hear" case from the start.

### The source volumes

<p class="src">About the guides merged in this library</p>

*Backend Engineering, Complete Volume* is itself thirteen books. Their sections are spread through this library by topic; each deep-dive heading names the book it came from.

- **Backend Development — A Working Guide**: What backend work actually is, independent of any stack: boundaries, state, consistency, failure and time — then the same concepts mapped across eight frameworks. Read this first, and it stays useful if you ever leave .NET.
- **C# & .NET**: The language, the runtime, ASP.NET Core, EF Core, and everything the platform does with your code.
- **C# Types & Data Structures**: The type system in depth: values and references, delegates and closures, primitives, strings, collections.
- **async / await**: From threads to state machines, scheduling, contexts, cancellation and the deadlocks they cause.
- **Relational Databases**: Modelling, index internals, query plans, transactions, replication and scale.
- **APIs**: HTTP itself, one call end to end, interface design beyond CRUD, auth, reliability, versioning.
- **Caching**: Anatomy, staleness and invalidation, failure modes, and every tier from in-process to CDN.
- **Data, Collections, LINQ & SQL**: A reference-style deep dive on how .NET stores, shapes and queries data: memory and the type system, collections and their cost models, LINQ, LINQ-to-SQL translation, EF Core, hand-written SQL, storage engines, and worked optimisation cases.
- **Linux from Zero**: The operating system every backend actually runs on: the shell, files and permissions, processes and signals, the text tools that answer production questions, services and logs — and what changes when you move .NET onto it.
- **Backend Platform**: Networking, TLS, proxies, Linux, containers, deployment, observability and incident response.
- **Design Patterns**: Patterns as vocabulary: the force each answers, what DI absorbed, and when the pattern is the problem.
- **Practical Answers**: Worked answers to the questions that come up in interviews and code review: structured logging and OpenTelemetry, EF Core interceptors and soft delete, DbContext concurrency and scopes, caching, the GC and boxing, async internals, and the cloud services you meet first.
- **Worked Builds**: Complete implementations rather than snippets: one feature built end to end with its tests, integrations that survive real failure, a slow endpoint taken from 4.2s to 90ms with the measurements at each step, and the artefacts that ship it.

*.NET Backend Deep Dive* gives each topic three layers: what and why, practical code, and a short interview answer. *Programming, from the machine up* supplies the core explanation at the start of chapters 0.1 to 13.2.


### Start here

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Start here</p>

<a id="be-z1"></a>

#### The claim this document makes

Backend work is a small number of ideas applied over and over. Five facts generate almost every pattern in the field, ten problems recur in every framework, and the rest is vocabulary.

This document is written for someone who has done this before and wants the structure underneath the habits. [Everything interesting happens at a boundary](#be-z3) is where the real content starts; [One request, end to end](05-03-async-and-await.md#be-c328) is where it comes back together.

``` text
The problems, all of them:

  1. Listen, and turn bytes into a request         (see: The universal sequence)
  2. Route the request to some code                (see: Routing: matching a request to code)
  3. Run cross-cutting work around it              (see: The pipeline: middleware by any name)
  4. Give that code its dependencies               (see: Dependency injection, and what it is actually for)
  5. Read configuration without hard-coding it     (see: Configuration and secrets)
  6. Talk to a database                            (see: The four ways to talk to a database)
  7. Trust nothing that came from outside          (see: Validation: three different jobs)
  8. Decide who may do what                        (see: Authentication and authorization)
  9. Do work that outlives the request             (see: Work that outlives the request)
 10. Be debuggable when it goes wrong              (see: Observability: three things, three jobs)

And four decisions that outlive all of the above:

 11. What shape the API takes                      (see: Resources, verbs, and what each one promises)
 12. What you refuse to trust                      (see: The shortlist)
 13. Where the time goes, and what to do about it  (see: Where the time actually goes)
 14. How the system is divided                     (see: One deployable or many)
```

Once you can name those, learning a framework is a mapping exercise. You are not learning what a middleware is again; you are learning what this framework calls one and where the file goes.

> **\[Why it matters\]** **Why this is worth writing down.** Developers who only know one framework tend to confuse the framework's opinions with how backends work. They then find the next framework harder than it is, because they cannot tell which of their knowledge was universal and which was local.
>
> The test: could you explain what your framework's DI container is *for* to someone using a framework that has none? If not, you know the API and not the problem ([Dependency injection, and what it is actually for](#be-z12)).

<a id="be-z2"></a>

#### How to use it

Code examples appear in more than one language, chosen so the comparison teaches something. Where every framework does the same thing, one example is enough.

| If you are                             | Read                                                                                                                                 |
|----------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| Learning your first framework          | Parts 2 and 3 alongside its tutorial. The tutorial shows the how; this shows the why.                                                |
| Moving to a second framework           | [The same concept, eight frameworks](#be-z55) first, then whichever concepts feel unfamiliar.                                        |
| Interviewing                           | Parts 4 and 5. Data access and boundaries are where most questions live.                                                             |
| Designing a service                    | Part 4, then [The four concurrency models](#be-z34).                                                                                 |
| Deciding on a stack                    | [The four concurrency models](#be-z34) and [What does not transfer](#be-z57).                                                        |
| Reviewing someone's service            | [The shortlist](#be-z33), [The order to optimise in](#be-z39), [Keeping it changeable](#be-z48).                                     |
| Making it faster                       | [Where the time actually goes](12-04-scaling-and-performance.md#be-g705), then [The order to optimise in](#be-z39), then [N+1: the universal mistake](#be-z17).      |
| **Refreshing after years of practice** | Part 2 slowly, then [One request, end to end](05-03-async-and-await.md#be-c328) and [What to actually hold in your head](#be-z52). Skim the middle for gaps. |
| Working in .NET                        | [.NET up close](08-03-a-modern-net-backend.md#be-z53)                                                                                                             |
| Working in Node or TypeScript          | [Node and TypeScript up close](08-04-a-node-and-typescript-backend.md#be-z54)                                                                                              |

> **\[Tip\]** **The document is self-contained.** It assumes you can program and have seen a web application, and nothing else. Every concept is introduced where it is used, and the code examples span several languages so nothing depends on knowing one framework.

> **\[Tip\]** **If you are refreshing rather than learning, read Part 2 first and slowly.** It names the five facts the rest of the document is derived from: boundaries, state, consistency, unknowable failure, and time. Most experienced developers act on all five already without having separated them, and separating them is what makes an unfamiliar situation tractable.
>
> Then [What to actually hold in your head](#be-z52) is the compressed version — five ideas, five numbers, five habits, five questions — and it doubles as a self-audit for a service you already own.

### The ideas underneath

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · The ideas underneath</p>

<a id="be-z3"></a>

#### Everything interesting happens at a boundary

A backend is not primarily logic. It is a stack of boundaries, and essentially all of your cost, failure, and design effort is spent crossing them.

``` text
  FUNCTION      free, reliable, typed
  MODULE        free, reliable, typed if you enforce it
  PROCESS       serialisation, a context switch, no shared memory
  MACHINE       + latency, + partial failure, + ordering problems
  TRUST         + everything is a lie until validated
  TRANSACTION   + atomicity ends here, whether you meant it to or not
  TYPE          + guarantees stop; runtime shapes take over
```

Look at what changes as you descend. Not just speed — the *kind* of reasoning available to you.

``` text
Inside a function:      a call either happens or throws. Two outcomes.
Across a machine:       a call succeeds, fails, or SUCCEEDS WITHOUT YOU
                        KNOWING. Three outcomes, and the third has no
                        local evidence ((see: Failure, and the thing you cannot know)).

Inside a transaction:   you can reason about a consistent snapshot.
Outside one:            every read may be stale and every write may
                        interleave ((see: Consistency is a set of dials)).

Inside your types:      the compiler holds the invariant.
Across the boundary:    only what you check at the edge holds
                        ((see: Validation: three different jobs)).
```

This single frame explains a large fraction of this document. Why N+1 is the dominant performance bug: it crosses a machine boundary in a loop. Why over-posting exists: a trust boundary was crossed by field-name matching. Why sagas exist: a transaction boundary was cut in half. Why idempotency is mandatory: a machine boundary makes success unobservable.

> **\[Why it matters\]** **The design heuristic that follows.** When something is hard, ask which boundary you are fighting, and whether it is in the right place.
>
> Most architectural pain is a boundary drawn across something that needed to stay whole — a transaction split into two services, an aggregate split across two tables, an invariant split between the client and the server. Redrawing the boundary usually beats engineering around it ([Finding the boundaries](12-01-monoliths-modules-and-services.md#be-a166)).

<a id="be-z4"></a>

#### State is the thing that makes it hard

Pure computation is easy. Given the same input it produces the same output, it can be tested trivially, and it can be run anywhere, any number of times, in parallel.

Everything difficult in backend work comes from mutable state that more than one thing can observe.

``` text
Difficulty ≈ (how mutable) × (how many observers) × (how long it lives)

  a local variable            mutable, one observer, microseconds   trivial
  a request-scoped object     mutable, one request                  easy
  an in-process cache         mutable, all threads, process life    careful
  a database row              mutable, every instance, forever      hard
  a distributed cache         mutable, everyone, and it can lie     harder
  the same fact in two stores mutable, and they can DISAGREE        hardest
```

Read the standard advice again with that in mind, and it stops being a list of unrelated rules.

``` text
"Keep handlers stateless"        → remove observers
"Prefer immutable data"          → remove mutability
"One source of truth"            → remove duplicate state
"Short transactions"             → shorten state's exposure
"Derive, do not store"           → remove state that can disagree
"Scoped, not singleton"          → shorten lifetime ((see: Object lifetimes: the bug that only shows under load))
"Idempotent operations"          → make repeated writes not matter

// Seven habits, one underlying move: reduce the amount of mutable
// state that anything else can see.
```

``` text
// The corollary that experienced developers use constantly: a value you
// can DERIVE is not state. It cannot disagree with anything.
//
//   total stored alongside the lines  → two facts, eventually different
//   total computed from the lines     → one fact
//
// The exception is when you need it FROZEN at a point in time - a price
// as charged, a rate as applied. That is not a duplicate of the current
// value. It is a different fact, and storing it is correct
// ((see: Time: three clocks, none of them trustworthy)).
```

> **\[Tip\]** **Where state hides, in rough order of how often it surprises people.** A module-level variable. A singleton holding a request-scoped object. An ORM's identity map. A connection's session settings. A cache you forgot is per-instance. A static configuration object mutated at runtime. A local file on a machine that will be replaced.
>
> Each of these behaves correctly in development, where there is one process handling one request at a time, and incorrectly under concurrency or scale-out ([Scaling out, and what it multiplies](#be-z40)).

<a id="be-z5"></a>

#### Consistency is a set of dials

The usual framing is a binary: strongly consistent or eventually consistent. That is not useful, because real systems are consistent in some dimensions and not others, deliberately, per operation.

``` text
Five independent dials:

  ATOMICITY    do these writes happen all-or-nothing?
  ISOLATION    what can I see of other in-flight work?
  DURABILITY   has it survived a crash when I get the acknowledgement?
  STALENESS    how old may a read be?
  ORDERING     do observers agree on the order things happened?

// A transaction gives you the first three within one database. It gives
// you nothing about the last two once more than one store or replica
// is involved.
```

``` text
The same system, different dials per operation, on purpose:

  taking payment          atomic, durable, no staleness allowed
  reserving stock         atomic, and ordering matters
  updating a search index staleness of seconds is fine
  a dashboard             staleness of minutes is fine
  an audit log            durability critical, ordering critical,
                          staleness irrelevant
  a "last seen" timestamp all five dials basically loose

// Choosing the same setting for all of them is the mistake. Full
// strictness everywhere is slow and often impossible; looseness
// everywhere is wrong.
```

``` text
// The isolation dial deserves a specific warning, because its default
// is weaker than most people assume.
//
// READ COMMITTED - the usual default - guarantees only that you never
// read uncommitted data. It does NOT stop this:
//
//   read qty = 3      →  another transaction sets it to 1  →  you write 1
//
// Both reads were legal. The transaction did nothing wrong. This is why
// wrapping a check-then-act in a transaction does not fix it, and why
// the fix is a conditional write instead ((see: Transactions and what they actually guarantee)).
```

> **\[Why it matters\]** **The deep reason you cannot have everything.** Once state is on more than one machine, a network partition forces a choice: refuse to answer, or answer possibly-stale. There is no third option, and no amount of engineering produces one.
>
> So the practical skill is not achieving consistency. It is knowing, for each operation, which dial you have turned down and whether the business can tolerate it. A team that can articulate that per endpoint is doing something most cannot.

<a id="be-z6"></a>

#### Failure, and the thing you cannot know

Local code has two outcomes: it returned, or it threw. Remote code has three, and the third is the one that generates half the patterns in this document.

``` text
  SUCCESS   it worked, and you were told
  FAILURE   it did not work, and you were told
  UNKNOWN   the connection died. It may have worked. You cannot tell.

// There is no way to distinguish "the request never arrived" from "it
// arrived, succeeded, and the response was lost". None. Not with a
// longer timeout, not with a better client, not ever.
```

Every one of these follows from that single fact:

``` text
  retries              because UNKNOWN might have been failure
  idempotency keys     because UNKNOWN might have been success
                       ((see: Idempotency and safe retries))
  at-least-once        because a broker faces the same problem
  the outbox           because the alternative is losing the intent
                       ((see: Work that outlives the request))
  compensating actions because you cannot roll back a remote commit
  reconciliation jobs  because eventually two systems will disagree
                       and something must notice
```

``` text
The other failure properties worth holding:

  PARTIAL       some parts of the system are up. This is the NORMAL
                state of a distributed system, not an incident.
  GREY          not down, just slow. Far worse than down, because
                nothing sheds load and everything queues.
  CORRELATED    failures cluster. One dependency's bad minute becomes
                every service's bad minute, all retrying together.
  CASCADING     retries amplify. The recovering system is hit hardest
                exactly when it is weakest — which is why jitter is
                not a nicety ((see: Async, and the mistake every stack shares)).
```

> **\[Trap\]** **Grey failure is the one that causes real outages.** A dependency that returns errors quickly is survivable: circuit breakers open, load sheds, you degrade.
>
> A dependency that takes forty seconds instead of two hundred milliseconds holds your threads, your connections, and your memory, until a service that never called it directly stops responding. This is why a timeout on every outbound call is load-bearing infrastructure rather than tidiness ([Rate limiting and protecting yourself](06-07-servers-proxies-load-balancers-and-diagnosing-the-network.md#be-g650)).

> **\[Why it matters\]** **The mental shift that separates senior work here.** Stop asking "how do I make this not fail" and start asking "what happens when this fails, and is that acceptable?"
>
> Every synchronous call to something you do not control needs that question answered explicitly, in code, at the moment the call is added. Where nobody asked, the default answer is usually "the whole feature breaks", and it stays invisible until the day it executes.

<a id="be-z7"></a>

#### Time: three clocks, none of them trustworthy

Time is the second hard problem after state, and it is hard because there are at least three different notions of it and code routinely conflates them.

``` text
  WALL CLOCK    what time is it? Jumps forward and back with NTP
                corrections, DST, and leap seconds. Not monotonic.
                Use for: timestamps humans read.

  MONOTONIC     how much time has passed? Never goes backwards, means
                nothing as an absolute. Use for: durations, timeouts,
                rate limits.

  LOGICAL       what happened before what? A counter or version, with
                no relation to physical time. Use for: ordering,
                concurrency detection, event sequencing.
```

``` csharp
// Measuring a duration with a wall clock is a real bug:
const start = Date.now();
await work();
const ms = Date.now() - start;         // may be NEGATIVE after an NTP step

// Use the monotonic clock:
const start = performance.now();       // Node
var sw = Stopwatch.StartNew();         // .NET
start := time.Now(); elapsed := time.Since(start)   // Go's Since is monotonic
```

``` text
// And the one that matters most in distributed systems: you cannot
// order events across machines by their timestamps.
//
//   machine A stamps an event 12:00:00.100
//   machine B stamps an event 12:00:00.050
//   B's event may still have happened AFTER A's.
//
// Clock skew of tens of milliseconds is normal. Ordering needs a
// logical clock: a sequence number, a version, or a vector.
```

Separately from all of that, there are two *domains* of time, and confusing them produces confident wrong answers about the past.

``` text
  SYSTEM TIME    when the database row changed
  BUSINESS TIME  when the fact became true in the world

// A price change entered three days late:
//   system time says it took effect today
//   business time says it took effect three days ago
//
// A temporal table records system time. It cannot answer "what was the
// price on Tuesday" unless business-effective dates are stored as
// columns. Audit and correctness need both ((see: State is the thing that makes it hard)).
```

> **\[Tip\]** **Inject the clock, always.** Reading the current time directly from a static makes every time-dependent rule untestable, and time-dependent rules are exactly the ones worth testing hard.
>
> `TimeProvider` in .NET, an injected `Clock` interface elsewhere. Then a test can sit on a DST boundary, a leap day, or a midnight crossing without waiting for one ([Testing a backend](#be-z43)).

### The request lifecycle

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · The request lifecycle</p>

<a id="be-z8"></a>

#### The universal sequence

``` text
  1. LISTEN        a socket is bound to a port, accepting connections
  2. ACCEPT        a client connects; TLS is negotiated if applicable
  3. READ          bytes arrive. Possibly in several packets.
  4. PARSE         bytes → method, path, headers, body
  5. ROUTE         match the path and method to a handler
  6. PIPELINE      run cross-cutting work: logging, auth, rate limits
  7. BIND          turn the raw body and path into typed arguments
  8. HANDLE        YOUR CODE runs. Usually the only part you write.
  9. SERIALISE     the return value becomes bytes
 10. WRITE         status line, headers, body go back out
 11. CLEAN UP      dispose per-request resources; the connection may persist
```

Two observations about that list explain most confusion people have with frameworks.

**You write step 8.** Everything else is the framework, which is exactly why the framework's decisions about steps 5 to 7 and 9 shape how your code has to look.

**Steps 1 to 4 are usually not your framework at all.** They are a separate HTTP server, and knowing that matters when you are debugging.

``` text
  .NET        Kestrel                     built in
  Java        Tomcat, Jetty, Netty        pluggable
  Node        the http module             built into the runtime
  Python      Gunicorn / Uvicorn          SEPARATE process, usually
  Go          net/http                    standard library
  PHP         php-fpm behind nginx        separate, always
  Ruby        Puma                        separate
```

> **\[Trap\]** **Limits live in the server, not in your code.** Maximum request body size, header size, connection timeouts, and keep-alive behaviour are configured in the HTTP server, and your framework often cannot override them.
>
> This is why a large upload fails with an error your application never logged. The request was rejected at step 3, before any of your code existed. Look at the server config, not the handler ([Errors and status codes](06-06-http.md#be-e498)).

<a id="be-z9"></a>

#### Routing: matching a request to code

Routing answers one question: given a method and a path, which function runs? Every framework does it, and there are only three styles.

``` text
EXPLICIT   you declare each route and its handler
CONVENTION the framework derives routes from names or folder structure
ANNOTATED  routes are declared as attributes on methods
```

``` csharp
// Explicit — Express, Fastify, Go, .NET minimal APIs
app.get('/orders/:id', getOrder);
app.MapGet("/orders/{id:long}", GetOrder);
mux.HandleFunc("GET /orders/{id}", getOrder)

// Annotated — Spring, NestJS, ASP.NET controllers, FastAPI
@GetMapping("/orders/{id}")            // Spring
@Get(':id')                            // NestJS
@app.get("/orders/{id}")               // FastAPI

// Convention — Rails, Next.js, Django to a degree
resources :orders                      # Rails: generates seven routes
app/api/orders/[id]/route.ts           # Next.js: the path IS the route
```

The differences that actually bite are not stylistic.

``` text
MATCH ORDER      most frameworks match in DECLARATION order, so a
                 catch-all declared early swallows everything after it.
                 Some match most-specific-first instead. Know which.

CONSTRAINTS      /orders/{id:int} rejects a non-numeric id at routing
                 time. Without constraints, "abc" reaches your handler
                 and you must reject it there ((see: Validation: three different jobs)).

TRAILING SLASH   /orders and /orders/ may be different routes,
                 the same route, or a redirect. Source of real bugs.

METHOD MISMATCH  a path that exists for GET but not POST should be
                 405, not 404. Not every framework gets this right.
```

> **\[Tip\]** **Whatever the style, keep the handler thin.** A handler's job is to translate HTTP into a call and a result back into HTTP. Business logic inside a handler cannot be tested without the framework and cannot be reused by a background job ([Where the logic lives](#be-z15)).

<a id="be-z10"></a>

#### The pipeline: middleware by any name

Work that applies to many routes — logging, authentication, compression, rate limiting — goes in a chain the request passes through. Every framework has this and every framework names it differently.

``` text
  Express, Koa, ASP.NET Core     middleware
  Spring                         filters and interceptors
  NestJS                         middleware, guards, interceptors, pipes
  Django                         middleware
  Rails                          Rack middleware, plus controller filters
  Go                             handler wrappers (just functions)
  FastAPI                        middleware and dependencies
```

``` csharp
// The shape is identical everywhere: a function that receives the request
// and the next step, and may act before, after, or instead of it.

// Node
app.use(async (req, res, next) => {
  const start = Date.now();
  await next();                                    // run everything downstream
  console.log(`${req.method} ${req.path} ${Date.now() - start}ms`);
});

// .NET
app.Use(async (ctx, next) => {
    var sw = Stopwatch.StartNew();
    await next();
    logger.LogInformation("{Method} {Path} {Ms}ms",
        ctx.Request.Method, ctx.Request.Path, sw.ElapsedMilliseconds);
});

// Go — no framework needed; a wrapper IS middleware
func logging(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        start := time.Now()
        next.ServeHTTP(w, r)
        log.Printf("%s %s %v", r.Method, r.URL.Path, time.Since(start))
    })
}
```

**Order is the contract, and it is the same everywhere.**

``` text
  error handling        outermost, so it catches everything below
    request logging     so failed requests are still logged
      CORS              before auth: a preflight carries no credentials
        authentication  establish WHO
          authorization then decide MAY THEY
            rate limit  cheaper to reject before doing real work
              handler
```

> **\[Trap\]** **Two ordering mistakes account for most pipeline bugs.** Authorization before authentication, which always sees an anonymous user and either denies everything or lets everything through. And error handling registered too late, so failures in earlier middleware escape as an unhandled crash with no log line.
>
> When a framework seems to ignore your middleware, check registration order before anything else.

<a id="be-z11"></a>

#### Binding: raw input to typed arguments

A request is bytes and strings. Your handler wants an integer id and a typed body. Something has to convert, and this is where statically and dynamically typed stacks diverge most.

``` csharp
// Where the framework can see types at runtime, binding is automatic:
// .NET — reflection over parameter types
app.MapPost("/orders/{id:long}", (long id, CreateOrder body) => { ... });

// Spring — annotations plus reflection
public Order create(@PathVariable Long id, @RequestBody CreateOrder body)

// Where types are erased at runtime, you declare the shape separately:
// TypeScript — the type is gone after compile, so a runtime schema is needed
const Body = z.object({ sku: z.string().min(3), qty: z.number().int().min(1) });
app.post('/orders/:id', (req, res) => {
  const id = Number(req.params.id);
  if (!Number.isInteger(id)) return res.status(400).json({ error: 'bad id' });
  const parsed = Body.safeParse(req.body);
  if (!parsed.success) return res.status(422).json(parsed.error.flatten());
  ...
});

// Python — type hints ARE available at runtime, so FastAPI binds like .NET
@app.post("/orders/{id}")
def create(id: int, body: CreateOrder): ...
```

That distinction explains a lot of the ecosystem difference between them ([What does not transfer](#be-z57)). Runtime types let a framework do binding, validation, serialisation, and ORM mapping for you. Erased types mean each of those needs an explicit schema.

> **\[Trap\]** **Binding is an attack surface, whichever stack you are on.** If the framework maps a request body onto a domain object by matching field names, a client can set any field it can name — including ones you never intended, like `isAdmin` or `price`.
>
> Bind to a dedicated input type that contains only what a client may send, then map to your domain object deliberately. This is called over-posting or mass assignment, and it is the same bug in every language ([Validation: three different jobs](#be-z22)).

### Wiring the application

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Wiring the application</p>

<a id="be-z12"></a>

#### Dependency injection, and what it is actually for

DI has a reputation for being an enterprise ceremony. It solves two real problems, and both appear in any application big enough to test.

``` csharp
PROBLEM 1: your code creates what it depends on, so you cannot substitute it.

class OrderService {
  constructor() { this.db = new PostgresClient(process.env.DB_URL); }
}
// Untestable without a database. Unusable with a second database.

PROBLEM 2: something must decide how long each object lives, and how many
           exist. A database pool should be shared. A per-request unit of
           work must not be.
```

``` csharp
// Dependencies are handed in, not constructed:
class OrderService {
  constructor(private db: Database, private clock: Clock) {}
}
// Now: real db in production, fake in tests, fixed clock in date tests.
// The class no longer knows or cares.
```

Frameworks differ in how much machinery they put around that idea, not in the idea.

``` text
  Spring, ASP.NET Core, NestJS   a container that resolves graphs by type
  FastAPI                        function-level Depends()
  Django, Rails                  little formal DI; framework globals instead
  Express                        none; you wire it by hand
  Go                             none by convention; explicit construction,
                                 sometimes generated by wire
```

> **\[Why it matters\]** **The absence of a container is not the absence of DI.** Go code passing dependencies into a struct at startup is doing dependency injection. Express code building services in one file and closing over them is doing the same thing.
>
> A container only automates resolving the graph. On a small service, hand-wiring is clearer and has no downside. The container earns its place when the graph is deep and lifetimes vary.

<a id="be-z13"></a>

#### Object lifetimes: the bug that only shows under load

Three lifetimes exist everywhere, whatever they are called.

| Lives for         | Called                   | Use for                                |
|-------------------|--------------------------|----------------------------------------|
| The whole process | singleton, module-scoped | pools, caches, HTTP clients, config    |
| One request       | scoped, request-scoped   | DB session, unit of work, current user |
| Every resolution  | transient, factory       | cheap stateless helpers                |

``` csharp
// THE BUG, in any language: a long-lived object holds a short-lived one.

class CacheService {                       // singleton
  constructor(private db: DbSession) {}    // per-request!
}
// The first request's session is captured forever. It is used after being
// closed, it holds a connection that never returns to the pool, and it
// retains every object it ever loaded.

// Symptoms, in the order you meet them:
//   works fine in development (one request at a time)
//   intermittent "session is closed" errors under load
//   connection pool exhaustion after an hour
//   memory that never comes back down
```

``` csharp
// The fix is to inject a FACTORY, so the singleton creates a short-lived
// instance per use and disposes it:
class CacheService {
  constructor(private sessions: () => DbSession) {}
  async load(id: string) {
    const s = this.sessions();
    try { return await s.find(id); } finally { await s.close(); }
  }
}
// .NET calls this IServiceScopeFactory; Spring calls it ObjectProvider;
// the concept is the same and so is the bug it prevents.
```

> **\[Trap\]** **Anything holding per-request state must not be a singleton.** The list is short and worth memorising: database sessions, transactions, the current user, the request's correlation id, and anything built from a request header such as a tenant.
>
> The reason this bug is hard is that the symptom appears far from the cause, and only with concurrency. In development it works perfectly ([Observability: three things, three jobs](#be-z42)).

<a id="be-z15"></a>

#### Where the logic lives

Frameworks give you handlers. They do not tell you where business rules go, and that decision matters more than any framework feature.

``` text
  HTTP layer        parse, validate shape, call inward, map result to a
                    status code. Knows about HTTP. Knows no business rules.

  Application layer one function per use case. Owns the transaction
                    boundary. Orchestrates. Knows nothing about HTTP.

  Domain layer      the rules. Pure, testable, no framework, no database.

  Infrastructure    database, queues, external APIs. Implements interfaces
                    the layers above define.
```

``` csharp
// The test for whether the split is real: could a background job reuse
// the use case without going through HTTP?

// GOOD — logic is callable from anywhere
app.post('/orders', async (req, res) => {
  const input = CreateOrderInput.parse(req.body);          // HTTP concern
  const result = await placeOrder(input, ctx.user);        // the use case
  return result.ok ? res.status(201).json(result.value)
                   : res.status(409).json({ error: result.error });
});

// BAD — the rules are trapped in the handler
app.post('/orders', async (req, res) => {
  const stock = await db.query('SELECT ...');
  if (stock < req.body.qty) return res.status(409)...       // a rule, here
  const total = req.body.qty * price * (1 - discount);      // a rule, here
  await db.query('INSERT ...');
});
// Unreusable, and untestable without an HTTP request.
```

> **\[Why it matters\]** **This is the part that transfers most completely between frameworks.** The HTTP layer is framework-specific and small. Everything below it is ordinary code in your language, and moving it from Express to Fastify, or from controllers to minimal APIs, touches only the outer edge.
>
> Teams that mix rules into handlers find framework migrations enormous, and conclude the framework was the problem. It was the layering.

> **\[Tip\]** **Do not build all four layers on a small service.** Two are usually enough: HTTP handlers, and everything else. The point is that business rules are callable without HTTP, not that you have four folders.

### Talking to a database

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Talking to a database</p>

<a id="be-z16"></a>

#### The four ways to talk to a database

Every data access library in every language is one of these four, or a blend of two.

| Pattern           | You write                           | Examples                                   |
|-------------------|-------------------------------------|--------------------------------------------|
| **Raw driver**    | SQL strings and parameter arrays    | `pg`, JDBC, `database/sql`                 |
| **Query builder** | SQL composed in your language       | Knex, jOOQ, Dapper, SQLAlchemy Core        |
| **Data mapper**   | objects; the library tracks changes | EF Core, Hibernate, SQLAlchemy ORM, Prisma |
| **Active record** | objects that save themselves        | Rails AR, Django ORM, Eloquent             |

``` csharp
-- The same query, four ways:

-- Raw
const { rows } = await db.query(
  'SELECT id, total FROM orders WHERE customer_id = $1 AND status = $2',
  [customerId, 'open']);

-- Query builder
const rows = await knex('orders')
  .select('id', 'total').where({ customer_id: customerId, status: 'open' });

-- Data mapper
var rows = await db.Orders
  .Where(o => o.CustomerId == customerId && o.Status == Status.Open)
  .Select(o => new { o.Id, o.Total }).ToListAsync();

-- Active record
rows = Order.objects.filter(customer_id=customer_id, status='open')
```

**The trade is always the same.** Moving down the table you gain expressiveness and lose visibility into what SQL runs. Active record is the fastest to write and the easiest to accidentally make issue fifty queries.

> **\[Tip\]** **The productive combination is a mapper for writes and something lower-level for hot reads.** Writes benefit from change tracking and relationship handling. Reads where you know exactly the shape you want are clearer and faster as explicit queries.
>
> Using two tools is not indecision. It is recognising that reads and writes have genuinely different requirements ([Transactions and what they actually guarantee](#be-z19)).

<a id="be-z17"></a>

#### N+1: the universal mistake

This causes more slow endpoints than everything else combined, in every language and every ORM. It looks like tidy code, which is why it survives review.

``` csharp
// Every ORM, same shape:
const orders = await Order.findAll({ limit: 50 });     // 1 query
for (const o of orders) {
  console.log(o.customer.name);                        // 1 query EACH
}
// 51 queries. Each one a network round trip.
```

``` text
Why it hurts more than it looks:

  a query is ~0.5 ms in the same datacentre
  51 queries is ~25 ms of pure waiting, minimum
  cross-region, at 90 ms each, it is 4.5 SECONDS
  and it scales with the page size, so it passes review at 10 rows
    and fails in production at 500
```

``` csharp
// The fix has the same two forms everywhere:

// 1. Tell the ORM to fetch related data in the same query
Order.findAll({ include: [Customer] });                  // Sequelize
db.Orders.Include(o => o.Customer)                       // EF Core
Order.objects.select_related('customer')                 # Django
session.query(Order).options(joinedload(Order.customer)) # SQLAlchemy
prisma.order.findMany({ include: { customer: true } })   // Prisma

// 2. Better for lists: project only what you need, so there is no
//    relationship to load at all
db.Orders.Select(o => new Row(o.Id, o.Total, o.Customer.Name))
```

> **\[Why it matters\]** **Lazy loading is the feature that creates this bug.** It makes a database round trip look like a property access, which is precisely what hides the cost.
>
> Where your ORM lets you disable it, do. Where it does not, the discipline is to know that touching a relationship inside a loop is a query, every time ([Seeing what your ORM actually does](#be-z18)).

> **\[Trap\]** **DataLoader is a workaround, not a fix.** Batching per-item fetches into one query is genuinely useful in GraphQL, where the query shape is the client's decision.
>
> Everywhere else, the right fix is to fetch the right shape in the first place. Batching still means two round trips where one would do.

<a id="be-z18"></a>

#### Seeing what your ORM actually does

You cannot reason about data access you cannot see, and every ORM will happily hide fifty queries behind four lines. Turn logging on before you need it.

``` text
// Log every statement in development:
new Sequelize(url, { logging: console.log })
prisma = new PrismaClient({ log: ['query'] })
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information)   // EF Core
logging.getLogger('sqlalchemy.engine').setLevel(logging.INFO)
spring.jpa.show-sql=true                                        # Hibernate
LOGGING = {'loggers': {'django.db.backends': {'level': 'DEBUG'}}}
```

``` csharp
// Better than reading logs: assert the COUNT in a test. This is what
// stops an N+1 being reintroduced by a refactor six months later.
test('order list issues one query', async () => {
  const counter = countQueries();
  await getOrderList({ limit: 50 });
  expect(counter.total).toBe(1);
});
```

``` text
// And in production, emit queries-per-request as a metric, not a log.
// Latency alone cannot distinguish "one slow query" from "forty fast
// ones", and those have completely different fixes ((see: Observability: three things, three jobs)).
```

> **\[Tip\]** **Read one query plan early in your career, and the abstraction stops being opaque.** `EXPLAIN ANALYZE` in Postgres, or the execution plan in SQL Server, tells you whether the database jumped straight to your rows or read the whole table.
>
> The specific number to look for is rows read versus rows returned. Reading 400,000 to return 30 means an index is missing or has the wrong column order, and no ORM setting will fix it.

<a id="be-z19"></a>

#### Transactions and what they actually guarantee

``` text
A transaction gives you all-or-nothing across several statements.
It does NOT, by default, give you protection from concurrent readers
and writers doing something surprising.

  READ COMMITTED     the usual default. You never read uncommitted data.
                     You CAN read different values twice in one transaction.
  REPEATABLE READ    rows you read stay stable within the transaction.
  SERIALIZABLE       as if transactions ran one after another. Slowest,
                     and may fail with a serialisation error you must retry.
```

``` csharp
// THE bug transactions do not prevent, in every language:
const stock = await db.query('SELECT qty FROM stock WHERE sku = $1', [sku]);
if (stock.qty < wanted) throw new Error('insufficient');
await db.query('UPDATE stock SET qty = qty - $1 WHERE sku = $2', [wanted, sku]);

// Two requests both read 3, both pass the check, both subtract 2.
// A transaction around this does not help: both reads are legal.
```

``` sql
-- The fix is to make the check and the write one atomic statement:
UPDATE stock SET qty = qty - $1 WHERE sku = $2 AND qty >= $1;
-- Zero rows affected means someone else got there first. Check the count.

-- And state the rule where nothing can bypass it:
ALTER TABLE stock ADD CONSTRAINT qty_non_negative CHECK (qty >= 0);
```

``` text
Two rules that hold in every stack:

  KEEP TRANSACTIONS SHORT. They hold locks. A transaction open across an
    HTTP call to a payment provider turns their slow day into your outage.

  NEVER DO EXTERNAL I/O INSIDE ONE. Do it before, or after, from an outbox.
    Not in the middle ((see: Work that outlives the request)).
```

> **\[Trap\]** **Optimistic concurrency is the pattern to reach for on user-editable data.** Add a version column, include it in the `WHERE` clause of every update, and treat zero affected rows as a conflict.
>
> Without it, two users editing the same record means the second silently overwrites the first, and nobody finds out. Every mature ORM supports this and most projects never switch it on.

### The boundary

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · The boundary</p>

<a id="be-z22"></a>

#### Validation: three different jobs

"Validation" covers three distinct checks that belong in three different places. Conflating them is why validation logic ends up scattered and duplicated.

``` text
1. SHAPE       is this well-formed? Right fields, right types, in range.
               At the HTTP boundary. Returns 400 or 422.
               "qty must be an integer between 1 and 10000"

2. AUTHORITY   may THIS caller do this to THIS thing?
               Needs identity and often the resource itself. Returns 403.
               "you may edit orders, but not this tenant's orders"

3. BUSINESS    is this legal given the current state of the world?
               In the domain. Returns 409 or 422.
               "the order is already shipped and cannot be cancelled"
```

``` csharp
// Shape validation at the edge, with a schema. Reject early and cheaply.
const CreateOrder = z.object({
  customerId: z.string().uuid(),
  lines: z.array(z.object({
    sku: z.string().min(3).max(50),
    qty: z.number().int().min(1).max(10_000),
  })).min(1).max(500),                    // ALWAYS bound a collection
});

// Business validation in the domain, where the state lives:
cancel() {
  if (this.status === 'shipped')
    throw new DomainError('Cannot cancel a shipped order');
  this.status = 'cancelled';
}
```

> **\[Trap\]** **Two rules that hold in every stack.** Client-side validation has zero security value — anyone can post whatever they like with curl — so every bound must exist server-side too. And an unbounded array is a denial-of-service vector before any business rule runs, so cap collection sizes and say so in the error.

> **\[Tip\]** **Return all the field errors at once, keyed by field path.** Returning the first failure means a form with three problems takes three round trips to fix. A map from `lines[0].qty` to a message lets a client highlight every field immediately ([Errors and status codes](06-06-http.md#be-e498)).

### Designing the API

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Designing the API</p>

<a id="be-z26"></a>

#### Resources, verbs, and what each one promises

HTTP verbs carry two guarantees that clients, proxies, and retry policies all rely on. Breaking them breaks infrastructure you do not control.

| Verb     | Safe | Idempotent      | Means                        |
|----------|------|-----------------|------------------------------|
| `GET`    | yes  | yes             | read; changes nothing        |
| `HEAD`   | yes  | yes             | headers only                 |
| `PUT`    | no   | yes             | replace at a known address   |
| `DELETE` | no   | yes             | remove; repeating is a no-op |
| `PATCH`  | no   | not necessarily | partial update               |
| `POST`   | no   | no              | anything else                |

``` text
Safe        means no side effects. A crawler, a proxy, or a browser
            prefetch may call it without asking.
Idempotent  means calling it twice has the same effect as once.
            This is what makes a retry safe.

// A GET that mutates state is a real bug, not a style issue. Something
// will eventually prefetch it.
```

``` sql
// Name resources as nouns, and let the verb carry the action:
  GET    /orders                  list
  POST   /orders                  create
  GET    /orders/1042             read one
  PUT    /orders/1042             replace
  DELETE /orders/1042             remove
  GET    /orders/1042/lines       a sub-collection

// For things that are genuinely not CRUD, a verb sub-resource is
// clearer than contorting the model:
  POST /orders/1042/cancel
  POST /orders/1042/refunds
// This is fine. "cancel" is a real business operation with its own
// rules and its own permissions. Do not force it into a PATCH.
```

> **\[Tip\]** **Nest at most one level deep.** `/customers/5/orders/1042/lines/7` is unusable: it is long, it duplicates information, and it forces every client to know a hierarchy that may change.
>
> If a resource has its own identifier, address it directly at `/lines/7`. Use nesting only where the child has no meaning without the parent.

<a id="be-z28"></a>

#### Idempotency and safe retries

Networks fail after the server acted but before the client heard back. The client cannot tell that apart from a request that never arrived, so it retries, and you get two orders.

``` yaml
POST /orders
Idempotency-Key: 8f14e45f-ea9b-4c5a-9e2f-1d3c7a2b6d40

// The client generates a key per logical operation and reuses it on
// every retry of that operation. The server:
//   1. has it seen this key? return the SAME response, do not act again
//   2. otherwise act, and record the key with the response
```

``` sql
-- The unique constraint is what makes it correct under CONCURRENCY.
-- A check-then-insert without one is a race, not a solution.
CREATE TABLE idempotency (
  key         text PRIMARY KEY,
  response    jsonb NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now()
);
-- Two simultaneous requests with the same key: one insert wins, the
-- other gets a constraint violation, catches it, and returns the
-- winner's response. Plus a job to expire old keys - they are only
-- useful for hours.
```

``` text
Three distinct failure modes, needing three different mechanisms:

  client retries after a lost response   → the key lookup
  two concurrent identical requests      → the unique constraint
  crash between saving and publishing    → the outbox ((see: Work that outlives the request))

// Handling only the first is the common case, and it is the one that
// still produces duplicates under load.
```

> **\[Tip\]** **Design the operation to be naturally idempotent where you can.** `PUT /orders/{client-generated-id}` needs no key table at all: the client picks the id, and a second attempt hits the same address.
>
> Letting clients supply identifiers feels wrong at first and removes an entire class of problem.

### Security beyond authentication

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Security beyond authentication</p>

<a id="be-z30"></a>

#### Injection: one bug, many surfaces

Injection is always the same mistake: data from outside is treated as code or structure. The surfaces differ; the cause does not.

``` text
// SQL — never build a query by concatenation. Ever.
db.query(`SELECT * FROM users WHERE email = '${email}'`);        // BROKEN
db.query('SELECT * FROM users WHERE email = $1', [email]);       // correct

// Parameters are not string escaping. The value is sent SEPARATELY from
// the statement, so it can never be parsed as SQL. That is why it is
// safe and escaping is not.
```

``` text
The other surfaces, same principle:

  COMMAND    passing user input to a shell. Use an argument array, never
             a shell string. Better: do not shell out.
  PATH       "../../etc/passwd" in a filename. Resolve the path, then
             verify it is still inside the directory you intended.
  TEMPLATE   user input rendered as a template expression - can lead to
             remote code execution in some engines.
  LOG        a newline in user input forges log entries. Structured
             logging avoids it ((see: Observability: three things, three jobs)).
  SSRF       a user-supplied URL that your SERVER then fetches. Validate
             the scheme, resolve the host, reject private and link-local
             ranges, and re-check after redirects.
```

``` text
// The one that catches people who already know about SQL injection:
// ORMs protect the VALUES and not always the STRUCTURE.
db.orders.orderBy(req.query.sort)                    // may interpolate raw
knex.raw(`ORDER BY ${req.query.sort}`)               // definitely does

// Column and table names cannot be parameterised. They must come from
// an allow-list ((see: Pagination, filtering, and sorting)).
```

> **\[Trap\]** **SSRF is the injection people miss, and it is worse in the cloud.** A user-supplied webhook URL, avatar URL, or "import from link" feature makes your server issue the request — from inside your network, with whatever access it has.
>
> Pointing it at the cloud metadata endpoint has been used to steal credentials from real production systems. Validate, resolve, reject private ranges, and send from an egress-restricted worker rather than from the process that can reach your database.

<a id="be-z31"></a>

#### Secrets, and how they leak

``` csharp
Where secrets actually leak, in rough order of frequency:

  committed to source control        and history keeps them forever
  in a log line                     a request body, a connection string
  in an error response              a stack trace with credentials
  in a crash dump                   dumps contain ALL of memory
  in a client-side bundle           anything sent to a browser is public
  in a container image layer        deleting a file in a later layer does
                                    not remove it from the earlier one
  in CI output                      build logs are often widely readable
```

``` text
The hierarchy, best first:

  1. NO SECRET AT ALL. A platform identity: the platform vouches for the
     workload and issues short-lived credentials. Nothing to leak.
  2. A secret manager, fetched at runtime, rotatable without a deploy.
  3. An environment variable injected by the platform.
  4. A config file outside the image.
  5. A config file in the image.            ← avoid
  6. In source control.                     ← never
```

``` text
// Password storage, since it still gets done wrong:
//   use a SLOW, salted KDF: argon2id, scrypt, or bcrypt
//   never a fast hash - SHA-256 is designed to be quick, which is
//     exactly wrong here
//   never encryption - it is reversible by design
//   compare in CONSTANT TIME, so response timing does not leak

// API keys are different: they have full entropy, so there is nothing
// to brute-force. A single SHA-256 is fine. Store the hash, show the
// key once, and prefix it ("acme_live_...") so leak scanners can spot it.
```

> **\[Tip\]** **Assume every secret currently in your system has leaked, and check.** Run a secret scanner over your repository history, not just the working tree. Rotate anything found, because deleting the commit does not help — the history is already cloned onto other machines.

<a id="be-z32"></a>

#### Transport, headers, and the browser

``` text
HTTPS everywhere, and:
  HSTS                      tells browsers never to try plain HTTP again
  redirect HTTP → HTTPS     but HSTS is what actually protects the first
                            request on a later visit
  TLS 1.2 minimum           1.3 preferred; disable older versions
```

``` yaml
Response headers that do real work:

  Content-Security-Policy       the single most effective XSS mitigation
  X-Content-Type-Options: nosniff   stop the browser guessing content type
  Cache-Control: private        on ANY authenticated response, or a shared
                                proxy may serve one user's data to another
  Strict-Transport-Security     HSTS
  Referrer-Policy               stop leaking URLs to third parties
```

``` yaml
// Cookies, if you use them for sessions:
Set-Cookie: session=...; HttpOnly; Secure; SameSite=Lax; Path=/

  HttpOnly    JavaScript cannot read it, so XSS cannot steal it
  Secure      HTTPS only
  SameSite    blocks most CSRF; Lax is the usual right choice

// And the corollary: do NOT store tokens in localStorage. Any XSS reads
// it and exfiltrates a credential valid until expiry. An HttpOnly cookie
// is unreadable by script even on a fully compromised page.
```

``` text
// CORS is frequently misunderstood, so plainly:
//   it is enforced by the BROWSER, to protect users
//   it does NOT protect your API - curl ignores it entirely
//   AllowAnyOrigin and credentials are mutually exclusive by spec
//   response headers are invisible to JavaScript unless listed in
//     Access-Control-Expose-Headers
//   without a preflight cache, every request pays an extra round trip
```

> **\[Trap\]** **Authorisation must be enforced server-side regardless of what the client does.** A hidden button, a disabled field, and a route guard prevent mistakes, not attacks. Anyone can call the endpoint directly with the same credential.
>
> Every rule enforced in the UI must be enforced again on the server, per resource ([Authentication and authorization](10-02-authentication-and-authorization.md#be-a185)).

<a id="be-z33"></a>

#### The shortlist

Most real breaches come from a small set of causes. This is the list worth checking on any service you own.

1.  **Broken access control.** Consistently the most common. An endpoint that checks authentication but not *which* record you are touching. Test the negative case: authenticate as user A, request user B's resource, assert it fails ([Authentication and authorization](10-02-authentication-and-authorization.md#be-a185)).
2.  **Injection.** Parameterise values; allow-list structure ([Injection: one bug, many surfaces](#be-z30)).
3.  **Leaked secrets.** Scan history, prefer platform identity ([Secrets, and how they leak](#be-z31)).
4.  **Vulnerable dependencies.** An unpatched library is the easiest way in. Automate the scan and actually merge the updates.
5.  **Security misconfiguration.** Debug mode in production, default credentials, an admin endpoint exposed, a public storage bucket.
6.  **Over-posting.** Binding a request body onto a domain object lets a client set any field it can name ([Binding: raw input to typed arguments](#be-z11)).
7.  **Verbose errors.** Stack traces and SQL fragments in responses are reconnaissance ([Errors and status codes](06-06-http.md#be-e498)).
8.  **Missing rate limits.** Credential stuffing and scraping both rely on their absence ([Rate limiting and protecting yourself](06-07-servers-proxies-load-balancers-and-diagnosing-the-network.md#be-g650)).
9.  **SSRF.** Any feature that fetches a user-supplied URL ([Injection: one bug, many surfaces](#be-z30)).
10. **No audit trail.** Not a breach on its own, and it is why you cannot tell what happened during one.

> **\[Why it matters\]** **Note that number one is not exotic.** Broken access control is an `if` statement somebody forgot, on one endpoint out of two hundred, and it is the most common serious vulnerability in real systems.
>
> Which means the highest-value security work available to most teams is not a tool. It is one parameterised test over the whole route table, asserting that one tenant cannot reach another's data ([Testing a backend](#be-z43)).

> **\[Tip\]** **Log security-relevant events as a deliberate category.** Logins, failures, permission denials, password changes, permission grants, and data exports. Enough to reconstruct what an account did, retained long enough to matter, and not containing the sensitive values themselves.

### Concurrency and work that outlives the request

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Concurrency and work that outlives the request</p>

<a id="be-z34"></a>

#### The four concurrency models

| Model                   | Used by                   | Wins at                       | Loses at                           |
|-------------------------|---------------------------|-------------------------------|------------------------------------|
| **Event loop**          | Node, Python asyncio      | I/O fan-out, low memory       | CPU-bound work blocks everything   |
| **Thread pool**         | .NET, Java, Spring        | mixed CPU and I/O             | shared-state bugs are possible     |
| **Green threads**       | Go, Java 21+ virtual      | simple code, high concurrency | still needs locks for shared state |
| **Process per request** | PHP, classic Rails/Django | total isolation, simplicity   | memory, no in-process cache        |

``` text
EVENT LOOP — one thread runs your code; the OS does the waiting.
  A CPU-heavy loop stalls EVERY request in that process.
  No shared-state bugs, because nothing runs concurrently.
  Scale by running one process per core.

THREAD POOL — many threads, real parallelism.
  CPU work parallelises for free.
  Shared mutable state is now possible, so you inherit locks, race
    conditions, and memory visibility concerns.

GREEN THREADS — the runtime multiplexes cheap tasks onto real threads.
  You write blocking-looking code and get async behaviour.
  Millions of concurrent tasks are viable.
  Shared state still needs protecting.

PROCESS PER REQUEST — nothing is shared, ever.
  Impossible to have a concurrency bug in your own code.
  Also impossible to have an in-process cache or connection pool that
    outlives a request, so both must be external.
```

> **\[Why it matters\]** **Neither trade is better; they are opposite.** The event loop bought safety by giving up CPU scaling. The thread pool bought CPU scaling by giving up safety.
>
> What follows practically: on an event loop, keep CPU work out of the request path or move it to a worker. On a thread pool, be disciplined about shared state and never block on async work ([Async, and the mistake every stack shares](#be-z35)).

<a id="be-z35"></a>

#### Async, and the mistake every stack shares

Async does not make anything faster. One database query takes exactly as long either way. What it changes is how many operations one process can have in flight while waiting.

``` yaml
Synchronous:   one thread per in-flight request, parked while waiting.
               1,000 concurrent requests = 1,000 threads = ~1 GB of stack.

Asynchronous:  one thread handles many in-flight requests, resuming each
               as its I/O completes.
               1,000 concurrent requests = a handful of threads.

// Which is why async matters at 10,000 requests/second and buys you
// nothing at 10 requests/minute.
```

``` csharp
// THE MISTAKE, in every language that has both models: blocking on async.
var result = GetDataAsync().Result;              // C#
const r = await someSync();                      // fine
future.get()                                     // Java, on a pool thread
asyncio.run(coro())                              // Python, inside a handler

// You take a cheap waiting operation and turn it back into an expensive
// parked thread. Under load, every pool thread parks, new work queues,
// and the symptom looks EXACTLY like a slow database.
// The tell: CPU near idle, latency high, request queue growing.
```

``` csharp
// The other universal mistake: unbounded fan-out.
await Promise.all(ids.map(id => fetchOne(id)));       // 10,000 at once
await Task.WhenAll(ids.Select(FetchOneAsync));        // same bug

// Everything downstream falls over, including systems you do not own.
// Bound it, always:
await Parallel.ForEachAsync(ids, new() { MaxDegreeOfParallelism = 8 }, ...);
// or a semaphore, or p-limit, or a worker pool with a fixed size.

// Size it from the DOWNSTREAM constraint - the connection pool, the
// partner's rate limit - not from your core count.
```

> **\[Trap\]** **Any `Promise.all` or `WhenAll` over a collection whose size you do not control is this bug waiting for a large input.** It works in testing with ten items and takes down a shared dependency with ten thousand.
>
> It is one of the few code changes that can cause an outage for other teams, which is why it deserves a review rule rather than good intentions.

<a id="be-z36"></a>

#### Work that outlives the request

Send an email, update a search index, notify a partner, generate a report. The user should not wait, and the work must not be lost.

``` csharp
// THE MISTAKE: fire and forget.
sendEmail(order);                         // no await, no record
_ = SendEmailAsync(order);                // same thing

// If the process dies, restarts, or is deployed over, the work is gone
// and nothing recorded that it was owed. Nobody finds out.
```

``` sql
// THE PATTERN: write the intent in the SAME transaction as the change.
// This is the outbox, and it is the same in every stack.

BEGIN;
  INSERT INTO orders (...) VALUES (...);
  INSERT INTO outbox (type, payload) VALUES ('order.placed', '{...}');
COMMIT;                                   -- both, or neither

// A separate worker then reads the outbox, does the work, marks it done.
// If it crashes mid-send, the row is still there and it runs again -
// which is why the work must be IDEMPOTENT.
```

``` text
Three properties every background job needs, in every language:

  IDEMPOTENT   it will run twice. A redelivery, a retry, a deploy
               mid-batch. Key the effect so a repeat is a no-op.

  RESUMABLE    it will be killed halfway. Progress must live in durable
               state, not in a local variable. A watermark - "last id
               processed" - is usually enough.

  OBSERVABLE   nobody watches a job that works, so the failure mode is
               SILENCE. Metrics are not optional here.
```

``` text
// The metric that actually catches a stopped worker is AGE, not count:
//   "oldest unprocessed item is 4 hours old"     ← actionable
//   "queue depth is 500"                          ← meaningless alone
// 500 might drain in ten seconds or have been stuck since Tuesday.
```

> **\[Trap\]** **Clock-triggered work needs coordination the moment you run more than one instance.** Every instance has the same clock, so every instance fires. A nightly job silently becomes three nightly jobs when someone scales the deployment, and nothing in the code changed.
>
> Use a scheduler that fires once (a platform scheduler, or a clustered one), or take a lock. And make the job idempotent anyway, because a lock with a timeout does not guarantee exclusion.

<a id="be-z37"></a>

#### Caching, and the three ways it goes wrong

``` text
Where a cache can live, cheapest first:

  the client        HTTP headers. The request never happens at all.
  a CDN             shared, close to users. Public data only.
  in-process        nanoseconds, but per-instance and lost on restart.
  distributed       Redis or similar. Shared, survives restarts, a
                    network hop away.
  the database      its own buffer pool. You get this for free.
```

``` yaml
Failure 1: THE STAMPEDE
  A popular entry expires. A hundred requests all miss, all rebuild it,
  all hit the database with the same expensive query.
  Fix: jitter the TTL so entries do not expire together, and let only
  one caller rebuild while others wait or serve stale.

Failure 2: THE LEAK ACROSS TENANTS
  A cache key without the tenant in it serves one customer's data to
  another. The single most damaging cache bug there is.
  Fix: tenant in every key. Also a version segment, so a shape change
  makes old entries unreachable instead of misinterpreted.
    app:v4:tenant:1042:orders:open

Failure 3: THE STALE WRITE
  Evicting before the commit lets a concurrent read repopulate the
  cache with the OLD value and keep it for the full TTL.
  Fix: commit first, THEN evict. Order matters.
```

``` text
// HTTP caching is the one people skip, and it is the cheapest:
//   ETag: "a3f9c21"          plus  If-None-Match on the next request
//   → 304 Not Modified, no body at all
// A version column from your database makes a perfect ETag.
// And on any authenticated response: Cache-Control: private
// Without it, a shared proxy may serve one user's data to another.
```

> **\[Tip\]** **Cache the shape you return, not your entities.** Serialising a tracked ORM entity either fails or drags half the object graph into your cache, and it couples your cache format to your schema.
>
> Cache the response type. It is smaller, it is stable, and it is what the caller actually wanted.

### Performance and scale

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Performance and scale</p>

<a id="be-z39"></a>

#### The order to optimise in

Ranked by how much they typically return. Work down the list; stop when it is fast enough.

1.  **Measure first.** Not optional and not a platitude. Roughly half of all optimisation effort is spent on code that was not the bottleneck ([Observability: three things, three jobs](#be-z42)).
2.  **Remove round trips.** N+1 queries, sequential awaits that could be parallel, chatty service calls. Almost always the largest single win ([N+1: the universal mistake](#be-z17)).
3.  **Fix the indexes.** One missing index can be a thousandfold difference on one query ([Seeing what your ORM actually does](#be-z18)).
4.  **Fetch less.** Only the columns and rows you use. Wide `SELECT *` queries cost in transfer, memory, and serialisation.
5.  **Cache what is stable.** Correctly keyed, with a deliberate invalidation story ([Caching, and the three ways it goes wrong](#be-z37)).
6.  **Move work out of the request.** If the user does not need to wait, do not make them ([Work that outlives the request](#be-z36)).
7.  **Reduce serialisation.** Large JSON payloads cost real CPU and memory on both ends.
8.  **Then, and only then, the code.** Allocations, algorithms, data structures.

``` text
// A worked example, same endpoint, same framework:
//
//   51 queries, entities loaded, 40 columns each      1,230 ms
//   1 query, projected to 4 columns, covering index      110 ms
//
// No unsafe code, no clever data structures, no rewrite. Four
// structural decisions ((see: The four ways to talk to a database)).
```

> **\[Trap\]** **Never optimise on averages.** A 40 ms mean latency with a 3-second p99 means one request in a hundred is dreadful, and that request is the one users remember and complain about.
>
> Look at p95 and p99. The mean hides exactly the behaviour you are being asked to fix.

<a id="be-z40"></a>

#### Scaling out, and what it multiplies

``` text
VERTICAL   a bigger machine. Simple, immediate, has a ceiling, and one
           machine is one failure domain.
HORIZONTAL more machines. No ceiling, and it requires your process to be
           stateless.

// "Stateless" specifically means: no state in the process that a
// subsequent request needs. So no in-memory sessions, no local file
// uploads, no in-process job queue, no local scheduler.
```

``` text
THE THING PEOPLE MISS: scaling out multiplies your load on everything
you did NOT scale.

  4 instances → 40 instances also means:
    10× the database connections    ((see: Connection pooling))
    10× the query rate against ONE database
    10× the cache-miss stampedes on the same keys
    10× the scheduled jobs, if a timer runs in-process
    10× the calls to every partner API

// The application tier scales elastically. The data tier does not, and
// it is almost always the real ceiling.
```

``` text
What has to change before horizontal scaling works:

  sessions          → a shared store, or stateless tokens
  uploaded files    → object storage, not local disk
  in-process cache  → fine, but per-instance and less effective
  scheduled jobs    → a platform scheduler or a lock, or they run N times
  background queues → an external broker
  websockets        → sticky routing, or a shared backplane
```

> **\[Tip\]** **Read replicas are the usual next step, with one rule.** Send reports and browsing to replicas; send anything that *feeds a write decision* to the primary.
>
> A stale read used to decide a write reintroduces the check-then-act bug with a replication lag window instead of a race window, and it is far harder to spot ([Transactions and what they actually guarantee](#be-z19)).

<a id="be-z41"></a>

#### Knowing your limits before your users do

``` text
Three different tests, frequently confused:

  LOAD test      expected traffic. Does it meet its latency target?
  STRESS test    increase until it breaks. WHERE does it break, and how?
  SOAK test      normal traffic for hours. Do leaks or drift appear?

// The stress test is the valuable one, because the answer tells you
// which resource binds first - and that is almost never the one people
// guess ((see: Connection pooling)).
```

``` text
// What to watch during a stress test, not just latency:
//   connection pool utilisation      usually the first thing to saturate
//   queue depth and its rate of growth
//   memory, and whether it returns after the load stops
//   error rate by TYPE - timeouts and 500s mean different things
//   the DEPENDENCY's metrics, not only yours
```

``` text
Failure modes, and which you want:

  GRACEFUL     reject excess work fast, keep serving the rest.
               Return 429 or 503 quickly and cheaply.
  UNGRACEFUL   accept everything, queue it, and slow to a crawl until
               timeouts cascade and nothing succeeds.

// Accepting work you cannot complete is worse than refusing it. A fast
// 503 lets a client retry elsewhere; a 30-second timeout wastes their
// budget and yours ((see: Rate limiting and protecting yourself)).
```

> **\[Why it matters\]** **Find the binding limit and alarm on approaching it, not on exceeding it.** Every system has one resource that saturates first: connections, threads, memory, a partner's rate limit, a queue's throughput.
>
> Knowing which one, and having a dashboard for it, converts a future incident into a capacity conversation. Not knowing means the first time you learn is during an outage.

### Running it

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Running it</p>

<a id="be-z42"></a>

#### Observability: three things, three jobs

|             | Answers                              | Cost                         |
|-------------|--------------------------------------|------------------------------|
| **Logs**    | what happened in this one case       | per line, and it adds up     |
| **Metrics** | what is happening in aggregate       | per unique label combination |
| **Traces**  | where the time went, across services | per span; usually sampled    |

``` csharp
// Structured logs, not string concatenation. The difference is whether
// you can QUERY them later.

// Bad — unqueryable
log.info(`Order ${id} for customer ${customerId} failed: ${err.message}`);

// Good — fields you can filter and group by
log.info('Order creation failed',
  { orderId: id, customerId, tenantId, error: err.message, traceId });
```

``` text
The four metrics that cover most of what you need:

  request rate                  how much traffic
  error rate                    how much of it is failing
  latency distribution          p50, p95, p99 — never the mean
  saturation                    pool usage, queue depth, memory

// The mean hides everything. A 40 ms mean with a 3-second p99 means
// one request in a hundred is terrible, and that is the one users
// remember and complain about.
```

``` text
// Two domain-specific metrics worth more than most generic ones:
//   queries per request     distinguishes "one slow query" from
//                           "forty fast ones" - completely different fixes
//   oldest unprocessed item age   the only reliable way to notice a
//                           background worker has silently stopped
```

> **\[Trap\]** **High-cardinality labels will bankrupt you.** Tagging a metric by user id, order id, or full URL path creates one time series per distinct value. A million users is a million series, and metric systems price and perform per series.
>
> Identifiers belong in logs and traces, which are per-event. Metrics get low-cardinality labels only: endpoint, status class, tenant if you have tens of them.

> **\[Tip\]** **A trace id on every log line and every error response is the highest-value thing in this section.** It costs almost nothing and it turns "a user says it was slow" into a single query. Without it, correlating a report to a request is guesswork.

<a id="be-z43"></a>

#### Testing a backend

| Test              | Covers                               | Speed             |
|-------------------|--------------------------------------|-------------------|
| Domain unit tests | rules, calculations, invariants      | milliseconds      |
| Integration tests | queries, migrations, constraints     | seconds           |
| API tests         | routing, binding, auth, status codes | seconds           |
| Contract tests    | assumptions about other services     | slow; run nightly |

``` csharp
// Use a REAL database for integration tests. In-memory or SQLite
// substitutes do not enforce the same constraints, do not translate the
// same SQL, and have no query planner - so they pass tests that
// production fails.

// Testcontainers exists in every major language and starts a real one
// in a container for the test run:
const pg = await new PostgreSqlContainer().start();      // Node
var db = new PostgreSqlBuilder().Build();                // .NET
with PostgresContainer() as pg:                          # Python
```

``` text
What is worth testing, in rough order of value:

  1. Anything with money, dates, or time zones. Use awkward inputs:
     33.33, a DST transition, a leap day, a midnight crossing.
  2. Every authorization rule, including the NEGATIVE case. One
     parameterised test asserting tenant A cannot read tenant B's
     resource, across every endpoint.
  3. Query counts, so an N+1 cannot be reintroduced ((see: Seeing what your ORM actually does)).
  4. Migrations, by running them in the test fixture.
  5. Error paths. They are the least tested and most user-visible.
```

> **\[Why it matters\]** **Mock at the boundary, not inside your own code.** Mock the payment provider, the email service, the clock. Do not mock your own repository to test your own service — you end up asserting that your mock behaves like your mock.
>
> Tests that mock heavily pass while the system is broken, because the mocks encode what you believed rather than what is true.

<a id="be-z44"></a>

#### Health checks and deploying safely

``` text
Two different checks, and conflating them causes outages:

  LIVENESS   "is this process alive?" Failing it RESTARTS the container.
             Must not depend on anything external.

  READINESS  "should this instance receive traffic?" Failing it removes
             it from the load balancer. May check dependencies.
```

``` csharp
// THE CLASSIC OUTAGE: a liveness probe that queries the database.
// The database has a slow minute. Every instance fails liveness.
// The orchestrator restarts them ALL, simultaneously.
// Now nothing is serving, and the restarts add load to the database
// that was already struggling.

// Liveness: does the process respond at all?
app.get('/health/live', (_, res) => res.send('ok'));

// Readiness: can it actually do useful work?
app.get('/health/ready', async (_, res) => {
  const ok = await db.ping().catch(() => false);
  res.status(ok ? 200 : 503).json({ database: ok });
});
```

``` text
Graceful shutdown, which most services get wrong:

  1. receive SIGTERM
  2. FAIL READINESS immediately, so the load balancer stops sending work
  3. keep serving in-flight requests
  4. finish or abandon background work cleanly
  5. close pools and connections
  6. exit

// Skip step 2 and the load balancer keeps sending requests to a process
// that is shutting down. Users see errors during every deploy.
// Note there is a delay between failing readiness and the balancer
// noticing - usually a few seconds. Sleep through it before step 3.
```

> **\[Tip\]** **Deploy strategy in one line each.** Rolling replaces instances gradually and needs old and new code to coexist, which is why migrations must be backwards compatible ([Schema migrations](07-05-schema-design-and-migrations.md#be-a98)). Blue-green switches all at once and makes rollback instant at the cost of double capacity. Canary sends a small share of traffic to the new version first, and is the only one that catches problems before everyone sees them.
>
> Whichever you pick, the thing to rehearse is the rollback. It is the only procedure you will need in a hurry.

### Structure and evolution

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Structure and evolution</p>

<a id="be-z45"></a>

#### One deployable or many

The usual framing — monolith versus microservices — hides the real variable. There are two independent questions, and conflating them is the mistake.

``` text
                    LOOSELY COUPLED          TIGHTLY COUPLED

  ONE DEPLOYABLE    modular monolith         "the big ball of mud"
                    ← the right default      ← what people flee

  MANY DEPLOYABLES  microservices            distributed monolith
                    ← real, expensive        ← the worst quadrant

// Coupling and distribution are SEPARATE decisions. The pain people
// attribute to monoliths is coupling, and splitting a coupled system
// into services keeps the coupling and adds a network.
```

``` text
What distribution actually costs, in full:

  no transactions across services → sagas and compensating actions
  every call can fail             → retries, timeouts, circuit breakers
  every call is 1 ms not 1 ns     → and N+1 is now over the network
  debugging needs tracing         → across process boundaries
  local development gets harder   → run twelve things, or mock eleven
  data consistency becomes eventual → and the UI must handle it
  one schema change spans repos   → and coordinated releases

// That is the bill. It is worth paying for genuine independent
// scaling or genuine team independence. It is not worth paying for
// tidiness.
```

**The honest default is a modular monolith:** one deployable, with hard internal boundaries enforced by tooling rather than good intentions.

``` text
src/
  modules/
    orders/          owns the orders schema. Exposes a contract.
    inventory/       owns the inventory schema.
    billing/
  shared/            value types only. No business logic.

-- The boundary that actually holds: one database schema per module,
-- and NO cross-schema foreign keys. Modules reference each other by
-- id through a published contract, not by joining tables.
```

> **\[Tip\]** **Enforce boundaries in the build, not in review.** An architecture test that fails when one module imports another's internals is a few lines and works forever. Code review does not, because the person reviewing does not always know the rule.
>
> Boundaries are cheap in an empty project and expensive to retrofit once every module queries every table. That asymmetry is the entire argument for doing it now.

<a id="be-z47"></a>

#### Events, and what they buy

``` text
SYNCHRONOUS call          "give me an answer now"
  simple, easy to debug, and the caller's fate is tied to the callee's.
  If it is down or slow, you are down or slow.

ASYNCHRONOUS event        "this happened; react if you care"
  the publisher does not know or care who listens. Resilient to the
  consumer being down. And now the caller cannot know the outcome.
```

``` text
Use synchronous when:            Use events when:
  you need the answer to            the work can happen later
    continue                        several parties care
  the caller must know it           the publisher should not know
    succeeded                         who reacts
  it is a query                     you want to decouple deployments
```

``` sql
// The universal rule for publishing events: write the intent in the
// SAME transaction as the change, then publish from there.

BEGIN;
  UPDATE orders SET status = 'placed' WHERE id = 1042;
  INSERT INTO outbox (type, payload) VALUES ('order.placed', '{...}');
COMMIT;

// Publishing directly after the commit means a crash in between loses
// the event with nothing recording that it was owed
// ((see: Work that outlives the request)).
```

``` text
What events force you to accept:

  AT-LEAST-ONCE delivery   consumers must be idempotent
  OUT-OF-ORDER arrival     compare event timestamps, not arrival order
  EVENTUAL consistency     and the UI must represent it honestly
  NO TRANSACTIONS          across the publish boundary
  A SCHEMA you cannot      change freely once others consume it
```

> **\[Trap\]** **Do not use events to avoid designing a boundary.** Publishing an event whose only consumer is one service that always reacts immediately is a synchronous call with extra latency, extra failure modes, and no traceability.
>
> Events earn their cost when there are several interested parties, or when the publisher genuinely must not know who reacts. Otherwise, call the thing.

<a id="be-z48"></a>

#### Keeping it changeable

Most systems do not become hard to change because of one bad decision. They accumulate small irreversibilities until nobody is willing to touch anything.

``` text
The things that make change cheap, in order of value:

  1. TESTS ON THE RULES, not on the plumbing. A test suite that breaks
     when you rename a method but not when you break a discount
     calculation is worse than none, because it costs work and buys
     no confidence ((see: Testing a backend)).

  2. BUSINESS LOGIC CALLABLE WITHOUT HTTP. This is what makes a
     framework migration a small job instead of a rewrite
     ((see: Where the logic lives)).

  3. BACKWARDS-COMPATIBLE MIGRATIONS. Expand, backfill, contract.
     Three deploys, no downtime ((see: Schema migrations)).

  4. OUTPUT TYPES SEPARATE FROM ENTITIES. So a schema change is not
     automatically an API change ((see: API contracts and versioning)).

  5. AN ANTI-CORRUPTION LAYER at every external boundary. Translate
     someone else's model into yours at the edge, once, so their
     structure never spreads through your code.
```

``` csharp
// And the things that quietly remove your options:

//   a public setter on a value that should be derived
//   a status enum tracking several independent facts at once
//   business rules in a handler, unreachable from a job
//   a foreign key across a module boundary
//   an external structure used directly as a domain type
//   a shared table between two deployables
//   a stored value that could have been computed

// None is fatal alone. Together they are why "we cannot change that"
// becomes the answer to everything.
```

> **\[Why it matters\]** **The most useful habit is writing down the decision, not the design.** A short record per significant choice: what we decided, what we considered, why, and what it costs.
>
> In eighteen months someone will ask why the system is like this, and "nobody remembers" is what turns a revisitable decision into a permanent constraint. A paragraph written at the time prevents that, and it also tells you when the decision is due for review — when the cost you wrote down stops being acceptable.

> **\[Tip\]** **A closing bias worth holding.** Prefer the boring choice, the smaller number of moving parts, and the decision you can reverse. Most systems are limited by how quickly they can be changed safely, not by how elegant they were on day one ([The claim this document makes](#be-z1)).

### How it comes together

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · How it comes together</p>

<a id="be-z50"></a>

#### Where the time went

The same operation twice. Same framework, same database, same features. Every difference is a structural decision made at one of the steps above.

| Step                  | Careless      | Careful     | What changed                   |
|-----------------------|---------------|-------------|--------------------------------|
| Client requests       | 240 ms        | 80 ms       | one call, not three sequential |
| Connection setup      | 120 ms        | 0 ms        | connection reuse               |
| Pipeline and auth     | 3 ms          | 3 ms        | —                              |
| Validation and domain | 2 ms          | 2 ms        | —                              |
| Queries issued        | 51            | 1           | projection instead of N+1      |
| Database time         | 410 ms        | 12 ms       | covering index, one round trip |
| Materialisation       | 95 ms         | 8 ms        | no tracking, fewer columns     |
| Serialisation         | 60 ms         | 6 ms        | output type, cached options    |
| Notifications         | 300 ms        | 0 ms        | outbox, not inline             |
| **Total**             | **~1,230 ms** | **~110 ms** |                                |

``` text
Where the time actually was, in the careless column:

  waiting on another machine        770 ms   (63%)
  turning rows into objects          95 ms
  turning objects into bytes         60 ms
  running your business logic          2 ms   (0.2%)

// Which is the argument against optimising application code first.
// Halving the logic saves one millisecond ((see: Where the time actually goes)).
```

> **\[Why it matters\]** **Every improvement in the right column is a boundary crossed less often, or with less carried across it.** Fewer round trips, fewer columns, fewer objects, less deferred work made synchronous.
>
> No unsafe code, no clever data structures, no framework change. That is the shape of essentially all real performance work, and it is why the ideas in Part 2 are worth more than any technique in Part 9.

<a id="be-z51"></a>

#### The same request, going wrong

The path above assumed success. Here is the same operation under the four failure modes you will actually meet, and what each one requires of the design.

``` yaml
1. THE CLIENT RETRIES AFTER A LOST RESPONSE
   The order was created; the response never arrived. The client
   cannot tell, so it retries ((see: Failure, and the thing you cannot know)).
   Requires: an idempotency key, checked before acting, returning
   the SAME response rather than acting again.

2. TWO REQUESTS ARRIVE AT ONCE
   Both check stock, both see enough, both write. A transaction does
   not prevent this — both reads were legal ((see: Consistency is a set of dials)).
   Requires: a conditional write, or a unique constraint, or a
   version check. Something the DATABASE enforces atomically.

3. THE PROCESS DIES AFTER COMMIT, BEFORE NOTIFYING
   The order exists. The email was never sent, and nothing recorded
   that it was owed.
   Requires: the outbox — the intent committed with the change, and a
   worker that retries ((see: Work that outlives the request)).

4. A DEPENDENCY GOES GREY
   The address validator now takes forty seconds. Requests hold
   threads, then connections, then the pool is exhausted — and
   endpoints that never call it start failing ((see: Failure, and the thing you cannot know)).
   Requires: a timeout shorter than your request budget, a circuit
   breaker, and a decision about what to do when it is unavailable.
```

``` csharp
// The fourth one deserves emphasis because it is where the root cause
// is a MISSING DECISION rather than a missing mechanism.
//
// Nobody ever asked "what should happen if address validation is
// down?" So the default answer became "take down checkout", and that
// default was invisible until the day it ran.
//
// The fix is not only a timeout. It is answering the question:
try { addr = await validate(input); }
catch (TimeoutError | CircuitOpenError) {
  log.warn('validation unavailable; accepting unvalidated');
  addr = input.asUnvalidated();          // degrade, do not fail
}
// For a shipping address, degrading is right. For a payment
// authorisation, failing is right. Decide per call site.
```

> **\[Tip\]** **Four failures, four different mechanisms.** A key, a constraint, an outbox, a timeout. Teams routinely implement one of the four and believe they have handled retries.
>
> They are not variants of one problem. They are four distinct consequences of one fact: the machine boundary makes outcomes unobservable ([Everything interesting happens at a boundary](#be-z3)).

<a id="be-z52"></a>

#### What to actually hold in your head

The compressed version. If you retain nothing else from this document, retain this.

``` text
THE FIVE IDEAS
  cost and failure live at boundaries              (see: Everything interesting happens at a boundary)
  difficulty is mutable shared state              (see: State is the thing that makes it hard)
  consistency is dials, chosen per operation      (see: Consistency is a set of dials)
  remote outcomes are sometimes unknowable        (see: Failure, and the thing you cannot know)
  there are three clocks and two time domains     (see: Time: three clocks, none of them trustworthy)

THE FIVE NUMBERS
  method call            ~1 ns
  memory read          ~100 ns
  local DB query        ~0.5 ms       = 500,000 method calls
  same-region HTTP        ~1 ms
  cross-continent       ~200 ms       = 400 local queries

THE FIVE HABITS
  bind to input types, never to domain objects
  one query per operation, projected to what you use
  write intent in the same transaction as the change
  put a timeout on every outbound call
  inject the clock

THE FIVE QUESTIONS FOR ANY ENDPOINT
  how many round trips does this make?
  what happens if it runs twice?
  what happens if the dependency is slow, not down?
  is the tenant a predicate, or a check afterwards?
  could a background job reuse this logic without HTTP?
```

> **\[Why it matters\]** **Why this list and not a longer one.** Everything else in this document is derivable from it. Pagination follows from round trips. The outbox follows from unknowable outcomes. Lifetimes follow from state. Optimistic concurrency follows from the isolation dial.
>
> Which is the difference between remembering a framework's API and understanding backend work: one list is finite and stable, and the other turns over every few years ([What does not transfer](#be-z57)).

> **\[Tip\]** **A way to use this if you are refreshing rather than learning.** Take a service you already own and answer the five questions for its three busiest endpoints. Then check the five habits.
>
> The gaps you find will not be evenly distributed. Most experienced teams are strong on two or three of the habits and have never systematically applied the others, and the unapplied ones are where the next incident is.

### The mapping

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · The mapping</p>

<a id="be-z55"></a>

#### The same concept, eight frameworks

| Concept           | ASP.NET Core                      | Spring Boot               | Express          | NestJS                            |
|-------------------|-----------------------------------|---------------------------|------------------|-----------------------------------|
| Entry point       | `Program.cs`                      | `@SpringBootApplication`  | `app.js`         | `main.ts`                         |
| Route             | `MapGet` / controller             | `@GetMapping`             | `app.get`        | `@Get()`                          |
| Pipeline step     | middleware                        | filter, interceptor       | middleware       | middleware, guard, interceptor    |
| DI                | `IServiceCollection`              | `@Component`              | manual           | `@Injectable`                     |
| Per-request scope | `AddScoped`                       | `@RequestScope`           | closure / `req`  | `Scope.REQUEST`                   |
| Config            | `IConfiguration`                  | `application.yml`         | `process.env`    | `ConfigModule`                    |
| Validation        | DataAnnotations, FluentValidation | Bean Validation           | Zod, Joi         | `ValidationPipe`                  |
| ORM               | EF Core, Dapper                   | JPA/Hibernate, jOOQ       | Prisma, Drizzle  | TypeORM, Prisma                   |
| Migrations        | EF Migrations                     | Flyway, Liquibase         | Prisma Migrate   | TypeORM migrations                |
| Background work   | `BackgroundService`               | `@Scheduled`, `@Async`    | BullMQ           | `@nestjs/bull`                    |
| Testing           | xUnit + `WebApplicationFactory`   | JUnit + `@SpringBootTest` | Jest + Supertest | Jest + `Test.createTestingModule` |

| Concept           | Django                 | FastAPI                | Rails                          | Go (stdlib)            |
|-------------------|------------------------|------------------------|--------------------------------|------------------------|
| Entry point       | `manage.py`, `wsgi.py` | `main.py`              | `config/application.rb`        | `main.go`              |
| Route             | `urls.py`              | `@app.get`             | `routes.rb`                    | `mux.HandleFunc`       |
| Pipeline step     | middleware             | middleware, dependency | Rack middleware, filter        | handler wrapper        |
| DI                | none; app registry     | `Depends()`            | none; conventions              | explicit construction  |
| Per-request scope | `request` object       | `Depends` per request  | `request`, `CurrentAttributes` | `context.Context`      |
| Config            | `settings.py`          | `pydantic-settings`    | `credentials`, ENV             | `os.Getenv`, Viper     |
| Validation        | forms, serializers     | Pydantic               | model validations              | manual, or `validator` |
| ORM               | Django ORM             | SQLAlchemy             | ActiveRecord                   | `sqlc`, GORM           |
| Migrations        | built in               | Alembic                | built in                       | `golang-migrate`       |
| Background work   | Celery                 | Celery, ARQ            | Sidekiq, ActiveJob             | goroutine + queue      |
| Testing           | `TestCase`             | `TestClient`           | Minitest, RSpec                | `testing`, `httptest`  |

> **\[Tip\]** **Read the table by row, not by column.** Each row is one problem from this document. Once you know the problem, you are looking up a name rather than learning a concept, and that is the difference between a week and an afternoon.

<a id="be-z56"></a>

#### Learning a new framework in a day

Answer these fourteen questions and you can work in any backend framework. They are ordered so each answer makes the next easier to find.

1.  **Where does the application start?** Find the file that boots it and read it end to end.
2.  **How are routes declared?** Explicit, annotated, or convention ([Routing: matching a request to code](#be-z9))?
3.  **How do I add something to the pipeline, and in what order does it run** ([The pipeline: middleware by any name](#be-z10))?
4.  **How does a handler get its dependencies** ([Dependency injection, and what it is actually for](#be-z12))?
5.  **What are the lifetimes called, and which is the default** ([Object lifetimes: the bug that only shows under load](#be-z13))?
6.  **Where does configuration come from, and what overrides what** ([Configuration and secrets](11-04-ci-cd-pipelines.md#be-g701))?
7.  **How is the request body turned into a typed object, and where does validation go** ([Validation: three different jobs](#be-z22))?
8.  **What is the data access story, and can I see the SQL** ([Seeing what your ORM actually does](#be-z18))?
9.  **How do I open a transaction, and what is the isolation default** ([Transactions and what they actually guarantee](#be-z19))?
10. **How are migrations created and applied** ([Schema migrations](07-05-schema-design-and-migrations.md#be-a98))?
11. **How do errors become responses, and where is the one place to handle them** ([Errors and status codes](06-06-http.md#be-e498))?
12. **How do I run work outside a request** ([Work that outlives the request](#be-z36))?
13. **How do I write a test that hits a real route** ([Testing a backend](#be-z43))?
14. **What is the concurrency model, and what does that forbid** ([The four concurrency models](#be-z34))?

> **\[Why it matters\]** **Question 14 is the one people skip and should not.** It determines what you are allowed to do. On an event loop, a CPU-heavy handler stalls every other request in the process. On a thread pool, a shared mutable dictionary is a race condition. On process-per-request, an in-process cache does not survive.
>
> Those are not style preferences. They are constraints, and violating them produces bugs that only appear under load.

> **\[Tip\]** **A faster route than the tutorial: read the framework's own test suite.** It shows real usage without the narrative simplifications tutorials make, and it tells you what the maintainers consider the supported way to do things.

<a id="be-z57"></a>

#### What does not transfer

The honest counterweight to this whole document. Four things are genuinely different, and assuming otherwise is how experienced developers write bad code in a new stack.

``` text
1. THE CONCURRENCY MODEL
   Not a detail, not abstractable. Code that is correct on an event loop
   can be a race condition on a thread pool, and CPU work that is fine
   on a thread pool stalls an event loop entirely ((see: The four concurrency models)).

2. WHETHER TYPES EXIST AT RUNTIME
   TypeScript types are erased; C#, Java, and Python annotations are not.
   Erased types mean binding, validation, serialisation, and ORM mapping
   each need an explicit schema. This is why Node needs Zod at every
   boundary and .NET does not ((see: Binding: raw input to typed arguments)).

3. HOW THE PROCESS MODEL WORKS
   Whether state can outlive a request at all. A module-level cache is
   normal in Node and .NET, and useless in classic PHP. A connection
   pool assumes a long-lived process, which serverless breaks
   ((see: Connection pooling)).

4. ECOSYSTEM CULTURE
   How much the framework decides for you. Rails and Django have strong
   opinions and you fight them at your cost. Express and Go have almost
   none, so your team must supply structure. Neither is better; they
   demand different things from you.
```

And two things that look framework-specific but are not, worth stating because people assume the opposite.

``` text
SQL and the database    entirely transferable. Indexes, query plans,
                        transactions, isolation levels, and N+1 behave
                        the same regardless of the language calling them.
                        This is the most durable knowledge in backend
                        work, and the least language-dependent.

HTTP                    equally transferable. Status codes, caching
                        headers, conditional requests, and content
                        negotiation are the protocol, not the framework.
```

> **\[Why it matters\]** **Which suggests where to spend your learning time.** Frameworks turn over every few years. SQL, HTTP, and the ability to reason about concurrency and data access do not.
>
> Someone who understands query plans and transaction isolation is useful in any stack for decades. Someone who knows one framework's API deeply and nothing underneath it has to relearn their value every time the industry moves ([The claim this document makes](#be-z1)).

> **\[Tip\]** **The practical version of that advice:** go deep in one backend ecosystem so you have somewhere to reason from, learn a second well enough to be useful in it, and go genuinely deep on the database and the protocol instead of on a third framework.

### How These Topics Connect

<p class="src">From <em>.NET Backend Deep Dive</em> · How These Topics Connect</p>

<a id="dn-0"></a>

They aren't separate subjects. One real request touches all of them:

```
Client
  │  presigned URL / JWT (SSO)
  ▼
API Gateway  ──── 29s timeout, 10MB payload, throttling
  ▼
ASP.NET Core
  ├─ Middleware: auth, correlation id
  ├─ OpenTelemetry: create Activity/span, propagate traceparent
  ├─ Serilog: structured logs + attribute redaction + TraceId enrichment
  ▼
Service (Scoped)
  ├─ HybridCache: L1 memory + L2 Redis, stampede protection
  ├─ Task.WhenAll -> requires IDbContextFactory, never a shared DbContext
  ▼
EF Core
  ├─ Interceptor: audit columns + soft delete (Deleted -> Modified)
  ├─ Query filter: IsDeleted = 0 (+ filtered index)
  ├─ Projection + AsNoTracking + AsSplitQuery -> no N+1, no cartesian blowup
  ▼
SQL Server ──── indexes, execution plans, RCSI, keyset paging
  │
  └─ Outbox table ──> BackgroundService (singleton + CreateAsyncScope)
                        └──> Message bus (event) ──> idempotent consumer
```

Running through all of it: every allocation adds GC pressure, every `.Result` consumes a thread pool thread, and every unnecessary query is latency that OTel will point straight at.

---

### Start here

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Start here</p>

<a id="be-m1"></a>

#### How to use this guide

This is a reference, not a book. Nobody should read it front to back. Every section stands alone, states its cost model, and links to what it depends on — so the intended use is to arrive from a search, a cross-reference, or one of the paths below.

Four ways in

| If you want to…                 | Start at                                                                                                             |
|---------------------------------|----------------------------------------------------------------------------------------------------------------------|
| **Fix something slow**          | [The review checklist these cases produce](14-05-what-actually-goes-wrong-field-guide-and-checklists.md#be-m306) — the ordered checklist, then [Anti-pattern catalogue](04-08-anti-patterns.md#be-a83) |
| **Understand a specific thing** | the search box, or the sidebar. Press `/` to search full text.                                                       |
| **Build the foundations**       | read Parts 1–4 in order: computer, network, memory, runtime                                                          |
| **Review a design or a PR**     | [Anti-pattern catalogue](04-08-anti-patterns.md#be-a83), then the trap index in the appendix                                               |

Reading paths

``` text
FOUNDATIONS        §3 how a computer works
                   §12 how machines talk
                   §21 how .NET stores data
                   §37 how .NET runs
                   Long, but everything later assumes it.

DATA STRUCTURES    §98 collections
AND ALGORITHMS     §114 structures you build yourself
                   §123 algorithms at scale

LINQ AND SQL       §138 LINQ
(the guide's       §155 where LINQ becomes SQL
spine)             §173 EF Core in depth
                   §180 SQL by hand
                   §194 inside the storage engine
                   Read §155 first if nothing else.

BUILDING A SERVICE §228 how an app is wired
                   §236 API surfaces
                   §244 modules and services
                   §253 domain modeling
                   §267 observability and delivery

COPY A PATTERN     §284 bulk import
                   §285 list endpoint, done properly
                   §286 idempotent command handler
                   §289 a worker that does not lose work
                   Ten complete, realistic implementations.

WHEN IT BREAKS     §293 twelve worked cases, diagnosed and fixed
                   §276 optimization, ranked by payoff
```

Conventions

| Marker         | Means                                                                  |
|----------------|------------------------------------------------------------------------|
| Why            | the reasoning behind a rule, or when it does not apply                 |
| Tip            | the practical thing to actually do                                     |
| Trap           | a specific way this goes wrong, usually silently                       |
| `§N`           | a link to another section — numbers are generated, so they never drift |
| Worked example | a link to the case study that demonstrates the section                 |

> **\[Tip\]**
>
> **Keyboard:** `/` focuses search (it matches full section text, not just titles), `j` and `k` move between sections, `t` toggles the theme, `Esc` clears. Every heading has a permalink on hover, and every code block has a copy button.

> **\[Why it matters\]**
>
> **On the numbers in this guide.** Latency figures, sizes, and speed-ups are representative of systems of the shape described, not measurements from one specific application. The case studies say so explicitly ([The system we will be fixing](14-03-worked-cases-from-production.md#be-m293)). Treat ratios and diagnostic paths as the content; verify absolute figures against your own system, which [Seeing it yourself: measuring layout and allocation](01-05-memory.md#be-m36) and [Measure first: tools and method](12-04-scaling-and-performance.md#be-m275) show how to do.

> **\[Trap\]**
>
> **Where this guide will go out of date fastest:** EF Core behaviour that changed between major versions, .NET APIs introduced in 8 and 9, and anything about a specific database engine's optimiser. The reasoning survives; the specifics deserve a check against your actual version before you rely on them.

<a id="be-m2"></a>

#### Numbers worth knowing

One page of magnitudes. Not for precision — for calibration, so an estimate is wrong by 2× rather than by 10,000×.

Latency, scaled so the ratios are visible

| Operation                 | Time         | × L1            |
|---------------------------|--------------|-----------------|
| L1 cache reference        | 1 ns         | 1               |
| Branch mispredict         | 5 ns         | 5               |
| L3 cache reference        | 12 ns        | 12              |
| Main memory reference     | 80 ns        | 80              |
| System call               | 100–1,000 ns | ~500            |
| Context switch            | 1–10 µs      | ~5,000          |
| Exception throw + catch   | 5–50 µs      | ~20,000         |
| NVMe read                 | 100 µs       | 100,000         |
| Redis round trip, same AZ | 0.3 ms       | 300,000         |
| SQL round trip, same AZ   | 0.5–2 ms     | ~1,000,000      |
| Cross-region round trip   | 50–180 ms    | ~10<sup>8</sup> |

Sizes and limits

| Thing                             | Value                          |
|-----------------------------------|--------------------------------|
| Cache line                        | 64 bytes                       |
| Memory page / database page       | 4 KB / **8 KB**                |
| Object header (x64)               | 16 bytes; minimum object 24    |
| Reference / `Guid` / `decimal`    | 8 / 16 / 16 bytes              |
| Large Object Heap threshold       | 85,000 bytes                   |
| Default thread stack              | 1 MB (Windows and thread pool) |
| Network MTU                       | ~1,500 bytes                   |
| SQL Server max row (in-row)       | 8,060 bytes                    |
| SQL Server parameters per command | **2,100**                      |
| Lock escalation threshold         | ~5,000 locks on one object     |
| Default ADO.NET max pool size     | 100 per connection string      |
| Ephemeral ports available         | ~28,000; `TIME_WAIT` ~60 s     |
| Index scan-vs-seek tipping point  | ~1–2% of the table             |

Rules of thumb

``` csharp
Hand-written loop vs equivalent LINQ chain      2-5x
Struct array vs class array (iteration)         3-10x  (locality, §22)
List.Contains in a loop vs HashSet              O(n*m) -> O(n+m)
Projection vs fetching whole entities           5-10x fewer bytes
Pooled connection vs new connection             ~20x on the first hop
JSON compressed vs raw                          5-10x smaller
decimal vs double arithmetic                    10-20x slower
Reflection property get vs direct field         ~100x slower
Availability of a 5-service chain at 99.9% each 99.5%
```

> **\[Why it matters\]**
>
> **How to use this.** Before optimising, estimate. If your guess and the measurement disagree by orders of magnitude, the guess located the real problem — usually a layer you had not considered. The most common miscalibration is treating a network round trip as if it were a function call; it is closer to a million of them ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705)).
