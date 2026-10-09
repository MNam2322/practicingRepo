---
id: "4-7"
number: "4.7"
title: "Design patterns"
part: 4
part_title: "Designing programs"
words: 22051
minutes: 96
---

# 4.7 Design patterns

> **Quick look.** Design patterns as a shared vocabulary of trade-offs, and when not to use them.
>
> - A pattern is a named solution plus its consequences; read the costs before using one.
> - Creational: Builder and Object Pool still earn their place; DI absorbed most of the rest; Singleton is mostly a DI lifetime now.
> - Structural: Adapter changes shape, Decorator adds behaviour, Proxy controls access, Facade simplifies a subsystem, Composite treats one and many alike.
> - Behavioural: Strategy (almost free with DI), State, Command, Chain of Responsibility, Observer vs Mediator.
> - Repository and Unit of Work: DbContext already is both; wrap it only if you need the boundary.
> - Work backwards from the symptom to the pattern, and reach patterns by refactoring, not up-front design (avoid pattern fever).


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Choosing a pricing strategy per market

Vietnam, the EU and the US each calculate taxes differently, and a growing switch statement is touched by every change.

*Strategy with keyed services*

```csharp
public interface ITaxStrategy { Money Apply(Money net); }
builder.Services.AddKeyedSingleton<ITaxStrategy, VietnamVat>("VN");
builder.Services.AddKeyedSingleton<ITaxStrategy, EuVat>("EU");
builder.Services.AddKeyedSingleton<ITaxStrategy, UsSalesTax>("US");

public sealed class Checkout(IServiceProvider sp)
{
    public Money Total(Money net, string market) =>
        sp.GetRequiredKeyedService<ITaxStrategy>(market).Apply(net);
}
```

> **Lesson:** Strategy replaces a growing switch with one class per variation; adding a market adds code instead of editing it.

#### Example 2: A booking lifecycle as a state machine

Status checks like `if (status == Paid || status == Ticketed)` are scattered everywhere, and invalid transitions slip through.

*Explicit transitions*

```csharp
public enum BookingStatus { Pending, Paid, Ticketed, Cancelled }

static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
{
    [BookingStatus.Pending]  = [BookingStatus.Paid, BookingStatus.Cancelled],
    [BookingStatus.Paid]     = [BookingStatus.Ticketed, BookingStatus.Cancelled],
    [BookingStatus.Ticketed] = [BookingStatus.Cancelled],
    [BookingStatus.Cancelled] = [],
};

public void MoveTo(BookingStatus next)
{
    if (!Allowed[Status].Contains(next)) throw new DomainException($"{Status} -> {next} is not allowed");
    Status = next;
}
```

> **Lesson:** Most domains are state machines wearing a status column. A transition table is readable, testable and hard to bypass.

### Foundations

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Foundations</p>

<p class="pd">What a pattern actually is, why the vocabulary matters more than the code, and the two forces that every pattern in this guide is balancing.</p>

<a id="be-h719"></a>

#### What a design pattern actually is

A design pattern is **a named solution to a recurring problem, together with the trade-offs it makes**. That last clause is the part usually dropped, and dropping it is why patterns get misapplied.

``` text
  a pattern is NOT

    a library                you cannot install Strategy
    a language feature       though a language feature can make one trivial
    code to copy             the structure adapts to your situation
    a goal                   "we used seven patterns" is not an achievement
    always good              every pattern makes something WORSE in exchange
                             for making something else better

  a pattern IS

    a NAME          so a team can discuss a design in three words
    a PROBLEM       the situation that makes it appropriate
    the FORCES      the competing pressures it balances
    a STRUCTURE     roles and relationships, not literal code
    CONSEQUENCES    what you gain, and what you give up
```

The historical note matters for reading the catalogue correctly. The original 23 patterns were *observed, not invented* — four authors looked at large C++ and Smalltalk systems in the early 1990s and documented structures that kept recurring. That has two consequences: some exist to work around limitations those languages had and modern C# does not, and the catalogue is a snapshot of one era rather than a complete list.

| Section of a pattern description | Question it answers                       |
|----------------------------------|-------------------------------------------|
| **Intent**                       | What is this for, in one sentence?        |
| **Problem**                      | What situation makes it appropriate?      |
| **Forces**                       | What competing pressures are in tension?  |
| **Structure**                    | Which roles exist and how do they relate? |
| **Consequences**                 | **What does it cost?**                    |
| **Known uses**                   | Where does the framework already do this? |

> **\[Note\]**
>
> **Read the consequences first.** Most people learn patterns from the structure diagram and skip the consequences, which is exactly backwards. The structure is easy to reproduce and tells you nothing about whether you should. Every pattern in this guide is presented problem-first and cost-last for that reason — if you cannot state what a pattern costs you, you do not yet know it well enough to apply it.

###### One real requirement, three defensible designs

The requirement, from a wholesale distributor: *“When an order ships, notify the customer. Some customers want email, some want an EDI message to their ERP, some want both, and the enterprise customers want a webhook.”* Here is that requirement implemented three ways, all of which work.

``` csharp
// DESIGN 1 - direct. Two channels, one team, no extension expected.
public sealed class ShipmentNotifier(
    IEmailSender email, IEdiGateway edi, ILogger<ShipmentNotifier> log)
{
    public async Task NotifyAsync(Shipment s, CustomerPrefs prefs, CancellationToken ct)
    {
        if (prefs.Email)
            await email.SendAsync(prefs.EmailAddress!, ShipmentTemplate.For(s), ct);

        if (prefs.Edi)
            await edi.SendAsync(prefs.EdiPartnerId!, Edi856.From(s), ct);
    }
}
// 12 lines. Everything visible. A new developer understands it in 30 seconds.
// CORRECT until channels are added by people who cannot edit this file.
```

``` csharp
// DESIGN 2 - Strategy. Four channels, added regularly, each with its own
// dependencies and its own failure behaviour.
public interface INotificationChannel
{
    ChannelKind Kind { get; }
    Task SendAsync(Shipment shipment, CustomerPrefs prefs, CancellationToken ct);
}

public sealed class EdiChannel(IEdiGateway edi, ILogger<EdiChannel> log)
    : INotificationChannel
{
    public ChannelKind Kind => ChannelKind.Edi;

    public async Task SendAsync(Shipment s, CustomerPrefs prefs, CancellationToken ct)
    {
        if (prefs.EdiPartnerId is null)
            throw new NotificationConfigurationException(Kind, "No EDI partner id");

        await edi.SendAsync(prefs.EdiPartnerId, Edi856.From(s), ct);
    }
}

public sealed class ShipmentNotifier(
    IEnumerable<INotificationChannel> channels, ILogger<ShipmentNotifier> log)
{
    public async Task NotifyAsync(Shipment s, CustomerPrefs prefs, CancellationToken ct)
    {
        foreach (var channel in channels.Where(c => prefs.Wants(c.Kind)))
        {
            try
            {
                await channel.SendAsync(s, prefs, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // ONE channel failing must not stop the others. This decision -
                // isolation - is the reason Strategy beat a switch here, more
                // than extensibility did.
                log.LogError(ex, "Channel {Kind} failed for shipment {Id}",
                             channel.Kind, s.Id);
            }
        }
    }
}
```

``` csharp
// DESIGN 3 - events. Channels are owned by OTHER TEAMS, in other services.
public sealed class ShipmentService(IOutbox outbox, AppDbContext db)
{
    public async Task ShipAsync(ShipmentId id, TrackingNumber tracking,
                                CancellationToken ct)
    {
        var shipment = await db.Shipments.FindAsync([id], ct)
                       ?? throw new NotFoundException(nameof(Shipment), id);

        shipment.MarkShipped(tracking);

        // the notifier does not exist here. Whoever cares, subscribes.
        outbox.Add(new ShipmentDispatched(shipment.Id, shipment.OrderId,
                                          tracking.Value, _clock.GetUtcNow()));

        await db.SaveChangesAsync(ct);   // state + event, ONE transaction
    }
}
// this service now knows NOTHING about notification. That is the point,
// and also the cost: "what happens when a shipment ships?" is no longer
// answerable from this file, or from this repository.
```

|                    | Design 1               | Design 2              | Design 3                 |
|--------------------|------------------------|-----------------------|--------------------------|
| Add a channel      | Edit this file         | Add a file            | Another team's work      |
| See the whole flow | **Here**               | Five files            | Across services          |
| One channel fails  | Stops the rest         | **Isolated**          | **Isolated**             |
| Test it            | Two mocks              | Per channel           | Integration test         |
| Right when         | 1–2 channels, one team | 3+ channels, one team | Channels owned elsewhere |

None of these is the “correct” one, and moving from 1 to 2 to 3 as the situation changes is normal and cheap. Starting at 3 means paying for distributed debugging and eventual consistency before you know whether anyone else will ever own a channel.

<a id="be-h720"></a>

#### The vocabulary is the value

The most useful thing patterns give you is not the code. It is the ability to compress a paragraph of design discussion into one word that both people understand identically.

``` text
  WITHOUT the vocabulary

    "So we'd have an interface, and then several classes that all implement
     it in different ways, and at runtime we pick which one to use based on
     the customer's plan, and the calling code shouldn't know which one it
     got, and we should be able to add a new one without changing the
     caller..."

  WITH it

    "Strategy, selected by plan."

  four words. Same design. And the second version can be DISAGREED WITH
  precisely - "that's not Strategy, the variants aren't interchangeable,
  that's a State machine" is a productive sentence.
```

| Compresses to           | Which means                                                    |
|-------------------------|----------------------------------------------------------------|
| “Wrap it in an adapter” | Different interface, same behaviour, translate at the boundary |
| “That's a decorator”    | Same interface, added behaviour, composable                    |
| “Use an outbox”         | Write the message in the same transaction as the data          |
| “It's a god object”     | One class knows and does too much; split it                    |
| “Anti-corruption layer” | Their model stops here; translate to ours                      |
| “Strangler fig”         | Route by route, both running, no big bang                      |

This is also why **naming a pattern in code is worth doing**. A class called `OrderProcessor` tells the next reader nothing. `CachingOrderRepository` tells them it is a decorator over another repository and that they should look for the inner one. The name is documentation that cannot go stale.

> **\[Note\]**
>
> **The failure mode of the vocabulary.** Using a pattern name for something that is not that pattern is worse than not naming it, because the reader now has a confident and wrong mental model. If a class is called `OrderFactory` and it also validates, persists and publishes, the name is actively lying — and the reader will spend twenty minutes looking for the factory.

<a id="be-h721"></a>

#### Forces: why every pattern is a trade

A **force** is a pressure acting on a design. Patterns exist because forces conflict — if they did not, the obvious solution would always be right and there would be nothing to document.

``` text
  the forces that recur throughout this guide

  FLEXIBILITY  vs  SIMPLICITY
      every extension point is a concept the next reader must learn

  DECOUPLING   vs  DIRECTNESS
      an interface between A and B means you cannot see B from A
      - which is the point, and also the cost when debugging

  REUSE        vs  FITNESS
      code shared by three callers serves none of them perfectly

  EXPLICITNESS vs  CONCISENESS
      a decorator chain is composable and invisible at the call site

  PERFORMANCE  vs  CLARITY
      indirection costs a lookup; abstraction costs an allocation

  CHANGEABILITY vs UNDERSTANDABILITY
      the most changeable design is rarely the most obvious one

  a pattern is a documented POSITION on one or more of these. Applying a
  pattern where the force does not exist buys you only the cost.
```

``` csharp
// the same problem, three positions on the flexibility/simplicity force

// 1. no pattern. Simplest. Changing it means editing this method.
decimal Price(Order o) => o.Total * 0.9m;

// 2. a switch. Slightly more flexible; all variants visible in one place.
decimal Price(Order o) => o.Plan switch
{
    Plan.Standard => o.Total,
    Plan.Volume   => o.Total * 0.9m,
    Plan.Contract => o.Total * 0.75m,
};

// 3. Strategy. Most flexible; a new variant needs no edit to this code.
//    and now the logic is in three files, and finding "what actually
//    happens for a Volume customer" is a navigation exercise.
decimal Price(Order o) => _strategies.First(s => s.AppliesTo(o)).Calculate(o);

// NONE of these is correct in the abstract. Option 2 is right until
// variants are added by people who cannot edit this file - at which
// point option 3 becomes right. The FORCE decides, not the pattern.
```

> **\[Why it matters\]** This is the single most useful idea in the guide. When someone proposes a pattern, the productive question is not “is that the right pattern?” but **“which force are we responding to, and is it actually present?”** If nobody can name the pressure, the pattern is decoration.

###### A design evolving over two years, and where each pattern earned its place

``` text
  the same feature, at four points in its life. Note that at every stage
  the CURRENT code is correct - and the NEXT stage is only correct once
  something has changed in the world.

  MONTH 1   one customer type, one rule
              decimal Price(Order o) => o.Total;
            correct. Any abstraction here is guessing.

  MONTH 4   a second rule appears
              if (customer.IsVolume) return o.Total * 0.9m;
              return o.Total;
            still correct. TWO cases do not justify an abstraction -
            you cannot yet tell which axis varies.

  MONTH 9   five rules, and finance keeps asking for more
              switch on plan, five arms, each 3-4 lines
            the FORCE has now appeared: the file changes weekly, and
            each change risks the other four arms.
            -> extract to Strategy. Now a new rule is a NEW FILE.

  MONTH 20  rules must be configurable per tenant, without deploying
            -> the strategies are now selected from configuration, and
               a Specification describes when each applies.
            the force changed AGAIN: from "who edits" to "who deploys".

  the lesson: the pattern did not become correct. THE FORCE APPEARED.
  applying month-20 structure in month 1 would have been wrong, and
  would have been wrong along the wrong axis.
```

| At each stage, ask                                            | If the answer is no                              |
|---------------------------------------------------------------|--------------------------------------------------|
| Has this code changed more than twice for the same reason?    | Leave it                                         |
| Do the variants have genuinely different logic?               | A switch is clearer                              |
| Are variants added by people who cannot edit this file?       | You do not need the extension point yet          |
| Would the abstraction have a name a domain expert recognises? | You are abstracting a mechanism, not a concept   |
| Can I state what this costs the next reader?                  | You do not understand it well enough to apply it |

The trap in this progression is **abstracting along the wrong axis**. At month 4 you might reasonably guess that pricing varies by *customer type*. If it turns out to vary by *product category*, the interface you built is now in the way — and removing it is harder than adding it was, because five callers already depend on its shape. Waiting until month 9 costs some duplication; guessing costs a refactor.

<a id="be-h723"></a>

#### When not to use a pattern

Patterns have a failure mode with a name: **pattern fever** — applying structure because it is known, rather than because a problem demands it. It produces code that is impressive to write and miserable to read.

``` text
  the honest sequence, and where patterns enter

  1  WRITE THE OBVIOUS THING
       direct, concrete, no indirection. It works.

  2  A SECOND CASE APPEARS
       add an if. Still fine. Two cases do not justify an abstraction.

  3  A THIRD CASE APPEARS, and the shape is now clear
       -> NOW refactor to a pattern. You have seen the variation,
          so you know what varies.

  the mistake is starting at 3, before you know what varies. An
  abstraction designed for imagined variation is almost always wrong
  about which axis actually varies - and a wrong abstraction is harder
  to remove than no abstraction.

  "duplication is far cheaper than the wrong abstraction" - Sandi Metz
```

| Warning sign                                          | What it usually means                                            |
|-------------------------------------------------------|------------------------------------------------------------------|
| An interface with exactly one implementation, forever | The abstraction is for testing only — sometimes valid, often not |
| A factory that always returns the same type           | Ceremony. Call the constructor                                   |
| Five layers between the request and the query         | Each layer must justify itself                                   |
| A name ending in `Manager`, `Helper`, `Util`          | Low cohesion — nobody could name what it does                    |
| A pattern applied “so we can swap it later”           | YAGNI. You will not, and the seam will be in the wrong place     |
| The team debating pattern names, not behaviour        | The vocabulary has become the goal                               |

**A pattern is earning its place when**

- You can name the force it addresses
- The variation it abstracts has actually occurred
- It removes duplication that already exists
- It makes an invalid state impossible
- A newcomer's question is answered by its name

**It is not when**

- It was chosen before the problem was understood
- The variation is hypothetical
- It adds a file without removing a decision
- You cannot explain what it costs
- The reason given is “best practice”

###### Removing an abstraction that was added too early

``` text
  the situation, and it is extremely common

    someone added IOrderProcessor "so we can swap implementations".
    two years later there is still ONE implementation, and the
    interface has 14 methods because every new need was added to it.

    it is now WORSE than no abstraction:
      - "go to definition" lands on the interface, never the code
      - the interface is the class's public surface, renamed
      - 14 methods means every consumer depends on all 14
      - a real second implementation would need a DIFFERENT shape

  REMOVING IT IS SAFE AND MECHANICAL, and people do not do it because
  deleting feels riskier than adding. It is not.
```

``` csharp
// step 1: inline the interface at the CONSUMER, one at a time
public class OrderController(IOrderProcessor processor)   // before
public class OrderController(OrderProcessor processor)     // after
// compile. Tests pass. Ship it. Repeat per consumer.

// step 2: when no consumer references the interface, delete it.
//   the compiler tells you when you are done - there is no judgement
//   involved and no way to get it wrong silently.

// step 3: if a test mocked it, ask WHY.
//   fast and deterministic?  -> use the real thing. A test that
//                               constructs the real OrderProcessor
//                               tests more than one asserting on a mock.
//   slow or non-deterministic? -> the interface belongs at THAT
//                               boundary (the database, the clock,
//                               the HTTP call), not around this class.
```

| Abstraction                       | Safe to remove when                         | Signal to keep                       |
|-----------------------------------|---------------------------------------------|--------------------------------------|
| Interface with one implementation | Nothing mocks it, or the real thing is fast | It crosses a layer boundary          |
| A factory returning one type      | Always — call the constructor               | Construction needs runtime arguments |
| A wrapper that only forwards      | Always                                      | It adapts a shape you do not control |
| A generic repository              | Reads are projected directly                | Writes go through aggregates         |
| A base class of protected helpers | Convert to injected collaborators           | Callers hold the base type           |
| A mediator over three handlers    | Cross-cutting concerns are absent           | A pipeline is genuinely needed       |

> **\[Note\]**
>
> **Deleting is a design activity.** Codebases accumulate abstractions because adding one is a visible contribution and removing one is not. The result is layers nobody can justify, each individually defensible. Once a quarter, pick the interface with one implementation that annoys you most and delete it — the compiler makes it a mechanical, reversible exercise, and it is one of the few refactors with no risk of behaviour change at all.

<a id="be-h724"></a>

#### How the patterns relate: a map

Reading the catalogue as a flat list of twenty-three items is why people forget it. Read as a small number of families answering a small number of questions, it becomes navigable.

