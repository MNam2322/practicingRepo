---
id: "4-5"
number: "4.5"
title: "Design principles that hold up"
part: 4
part_title: "Designing programs"
words: 7508
minutes: 33
---

# 4.5 Design principles that hold up

> **Quick look.** Rules of thumb that keep code cheap to change, and the signs it is getting expensive.
>
> - Low coupling between modules, high cohesion within them.
> - SOLID: one reason to change; extend without editing; subtypes keep their base's promises; small interfaces; depend on abstractions owned by the higher-level code.
> - DRY is about repeated knowledge, not repeated-looking text; YAGNI: don't build for needs you don't have.
> - Rule of three: abstract only after the third occurrence shows what actually varies.
> - Refactor in small tested steps, never mixed with behaviour changes; pay down tech debt steadily.
> - Warning signs: god classes, boolean flags that switch behaviour, train-wreck call chains.


### The big picture

Code is read far more often than it is written, and it keeps changing for years. The cost of software is mostly the cost of changing it later.

The principles in this chapter, such as low coupling, high cohesion, SOLID, DRY and YAGNI, are rules of thumb for keeping change cheap. None of them is a law, and each can be overdone. This chapter explains each one in plain terms, works through a real before-and-after refactoring, and names the warning signs that code is getting hard to change.

### Before you start

**Builds on:** 4.3 Composition and dependency injection, 4.2 Abstraction and interfaces, 2.3 Types.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Backend** (8.1): The part of a system that runs on the organization's servers, holding data and enforcing rules.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What are coupling and cohesion, and which way should each go?**

> Coupling is how much one module depends on the details of another; keep it low. Cohesion is how closely the parts inside one module belong together; keep it high. Low coupling and high cohesion let you change one thing without touching many.

**2. What does the single responsibility principle actually ask for?**

> That a module have one reason to change, meaning it serves one group of people or one concern. It doesn't mean "do only one tiny thing". A fare calculator that also formats emails has two reasons to change.

**3. What is the Liskov substitution principle? Give a .NET example of breaking it.**

> Any subtype must be usable wherever its base type is expected without breaking the base's contract. `ReadOnlyCollection<T>` implements `IList<T>` but throws `NotSupportedException` from `Add`, so code written for `IList<T>` can fail.

**4. In the dependency inversion principle, who owns the interface?**

> The high-level code that uses it. A fare report defines the `IFareSource` it needs, and the database adapter implements it. The detail depends on the policy, not the other way round.

**5. Is DRY about identical-looking code?**

> No. "Don't repeat yourself" is about knowledge: each business rule should live in one place. Two pieces of code that look alike but represent different rules should stay separate, because they will change for different reasons.

**6. What does the Law of Demeter warn against?**

> Reaching through objects: `booking.Flight.Aircraft.Operator.Name`. Each dot couples the caller to another class's structure. Ask the nearest object for what you need instead.

**7. What is refactoring, strictly?**

> Changing the structure of code without changing its behaviour, in small steps, with tests confirming each step. Adding features or fixing bugs at the same time isn't refactoring.

### Why principles matter

Code is read far more often than it is written, and it keeps changing for as long as it is used. The cost of software is mostly the cost of changing it later. A design is good when the changes people actually need are easy, safe and local.

The principles in this chapter are rules of thumb for getting there. None is a law. They sometimes pull against each other, and applying any of them blindly makes code worse. What they share is one goal: keep each change small and each piece understandable on its own.

### Coupling and cohesion

Two ideas from Larry Constantine in the 1970s underpin almost everything else.

**Coupling** is how much one module depends on another. Modules that talk only through small, stable contracts are loosely coupled: you can change one without touching the other. Modules that reach into each other's data, share global state or depend on each other's internal order of operations are tightly coupled. A change in one forces changes in the other, and often in ways nobody notices until something breaks.

**Cohesion** is how closely the parts inside one module belong together. A `FareCalculator` whose methods all concern pricing is highly cohesive. A `Utilities` class with a date formatter, an email sender and a CSV parser has low cohesion: its parts have nothing in common but the file they sit in.

Aim for low coupling between modules and high cohesion within them. Then each change lands in one place.

### Separation of concerns

Dijkstra used the phrase **separation of concerns** in 1974: think about one aspect of a problem at a time. In code, that means keeping different concerns in different places. Parsing input, applying business rules, storing data and presenting results are separate concerns. So are security, logging and caching. When each has its own home, you can change the storage without touching the rules, and test the rules without a database.

### SOLID

Robert C. Martin gathered five object-oriented design principles in the early 2000s, and Michael Feathers gave them the acronym **SOLID**.

**S: the single responsibility principle.** A module should have one reason to change. Martin later clarified this as "one actor": one group of people whose requests drive changes. A class that calculates fares and also formats confirmation emails changes when the revenue team changes pricing and when the marketing team changes wording. Split it, and each change touches one class.

**O: the open-closed principle.** Software should be open for extension and closed for modification: you add new behaviour by adding code, not by editing working code. Strategies, plugins and decorators from Composition and dependency injection (4.3) are the tools. New pricing rules become new classes, and the code that applies rules doesn't change. Apply this where change is expected, not everywhere; designing every class for extension is its own kind of complexity.

**L: the Liskov substitution principle.** Named after Barbara Liskov, who stated it in 1987. Any subtype must work wherever its base type is expected, keeping all of the base's promises. A subtype may accept more and promise more, never less. .NET itself breaks it in one well-known place: `ReadOnlyCollection<T>` implements `IList<T>` but throws on `Add`. Code written against `IList<T>` can't trust `Add` to work. That is why the read-only interfaces, such as `IReadOnlyList<T>`, were added later.

