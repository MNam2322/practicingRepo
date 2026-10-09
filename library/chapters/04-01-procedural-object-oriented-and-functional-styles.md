---
id: "4-1"
number: "4.1"
title: "Procedural, object-oriented and functional styles"
part: 4
part_title: "Designing programs"
words: 4092
minutes: 18
---

# 4.1 Procedural, object-oriented and functional styles

> **Quick look.** Procedural, object-oriented and functional styles, and how good C# mixes them.
>
> - Procedural separates data and procedures; OO bundles them; functional transforms unchanging values.
> - Virtual methods make new types easy to add; records with pattern matching make new operations easy to add.
> - Keep business rules with the data they protect (avoid anemic models with scattered rules).
> - Records for data, classes for things with identity and rules, pure static functions for calculations.
> - Avoid deep inheritance hierarchies and switch statements on type codes.


### The big picture

There is more than one way to organize a program. You can keep data and procedures separate, bundle data with the code that protects it into objects, or build everything from functions that transform values.

C# supports all three styles, and good C# code mixes them. This chapter solves the same small problem three ways so you can see the difference, then explains when each style makes change easy and when it makes change hard. It also covers a common trap in business applications: classes that hold data but none of its rules.

### Before you start

**Builds on:** 2.1 What a programming language is, 2.5 Control flow and functions, 1.5 Memory, 1.4 The processor.

**Ideas explained later:**

- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Mutable state** (4.4): Data that can change after it is created.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Entity** (7.5): A kind of thing the business deals with, usually stored as a table.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **Cache miss** (8.6): A lookup that doesn't find the value, so it must be loaded from the source.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, switch expressions and patterns, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What are encapsulation, inheritance and polymorphism?**

> Encapsulation: an object hides its data and allows changes only through its own methods. Inheritance: a class takes over the members of a base class and can extend them. Polymorphism: code calls a method on a general type and the right version runs for each actual type.

**2. What makes functional programming different from the other two styles?**

> It treats functions as values and avoids changing data. Programs are built by combining functions that take values and return new values, with side effects pushed to the edges.

**3. What is the expression problem?**

> The trade-off between adding new kinds of data and adding new operations. With classes and virtual methods, a new type is easy and a new operation means editing every class. With records and pattern matching, a new operation is easy and a new type means editing every switch.

**4. What is an anemic domain model?**

> Domain classes that are only bags of properties, with all the rules in separate service classes. The objects can be put into invalid states, and the rules are scattered. A rich domain model keeps the rules with the data they protect.

**5. Does calling a method through a base-class reference run the base version or the derived one?**

> The derived one, if the method is `virtual` and the derived class overrides it. That is dynamic dispatch. A non-virtual method, or one hidden with `new`, runs the version for the reference's declared type.

**6. What is data-oriented design?**

> Organizing code around how data is laid out and processed in bulk, usually arrays of plain values, rather than around objects. It is common in games and high-performance code because it suits the cache.

### Three ways to organize a program

What a programming language is (2.1) introduced paradigms: ways of thinking about programs. This chapter looks at the three that shape most real code. **Procedural**, **object-oriented** and **functional** programming answer the same question differently: where do the data and the rules about it live?

C# supports all three, and good C# mixes them. The skill is knowing which fits each part of a program.

### Procedural programming

**Procedural programming** separates data from the code that works on it. Data sits in plain structures. Procedures, also called routines or functions, take data in, change it or compute from it, and return results. The program is a sequence of procedure calls.

C, Pascal and early BASIC work this way. In C#, a static class full of static methods that take and return plain data is procedural.

It is direct and easy to follow for small programs. Each procedure does one visible thing. There is no hidden machinery.

The weakness shows at scale. Any procedure can change any data, so the rules about what makes data valid live nowhere in particular. When a rule changes, you have to find every procedure that touches that data. In a large codebase, that is hard.

### Object-oriented programming

**Object-oriented programming** (OOP) bundles data with the code that works on it. An **object** holds some state and offers operations, called methods, that act on that state. A **class** is the blueprint: it declares the fields and methods, and every object created from it is an instance of it.

Three ideas define the style.

**Encapsulation** hides an object's data behind its methods. Outside code can't change the fields directly; it has to ask the object, and the object checks the request. So the rules about valid data live in one place: the class. A `Booking` can refuse a seat when the flight is full, and nobody can bypass that by editing a list.