``` text
  THE THREE GoF FAMILIES, by the question they answer

  CREATIONAL   "how does this object get made?"
                 -> hide construction, so the caller need not know the
                    concrete type or the assembly steps
                 Factory Method, Abstract Factory, Builder,
                 Prototype, Singleton

  STRUCTURAL   "how do these objects fit together?"
                 -> compose objects into larger structures without
                    the pieces knowing about the composition
                 Adapter, Bridge, Composite, Decorator,
                 Facade, Flyweight, Proxy

  BEHAVIOURAL  "how do these objects collaborate?"
                 -> assign responsibility and route communication
                 Chain of Responsibility, Command, Iterator, Mediator,
                 Memento, Observer, State, Strategy, Template Method,
                 Visitor  (+ Interpreter, rarely used)

  AND THE FAMILIES THAT CAME LATER

  DOMAIN         entity, value object, aggregate, repository,
                 specification, domain event
  ARCHITECTURAL  layers, hexagonal, CQRS, event sourcing, vertical slice
  DISTRIBUTED    outbox, saga, circuit breaker, ACL, strangler fig
  CONCURRENCY    producer-consumer, pipeline, worker pool, actor
```

``` text
  THE CONFUSABLE PAIRS - and these are what interviews and code reviews
  actually turn on

    Adapter    vs  Facade        different interface  vs  simpler interface
    Decorator  vs  Proxy         adds behaviour       vs  controls access
    Strategy   vs  State         caller picks         vs  the object transitions
    Factory    vs  Builder       which type           vs  how to assemble it
    Mediator   vs  Observer      centralised routing  vs  broadcast
    Template   vs  Strategy      inheritance          vs  composition
    Composite  vs  Decorator     tree of many         vs  chain of one

  each of these pairs has the SAME STRUCTURE and a DIFFERENT INTENT.
  which is the clearest evidence that intent, not structure, is what a
  pattern actually is.
```

> **\[Note\]**
>
> **How to use this guide.** Do not read it front to back. Read this part, read the principles, then use the rest as a reference: when you meet a design problem, find the family that matches the question you are asking, and read the two or three candidates. The confusable pairs above are worth returning to — distinguishing them is most of what “knowing patterns” means in practice.

###### The confusable pairs, resolved by a single question each

``` text
  seven pairs share a structure and differ in intent. One question
  resolves each, and these are what code reviews actually turn on.

  ADAPTER vs FACADE
    Q: does the client's expected interface ALREADY EXIST?
       yes -> Adapter   (you must match a shape someone else defined)
       no  -> Facade    (you are inventing a simpler shape)

  DECORATOR vs PROXY
    Q: can the wrapper decide NOT to call the inner object?
       no  -> Decorator (always delegates; adds behaviour around it)
       yes -> Proxy     (controls access; may refuse, defer, or fake)

  STRATEGY vs STATE
    Q: can the object change its own implementation as a RESULT of an
       operation?
       no  -> Strategy  (the caller chooses; it stays chosen)
       yes -> State     (the object transitions itself)

  MEDIATOR vs OBSERVER
    Q: does one component know the whole flow?
       yes -> Mediator  (central routing, ordered, testable in one place)
       no  -> Observer  (broadcast; subscribers are independent)

  TEMPLATE METHOD vs STRATEGY
    Q: is the variation supplied by a SUBCLASS or by a COLLABORATOR?
       subclass     -> Template Method
       collaborator -> Strategy

  COMPOSITE vs DECORATOR
    Q: how many children does the wrapper hold?
       many, as a tree -> Composite
       exactly one     -> Decorator

  FACTORY METHOD vs ABSTRACT FACTORY
    Q: would mixing products from two implementations be INVALID?
       yes -> Abstract Factory  (the family must be chosen together)
       no  -> Factory Method    (or just DI)
```

| Named wrongly                          | Reader assumes                 | Actually                                    |
|----------------------------------------|--------------------------------|---------------------------------------------|
| A proxy called `*Decorator`            | It always delegates            | It can refuse — and the caller cannot tell  |
| A facade called `*Adapter`             | It translates one thing        | It orchestrates five                        |
| A state machine called `*Strategy`     | The choice is stable           | It changes underneath them                  |
| An orchestrator called `*EventHandler` | It is one independent reaction | It is a sequence with ordering requirements |
| A god object called `*Facade`          | It is a thin convenience       | It is the only way in, and it grew          |

The naming point is not pedantry. A pattern name in a class name is a *promise about behaviour*, and the next reader will act on it without verifying. Calling an authorisation proxy a decorator promises “this always delegates” — so the reader debugging a missing result will not think to look there.

### Creational Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Creational Patterns</p>

<p class="pd">Patterns about object creation. Hiding construction so the caller need not know the concrete type or the assembly steps — and what dependency injection made redundant.</p>

<a id="be-h733"></a>

#### What creational patterns are for

All five answer one question: **how does the caller get an object without knowing how it is made?** The reason that question is worth answering is that `new` is the most binding statement in object-oriented code — it names a concrete type at compile time and cannot be substituted.

``` text
  the problem, stated plainly

    public class OrderService
    {
        private readonly SqlOrderRepository _repo = new();   // <- the problem
    }

    this line permanently decides:
      WHICH implementation is used
      WHEN it is created
      WHAT it is constructed with
      THAT it cannot be replaced in a test
      THAT OrderService cannot compile without the database assembly

  every creational pattern moves one or more of those decisions
  somewhere the caller does not have to know about.

  which decision is moved is what distinguishes them:

    Factory Method    WHICH TYPE - decided by a subclass
    Abstract Factory  WHICH FAMILY of types - decided by configuration
    Builder           HOW TO ASSEMBLE - many steps, many options
    Prototype         COPY an existing one instead of constructing
    Singleton         HOW MANY exist
    Object Pool       REUSE rather than construct
```

> **\[Note\]**
>
> **Read this part knowing where it ends.** Dependency injection subsumes most of what these patterns do, and the final section of this part says so directly. They remain worth knowing because you will meet them in existing code, because the framework uses several of them, and because the two that survive — Builder and Object Pool — solve problems DI does not touch.

<a id="be-h734"></a>

#### Factory Method

**Intent:** define an interface for creating an object, but let subclasses decide which class to instantiate. The creation is deferred to a subclass rather than fixed in the base.

``` csharp
// the base class knows WHEN to create, not WHAT to create
public abstract class DocumentProcessor
{
    public ProcessResult Process(Stream input)
    {
        var parser = CreateParser();          // <- the factory method
        var doc = parser.Parse(input);
        return Validate(doc);                  // shared logic lives here
    }

    protected abstract IParser CreateParser(); // subclasses decide
}

public sealed class CsvProcessor : DocumentProcessor
{
    protected override IParser CreateParser() => new CsvParser();
}
public sealed class XmlProcessor : DocumentProcessor
{
    protected override IParser CreateParser() => new XmlParser();
}
```

| Force                                                     | Position taken                                              |
|-----------------------------------------------------------|-------------------------------------------------------------|
| The base class must not know its subtypes' concrete types | Creation is abstracted into a method                        |
| The algorithm is shared; one step varies                  | Template method, with the varying step being construction   |
| Cost                                                      | Requires inheritance, so it inherits inheritance's problems |

**Where you already meet it:** `DbProviderFactory.CreateConnection()`, `WebRequest.Create()`, `ILoggerFactory.CreateLogger()`, and `HttpMessageHandler` creation inside `IHttpClientFactory`.

> **\[Note\]**
>
> **The modern equivalent.** In C# this is usually better expressed as an injected `Func<IParser>` or a keyed service, because it needs no inheritance. Factory Method survives where a framework already gives you a base class to override — which is exactly where you will encounter it.

###### The three modern replacements, and when the classic form still wins

``` csharp
// CLASSIC - requires inheritance
public abstract class Processor
{
    protected abstract IParser CreateParser();
    public Result Run(Stream s) => Validate(CreateParser().Parse(s));
}

// REPLACEMENT 1: an injected factory delegate. No inheritance at all.
public sealed class Processor(Func<IParser> createParser)
{
    public Result Run(Stream s) => Validate(createParser().Parse(s));
}
services.AddScoped<Func<IParser>>(sp => () => sp.GetRequiredService<CsvParser>());

// REPLACEMENT 2: keyed services, when the caller knows which
services.AddKeyedScoped<IParser, CsvParser>("csv");
public sealed class Processor(IServiceProvider sp)
{
    public Result Run(string format, Stream s) =>
        Validate(sp.GetRequiredKeyedService<IParser>(format).Parse(s));
}

// REPLACEMENT 3: pass the collaborator in. Often the simplest thing,
// and frequently overlooked because the pattern name suggests otherwise.
public sealed class Processor
{
    public Result Run(IParser parser, Stream s) => Validate(parser.Parse(s));
}
```

``` text
  WHEN THE FACTORY IS STILL NECESSARY - the cases DI does not cover

  1  CONSTRUCTION NEEDS RUNTIME ARGUMENTS
       the container knows about services, not about the customer id
       you happen to have in this method.

         ActivatorUtilities.CreateInstance<Handler>(sp, orderId, tenantId)
         // resolves the SERVICE dependencies from the container and
         // supplies the rest positionally.

  2  A SHORT-LIVED OBJECT INSIDE A LONG-LIVED ONE
       a singleton needs a scoped DbContext -> IServiceScopeFactory.
       this is the lifetime mismatch, and it is the single most common
       reason a factory exists in .NET.

  3  CREATION IS EXPENSIVE AND CONDITIONAL
       injecting the object constructs it always. Injecting Func<T>
       constructs it only if the code path is taken.

  4  YOU NEED MANY INSTANCES
       Func<T> or IDbContextFactory<T> - one per unit of work, in a loop.

  if none of the four apply, you do not need a factory. You need a
  constructor parameter.
```

| Mechanism               | Compile-time safe     | Runtime cost          | Fits                                |
|-------------------------|-----------------------|-----------------------|-------------------------------------|
| Constructor parameter   | **Yes**               | None                  | The default                         |
| `Func<T>`               | **Yes**               | A closure             | Lazy or repeated creation           |
| Keyed service           | No — a bad key throws | Dictionary lookup     | Runtime selection by name           |
| `ActivatorUtilities`    | No                    | Reflection, cacheable | Mixing DI with runtime arguments    |
| `IServiceScopeFactory`  | Yes                   | Scope creation        | Scoped inside singleton             |
| Abstract factory method | Yes                   | None                  | A framework gave you the base class |

<a id="be-h735"></a>

#### Abstract Factory

**Intent:** create *families* of related objects without specifying their concrete classes. The distinguishing feature versus Factory Method is the word *family* — several products that must be consistent with one another.

``` csharp
// the force: you must not mix a SQL Server connection with a Postgres
// command builder. The family must be chosen once, together.
public interface IDataAccessFactory
{
    IDbConnection    CreateConnection();
    ISqlDialect      CreateDialect();
    IBulkLoader      CreateBulkLoader();
}

public sealed class PostgresFactory : IDataAccessFactory
{
    public IDbConnection CreateConnection() => new NpgsqlConnection(_cs);
    public ISqlDialect   CreateDialect()    => new PostgresDialect();
    public IBulkLoader   CreateBulkLoader() => new CopyBulkLoader();
}
// choosing PostgresFactory guarantees all three are consistent.
// that GUARANTEE is the whole value - not the indirection.
```

``` text
  Factory Method  vs  Abstract Factory - the actual difference

    Factory Method    ONE product, chosen by a SUBCLASS
                      "which parser do I create?"
                      uses inheritance

    Abstract Factory  A FAMILY of products, chosen by CONFIGURATION
                      "which set of database components do I create?"
                      uses composition

  the test: does the design break if a caller mixes products from two
  different implementations? If yes, you need Abstract Factory, and the
  factory interface is what prevents the mixing.
  If products are independent, you do not - use separate registrations.
```

> **\[Trap\]**
>
> **Common trap.** Abstract Factory is heavily over-applied. If the products can be varied independently — and they usually can — you do not have a family, and three separate DI registrations are simpler and more flexible than one factory interface. Reserve it for the case where mixing implementations is genuinely invalid, which is rarer than it appears.

###### A case where the family is real: multi-tenant document generation

Abstract Factory is over-applied, so here is a case where it genuinely earns its place — the products *must* be consistent with one another, and mixing them produces documents that are subtly, expensively wrong.

``` csharp
// the family: everything needed to produce a legally-compliant invoice
// for one jurisdiction. Mixing a Dutch numbering scheme with a German
// VAT calculation and an Italian XML format is not merely odd - it
// produces documents a tax authority will reject.
public interface IInvoicingProfile
{
    CountryCode Country { get; }
    IVatCalculator      VatCalculator { get; }
    IInvoiceNumbering   Numbering { get; }
    IInvoiceSerialiser  Serialiser { get; }
    IArchivalPolicy     Archival { get; }
}

public sealed class NetherlandsProfile(IClock clock) : IInvoicingProfile
{
    public CountryCode Country => CountryCode.NL;
    public IVatCalculator VatCalculator { get; } = new DutchVatCalculator();
    public IInvoiceNumbering Numbering { get; } = new SequentialPerYear(clock);
    public IInvoiceSerialiser Serialiser { get; } = new UblPeppolSerialiser();
    public IArchivalPolicy Archival { get; } = new RetainYears(7);
}

public sealed class ItalyProfile(IClock clock, ISdiClient sdi) : IInvoicingProfile
{
    public CountryCode Country => CountryCode.IT;
    public IVatCalculator VatCalculator { get; } = new ItalianVatCalculator();
    public IInvoiceNumbering Numbering { get; } = new SequentialPerYear(clock);
    public IInvoiceSerialiser Serialiser { get; } = new FatturaPaSerialiser();
    public IArchivalPolicy Archival { get; } = new SdiArchival(sdi, years: 10);
}
```

``` csharp
// selection happens ONCE, and everything downstream is consistent by
// construction - there is no code path that can mix two profiles.
public sealed class InvoiceGenerator(IEnumerable<IInvoicingProfile> profiles)
{
    private readonly FrozenDictionary<CountryCode, IInvoicingProfile> _byCountry =
        profiles.ToFrozenDictionary(p => p.Country);

    public async Task<GeneratedInvoice> GenerateAsync(Order order, CancellationToken ct)
    {
        if (!_byCountry.TryGetValue(order.BillingCountry, out var profile))
            throw new UnsupportedJurisdictionException(order.BillingCountry);

        var vat     = profile.VatCalculator.Calculate(order.Lines);
        var number  = await profile.Numbering.NextAsync(order.BillingCountry, ct);
        var invoice = Invoice.Create(number, order, vat);
        var payload = profile.Serialiser.Serialise(invoice);

        return new GeneratedInvoice(invoice, payload, profile.Archival);
    }
}

// a startup test that proves every supported country has a complete profile
[Fact]
public void Every_supported_country_has_a_profile()
{
    foreach (var country in SupportedCountries.All)
        Assert.True(_byCountry.ContainsKey(country), $"No invoicing profile for {country}");
}
```

``` text
  THE TEST FOR WHETHER YOU ACTUALLY NEED IT

    "would mixing products from two implementations be INVALID?"

    invoicing profile   YES. Dutch numbering + Italian format is a
                        document the tax authority rejects. The factory
                        makes that combination unrepresentable.

    logger + cache      NO. Any logger works with any cache. Two
                        separate registrations, and a factory adds
                        nothing but indirection.

  and the honest warning: most "abstract factories" in real codebases
  fail this test. If the products can vary independently - and they
  usually can - you have three registrations, not a family.
```

<a id="be-h736"></a>

#### Builder

**Intent:** separate the construction of a complex object from its representation, so the same process can produce different results. In practice: *an object with many optional parts, assembled step by step*.

``` text
  THE FORCE: the telescoping constructor

    new Report(title)
    new Report(title, from)
    new Report(title, from, to)
    new Report(title, from, to, format)
    new Report(title, from, to, format, includeCharts)
    new Report(title, from, to, format, includeCharts, groupBy)
    ...

    -> which overload? what does the fourth `true` mean at the call site?
       and every optional combination needs its own overload.

  Builder replaces the combinatorics with a fluent sequence, and named
  steps make the call site readable without consulting the signature.
```

``` csharp
var report = new ReportBuilder()
    .Titled("Q3 revenue")
    .Between(from, to)
    .GroupedBy(r => r.Country)
    .IncludingCharts()
    .AsPdf()
    .Build();                                  // validation happens HERE

// this is the pattern that SURVIVED, and it is everywhere in .NET:
//   WebApplication.CreateBuilder(args)...Build()
//   new HostBuilder().ConfigureServices(...).Build()
//   new DbContextOptionsBuilder<T>().UseNpgsql(cs).Options
//   new StringBuilder().Append(...).ToString()

// the modern C# alternative for the SIMPLE case: an object initialiser
// plus `required`, which gives you named arguments and compile-time
// enforcement with no builder class at all.
var report = new Report
{
    Title = "Q3 revenue",                      // required - won't compile without
    From = from, To = to,
    Format = ReportFormat.Pdf,                 // optional, with a default
};
```

| Use a builder when                                    | Use an object initialiser when |
|-------------------------------------------------------|--------------------------------|
| Construction has **steps with order or dependencies** | The properties are independent |
| Validation must run across the whole set              | Each property validates alone  |
| The same process yields different representations     | One shape only                 |
| The result must be immutable after building           | `init` accessors suffice       |
| You want a fluent, discoverable API for consumers     | Internal construction          |

> **\[Note\]**
>
> **Builder is one of the two creational patterns worth reaching for today.** Object initialisers with `required` and `init` cover the simple case entirely, so use them by default. Builder earns its place when construction has *order*, cross-field validation, or a public API you want to be discoverable through IntelliSense — which is precisely why the .NET host and options APIs are built this way.

###### Validation, immutability, and the fluent-API details that matter

``` csharp
public sealed class ReportBuilder
{
    private string? _title;
    private DateOnly? _from, _to;
    private readonly List<Column> _columns = [];

    public ReportBuilder Titled(string title)
    {
        _title = title ?? throw new ArgumentNullException(nameof(title));
        return this;                                  // return THIS for chaining
    }

    public ReportBuilder Between(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("'to' must not precede 'from'");
        (_from, _to) = (from, to);
        return this;
    }

    public Report Build()
    {
        // CROSS-FIELD validation belongs HERE, not in the setters -
        // this is the only point where the whole set is known.
        if (_title is null) throw new InvalidOperationException("Title is required");
        if (_from is null)  throw new InvalidOperationException("A range is required");
        if (_columns.Count == 0) throw new InvalidOperationException("At least one column");

        return new Report(_title, _from.Value, _to!.Value, [.. _columns]);
        //                                                  ^^^^^^^^^^^^ COPY.
        // without it, the builder and the report share a list, and calling
        // the builder again mutates a Report you already handed out.
    }
}
```

