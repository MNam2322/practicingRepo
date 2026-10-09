---
id: "15-2"
number: "15.2"
title: "Terminology by domain"
part: 15
part_title: "Reference"
words: 8685
minutes: 38
---

# 15.2 Terminology by domain

> **Quick look.** Terminology tables grouped by domain, so related terms sit side by side.
>
> - Data shapes: DTO, entity, value object, aggregate, view model, projection.
> - Architecture, runtime, DI, async, data access, messaging, operations and testing vocabularies.
> - C# language terms and every acronym expanded.
> - Platform terms: networking, HTTP and TLS, proxies, Linux, containers, storage, deployment.
> - Pattern, design-principle, domain and distributed-systems vocabularies.
> - Example of a distinction that matters: a stub supplies data, a mock asserts a call was made.


### Terminology

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Terminology</p>

<p class="pd">Plain-English definitions for the vocabulary used throughout this guide. Grouped by domain rather than alphabetically, so related terms sit together and the distinctions between them are visible. Use browser find (Ctrl/⌘+F) to jump to a specific word.</p>

<a id="be-a1"></a>

#### Data shapes: DTO, entity, model and the rest

This group causes more confusion than any other, because the words are used loosely in conversation and precisely in code. The underlying question each answers is: *what is this object for, and who is allowed to depend on it?*

| Term                            | What it means                                                                                                                                                         | Why it exists                                                                                                                                                 |
|---------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **DTO** (Data Transfer Object)  | A plain object whose only job is to carry data across a boundary — over HTTP, onto a queue, between layers. No behaviour, no rules                                    | So the shape you expose is decoupled from the shape you store. Renaming a database column then does not break your API                                        |
| **POCO** (Plain Old CLR Object) | An ordinary class with no inheritance from a framework base type and no framework attributes required                                                                 | It means “this class is not tied to any library”. Contrast with older frameworks that forced you to inherit from their base classes                           |
| **Entity**                      | An object with an identity that persists over time — a specific customer, a specific order. Two entities with the same values are still different if their ids differ | Identity is what lets you say “update *this* order”. It usually maps to a database row                                                                        |
| **Value object**                | An object defined entirely by its values, with no identity. Money, a date range, an address. Two with equal values *are* the same                                     | You can freely copy and replace them. €10 is €10; there is no “which €10”                                                                                     |
| **Model**                       | Deliberately vague in general use. In this guide: the objects representing your business concepts                                                                     | The word is overloaded — “model” means something different in MVC, in EF Core, and in machine learning. Always check the context                              |
| **View model**                  | An object shaped for one specific screen or response, often combining data from several sources                                                                       | A screen rarely needs exactly one entity. A view model lets the UI have the shape it wants without distorting the domain                                      |
| **Aggregate**                   | A cluster of entities and value objects treated as one unit for changes — an `Order` with its `OrderLines`                                                            | It defines a consistency boundary: the whole aggregate is saved together, and rules spanning it can be enforced                                               |
| **Aggregate root**              | The single entity in an aggregate that outside code is allowed to reference                                                                                           | Nothing reaches into an `OrderLine` directly; everything goes through the `Order`, so the order can enforce its own rules                                     |
| **Record**                      | A C# language feature: a type with value-based equality and concise syntax, usually immutable                                                                         | Ideal for DTOs and value objects, because equality and immutability are what you want there anyway                                                            |
| **Projection**                  | Selecting a subset or reshaping of data, rather than loading whole objects                                                                                            | Loading 3 columns instead of 40 is faster. In EF Core this is a `Select()` into a DTO                                                                         |
| **Anemic domain model**         | Classes containing only data, with all the logic sitting in separate service classes                                                                                  | Named as a criticism, though it is extremely common and often fine. The objection is that rules end up scattered rather than living with the data they govern |
| **Mapping**                     | Converting between two shapes — entity to DTO, DTO to entity                                                                                                          | The cost of keeping shapes separate. Done by hand, or by a library such as Mapperly or AutoMapper                                                             |

> **\[Note\]**
>
> **The distinction that matters most.** An **entity** is how you *store* something. A **DTO** is how you *transmit* it. A **view model** is how you *display* it. Collapsing all three into one class is the single most common shortcut, and the cost arrives later: your database schema becomes your public API, so you cannot change one without breaking the other.

<a id="be-a2"></a>

#### Architecture vocabulary

| Term                                                | What it means                                                                                                                                        | Note                                                                                                  |
|-----------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------|
| **Layer**                                           | A logical grouping of code by responsibility — presentation, application, domain, infrastructure                                                     | Logical. All layers can live in one process                                                           |
| **Tier**                                            | A physical deployment unit — a separate machine or process                                                                                           | “Three-layer” and “three-tier” are *not* synonyms, though they are used as such constantly            |
| **Separation of concerns**                          | Each part of the code has one reason to change                                                                                                       | The principle behind almost every other item in this table                                            |
| **Coupling**                                        | How much one piece of code depends on another                                                                                                        | Low coupling means you can change one thing without changing five others                              |
| **Cohesion**                                        | How closely related the things inside one unit are                                                                                                   | High cohesion means a class does one job. Low cohesion is a `Helpers` class                           |
| **Cross-cutting concern**                           | Something needed everywhere — logging, authorisation, validation, caching                                                                            | Handled by middleware, filters or decorators, rather than repeated in every method                    |
| **Middleware**                                      | A component in a pipeline that each request passes through, in order                                                                                 | In ASP.NET Core, authentication, routing and error handling are all middleware                        |
| **Hexagonal architecture** / **ports and adapters** | The domain sits in the middle and defines interfaces (“ports”); databases, APIs and UIs are interchangeable implementations (“adapters”) at the edge | Same idea as Clean and Onion architecture, differently drawn                                          |
| **Clean architecture** / **Onion architecture**     | Concentric layers where dependencies point *inward*, so the domain depends on nothing                                                                | The practical consequence: your business logic has no `using` for a database library                  |
| **Vertical slice architecture**                     | Organising by feature rather than by layer — everything for “place order” in one folder                                                              | Reduces the file-hopping that layered structures cause                                                |
| **CQRS** (Command Query Responsibility Segregation) | Separating the code paths that *change* data from those that *read* it                                                                               | Does *not* require two databases. At its simplest it is just separate classes for reads and writes    |
| **Event sourcing**                                  | Storing the sequence of things that happened, rather than only the current state                                                                     | You can reconstruct any past state. Frequently confused with CQRS, which is a separate idea           |
| **Repository**                                      | An abstraction that makes data access look like a collection — `Add`, `Remove`, `FindById`                                                           | Hides the storage mechanism. Whether it is worth adding on top of EF Core is a genuine ongoing debate |
| **Unit of work**                                    | Tracks a set of changes and commits them together as one transaction                                                                                 | EF Core's `DbContext` already is one, which is why wrapping it in another is often redundant          |
| **Service**                                         | Extremely overloaded. Can mean a class holding logic, a background worker, a deployed application, or an interface in DI                             | Always establish which sense is meant. “Service” alone conveys almost nothing                         |
| **Facade**                                          | One simple interface in front of several complicated ones                                                                                            | So callers do not need to orchestrate five things themselves                                          |
| **Gateway**                                         | A component that sits in front of other services, routing and often handling auth and rate limits                                                    | An API gateway is the common case                                                                     |
| **BFF** (Backend For Frontend)                      | A backend built for one specific client — one for web, one for mobile                                                                                | Each client gets exactly the shape it needs, instead of one API compromising for all                  |
| **Monolith**                                        | One deployable application containing everything                                                                                                     | Not an insult. A well-structured monolith beats badly split microservices                             |
| **Modular monolith**                                | One deployable application with strictly enforced internal boundaries                                                                                | Often the right answer: the boundaries of microservices, without the distributed-systems cost         |

