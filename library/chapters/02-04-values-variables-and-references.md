---
id: "2-4"
number: "2.4"
title: "Values, variables and references"
part: 2
part_title: "From source code to running program"
words: 4171
minutes: 18
---

# 2.4 Values, variables and references

> **Quick look.** What exactly gets copied or compared when you assign, pass or compare a value.
>
> - Assignment and argument passing copy what is in the variable; for a class that is a reference, so both names share one object.
> - ref, out and in pass the variable itself instead of a copy.
> - Classes compare by identity unless they override equality; records and strings compare by value.
> - Equal objects must have equal hash codes, and keys must not change while stored.
> - Lambdas capture variables, not values, which bites in loops.
> - Boxing copies a value type into a heap object: an allocation plus future GC work.


### The big picture

A variable is a name for a piece of storage. That sounds simple, but most surprises in C# come down to one question: when you assign a value, pass it to a method or compare two values, what exactly is copied or compared?

This chapter answers it. You will see why changing an object inside a method also changes it for the caller, and why two identical-looking objects may not be equal. You will also see why changing a key already in a dictionary makes it unfindable, and why lambdas created in a loop can all see the same final value. These behaviours show up in every codebase, so they are worth knowing by heart.

### Before you start

**Builds on:** 1.5 Memory.

**Ideas explained later:**

- **A record** (6.4): A DNS record giving an IPv4 address.
- **Deconstruction** (2.5): Taking a tuple or record apart into separate variables.
- **Immutable collection** (3.2): A collection that can't be changed after creation. "Changing" it returns a new one.
- **Control flow** (2.5): The order in which a program's statements run: sequence, choice and repetition.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, LINQ, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does C# pass to a method by default?**

> A copy. For a value type, a copy of the value. For a reference type, a copy of the reference, so the method can change the object but can't make the caller's variable point somewhere else.

**2. What is the difference between ref, out and in?**

> All three pass the variable itself instead of a copy. `ref` requires it to be set before the call and allows changes. `out` requires the method to set it. `in` passes it read-only, to avoid copying large structs.

**3. Two separate objects of a class have the same field values. Does a == b return true?**

> Not by default. For classes, `==` compares identity: are these the same object? Records, strings and types that override equality compare values instead.

**4. You change a property of an object that is already a key in a HashSet. What happens?**

> If the property takes part in the hash code, the set can no longer find the object. It sits in the bucket for its old hash. Keys must not change while they are in a hash-based collection.

**5. Why can changing a public const in a library fail to update the apps that use it?**

> A `const` value is copied into the calling code at compile time. Apps keep the old value until they are recompiled. `static readonly` is read at run time and doesn't have this problem.

**6. A for loop adds three lambdas that each return the loop variable i. What do they return afterwards?**

> All return 3. The lambdas capture the single variable `i`, not its value at the time. A `foreach` loop creates a new variable on each pass, so its lambdas return 0, 1 and 2.

**7. What does a with expression do on a record?**

> It makes a copy with some properties changed: `flight with { Gate = "B12" }`. The original is untouched.

### Variables are named storage

A **variable** is a name for a piece of storage. The storage holds a value. Where that storage lives, on the stack, inside an object on the heap, or in a register, was covered in Memory (1.5). This chapter is about how values move between variables, and what "the same" means.

**Declaring** a variable creates the name and fixes its type. **Assignment** puts a value into it. **Initializing** is the first assignment.

C# enforces **definite assignment**. You can't read a local variable until the compiler can prove it has been assigned on every path. Fields are different. They start at their type's default value: zero for numbers, `false` for `bool`, `null` for references. `default(T)` gives that value for any type.

### Copying

Assignment always copies what is in the variable.

For a value type, the variable holds the data itself. Assignment copies the data. The two variables are then independent.

For a reference type, the variable holds a reference. Assignment copies the reference. Both variables then refer to the same object, and a change through one is visible through the other.

This single rule explains most surprises about C# variables. It is worth being able to say without thinking: assignment copies the variable's contents, and for classes the contents are a reference.

### Passing arguments

Calling a method is also assignment. Each argument is copied into the method's parameter. This is **pass by value**, and it is C#'s default for every type.

With a class, the reference is copied. The method can change the object through it, and the caller sees the change. But if the method assigns a new object to its parameter, only its own copy changes. The caller's variable still refers to the original object.

