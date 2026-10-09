---
id: "0-1"
number: "0.1"
title: "Reading the code in this guide"
part: 0
part_title: "Start here"
words: 4653
minutes: 20
---

# 0.1 Reading the code in this guide

> **Quick look.** A phrasebook for the modern C# syntax used in every example, so the symbols never hide the idea.
>
> - Examples are single-file programs: save as name.cs and run dotnet run name.cs with the .NET 10 SDK.
> - Code runs from the top; type declarations go at the bottom of the file.
> - var lets the compiler infer a fixed type; => reads as "gives"; $"..." inserts values into text.
> - ?. returns null instead of crashing on null; ?? supplies a fallback value.
> - LINQ chains read left to right like a pipeline: Where, Select, Count.
> - Classic slips: = versus ==, the three meanings of =>, the two meanings of using, forgetting await, integer division.


### The big picture

Every example in this guide is real, runnable C#. Modern C# packs a lot into a few characters: arrows, question marks, dollar signs and square brackets. If you haven't written C# recently, those symbols can hide the idea an example is trying to show.

This chapter is a phrasebook. It explains each piece of syntax the examples use, with a tiny example each, in plain terms. It doesn't teach the deeper ideas; it links to the chapters that do. Skim it once, then use it as a reference. Every chapter's "Before you start" box links straight to the sections its code relies on.

### Before you start

**Builds on:** .

**Ideas explained later:**

- **SDK** (11.2): Software development kit: the tools needed to build for a platform. The .NET SDK includes the dotnet command, compilers and MSBuild.
- **Project file** (11.2): The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **JIT** (JIT compiler, 2.2): A compiler that turns bytecode into machine code while the program runs, just before it is needed.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.
- **Exit code** (1.7): The number a process returns when it ends. Zero means success.
- **Operating system** (1.7): The software that shares hardware among programs and protects them from each other.
- **Default value** (2.4): The value a field or array element starts with: zero, false or null.
- **A record** (6.4): A DNS record giving an IPv4 address.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **Terminal** (1.7): A text window where you type commands for the shell to run, such as dotnet run.
- **LINQ** (2.5): Language Integrated Query: C# methods such as Where, Select and Count that filter and transform sequences of items.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does var total = 0; mean?**

> Declare a variable called `total` and let the compiler work out its type from the value. `0` is an `int`, so `total` is an `int`, fixed for good. It is not a dynamic or "any" type.

**2. What does x => x * 2 mean?**

> A small unnamed function, called a lambda: "given x, return x times 2". It is usually passed to another method, as in `numbers.Select(x => x * 2)`.

**3. What does $"{flight} leaves at {time:HH:mm}" produce?**

> A string with the values of `flight` and `time` inserted where the braces are, and the time formatted as hours and minutes. The `$` turns on this string interpolation.

**4. What does record Flight(string Number, string Gate); declare?**

> A complete small class with two read-only properties, `Number` and `Gate`, a constructor taking both, and equality based on their values.

**5. What is the difference between a?.Length and a ?? "none"?**

> `a?.Length` gives `a.Length`, or null if `a` is null, instead of crashing. `a ?? "none"` gives `a`, or `"none"` if `a` is null.

**6. How do you run one of this guide's examples?**

> Install the .NET 10 SDK, save the code as a file such as `bits.cs`, and run `dotnet run bits.cs` in a terminal in the same folder. No project file is needed.

<a id="mu-code-running"></a>

### Running the examples
Every example is a complete program in a single file. To run one:

1. Install the .NET 10 SDK from dot.net. SDK stands for software development kit: the compiler, the runtime and the `dotnet` command.
2. Save the code in a file with a `.cs` extension, for example `bits.cs`.
3. Open a terminal in that folder and run `dotnet run bits.cs`.

The first run takes a few seconds, because the SDK builds the program. Later runs are faster.

Some examples say `dotnet run -c Release file.cs`. The `-c Release` part builds an optimized version, which matters when an example measures speed, as Compilers, interpreters and JIT (2.2) explains.

Comments show what the code prints. In `Console.WriteLine(2 + 2);   // 4`, the `// 4` is the expected output. When a result depends on timing or your machine, the comment says "about" or "usually".