| Fluent API decision                     | Guidance                                                                     |
|-----------------------------------------|------------------------------------------------------------------------------|
| Return `this` or a new builder?         | `this` is simpler; a new instance makes the builder reusable and thread-safe |
| Validate in the step or in `Build()`?   | Per-field in the step (fail fast), cross-field in `Build()`                  |
| Should `Build()` be callable twice?     | Decide and document. If yes, deep-copy every mutable member                  |
| Expose the builder or a static factory? | `Report.Create().Titled(...)` reads better than `new ReportBuilder()`        |
| Required steps first?                   | A **staged builder** can enforce it — see below                              |

``` csharp
// STAGED BUILDER: the compiler enforces the order, so an incomplete
// build is not merely a runtime error - it does not compile.
public interface INeedTitle  { INeedRange Titled(string t); }
public interface INeedRange  { ICanBuild  Between(DateOnly f, DateOnly t); }
public interface ICanBuild   { ICanBuild GroupedBy(...); Report Build(); }

Report.Create()          // returns INeedTitle
      .Titled("Q3")      // returns INeedRange  - Build() is not available yet
      .Between(a, b)     // returns ICanBuild
      .Build();

// more interfaces, and the mandatory steps become impossible to forget.
// worth it for a widely-used public API; overkill for internal code.
```

<a id="be-h737"></a>

#### Prototype

**Intent:** create new objects by copying an existing instance rather than constructing from scratch. The force is that construction is expensive, or that the desired starting state is easier to copy than to describe.

``` csharp
// the classic form: a Clone method
public interface IPrototype<T> { T Clone(); }

// in modern C#, records give you this for free - and correctly
public record ReportTemplate(string Title, DateOnly From, DateOnly To,
                            ReportFormat Format, bool IncludeCharts);

var quarterly = new ReportTemplate("Quarterly", q3Start, q3End,
                                   ReportFormat.Pdf, true);

// `with` produces a COPY with changes. This is Prototype, built into the
// language, with value equality thrown in.
var monthly = quarterly with { Title = "Monthly", From = m, To = m2 };
```

``` text
  SHALLOW vs DEEP - the trap in every copy operation

    record Order(Guid Id, List<OrderLine> Lines);

    var copy = original with { Id = Guid.NewGuid() };

    copy.Lines IS THE SAME LIST OBJECT as original.Lines.
    adding a line to the copy adds it to the original.

    `with` and MemberwiseClone are both SHALLOW. They copy the reference,
    not the thing referenced.

  the fixes, in order of preference
    1  make the members immutable too (ImmutableList, or a record all
       the way down) - then sharing is harmless
    2  copy explicitly:  original with { Lines = [.. original.Lines] }
    3  a hand-written deep clone - and keep it in step with the fields
    4  serialise and deserialise - correct, slow, and it silently
       drops anything not serialisable
```

Prototype as a named pattern has largely dissolved into the language: records, `with`, and `MemberwiseClone` cover it. What has *not* dissolved is the shallow-copy hazard above, which is why the pattern is still worth knowing by name — it is the thing that makes you ask “is this copy deep enough?”

###### How deep is deep enough, and the three copy strategies

``` csharp
public record Order(Guid Id, Customer Customer, List<OrderLine> Lines);

var copy = original with { Id = Guid.NewGuid() };

// what `with` ACTUALLY did:
//   Id        -> a new value          (value type, copied)
//   Customer  -> THE SAME OBJECT      (reference copied)
//   Lines     -> THE SAME LIST        (reference copied)
//
// copy.Lines.Add(x)  ->  original.Lines now contains x.
// this compiles, looks correct, and is a genuinely nasty bug because
// the mutation happens somewhere else entirely.
```

``` text
  HOW DEEP IS DEEP ENOUGH - and the answer is not "all the way"

    copy every member that is BOTH mutable AND reachable by the caller.
    stop at anything immutable, and at anything shared deliberately.

      Guid, int, DateOnly        value types      -> nothing to do
      string                     immutable        -> nothing to do
      record with value members  immutable        -> nothing to do
      List<T>, T[], Dictionary   MUTABLE          -> COPY
      a record holding a List    mutable INSIDE   -> copy the list too
      ILogger, IClock            deliberately shared -> do NOT copy
      a DbContext                shared, and copying is meaningless

    the test: "if the caller mutates this member on the copy, does the
               original change?" If yes, and that surprises anyone, copy it.
```

``` csharp
// STRATEGY 1 - explicit, and the one to prefer
var copy = original with
{
    Id = Guid.NewGuid(),
    Lines = [.. original.Lines],              // a new list, same line objects
};
// note: the LINES themselves are still shared. If OrderLine is a record
// with only value members, that is fine - nothing can mutate them.
// If OrderLine is mutable, you need [.. original.Lines.Select(l => l with { })].

// STRATEGY 2 - make the members immutable, so copying is free
public record Order(Guid Id, Customer Customer, ImmutableList<OrderLine> Lines);
// now `with` is CORRECT with no extra work, permanently. This is the
// structural fix, and it removes the entire class of bug.

// STRATEGY 3 - serialise and deserialise
var copy = JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(original))!;
// correct, slow, and it SILENTLY DROPS anything not serialisable -
// non-public members, delegates, cyclic references. Use for a quick
// deep copy in a test, not in production code.
```

> **\[Trap\]**
>
> **Common trap.** `MemberwiseClone` has exactly the same shallow behaviour as `with`, and `ICloneable` never specified which it does — which is precisely why .NET's own guidance discourages implementing it. If you expose a `Clone` method, document the depth, because the caller cannot infer it and will assume whichever is convenient.

<a id="be-h738"></a>

#### Singleton

**Intent:** ensure a class has exactly one instance, and give global access to it. It is the most famous pattern and the most criticised, and both facts are deserved.

``` csharp
// the classic implementation
public sealed class ConfigManager
{
    private static readonly Lazy<ConfigManager> _i = new(() => new ConfigManager());
    public static ConfigManager Instance => _i.Value;
    private ConfigManager() { }
}

// four real problems, none of which are about thread safety:
//
// 1. HIDDEN DEPENDENCY   nothing in a consumer's constructor reveals that
//                        it uses ConfigManager. You find out by reading
//                        every line of the body.
// 2. UNTESTABLE          you cannot substitute it. Tests share one instance.
// 3. STATIC STATE        state persists across tests, so test ORDER matters
//                        and failures become order-dependent.
// 4. LIFETIME = PROCESS  not the application's lifetime. Nothing disposes it.
```

``` csharp
// the DI version: identical guarantees, none of the problems
services.AddSingleton<IConfigManager, ConfigManager>();

public class OrderService(IConfigManager config)   // <- VISIBLE dependency
{ }

// one instance, created lazily on first resolution, disposed at shutdown,
// substitutable in a test, and declared in the constructor where the next
// reader will see it.
```

| Singleton is defensible when               | Even then                                           |
|--------------------------------------------|-----------------------------------------------------|
| A stateless, pure utility                  | A static class is clearer than a singleton instance |
| A genuine hardware or process resource     | Wrap it in an interface anyway                      |
| Framework-mandated                         | Nothing to decide                                   |
| Performance-critical, no substitution ever | Measure first; the indirection is usually free      |

> **\[Note\]**
>
> **The distinction worth holding on to.** “There should be one instance” is a legitimate requirement. “There should be one instance *and everybody reaches it through a static property*” is the anti-pattern. DI satisfies the first without the second, which is why the singleton *lifetime* is standard practice while the singleton *pattern* is not.

###### Thread safety done properly, and why Lazy is the right answer

``` csharp
// 1. NAIVE - broken. Two threads can both pass the null check.
public static Config Instance => _instance ??= new Config();

// 2. LOCK - correct, and it locks on EVERY access, forever
public static Config Instance
{
    get { lock (_gate) { return _instance ??= new Config(); } }
}

// 3. DOUBLE-CHECKED LOCKING - correct, and easy to get subtly wrong.
//    the `volatile` is REQUIRED: without it the CPU or JIT may publish
//    the reference before the constructor has finished, so another
//    thread sees a non-null, half-constructed object.
private static volatile Config? _instance;
public static Config Instance
{
    get
    {
        if (_instance is null)
            lock (_gate) { _instance ??= new Config(); }
        return _instance;
    }
}

// 4. Lazy<T> - correct, and you cannot get it wrong
private static readonly Lazy<Config> _lazy =
    new(() => new Config(), LazyThreadSafetyMode.ExecutionAndPublication);
public static Config Instance => _lazy.Value;

// 5. STATIC CONSTRUCTOR - the CLR guarantees it runs exactly once,
//    before first use, with no lock in the fast path.
private static readonly Config _instance = new();
```

``` text
  and the failure that survives ALL of the above

    a correctly-implemented singleton that is MUTABLE and shared
    across threads is still broken - the pattern guarantees ONE
    instance, not that the instance is thread-safe.

      public sealed class Cache            // one instance, correctly
      {
          private readonly Dictionary<string, object> _items = new();
          public void Set(string k, object v) => _items[k] = v;   // NOT SAFE
      }

      Dictionary is not thread-safe. Concurrent writes corrupt its
      internal buckets, and the symptom is an infinite loop inside
      Dictionary.FindEntry, on a thread you cannot interrupt.

    -> singleton lifetime obliges you to make the TYPE thread-safe:
       ConcurrentDictionary, immutable state, or explicit locking.

    this is also the single most common bug when converting a scoped
    service to a singleton for performance.
```

Which is why the DI framing matters more than the implementation: `services.AddSingleton<T>()` gives you the lifetime with none of the above, and makes the thread-safety obligation explicit at the registration — the one place a reviewer looks.

<a id="be-h739"></a>

#### Object Pool, and what DI replaced

**Object Pool intent:** reuse expensive-to-create objects rather than constructing and discarding them. It is the other creational pattern that survives intact, because it solves a problem DI does not address at all — allocation cost, not dependency resolution.

``` csharp
// where it matters: objects whose CONSTRUCTION or ALLOCATION is the cost
var pool = ObjectPool.Create<StringBuilder>();
var sb = pool.Get();
try   { sb.Append(...); return sb.ToString(); }
finally { sb.Clear(); pool.Return(sb); }        // ALWAYS return, in a finally

// and the framework's own pools, which you use without noticing:
//   ArrayPool<byte>.Shared        buffers over ~1 KB
//   MemoryPool<T>                 async pipelines
//   connection pools              ADO.NET, HttpClient handlers
//   ArrayPool inside Kestrel      request buffers

// the two bugs pooling introduces, both non-deterministic:
//   using an object after returning it - another caller now owns it
//   forgetting to return it - not a leak, but the pool is now pointless
```

``` text
  WHAT DEPENDENCY INJECTION REPLACED

  Singleton         -> AddSingleton.  Same guarantee, visible dependency,
                       substitutable, disposed properly.

  Factory Method    -> an injected Func<T>, or a keyed service.
                       No inheritance required.

  Abstract Factory  -> usually just several registrations. The container
                       resolves a consistent set because you configured it.

  Service Locator   -> NOT replaced; REJECTED. Asking a container for a
                       dependency mid-method reintroduces exactly the
                       hidden-dependency problem singletons had.

  WHAT SURVIVED

  Builder           -> construction with order and validation
  Object Pool       -> allocation cost
  Prototype         -> absorbed into records and `with`
```

> **\[Note\]**
>
> **The through-line of this part.** Creational patterns exist because `new` binds a caller to a concrete type at compile time. A DI container is a general solution to that problem, so most of the family became container configuration. The survivors are the ones addressing a *different* problem — how to assemble something complicated, and how to avoid allocating at all.

### Structural Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Structural Patterns</p>

<p class="pd">Patterns about composition — how objects fit together into larger structures without the pieces knowing about the arrangement.</p>

<a id="be-h740"></a>

#### What structural patterns are for

Creational patterns are about getting an object. Structural patterns are about **assembling objects into a larger whole**, in a way that keeps the pieces independent of the arrangement.

``` text
  all seven wrap or combine objects. The DIFFERENCE IS INTENT, and four
  of them have the same structure - which is why they are confused.

    ADAPTER     change the INTERFACE          "it doesn't fit"
    BRIDGE      separate two dimensions       "these vary independently"
    COMPOSITE   treat a TREE like a leaf      "one or many, same code"
    DECORATOR   add BEHAVIOUR                 "same thing, plus something"
    FACADE      simplify the INTERFACE        "this is too complicated"
    FLYWEIGHT   share to save MEMORY          "too many identical objects"
    PROXY       control ACCESS                "not yet / not you / not here"

  the four that wrap ONE object with the SAME interface:
    Decorator, Proxy   - and the difference is purely intent
  the ones that change the interface:
    Adapter (different shape), Facade (simpler shape)
```

| Question you are asking                              | Pattern   |
|------------------------------------------------------|-----------|
| “This interface is wrong for my code”                | Adapter   |
| “This is too many moving parts for callers”          | Facade    |
| “I want to add behaviour without changing the class” | Decorator |
| “I want to control when or whether it is reached”    | Proxy     |
| “A group should behave like a single item”           | Composite |
| “These two things vary independently”                | Bridge    |
| “I have a million near-identical objects”            | Flyweight |

###### The small ones worth knowing by name

Not every pattern needs a section. Three appear constantly in real code and cost one sentence each to recognise. **Null Object**: return a do-nothing implementation instead of `null`, so callers stop branching — `NullLogger` and `EmptyDataReader` are the canonical examples, and an empty collection is the same idea. **Flyweight**: share immutable instances rather than duplicating them — string interning and cached boxed integers are the runtime doing it for you. **Marker interface**: a type with no members, used to tag classes for discovery — largely replaced by attributes, but still how many assembly-scanning registrations work.

``` csharp
// Null Object: the branch disappears from every call site
public interface IAuditSink { void Record(AuditEntry e); }

public sealed class NullAuditSink : IAuditSink
{
    public static readonly NullAuditSink Instance = new();
    public void Record(AuditEntry e) { }          // deliberately nothing
}

// consumer never checks for null, and tests need no mock:
services.AddSingleton<IAuditSink>(config.GetValue<bool>("Audit:Enabled")
    ? new DatabaseAuditSink(db)
    : NullAuditSink.Instance);
```

<a id="be-h741"></a>

#### Adapter

**Intent:** convert the interface of a class into another interface the client expects. Same behaviour, different shape. It is the pattern you reach for when integrating anything you did not write.

``` csharp
// what you need
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(Money amount, CardToken token, CancellationToken ct);
}

// what the vendor gives you - different names, different types, different shape
public class StripeClient
{
    public Task<StripeCharge> CreateChargeAsync(long amountInCents, string currency,
                                                string source);
}

// the adapter: translation, and nothing else
public sealed class StripeGateway(StripeClient stripe) : IPaymentGateway
{
    public async Task<PaymentResult> ChargeAsync(Money amount, CardToken token,
                                                 CancellationToken ct)
    {
        var charge = await stripe.CreateChargeAsync(
            amount.MinorUnits, amount.Currency.Code, token.Value);

        return charge.Status switch
        {
            "succeeded" => PaymentResult.Success(charge.Id),
            "pending"   => PaymentResult.Pending(charge.Id),
            _           => PaymentResult.Failed(charge.FailureMessage),
        };
    }
}
// your domain now knows nothing about Stripe. Swapping providers means
// writing ONE class - not editing forty call sites.
```

**The interface must be yours.** That is the detail that makes an adapter valuable rather than decorative. If `IPaymentGateway` is shaped like Stripe's API — because you defined it after reading their docs — then the second provider will not fit it, and you have gained nothing. Define the interface from *your domain's* needs, then adapt to it.

> **\[Note\]**
>
> **Scaled up, this is the anti-corruption layer.** An adapter translates one class. An anti-corruption layer translates an entire external *model* — their status codes, their date formats, their naming — at the boundary, so none of it leaks inward. It is the same idea applied to a whole integration, and it is covered in the distributed part.

###### Designing the interface you adapt to, which decides whether it works

``` text
  THE MISTAKE THAT MAKES AN ADAPTER WORTHLESS

    you need to integrate Stripe. So you read Stripe's docs and write:

      public interface IPaymentGateway
      {
          Task<Charge> CreateChargeAsync(long amountInCents, string currency,
                                         string source, string? idempotencyKey);
      }

    this is Stripe's API with the word "interface" in front of it.

    two years later you add Adyen. It has no concept of "source", uses
    minor units differently, and returns a result that does not map to
    Charge. The interface does not fit, so you either:
      change the interface (and every caller), or
      contort Adyen into Stripe's shape (and leak Stripe's model anyway)

  THE FIX: design the interface from YOUR DOMAIN'S needs, before and
  independently of any provider.

    what does the ORDER PROCESS need?
      "take this amount from this customer, and tell me if it worked,
       is pending, or failed - and let me retry safely."

      public interface IPaymentGateway
      {
          Task<PaymentOutcome> ChargeAsync(Money amount, PaymentMethod method,
                                           IdempotencyKey key, CancellationToken ct);
      }

    now BOTH providers are adapted TO it, and neither shape is privileged.
```

``` csharp
// the domain's vocabulary, owned by you
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public long MinorUnits => (long)(Amount * Currency.MinorUnitFactor);
}

public abstract record PaymentOutcome
{
    public sealed record Succeeded(string ProviderReference) : PaymentOutcome;
    public sealed record Pending(string ProviderReference) : PaymentOutcome;
    public sealed record Failed(PaymentFailureReason Reason, string? Detail)
        : PaymentOutcome;
}
// note PaymentFailureReason is YOUR enum - InsufficientFunds, CardExpired,
// Declined, ProviderUnavailable. Each adapter maps the provider's dozens
// of codes onto YOUR handful, and the mapping table IS the adapter's value.

public sealed class StripeGateway(StripeClient stripe) : IPaymentGateway
{
    public async Task<PaymentOutcome> ChargeAsync(Money amount, PaymentMethod m,
        IdempotencyKey key, CancellationToken ct)
    {
        try
        {
            var charge = await stripe.CreateChargeAsync(
                amount.MinorUnits, amount.Currency.Code, m.Token, key.Value, ct);
            return charge.Status switch
            {
                "succeeded" => new PaymentOutcome.Succeeded(charge.Id),
                "pending"   => new PaymentOutcome.Pending(charge.Id),
                _ => new PaymentOutcome.Failed(Map(charge.FailureCode), charge.Message)
            };
        }
        catch (StripeException ex) when (ex.HttpStatusCode >= HttpStatusCode.InternalServerError)
        {
            // a PROVIDER OUTAGE is a different outcome from a DECLINE, and
            // the domain must be able to tell them apart - one is retryable.
            return new PaymentOutcome.Failed(PaymentFailureReason.ProviderUnavailable,
                                             ex.Message);
        }
    }
}
```

