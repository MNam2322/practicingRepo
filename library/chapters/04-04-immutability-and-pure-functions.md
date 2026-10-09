---
id: "4-4"
number: "4.4"
title: "Immutability and pure functions"
part: 4
part_title: "Designing programs"
words: 5407
minutes: 24
---

# 4.4 Immutability and pure functions

> **Quick look.** Keep change under control with pure functions and immutable data.
>
> - A pure function's result depends only on its inputs and it has no side effects.
> - Pass time and other hidden inputs in, and logic becomes pure and testable.
> - Immutable data is safe to share; changes create new versions cheaply through structural sharing.
> - Records give shallow value equality: a record holding a List compares that list by reference.
> - Value objects validate themselves on creation; publish read-mostly shared data as immutable snapshots.
> - Keep decisions in a pure core and input/output in a thin outer shell.


### The big picture

Most bugs involve something that changed when you didn't expect it: a list reordered by a helper, a setting changed by another request, a value updated by two threads at once. This chapter is about two tools for keeping change under control.

Pure functions compute their results only from their inputs, with no hidden effects, which makes them trivial to test and safe to run anywhere. Immutable data can't change once created, which makes it safe to share. You will see how to apply both in ordinary C#, and how to structure a program so the tricky decisions stay pure while a thin outer layer does the input and output.

### Before you start

**Builds on:** 2.4 Values, variables and references, 3.8 Recursion and dynamic programming, 1.3 Text is numbers too, 3.2 Arrays and lists, 3.3 Hash tables, 2.3 Types.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Git** (11.1): The distributed version control system used almost everywhere, created in 2005.
- **Business logic** (8.1): The rules and calculations that are actually about the business, separate from HTTP and storage.
- **A record** (6.4): A DNS record giving an IPv4 address.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, records, collection expressions, generic types, lambdas, LINQ, switch expressions and patterns, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What makes a function pure?**

> Its result depends only on its arguments, and it has no side effects: it doesn't change anything outside itself, read the clock, do I/O or use randomness. Called twice with the same arguments, it gives the same answer.

**2. Is a method that reads DateTime.UtcNow pure?**

> No. Its result depends on when it runs, which is hidden input. Pass the time in as a parameter, or inject a `TimeProvider`, and the rest of the logic becomes pure and testable.

**3. How does ImmutableList<T>.Add avoid copying the whole list?**

> Structural sharing. The list is a balanced tree, and the new version creates only the few nodes on the path to the change. Everything else is shared with the old version, which stays unchanged.

**4. What is a value object?**

> A small immutable type defined entirely by its values, with no identity of its own, such as an airport code, a money amount or a date range. Two with the same values are equal, and invalid values are rejected when one is created.

**5. What does "functional core, imperative shell" mean?**

> Put the decisions in pure functions that take data and return data, such as a list of actions to take. Keep a thin outer layer that reads inputs, calls the core and carries out the actions. The core is easy to test; the shell is too simple to need much testing.

**6. Why are immutable objects automatically thread-safe to read?**

> Races need at least one thread writing while another reads. If nothing can change after construction, every thread sees the same, complete value, with no locks.

**7. What is the command-query separation principle?**

> A method should either answer a question and change nothing (a query) or change something and return nothing (a command), not both. Callers can then call queries freely without worrying about effects.

### The trouble with change

Most bugs in real programs involve state that changed when someone didn't expect it to. A list was sorted by a method that only seemed to read it. A setting was changed by one request while another was using it. A counter was updated by two threads at once. An object was modified after being put into a dictionary, as Values, variables and references (2.4) showed.

**Mutable state** is data that can change after it is created. It isn't bad in itself; programs exist to change things. The problem is shared mutable state that changes where you can't see it. Every piece of code that can change a value is a piece of code you must read to understand it.

This chapter is about two tools for controlling change: immutable data and pure functions.

### Side effects