**Inheritance** lets a class build on another. A derived class takes over the members of its base class and can add or change behaviour. `BusinessSeat` can be a kind of `Seat`.

**Polymorphism**, meaning "many forms", lets code work with a general type while each object behaves according to its actual type. A method marked `virtual` in a base class can be overridden in derived classes. A call through a base-class reference runs the override for the actual object. This is called dynamic dispatch. Interfaces give the same effect without inheritance, as Abstraction and interfaces (4.2) shows.

OOP became mainstream in the 1990s with C++ and Java, and C# followed. It fits systems with many kinds of things that each have their own state and rules: user interfaces, simulations, business domains.

Its weaknesses are the flip side. Deep inheritance hierarchies become rigid and hard to change. Objects with lots of mutable state are hard to reason about and hard to share between threads. And forcing everything into objects produces classes that exist only to hold one function.

### Functional programming

**Functional programming** builds programs from functions that take values and return new values, without changing anything along the way. Data is usually immutable. Instead of updating a list, you produce a new list. Instead of loops that change a counter, you transform sequences with functions like `Select`, `Where` and `Aggregate`.

Functions are ordinary values. A **first-class function** can be stored in a variable, passed as an argument and returned from another function, as Control flow and functions (2.5) showed with lambdas and delegates. Small functions combine into bigger ones.

Haskell, Elm and F# are built around this style. C# took a great deal from it: lambdas, LINQ, records, `with` expressions, pattern matching and switch expressions.

Functional code is easy to test, because a function's result depends only on its inputs. It is safe to run in parallel, because nothing is shared and changed. And it is easy to reason about, because a value never changes under you. The details are in Immutability and pure functions (4.4).

The weakness is that real programs must change the world: save to a database, send an email, update the screen. Functional programming doesn't avoid that; it pushes those effects to the edges, and that takes some discipline. Very abstract functional code can also be hard for newcomers to read.

### One problem, three ways

Here is the same small task in each style. A booking has seats. Business seats add a 50 lounge fee to the base fare. Members get 10% off the total.

```csharp
// styles.cs — run with: dotnet run styles.cs
using System.Linq;

// 1. Procedural: plain data here, procedures elsewhere
var seatData = new List<(string Cabin, decimal BaseFare)> { ("economy", 120m), ("business", 900m), ("economy", 120m) };
Console.WriteLine(Procedural.Total(seatData, isMember: true));   // 1071.0

// 2. Object-oriented: each object knows its own rules
var booking = new Booking(member: true);
booking.Add(new EconomySeat(120m));
booking.Add(new BusinessSeat(900m));
booking.Add(new EconomySeat(120m));
Console.WriteLine(booking.Total());                              // 1071.0

// 3. Functional: immutable data through pure functions
Seat[] seats = [new Economy(120m), new Business(900m), new Economy(120m)];
Console.WriteLine(Functional.Total(seats, isMember: true));      // 1071.0

static class Procedural
{
    public static decimal Total(List<(string Cabin, decimal BaseFare)> seats, bool isMember)
    {
        decimal total = 0;
        foreach (var (cabin, fare) in seats)
            total += cabin == "business" ? fare + 50m : fare;   // the rule lives in this procedure
        return isMember ? total * 0.9m : total;
    }
}

abstract class SeatBase(decimal baseFare)
{
    protected decimal BaseFare { get; } = baseFare;
    public virtual decimal Price() => BaseFare;
}

sealed class EconomySeat(decimal baseFare) : SeatBase(baseFare);

sealed class BusinessSeat(decimal baseFare) : SeatBase(baseFare)
{
    public override decimal Price() => BaseFare + 50m;         // polymorphism: each seat prices itself
}

sealed class Booking(bool member)
{
    readonly List<SeatBase> seats = [];                         // encapsulated: only Booking changes it
    public void Add(SeatBase seat) => seats.Add(seat);
    public decimal Total()
    {
        decimal sum = seats.Sum(s => s.Price());
        return member ? sum * 0.9m : sum;
    }
}

abstract record Seat(decimal BaseFare);
sealed record Economy(decimal BaseFare) : Seat(BaseFare);
sealed record Business(decimal BaseFare) : Seat(BaseFare);

static class Functional
{
    public static decimal Price(Seat seat) => seat switch
    {
        Business b => b.BaseFare + 50m,
        _ => seat.BaseFare,
    };

    public static decimal Total(IEnumerable<Seat> seats, bool isMember) =>
        ApplyMembership(seats.Sum(s => Price(s)), isMember);

    static decimal ApplyMembership(decimal total, bool isMember) => isMember ? total * 0.9m : total;
}
```

