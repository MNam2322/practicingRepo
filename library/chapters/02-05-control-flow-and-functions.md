---
id: "2-5"
number: "2.5"
title: "Control flow and functions"
part: 2
part_title: "From source code to running program"
words: 7125
minutes: 31
---

# 2.5 Control flow and functions

> **Quick look.** Choices, loops and functions in modern C#, and the traps in each.
>
> - && and || stop evaluating as soon as the answer is known.
> - Switch expressions and patterns test types, values, properties and list shapes in one step.
> - Changing a collection inside foreach throws.
> - LINQ queries and iterators run only when enumerated, and again every time you enumerate.
> - Lambdas and delegates pass behaviour as a value; a lambda that captures variables allocates each time.
> - Events are the most common managed memory leak: always unsubscribe.


### The big picture

Every program is built from three things: steps in order, choices, and repetition. Functions sit on top, packaging steps into reusable units. You know the basics already, so this chapter focuses on what modern C# adds and where the traps are.

That means conditions that skip their right-hand side, switch expressions that test the shape of data, loops that break when the collection changes under them, and queries that don't run when you write them. The chapter also covers functions as values, the idea behind LINQ and much of modern C#.

### Before you start

**Builds on:** 1.4 The processor, 2.4 Values, variables and references.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Dynamic programming** (3.8): Solving a problem by combining stored answers to overlapping smaller subproblems.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, records, collection expressions, lambdas, LINQ, switch expressions and patterns, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. In f != null && f.IsLate, why is there no crash when f is null?**

> `&&` uses short-circuit evaluation. If the left side is false, the right side is never evaluated. `||` skips its right side when the left side is true.

**2. What happens when no arm of a switch expression matches?**

> It throws `SwitchExpressionException` at run time. The compiler warns beforehand if it can see that some inputs aren't covered.

**3. What does yield return do?**

> It turns a method into an iterator. The method runs only as far as the next `yield return` each time the caller asks for another item, then pauses. Nothing runs until someone starts enumerating.

**4. Why can calling Count() and then looping over the same LINQ query do the work twice?**

> A LINQ query is a recipe, not a result. This is deferred execution. Each enumeration runs the recipe again from the start. Call `ToList()` once if you need the results more than once.

**5. What is a delegate?**

> A type-safe reference to a method. `Func<int, bool>` is a delegate type for methods taking an `int` and returning a `bool`. Lambdas are usually converted into delegates.

**6. What happens if you add to a List while a foreach loop is going over it?**

> The next step of the loop throws `InvalidOperationException`: "Collection was modified". Collect changes in a separate list, or loop over a copy.

**7. What is an extension method?**

> A static method that can be called as if it were an instance method of another type. All of LINQ is built from extension methods on `IEnumerable<T>`. C# 14 extends the idea to extension properties and other members.

### Three building blocks

Every program, however large, is built from three kinds of **control flow**. **Sequence**: do this, then that. **Choice**: do this or that depending on a condition. **Repetition**: do this again until a condition changes.

At the machine level, choice and repetition are just jumps, as The processor (1.4) showed. Early languages exposed those jumps directly with `goto`. Programs became tangles that were hard to follow. In 1968 Edsger Dijkstra argued that `goto` should go. **Structured programming** replaced arbitrary jumps with blocks that have one way in and one way out: `if`, loops and functions. C# still has `goto`, but almost nobody uses it.

### Conditions

A condition is an expression that gives a `bool`. Comparisons such as `==`, `<` and `>=` produce one. Logical operators combine them. `&&` means "and", `||` means "or" and `!` means "not".

`&&` and `||` use **short-circuit evaluation**. They stop as soon as the answer is known. In `a && b`, if `a` is false, `b` is never evaluated. This is what makes `f != null && f.IsLate` safe. It also means side effects on the right, such as a method call, may not happen.

Two rules, called De Morgan's laws, help untangle conditions. `!(a && b)` is the same as `!a || !b`. `!(a || b)` is the same as `!a && !b`. Negated conditions with several parts are a common source of mistakes, so rewriting them with these laws often makes the logic clearer.

### If, else and guard clauses

`if` runs a block only when a condition is true, with an optional `else` for the other case. Deeply nested `if` blocks are hard to read. A **guard clause** checks for a problem at the start of a method and returns or throws straight away. The rest of the method then handles only the normal case, without nesting.

The conditional operator `condition ? a : b` picks one of two values. The null-coalescing operator `a ?? b` gives `a` unless it is null, then `b`. `?.` calls a member only if the left side isn't null, and gives null otherwise.

### Switch and pattern matching

**Pattern matching** tests a value against a shape and pulls out parts of it in one step. C# has built up a rich set of patterns since C# 7.

A type pattern checks the type: `status is Airborne a`. A constant pattern checks a value: `0`. A relational pattern compares: `< 10_000`. Logical patterns combine others with `and`, `or` and `not`. A property pattern looks inside an object: `{ Delay.TotalMinutes: > 60 }`. A list pattern matches arrays and lists by position: `[var first, .., var last]`.

A **switch expression** picks a result using patterns, from top to bottom. The first matching arm wins. The compiler warns if an arm can never be reached, and if some inputs match no arm at all.