The test of an adapter interface: **could you have written it before choosing the provider?** If yes, the adapter is doing real work. If the interface only makes sense once you have read one vendor's documentation, you have renamed a dependency rather than inverted it — and the second provider will prove it.

<a id="be-h742"></a>

#### Decorator

**Intent:** attach additional responsibilities to an object dynamically, keeping the same interface. The defining property is that decorators are **composable** — you can stack them in any order, at runtime.

``` csharp
public interface IOrderService { Task<Order?> GetAsync(Guid id, CancellationToken ct); }

public sealed class OrderService : IOrderService { /* the real work */ }

public sealed class CachingOrderService(IOrderService inner, IMemoryCache cache)
    : IOrderService
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"order:{id}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return inner.GetAsync(id, ct);          // delegate to the INNER
        });
}

public sealed class LoggingOrderService(IOrderService inner, ILogger<IOrderService> log)
    : IOrderService
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await inner.GetAsync(id, ct);
        log.LogInformation("GetAsync({Id}) took {Ms}ms", id, sw.ElapsedMilliseconds);
        return result;
    }
}
```

``` text
  ORDER MATTERS, and it is the thing people get wrong

    services.AddScoped<IOrderService, OrderService>();
    services.Decorate<IOrderService, CachingOrderService>();
    services.Decorate<IOrderService, LoggingOrderService>();

    wrapping happens in REGISTRATION order, so the LAST is OUTERMOST:

      Logging  ->  Caching  ->  OrderService

    which means: cache hits ARE logged. Usually what you want.

    reverse the two registrations and you get

      Caching  ->  Logging  ->  OrderService

    now cache hits are NOT logged, and your timing metric measures only
    the misses - which makes the service look far faster than it is.

  the general rule: put cross-cutting concerns you want to observe
  EVERYTHING (logging, tracing) OUTERMOST.
```

| Decorator                                | Inheritance                     |
|------------------------------------------|---------------------------------|
| Composable — any combination, at runtime | Fixed at compile time           |
| N behaviours = N classes                 | N behaviours = up to 2ⁿ classes |
| The wrapped object can be swapped        | The base is fixed               |
| Adds an indirection per layer            | Direct calls                    |
| Invisible at the call site               | Visible in the type             |

> **\[Trap\]**
>
> **Common trap.** A decorator chain is invisible where it is used — someone reading `await _orders.GetAsync(id)` has no indication that four classes run first. Keep chains short, name them so they appear meaningfully in stack traces, and register them in one obvious place. A five-deep chain assembled across three extension methods is genuinely unpleasant to debug.

###### Wiring a chain, and keeping it debuggable

``` csharp
// without a container helper, the manual wiring shows what is happening
services.AddScoped<OrderService>();
services.AddScoped<IOrderService>(sp =>
    new LoggingOrderService(
        new CachingOrderService(
            sp.GetRequiredService<OrderService>(),
            sp.GetRequiredService<IMemoryCache>()),
        sp.GetRequiredService<ILogger<IOrderService>>()));
// explicit, and unbearable past two decorators.

// Scrutor does the same thing, readably
services.AddScoped<IOrderService, OrderService>();
services.Decorate<IOrderService, CachingOrderService>();
services.Decorate<IOrderService, LoggingOrderService>();
// LAST registered is OUTERMOST:  Logging -> Caching -> OrderService
```

``` text
  ORDERING, decided by what each layer must SEE

    LOGGING / TRACING   outermost.
                        must observe everything, including cache hits.
                        put it inside the cache and your metrics only
                        measure misses - so the service looks far faster
                        than it is.

    AUTHORISATION       outside the cache.
                        inside it, a cached result is returned to a
                        caller who was never authorised for it.
                        this one is a SECURITY bug, not a metrics bug.

    CACHING             outside the expensive work, inside auth+logging.

    RETRY               innermost, closest to the failure.
                        outside the cache, a retry re-runs the cache
                        lookup pointlessly.

    VALIDATION          outermost, before anything expensive happens.

    a defensible default:
      Logging -> Authorisation -> Validation -> Caching -> Retry -> Real
```

| Keeping a chain debuggable             | How                                                                 |
|----------------------------------------|---------------------------------------------------------------------|
| Name by behaviour                      | `CachingOrderService`, not `OrderServiceDecorator2`                 |
| Register in one place                  | One extension method, so the order is reviewable                    |
| Log the chain at startup               | In development, dump the resolved decorator order once              |
| Keep it short                          | Past four layers, a stack trace stops being readable                |
| Prefer a pipeline for uniform concerns | If every service needs the same five decorators, that is a pipeline |

> **\[Trap\]**
>
> **Common trap.** A decorator that *swallows* or *changes* the result is no longer a decorator — it is a proxy, and the name should say so. `CachingOrderService` returning stale data is expected; `CachingOrderService` returning `null` because authorisation failed is a lie the next reader will not catch.

<a id="be-h743"></a>

#### Proxy

**Intent:** provide a surrogate for another object to *control access to it*. Structurally identical to Decorator; the difference is entirely intent — a decorator *adds* behaviour, a proxy *governs* whether and when the real thing is reached.

| Kind of proxy       | Controls                       | Example                                              |
|---------------------|--------------------------------|------------------------------------------------------|
| **Virtual**         | *When* it is created           | Lazy loading — EF Core navigation properties         |
| **Remote**          | *Where* it lives               | A gRPC or HTTP client that looks like a local object |
| **Protection**      | *Who* may reach it             | An authorisation check before delegating             |
| **Smart reference** | *What happens* on access       | Reference counting, logging access, locking          |
| **Caching**         | *Whether* it is reached at all | Overlaps with Decorator — intent decides the name    |

``` csharp
// a protection proxy - the real service is never reached without authority
public sealed class AuthorizingOrderService(
    IOrderService inner, IAuthorizationService authz, ICurrentUser user) : IOrderService
{
    public async Task<Order?> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await inner.GetAsync(id, ct);
        if (order is null) return null;

        var result = await authz.AuthorizeAsync(user.Principal, order, "ViewOrder");
        if (!result.Succeeded) throw new ForbiddenException();
        return order;
    }
}

// EF Core's lazy loading is a VIRTUAL proxy, generated at runtime:
public class Order
{
    public virtual Customer Customer { get; set; } = null!;   // `virtual` is required
}
// EF subclasses Order and overrides the property to load on first access.
// which is also why lazy loading causes N+1: the access looks free and
// issues a query.
```

The Decorator/Proxy distinction is worth being precise about in review, because the name sets the reader's expectation. `CachingOrderService` suggests “the same results, faster”. `AuthorizingOrderService` suggests “this may refuse”. Naming a proxy as a decorator hides the fact that it can change the outcome.

###### The lazy-loading proxy, and why it causes N+1

``` csharp
// EF Core lazy loading is a VIRTUAL proxy, generated at runtime.
public class Order
{
    public virtual Customer Customer { get; set; } = null!;   // `virtual` REQUIRED
    public virtual ICollection<OrderLine> Lines { get; set; } = [];
}
services.AddDbContext<AppDbContext>(o => o.UseLazyLoadingProxies().UseNpgsql(cs));

// EF SUBCLASSES Order at runtime and overrides the property:
//
//   public override Customer Customer
//   {
//       get { if (!_loaded) _loader.Load(this, nameof(Customer)); return base.Customer; }
//   }
//
// so this line:
Console.WriteLine(order.Customer.Name);
// ...is a DATABASE QUERY that looks like a property read.
```

``` text
  WHY THE PROXY PATTERN AND N+1 ARE THE SAME THING

    the proxy's whole PURPOSE is that the caller CANNOT TELL the real
    object was not there. That transparency is the feature.

    it is also the bug: the caller cannot tell it is issuing a query.

      foreach (var o in orders)          // 1 query
          Print(o.Customer.Name);         // 1 query PER ITERATION

    50 orders -> 51 queries. Nothing in the C# suggests it.

  the general lesson, beyond EF: A PROXY THAT DOES EXPENSIVE WORK
  TRANSPARENTLY WILL BE CALLED IN A LOOP. Transparency and cost are
  in direct tension, and the pattern gives you no way to signal the
  cost at the call site.

  which is why the guidance for lazy loading has settled on:

    DISABLE it. Load explicitly with Include, or project with Select.
    an EXPLICIT query is visible in review; a lazy load is not.

    and if you keep it, at minimum detect it:
      o.ConfigureWarnings(w =>
          w.Throw(CoreEventId.LazyLoadOnDisposedContextWarning)
           .Log(CoreEventId.DetachedLazyLoadingWarning));
```

| Proxy kind       | Hidden cost                         | How to make it visible                          |
|------------------|-------------------------------------|-------------------------------------------------|
| Lazy loading     | A query per access                  | Disable it; use `Include` or projection         |
| Remote proxy     | A network call per method           | Name it `*Client`; make methods `async`         |
| Caching proxy    | Stale data                          | Log hits and misses; expose the hit ratio       |
| Protection proxy | A refusal the caller may not expect | Return a result type, not a silent null         |
| Smart reference  | Locking or counting                 | Document it — nothing at the call site shows it |

The design principle that falls out: **make expensive operations look expensive**. An `async` method with a `CancellationToken` reads as I/O; a property read does not. Where a proxy must be transparent, the compensating control is instrumentation — a metric on calls per request will surface an N+1 that no amount of code reading would.

<a id="be-h744"></a>

#### Facade

**Intent:** provide a single simplified interface to a set of interfaces in a subsystem. Unlike an adapter, the facade's interface is *new* — it is not a translation of anything.

``` csharp
// without: the caller orchestrates five collaborators and must know the order
var order = await _orders.GetAsync(id, ct);
var stock = await _inventory.ReserveAsync(order.Lines, ct);
var payment = await _payments.ChargeAsync(order.Total, order.Token, ct);
await _shipping.ScheduleAsync(order, ct);
await _notifications.SendConfirmationAsync(order, ct);
// every caller repeats this, and every caller can get the order wrong.

// with a facade: one method, one concept
public sealed class CheckoutService(
    IOrderRepository orders, IInventory inventory, IPaymentGateway payments,
    IShipping shipping, INotifier notifier)
{
    public async Task<CheckoutResult> CompleteAsync(Guid orderId, CancellationToken ct)
    {
        // the orchestration - including the compensation logic - lives HERE, once
    }
}
```

| Adapter                                                    | Facade                               |
|------------------------------------------------------------|--------------------------------------|
| Interface dictated by the *client*'s existing expectations | Interface invented for convenience   |
| Usually wraps **one** thing                                | Usually wraps **several**            |
| Translates                                                 | Simplifies and orchestrates          |
| Same capability, different shape                           | A subset of capability, easier shape |

> **\[Trap\]**
>
> **Common trap.** A facade must not become the only way in. If it hides the subsystem entirely, every unanticipated need becomes a new method on the facade — and it grows into a god object. Keep the subsystem accessible for callers with unusual requirements; the facade is a convenience for the common path, not a wall.

###### A checkout facade, and where the orchestration actually lives

``` csharp
// WITHOUT - every caller repeats the sequence, and every caller can
// get the order or the compensation wrong
var order     = await _orders.ByIdAsync(orderId, ct);
var reserved  = await _inventory.ReserveAsync(order.Lines, ct);
var payment   = await _payments.ChargeAsync(order.Total, method, key, ct);
await _shipping.ScheduleAsync(order, ct);
await _notifications.SendConfirmationAsync(order, ct);
// what if payment fails after the reservation? What if shipping fails
// after payment? Each caller invents its own answer, and they differ.
```

``` csharp
// WITH - one method, and the compensation logic exists exactly once
public sealed class CheckoutService(
    IOrderStore orders, IInventoryService inventory, IPaymentGateway payments,
    IShippingService shipping, IOutbox outbox, AppDbContext db,
    TimeProvider clock, ILogger<CheckoutService> log)
{
    public async Task<CheckoutResult> CompleteAsync(
        OrderId orderId, PaymentMethod method, IdempotencyKey key,
        CancellationToken ct)
    {
        var order = await orders.ByIdAsync(orderId, ct);
        if (order is null) return CheckoutResult.NotFound();
        if (order.Status != OrderStatus.Draft) return CheckoutResult.AlreadyPlaced(order.Id);

        // 1. RESERVE - reversible, so it goes first
        var reservation = await inventory.ReserveAsync(order.Lines, ct);
        if (!reservation.Succeeded)
            return CheckoutResult.OutOfStock(reservation.UnavailableSkus);

        try
        {
            // 2. CHARGE - the irreversible-ish step. Idempotency key so a
            //    retry after a timeout does not charge twice.
            var payment = await payments.ChargeAsync(order.Total, method, key, ct);

            if (payment is PaymentOutcome.Failed f)
            {
                await inventory.ReleaseAsync(reservation.Id, ct);      // COMPENSATE
                return CheckoutResult.PaymentDeclined(f.Reason);
            }

            // 3. COMMIT our own state and the outbound events atomically.
            //    shipping and notification are NOT called here - they are
            //    events, so a failure in either cannot undo the payment.
            order.MarkPlaced(clock.GetUtcNow());
            outbox.Add(new OrderPlaced(order.Id, order.CustomerId, order.Total));
            outbox.Add(new StockCommitted(reservation.Id));
            await db.SaveChangesAsync(ct);

            return CheckoutResult.Placed(order.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // the reservation must not leak if anything above threw
            await inventory.ReleaseAsync(reservation.Id, CancellationToken.None);
            //                                            ^^^^^^^^^^^^^^^^^^^^^^^
            // compensation must NOT be cancellable - cancelling cleanup
            // leaves the system worse than the original failure did.
            log.LogError(ex, "Checkout failed for {OrderId}", orderId);
            throw;
        }
    }
}
```

``` text
  THE ORDERING RULE THIS DEMONSTRATES

    put the REVERSIBLE steps first and the IRREVERSIBLE steps last.

      reserve stock      reversible  -> first
      charge payment     compensable -> middle (a refund is a NEW event)
      schedule shipping  hard to undo once picked
      send email         IRREVERSIBLE -> last, and via an event

    ordering this way means most failures need no compensation at all,
    because nothing irreversible has happened yet.

  AND: the facade does NOT call shipping and notification directly.
  they are events. If it called them, a failure in the email service
  would surface as a failed checkout after the money was taken.

  the boundary is: the facade owns everything that must be consistent
  with the payment. Everything else is a consequence, and consequences
  go through the outbox.
```

> **\[Trap\]**
>
> **Common trap.** A facade must not become the only way in. Keep the subsystems accessible for callers with unusual needs, or every unanticipated requirement becomes a new method on the facade — and within a year it is a god object with fourteen dependencies that every team must change and nobody owns.

<a id="be-h745"></a>

#### Composite

**Intent:** compose objects into tree structures, and let clients treat individual objects and compositions *identically*. The force is the `if (isGroup)` check that otherwise appears at every call site.

``` csharp
// both a leaf and a group implement the SAME interface
public interface IValidationRule
{
    ValidationResult Validate(Order order);
}

public sealed class MinimumTotalRule(decimal min) : IValidationRule   // leaf
{
    public ValidationResult Validate(Order o) =>
        o.Total >= min ? ValidationResult.Ok : ValidationResult.Fail($"Minimum {min}");
}

public sealed class AllOf(params IValidationRule[] rules) : IValidationRule  // composite
{
    public ValidationResult Validate(Order o)
    {
        var failures = rules.Select(r => r.Validate(o)).Where(r => !r.IsValid).ToList();
        return failures.Count == 0 ? ValidationResult.Ok : ValidationResult.Combine(failures);
    }
}

// now a tree is built from the same parts, and the caller cannot tell
IValidationRule rules = new AllOf(
    new MinimumTotalRule(10m),
    new AnyOf(new HasBillingAddress(), new IsInternalCustomer()),
    new AllOf(new StockAvailable(), new NotOnCreditHold()));

var result = rules.Validate(order);          // one call, whatever the depth
```

**Where you already meet it:** the file system (a directory is a file), the DOM, `IServiceCollection`'s composite validators, ASP.NET Core's `CompositeEndpointDataSource`, and every UI framework's control tree.

> **\[Trap\]**
>
> **Common trap.** Composite pushes a design tension into the interface: should a leaf expose `Add` and `Remove`? Putting them on the shared interface means leaves must throw (a Liskov violation); putting them only on the composite means callers must type-check. There is no clean answer — prefer the second, since an explicit cast is honest and a throwing method is not.

###### The Add/Remove problem, and the two honest resolutions

``` text
  THE TENSION AT THE HEART OF COMPOSITE

    the pattern's promise: clients treat a leaf and a composite IDENTICALLY.
    so both implement the same interface.

    but a composite has children and a leaf does not. Where do Add and
    Remove go?

  OPTION A - on the shared interface (GoF's original suggestion)

    interface IComponent { void Add(IComponent c); void Remove(IComponent c); }
    class Leaf : IComponent { public void Add(IComponent c) => throw new
                                NotSupportedException(); }

    + full transparency: a client never type-checks
    - EVERY LEAF LIES. This is a Liskov violation by construction, and
      the caller has no way to know except by trying.

  OPTION B - only on the composite

    interface IComponent { Result Evaluate(Context c); }
    class Composite : IComponent { public void Add(IComponent c) {...} }

    + no lying. The type system tells the truth.
    - a client that BUILDS trees must type-check or hold the concrete type

  the resolution that works in practice: OPTION B, plus building the
  tree through a CONSTRUCTOR rather than through Add at all.
```

``` csharp
// immutable composite: no Add, no Remove, no lying, no type checks
public interface IValidationRule
{
    ValidationResult Validate(Order order);
}

public sealed class AllOf(params IValidationRule[] rules) : IValidationRule
{
    public ValidationResult Validate(Order o)
    {
        var failures = rules.Select(r => r.Validate(o))
                            .Where(r => !r.IsValid).ToList();
        return failures.Count == 0 ? ValidationResult.Ok
                                   : ValidationResult.Combine(failures);
    }
}
public sealed class AnyOf(params IValidationRule[] rules) : IValidationRule { ... }
public sealed class Not(IValidationRule rule) : IValidationRule { ... }

// the tree is BUILT, not MUTATED - and it reads like the rule it encodes
IValidationRule rules = new AllOf(
    new MinimumTotal(10m),
    new AnyOf(new HasBillingAddress(), new IsInternalCustomer()),
    new Not(new IsOnCreditHold()));

var result = rules.Validate(order);      // one call, whatever the depth
```

| Where you already meet Composite | The leaf      | The composite                           |
|----------------------------------|---------------|-----------------------------------------|
| The file system                  | A file        | A directory                             |
| The DOM                          | A text node   | An element                              |
| `ILogger`                        | One provider  | The logger that fans out to all of them |
| ASP.NET Core endpoints           | One endpoint  | `CompositeEndpointDataSource`           |
| Validation rules                 | One rule      | `AllOf` / `AnyOf`                       |
| Specifications                   | One predicate | `And` / `Or`                            |