A **side effect** is anything a function does besides return its result. Changing a field, a static variable or an argument it was given. Writing to a file, a database, the network or the console. Reading the clock or a random number generator, which is hidden input rather than output, but has the same effect on predictability. Throwing an exception is often counted too.

Side effects are how programs do useful work. But a function with hidden side effects can't be understood from its signature, can't be safely called twice, and can't be tested without setting up the world around it.

### Pure functions

A **pure function** has no side effects, and its result depends only on its arguments. `Math.Max(a, b)` is pure. A fare calculation that takes a route, a date and a rule table and returns a price is pure. A method that sorts the list it was given is not, and neither is one that reads `DateTime.UtcNow`.

Pure functions have a useful property called **referential transparency**: any call can be replaced by its result without changing the program. `Price(route, date)` always means the same number for the same inputs.

That gives several practical benefits. Tests are simple: pass inputs, check outputs, with no setup and no mocks. Results can be cached, as memoization in Recursion and dynamic programming (3.8) did. Calls can run in parallel, because they share nothing. And bugs can be reproduced exactly from the inputs.

Making code pure is often just a matter of passing in what it used to fetch. Instead of reading the clock, take `now` as a parameter. Instead of loading the fare table, take it as an argument. The function that loads things and the function that decides things become separate.

```csharp
// pure.cs — run with: dotnet run pure.cs
using System.Collections.Immutable;
using System.Linq;

// 1. A hidden side effect: this "read-only" helper reorders the caller's list
var departures = new List<string> { "SQ321", "BA117", "QF1" };
Console.WriteLine(FirstAlphabeticalImpure(departures));   // BA117
Console.WriteLine(string.Join(",", departures));          // BA117,QF1,SQ321: the caller's list changed
var untouched = new List<string> { "SQ321", "BA117", "QF1" };
Console.WriteLine(FirstAlphabetical(untouched));          // BA117
Console.WriteLine(string.Join(",", untouched));           // SQ321,BA117,QF1: unchanged

// 2. Hidden input: pass the time in, and the rule becomes pure and testable
var t0 = new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.Zero);
var ba117 = new Flight("BA117", Departure: t0.AddMinutes(30), GateOpen: false);
Console.WriteLine(BoardingDue(ba117, now: t0));            // True: 30 minutes to go
Console.WriteLine(BoardingDue(ba117, now: t0.AddMinutes(-60)));  // False

// 3. A value object rejects bad values at the door
var lhr = new AirportCode("LHR");
Console.WriteLine(lhr == new AirportCode("LHR"));          // True: equal by value
try { _ = new AirportCode("London"); }
catch (ArgumentException ex) { Console.WriteLine(ex.Message.Split(" (")[0]); }   // 'London' is not a three-letter airport code.

// 4. Structural sharing: new versions without disturbing old ones
var v1 = ImmutableList.Create("LHR", "JFK");
var v2 = v1.Add("SIN");
Console.WriteLine($"{v1.Count} {v2.Count}");               // 2 3: v1 is unchanged

// 5. Functional core, imperative shell
Flight[] board = [ba117, new Flight("QF1", t0.AddMinutes(90), false), new Flight("SQ321", t0.AddMinutes(20), true)];
IReadOnlyList<Command> plan = Plan(board, now: t0);        // pure: decides what should happen
foreach (Command command in plan) Execute(command);        // impure: makes it happen
// open gate for BA117
// announce: BA117 now boarding

static string FirstAlphabeticalImpure(List<string> items)
{
    items.Sort(StringComparer.Ordinal);                    // side effect on the caller's data
    return items[0];
}

static string FirstAlphabetical(IReadOnlyList<string> items) => items.Min(StringComparer.Ordinal)!;

static bool BoardingDue(Flight f, DateTimeOffset now) => !f.GateOpen && f.Departure - now <= TimeSpan.FromMinutes(40);

static IReadOnlyList<Command> Plan(IEnumerable<Flight> flights, DateTimeOffset now) =>
    flights.Where(f => BoardingDue(f, now))
           .SelectMany(f => new Command[] { new OpenGate(f.Number), new Announce($"{f.Number} now boarding") })
           .ToList();

static void Execute(Command command)
{
    switch (command)                                       // the only place with side effects
    {
        case OpenGate g: Console.WriteLine($"open gate for {g.Flight}"); break;
        case Announce a: Console.WriteLine($"announce: {a.Message}"); break;
    }
}

record Flight(string Number, DateTimeOffset Departure, bool GateOpen);

abstract record Command;
sealed record OpenGate(string Flight) : Command;
sealed record Announce(string Message) : Command;

readonly record struct AirportCode
{
    public string Value { get; }
    public AirportCode(string value)
    {
        if (value is not { Length: 3 } || !value.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"'{value}' is not a three-letter airport code.", nameof(value));
        Value = value;
    }
    public override string ToString() => Value;
}
```

