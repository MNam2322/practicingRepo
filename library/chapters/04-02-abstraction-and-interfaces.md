---
id: "4-2"
number: "4.2"
title: "Abstraction and interfaces"
part: 4
part_title: "Designing programs"
words: 3564
minutes: 15
---

# 4.2 Abstraction and interfaces

> **Quick look.** Abstraction: hiding detail behind contracts that callers can rely on.
>
> - Hide each design decision likely to change inside one module.
> - A contract covers preconditions, results, invariants, errors and cost.
> - Interfaces state contracts; abstract classes share real code through inheritance.
> - Interfaces are most useful as seams at the edges of a system, not around everything.
> - Every abstraction leaks a little; never hide expensive or remote work behind a cheap-looking call.
> - The wrong abstraction costs more than duplication; don't abstract too early.


### The big picture

Every useful piece of code hides detail behind a simpler face. A method named `CalculateFare` hides the arithmetic; a repository hides the SQL. That hiding is abstraction. Done well, it lets you use code without reading its insides. Done badly, the hidden details leak out at the worst moment.

This chapter covers the promises code makes to its callers, how interfaces and abstract classes express them in C#, when an interface is worth adding, and how to avoid both leaky abstractions and abstractions created too early.

### Before you start

**Builds on:** 2.1 What a programming language is, 4.1 Procedural, object-oriented and functional styles, 2.3 Types, 1.2 Numbers inside the machine, 1.6 Storage and files.

**Ideas explained later:**

- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **TCP** (6.3): Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **Byte stream** (6.3): A continuous sequence of bytes with no built-in message boundaries.
- **Client** (8.1): A program that sends requests to a server.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **SQL Server** (7.2): Microsoft's relational database product.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What are preconditions, postconditions and invariants?**

> A precondition is what must be true before a method is called, such as "the name is not empty". A postcondition is what the method guarantees afterwards. An invariant is what is always true of an object between calls, such as "passengers never exceed capacity".

**2. When should you use an abstract class instead of an interface?**

> When implementations share real code or state, and a single base class makes sense. Interfaces suit contracts that unrelated types can implement, and a class can implement many interfaces but inherit only one class.

**3. What is a default interface method?**

> A method with a body declared inside an interface, added in C# 8. Implementing types get it for free and may override it. It lets you add members to a published interface without breaking existing implementations.

**4. What is a leaky abstraction? Give an example.**

> An abstraction whose hidden details still affect you. `IQueryable` looks like an in-memory collection, but some expressions can't be translated to SQL, and innocent-looking loops cause one query per item.

**5. Should every class have an interface?**

> No. Add an interface where there is a real reason: several implementations, a boundary such as a database or an external service, or a need to replace the class in tests. An interface with one implementation and no such reason is just ceremony.

**6. What does "duplication is far cheaper than the wrong abstraction" mean?**

> Merging similar-looking code too early creates a shared piece that must serve cases that later turn out to differ. It grows flags and special cases. Waiting until the pattern is clear, often by the third occurrence, gives a better abstraction.

### What an abstraction is

What a programming language is (2.1) described abstraction as hiding details so you can work with bigger ideas. This whole guide is a stack of abstractions. Bits become numbers. Numbers become characters. Memory becomes objects. Sockets become HTTP requests.

In your own code, you build abstractions all the time. A method named `CalculateFare` hides the arithmetic. A `FlightRepository` hides the SQL. A `Booking` class hides the list of seats and its rules. A good abstraction lets the reader of the calling code stop thinking about what is underneath.

### Information hiding

In 1972, David Parnas argued that a program should be split into modules according to which design decisions are likely to change. Each module hides one such decision from the rest. This is **information hiding**.

The file format of the schedule might change, so hide it behind a reader. The database might move from SQL Server to PostgreSQL, so hide the SQL behind a repository. The fare rules change every season, so keep them in one place. When the decision changes, only one module needs editing.

Encapsulation, from Procedural, object-oriented and functional styles (4.1), is the mechanism. Information hiding is the reason for using it.

### Contracts

An abstraction is only useful if callers can rely on it without looking inside. What they may rely on is its **contract**.

A **precondition** is something that must be true when the method is called: the airport code has three letters, the list is not null. A **postcondition** is something the method promises afterwards: the returned list is sorted, the booking has been saved. An **invariant** is something that is always true of an object between method calls: a cabin never has more passengers than seats.

A full contract also covers what happens on failure, which exceptions are thrown, whether null can be returned, whether the method is safe to call from several threads, and roughly what it costs. A method that looks like a cheap property but runs a database query breaks callers' expectations as surely as one that returns wrong answers.