The last two rows show why Composite and Specification are so often mentioned together: a specification that supports `And` and `Or` *is* a composite, and the combining operators are composite nodes. Recognising that lets you reuse the same immutable-tree design for both.

<a id="be-h746"></a>

#### Bridge and Flyweight

The two least-used structural patterns, included because when you need them nothing else will do.

##### Bridge: separating two dimensions of variation

``` text
  THE FORCE: two things vary INDEPENDENTLY, and inheritance multiplies them

    reports:   Sales, Inventory, Financial
    outputs:   Pdf, Excel, Html

    with inheritance:  SalesPdfReport, SalesExcelReport, SalesHtmlReport,
                       InventoryPdfReport, ... = 3 x 3 = 9 classes
                       adding one output format adds THREE classes.

    Bridge splits the hierarchy in two and composes them:

      Report (abstraction)  --has-a-->  IRenderer (implementation)
        SalesReport                       PdfRenderer
        InventoryReport                   ExcelRenderer
        FinancialReport                   HtmlRenderer

      3 + 3 = 6 classes, and adding an output format adds ONE.
```

``` csharp
public abstract class Report(IRenderer renderer)
{
    protected IRenderer Renderer { get; } = renderer;    // the BRIDGE
    public abstract Task<Stream> GenerateAsync(CancellationToken ct);
}

public sealed class SalesReport(IRenderer renderer, ISalesData data)
    : Report(renderer)
{
    public override async Task<Stream> GenerateAsync(CancellationToken ct)
        => Renderer.Render(await data.LoadAsync(ct));    // any renderer works
}
// the test for Bridge: does adding a variant on ONE axis force you to add
// classes on the OTHER? If yes, the axes are tangled - split them.
```

##### Flyweight: sharing to save memory

**Intent:** share common state between many objects instead of duplicating it. The force is memory, and nothing else — which is why it is rare in business software and common in rendering, text processing and games.

``` csharp
// intrinsic state (shared, immutable) vs extrinsic state (per instance)
// 100,000 map markers: the ICON is shared, the POSITION is not.

public sealed record MarkerStyle(string IconPath, int Size, string Colour);  // intrinsic

public static class MarkerStyles      // the flyweight factory
{
    private static readonly ConcurrentDictionary<string, MarkerStyle> _cache = new();
    public static MarkerStyle Get(string kind) =>
        _cache.GetOrAdd(kind, k => new MarkerStyle($"/icons/{k}.svg", 24, "#3b82f6"));
}

public readonly record struct Marker(double Lat, double Lon, MarkerStyle Style);
// 100,000 markers share ~6 MarkerStyle instances instead of holding 100,000.

// .NET does this for you in two familiar places:
//   string interning - identical literals share one instance
//   Enum boxing cache, and small int caches in some runtimes
```

> **\[Note\]**
>
> **Both are worth recognising, rarely worth reaching for.** Bridge is the answer when you notice a class count multiplying across two axes — and the recognition is the valuable part, since the fix is obvious once named. Flyweight is a memory optimisation and should follow a measurement, never precede one; in ordinary line-of-business code it almost never applies.

### Behavioural Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Behavioural Patterns</p>

<p class="pd">Patterns about collaboration — how responsibility is assigned and how objects communicate. The largest family, and the one with the most everyday use.</p>

<a id="be-h747"></a>

#### What behavioural patterns are for

``` text
  the question each one answers

    STRATEGY         "which algorithm?"        caller picks, at runtime
    STATE            "which behaviour now?"    the object decides, by state
    TEMPLATE METHOD  "same steps, different    inheritance fills the gaps
                      details?"
    COMMAND          "do this, later, or       an action as an object
                      undo it?"
    CHAIN            "who handles this?"       pass it along until someone does
    MEDIATOR         "who talks to whom?"      a hub, so they need not know
    OBSERVER         "who cares that this      broadcast to subscribers
                      happened?"
    ITERATOR         "how do I walk it?"       traverse without exposing shape
    VISITOR          "new operation, closed    push the operation outward
                      hierarchy?"
    MEMENTO          "how do I undo?"          capture and restore state

  and the pairs to keep straight, because the structures are identical:
    Strategy vs State      -> who chooses: the CALLER, or the OBJECT?
    Mediator vs Observer   -> routing: CENTRAL, or BROADCAST?
    Template vs Strategy   -> variation via INHERITANCE, or COMPOSITION?
```

This family is where most day-to-day design work happens, because most design work is about deciding *which object is responsible for what*. The structural patterns arrange objects; these decide who does the work.

> **\[Why it matters\]** Behavioural patterns are where most design debate actually happens, because they answer the only question that really matters in a review: *which object should own this responsibility?* Naming them converts an argument about preference into a comparison of known trade-offs.

<a id="be-h748"></a>

#### Strategy

**Intent:** define a family of interchangeable algorithms, encapsulate each one, and let the caller choose. The most useful behavioural pattern, and the one DI makes almost free.

``` csharp
public interface IPricingStrategy
{
    bool AppliesTo(Customer c);
    Money Calculate(Order o);
}

public sealed class VolumePricing : IPricingStrategy
{
    public bool AppliesTo(Customer c) => c.AnnualSpend > 100_000m;
    public Money Calculate(Order o) => o.Subtotal * 0.85m;
}

// registering N implementations and injecting IEnumerable<T> is the
// single most useful DI feature people do not know about
public sealed class Pricer(IEnumerable<IPricingStrategy> strategies)
{
    public Money Price(Customer c, Order o) =>
        strategies.First(s => s.AppliesTo(c)).Calculate(o);
}

// or, when the CALLER knows which one it wants, keyed services:
services.AddKeyedScoped<IPricingStrategy, VolumePricing>("volume");
public class Svc([FromKeyedServices("volume")] IPricingStrategy pricing) { }
```

``` text
  STRATEGY vs STATE - identical structure, opposite intent

    STRATEGY
      the CALLER selects the implementation, and it does not change
      by itself. The strategies do not know about each other.

        pricer.Price(customer, order)     // Volume today, Volume tomorrow

    STATE
      the OBJECT changes its own behaviour as its state transitions,
      and the states DO know about each other - each decides what
      comes next.

        order.Approve()   // Draft -> Submitted; now Approve() behaves
                          // differently, or is invalid

    the test: "can the object change which implementation it uses,
               by itself, as a result of an operation?"
               yes -> State.  no -> Strategy.
```

| Use Strategy when                                | Use a switch when                |
|--------------------------------------------------|----------------------------------|
| Variants are added by other teams, or by plugins | The set is closed and small      |
| Each variant needs its own dependencies          | All variants are one-liners      |
| Variants have real logic worth testing alone     | You want exhaustiveness checking |
| The set grows regularly                          | The variants change together     |

###### Four selection mechanisms, compared

``` csharp
// 1. SELF-SELECTING - the strategies know when they apply.
//    best when the condition is domain logic.
public interface IPricingStrategy { bool AppliesTo(Customer c); Money Calculate(Order o); }
public class Pricer(IEnumerable<IPricingStrategy> all)
{
    public Money Price(Customer c, Order o) => all.First(s => s.AppliesTo(c)).Calculate(o);
}
//    + the rule lives with the strategy
//    - ORDER MATTERS and is implicit. Two matching strategies -> first wins,
//      and "first" is registration order. Make it explicit with a Priority.

// 2. KEYED - the caller names what it wants.
services.AddKeyedScoped<IPricingStrategy, VolumePricing>("volume");
public class Svc(IServiceProvider sp)
{
    Money Price(string plan, Order o) =>
        sp.GetRequiredKeyedService<IPricingStrategy>(plan).Calculate(o);
}
//    + explicit, and O(1)
//    - a missing key is a RUNTIME failure. Validate the key set at startup.

// 3. DICTIONARY - built once, no container involvement
private static readonly FrozenDictionary<PlanType, IPricingStrategy> _map = ...;
//    + fastest, and exhaustiveness is checkable in a test
//    - strategies cannot have injected dependencies easily

// 4. DELEGATE - no interface at all
private static readonly Dictionary<PlanType, Func<Order, Money>> _rules = ...;
//    + minimal ceremony
//    - nothing to search for; no place to attach a test or a dependency
```

| Choose         | When                                                 |
|----------------|------------------------------------------------------|
| Self-selecting | The applicability rule is itself domain logic        |
| Keyed          | The caller knows which one, by name or configuration |
| Dictionary     | A closed set, no dependencies, hot path              |
| Delegate       | One-line transformations, local to one class         |

The failure mode specific to self-selecting strategies is worth naming: **overlapping conditions**. Two strategies both return true, and which one wins depends on DI registration order — which nobody thinks of as behaviour. Either make the conditions provably disjoint, add an explicit `Priority`, or assert in a test that exactly one strategy matches each of your known customer shapes.

<a id="be-h749"></a>

#### State

**Intent:** let an object alter its behaviour when its internal state changes, so that it appears to change class. The force is a growing tangle of `if (status == ...)` checks spread across every method.

``` csharp
// the smell this replaces
public void Cancel()
{
    if (Status == "Draft")     { Status = "Cancelled"; return; }
    if (Status == "Submitted") { Refund(); Status = "Cancelled"; return; }
    if (Status == "Shipped")   throw new InvalidOperationException("Already shipped");
    if (Status == "Cancelled") return;
}
// ...and the same ladder repeats in Approve(), Ship(), Refund(), Amend().
// adding a state means finding and editing every one of them.

// the state machine, made explicit and total
public abstract record OrderState
{
    public abstract OrderState Cancel(Order o);
    public abstract OrderState Ship(Order o);
}
public sealed record Draft : OrderState
{
    public override OrderState Cancel(Order o) => new Cancelled();
    public override OrderState Ship(Order o) => throw new InvalidTransition("Draft->Shipped");
}
public sealed record Submitted : OrderState
{
    public override OrderState Cancel(Order o) { o.Refund(); return new Cancelled(); }
    public override OrderState Ship(Order o) => new Shipped();
}
// now: every state must answer every operation. Adding a state is a
// COMPILE ERROR until you have handled every transition - which is
// exactly the safety the if-ladder never gave you.
```

``` text
  A LIGHTER FORM, and often the right one in C#

    a transition TABLE, when behaviour per state is trivial

      private static readonly Dictionary<(Status, Trigger), Status> _transitions = new()
      {
          [(Draft,     Submit)]  = Submitted,
          [(Submitted, Approve)] = Approved,
          [(Submitted, Cancel)]  = Cancelled,
          [(Approved,  Ship)]    = Shipped,
      };

      public bool TryFire(Trigger t)
          => _transitions.TryGetValue((Status, t), out var next) && (Status = next) == next;

    + every legal transition visible in ONE place, reviewable by a
      non-programmer
    + illegal transitions are the DEFAULT, not something you must remember
    - no per-state behaviour, only per-state legality

    use the table when the question is "is this allowed?"
    use full State when each state also BEHAVES differently.
```

**Where you already meet it:** workflow engines, the TCP connection state machine, `HttpClient`'s internal request lifecycle, and Stateless or MassTransit's saga state machines — all of which are this pattern with persistence added.

###### Persisting a state machine, and making illegal states unrepresentable

``` csharp
// the persistence problem: the DATABASE stores a string, the DOMAIN wants
// behaviour. The mapping happens at the boundary, in one place.
public class Order
{
    public OrderStatus Status { get; private set; }        // persisted as a string
    private OrderState State => OrderState.For(Status);    // behaviour, derived

    public void Ship(TrackingNumber t)
    {
        Status = State.Ship(this, t);                       // the state decides
    }
}

public abstract class OrderState
{
    private static readonly FrozenDictionary<OrderStatus, OrderState> _states = ...;
    public static OrderState For(OrderStatus s) => _states[s];

    public virtual OrderStatus Ship(Order o, TrackingNumber t) =>
        throw new InvalidTransitionException(o.Status, nameof(Ship));
    // DEFAULT IS REFUSAL. A state must OPT IN to each transition, so a
    // new state cannot silently allow everything.
}
```

``` text
  THREE LEVELS, and choosing deliberately

  1  A TRANSITION TABLE - when only LEGALITY varies

       (Draft,     Submit)  -> Submitted
       (Submitted, Approve) -> Approved
       (Approved,  Ship)    -> Shipped

     + every legal transition visible in ONE place, reviewable by a
       non-programmer, and testable exhaustively
     + illegal transitions are the DEFAULT
     - no per-state behaviour

  2  STATE CLASSES - when each state also BEHAVES differently

     + adding a state forces you to handle every transition
     - more types

  3  A STATE MACHINE LIBRARY (Stateless, MassTransit sagas)
     when you also need: entry/exit actions, guards, timeouts,
     persistence and a visual diagram
     - a dependency, and a DSL to learn

  most business objects need 1. Reaching for 2 or 3 without needing
  per-state behaviour is the usual over-application.
```

``` csharp
// and the modelling ideal: make illegal states UNREPRESENTABLE, so the
// state machine is enforced by the TYPE SYSTEM rather than at runtime.
public abstract record Order
{
    public sealed record Draft(OrderId Id, IReadOnlyList<Line> Lines) : Order
    {
        public Submitted Submit() => new(Id, Lines, DateTimeOffset.UtcNow);
    }
    public sealed record Submitted(OrderId Id, IReadOnlyList<Line> Lines,
                                   DateTimeOffset At) : Order
    {
        public Shipped Ship(TrackingNumber t) => new(Id, Lines, t);
    }
    public sealed record Shipped(OrderId Id, IReadOnlyList<Line> Lines,
                                 TrackingNumber Tracking) : Order;
}
// a Shipped order has NO Submit method. Calling it is a COMPILE error,
// and Shipped is the only state that HAS a tracking number - so
// "shipped without tracking" cannot be constructed.
// the cost: ORM mapping becomes considerably harder. Worth it for a
// core aggregate; overkill for most.
```

> **\[Why it matters\]** The State pattern is the cure for the specific mess every long-lived entity grows: status checks scattered across a dozen methods, each one a chance to forget a case. Once transitions live in one place, the illegal ones become impossible rather than merely discouraged.

<a id="be-h750"></a>

#### Template Method

**Intent:** define the skeleton of an algorithm in a base class, deferring some steps to subclasses. The shape is fixed; the details vary.

``` csharp
public abstract class ImportJob
{
    public async Task<ImportResult> RunAsync(CancellationToken ct)   // the TEMPLATE
    {
        var raw = await FetchAsync(ct);          // abstract - subclass supplies
        var validated = Validate(raw);           // virtual  - subclass MAY override
        var saved = await SaveAsync(validated, ct);
        await NotifyAsync(saved, ct);            // virtual, default does nothing
        return saved;
    }

    protected abstract Task<RawData> FetchAsync(CancellationToken ct);
    protected abstract Task<ImportResult> SaveAsync(RawData d, CancellationToken ct);
    protected virtual RawData Validate(RawData d) => d;
    protected virtual Task NotifyAsync(ImportResult r, CancellationToken ct)
        => Task.CompletedTask;
}
```

``` text
  HOW IT DEGRADES - and it does, reliably, past about three subclasses

    a subclass needs the steps in a different ORDER
      -> the template cannot express it

    a subclass needs to SKIP a step
      -> an override that does nothing. The template now lies.

    a NEW step is needed by some subclasses
      -> a virtual no-op added for everyone else

    the base gains a flag to change its own behaviour
      -> protected bool SkipValidation
         THIS is the signal it has failed. The skeleton is no longer
         a skeleton; it is a configurable procedure.

  THE EXIT: turn the steps into INJECTED COLLABORATORS.

    RunAsync(IFetcher f, IValidator v, ISaver s, INotifier n)

    now: order is caller-controlled, each piece is independently
    testable, combinations are free, and nothing inherits anything.
    which is STRATEGY, applied per step.
```

| Template Method                       | Strategy                         |
|---------------------------------------|----------------------------------|
| Variation via **inheritance**         | Variation via **composition**    |
| One subclass supplies all the steps   | Each step can vary independently |
| The skeleton is fixed                 | The caller composes the skeleton |
| Fine at 2–3 subclasses                | Scales to any number             |
| Steps can access protected base state | Steps are self-contained         |

> **\[Note\]**
>
> **Where it still belongs.** Template Method is right when a *framework* owns the skeleton and you fill in the gaps — `BackgroundService.ExecuteAsync`, `ControllerBase`, a test base class. It is wrong when *you* own both the skeleton and the subclasses, because then you have the option of composing instead, and composing scales.

###### Converting one to composition, with the framework case kept

``` csharp
// BEFORE: five subclasses, and the base has grown a flag
public abstract class ImportJob
{
    protected bool SkipValidation { get; set; }      // <- the failure signal

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var raw = await FetchAsync(ct);
        var data = SkipValidation ? raw : Validate(raw);
        var saved = await SaveAsync(data, ct);
        await NotifyAsync(saved, ct);
        return saved;
    }
    protected abstract Task<Raw> FetchAsync(CancellationToken ct);
    protected virtual Raw Validate(Raw r) => r;
    protected abstract Task<Result> SaveAsync(Raw r, CancellationToken ct);
    protected virtual Task NotifyAsync(Result r, CancellationToken ct)
        => Task.CompletedTask;
}
```

``` csharp
// AFTER: the steps are collaborators, and the skeleton is composed
public interface IFetcher  { Task<Raw> FetchAsync(CancellationToken ct); }
public interface IValidator { Raw Validate(Raw raw); }
public interface ISaver    { Task<Result> SaveAsync(Raw raw, CancellationToken ct); }
public interface INotifier { Task NotifyAsync(Result r, CancellationToken ct); }

public sealed class ImportJob(IFetcher fetch, IValidator validate,
                              ISaver save, INotifier notify)
{
    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var raw = await fetch.FetchAsync(ct);
        var data = validate.Validate(raw);
        var saved = await save.SaveAsync(data, ct);
        await notify.NotifyAsync(saved, ct);
        return saved;
    }
}

// "skip validation" is no longer a flag - it is a DIFFERENT VALIDATOR
public sealed class NoValidation : IValidator { public Raw Validate(Raw r) => r; }
// a null object, and the skeleton does not know or care.

// and a job needing a different ORDER is now just a different class,
// composed from the same four pieces. Under inheritance that was impossible.
```

``` text
  WHEN TO KEEP TEMPLATE METHOD - and it is a real, common case

    A FRAMEWORK OWNS THE SKELETON and gives you hooks:

      BackgroundService.ExecuteAsync
      ControllerBase / PageModel
      DbContext.OnModelCreating
      Attribute-based filters
      a test base class with shared setup

    here you do NOT own the skeleton, cannot compose it, and the
    framework's contract IS the template. Overriding is correct.

  THE DISTINCTION

    someone ELSE owns the skeleton  -> Template Method is correct
    YOU own the skeleton AND the
      subclasses                    -> composition is available, and
                                       scales better

  the second case is where it degrades, because you have the option of
  composing and did not take it.
```