```csharp
// flow.cs — run with: dotnet run flow.cs
Console.WriteLine(Band(0));                      // on the ground
Console.WriteLine(Band(8_500));                  // low level
Console.WriteLine(Band(12_000));                 // mid level
Console.WriteLine(Band(37_000));                 // flight level 370

Console.WriteLine(Route(["LHR"]));               // just LHR
Console.WriteLine(Route(["LHR", "JFK"]));        // LHR to JFK, direct
Console.WriteLine(Route(["SIN", "DXB", "LHR", "JFK"]));  // SIN to JFK via 2 stops

var late = new Arrival("BA117", TimeSpan.FromMinutes(95));
Console.WriteLine(Advice(late));                 // BA117: rebook connections
Console.WriteLine(Advice(late with { Delay = TimeSpan.FromMinutes(20) }));  // BA117: on time or close

static string Band(int altitudeFt) => altitudeFt switch
{
    < 0 => throw new ArgumentOutOfRangeException(nameof(altitudeFt)),
    0 => "on the ground",
    < 10_000 => "low level",
    < 18_000 => "mid level",
    _ => $"flight level {altitudeFt / 100}",
};

static string Route(string[] legs) => legs switch
{
    [] => "no legs",
    [var only] => $"just {only}",
    [var from, var to] => $"{from} to {to}, direct",
    [var from, .. var middle, var to] => $"{from} to {to} via {middle.Length} stops",
};

static string Advice(Arrival a) => a switch
{
    { Delay.TotalMinutes: > 60 } => $"{a.Flight}: rebook connections",
    { Delay.TotalMinutes: > 30 and <= 60 } => $"{a.Flight}: warn connecting passengers",
    _ => $"{a.Flight}: on time or close",
};

record Arrival(string Flight, TimeSpan Delay);
```

### Loops

C# has four loops. `for` counts: start, condition, step. `foreach` walks through anything **enumerable**, meaning anything with a `GetEnumerator` method, such as arrays, lists and dictionaries. `while` checks its condition before each pass. `do` checks after, so it always runs at least once.

`break` leaves a loop. `continue` skips to the next pass.

The classic loop bug is being **off by one**: `i <= length` instead of `i < length`, or starting at 1 instead of 0. `foreach` avoids most of these by not using indexes at all.

Changing a collection while a `foreach` walks over it throws `InvalidOperationException`. The enumerator notices that the collection's version number has changed. Loop over a copy, collect the changes and apply them afterwards, or use `RemoveAll` for removals.

### Iterators and deferred execution

A method that uses `yield return` is an **iterator**. The compiler turns it into a small state machine. Each time the caller asks for the next item, the method runs until the next `yield return`, hands over that item and pauses. It resumes from the same point on the next request.

This means an iterator can describe an endless sequence, or a huge file read line by line, without holding it all in memory.

It also means nothing happens until someone enumerates. LINQ methods such as `Where` and `Select` are built this way. Building a query does no work. The work happens during `foreach`, `ToList`, `Count` or `First`. This is **deferred execution**. Enumerate the same query twice, and all the work runs twice.

```csharp
// lazy.cs — run with: dotnet run lazy.cs
using System.Linq;

IEnumerable<int> Numbers()
{
    for (int i = 1; i <= 3; i++)
    {
        Console.WriteLine($"  producing {i}");
        yield return i;
    }
}

var odd = Numbers().Where(n => n % 2 == 1);
Console.WriteLine("query built, nothing produced yet");
foreach (int n in odd) Console.WriteLine($"got {n}");
// producing 1, got 1, producing 2, producing 3, got 3

Console.WriteLine($"count: {odd.Count()}");      // produces all three again
var once = odd.ToList();                         // runs once, keeps the results
Console.WriteLine($"cached: {once.Count}, {once.Count}");   // no more producing
```

### Functions

A **function** is a named, reusable piece of code that takes parameters and may return a value. In C#, functions that belong to a type are called **methods**.

A method's **signature** is its name plus its parameter types. Two methods can share a name if their parameters differ. That is an **overload**. `Console.WriteLine` has many.

Parameters can have default values, which makes them optional: `void Log(string text, bool urgent = false)`. Callers can name arguments to make calls clearer: `Log("gate change", urgent: true)`. A `params` parameter accepts any number of arguments.

A method can return several values at once as a **tuple**: `(int Min, int Max) Range(int[] values)`. The caller can take it apart into separate variables, which is called **deconstruction**: `var (min, max) = Range(data);`.

Short methods can use an expression body: `int Square(int x) => x * x;`. A **local function** is a method declared inside another method. It keeps a helper next to the only place that uses it.

### Lambdas, delegates and higher-order functions

A **lambda expression** is a function without a name, written inline: `x => x * 2`, or `(a, b) => a + b`. Lambdas can capture variables around them, as Values, variables and references (2.4) explained.

A **delegate** is a type for references to methods. `Func<int, int>` is a delegate type for methods that take an `int` and return an `int`. `Action<string>` is for methods that take a `string` and return nothing. `Predicate<T>` returns a `bool`. A lambda, a method name or a local function can all be turned into a delegate.

A **higher-order function** takes a function as a parameter, returns one, or both. LINQ is full of them: `Where` takes a condition, `Select` takes a transformation and `OrderBy` takes a key picker. Higher-order functions let you separate what varies, the lambda, from what stays the same, the looping.