### Immutable data

An object that can't change after it is created is immutable, as Text is numbers too (1.3) said of strings. C# gives you several tools: `readonly` fields, `init` properties, records with `with` expressions, `readonly struct` and the immutable and frozen collections from Arrays and lists (3.2) and Hash tables (3.3).

Immutable data removes whole categories of bugs. It can be shared between threads with no locks, because no thread can write. It can be handed out without defensive copies. It can be used as a dictionary key safely. And a value you checked at one moment is still valid at the next.

"Changing" immutable data means making a new version. `booking with { Gate = "B12" }` gives a new booking; anyone holding the old one still sees the old gate. That is often exactly what you want: the old version is a **snapshot** of how things were, which is useful for auditing, undo, comparison and concurrency.

### Persistent data structures

Copying a whole collection for every change would be slow. Immutable collections avoid it with **structural sharing**. `ImmutableList<T>` is a balanced tree. Adding an item creates new nodes only along the path from the root to the change, about log₂ *n* of them. Every other node is shared by the old and new versions. Both versions stay fully usable.

A collection that keeps its old versions intact after changes like this is called a **persistent data structure**. "Persistent" here means old versions persist, not that anything is saved to disk. Functional languages are built on them, and so is Git: each commit shares every unchanged file with the commit before it.

The opposite approach, **copy-on-write**, shares data until the first change and then copies all of it. `ImmutableArray<T>` works this way. Reading is as fast as an array; each change costs a full copy.

### Value objects

A **value object** is a small immutable type defined entirely by its values: an airport code, a money amount with its currency, a date range, a flight number. Two value objects with the same values are interchangeable. They have no identity of their own, unlike a booking or an aircraft, which remain themselves even when their details change.

Value objects are where validation belongs. `AirportCode` checks its format in its constructor, so every `AirportCode` in the program is valid, and methods that take one don't need to check again. That is the strongly typed ID idea from Types (2.3) with rules attached.

One C# detail matters here. A struct always has a default value that skips the constructor. `default(AirportCode)` has a null `Value`, and so do the elements of a new `AirportCode[]`. Either make the type a `sealed record` class, where the risk is null instead, or treat the default as "no value" explicitly.

### Command-query separation

Bertrand Meyer proposed a simple rule for methods. A **query** answers a question and changes nothing. A **command** changes something and returns nothing. A method should be one or the other. This is **command-query separation**.

It lets callers trust queries: calling `IsFull()` twice, in a log statement or a debugger, can't change the program. Commands are then the only places to look when something changes. The rule is a guideline, not a law. `Stack.Pop()` both changes the stack and returns the item, and that's the practical choice. But a "getter" that quietly updates state is a trap.

### Functional core, imperative shell

Real programs have to read databases, call services and send messages. Gary Bernhardt described a structure that keeps those effects contained: a **functional core** surrounded by an **imperative shell**.