| Degradation signal                               | Meaning                                       |
|--------------------------------------------------|-----------------------------------------------|
| A `protected bool Skip*` flag                    | The skeleton is now configurable, not fixed   |
| An override that does nothing                    | That step does not belong in the template     |
| A subclass needing a different order             | The skeleton is wrong for it                  |
| A virtual no-op added for existing subclasses    | The template is accreting                     |
| The base needs a new dependency for one subclass | Every subclass and test now changes           |
| Three levels of inheritance                      | Nobody can tell where a method is implemented |

<a id="be-h751"></a>

#### Command

**Intent:** turn a request into an object. Once an action is a value rather than a method call, it can be queued, logged, retried, undone, serialised, and sent over a network — none of which a method call supports.

``` csharp
// the action, as data
public sealed record PlaceOrder(Guid CustomerId, IReadOnlyList<LineDto> Lines)
    : ICommand<Guid>;

public sealed class PlaceOrderHandler(AppDbContext db) : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder cmd, CancellationToken ct) { ... }
}

// what being an OBJECT buys you, and none of it works with a method call:
await bus.SendAsync(cmd, ct);            // send it over a network
await queue.EnqueueAsync(cmd, ct);       // process it later
audit.Record(cmd);                        // log exactly what was asked for
await retryPolicy.ExecuteAsync(() => handler.HandleAsync(cmd, ct));
```

``` text
  UNDO - the classic motivation, and the honest version

    interface ICommand { void Execute(); void Undo(); }

    a stack of executed commands, popped and undone in reverse.
    this works beautifully for an EDITOR - text, drawings, spreadsheets -
    where the application owns all the state.

    it works POORLY for a business system, because:
      the side effects left the process (an email was sent)
      another user has since acted on the change
      the "undo" is itself a business event with its own rules

    which is why business systems use COMPENSATION, not undo:
      the refund is a NEW transaction, visible to the customer,
      not an erasure of the charge.

    same pattern, different honesty about what "reversing" means.
```

| Command earns its place when                    | A method call is enough when      |
|-------------------------------------------------|-----------------------------------|
| The action must cross a process boundary        | Everything is in-process          |
| It must be queued or scheduled                  | It happens now                    |
| It must be retried or replayed                  | Failure is immediate and final    |
| It must be audited as intent                    | Logging the effect suffices       |
| It must be undoable                             | Nothing is reversed               |
| You want a uniform pipeline around every action | There is no cross-cutting concern |

**The pipeline consequence:** once every action is a command object flowing through one dispatcher, you get a single place to add validation, logging, transactions and authorisation — which is why the mediator pattern and Command are almost always used together, and why that combination underpins CQRS.

###### Undo, and why it is harder than the textbook version

``` csharp
// the textbook undo stack
public interface IUndoableCommand { void Execute(); void Undo(); }

public sealed class AddLineCommand(Order order, Line line) : IUndoableCommand
{
    public void Execute() => order.AddLine(line);
    public void Undo()    => order.RemoveLine(line);
}

public sealed class CommandStack
{
    private readonly Stack<IUndoableCommand> _done = new(), _undone = new();

    public void Do(IUndoableCommand c) { c.Execute(); _done.Push(c); _undone.Clear(); }
    //                                                ^^^^^^^^^^^^^^ REDO is
    // invalidated by a new action - this line is the one people forget,
    // and omitting it lets a redo reapply an action to a state it never
    // applied to.

    public void Undo() { if (_done.TryPop(out var c)) { c.Undo(); _undone.Push(c); } }
    public void Redo() { if (_undone.TryPop(out var c)) { c.Execute(); _done.Push(c); } }
}
```

``` text
  WHY THIS WORKS FOR EDITORS AND FAILS FOR BUSINESS SYSTEMS

    an editor OWNS ALL THE STATE. Undo is a local, total operation.

    a business system does not:

      the side effect LEFT THE PROCESS
        an email was sent. There is no Undo() for that.

      SOMEONE ELSE ACTED on the change
        the warehouse already picked the item.

      the undo is ITSELF a business event
        with its own authorisation, its own audit entry, and its own
        accounting treatment.

      state moved on
        undoing step 3 when steps 4 and 5 happened is not "pop the stack".

  -> business systems use COMPENSATION, not undo:
       a refund is a NEW transaction, visible to the customer, with its
       own id - not an erasure of the charge.

     same pattern, honest about what "reversing" means.
```

| If you need undo, ask                             | Because                                                      |
|---------------------------------------------------|--------------------------------------------------------------|
| Are all side effects inside this process?         | If not, undo is compensation                                 |
| Can another actor observe the intermediate state? | If yes, undo is a lie                                        |
| Is the undo itself auditable?                     | It is a business event, not a mechanism                      |
| Do you need undo, or *versioning*?                | Often the real requirement is “show me the previous version” |
| Do you need undo, or a *draft*?                   | Editing a draft avoids the problem entirely                  |

<a id="be-h752"></a>

#### Chain of Responsibility

**Intent:** pass a request along a chain of handlers until one handles it. The sender does not know which handler will respond, or how many there are.

``` csharp
// you already use this every day: ASP.NET Core middleware IS the pattern
app.Use(async (ctx, next) =>
{
    if (IsBlocked(ctx)) { ctx.Response.StatusCode = 403; return; }  // handle, STOP
    await next(ctx);                                                // or pass along
});

// the general form
public interface IHandler<T>
{
    Task<bool> TryHandleAsync(T request, CancellationToken ct);   // true = handled
}

public sealed class Dispatcher<T>(IEnumerable<IHandler<T>> handlers)
{
    public async Task HandleAsync(T request, CancellationToken ct)
    {
        foreach (var h in handlers)
            if (await h.TryHandleAsync(request, ct)) return;
        throw new UnhandledRequestException(typeof(T));   // ALWAYS have a terminal case
    }
}
```

| Variant        | Behaviour                               | Example                        |
|----------------|-----------------------------------------|--------------------------------|
| **First wins** | Stop at the first handler that responds | Routing, exception mapping     |
| **All run**    | Every handler sees it                   | Validation, notification       |
| **Pipeline**   | Each wraps the next, before and after   | Middleware, MediatR behaviours |
| **Filtered**   | Handlers declare what they accept       | Strategy with a chain          |

> **\[Trap\]**
>
> **Common trap.** A chain with no terminal handler fails silently — the request reaches the end and nothing happens, with no error to investigate. Always have a fallback that either handles the request or throws. The same applies to middleware: a component that neither writes a response nor calls `next` produces a 200 with an empty body, which is one of the more confusing bugs to track down.

###### Middleware, read as the pattern it is

``` csharp
// ASP.NET Core middleware IS Chain of Responsibility, and reading it
// that way explains every behaviour that otherwise seems arbitrary.
app.Use(async (ctx, next) =>
{
    // BEFORE - runs on the way IN, in registration order
    var sw = Stopwatch.StartNew();

    await next(ctx);            // pass to the NEXT link
                                // NOT calling next() STOPS the chain -
                                // this is how short-circuiting works

    // AFTER - runs on the way OUT, in REVERSE order
    log.LogInformation("{Path} took {Ms}ms", ctx.Request.Path, sw.ElapsedMilliseconds);
});
```

``` text
  THE ORDER, and why each position is where it is

    UseExceptionHandler      OUTERMOST - must catch everything below
    UseHsts / UseHttpsRedirection
    UseStaticFiles           short-circuits before routing - a static
                             file never touches auth or routing
    UseRouting               DECIDES which endpoint matches
      |
      |  <- UseCors, UseAuthentication, UseAuthorization MUST be HERE:
      |     after routing (so the endpoint's metadata is known) and
      |     before endpoint execution (so it can refuse).
      |
    UseEndpoints             INNERMOST - actually runs the handler

  the classic bug: UseAuthorization BEFORE UseRouting.

    it runs, finds no endpoint metadata, and therefore finds no
    [Authorize] attribute - so it ALLOWS EVERYTHING.
    no error, no warning. The endpoint is simply unprotected.

  and the other one: a middleware that neither writes a response nor
  calls next() produces a 200 with an empty body. No exception, no log
  line, and a very confusing hour.
```

| Chain variant        | Terminal behaviour                      | Example                         |
|----------------------|-----------------------------------------|---------------------------------|
| **First wins**       | Stop at the first that handles it       | Exception mapping, routing      |
| **All run**          | Every link sees it                      | Validation, notification        |
| **Pipeline**         | Each wraps the next, before *and* after | Middleware, mediator behaviours |
| **Short-circuiting** | Any link may stop the rest              | Auth, rate limiting, caching    |

> **\[Trap\]**
>
> **Common trap.** Every chain needs a terminal case, and the failure without one is silent. In a “first wins” chain, an unhandled request reaches the end and nothing happens — no exception to investigate, no log entry, just a request that did nothing. Always finish with a handler that either handles it or throws.

<a id="be-h753"></a>

#### Observer and Mediator

Two ways to decouple communication, frequently confused because both remove direct references between collaborators. The difference is **where the knowledge of the wiring lives**.

``` text
  OBSERVER - broadcast. The subject does not know who listens.

      Order ---publishes---> OrderPlaced
                                 |
                +----------------+----------------+
                v                v                v
           EmailHandler     StockHandler     AuditHandler

    + the subject knows nothing about the handlers
    + adding a handler requires no change to the subject
    - NOBODY knows the full picture; "what happens when an order is
      placed?" requires searching for every subscriber
    - ordering is unspecified, and failure handling is per-handler

  MEDIATOR - central routing. Colleagues know only the mediator.

      A ----+                    +----> B
            |                    |
            +---> [ Mediator ]---+----> C
            |                    |
      D ----+                    +----> E

    + the flow is in ONE place, readable and testable
    + ordering and failure handling are explicit
    - the mediator becomes a hub, and can grow into a god object
```

``` csharp
// Observer, the domain-event form - the one worth using
public abstract class Entity
{
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> Events => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
}

public class Order : Entity
{
    public void Place()
    {
        Status = OrderStatus.Placed;
        Raise(new OrderPlaced(Id, Total));      // recorded, NOT dispatched
    }
}

// dispatched AFTER the transaction commits - this is the important detail.
// dispatching inside the transaction means a handler failure rolls back
// the business operation, and a handler's side effects happen before the
// data is durable.
public override async Task<int> SaveChangesAsync(CancellationToken ct)
{
    var result = await base.SaveChangesAsync(ct);
    foreach (var e in CollectEvents()) await _dispatcher.PublishAsync(e, ct);
    return result;
}
```

| Use Observer when                         | Use Mediator when                            |
|-------------------------------------------|----------------------------------------------|
| Many independent reactions to one fact    | A defined sequence across several components |
| Subscribers may be added later, elsewhere | The flow must be readable in one place       |
| Order does not matter                     | Order matters                                |
| A failure in one reaction is tolerable    | Failure handling must be coordinated         |
| It is a *notification*                    | It is an *orchestration*                     |

> **\[Trap\]**
>
> **Common trap.** C#'s `event` keyword is Observer, and it is the most common source of managed memory leaks: the delegate holds a reference to the subscriber, so a long-lived publisher keeps every short-lived subscriber alive forever. Unsubscribe explicitly, or avoid events across lifetime boundaries and inject an interface instead.

###### The event handler leak, and how to avoid it entirely

``` text
  THE most common managed memory leak in .NET

    publisher.SomethingHappened += subscriber.OnSomething;

    the DELEGATE holds a reference to `subscriber`.
    the EVENT holds the delegate.
    the PUBLISHER holds the event.

      publisher  --->  delegate  --->  subscriber

    if the publisher is LONG-LIVED (a singleton, a static, a cache) and
    the subscriber is SHORT-LIVED (a page, a scoped service, a form),
    the subscriber CAN NEVER BE COLLECTED. Every one ever created stays
    alive, holding everything it references.

    symptom: memory climbs steadily; a heap dump is full of objects that
             should be gone; gcroot shows the chain back to a static event.
```

``` csharp
// FIX 1 - unsubscribe, and make it structural
public sealed class OrderWatcher : IDisposable
{
    private readonly IOrderPublisher _pub;
    public OrderWatcher(IOrderPublisher pub) { _pub = pub; _pub.Placed += OnPlaced; }
    public void Dispose() => _pub.Placed -= OnPlaced;    // MUST run
}
// note: -= requires the SAME delegate instance. Subscribing with a lambda
// and trying to unsubscribe with an identical-looking lambda does NOTHING:
//   pub.Placed += e => Handle(e);
//   pub.Placed -= e => Handle(e);      // a DIFFERENT delegate. No-op.
// use a method group, or keep a reference to the delegate.

// FIX 2 - do not use events across lifetime boundaries at all.
// inject an interface instead, and let DI manage the lifetime:
public interface IOrderPlacedHandler { Task HandleAsync(OrderPlaced e, CancellationToken ct); }
// the publisher resolves handlers per operation; nothing is retained.

// FIX 3 - domain events collected on the entity, dispatched after commit.
// nothing subscribes to anything; the entity records, the infrastructure
// dispatches, and there is no long-lived reference in either direction.
```

| Mechanism                | Leak risk | Notes                                                    |
|--------------------------|-----------|----------------------------------------------------------|
| C# `event`               | **High**  | Only safe when publisher and subscriber share a lifetime |
| `IObservable<T>` / Rx    | Moderate  | `Subscribe` returns `IDisposable` — dispose it           |
| Weak event pattern       | Low       | Verbose; the subscriber can be collected                 |
| DI-resolved handlers     | **None**  | Nothing is retained between operations                   |
| Domain events + dispatch | **None**  | Recorded on the entity, cleared after commit             |

<a id="be-h754"></a>

#### Iterator, Visitor and Memento

##### Iterator: traverse without exposing the structure

**Intent:** access the elements of a collection sequentially without revealing how it is stored. In C# this is entirely absorbed into the language — `IEnumerable<T>`, `foreach` and `yield return` *are* the pattern.

``` csharp
// yield return generates a full state machine for you
public IEnumerable<Order> LargeOrders(decimal threshold)
{
    foreach (var o in _orders)          // the storage could be anything
        if (o.Total > threshold)
            yield return o;              // LAZY: nothing runs until enumerated
}

// and the async form, for streaming from a database or network
public async IAsyncEnumerable<Order> StreamAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var o in _db.Orders.AsAsyncEnumerable().WithCancellation(ct))
        yield return o;
}
// the caller never learns whether this is a list, a tree, a file or a socket.
```

##### Visitor: adding operations to a closed hierarchy

**Intent:** represent an operation to be performed on the elements of an object structure, so a new operation can be added without modifying the classes. It solves a real problem and it is verbose, which is why pattern matching has largely displaced it in C#.

``` csharp
// the classic Visitor requires a method per type, on both sides
public interface IExpressionVisitor<T>
{
    T VisitLiteral(Literal l);
    T VisitAdd(Add a);
    T VisitMultiply(Multiply m);
}
// adding an operation = one new visitor. Good.
// adding a NODE TYPE = editing every visitor. That is the trade.

// modern C#: a switch expression over a closed hierarchy does the same job
public static decimal Evaluate(Expression e) => e switch
{
    Literal l  => l.Value,
    Add a      => Evaluate(a.Left) + Evaluate(a.Right),
    Multiply m => Evaluate(m.Left) * Evaluate(m.Right),
    // no default: the compiler WARNS if a sealed hierarchy gains a case
};
// same properties, a fraction of the code - provided the hierarchy is
// sealed and in one assembly so exhaustiveness can be checked.
```

##### Memento: capture and restore state

**Intent:** capture an object's internal state so it can be restored later, without violating encapsulation. Undo, snapshots, and optimistic-concurrency rollback.

``` csharp
// a record makes the memento trivial and immutable by construction
public sealed record OrderMemento(OrderStatus Status, decimal Total,
                                  IReadOnlyList<OrderLine> Lines);

public class Order
{
    public OrderMemento Capture() => new(Status, Total, [.. Lines]);  // deep enough
    public void Restore(OrderMemento m) { Status = m.Status; ...; }
}
// note [.. Lines] - a shallow copy of the list would share it, and the
// memento would change as the order changes. The same trap as Prototype.
```

> **\[Note\]**
>
> **Two of these three have dissolved into C#.** Iterator is `IEnumerable<T>`; Memento is a record. Visitor survives only where the hierarchy is genuinely open or spans assemblies — otherwise a switch expression over sealed types gives you the same extensibility with exhaustiveness checking the visitor never had.

###### Deferred execution, and the bugs it creates

``` csharp
// `yield return` builds a state machine. NOTHING runs until enumeration.
public IEnumerable<Order> LargeOrders(decimal threshold)
{
    Console.WriteLine("starting");        // does NOT print here
    foreach (var o in _repository.All())
        if (o.Total > threshold)
            yield return o;
}

var query = LargeOrders(100);             // nothing has happened yet
foreach (var o in query) { }              // NOW it runs, and prints
```

``` text
  THE FOUR DEFERRED-EXECUTION BUGS, all of which compile cleanly

  1  ARGUMENTS ARE NOT VALIDATED WHEN YOU EXPECT

       public IEnumerable<T> Take(IEnumerable<T> src, int n)
       {
           if (n < 0) throw new ArgumentException();   // throws at
                                                        // ENUMERATION,
                                                        // not at CALL
           foreach (...) yield return ...;
       }
       -> the stack trace points at the foreach, not the caller who
          passed -1. Split into a normal method that validates and a
          private iterator that yields.

  2  ENUMERATING TWICE RUNS IT TWICE

       var q = db.Orders.Where(o => o.Total > 100);
       var count = q.Count();               // query 1
       foreach (var o in q) { }             // query 2 - the SAME query
       -> and the results can DIFFER, because the data changed between.
       -> materialise once with ToList() if you need it twice.

  3  THE CONTEXT IS GONE BY THE TIME IT RUNS

       IEnumerable<Order> Get()
       {
           using var db = new AppDbContext();
           return db.Orders.Where(...);      // deferred!
       }                                     // db DISPOSED here
       // the caller enumerates -> ObjectDisposedException
       -> return a materialised list, or make the method async and
          await inside the using.

  4  CLOSURE CAPTURE IN A LOOP

       for (int i = 0; i < 3; i++) queries.Add(items.Where(x => x.Id == i));
       // pre-C# 5 foreach, and still a hazard with `for`: every query
       // captures the SAME i, which is 3 by the time they run.
```

| Return type           | Says to the caller                                               |
|-----------------------|------------------------------------------------------------------|
| `IEnumerable<T>`      | May be lazy. Enumerate once. May throw late                      |
| `IReadOnlyList<T>`    | **Already materialised.** Safe to enumerate repeatedly           |
| `IQueryable<T>`       | Not executed, and composable — but tied to a provider            |
| `IAsyncEnumerable<T>` | Streams; each step may await                                     |
| `T[]`                 | Materialised and mutable — usually the wrong choice for a return |