To pass the variable itself, use a keyword. A **ref parameter** makes the parameter another name for the caller's variable. Assigning to it changes the caller's variable. `out` is like `ref`, but the method must assign it before returning, and the caller needn't set it first. `int.TryParse(text, out int value)` uses this. `in` passes by reference but read-only. It avoids copying large structs.

```csharp
// values.cs — run with: dotnet run values.cs
using System.Linq;

var flight = new Flight("BA117", "A10");
ChangeGate(flight);
Console.WriteLine(flight.Gate);       // B12: the method changed the shared object

Replace(flight);
Console.WriteLine(flight.Number);     // BA117: the method only replaced its own copy of the reference

ReplaceRef(ref flight);
Console.WriteLine(flight.Number);     // AA100: ref let the method change the caller's variable

int a = 1, b = 2;
Swap(ref a, ref b);
Console.WriteLine($"{a} {b}");        // 2 1

static void ChangeGate(Flight f) => f.Gate = "B12";
static void Replace(Flight f) => f = new Flight("AA100", "C1");
static void ReplaceRef(ref Flight f) => f = new Flight("AA100", "C1");
static void Swap(ref int x, ref int y) => (x, y) = (y, x);

class Flight(string number, string gate)
{
    public string Number { get; } = number;
    public string Gate { get; set; } = gate;
}
```

### Aliasing

When two variables refer to the same object, they are **aliases**. Aliasing is normal and useful. It is also behind a whole family of bugs.

A class hands out its internal `List<T>` through a property. A caller adds an item. The class's data has changed behind its back, and whatever rules it meant to enforce were skipped.

There are three common fixes. Expose a read-only view, such as `IReadOnlyList<T>`. Return a **defensive copy**, a new list with the same items. Or use immutable collections, such as `ImmutableArray<T>`, that can't be changed by anyone.

Read-only views stop callers from changing the collection. They don't stop the owner changing it while a caller is looping over it.

### Identity and equality

"Are these the same?" has two meanings.

**Identity** asks whether two references point to the same object. `ReferenceEquals(a, b)` always answers this question.

**Equality** asks whether two values should be treated as equal, even if they are separate objects. Two `DateTime` values for the same moment are equal. Two strings with the same characters are equal.

For classes, `==` and `Equals` default to identity. A type can override them to compare values. `string` does. Records do, automatically. For structs, `Equals` compares all fields by default, though slowly, using reflection, unless you override it. Record structs generate fast versions.

Every type that overrides `Equals` must also override `GetHashCode`, and keep them consistent. Equal values must have equal **hash codes**. Hash-based collections such as `Dictionary` and `HashSet` use the hash code to choose where to store and look for an item, as covered in Hash tables (3.3). If the hash code changes while an item is stored, the collection looks in the wrong place.

```csharp
// equality.cs — run with: dotnet run equality.cs
using System.Linq;

var p1 = new PlainPoint { X = 1 };
var p2 = new PlainPoint { X = 1 };
Console.WriteLine(p1 == p2);                     // False: a class compares identity

var r1 = new RecordPoint { X = 1 };
var r2 = new RecordPoint { X = 1 };
Console.WriteLine(r1 == r2);                     // True: a record compares values
Console.WriteLine(ReferenceEquals(r1, r2));      // False: still two objects

string s1 = "LHR";
string s2 = new string(new[] { 'L', 'H', 'R' });
Console.WriteLine(s1 == s2);                     // True: string compares characters
Console.WriteLine(ReferenceEquals(s1, s2));      // False

var set = new HashSet<RecordPoint> { r1 };
r1.X = 2;                                        // change a key that is already stored
Console.WriteLine(set.Contains(r1));             // False: it is in the bucket for the old hash
Console.WriteLine(set.Any(p => p == r1));        // True: it is still in there

class PlainPoint { public int X { get; set; } }
record RecordPoint { public int X { get; set; } }
```

### Records and with

A **record** is a type designed to hold data. The compiler generates value-based equality, a readable `ToString` and deconstruction. Positional records are very short: `record Flight(string Number, string Gate);` creates a class with two init-only properties and a constructor.

Records are meant to be used without changing them. A **with expression** creates a copy with some properties changed: `var moved = flight with { Gate = "B12" };`. The original is left as it was.

A `with` expression makes a **shallow copy**. The new record gets the same references as the old one. If a record holds a `List<T>`, both records share that list. Keep records' contents immutable, or copy the collections yourself.

`record` alone means a class. `record struct` makes a value type with the same features. `readonly record struct` also forbids changing its fields.