> **\[Trap\]**
>
> **Common trap.** Beware of using “layer” and “tier” interchangeably in a design discussion. One person may be describing folder structure while another is describing servers to provision, and the conversation continues for twenty minutes before anyone notices.

<a id="be-a3"></a>

#### Runtime and platform vocabulary

| Term                               | What it means                                                                           | Note                                                                                                    |
|------------------------------------|-----------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------|
| **.NET**                           | The modern, cross-platform version (.NET 5 onwards). Runs on Windows, Linux and macOS   | What you should build new work on                                                                       |
| **.NET Framework**                 | The original Windows-only version, ending at 4.8. Still supported, no longer developed  | The distinction matters constantly when reading documentation                                           |
| **CLR** (Common Language Runtime)  | The virtual machine that executes your code — memory, threads, type safety, exceptions  | Roughly analogous to the JVM                                                                            |
| **IL** / **CIL** / **MSIL**        | Intermediate Language: the low-level instruction set your C# compiles to                | Three names for the same thing. A `.dll` contains IL, not machine code                                  |
| **JIT** (Just-In-Time compilation) | The runtime converts IL to machine code the first time each method runs                 | Slower first call, then fast — and it can optimise using facts only known at runtime                    |
| **AOT** / **Native AOT**           | Compiling all the way to machine code at build time instead                             | Near-instant startup and lower memory, at the cost of no runtime code generation and limited reflection |
| **GC** (Garbage Collector)         | Reclaims memory you no longer reference, automatically                                  | Why C# has no `free()`. It also means occasional pauses                                                 |
| **Managed code**                   | Code the CLR runs and controls                                                          | Memory safety and type safety are enforced for you                                                      |
| **Unmanaged code**                 | Native code outside the CLR — a C library, an OS call                                   | You are responsible for its memory                                                                      |
| **Assembly**                       | A compiled unit: a `.dll` or `.exe`                                                     | The unit of versioning and deployment                                                                   |
| **NuGet**                          | The package manager and package format                                                  | Equivalent to npm or Maven                                                                              |
| **TFM** (Target Framework Moniker) | The short code identifying what you build against — `net8.0`, `net48`, `netstandard2.0` | What appears in `<TargetFramework>` in your project file                                                |
| **.NET Standard**                  | An older specification of APIs common to several runtimes                               | Largely superseded, but still seen in libraries needing broad compatibility                             |
| **BCL** (Base Class Library)       | The built-in library: collections, strings, files, HTTP, dates                          | The parts you use without adding a package                                                              |
| **SDK vs runtime**                 | The SDK builds code; the runtime only runs it                                           | A build server needs the SDK; a production container usually needs only the runtime                     |
| **Reflection**                     | Inspecting and invoking types at runtime by name                                        | Powerful, slow, and the main thing that breaks under trimming and AOT                                   |
| **Trimming**                       | Removing unused code from the published output to shrink it                             | Reflection can defeat it, because the trimmer cannot see the usage                                      |
| **P/Invoke** (Platform Invoke)     | Calling a native function from managed code                                             | How you reach an OS or C library API                                                                    |
| **Marshalling**                    | Converting data between managed and native representations                              | Where P/Invoke bugs live                                                                                |
| **Boxing**                         | Wrapping a value type (like `int`) in an object so it can live on the heap              | Allocates. In a hot loop it is a common, invisible performance cost                                     |
| **Span\<T\>**                      | A view over a contiguous region of memory, without copying it                           | How modern .NET parses and slices data with no allocation                                               |

<a id="be-a4"></a>

#### Dependency injection vocabulary

DI is central to how modern .NET applications are wired together, and its vocabulary trips people up because several terms describe the same idea from different angles.

| Term                           | What it means                                                                                                 | Note                                                                                              |
|--------------------------------|---------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| **Dependency**                 | Anything a class needs to do its job — a database connection, a logger, a clock                               | “This class depends on `IEmailSender`”                                                            |
| **DI** (Dependency Injection)  | Giving a class its dependencies from outside, usually via its constructor, instead of it creating them itself | So you can substitute them — a fake email sender in tests, a real one in production               |
| **IoC** (Inversion of Control) | The general principle: the framework calls your code, rather than your code driving the framework             | DI is one form of it. The two terms are used almost interchangeably in practice                   |
| **Container**                  | The object that knows how to construct your classes and their dependencies                                    | In .NET, `IServiceProvider`, configured via `IServiceCollection`                                  |
| **Registration**               | Telling the container “when something needs `IFoo`, give it a `Foo`”                                          | `services.AddScoped<IFoo, Foo>()`                                                                 |
| **Resolution**                 | The container actually building an instance when it is asked for                                              | Happens automatically when a controller or handler is constructed                                 |
| **Transient**                  | A new instance every single time it is requested                                                              | The safe default for stateless, cheap objects                                                     |
| **Scoped**                     | One instance per scope — in a web application, per HTTP request                                               | What `DbContext` uses, so all work in one request shares one unit of work                         |
| **Singleton**                  | One instance for the entire application lifetime                                                              | Must be thread-safe, because many requests will use it simultaneously                             |
| **Captive dependency**         | A longer-lived object holding a reference to a shorter-lived one — a singleton capturing a scoped `DbContext` | A genuine bug: the captured object outlives its intended scope and is then shared across requests |
| **Composition root**           | The single place where the whole object graph is wired up                                                     | `Program.cs`. Registration should happen here and nowhere else                                    |
| **Service locator**            | Asking the container for a dependency mid-method, instead of receiving it in the constructor                  | Widely considered an anti-pattern: the dependency becomes invisible in the class's signature      |
| **Options pattern**            | Binding configuration into a strongly-typed class and injecting it                                            | `IOptions<MyOptions>`, instead of reading magic strings from config everywhere                    |
| **Factory**                    | An object whose job is creating other objects                                                                 | Used when construction needs a runtime decision the container cannot make                         |
| **Decorator**                  | Wrapping an implementation in another with the same interface, to add behaviour                               | How caching, logging or retry is added without modifying the original class                       |

<a id="be-a5"></a>

#### Async and concurrency vocabulary