```csharp
// functions.cs — run with: dotnet run functions.cs
using System.Linq;

int[] delays = [120, 45, 300, 80, 15];

var (min, max) = Range(delays);
Console.WriteLine($"{min} to {max}");                     // 15 to 300

Func<int, bool> isLate = d => d > 60;                     // a lambda stored as a delegate
Console.WriteLine(delays.Count(isLate));                  // 3

Func<int, int> addBuffer = MakeAdder(10);                 // a function that returns a function
Console.WriteLine(string.Join(",", delays.Select(addBuffer)));  // 130,55,310,90,25

int tries = 0;
Console.WriteLine(Retry(() => ++tries >= 3, attempts: 5) ? $"succeeded after {tries} tries" : "gave up");  // succeeded after 3 tries

Log("gate change");
Log("runway closed", urgent: true);
Console.WriteLine("LHR".Twice());                         // LHRLHR: an extension method

static (int Min, int Max) Range(int[] values) => (values.Min(), values.Max());

static Func<int, int> MakeAdder(int amount) => x => x + amount;

static bool Retry(Func<bool> attempt, int attempts)
{
    for (int i = 0; i < attempts; i++)
        if (attempt()) return true;
    return false;
}

static void Log(string text, bool urgent = false) =>
    Console.WriteLine(urgent ? $"URGENT: {text}" : text);

static class StringExtensions
{
    public static string Twice(this string s) => s + s;
}
```

### Extension methods

An **extension method** is a static method whose first parameter is marked with `this`. It can then be called as if it belonged to that type: `"LHR".Twice()`. It gains no special access. It is still a static method outside the class, and the compiler just rewrites the call.

All of LINQ works this way. `Where`, `Select` and the rest are extension methods on `IEnumerable<T>`, which is why they work on every collection. C# 14 adds extension blocks, which can also add properties and static members to existing types.

### In short

`&&` and `||` stop evaluating as soon as the answer is known. Switch expressions and patterns test types, values, properties and lists in one step. Changing a collection during `foreach` throws. Iterators and LINQ queries run only when enumerated, and run again every time. Lambdas and delegates let you pass behaviour as a value, which is what makes higher-order functions such as LINQ possible.

### What goes wrong

**Side effects hidden by short-circuiting.** `if (IsReady() || Load())` never calls `Load` when `IsReady` is true. Don't rely on side effects inside conditions.

**Non-exhaustive switches.** A new enum value or subtype arrives, and a switch expression throws at run time. Treat the compiler's exhaustiveness warnings as errors, and think about what the default arm should really do.

**Changing a collection while looping over it.** "Collection was modified" exceptions. Loop over a copy or use `RemoveAll`.

**Multiple enumeration.** A LINQ query over a database, a file or an expensive calculation runs several times because it is enumerated several times. Materialize it once with `ToList()` or `ToArray()`. The .NET analyzers can warn about this.

**Deferred execution surprises.** A query built inside a `using` block is enumerated after the block ends, when its database connection or file is already closed. Materialize before leaving the block.

**Lambdas that capture too much.** A long-lived callback captures a large object and keeps it in memory. Capture only what the lambda needs.

### Cost model

`if`, `switch` and loops compile to comparisons and jumps, costing a cycle or so each, plus branch mispredictions when the data is unpredictable. A switch on many constant values often becomes a jump table, costing the same whatever the number of cases. Calling through a delegate costs a few nanoseconds and usually prevents inlining. An iterator allocates a small state-machine object. LINQ adds a delegate call per item per step. It is fast enough for most code and noticeably slower than a plain loop in tight inner loops, although recent .NET versions have closed much of the gap.

### In other languages (optional)

**JavaScript and TypeScript.** Functions are values, and arrow functions `x => x * 2` are the lambdas. `&&` and `||` return one of their operands rather than a `bool`, so `name || "unknown"` is a common default. `??` is the null-coalescing operator, and `?.` is optional chaining. Array methods like `map` and `filter` run immediately and build new arrays; generators with `function*` and `yield` are lazy.

**Python.** Uses indentation for blocks. `match` statements, added in 3.10, do structural pattern matching. `yield` makes generators, and generator expressions such as `(x * 2 for x in items)` are lazy. `and` and `or` short-circuit.

**SQL.** `CASE WHEN ... THEN ... ELSE ... END` is the switch expression. Whether the parts of a `WHERE` condition short-circuit, and in what order, is up to the optimizer, so never rely on it.

**F#.** Functions and pattern matching are the core of the language, and the compiler checks that every case is handled.

### Key terms

- **Control flow**: The order in which a program's statements run: sequence, choice and repetition.
- **Structured programming**: Building programs from blocks with one entry and one exit, instead of arbitrary jumps.
- **Short-circuit evaluation**: Stopping evaluation of a condition as soon as its answer is known, as `&&` and `||` do.
- **Guard clause**: A check at the start of a method that returns or throws early, so the rest handles only the normal case.
- **Pattern matching**: Testing a value against a shape and extracting parts of it in one step.
- **Switch expression**: An expression that picks a result using patterns, from top to bottom.
- **Enumerable**: Anything that can be walked through with foreach, by providing a GetEnumerator method.
- **Off by one**: A loop or index that runs one step too far or stops one step too soon.
- **Iterator**: A method that uses yield return to hand out items one at a time, pausing between them.
- **Deferred execution**: Building a query without running it. The work happens only when it is enumerated.
- **Function**: A named, reusable piece of code that takes parameters and may return a value.
- **Overload**: One of several methods sharing a name but taking different parameters.
- **Tuple**: A small group of values handled as one, such as `(int Min, int Max)`.
- **Deconstruction**: Taking a tuple or record apart into separate variables.
- **Lambda expression**: A function without a name, written inline, such as `x => x * 2`.
- **Delegate**: A type for references to methods, such as `Func<int, bool>`.
- **Higher-order function**: A function that takes or returns other functions. Most of LINQ is built from them.
- **Extension method**: A static method that can be called as if it were an instance method of another type.