**I: the interface segregation principle.** Clients shouldn't be forced to depend on methods they don't use. Many small, focused interfaces beat one large one. A report that only reads flights should take an `IFlightReader`, not an `IFlightRepository` with thirty methods for writing, deleting and archiving.

**D: the dependency inversion principle.** High-level policy shouldn't depend on low-level details; both should depend on abstractions. And the abstraction belongs to the policy. A fare report defines the interface it needs for reading fares. The SQL adapter implements that interface. The arrow of dependency points from the detail to the policy, which is the reverse, the inversion, of a naive design where the report calls the SQL code directly.

### A refactoring, before and after

The same small job, done two ways. The first puts parsing, validation, pricing and formatting into one method with flags. The second gives each concern its own small piece.

```csharp
// principles.cs — run with: dotnet run principles.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

string input = "BA117,LHR,JFK,450\nSQ321,LHR,SIN,x\nQF1,SYD,LHR,1200";

// Before: one method with four reasons to change
Console.WriteLine(Before.Process(input, peak: true, format: "text"));

// After: each piece has one job; the policy depends only on interfaces it owns
var report = new FareReport(new CsvFareParser(), new PeakRule(), new TextFormatter());
Console.WriteLine(report.Run(input));
// Both print:
// BA117 LHR-JFK 540.0
// QF1 SYD-LHR 1440.0
// skipped 1 bad line(s)

// A Liskov violation inside .NET itself
IList<string> gates = new ReadOnlyCollection<string>(["A1", "A2"]);
try { gates.Add("A3"); }
catch (NotSupportedException) { Console.WriteLine("IList<T>.Add threw: this subtype doesn't keep the contract"); }

static class Before
{
    public static string Process(string csv, bool peak, string format)
    {
        var output = new List<string>();
        int bad = 0;
        foreach (string row in csv.Split('\n'))
        {
            string[] p = row.Split(',');
            if (p.Length != 4 || !decimal.TryParse(p[3], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fare))
            { bad++; continue; }
            decimal price = peak ? fare * 1.2m : fare;
            string shown = price.ToString(CultureInfo.InvariantCulture);
            output.Add(format == "text" ? $"{p[0]} {p[1]}-{p[2]} {shown}" : $"{{\"flight\":\"{p[0]}\",\"price\":{shown}}}");
        }
        output.Add($"skipped {bad} bad line(s)");
        return string.Join("\n", output);
    }
}

record FareLine(string Flight, string From, string To, decimal Fare);

interface IFareParser { (List<FareLine> Lines, int Bad) Parse(string input); }
interface IPricingRule { decimal Apply(decimal fare); }
interface IReportFormatter { string Format(IEnumerable<(FareLine Line, decimal Price)> rows, int bad); }

sealed class FareReport(IFareParser parser, IPricingRule rule, IReportFormatter formatter)
{
    public string Run(string input)                          // the policy: no parsing, pricing or formatting details
    {
        var (lines, bad) = parser.Parse(input);
        return formatter.Format(lines.Select(l => (l, rule.Apply(l.Fare))), bad);
    }
}

sealed class CsvFareParser : IFareParser
{
    public (List<FareLine> Lines, int Bad) Parse(string input)
    {
        var lines = new List<FareLine>();
        int bad = 0;
        foreach (string row in input.Split('\n'))
        {
            string[] p = row.Split(',');
            if (p.Length == 4 && decimal.TryParse(p[3], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fare))
                lines.Add(new FareLine(p[0], p[1], p[2], fare));
            else bad++;
        }
        return (lines, bad);
    }
}

sealed class PeakRule : IPricingRule { public decimal Apply(decimal fare) => fare * 1.2m; }

sealed class TextFormatter : IReportFormatter
{
    public string Format(IEnumerable<(FareLine Line, decimal Price)> rows, int bad) =>
        string.Join("\n", rows.Select(r => $"{r.Line.Flight} {r.Line.From}-{r.Line.To} {r.Price.ToString(CultureInfo.InvariantCulture)}")
                              .Append($"skipped {bad} bad line(s)"));
}
```

The second version is longer. It pays off when things change. A JSON formatter is a new class, not another branch in a condition. A new pricing rule is a new class. A parser for another file format doesn't touch pricing. Each piece can be tested alone. For a one-off script, the first version is fine. For code that will live for years and change monthly, the second is cheaper.

### DRY

The Pragmatic Programmer, from 1999, gave the principle **DRY**, "don't repeat yourself": every piece of knowledge should have a single, unambiguous representation in a system. If the rule "business seats add a lounge fee" lives in three places, sooner or later one of them is wrong.

DRY is about knowledge, not text. Two methods that look identical but represent different business rules are not duplication; they are coincidence. Merging them couples two things that will change for different reasons, which leads straight to the wrong abstraction described in Abstraction and interfaces (4.2). Before removing duplication, ask whether the two copies would always change together.

### YAGNI and KISS

**YAGNI**, "you aren't gonna need it", comes from Extreme Programming in the late 1990s. Don't build features, options or extension points until there is a real need for them. Speculative flexibility has to be written, tested, documented and understood by every reader, and the need it anticipates often never comes, or comes in a different shape.

**KISS**, "keep it simple", says the same thing more broadly. The simplest design that meets today's needs, and is easy to change tomorrow, usually beats a clever design that anticipates everything.

