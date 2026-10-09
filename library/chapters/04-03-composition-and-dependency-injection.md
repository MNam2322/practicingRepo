---
id: "4-3"
number: "4.3"
title: "Composition and dependency injection"
part: 4
part_title: "Designing programs"
words: 11330
minutes: 49
---

# 4.3 Composition and dependency injection

> **Quick look.** Build behaviour by composing objects, and wire them with dependency injection.
>
> - Favour composition over inheritance; inheritance is for substitutability, not reuse.
> - Decorators add behaviour (caching, retry, logging), adapters make classes fit, strategies pass in what varies.
> - Take dependencies through constructors and wire everything in one place; avoid the service locator.
> - Lifetimes: singleton, scoped (per request), transient. Never inject a scoped service into a singleton (captive dependency).
> - BackgroundService is a singleton: create a scope with IServiceScopeFactory for each unit of work.
> - Factories (IHttpClientFactory, IDbContextFactory, keyed services) cover the cases constructor injection can't.
> - Validate registrations at start-up; a huge constructor means the class does too much.


### The big picture

Inheritance was once the main way to reuse code in object-oriented programs, and it turned out to be fragile. The modern approach is composition: build behaviour by giving objects other objects to work with.

This chapter shows why inheritance breaks, then builds a small stack of reusable pieces, caching, retrying and logging, around a fare lookup, first by hand and then with .NET's dependency injection container. You will see how rearranging the pieces changes behaviour without editing any class, along with the patterns and pitfalls that come with this style.

### Before you start

**Builds on:** 2.6 Scope and lifetime, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Relationship** (7.5): How entities relate, expressed in a schema with foreign keys.
- **Client** (8.1): A program that sends requests to a server.
- **Generic host** (8.3): The .NET component that owns configuration, logging, dependency injection and app lifetime.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Backend** (8.1): The part of a system that runs on the organization's servers, holding data and enforcing rules.
- **TCP** (6.3): Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **UDP** (6.3): User Datagram Protocol: independent messages with ports and no delivery guarantees.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, generic types, lambdas, tuples, exceptions, using for cleanup, attributes, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does "favour composition over inheritance" mean in practice?**

> Build behaviour by giving an object other objects to work with, usually through interfaces, instead of deriving from a base class. You can then mix, swap and wrap behaviour without a rigid class hierarchy.

**2. What is the fragile base class problem?**

> A change inside a base class, even one that looks safe, breaks derived classes that relied on how it worked internally. Inheritance couples derived classes to their base's implementation, not just its contract.

**3. What does a decorator do?**

> It implements an interface by wrapping another implementation of the same interface, adding behaviour before or after passing each call through. Caching, logging and retries are typical decorators.

**4. What is the composition root?**

> The single place near the program's entry point where all the objects are created and wired together. In ASP.NET Core, it is the service registrations in Program.cs.

**5. Why is injecting IServiceProvider and resolving services from it inside a class considered an anti-pattern?**

> That is the service locator pattern. The class's real dependencies are hidden inside its methods instead of visible in its constructor, missing registrations fail only at run time, and tests must build a whole container.

**6. What are keyed services in .NET 8 dependency injection?**

> Registrations under a key, such as `AddKeyedSingleton<IPricingRule, PeakPricing>("peak")`. A consumer asks for a specific one with `[FromKeyedServices("peak")]`, so several implementations of one interface can coexist.

**7. A constructor takes eleven dependencies. What does that suggest?**

> The class probably does too many things. Dependency injection makes that visible; the fix is usually to split the class, not to hide the dependencies.

### Two ways to reuse behaviour

There are two basic ways for one piece of code to use another's behaviour.

With **inheritance**, a class is a kind of another class. `BusinessSeat` inherits from `Seat` and gets its members. The relationship is fixed when the code is written.

With **composition**, an object holds other objects and uses them. A `QuoteService` has an `IFareSource` and an `IPricingRule`, and asks them for help. The parts can be chosen, swapped and combined when the program runs.

Passing a call on to an object you hold is called **delegation**. It is the basic move of composition: "I don't do this myself; I ask my collaborator."

### Why inheritance is fragile

Inheritance ties a derived class closely to its base. It inherits the base's contract and also depends on how the base happens to work inside.

A famous example comes from Java. A developer derived from `HashSet` to count how many items were ever added, overriding both `add` and `addAll` to increase a counter. Adding three items with `addAll` counted six. Inside `HashSet`, `addAll` called `add` for each item, so each was counted twice. Nothing in the public contract said so. If a later version stopped calling `add`, the count would change again. This is the **fragile base class problem**: safe-looking changes in a base class break subclasses.

Inheritance has other costs too. A class can have only one base. `protected` members expose internals to every subclass, forever. Deep hierarchies make it hard to see which code actually runs.

In 1994, the "Gang of Four" book *Design Patterns* gave the advice that has guided object-oriented design since: favour object composition over class inheritance. **Composition over inheritance** doesn't ban inheritance. It says inheritance should be a deliberate choice for true "is a kind of" relationships with a stable base, not the default way to share code.

### Design patterns

That book named 23 **design patterns**: reusable shapes for recurring design problems. Many are composition patterns. Three appear in almost every codebase.

The **decorator pattern** wraps an object in another that implements the same interface. The wrapper adds something, such as caching, logging, retries or timing, and passes the call through. Decorators stack, and each one does one job. The code that uses the interface can't tell how many layers there are.

The **adapter pattern** wraps an object that has the wrong interface so it fits the one you need. A third-party weather client with its own method names becomes an `IWeatherSource` your code understands. Adapters keep outside libraries from spreading through your code.

The **strategy pattern** passes in the part of an algorithm that varies. A `Pricer` takes an `IPricingRule`; peak and off-peak rules are two strategies. A lambda is often the simplest strategy of all.

### Dependency injection, revisited

Scope and lifetime (2.6) introduced dependency injection: a class receives what it depends on instead of creating it. The usual form is **constructor injection**. The class lists its dependencies as constructor parameters, stores them and uses them.

This flips who is in control. The class no longer decides which fare source or pricing rule it uses; whoever creates it decides. That reversal is called **inversion of control**, sometimes summed up as "don't call us, we'll call you". The class becomes simpler and easier to test, because a test can pass in fakes. And the choice of implementations moves to one place.

That one place is the **composition root**: the spot, as close as possible to the program's entry point, where every object is created and wired together. In a console app it is the top of `Main`. In ASP.NET Core it is the service registrations in `Program.cs`. Nothing outside the composition root should decide which concrete class to create.

### Composition by hand

You don't need a framework to do this. Wiring objects together with `new` in the composition root is called **pure DI**, and for small programs it is often clearest.

```csharp
#:package Microsoft.Extensions.DependencyInjection@10.0.0
// compose.cs — run with: dotnet run compose.cs
using Microsoft.Extensions.DependencyInjection;

// 1. Pure DI: decorators stacked by hand in the composition root
var real = new SlowFareSource();
IFareSource fares = new LoggingFares(new CachingFares(new RetryingFares(real, attempts: 3)));
Console.WriteLine(fares.Quote("LHR-JFK"));   // [log] LHR-JFK: 450, then 450
Console.WriteLine(fares.Quote("LHR-JFK"));   // [log] LHR-JFK: 450, then 450, from the cache
Console.WriteLine($"calls to the real source: {real.Calls}");   // 2: one timeout, one success

// 2. Strategy: the varying part is passed in
Console.WriteLine(new Pricer(new PeakPricing()).Price(450m));      // 540.0
Console.WriteLine(new Pricer(new OffPeakPricing()).Price(450m));   // 405.0
Console.WriteLine(new Pricer(new LambdaPricing(f => f - 25m)).Price(450m));   // 425: a strategy from a lambda

// 3. The same wiring with a container
var services = new ServiceCollection();
services.AddSingleton<SlowFareSource>();
services.AddSingleton<IFareSource>(sp =>
    new LoggingFares(new CachingFares(new RetryingFares(sp.GetRequiredService<SlowFareSource>(), attempts: 3))));
services.AddKeyedSingleton<IPricingRule, PeakPricing>("peak");
services.AddKeyedSingleton<IPricingRule, OffPeakPricing>("off-peak");
services.AddTransient<QuoteService>();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

var quotes = provider.GetRequiredService<QuoteService>();
Console.WriteLine(quotes.Quote("LHR-JFK", peak: true));    // [log] LHR-JFK: 450, then 540.0
Console.WriteLine(quotes.Quote("LHR-JFK", peak: false));   // [log] LHR-JFK: 450, then 405.0

interface IFareSource { decimal Quote(string route); }

sealed class SlowFareSource : IFareSource
{
    public int Calls { get; private set; }
    public decimal Quote(string route)
    {
        Calls++;
        if (Calls == 1) throw new TimeoutException("fare system timed out");   // fails once, like a real network
        return 450m;
    }
}

sealed class RetryingFares(IFareSource inner, int attempts) : IFareSource
{
    public decimal Quote(string route)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return inner.Quote(route); }
            catch (TimeoutException) when (attempt < attempts) { }   // try again
        }
    }
}

sealed class CachingFares(IFareSource inner) : IFareSource
{
    readonly Dictionary<string, decimal> cache = new();
    public decimal Quote(string route)
    {
        if (cache.TryGetValue(route, out decimal known)) return known;
        return cache[route] = inner.Quote(route);
    }
}

sealed class LoggingFares(IFareSource inner) : IFareSource
{
    public decimal Quote(string route)
    {
        decimal fare = inner.Quote(route);
        Console.WriteLine($"[log] {route}: {fare}");
        return fare;
    }
}

interface IPricingRule { decimal Apply(decimal fare); }
sealed class PeakPricing : IPricingRule { public decimal Apply(decimal fare) => fare * 1.2m; }
sealed class OffPeakPricing : IPricingRule { public decimal Apply(decimal fare) => fare * 0.9m; }
sealed class LambdaPricing(Func<decimal, decimal> rule) : IPricingRule { public decimal Apply(decimal fare) => rule(fare); }

sealed class Pricer(IPricingRule rule)
{
    public decimal Price(decimal fare) => rule.Apply(fare);
}

sealed class QuoteService(
    IFareSource fares,
    [FromKeyedServices("peak")] IPricingRule peakRule,
    [FromKeyedServices("off-peak")] IPricingRule offPeakRule)
{
    public decimal Quote(string route, bool peak) => (peak ? peakRule : offPeakRule).Apply(fares.Quote(route));
}
```

