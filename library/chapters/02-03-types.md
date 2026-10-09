---
id: "2-3"
number: "2.3"
title: "Types"
part: 2
part_title: "From source code to running program"
words: 15113
minutes: 66
---

# 2.3 Types

> **Quick look.** Types tell the program what bits mean and let the compiler catch mistakes before the code runs.
>
> - Static typing checks at compile time; var still gives a fixed type.
> - Nullable reference types are compile-time warnings only: data from JSON, EF or reflection can still be null.
> - Generics in .NET are reified: one type-safe implementation with no boxing for value types.
> - Use small wrapper types for ids and units so mix-ups become compile errors (avoid primitive obsession).
> - Structs only for small, immutable values; mutable structs are a documented trap.
> - If you override Equals you must override GetHashCode, and dictionary keys must never change.
> - record class still allocates on the heap; use readonly record struct for small values.
> - Random v4 Guids make poor clustered database keys; prefer sequential ids.


### The big picture

Memory holds only bits. A type tells the program what those bits mean and what you may do with them. Types are also the compiler's main tool for catching mistakes before the program ever runs: adding a date to a price, passing an aircraft id where a flight id belongs, or using a value that might be missing.

This chapter explains how C# checks types, how it handles missing values (null), how generics let one piece of code work safely with many types, and how to design small types that make whole classes of bugs impossible to write. That last idea matters in aviation, where unit mix-ups have made real aircraft run out of fuel.

### Before you start

**Builds on:** 1.2 Numbers inside the machine, 1.5 Memory.

**Ideas explained later:**

- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.
- **Control flow** (2.5): The order in which a program's statements run: sequence, choice and repetition.
- **Inheritance** (4.1): A class taking over and extending the members of a base class.
- **Metric** (11.6): A number measured repeatedly over time and aggregated as it is recorded.
- **Pattern matching** (2.5): Testing a value against a shape and extracting parts of it in one step.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, primary constructors, records, generic types, lambdas, null operators, switch expressions and patterns, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between static and dynamic typing?**

> With static typing, types are checked when the code is compiled, and wrong code doesn't build. With dynamic typing, types are checked while the code runs, and wrong code fails when that line executes. C# is static. Python and JavaScript are dynamic.

**2. Does var make C# code dynamically typed?**

> No. `var` asks the compiler to work out the type from the right-hand side. The variable still has one fixed type, checked at compile time. This is type inference. `dynamic` is the keyword that really turns off compile-time checks.

**3. What do nullable reference types check, and when?**

> They make the compiler track which references may be null and warn when you might use one without checking. It happens only at compile time. At run time, nothing stops a null arriving, for example from JSON or older code.

**4. Why is List<int> better than the old ArrayList?**

> It is type-safe, so only ints go in and you get ints out without a cast. And it stores the ints directly, with no boxing. .NET generics are kept at run time and specialized for value types.

**5. In TypeScript, can you pass an object to a function expecting a Point if you never declared it as a Point?**

> Yes, as long as it has the right shape. TypeScript uses structural typing: types match if their members match. C# uses nominal typing: the declared type name must match.

**6. An enum Status has values 0, 1 and 2. Can a Status variable hold 7?**

> Yes. `(Status)7` compiles and runs. An enum is just a named integer type. Validate values that come from outside with `Enum.IsDefined` or a switch with a default case.

**7. What is a strongly typed ID, and what does it prevent?**

> A small wrapper type such as `FlightId` instead of a bare `int` or `Guid`. It stops you passing a flight id where an aircraft id belongs, because the compiler sees two different types.

### What a type is

Memory holds only bits, as Part 1 showed. The same 32 bits could be an `int`, a `float`, four ASCII characters or part of an address. A **type** tells the program how to read the bits and what you are allowed to do with them.

A type is a set of possible values plus the operations allowed on them. An `int` is any whole number from about −2.1 billion to 2.1 billion, with arithmetic and comparison. A `string` is any sequence of characters, with operations like `Length`, `Substring` and concatenation.

Types catch mistakes. Adding a number to a date, calling a method that doesn't exist or passing text where a number belongs are all type errors. The question is when they get caught.

### Static and dynamic typing

In a **statically typed** language, every variable and expression has a type known at compile time. The compiler checks every use before the program runs. C#, Java, Go, Rust and TypeScript are static.

In a **dynamically typed** language, values carry their types, but variables don't. A variable can hold a number now and a string later. Checks happen while the code runs, when an operation is actually attempted. Python, JavaScript and Ruby are dynamic.

Static typing finds whole classes of bugs before anything runs. It also powers editor features: autocomplete, go-to-definition and safe renaming all rely on knowing types. Dynamic typing makes small scripts quicker to write and some kinds of code more flexible. Large codebases in dynamic languages usually add optional type hints, such as TypeScript for JavaScript or type annotations checked by mypy for Python.

### Strong and weak typing

A separate question is how strictly a language treats mixed types. The terms are used loosely, but the idea is clear.

A language with **strong typing** refuses to silently mix unrelated types. In Python, `"1" + 1` raises an error.

A language with weaker typing converts values automatically to make an operation work. In JavaScript, `"1" + 1` is `"11"`, and `"5" * "2"` is `10`. These automatic conversions are called **coercion**. They are convenient in small scripts and a famous source of bugs in larger ones.

C# is static and fairly strong. It allows safe conversions such as `int` to `long` automatically, and requires an explicit cast for anything that might lose information, as covered in Numbers inside the machine (1.2).

### Type inference

Static typing doesn't mean writing every type by hand. **Type inference** lets the compiler work types out from context.

`var count = 0;` makes `count` an `int`. `var names = new List<string>();` makes it a `List<string>`. Lambdas infer their parameter types from where they are used: in `flights.Where(f => f.IsLate)`, `f` is known to be a `Flight`.

The reverse works too. **Target-typed** `new` takes its type from the variable: `List<string> names = new();`. Collection expressions, added in C# 12, do the same: `int[] numbers = [1, 2, 3];`.

Inference never changes the rules. The variable still has exactly one type, fixed at compile time.

### The C# type system

C# has a small set of kinds of type.

Built-in types include `int`, `double`, `decimal`, `bool`, `char` and `string`. **Structs** are value types and **classes** are reference types, as described in Memory (1.5). **Records** are classes or structs with value-based equality, covered in Values, variables and references (2.4). **Enums** are named sets of integer constants. **Interfaces** describe a set of members a type promises to have. **Delegates** are types for references to methods, covered in Control flow and functions (2.5).

Every type ultimately derives from `object`. That is called a unified type system. It means any value can be treated as an `object`, with boxing for value types.

### Null

A reference can point at nothing: `null`. Using a null reference throws `NullReferenceException`, the most common crash in .NET code. Tony Hoare, who invented null references in 1965, later called them his "billion-dollar mistake".

Value types can't be null. An `int` always holds a number. `int?`, short for `Nullable<int>`, is a small struct that holds an `int` plus a flag saying whether it has a value.

C# 8 added **nullable reference types**. With the feature on, `string` means "never null" and `string?` means "may be null". The compiler tracks this through your code. It warns when you might use a `string?` without checking, and when you might put null into a `string`. The `!` operator tells the compiler "I know this isn't null" and silences the warning. Every `!` is a claim you have to be sure of.

The checks are compile-time only. JSON deserialization, reflection and older libraries can still hand you nulls where the types say there are none. Validate data at the boundaries of your program.

### Generics

**Generics** let you write a type or method once and use it with many types. `List<T>` works as `List<int>`, `List<string>` or `List<Flight>`. The `T` is a **type parameter**, filled in when the type is used.

A **type constraint** limits what `T` can be, so the code can rely on its members. `where T : IComparable<T>` means `T` can be compared. `where T : class` means `T` is a reference type. `where T : new()` means it has a parameterless constructor.

.NET generics are real at run time. `List<int>` and `List<string>` are different types, and the JIT produces specialized machine code for each value type. So `List<int>` stores ints directly with no boxing. Java works differently: it erases type parameters at compile time, and a `List<Integer>` holds boxed objects.

**Variance** decides when a generic type can stand in for another. An `IEnumerable<string>` can be used where an `IEnumerable<object>` is expected, because you only read from it. This is marked with `out` in the interface, as in `IEnumerable<out T>`. A `List<string>` can't be used as a `List<object>`, because someone could then add a number to it.

### Nominal and structural typing

C# uses **nominal typing**. Two types are compatible only if they are declared to be, by name, through inheritance or interfaces. A class with `X` and `Y` properties is not a `Point` unless it says so.

TypeScript uses **structural typing**. Types are compatible if their shapes match. Any object with numeric `x` and `y` fits where a `{ x: number; y: number }` is expected, whatever it is called. Go interfaces work the same way.

### Modelling with types

Types can do more than describe data. They can make wrong programs impossible to write.

**Strongly typed IDs.** A method `Assign(int flightId, int aircraftId)` accepts its arguments in the wrong order without complaint. With `readonly record struct FlightId(int Value)` and `AircraftId`, swapping them doesn't compile.

**Units.** A number with a unit attached can't be confused with a number in another unit. Unit mix-ups have caused real disasters. In 1983 an Air Canada Boeing 767 ran out of fuel in flight because fuel was calculated in pounds where kilograms were needed. The crew glided it to a safe landing at Gimli. In 1999 NASA lost the Mars Climate Orbiter because one team's software produced imperial units and another's expected metric.

**Closed sets of cases.** A flight is scheduled, airborne or landed, and each state has different data. An airborne flight has an altitude. A landed flight has a landing time. A single class with nullable fields for everything allows impossible mixes. A small hierarchy with one record per state allows only real ones. Languages such as F#, Rust, Swift and TypeScript support this directly with **discriminated unions**. C# approximates them with records and pattern matching.

```csharp
// types.cs — run with: dotnet run types.cs
using System.Diagnostics;

var cruise = new Feet(35_000);
var runway = new Meters(3_000);
Console.WriteLine(cruise.ToMeters());              // Meters { Value = 10668 }
Console.WriteLine(cruise + new Feet(1_000));       // Feet { Value = 36000 }
// Console.WriteLine(cruise + runway);             // does not compile: no + for Feet and Meters

Assign(new FlightId(117), new AircraftId(42));
// Assign(new AircraftId(42), new FlightId(117)); // does not compile: arguments swapped

Console.WriteLine(Describe(new Airborne(new Feet(37_000))));                  // airborne at 37000 ft
Console.WriteLine(Describe(new Landed(new DateTimeOffset(2026, 9, 28, 14, 5, 0, TimeSpan.Zero))));  // landed at 14:05

string? callsign = FindCallsign(117);
// Console.WriteLine(callsign.Length);             // warning CS8602: callsign may be null
Console.WriteLine(callsign?.Length ?? 0);          // 13

Console.WriteLine(Max(3, 7));                      // 7
Console.WriteLine(Max("apple", "pear"));           // pear

Status s = (Status)7;                              // compiles and runs
Console.WriteLine(Enum.IsDefined(s));              // False

dynamic d = "text";                                // checks move to run time
Console.WriteLine(d.Length);                       // 4
try { Console.WriteLine(d.Altitude); }
catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { Console.WriteLine("no such member, found at run time"); }

static void Assign(FlightId flight, AircraftId aircraft) =>
    Console.WriteLine($"flight {flight.Value} on aircraft {aircraft.Value}");

static string Describe(FlightStatus status) => status switch
{
    Scheduled => "scheduled",
    Airborne a => $"airborne at {a.Altitude.Value} ft",
    Landed l => $"landed at {l.At:HH:mm}",
    _ => throw new UnreachableException(),   // C# can't yet prove this hierarchy is closed
};

static string? FindCallsign(int flight) => flight == 117 ? "SPEEDBIRD 117" : null;

static T Max<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

readonly record struct Feet(double Value)
{
    public static Feet operator +(Feet a, Feet b) => new(a.Value + b.Value);
    public Meters ToMeters() => new(Value * 0.3048);
}
readonly record struct Meters(double Value);
readonly record struct FlightId(int Value);
readonly record struct AircraftId(int Value);

abstract record FlightStatus;
sealed record Scheduled : FlightStatus;
sealed record Airborne(Feet Altitude) : FlightStatus;
sealed record Landed(DateTimeOffset At) : FlightStatus;

enum Status { Scheduled, Airborne, Landed }
```