A few examples start with a line like `#:package Microsoft.Extensions.DependencyInjection@10.0.0`. That **package directive** tells `dotnet run` to download a library from NuGet, .NET's public library repository, before building. It only works in single-file programs like these.

<a id="mu-code-statements"></a>

### Statements, expressions and comments
A **statement** is one instruction, ending with a semicolon: `total = total + 1;`. Statements run one after another, from top to bottom.

An **expression** is anything that produces a value: `2 + 2`, `price * 1.2m`, `name.Length`, `IsLate(flight)`. Most statements contain expressions.

Curly braces `{ }` group statements into a block, which belongs to an `if`, a loop, a method or a type.

Text after `//` to the end of the line is a comment, ignored by the compiler. `/* ... */` comments can span lines. `///` comments are documentation comments, shown by editors as help text.

<a id="mu-code-top-level"></a>

### Programs without a Main method
A classic C# program needs a class with a `static void Main()` method where it starts. The examples use **top-level statements** instead, available since C# 9: the program simply starts at the first line of the file and runs down.

In a top-level file, statements come first. Method declarations can appear among them. Type declarations, such as classes, records and enums, must come after all the statements, at the bottom of the file. So when an example uses a type like `Flight` near the top, look for its definition at the end.

`return 0;` on the last line of the top-level statements sets the program's exit code, covered in The operating system (1.7).

A **using directive** at the very top, like `using System.Linq;`, makes the types in that namespace available by their short names. A namespace is a named group of related types, such as `System.Text` for text handling. Common namespaces are included automatically in these programs, and examples add others explicitly where needed.

<a id="mu-code-var"></a>

### Variables and var
A variable is declared with its type and a name, usually with a first value: `int count = 0;`, `string code = "LHR";`, `decimal fare = 120.50m;`.

`var` lets the compiler work out the type from the value on the right: `var count = 0;` is exactly the same as `int count = 0;`. The type is still fixed and checked, as Types (2.3) explains; you just don't have to write it out.

Number literals can carry a letter that sets their type: `120m` is a `decimal`, `1.5f` a `float`, `10L` a `long`, `5u` a `uint`. Underscores are visual separators: `1_000_000` is one million. `0x` starts a hex number and `0b` a binary one, as Bits and bytes (1.1) shows.

`const int Limit = 100;` declares a value that can never change.

<a id="mu-code-methods"></a>

### Methods and local functions
A **method** is a named block of code that can take inputs and return an output. In the examples, most methods are declared after the main code:

```csharp
Console.WriteLine(Square(7));             // 49: calling the method

static int Square(int x)                  // returns an int, takes one int called x
{
    return x * x;
}
```

`x` here is a **parameter**: the name the method uses for its input. The `7` in the call is the **argument**: the actual value passed in. `void` in place of a return type means the method returns nothing.

`static` means the method doesn't belong to any particular object. Inside top-level code, `static` also stops a method from using the variables around it by accident.

A method declared inside the top-level code, or inside another method, is a **local function**. It can use the variables around it, unless it is marked `static`. The examples often put a small helper like this next to the code that needs it.

Arguments can be passed by name for clarity: `Price(cabin: "business", baseFare: 900m)`. A parameter with a default value is optional: in `void Log(string text, bool urgent = false)`, callers may leave out `urgent`.

<a id="mu-code-expression-bodied"></a>

### Short methods with =>
When a method just returns one expression, it can be written with `=>` instead of braces and `return`. This is an **expression-bodied member**:

```csharp
static int Square(int x) => x * x;        // the same as the braces-and-return version above
```

The same arrow appears in two other places, with different meanings: in lambdas and in switch expressions, both below. In every case, read `=>` as "gives".

<a id="mu-code-types"></a>

### Classes, properties and objects
A class describes a kind of object: the data each object holds and the methods it offers. `new` creates an object.

```csharp
var gate = new Gate("B12");
gate.Open();
Console.WriteLine(gate.IsOpen);           // True

class Gate
{
    public string Code { get; }           // a property you can read but not change
    public bool IsOpen { get; private set; }   // readable anywhere, changeable only inside Gate

    public Gate(string code) { Code = code; }  // the constructor runs when the object is created

    public void Open() => IsOpen = true;
}
```