Notice the order of the decorators. Logging is outermost, so it logs every quote, including cached ones. Caching sits outside retrying, so a cached answer never triggers a retry. Swap two lines and the behaviour changes, without touching any class.

### DI containers

A **DI container** automates the composition root. You register which implementation to use for each service and with which lifetime. When something asks for a service, the container works out its constructor parameters, creates those recursively, and hands back the finished object. .NET's built-in container is `Microsoft.Extensions.DependencyInjection`, used by ASP.NET Core and the Generic Host.

A few features are worth knowing. `ValidateOnBuild` checks at start-up that every registered service can actually be created, so a missing registration fails immediately instead of on the first request. Asking for `IEnumerable<IPricingRule>` returns every registered implementation. Open generics let one registration cover every type argument: `AddScoped(typeof(IRepository<>), typeof(Repository<>))`. A **keyed service**, added in .NET 8, is registered under a key, so several implementations of the same interface can be chosen by name.

The built-in container deliberately stays simple. It has no built-in decorator support, which is why the example registers the stack with a factory lambda. Libraries such as Scrutor add it.

### The service locator trap

It is tempting to inject `IServiceProvider` itself and ask it for whatever a method needs. This is the **service locator** pattern, and it is usually a mistake.

The class's constructor no longer shows what it depends on. A missing registration fails deep inside some method, in production, instead of at start-up. Tests must build a container, or fake a provider, to run the class at all. And lifetime rules are easy to break by accident.

There are legitimate uses, mostly inside infrastructure: a singleton that creates a scope per job with `IServiceScopeFactory`, as Scope and lifetime (2.6) showed, or framework code that resolves handlers by type. In ordinary application classes, ask for dependencies in the constructor.

### Factories

Sometimes an object needs both injected services and values known only at run time, such as a flight number chosen by the user. A **factory** handles this. It is an object or function, itself injected, that creates the object when given the run-time values. A `Func<string, FlightTracker>` registered in the container works as a simple factory. `IHttpClientFactory`, covered in Talking to the backend (9.7) and TCP and UDP (6.3), is a factory the framework provides to manage connection pooling safely.

### Configuration with the options pattern

Settings are dependencies too. The **options pattern** binds a section of configuration to a small class and injects it as `IOptions<T>`.

```csharp
// In Program.cs of an ASP.NET Core app
builder.Services.AddOptions<FareOptions>()
    .Bind(builder.Configuration.GetSection("Fares"))
    .ValidateDataAnnotations()      // [Range], [Required] and so on on FareOptions
    .ValidateOnStart();             // refuse to start with bad settings

public sealed class FareOptions
{
    [Range(0.5, 3.0)] public decimal PeakMultiplier { get; set; } = 1.2m;
    [Required] public string Currency { get; set; } = "GBP";
}
```

`ValidateOnStart` applies the fail-fast idea from Errors and exceptions (2.8): a misconfigured service stops at start-up with a clear message, instead of failing on some request hours later. `IOptionsMonitor<T>` gives values that update when configuration changes.

### Signs of a design problem

Dependency injection makes a class's needs visible, and that is useful feedback.

A constructor with eight or ten parameters is a class doing too much. Split it by responsibility, rather than bundling the parameters into an "aggregate service" that hides them.

A dependency used by only one method suggests that method belongs somewhere else.

Circular dependencies, where A needs B and B needs A, fail in the container and signal that the two classes are really one concept, or that a third concept is missing between them.

### In short

Favour composition over inheritance, because inheritance ties classes to each other's internals. Decorators wrap an interface to add behaviour, adapters make classes fit an interface, and strategies pass in the part that varies. Receive dependencies through constructors and wire everything together in one place, by hand or with a container. Avoid resolving services from inside classes, validate registrations at start-up, and treat a huge constructor as a sign that a class does too much.

### What goes wrong

**Inheriting to reuse code.** A class derives from `BaseController` or `BaseService` to pick up helpers, and every change to the base ripples through dozens of subclasses. Move the helpers into injectable services.

**`new` scattered through the code.** Classes create their own database clients and HTTP clients, so nothing can be replaced for tests or configuration. Create concrete classes only in the composition root.

**Service locator.** Hidden dependencies and run-time resolution failures. Use constructor injection, and turn on `ValidateOnBuild`.

**Decorators in the wrong order.** A retry outside a cache retries cache misses only, while a retry inside a circuit breaker may defeat it. Decide the order deliberately and test it.

**Captive dependencies.** A singleton decorator wrapping a scoped service keeps one instance forever, as Scope and lifetime (2.6) warned. Match decorator lifetimes to what they wrap.

**Configuration read everywhere.** Code calls `configuration["Fares:Multiplier"]` in many places, with no validation and no single definition. Use the options pattern.

### Cost model

Each decorator adds one interface call, a few nanoseconds, which is negligible next to the network calls they usually wrap. Resolving a service from the container costs tens to hundreds of nanoseconds, and the container caches how to build each type after the first request. `ValidateOnBuild` adds a little start-up time in exchange for failing early. The real costs of composition are indirection and discoverability: more small types to navigate. Good names and a clear composition root pay that back.

### In other languages (optional)

**TypeScript.** Angular has a DI container built in, with providers and injection tokens. In React, composition of components is the main tool, and context passes shared services down the tree. Plain TypeScript code often uses pure DI: pass dependencies as constructor or function parameters.

**Python.** Mostly pure DI through function and constructor parameters. Frameworks such as FastAPI have their own dependency injection for request handlers.

**Java.** Spring made DI containers mainstream, using annotations and classpath scanning. Its concepts map closely to .NET's.

**Go.** No containers by convention. Dependencies are passed explicitly in `main`, and struct embedding provides composition.

### Key terms

- **Composition**: Building behaviour by giving an object other objects to work with.
- **Delegation**: Passing a call on to an object you hold instead of handling it yourself.
- **Fragile base class problem**: Safe-looking changes in a base class breaking derived classes that depended on its internals.
- **Composition over inheritance**: The guideline to prefer combining objects over deriving classes when sharing behaviour.
- **Design pattern**: A named, reusable shape for solving a recurring design problem.
- **Decorator pattern**: Wrapping an object in another with the same interface to add behaviour around each call.
- **Adapter pattern**: Wrapping an object so that it fits an interface it doesn't implement itself.
- **Strategy pattern**: Passing in the part of an algorithm that varies as an object or function.
- **Constructor injection**: Receiving dependencies as constructor parameters.
- **Inversion of control**: Letting outside code decide which implementations a class works with, instead of the class itself.
- **Composition root**: The single place near the entry point where all objects are created and wired together.
- **Pure DI**: Wiring dependencies by hand with `new` in the composition root, without a container.
- **DI container**: A library that creates objects and their dependencies from registrations.
- **Keyed service**: A service registered under a key, so several implementations of one interface can be requested by name.
- **Service locator**: Asking a container for dependencies from inside a class instead of receiving them. Usually an anti-pattern.
- **Factory**: An object or function that creates other objects, often combining injected services with run-time values.
- **Options pattern**: Binding a configuration section to a class and injecting it, with validation.

### Where this leads

Keeping decorators and strategies free of hidden state is in Immutability and pure functions (4.4). The principles behind small, focused dependencies are in Design principles that hold up (4.5). Replacing dependencies with fakes in tests is in Testing (4.6). A full ASP.NET Core composition root is in A modern .NET backend (8.3). Resilience decorators around `HttpClient` are in Reliability (12.5).

### Check yourself

1. Swap the order of `CachingFares` and `RetryingFares` in `compose.cs`. Predict how the output and the call count change, then run it.
2. Write a `TimingFares` decorator that prints how long each quote took. Decide where in the stack it belongs to measure real fare lookups only.
3. Remove the `off-peak` registration and run again. Check whether `ValidateOnBuild` catches it at start-up.
4. Write an adapter that makes a class with a method `GetPrice(string origin, string destination)` fit `IFareSource`.
5. Find a class in real code that inherits from a base class only to reuse helper methods. Sketch how it would look using composition instead.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Adding caching and retries without touching the client