### Keeping values from changing

C# has several ways to stop a value changing, and they mean different things.

A **compile-time constant**, declared with `const`, is fixed when the code is compiled. Only numbers, strings, booleans, characters and null can be `const`. The compiler copies the value into every place that uses it, even in other assemblies.

`readonly` on a field means it can only be assigned in its declaration or a constructor. The field can't be pointed at something else later, but if it refers to an object, that object can still change.

`init` on a property means it can only be set while the object is being created, including in object initializers and `with` expressions.

`static readonly` is the usual choice for shared values that aren't simple constants, and for public values in libraries, because other assemblies read them at run time.

### Closures capture variables

A lambda or local function can use variables from the method around it. It then forms a **closure**: the function plus the variables it uses.

A closure captures the variable itself, not a snapshot of its value. If the variable changes later, the closure sees the new value. The compiler makes this work by moving captured variables out of the stack frame into a small hidden object on the heap. That is why a captured variable can outlive the method that declared it, as Scope and lifetime (2.6) explains.

This leads to a classic bug with `for` loops, which share one loop variable across all passes. `foreach` loops create a fresh variable on each pass, since C# 5.

```csharp
// closures.cs — run with: dotnet run closures.cs
using System.Linq;

var actions = new List<Func<int>>();
for (int i = 0; i < 3; i++) actions.Add(() => i);
Console.WriteLine(string.Join(",", actions.Select(f => f())));   // 3,3,3: one shared i

actions.Clear();
foreach (int i in new[] { 0, 1, 2 }) actions.Add(() => i);
Console.WriteLine(string.Join(",", actions.Select(f => f())));   // 0,1,2: a new i each pass

actions.Clear();
for (int i = 0; i < 3; i++) { int copy = i; actions.Add(() => copy); }
Console.WriteLine(string.Join(",", actions.Select(f => f())));   // 0,1,2: copy is new each pass

int counter = 0;
Action increment = () => counter++;
increment(); increment();
Console.WriteLine(counter);                                      // 2: the closure changed the real variable
```

### In short

Assignment and argument passing always copy what is in the variable; for classes, that is a reference, so both names share one object. `ref`, `out` and `in` pass the variable itself instead. Classes compare by identity unless they override equality, while records and strings compare by value. Equal objects must have equal hash codes, and keys must not change while they are stored. Lambdas capture variables, not values, which matters in `for` loops.

### What goes wrong

**Leaking internal collections.** A property returns the class's own `List<T>`, and callers change it. Return `IReadOnlyList<T>`, a copy or an immutable collection.

**Mutable hash keys.** An object changes after being added to a `Dictionary` or `HashSet`, and lookups start failing. Make key types immutable, or base equality only on fields that never change.

**`Equals` without `GetHashCode`.** Equal objects get different hash codes and a dictionary treats them as different keys. The compiler warns about this. Records get it right automatically.

**Public `const` in shared libraries.** A library changes `const int MaxPassengers`, and apps that weren't recompiled keep the old value. Use `static readonly` for public values that might ever change.

**Loop variable capture.** Callbacks, timers or tasks created in a `for` loop all see the final value. Copy the variable into a local inside the loop.

**Shallow copies.** A `with` expression or a copy of a list shares the objects inside it. Changing one "copy" changes the other.

### Cost model

Copying a reference costs 8 bytes on a 64-bit machine. Copying a struct costs its full size, so large structs passed around often cost more than small classes. `in` and `ref` avoid the copy. A closure costs one small heap object for its captured variables, created when the method starts, plus a delegate object. In hot paths, `static` lambdas, which can't capture anything, avoid both. Value equality on records compares every field, which is cheap for small records and grows with their size.

### In other languages (optional)

**JavaScript and TypeScript.** Primitives, such as numbers and strings, are copied. Objects and arrays are shared by reference. `===` on objects compares identity, and there is no built-in value equality. `const` stops a variable being reassigned but doesn't freeze the object; `Object.freeze` does, shallowly. `let` in a `for` loop creates a new binding each pass, so closures see different values, while the older `var` shares one.

**Python.** Every variable holds a reference, even for numbers. `is` compares identity, and `==` compares values. Mutable default arguments are a classic trap: `def add(item, items=[])` shares one list across all calls.

**Java.** Also passes everything by value, including references. Records, added in Java 16, give value equality like C# records.