A **property** looks like a field from outside but is accessed through `get` and `set`. `{ get; }` is read-only after construction. `{ get; set; }` can be changed by anyone. `{ get; init; }` can only be set while the object is being created. `public` and `private` control who may use a member, as Scope and lifetime (2.6) explains.

A `struct` is declared the same way and behaves as a value that is copied, as Memory (1.5) explains. `sealed` means no other class may inherit from this one. `abstract` means the class exists only to be inherited from, as Abstraction and interfaces (4.2) covers.

<a id="mu-code-primary-constructors"></a>

### Primary constructors
Many examples declare constructor parameters right after the class name. This is a **primary constructor**, added for classes in C# 12:

```csharp
class Pricer(decimal multiplier)
{
    public decimal Price(decimal fare) => fare * multiplier;   // the parameter is usable in every member
}
```

`new Pricer(1.2m)` creates one. It is shorthand for a constructor that stores the value in a hidden field. When a class inherits from another, the base's arguments follow the base class name: `class EconomySeat(decimal fare) : Seat(fare);`. A declaration that ends with `;` instead of `{ }` has no body at all.

<a id="mu-code-records"></a>

### Records
A record is a class designed for holding data. One line declares everything:

```csharp
record Flight(string Number, string Gate);

var f = new Flight("BA117", "A10");
var moved = f with { Gate = "B12" };      // a copy with one property changed
Console.WriteLine(moved);                 // Flight { Number = BA117, Gate = B12 }
Console.WriteLine(f == new Flight("BA117", "A10"));   // True: records compare by value
```

That single line gives two read-only properties, a constructor, value-based equality and a readable `ToString`. `record struct` makes a value-type version. What records mean for copying and equality is in Values, variables and references (2.4).

<a id="mu-code-strings"></a>

### Strings and string interpolation
A `$` before a string turns on **string interpolation**: expressions inside braces are evaluated and inserted.

```csharp
string flight = "BA117";
int delay = 45;
Console.WriteLine($"{flight} is {delay} minutes late");    // BA117 is 45 minutes late
Console.WriteLine($"{delay / 60.0:F2} hours");             // 0.75 hours: :F2 means two decimal places
Console.WriteLine($"[{flight,-8}]");                        // [BA117   ]: ,-8 pads to 8 characters, left-aligned
Console.WriteLine($"{{literal braces}}");                   // {literal braces}: doubled braces are printed as braces
```

After a colon comes a **format**: `F2` for two decimals, `N0` for thousands separators, `X2` for two hex digits, `B8` for eight binary digits, `HH:mm` for a time. After a comma comes a width: positive pads on the left, negative on the right.

`@"C:\temp"` is a verbatim string, where backslashes are ordinary characters. `"""..."""` is a raw string literal, which can contain quotes and span lines. `\n` inside ordinary strings is a line break, as Text is numbers too (1.3) explains.

<a id="mu-code-collections"></a>

### Arrays, lists and collection expressions
`int[]` is an array of ints and `List<string>` is a growable list of strings, both explained in Arrays and lists (3.2). `Dictionary<string, int>` maps keys to values, covered in Hash tables (3.3).

Items are read by position with square brackets, counting from zero: `codes[0]` is the first. `^1` counts from the end: `codes[^1]` is the last. A range `..` takes a slice: `codes[1..3]` is items 1 and 2, and `codes[..2]` the first two.

A **collection expression**, added in C# 12, builds a collection from items in square brackets. The target type decides what kind:

```csharp
int[] delays = [120, 45, 300];
List<string> codes = ["LHR", "JFK"];
string[] none = [];                       // an empty array
int[] more = [.. delays, 15];             // .. spreads another collection's items in
```

**Target-typed new** leaves out the type when it is already known from the left: `List<string> names = new();` and `Dictionary<string, int> counts = new();`.

<a id="mu-code-generics"></a>

### Generic types at a glance
Angle brackets hold type parameters. `List<int>` is a list of ints; `List<Flight>` a list of flights. A method declared as `static T Max<T>(T a, T b)` works for any type `T`, filled in when it is called. `where T : ...` after it limits which types are allowed. The full story is in Types (2.3).