The core holds the decisions, written as pure functions. It takes plain data in and returns plain data out, often a list of things to do, as `Plan` does above. The shell does the I/O. It loads the data, calls the core and carries out the returned commands, as `Execute` does.

Most of the logic, and most of the bugs, live in the core, and the core is trivial to test. The shell is thin, with few decisions, and is tested with a few integration tests. The same idea appears under other names: "pure business logic with repositories at the edges", or the ports-and-adapters architecture covered in Monoliths, modules and services (12.1).

### Temporal coupling

Mutable objects often have to be used in a particular order. Call `Initialize` before `Load`. Set `Connection` before calling `Query`. Nothing enforces the order; the code just breaks if you get it wrong. This is **temporal coupling**.

Immutability usually removes it. If an object needs a connection, take it in the constructor, and every instance is ready to use. If an operation has stages, return a new object from each stage that only offers the next steps.

### Immutability and threads

Shared mutable data between threads needs locks, as Threads and locks (5.2) explains. Immutable data doesn't. A common pattern for shared settings or caches is to keep an immutable snapshot in a field. Every reader uses whatever snapshot is current. A writer builds a complete new snapshot and swaps the reference in one step. Readers never see a half-updated state. When several writers may race, `ImmutableInterlocked.Update` retries the swap safely.

### In short

A side effect is anything a function does beyond returning its result, and a pure function has none. Pass the time and other hidden inputs in, and logic becomes pure and testable. Immutable data is safe to share and hand out; changing it means creating a new version, which structural sharing makes cheap. Value objects validate themselves when created. Keep decisions in a pure core and input and output in a thin shell around it.

### What goes wrong

**Hidden mutation of arguments.** A helper sorts, trims or clears a list it was passed, and the caller's data changes. Take read-only types such as `IReadOnlyList<T>`, and return new collections.

**Hidden inputs.** Logic that reads `DateTime.Now`, `Random.Shared`, environment variables or static settings deep inside can't be tested reliably. Pass them in, or inject `TimeProvider` and similar abstractions.

**Queries with side effects.** A property getter that loads data, updates a counter or logs changes behaviour when inspected in the debugger. Keep queries free of effects.

**Default structs that skip validation.** `default(AirportCode)` or a new array of value objects holds invalid values the constructor would have rejected. Handle the default case, or use a class.

**"Immutable" records with mutable insides.** A record holding a `List<T>` can still change through that list, and `with` shares it. Use immutable collections inside records.

**Copying large structures on every change.** `ImmutableArray<T>.Add` or record `with` in a tight loop over big data allocates heavily. Use a builder, or a local mutable structure that becomes immutable at the end.

### Cost model

A `with` expression on a small record costs one allocation, tens of nanoseconds. `ImmutableList<T>` operations are O(log n) and several times slower than `List<T>` for reads, because they walk a tree of separate nodes. `ImmutableArray<T>` reads as fast as an array, and each change is O(n). Frozen collections are fastest to read of all. Pure functions themselves cost nothing extra; the gains come from simpler tests, safe caching and safe parallelism. Most code can afford immutability everywhere except inner loops over large data.

### In other languages (optional)

**JavaScript and TypeScript.** `const` prevents reassignment but not mutation. `Object.freeze` freezes one level deep. TypeScript's `readonly` modifier and `ReadonlyArray<T>` enforce immutability at compile time. Spread syntax, `{ ...booking, gate: "B12" }`, creates updated copies, and state management in React relies on it.

**Python.** Tuples and `frozenset` are immutable. `@dataclass(frozen=True)` creates immutable data classes.

**Java.** Records are shallowly immutable. `List.of` creates unmodifiable lists.

**F#, Haskell, Clojure and Elm.** Immutable by default, with persistent collections built in. Mutation has to be asked for explicitly.

**Rust.** Variables are immutable unless declared `mut`, and the compiler prevents data being changed while it is shared.

### Key terms