The exchange-rate client needs a 5-minute cache and logging, but its class is used in 30 places.

*A decorator registered in one place*

```csharp
public sealed class CachedRates(IRateClient inner, IMemoryCache cache) : IRateClient
{
    public Task<decimal> GetAsync(string from, string to, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{from}:{to}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return inner.GetAsync(from, to, ct);
        })!;
}

builder.Services.AddHttpClient<RateClient>().AddStandardResilienceHandler();   // retries, timeouts
builder.Services.AddScoped<IRateClient>(sp =>
    new CachedRates(sp.GetRequiredService<RateClient>(), sp.GetRequiredService<IMemoryCache>()));
```

> **Lesson:** Composition adds behaviour around an interface. Wiring in one place changes behaviour without editing any caller.

#### Example 2: Scoped services inside a background worker

A BackgroundService that sends reminder emails gets `Cannot consume scoped service 'BookingDb' from singleton`.

*A scope per unit of work*

```csharp
public sealed class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ReminderSender>();
                await sender.SendDueAsync(stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Reminder batch failed");   // keep the loop alive
            }
        }
    }
}
```

> **Lesson:** BackgroundService is a singleton. Create a fresh scope for each iteration and catch errors so the loop survives.

### Dependency Injection & Concurrency

<p class="src">From <em>.NET Backend Deep Dive</em> · Dependency Injection & Concurrency</p>

<a id="dn-9"></a>

#### Scopes, custom scopes, and lifetimes

##### The three lifetimes

| Lifetime | Lives for | Example |
|---|---|---|
| Transient | A new instance per resolution | Lightweight stateless helpers |
| Scoped | One instance per request (or per scope you create) | `DbContext`, `ICurrentUser`, unit of work |
| Singleton | The lifetime of the app | Caches, `HttpClientFactory`, config, `IHostedService` |

##### Captive dependency — the most common DI bug

```csharp
public class MySingleton
{
    private readonly AppDbContext _db;      // Scoped "captured" inside a Singleton
    public MySingleton(AppDbContext db) => _db = db;
    // This DbContext now lives forever: memory leak, a connection held indefinitely,
    // an ever-growing ChangeTracker, and stale data.
}
```

Turn on validation from day one:

```csharp
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;      // throw when resolving scoped from the root provider
    o.ValidateOnBuild = true;     // catch it at startup instead of at runtime
});
```

##### Custom scopes — "created here, disposed there, and nothing else is affected"
A scope you create is **completely independent** of the request scope. When it's disposed, every scoped service inside it (including its `DbContext`) is disposed. It has **no effect** on the request's own `DbContext` — they're different instances.

```csharp
using (var scope = _scopeFactory.CreateScope())          // scope OPENS
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // this db exists ONLY inside the block
    await db.SaveChangesAsync();
}                                                        // scope CLOSES -> db.Dispose()
// Using db here => ObjectDisposedException
```

Use the async variant so `DisposeAsync` runs:

```csharp
await using var scope = _scopeFactory.CreateAsyncScope();
```

**Trap:** don't let entities or services from that scope leak out of the block. Return mapped DTOs, not tracked entities.

---

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a58"></a>

#### Dependency injection and service lifetimes

A DI container is a dictionary from abstraction to implementation plus a policy for how long each instance lives. .NET ships one in the box (`Microsoft.Extensions.DependencyInjection`); it is deliberately simple and it is what the whole framework is built on.

| Lifetime  | One instance per            | Typical use                                          |
|-----------|-----------------------------|------------------------------------------------------|
| Singleton | Application                 | Caches, HttpClientFactory, config, stateless helpers |
| Scoped    | Request (or explicit scope) | DbContext, unit of work, per-request user context    |
| Transient | Injection site              | Lightweight stateless services, mappers, validators  |

``` csharp
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddTransient<IEmailFormatter, EmailFormatter>();

// keyed services (.NET 8+)
builder.Services.AddKeyedScoped<IPaymentGateway, StripeGateway>("stripe");
public class Checkout([FromKeyedServices("stripe")] IPaymentGateway gw) { }
```

> **\[Trap\]**
>
> **Common trap.** **dependency — see Terminology"\>Captive dependency.** Injecting a scoped `DbContext` into a singleton pins one DbContext for the process lifetime — stale data, thread-safety violations, connection leaks. Inject `IServiceScopeFactory` and create a scope per unit of work instead.

###### Worked example

The captive dependency bug, shown end to end, because reading the definition rarely makes it stick.

``` csharp
// registrations that look completely reasonable
services.AddDbContext<AppDb>(o => o.UseNpgsql(cs));         // SCOPED (this is the default)
services.AddSingleton<PriceCache>();                        // SINGLETON

public class PriceCache(AppDb db)                           // <- the mistake
{
    private Dictionary<int, decimal>? _prices;

    public async Task<decimal> GetAsync(int id)
    {
        _prices ??= await db.Prices.ToDictionaryAsync(p => p.Id, p => p.Value);
        return _prices[id];
    }
}
```

``` text
  what happens

  request 1  -> scope A created -> PriceCache built ONCE, capturing scope A's AppDb
             -> scope A disposed at end of request -> AppDb.Dispose() called
  request 2  -> PriceCache is the same singleton instance, still holding the DISPOSED AppDb
             -> ObjectDisposedException, OR (worse) it happens to still work
                because the query was cached, and the bug surfaces days later
  under load -> multiple requests hit the singleton concurrently, all sharing one
                DbContext, which is not thread-safe -> corrupted change tracker,
                "A second operation was started on this context" exceptions
```

``` csharp
// fix 1: the singleton creates a scope per operation
public class PriceCache(IServiceScopeFactory scopes)
{
    private Dictionary<int, decimal>? _prices;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<decimal> GetAsync(int id, CancellationToken ct)
    {
        if (_prices is null)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (_prices is null)
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    _prices = await db.Prices.ToDictionaryAsync(p => p.Id, p => p.Value, ct);
                }
            }
            finally { _gate.Release(); }
        }
        return _prices[id];
    }
}

// fix 2: catch it at startup instead of in production
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
// -> InvalidOperationException at Build(): "Cannot consume scoped service 'AppDb'
//    from singleton 'PriceCache'."
```

`ValidateScopes` is on by default in Development and off in Production. Turning it on everywhere costs a few milliseconds at startup and converts an intermittent production data bug into a deterministic boot failure. There is no good reason not to.

###### Choosing a lifetime, with the actual questions to ask

| Question                                            | If yes                                                          |
|-----------------------------------------------------|-----------------------------------------------------------------|
| Does it hold mutable per-request state?             | Scoped — a singleton would share it across users                |
| Is it expensive to construct and stateless?         | Singleton                                                       |
| Is it thread-safe?                                  | If not, it cannot be a singleton                                |
| Does it hold an unmanaged resource or a connection? | Scoped, disposed with the request                               |
| Is it cheap and stateless?                          | Transient is fine; singleton is also fine and avoids allocation |
| Does it depend on anything scoped?                  | It cannot be a singleton, whatever else is true                 |

A useful default for application services: **scoped**. It is almost never wrong, it composes with `DbContext`, and the cost of a per-request allocation is negligible next to the cost of the I/O the request is doing. Reach for singleton when you have measured a reason.

### Under the Hood

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Under the Hood</p>

<p class="pd">What actually happens between dotnet run and a byte on the wire. Assembly loading, the JIT, the thread pool, Kestrel, routing, model binding, filters and the DI container — the machinery that makes everything above it feel like magic until it breaks.</p>

<a id="be-a48"></a>

#### The DI container at runtime

`IServiceCollection` is just a list of `ServiceDescriptor` records. `Build()` turns that list into a `ServiceProvider`, and from then on the registration list is frozen — you cannot add services after the app starts.

``` text
  registration      ServiceDescriptor { ServiceType, ImplementationType, Lifetime }
        |
   Build()  ->  ServiceProviderEngine
        |         builds a CALL SITE tree per service type
        |         (constructor -> its parameters -> their constructors ...)
        v
   first resolve   interpreted call site        (fast to create)
   after ~2 calls  compiled expression tree      (fast to run)

  Singletons cached on the root provider
  Scoped     cached on the current IServiceScope
  Transient  built every time; if IDisposable, tracked by the owning scope
```

``` csharp
// validate the whole graph at startup instead of on first request
builder.Host.UseDefaultServiceProvider((ctx, o) =>
{
    o.ValidateScopes  = true;   // catches captive dependencies
    o.ValidateOnBuild = true;   // catches missing registrations at boot
});
```

> **\[Trap\]**
>
> **Common trap.** Transient `IDisposable` services are tracked by the scope that resolved them and only disposed when it ends. Resolving transient disposables from the *root* provider — which a singleton effectively does — means they are never released until shutdown. That is a slow, silent memory leak with a clean-looking stack trace.

When you need a third-party container (Autofac, Lamar) for decorators, assembly scanning or interception, swap it at the host level with `UseServiceProviderFactory`; the framework keeps using `IServiceProvider` and never notices.

###### How it actually works

A concrete resolution walkthrough. Given these registrations:

``` csharp
services.AddSingleton<IClock, SystemClock>();
services.AddScoped<AppDb>();
services.AddScoped<IOrderRepository, OrderRepository>();     // ctor(AppDb, IClock)
services.AddTransient<IOrderService, OrderService>();        // ctor(IOrderRepository, ILogger<OrderService>)
```

``` text
  first request for IOrderService:

  build the call site tree (ONCE, then cached):
    IOrderService -> ctor OrderService
       IOrderRepository -> ctor OrderRepository
          AppDb        -> ctor AppDb(DbContextOptions)   [scoped]
          IClock       -> SystemClock                    [singleton, root-cached]
       ILogger<OrderService> -> from ILoggerFactory      [singleton]

  resolve:
    IClock            -> already in the ROOT cache, returned
    AppDb             -> not in THIS scope's cache -> construct, cache in scope,
                         and register for disposal at scope end
    IOrderRepository  -> construct, cache in scope
    IOrderService     -> construct fresh (transient); it is IDisposable? then the
                         SCOPE tracks it and disposes it when the request ends
```

Two consequences follow directly. First, resolution is fast after the first call because the tree is compiled into an expression — but the first call in a process is not, which contributes to cold start. Second, disposal is owned by whichever scope resolved the instance, which is the mechanism behind the transient-disposable leak.

``` csharp
// the leak, precisely
services.AddSingleton<ReportRunner>();          // lives for the whole process
services.AddTransient<IExporter, FileExporter>();   // IDisposable

public class ReportRunner(IServiceProvider root)   // injected root provider
{
    public void Run()
    {
        var e = root.GetRequiredService<IExporter>();   // resolved from the ROOT scope
        e.Export();                                      // never disposed until shutdown
    }                                                    // one leaked file handle per call
}

// fix: create a scope, or inject a factory delegate
public class ReportRunner2(IServiceScopeFactory scopes)
{
    public void Run()
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<IExporter>().Export();
    }   // disposed here
}
```

###### Registration techniques you will need eventually

``` csharp
// 1. register many implementations, resolve as IEnumerable
services.Scan(s => s.FromAssemblyOf<IValidator>()
    .AddClasses(c => c.AssignableTo(typeof(IValidator<>)))
    .AsImplementedInterfaces()
    .WithScopedLifetime());                                    // Scrutor

// 2. one concrete type behind two interfaces, sharing ONE instance
services.AddSingleton<TokenCache>();
services.AddSingleton<ITokenReader>(sp => sp.GetRequiredService<TokenCache>());
services.AddSingleton<ITokenWriter>(sp => sp.GetRequiredService<TokenCache>());
// NOT AddSingleton<ITokenReader, TokenCache>() twice - that creates two caches

// 3. conditional registration
services.TryAddSingleton<IClock, SystemClock>();      // only if nobody registered one
services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, MyFilter>());

// 4. options that depend on other services
services.AddOptions<SmtpOptions>()
    .Configure<IHostEnvironment>((o, env) => o.Sandbox = !env.IsProduction());

// 5. a factory delegate instead of a factory interface, for simple cases
services.AddSingleton<Func<string, IExporter>>(sp => key => key switch
{
    "csv"  => sp.GetRequiredService<CsvExporter>(),
    "xlsx" => sp.GetRequiredService<XlsxExporter>(),
    _      => throw new NotSupportedException(key)
});
```

### How an application is wired

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How an application is wired</p>

<a id="be-m229"></a>

#### Dependency injection: the container and resolution

An `IServiceCollection` is a list of *descriptors*: service type, implementation, lifetime. `Build()` compiles that list into a provider that can construct object graphs on demand.

``` csharp
services.AddScoped<IOrderQueries, OrderQueries>();          // by type
services.AddSingleton<IClock>(TimeProvider.System);         // by instance
services.AddScoped<IRepo>(sp =>                             // by factory
    new Repo(sp.GetRequiredService<AppDb>(), sp.GetRequiredService<ILogger<Repo>>()));
services.TryAddScoped<ICache, MemoryCache>();               // only if not registered
services.AddKeyedScoped<IExporter, CsvExporter>("csv");     // .NET 8 keyed services
```

**Resolution** picks the constructor with the most parameters it can satisfy, resolves each recursively, and caches according to lifetime. Registering the same service type twice does not replace — the last wins for a single resolve, and *all* are returned by `IEnumerable<T>`, which is how pipelines of handlers are built.

``` csharp
services.AddScoped<IValidator, RangeValidator>();
services.AddScoped<IValidator, RequiredValidator>();
// ctor(IEnumerable<IValidator> validators) receives both, in registration order.
```

**Disposal is ownership-based:** the container disposes anything it constructed that implements `IDisposable`, when the owning scope ends. It does *not* dispose instances you registered yourself — you gave it an object you already owned.

> **\[Trap\]**
>
> **Transient disposables are captured by the resolving scope.** A transient `IDisposable` resolved from the *root* provider is held until the application shuts down — so a transient `HttpClient` or `DbContext` resolved outside a scope is an unbounded leak that looks like a slow memory climb. Either make it scoped, or do not register disposables as transient ([Connection pooling and exhaustion](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462)).

> **\[Tip\]**
>
> **Coming from Ninject or Autofac,** expect `Microsoft.Extensions.DependencyInjection` to be deliberately minimal: no property injection, no auto-registration by convention, no interception, no child kernels beyond scopes. That is a design choice — it is a common abstraction that richer containers plug into. If a legacy codebase depends on those features, keep the richer container and bridge it via `IServiceProviderFactory` rather than rewriting every binding.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a82"></a>

#### Composition over inheritance, in practice

Inheritance couples a subclass to a base class's implementation forever, across every future change to that base. Composition couples it only to an interface. The .NET base class library is itself a long argument for this — `Stream` is composed, not subclassed, in almost all real usage.

``` csharp
// inheritance: the fragile base class problem
public class Repository<T> where T : class
{
    protected readonly AppDb Db;
    public virtual Task<T?> GetAsync(Guid id, CancellationToken ct) => Db.Set<T>().FindAsync(...);
    public virtual Task AddAsync(T e, CancellationToken ct) { ... }
}

public class OrderRepository : Repository<Order>
{
    // needs Include; must override and duplicate the base logic
    public override Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        Db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);
}
// six months later, the base adds soft-delete filtering to GetAsync.
// OrderRepository silently does NOT get it, because it overrode the method.
```

``` csharp
// composition: shared behaviour is a collaborator, not an ancestor
public sealed class OrderRepository(AppDb db, ISoftDeleteFilter filter) : IOrderRepository
{
    public Task<Order?> FindAsync(OrderId id, CancellationToken ct) =>
        filter.Apply(db.Orders).Include(o => o.Lines)
              .SingleOrDefaultAsync(o => o.Id == id, ct);
}
// the filter changes -> every repository that composes it gets the change.
// no repository can accidentally opt out by overriding.
```

| Reach for inheritance when                                                 | Reach for composition when                     |
|----------------------------------------------------------------------------|------------------------------------------------|
| The relationship is genuinely ‘is a kind of’                               | The relationship is ‘uses’ or ‘has’            |
| The base is abstract and its contract is stable                            | The shared code is likely to change            |
| The framework requires it (`DbContext`, `BackgroundService`, `Controller`) | You are sharing code, not modelling a taxonomy |
| The hierarchy is one level deep                                            | You would need a second or third level         |

Modern C# blunts the choice: default interface members share behaviour without a base class, extension methods add operations to types you do not own, and primary constructors make composition as terse as inheritance ever was. `sealed` by default is a reasonable house rule — unseal deliberately, with a reason, rather than leaving every class open by accident.

###### The refactor, and where inheritance is still right

``` csharp
// inheritance for reuse - the version that stops scaling
public abstract class ReportBase
{
    protected ILogger Logger { get; }
    protected AppDbContext Db { get; }
    protected IEmailSender Email { get; }      // half the subclasses never email
    protected IExcelWriter Excel { get; }      // a third never write Excel
}
// the tell: adding a dependency for ONE subclass changes the base class,
// therefore every subclass, therefore every test.

// composition - each report takes only what it uses
public sealed class SalesReport(AppDbContext db, IExcelWriter excel)
{
    public async Task<Stream> GenerateAsync(DateOnly from, DateOnly to,
                                            CancellationToken ct)
    {
        var rows = await db.Orders.Where(...).Select(...).ToListAsync(ct);
        return excel.Write(rows);
    }
}
// the constructor is now DOCUMENTATION: it lists exactly what this report
// depends on, and a test supplies exactly those two.
```

| Inheritance is right when                   | Example                                            |
|---------------------------------------------|----------------------------------------------------|
| A genuine *is-a* relationship in the domain | `Circle : Shape`                                   |
| A framework requires it                     | `ControllerBase`, `BackgroundService`, `DbContext` |
| A closed hierarchy you own entirely         | `abstract record Payment` with sealed cases        |
| The base is abstract and adds no state      | A marker for pattern matching                      |

| Inheritance is wrong when           | Symptom                                        |
|-------------------------------------|------------------------------------------------|
| It exists to share code             | A `*Base` class of protected helpers           |
| Subclasses override to *disable*    | An override that throws, or does nothing       |
| The base knows about subclasses     | A type check or switch inside the base         |
| The hierarchy is more than two deep | Nobody can tell where a method is implemented  |
| You need behaviour from two places  | C# has no multiple inheritance — you are stuck |