### Where this leads

How long the variables used by functions live is in Scope and lifetime (2.6). Recursion, where a function calls itself, is in Recursion and dynamic programming (3.8). Choosing between loops, LINQ and other styles is in Procedural, object-oriented and functional styles (4.1). Functions without side effects are in Immutability and pure functions (4.4). Asynchronous methods, which are state machines like iterators, are in Async and await (5.3). LINQ translated into SQL is in ORMs and EF Core (7.7).

### Check yourself

1. Add a case to `Advice` in `flow.cs` for flights with a negative delay, meaning early. Put it in the right position and check that the compiler complains if you put it in the wrong one.
2. Rewrite `if (!(isOpen && hasCapacity)) Reject();` using De Morgan's laws, and check both versions give the same result for all four combinations.
3. Write an iterator `Fibonacci()` that never ends. Use `Take(10)` to print the first ten numbers.
4. Write a higher-order function `Timed<T>(Func<T> work)` that runs `work`, prints how long it took and returns its result.
5. Find a LINQ query in real code that is enumerated more than once. Decide whether that is a bug.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A query that ran three times

A dashboard method builds a LINQ query, checks `Any()`, takes `Count()`, then loops over it. The database log shows three identical queries.

*Materialize once*

```csharp
var delayed = await db.Flights
    .Where(f => f.Status == FlightStatus.Delayed)
    .Select(f => new { f.Number, f.DelayMinutes })
    .ToListAsync(ct);                     // one query

if (delayed.Count == 0) return Results.NoContent();
var avg = delayed.Average(f => f.DelayMinutes);
```

> **Lesson:** LINQ queries run each time you enumerate them. Materialize with ToList once when you'll use the results several times.

#### Example 2: A switch that missed the new status

A new `Diverted` flight status ships. The fee calculator silently returns 0 for it.

*Exhaustive switch expression*

```csharp
decimal ChangeFee(FlightStatus s) => s switch
{
    FlightStatus.Scheduled => 50m,
    FlightStatus.Delayed   => 0m,
    FlightStatus.Cancelled => 0m,
    FlightStatus.Diverted  => 0m,
    _ => throw new ArgumentOutOfRangeException(nameof(s), s, "Unhandled status"),
};
// <WarningsAsErrors>CS8509;CS8524</WarningsAsErrors> turns missing cases into build errors.
```

> **Lesson:** Make switches exhaustive and fail loudly on unknown values, so a new case breaks the build rather than the books.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a13"></a>

#### The C# building blocks used everywhere

A handful of language features appear on nearly every page of this guide and are rarely explained anywhere, because they are assumed. If `sealed`, a lambda, a closure or an attribute is not immediately obvious to you, this section is the missing piece. If they are, skip it.

##### Delegates, lambdas and closures

``` text
  a DELEGATE is a variable that holds a METHOD.

    Func<int, int> double_ = Multiply;      // points at a method
    int Multiply(int x) => x * 2;
    double_(21);                             // calls it -> 42

    the built-in shapes cover almost everything:
      Action              takes nothing, returns nothing
      Action<T>           takes a T, returns nothing
      Func<TResult>       takes nothing, returns a TResult
      Func<T, TResult>    takes a T, returns a TResult
      Predicate<T>        takes a T, returns bool

  a LAMBDA is a method written inline, with no name:

    Func<int, int> double_ = x => x * 2;
                             ^^^^^^^^^^ this IS the method

  a CLOSURE is a lambda that USES A VARIABLE FROM OUTSIDE ITSELF:

    int factor = 3;                          // a local
    Func<int, int> scale = x => x * factor;  // CAPTURES factor

    `factor` lives in this method's stack frame, which disappears when
    the method returns - but `scale` may outlive it. So the compiler
    MOVES factor to a heap object and both refer to that.

    -> a closure ALLOCATES. A lambda capturing nothing does not.
    -> and the capture is BY REFERENCE: changing factor afterwards
       changes what scale computes.
```

``` csharp
// the capture-by-reference behaviour, which surprises people
int factor = 3;
Func<int, int> scale = x => x * factor;
Console.WriteLine(scale(10));     // 30
factor = 5;                       // the LOCAL changed...
Console.WriteLine(scale(10));     // 50  <- ...and so did the lambda

// `static` on a lambda forbids capture, and the compiler ENFORCES it.
// useful on hot paths, where an accidental capture allocates per call.
Func<int, int> pure = static x => x * 2;      // compile error if it captures
```

##### Expression trees: code as data