<a id="mu-code-lambdas"></a>

### Lambdas
A lambda is a small function written inline, without a name. Read the arrow as "gives":

```csharp
Func<int, int> twice = x => x * 2;                 // given x, gives x * 2
Func<int, int, int> add = (a, b) => a + b;         // two inputs
Action hello = () => Console.WriteLine("hello");   // no inputs, no result
Func<int, bool> isLate = d =>
{
    int limit = 60;                                // a lambda can have a full body in braces
    return d > limit;
};
Console.WriteLine(twice(21));                      // 42
```

`Func<int, int>` is the type of a function that takes an `int` and returns an `int`; the last type is always the result. `Action` is a function that returns nothing. Lambdas are usually passed straight into other methods, most often LINQ. How they capture the variables around them is in Values, variables and references (2.4), and more in Control flow and functions (2.5).

<a id="mu-code-linq"></a>

### LINQ
LINQ is a set of methods that work on any sequence: arrays, lists and query results. Each takes a lambda saying what to do with each item. Read a chain from left to right, like a pipeline:

```csharp
int[] delays = [120, 45, 300, 80, 15];
var late = delays.Where(d => d > 60)       // keep the items where the condition is true: 120, 300, 80
                 .OrderBy(d => d)          // sort them: 80, 120, 300
                 .Select(d => d / 60.0)    // transform each one into hours
                 .ToList();                // run the whole chain and collect the results in a list
Console.WriteLine(delays.Count(d => d > 60));   // 3
Console.WriteLine(delays.Sum());                 // 560
```

Other common ones are `Any` and `All` (does any, or every, item match?), `First` and `Min` and `Max`, `Take` and `Skip`, and `Zip`, which pairs up two sequences. A LINQ chain does nothing until something asks for the results, such as `ToList`, `Count` or a `foreach`. That behaviour is explained in Control flow and functions (2.5).

<a id="mu-code-null"></a>

### Handling null
`null` means "no object". A `?` after a type, as in `string?`, says a variable may hold null. How the compiler checks this is in Types (2.3).

Three operators make null easier to handle. The **null-conditional operator** `?.` reads a member only if the object isn't null: `callsign?.Length` gives the length, or null. The **null-coalescing operator** `??` supplies a fallback: `callsign ?? "unknown"`. `??=` assigns only if the variable is currently null. A `!` after an expression, as in `FindFlight()!`, tells the compiler "trust me, this isn't null". It changes nothing at run time, so it is only safe when you are sure.

The `?` has one more job: `condition ? a : b` picks `a` if the condition is true and `b` if not.

<a id="mu-code-switch"></a>

### Switch expressions and patterns
A switch expression picks a result by testing a value against a list of cases, top to bottom. The first matching case wins, and `_` matches anything:

```csharp
static string Band(int altitude) => altitude switch
{
    0 => "on the ground",
    < 10_000 => "low level",              // a comparison
    >= 10_000 and < 18_000 => "mid level",
    _ => "flight level",                  // everything else
};
```

`is` tests a value against a pattern: `if (status is Airborne a)` checks the type and names the result `a`. Braces test properties: `flight is { Delay.TotalMinutes: > 60 }`. Square brackets test lists: `[var first, .., var last]`. All the kinds of pattern are in Control flow and functions (2.5).

<a id="mu-code-tuples"></a>

### Tuples and deconstruction
A tuple groups a few values without declaring a type: `(string Code, int Km)` is a pair. A method can return one: `static (int Min, int Max) Range(int[] values)`. **Deconstruction** unpacks it into separate variables: `var (min, max) = Range(data);`. Swapping two variables is `(a, b) = (b, a);`. `_` means "I don't need this part": `var (_, km) = route;`.

<a id="mu-code-exceptions"></a>

### Exceptions
`throw new InvalidOperationException("message")` stops the current code and reports a problem. `try { ... } catch (SomeException ex) { ... }` runs the catch block if that kind of exception happens in the try block. `finally { ... }` runs afterwards either way. The full picture is in Errors and exceptions (2.8).