These pull against the open-closed principle. Resolve the tension with evidence: build for extension where change has already happened or is certain, and keep things simple everywhere else.

### The Law of Demeter

The **Law of Demeter**, from a 1987 project at Northeastern University, says a method should only talk to its close collaborators: its own fields, its parameters and objects it creates. Not to their friends.

`booking.Flight.Aircraft.Operator.ContactEmail` couples the calling code to the structure of four classes. If the aircraft's operator moves somewhere else, this line breaks, along with every other line like it. Asking `booking` for what you need, or passing the email in directly, keeps the knowledge in one place.

A related guideline is **tell, don't ask**. Rather than asking an object for its data and making a decision about it, tell the object what you want and let it decide: `cabin.Board(passenger)` instead of checking `cabin.Passengers.Count < cabin.Capacity` yourself and then adding to its list.

### The principle of least astonishment

Code should do what a reasonable reader expects from its name and signature. A method called `GetFlight` shouldn't create one if it is missing. A property shouldn't make a network call. `Equals` shouldn't throw. This **principle of least astonishment** is mostly about naming and contracts. Every surprise is a bug waiting for the next person who trusts the name.

### Code smells

A **code smell**, a term from Kent Beck and Martin Fowler, is a surface sign that suggests a deeper design problem. It isn't a bug; it is a reason to look closer.

Common smells include a long method that does many things, a large class with low cohesion, and a long parameter list. **Feature envy** is a method that uses another class's data more than its own, and probably belongs there. **Shotgun surgery** is one change requiring edits in many classes, a sign of scattered knowledge. **Primitive obsession** is using strings and ints where a small type would carry meaning, as Types (2.3) discussed. **Data clumps** are the same few values always passed around together, asking to become one type.

### Refactoring

**Refactoring** is changing the structure of code without changing what it does. It is done in small, safe steps: rename, extract a method, move a method to the class whose data it uses, introduce a parameter object, replace a condition with polymorphism. After each step, the tests still pass.

Tests are what make refactoring safe, which is the subject of Testing (4.6). Refactoring without tests is just editing and hoping. Keep refactoring and behaviour changes in separate commits, so each can be reviewed and reverted on its own.

### Technical debt

Ward Cunningham introduced the **technical debt** metaphor in 1992. Taking a shortcut to ship sooner is like borrowing money. It can be a sound decision. But until the shortcut is cleaned up, every change in that area costs extra, like interest. Leave the debt too long, and the interest takes most of the team's time.

Some debt is deliberate: "we'll ship with a simple design and revisit when we have real usage data." Some is accidental: the design made sense until the requirements moved. Both are normal. The problems come from debt nobody tracks, and from never paying any back. Small, continuous refactoring as part of everyday work is the cheapest repayment plan.

### When principles conflict

Principles are tools for judgement, not substitutes for it. DRY can conflict with low coupling. The open-closed principle can conflict with YAGNI. Single responsibility, pushed too far, scatters one idea across twenty tiny classes and destroys cohesion. When principles disagree, go back to the goal: which design makes the likely changes easiest, and which is easiest for the next reader to understand?

### In short

Keep dependencies between modules low and relatedness within them high. SOLID gives five guidelines: one reason to change, extend without editing, subtypes keep their base's promises, small interfaces, and dependencies that point at abstractions owned by the higher-level code. DRY is about repeated knowledge, not repeated-looking text. Don't build for needs you don't have yet. Refactor in small, tested steps, and pay down technical debt steadily.

### What goes wrong

**God classes.** One `FlightManager` with 4,000 lines does everything, and every change risks breaking something unrelated. Split by responsibility, one piece at a time, behind tests.

**Flags that switch behaviour.** `Process(data, peak: true, format: "json", dryRun: false)` combines several jobs in one method. Each flag doubles the paths to test. Split into separate methods or strategies.

**Over-applying principles.** Interfaces for everything, factories for factories, and single-method classes for every step make simple code hard to follow. Match the structure to the real rate of change.

**Removing coincidental duplication.** Two similar methods are merged, and a later change to one rule breaks the other. Only merge code that represents the same knowledge.

**Train wrecks.** Long chains like `a.B.C.D.E` break whenever any class in the chain changes shape. Ask the nearest object.

**Refactoring and changing behaviour at once.** A large commit both restructures and fixes bugs, so reviewers can't tell which change caused a problem. Separate them.

**Unpaid technical debt.** Every change takes longer than the last, and estimates keep slipping. Make debt visible and set aside time to pay it down steadily.

### Cost model

Good structure costs a little more to write at first: more types, more files, more names. It saves time on every later change, and most code is changed many times. Poor structure is cheap at first and grows expensive, often without anyone noticing, until simple changes take days. At run time these principles cost almost nothing; an extra interface call or small object is nanoseconds. The main cost of over-engineering is paid by readers, not processors.

### In other languages (optional)

**TypeScript and JavaScript.** The same principles apply to modules and functions. Structural typing makes small interfaces cheap to declare where they are used. Large frontend apps suffer especially from coupling through shared global state.

**Python.** "Explicit is better than implicit" and "simple is better than complex" from the Zen of Python (`import this`) echo KISS and the principle of least astonishment.

**Go.** The proverb "a little copying is better than a little dependency" leans deliberately toward duplication over coupling. Small interfaces defined by their users apply interface segregation and dependency inversion by default.

**Functional languages.** Many SOLID ideas appear naturally: small functions, functions passed as strategies, and pure logic separated from effects.