``` text
  the SAME source text means two different things depending on the type
  you assign it to. This is the single most surprising thing in C#, and
  it is what makes EF Core possible.

    Func<Order, bool>            f = o => o.Total > 100;
      -> COMPILED. A method you can call. Opaque.

    Expression<Func<Order, bool>> e = o => o.Total > 100;
      -> a DATA STRUCTURE describing the code:

           Lambda
             Body: GreaterThan
               Left:  MemberAccess(o, "Total")
               Right: Constant(100)
             Parameters: [ o ]

      -> nothing has been compiled. Something else can WALK this tree
         and turn it into something else entirely - such as:

           WHERE total > 100

  which is why:
    IEnumerable<T>.Where takes a Func      -> runs in memory, in C#
    IQueryable<T>.Where  takes an Expression -> translated to SQL

  and why a method call inside a query throws at RUNTIME: EF walks the
  tree, meets MethodCall(IsActive), has no SQL for it, and gives up.
```

##### Attributes: metadata attached to code

``` csharp
// an ATTRIBUTE is data attached to a type or member, in square brackets.
// it does NOTHING by itself - something must read it.

[Authorize(Policy = "orders:write")]        // ASP.NET Core reads this
[Obsolete("Use PlaceOrderV2")]              // the COMPILER reads this
public sealed class PlaceOrderHandler { }

// reading them yourself, which is what every framework is doing:
var attr = typeof(PlaceOrderHandler).GetCustomAttribute<AuthorizeAttribute>();
if (attr is not null) { /* the policy is attr.Policy */ }

// this is REFLECTION - inspecting types at run time. It is how model
// binding, validation, serialisation and DI registration-by-convention
// all work, and it is why those things break under Native AOT: the
// compiler cannot see a type that is only named in a string.
```

##### Extension methods

``` csharp
// a STATIC method that can be CALLED AS IF it were an instance method.
public static class StringExtensions
{
    public static bool IsNullOrBlank(this string? value)   // note `this`
        => string.IsNullOrWhiteSpace(value);
}

name.IsNullOrBlank();              // reads like an instance method
StringExtensions.IsNullOrBlank(name);   // identical - this is what it compiles to

// the entire LINQ surface is extension methods on IEnumerable<T>:
//   Where, Select, OrderBy, First, Any, Sum, GroupBy...
// which is why `using System.Linq;` makes them appear, and why removing
// the using makes them vanish with a confusing error.

// they cannot access private members, and an instance method ALWAYS
// wins over an extension method with the same signature.
```

##### The modifiers, and what each actually prevents

| Modifier     | Prevents                                 | Why you would use it                                                                               |
|--------------|------------------------------------------|----------------------------------------------------------------------------------------------------|
| **`sealed`** | Inheriting from the class                | **The default choice.** Says “this is not an extension point”, and lets the JIT devirtualise calls |
| `static`     | Instantiation; per-instance state        | Utilities, extension classes. **Static mutable state is shared by every thread**                   |
| `readonly`   | Reassignment after construction          | Fields that must not change. On a `struct`, makes the whole type immutable                         |
| `const`      | Any change without recompiling *callers* | Truly fixed values only — the value is copied into every consumer at build time                    |
| `init`       | Assignment after the object initialiser  | Immutable properties that are still easy to construct                                              |
| `required`   | Constructing without setting it          | Turns “you forgot a property” into a compile error                                                 |
| `internal`   | Use from another assembly                | The default for top-level types; keeps your public surface small                                   |
| `virtual`    | — it *permits* overriding                | An intentional extension point. Costs a virtual dispatch                                           |

The `const` row is worth a moment: because the value is baked into every calling assembly at compile time, changing a `public const` in a library and shipping only that library leaves every consumer using the old value until *they* recompile. Use `static readonly` for anything that might ever change.

##### IDisposable and using

``` csharp
// IDisposable means "I hold something that must be released" - a file
// handle, a socket, a database connection, an unmanaged buffer.
// the GC frees MEMORY; it does not know about any of those.

using var conn = new NpgsqlConnection(cs);     // disposed at end of scope
await conn.OpenAsync(ct);
// ...
// conn.Dispose() runs here, automatically, EVEN IF an exception is thrown.

// which is exactly equivalent to:
var conn = new NpgsqlConnection(cs);
try { /* ... */ }
finally { conn.Dispose(); }

// and the async form, for anything releasing over a network
await using var scope = scopeFactory.CreateAsyncScope();

// FORGETTING using is the cause of most connection-pool exhaustion:
// the connection is not returned, the pool empties, and the error
// blames the database.
```

##### Tuples, deconstruction and pattern matching

``` csharp
// a TUPLE groups values with no type declaration
(string Name, int Age) person = ("Nam", 34);
Console.WriteLine(person.Name);

// returning two things without inventing a type
static (bool Ok, string? Error) Validate(Order o) =>
    o.Lines.Count == 0 ? (false, "No lines") : (true, null);

// DECONSTRUCTION unpacks into separate variables
var (ok, error) = Validate(order);

// PATTERN MATCHING tests shape and extracts, in one expression
var description = shape switch
{
    Circle { Radius: > 100 } c => $"Big circle, r={c.Radius}",   // property pattern
    Circle c                   => $"Circle, r={c.Radius}",
    Rectangle(var w, var h)    => $"{w} by {h}",                 // positional
    null                       => "nothing",
    _                          => "something else"
};

if (result is Success { Value: var v }) Use(v);     // test AND extract
```