**Rust.** Assignment moves ownership by default. After `let b = a;`, `a` can no longer be used unless its type is copyable. That rules out aliasing bugs at compile time.

### Key terms

- **Variable**: A name for a piece of storage that holds a value.
- **Definite assignment**: The C# rule that a local variable must be assigned on every path before it is read.
- **Default value**: The value a field or array element starts with: zero, false or null.
- **Pass by value**: Passing a copy of each argument into a method. C#'s default for every type.
- **ref parameter**: A parameter that is another name for the caller's variable, so assigning it changes the caller's variable.
- **Alias**: Another variable referring to the same object.
- **Defensive copy**: A copy handed out instead of internal data, so callers can't change the original.
- **Identity**: Whether two references point to the same object.
- **Equality**: Whether two values should be treated as equal, even if they are separate objects.
- **Hash code**: A number computed from a value, used by hash-based collections to decide where to store it.
- **Record**: A C# type for holding data, with generated value equality, ToString and with support.
- **With expression**: Creating a copy of a record with some properties changed.
- **Shallow copy**: A copy that shares the objects inside the original instead of copying them too.
- **Compile-time constant**: A value fixed at compile time and copied into every place it is used. Declared with const.
- **Closure**: A function together with the variables it captured from its surroundings.

### Where this leads

Lambdas and delegates in detail are in Control flow and functions (2.5). How long captured variables and objects live is in Scope and lifetime (2.6). How hash codes decide where items go is in Hash tables (3.3). Designing with immutable values is in Immutability and pure functions (4.4). Sharing mutable objects between threads is in Why concurrency is hard (5.1).

### Check yourself

1. Write a `Crew` class that holds a list of names and exposes it safely. Try three designs: `IReadOnlyList<string>`, a defensive copy and `ImmutableArray<string>`. List what each one protects against.
2. Turn `PlainPoint` in `equality.cs` into a class with correct `Equals` and `GetHashCode` overrides. Check that `p1 == p2` still says false, and explain why.
3. Make `RecordPoint`'s `X` init-only. Show that the `HashSet` bug can no longer be written.
4. Write a record `Leg(string From, string To, List<string> Crew)`. Copy it with `with`, add a name to the copy's crew and check the original.
5. Start three tasks in a `for` loop with `Task.Run(() => Console.WriteLine(i))`. Run it several times and explain the output.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A helper that reordered the caller's list

A pricing helper sorts the passenger list to find the cheapest fare. The booking screen then shows passengers in a different order.

*Before*

```csharp
decimal Cheapest(List<Passenger> ps) { ps.Sort((a, b) => a.Fare.CompareTo(b.Fare)); return ps[0].Fare; }
```

*After*

```csharp
decimal Cheapest(IReadOnlyList<Passenger> ps) => ps.Min(p => p.Fare);   // no mutation, clearer intent
```

> **Lesson:** Passing a reference shares the object. Accept read-only interfaces and avoid mutating arguments.

#### Example 2: Lambdas in a loop that all saw the last value