### Key terms

- **Coupling**: How much one module depends on the details of another. Keep it low.
- **Cohesion**: How closely the parts inside one module belong together. Keep it high.
- **Separation of concerns**: Keeping different aspects of a program, such as parsing, rules and storage, in different places.
- **SOLID**: Five object-oriented design principles: single responsibility, open-closed, Liskov substitution, interface segregation and dependency inversion.
- **Single responsibility principle**: A module should have one reason to change.
- **Open-closed principle**: Software should allow new behaviour to be added without modifying working code.
- **Liskov substitution principle**: A subtype must work wherever its base type is expected, keeping all of its promises.
- **Interface segregation principle**: Clients shouldn't depend on interface members they don't use.
- **Dependency inversion principle**: High-level policy and low-level details should both depend on abstractions owned by the policy.
- **DRY**: Don't repeat yourself: each piece of knowledge should live in one place.
- **YAGNI**: You aren't gonna need it: don't build for needs that don't exist yet.
- **KISS**: Keep it simple: prefer the simplest design that meets the need.
- **Law of Demeter**: A method should talk only to its close collaborators, not reach through them to others.
- **Tell, don't ask**: Tell an object what you want done instead of pulling out its data and deciding for it.
- **Principle of least astonishment**: Code should behave the way its name and signature lead a reader to expect.
- **Code smell**: A surface sign in code that suggests a deeper design problem.
- **Refactoring**: Changing the structure of code without changing its behaviour, in small tested steps.
- **Technical debt**: The ongoing extra cost of changes caused by shortcuts or outdated design.

### Where this leads

Tests that make refactoring safe are in Testing (4.6). Coupling between threads is in Why concurrency is hard (5.1). Coupling between a backend and its clients is in API design (8.2). The same principles applied to whole systems, including modular monoliths and service boundaries, are in Monoliths, modules and services (12.1).

### Check yourself

1. Add JSON output to both versions in `principles.cs`. Count how many lines changed in existing code in each version.
2. Add an off-peak rule chosen at run time, again in both versions, and compare.
3. Write a `Square` class that inherits from a `Rectangle` with settable `Width` and `Height`. Write a method that works for every `Rectangle` and breaks for `Square`, and explain which principle it violates.
4. Find a method in real code with a boolean parameter that switches its behaviour. Split it into two methods and see how the call sites read.
5. Pick one code smell from this chapter and find an example of it in code you know. Describe the refactoring steps you would take, in order.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A boolean parameter that grew into a monster

`Send(booking, true, false, true)` appears in 40 places, and nobody knows what each flag means.

*Name intent instead*

```csharp
public sealed record SendOptions(bool IncludeInvoice = false, bool Sms = false, bool HighPriority = false);

await notifier.SendAsync(booking, new SendOptions(IncludeInvoice: true, HighPriority: true), ct);
// or separate methods: SendConfirmationAsync / SendUrgentChangeAsync
```

> **Lesson:** Flags that switch behaviour hide several methods in one. Name the options or split the method.

#### Example 2: The duplication you should leave alone

Two validators both check "length ≤ 50": one for passenger names, one for promo codes. A refactor merges them, and later a change to one rule breaks the other.

*Keep coincidental duplication separate*

```csharp
public static class PassengerRules { public const int MaxNameLength = 50; }  // from the airline's systems
public static class PromoRules     { public const int MaxCodeLength = 50; }  // marketing's choice
// Same number today, different reasons to change tomorrow.
```

> **Lesson:** DRY is about repeated knowledge, not repeated text. Merge only when the same rule has the same reason to change.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a73"></a>

#### SOLID, in .NET terms

Five principles that are quoted constantly and applied inconsistently. Each one has a concrete .NET shape, and a concrete smell that tells you it has been violated.

| Principle                 | Means                             | Violation smell in a .NET codebase                                          |
|---------------------------|-----------------------------------|-----------------------------------------------------------------------------|
| **S**ingle responsibility | One reason to change              | `OrderService` with 40 methods touching email, PDF, pricing and persistence |
| **O**pen/closed           | Extend without editing            | A `switch` on an enum that grows a branch every quarter                     |
| **L**iskov substitution   | Subtypes honour the base contract | An override that throws `NotSupportedException`                             |
| **I**nterface segregation | Small, role-shaped interfaces     | `IRepository<T>` with 20 members, most stubbed in tests                     |
| **D**ependency inversion  | Depend on abstractions you own    | The domain project referencing `Microsoft.EntityFrameworkCore`              |

``` csharp
// Open/closed, concretely. Before: every new channel edits this method.
public decimal Fee(Order o) => o.Channel switch
{
    Channel.Web    => o.Total * 0.01m,
    Channel.Phone  => o.Total * 0.03m,
    Channel.Partner=> o.Total * 0.02m,       // <- the file every PR touches
    _ => throw new NotSupportedException()
};

// After: adding a channel is adding a file.
public interface IFeePolicy { Channel Channel { get; } decimal Fee(Order o); }

public sealed class PartnerFeePolicy : IFeePolicy
{
    public Channel Channel => Channel.Partner;
    public decimal Fee(Order o) => o.Total * 0.02m;
}

public sealed class FeeCalculator(IEnumerable<IFeePolicy> policies)
{
    private readonly Dictionary<Channel, IFeePolicy> _map = policies.ToDictionary(p => p.Channel);
    public decimal Fee(Order o) => _map[o.Channel].Fee(o);
}
```