| Term                        | What it means                                                                          | Note                                                                                              |
|-----------------------------|----------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| **Synchronous**             | The caller waits for the work to finish before continuing                              | Simple, and it occupies a thread while waiting                                                    |
| **Asynchronous**            | The caller starts the work and is free to do something else until it completes         | The thread is released while waiting on I/O — that is the whole point                             |
| **Blocking**                | A thread sits idle, doing nothing, waiting for something to finish                     | Wasteful. Under load, blocked threads are why a service stops responding                          |
| **Thread**                  | An independent path of execution within a process                                      | Relatively expensive: roughly 1 MB of stack each                                                  |
| **Thread pool**             | A managed set of reusable threads the runtime hands out for work                       | Avoids the cost of creating a thread per task                                                     |
| **Task**                    | An object representing work that will complete in future, possibly with a result       | Not the same as a thread — an async `Task` waiting on I/O uses no thread at all                   |
| **`async`/`await`**         | Language syntax that lets you write asynchronous code in a sequential style            | The compiler rewrites the method into a state machine that resumes where it left off              |
| **Concurrency**             | Several things in progress at once, interleaved                                        | One chef alternating between three pans                                                           |
| **Parallelism**             | Several things executing at literally the same instant, on different cores             | Three chefs, one pan each. Concurrency is about structure, parallelism about hardware             |
| **Race condition**          | A bug where the outcome depends on the unpredictable relative timing of two operations | Notoriously intermittent, and often invisible in testing                                          |
| **Thread safety**           | A class behaving correctly when used by several threads simultaneously                 | Most classes are *not* thread-safe unless documented as such                                      |
| **Deadlock**                | Two operations each waiting for something the other holds, so neither ever proceeds    | The classic .NET version is calling `.Result` on an async method from a synchronisation context   |
| **Idempotent**              | Doing the operation twice has the same effect as doing it once                         | Essential for anything retried. “Set status to Shipped” is idempotent; “add €10” is not           |
| **`CancellationToken`**     | A token passed down a call chain so long-running work can be asked to stop             | Pass it everywhere it is accepted. A cancelled HTTP request should not keep querying the database |
| **`ConfigureAwait(false)`** | “Do not bother resuming on the original context”                                       | Important in library code and in .NET Framework; largely unnecessary in modern ASP.NET Core       |
| **Backpressure**            | A slow consumer signalling a fast producer to slow down                                | Without it, work queues grow until memory runs out                                                |
| **Sync over async**         | Calling asynchronous code synchronously via `.Result` or `.Wait()`                     | A leading cause of thread-pool starvation and deadlock. Async all the way down instead            |

> **\[Note\]**
>
> **The one-sentence version.** Asynchronous code exists so that a thread waiting for a database or a network call can be released to serve another request. It does not make anything faster in isolation — it makes the *service* able to handle far more concurrent work with the same number of threads.

<a id="be-a6"></a>

#### Data access vocabulary

| Term                               | What it means                                                             | Note                                                                                                     |
|------------------------------------|---------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------|
| **ORM** (Object-Relational Mapper) | A library that translates between objects and database tables             | EF Core is .NET's main one. Dapper is a lighter alternative that maps results but does not track changes |
| **`DbContext`**                    | EF Core's session: it tracks your entities and saves changes              | Short-lived and scoped per request. Never a singleton                                                    |
| **Change tracking**                | EF Core watching loaded entities and working out what to write on save    | Convenient, and it costs memory and CPU — hence `AsNoTracking()` for read-only queries                   |
| **Migration**                      | A versioned script describing a schema change                             | Kept in source control, applied in order, so every environment reaches the same schema                   |
| **Eager loading**                  | Fetching related data in the same query — `Include()`                     | One round trip; a potentially large result                                                               |
| **Lazy loading**                   | Fetching related data automatically the moment you touch the property     | Convenient, and the usual cause of the N+1 problem                                                       |
| **Explicit loading**               | Loading related data when you ask for it, deliberately                    | Most predictable of the three                                                                            |
| **N+1 problem**                    | One query to get a list, then one more *per item* to get its related data | 50 orders becomes 51 queries. The most common ORM performance bug there is                               |
| **Connection pooling**             | Reusing database connections instead of opening a new one per query       | On by default. Opening a connection is expensive; the pool hides that                                    |
| **Transaction**                    | A group of operations that all succeed or all fail together               | Prevents money leaving one account without arriving at the other                                         |
| **Isolation level**                | How much one in-progress transaction can see of another                   | Trades correctness against concurrency                                                                   |
| **Optimistic concurrency**         | Assume no conflict; check at write time that nothing changed              | A version column in the `WHERE` clause. No locks held while the user thinks                              |
| **Pessimistic concurrency**        | Lock the row when you read it, until you commit                           | No conflicts possible, at the cost of others waiting                                                     |
| **Stored procedure**               | SQL code stored and executed inside the database                          | Sometimes the right tool; harder to version and test than application code                               |
| **Seeding**                        | Inserting initial reference data as part of setup                         | Lookup tables, a first admin user                                                                        |
| **Polyglot persistence**           | Deliberately using more than one kind of database, each for what it suits | A relational store for orders, Redis for caching, a search engine for search                             |

<a id="be-a7"></a>

#### APIs, messaging and resilience vocabulary

| Term                            | What it means                                                                                                      | Note                                                                                                                                                      |
|---------------------------------|--------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|
| **REST**                        | An architectural style using HTTP verbs and resource URLs                                                          | Most “REST” APIs are really HTTP-and-JSON APIs, which is fine                                                                                             |
| **RPC** (Remote Procedure Call) | Calling a function on another machine as if it were local                                                          | The mental model is “call a method” rather than “act on a resource”                                                                                       |
| **gRPC**                        | A fast binary RPC framework using Protocol Buffers over HTTP/2                                                     | Excellent between your own services; awkward from a browser                                                                                               |
| **GraphQL**                     | A query language letting clients request exactly the fields they want                                              | Solves over-fetching; moves complexity to the server                                                                                                      |
| **Endpoint**                    | One addressable operation — a URL and method combination                                                           |                                                                                                                                                           |
| **Payload**                     | The actual data in a request or response body                                                                      |                                                                                                                                                           |
| **Contract**                    | The agreed shape of a message or API between two parties                                                           | Breaking a contract breaks your callers, which is why versioning exists                                                                                   |
| **Idempotency key**             | A client-supplied unique id so the server can detect and ignore a duplicate request                                | How you make “charge this card” safe to retry                                                                                                             |
| **Retry**                       | Attempting a failed operation again                                                                                | Only safe if the operation is idempotent                                                                                                                  |
| **Exponential backoff**         | Waiting progressively longer between retries                                                                       | Prevents a struggling service being hammered by everyone retrying at once                                                                                 |
| **Jitter**                      | Adding randomness to backoff delays                                                                                | Stops all clients retrying in synchronised waves                                                                                                          |
| **Timeout**                     | Giving up after a set duration                                                                                     | Every outbound call needs one. A hanging call holds resources indefinitely                                                                                |
| **Circuit breaker**             | After repeated failures, stop calling a dependency for a while and fail fast                                       | Gives the failing service room to recover, instead of pinning it under load                                                                               |
| **Bulkhead**                    | Limiting how many concurrent calls one dependency may consume                                                      | So one slow dependency cannot exhaust every thread in your service                                                                                        |
| **Broker**                      | The infrastructure that stores and delivers messages                                                               | RabbitMQ, Azure Service Bus, Kafka                                                                                                                        |
| **Queue**                       | Messages delivered to exactly one consumer                                                                         | For distributing work                                                                                                                                     |
| **Topic** / **pub-sub**         | Messages delivered to every interested subscriber                                                                  | For broadcasting that something happened                                                                                                                  |
| **Command**                     | A message instructing something to happen. Named imperatively: `PlaceOrder`                                        | One recipient, and it may be rejected                                                                                                                     |
| **Event**                       | A message stating something already happened. Named in the past tense: `OrderPlaced`                               | Any number of recipients, and it cannot be rejected                                                                                                       |
| **At-least-once delivery**      | A message will arrive, possibly more than once                                                                     | The normal guarantee. Consumers must therefore be idempotent                                                                                              |
| **At-most-once delivery**       | A message arrives once or not at all                                                                               | Simpler, and you can lose messages                                                                                                                        |
| **Exactly-once delivery**       | Each message processed precisely once                                                                              | Not achievable in general across a network. What is achievable is at-least-once delivery plus idempotent processing, which has the same observable effect |
| **Dead-letter queue**           | Where messages go after repeatedly failing to be processed                                                         | So a single poison message does not block the queue forever                                                                                               |
| **Outbox pattern**              | Writing a message into your own database in the same transaction as the data change, then publishing it separately | Guarantees the message is not lost if publishing fails after the data was committed                                                                       |
| **Saga**                        | A long-running process across several services, with compensating actions instead of a rollback                    | How you approximate a transaction when a real distributed one is not available                                                                            |
| **Correlation id**              | An identifier passed through every hop of a request                                                                | How you find all the logs belonging to one user action across five services                                                                               |