All three print the same answer. The difference shows when things change.

### The expression problem

Imagine two kinds of change. First, a new kind of seat: premium economy, with a 20 fee. Second, a new operation on seats: the refund amount.

In the object-oriented version, a new seat type is easy. Add one class with its own `Price` override, and nothing else changes. A new operation is harder: add a virtual `Refund` method to the base class and override it in every seat class.

In the functional version, it is the other way round. A new operation is easy: write one new function with its own switch. A new seat type is harder: find every switch over `Seat` and add a case to each.

This trade-off is known as the **expression problem**. No single design makes both kinds of change easy. So ask which kind of change is more likely. Many kinds of thing with a stable set of operations, such as plugins or UI controls, suit classes and virtual methods. A fixed set of cases with a growing set of operations, such as flight states, message types or syntax tree nodes, suits records and pattern matching.

### Where the rules live

Business applications often end up with an **anemic domain model**: entity classes that are nothing but public getters and setters, and "service" classes that hold every rule. Martin Fowler named it an anti-pattern in 2003. Any code can set any property, so objects can reach states the business says are impossible, and each rule may be copied into several services.

A **rich domain model** keeps rules with the data they protect. `Booking.Add` checks capacity. `Flight.Depart()` refuses to depart a cancelled flight. Properties that shouldn't change freely have private setters or none at all.

Not everything needs this. Data that is only moved around, such as API request and response types or rows in a report, is fine as plain records. Put the effort where the rules are.

### Data-oriented design

Object-oriented designs often scatter related data across many small heap objects. Memory (1.5) showed why that is slow for bulk processing: every object is a pointer to follow and a likely cache miss.

**Data-oriented design** starts from the data and how it is processed. Keep the values that are processed together in arrays side by side, often as plain structs. Write functions that run over whole arrays. Games use this through entity-component systems, where each component type lives in its own array. Analytics engines and columnstore databases store each column separately for the same reason.

It is a performance technique for hot paths over lots of data, not a general style for business code.

### Mixing styles in C#

Modern C# code usually combines the three.

Use **records and pattern matching** for data: messages, events, API contracts, value objects and closed sets of cases.

Use **classes with encapsulation** for things with identity and rules that change over time, such as a booking or an aircraft's maintenance state, and for services that hold dependencies, such as a repository or an HTTP client wrapper.

Use **static pure functions** for calculations: fares, distances, validation. They are the easiest code to test.

Use **LINQ and pipelines** for transforming collections.

Use **procedural code** freely inside methods. A clear loop is often better than a clever pipeline.

Prefer interfaces and composition to deep inheritance, as Composition and dependency injection (4.3) explains. Keep inheritance shallow, and mark classes `sealed` unless they are designed to be extended.

### In short

Procedural code separates data from procedures; object-oriented code bundles them, using encapsulation, inheritance and polymorphism; functional code transforms unchanging values with functions. Classes with virtual methods make new types easy to add, while records with pattern matching make new operations easy to add. Keep business rules with the data they protect. Use records for data, classes for things with identity and rules, and pure static functions for calculations.

### What goes wrong

**Deep inheritance hierarchies.** Five levels of base classes make every change ripple unpredictably, and nobody knows which override runs. Keep hierarchies shallow, and prefer composition.

**Anemic models with scattered rules.** The same validation is copied into three services and one copy is out of date. Move rules into the types they protect.

**Everything is a class.** Classes named `FareCalculatorHelperManager` that hold one method add ceremony without value. A static function or a record is often enough.

**Switch statements on type codes.** A string or enum "type" checked in many places means every new type requires hunting down every check. Choose deliberately between polymorphism and exhaustive pattern matching.

**Hiding instead of overriding.** A method declared with `new` instead of `override` runs the base version when called through a base reference. The compiler warns about this; don't ignore it.

**Functional code nobody can read.** Chains of twenty LINQ operators with nested lambdas are hard to debug. Break them into named steps.

### Cost model

A virtual call costs a load of the method address plus an indirect jump, a nanosecond or two, and it usually blocks inlining unless the JIT can devirtualize it, as The processor (1.4) described. Sealing classes helps. Immutable functional code allocates new objects instead of changing old ones, which costs garbage collection work; for small records this is cheap, and for large collections structural sharing helps. Data-oriented layouts can make bulk processing several times faster than the same work over scattered objects.