### Casts and type tests

Sometimes you have a value of a general type and need a specific one. A **cast** like `(Airborne)status` converts it, and throws `InvalidCastException` if the value isn't that type.

Type tests are safer. `status is Airborne a` checks the type and, if it matches, gives you the value as `a`. `as` returns null instead of throwing. Pattern matching, covered in Control flow and functions (2.5), builds on the same idea.

Frequent casting is a hint that the types don't fit the problem. Often a better design removes the need.

### In short

Static typing checks types when code is compiled; dynamic typing checks them while it runs. `var` lets the compiler infer a type but doesn't make it dynamic. Nullable reference types make the compiler warn about possible nulls, but only at compile time, so data from outside still needs checking. Generics let code work with many types safely and without boxing. Small wrapper types for ids and units, and closed sets of states, turn whole families of mistakes into compile errors.

### What goes wrong

**Nullable warnings ignored or silenced with `!`.** The warning was right, and the crash comes later. Turn on `<Nullable>enable</Nullable>`, treat nullable warnings as errors in new code and use `!` only where you can prove it.

**Trusting types at the boundary.** A property declared `string` receives null from a JSON payload. Validate incoming data, or configure the serializer to reject missing required fields.

**Primitive obsession.** Using `int`, `string` and `Guid` for every id and value lets arguments be swapped silently. Wrap important ids and units in small types.

**Invalid enum values.** An API receives status 7 and stores it happily. Check with `Enum.IsDefined` or handle the default case of every switch.

**Coercion in JavaScript.** `"10" > "9"` is false, because both are strings and compare character by character. Convert explicitly with `Number(...)` and use `===`.

**`dynamic` spreading through code.** Every use of a `dynamic` value is unchecked. A typo compiles and fails in production. Convert to a real type as soon as you can.

### Cost model

Static types cost nothing at run time. The checks happen during compilation. Small wrapper types like `readonly record struct FlightId(int Value)` also cost nothing: the JIT treats them like the bare `int`. Generics over value types get specialized machine code, so there is no boxing. `dynamic` is much slower, because every operation is looked up at run time, although the runtime caches the lookups. Casts and type tests are cheap, usually a comparison of type pointers.

### In other languages (optional)

**TypeScript.** Structural and static, with union types built in: `type Status = { kind: "airborne"; altitude: number } | { kind: "landed"; at: Date }`. The compiler checks that a `switch` on `kind` handles every case. With `strictNullChecks`, `null` and `undefined` must be handled explicitly. Types disappear at run time.

**JavaScript.** Dynamic and weakly typed. `typeof` gives a value's type at run time. `==` coerces types before comparing, and `===` doesn't.

**Python.** Dynamic and strongly typed. Type hints such as `def area(r: float) -> float:` are ignored when the code runs, and are checked by tools like mypy or pyright.

**F#, Rust and Swift.** Have discriminated unions and require every case to be handled. F# also has units of measure: `35000.0<ft>` and `3000.0<m>` can't be added.

**SQL.** Every column has a type. Databases differ in how strictly they apply it. SQL Server and PostgreSQL reject or convert values that don't fit. SQLite accepts almost anything in any column.

### Key terms

- **Type**: A set of possible values plus the operations allowed on them.
- **Static typing**: Checking types when code is compiled, before it runs.
- **Dynamic typing**: Checking types while code runs, when each operation is attempted.
- **Strong typing**: Refusing to silently mix unrelated types. The term is used loosely.
- **Coercion**: Automatically converting a value to another type to make an operation work.
- **Type inference**: The compiler working out a type from context, as with `var`.
- **Nullable reference types**: The C# feature that tracks which references may be null and warns about unsafe uses.
- **Generics**: Types and methods written once with type parameters, such as `List<T>`.
- **Type parameter**: The placeholder, such as `T`, filled in when a generic type is used.
- **Type constraint**: A rule limiting what a type parameter can be, such as `where T : IComparable<T>`.
- **Variance**: The rules for when one generic type can stand in for another, marked with `in` and `out`.
- **Nominal typing**: Types are compatible only if declared to be, by name. C# works this way.
- **Structural typing**: Types are compatible if their members match. TypeScript works this way.
- **Discriminated union**: A type that is exactly one of a fixed set of cases, each with its own data.
- **Strongly typed ID**: A small wrapper type for an identifier, so ids of different things can't be mixed up.
- **Cast**: An explicit conversion to another type, which may fail or lose information.

### Where this leads

How values of these types are copied and compared is in Values, variables and references (2.4). Pattern matching on types is in Control flow and functions (2.5). Interfaces as a design tool come back in Abstraction and interfaces (4.2). Validating incoming data is in Validation and errors at the boundary (8.5). TypeScript's type system gets its own chapter in TypeScript (9.3).

### Check yourself

1. Add a `Diverted` state to `FlightStatus` with a destination airport code. Update `Describe`, and notice that nothing warned you before you did.
2. Add `Knots` and `KilometresPerHour` types with a conversion between them. 1 knot is 1.852 km/h.
3. Turn on `<Nullable>enable</Nullable>` in a copy of an older project. Count the warnings and fix those in one file.
4. Write `Clamp<T>(T value, T min, T max)` with a generic constraint so it works for `int`, `double` and `DateTime`.
5. In a browser console, predict and then check `"10" > "9"`, `10 > "9"` and `[] + {}`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Passing a FlightId where a BookingId belongs

A refund method takes `(Guid bookingId, Guid flightId)`. A caller swaps the arguments; it compiles and refunds the wrong booking.

*Strong ids*

```csharp
public readonly record struct BookingId(Guid Value);
public readonly record struct FlightId(Guid Value);

public Task RefundAsync(BookingId booking, FlightId flight, CancellationToken ct) => ...;

await refunds.RefundAsync(flightId, bookingId, ct);   // compile error: argument types don't match
```

> **Lesson:** Wrap ids and units in tiny types. A swapped argument becomes a compile error instead of a production incident.

#### Example 2: A non-nullable property that was null anyway

`string Email` is non-nullable, yet a NullReferenceException comes from a partner webhook payload with no email.

**What goes wrong.** Nullable reference types are compiler warnings only; deserialized data isn't checked.

*Validate at the boundary*

```csharp
public sealed record PartnerEvent
{
    public required string Email { get; init; }   // JSON missing the property now fails to deserialize
}

var evt = JsonSerializer.Deserialize<PartnerEvent>(body)
          ?? throw new BadHttpRequestException("Empty body");
if (string.IsNullOrWhiteSpace(evt.Email)) return Results.ValidationProblem(
    new Dictionary<string, string[]> { ["email"] = ["Email is required"] });
```

> **Lesson:** The compiler trusts annotations; the outside world doesn't follow them. Check data where it enters.

### CLR: GC, Records, Boxing

<p class="src">From <em>.NET Backend Deep Dive</em> · CLR: GC, Records, Boxing</p>

<a id="dn-26"></a>

#### Records vs Classes

```csharp
// record class (the default) - a reference type, STILL on the heap, STILL collected
public record Person(string Name, int Age);

// roughly equivalent to:
public class Person
{
    public string Name { get; init; }
    public int Age { get; init; }
    // + value-based Equals/GetHashCode
    // + a readable ToString(): Person { Name = Ann, Age = 30 }
    // + Deconstruct
    // + a copy constructor and Clone method (for 'with')
}
```

| | class | record (class) | record struct |
|---|---|---|---|
| Kind | Reference | Reference | **Value** |
| Lives on | Heap | Heap | Stack / inline in its parent |
| Equality | Reference | **Value** | Value |
| `with` expression | No | Yes | Yes |
| Mutable by default | Yes | Init-only (positional) | Use `readonly record struct` for immutability |
| GC pressure | Yes | **Identical to a class** | None (unless boxed) |

**To answer the question directly:** `record` does **not** reduce GC pressure. A `record class` allocates on the heap exactly like a class. In fact `with` **increases** allocation, because each use creates a new object:

```csharp
var p2 = p1 with { Age = 31 };    // a NEW heap object
// Inside a million-iteration loop, that's a million pieces of garbage
```

If you want to reduce GC, use `record struct` / `readonly record struct` for small data.

**A caution about record structs:** large value types (>16–24 bytes) get **copied** on every parameter pass, which can be slower than a class. Use `in` parameters or `ref readonly` to avoid the copies.

##### Two traps in value equality

**Collections break it silently.** Value equality compares every public member, and a `List<T>` compares by reference:

```csharp
record Order(int Id, List<Item> Items);

var a = new Order(1, new List<Item> { item });
var b = new Order(1, new List<Item> { item });
a == b;   // FALSE - same contents, different List instances
```

Use an immutable collection with structural equality, or override `Equals`.

**`with` is a shallow copy.** The new record shares the *same* `List` instance as the original, so mutating it changes both. An "immutable record" is only immutable one level deep.

**Use records for:** DTOs, API contracts, domain value objects, messages/events, query results — anywhere value equality is what you mean.
**Use classes for:** EF entities (identity is the `Id`, not the field values; they must be mutable for change tracking, and lazy-loading proxies need virtual members), services, and anything with behavior and a lifecycle.

**Records don't help with large datasets.** They allocate exactly like classes, and value `Equals`/`GetHashCode` compare every member — measurably slower than reference equality when churning through millions of objects. The scaling levers are projection, `AsNoTracking`, keyset paging and streaming (Part 2.2), not the type keyword. Records describe the *shape* of your data, not its scale.

### Nullable Reference Types & Analyzers

<p class="src">From <em>.NET Backend Deep Dive</em> · Nullable Reference Types & Analyzers</p>

<a id="dn-144"></a>

#### What NRTs actually give you

```xml
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

```csharp
string  name;   // must not be null - the compiler warns if it might be
string? name;   // may be null - the compiler forces you to check
```

This is **compile-time flow analysis, not a runtime guarantee.** Reflection, deserialization, EF materialization, and any reference from a `#nullable disable` assembly can still hand you a null in a non-nullable slot. NRTs eliminate the *category* of careless nulls, not every null.

```csharp
// The null-forgiving operator ! is a claim you are making to the compiler.
// Each use should be justifiable; a codebase full of ! has disabled the feature.
var user = _cache.Get(id)!;     // "I know this is populated" - are you sure?
```

Useful annotations that carry real information:

```csharp
bool TryGet(string key, [NotNullWhen(true)] out User? user);  // non-null when true
[MemberNotNull(nameof(_conn))] private void Init() { _conn = ...; }
T Pick<T>([NotNullIfNotNull(nameof(input))] T? input) => ...;
```