> **\[Trap\]**
>
> **Common trap.** “Exactly-once” is the term most likely to cause a design mistake. Vendors advertise it, and what they actually provide is at-least-once delivery with deduplication over a limited window. Design your consumers to be idempotent regardless of what the broker promises.

<a id="be-a8"></a>

#### Operations and delivery vocabulary

| Term                                      | What it means                                                                                                       | Note                                                                                                             |
|-------------------------------------------|---------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| **CI** (Continuous Integration)           | Every commit is automatically built and tested                                                                      | Catches breakage within minutes rather than at release                                                           |
| **CD** (Continuous Delivery / Deployment) | Delivery: every build is *ready* to release. Deployment: every build *is* released                                  | The two are frequently conflated                                                                                 |
| **Pipeline**                              | The automated sequence from commit to deployed application                                                          |                                                                                                                  |
| **Artifact**                              | The built output that gets deployed — a container image, a zip, a NuGet package                                     | **Build once, deploy that same artifact everywhere.** Rebuilding per environment means you tested something else |
| **Environment**                           | A deployed instance of the system — development, staging, production                                                | Configuration differs; the artifact should not                                                                   |
| **Blue-green deployment**                 | Two identical environments; switch traffic from old to new in one step                                              | Instant rollback by switching back                                                                               |
| **Canary release**                        | Sending a small percentage of traffic to the new version first                                                      | Limits the blast radius of a bad release                                                                         |
| **Rolling deployment**                    | Replacing instances gradually, a few at a time                                                                      | The default under an orchestrator. Requires both versions to work simultaneously                                 |
| **Feature flag**                          | A switch that turns a feature on or off without deploying                                                           | Separates *deploying* code from *releasing* a feature                                                            |
| **IaC** (Infrastructure as Code)          | Defining infrastructure in files rather than clicking in a portal                                                   | Terraform, Bicep, Pulumi. Reviewable, repeatable, version-controlled                                             |
| **Container**                             | An isolated process with its own filesystem view, sharing the host kernel                                           | Not a virtual machine — far lighter, and no separate OS                                                          |
| **Image**                                 | The immutable template a container is started from                                                                  |                                                                                                                  |
| **Registry**                              | Where images are stored and pulled from                                                                             | Docker Hub, Azure Container Registry, ECR                                                                        |
| **Orchestrator**                          | Software that schedules containers across machines and keeps them running                                           | Kubernetes, ECS, Azure Container Apps                                                                            |
| **Sidecar**                               | A helper container running alongside your application in the same unit                                              | Used for proxies, log shippers, secret injection                                                                 |
| **Liveness vs readiness probe**           | Liveness: is the process alive (restart if not). Readiness: should it receive traffic yet                           | Confusing them causes either restart loops or traffic to an unready instance                                     |
| **Observability**                         | Being able to work out what a running system is doing from its outputs                                              | The goal. Telemetry is the means                                                                                 |
| **Telemetry**                             | The data a system emits about itself                                                                                | Logs, metrics and traces                                                                                         |
| **Log**                                   | A timestamped record of a discrete event                                                                            | Best structured (as data), not as a formatted sentence                                                           |
| **Metric**                                | A number measured over time — requests per second, memory used                                                      | Cheap to store, good for alerting and trends                                                                     |
| **Trace** and **span**                    | A trace is one request's full journey; a span is one operation within it                                            | How you see that 90% of a request's time was one database call                                                   |
| **OpenTelemetry**                         | The vendor-neutral standard for producing telemetry                                                                 | Instrument once, send it wherever you like                                                                       |
| **SLI / SLO / SLA**                       | Indicator: what you measure. Objective: your internal target. Agreement: the contractual promise, with consequences | In that order of strictness                                                                                      |
| **Error budget**                          | How much unreliability your SLO permits before you must stop shipping features                                      | Turns reliability into a number rather than an argument                                                          |
| **RTO / RPO**                             | Recovery Time Objective: how long recovery may take. Recovery Point Objective: how much data you may lose           | The two numbers that determine your entire backup strategy                                                       |
| **Secret**                                | A credential or key that must not be in source control                                                              | Managed by Key Vault, Secrets Manager, or your orchestrator                                                      |

<a id="be-a9"></a>

#### Testing vocabulary

| Term                              | What it means                                                                                | Note                                                                                           |
|-----------------------------------|----------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------|
| **Unit test**                     | Tests one small piece of code in isolation                                                   | Fast, numerous, and cheap to run. Where most tests should live                                 |
| **Integration test**              | Tests several components working together, often including a real database                   | Slower, and catches what unit tests cannot — the wiring                                        |
| **End-to-end test**               | Drives the whole system as a user would                                                      | Highest confidence, slowest, most fragile. A few, not hundreds                                 |
| **Smoke test**                    | A minimal check that the deployment basically works                                          | Run immediately after deploying                                                                |
| **Load test**                     | Measuring behaviour under expected traffic                                                   | Answers “does it hold up at 500 requests per second?”                                          |
| **Stress test**                   | Pushing past the expected limit to find the breaking point                                   | Answers “what fails first, and how?”                                                           |
| **Test double**                   | The umbrella term for any stand-in used in a test                                            | Mock, stub, fake and spy are all test doubles                                                  |
| **Stub**                          | Returns fixed, canned answers                                                                | Used to supply input the test needs                                                            |
| **Mock**                          | Pre-programmed with expectations about how it will be called, and verifies them              | Powerful, and it couples the test to *how* the code works, not just what it produces           |
| **Fake**                          | A working but simplified implementation — an in-memory repository                            | Often the best option, and the most under-used                                                 |
| **Spy**                           | Records how it was called while still behaving normally                                      |                                                                                                |
| **Fixture**                       | The known starting state a test runs against                                                 |                                                                                                |
| **AAA** (Arrange, Act, Assert)    | The three-part structure of a readable test: set up, do the thing, check the result          | One *Act* per test                                                                             |
| **SUT** (System Under Test)       | The thing the test is actually testing                                                       | Naming it explicitly keeps the test focused                                                    |
| **Coverage**                      | The percentage of code executed by tests                                                     | A useful diagnostic and a terrible target. 100% coverage of untested assertions proves nothing |
| **Flaky test**                    | One that passes and fails without the code changing                                          | Worse than no test, because it teaches the team to ignore failures                             |
| **Test pyramid**                  | The guidance to have many unit tests, fewer integration tests, and very few end-to-end tests | Based on cost and speed                                                                        |
| **Contract test**                 | Verifies that a producer and a consumer still agree on a message shape                       | Catches a breaking API change before it reaches the consumer                                   |
| **Mutation testing**              | Deliberately introducing bugs to see whether your tests catch them                           | Measures whether your assertions are meaningful, which coverage does not                       |
| **Snapshot test**                 | Comparing output to a previously recorded version                                            | Fast to write; easy to approve a wrong change without reading it                               |
| **TDD** (Test-Driven Development) | Writing the failing test before the implementation                                           | A design practice as much as a testing one                                                     |