C# checks part of the contract for you. Types and nullable annotations, from Types (2.3), catch many precondition failures at compile time. Guard clauses check the rest at run time. Invariants are enforced by encapsulation: if only the class's own methods can change its data, and each method preserves the invariant, the invariant always holds.

### Interfaces

An **interface** is a named contract with no data. It lists members that any implementing type must provide. `IEnumerable<T>` promises a way to walk through items. `IDisposable` promises a way to release resources. Code written against an interface works with every type that implements it, including ones written later by someone else.

A class can implement any number of interfaces, and so can a struct. That makes interfaces the main tool for polymorphism in modern C#, without the rigidity of inheritance.

Interfaces have gained features over time. A **default interface method**, added in C# 8, has a body in the interface itself. Implementing types get it unless they provide their own. It lets a library add a member to a published interface without breaking every existing implementation. **Static abstract members**, added in C# 11, let an interface require static methods and operators. They are what make generic math with `INumber<T>` possible, as Numbers inside the machine (1.2) showed.

A type can also implement an interface member explicitly, by naming the interface: `void IDisposable.Dispose()`. This is **explicit interface implementation**. The member is then only visible through the interface, not on the class itself. It resolves name clashes between two interfaces and keeps rarely used members out of a class's normal surface.

### Abstract classes

An **abstract class** can't be instantiated directly. It exists to be inherited. Unlike an interface, it can hold fields, constructors and fully implemented methods alongside abstract ones that derived classes must supply.

A classic use is the **template method**. The base class defines the outline of an operation, with fixed steps, and leaves specific steps abstract. Every report has the same header and footer; each kind of report supplies its own body.

Choose an abstract class when implementations genuinely share code and state, and a single inheritance line makes sense. Choose an interface for a contract that unrelated types can fulfil. When in doubt, start with an interface. It keeps implementations free, and shared code can live in helper classes or default methods.

```csharp
// interfaces.cs — run with: dotnet run interfaces.cs
using System.Diagnostics;
using System.Linq;

// 1. Code written against an interface works with any implementation
IFlightSource[] sources = [new InMemoryFlights(), new CsvFlights("BA117,LHR,JFK\nSQ321,LHR,SIN\nQF2,SYD,LHR")];
foreach (IFlightSource source in sources)
    Console.WriteLine($"{source.Name}: {string.Join(", ", source.FlightsFrom("LHR").Select(f => f.Number))}");
// in memory: BA117, BA15
// csv: BA117, SQ321

// 2. A default interface method, inherited by both
Console.WriteLine(sources[1].CountFrom("LHR"));            // 2

// 3. An invariant the class enforces itself
var cabin = new Cabin(capacity: 2);
cabin.Board("Ada");
cabin.Board("Grace");
try { cabin.Board("Linus"); }
catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }   // Cabin full: 2 of 2 seats taken.
try { cabin.Board(" "); }
catch (ArgumentException) { Console.WriteLine("precondition failed: name is blank"); }
Console.WriteLine(cabin.Passengers.Count);                 // 2: the rule held

// 4. A template method in an abstract class
Report report = new DelayReport();
Console.WriteLine(report.Render());
// == Delays ==
// BA117 +45
// -- end --

interface IFlightSource
{
    string Name { get; }

    /// <summary>Flights departing the given airport. Never null; may be empty.</summary>
    IEnumerable<Flight> FlightsFrom(string airportCode);

    int CountFrom(string airportCode) => FlightsFrom(airportCode).Count();   // default implementation
}

record Flight(string Number, string From, string To);

sealed class InMemoryFlights : IFlightSource
{
    readonly Flight[] flights = [new("BA117", "LHR", "JFK"), new("BA15", "LHR", "SIN"), new("QF1", "SYD", "LHR")];
    public string Name => "in memory";
    public IEnumerable<Flight> FlightsFrom(string airportCode) => flights.Where(f => f.From == airportCode);
}

sealed class CsvFlights(string csv) : IFlightSource
{
    public string Name => "csv";
    public IEnumerable<Flight> FlightsFrom(string airportCode) =>
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
           .Select(line => line.Split(','))
           .Select(p => new Flight(p[0], p[1], p[2]))
           .Where(f => f.From == airportCode);
}

sealed class Cabin(int capacity)
{
    readonly List<string> passengers = [];
    public IReadOnlyList<string> Passengers => passengers;   // read-only view: nobody else can add

    public void Board(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);      // precondition
        if (passengers.Count >= capacity)
            throw new InvalidOperationException($"Cabin full: {passengers.Count} of {capacity} seats taken.");
        passengers.Add(name);
        Debug.Assert(passengers.Count <= capacity);           // invariant, checked in Debug builds
    }
}

abstract class Report
{
    public string Render() => $"== {Title} ==\n{Body()}\n-- end --";   // the fixed outline
    protected abstract string Title { get; }
    protected abstract string Body();                                    // the part each report supplies
}

sealed class DelayReport : Report
{
    protected override string Title => "Delays";
    protected override string Body() => "BA117 +45";
}
```