- **Mutable state**: Data that can change after it is created.
- **Side effect**: Anything a function does besides return a result, such as changing data or doing I/O.
- **Pure function**: A function whose result depends only on its arguments and which has no side effects.
- **Referential transparency**: The property that a call can be replaced by its result without changing the program.
- **Snapshot**: An unchanging copy of state as it was at one moment.
- **Structural sharing**: Letting a new version of a data structure reuse the unchanged parts of the old one.
- **Persistent data structure**: A data structure whose old versions remain intact and usable after changes.
- **Copy-on-write**: Sharing data until the first change, then copying it all.
- **Value object**: A small immutable type defined by its values, with no identity, that validates itself.
- **Command-query separation**: The rule that a method should either change state or return information, not both.
- **Functional core**: The pure part of a program that makes the decisions, taking and returning plain data.
- **Imperative shell**: The thin outer layer that does I/O and carries out the core's decisions.
- **Temporal coupling**: A hidden requirement that methods be called in a particular order.

### Where this leads

Design principles that build on these ideas are in Design principles that hold up (4.5). Testing a functional core is in Testing (4.6). Why shared mutable state is the root of concurrency bugs is in Why concurrency is hard (5.1). Immutable state in user interfaces is in Components and state (9.4). Events as immutable facts come back in Messaging and events (12.3).

### Check yourself

1. Add a rule to `Plan` in `pure.cs`: a flight departing within 10 minutes with its gate still closed gets an `Escalate` command. Test it by calling `Plan` with chosen times, without running `Execute`.
2. Rewrite `BoardingDue` to take a `TimeProvider` instead of `now`. Write a small fake `TimeProvider` that returns a fixed time.
3. Create a `Money` value object with an amount and a currency code. Make adding two amounts in different currencies throw.
4. Take a class from real code that reads `DateTime.Now`. Refactor it so the time is passed in or injected.
5. Measure the time to add 100,000 items to a `List<int>`, an `ImmutableList<int>` and an `ImmutableList<int>.Builder`. Explain the differences.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Pricing logic that depended on the clock

"Early bird" discount tests fail every evening because the code reads `DateTime.UtcNow` internally.

*Pass time in*

```csharp
public static decimal Price(decimal baseFare, DateTimeOffset bookedAt, DateTimeOffset departs) =>
    (departs - bookedAt).TotalDays >= 60 ? baseFare * 0.85m : baseFare;

// shell: var price = Pricing.Price(fare, clock.GetUtcNow(), flight.DepartureUtc);
// test:  Pricing.Price(100m, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)) == 85m
```

> **Lesson:** Hidden inputs make code impure and flaky to test. Pass time, randomness and configuration in as arguments.

#### Example 2: A record with a list inside that compared unequal

Two identical itinerary records aren't equal, so de-duplication fails.

*Use immutable collections with value semantics*

```csharp
// Before: record Itinerary(string Pnr, List<string> Legs);  -> List compares by reference
public sealed record Itinerary(string Pnr, ImmutableArray<string> Legs)
{
    public bool Equals(Itinerary? other) => other is not null && Pnr == other.Pnr && Legs.SequenceEqual(other.Legs);
    public override int GetHashCode() => HashCode.Combine(Pnr, Legs.Length);
}
```

> **Lesson:** Record equality is shallow. Collections inside records need immutable types and explicit equality.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a16"></a>

#### Records, equality, and immutability

By default, classes compare by reference and structs compare field-by-field via reflection (slow). Records fix both: `record` gives you value equality, a readable `ToString()`, deconstruction and non-destructive mutation via `with`.

``` csharp
public record Money(decimal Amount, string Currency);

var a = new Money(10m, "EUR");
var b = new Money(10m, "EUR");
Console.WriteLine(a == b);              // True  (value equality)

var c = a with { Amount = 20m };        // new instance, Currency copied

// record struct: value semantics + fast generated equality
public readonly record struct Coord(int X, int Y);
```