The guidance that follows: **return `IReadOnlyList<T>` from anything that has already done the work**, and reserve `IEnumerable<T>` for genuinely lazy sequences. The return type is the only signal the caller has about whether enumeration is free, and `IEnumerable` everywhere throws that signal away.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a74"></a>

#### Repository and Unit of Work: the honest debate

`DbContext` *is* a unit of work, and `DbSet<T>` *is* a repository. So wrapping them in your own `IRepository<T>` is either a valuable boundary or pure ceremony, depending on what you actually want from it.

**Arguments for a repository**

- The domain layer takes no dependency on EF Core
- Query logic has one home instead of being scattered through handlers
- You can enforce invariants — tenant filters, soft-delete, aggregate loading
- Swapping to Dapper or a document store touches one class

**Arguments against**

- `IQueryable` leaks EF through the abstraction anyway
- A generic `IRepository<T>` re-implements `DbSet`, worse
- You lose `Include`, projections, split queries and `ExecuteUpdate`
- Nobody has ever actually swapped the database

##### The position most teams land on

Use a repository for **writes**, shaped around aggregates, with intention-revealing method names. Use `DbContext` or Dapper directly for **reads**, projecting to DTOs. That gives you the invariant enforcement where it matters and full query power where you need it.

``` csharp
// write side: aggregate-shaped, no IQueryable escaping
public interface IOrderRepository
{
    Task<Order?> FindAsync(OrderId id, CancellationToken ct);          // loads the WHOLE aggregate
    Task<Order?> FindByExternalRefAsync(string reference, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
}

public sealed class OrderRepository(AppDb db) : IOrderRepository
{
    public Task<Order?> FindAsync(OrderId id, CancellationToken ct) =>
        db.Orders
          .Include(o => o.Lines)              // the aggregate is always loaded complete
          .Include(o => o.Payments)
          .SingleOrDefaultAsync(o => o.Id == id, ct);

    public void Add(Order order) => db.Orders.Add(order);
    public void Remove(Order order) => db.Orders.Remove(order);
}

// read side: no repository at all
public sealed class OrderQueries(ReadDb db)
{
    public Task<OrderListItem[]> RecentAsync(Guid tenantId, int take, CancellationToken ct) =>
        db.Orders.Where(o => o.TenantId == tenantId)
                 .OrderByDescending(o => o.PlacedAt).Take(take)
                 .Select(o => new OrderListItem(o.Id, o.PlacedAt, o.Total, o.Lines.Count))
                 .ToArrayAsync(ct);
}
```

###### Unit of Work, and who owns SaveChanges

The question a repository always raises: if `Add` does not save, who does? Three answers, in increasing order of how well they scale.

| Approach               | How                                                     | Problem                                                                  |
|------------------------|---------------------------------------------------------|--------------------------------------------------------------------------|
| Repository saves       | `AddAsync` calls `SaveChanges`                          | Two repository calls means two transactions — you lost atomicity         |
| Explicit `IUnitOfWork` | Handler calls `uow.SaveChangesAsync()`                  | Works; every handler must remember                                       |
| Pipeline behaviour     | A transaction behaviour saves after the handler returns | Best — handlers cannot forget, and the outbox joins the same transaction |

``` csharp
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }

// AppDb implements it, so nothing extra is constructed
public class AppDb(DbContextOptions<AppDb> o) : DbContext(o), IUnitOfWork;

// and the handler stays clean because the behaviour owns the commit
public async Task<Guid> Handle(PlaceOrder cmd, CancellationToken ct)
{
    var order = Order.Place(cmd.CustomerId, cmd.Lines);
    _repo.Add(order);
    return order.Id;                 // no SaveChanges here - the pipeline does it
}
```

> **\[Trap\]**
>
> **Common trap.** A generic `IRepository<T>` exposing `IQueryable<T> Query()` is the worst of both worlds: the abstraction promises independence from EF Core while handing callers an EF-specific expression tree whose behaviour — lazy loading, client evaluation, tracking — is entirely EF's. If you expose `IQueryable`, be honest and expose `DbContext`.

<a id="be-a78"></a>

#### The anti-corruption layer

When you integrate with a system whose model is wrong for you — a partner API, a legacy database, a vendor SDK — the default outcome is that its concepts leak into your domain and stay there for a decade. An anti-corruption layer is a deliberate translation boundary that prevents this.

``` text
  without an ACL                          with an ACL

  [ your domain ]                         [ your domain ]   speaks YOUR language
     uses CUST_REC_TYP_3                        ^
     handles their null-means-zero              | Customer, Money, Address
     knows their status codes 01-17        [ translator ]   the ONLY place that knows theirs
     copes with their date format               ^
        ^                                       | CUST_REC_TYP_3, status "07"
        |                                  [ partner client ]
  [ partner API ]                          [ partner API ]

  their v2 release changes 40 files        their v2 release changes 1 file
```

``` csharp
// their model, generated from a WSDL or an OpenAPI doc - never leaves this namespace
namespace Integration.PartnerX.Contracts
{
    internal sealed class CUST_REC_TYP_3
    {
        public string? CUST_NM { get; set; }
        public string? CUST_STAT_CD { get; set; }     // "01".."17"
        public string? EFF_DT { get; set; }           // "20260727"
        public decimal CRD_LMT_AMT { get; set; }      // always EUR, undocumented
    }
}

// the translator: the single place that understands their world
internal sealed class PartnerXTranslator
{
    private static readonly FrozenDictionary<string, CustomerStatus> StatusMap =
        new Dictionary<string, CustomerStatus>
        {
            ["01"] = CustomerStatus.Prospect,
            ["07"] = CustomerStatus.Active,
            ["12"] = CustomerStatus.Suspended,
            ["17"] = CustomerStatus.Closed,
        }.ToFrozenDictionary();

    public Customer ToDomain(CUST_REC_TYP_3 src)
    {
        if (!StatusMap.TryGetValue(src.CUST_STAT_CD ?? "", out var status))
            throw new IntegrationException("partnerx.unknown_status",
                $"Unmapped partner status '{src.CUST_STAT_CD}'.");   // fail loudly, do not guess

        return new Customer(
            Name: src.CUST_NM?.Trim() ?? throw new IntegrationException("partnerx.missing_name", "..."),
            Status: status,
            EffectiveFrom: DateOnly.ParseExact(src.EFF_DT!, "yyyyMMdd", CultureInfo.InvariantCulture),
            CreditLimit: Money.Of(src.CRD_LMT_AMT, "EUR"));
    }
}

// what the rest of the application sees
public interface IPartnerDirectory
{
    Task<Customer?> FindAsync(CustomerReference reference, CancellationToken ct);
}
```

###### Rules that keep an ACL from eroding

1.  **Their types are `internal`** to the integration assembly. If a partner DTO can be referenced from the domain project, it eventually will be.
2.  **Translate at the edge, both ways.** Inbound and outbound. A half-ACL that translates responses but sends their DTOs outward is still coupled.
3.  **Fail loudly on the unknown.** An unmapped status code must throw, not default to something plausible. Silent defaulting is how a partner's new status becomes six months of wrong invoices.
4.  **Keep a golden-file test per contract.** Store a real (anonymised) response payload and assert the translation. When they change a field, the test tells you, not production.
5.  **Version the translator, not the domain.** Their v2 gets `PartnerXV2Translator`; the domain model does not acquire a `V2` suffix.

The same pattern applies internally, and this is where it matters most for you: during a strangler-fig migration the new services should speak to the legacy system through an ACL, never by importing its model. Otherwise the new code inherits every legacy assumption and the migration produces a second system with the first system's design.

<a id="be-a79"></a>

#### Adapter, facade and proxy

Three structural patterns that look similar and answer different questions. Distinguishing them is worth the two minutes because the names get used interchangeably and then nobody knows what a class is for.

| Pattern     | Question it answers                                           | Interface                       | Example in .NET                                              |
|-------------|---------------------------------------------------------------|---------------------------------|--------------------------------------------------------------|
| **Adapter** | How do I use this thing through the interface I already have? | Different from the wrapped type | `ILogger` over log4net; `Stream` over a vendor buffer        |
| **Facade**  | How do I hide this subsystem's complexity?                    | New and simpler                 | `WebApplicationBuilder`; a typed client over five HTTP calls |
| **Proxy**   | How do I control access to this thing?                        | The same as the wrapped type    | `Lazy<T>`; a caching decorator; a gRPC client stub           |

``` csharp
// ADAPTER: an old logging library, made to fit the interface the app already uses
public sealed class Log4NetAdapter(log4net.ILog inner) : Microsoft.Extensions.Logging.ILogger
{
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                            Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, ex);
        switch (level)
        {
            case LogLevel.Error or LogLevel.Critical: inner.Error(message, ex); break;
            case LogLevel.Warning:                    inner.Warn(message, ex);  break;
            default:                                  inner.Info(message);      break;
        }
    }
    public bool IsEnabled(LogLevel l) => l switch
    {
        LogLevel.Debug or LogLevel.Trace => inner.IsDebugEnabled,
        LogLevel.Warning                 => inner.IsWarnEnabled,
        _                                => inner.IsInfoEnabled
    };
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
```

``` csharp
// FACADE: four services and a transaction, behind one intention-revealing call
public sealed class CheckoutFacade(
    IBasketService basket, IPricingService pricing,
    IPaymentGateway payments, IOrderRepository orders, IUnitOfWork uow)
{
    public async Task<CheckoutResult> CheckoutAsync(BasketId id, PaymentMethod method,
                                                    CancellationToken ct)
    {
        var contents = await basket.GetAsync(id, ct);
        var quote    = await pricing.QuoteAsync(contents, ct);
        var auth     = await payments.AuthoriseAsync(quote.Total, method, ct);
        if (!auth.Approved) return CheckoutResult.Declined(auth.Reason);

        var order = Order.Place(contents, quote, auth.Reference);
        orders.Add(order);
        await uow.SaveChangesAsync(ct);
        return CheckoutResult.Success(order.Id);
    }
}
```

``` csharp
// PROXY: same interface, controls when and whether the real work happens
public sealed class CachingExchangeRates(IExchangeRates inner, HybridCache cache) : IExchangeRates
{
    public ValueTask<decimal> GetAsync(string from, string to, CancellationToken ct) =>
        cache.GetOrCreateAsync($"fx:{from}:{to}",
            async token => await inner.GetAsync(from, to, token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
            cancellationToken: ct);
}
```

> **\[Note\]**
>
> **A facade is not an anti-corruption layer.** A facade simplifies something you own and whose model you accept. An ACL translates something you do not own and whose model you reject. Calling an ACL a facade makes it sound optional, which is how it ends up half-implemented.

###### Telling them apart, and the anti-corruption layer

``` text
  all three wrap something. The DIFFERENCE is what they change.

  ADAPTER   changes the INTERFACE.
            you have IPaymentGateway; Stripe has a different shape.
            StripeAdapter : IPaymentGateway  -> translates.

  FACADE    changes the COMPLEXITY.
            six subsystems, one method that orchestrates them.
            the interface is NEW and simpler, not a translation.

  PROXY     keeps the SAME interface, adds behaviour.
            caching, lazy loading, logging, access control, remoting.
            the caller cannot tell it is not the real thing.
            -> a DECORATOR is a proxy whose purpose is adding behaviour.
               the distinction is intent, not structure.

  quick test
    same interface, different behaviour       -> proxy / decorator
    different interface, same behaviour       -> adapter
    simpler interface, several collaborators  -> facade
```

``` csharp
// the anti-corruption layer: an adapter with a purpose, at scale.
// it stops a legacy or third-party model leaking into your domain.

public class LegacyCustomerRecord           // what you are given, and cannot change
{
    public string CUST_ID { get; set; }
    public string CUST_NM { get; set; }
    public string STAT_CD { get; set; }      // "A", "I", "P", "X"
    public string CRT_DT  { get; set; }      // "20260807" as a string
}

public sealed record Customer(CustomerId Id, string Name,
                              CustomerStatus Status, DateOnly CreatedOn);

// the ACL - the ONLY place that knows CUST_NM exists, or that "A" means Active
public sealed class LegacyCustomerAdapter(ILegacyClient legacy) : ICustomerSource
{
    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct)
    {
        var r = await legacy.FetchCustomerAsync(id.Value, ct);
        return r is null ? null : new Customer(
            new CustomerId(r.CUST_ID),
            r.CUST_NM.Trim(),
            r.STAT_CD switch { "A" => CustomerStatus.Active,
                               "I" => CustomerStatus.Inactive,
                               "P" => CustomerStatus.Pending,
                               _   => CustomerStatus.Unknown },
            DateOnly.ParseExact(r.CRT_DT, "yyyyMMdd", CultureInfo.InvariantCulture));
    }
}
// when the legacy system is replaced, ONE class changes.
// without the ACL, STAT_CD == "A" is in forty places.
```

The anti-corruption layer is the most valuable of the three in a modernisation context, and it is worth building even for an integration you expect to keep. Its real function is not translation but *containment*: a boundary around a model you do not control, so that model's changes cannot propagate through your codebase.

<a id="be-a80"></a>

#### Strategy, template method and null object

Three behavioural patterns that remove conditionals, in decreasing order of how often you will reach for them.

##### Strategy: the algorithm is chosen, not hardcoded

Already covered structurally under keyed services and factories — the pattern is one interface, many implementations, selection at runtime. Worth naming because it is the single most common way to eliminate a growing `switch`.

##### Template method: the shape is fixed, the steps vary

``` csharp
public abstract class ImportJob<T>
{
    // the algorithm is defined once, and cannot be reordered by a subclass
    public async Task<ImportResult> RunAsync(Stream source, CancellationToken ct)
    {
        var rows   = await ParseAsync(source, ct);          // varies
        var valid  = rows.Where(IsValid).ToList();          // varies
        var errors = rows.Except(valid).Select(Describe);   // varies

        foreach (var batch in valid.Chunk(BatchSize))       // fixed
            await PersistAsync(batch, ct);                  // varies

        await AuditAsync(valid.Count, errors, ct);          // fixed
        return new ImportResult(valid.Count, errors.ToList());
    }

    protected virtual int BatchSize => 1000;
    protected abstract Task<IReadOnlyList<T>> ParseAsync(Stream s, CancellationToken ct);
    protected abstract bool IsValid(T row);
    protected abstract Task PersistAsync(IReadOnlyList<T> batch, CancellationToken ct);
}
```

Template method uses inheritance, which is why modern .NET often prefers the composed alternative: a fixed pipeline class taking `IParser<T>`, `IValidator<T>` and `ISink<T>` as constructor dependencies. Same fixed shape, testable parts, no base-class coupling. Use inheritance only when the variation genuinely is ‘a kind of’ the base.

##### Null object: absence with behaviour

``` csharp
public sealed class NullMetrics : IMetrics
{
    public static readonly NullMetrics Instance = new();
    public void Increment(string name, params KeyValuePair<string, object?>[] tags) { }
    public IDisposable Time(string name) => NoopDisposable.Instance;
}

// no null checks anywhere in the consuming code
services.TryAddSingleton<IMetrics>(NullMetrics.Instance);   // overridden when telemetry is on
```

> **\[Trap\]**
>
> **Common trap.** Null object is right for optional *collaborators* — metrics, notifications, audit sinks. It is wrong for missing *data*. A `NullCustomer` that returns empty strings turns a clear `NullReferenceException` at the point of the bug into a silently blank invoice posted to a customer who does not exist.

###### Strategy via DI, and when template method turns bad

``` csharp
// strategy, resolved by key (.NET 8+) - no factory class, no switch
builder.Services.AddKeyedScoped<IPricingStrategy, StandardPricing>("standard");
builder.Services.AddKeyedScoped<IPricingStrategy, VolumePricing>("volume");
builder.Services.AddKeyedScoped<IPricingStrategy, ContractPricing>("contract");

// or, when the strategy selects itself - often cleaner, and more testable
public interface IPricingStrategy
{
    bool AppliesTo(Customer c);
    Money Calculate(Order o);
}

public class Pricer(IEnumerable<IPricingStrategy> strategies)   // ALL registered
{
    public Money Price(Customer c, Order o) =>
        strategies.First(s => s.AppliesTo(c)).Calculate(o);
}
// registering N implementations of one interface and injecting IEnumerable<T>
// is the single most useful DI feature people do not know about.
```

``` text
  TEMPLATE METHOD - and how it goes wrong

  abstract class ImportJob
  {
      public void Run()          // the template: a fixed skeleton
      {
          var data = Fetch();    // abstract - subclass supplies
          Validate(data);        // virtual  - subclass MAY override
          Save(data);            // abstract
          Notify();              // virtual, default does nothing
      }
  }

  fine with 2-3 subclasses and a genuinely stable skeleton.

  it degrades when
    a subclass needs the steps in a different ORDER
    a subclass needs to skip a step   -> an override that does nothing
    a new step is needed by SOME      -> a virtual no-op for the rest
    the base gains a flag to change its own behaviour
      -> `protected bool SkipValidation` is the smell that it has failed

  the exit: turn the steps into INJECTED COLLABORATORS.
      Run(IFetcher f, IValidator v, ISaver s)
  the skeleton becomes composition, order is caller-controlled, each piece
  is independently testable, and nothing inherits anything.
```

| Null object           | Nullable                        | Prefer                                                   |
|-----------------------|---------------------------------|----------------------------------------------------------|
| `NullLogger.Instance` | `ILogger?` with `?.` everywhere | **Null object** — removes a check at every call site     |
| `NoOpCache`           | `ICache?`                       | Null object, when absence means “do nothing”             |
| `Customer.None`       | `Customer?`                     | **Nullable** — absence is meaningful and must be handled |

The distinction: a null object is right when “nothing” is a valid, complete behaviour the caller need not know about. It is *wrong* when absence is information the caller must act on — a `Customer.None` flowing silently through a pricing calculation produces a plausible wrong answer instead of an obvious error.

### Choosing and Applying

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Choosing and Applying</p>

<p class="pd">Turning the catalogue into a decision. How to recognise which pattern a problem wants, what each one costs, and how to refactor toward one without a rewrite.</p>

<a id="be-h784"></a>

#### Recognising which pattern a problem wants

Patterns are usually taught structure-first, which is why they are hard to apply. Working backwards from the *symptom* is far more useful.