### Interfaces as seams

An interface also creates a **seam**: a place where one implementation can be swapped for another without editing the code that uses it. In production, `IFlightSource` reads from the operations database. In a test, a small in-memory version returns fixed flights. On a developer's laptop, it might read a CSV file. Testing (4.6) relies heavily on seams.

The most valuable seams sit at the edges of a system: databases, message queues, other services, the file system and the clock. Those are the parts that are slow, unreliable or hard to control in tests.

### Do you need an interface?

A common habit in .NET code is to give every class an interface: `IFlightService` for `FlightService`, and so on. It isn't wrong, but it isn't free. Each interface is one more file to keep in step, and "go to definition" lands on the interface rather than the code.

Add an interface when there is a reason. There are, or will soon be, several implementations. The class sits at a boundary you want to replace in tests. You are publishing a library and want freedom to change the implementation. Without one of these, a sealed class is simpler. You can always extract an interface later; modern IDEs do it in seconds.

### Leaky abstractions

In 2002, Joel Spolsky coined the Law of **Leaky Abstractions**: "All non-trivial abstractions, to some degree, are leaky." The hidden details eventually show through, and you have to understand them anyway.

Examples are everywhere. TCP promises a reliable byte stream, but when the network fails, you see timeouts and resets. A remote call through a generated client looks like a local method call, but it can take a hundred milliseconds or fail halfway. EF Core's `IQueryable` looks like an in-memory collection, but some lambdas can't become SQL, and a loop that touches a navigation property can send one query per item. A file system looks permanent until you meet the caches and flushes in Storage and files (1.6).

This is the reason this guide starts from the machine. Abstractions save time on good days. Knowing what is underneath saves you on bad days. When you design an abstraction, be honest in its contract about what can leak: say that a method makes a network call, name the exceptions, and don't disguise expensive work as cheap properties.

### Premature and wrong abstractions

The opposite mistake is abstracting too soon. Two pieces of code look alike, so they are merged into one shared method. Later, one case needs something slightly different, so the shared method gets a flag. Then another flag. After a year, it is a tangle of conditions serving cases that were never really the same.

Sandi Metz summed this up: "Duplication is far cheaper than the wrong abstraction." A common guideline is the **rule of three**: tolerate the first duplicate, and extract an abstraction when the pattern appears a third time and you can see what really varies. If an abstraction turns out to be wrong, it is often best to inline it back into its callers and start again.

### Duck typing and structural contracts

Not every language names its contracts. In Python and JavaScript, any object with the right methods can be used, whatever its declared type: "if it walks like a duck and quacks like a duck, it's a duck." This is **duck typing**. TypeScript and Go check it at compile time with structural typing, as Types (2.3) explained.

C# is nominal, but it has a few duck-typed corners. `foreach` works on any type with a suitable `GetEnumerator` method, `await` on any type with a suitable `GetAwaiter`, and `using` on ref structs with a `Dispose` method, whether or not they implement the matching interface.

### In short

Hide each design decision that is likely to change inside one module. A contract is everything callers may rely on: what must be true before a call, what is true after, what never changes, which errors occur and what it costs. Interfaces state contracts without data, while abstract classes share real code through inheritance. Interfaces create seams for swapping implementations, most usefully at the edges of a system. Every abstraction leaks a little, and the wrong abstraction costs more than duplication.

### What goes wrong

**Contracts nobody wrote down.** Callers guess whether a method can return null, throws on missing data or is thread-safe, and guess differently. Use nullable annotations, XML doc comments and clear names.

**Invariants that can be bypassed.** Public setters and exposed mutable lists let any code break the rules a class was meant to enforce. Keep state private and change it only through methods that check.

**Interfaces for everything.** Hundreds of single-implementation interfaces add friction without adding flexibility. Add them where there is a boundary or a real alternative.

**Fat interfaces.** An interface with thirty members forces every implementation, and every test fake, to provide all of them. Split it by what callers actually use.

**Hiding expensive or remote work.** A property that queries a database, or a method that looks local but calls a service, surprises callers with latency and failures. Make cost and failure visible in names and signatures, such as `LoadAsync`.