###### Interface segregation, and the mock that proves you got it wrong

The most reliable detector for a bloated interface is a test file. If stubbing a dependency requires configuring fifteen members when the test cares about one, the interface is describing a *class* rather than a *role*.

``` csharp
// one fat interface: every consumer depends on everything
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Order>> SearchAsync(OrderQuery q, CancellationToken ct);
    Task AddAsync(Order o, CancellationToken ct);
    Task<int> CountByStatusAsync(OrderStatus s, CancellationToken ct);
    Task<Stream> ExportCsvAsync(OrderQuery q, CancellationToken ct);
    Task ArchiveOlderThanAsync(DateOnly cutoff, CancellationToken ct);
}

// role interfaces: each consumer states exactly what it needs
public interface IOrderReader  { Task<Order?> GetAsync(Guid id, CancellationToken ct); }
public interface IOrderWriter  { Task AddAsync(Order o, CancellationToken ct); }
public interface IOrderArchive { Task ArchiveOlderThanAsync(DateOnly cutoff, CancellationToken ct); }

// one implementation can satisfy all three
public sealed class SqlOrderRepository : IOrderReader, IOrderWriter, IOrderArchive { }

services.AddScoped<SqlOrderRepository>();
services.AddScoped<IOrderReader>(sp => sp.GetRequiredService<SqlOrderRepository>());
services.AddScoped<IOrderWriter>(sp => sp.GetRequiredService<SqlOrderRepository>());
```

The dependency graph now tells the truth: the archival job depends on archival, not on search and export. That is what makes it safe to change the export signature without recompiling half the solution.

> **\[Why it matters\]** SOLID is not a checklist to satisfy; it is a vocabulary for explaining *why* a change was hard. “This was painful because the fee logic violated open/closed” is a more useful review comment than “this feels messy”.

### Design Principles

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Design Principles</p>

<p class="pd">The principles the patterns are made of. SOLID stated precisely rather than as slogans, plus the shorter rules and what to do when they conflict — which they routinely do.</p>

<a id="be-h725"></a>

#### SOLID: what it is, and what it is not

Five principles, coined separately and grouped later for the acronym. They are **heuristics about change**, not laws — each answers “what makes this code painful to modify?” and each can be taken too far.

|                           | Says                                      | Prevents                                        |
|---------------------------|-------------------------------------------|-------------------------------------------------|
| **S**ingle responsibility | One reason to change                      | A class that breaks whenever anything moves     |
| **O**pen/closed           | Open to extension, closed to modification | Editing tested code to add a case               |
| **L**iskov substitution   | A subtype must be usable as its base      | Inheritance that lies                           |
| **I**nterface segregation | Many small interfaces beat one large one  | Implementing methods you do not need            |
| **D**ependency inversion  | Depend on abstractions, not concretions   | A domain that cannot compile without a database |

The honest caveat, stated up front: SOLID is a set of *tendencies*, and applying all five maximally produces an over-abstracted codebase where every class is an interface with one implementation. The value is in recognising which one a specific piece of pain corresponds to, not in scoring highly on all five.

<a id="be-h726"></a>

#### S — single responsibility, stated properly

The common phrasing — “a class should do one thing” — is uselessly vague, because “one thing” can mean anything. The precise statement is: **a class should have one reason to change**, where a reason is a person or role who could request it.

``` csharp
// three reasons to change, in one class
public class Report
{
    public decimal CalculateTotals()  { }   // changes when FINANCE changes the rules
    public string RenderHtml()        { }   // changes when DESIGN changes the layout
    public void SaveToDatabase()      { }   // changes when the DBA changes the schema
}
// three different people can each force an edit to this file.
// they will conflict in version control, and a design change risks
// breaking a financial calculation.

// one reason each
public class ReportCalculator { public ReportTotals Calculate(...); }  // finance
public class ReportRenderer   { public string ToHtml(ReportTotals t); } // design
public class ReportRepository { public Task SaveAsync(Report r); }      // data
```

``` text
  THE TEST, in one question

    "who would ask me to change this?"

    one answer   -> one responsibility. Good.
    two answers  -> two reasons to change. Consider splitting.

  and the counter-test, which stops you over-applying it:

    "would these two ALWAYS change together?"

    if yes, splitting them creates coupling for no benefit - now a
    single change touches two files and neither is understandable alone.

  the failure mode of over-applying S is a class per method:
    OrderCreator, OrderValidator, OrderSaver, OrderNotifier,
    OrderCreationOrchestrator...
  cohesion has been destroyed in the name of responsibility.
```

> **\[Note\]**
>
> **The useful reframing.** Single responsibility is really about **who**, not **what**. Group code that changes for the same person's reasons; separate code that changes for different people's reasons. That framing tells you both when to split and when splitting would be wrong — which “do one thing” never does.

<a id="be-h727"></a>

#### O — open for extension, closed for modification

Adding a new case should mean adding code, not editing existing tested code. The reason is risk: editing a working class can break behaviour that already works, and a large `switch` touched by every feature becomes the most dangerous file in the codebase.

``` csharp
// CLOSED to extension: every new payment type edits this method,
// and every edit risks the existing branches.
public decimal Fee(Payment p) => p.Type switch
{
    "card"     => p.Amount * 0.029m + 0.30m,
    "transfer" => 0.25m,
    "wallet"   => p.Amount * 0.015m,
    _ => throw new NotSupportedException()
};

// OPEN to extension: a new type is a NEW FILE. Nothing existing is touched.
public interface IFeeCalculator
{
    bool Handles(PaymentType type);
    decimal Fee(Payment p);
}
public sealed class CardFee : IFeeCalculator { ... }
public sealed class WalletFee : IFeeCalculator { ... }

public decimal Fee(Payment p) =>
    _calculators.First(c => c.Handles(p.Type)).Fee(p);
```