Retry timers scheduled in a loop all fire for the last flight in the list (older C# `for` loop capture).

*Capture a copy per iteration*

```csharp
for (int i = 0; i < flights.Count; i++)
{
    var flight = flights[i];                        // new variable each iteration
    timers.Add(new Timer(_ => Retry(flight), null, 1000, Timeout.Infinite));
}
// foreach already creates a fresh variable per iteration (C# 5+); `for` does not.
```

> **Lesson:** Lambdas capture variables, not values. Copy the loop value into a local before capturing it.

### CLR: GC, Records, Boxing

<p class="src">From <em>.NET Backend Deep Dive</em> · CLR: GC, Records, Boxing</p>

<a id="dn-27"></a>

#### Boxing / Unboxing

**Boxing** wraps a value type in a heap object. **Unboxing** extracts it (with a type check).

```csharp
int i = 42;
object o = i;          // BOXING: heap allocation, copies 42 into it
int j = (int)o;        // UNBOXING: type check + copy back out
```

Cost: one allocation, one copy, future GC work, and cache misses (data scattered across the heap instead of contiguous).

##### Where boxing happens invisibly

```csharp
// 1) Non-generic collections
ArrayList list = new();
list.Add(42);                             // boxes every element
// -> use List<int>

// 2) A struct implementing an interface, called through that interface
IComparable c = 42;                       // boxes
void Foo(IMyInterface x) { }              // passing a struct boxes it
// -> use generics with a constraint: void Foo<T>(T x) where T : IMyInterface

// 3) params object[] / string.Format / older logging APIs
Console.WriteLine("{0} {1}", intA, intB); // boxes both
// -> modern string interpolation uses DefaultInterpolatedStringHandler and avoids it

// 4) Enums as dictionary keys or in comparisons
dict[MyEnum.A]                            // enum GetHashCode could box (improved in recent .NET)

// 5) Comparing structs that don't override Equals
//    The default ValueType.Equals uses reflection AND boxing -> very slow
public struct P { public int X, Y; }
p1.Equals(p2);                            // slow + boxes
// -> implement IEquatable<P> and override Equals(P other)

// 6) Assigning Nullable<T> to object
int? n = 5; object o2 = n;                // boxes as int (or null)

// 7) LINQ over structs via IEnumerable (not boxing per se, but iterator + interface dispatch)
```

**How to detect it:** BenchmarkDotNet's `[MemoryDiagnoser]` showing non-zero `Allocated` in a method that only touches value types means boxing. Or read the IL and look for the `box` instruction.

##### Interview answer
> "Boxing copies a value type onto the heap so it can be used as a reference type — an allocation, a copy, and future GC work. It shows up in non-generic collections, calling interface methods on structs, `params object[]`, and the default `ValueType.Equals`. Avoid it with generic constraints, `IEquatable<T>`, and `List<T>` instead of `ArrayList`."

---

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b238"></a>

#### Boxing

Putting a value type where a reference is expected allocates a heap box and copies the value in:

``` csharp
int x = 42;
object o = x;          // BOX: allocate 24 bytes, copy 42 into it
int y = (int)o;        // UNBOX: copy back out
```

It happens in more places than people expect:

``` text
IComparable c = 5;                      // boxes — interface reference
ArrayList list = new(); list.Add(5);    // boxes — non-generic collection
object.Equals(5, 5);                    // boxes both
string.Format("{0}", 5);                // boxes (pre-interpolation-handler)
Console.WriteLine("v: " + 5);           // boxes via object overload
enum e = MyEnum.A; Dictionary<MyEnum,_> // enum keys boxed pre-.NET Core
```

The costs are an allocation, GC pressure, an indirection on every read, and loss of cache locality. In a hot loop, boxing is frequently the entire performance problem.

**Generics avoid it**, which is the main reason `List<int>` exists and `ArrayList` doesn't get used: the JIT compiles a specialized `List<int>` that stores raw ints (0.9).

**Structs implementing interfaces box on interface calls** — unless the call goes through a *constrained generic*, where the JIT can dispatch directly:

``` text
void Slow(IComparable c) { }                     // boxes any struct passed in
void Fast<T>(T c) where T : IComparable<T> { }   // no boxing — constrained call
```

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · async / await · Ground Floor</p>

<a id="be-c306"></a>

#### Value types and reference types

In .NET, every type is one or the other, and the difference is *where the data lives* and *what happens when you assign*.

**Value types** (`int`, `double`, `bool`, `DateTime`, `Guid`, all `struct`s, all `enum`s) hold their data directly. Assignment copies the data.

**Reference types** (`class`, `interface`, `string`, arrays, delegates) hold a **reference** — a memory address, 8 bytes on x64 — pointing at an object on the heap. Assignment copies the address, not the object. Two variables then point at the same object.

``` csharp
struct Point { public int X, Y; }
class Person { public string Name; }

var p1 = new Point { X = 1 };
var p2 = p1;         // full copy
p2.X = 99;           // p1.X is still 1

var a = new Person { Name = "Nam" };
var b = a;           // copies the 8-byte reference only
b.Name = "Other";    // a.Name is now "Other" — same object
```

**Where they live** is the part most people get slightly wrong, and it matters for async:

> "Value types go on the stack" is a useful lie. The truth is: **a value type lives wherever its container lives.** A local `int` in a method sits in that method's stack frame. An `int` *field of a class* sits on the heap, inside that object. An `int` in an array sits on the heap, inside the array.

That's exactly what happens in a state machine. Your local `int id` starts life in a stack frame and, after the compiler's transformation, becomes a field of a heap object — same type, different home.

**Boxing** is what happens when a value type is forced onto the heap on its own: `object o = 42;` allocates a heap box, copies the 42 in, and gives you a reference. Costs an allocation and a later collection. This is relevant later because a Release-build state machine is a *struct* that gets boxed at its first suspension — that box is the async allocation everyone talks about.