Use records for DTOs, messages, domain value objects and query results — anything defined by its data rather than its identity. Keep `class` for entities with an identity that survives field changes (a `Customer` is still the same customer after a rename).

##### The decision: record, class or struct?

| Shape                                      | Use                        | Because                                                      |
|--------------------------------------------|----------------------------|--------------------------------------------------------------|
| DTO, API contract, message                 | `record`                   | Value equality and immutability are exactly what you want    |
| Domain value object (`Money`, `DateRange`) | `readonly record struct`   | Small, immutable, compared by value, no allocation           |
| Domain entity with an id                   | `class`                    | Identity survives field changes; EF Core tracks it           |
| A dictionary or set key                    | `record` / `record struct` | Correct `Equals` and `GetHashCode`, generated, and immutable |
| Anything mutated in place                  | `class`                    | `with` copies — mutating a record fights the design          |
| A configuration or options object          | `class`                    | The binder needs settable properties                         |

##### How you would recognise the bug

| Symptom                                       | Cause                                                                          |
|-----------------------------------------------|--------------------------------------------------------------------------------|
| Two records that look identical are not equal | A reference-type member — a `List` or an array — compared by reference         |
| A `HashSet` of records contains duplicates    | The same cause, via `GetHashCode`                                              |
| A derived record never equals its base        | The generated `EqualityContract` check — and this is usually what you want     |
| EF Core does not detect a change              | You replaced the tracked instance with `with` instead of mutating it           |
| A record deserialises with nulls              | No `required` and no constructor binding, so the deserialiser was free to skip |

> **\[Trap\]**
>
> **Common trap.** Record equality is shallow for reference-type members. A record holding a `List<T>` compares the list *reference*, so two records with equal-looking lists are not equal.

###### How it actually works

The compiler generates a lot for a record. Knowing what, exactly, removes most of the surprises.

``` csharp
public record Money(decimal Amount, string Currency);

// the compiler emits roughly:
public class Money : IEquatable<Money>
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }

    protected virtual Type EqualityContract => typeof(Money);   // <- the type check

    public virtual bool Equals(Money? other) =>
        other is not null
        && EqualityContract == other.EqualityContract           // subclass-safe
        && EqualityComparer<decimal>.Default.Equals(Amount, other.Amount)
        && EqualityComparer<string>.Default.Equals(Currency, other.Currency);

    public override int GetHashCode() =>
        (EqualityComparer<Type>.Default.GetHashCode(EqualityContract) * -1521134295
         + EqualityComparer<decimal>.Default.GetHashCode(Amount)) * -1521134295
         + EqualityComparer<string>.Default.GetHashCode(Currency);

    protected Money(Money original) { Amount = original.Amount; Currency = original.Currency; }
    public virtual Money <Clone>$() => new Money(this);         // this is what 'with' calls

    public override string ToString() => $"Money {{ Amount = {Amount}, Currency = {Currency} }}";
    public void Deconstruct(out decimal amount, out string currency) => ...
}
```

The `EqualityContract` check is why a `DiscountedMoney` deriving from `Money` is never equal to a plain `Money`, even with identical values — which is almost always what you want and is a genuine improvement over hand-written `Equals`, where this is the classic bug.

###### Worked example

A value object done properly, using a record struct with validation and a private constructor:

``` csharp
public readonly record struct Money : IComparable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency) => (Amount, Currency) = (amount, currency);

    public static Money Of(decimal amount, string currency)
    {
        if (currency is not { Length: 3 })
            throw new ArgumentException("ISO 4217 code required.", nameof(currency));
        return new Money(decimal.Round(amount, 2, MidpointRounding.ToEven),
                         currency.ToUpperInvariant());
    }

    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency
            ? new Money(a.Amount + b.Amount, a.Currency)
            : throw new InvalidOperationException($"Cannot add {a.Currency} to {b.Currency}.");

    public int CompareTo(Money other) =>
        Currency == other.Currency ? Amount.CompareTo(other.Amount)
                                   : throw new InvalidOperationException("Different currencies.");

    public override string ToString() => $"{Amount:0.00} {Currency}";
}

var total = Money.Of(19.99m, "eur") + Money.Of(5.00m, "EUR");   // 24.99 EUR
var bad   = Money.Of(10m, "EUR") + Money.Of(10m, "USD");        // throws, loudly, at the right place
```