| Keep the switch when                 | Extract when                              |
|--------------------------------------|-------------------------------------------|
| The set of cases is closed and small | New cases arrive regularly                |
| All cases are trivial one-liners     | Cases have real logic                     |
| You want them visible in one place   | Cases are added by other teams or plugins |
| Exhaustiveness checking is valuable  | Each case needs its own dependencies      |
| The cases change together            | The cases change independently            |

> **\[Trap\]**
>
> **Common trap.** Open/closed is the principle most often used to justify premature abstraction. A `switch` with three stable cases is *better* than three files and a resolver — it is visible, exhaustive, and the compiler checks it. Apply the principle when the modification pain is *real and recurring*, not when you can imagine it occurring.

<a id="be-h728"></a>

#### L — Liskov substitution, and why it is the subtle one

If `Duck` is a subtype of `Bird`, then anywhere the code expects a `Bird`, a `Duck` must work — without the caller knowing or checking. Violations do not fail to compile; they fail at runtime, in a caller that did nothing wrong.

``` csharp
// the classic violation, and it type-checks perfectly
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
}
public class Square : Rectangle          // a square IS a rectangle, mathematically
{
    public override int Width  { set { base.Width = base.Height = value; } }
    public override int Height { set { base.Width = base.Height = value; } }
}

// a caller that is entirely reasonable:
void Resize(Rectangle r) { r.Width = 5; r.Height = 4; Debug.Assert(r.Width == 5); }
Resize(new Square());   // ASSERTION FAILS. Width is 4.

// the caller's assumption was correct FOR RECTANGLES. Square broke it.
// the lesson: "is-a" in the domain does not imply "is-substitutable-for"
// in code. Substitutability is about BEHAVIOUR, not taxonomy.
```

| A subtype must not                       | Because                                       |
|------------------------------------------|-----------------------------------------------|
| Strengthen preconditions                 | The caller passed something the base accepted |
| Weaken postconditions                    | The caller relied on what the base guaranteed |
| Throw new exception types                | The caller cannot know to handle them         |
| Change observable invariants             | As in the `Square` case                       |
| Do nothing where the base does something | A silent no-op is a behaviour change          |

``` text
  the everyday violation, in .NET

    IList<T>.Add()  on the result of Array.AsReadOnly()
      -> throws NotSupportedException

    a read-only collection IS-A IList<T> by the type system, and is NOT
    substitutable for one. Every caller of IList<T> must now defend
    against a method that the interface says exists.

    which is why .NET later split the hierarchy:
      IEnumerable<T>          -> iterate
      IReadOnlyCollection<T>  -> + count
      IReadOnlyList<T>        -> + index
      ICollection<T>          -> + mutate
      IList<T>                -> + indexed mutate

    that split is INTERFACE SEGREGATION solving a LISKOV problem, which
    is a good illustration of how the principles interlock.
```

> **\[Note\]**
>
> **The practical test.** Write the tests for the base type, then run them against every subtype. If any test must be modified or skipped, substitutability is broken. That is a mechanical check, and it is far more useful than arguing about whether a square is a rectangle.

<a id="be-h729"></a>

#### I — interface segregation

No client should be forced to depend on methods it does not use. The cost of a fat interface is not the extra methods — it is that every implementer must supply them, and every change to any of them recompiles and risks every client.

``` csharp
// a fat interface: every implementer must supply everything
public interface IRepository<T>
{
    Task<T?> GetAsync(Guid id);
    Task<IReadOnlyList<T>> ListAsync();
    Task AddAsync(T item);
    Task UpdateAsync(T item);
    Task DeleteAsync(Guid id);
    Task<int> CountAsync();
    IQueryable<T> Query();                  // leaks the persistence model
}
// a read-only reporting implementation must now throw from five methods -
// which is also a Liskov violation.

// segregated: a client depends on exactly what it uses
public interface IOrderReader { Task<Order?> GetAsync(Guid id, CancellationToken ct); }
public interface IOrderWriter { Task AddAsync(Order o, CancellationToken ct); }

// and a class can implement both where that makes sense
public sealed class SqlOrderRepository : IOrderReader, IOrderWriter { ... }
// the SPLIT is for the CONSUMER's benefit, not the implementer's.
```

The consumer-side framing is the one that makes this principle click. An interface is defined *by what its callers need*, not by what its implementation happens to offer. A handler that only reads should take `IOrderReader` — and its constructor now documents, truthfully, that it cannot write.

> **\[Trap\]**
>
> **Common trap.** Taken to its extreme this produces one interface per method, which is its own kind of unreadable. The balance point: segregate when different clients genuinely need different subsets, or when a subset would otherwise force an implementer to throw. Do not segregate an interface whose methods are always used together.

<a id="be-h730"></a>

#### D — dependency inversion

The most consequential of the five, and the one the whole architecture part rests on. It states two things: high-level code should not depend on low-level code, and *both* should depend on abstractions — with the abstraction owned by the high-level side.