**EF caveat:** a non-nullable reference property becomes a `NOT NULL` column and a *required* relationship. Turning NRTs on can therefore change your schema — check the generated migration.

<a id="dn-145"></a>

#### Analyzers worth enabling

```xml
<AnalysisMode>Recommended</AnalysisMode>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

High-value rules: `CA2007` (missing `ConfigureAwait` in libraries), `CA1849` (sync call inside an async method — catches `.Result`), `CA1307/1309` (string comparison culture), `CA2016` (forward `CancellationToken`), `CA1062` (validate public arguments), `CA5350+` (weak crypto).

Worth adding: **Roslynator**, **Meziantou.Analyzer**, and **SonarAnalyzer.CSharp**. Together they catch most of the anti-patterns in Part 18 automatically, which is far more reliable than code review.

```ini
# .editorconfig - one file, enforced identically in IDE and CI
[*.cs]
dotnet_diagnostic.CA1849.severity = error
csharp_style_prefer_primary_constructors = false:suggestion
```

> The point isn't style. `dotnet format --verify-no-changes` plus analyzers-as-errors in CI removes an entire genre of review comment, so reviews can be about design instead.

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a15"></a>

#### Value types, reference types, and where things live

A `class` is a reference type: the variable holds a pointer, the object lives on the heap, assignment copies the pointer. A `struct` is a value type: the variable *is* the data, assignment copies every field. This single distinction explains an enormous number of bugs.

``` csharp
struct Point { public int X, Y; }
class  Node  { public int X, Y; }

var a = new Point { X = 1 };
var b = a;          // full copy
b.X = 99;           // a.X is still 1

var n = new Node { X = 1 };
var m = n;          // copies the reference
m.X = 99;           // n.X is now 99 too
```

“Stack vs heap” is the usual shorthand but it is not a rule. A struct that is a field of a class lives on the heap inside that class. A captured local in a lambda gets hoisted into a compiler-generated class on the heap. The reliable statement is: *value types are copied, reference types are aliased.*

##### The decision: struct or class?

| Choose `struct` when **all** of these hold                        | Choose `class` otherwise                       |
|-------------------------------------------------------------------|------------------------------------------------|
| It is genuinely one value — a point, a money amount, a date range | It has an identity that survives field changes |
| Under roughly 16–24 bytes                                         | It is large, or its size will grow             |
| Immutable                                                         | It is mutated after construction               |
| Short-lived, or held in a large array you iterate                 | It is stored in a polymorphic collection       |
| You never need `null` to mean “absent”                            | Absence is a real state                        |
| It is never boxed on a hot path                                   | It implements an interface you call through    |

If you are unsure, use a `class` — or a `record`. A struct chosen wrongly is quietly *slower* than the class it replaced, because every assignment, every parameter pass and every collection insertion copies it in full. The performance argument holds only for small, immutable values in tight loops or large arrays.

##### How you would recognise the bug

| Symptom                                            | Cause                                                                             |
|----------------------------------------------------|-----------------------------------------------------------------------------------|
| A change to a struct ‘did not take’                | You mutated a **copy** — from a property, a `foreach` variable, or a list indexer |
| Gen 0 collections in the thousands per second      | Boxing in a loop — usually a value type passed through a non-generic interface    |
| Two objects with identical fields are not equal    | A class comparing by reference; you wanted a `record`                             |
| A large struct made things slower                  | Every pass copies it — take it by `in`, or make it a class                        |
| A mutable struct in a collection behaves strangely | The indexer returns a copy. This is why mutable structs are discouraged outright  |

> **\[Why it matters\]** Boxing — putting a value type into an `object` or a non-generic interface — silently allocates. In a hot loop that is the difference between zero allocations and gigabytes of GC pressure.

###### How it actually works

Take a concrete layout. A `Point` struct with two ints is 8 bytes of pure data. A `Node` class with the same two ints is 8 bytes of data plus a 8-byte object header plus an 8-byte method table pointer on 64-bit — 24 bytes, of which two thirds is bookkeeping, and it lives somewhere else in memory that you reach through a pointer.

``` text
  Point[] points = new Point[1000];        Node[] nodes = new Node[1000];

  one allocation, 8000 bytes,              1001 allocations, ~32,000 bytes,
  perfectly contiguous:                    array of pointers into a scattered heap:

  [x0|y0][x1|y1][x2|y2]...                 [ptr]->{hdr|mt|x|y}
                                           [ptr]------------->{hdr|mt|x|y}
  iterate: the CPU prefetcher wins,        [ptr]->{hdr|mt|x|y}
  ~1 cache line per 8 points               iterate: a cache miss per element
```

On modern hardware a level-1 cache hit costs about 1 nanosecond and a main-memory fetch about 100. Summing a million points from the struct array typically runs several times faster than the class array, and nothing about the C# looks different. This is the entire reason value types exist as a language feature rather than an optimisation detail.

###### The rules that follow from this

| Guideline                                  | Reason                                                          |
|--------------------------------------------|-----------------------------------------------------------------|
| Keep structs small — roughly 16–24 bytes   | Above that, copying costs more than a pointer dereference saves |
| Make structs `readonly`                    | Prevents defensive copies the compiler must otherwise insert    |
| Pass big structs by `in` / `ref`           | Avoids copying on every call                                    |
| Never mutate a struct through an interface | Boxing means you mutate the box, and the original is unchanged  |
| Watch `List<BigStruct>` indexers           | `list[0].X = 5` mutates a copy; use `CollectionsMarshal.AsSpan` |

``` csharp
// the boxing trap in full
interface ICounter { void Increment(); int Value { get; } }

struct Counter : ICounter
{
    public int Value { get; private set; }
    public void Increment() => Value++;
}

Counter c = new();
ICounter boxed = c;        // BOXES: a copy of c is placed on the heap
boxed.Increment();         // increments the box
Console.WriteLine(c.Value);       // 0   - the original never changed
Console.WriteLine(boxed.Value);   // 1
```

Boxing also happens quietly in places you would not look: adding a struct to a non-generic collection, using it as a `object` parameter, string interpolation of a value type before .NET 6, and `Enum.HasFlag` on older runtimes. Each is one small heap allocation, and each is invisible until you look at an allocation profile.

<a id="be-a17"></a>

#### Nullable reference types

Enable `<Nullable>enable</Nullable>` in the csproj and the compiler starts tracking whether each reference can be null. `string` means “never null”; `string?` means “might be”. It is compile-time only — nothing is enforced at runtime.

``` csharp
public User? Find(string id) => _db.Users.SingleOrDefault(u => u.Id == id);

var user = Find("42");
Console.WriteLine(user.Name);        // CS8602 warning: possible null dereference

if (user is not null)
    Console.WriteLine(user.Name);    // flow analysis narrows the type

// escape hatches, use sparingly
Console.WriteLine(user!.Name);       // "trust me"  -> still throws if wrong

// document intent for helpers
public static void ThrowIfNull([NotNull] object? o, string name) { ... }
```

##### The decision: is this thing nullable?

| Ask                                                              | If yes                                                              | If no                       |
|------------------------------------------------------------------|---------------------------------------------------------------------|-----------------------------|
| Can a caller legitimately have nothing to give?                  | `string?`                                                           | `string`                    |
| Is it set after construction — by a binder, an ORM, a framework? | `required`, or a constructor parameter                              | —                           |
| Does it come from outside — JSON, a database, a header?          | Treat it as nullable **regardless of the annotation**, and validate | —                           |
| Is absence meaningful to the domain?                             | Nullable, and handle it explicitly                                  | Non-nullable with a default |
| Are you writing `!` to silence a warning?                        | **Stop** — the compiler is right more often than you are            | —                           |

##### How you would recognise the bug

| Symptom                                               | Cause                                                                                                            |
|-------------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| A `NullReferenceException` on a non-nullable property | It came from deserialisation, reflection, or an unannotated assembly — the annotation is not enforced at runtime |
| Warnings appear only after adding a package           | That package is annotated and your code was not checked against it before                                        |
| A warning inside a lambda you already null-checked    | Flow analysis does not carry a check into a closure — assign to a local first                                    |
| Thousands of warnings on enabling it                  | You switched the whole solution at once. Use the ratchet                                                         |
| No warnings, and you expected some                    | `<Nullable>annotations</Nullable>`, or the file is `#nullable disable`                                           |

> **\[Note\]**
>
> **Adopting it in a legacy codebase.** Turn warnings into errors only for new projects. For old ones, enable per-file with `#nullable enable` at the top and migrate a file at a time. A big-bang switch produces thousands of warnings and gets reverted.

###### How it actually works, and migrating an existing codebase

The most important fact: **nullable reference types are a compile-time analysis with no runtime component whatsoever.** The compiler emits attributes; the CLR ignores them entirely. Nothing stops a caller from a different assembly, from reflection, or from JSON deserialisation putting a null in a non-nullable field.

``` csharp
// what you write
public class Order { public string Reference { get; set; } }

// what the compiler emits, roughly
[NullableContext(1)] [Nullable(0)]
public class Order { public string Reference { get; set; } }
// -> metadata only. There is no null check in the generated IL.

// so all of these compile clean and produce null at runtime:
var o = JsonSerializer.Deserialize<Order>("{}")!;      // Reference is null
var o2 = (Order)Activator.CreateInstance(typeof(Order))!;
var o3 = default(Order);
dapper.Query<Order>("select null as Reference");        // null
// which is why boundary validation still matters. The compiler protects you
// from YOUR code, not from data.
```

| Annotation                          | Means                         | Use when                                                           |
|-------------------------------------|-------------------------------|--------------------------------------------------------------------|
| `string?`                           | May be null                   | The honest default for anything optional                           |
| `string`                            | Should not be null            | A promise you are making to callers                                |
| `!` (null-forgiving)                | “Trust me”                    | You have checked it in a way the compiler cannot see — comment why |
| `[NotNullWhen(true)]`               | Not null if this returns true | `TryParse`-style methods                                           |
| `[MemberNotNull(nameof(x))]`        | This method initialises `x`   | Init helpers called from a constructor                             |
| `[NotNullIfNotNull(nameof(input))]` | Null out only if null in      | Pass-through transforms                                            |
| `required`                          | The *caller* must set it      | Better than `!` for DTO properties                                 |

``` text
// the ratchet, for an existing codebase. Enabling it everywhere at once
// produces thousands of warnings and gets reverted.

// 1. enable the CONTEXT but not the warnings, project-wide
<Nullable>annotations</Nullable>

// 2. turn on warnings per file, starting with leaf types
#nullable enable
namespace Acme.Domain;   // this file is now fully checked

// 3. or per project, once a project is clean
<Nullable>enable</Nullable>

// 4. and gate the count in CI so it can only go DOWN
<WarningsAsErrors>CS8600;CS8602;CS8603;CS8618</WarningsAsErrors>
```

> **\[Trap\]**
>
> **Common trap.** `!` is not a fix, it is a suppression, and it is contagious in review — once a codebase has fifty of them nobody questions the fifty-first. Prefer `required` for properties the caller must supply, a constructor parameter for anything genuinely mandatory, and an actual null check where the value comes from outside. Reserve `!` for cases where you have proven non-nullness by a means the compiler cannot follow — and write the reason beside it.

<a id="be-a18"></a>

#### Generics, constraints and variance