### In other languages (optional)

**JavaScript and TypeScript.** Objects are built on prototypes; `class` syntax, added in 2015, is a friendlier layer over them. Functions are first-class, and much modern frontend code, React in particular, leans functional: components as functions and immutable state.

**Python.** Multi-paradigm, with classes, first-class functions and comprehensions. Duck typing replaces most interfaces.

**Java.** Traditionally strongly object-oriented, and has added lambdas, records, sealed classes and pattern matching, following the same path as C#.

**Go.** Has structs and methods but no inheritance at all. Interfaces are satisfied implicitly, and composition is done by embedding one struct in another.

**F#.** Functional first, with discriminated unions and pattern matching, and full access to .NET classes when needed.

### Key terms

- **Procedural programming**: Organizing a program as procedures that operate on separate, plain data.
- **Object-oriented programming**: Organizing a program as objects that bundle state with the methods that act on it.
- **Functional programming**: Building programs from functions that take values and return new values, avoiding changes to data.
- **Object**: An instance of a class, holding state and offering methods.
- **Class**: A blueprint that declares the fields and methods of its objects.
- **Encapsulation**: Hiding an object's data behind its methods so that its rules can't be bypassed.
- **Inheritance**: A class taking over and extending the members of a base class.
- **Polymorphism**: Code working with a general type while each object behaves according to its actual type.
- **Virtual method**: A method that derived classes can override, chosen at run time by the object's actual type.
- **Dynamic dispatch**: Choosing which method implementation to run based on an object's actual type at run time.
- **First-class function**: A function that can be stored, passed and returned like any other value.
- **Expression problem**: The trade-off between making new data types easy to add and making new operations easy to add.
- **Anemic domain model**: Domain classes that only hold data, with the rules scattered in separate services.
- **Rich domain model**: Domain classes that enforce their own rules on the data they hold.
- **Data-oriented design**: Organizing code around the layout and bulk processing of data, typically arrays of plain values.

### Where this leads

Interfaces and contracts are in Abstraction and interfaces (4.2). Why composition usually beats inheritance is in Composition and dependency injection (4.3). The functional ideas in depth are in Immutability and pure functions (4.4). Principles for keeping any of these styles maintainable are in Design principles that hold up (4.5). Functional components in the browser appear in React today (9.5).

### Check yourself

1. Add a premium economy seat with a 20 fee to all three versions in `styles.cs`. Note which files and methods each version needed you to change.
2. Now add a `Refund` calculation, 80% of the price for economy and 100% for business, to all three versions. Compare again.
3. Give `Booking` a capacity and make `Add` throw when it is full. Explain why the procedural version can't enforce the same rule as reliably.
4. Change `BusinessSeat.Price` from `override` to `new`, call it through a `SeatBase` reference, and explain the output.
5. Find a class in real code that has only properties, and a service that holds its rules. Move one rule into the class.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Fare rules scattered across services

The rule "a cancelled booking can't be changed" is checked in four services, and one of them forgot.

*Keep the rule with the data*

```csharp
public sealed class Booking
{
    public BookingStatus Status { get; private set; }
    public FlightId Flight { get; private set; }

    public void ChangeFlight(FlightId newFlight, DateTimeOffset now)
    {
        if (Status == BookingStatus.Cancelled) throw new DomainException("Cancelled bookings can't be changed");
        if (now > DepartureUtc.AddHours(-2)) throw new DomainException("Too close to departure");
        Flight = newFlight;
    }
    public DateTimeOffset DepartureUtc { get; private set; }
}
```

> **Lesson:** Put rules on the type that owns the data, with private setters, so no caller can skip them.

#### Example 2: A pure fee calculation

Change fees depend on fare class, days before departure and loyalty tier. The calculation had database calls mixed in and was hard to test.

*Pure function, inputs passed in*

```csharp
public static decimal ChangeFee(FareClass fare, int daysBefore, Tier tier) =>
    (fare, daysBefore, tier) switch
    {
        (_, _, Tier.Platinum)          => 0m,
        (FareClass.Flex, _, _)         => 0m,
        (FareClass.Standard, >= 14, _) => 25m,
        (FareClass.Standard, _, _)     => 60m,
        (FareClass.Basic, _, _)        => 120m,
    };
// [Theory, InlineData(...)] tests every row in milliseconds, no database needed.
```