``` text
  the ownership detail is the entire point, and it is usually missed

  NOT INVERTED
    OrderService  --->  SqlOrderRepository
    the domain depends on the database. Change the database, change the domain.

  "INVERTED", but the interface lives with the implementation
    OrderService  --->  IOrderRepository  <---  SqlOrderRepository
                        (in Infrastructure)
    the domain still references the infrastructure project. NOTHING was
    inverted; an interface was added.

  ACTUALLY INVERTED
    Domain project:          IOrderRepository      <- DEFINED HERE
                             OrderService
    Infrastructure project:  SqlOrderRepository : IOrderRepository
                             (references Domain)

    the compile-time arrow now points INWARD, from infrastructure to domain.
    the domain project has no reference to the infrastructure project at all.

  the mechanical test: open Domain.csproj. If it references
  Infrastructure, or any database package, inversion has not happened.
```

``` csharp
// the domain defines what it NEEDS, in its own vocabulary
namespace Acme.Domain;
public interface IOrderRepository
{
    Task<Order?> ByIdAsync(OrderId id, CancellationToken ct);
    Task SaveAsync(Order order, CancellationToken ct);
}
// note: no IQueryable, no DbSet, no SQL. If the interface mentions the
// database's concepts, the dependency was not inverted - it was renamed.

// infrastructure implements it, and depends on the domain
namespace Acme.Infrastructure;
internal sealed class SqlOrderRepository(AppDbContext db) : IOrderRepository { ... }

// composition root wires them, and is the ONLY place that knows both
services.AddScoped<IOrderRepository, SqlOrderRepository>();
```

> **\[Note\]**
>
> **Why this one matters most.** Dependency inversion is what makes the domain testable without a database, makes infrastructure replaceable, and makes business rules survive a framework rewrite. It is also the principle behind hexagonal, clean and onion architecture — all three are the same idea drawn differently. If you apply one principle from SOLID, apply this one.

###### The project structure that makes it real, and how it erodes

``` text
<!-- Domain.csproj - the whole test of whether inversion happened -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <!-- NO PackageReference. None at all, beyond the BCL. -->
</Project>

<!-- Application.csproj -->
<ItemGroup><ProjectReference Include="..\Domain\Domain.csproj" /></ItemGroup>

<!-- Infrastructure.csproj - depends INWARD -->
<ItemGroup>
  <ProjectReference Include="..\Domain\Domain.csproj" />
  <ProjectReference Include="..\Application\Application.csproj" />
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
</ItemGroup>

<!-- Api.csproj - the composition root references EVERYTHING,
     because it is the only place allowed to know both sides -->
```

``` text
  THE THREE WAYS IT ERODES, all of which compile

  1  THE INTERFACE LEAKS THE IMPLEMENTATION

       public interface IOrderRepository
       {
           IQueryable<Order> Query();          // <- this IS Entity Framework
       }

     the domain no longer references EF... except that IQueryable is EF's
     model, translated by EF, failing at runtime when EF cannot translate.
     The dependency was renamed, not inverted.

  2  A "SHARED" OR "COMMON" PROJECT

       Domain -> Shared -> EntityFrameworkCore

     the arrow still reaches the database. Shared projects are where
     inversion goes to die, because nobody audits them.

  3  ATTRIBUTES

       public class Order
       {
           [Column("order_total", TypeName = "decimal(19,4)")]   // <- EF
           public decimal Total { get; set; }
       }

     now the domain needs the EF package for its ATTRIBUTES.
     -> use Fluent API configuration in the infrastructure project instead.
        the domain class stays clean; the mapping lives where it belongs.
```

``` csharp
// the mapping, moved OUT of the domain
namespace Acme.Infrastructure.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.Property(o => o.Total).HasColumnName("order_total").HasColumnType("decimal(19,4)");
        b.OwnsMany(o => o.Lines, l => l.ToTable("order_lines"));
        b.Property<byte[]>("RowVersion").IsRowVersion();   // shadow property -
                                                            // concurrency token
                                                            // with no domain field
    }
}
// the domain class now has no attributes, no EF reference, and no
// knowledge that a database exists.
```

<a id="be-h731"></a>

#### DRY, YAGNI, KISS — and when they conflict

| Principle                           | Says                                                            | Failure when over-applied                              |
|-------------------------------------|-----------------------------------------------------------------|--------------------------------------------------------|
| **DRY**                             | Every piece of *knowledge* has one authoritative representation | Coupling unrelated code because it looked similar      |
| **YAGNI**                           | Do not build it until you need it                               | No extension points anywhere; every change is invasive |
| **KISS**                            | Prefer the simplest thing that works                            | Refusing abstraction that would genuinely help         |
| **Law of Demeter**                  | Talk to friends, not strangers                                  | Wrapper methods forwarding everything                  |
| **Principle of least astonishment** | Behave the way a reader expects                                 | — hard to over-apply                                   |

``` text
  DRY IS ABOUT KNOWLEDGE, NOT ABOUT TEXT

    these two look identical and are NOT duplication:

      decimal vatRate  = 0.21m;      // Dutch VAT, set by law
      decimal discount = 0.21m;      // our promotional discount

    unify them and the next VAT change silently alters the promotion.
    they are two different FACTS that currently share a value.

    and these two look different and ARE duplication:

      if (order.Total > 100) ...            // in the checkout service
      if (o.Sum(l => l.Price) > 100) ...    // in the reporting service

    the same RULE - "large order" - expressed twice. Change the threshold
    and one of them will be missed.

  the test is not "does this text repeat?" but
                 "if this fact changed, would I have to edit both?"
```