Compare that with passing `decimal` around. The currency mismatch bug becomes structurally impossible to write, rounding happens in exactly one place, and every method signature that takes a `Money` documents its own units. Two hundred lines of defensive checks scattered through a codebase collapse into one type.

### What Modern C# Changed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · What Modern C# Changed</p>

<p class="pd">Several GoF patterns exist to work around limitations C++ and Smalltalk had. Modern C# removed some of those limitations — here is what changed, and what did not.</p>

<a id="be-h756"></a>

#### What records and immutability changed

| Pattern                    | Before                                       | Now                                                  |
|----------------------------|----------------------------------------------|------------------------------------------------------|
| **Prototype**              | `ICloneable`, a hand-written `Clone()`       | `with` expressions                                   |
| **Memento**                | A snapshot class per type, kept in step      | A record, immutable by construction                  |
| **Value Object** (DDD)     | Override `Equals`, `GetHashCode`, `==`, `!=` | `record` or `readonly record struct`                 |
| **Builder** (simple cases) | A builder class per type                     | Object initialisers + `required`                     |
| **Null Object**            | A no-op implementation                       | Still useful — nullable types are a different answer |

``` csharp
// what a value object cost in C# 8
public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public Currency Currency { get; }
    public Money(decimal a, Currency c) { Amount = a; Currency = c; }
    public bool Equals(Money? o) => o is not null && Amount == o.Amount
                                    && Currency == o.Currency;
    public override bool Equals(object? o) => Equals(o as Money);
    public override int GetHashCode() => HashCode.Combine(Amount, Currency);
    public static bool operator ==(Money? a, Money? b) => Equals(a, b);
    public static bool operator !=(Money? a, Money? b) => !Equals(a, b);
    public override string ToString() => $"{Amount} {Currency}";
}

// and now
public readonly record struct Money(decimal Amount, Currency Currency);
// value equality, GetHashCode, ToString, deconstruction, immutability,
// and no heap allocation. Twenty lines removed, and none of them can
// now be wrong.
```

> **\[Trap\]**
>
> **Common trap.** Records give **shallow** value equality. A record holding a `List<T>` compares that list *by reference*, so two records with identical contents are not equal — and a `HashSet` of them will contain duplicates. Use immutable collections in records, or override `Equals` for that member.

###### Where record equality still surprises people

``` csharp
// 1. SHALLOW EQUALITY - the big one
public record Order(Guid Id, List<string> Tags);

var a = new Order(id, ["x"]);
var b = new Order(id, ["x"]);
a == b;                                  // FALSE. The lists differ by reference.

// fix: use an immutable collection with value semantics, or compare manually
public record Order(Guid Id, ImmutableArray<string> Tags);
// ImmutableArray is a struct wrapping an array - STILL reference equality
// on the array. Even this is not enough.

// what actually works: an explicit comparison
public record Order(Guid Id, IReadOnlyList<string> Tags)
{
    public virtual bool Equals(Order? o) =>
        o is not null && Id == o.Id && Tags.SequenceEqual(o.Tags);
    public override int GetHashCode() =>
        Tags.Aggregate(Id.GetHashCode(), (h, t) => HashCode.Combine(h, t));
}
// note `virtual` on Equals - required, or the compiler's generated
// version and yours conflict.
```