> **\[Note\]**
>
> **Where each of these shows up later.** Lambdas and closures underpin every LINQ query and every DI factory. Expression trees are why EF Core can produce SQL, and why a method call inside a query fails at runtime. Attributes plus reflection are how model binding, validation and DI-by-convention work — and why they break under Native AOT. `IDisposable` is behind connection pooling and scope disposal. If a later section assumes one of these, this is the page to come back to.

<a id="be-a20"></a>

#### Delegates, events, lambdas and expression trees

A delegate is a typed function pointer. `Func<A,B>` returns a value, `Action<A>` does not, `Predicate<T>` is `Func<T,bool>` by another name. Events are delegates with restricted access: subscribers can only `+=` and `-=`, not invoke or overwrite.

``` csharp
// lambda -> compiled delegate, you can call it
Func<Order, bool> isBig = o => o.Total > 100;
bool r = isBig(order);

// lambda -> expression tree, you can INSPECT it
Expression<Func<Order, bool>> tree = o => o.Total > 100;
Console.WriteLine(tree.Body);   // (o.Total > 100)

// this is how EF Core turns your Where() into SQL
```

> **\[Trap\]**
>
> **Common trap.** Event subscriptions are the classic managed memory leak. A long-lived publisher holding a delegate to a short-lived subscriber keeps that subscriber alive forever. Always unsubscribe, or use weak-event patterns.

###### Lambdas vs expression trees

The same lambda syntax compiles to two completely different things depending on the target type. Assigned to `Func<T,bool>` it becomes a compiled method plus a delegate: fast, opaque, executable only. Assigned to `Expression<Func<T,bool>>` it becomes a *data structure* describing the code — a tree of nodes another library can read. That single distinction is why `Where(o => o.Total > 100)` becomes SQL on `IQueryable` and runs in memory on `IEnumerable`.

``` csharp
Func<Order, bool>              compiled = o => o.Total > 100;   // executable
Expression<Func<Order, bool>>  tree     = o => o.Total > 100;   // inspectable

// the tree can be read, rewritten, or translated:
var body = (BinaryExpression)tree.Body;
Console.WriteLine(body.NodeType);           // GreaterThan
Console.WriteLine(body.Left);               // o.Total
var runnable = tree.Compile();              // → delegate, at a cost (~µs)

// This is the accidental-full-table-scan mechanism:
IQueryable<Order> q = db.Orders;
q.Where(o => o.Total > 100)         // translated → WHERE Total > 100
 .AsEnumerable()                     // ← everything after this is in memory
 .Where(o => Score(o) > 0.5);        // C# method: cannot be translated
```

> **\[Trap\]**
>
> **Common trap.** A multicast delegate with a return value only gives you the *last* handler’s result — the rest are silently discarded. And one throwing handler aborts the remaining invocations. If every subscriber matters, iterate `GetInvocationList()` yourself and aggregate.

> **\[Trap\]**
>
> **`async void` handlers.** Event signatures return `void`, which tempts `async void`. An exception inside one cannot be caught by the caller and takes the process down. Keep the body inside a try/catch, or route to an async method you can await somewhere that owns the error.

<a id="be-a27"></a>

#### Pattern matching and modern syntax

Pattern matching has grown from a switch replacement into a genuine way to express branching logic. Modern C# code reads very differently from C# 5 code, and interviewers notice.

``` csharp
// switch expression + property, relational, logical and list patterns
decimal Fee(Payment p) => p switch
{
    { Method: Card, Amount: > 1000m } => p.Amount * 0.015m,
    { Method: Card }                  => p.Amount * 0.025m,
    { Method: Transfer, Currency: "EUR" or "GBP" } => 0m,
    null => throw new ArgumentNullException(nameof(p)),
    _    => 1.50m
};

// type pattern with narrowing
if (shape is Circle { Radius: > 0 } c) Use(c.Radius);

// collection expressions (C# 12) and primary constructors
int[] xs = [1, 2, 3, ..more];
public class Service(IRepo repo, ILogger<Service> log) { }
```

Other things worth having in your fingers: file-scoped namespaces, global usings, `required` members, `init` setters, target-typed `new()`, raw string literals for JSON and SQL, and top-level statements in `Program.cs`.

###### Exhaustiveness, and when a switch replaces polymorphism

``` csharp
// the compiler checks exhaustiveness for switch EXPRESSIONS, not statements
string Describe(Shape s) => s switch
{
    Circle c    => $"circle r={c.Radius}",
    Square sq   => $"square {sq.Side}",
    // no default: warning CS8509 - "not all possible values are covered"
    // and at runtime an unmatched value throws SwitchExpressionException
};

// exhaustiveness is only PROVABLE for closed hierarchies.
// mark the base abstract and the derived types sealed, in one assembly:
public abstract record Shape;
public sealed record Circle(double Radius) : Shape;
public sealed record Square(double Side)   : Shape;
// now adding a third shape produces a WARNING at every switch that missed it -
// which is the entire value of this pattern.

// enums are never exhaustive: any int can be cast to any enum
DayOfWeek d = (DayOfWeek)99;   // legal. Always handle the default case.
```