> **\[Note\]**
>
> **The distinction worth internalising.** A **stub** supplies data so the test can run. A **mock** asserts that a particular call was made. Reach for a stub or a fake by default: mocks verify *implementation details*, so a refactor that changes nothing about behaviour still breaks them — which is how a test suite becomes something people resent rather than rely on.

<a id="be-a10"></a>

#### C# language vocabulary

The language features used throughout this guide. Several appear from the first pages onward and are rarely spelled out anywhere, so they are collected here.

| Term                 | Means                                                          | Note                                                  |
|----------------------|----------------------------------------------------------------|-------------------------------------------------------|
| **Delegate**         | A type that holds a reference to a method                      | `Func<T>`, `Action`, `Predicate<T>` are all delegates |
| **Lambda**           | An inline anonymous function: `x => x * 2`                     | Creates a delegate, or an expression tree             |
| **Closure**          | A lambda that *captures* a variable from its surrounding scope | The captured variable is moved to the heap            |
| **Expression tree**  | Code represented as *data* rather than compiled                | What lets EF Core translate LINQ into SQL             |
| **Extension method** | A static method callable as if it were an instance method      | `this` on the first parameter                         |
| **Attribute**        | Declarative metadata attached to code, in square brackets      | `[Authorize]`, `[Obsolete]`. Read via reflection      |
| **Assembly**         | One compiled `.dll` or `.exe`, plus its metadata               | The unit of deployment and of versioning              |
| **Namespace**        | A naming scope for types                                       | Purely organisational; unrelated to assemblies        |
| **Generic**          | A type or method parameterised by another type                 | `List<T>`. **Reified** in .NET, unlike Java           |
| **Constraint**       | A restriction on a type parameter                              | `where T : class, new()`                              |
| **Boxing**           | Wrapping a value type in a heap object                         | Allocates; a common hidden cost in loops              |
| **Tuple**            | An ad-hoc group of values with no declared type                | `(string Name, int Age)`                              |
| **Deconstruction**   | Unpacking an object into separate variables                    | `var (name, age) = person;`                           |
| **Pattern matching** | Testing shape and extracting values in one expression          | `is` and `switch` expressions                         |

| Modifier     | Means                                      | Note                                                               |
|--------------|--------------------------------------------|--------------------------------------------------------------------|
| `static`     | Belongs to the type, not to an instance    | One copy for the whole process                                     |
| `virtual`    | May be overridden by a derived class       | Costs a virtual dispatch                                           |
| `override`   | Replaces a base implementation             | Must match a `virtual` or `abstract` member                        |
| `abstract`   | Declared without an implementation         | The class cannot be instantiated                                   |
| **`sealed`** | **Cannot be inherited or overridden**      | The default choice for a class; also enables some JIT optimisation |
| `readonly`   | Assignable only in the constructor         | On a struct, means the whole type is immutable                     |
| `const`      | Compile-time constant, baked into callers  | Changing one requires recompiling every consumer                   |
| `init`       | Settable only during object initialisation | Immutable afterwards                                               |
| `required`   | The initialiser *must* set it              | A compile error otherwise                                          |
| `partial`    | The type is split across files             | How source generators add to your types                            |
| `internal`   | Visible within this assembly only          | The default for a top-level type                                   |
| `record`     | A type with generated value equality       | `class` unless declared `record struct`                            |

| Interface you meet constantly | Means                                                       |
|-------------------------------|-------------------------------------------------------------|
| **`IDisposable`**             | Holds something that must be released — use it with `using` |
| `IAsyncDisposable`            | The same, released asynchronously — `await using`           |
| `IEnumerable<T>`              | Can be iterated, possibly lazily                            |
| `IAsyncEnumerable<T>`         | Iterated with `await foreach`; each step may await          |
| `IQueryable<T>`               | An expression tree a provider translates — usually to SQL   |
| `IReadOnlyList<T>`            | Already materialised; safe to enumerate repeatedly          |

<a id="be-a11"></a>

#### Acronyms and abbreviations

Every abbreviation used in this guide, expanded. Grouped roughly by area.

| Platform and runtime | Expands to                   | Means                                               |
|----------------------|------------------------------|-----------------------------------------------------|
| **CLR**              | Common Language Runtime      | The virtual machine that executes .NET code         |
| **BCL**              | Base Class Library           | The built-in types shipped with the runtime         |
| **IL**               | Intermediate Language        | The portable instruction set in a compiled assembly |
| **JIT**              | Just-In-Time compilation     | IL → machine code, per method, on first call        |
| **AOT**              | Ahead-Of-Time compilation    | Machine code produced at build time; no JIT         |
| **GC**               | Garbage Collector            | Reclaims heap memory automatically                  |
| **LOH**              | Large Object Heap            | Objects ≥ 85,000 bytes; collected only with gen 2   |
| **SDK**              | Software Development Kit     | Everything needed to build; contains the runtime    |
| **TFM**              | Target Framework Moniker     | `net10.0`, `net48`, `netstandard2.0`                |
| **LTS** / **STS**    | Long / Standard Term Support | 3 years of support versus 18 months                 |
| **DI**               | Dependency Injection         | Receiving collaborators from outside                |

| Data and querying    | Expands to                              | Means                                                      |
|----------------------|-----------------------------------------|------------------------------------------------------------|
| **LINQ**             | Language Integrated Query               | Query syntax over collections and databases, built into C# |
| **EF** / **EF Core** | Entity Framework Core                   | Microsoft's object-relational mapper                       |
| **ORM**              | Object-Relational Mapper                | Translates between objects and tables                      |
| **SQL**              | Structured Query Language               | The language relational databases speak                    |
| **DDL** / **DML**    | Data Definition / Manipulation Language | `CREATE TABLE` versus `INSERT`                             |
| **CRUD**             | Create, Read, Update, Delete            | The four basic data operations                             |
| **ACID**             | Atomic, Consistent, Isolated, Durable   | The guarantees a transaction gives                         |
| **MVCC**             | Multi-Version Concurrency Control       | Readers see a snapshot; they do not block writers          |
| **CDC**              | Change Data Capture                     | Streaming a database's changes to other systems            |
| **TTL**              | Time To Live                            | How long a cached or DNS value stays valid                 |

| Web, APIs and hosting | Expands to                            | Means                                                      |
|-----------------------|---------------------------------------|------------------------------------------------------------|
| **ASP.NET Core**      | —                                     | The web framework. Not an acronym any more — just the name |
| **HTTP** / **HTTPS**  | HyperText Transfer Protocol (Secure)  | The request/response protocol of the web                   |
| **REST**              | Representational State Transfer       | An architectural style using HTTP's own verbs and codes    |
| **API**               | Application Programming Interface     | A contract another program calls                           |
| **JSON**              | JavaScript Object Notation            | The usual payload format                                   |
| **URL** / **URI**     | Uniform Resource Locator / Identifier | An address; a URL is a URI that locates                    |
| **gRPC**              | —                                     | A binary RPC protocol over HTTP/2, using Protobuf          |
| **IIS**               | Internet Information Services         | Microsoft's Windows web server                             |
| **YARP**              | Yet Another Reverse Proxy             | Microsoft's reverse proxy, as a .NET library               |
| **WCF**               | Windows Communication Foundation      | The .NET Framework RPC stack; CoreWCF on modern .NET       |
| **CORS**              | Cross-Origin Resource Sharing         | Browser rules for calling another origin                   |
| **SSE**               | Server-Sent Events                    | One-way streaming from server to client over plain HTTP    |