<a id="mu-code-using"></a>

### using, in two senses
The word `using` means two unrelated things. At the top of a file, `using System.Linq;` is a using directive, described earlier. Inside code, `using var file = File.OpenRead(path);` means "clean this object up automatically when the current block ends". `using (var x = ...) { }` does the same for one block. Why cleanup matters is in Scope and lifetime (2.6).

<a id="mu-code-async"></a>

### async and await
A method marked `async` can use `await`. `await Task.Delay(100);` pauses that method for 100 milliseconds without blocking the thread, and `await LoadFare(route)` pauses until the fare arrives. A method that does this returns `Task`, or `Task<T>` for a result of type `T`. The examples use `Task.Delay` to stand in for slow network or database calls. Until you reach Async and await (5.3), you can read `await x` as "wait for x, efficiently".

<a id="mu-code-attributes"></a>

### Attributes
Square brackets on the line before a declaration hold an **attribute**: extra information for the compiler, runtime or a library. `[Flags]` marks an enum as a set of bit flags, as Bits and bytes (1.1) shows. `[Fact]` marks a test method for the test runner, as Testing (4.6) shows. Attributes don't change what the code does on their own; something reads them.

### Reading unfamiliar code

When an example looks dense, try this order. Read the comments first: they say what each part is for and what it prints. Find the types it uses, usually defined at the bottom of the file. Read the main code from the top, one statement at a time, and look up any syntax here. Then run it, change one thing, and run it again. An editor such as Visual Studio, VS Code with the C# Dev Kit, or JetBrains Rider shows the type of anything you hover over, which answers most questions instantly.

### In short

The examples are single-file programs, run with `dotnet run file.cs`. Code runs from the top, and types are declared at the bottom. `var` lets the compiler work out a type, `=>` reads as "gives", `$"..."` inserts values into text, and `?.` and `??` handle null. LINQ chains read from left to right like a pipeline. When a line looks unfamiliar, look it up here, then follow the link to the chapter that explains the idea behind it.

### What goes wrong

**`=` versus `==`.** A single `=` assigns; a double `==` compares. C# rejects `if (x = 5)` for most types, which catches the classic mistake.

**Three meanings of `=>`.** An expression-bodied method, a lambda and a switch arm all use the arrow. Look at what comes before it: a method signature, a parameter list or a pattern.

**Two meanings of `?`.** After a type, it means "may be null". Between values, as in `a ? b : c`, it chooses. After an object, as in `a?.b`, it guards against null.

**Two meanings of `using`.** A directive at the top of the file imports a namespace; a statement inside code cleans up an object.

**Forgetting `await`.** Calling an async method without `await` starts it and carries on without waiting, and its errors disappear. The compiler warns about this; read the warning.

**Integer division.** `7 / 2` is 3, not 3.5, because both numbers are integers. Write `7 / 2.0`, as Numbers inside the machine (1.2) explains.

**Types declared too early.** In a top-level program, a class declared above the statements is a compile error. Keep types at the bottom.

### Cost model

Nearly all of this syntax is shorthand that compiles to the same code you would write the long way. `var`, records, primary constructors, interpolated strings and collection expressions cost nothing extra at run time compared with their longer forms. Lambdas and LINQ have small real costs, a delegate call per item and some allocations, discussed in Control flow and functions (2.5). None of it matters for understanding the examples.

### In other languages (optional)

**JavaScript and TypeScript.** Arrow functions look almost the same: `x => x * 2`. Template literals use backticks and a dollar sign: `` `${flight} is late` ``. `?.` and `??` work the same way. TypeScript writes types after names: `let count: number = 0`.

**Python.** f-strings do interpolation: `f"{flight} is late"`. Lambdas use a keyword: `lambda x: x * 2`. Indentation replaces braces, and there are no semicolons.

**Java.** Lambdas use a thin arrow, `x -> x * 2`, and records look similar: `record Flight(String number, String gate) {}`.

### Key terms