``` text
  WHEN THEY CONFLICT - a resolution order that works

    1  CORRECTNESS       nothing else matters if it is wrong
    2  CLARITY           the next reader is the primary user of this code
    3  DRY               remove genuine knowledge duplication
    4  FLEXIBILITY       extension points, where the need is demonstrated

  so:  duplication that is CLEARER than the abstraction stays.
       an abstraction that is unclear loses to duplication.
       and neither ever wins against correctness.

  "duplication is far cheaper than the wrong abstraction."
```

> **\[Note\]**
>
> **The rule of three, applied to all of them.** First occurrence: write it. Second: notice, and copy it. Third: now you have seen what actually varies, so abstract it. Abstracting at the first or second occurrence means guessing which axis varies, and a wrong abstraction is materially harder to remove than duplication — because callers have already built on it.

### Foundations

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Foundations</p>

<p class="pd">What a pattern actually is, why the vocabulary matters more than the code, and the two forces that every pattern in this guide is balancing.</p>

<a id="be-h722"></a>

#### Coupling and cohesion: the two forces underneath everything

Nearly every principle and pattern in this guide is an argument about these two properties. They are worth defining precisely, because they are used loosely far more often than they are used correctly.

``` text
  COUPLING - how much one unit depends on another

    LOW                                                        HIGH
    |                                                             |
    passes a value      calls an interface     calls a concrete class
                        it defined                    reaches into its fields
                                                      knows its database schema
                                                      shares its global state

    the test: "if I change B, must I change A?"
              if yes, A is coupled to B.

  COHESION - how related the things inside one unit are

    HIGH                                                        LOW
    |                                                             |
    everything serves    a class of related     a "Helpers" or "Utils"
    one purpose          operations             class. A "Manager".

    the test: "can I describe what this class does WITHOUT using
               the word 'and'?"

  the goal is LOW coupling and HIGH cohesion, and they trade off:
  splitting a class to raise cohesion creates a dependency between the
  halves. That trade is what most design arguments are actually about.
```

| Kind of coupling | Example                                     | Severity                                   |
|------------------|---------------------------------------------|--------------------------------------------|
| **Data**         | Passing a value as a parameter              | Lowest — unavoidable and fine              |
| **Interface**    | Depending on an abstraction you own         | Low — the target                           |
| **Concrete**     | `new SqlOrderRepository()` inside a service | Moderate — blocks substitution             |
| **Temporal**     | “Call `Init()` before `Run()`”              | High — invisible, and easy to get wrong    |
| **Content**      | Reaching into another object's internals    | High — any change breaks you               |
| **Common**       | Shared mutable static state                 | Highest — nothing is testable in isolation |

**Temporal coupling** deserves particular attention because it is the one that hides. A class requiring methods to be called in a specific order, with nothing in the type system enforcing it, is coupled to its caller in a way no interface expresses. The fix is usually to make the invalid state unrepresentable — take the dependency in the constructor, or return a different type from the first call that is the only thing the second call accepts.

> **\[Note\]**
>
> **A practical test you can apply in review.** Point at a class and ask two questions. *What does it depend on?* — if the list is long or contains concrete types, coupling is high. *What does it do?* — if the answer needs an “and”, cohesion is low. Almost every design smell in the anti-pattern part is a specific case of one of those two answers being bad.

###### Measuring it, rather than arguing about it

``` csharp
// COUPLING has a proxy metric you can read in ten seconds:
// the constructor parameter count.

public class OrderService(                    // 11 dependencies
    AppDbContext db, IEmailSender email, IPdfGenerator pdf,
    IWarehouseClient warehouse, IPricingEngine pricing, ILogger<OrderService> log,
    IConfiguration config, IMapper mapper, ICacheService cache,
    IEventPublisher events, ICurrentUser user) { }

//   1-3   healthy
//   4-5   watch it
//   6-7   cohesion is probably already gone
//   8+    split it today

// and the split is usually ALREADY VISIBLE in the parameter list:
//   db + mapper + cache        -> data access
//   email + pdf + warehouse    -> outbound integrations
//   pricing + events           -> domain operations
// those three groups are three services waiting to be extracted.
```

``` text
  COHESION, measured by the "and" test - applied to real class names

    OrderService                "creates orders AND prices them AND
                                 emails confirmations AND generates PDFs"
                                -> four responsibilities

    OrderPricingCalculator      "calculates the price of an order"
                                -> one. No "and" needed.

    DataHelper                  cannot be described at all
    OrderManager                "manages" is not a responsibility
    Utils                       the lowest-cohesion name available

  the naming heuristic that follows:
    if you cannot name a class without Manager, Helper, Util, Handler
    or Service, you have not yet identified what it does.

    "Service" is the acceptable exception, and only when qualified:
    OrderPricingService is fine. OrderService is a warning.
```

| Coupling smell             | Concretely                                                              |
|----------------------------|-------------------------------------------------------------------------|
| **Feature envy**           | A method uses another object's data more than its own — move the method |
| **Inappropriate intimacy** | Two classes reach into each other's internals                           |
| **Message chains**         | `a.B().C().D()` — you now depend on three types                         |
| **Shotgun surgery**        | One change requires editing eight files — coupling is too high          |
| **Divergent change**       | One file is edited for eight unrelated reasons — cohesion is too low    |

The last two rows are worth reading together, because they are opposites and the fix for each makes the other worse. Shotgun surgery says *gather these things*; divergent change says *split this thing*. Most design disagreements are two people correctly diagnosing different halves of that tension.