| Networking and security | Expands to                                        | Means                                                    |
|-------------------------|---------------------------------------------------|----------------------------------------------------------|
| **TCP** / **IP**        | Transmission Control Protocol / Internet Protocol | Reliable ordered bytes, over addressed packets           |
| **TLS**                 | Transport Layer Security                          | Encryption for connections; the S in HTTPS               |
| **DNS**                 | Domain Name System                                | Turns a name into an address                             |
| **NAT**                 | Network Address Translation                       | Many private addresses sharing one public one            |
| **JWT**                 | JSON Web Token                                    | A signed, expiring package of claims                     |
| **OIDC**                | OpenID Connect                                    | An identity layer on top of OAuth 2.0                    |
| **SSRF**                | Server-Side Request Forgery                       | Tricking your server into fetching a URL for an attacker |
| **RBAC** / **ABAC**     | Role / Attribute Based Access Control             | Permissions by role, versus by claims and context        |
| **UTC**                 | Coordinated Universal Time                        | The timezone every timestamp should be stored in         |

| Operations                  | Expands to                                      | Means                                                    |
|-----------------------------|-------------------------------------------------|----------------------------------------------------------|
| **CI** / **CD**             | Continuous Integration / Delivery               | Automated build and test; automated release              |
| **IaC**                     | Infrastructure as Code                          | Infrastructure defined in version-controlled files       |
| **SLI** / **SLO** / **SLA** | Service Level Indicator / Objective / Agreement | The measure, the target, the contract                    |
| **RPO** / **RTO**           | Recovery Point / Time Objective                 | How much data you may lose; how long you may be down     |
| **OTel**                    | OpenTelemetry                                   | The vendor-neutral standard for traces, metrics and logs |
| **APM**                     | Application Performance Monitoring              | Tooling that traces and profiles a running system        |
| **SBOM**                    | Software Bill of Materials                      | A manifest of everything your build included             |
| **CVE**                     | Common Vulnerabilities and Exposures            | A published, identified security flaw                    |

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b232"></a>

#### The words this document uses

These appear everywhere below, so they're worth pinning down before anything else.

**IL (Intermediate Language)** — what the C# compiler actually produces. Not machine code: a compact, stack-based instruction set that describes *what* to do without committing to a CPU. `csc` turns `a + b` into IL that says "push a, push b, add."

**Assembly** — a `.dll` or `.exe` containing IL plus **metadata**: a complete machine-readable description of every type, method, field, and signature inside it. That metadata is why reflection works and why decompilers can reconstruct readable C#.

**CLR (Common Language Runtime)** — the engine that runs it. It loads assemblies, verifies IL, manages the heap, runs the garbage collector, and handles exceptions. "Managed code" means code the CLR is supervising; "unmanaged" means native code outside it, reached through P/Invoke.

**JIT (Just-In-Time compiler)** — turns IL into real machine code, **one method at a time, on first call**. This has three consequences that matter later:

- The JIT sees the actual CPU it's running on, so it can use instructions an ahead-of-time compiler couldn't assume.
- It sees runtime facts a static compiler can't — which concrete type is really in that variable — enabling *devirtualization* and *inlining*.
- The first call to any method pays compilation cost. This is why benchmarks need warmup, and why startup-sensitive apps use ReadyToRun or Native AOT to pre-compile.

**BCL (Base Class Library)** — the built-in types: `System.String`, `List<T>`, `Dictionary<K,V>`, and everything else you didn't write. "The framework" colloquially.

**Type system terms:** a **type** describes shape and behaviour; an **instance** is one concrete occurrence; a **member** is anything declared inside a type (field, property, method, event); a **field** stores data; a **property** is a pair of methods that look like a field.

### Terminology

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Terminology</p>

<p class="pd">Plain-English definitions for the vocabulary in this guide. Grouped by domain so related terms sit together and the distinctions are visible. Use browser find (Ctrl/⌘+F) for a specific word.</p>

<a id="be-g626"></a>

#### Networking terms

| Term                 | Means                                                                  | Note                                                               |
|----------------------|------------------------------------------------------------------------|--------------------------------------------------------------------|
| **IP address**       | Identifies a host on a network                                         | IPv4 is four octets; IPv6 is much longer and increasingly common   |
| **Port**             | Identifies a process on that host                                      | IP gets you to the machine; the port gets you to the program       |
| **Socket**           | One end of a connection                                                | Identified by the 4-tuple: source IP+port, destination IP+port     |
| **CIDR**             | `10.0.4.0/22` — an address range written as address plus prefix length | The prefix says how many leading bits are the network              |
| **Subnet**           | A slice of a network with its own routing rules                        | In the cloud, public vs private is purely a route-table difference |
| **Gateway**          | Where packets go when the destination is not local                     | The default route                                                  |
| **NAT**              | Rewriting addresses so private hosts share a public IP                 | Lets a private server reach out without being reachable            |
| **DNS**              | Names to addresses                                                     | Cached at many layers, which is why changes lag                    |
| **TTL**              | How long a DNS answer may be cached                                    | Lower it days before a planned cutover                             |
| **RTT**              | Round-trip time                                                        | The floor on latency that no server tuning can cross               |
| **TCP**              | Reliable, ordered, connection-oriented transport                       | What almost all your traffic uses                                  |
| **UDP**              | Fire-and-forget datagrams                                              | No guarantees, no handshake; the base for QUIC and DNS             |
| **QUIC**             | Reliability and encryption rebuilt over UDP                            | What HTTP/3 runs on                                                |
| **MTU**              | Largest packet a link will carry                                       | Mismatches cause mysterious hangs on large payloads                |
| **Ephemeral port**   | The random high port a client uses as its source                       | Exhausting the range causes `EADDRNOTAVAIL`                        |
| **VPC**              | Your private network in a cloud                                        | Called a VNet in Azure                                             |
| **Peering**          | Connecting two private networks directly                               | Impossible if their address ranges overlap                         |
| **Egress / ingress** | Traffic out of / into a network                                        | Egress is usually the one that costs money                         |

<a id="be-g627"></a>

#### HTTP, TLS and web terms

| Term            | Means                                                                     | Note                                                          |
|-----------------|---------------------------------------------------------------------------|---------------------------------------------------------------|
| **Idempotent**  | Doing it twice has the same effect as once                                | What makes a request safe to retry                            |
| **Safe**        | Has no side effects                                                       | `GET` must be safe, or prefetchers will cause trouble         |
| **TLS**         | Encryption, integrity and server authentication for a connection          | SSL is the obsolete predecessor; the name persists            |
| **Certificate** | A signed statement that a public key belongs to a name                    | Has an expiry, which is why automation matters                |
| **CA**          | Certificate Authority — something trusted to sign certificates            | Roots are pre-installed; intermediates are what actually sign |
| **Chain**       | Leaf → intermediate → root                                                | Omitting the intermediate breaks non-browser clients          |
| **SNI**         | The client tells the server which hostname it wants, during the handshake | How one IP serves many certificates                           |
| **mTLS**        | Both sides present certificates                                           | Strong service-to-service authentication                      |
| **ACME**        | The protocol behind automated certificate issuance                        | What Let's Encrypt and cert-manager use                       |
| **CORS**        | Rules letting a browser call a different origin                           | Enforced by the browser, not the server                       |
| **CDN**         | Caches and serves content from locations near users                       | Also absorbs traffic that would hit your origin               |
| **Origin**      | The server behind the CDN or load balancer                                | What you actually run                                         |
| **Edge**        | The provider location closest to the user                                 | Where a CDN or edge function executes                         |
| **WAF**         | Web Application Firewall — filters known attack patterns                  | A layer, not a substitute for secure code                     |