> **Lesson:** Functional style suits calculations: pass everything in, return a value, and the logic becomes trivially testable.

### What Modern C# Changed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · What Modern C# Changed</p>

<p class="pd">Several GoF patterns exist to work around limitations C++ and Smalltalk had. Modern C# removed some of those limitations — here is what changed, and what did not.</p>

<a id="be-h757"></a>

#### What lambdas and first-class functions changed

Several GoF patterns are, structurally, “an interface with one method”. In a language where a function is a value, that interface is often unnecessary ceremony.

``` csharp
// Strategy, as an interface
public interface IDiscountStrategy { decimal Apply(decimal total); }
public class TenPercentOff : IDiscountStrategy
{ public decimal Apply(decimal t) => t * 0.9m; }

// Strategy, as a function
Func<decimal, decimal> discount = t => t * 0.9m;

// Command, as a function
Action undo = () => order.Status = previous;

// Template Method, as injected steps
async Task<Result> RunAsync(
    Func<CancellationToken, Task<Raw>> fetch,
    Func<Raw, Validated> validate,
    Func<Validated, CancellationToken, Task<Result>> save,
    CancellationToken ct)
{
    var raw = await fetch(ct);
    return await save(validate(raw), ct);
}
// no base class, no subclasses, any combination composed at the call site.
```

| Prefer an interface when                  | Prefer a delegate when          |
|-------------------------------------------|---------------------------------|
| It needs a **name** that documents intent | The behaviour is obvious inline |
| It has dependencies to inject             | It is a pure transformation     |
| It has more than one method               | One operation only              |
| It must be discovered by DI               | The caller supplies it directly |
| It carries state between calls            | It is stateless                 |
| Implementations are tested separately     | It is trivial                   |

The pragmatic rule: **a delegate for the small and local, an interface for the named and injected**. A codebase where every strategy is a lambda becomes hard to navigate, because there is nothing to search for; one where every one-line transformation is an interface with one implementation is ceremony. The line is roughly whether you would want to find all implementations.

###### Closure capture, and the allocation nobody sees

``` csharp
// a lambda that captures a variable ALLOCATES. The compiler generates a
// class to hold the captured state.

public void Process(List<Order> orders, decimal threshold)
{
    // captures `threshold` -> a display class is allocated PER CALL
    var large = orders.Where(o => o.Total > threshold).ToList();
}

// captures NOTHING -> the delegate is cached in a static field,
// allocated once for the lifetime of the process
var positive = orders.Where(o => o.Total > 0).ToList();

// and the fix when it matters: pass state explicitly
var large = orders.Where(static o => o.Total > 0).ToList();
//                       ^^^^^^ `static` lambda: the COMPILER ENFORCES
//                       that nothing is captured. If you accidentally
//                       capture, it is a build error.
```

``` text
  THE EVENT-HANDLER LEAK, which is closure capture plus lifetime

    publisher.Changed += o => _cache.Invalidate(o.Id);

    the lambda captures `this` (because it uses _cache).
    the delegate holds the lambda. The event holds the delegate.
    the PUBLISHER holds the event.

      long-lived publisher ---> delegate ---> THIS OBJECT, forever

    and you CANNOT unsubscribe:

      publisher.Changed -= o => _cache.Invalidate(o.Id);   // NO-OP

    a lambda expression creates a NEW delegate each time it is written.
    the -= compares delegates and finds no match. Nothing happens, and
    nothing warns you.

    -> subscribe with a METHOD GROUP, or keep the delegate in a field:

         private readonly EventHandler<Order> _handler;
         _handler = OnChanged;
         publisher.Changed += _handler;
         ...
         publisher.Changed -= _handler;      // this one works
```

| Delegate                           | Interface                          | Choose by                        |
|------------------------------------|------------------------------------|----------------------------------|
| No name to search for              | Findable by “find implementations” | Will you need to enumerate them? |
| Cannot take DI dependencies easily | Constructor injection              | Does it need collaborators?      |
| One operation                      | Several related operations         | How many methods?                |
| Allocation per capture             | One allocation, reused             | Hot path?                        |
| Inline at the call site            | A separate, testable class         | Is it worth a test of its own?   |

A practical convention: **delegates for the small, local and pure; interfaces for the named, injected and tested**. The deciding question is usually whether you will ever want to answer “what are all the implementations of this?” — a question you cannot ask of a lambda.