A useful reframing: inheritance is for **substitutability**, not for reuse. Ask “will callers hold a reference to the base type and treat every subclass identically?” If yes, the hierarchy is doing real work. If the base type never appears in a signature and exists only so subclasses can call `protected` helpers, that is a service waiting to be injected.

### Design Principles

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Design Principles</p>

<p class="pd">The principles the patterns are made of. SOLID stated precisely rather than as slogans, plus the shorter rules and what to do when they conflict — which they routinely do.</p>

<a id="be-h732"></a>

#### Composition over inheritance

Inheritance is for **substitutability**, not for reuse. That single distinction resolves most arguments about it, and it is the reason so many GoF patterns exist — several are explicitly composition-based alternatives to an inheritance hierarchy.

``` text
  WHY INHERITANCE STOPS SCALING - the combinatorial problem

    you have Reports. Some are scheduled. Some are emailed. Some are
    compressed. Any combination is possible.

    with inheritance:
      Report
        ScheduledReport
          ScheduledEmailedReport
            ScheduledEmailedCompressedReport
        EmailedReport
          EmailedCompressedReport
        CompressedReport
      -> 2^3 = 8 classes for 3 independent traits. 4 traits = 16.

    with composition:
      new Compressed(new Emailed(new Scheduled(report)))
      -> 3 classes, and any combination assembled at runtime.

    this is DECORATOR, and the combinatorial explosion is exactly the
    force it exists to answer.
```

``` csharp
// inheritance for REUSE - the shape that stops working
public abstract class ReportBase
{
    protected ILogger Logger { get; }
    protected AppDbContext Db { get; }
    protected IEmailSender Email { get; }     // half the subclasses never email
    protected IExcelWriter Excel { get; }     // a third never write Excel
}
// the tell: adding a dependency for ONE subclass changes the base class,
// therefore every subclass, therefore every test.

// composition - each takes exactly what it uses
public sealed class SalesReport(AppDbContext db, IExcelWriter excel)
{
    public Task<Stream> GenerateAsync(...) { ... }
}
// the constructor is now documentation: it lists what this report needs.
```

| Inheritance is right when                               | Wrong when                                    |
|---------------------------------------------------------|-----------------------------------------------|
| A genuine *is-a*, and substitutable                     | It exists to share code                       |
| A framework requires it (`ControllerBase`, `DbContext`) | Subclasses override methods to *disable* them |
| A closed hierarchy you own, for pattern matching        | The base must know about its subclasses       |
| The base is abstract and holds no state                 | The hierarchy is more than two levels deep    |
| Callers hold the base type and treat all subtypes alike | You need behaviour from two places            |

The test that settles it: **does the base type ever appear in a method signature?** If callers hold `Shape` and treat every subtype identically, the hierarchy is doing real work. If the base type appears nowhere except in `: BaseClass` declarations, it exists only so subclasses can call `protected` helpers — and that is a collaborator waiting to be injected.

###### The refactor, step by step, with nothing breaking in between

``` csharp
// BEFORE: a base class with four dependencies, used by nine subclasses.
public abstract class ReportBase
{
    protected ILogger Logger { get; }
    protected AppDbContext Db { get; }
    protected IEmailSender Email { get; }
    protected IExcelWriter Excel { get; }

    protected ReportBase(ILogger l, AppDbContext db, IEmailSender e, IExcelWriter x)
    { Logger = l; Db = db; Email = e; Excel = x; }

    protected void Notify(string to, Stream s) { /* uses Email */ }
}
public sealed class SalesReport : ReportBase { /* uses Db and Excel only */ }
```

``` text
  THE REFACTOR, in four shippable steps. Nothing is broken between them.

  STEP 1  extract the base's BEHAVIOUR to a service
            protected void Notify(...)  ->  class ReportNotifier
          the base now DELEGATES to it. Every subclass still compiles.
          -> ship it.

  STEP 2  push dependencies down, one subclass at a time
            SalesReport takes (AppDbContext, IExcelWriter) directly and
            passes nulls or defaults to the base for what it does not use.
          ugly, and temporary.
          -> ship it.

  STEP 3  once a subclass uses NOTHING from the base, drop the inheritance
            public sealed class SalesReport(AppDbContext db, IExcelWriter x)
          the base shrinks as each subclass leaves.
          -> ship each one.

  STEP 4  when the last subclass leaves, DELETE the base class.

  the property that makes this safe: at every step the code compiles,
  the tests pass, and it can be deployed. A refactor that is only
  correct at the end is a rewrite, and it gets abandoned at 70%.
```

| What the base was doing                  | Becomes                                          |
|------------------------------------------|--------------------------------------------------|
| Holding shared dependencies              | Constructor parameters on each class             |
| Providing helper methods                 | An injected service                              |
| Enforcing a call sequence                | A method taking the collaborators, or a pipeline |
| Providing a shared type for a collection | An interface, if callers genuinely need one      |
| Nothing — just grouping                  | Delete it; a namespace groups things             |

The signal that step 4 has arrived is worth naming: the base class's constructor becomes empty, and its body holds only abstract members. At that point it is an *interface*, and if callers never hold the type, not even that.

### What Modern C# Changed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · What Modern C# Changed</p>

<p class="pd">Several GoF patterns exist to work around limitations C++ and Smalltalk had. Modern C# removed some of those limitations — here is what changed, and what did not.</p>

<a id="be-h755"></a>

#### What dependency injection absorbed

``` text
  DI is a general solution to "how does this object get its collaborators?"
  which is the question most creational patterns were answering.

    Singleton          -> AddSingleton
                          same guarantee. Visible in the constructor,
                          substitutable in a test, disposed at shutdown.

    Factory Method     -> an injected Func<T>, or a keyed service.
                          no inheritance needed.

    Abstract Factory   -> usually several registrations. The container
                          resolves a consistent set because you configured it.
                          Keep the factory only when mixing implementations
                          would be INVALID.

    Service Locator    -> NOT absorbed. REJECTED.
                          asking the container for a dependency mid-method
                          reintroduces exactly the hidden-dependency
                          problem that made Singleton bad.

    Object Pool        -> survives. Different problem: allocation, not
                          resolution.
    Builder            -> survives. Different problem: assembly, not
                          resolution.
```

``` csharp
// Strategy, before and after DI

// BEFORE: a factory, a switch, and a registry to maintain
public class PricingFactory
{
    public IPricingStrategy Create(PlanType plan) => plan switch
    {
        PlanType.Volume   => new VolumePricing(),
        PlanType.Contract => new ContractPricing(new TaxService()),  // and its deps
        _ => new StandardPricing()
    };
}

// AFTER: registration is the factory, and the container handles the graph
services.AddScoped<IPricingStrategy, VolumePricing>();
services.AddScoped<IPricingStrategy, ContractPricing>();
services.AddScoped<IPricingStrategy, StandardPricing>();

public class Pricer(IEnumerable<IPricingStrategy> all)   // all of them, injected
{
    public Money Price(Customer c, Order o) =>
        all.First(s => s.AppliesTo(c)).Calculate(o);
}
```

> **\[Note\]**
>
> **The general principle.** A pattern that exists to *hide construction* is largely redundant when a container does construction. A pattern that exists to *structure behaviour* — Strategy, Decorator, State, Command — is unaffected, because DI never addressed that. Sorting the catalogue by that distinction tells you which half to still take seriously.

###### Captive dependencies, and the validation that catches them

``` text
  THE LIFETIME MISMATCH that every framework factory exists to solve

    SINGLETON lives for the application.
    SCOPED    lives for one request.

    singleton --injects--> scoped

      the singleton captures ONE instance and holds it FOREVER.
      that DbContext is now shared by every request, concurrently,
      for the life of the process.

      -> a CAPTIVE DEPENDENCY. Corrupt change-tracker state, threading
         errors, and a connection never returned to the pool.

  .NET detects the DIRECT case at startup and throws.
  it CANNOT detect the indirect case - through a lambda, a captured
  IServiceProvider, or a factory delegate you wrote yourself.

  the factory is the fix: capture the ABILITY TO CREATE, not an instance.
```

``` csharp
// WRONG - a captive DbContext, and it will work in testing
public class CacheWarmer(AppDbContext db) : IHostedService { }   // SINGLETON

// RIGHT - a scope per unit of work
public class CacheWarmer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await WarmAsync(db, ct);
    }                                  // scope disposed -> context disposed
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// and catch it at STARTUP rather than under load
builder.Host.UseDefaultServiceProvider((ctx, o) =>
{
    o.ValidateScopes  = true;   // singleton depending on scoped -> throws
    o.ValidateOnBuild = true;   // ANY unresolvable service -> throws
});
// both default to ON in Development ONLY. Turn them on everywhere -
// the cost is milliseconds at startup, and the alternative is
// discovering it as a 500 on one endpoint, weeks later.
```

| Lifetime    | Lives for            | Do not inject                                                         |
|-------------|----------------------|-----------------------------------------------------------------------|
| `Singleton` | The application      | Scoped or transient-with-state                                        |
| `Scoped`    | One request or scope | Nothing shorter exists                                                |
| `Transient` | One resolution       | — but beware: a transient captured by a singleton becomes a singleton |