<a id="be-g628"></a>

#### Servers, proxies and load balancing

| Term                | Means                                                           | Note                                                     |
|---------------------|-----------------------------------------------------------------|----------------------------------------------------------|
| **Reverse proxy**   | Sits in front of your servers, on their behalf                  | Clients do not know it is there                          |
| **Forward proxy**   | Sits in front of clients, on their behalf                       | Corporate egress filtering                               |
| **Load balancer**   | Distributes requests across instances                           | L4 routes on IP and port; L7 understands HTTP            |
| **VIP**             | Virtual IP — one stable address in front of many servers        |                                                          |
| **Upstream**        | What the proxy forwards to                                      | Your application                                         |
| **TLS termination** | Where encryption is decrypted                                   | Usually the load balancer                                |
| **Health check**    | A periodic probe deciding if an instance should receive traffic |                                                          |
| **Liveness**        | “Is the process alive?”                                         | If not → **restart**. Must not check dependencies        |
| **Readiness**       | “Should it receive traffic?”                                    | If not → **remove from the pool**, do not restart        |
| **Draining**        | Stopping new traffic while finishing in-flight requests         | What makes a zero-downtime deploy possible               |
| **Sticky session**  | Pinning a client to one instance                                | Solves a problem better solved by removing server state  |
| **Rate limiting**   | Capping requests per client per period                          |                                                          |
| **Load shedding**   | Deliberately rejecting requests under overload                  | Better than queueing them all until everything times out |
| **Circuit breaker** | Stop calling a failing dependency for a while                   | Gives it room to recover                                 |
| **Backpressure**    | A slow consumer signalling a fast producer to slow down         | Without it, queues grow until something breaks           |

<a id="be-g629"></a>

#### Linux and process terms

| Term                | Means                                                        | Note                                                      |
|---------------------|--------------------------------------------------------------|-----------------------------------------------------------|
| **PID**             | Process id                                                   | PID 1 is init, and must reap orphans                      |
| **Signal**          | A notification sent to a process                             | `SIGTERM` is catchable; `SIGKILL` is not                  |
| **File descriptor** | A handle to an open file, socket or pipe                     | Limited per process; exhausting them breaks everything    |
| **systemd**         | The service manager on most modern Linux                     | Starts, supervises, restarts and logs services            |
| **journald**        | systemd's log store                                          | Queried with `journalctl`                                 |
| **cgroup**          | Kernel mechanism limiting a group's CPU, memory and I/O      | What container resource limits use                        |
| **Namespace**       | Kernel mechanism giving a process its own view of the system | What makes a container look isolated                      |
| **OOM killer**      | The kernel terminating a process when memory runs out        | `SIGKILL`, so no logs and no cleanup                      |
| **ulimit**          | Per-process resource limits                                  | File descriptors is the one you will hit                  |
| **Load average**    | Runnable plus uninterruptible processes, averaged            | On Linux it includes disk wait, so high load can mean I/O |
| **Page cache**      | The kernel caching file contents in spare RAM                | Why ‘free’ memory looks low and that is fine              |
| **Swap**            | Using disk as overflow memory                                | Catastrophic for database latency                         |
| **Inode**           | Filesystem metadata entry per file                           | Can run out while disk space remains                      |
| **THP**             | Transparent Huge Pages                                       | Disable it on database hosts                              |

<a id="be-g630"></a>

#### Compute, containers and orchestration

| Term                   | Means                                                                    | Note                                                |
|------------------------|--------------------------------------------------------------------------|-----------------------------------------------------|
| **Image**              | The immutable template a container starts from                           | Built in layers; identified by a digest             |
| **Container**          | A process running from an image, isolated by namespaces and cgroups      | **Not** a lightweight VM — no guest kernel          |
| **Registry**           | Where images are stored                                                  |                                                     |
| **Digest**             | A content hash identifying an exact image                                | Pin by this, not by tag                             |
| **Orchestrator**       | Schedules containers across machines and keeps them running              | Kubernetes, ECS, Nomad                              |
| **Pod**                | One or more containers sharing a network namespace                       | Kubernetes' smallest deployable unit                |
| **Deployment**         | Declares N replicas and manages their rollout                            |                                                     |
| **Service**            | A stable virtual address in front of pods                                | The IP is virtual and exists only as routing rules  |
| **Ingress / Gateway**  | External HTTP routing into services                                      | Gateway API is replacing Ingress                    |
| **StatefulSet**        | Pods with stable names and their own persistent volumes                  | For databases and anything with durable local state |
| **Requests / limits**  | What the scheduler reserves / the hard cap                               | Scheduling uses *requests*, not actual usage        |
| **Taint / toleration** | A node repels pods unless they tolerate it                               | For dedicated node pools                            |
| **PDB**                | Pod Disruption Budget — minimum availability during voluntary disruption | Without one, a node drain can evict every replica   |
| **Sidecar**            | A helper container beside the main one                                   | Proxies, log shippers                               |
| **Serverless**         | The platform runs your code on demand and scales it                      | Cheap when spiky, expensive when constant           |
| **Cold start**         | Latency when an instance must be created first                           | The main serverless latency concern                 |

<a id="be-g631"></a>

#### Storage, database and data terms

| Term                | Means                                                | Note                                                    |
|---------------------|------------------------------------------------------|---------------------------------------------------------|
| **Block storage**   | A raw disk you format yourself                       | Databases. Lowest latency                               |
| **File storage**    | A filesystem shared over a network                   | Convenient; **never for a database**                    |
| **Object storage**  | Whole blobs addressed by key over HTTP               | Backups, media, data lakes                              |
| **IOPS**            | I/O operations per second                            | What a transactional database is limited by             |
| **Throughput**      | MB per second                                        | What a backup or scan is limited by                     |
| **Latency**         | Time for one operation                               | The number users feel; watch `await`                    |
| **Snapshot**        | A point-in-time copy, usually incremental            | Crash-consistent unless the application is quiesced     |
| **RAID**            | Combining disks for redundancy or speed              | RAID 10 for databases                                   |
| **Durability**      | Will the bytes still exist?                          | Replication and checksums                               |
| **Availability**    | Can I reach them now?                                | Redundancy and failover                                 |
| **Recoverability**  | Can I get the version from before the mistake?       | **Only backups provide this**                           |
| **Replica**         | A live copy kept in sync                             | **Not a backup** — it replicates mistakes too           |
| **Failover**        | Promoting a standby to primary                       | Needs fencing, or you risk split brain                  |
| **Split brain**     | Two primaries accepting conflicting writes           | The worst outcome in database operations                |
| **Quorum**          | A majority agreeing before acting                    | How split brain is prevented                            |
| **Connection pool** | Reusing connections instead of opening one per query | Belongs in infrastructure at scale, not only in the app |

<a id="be-g632"></a>

#### Deployment, operations and security