| Pattern    | Syntax                 | Note                                        |
|------------|------------------------|---------------------------------------------|
| Type       | `is Circle c`          | Declares and assigns in one step            |
| Property   | `{ Total: > 100 }`     | Nests arbitrarily deep                      |
| Positional | `Circle(> 5)`          | Requires `Deconstruct`; records get it free |
| Relational | `>= 18 and < 65`       | Combine with `and`, `or`, `not`             |
| List       | `[first, .., last]`    | C# 11+; matches on shape and position       |
| `var`      | `var x when x.IsValid` | Always matches; useful for a guard          |
| Discard    | `_`                    | The explicit catch-all                      |

**Pattern matching fits when**

- The behaviour belongs to the *caller*, not the type — formatting, mapping, routing
- The type hierarchy is closed and stable
- You are matching on shape or value, not identity
- Adding a case should force every switch to be revisited

**Polymorphism fits when**

- The behaviour genuinely belongs to the type
- New types are added often, by other people
- The same switch would otherwise appear in five places
- You would be switching on a type to call a method it could implement

The heuristic: **if the same set of cases appears in more than two switches, the behaviour wants to live on the type.** A single switch mapping a domain type to a DTO is good design. Five switches over the same hierarchy, scattered, is a virtual method someone declined to write.

### Delegates, Lambdas, and Closures

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Delegates, Lambdas, and Closures</p>

<p class="pd">A whole category of type that isn&#x27;t a struct or a class in the usual sense, and the place where hidden allocations come from.</p>

<a id="be-b250"></a>

#### A delegate is a typed function pointer — plus a target

``` csharp
delegate int Transform(int x);          // declares a TYPE
Transform t = x => x * 2;
t(5);                                   // 10
```

At runtime a delegate instance holds two things: a **method pointer** and a **target** — the object the method is called on, or null for a static method. That pairing is the whole idea: it lets you pass "this method, on this instance" as a value.

`Func<>` and `Action<>` cover almost every case, so you rarely declare your own:

``` text
Action                  // void, no args
Action<T>               // void, takes T
Func<TResult>           // returns TResult
Func<T, TResult>        // takes T, returns TResult
Predicate<T>            // Func<T,bool> by another name
```

**Delegates are multicast.** One delegate can hold an invocation list of several methods:

``` csharp
Action a = () => Console.Write("1");
a += () => Console.Write("2");
a();                                     // prints 12
```

Two traps in that. For a `Func<>`, **only the last return value survives** — the others are computed and discarded. And if one handler throws, the remaining ones **never run**, because the exception propagates out of the combined invocation. If handlers must be independent, iterate `GetInvocationList()` and wrap each call.

**Delegates are immutable.** `a += b` doesn't modify `a`; it creates a new delegate and reassigns. That's what makes the `-=` removal idiom work and why `event` accessors need interlocked updates to be thread-safe.

<a id="be-b251"></a>

#### Events are delegates with restricted access

``` csharp
public event EventHandler<OrderEvent>? Placed;

// raise it safely — the local copy avoids a null-race if the last handler
// unsubscribes between the check and the call
Placed?.Invoke(this, args);
```