The transient row catches people. A transient service injected into a singleton is resolved *once* and held forever — so it behaves as a singleton, regardless of its registration. Lifetime is determined by the *longest-lived thing holding a reference*, not by the registration.

### Factories, Lifetimes & Object Creation

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Factories, Lifetimes & Object Creation</p>

<p class="pd">Almost every awkward question in a .NET codebase — ‘how do I get a DbContext in a singleton?’, ‘why is my HttpClient exhausting sockets?’, ‘how do I pick an implementation at runtime?’ — has the same answer: a factory. Here is the whole family.</p>

<a id="be-a84"></a>

#### Why the framework is full of factories

Constructor injection works when the dependency's lifetime matches yours and its construction needs nothing from the caller. A factory exists for exactly the cases where one of those is false.

| Problem                                   | Factory that solves it                            |
|-------------------------------------------|---------------------------------------------------|
| I live longer than what I need            | `IServiceScopeFactory`                            |
| Construction needs a runtime argument     | `ActivatorUtilities`, or a typed factory delegate |
| The instance must be pooled or reset      | `ObjectPool<T>`, `IDbContextFactory<T>`           |
| The underlying handler must rotate        | `IHttpClientFactory`                              |
| I need several of the same thing, by name | Named options, keyed services                     |
| Which implementation depends on data      | A strategy resolver over `IEnumerable<T>`         |

The anti-pattern to avoid is the service locator: injecting `IServiceProvider` and calling `GetService` wherever you feel like it. It hides dependencies, defeats compile-time checking, and makes testing miserable. A narrow, purpose-named factory interface is the legitimate version of the same idea.

``` csharp
// service locator - avoid
public class BadHandler(IServiceProvider sp)
{
    public void Run() => sp.GetRequiredService<IEmailSender>().Send();  // invisible dependency
}

// explicit factory - fine
public interface IReportGeneratorFactory { IReportGenerator Create(ReportKind kind); }
public class GoodHandler(IReportGeneratorFactory factory) { }
```

###### The lifetime mismatch that factories exist to solve

``` text
  the problem, precisely

    a SINGLETON lives for the application.
    a SCOPED service lives for one request.

    singleton  --injects-->  scoped
      the singleton captures ONE instance and holds it forever.
      that DbContext is now shared by every request, concurrently,
      for the life of the process.
      -> a CAPTIVE DEPENDENCY: corrupt state, threading errors, and a
         connection that is never returned.

    .NET detects the DIRECT case at startup (ValidateScopes) and throws.
    it CANNOT detect the indirect case through a lambda or a captured
    IServiceProvider.

  the factory is the fix: instead of capturing an INSTANCE, capture the
  ABILITY TO CREATE one, and create it per unit of work.
```

``` csharp
// WRONG - a captive DbContext
public class CacheWarmer(AppDbContext db) : IHostedService { }   // singleton!

// RIGHT - capture the factory, create a scope per unit of work
public class CacheWarmer(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await WarmAsync(db, ct);
    }                                    // scope disposed -> DbContext disposed
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// catch it at startup rather than in production
builder.Host.UseDefaultServiceProvider((ctx, o) =>
{
    o.ValidateScopes  = true;   // on by default in Development ONLY
    o.ValidateOnBuild = true;   // fail fast on ANY unresolvable graph
});
// turn BOTH on in every environment. The cost is milliseconds at startup;
// the alternative is discovering it under load.
```

| Factory                | Creates           | Because                                          |
|------------------------|-------------------|--------------------------------------------------|
| `IServiceScopeFactory` | A DI scope        | Singletons need scoped services per unit of work |
| `IHttpClientFactory`   | `HttpClient`      | Handler pooling and DNS refresh                  |
| `IDbContextFactory<T>` | `DbContext`       | Blazor, background work, parallel queries        |
| `ILoggerFactory`       | `ILogger`         | The category is per-consumer                     |
| `IOptionsFactory<T>`   | Options instances | Named and reloadable configuration               |
| `ActivatorUtilities`   | Anything          | Mixing DI services with runtime arguments        |

<a id="be-a85"></a>

#### Scopes and IServiceScopeFactory

A scope is a lifetime boundary. ASP.NET Core creates one per request and disposes it when the response completes; anything else that needs scoped services must create its own.

``` csharp
public sealed class InvoiceWorker(IServiceScopeFactory scopes, ILogger<InvoiceWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            // one scope per unit of work: fresh DbContext, disposed deterministically
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var svc = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

            try { await svc.RunPendingAsync(ct); }
            catch (Exception ex) { log.LogError(ex, "Invoice pass failed"); }
        }   // scope disposed -> DbContext disposed -> connection returned
    }
}
```

| Where you are           | How to get a scope                                       |
|-------------------------|----------------------------------------------------------|
| HTTP request handler    | You already have one — just inject the scoped service    |
| `BackgroundService`     | `IServiceScopeFactory.CreateAsyncScope()` per iteration  |
| Message consumer        | MassTransit and Wolverine create one per message for you |
| Hangfire job            | The Hangfire DI integration creates one per job          |
| `Program.cs` at startup | `app.Services.CreateScope()` for seeding or migrations   |

> **\[Trap\]**
>
> **Common trap.** One scope for the entire lifetime of a worker is the same bug as a captive dependency wearing a disguise: the change tracker grows forever, entities go stale, and memory climbs. Scope per unit of work, always.

###### Where scopes come from, and creating them yourself

``` text
  who creates a scope, automatically

    an HTTP request         ASP.NET Core, per request
    a SignalR hub method    per invocation
    a gRPC call             per call
    a background service    NO - it is a singleton. You create it.
    a Quartz/Hangfire job   the integration usually does. VERIFY.
    a message consumer      depends entirely on the library
    a console app           NO. You create it.

  the rule: if nothing created a scope, resolving a scoped service either
  throws, or silently resolves from the ROOT scope - which means it lives
  as long as the application. The second case is the dangerous one.
```

``` csharp
// a message consumer: ONE SCOPE PER MESSAGE, not per consumer
public class OrderConsumer(IServiceScopeFactory scopes)
{
    public async Task HandleAsync(OrderPlaced msg, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();   // async disposal
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var handler = sp.GetRequiredService<IOrderHandler>();

        await handler.HandleAsync(msg, ct);
        await db.SaveChangesAsync(ct);
    }   // scope disposed: DbContext disposed, connection returned, and the
        // change tracker does not leak into the next message
}
```

| Mistake                                    | Consequence                                           |
|--------------------------------------------|-------------------------------------------------------|
| One scope for the consumer's lifetime      | The change tracker accumulates every entity ever seen |
| Resolving from the root provider           | Scoped services become singletons, silently           |
| Not disposing the scope                    | Connections never returned; the pool exhausts         |
| Capturing a service outside its scope      | Use-after-dispose, at some later point                |
| A scope per *batch* rather than per *item* | One poison item fails the whole batch; memory grows   |

The single most useful diagnostic here: turn on `ValidateScopes` and `ValidateOnBuild` in *every* environment. The first catches a singleton depending on a scoped service; the second catches any registration that cannot be constructed at all — which otherwise surfaces as a 500 on the one endpoint that needs it, weeks later.

<a id="be-a88"></a>

#### Logger, options and other framework factories

##### ILoggerFactory

Injecting `ILogger<MyClass>` is really the factory creating a logger whose category is the full type name, then fanning every write out to all registered providers. You only touch `ILoggerFactory` directly when the category is dynamic.

``` csharp
var logger = loggerFactory.CreateLogger($"Tenant.{tenantId}.Import");
```

##### Named options

The same options type configured several ways, resolved by name. This is the idiomatic way to handle “three upstream systems, same shape of settings”.

``` csharp
builder.Services.Configure<FeedOptions>("bloomberg", cfg.GetSection("Feeds:Bloomberg"));
builder.Services.Configure<FeedOptions>("reuters",   cfg.GetSection("Feeds:Reuters"));

public class FeedReader(IOptionsMonitor<FeedOptions> monitor)
{
    public FeedOptions For(string name) => monitor.Get(name);
}
```

##### ActivatorUtilities: constructor injection plus runtime arguments

``` csharp
// mixes DI-resolved dependencies with values only known at call time
var importer = ActivatorUtilities.CreateInstance<CsvImporter>(sp, filePath, tenantId);

// cache the factory when it is on a hot path
private static readonly ObjectFactory<CsvImporter> Create =
    ActivatorUtilities.CreateFactory<CsvImporter>([typeof(string), typeof(Guid)]);
var importer2 = Create(sp, [filePath, tenantId]);
```

###### IOptions, IOptionsSnapshot, IOptionsMonitor — the one people get wrong

| Interface             | Lifetime   | Reloads?                    | Use in                                   |
|-----------------------|------------|-----------------------------|------------------------------------------|
| `IOptions<T>`         | Singleton  | **No** — read once, ever    | Singletons; config that cannot change    |
| `IOptionsSnapshot<T>` | **Scoped** | Yes, per request            | Scoped services; per-request consistency |
| `IOptionsMonitor<T>`  | Singleton  | Yes, with a change callback | Singletons that must see updates         |