| Term                    | Means                                                             | Note                                                  |
|-------------------------|-------------------------------------------------------------------|-------------------------------------------------------|
| **Artifact**            | The built output that gets deployed                               | Build once; promote the same one through environments |
| **Rolling deploy**      | Replacing instances gradually                                     | Requires both versions to work simultaneously         |
| **Blue-green**          | Two environments; switch traffic between them                     | Instant rollback                                      |
| **Canary**              | A small percentage of traffic to the new version first            | Limits blast radius                                   |
| **Feature flag**        | A switch changing behaviour without deploying                     | Separates deploying from releasing                    |
| **Expand and contract** | Additive change, migrate, then remove — over three releases       | What makes zero-downtime schema change possible       |
| **IaC**                 | Infrastructure defined in version-controlled files                | Terraform, Bicep, Pulumi                              |
| **State**               | The IaC tool's record of what it created                          | Losing it is painful; never store it locally          |
| **Drift**               | Reality no longer matching the code                               | Caused by console changes during incidents            |
| **GitOps**              | A controller continuously reconciling the cluster to a repository | Audit trail and drift correction for free             |
| **Observability**       | Being able to determine what a system is doing from its outputs   | The goal; telemetry is the means                      |
| **Trace / span**        | One request's journey / one operation within it                   | Correlated by a trace id                              |
| **Cardinality**         | The number of unique label combinations in a metric               | High cardinality kills metrics systems                |
| **SLI / SLO / SLA**     | What you measure / your target / the contractual promise          | In that order of strictness                           |
| **Error budget**        | How much unreliability the SLO permits                            | Turns reliability into a number                       |
| **RTO / RPO**           | How long recovery may take / how much data may be lost            | These two determine your whole DR design              |
| **Least privilege**     | Granting only what is needed                                      | Limits what a compromise can reach                    |
| **Workload identity**   | The platform vouching for a workload, so no secret is stored      | Nothing to leak, nothing to rotate                    |
| **Blast radius**        | What else is affected if this is compromised                      | The question that most changes a design               |
| **SBOM**                | An inventory of what is in a build                                | So you can answer “are we affected?” when a CVE lands |

### Terminology

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Terminology</p>

<p class="pd">Plain-English definitions for the vocabulary used throughout. Grouped by domain so related terms sit together. Use browser find (Ctrl/⌘+F) for a specific word.</p>

<a id="be-h715"></a>

#### Pattern vocabulary

| Term             | Means                                                        | Note                                                           |
|------------------|--------------------------------------------------------------|----------------------------------------------------------------|
| **Pattern**      | A named solution to a recurring problem, with its trade-offs | The trade-offs are the part usually dropped                    |
| **Intent**       | What a pattern is for, in one sentence                       | The thing that distinguishes patterns with identical structure |
| **Forces**       | The competing pressures a pattern balances                   | If no force is present, the pattern is decoration              |
| **Consequences** | What the pattern costs you                                   | Read this section first                                        |
| **Idiom**        | A pattern specific to one language                           | `using` for disposal is a C# idiom                             |
| **Anti-pattern** | A recurring solution that makes things worse                 | Named so it can be recognised and refused                      |
| **Smell**        | A surface symptom suggesting a deeper problem                | Not a bug — an indication to look closer                       |
| **Refactoring**  | Changing structure without changing behaviour                | How you usually arrive at a pattern                            |
| **GoF**          | “Gang of Four” — the four authors of the 1994 catalogue      | Shorthand for the original 23 patterns                         |
| **Boilerplate**  | Code that must be written but carries no decision            | What language features remove                                  |

<a id="be-h716"></a>

#### Design principle vocabulary

| Term                     | Means                                                  | Note                                     |
|--------------------------|--------------------------------------------------------|------------------------------------------|
| **Coupling**             | How much one unit depends on another                   | Test: if I change B, must I change A?    |
| **Cohesion**             | How related the things inside one unit are             | Test: can I describe it without “and”?   |
| **Abstraction**          | A simplified interface hiding detail                   | Leaks when the detail shows through      |
| **Encapsulation**        | Hiding internal state behind behaviour                 | Public setters are the usual breach      |
| **Polymorphism**         | One call site, several behaviours by type              | What replaces most type switches         |
| **Inversion of control** | The framework calls you, not the reverse               | DI is one form of it                     |
| **Dependency injection** | Receiving collaborators from outside                   | Usually via the constructor              |
| **Composition root**     | The single place the object graph is wired             | `Program.cs`                             |
| **Seam**                 | A place where behaviour can be changed without editing | What you create first when refactoring   |
| **Invariant**            | A condition that must always hold                      | What an aggregate exists to protect      |
| **Idempotent**           | Doing it twice equals doing it once                    | What makes retrying safe                 |
| **Immutable**            | Cannot change after construction                       | Removes a whole class of concurrency bug |
| **Side effect**          | An observable change beyond the return value           | Writes, sends, logs                      |
| **Pure function**        | Same input, same output, no side effects               | Trivially testable                       |

<a id="be-h717"></a>

#### Domain and architecture vocabulary

| Term                    | Means                                                | Note                                                               |
|-------------------------|------------------------------------------------------|--------------------------------------------------------------------|
| **Entity**              | An object with identity that persists through change | Two with equal fields differ if ids differ                         |
| **Value object**        | Defined entirely by its values; immutable            | Two with equal fields *are* the same                               |
| **Aggregate**           | A cluster changed together, as one unit              | Also the transaction and locking boundary                          |
| **Aggregate root**      | The only entity outside code may reference           | Everything goes through it                                         |
| **Repository**          | A collection-like interface over stored aggregates   | Best kept specific, not generic                                    |
| **Unit of work**        | Tracks changes and commits them together             | `DbContext` already is one                                         |
| **Domain event**        | A record that something meaningful happened          | Past tense: `OrderPlaced`                                          |
| **Bounded context**     | A boundary within which a model is consistent        | “Customer” means different things either side                      |
| **Ubiquitous language** | The business's words, used in code                   | If the code says `Item` and the business says `Line`, fix the code |
| **Anaemic model**       | Data classes with logic in separate services         | A criticism; also often fine for CRUD                              |
| **DTO**                 | An object that only carries data across a boundary   | Not an entity, and not a view model                                |
| **CQRS**                | Separating the read model from the write model       | A spectrum; most value is at the cheap end                         |
| **Event sourcing**      | Storing events, deriving current state               | Separate idea from CQRS                                            |
| **Layer**               | A logical grouping by responsibility                 | Logical — not a machine                                            |
| **Tier**                | A physical deployment unit                           | Not a synonym for layer                                            |

<a id="be-h718"></a>

#### Distributed and concurrency vocabulary

| Term                | Means                                                      | Note                                           |
|---------------------|------------------------------------------------------------|------------------------------------------------|
| **Partial failure** | The call may or may not have happened, and you cannot tell | The fact every distributed pattern responds to |
| **At-least-once**   | It will arrive, possibly more than once                    | The normal guarantee                           |
| **Exactly-once**    | Not achievable in delivery                                 | Achievable as an *effect*, via idempotency     |
| **Outbox**          | Writing the message in the same transaction as the data    | Solves the dual-write problem                  |
| **Saga**            | A multi-step process with compensating actions             | Not a rollback — compensation is a new event   |
| **Compensation**    | An action that undoes an earlier one's effect              | A refund, not an erasure                       |
| **Circuit breaker** | Stop calling a failing dependency for a while              | Protects *you*, not only them                  |
| **Bulkhead**        | Limiting concurrent calls to one dependency                | Stops one slow dependency taking everything    |
| **Backpressure**    | A slow consumer signalling a producer to slow down         | What a bounded buffer provides                 |
| **Jitter**          | Randomness added to retry delays                           | Prevents synchronised retry storms             |
| **Race condition**  | Outcome depends on unpredictable timing                    | Intermittent; often invisible in testing       |
| **Thread safety**   | Correct when used by several threads at once               | Most classes are not, unless documented        |
| **Actor**           | State owned by one thing, reached only by messages         | Removes locking for that state                 |
| **Sidecar**         | A helper process sharing the application's lifecycle       | Proxies, log shippers                          |
| **Strangler fig**   | Replacing a system route by route while it runs            | Each increment must ship                       |