`event` is a delegate field plus generated `add`/`remove` accessors. The restriction is the point: outside code can subscribe and unsubscribe, but cannot **invoke** it, **assign** it (wiping everyone else's handlers), or read the invocation list. Without `event`, any consumer could do `Placed = null`.

**Events are the most common managed memory leak.** A subscription is a reference *from the publisher to the subscriber*:

``` csharp
longLivedService.DataChanged += shortLivedView.OnChanged;
// longLivedService now holds a reference to shortLivedView — forever
```

The view can never be collected while the service lives. Unsubscribe in `Dispose`, or use weak event patterns. This is why an app's memory grows as users navigate between screens.

<a id="be-b252"></a>

#### Lambdas, and what they actually allocate

This is the part with real performance consequences.

**A lambda capturing nothing** is cached — the compiler creates one static instance and reuses it. Zero allocation per call:

``` csharp
list.Where(x => x > 5);          // no capture → cached delegate, allocated once ever
```

**A lambda capturing a local** requires that local to outlive the method, so the compiler generates a hidden class — a **display class** — hoists the variable into a field of it, and allocates one per invocation:

``` csharp
void M(int threshold)
{
    list.Where(x => x > threshold);   // captures threshold
}

// the compiler generates roughly:
sealed class <>c__DisplayClass0
{
    public int threshold;
    public bool <M>b__0(int x) => x > this.threshold;
}
```

So that call allocates a display class **and** a delegate, every time `M` runs. In a hot loop that's the entire cost.

**Capturing `this` is the subtle one.** Referencing any instance field captures the whole object:

``` csharp
class Service
{
    private int _limit;
    void M() => list.Where(x => x > _limit);   // captures THIS, not _limit
}
```

The lambda now keeps the entire `Service` alive. If that lambda is stored somewhere long-lived — an event handler, a cache, a timer callback — you've leaked the service and its whole object graph. The fix is to copy to a local first: `int limit = _limit;` then capture `limit`.

**Capture is by reference, not by value.** The lambda sees the variable, not a snapshot of it:

``` csharp
int i = 0;
Action print = () => Console.Write(i);
i = 42;
print();                                  // 42, not 0
```

Which explains the classic loop bug:

``` csharp
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.Write(i));   // ALL THREE share one variable → 3,3,3

for (int i = 0; i < 3; i++)
{
    int copy = i;                          // a new variable per iteration
    actions.Add(() => Console.Write(copy));// → 0,1,2
}
```

`foreach` was changed in C# 5 to declare its variable inside the loop, so it behaves correctly. `for` was not, and still bites.

**Avoiding the allocation** when it matters — pass state through an overload instead of capturing:

``` csharp
// allocates a display class per call
dict.GetOrAdd(key, k => Build(k, config));

// no capture: state is passed as an argument
dict.GetOrAdd(key, static (k, cfg) => Build(k, cfg), config);
```

`static` on a lambda (C# 9) is a **compiler-enforced guarantee that nothing is captured** — it won't compile if you accidentally reference a local or a field. Use it liberally; it turns an invisible performance question into a compile error.

<a id="be-b253"></a>

#### Expression trees — code as data

``` csharp
Func<int, bool> f = x => x > 5;              // compiled delegate — executable
Expression<Func<int, bool>> e = x => x > 5;  // a TREE describing the code
```

Identical syntax, completely different object. The second doesn't execute anything; it builds a data structure:

``` text
LambdaExpression
 └── BinaryExpression (GreaterThan)
      ├── ParameterExpression (x)
      └── ConstantExpression (5)
```

Something else can then walk that tree and translate it. **This is how `IQueryable` becomes SQL**: EF Core inspects the tree, sees a `GreaterThan` between a mapped column and a constant, and emits `WHERE x > 5`.

Which explains the failure mode in VII.4 precisely. When you write something EF can't translate — a call to your own method, or an unsupported BCL method — it can't turn that node into SQL. Depending on version and configuration it either throws or silently switches to client evaluation, pulling the table into memory and filtering there. **The reason "it worked on 50 rows and died on 5 million" is that the expression tree stopped being translatable and nobody noticed.**

You can also build trees at runtime and `Compile()` them into delegates — the basis of fast serializers, mappers, and DI containers that avoid reflection on every call.

------------------------------------------------------------------------

### Generics and modern data features

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Generics and modern data features</p>

<a id="be-m62"></a>

#### Collection expressions and spreads

C# 12 gave one literal syntax for every collection shape, and the compiler picks an efficient construction strategy for the target type.

``` csharp
int[]              a = [1, 2, 3];
List<int>          b = [1, 2, 3];
Span<int>          c = [1, 2, 3];        // may be stack-allocated - no heap at all
ImmutableArray<int> d = [1, 2, 3];
int[]              e = [];               // uses Array.Empty<int>(), zero allocation

int[] combined = [..a, 0, ..b];          // spread operator
ReadOnlySpan<char> separators = [',', ';', '\t'];   // no array allocated
```

The genuinely useful case is passing a small set to a `ReadOnlySpan` parameter. Previously `Split(new[] { ',', ';' })` allocated an array on every call; a collection expression targeting a span does not.

``` csharp
// Your own types opt in with a builder:
[CollectionBuilder(typeof(MyListBuilder), nameof(MyListBuilder.Create))]
public readonly struct MyList<T> { ... }
```

**Other modern data-shaping features worth having in reach:**

``` csharp
// Primary constructors on classes and structs (C# 12)
public class OrderService(IRepo repo, ILogger<OrderService> log) { ... }

// Required members - compile-time enforcement without a constructor
public class Dto { public required string Name { get; init; } }

// init-only setters - immutable after construction, still object-initialiser friendly
// field keyword (C# 13) - a property body without declaring the backing field
public string Name { get => field; set => field = value.Trim(); }
```

> **\[Tip\]**
>
> In a legacy codebase these are the cheapest modernisations available: they are local, they do not change runtime behaviour, and `required` in particular converts a whole class of "forgot to set a property" runtime bugs into compile errors.

### Optimization in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Optimization in practice</p>

<a id="be-m280"></a>

#### Closures and delegate allocation

A lambda that captures nothing is cached in a static field and allocates once per process. A lambda that captures anything allocates a *display class* holding the captured variables, plus a delegate, on every execution of the enclosing scope.

``` csharp
// Zero allocation after first call
items.Where(static x => x.IsActive);

// Allocates a display class + delegate per call
int min = GetMin();
items.Where(x => x.Value > min);

// Worse: a capture inside a loop allocates per iteration
foreach (var k in keys)
    tasks.Add(Task.Run(() => Process(k)));   // one closure per key
```

Several BCL APIs offer a state parameter specifically to avoid the capture:

``` csharp
Task.Run(state => Process((string)state), k);
dict.GetOrAdd(key, static (k, arg) => Create(k, arg), argument);   // ConcurrentDictionary
CancellationToken.Register(static s => ((MyType)s!).Cancel(), this);
string.Create(len, state, static (span, s) => { ... });
```

> **\[Trap\]**
>
> **Accidental capture of `this`.** Referring to any instance field or method inside a lambda captures the entire enclosing object, not just that field. In a long-lived subscription (an event handler, a timer callback, a cached delegate) that keeps the whole object alive — a classic managed memory leak. Copy the value you need into a local first, or use `static` plus an explicit state argument.

> **\[Tip\]**
>
> Adding `static` to a lambda costs nothing and turns silent capture into a compile error. It is worth applying by default in library and hot-path code — you find surprising captures immediately.

Foundations: [A worked trace: where everything actually goes](01-05-memory.md#be-m26)