``` csharp
// the bug: IOptions<T> in a singleton, expecting reload to work
public class RateLimiter(IOptions<LimitOptions> options)      // singleton
{
    private readonly int _max = options.Value.Max;            // read ONCE, ever
    // editing appsettings.json at runtime changes nothing here.
}

// the fix
public class RateLimiter(IOptionsMonitor<LimitOptions> options)
{
    public bool Allow(string key) => Count(key) < options.CurrentValue.Max;
    //                                           ^^^^^^^^^^^^ re-read each call
}

// and IOptionsSnapshot in a SINGLETON throws at startup - it is scoped.
// ValidateOnBuild catches this before a request ever arrives.

// validate configuration at STARTUP, not at first use
builder.Services.AddOptions<LimitOptions>()
    .Bind(builder.Configuration.GetSection("Limits"))
    .Validate(o => o.Max > 0, "Limits:Max must be positive")
    .ValidateDataAnnotations()
    .ValidateOnStart();          // the app REFUSES TO BOOT on bad config
```

``` csharp
// named options: several configurations of one type
builder.Services.Configure<HttpOptions>("billing",  c.GetSection("Http:Billing"));
builder.Services.Configure<HttpOptions>("shipping", c.GetSection("Http:Shipping"));

public class BillingClient(IOptionsMonitor<HttpOptions> options)
{
    private HttpOptions Config => options.Get("billing");
}

// and the logger factory's real job: the CATEGORY comes from the consumer
public class OrderService(ILogger<OrderService> log) { }
// -> category "Acme.Services.OrderService", which is what makes
//    per-namespace filtering work:
//    "Logging": { "LogLevel": { "Acme.Services": "Debug" } }
```

<a id="be-a90"></a>

#### Keyed services and choosing an implementation at runtime

Two shapes cover almost every “which implementation?” problem: keyed resolution when the key is known at the injection site, and a strategy resolver when the choice depends on data.

``` csharp
// 1. keyed services (.NET 8+) - the key is static
builder.Services.AddKeyedScoped<IExporter, CsvExporter>("csv");
builder.Services.AddKeyedScoped<IExporter, XlsxExporter>("xlsx");

public class Endpoint([FromKeyedServices("csv")] IExporter exporter) { }

// 2. strategy resolver - the key comes from the request
public interface IExporter { string Format { get; } Task<Stream> ExportAsync(Query q); }

public sealed class ExporterResolver(IEnumerable<IExporter> exporters)
{
    private readonly Dictionary<string, IExporter> _map =
        exporters.ToDictionary(e => e.Format, StringComparer.OrdinalIgnoreCase);

    public IExporter For(string format) =>
        _map.TryGetValue(format, out var e) ? e
            : throw new NotSupportedException($"No exporter for '{format}'.");
}
```

Registering every implementation of an interface and injecting `IEnumerable<IExporter>` is the pattern that makes new formats a one-file change: add the class, register it, done. No switch statement to update, no existing file to touch.

> **\[Note\]**
>
> **Open-closed, concretely.** The resolver dictionary *is* the open-closed principle in practice. You extend behaviour by adding a type, not by editing a conditional that grows a branch every quarter.

###### Worked example

A realistic strategy setup: payment providers selected per transaction, each with its own configuration, resilience policy and feature support.

``` csharp
public interface IPaymentGateway
{
    string Provider { get; }
    bool Supports(Currency currency);
    Task<PaymentResult> ChargeAsync(ChargeRequest r, CancellationToken ct);
}

// registration: each provider brings its own typed HttpClient and options
foreach (var name in new[] { "stripe", "adyen", "mollie" })
{
    builder.Services.AddOptions<GatewayOptions>(name)
        .Bind(builder.Configuration.GetSection($"Gateways:{name}"))
        .ValidateOnStart();

    builder.Services.AddHttpClient(name, (sp, c) =>
    {
        var o = sp.GetRequiredService<IOptionsMonitor<GatewayOptions>>().Get(name);
        c.BaseAddress = o.BaseAddress;
        c.Timeout = o.Timeout;
    }).AddStandardResilienceHandler();
}

builder.Services.AddKeyedScoped<IPaymentGateway, StripeGateway>("stripe");
builder.Services.AddKeyedScoped<IPaymentGateway, AdyenGateway>("adyen");
builder.Services.AddKeyedScoped<IPaymentGateway, MollieGateway>("mollie");

// selection: routing rules live in ONE place, and are testable in isolation
public sealed class GatewaySelector(IServiceProvider sp, IOptionsMonitor<RoutingOptions> routing)
{
    public IPaymentGateway For(ChargeRequest r)
    {
        var key = routing.CurrentValue.Rules
            .FirstOrDefault(rule => rule.Matches(r))?.Gateway
            ?? routing.CurrentValue.Default;

        var gateway = sp.GetRequiredKeyedService<IPaymentGateway>(key);

        if (!gateway.Supports(r.Currency))
            throw new NotSupportedException($"{key} does not support {r.Currency}.");

        return gateway;
    }
}
```

Adding a fourth provider now touches: one new class, three lines of registration, and a config section. No existing file changes, no switch statement grows, and the routing rules can be moved to configuration and changed without a deploy. Compare that to a `switch` in a service class, which every provider addition must edit and every merge must reconcile.

###### When keyed services are the wrong tool

- **The key is a business concept.** If you find yourself with keys like `"premium-customer-eu"`, that logic belongs in a domain service, not in container configuration.
- **You need all of them, not one.** Injecting `IEnumerable<T>` and filtering is clearer than resolving keys in a loop.
- **The implementations differ in interface, not just behaviour.** That is a sign the abstraction is wrong, and a key is hiding it.
- **The choice never varies at runtime.** Then it is configuration, and a single registration chosen at startup is simpler.
- **You are in a library.** Keyed registrations in a shared package collide with consumers' keys; expose a factory interface instead.

<a id="be-a91"></a>

#### Decorators, interception and the chain of responsibility

Cross-cutting behaviour — caching, retries, logging, validation — belongs around a service, not inside it. The built-in container cannot decorate out of the box, but the pattern is easy by hand and trivial with Scrutor.

``` csharp
public sealed class CachingOrderRepository(IOrderRepository inner, HybridCache cache)
    : IOrderRepository
{
    public ValueTask<Order?> GetAsync(Guid id, CancellationToken ct) =>
        cache.GetOrCreateAsync($"order:{id}",
            async token => await inner.GetAsync(id, token), cancellationToken: ct);
}

// Scrutor
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.Decorate<IOrderRepository, CachingOrderRepository>();
builder.Services.Decorate<IOrderRepository, LoggingOrderRepository>();
// resolution order: Logging -> Caching -> Sql
```

The same idea appears everywhere in .NET under different names: `DelegatingHandler` for HTTP, middleware for requests, `IPipelineBehavior` for MediatR-style dispatch, and Polly strategies for resilience. Learn it once and you recognise four framework designs.

###### Composing behaviour, and the order that results

``` csharp
// Scrutor: decoration without a custom container
services.AddScoped<IOrderService, OrderService>();
services.Decorate<IOrderService, ValidatingOrderService>();
services.Decorate<IOrderService, CachingOrderService>();
services.Decorate<IOrderService, LoggingOrderService>();

// wrapping happens in REGISTRATION order, so the LAST registered is OUTERMOST:
//
//   Logging  ->  Caching  ->  Validating  ->  OrderService
//
// usually what you want: log everything including cache hits, and do not
// validate what the cache already answered. The wrong order produces a
// cache storing invalid results, or logging that misses cache hits.

public sealed class CachingOrderService(IOrderService inner, IMemoryCache cache)
    : IOrderService
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"order:{id}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return inner.GetAsync(id, ct);
        });
}
```

``` csharp
// the pipeline form - a chain of responsibility around every request
public sealed class ValidationBehaviour<TReq, TRes>(IEnumerable<IValidator<TReq>> validators)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq request, RequestHandlerDelegate<TRes> next,
                                   CancellationToken ct)
    {
        var failures = validators.Select(v => v.Validate(request))
                                 .SelectMany(r => r.Errors)
                                 .Where(f => f is not null).ToList();
        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();                       // pass to the next link
    }
}

// here registration order IS execution order
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehaviour<,>));
// Logging -> Validation -> Transaction -> Handler
// note transaction INSIDE validation: do not open a transaction for a
// request that is about to be rejected.
```

| Approach            | Mechanism                          | Trade                                                          |
|---------------------|------------------------------------|----------------------------------------------------------------|
| Manual decorator    | A class implementing the interface | Explicit, debuggable, verbose                                  |
| Scrutor `Decorate`  | Container-level wrapping           | Concise; the chain is invisible at the call site               |
| Mediator behaviours | A pipeline around every request    | One place for cross-cutting concerns; ties you to the mediator |
| `DispatchProxy`     | Runtime proxy generation           | No compile-time safety; reflection cost                        |
| Castle DynamicProxy | IL-emitted proxies                 | Powerful; breaks under Native AOT                              |
| Source generator    | Compile-time wrapping              | Fast and AOT-safe; more to build                               |

> **\[Trap\]**
>
> **Common trap.** A decorator chain is invisible at the call site — someone reading `await _orders.GetAsync(id)` has no indication that four classes run first. Keep chains short, name them so they appear meaningfully in stack traces and spans, and register them in one obvious place. Debugging a five-deep chain assembled across three extension methods is a genuinely unpleasant afternoon.