**Abstracting too early.** A shared helper with five boolean parameters serves nobody well. Duplicate until the real pattern is clear, and inline a wrong abstraction rather than patch it.

### Cost model

A call through an interface costs slightly more than a direct call: a lookup of the method in the object's type data and an indirect jump, typically a nanosecond or two. It usually blocks inlining unless the JIT can see the concrete type, and Dynamic PGO often can. For value types, calling through an interface can box, unless the call is made through a generic method constrained to that interface. The larger costs of abstraction are human: each layer is something to read, name and keep in step.

### In other languages (optional)

**TypeScript.** Interfaces describe shapes and are checked structurally. Any object with matching members satisfies one, whether or not it says so. They vanish at run time.

**JavaScript and Python.** Rely on duck typing. Python can make contracts explicit with abstract base classes from the `abc` module or with `Protocol` types checked by static tools.

**Java.** Interfaces with default methods, and abstract classes, work much like C#'s.

**Go.** Interfaces are satisfied implicitly: any type with the right methods implements an interface, even one defined in another package later. Go style favours very small interfaces, often with a single method.

**Rust.** Traits play the role of interfaces, including default methods and implementations for types defined elsewhere.

### Key terms

- **Information hiding**: Splitting a program so that each module hides a design decision likely to change.
- **Contract**: Everything callers may rely on about a method or type, beyond its signature.
- **Precondition**: Something that must be true when a method is called.
- **Postcondition**: Something a method guarantees when it returns.
- **Invariant**: Something that is always true of an object between method calls.
- **Interface**: A named contract listing members that implementing types must provide.
- **Default interface method**: A method with a body declared inside an interface, inherited by implementing types.
- **Static abstract member**: A static method or operator that an interface requires implementing types to provide.
- **Explicit interface implementation**: Implementing an interface member so that it is only visible through the interface.
- **Abstract class**: A class that can't be instantiated and exists to be inherited, possibly with shared code.
- **Template method**: A base-class method that fixes an operation's outline and leaves some steps to derived classes.
- **Seam**: A place where one implementation can be swapped for another without changing the calling code.
- **Leaky abstraction**: An abstraction whose hidden details still affect the code that uses it.
- **Rule of three**: Waiting until a pattern appears three times before extracting a shared abstraction.
- **Duck typing**: Accepting any object that has the needed members, regardless of its declared type.

### Where this leads

Wiring implementations to interfaces is in Composition and dependency injection (4.3). Small, focused interfaces are one of the principles in Design principles that hold up (4.5). Replacing real implementations in tests is in Testing (4.6). The leaks of the network are explored in TCP and UDP (6.3) and Distributed systems basics (12.2), and the leaks of `IQueryable` in ORMs and EF Core (7.7).

### Check yourself

1. Add a third `IFlightSource` that reads from a real CSV file on disk. Decide what the contract says when the file doesn't exist, and implement it.
2. Write down the full contract of `Cabin.Board` in a doc comment: preconditions, postcondition, invariant, exceptions and thread safety.
3. Make `Cabin` thread-safe with a lock, and explain which part of its contract changed.
4. Override `CountFrom` in `InMemoryFlights` with a faster version, and check that calls through `IFlightSource` use it.
5. Find an interface in a codebase you know with only one implementation. Decide whether there is a real reason for it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: An interface that hid a network call

`IFareProvider.GetFare()` looked like a cheap property-style call. Someone called it in a loop of 500 items; it was an HTTP request each time.

*Make cost visible in the contract*

```csharp
public interface IFareProvider
{
    // async, batched, cancellable: the signature tells callers this crosses the network
    Task<IReadOnlyDictionary<RouteKey, decimal>> GetFaresAsync(
        IReadOnlyCollection<RouteKey> routes, CancellationToken ct);
}
```

> **Lesson:** An abstraction must not hide expensive or remote work. Let the signature show it: async, batched, cancellable.

#### Example 2: A seam for the payment provider

Tests can't run because checkout calls the real payment gateway.

*An interface at the edge, owned by your code*

```csharp
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(Money amount, string token, Guid idempotencyKey, CancellationToken ct);
}
public sealed class StripeGateway(HttpClient http) : IPaymentGateway { /* real calls */ }
public sealed class FakeGateway : IPaymentGateway
{
    public List<Money> Charged { get; } = [];
    public Task<PaymentResult> ChargeAsync(Money a, string t, Guid k, CancellationToken ct)
    { Charged.Add(a); return Task.FromResult(PaymentResult.Ok("fake-123")); }
}
```

> **Lesson:** Interfaces earn their place at system edges: external services, the clock, the file system. Not around every class.