| Symptom in the code                                        | Consider                             |
|------------------------------------------------------------|--------------------------------------|
| A `switch` on type, repeated in several places             | Polymorphism, or Strategy            |
| A `switch` on status, in every method                      | State                                |
| `if (isGroup)` at every call site                          | Composite                            |
| The same wrapper logic before and after many calls         | Decorator, or a pipeline             |
| A constructor with many optional parameters                | Builder, or object initialisers      |
| A third-party type appearing throughout the domain         | Adapter, or an anti-corruption layer |
| Callers orchestrating five collaborators in a fixed order  | Facade                               |
| Several components needing to know when something happened | Observer, or domain events           |
| An action that must be queued, retried, logged or undone   | Command                              |
| Two hierarchies multiplying (3 × 3 = 9 classes)            | Bridge                               |
| Loading a whole object to read one field                   | Proxy (lazy), or a projection        |
| The same validation logic in memory and in a query         | Specification                        |
| “Write the database, then publish”                         | Outbox                               |
| Steps across services with no shared transaction           | Saga                                 |
| Replacing a system that cannot stop running                | Strangler fig                        |

``` text
  and the three questions that resolve most remaining ambiguity

  1  WHO CHOOSES?
       the caller       -> Strategy
       the object       -> State
       a subclass       -> Template Method / Factory Method

  2  WHAT IS BEING CHANGED?
       the interface    -> Adapter (different) / Facade (simpler)
       the behaviour    -> Decorator
       the access       -> Proxy

  3  HOW MANY?
       one, wrapped     -> Decorator / Proxy
       many, as one     -> Composite
       many, in turn    -> Chain of Responsibility
       many, at once    -> Observer
```

###### A worked diagnosis, from symptom to decision

``` text
  THE REPORT, as it arrives

    "adding a new notification channel takes two days and we keep
     breaking SMS when we add something else."

  STEP 1 - look at the code, not the request

    public async Task NotifyAsync(Order o, User u)
    {
        if (u.Prefs.Email) { ...30 lines of SMTP... }
        if (u.Prefs.Sms)   { ...25 lines of a gateway API... }
        if (u.Prefs.Push)  { ...40 lines of a push service... }
        if (u.Prefs.Slack) { ...20 lines... }
    }
    // 130 lines, four dependencies, one method, edited for every change.

  STEP 2 - name the FORCES, before naming a pattern

    a) channels are added regularly           -> extension pressure
    b) each channel has its own dependencies  -> cohesion pressure
    c) a change to one breaks another         -> coupling pressure
    d) all channels should run, not just one  -> "all", not "first wins"
    e) one channel failing must not stop the rest -> isolation
    f) the SET of channels is per user        -> runtime selection

  STEP 3 - map forces to candidates

    a + b + c  -> Strategy: one class per channel, own dependencies
    d          -> NOT Chain of Responsibility. All run, so it is closer
                  to Observer / a broadcast over strategies.
    e          -> each invocation individually guarded
    f          -> filter the strategies by the user's preferences

  STEP 4 - the design that falls out

    public interface INotificationChannel
    {
        ChannelKind Kind { get; }
        Task SendAsync(Notification n, CancellationToken ct);
    }

    public sealed class Notifier(IEnumerable<INotificationChannel> channels,
                                 ILogger<Notifier> log)
    {
        public async Task NotifyAsync(Notification n, UserPrefs prefs,
                                      CancellationToken ct)
        {
            foreach (var c in channels.Where(c => prefs.Wants(c.Kind)))
            {
                try { await c.SendAsync(n, ct); }
                catch (Exception ex) { log.LogError(ex, "{Channel} failed", c.Kind); }
            }
        }
    }

  STEP 5 - state the COST, out loud

    + a new channel is a new file, and nothing existing is edited
    + each channel is independently testable
    + one failure does not stop the others
    - the logic is now in five files
    - "what happens when an order is placed?" needs a search
    - a channel that must run FIRST has nowhere to say so

    the last one is a real limitation. If ordering later matters, this
    design will need a Priority - which is the moment to add it,
    not now.
```

The step people skip is 2. Jumping from the symptom to a pattern name produces plausible-looking designs that answer the wrong pressure — here, choosing Chain of Responsibility because “several handlers” sounds right would have produced a design where only the first matching channel fires, which is exactly the opposite of the requirement.

<a id="be-h785"></a>

#### What each pattern costs

The consequences section of a pattern is the one people skip. Here it is, collected — because knowing the cost is what turns pattern knowledge into judgement.

| Pattern            | Costs you                                                           |
|--------------------|---------------------------------------------------------------------|
| **Strategy**       | A file per variant; the logic is no longer in one place             |
| **Decorator**      | Invisible at the call site; deep stack traces                       |
| **Proxy**          | The caller cannot tell the real thing was not reached               |
| **Adapter**        | A class to maintain, and it must track the external API             |
| **Facade**         | Can grow into a god object if it becomes the only way in            |
| **Composite**      | A Liskov tension: does a leaf expose `Add`?                         |
| **State**          | More classes; transitions must all be enumerated                    |
| **Observer**       | Nobody knows the full flow; memory leaks via events                 |
| **Mediator**       | Navigation is harder; the hub can bloat                             |
| **Command**        | Serialisation, versioning, and a handler per action                 |
| **Repository**     | A layer that can hide the ORM's useful features                     |
| **CQRS**           | Two models to keep coherent; at level 4, visible staleness          |
| **Event sourcing** | Event schema evolution, forever; projections; a smaller talent pool |
| **Saga**           | Compensation logic, timeouts, and stuck-instance operations         |
| **Actors**         | Another runtime to operate; message-flow debugging                  |

``` text
  THE COST NOBODY PUTS IN THE TABLE

    every pattern is a CONCEPT THE NEXT READER MUST ALREADY KNOW.

    a codebase using twelve patterns requires a reader who knows twelve
    patterns to read it fluently. That is fine in a team that does.
    It is a real onboarding cost in a team that does not - and the
    person paying it is rarely the person who chose.

    which is why the most defensible position is usually:
      use FEW patterns, use them CONSISTENTLY, and NAME them in the code.

    ten uses of Decorator, all called *Decorator or *ing<Thing>, is
    far more learnable than one use each of ten different patterns.
```

###### The same feature, with and without patterns, measured

``` csharp
// WITHOUT PATTERNS - 34 lines, one file, one class
public sealed class OrderService(AppDbContext db, IEmailSender email, TimeProvider clock)
{
    public async Task<Guid> PlaceAsync(Guid customerId, List<(Guid, int)> lines,
                                       CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([customerId], ct)
                       ?? throw new InvalidOperationException("No such customer");

        var order = new Order { CustomerId = customerId, PlacedAt = clock.GetUtcNow() };
        foreach (var (productId, qty) in lines)
        {
            var p = await db.Products.FindAsync([productId], ct)!;      // N+1, note
            order.Lines.Add(new OrderLine { ProductId = productId, Quantity = qty,
                                            UnitPrice = p.ListPrice });
        }

        order.Total = order.Lines.Sum(l => l.UnitPrice * l.Quantity);
        if (customer.Tier == "Volume") order.Total *= 0.9m;

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await email.SendAsync(customer.Email, "Order placed", ct);
        return order.Id;
    }
}
```

``` text
  THE SAME FEATURE, FULLY PATTERNED - the file count

    Domain/
      Order.cs                       aggregate root
      OrderLine.cs                   entity
      Money.cs, Quantity.cs          value objects
      OrderId.cs, CustomerId.cs      strongly-typed ids
      IOrderRepository.cs            port
      OrderPlaced.cs                 domain event
    Application/
      PlaceOrder/Command.cs
      PlaceOrder/Handler.cs
      PlaceOrder/Validator.cs
      IPricingEngine.cs
      IUnitOfWork.cs
    Infrastructure/
      EfOrderRepository.cs
      OrderConfiguration.cs          EF mapping
      EfUnitOfWork.cs
      OutboxDispatcher.cs
    Api/
      PlaceOrderEndpoint.cs

    18 FILES. ~340 lines. Same behaviour.

  WHAT THE 306 EXTRA LINES BOUGHT
    + the N+1 is gone (the handler batches the product lookup)
    + pricing is testable and extensible without touching this code
    + the domain is unit-testable with no database - milliseconds
    + Order cannot be constructed in an invalid state
    + the email cannot fail the order (it is an event)
    + a second caller (the message consumer) reuses the handler

  WHAT THEY COST
    - a new developer needs a day to find where anything happens
    - a trivial change touches four projects
    - "add a field" is a 6-file change
    - the team must agree on and maintain the conventions
```

| Feature complexity              | Sensible design                | Why                                          |
|---------------------------------|--------------------------------|----------------------------------------------|
| A CRUD admin screen             | The 34-line version            | There are no invariants to protect           |
| An internal tool, one developer | One project, folders           | Nobody else must reason about it             |
| A core business flow, 3 teams   | The patterned version          | Boundaries make parallel work possible       |
| A flow with regulatory rules    | Patterned, plus event sourcing | History is a requirement                     |
| A prototype                     | The 34-line version            | It may be deleted next month                 |
| A 10-year system                | Patterned                      | The cost amortises; the alternative does not |

> **\[Note\]**
>
> **The question that decides it.** Not “is this good design?” but *“how many people, over how many years, will have to change this?”* One person for six months: the 34-line version is correct, and the patterned one is costume. Three teams for a decade: the indirection buys real optionality, and the 34-line version becomes the thing nobody dares touch.

<a id="be-h786"></a>

#### Refactoring toward a pattern

Patterns are usually reached by *refactoring into*, not by designing up front — which is the whole point of the rule of three. These are the common paths.

``` csharp
// STEP 1: the code as it is. A switch that has grown to five cases.
public decimal Fee(Payment p) => p.Type switch
{
    "card" => p.Amount * 0.029m + 0.30m,
    "transfer" => 0.25m,
    ...
};

// STEP 2: extract each case to a method. Behaviour unchanged, and the
// shape of each variant becomes visible.
private decimal CardFee(Payment p) => p.Amount * 0.029m + 0.30m;

// STEP 3: extract to types implementing a common interface.
public sealed class CardFee : IFeeCalculator { ... }

// STEP 4: replace the switch with resolution.
public decimal Fee(Payment p) => _calculators.First(c => c.Handles(p.Type)).Fee(p);

// each step is small, mechanical, and independently shippable - and you
// can STOP at step 2 or 3 if the benefit has already been realised.
// that optionality is why refactoring beats designing up front.
```

| From                              | Toward                  | First move                                                         |
|-----------------------------------|-------------------------|--------------------------------------------------------------------|
| A repeated `switch`               | Strategy / polymorphism | Extract each case to a method                                      |
| A status `switch` in every method | State                   | Enumerate the legal transitions in a table                         |
| A god object                      | Several services        | Extract by *constructor dependency* — the groups are already there |
| Cross-cutting `if`s               | Decorator / pipeline    | Extract the concern to a wrapper for one call                      |
| A third-party type in the domain  | Anti-corruption layer   | Define your own type; translate at one boundary                    |
| Dual writes                       | Outbox                  | Add the table; write in the same transaction; relay later          |
| A monolith                        | Modular monolith        | Enforce module boundaries with an architecture test                |
| A legacy system                   | Strangler fig           | Put a proxy in front routing 100% to legacy — ship that alone      |

> **\[Note\]**
>
> **The first move is always the seam.** Every refactoring above starts by creating a place where the change can happen — an extracted method, a proxy in front, a new interface with one implementation. Creating the seam is safe, shippable, and reversible, and it converts a large risky change into a series of small ones. Teams that get stuck usually skipped it and tried to do the whole thing at once.

###### A refactor with tests as the safety net, step by step

``` text
  THE PRECONDITION people skip: CHARACTERISATION TESTS.

    before changing structure, write tests that capture what the code
    CURRENTLY DOES - including the behaviour you think is wrong.

      the goal is not correctness. It is DETECTING CHANGE.

    for a legacy method with no tests:
      1  find the seams - what goes in, what comes out, what it touches
      2  write a test that calls it and asserts the CURRENT output,
         whatever that is
      3  if the output is a mess, snapshot it: serialise the result
         and assert it has not changed
      4  now refactor. Any behaviour change breaks a test.

    without this, a refactor is a rewrite with extra confidence.
```

``` csharp
// characterisation test - it does not assert CORRECTNESS, it asserts
// SAMENESS. Ugly, and exactly right for this purpose.
[Theory]
[InlineData("card", 100.00, 3.20)]
[InlineData("card", 0.00, 0.30)]        // is this right? Unknown. It is CURRENT.
[InlineData("transfer", 100.00, 0.25)]
[InlineData("wallet", 100.00, 1.50)]
public void Fee_matches_current_behaviour(string type, decimal amount, decimal expected)
{
    var fee = _calculator.Fee(new Payment(type, amount));
    Assert.Equal(expected, fee);
}

// with those in place, the four-step extraction is safe:
//   1. extract each switch arm to a private method     -> tests pass
//   2. extract each method to a class + interface      -> tests pass
//   3. register them; replace the switch with resolution -> tests pass
//   4. delete the switch                                -> tests pass
// and you can STOP after step 1 or 2 if the benefit is already realised.
```

| Refactoring safety net     | When to use it                                       |
|----------------------------|------------------------------------------------------|
| Unit tests                 | The code is already testable                         |
| **Characterisation tests** | **Legacy code with unknown behaviour**               |
| Snapshot / approval tests  | Complex output that is hard to assert piecemeal      |
| Contract tests             | The refactor crosses a service boundary              |
| Query-count assertions     | The refactor touches data access                     |
| Parallel run               | High-risk: run old and new, compare, log differences |
| Feature flag               | Ship both, switch at runtime, revert in seconds      |

> **\[Note\]**
>
> **The last two rows are underused.** For a genuinely risky refactor — a pricing engine, a calculation with regulatory weight — run the old and new implementations *side by side in production*, return the old result, and log every disagreement. After a week of zero disagreements you have evidence rather than confidence, and switching is a flag change. This costs a day to build and it is the only technique on the list that tests against real production data.

<a id="be-h787"></a>

#### Where to go next

| If you want                    | Read                                                                                             |
|--------------------------------|--------------------------------------------------------------------------------------------------|
| The original catalogue         | Gamma, Helm, Johnson & Vlissides, *Design Patterns* (1994) — dated in places, precise throughout |
| A gentler introduction         | Freeman & Robson, *Head First Design Patterns*                                                   |
| Enterprise-scale patterns      | Fowler, *Patterns of Enterprise Application Architecture*                                        |
| Domain modelling               | Evans, *Domain-Driven Design*; Vernon, *Implementing DDD*                                        |
| Integration and messaging      | Hohpe & Woolf, *Enterprise Integration Patterns*                                                 |
| Refactoring toward patterns    | Fowler, *Refactoring*; Kerievsky, *Refactoring to Patterns*                                      |
| Distributed systems reality    | Kleppmann, *Designing Data-Intensive Applications*                                               |
| Failure patterns in production | Nygard, *Release It!*                                                                            |

A caveat on citations: verify these yourself. I do not have access to a database of publications, so treat titles and authors as a starting point for a search rather than as confirmed references.

- **Read the frameworks you already use.** ASP.NET Core middleware is Chain of Responsibility. EF Core lazy loading is Proxy. `ILogger` is Adapter plus Composite. Recognising them in real code teaches more than any example.
- **Refactor toward a pattern once, deliberately.** Take a switch that has grown to five cases and walk it through the four steps above. Doing it once makes the shape recognisable forever.
- **Argue about the forces, not the names.** “Which pattern is this?” is a much less useful question than “which pressure are we responding to, and is it actually present?”
- **Learn the confusable pairs cold.** Adapter/Facade, Decorator/Proxy, Strategy/State, Mediator/Observer. Distinguishing them *is* most of what knowing patterns means in practice.
- **Notice when you are pattern-matching on structure.** Two patterns with identical structure and different intent are different patterns — which is the clearest evidence that intent is what a pattern actually is.

> **\[Note\]**
>
> **The through-line.** A pattern is a *named trade-off*. The name is worth having because it compresses a design conversation into one word. The trade-off is worth knowing because it tells you when the name does not apply. Everything else — the diagrams, the class structures, the twenty-three-item list — is in service of those two things, and a codebase full of correctly-implemented patterns applied to problems that were not present is worse than one with none.

###### Finding these patterns in code you already have

``` bash
# the highest-return exercise in this guide: go and read the framework
# you already use, looking for the patterns by name.

# 1. CHAIN OF RESPONSIBILITY - ASP.NET Core middleware
#    each link decides whether to call next(). Read your Program.cs
#    top to bottom and name what each one does to the request.

# 2. DECORATOR - IHttpClientFactory's handler chain
#    DelegatingHandler wraps DelegatingHandler wraps the primary handler.
#    AddStandardResilienceHandler adds several at once.
grep -rn "DelegatingHandler" ./src

# 3. PROXY - EF Core lazy loading, and any `virtual` navigation property
grep -rn "UseLazyLoadingProxies" ./src; grep -rn "virtual ICollection" ./src

# 4. STRATEGY - any IEnumerable<T> of one interface injected together
grep -rn "IEnumerable<I" ./src --include=*.cs

# 5. TEMPLATE METHOD - anything you inherit from a framework base class
grep -rnE ": (BackgroundService|ControllerBase|DbContext)" ./src

# 6. OBSERVER - events, and domain event collections
grep -rn "public event" ./src; grep -rn "DomainEvents" ./src

# 7. ANTI-PATTERN HUNT - constructors with too many parameters
grep -rnE "public [A-Z][A-Za-z]+[(](.*,){7,}" ./src
```

``` csharp
// and one architecture test worth adding today, whatever your architecture.
// it costs ten minutes and catches drift for years.
[Fact]
public void No_ambient_time()
{
    var offenders = Types.InAssembly(typeof(Order).Assembly)
        .That().DoNotResideInNamespace("Acme.Infrastructure.Time")
        .Should().NotHaveDependencyOn("System.DateTime")   // approximate; see note
        .GetResult();
    Assert.True(offenders.IsSuccessful);
}
// NetArchTest cannot see static member calls precisely, so in practice a
// Roslyn analyser or a banned-API list is stronger:
//   BannedSymbols.txt:
//     M:System.DateTime.get_Now;Use TimeProvider
//     M:System.DateTime.get_UtcNow;Use TimeProvider
//     M:System.Guid.NewGuid;Inject an id generator for deterministic tests
// plus <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" />
// -> now the BUILD fails, with a message telling the developer what to use.
```

- **Name one pattern per code review.** “This is a decorator” or “this is closer to a proxy, because it can refuse” — the vocabulary only becomes useful once a team shares it.
- **Refactor toward one pattern, deliberately, once.** Take a switch that has grown to five arms and walk it through extract-method, extract-class, register, delete. Doing it once makes the shape recognisable forever.
- **Write the characterisation tests first.** Without them a refactor is a rewrite with extra confidence.
- **Add the banned-API list above.** It is the cheapest structural improvement in this guide, and it removes an entire category of untestable code permanently.
- **Delete one abstraction a quarter.** Codebases accumulate them because adding is visible and removing is not.

A caveat on the book list in the section above: verify those titles and authors yourself. I do not have access to a publication database, so treat them as search starting points rather than confirmed references.