<a id="be-a92"></a>

#### The classic creational patterns, written the modern way

| Pattern          | Modern .NET form                                                           |
|------------------|----------------------------------------------------------------------------|
| Singleton        | `AddSingleton` — never the static `Instance` property, which is untestable |
| Factory method   | A delegate registration: `AddScoped<IThing>(sp => ...)`                    |
| Abstract factory | An interface returning a family of related objects, resolved from DI       |
| Builder          | Fluent configuration; `WebApplicationBuilder` is the canonical example     |
| Prototype        | `record` with `with` expressions                                           |
| Object pool      | `ObjectPool<T>`                                                            |

``` csharp
// a static factory that keeps a type valid by construction
public readonly record struct Email
{
    public string Value { get; }
    private Email(string v) => Value = v;

    public static Result<Email> Create(string input) =>
        MailAddress.TryCreate(input, out _)
            ? Result.Ok(new Email(input.Trim().ToLowerInvariant()))
            : Result.Fail<Email>("Not a valid email address.");
}
```

> **\[Why it matters\]** A private constructor plus a named static factory means an invalid instance cannot exist anywhere in the system. Every downstream method that takes an `Email` can skip validation entirely — the type is the proof. This is the single highest-leverage use of factories in domain code.

###### What DI made obsolete, and what survives

| Pattern              | Classic form                             | Modern .NET                                                       |
|----------------------|------------------------------------------|-------------------------------------------------------------------|
| **Singleton**        | `static Instance` with a lock            | `AddSingleton` — testable, replaceable, disposed by the container |
| **Factory method**   | A virtual method returning a base type   | A registered `Func<T>`, or keyed services                         |
| **Abstract factory** | A factory interface with several creates | Usually just DI — inject the things directly                      |
| **Builder**          | A mutable builder with `Build()`         | `record` + `with`, or an options delegate                         |
| **Prototype**        | `ICloneable`                             | `record` copy semantics                                           |
| **Object pool**      | Hand-rolled                              | `ObjectPool<T>`, `ArrayPool<T>`                                   |

``` csharp
// the static singleton, and why it is worth removing
public sealed class ConfigManager
{
    private static readonly Lazy<ConfigManager> _i = new(() => new());
    public static ConfigManager Instance => _i.Value;
    private ConfigManager() { }
}
// problems: cannot be substituted in a test; a hidden dependency, since
// nothing in a constructor signature reveals it; lifetime is the PROCESS,
// not the application; static state persists across tests, so test order
// starts to matter.

// DI version - identical guarantees, none of the problems
services.AddSingleton<IConfigManager, ConfigManager>();
// one instance, created lazily on first resolution, disposed at shutdown,
// substitutable in tests, and VISIBLE in every consumer's constructor.
```

``` csharp
// builder, the modern way: records give copy-and-modify for free
public record OrderQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public OrderStatus? Status { get; init; }
    public int Take { get; init; } = 50;
}

var q = new OrderQuery { From = today.AddDays(-7) } with { Status = OrderStatus.Open };
// immutable, no builder class, no Build(), no half-constructed state.

// and the options-delegate builder the framework uses everywhere
services.AddHttpClient("billing", c =>
{
    c.BaseAddress = new Uri(cfg["Billing:Url"]!);
    c.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler();
// this Add*(name, configure) shape is worth copying for your own extension
// points: discoverable, composable and self-documenting.
```

The pattern worth noticing across the table: **most creational patterns were workarounds for the absence of a container and of immutable types.** With DI handling lifetime and construction, and records handling copy-with-changes, the survivors are the ones solving a problem neither addresses — pooling, and selecting an implementation from a value not known at registration time.

### DI, scopes and DbContext concurrency

<p class="src">From <em>Backend Engineering</em> · Practical Answers · DI, scopes and DbContext concurrency</p>

<p class="pd">The questions that come up the moment an application does more than one thing at a time: parallel work with a scoped DbContext, factories, custom scopes, and scoped services inside a singleton.</p>

<a id="be-q10"></a>

#### IDbContextFactory and registering both

``` csharp
// register the factory; AddDbContextFactory can coexist with AddDbContext
builder.Services.AddDbContextFactory<AppDb>(o => o.UseNpgsql(cs));

// .NET 8+: get BOTH a scoped DbContext (normal request work) and a factory
// (parallel or long-lived work) from one registration
builder.Services.AddDbContextFactory<AppDb>(o => o.UseNpgsql(cs),
    lifetime: ServiceLifetime.Scoped);
builder.Services.AddScoped<AppDb>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext());
```

| Use the scoped DbContext when              | Use IDbContextFactory when                                                |
|--------------------------------------------|---------------------------------------------------------------------------|
| Handling a normal request end to end       | Running several operations in parallel                                    |
| You want one unit of work per request      | Inside a singleton or a BackgroundService                                 |
| Change tracking across a request is useful | Blazor Server (a circuit outlives a request)                              |
|                                            | Long-running work where a per-request context would be held open too long |

> **\[Why it matters\]** The factory is not an optimisation; it is the correct lifetime tool. A context is a unit of work and a change tracker — the longer it lives and the more it tracks, the slower and more surprising it gets. Short, owned, disposed.

<a id="be-q11"></a>

#### Scoped services inside a singleton or BackgroundService

`BackgroundService` is a singleton, so it cannot take a scoped dependency in its constructor — and if you force it to, you have created a captive dependency: one `DbContext` alive for the life of the process, accumulating tracked entities and never seeing fresh data. The answer is to create a scope per unit of work.

``` csharp
// ✗ captive dependency — compiles, then rots
public class SyncWorker(AppDb db) : BackgroundService { … }
//            ↑ singleton holding a scoped context forever

// ✓ a scope per iteration — the standard pattern
public class SyncWorker(IServiceScopeFactory scopes, ILogger<SyncWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try
            {
                // one scope per tick: fresh DbContext, fresh scoped services,
                // everything disposed at the closing brace
                await using var scope = scopes.CreateAsyncScope();
                var db     = scope.ServiceProvider.GetRequiredService<AppDb>();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                var due = await db.Reminders
                    .Where(r => r.DueAt <= DateTime.UtcNow && !r.Sent)
                    .Take(100)
                    .ToListAsync(stopping);

                foreach (var r in due)
                {
                    await sender.SendAsync(r, stopping);
                    r.Sent = true;
                }
                await db.SaveChangesAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;                                  // shutting down: normal
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Sync iteration failed");   // never let the loop die
            }
        }
    }
}
```

The same shape applies to a queue consumer: create the scope when a message arrives, dispose it when the message is handled. That scope is the message’s unit of work, exactly as a request scope is the request’s.

``` csharp
// consumer: one scope per message
public async Task HandleAsync(OrderPlaced msg, CancellationToken ct)
{
    await using var scope = _scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    // …handle…
    await db.SaveChangesAsync(ct);
}

// bounded parallelism, each with its own scope
await Parallel.ForEachAsync(messages,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (msg, token) =>
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IHandler>();
        await handler.HandleAsync(msg, token);
    });
```

> **\[Trap\]**
>
> **Common trap.** A scope created inside another scope is independent — it does not extend or nest into the outer one, and disposing it does not touch the parent. That is exactly what you want. Turn on `ValidateScopes` (the default in Development) so the container throws at startup when a singleton captures a scoped service, rather than at 3am when the context is stale.

<a id="be-q12"></a>

#### The factory patterns the framework gives you

| Type                         | What it makes        | Typical use                                                  |
|------------------------------|----------------------|--------------------------------------------------------------|
| `IServiceScopeFactory`       | A DI scope           | Background services, consumers, any non-request unit of work |
| `IDbContextFactory<T>`       | A DbContext          | Parallel queries, singletons, Blazor                         |
| `IHttpClientFactory`         | An HttpClient        | All outbound HTTP — handles socket reuse and DNS refresh     |
| `ILoggerFactory`             | An ILogger           | When the category is decided at runtime                      |
| `IServiceProviderFactory<T>` | The container itself | Swapping in Autofac/Lamar at host build time                 |

``` csharp
// resolving one of several implementations at runtime — keyed services (.NET 8)
builder.Services.AddKeyedScoped<IPaymentGateway, StripeGateway>("stripe");
builder.Services.AddKeyedScoped<IPaymentGateway, PaddleGateway>("paddle");

public class Checkout([FromKeyedServices("stripe")] IPaymentGateway gateway) { }

// or a factory delegate when the key is only known at call time
builder.Services.AddScoped<Func<string, IPaymentGateway>>(sp => key =>
    sp.GetRequiredKeyedService<IPaymentGateway>(key));

// replacing the whole container (Autofac) — this is IServiceProviderFactory
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(b =>
    b.RegisterModule(new PaymentsModule()));
```

> **\[Trap\]**
>
> **Common trap.** Injecting `IServiceProvider` and resolving from it inside a method is the **service locator** anti-pattern: dependencies become invisible, tests need a container, and lifetime bugs hide. The factories above are the legitimate exceptions — they express “I need a *new* one, on demand”, which constructor injection genuinely cannot say.