- **Statement**: One instruction in a program, usually ending with a semicolon.
- **Expression**: Any piece of code that produces a value, such as `2 + 2` or `name.Length`.
- **Top-level statements**: Code written directly in a file without a Main method. The program starts at the first line.
- **Using directive**: A line such as `using System.Linq;` that makes a namespace's types available by short name.
- **Method**: A named block of code belonging to a type, which can take inputs and return an output.
- **Parameter**: The name a method uses for one of its inputs.
- **Argument**: The actual value passed to a method when calling it.
- **Local function**: A method declared inside other code, next to where it is used.
- **Expression-bodied member**: A method or property written with `=>` and a single expression instead of a block.
- **Property**: A member that looks like a field from outside but is read and written through get and set.
- **Primary constructor**: Constructor parameters written right after a class or struct name, usable throughout its body.
- **String interpolation**: Inserting expression values into a string marked with `$`, such as `$"{flight} is late"`.
- **Collection expression**: Building a collection from items in square brackets, such as `[1, 2, 3]`.
- **Target-typed new**: Writing `new()` without a type name when the type is already known from context.
- **Null-conditional operator**: `?.`, which reads a member only if the object isn't null, and gives null otherwise.
- **Null-coalescing operator**: `??`, which gives its left side unless that is null, and its right side otherwise.
- **Attribute**: Extra information in square brackets attached to a declaration, such as `[Flags]`.
- **Package directive**: A `#:package` line at the top of a single-file program that downloads a NuGet library before building.

### Where this leads

The guide itself starts in Bits and bytes (1.1). The ideas behind the syntax come later, each in its own place. Types and `var` are in Types (2.3). Records and closures are in Values, variables and references (2.4). Lambdas, LINQ and patterns are in Control flow and functions (2.5). `using` and cleanup are in Scope and lifetime (2.6). Exceptions are in Errors and exceptions (2.8), and `await` is in Async and await (5.3).

### Check yourself

1. Install the .NET 10 SDK, save `Console.WriteLine("hello");` as `hello.cs` and run it with `dotnet run hello.cs`.
2. Rewrite `static int Square(int x) { return x * x; }` as an expression-bodied method and as a lambda stored in a `Func<int, int>`.
3. Declare a record `Airport(string Code, string City)`, create two equal ones and check that `==` returns true.
4. Take the LINQ example from this chapter and add a step that keeps only delays under 200 minutes. Predict the output first.
5. Open any example from Part 1 and list every piece of syntax in it that you had to look up here.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Reading a real handler line by line

You join a team and the first file you open is a minimal API endpoint that looks up a booking. Every line uses modern syntax, and you need to know exactly what it does before changing it.

*The code*

```csharp
app.MapGet("/bookings/{code}", async (string code, BookingDb db, CancellationToken ct) =>
{
    var booking = await db.Bookings
        .Where(b => b.Code == code.ToUpperInvariant())
        .Select(b => new BookingDto(b.Code, b.Passenger.Name, b.Flight.Number))
        .SingleOrDefaultAsync(ct);

    return booking is null
        ? Results.NotFound()
        : Results.Ok(booking);
});

record BookingDto(string Code, string Passenger, string Flight);
```

- `async (...) =>` is a lambda the framework calls per request; its parameters are bound from the route (`code`), the DI container (`db`) and the request (`ct`).
- `b => b.Code == ...` is an expression, not code that runs in memory: EF turns it into SQL.
- `Select(... new BookingDto(...))` fetches only three columns instead of whole entities.
- `is null ? ... : ...` returns 404 or 200; `record` declares an immutable DTO with value equality in one line.

> **Lesson:** Read modern C# by asking three questions per line: where does each value come from, what runs in memory versus in the database, and what is returned.

#### Example 2: The integer division that broke a progress bar

An upload page shows 0% until the very end, then jumps to 100%.

**What goes wrong.** Both operands are int, so `uploaded / total` is integer division: 999 / 1000 is 0.

*Before*

```csharp
long uploaded = 999, total = 1000;
int percent = (int)(uploaded / total * 100);   // 0
```

*After*

```csharp
int percent = (int)(uploaded * 100 / total);   // 99: multiply first, still integers
double exact = uploaded * 100.0 / total;       // 99.9: force floating point with a double literal
```

> **Lesson:** Integer division truncates silently. Multiply first or make one operand floating point, and check edge cases like 0 and total.