``` text
  THE OTHER FOUR SURPRISES

  2  INHERITANCE AND EqualityContract

       record Base(int X);
       record Derived(int X, int Y) : Base(X);

       new Base(1) == new Derived(1, 2)   -> FALSE, always.

     the generated Equals compares EqualityContract (the runtime type)
     first. Usually what you want, and it surprises people who expected
     "a Derived is a Base".

  3  MUTABLE RECORDS ARE LEGAL AND DANGEROUS

       record Order { public List<Line> Lines { get; set; } = []; }

       a record used as a DICTIONARY KEY whose fields change afterwards
       becomes UNFINDABLE - the hash was computed at insertion.
       this is the GetHashCode contract violation, and records make it
       easier to hit because equality is generated and looks safe.

  4  `with` ON A DERIVED TYPE PRESERVES THE TYPE

       Base b = new Derived(1, 2);
       var c = b with { X = 9 };          // c is a Derived, not a Base
     correct, and occasionally surprising.

  5  POSITIONAL RECORDS GENERATE PUBLIC INIT PROPERTIES

       record Order(Guid Id);
       -> public Guid Id { get; init; }
     so a positional record is NOT encapsulated by default. Anything
     with invariants needs an explicit body and a private constructor.
```

| Use                      | For                                                        |
|--------------------------|------------------------------------------------------------|
| `record class`           | DTOs, messages, events — reference type, value equality    |
| `readonly record struct` | Small value objects — `Money`, `OrderId`. No allocation    |
| `record struct`          | Rarely — mutable value semantics is usually a mistake      |
| `class`                  | Entities with identity, anything mutated in place          |
| `sealed record`          | **The default** — avoids the inheritance equality subtlety |

### Concurrency over shared data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Concurrency over shared data</p>

<a id="be-m134"></a>

#### Immutable snapshot publishing

The highest-performance pattern for read-mostly shared data, and the simplest to reason about: readers never synchronise at all, because the object they hold never changes.

``` csharp
public sealed class RuleTable {
    // The only mutable state is one reference. Reference assignment is atomic.
    private FrozenDictionary<string, Rule> _rules = FrozenDictionary<string, Rule>.Empty;

    // Readers: no lock, no Interlocked, no contention. Reads scale linearly with cores.
    public bool TryGet(string key, out Rule rule) =>
        Volatile.Read(ref _rules).TryGetValue(key, out rule!);

    // Writer: build the new table off to the side, then publish with one write.
    public void Publish(IEnumerable<Rule> fresh) =>
        Volatile.Write(ref _rules, fresh.ToFrozenDictionary(r => r.Key, StringComparer.Ordinal));
}
```

Why it is correct: a reader either sees the old table or the new one, never a half-built one, because the table is fully constructed before the reference is published and `Volatile.Write` prevents the construction from being reordered after the publish. Readers already inside a call keep using the old table, which is still a valid, consistent snapshot — it just gets collected once they finish.

``` csharp
// The same idea with a functional update under CAS, when writers are concurrent:
private ImmutableDictionary<string, Rule> _map = ImmutableDictionary<string, Rule>.Empty;

public void AddOrUpdate(string key, Rule rule) {
    ImmutableDictionary<string, Rule> before, after;
    do {
        before = Volatile.Read(ref _map);
        after  = before.SetItem(key, rule);         // structural sharing, O(log n)
    } while (Interlocked.CompareExchange(ref _map, after, before) != before);
}
```

> **\[Why it matters\]**
>
> **The tradeoff.** Every publish allocates a whole new table, so this is right for data that is read millions of times and written occasionally — configuration, feature flags, routing tables, price rules, permission maps. It is wrong for a hot write path, where `ConcurrentDictionary`'s striped locking wins ([Concurrent collections](05-02-threads-and-locks.md#be-b282), [Immutable collections](03-02-arrays-and-lists.md#be-m107)).

> **\[Trap\]**
>
> **Consistency across two snapshots.** Reading two independently-published tables gives you no guarantee they are from the same generation. If several tables must agree, put them in one immutable object and publish that single reference — one atomic swap for the whole consistent set.

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)