Generics give you one implementation that is type-safe and allocation-free across many types. Unlike Java, .NET generics are *reified*: the runtime knows the real type argument, and it generates specialised machine code per value type. `List<int>` stores raw ints, not boxed objects.

``` csharp
public interface IRepository<T> where T : class, IEntity, new()
{
    Task<T?> GetAsync(Guid id, CancellationToken ct = default);
}

// covariance: IEnumerable<out T>  -> IEnumerable<Dog> is an IEnumerable<Animal>
IEnumerable<Animal> animals = new List<Dog>();

// contravariance: IComparer<in T> -> IComparer<Animal> can compare Dogs
IComparer<Dog> byName = Comparer<Animal>.Default;
```

##### The decision: should this be generic?

| Make it generic when                                | Do not when                                              |
|-----------------------------------------------------|----------------------------------------------------------|
| The *same logic* applies to several unrelated types | You have one caller and one type                         |
| You would otherwise write `object` and cast         | A simple interface or an overload would do               |
| It is a container, a pipeline stage, or a wrapper   | The type parameter appears once and is never constrained |
| Boxing a value type would otherwise occur           | Only reference types are ever used                       |
| You can express the requirement as a constraint     | The body needs a type check on `T`                       |

`if (typeof(T) == typeof(Foo))` inside a generic method is the reliable signal that the abstraction is wrong: you have one generic signature over what are really two different methods. Write the two methods, or introduce a constraint that makes the difference expressible.

##### How you would recognise the bug

| Symptom                                                       | Cause                                                                          |
|---------------------------------------------------------------|--------------------------------------------------------------------------------|
| `IEnumerable<Derived>` will not assign to `IEnumerable<Base>` | You typed it as `List<T>`, which is invariant. Return the interface            |
| Boxing inside a generic method                                | An unconstrained `T` called through an interface — add the constraint          |
| `ArrayTypeMismatchException`                                  | Array covariance — the historic hole in the type system                        |
| A generic works locally and fails under Native AOT            | An instantiation the compiler could not discover ahead of time                 |
| `where T : new()` is slow                                     | It compiles to `Activator.CreateInstance` — reflection. Pass a factory instead |

The mnemonic: `out` means T only comes *out* (producer, covariant), `in` means T only goes *in* (consumer, contravariant). `List<T>` does both, so it is invariant.

###### How generics are implemented, and why that matters

``` text
  .NET generics are REIFIED - the type argument survives to runtime.
  Java erases them. This one difference explains most of the behaviour.

  REFERENCE type arguments        List<string>, List<object>, List<Order>
    -> ONE shared machine-code body, because every reference is
       a pointer of the same size
    -> cheap: no code bloat

  VALUE type arguments            List<int>, List<double>, List<MyStruct>
    -> a SEPARATE specialised body per type
    -> no boxing, no casts, fully inlinable  == FAST
    -> but each instantiation costs code size and JIT time

  consequences you can observe:
    typeof(List<int>) != typeof(List<string>)      // real distinct types
    generic code can ask  typeof(T)  at runtime
    a struct in a generic collection is stored INLINE, not boxed
    Native AOT must know every instantiation AHEAD OF TIME - which is why
      heavy generic virtual method dispatch can fail to trim
```

``` csharp
// constraints are not just validation - they change the CODE GENERATED

void Process<T>(T item) { }                    // T could be anything: conservative

void Process<T>(T item) where T : struct { }   // no null checks needed
void Process<T>(T item) where T : class  { }   // reference semantics assumed
void Process<T>(T item) where T : IComparable<T>
{
    // WITHOUT the constraint this would box a struct T to call the interface.
    // WITH it, the JIT emits a CONSTRAINED call - direct, no boxing.
    item.CompareTo(item);
}

// static abstract members (C# 11+): generic math without boxing or delegates
public static T Sum<T>(IEnumerable<T> values) where T : INumber<T>
{
    T total = T.Zero;
    foreach (var v in values) total += v;      // resolved per instantiation
    return total;
}
Sum(new[] { 1, 2, 3 });        // int specialisation
Sum(new[] { 1.5m, 2.5m });     // decimal specialisation - same source, no boxing
```

``` text
  VARIANCE - only on INTERFACES and DELEGATES, never on classes

  out T   COVARIANT      T appears only in OUTPUT position
                         IEnumerable<out T>
                         IEnumerable<string> IS-A IEnumerable<object>   OK

  in T    CONTRAVARIANT  T appears only in INPUT position
                         IComparer<in T>
                         IComparer<object> IS-A IComparer<string>       OK

  no keyword = INVARIANT
                         List<string> is NOT a List<object>
                         because you could Add(new Order()) to it and
                         break the list. The restriction is protecting you.

  and the historic hole: ARRAYS are covariant and should not be.
    object[] a = new string[1];
    a[0] = 42;              // compiles. Throws ArrayTypeMismatchException.
  every array store carries a runtime type check because of this.
```

| Pattern                                            | Why                                                              |
|----------------------------------------------------|------------------------------------------------------------------|
| Return `IEnumerable<T>`, accept `IReadOnlyList<T>` | Covariance on the way out, no surprise enumeration on the way in |
| Constrain to `notnull`                             | Prevents `T` being a nullable reference in a dictionary key      |
| Avoid `where T : new()`                            | It compiles to `Activator.CreateInstance` — reflection, and slow |
| Prefer `INumber<T>` over `dynamic`                 | Static dispatch instead of runtime binding                       |
| Watch generic *virtual* methods under AOT          | Every instantiation must be discoverable at compile time         |

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b234"></a>

#### What a type actually is

A type answers four questions the compiler and runtime need:

- **How many bytes?** `int` is 4, `long` is 8, a `struct` is the sum of its fields plus padding.
- **How is it laid out?** Which field sits at which offset.
- **What happens on assignment?** Copy the data, or copy a reference to it.
- **What operations are legal?** Its members, and which interfaces it satisfies.

Everything else — inheritance, generics, nullability — is built on those.

<a id="be-b236"></a>

#### Assignment: the question that decides everything

``` csharp
struct SPoint { public int X, Y; }
class  CPoint { public int X, Y; }

var s1 = new SPoint { X = 1 };
var s2 = s1;      // copies 8 bytes
s2.X = 99;        // s1.X is still 1

var c1 = new CPoint { X = 1 };
var c2 = c1;      // copies an 8-byte reference
c2.X = 99;        // c1.X is now 99 — same object
```

Every confusing behaviour downstream traces back to this. A struct in a `List<T>` is a *copy*, so mutating it through the indexer doesn't work the way you expect. Passing a large struct to a method copies it. Two references to the same object see each other's changes.

### The Type System

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Type System</p>

<a id="be-b241"></a>

#### The hierarchy

``` text
object
 ├── ValueType
 │    ├── all structs (int, double, DateTime, Guid, your structs)
 │    └── Enum ──► all enums
 ├── string, arrays, delegates
 └── your classes
```

Everything derives from `object`, including value types — which is exactly what makes boxing necessary. `int` *is* an `object` conceptually, but its representation is 4 raw bytes, so becoming one requires a box.

<a id="be-b242"></a>

#### struct or class

**Use a struct when all of these hold** (the framework's own guidance, and it's sound):

- It logically represents a single value, like a number or a coordinate
- Instance size is **≤ 16 bytes** (roughly two references)
- It's immutable
- It won't be boxed frequently

**Use a class otherwise**, and specifically when:

- Identity matters — two things with the same values are still different things
- It has a lifecycle, or contains mutable state shared by reference
- It's large, or polymorphic

The failure mode with large structs: every assignment, every method call, every collection insert copies the whole thing. A 200-byte struct passed to a method in a loop copies 200 bytes per call. Past ~16–24 bytes, pass by `in` or use a class.

**Mutable structs are a documented trap:**

``` csharp
struct Counter { public int N; public void Inc() => N++; }

var list = new List<Counter> { new Counter() };
list[0].Inc();          // compiler error — good
var arr = new Counter[1];
arr[0].Inc();           // works — arrays give direct element access

readonly Counter _c;
void M() { _c.Inc(); }  // silently operates on a DEFENSIVE COPY; _c.N stays 0
```

That last one is the nasty one: calling a non-readonly method on a `readonly` struct field makes the compiler copy it first, so the mutation is silently discarded. Make structs immutable and the whole category disappears.

<a id="be-b243"></a>

#### The struct variants

``` csharp
readonly struct Point { public readonly int X, Y; }          // no defensive copies
record struct Size(int W, int H);                            // value equality, mutable
readonly record struct Money(decimal Amount, string Ccy);     // the usual best choice
ref struct Buffer { public Span<byte> Data; }                 // stack-only
```

**`readonly struct`** tells the compiler nothing can mutate, so it stops emitting defensive copies when the struct is accessed through a `readonly` field or an `in` parameter. This is a real, measurable win and costs nothing.

**`record struct`** generates value equality, `GetHashCode`, `ToString`, and deconstruction. **`readonly record struct` is the default choice for a small value type** — you get correctness and performance without writing boilerplate.

**`ref struct`** (`Span<T>`, `ReadOnlySpan<T>`, `ValueStringBuilder`) is guaranteed to live on the stack. The restrictions follow from that guarantee:

- Cannot be a field of a class or a normal struct
- Cannot be boxed, or used as a generic argument (until `allows ref struct` in C# 13)
- **Cannot be used across an `await` or a `yield return`**

That last restriction connects directly to the async reference: an await hoists locals into fields of a heap object, and a `ref struct` cannot be a field of a heap object. Same for iterators, for the same reason.

<a id="be-b244"></a>

#### Records and immutability

``` csharp
public record Person(string Name, int Age)
{
    public string Display => $"{Name} ({Age})";
}

var a = new Person("Nam", 30);
var b = a with { Age = 31 };     // non-destructive mutation — new instance
a == b;                          // false — value equality, compares members
```

A `record` is a class with compiler-generated value equality, `ToString`, `Deconstruct`, and `with`. Use it for DTOs, domain values, messages, and anything you'd otherwise write `Equals` for by hand.

**`init` accessors** allow object-initializer syntax while keeping the property immutable afterward:

``` csharp
public class Config { public required string Host { get; init; } public int Port { get; init; } = 80; }
var c = new Config { Host = "x" };   // ok
c.Port = 90;                          // compile error
```

`required` (C# 11) forces the caller to set it, giving you constructor-like guarantees with initializer syntax.

**Immutability is not deep.** A record holding a `List<T>` is a record whose list can still be mutated by anyone holding it. For real immutability, hold `ImmutableArray<T>` or expose `IReadOnlyList<T>` over a defensive copy — and note that `IReadOnlyList<T>` is a *view*, not a guarantee; the holder of the underlying list can still change it.

<a id="be-b245"></a>

#### Nullability

Two different mechanisms with confusingly similar names:

**`Nullable<T>` / `T?` for value types** is a real struct: `{ T value; bool hasValue; }`. It's runtime machinery, and `int?` is 8 bytes rather than 4.

**Nullable reference types (`string?`)** are **compile-time only.** There is no runtime difference; the compiler tracks flow and warns. Enable per-project:

``` xml
<Nullable>enable</Nullable>
```

The distinction matters because NRT annotations do not protect you at a boundary. Data from JSON, a database, or reflection can be null in a non-nullable field and the runtime will not stop it. Validate at the edges; treat the annotations as a strong lint, not a guarantee.

``` csharp
string? s = GetMaybe();
int len = s!.Length;         // ! silences the compiler and proves nothing
if (s is not null) { }       // preferred over != null: can't be hijacked by operator overloads
```

<a id="be-b246"></a>

#### Equality — the contract

Four things must agree, and getting this wrong silently breaks dictionaries.

``` csharp
public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public bool Equals(Money? o) =>
        o is not null && Amount == o.Amount && Currency == o.Currency;

    public override bool Equals(object? o) => Equals(o as Money);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public static bool operator ==(Money? a, Money? b) =>
        a is null ? b is null : a.Equals(b);
    public static bool operator !=(Money? a, Money? b) => !(a == b);
}
```

**The rules:**

- Equal objects **must** return equal hash codes. The reverse is not required.
- `GetHashCode` must not change while the object is a dictionary key (I.8).
- Implement `IEquatable<T>` to avoid boxing on value types and a cast on reference types — dictionaries and `List.Contains` check for it.
- `HashCode.Combine` is the right tool. Never write `a.GetHashCode() ^ b.GetHashCode()` — XOR makes `(1,2)` and `(2,1)` collide.

**Default struct equality is slow and surprising.** If a struct contains any reference-type field, `ValueType.Equals` falls back to **reflection over the fields**. It's correct and roughly 100× slower than a hand-written comparison. Always implement `IEquatable<T>` on structs, or use `record struct`, which generates it.

**Default class equality is reference equality.** Two `Person` objects with identical fields are not equal unless you say so.

<a id="be-b247"></a>

#### Ordering

``` csharp
public class Version : IComparable<Version>
{
    public int CompareTo(Version? o) =>
        o is null ? 1 : (Major, Minor).CompareTo((o.Major, o.Minor));
}

// external ordering, without touching the type
var byAge = Comparer<Person>.Create((a, b) => a.Age.CompareTo(b.Age));
list.Sort(byAge);
```

`IComparable<T>` is the type's natural order; `IComparer<T>` is one of many possible orders supplied from outside. Value tuples implement comparison memberwise, which makes multi-key comparisons a one-liner.

**A comparer must be consistent** — transitive, antisymmetric, and stable across the sort. An inconsistent comparer makes `List.Sort` throw `InvalidOperationException` ("IComparer.Compare() method returns inconsistent results") or, worse, silently produce a wrong order.

`List<T>.Sort` uses **introsort**, a hybrid that switches strategy based on what the data turns out to be:

- **Quicksort** normally — fast, in-place, but degrades to O(n²) on adversarial input (already-sorted data with a naive pivot).
- **Heapsort** once recursion exceeds ~2·log₂(n), which means quicksort is doing badly. Heapsort is slower on average but guarantees O(n log n), so this caps the worst case.
- **Insertion sort** for runs under ~16 elements, where its low overhead beats both.

That combination gives quicksort's average speed with heapsort's worst-case guarantee. It's also **in-place**, so it allocates nothing — and **not stable**, meaning equal elements can be reordered.

`OrderBy` **is** stable: it allocates buffers and sorts an index array, preserving the original order of equal keys. So the choice is stability and allocation (`OrderBy`) versus neither (`Sort`). If two records with the same key must stay in their original order, you need `OrderBy`.

<a id="be-b248"></a>

#### The mutable-key bug

The single most common data-structure bug in C#:

``` csharp
var key = new MutableKey { Id = 1 };
var dict = new Dictionary<MutableKey, string> { [key] = "value" };

key.Id = 2;                    // hash code changes

dict[key];                     // KeyNotFoundException
dict.ContainsKey(key);         // false
dict.Count;                    // still 1 — the entry is there, unreachable
```

The entry sits in the bucket chosen by the *old* hash. Nothing will ever look there again. The value is leaked: reachable by enumeration, unreachable by key.

**Rule: dictionary and hash-set keys must be immutable**, or at least their hash-relevant fields must be. Strings, primitives, and `readonly record struct` are safe. A mutable class with a computed `GetHashCode` is a trap.

<a id="be-b249"></a>

#### How generics actually work

The JIT does two different things depending on the type argument:

- **Value types get their own specialized native code.** `List<int>` and `List<double>` are separate compiled implementations, storing raw values with no boxing and no indirection.
- **Reference types share one implementation**, because all references are the same size. `List<string>` and `List<Person>` share code, with type checks erased.

Two consequences: generics over value types are genuinely zero-cost abstractions, and generic code over many distinct value types increases code size and JIT time.

**Constraints unlock capability:**

``` csharp
where T : struct              // value type
where T : class               // reference type
where T : notnull
where T : new()               // parameterless constructor
where T : IComparable<T>      // constrained call — no boxing on structs
where T : Base, IFoo
where T : unmanaged           // no references anywhere inside; enables stackalloc/pointers
```

**Static abstract members** (C# 11) finally allow generic math:

``` csharp
static T Sum<T>(IEnumerable<T> xs) where T : INumber<T>
{
    T total = T.Zero;
    foreach (var x in xs) total += x;
    return total;
}
```

**Variance** — why `IEnumerable<string>` can be treated as `IEnumerable<object>` but `List<string>` cannot.

The rule follows from one question: **can the type argument come out, go in, or both?**

``` text
interface IProducer<out T> { T Get(); }        // T only comes OUT → covariant
interface IConsumer<in T>  { void Put(T x); }  // T only goes IN  → contravariant
interface IBoth<T>         { T Get(); void Put(T x); }  // both → invariant
```

**Covariance (`out`) is safe for producers.** If something only ever *hands you* strings, treating it as something that hands you objects is fine — every string is an object.

``` csharp
IEnumerable<string> s = ...;
IEnumerable<object> o = s;        // legal: you can only read, and strings are objects
```

**Contravariance (`in`) is safe for consumers.** If something can *accept* any object, it can certainly accept a string.

``` csharp
IComparer<object> co = ...;
IComparer<string> cs = co;        // legal: it handles anything, so it handles strings
```

**Both directions at once is unsafe**, which is why `List<T>` is invariant. If `List<string>` were a `List<object>`, you could add an `int` to it, and the next code to read a `string` out would fail. The compiler prevents it.

Variance applies **only to interfaces and delegates**, and only to reference types — `IEnumerable<int>` is not an `IEnumerable<object>`, because boxing changes the representation.

**Array covariance is a historical mistake** that predates generics and fails at runtime instead:

``` csharp
object[] arr = new string[10];
arr[0] = 42;                      // compiles; throws ArrayTypeMismatchException
```

It also means **every write to a reference-type array carries a runtime type check**, a permanent cost paid by all code for a feature nobody should use. Use `List<T>` or generics.

------------------------------------------------------------------------

### Members, Inheritance, and Lifetime

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Members, Inheritance, and Lifetime</p>

<p class="pd">Mechanisms that decide how types behave, several of which have surprising failure modes.</p>

<a id="be-b254"></a>

#### const, static readonly, readonly

Three ways to say "doesn't change," with materially different behaviour:

``` csharp
public const int Max = 100;               // compile-time; baked into CALLERS
public static readonly int Limit = 100;   // runtime; one per type
public readonly int Id;                    // runtime; one per instance, set in ctor
```

**`const` is the dangerous one across assembly boundaries.** Its value is copied into every call site at compile time. Ship `Library.dll` with `Max = 100`, an app compiles against it, then you change it to `200` and redeploy only the library — **the app still uses 100**, because the value was inlined into the app's IL. It will keep using 100 until recompiled.

Rule: `const` only for genuinely eternal values (`Math.PI`, `DaysInWeek`) or values private to one assembly. Use `static readonly` for anything a consumer might depend on, since it's read from the field at runtime.

`const` is also limited to compile-time constants — primitives, `string`, `enum`, and `null`. `static readonly` can hold anything.

<a id="be-b255"></a>

#### Virtual dispatch and sealed

``` csharp
class Base    { public virtual void M() { } }
class Derived : Base { public override void M() { } }
class Other   : Base { public new void M() { } }      // HIDES, doesn't override
```

**`virtual` costs an indirection.** A virtual call reads the object's method table pointer (0.4), looks up the slot, then jumps. A non-virtual call goes straight there, and can be inlined.

`new` is not a variant of `override` — it's a different thing that looks similar and behaves badly:

``` csharp
Base b = new Other();
b.M();      // calls Base.M — the STATIC type decides, because M isn't virtual here
```

That's almost never what anyone wants. If the compiler warns "hides inherited member," don't silence it with `new` unless you've thought about it.

**`sealed` is a free optimization.** A sealed class or a sealed override lets the JIT prove there's no further derived type, so it can **devirtualize** the call — turn the indirect jump into a direct one and often inline it. Sealing classes that aren't designed for inheritance costs nothing and can measurably help hot paths. It's the default in much of the BCL for this reason.

<a id="be-b256"></a>

#### Interfaces: explicit and default implementations

``` csharp
public class Repo : IDisposable, IAsyncDisposable
{
    void IDisposable.Dispose() { }        // EXPLICIT — invisible on the concrete type
    public ValueTask DisposeAsync() => default;
}

var r = new Repo();
r.Dispose();                               // compile error — explicit implementation
((IDisposable)r).Dispose();                // ok
```

Explicit implementation is for two cases: resolving a name collision when two interfaces demand the same signature, and hiding a member that would confuse callers on the concrete type.

**It also boxes structs.** Calling an explicitly-implemented interface member on a struct requires a cast to the interface, which is a box (0.5). This is a real hazard in performance-sensitive struct code.

**Default interface methods** (C# 8) let an interface ship a body:

``` csharp
interface ILogger
{
    void Log(string m);
    void LogError(string m) => Log("ERROR: " + m);   // default body
}
```

Useful for adding a member to a published interface without breaking implementers. But the default is only reachable *through the interface*, not on the concrete type, which surprises people — it behaves more like an extension method than an inherited member.

<a id="be-b257"></a>

#### ref, out, in — and ref returns

``` text
void ByValue(int x)      { x = 5; }        // caller unaffected
void ByRef(ref int x)    { x = 5; }        // caller's variable changes
void Out(out int x)      { x = 5; }        // MUST assign; caller needn't initialize
void In(in int x)        { /* read-only */ }  // pass by reference, no copy
```

For **value types**, `ref` passes the location instead of a copy — the reason to use it is either mutation or avoiding a large copy.

For **reference types**, `ref` means something subtler: without it you can already mutate the object, but with it you can **reassign the caller's variable** to a different object.

``` text
void Fill(List<int> l)     { l.Add(1); l = new List<int>(); }  // caller sees the Add only
void Swap(ref List<int> l) { l = new List<int>(); }            // caller's variable now points elsewhere
```

**`in` avoids copying a large struct** but has a catch: if the struct isn't `readonly`, the compiler inserts a **defensive copy** every time you call a member on it — because it can't prove the member won't mutate. So `in` on a non-readonly struct can be *slower* than passing by value. Pair `in` with `readonly struct` (I.3) or don't bother.

**`ref` returns and `ref` locals** let you alias storage rather than copy it:

``` csharp
ref int slot = ref array[5];
slot = 42;                         // writes array[5] directly

// update a struct inside a list without copying it out and back
ref var item = ref CollectionsMarshal.AsSpan(list)[0];
item.Count++;                       // mutates in place
```

This is how you mutate a struct stored in a collection — the problem from I.2 that has no other clean solution.

<a id="be-b258"></a>

#### Tuples and deconstruction

``` csharp
(string Name, int Age) t = ("Nam", 30);   // ValueTuple — a STRUCT
var (name, age) = t;                       // deconstruction
```

**`ValueTuple` (the `(a, b)` syntax) is a mutable struct with public fields.** `System.Tuple` is a legacy immutable class that allocates. Use the former.

Element names are a **compile-time fiction** — they're erased to `Item1`/`Item2` in IL and preserved only in an attribute on the signature. So reflection sees `Item1`, and two tuples with different names are the same type.

Tuples implement equality and comparison memberwise, which makes them excellent for composite keys and multi-field sorts:

``` csharp
var dict = new Dictionary<(int TenantId, string Sku), Product>();
list.Sort((a, b) => (a.Last, a.First).CompareTo((b.Last, b.First)));
```

Any type can support deconstruction by declaring a `Deconstruct` method; records generate one automatically.

<a id="be-b259"></a>

#### Pattern matching

Modern C# replaces most type-checking ceremony:

``` csharp
if (o is Order { Total: > 100, Customer.Country: "NL" } order)   // property patterns, nested
    Ship(order);

var label = shape switch
{
    Circle c when c.R > 10 => "big circle",
    Circle => "circle",
    Rectangle { W: var w, H: var h } when w == h => "square",
    null => "nothing",
    _ => "other"                              // discard — the catch-all
};

if (x is > 0 and < 100) { }                   // relational + logical patterns
if (list is [var first, .., var last]) { }    // list patterns (C# 11)
```

Two practical notes. `is null` and `is not null` are preferable to `== null` because `==` can be overloaded and give surprising results, while `is` always tests reference identity. And a `switch` expression without a matching arm throws `SwitchExpressionException` at runtime — the compiler warns about non-exhaustive matches, and that warning is worth treating as an error.

### Primitives

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Primitives</p>

<a id="be-b264"></a>

#### Dates and times

| Type             | Holds                      | Use for                      |
|------------------|----------------------------|------------------------------|
| `DateTime`       | date+time + a `Kind` enum  | avoid at boundaries          |
| `DateTimeOffset` | date+time + **UTC offset** | **timestamps — the default** |
| `DateOnly`       | date                       | birthdays, invoice dates     |
| `TimeOnly`       | time of day                | opening hours                |
| `TimeSpan`       | duration                   | elapsed time                 |
| `TimeZoneInfo`   | zone rules                 | conversions                  |

**`DateTime.Kind` is the trap.** It's an enum with `Unspecified`, `Utc`, and `Local`, and `Unspecified` is the default. Two `DateTime`s can compare equal while representing different instants, and serialization round-trips lose the `Kind` in some formats. **Prefer `DateTimeOffset` for anything that is a moment in time.**

**An offset is not a time zone.** `+02:00` doesn't tell you whether DST applies next month. For future appointments, store the instant *and* the IANA zone id (`Europe/Amsterdam`), then compute the local time when you need it.

For testing, take `TimeProvider` (.NET 8) as a dependency rather than calling `DateTime.UtcNow` — it makes time deterministic without a wrapper interface of your own.

<a id="be-b265"></a>

#### Guid

16 bytes. `Guid.NewGuid()` is a v4 random GUID, which is **terrible as a clustered database key** — random inserts fragment the index and cause page splits.

If you need database-friendly identifiers, use sequential ones: `Guid.CreateVersion7()` in .NET 9 (timestamp-ordered, standardized), or SQL Server's `NEWSEQUENTIALID()`.

Guid string formats differ (`D` with hyphens, `N` without, `B` braced). Parsing is lenient; formatting is not. Be explicit.

<a id="be-b266"></a>

#### enum

An enum is a thin veneer over its underlying integer, and it is **not type-safe**:

``` csharp
enum Status { Active = 1, Inactive = 2 }

var s = (Status)999;              // legal, no error
s.ToString();                     // "999"
Enum.IsDefined(typeof(Status), s) // false — you must check
```

Anything crossing a boundary — an API payload, a database column, a deserialized message — can carry an undefined value. Validate on the way in.

``` csharp
[Flags]
enum Perm { None = 0, Read = 1, Write = 2, Delete = 4 }   // powers of two
var p = Perm.Read | Perm.Write;
p.HasFlag(Perm.Write);                                     // true, but boxes pre-Core
(p & Perm.Write) == Perm.Write;                            // allocation-free
```

Give every enum an explicit `= 1`, `= 2` and never renumber — persisted values are a contract.

------------------------------------------------------------------------

### The type system in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The type system in practice</p>

<a id="be-m49"></a>

#### bool, enum, and flags

`bool` occupies 1 byte in a struct (not 1 bit), so eight `bool` fields cost 8 bytes plus padding. Consider a `[Flags]` enum or a bitfield if that matters.

An `enum` is a named integer. Its underlying type is `int` by default; change it for size or for a database mapping.

``` csharp
enum Status : byte { Draft = 0, Active = 1, Closed = 2 }

[Flags]
enum Permissions {
    None   = 0,
    Read   = 1 << 0,
    Write  = 1 << 1,
    Delete = 1 << 2,
    All    = Read | Write | Delete
}

var p = Permissions.Read | Permissions.Write;
bool canWrite = p.HasFlag(Permissions.Write);   // allocation-free since .NET Core 2.1
bool canWrite2 = (p & Permissions.Write) != 0;  // still marginally faster
```

> **\[Trap\]**
>
> **Trap.** Enums are not validated. `(Status)200` compiles and runs. Any value that came from a database, a query string, or JSON must be checked with `Enum.IsDefined` — and note `IsDefined` is unreliable for `[Flags]` combinations, where you should mask against the valid bits instead.

``` csharp
// Fast, allocation-free enum name lookups (.NET 5+)
Enum.GetValues<Status>();   // generic overload, no boxing
Enum.TryParse<Status>("Active", ignoreCase: true, out var st);
```

<a id="be-m50"></a>

#### Nullable value types

`int?` is `Nullable<int>`: a struct with two fields, `bool hasValue` and `T value`. It is not a reference, and it costs the size of `T` plus padding (so `int?` is 8 bytes, not 4).

``` csharp
int? a = null;
int b = a ?? 0;
int c = a.GetValueOrDefault();  // same, no branch on the caller side
int d = a!.Value;               // InvalidOperationException if null
```

**Lifted operators** propagate null: any arithmetic with a null operand yields null. But comparison operators return `false`, not null — which means both `a < b` and `a >= b` can be false at once.

``` csharp
int? x = null, y = 5;
Console.WriteLine(x + y);     // (empty) - null
Console.WriteLine(x < y);     // False
Console.WriteLine(x >= y);    // False  <-- both false
Console.WriteLine(x == y);    // False
Console.WriteLine(x == null); // True
```

Boxing a `Nullable<T>` is special-cased: a null one boxes to `null`, a non-null one boxes to a boxed `T`. There is no such thing as a boxed `Nullable<T>`.

> **\[Why it matters\]**
>
> **Connection to SQL.** This three-valued logic is exactly SQL's `NULL` semantics, and it is why EF Core translates `x != y` on nullable columns into `(x <> y OR (x IS NULL AND y IS NOT NULL) OR ...)`. See [Nullability and three-valued logic](07-02-the-relational-model-and-sql.md#be-d398).

<a id="be-m51"></a>

#### Nullable reference types (compile-time only)

With `<Nullable>enable</Nullable>`, `string` means "not null" and `string?` means "may be null". This is **purely a compiler analysis**. There is no runtime representation, no runtime check, and no difference in the emitted IL beyond a `[Nullable]` attribute for cross-assembly analysis.

``` text
#nullable enable
string s = null;      // warning CS8600, still compiles and runs
void M(string x) { }
M(null!);             // ! suppresses the warning; still a NullReferenceException later
```

Useful attributes when the flow analysis cannot see what you know:

``` text
[NotNullWhen(true)]  static bool TryGet(string k, [NotNullWhen(true)] out string? v)
[MemberNotNull(nameof(_conn))] private void Init()
[NotNullIfNotNull(nameof(input))] static string? Echo(string? input)
```

> **\[Trap\]**
>
> **Trap in a legacy codebase.** Enabling NRT project-wide on an old solution produces thousands of warnings and tempts blanket `!` suppression, which is worse than not enabling it. Enable per-file with `#nullable enable`, or set `<Nullable>annotations</Nullable>` to declare intent without warnings, and turn warnings on file by file as you touch code. Also: data coming from deserialization, EF materialization, or reflection bypasses the analysis entirely — a non-nullable property can absolutely be null at runtime.

<a id="be-m52"></a>

#### DateTime, DateTimeOffset, DateOnly, TimeSpan

| Type             | Size | Holds                   | Use when                                 |
|------------------|------|-------------------------|------------------------------------------|
| `DateTime`       | 8    | ticks + a 2-bit `Kind`  | you genuinely have no offset; be careful |
| `DateTimeOffset` | 16   | ticks + offset from UTC | **default for timestamps**               |
| `DateOnly`       | 4    | a calendar date         | birthdays, invoice dates, no time        |
| `TimeOnly`       | 8    | a clock time            | opening hours, schedules                 |
| `TimeSpan`       | 8    | an elapsed duration     | durations, timeouts                      |

A tick is 100 nanoseconds. `DateTime.Kind` is `Utc`, `Local`, or `Unspecified` — and `Unspecified` is what you get back from most databases, which is where bugs start.

``` csharp
DateTime.UtcNow          // use this
DateTime.Now             // local; depends on server TZ config - avoid on servers
DateTimeOffset.UtcNow    // best: unambiguous

// Resolution: ~1ms on older frameworks, ~100ns on modern .NET.
// For measuring elapsed time, never subtract DateTimes:
var sw = Stopwatch.StartNew();  // monotonic, immune to clock adjustments
long ts = Stopwatch.GetTimestamp();
```

For real time zone work use `TimeZoneInfo` and IANA IDs (`"Asia/Ho_Chi_Minh"`) — since .NET 6 these work on Windows too. `TimeProvider` (.NET 8) is the injectable abstraction that finally makes time testable.

> **\[Trap\]**
>
> **Trap.** Storing local times in a database is a data-loss bug: during a DST fall-back, one local hour occurs twice and you cannot recover which. Store UTC (or `timestamptz`), convert at the edge.

<a id="be-m53"></a>

#### Guid: layout, versions, and index behaviour

A `Guid` is a 16-byte struct. A random (v4) Guid has 122 random bits — collisions are not a practical concern. Its *ordering* is.

``` text
Guid.NewGuid();              // v4, fully random
Guid.CreateVersion7();       // .NET 9: time-ordered (UUIDv7), sortable by creation
Guid.Empty;                  // all zeros - a valid value, so a poor "no value" sentinel
```

**The index problem.** A clustered index on a random Guid means every insert lands at a random point in the B-tree, causing page splits, fragmentation, and a working set the size of the whole index instead of the hot tail. On a high-insert table this is a measurable throughput cliff.

Fixes, in order of preference:

1.  Use a sequential ID (`bigint identity`) as the clustered key and keep the Guid as a non-clustered unique column for external exposure.
2.  Use `UUIDv7` / `Guid.CreateVersion7()` — time-prefixed, so inserts append.
3.  Use SQL Server's `NEWSEQUENTIALID()` (server-generated, so you lose client-side ID assignment).

> **\[Trap\]**
>
> **Trap.** SQL Server's `uniqueidentifier` sort order is *not* byte order — it compares the last 6 bytes first. So a Guid sequence that looks sorted in .NET may not be sorted in SQL Server, and vice versa. Never assume cross-system Guid ordering.

Goes deeper: [Generating identifiers](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)

<a id="be-m54"></a>

#### records, readonly struct, ref struct

``` csharp
record class Person(string Name, int Age);   // reference type, value equality
record struct Point(int X, int Y);           // value type, value equality, MUTABLE
readonly record struct Money(decimal Amount, string Currency); // immutable value type
```

A `record` gives you: a primary constructor, `Equals`/`GetHashCode` by field, a readable `ToString`, `Deconstruct`, and `with` expressions for non-destructive mutation.

``` csharp
var p2 = person with { Age = 31 };   // shallow copy + one change
```

`readonly struct` matters for performance, not just correctness: without it, every method call on a struct held in a `readonly` field or `in` parameter creates a **defensive copy**, because the compiler cannot know the method will not mutate.

``` csharp
readonly struct Vec { public readonly double X, Y;
    public double Length => Math.Sqrt(X*X + Y*Y); }  // no defensive copies

// readonly members on a non-readonly struct give you the same guarantee per-member:
struct Counter { public int N; public readonly int Peek() => N; }
```

`ref struct` is stack-only ([Managed pointers: ref, in, and ref struct](01-05-memory.md#be-m35)). `Span<T>` is the archetype.

> **\[Trap\]**
>
> **Trap.** A `record`'s generated equality compares *all* fields, including collections — by reference. Two records holding equal-but-distinct `List<T>`s are not equal. Override `Equals` or use an immutable/structural collection if you need value semantics all the way down.

<a id="be-m55"></a>

#### Tuples: ValueTuple vs Tuple

``` csharp
(int Count, string Name) t = (5, "abc");   // ValueTuple - struct, no allocation
var old = Tuple.Create(5, "abc");          // Tuple - class, heap allocation, Item1/Item2
```

Use `ValueTuple` everywhere. Names are compiler metadata only — they are erased at runtime, so reflection sees `Item1`, and two tuples with different names are the same type.

``` csharp
// Good uses:
var (min, max) = GetRange();                        // multiple returns
dict.TryGetValue(k, out var v);                     // still prefer out for hot paths
var groups = items.GroupBy(x => (x.Year, x.Region)); // composite key, no custom class
list.Sort((a, b) => (a.Last, a.First).CompareTo((b.Last, b.First))); // multi-key compare
```

Tuple equality and `GetHashCode` are structural and cheap, which makes them an excellent ad-hoc composite dictionary key.

> **\[Tip\]**
>
> **Where not to use them.** Public API surfaces and anything serialized. A tuple has no schema, no validation, and no place to hang documentation. Once a "temporary" tuple crosses two layers, promote it to a `readonly record struct`.

<a id="be-m56"></a>

#### Equality: ==, Equals, GetHashCode, IEquatable

There are four distinct mechanisms and they can disagree:

| Mechanism               | Dispatch                                                | Used by                          |
|-------------------------|---------------------------------------------------------|----------------------------------|
| `operator ==`           | static, resolved at compile time by the *declared* type | your code                        |
| `virtual object.Equals` | virtual, runtime type                                   | collections, LINQ (via comparer) |
| `IEquatable<T>.Equals`  | generic, no boxing                                      | `EqualityComparer<T>.Default`    |
| `ReferenceEquals`       | pointer                                                 | identity checks                  |

``` csharp
object a = "hello", b = new string("hello".ToCharArray());
Console.WriteLine(a == b);        // False! static == on object = reference comparison
Console.WriteLine(a.Equals(b));   // True   virtual dispatch to string.Equals
```

**The contract for `GetHashCode`:** equal objects must return equal hash codes; unequal objects *should* usually differ; and the hash must not change while the object is in a hash-based collection.

``` csharp
public override int GetHashCode() => HashCode.Combine(Name, Age);  // use this, always
```

> **\[Trap\]**
>
> **The mutable-key bug.** Put an object in a `HashSet`, mutate a field used in its hash, and the object is now in the wrong bucket. It becomes unfindable and un-removable, and it will duplicate on re-add. Hash keys must be immutable. This is the single strongest argument for `readonly record struct` keys.

``` text
// Default comparer resolution order for a type T:
// 1. T : IEquatable<T>  -> uses it (no boxing, fast)
// 2. otherwise          -> object.Equals (boxes value types!)
// Always implement IEquatable<T> on structs used as keys.
```

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

<a id="be-m57"></a>

#### Comparison: IComparable, IComparer, stable ordering

`CompareTo` returns negative / zero / positive. The contract requires a **total order**: antisymmetric, transitive, and consistent. Violating it makes `Array.Sort` throw or produce garbage.

``` csharp
// WRONG - overflows for large values, and is not transitive with mixed signs
public int CompareTo(Item o) => this.Value - o.Value;

// RIGHT
public int CompareTo(Item o) => Value.CompareTo(o.Value);

// Multi-key, readable:
public int CompareTo(Item o) {
    int c = LastName.CompareTo(o.LastName);
    return c != 0 ? c : FirstName.CompareTo(o.FirstName);
}
```

**Stability** — whether equal elements keep their original relative order — differs by API and this trips people up:

| API                          | Algorithm                          | Stable?                   |
|------------------------------|------------------------------------|---------------------------|
| `List<T>.Sort`, `Array.Sort` | introsort                          | **No**                    |
| `Enumerable.OrderBy`         | stable quicksort over an index map | **Yes**                   |
| SQL `ORDER BY`               | engine-dependent                   | **No** — never rely on it |

> **\[Tip\]**
>
> **Rule.** Any `ORDER BY` that feeds pagination must be deterministic. If your sort key is not unique, append the primary key as a tiebreaker — otherwise page 2 can repeat or skip rows that page 1 already returned. This is the most common pagination bug in production.

Worked example: [Case: page 500 takes eight seconds](14-03-worked-cases-from-production.md#be-m296)

### Generics and modern data features

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Generics and modern data features</p>

<a id="be-m58"></a>

#### Generic specialization: what the JIT does

C# generics are not C++ templates (compiled per type at build) and not Java generics (erased at compile time). They are **reified** — the type argument survives to runtime — and the JIT decides how much code to generate.

- **Reference type arguments share one compiled body.** `List<string>`, `List<Order>`, and `List<Stream>` all run the same machine code, because every reference is the same 8-byte pointer. The type argument is carried in a hidden handle for the cases that need it.
- **Each value type argument gets its own compiled body.** `List<int>` and `List<double>` are separate code. That is what makes generics allocation-free for structs — there is no boxing, because the code knows the exact layout.

``` text
// Why this matters for equality on struct keys:
EqualityComparer<T>.Default
// - if T : IEquatable<T>, the JIT devirtualises straight to your Equals. No box.
// - otherwise it falls back to object.Equals, which BOXES on every comparison.
// A struct dictionary key without IEquatable<T> allocates twice per lookup.
```

Two costs to know about. Specialisation means **code bloat**: a heavily generic library instantiated over twenty struct types compiles twenty copies, which costs JIT time and instruction cache. And a generic method that takes a type argument it only uses reflectively pays a dictionary lookup on first use per instantiation.

> **\[Tip\]**
>
> **Practical consequence.** A generic method over a value type is often *faster* than the non-generic equivalent, because the JIT can inline through it and eliminate boxing. This is why `Enum.GetValues<T>()` was added, why `Span<T>` works at all, and why you should reach for a generic constraint rather than an `object` parameter whenever the type is known at the call site.

<a id="be-m59"></a>

#### Constraints and what each one unlocks

| Constraint                    | Unlocks                                                                               |
|-------------------------------|---------------------------------------------------------------------------------------|
| `where T : class`             | `null` comparison, reference equality, use as a reference type                        |
| `where T : struct`            | no null, `default` is all-zero, enables `T?` as `Nullable<T>`                         |
| `where T : notnull`           | the dictionary-key constraint; works with NRT analysis                                |
| `where T : new()`             | `new T()` — but goes through `Activator` for reference types, so it is slow in a loop |
| `where T : IFoo`              | interface members *without boxing* when `T` is a struct                               |
| `where T : BaseClass`         | base members, implicit conversion                                                     |
| `where T : unmanaged`         | `sizeof(T)`, `stackalloc T[]`, pointer arithmetic, `MemoryMarshal` casts              |
| `where T : Enum` / `Delegate` | the enum and delegate helper APIs                                                     |
| `where T : U`                 | relating two type parameters                                                          |
| `where T : allows ref struct` | C# 13: lets `T` be a `Span<T>`                                                        |

``` csharp
// The interface constraint is the important one for performance.
static bool Eq<T>(T a, T b) where T : IEquatable<T> => a.Equals(b);  // no box
static bool Eq(IEquatable<object> a, object b) => a.Equals(b);        // boxes structs

// unmanaged lets you treat any blittable struct as bytes:
static Span<byte> AsBytes<T>(ref T value) where T : unmanaged
    => MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), sizeof(T));
```

> **\[Trap\]**
>
> **`new()` is a trap in hot paths.** For reference types the runtime calls `Activator.CreateInstance`, which is roughly an order of magnitude slower than a direct `new`. Pass a `Func<T>` factory instead, or cache a compiled expression.

<a id="be-m60"></a>

#### Variance: in, out, and why arrays are broken

``` text
interface IEnumerable<out T>   // COVARIANT: T only comes out
interface IComparer<in T>      // CONTRAVARIANT: T only goes in
interface IList<T>             // INVARIANT: T goes both ways

IEnumerable<object> a = new List<string>();   // OK - covariance
IComparer<string>   b = someObjectComparer;    // OK - contravariance
IList<object>       c = new List<string>();    // compile error - and rightly so
```

The rule is a safety property, not an inconvenience: you can safely widen what a producer gives you, and safely narrow what a consumer accepts. `IList<T>` is both, so it must be invariant — otherwise you could add an `int` to a `List<string>` through an `IList<object>` alias.

Which is exactly what arrays let you do. Array covariance ([Arrays: layout, covariance, jagged vs multidimensional](03-02-arrays-and-lists.md#be-m98)) predates generics and was never made safe; the runtime pays a per-write type check to catch it, and you get an exception instead of a compile error.

``` csharp
object[] arr = new string[1];
arr[0] = 42;                       // ArrayTypeMismatchException at runtime
```

> **\[Tip\]**
>
> **Design consequence.** Declaring a parameter as `IEnumerable<BaseType>` or `IReadOnlyList<BaseType>` lets callers pass a collection of any derived type without converting. Declaring it as `List<BaseType>` forces them to build a new list. Variance is the reason "accept the widest interface" is good API advice, not just style.

> **\[Trap\]**
>
> **Variance only applies to reference conversions.** `IEnumerable<int>` is *not* an `IEnumerable<object>`, because that would require boxing each element, which is a representation change rather than a reinterpretation. Use `.Cast<object>()` and accept the allocation.

<a id="be-m61"></a>

#### Static abstract members and generic math

Before .NET 7 you could not write one `Sum` that worked for `int`, `double`, and `decimal` without boxing or code generation — there was no way to constrain `T` to "has an `operator +`". Static abstract interface members fixed that.

``` csharp
static T Sum<T>(ReadOnlySpan<T> values) where T : INumber<T>
{
    T total = T.Zero;
    foreach (var v in values) total += v;
    return total;
}
Sum<int>(ints); Sum<decimal>(prices); Sum<double>(readings);   // all specialised, no box
```

The interface hierarchy worth knowing: `INumber<T>` (comparable arithmetic), `INumberBase<T>`, `IAdditionOperators`, `IComparisonOperators`, `IParsable<T>` / `ISpanParsable<T>`, `IFormattable` / `ISpanFormattable` / `IUtf8SpanFormattable`, `IBinaryInteger<T>`, `IFloatingPoint<T>`, `IMinMaxValue<T>`.

``` csharp
// The parse interfaces let you write one generic reader over any parseable type:
static T Read<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T>
    => T.Parse(s, CultureInfo.InvariantCulture);
```

> **\[Why it matters\]**
>
> **Where this shows up in practice.** Generic value objects (`Quantity<T>`, `Measurement<T>`), one aggregation routine over several numeric column types, and generic parsing layers for CSV or protocol decoding. Before this feature, all of those meant either `dynamic` (slow), `Convert` (boxing), or one copy per type.

> **\[Trap\]**
>
> **Cost note.** Because each value-type instantiation compiles separately, a generic-math helper used over eight numeric types produces eight copies of the code. Fine for a few call sites; worth watching in a library with startup-time constraints or ahead-of-time compilation.

<a id="be-m63"></a>

#### Pattern matching over data shapes

``` csharp
// Type + property + relational + logical patterns, combined
string Describe(object o) => o switch {
    null                                    => "nothing",
    int n and (< 0 or > 100)                => "out of range",
    int n                                   => $"number {n}",
    string { Length: 0 }                    => "empty string",
    string s                                => s,
    Order { Total: > 1000, Status: "Open" } => "big open order",
    Order { Customer.Country: "VN" }        => "domestic",     // nested property
    IEnumerable<int> [var first, .., var last] => $"{first}..{last}",  // list pattern
    _                                       => "other"
};
```

List patterns (C# 11) match sequence shape directly, which is a clean replacement for index-and-length checks when parsing:

``` csharp
if (parts is [var cmd, var arg]) { ... }              // exactly two
if (parts is ["SET", var key, var value]) { ... }     // literal first element
if (parts is [_, .., var last]) { ... }               // at least two, capture last
```

> **\[Why it matters\]**
>
> **Why this belongs in a data guide.** Pattern matching is the tool that makes discriminated-union-shaped data ergonomic in C#, and the compiler checks exhaustiveness for enums and closed hierarchies (with `sealed` types it warns on missing cases). It replaces the type-code-plus-cast idiom that legacy codebases are full of, and it produces better IL than a chain of `is`/cast pairs because the type test happens once.

> **\[Trap\]**
>
> **Not translatable to SQL.** A `switch` expression inside a `Where` on an `IQueryable` will not translate. Simple ternaries and `?:` chains usually do (they become `CASE WHEN`); switch expressions generally do not. Keep pattern matching on the client side of the boundary ([The translation boundary](07-07-orms-and-ef-core.md#be-m155)).

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a31"></a>

#### Equality, comparers and hashing

If you override `Equals` you must override `GetHashCode`, because dictionaries and sets bucket by hash first. Two equal objects with different hashes will simply fail to find each other, silently.

``` csharp
public sealed class Sku : IEquatable<Sku>
{
    public string Value { get; }
    public bool Equals(Sku? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? o) => Equals(o as Sku);
    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
}

// or let the compiler do it
public readonly record struct Sku(string Value);

// custom comparison rules without touching the type
var byName = customers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
```

> **\[Trap\]**
>
> **Common trap.** A mutable object used as a dictionary key is a bug waiting to happen: mutate a field that participates in the hash and the entry becomes unreachable, even though it is still in the dictionary. Keys should be immutable value types or records.

###### The GetHashCode contract, and the bug it causes when broken

``` text
  THE CONTRACT

    1  equal objects MUST return the same hash code
    2  unequal objects MAY return the same hash code (a collision)
    3  the hash code MUST NOT CHANGE while the object is used as a key

  rule 3 is the one violated in practice, and the failure is silent.

    var key = new MutableKey { Id = 1 };
    dict[key] = "value";           // stored in bucket hash(1)
    key.Id = 2;                    // hash is now hash(2)
    dict[key];                     // looks in bucket hash(2) -> NOT FOUND
    dict.ContainsKey(key);         // false
    dict.Count;                    // 1  <- the entry is still there, unreachable

  the entry is now permanently lost: it cannot be found, cannot be removed,
  and keeps its value alive. A slow leak with no error.

  -> DICTIONARY KEYS MUST BE IMMUTABLE. Use a record, a readonly struct,
     or a primitive. Never a mutable entity.
```

``` csharp
// records give you correct value equality for free - and that is the point
public record OrderKey(long TenantId, long OrderId);
// Equals, GetHashCode, ToString, Deconstruct, and immutability, all generated.

// when you must write it by hand, use HashCode - never XOR fields yourself
public override int GetHashCode() => HashCode.Combine(TenantId, OrderId, Status);
// HashCode.Combine mixes properly. a ^ b collides for (1,2) and (2,1).

// a comparer, when the type is not yours or equality is context-dependent
public sealed class CaseInsensitiveSku : IEqualityComparer<string>
{
    public bool Equals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public int GetHashCode(string s) =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(s);   // MUST match Equals
}
var d = new Dictionary<string, Item>(new CaseInsensitiveSku());
// simpler: new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
```

| Type              | Default equality                                | Watch for                                                            |
|-------------------|-------------------------------------------------|----------------------------------------------------------------------|
| `class`           | Reference                                       | Two identical objects are not equal unless you override              |
| `record`          | Value, all members                              | Includes members you may not want compared                           |
| `struct`          | Value, but via **reflection** if not overridden | Slow, and it boxes. Always override on a struct you compare          |
| `record struct`   | Value, generated                                | Correct and fast — prefer over plain struct                          |
| Entity with an id | Reference by default                            | Usually you want id equality — and transient entities have no id yet |

For domain entities the honest answer is often to *not* override equality at all. An entity with a database identity has a subtle problem: two instances of an unsaved entity both have id 0, so id-equality makes them equal when they are different objects. Reference equality plus explicit id comparison where you need it is less clever and causes fewer surprises.

### What Modern C# Changed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · What Modern C# Changed</p>

<p class="pd">Several GoF patterns exist to work around limitations C++ and Smalltalk had. Modern C# removed some of those limitations — here is what changed, and what did not.</p>

<a id="be-h758"></a>

#### What generics and pattern matching changed

``` csharp
// GENERICS removed a whole category of "typed wrapper" pattern.
// before: a Repository per type, or object-based collections and casts.
// now: one generic, and the container closes it per type.
public interface IRepository<T> where T : Entity
{
    Task<T?> ByIdAsync(Guid id, CancellationToken ct);
}
services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

// and pipeline behaviours apply to EVERY request type, generically
public sealed class ValidationBehaviour<TReq, TRes>(IEnumerable<IValidator<TReq>> vs)
    : IPipelineBehavior<TReq, TRes> { ... }
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
// Decorator + Chain of Responsibility, applied to an open generic.
// none of this was expressible in the language the catalogue was written for.
```

``` csharp
// PATTERN MATCHING largely displaced Visitor, and shrank State
public static decimal Evaluate(Expression e) => e switch
{
    Literal l                    => l.Value,
    Add(var left, var right)     => Evaluate(left) + Evaluate(right),
    Negate { Inner: var inner }  => -Evaluate(inner),
    // sealed hierarchy -> the compiler WARNS about an unhandled case
};

// and it makes exhaustiveness a compile-time property, which the
// visitor pattern never provided:
public abstract record Expression;
public sealed record Literal(decimal Value) : Expression;
public sealed record Add(Expression Left, Expression Right) : Expression;
// adding a node type now produces a warning at EVERY switch that missed it.
```

``` text
  THE HONEST SUMMARY of this part

  DISSOLVED into the language
    Prototype     -> `with`
    Memento       -> records
    Iterator      -> IEnumerable<T>, yield, IAsyncEnumerable<T>
    Visitor       -> switch expressions over sealed hierarchies

  ABSORBED by the container
    Singleton, Factory Method, Abstract Factory

  UNCHANGED, because they structure BEHAVIOUR, not construction
    Strategy, Decorator, Proxy, Adapter, Facade, Composite,
    Chain of Responsibility, Command, State, Observer, Mediator

  the second list is the one worth studying. It is also, not
  coincidentally, the list of patterns that appear in modern frameworks:
  middleware is Chain of Responsibility, EF Core lazy loading is Proxy,
  ILogger is Adapter + Composite, MediatR is Command + Mediator.
```

> **\[Note\]**
>
> **Why the catalogue is still worth knowing.** Not to implement it. To *read* it — the frameworks you use are built from these patterns, and recognising which one you are looking at is what turns “how does middleware work?” into “oh, it is a chain, each link decides whether to call the next”. That recognition transfers to every framework you meet afterwards.

###### Open generics in DI, and exhaustiveness that actually holds

``` csharp
// registering an OPEN generic - one line covers every closed type
services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
// IRepository<Order>, IRepository<Customer>, ... all resolve.

// and the pipeline form: a decorator applied to EVERY request type
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehaviour<,>));

public sealed class ValidationBehaviour<TReq, TRes>(IEnumerable<IValidator<TReq>> vs)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq r, RequestHandlerDelegate<TRes> next,
                                   CancellationToken ct) { ... }
}
// Decorator + Chain of Responsibility, over an open generic. None of
// this was expressible in the language the GoF catalogue was written for -
// which is why "one pattern per problem" undersells what generics did.
```

``` text
  EXHAUSTIVENESS - what the compiler will and will not guarantee

    public abstract record Shape;
    public sealed record Circle(double R) : Shape;
    public sealed record Square(double S) : Shape;

    double Area(Shape s) => s switch
    {
        Circle c => Math.PI * c.R * c.R,
        Square q => q.S * q.S,
    };
    // CS8509 warning if a case is missing.

  BUT the guarantee is WEAKER than it appears:

    - `abstract record Shape` is NOT closed. Anyone in any assembly
      can derive from it, and the compiler cannot see them.
    - the warning is a WARNING, not an error, unless you elevate it.
    - at runtime, an unmatched value throws SwitchExpressionException.

  TO MAKE IT REAL:

    1  make the hierarchy genuinely closed - private constructor on
       the base, nested subtypes:

         public abstract record Shape
         {
             private Shape() { }                  // nobody outside can derive
             public sealed record Circle(double R) : Shape;
             public sealed record Square(double S) : Shape;
         }

    2  elevate the warning:
         <WarningsAsErrors>CS8509</WarningsAsErrors>

  now adding a case is a BUILD ERROR at every switch that missed it -
  which is the property Visitor was invented to provide, obtained for
  a fraction of the code.
```

| Pattern                        | C# feature that replaced or reshaped it                   |
|--------------------------------|-----------------------------------------------------------|
| Visitor                        | Switch expressions over a closed hierarchy                |
| Iterator                       | `IEnumerable<T>`, `yield`, `IAsyncEnumerable<T>`          |
| Prototype                      | `with` expressions                                        |
| Memento                        | Records                                                   |
| Null Object                    | Still useful — nullable types answer a different question |
| Typed collections / containers | Generics                                                  |
| Decorator over many types      | Open generic registration                                 |
