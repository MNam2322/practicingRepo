---
id: "1-2"
number: "1.2"
title: "Numbers inside the machine"
part: 1
part_title: "The machine"
words: 10599
minutes: 46
---

# 1.2 Numbers inside the machine

> **Quick look.** How numbers are stored, where each type breaks, and which type to use for money, measurements and time.
>
> - Integers have fixed ranges and wrap silently on overflow unless you use checked arithmetic.
> - double is fast but approximate (0.1 can't be stored exactly): never compare with ==, never use it for money.
> - decimal is exact for base-10 values and is the money type; store the currency with every amount.
> - Rounding is a business decision: .NET defaults to banker's rounding. Round once, at the end, through one helper.
> - Splitting amounts must not lose cents: give the remainder to someone explicitly so the parts still sum to the total.
> - Store and send times as UTC (DateTimeOffset for instants), convert to local only for display, and inject the clock (TimeProvider).
> - Use half-open intervals [start, end); store recurring events as a rule, not an expanded list.
> - Treat ids as strings: JavaScript loses precision on integers past 2^53.


### The big picture

Bits become numbers, and the way they do explains a whole family of real bugs: counters that suddenly go negative, invoices that are a penny out, and ids that change on their way to a web page.

A computer uses a fixed number of bits for each number, so every number type has limits. Whole numbers wrap around when they overflow, like a car's odometer rolling over. Fractions are stored in a binary form of scientific notation, so most decimal values, even 0.1, are only approximate. This chapter shows how each kind is stored, where each one breaks, and which C# type to choose for money, measurements, times and ids.

### Before you start

**Builds on:** 1.1 Bits and bytes.

**Ideas explained later:**

- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Project file** (11.2): The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **Narrowing** (9.3): The compiler working out a more specific type inside a branch from a check.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.

**Tools and technologies named here:**

- **.NET** (2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **SQL Server** (7.2): Microsoft's relational database product.

**The code uses:** variables and var, methods and local functions, generic types, LINQ, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. In C#, what does int.MaxValue + 1 give at run time by default?**

> −2,147,483,648, which is `int.MinValue`. C# arithmetic is unchecked unless you ask, so the value wraps around silently.

**2. Why is 0.1 + 0.2 not equal to 0.3 with double?**

> 0.1, 0.2 and 0.3 have no exact binary form. Each is stored as the nearest possible double. The sum of the two nearest values rounds to a different double than the nearest value to 0.3.

**3. When should you use decimal instead of double?**

> For money and other base-10 quantities that must add up exactly. Use double for measurements, science and graphics, where tiny rounding errors don't matter and speed does.

**4. What is the largest whole number a JavaScript number holds exactly?**

> 2⁵³ − 1, available as `Number.MAX_SAFE_INTEGER`. Every JavaScript number is a double, and doubles stop representing every integer above that.

**5. What is −7 % 2 in C#?**

> −1. The remainder takes the sign of the left-hand number. That is why `x % 2 == 1` misses negative odd numbers.

**6. What does Math.Round(2.5) return in C#?**

> 2. By default .NET rounds a value exactly halfway to the nearest even number. This is called banker's rounding. Pass `MidpointRounding.AwayFromZero` to get 3.

**7. Is double.Epsilon a good tolerance for comparing doubles?**

> No. It is the smallest positive double, about 5 × 10⁻³²⁴. The gap between 1.0 and the next double is about 2.2 × 10⁻¹⁶, and the gap grows with the size of the number. A tolerance has to match the size of your values.

**8. What happens to a signed 32-bit Unix timestamp in January 2038?**

> It overflows. The largest value, 2,147,483,647 seconds after 1 January 1970, is 19 January 2038 at 03:14:07 UTC.

**9. Why can double.Parse("1,5") return 15 on one server and 1.5 on another?**

> Parsing uses the current culture by default. In US English the comma is a thousands separator, so the result is 15. In German it is the decimal separator, so the result is 1.5. Use `CultureInfo.InvariantCulture` for data.

### Whole numbers without a sign

The simplest number type is an unsigned integer. It uses every bit for magnitude, exactly as in Bits and bytes (1.1). With *n* bits it holds 0 to 2ⁿ − 1.

| C# type | Bits | Range |
|---|---:|---|
| `byte` | 8 | 0 to 255 |
| `ushort` | 16 | 0 to 65,535 |
| `uint` | 32 | 0 to 4,294,967,295 |
| `ulong` | 64 | 0 to about 18.4 quintillion |

### Overflow

A number type has a fixed width. When a result needs more bits than the type has, the extra top bit is dropped. The value wraps around, like a car odometer going from 999999 to 000000. This is called **overflow**.

C# wraps silently by default. You can ask it to throw an `OverflowException` instead. Wrap an expression in `checked(...)`, or turn it on for a whole project with `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` in the project file.

### Negative numbers

Signed integers use a scheme called **two's complement**. The idea is simple. The top bit counts as negative.

In an 8-bit signed number, the top position is worth −128 instead of +128. The other seven positions keep their normal values.

So `0111 1111` is 127. `1000 0000` is −128. `1111 1111` is −128 + 127, which is −1.

To negate a number, flip all its bits and add 1. Take 5, which is `0000 0101`. Flip it to `1111 1010`. Add 1 to get `1111 1011`. Check it: −128 + 64 + 32 + 16 + 8 + 2 + 1 = −5.

Hardware uses this scheme because the same adding circuit works for signed and unsigned numbers. It also gives only one zero.

The range comes out lopsided. An 8-bit signed number goes from −128 to 127. There is one more negative value than positive. So `Math.Abs(int.MinValue)` has no answer that fits in an `int`. C# throws an `OverflowException`. Negating `int.MinValue` in unchecked code gives `int.MinValue` back.

| C# type | Bits | Range |
|---|---:|---|
| `sbyte` | 8 | −128 to 127 |
| `short` | 16 | −32,768 to 32,767 |
| `int` | 32 | −2,147,483,648 to 2,147,483,647 |
| `long` | 64 | about ±9.2 quintillion |

### Integer division

Dividing two integers gives an integer. The fraction is dropped, rounding toward zero. `7 / 2` is 3. `−7 / 2` is −3.

The remainder operator `%` takes the sign of the left-hand number. `−7 % 2` is −1. Python is different. It rounds down instead of toward zero, so in Python `−7 // 2` is −4 and `−7 % 2` is 1.

Dividing an integer by zero throws a `DivideByZeroException`. Dividing a double by zero does not throw. It gives infinity.

### Converting between number types

Moving a value into a bigger type is a **widening conversion**. An `int` always fits in a `long`, so C# does it for you without a cast.

Moving a value into a smaller type is a **narrowing conversion**. It can lose information, so C# makes you write a cast. The cast keeps only the low bits that fit. It does not check anything unless you use `checked`.

One widening conversion is not safe. C# converts `long` to `double` implicitly, but a double only holds 53 bits of precision. Large `long` values are silently rounded.

Casting a double to an integer drops the fraction. `Convert.ToInt32` rounds instead. Both are useful, and mixing them up is a common bug.

```csharp
// convert.cs — run with: dotnet run convert.cs
int i = 1_000;
long l = i;                                  // widening: implicit and safe
byte b = (byte)i;                            // narrowing: keeps the low 8 bits
Console.WriteLine(b);                        // 232  (1000 is 0x3E8; the low byte is 0xE8)
try { Console.WriteLine(checked((byte)i)); }
catch (OverflowException) { Console.WriteLine("1000 does not fit in a byte"); }

long big = 9_007_199_254_740_993;            // 2⁵³ + 1
double lossy = big;                          // implicit, but not exact
Console.WriteLine((long)lossy);              // 9007199254740992: the 1 was lost

Console.WriteLine((int)2.7);                 // 2: a cast truncates
Console.WriteLine(Convert.ToInt32(2.7));     // 3: Convert rounds
Console.WriteLine(Convert.ToInt32(2.5));     // 2: ties go to the even number

Console.WriteLine(Math.Round(2.5));                                 // 2
Console.WriteLine(Math.Round(3.5));                                 // 4
Console.WriteLine(Math.Round(2.5, MidpointRounding.AwayFromZero));  // 3
Console.WriteLine(Math.Round(2.345m, 2));                           // 2.34
Console.WriteLine(Math.Round(2.345m, 2, MidpointRounding.AwayFromZero)); // 2.35
Console.WriteLine(Math.Round(1.005, 2, MidpointRounding.AwayFromZero));  // 1, not 1.01: the double is really 1.00499999...
Console.WriteLine(Math.Round(1.005m, 2, MidpointRounding.AwayFromZero)); // 1.01: a decimal holds 1.005 exactly
```

### Rounding

Rounding sounds simple until a value sits exactly halfway. Rounding 2.5 to a whole number could give 2 or 3.

.NET's default rule rounds halfway values to the nearest even number. 2.5 becomes 2, and 3.5 becomes 4. This is **banker's rounding**. Over many values, it rounds up and down equally often, so totals don't drift upward.

Most people learned "round half up" at school. In .NET that is `MidpointRounding.AwayFromZero`. Tax, invoicing and payment rules usually say which rule to use. Follow the rule, and pass it explicitly every time.

The last two lines show a second trap. 1.005 as a double is really slightly less than 1.005, so even rounding away from zero gives 1, not 1.01: there is no midpoint to round away from. The same value as a `decimal` rounds as expected. Rounding exact money values needs `decimal`.

### Fractions: floating point

Fractions use **floating point**. It is scientific notation in base 2. A number is stored as three parts: a sign, an exponent and a fraction. The exponent says where the binary point "floats" to. The standard is called IEEE 754, and every mainstream processor and language uses it.

A `double` is 64 bits: 1 sign bit, 11 exponent bits and 52 fraction bits. That gives about 15 to 17 significant decimal digits. A `float` is 32 bits and gives about 6 to 9 digits.

Here is the catch. Only values built from powers of two are exact. 0.5, 0.25 and 0.75 are exact. 0.1 is not. In binary, 0.1 repeats forever, the same way 1/3 repeats in decimal. So the machine stores the nearest value it can. Small errors appear, and they add up over many operations.

Precision also shrinks as numbers grow. Above 2⁵³, a double cannot represent every integer. 2⁵³ + 1 rounds to 2⁵³. This matters in JavaScript, where every number is a double. A 64-bit database id sent as a JSON number can arrive in the browser slightly wrong. Send such ids as strings, or use `BigInt`.

Floating point has three special values. Positive infinity, negative infinity and **NaN**, which means "not a number". You get NaN from things like `0.0 / 0.0` or the square root of −1. NaN is not equal to anything, not even itself. There is also a negative zero. It equals zero, but `1 / −0.0` gives negative infinity.

Because of rounding, never compare doubles with `==`. Check whether they are close enough instead.

### Inside a double

A double stores a number as a sign, an exponent and a fraction. The value is 1.fraction × 2^exponent. The exponent is stored with 1023 added to it, so that negative exponents fit in an unsigned field. This offset is called the **bias**.

The gaps between doubles grow with their size. Just above 1.0, the next double is about 2.2 × 10⁻¹⁶ away. That gap is called **machine epsilon**. Near 10¹⁶, the gap is 2, so odd whole numbers are gone.

Very small numbers near zero get special treatment. They are called **subnormal** numbers, and they lose precision gradually instead of dropping straight to zero. On some processors, arithmetic on subnormals is much slower.

```csharp
// double.cs — run with: dotnet run double.cs
Console.WriteLine(Math.BitIncrement(1.0) - 1.0);    // 2.220446049250313E-16: machine epsilon
Console.WriteLine(double.Epsilon);                  // 5E-324: the smallest positive double
Console.WriteLine(Math.BitIncrement(1e16) - 1e16);  // 2: the gap near 10¹⁶
Console.WriteLine(BitConverter.DoubleToInt64Bits(1.0).ToString("X16"));  // 3FF0000000000000

double total = 0;
for (int k = 0; k < 10; k++) total += 0.1;
Console.WriteLine(total);           // 0.9999999999999999
Console.WriteLine(total == 1.0);    // False

decimal exact = 0;
for (int k = 0; k < 10; k++) exact += 0.1m;
Console.WriteLine(exact);           // 1.0

// A tolerance that scales with the size of the values
static bool Close(double a, double b, double rel = 1e-9) =>
    Math.Abs(a - b) <= rel * Math.Max(Math.Abs(a), Math.Abs(b));
Console.WriteLine(Close(total, 1.0));   // True
```

### Adding up many values

Each floating-point addition can round. Over millions of additions, the errors build up. Order matters too. Adding a tiny number to a huge one can lose the tiny number completely.

There are three fixes. Use `decimal` when values are exact base-10 amounts. Add small values before large ones. Or use **compensated summation**, such as the Kahan algorithm, which keeps track of the rounding error and feeds it back in.

A tolerance for comparisons should scale with the values. A fixed `1e-9` is far too strict for numbers around a billion, and far too loose for numbers around a millionth. The `Close` helper above uses a relative tolerance.

### decimal

C# has a `decimal` type for exact base-10 values. It is 128 bits. It stores digits in base 10 plus a scale that says where the decimal point goes. It holds 28 to 29 significant digits, and 0.1 is exact.

Use it for money. The price is speed and size. `decimal` arithmetic runs in software rather than in dedicated hardware, and each value takes 16 bytes instead of 8.

Databases have the same split. In SQL, `DECIMAL(p, s)` and `NUMERIC` are exact. `FLOAT` and `REAL` are binary floating point. EF Core logs a warning when a `decimal` property has no precision set, and SQL Server then uses `decimal(18,2)`.

```csharp
// numbers.cs — run with: dotnet run numbers.cs
byte b = 255;
b++;
Console.WriteLine(b);                        // 0: wrapped around

int big = int.MaxValue;
Console.WriteLine(big + 1);                  // -2147483648: silent overflow
try { Console.WriteLine(checked(big + 1)); }
catch (OverflowException) { Console.WriteLine("checked() caught the overflow"); }

Console.WriteLine(((sbyte)-5).ToString("B8"));   // 11111011: two's complement
Console.WriteLine(7 / 2);                    // 3
Console.WriteLine(-7 / 2);                   // -3: rounds toward zero
Console.WriteLine(-7 % 2);                   // -1: sign of the left side

Console.WriteLine(0.1 + 0.2);                // 0.30000000000000004
Console.WriteLine(0.1 + 0.2 == 0.3);         // False
Console.WriteLine(Math.Abs(0.1 + 0.2 - 0.3) < 1e-9);  // True: compare with a tolerance
Console.WriteLine(0.1m + 0.2m == 0.3m);      // True: decimal is exact here

double nan = double.NaN;
Console.WriteLine(nan == nan);               // False; the compiler even warns that comparing a value with itself looks like a mistake
Console.WriteLine(double.IsNaN(nan));        // True: the only reliable test
Console.WriteLine(1.0 / 0);                  // ∞
Console.WriteLine((double)(1L << 53) + 1 == (double)(1L << 53));  // True: precision ran out
```

### Numbers bigger than 64 bits

Sometimes 64 bits is not enough. .NET has three options.

`Int128` and `UInt128`, added in .NET 7, are fixed-size 128-bit integers. They are fast, because the processor handles them as two 64-bit halves.

`BigInteger` has no size limit. It grows as needed and stores its digits on the heap. It is much slower than fixed-size integers, and it gets slower as numbers get longer.

At the other end, `Half` is a 16-bit floating-point type. It is used in graphics and machine learning, where memory matters more than precision.

### Dates and times are numbers too

A date is stored as a count from a starting point, called an **epoch**. .NET's `DateTime` counts **ticks** of 100 nanoseconds since 1 January of the year 1. **Unix time** counts seconds since 1 January 1970, UTC. It is the common format between systems.

Old systems stored Unix time in a signed 32-bit integer. That runs out on 19 January 2038. Any code or database column that still stores seconds as a 32-bit `int` will break then.

`DateTime` has a weakness. Its `Kind` can be local, UTC or unspecified, and many bugs come from mixing them. `DateTimeOffset` stores the offset from UTC alongside the time. On servers, store and compare times in UTC or as `DateTimeOffset`. Convert to a local time zone only for display.

```csharp
// more.cs — run with: dotnet run more.cs
using System.Globalization;
using System.Numerics;

BigInteger f = 1;
for (int k = 2; k <= 30; k++) f *= k;
Console.WriteLine(f);                   // 265252859812191058636308480000000  (30 factorial)
Console.WriteLine(Int128.MaxValue);     // 170141183460469231731687303715884105727

var t = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
Console.WriteLine(t.ToUnixTimeSeconds());                                  // 1790596800
Console.WriteLine(DateTimeOffset.FromUnixTimeSeconds(int.MaxValue).ToString("u"));  // 2038-01-19 03:14:07Z

// Machine data: always invariant culture
Console.WriteLine(double.Parse("1.5", CultureInfo.InvariantCulture));      // 1.5
Console.WriteLine(1234.5.ToString("N2", CultureInfo.InvariantCulture));    // 1,234.50
Console.WriteLine(1234.5.ToString("N2", new CultureInfo("de-DE")));        // 1.234,50 (throws in invariant globalization mode)
Console.WriteLine(int.TryParse("12x", out _));                             // False: no exception

// Generic math: one method for every number type
Console.WriteLine(SumAll(new[] { 1, 2, 3 }));      // 6
Console.WriteLine(SumAll(new[] { 0.5, 0.25 }));    // 0.75
Console.WriteLine(SumAll(new[] { 0.1m, 0.2m }));   // 0.3

static T SumAll<T>(IEnumerable<T> values) where T : INumber<T>
{
    T total = T.Zero;
    foreach (T v in values) total += v;
    return total;
}
```

### Parsing and formatting

Turning text into a number is **parsing**. Turning a number into text is **formatting**. Both depend on culture by default. Cultures disagree about the decimal separator, the thousands separator and digit grouping.

The rule is simple. Data meant for machines, such as files, APIs, logs and configuration, uses `CultureInfo.InvariantCulture`. Text shown to a person uses that person's culture.

`int.Parse` throws on bad input. `int.TryParse` returns false instead. At a boundary where bad input is normal, such as a form field or a CSV import, use `TryParse`.

### Writing code for any number type

Since .NET 7, number types share interfaces such as `INumber<T>`. This is called **generic math**. The `SumAll` method above works for `int`, `double`, `decimal` and any other number type, with no copies of the code. The interfaces use static abstract members, so `T.Zero` and `+` resolve at compile time and cost nothing at run time.

### In short

Integer types hold a fixed range and wrap around silently when a result doesn't fit, unless you ask for `checked` arithmetic. Negative numbers use two's complement, which makes the range lopsided. `double` is fast but approximate: never compare doubles with `==`, and never store money in them. `decimal` is exact for base-10 values and right for money. Rounding, parsing and dates each have their own traps, so pass the rounding rule and the culture explicitly, and store times in UTC.

### What goes wrong

**Silent overflow in counters and time maths.** A 32-bit count of milliseconds overflows after 24.8 days as a signed `int`. As an unsigned value it lasts 49.7 days. Both limits have crashed real systems. Use `long` for sums, totals and durations, or use `TimeSpan`.

**Money in double.** Totals drift by fractions of a cent. Reports stop matching invoices. Use `decimal` in code and `DECIMAL` in the database.

**`x % 2 == 1` for odd numbers.** It fails for negative numbers. Use `x % 2 != 0`.

**Large ids in JavaScript.** Ids above 2⁵³ get silently rounded in the browser. Two different records end up with the same id.

**NaN spreading quietly.** Any arithmetic with NaN gives NaN. One bad input can turn a whole report into NaN. Sorting a list that contains NaN can give strange orders. Check inputs at the boundary.

**Casting a double to an int.** The cast truncates, so `(int)2.9999999` is 2. Values outside the int range have given different results on different .NET versions and processors. Check the range first, or use `Math.Round` and `checked`.

**Surprise banker's rounding.** An invoice total rounds 0.125 to 0.12 where the business expected 0.13. Pass a `MidpointRounding` value every time you round money.

**Implicit `long` to `double`.** Passing a large id or tick count through a `double` silently changes it. Keep large integers as `long`, `decimal` or strings.

**Parsing with the server's culture.** A service that parses "1.5" correctly on one server reads it as 15 after a move to a server with a different culture. Pass `InvariantCulture` for all machine data. Code analyzer CA1305 warns about this.

**Mixing local and UTC times.** `DateTime.Now` on a server depends on the server's time zone. Daylight-saving changes create hours that happen twice or never. Use `DateTimeOffset.UtcNow`, or `TimeProvider` for testable code.

**Using `double.Epsilon` as a tolerance.** It is so small that it works like `==`. Use a relative tolerance.

### Cost model

Adding or multiplying `int` and `long` values takes about one clock cycle. Double addition and multiplication take a few cycles. Integer division is much slower, often tens of cycles. `decimal` runs in software and is commonly an order of magnitude or more slower than `double`.

For ordinary business code this rarely matters, because the time goes to databases and networks. In a tight loop over millions of values, it does matter.

`Int128` costs about two 64-bit operations. `BigInteger` allocates on the heap, and multiplication gets slower as the numbers grow. Parsing and formatting numbers costs far more than arithmetic, typically tens to hundreds of nanoseconds per value. That matters when importing millions of CSV rows.

### In other languages (optional)

**JavaScript and TypeScript.** Every `number` is a double. `Math.round(2.5)` is 3 but `Math.round(-2.5)` is −2, because it rounds halfway values up toward positive infinity. `BigInt` values can't be mixed with numbers in arithmetic; `1n + 1` throws a `TypeError`. Integer division needs `Math.trunc(a / b)`.

**Python.** Integers never overflow; they grow as needed. `round(2.5)` is 2, the same banker's rule as .NET. `//` rounds down rather than toward zero. The `decimal` and `fractions` modules give exact arithmetic.

**SQL Server.** `7 / 2` is 3, because both sides are integers. `ROUND(2.5, 0)` rounds away from zero and gives 3.0. The `MONEY` type has only four decimal places and can lose precision in division, so many teams use `DECIMAL(19, 4)` instead.

**JSON.** The format itself has no size limit for numbers. Most parsers read them as doubles, so large integers need the same care as in JavaScript.

### Key terms

- **Unsigned integer**: A whole number with no sign, using every bit for magnitude. With n bits it holds 0 to 2ⁿ − 1.
- **Overflow**: A result too large for its type. The extra top bits are dropped and the value wraps around.
- **Two's complement**: The standard way to store negative integers. The top bit counts as a large negative value.
- **Floating point**: Scientific notation in base 2: a sign, an exponent and a fraction. Fast, but most decimal fractions are approximate.
- **NaN**: Not a number. The result of undefined operations like 0.0 / 0.0. It is not equal to anything, including itself.
- **Machine epsilon**: The gap between 1.0 and the next double, about 2.2 × 10⁻¹⁶. A guide to how precise a double is.
- **Widening conversion**: Converting a value to a larger type. Usually safe and implicit.
- **Narrowing conversion**: Converting a value to a smaller type. It can lose information, so it needs an explicit cast.
- **Banker's rounding**: Rounding halfway values to the nearest even number. .NET's default.
- **Epoch**: The starting point a date or time is counted from, such as 1 January 1970 for Unix time.
- **Unix time**: The number of seconds since 1 January 1970, 00:00 UTC.
- **Generic math**: Number interfaces such as `INumber<T>` that let one method work with every number type.

### Where this leads

Characters are stored as numbers in Text is numbers too (1.3). Languages attach meaning and rules to these bit patterns in Types (2.3). SQL's numeric types come back in The relational model and SQL (7.2). JavaScript's single number type is covered in Modern JavaScript (9.2).

### Check yourself

1. Predict the output of `sbyte s = 127; s++; Console.WriteLine(s);` before running it.
2. Print `0.1.ToString("G17")`. You will see the value the machine actually stored.
3. Write a method that sums an `int[]` of prices in cents into a `long`. Explain why the result type should be `long` even though the inputs are `int`.
4. Find a `decimal` property in one of your EF Core models. Check whether its precision is configured.
5. Write `RoundMoney(decimal amount)` that rounds to 2 places, half away from zero. Test it with 0.125m, 0.135m and −0.125m.
6. Search a codebase you know for `double.Parse`, `ToString()` on numbers and `DateTime.Now`. Decide which calls need `InvariantCulture` or UTC.
7. Use `SumAll` from `more.cs` with `Half`, `Int128` and `BigInteger`. Explain why one of them needs care with large arrays.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The invoice that was one cent out

A booking total is split across three passengers. The three shares add up to 99.99 for a 100.00 fare, and finance reconciliation fails.

*Allocate the remainder explicitly*

```csharp
static decimal[] Split(decimal total, int parts)
{
    var share = Math.Floor(total / parts * 100m) / 100m;   // 33.33
    var result = Enumerable.Repeat(share, parts).ToArray();
    var remainder = total - share * parts;                 // 0.01
    for (int i = 0; remainder > 0; i++, remainder -= 0.01m)
        result[i] += 0.01m;                                 // first passenger pays 33.34
    return result;                                          // sums to exactly 100.00
}
```

- Use decimal, never double, for money.
- Test the invariant: the parts always sum to the total.

> **Lesson:** Rounding each part independently loses cents. Decide who gets the remainder and make the sum an invariant you test.

#### Example 2: A flight that departed before it was booked

Reports show some bookings created after the flight's departure. The booking service stores `DateTime.Now` on a server in Singapore; flights are stored in UTC.

*Before*

```csharp
booking.CreatedAt = DateTime.Now;   // local server time, Kind = Local, offset lost in the database
```

*After*

```csharp
public sealed class BookingService(TimeProvider clock)
{
    public Booking Create(Flight f)
    {
        var now = clock.GetUtcNow();                 // DateTimeOffset in UTC
        if (now >= f.DepartureUtc) throw new InvalidOperationException("Flight has departed");
        return new Booking { CreatedAtUtc = now };
    }
}
// Tests pass a FakeTimeProvider and set any time they need.
```

> **Lesson:** Store instants in UTC as DateTimeOffset, convert only for display, and inject the clock so time-based rules are testable.

### Primitives

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Primitives</p>

<a id="be-b261"></a>

#### Integers and overflow

| Type               | Bytes  | Range              |
|--------------------|--------|--------------------|
| `sbyte`/`byte`     | 1      | −128..127 / 0..255 |
| `short`/`ushort`   | 2      | ±32K / 0..65K      |
| `int`/`uint`       | 4      | ±2.1B / 0..4.2B    |
| `long`/`ulong`     | 8      | ±9.2×10¹⁸          |
| `nint`/`nuint`     | 4 or 8 | pointer-sized      |
| `Int128`/`UInt128` | 16     | .NET 7+            |

**Arithmetic is unchecked by default** — it wraps silently:

``` csharp
int max = int.MaxValue;
max + 1;                       // −2147483648, no error

checked { max + 1; }           // OverflowException
```

Turn it on project-wide for anything financial or safety-relevant:

``` xml
<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
```

Note that `checked` does not apply to `decimal` (always checked) or floating point (never throws — it produces `Infinity`).

<a id="be-b262"></a>

#### Floating point

``` text
0.1 + 0.2 == 0.3            // false
(0.1 + 0.2).ToString("R")   // 0.30000000000000004
```

IEEE 754 binary floating point cannot represent 0.1 exactly, for the same reason decimal cannot represent 1/3. This isn't a bug and it never gets fixed.

| Type      | Bytes | Precision             | Use for                            |
|-----------|-------|-----------------------|------------------------------------|
| `float`   | 4     | ~7 digits             | graphics, ML, large arrays         |
| `double`  | 8     | ~15–17 digits         | science, general math              |
| `decimal` | 16    | 28–29 digits, base-10 | **money, anything a human audits** |

`decimal` stores base-10 digits with a scale, so 0.1 is exact. It's roughly 20× slower than `double` and has a much smaller range — the correct trade for currency.

**Never compare floats with `==`.** Compare against a tolerance appropriate to your magnitudes, and remember `NaN != NaN` is `true` — use `double.IsNaN`.

**Money:** `decimal` in-process, and for wire formats send an integer of minor units with an explicit currency (`{ "amount": 1999, "currency": "EUR" }`), because JSON numbers are doubles in most clients.

### The type system in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The type system in practice</p>

<a id="be-m45"></a>

#### Integral types, overflow, checked and unchecked

| Type                 | Bytes  | Range              | Use for                                  |
|----------------------|--------|--------------------|------------------------------------------|
| `byte` / `sbyte`     | 1      | 0..255 / -128..127 | binary data, flags                       |
| `short` / `ushort`   | 2      | ±32K / 0..65K      | rarely worth it alone; useful in structs |
| `int` / `uint`       | 4      | ±2.1B              | **the default**                          |
| `long` / `ulong`     | 8      | ±9.2 quintillion   | IDs, ticks, byte counts, money-in-cents  |
| `nint` / `nuint`     | 4 or 8 | platform           | interop, pointer arithmetic              |
| `Int128` / `UInt128` | 16     | ±1.7e38            | .NET 7+, rare                            |
| `BigInteger`         | heap   | unbounded          | cryptography, exact large math; slow     |

Arithmetic on `int` and smaller is done in 32-bit registers; `short + short` produces `int`. There is no speed benefit to using `short` for a local — only for reducing struct or array size.

**Overflow silently wraps by default.** C# compiles arithmetic as `unchecked` unless you say otherwise.

``` csharp
int max = int.MaxValue;
int wrapped = max + 1;          // -2147483648, no exception

checked { int boom = max + 1; } // OverflowException
// Or project-wide: <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
```

> **\[Trap\]**
>
> **Trap.** The classic overflow bug is `(low + high) / 2` in a binary search. Write `low + (high - low) / 2`. The second classic is multiplying two `int`s that represent sizes: `int bytes = count * recordSize;` — cast one operand to `long` first.

<a id="be-m46"></a>

#### Floating point: float and double

`float` (32-bit) and `double` (64-bit) are IEEE-754 binary types. They store *binary* fractions, so any decimal fraction whose denominator is not a power of two is approximated.

``` text
Console.WriteLine(0.1 + 0.2 == 0.3);     // False
Console.WriteLine((0.1 + 0.2).ToString("R")); // 0.30000000000000004
```

Precision: `float` gives ~7 significant decimal digits, `double` ~15–17. Special values: `NaN`, `±Infinity`, `-0.0`.

``` csharp
double nan = 0.0 / 0.0;
Console.WriteLine(nan == nan);          // False - NaN is not equal to itself
Console.WriteLine(nan.Equals(nan));     // True  - Equals uses bitwise comparison
Console.WriteLine(double.IsNaN(nan));   // True  - use this
```

Compare with a tolerance, and pick the tolerance relative to the magnitude:

> **\[Trap\]**
>
> **Trap.** A `NaN` key in a `Dictionary<double, T>` can be stored but never found by `==` semantics — though it *is* found by the dictionary, because dictionaries use `Equals`. Sorting a list containing `NaN` can throw `InvalidOperationException` ("IComparer.Compare() method returns inconsistent results") because `NaN` breaks the ordering contract.

<a id="be-m47"></a>

#### decimal: money and precision

`decimal` is a 128-bit type storing a 96-bit integer mantissa, a sign, and a base-10 scale exponent (0–28). It represents decimal fractions *exactly*, which is why it is the money type. Range is ±7.9e28 with 28–29 significant digits.

``` text
Console.WriteLine(0.1m + 0.2m == 0.3m);  // True
```

Cost: arithmetic is software-implemented, roughly **10–20x slower** than `double`. That is irrelevant for an invoice and very relevant for a million-row aggregation.

`decimal` preserves trailing zeros in its representation, which affects `ToString()` but not equality:

``` csharp
decimal a = 1.0m, b = 1.00m;
Console.WriteLine(a == b);            // True
Console.WriteLine(a.ToString());      // 1.0
Console.WriteLine(b.ToString());      // 1.00
Console.WriteLine(decimal.GetBits(b)[3]); // scale differs
```

> **\[Tip\]**
>
> **Alternative.** For high-volume financial computation, storing *minor units as `long`* (cents, or 1/10,000ths for FX) gives exact arithmetic at integer speed. Convert to `decimal` only at the display and persistence boundary. Whichever you choose, be explicit about rounding: `Math.Round(x, 2, MidpointRounding.AwayFromZero)` — the default is banker's rounding (`ToEven`), which surprises accountants.

### Money & Numeric Precision

<p class="src">From <em>.NET Backend Deep Dive</em> · Money & Numeric Precision</p>

<a id="dn-228"></a>

#### Never use binary floating point for money

Short, but it accounts for a disproportionate share of production bugs that involve an apology to a customer.

```csharp
double a = 0.1 + 0.2;          // 0.30000000000000004
Console.WriteLine(a == 0.3);   // False

decimal b = 0.1m + 0.2m;       // 0.3 exactly
```

`double` and `float` are base-2; values like 0.1 have no finite representation, so errors accumulate across thousands of operations. **`decimal` is base-10** with 28–29 significant digits — slower, but exact for the values humans actually use.

Map it through consistently: `decimal(19,4)` in SQL Server, `numeric` in Postgres, and a **string** on the wire in JSON (Part 25.4) so no JavaScript client silently converts it to a `double`.

The alternative used by many payment systems is **integer minor units** — store 1999 for $19.99. Immune to representation error and to accidental division, at the cost of remembering the scale everywhere. Either is defensible; mixing them is not.

<a id="dn-229"></a>

#### Rounding is a decision, not a default

```csharp
Math.Round(2.5)                                    // 2  - banker's rounding, the DEFAULT
Math.Round(2.5, MidpointRounding.AwayFromZero)     // 3  - what most people expect
```

.NET defaults to **banker's rounding** (round half to even), which is statistically unbiased but disagrees with the invoice your accountant expects. Neither is wrong; silently mixing them across services is. Decide once, encode it in a helper, and never call bare `Math.Round`.

Round **once, at the end**, and only when producing a human-facing or stored figure. Rounding intermediate results compounds the error.

**Division is where money disappears.** Splitting $10.00 three ways gives 3.33 × 3 = $9.99 — one cent has vanished. Allocate deliberately:

```csharp
// Largest-remainder: distribute the residual cents rather than losing them
static long[] Split(long totalCents, int parts)
{
    var each = totalCents / parts;
    var rem  = (int)(totalCents % parts);
    return Enumerable.Range(0, parts)
                     .Select(i => each + (i < rem ? 1 : 0))
                     .ToArray();   // [334, 333, 333]
}
```

The same applies to tax, discounts and multi-currency conversion: pick who absorbs the remainder, and make it explicit.

<a id="dn-230"></a>

#### Currency is part of the amount

An amount without a currency is meaningless, and adding two of them is a bug you want the compiler to catch — exactly the `Money` value object from Part 34.2. Store the currency code with every amount, never convert without recording the rate and the timestamp used, and remember that not every currency has two decimal places (JPY has none, KWD has three).

<a id="dn-231"></a>

#### Other numeric traps

```csharp
int.MaxValue + 1                    // silently wraps to int.MinValue by default
checked { int.MaxValue + 1; }       // throws OverflowException
// <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow> to make it the default

(int)2.9                            // 2 - truncation, not rounding
1 / 2                               // 0 - integer division
DateTime.MaxValue - DateTime.Now    // TimeSpan overflows more easily than you'd think

double.NaN == double.NaN            // False. Use double.IsNaN()
```

And beware identifiers that look numeric: a leading-zero account number, a phone number, or a long id parsed into a `double` by a JavaScript client loses precision past 2^53. Treat identifiers as **strings** unless you're doing arithmetic on them.

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a37"></a>

#### Money, decimals and rounding

Monetary arithmetic is the area where a plausible-looking implementation produces financially wrong results that nobody notices for months. Three decisions matter: the type, where you round, and who owns the remainder.

##### Never use a binary floating-point type

``` csharp
double a = 0.1, b = 0.2;
Console.WriteLine(a + b == 0.3);            // False
Console.WriteLine((a + b).ToString("R"));   // 0.30000000000000004

decimal c = 0.1m, d = 0.2m;
Console.WriteLine(c + d == 0.3m);           // True
```

`double` and `float` are base-2, so 0.1 has no exact representation, exactly as 1/3 has none in base 10. `decimal` is base-10 with 28–29 significant digits and a scaling factor, so every value you can write as a literal is stored exactly. It is roughly an order of magnitude slower than `double`, which is irrelevant for anything that also touches a database.

| Storage choice              | Shape                | Trade-off                                                        |
|-----------------------------|----------------------|------------------------------------------------------------------|
| `decimal` / `DECIMAL(19,4)` | 12345.6700           | Readable, exact, the usual default. Pick the scale deliberately  |
| Minor units as `long`       | 1234567 = €12,345.67 | No rounding ambiguity at all; what Stripe and most ledgers do    |
| `double`                    | —                    | Wrong. There is no context in which this is acceptable for money |

##### Rounding mode is a business rule

``` csharp
// .NET's default is BANKER'S rounding (round half to even) - which surprises people
Math.Round(2.5m)                                          // 2   <- not 3
Math.Round(3.5m)                                          // 4
Math.Round(2.5m, MidpointRounding.AwayFromZero)           // 3   <- "school" rounding

// decimal.Round is the same; ToString does something different again
(2.5m).ToString("F0")                                     // "3"  - away from zero

// so state the intent explicitly, every time
public static decimal ToCurrency(decimal value) =>
    Math.Round(value, 2, MidpointRounding.AwayFromZero);
```

Banker's rounding is statistically unbiased over many operations, which is why it is the default and why some financial standards require it. Others require half-up. Tax authorities frequently specify one and only one. The rule is that the mode is a decision to record, not a default to inherit.

###### Where to round, and the remainder problem

Rounding at every intermediate step compounds the error. Round once, at the point where the number becomes a monetary fact — a line total, an invoice total, a payment.

``` csharp
// WRONG: rounds three times, error accumulates
var lineTotal = Math.Round(qty * Math.Round(unitPrice * (1 + vatRate), 2), 2);

// RIGHT: full precision through the calculation, round at the boundary
var net   = qty * unitPrice;                  // exact
var vat   = net * vatRate;                    // exact
var gross = Money.Round(net + vat);           // one rounding, at the fact
```

The harder case is *allocation*: splitting an amount into parts that must sum back to the original. Naive division loses or invents money.

``` csharp
// splitting EUR 100.00 three ways
var naive = Math.Round(100.00m / 3, 2);       // 33.33  x3 = 99.99  -> one cent vanished

// largest-remainder allocation: distribute the difference deterministically
public static decimal[] Allocate(decimal total, int parts)
{
    var basis = Math.Floor(total / parts * 100) / 100;      // 33.33
    var result = Enumerable.Repeat(basis, parts).ToArray();

    var remainder = (int)Math.Round((total - basis * parts) * 100);   // 1 cent
    for (var i = 0; i < remainder; i++)
        result[i] += 0.01m;                                  // 33.34, 33.33, 33.33

    return result;                                           // sums to exactly 100.00
}

// weighted allocation (e.g. distributing a discount across lines by value)
public static decimal[] AllocateByRatio(decimal total, decimal[] weights)
{
    var sum = weights.Sum();
    var allocated = weights.Select(w => Math.Floor(total * w / sum * 100) / 100).ToArray();
    var remainder = (int)Math.Round((total - allocated.Sum()) * 100);

    // give the cents to the largest weights first - defensible and stable
    foreach (var i in Enumerable.Range(0, weights.Length)
                                .OrderByDescending(i => weights[i]).Take(remainder))
        allocated[i] += 0.01m;

    return allocated;
}
```

Whichever algorithm you choose, it must be deterministic and documented, because an auditor will eventually ask why customer A absorbed the extra cent and customer B did not. “Largest remainder, ties broken by line order” is an answer; “whatever `Math.Round` did” is not.

###### Multi-currency and the temporality of rates

``` csharp
// an amount is meaningless without its currency, and a conversion is meaningless
// without the instant it applied
public readonly record struct Money(decimal Amount, string Currency);

public readonly record struct ConvertedMoney(
    Money Original,
    Money Converted,
    decimal Rate,
    DateTimeOffset RateAt,
    string RateSource);      // store ALL of it - you will be asked to reproduce the figure
```

- **Never store a converted amount alone.** Store the original, the rate, the rate timestamp and its source. Recomputing later with today's rate produces a different number and an unexplainable discrepancy.
- **Not every currency has two decimals.** JPY and KRW have zero; BHD, KWD and TND have three. Hardcoding 2 is a real bug in international systems — use `RegionInfo` or an ISO 4217 table.
- **Never add two `Money` values of different currencies.** Make the operator throw, as shown in the records section — this catches the bug at the point of the mistake.
- **Rounding happens after conversion, in the target currency's precision**, not before.
- **Beware EF Core's default decimal mapping.** Without an explicit precision the provider may silently truncate. Configure it globally: `configurationBuilder.Properties<decimal>().HavePrecision(19, 4)`.

> **\[Trap\]**
>
> **Common trap.** A total stored as a column and also derivable from its lines will eventually disagree with them — after a partial refund, a rounding change, or a bad migration. Decide which is authoritative. If the stored total is authoritative (usually correct for an issued invoice, which must never change), then recalculating it on read is a bug, not a safety net.

### Time, money, and measurement

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Time, money, and measurement</p>

<a id="be-m69"></a>

#### Money arithmetic and rounding

Two decisions define correctness: *where* you round, and *which way*. Both must be explicit, because the defaults surprise people.

``` csharp
Math.Round(2.5m, 0);                                   // 2  - banker's rounding (ToEven)
Math.Round(2.5m, 0, MidpointRounding.AwayFromZero);    // 3  - what accountants expect
Math.Round(3.5m, 0);                                   // 4  - ToEven again, now it rounds up

decimal.Round(x, 2, MidpointRounding.AwayFromZero);
decimal.Truncate(x);              // toward zero, no rounding
Math.Floor(x); Math.Ceiling(x);   // toward -inf / +inf
```

`MidpointRounding.ToEven` is the default because it is statistically unbiased over many roundings. That is the right property for scientific aggregation and the wrong one for a single invoice line, where regulation and user expectation usually say half-up. Pick one per domain, write it down, and centralise it.

``` csharp
public static class Money {
    public const int Scale = 2;                        // per-currency in reality: JPY=0, BHD=3
    public static decimal Round(decimal v) =>
        Math.Round(v, Scale, MidpointRounding.AwayFromZero);
}
```

> **\[Trap\]**
>
> **Rounding at the wrong level changes the total.** Rounding each line and summing does not equal summing then rounding — on a 200-line invoice the two can differ by more than a cent, and both are defensible. Tax authorities usually specify which; if yours does not, decide once, document it, and add a test asserting the invariant. Silently having both behaviours in different code paths is the failure mode.

``` csharp
// Multiplication grows scale; division rarely terminates. Round deliberately after each.
decimal lineTotal = Money.Round(unitPrice * quantity);
decimal unitCost  = Money.Round(batchCost / units);
// Never chain several unrounded operations and round only at the end unless you
// have decided that is the rule - decimal has 28-29 digits, but the DECIMAL(18,4)
// column it lands in does not (§170).
```

> **\[Tip\]**
>
> **Never `double` for money.** It cannot represent 0.10 exactly, errors compound across additions, and the resulting discrepancies appear months later in a reconciliation nobody can explain ([Floating point: float and double](#be-m46)). Use `decimal`, or integer minor units. And never compare money with `==` after arithmetic that involved division — compare the rounded values.

Worked example: [Case: the report whose totals were wrong](14-03-worked-cases-from-production.md#be-m295)

<a id="be-m70"></a>

#### Allocating amounts without losing cents

Split 100.00 three ways. Each share is 33.333…; rounded, each is 33.33; the total is 99.99. One cent has vanished, and over a million transactions that is a real reconciliation failure.

The fix is the **largest remainder method**: floor everything, then distribute the leftover minor units one at a time to the shares with the largest fractional parts.

``` csharp
/// <summary>Splits an amount into parts that sum EXACTLY to the original.</summary>
public static decimal[] Allocate(decimal total, ReadOnlySpan<decimal> weights, int scale = 2) {
    decimal unit = 1m;
    for (int i = 0; i < scale; i++) unit /= 10m;             // 0.01 for scale 2

    decimal weightSum = 0m;
    foreach (var w in weights) weightSum += w;
    if (weightSum <= 0) throw new ArgumentException("Weights must sum above zero");

    var result = new decimal[weights.Length];
    var remainders = new (int Index, decimal Frac)[weights.Length];
    decimal allocated = 0m;

    for (int i = 0; i < weights.Length; i++) {
        decimal exact = total * weights[i] / weightSum;
        decimal floored = Math.Floor(exact / unit) * unit;    // always down first
        result[i] = floored;
        allocated += floored;
        remainders[i] = (i, exact - floored);
    }

    // Distribute the leftover, largest fractional part first. Ties by index,
    // so the result is deterministic and reproducible.
    int leftover = (int)Math.Round((total - allocated) / unit);
    Array.Sort(remainders, (a, b) => b.Frac != a.Frac
        ? b.Frac.CompareTo(a.Frac) : a.Index.CompareTo(b.Index));

    for (int k = 0; k < leftover; k++) result[remainders[k].Index] += unit;
    return result;
}
```

``` text
Allocate(100.00m, [1, 1, 1]);          // [33.34, 33.33, 33.33] - sums to 100.00
Allocate(0.05m,   [3, 7]);             // [0.02, 0.03]          - sums to 0.05
Allocate(-100.00m,[1, 1, 1]);          // works for refunds too (Floor goes toward -inf)
```

> **\[Tip\]**
>
> **The invariant to test.** For any total and any weights, `Allocate(t, w).Sum() == t`. That is a single property-based test that covers every case you would otherwise write by hand ([Contract tests for equality and comparers](04-06-testing.md#be-m225)). Add a second asserting that no share differs from its exact value by more than one minor unit.

> **\[Why it matters\]**
>
> **Where this comes up** is everywhere money is divided: splitting a discount across lines, apportioning shipping, distributing tax across a mixed-rate basket, revenue-sharing, instalment plans, and splitting a payment across invoices. Each is the same algorithm, and each is a source of long-running penny discrepancies when done with naive rounding.

<a id="be-m71"></a>

#### Percentages, tax, and order of operations

Percentage arithmetic is not associative once rounding enters, so the *sequence* is part of the specification. "10% discount plus 10% VAT" has several defensible answers and only one correct one for your jurisdiction and contract.

``` csharp
// Tax-exclusive (net -> gross): the common B2B shape.
decimal net   = Money.Round(unitPrice * qty);
decimal disc  = Money.Round(net * discountRate);
decimal taxed = Money.Round((net - disc) * vatRate);        // discount BEFORE tax
decimal gross = net - disc + taxed;

// Tax-inclusive (gross -> net): the common retail shape.
// Extracting tax from a gross figure is NOT gross * rate.
decimal net2 = Money.Round(gross / (1 + vatRate));
decimal vat2 = gross - net2;                                // subtract, never round twice
```

> **\[Trap\]**
>
> **Deriving both parts by rounding independently breaks the identity.** If you compute `net = Round(gross / 1.1)` and `vat = Round(gross * 0.1 / 1.1)`, then `net + vat` can differ from `gross` by a cent. Always compute one component and obtain the other by subtraction. The same rule applies to splitting any total into parts — derive the last part, do not round it ([Allocating amounts without losing cents](#be-m70)).

``` csharp
// Percentage change and its traps:
decimal change = (now - before) / before;        // undefined when before == 0
// Reversing is not symmetric: +50% then -50% is 0.75x, not 1.0x.
// Averaging percentages is almost always wrong - average the underlying
// quantities and compute the percentage from the totals.

// Basis points avoid the ambiguity entirely in interfaces and storage:
int rateBps = 825;                               // 8.25%
decimal rate = rateBps / 10_000m;
```

> **\[Tip\]**
>
> **Store the rate that applied, not just the result.** Tax rates change, discounts get renegotiated, and a historical invoice must be reproducible years later. Persist `VatRateApplied` and `DiscountRateApplied` on the line alongside the computed amounts — this is the same "frozen at a point in time" reasoning as storing the unit price at sale ([Normalization and deliberate denormalization](07-02-the-relational-model-and-sql.md#be-m189)).

> **\[Trap\]**
>
> **Multi-rate baskets need per-rate subtotals.** Applying an invoice-level discount to a basket containing 10% and 5% VAT lines requires allocating the discount across lines first ([Allocating amounts without losing cents](#be-m70)), then taxing each group at its own rate. Computing tax on a blended average rate is wrong and will not survive an audit.

<a id="be-m72"></a>

#### Units and dimensional safety

A `decimal Weight` does not say kilograms or pounds. A `double Distance` does not say metres or miles. The compiler cannot help, so the mismatch surfaces as a wrong number rather than an error — which is how the Mars Climate Orbiter was lost to a pound-force/newton confusion.

``` csharp
public readonly record struct Grams(decimal Value) : IComparable<Grams> {
    public static Grams FromKilograms(decimal kg) => new(kg * 1000m);
    public decimal Kilograms => Value / 1000m;

    public static Grams operator +(Grams a, Grams b) => new(a.Value + b.Value);
    public static Grams operator *(Grams a, int n)   => new(a.Value * n);
    public int CompareTo(Grams other) => Value.CompareTo(other.Value);
    public override string ToString() => $"{Value:0.###} g";
}

// The API now states its unit, and passing the wrong one does not compile.
decimal shipping = RateFor(Grams.FromKilograms(2.5m), destination);
```

Three rules keep this cheap:

- **One canonical internal unit** per dimension — grams, millimetres, minor currency units, UTC ticks. Convert at the boundary, never in the middle.
- **Convert on input and output only.** A conversion in a calculation is a rounding error waiting to accumulate.
- **Put the unit in the name** when a wrapper type is overkill: `TimeoutMs`, `WeightGrams`, `PriceMinorUnits`. It costs nothing and it removes the ambiguity at every call site.

> **\[Tip\]**
>
> For anything beyond a couple of dimensions, use UnitsNet rather than hand-rolling — it covers hundreds of units with conversions, parsing, and formatting already tested. Hand-roll only the two or three dimensions that are genuinely core to your domain, where you also want domain-specific operations on them ([Value objects and parsing at the boundary](08-05-validation-and-errors-at-the-boundary.md#be-m266)).

> **\[Trap\]**
>
> **Watch the type behind the unit.** A `readonly record struct` wrapping a `decimal` is free and exact; wrapping a `double` inherits every floating-point trap ([Floating point: float and double](#be-m46)), and making it a class adds an allocation to every arithmetic operation ([Struct vs class: the decision](02-03-types.md#be-b242)). And check the EF mapping actually translates — a value-converted property may lose ordering semantics in SQL ([Type mapping: CLR to SQL](07-07-orms-and-ef-core.md#be-m170)).

### Time, Culture & Text

<p class="src">From <em>.NET Backend Deep Dive</em> · Time, Culture & Text</p>

<a id="dn-140"></a>

#### `DateTime` is broken; use `DateTimeOffset`

A quiet source of production bugs that survive every code review.

```csharp
DateTime.Now       // local to the SERVER - meaningless in a cluster spanning regions
DateTime.UtcNow    // fine, but the Kind flag is easily lost through serialization,
                   // EF, or a database round-trip
DateTimeOffset.UtcNow  // carries the offset explicitly - unambiguous
```

**The rule:** store and transmit UTC, convert to local only at the presentation edge, and use `DateTimeOffset` for instants.

**But an offset is not a time zone.** `+07:00` doesn't tell you whether DST applies. For anything scheduled in the future — a recurring meeting, a billing cycle, a reminder — store the **IANA zone id** alongside the local time:

```csharp
// WRONG for future events: DST shifts and the meeting moves
var utc = localTime.ToUniversalTime();

// RIGHT: keep the intent
record Meeting(DateTime LocalStart, string TimeZoneId);  // "Asia/Ho_Chi_Minh"

var tz  = TimeZoneInfo.FindSystemTimeZoneById(m.TimeZoneId);
var utcInstant = TimeZoneInfo.ConvertTimeToUtc(m.LocalStart, tz);
```

.NET 6+ accepts IANA ids on Windows too, so `"Asia/Ho_Chi_Minh"` works cross-platform — prefer it over `"SE Asia Standard Time"`.

**DST edge cases that actually happen:** a local time can be **invalid** (the spring-forward gap) or **ambiguous** (the autumn-back overlap). `TimeZoneInfo.IsInvalidTime` / `IsAmbiguousTime` exist precisely for this, and ignoring them produces exceptions or silently doubled scheduled jobs.

For date-only and time-only concepts, .NET 6+ has `DateOnly` and `TimeOnly`. A birthday is not an instant.

**Testability:** inject `TimeProvider` rather than calling `DateTime.UtcNow` — otherwise time-dependent logic can't be tested without `Thread.Sleep`.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a33"></a>

#### Dates, times and TimeProvider

Time bugs are among the most expensive and least reproducible. Three rules eliminate most of them: store UTC, convert at the edges, and never call `DateTime.Now` in domain code.

| Type                    | Represents                     | Use for                                         |
|-------------------------|--------------------------------|-------------------------------------------------|
| `DateTimeOffset`        | An instant plus its UTC offset | Timestamps — the safe default                   |
| `DateTime` (UTC)        | An instant with a Kind flag    | Legacy interop; always set Kind                 |
| `DateOnly` / `TimeOnly` | A calendar date / a clock time | Birthdays, opening hours — no timezone involved |
| `TimeSpan`              | A duration                     | Timeouts and intervals                          |
| IANA zone id            | `Europe/Amsterdam`             | Future local times, recurring schedules         |

``` csharp
// inject time so it can be controlled in tests
public class TrialService(TimeProvider time)
{
    public bool IsExpired(Subscription s) =>
        time.GetUtcNow() > s.StartedAt.AddDays(30);
}

// in tests
var fake = new FakeTimeProvider(new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero));
fake.Advance(TimeSpan.FromDays(31));
```

> **\[Trap\]**
>
> **Common trap.** An offset is not a timezone. `+02:00` tells you nothing about whether daylight saving applies next month, so a recurring ‘09:00 Amsterdam’ meeting must store the zone id, not the offset. Also note that `TimeProvider` replaces the hand-rolled `IClock` interface every codebase used to have, and it also abstracts timers.

###### Choosing the right type, and testing time

| Type             | Represents                          | Use for                                         |
|------------------|-------------------------------------|-------------------------------------------------|
| `DateTimeOffset` | An instant, with a UTC offset       | **The default for a timestamp.** Unambiguous    |
| `DateTime`       | A date and time, plus a `Kind` flag | Legacy. The `Kind` is the problem — see below   |
| `DateOnly`       | A calendar date                     | Birthdays, invoice dates, anything with no time |
| `TimeOnly`       | A time of day                       | Opening hours, schedules                        |
| `TimeSpan`       | A duration                          | Elapsed time, timeouts                          |
| `TimeZoneInfo`   | A zone's rules over time            | Converting an instant to local, for display     |

``` text
  THE DateTime.Kind TRAP

    DateTimeKind.Utc          this is UTC
    DateTimeKind.Local        this is the SERVER'S local time
    DateTimeKind.Unspecified  we do not know   <- and this is the DEFAULT

  Unspecified is the dangerous one, because operations silently ASSUME:
    ToUniversalTime()   on Unspecified treats it as LOCAL
    ToLocalTime()       on Unspecified treats it as UTC
    -> the same value shifts in different directions depending on the call

  and databases return Unspecified for most column types, so a round trip
  through the database silently strips the Kind you carefully set.

  DateTimeOffset has no Kind. It carries the offset in the value.
  Use it, and the whole category of bug disappears.
```

``` csharp
// the rules that avoid nearly every date bug
// 1. store UTC, always. Convert to local only at the display boundary.
// 2. use DateTimeOffset for instants, DateOnly for dates.
// 3. do calendar arithmetic in the LOCAL zone, then convert back.

// this is wrong across a DST boundary:
var tomorrow = utcNow.AddHours(24);          // may be 23 or 25 local hours

// this is right:
var local = TimeZoneInfo.ConvertTime(now, tz);
var tomorrowLocal = local.AddDays(1);        // calendar-aware
var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowLocal.DateTime, tz);

// 4. store the ZONE ID for future events, not a fixed offset.
//    "09:00 Europe/Amsterdam every Monday" survives a DST rule change;
//    "08:00 UTC" silently becomes the wrong local time in winter.
```

``` csharp
// TimeProvider (.NET 8+) makes time injectable, which makes it testable
public class TokenService(TimeProvider clock)
{
    public bool IsExpired(Token t) => t.ExpiresAt <= clock.GetUtcNow();
}

// production
services.AddSingleton(TimeProvider.System);

// tests - no sleeping, no flakiness, and you can test the boundary exactly
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
var svc = new TokenService(clock);
clock.Advance(TimeSpan.FromMinutes(31));
Assert.True(svc.IsExpired(token));

// it also fakes timers, so you can test debounce, retry and polling logic
// without Thread.Sleep - which is the single biggest source of flaky tests.
```

### Time, money, and measurement

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Time, money, and measurement</p>

<a id="be-m65"></a>

#### Time zones and DST in practice

An *instant* and a *local time* are different kinds of data, and conflating them is the root of most date bugs. UTC identifies an instant. A wall-clock time identifies an instant only when paired with a zone, and even then not always.

``` csharp
var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");   // IANA ids work
// on Windows too since .NET 6 (ICU). Prefer IANA over "SE Asia Standard Time".

DateTimeOffset utc   = DateTimeOffset.UtcNow;
DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, zone);
DateTime back        = TimeZoneInfo.ConvertTimeToUtc(localUnspecified, zone);
```

**DST creates two kinds of broken local time**, and both throw or silently misbehave if you ignore them:

``` csharp
// Spring forward: 02:30 simply does not exist on that date.
if (zone.IsInvalidTime(local))
    local = local.AddHours(1);          // or reject the input - a deliberate choice
// ConvertTimeToUtc on an invalid time throws ArgumentException.

// Fall back: 02:30 happens TWICE, with two different offsets.
if (zone.IsAmbiguousTime(local)) {
    var offsets = zone.GetAmbiguousTimeOffsets(local);   // usually two
    var chosen  = offsets.Max();        // convention: the first occurrence (DST offset)
}
```

> **\[Trap\]**
>
> **Do not store future events as UTC instants.** "The weekly meeting at 09:00 Ho Chi Minh time" converted to UTC once and stored is wrong the moment a government changes its DST rules or offset — which happens several times a year somewhere in the world. Store the *local time plus the IANA zone id*, and resolve to an instant when you need one. Past events are the opposite: store the UTC instant, because what happened is settled.

| What you are storing        | Store as                                     |
|-----------------------------|----------------------------------------------|
| when something happened     | `DateTimeOffset` / `timestamptz`, in UTC     |
| a future appointment        | local `DateTime` + IANA zone id              |
| a birthday, an invoice date | `DateOnly` — no zone, no time, no conversion |
| opening hours               | `TimeOnly` + the venue's zone                |
| elapsed time                | `TimeSpan`, measured with `Stopwatch`        |

> **\[Tip\]**
>
> **The tz database changes.** Zone rules are data, shipped by the OS or by ICU, and updated as governments legislate. That means a stored zone id stays correct while a stored offset does not, and it means your container base image needs its tzdata kept current — a stale image gives correct-looking wrong answers after a rule change.

Worked example: [Case: page 500 takes eight seconds](14-03-worked-cases-from-production.md#be-m296)

<a id="be-m66"></a>

#### Intervals, overlaps, and scheduling

Use **half-open intervals** everywhere: `[start, end)`. Inclusive-end ranges force you to reason about the smallest representable tick, and they make adjacent intervals either overlap or leave a gap depending on precision.

``` csharp
public readonly record struct Interval(DateTimeOffset Start, DateTimeOffset End) {
    public Interval {
        if (End < Start) throw new ArgumentException("End precedes Start");
    }
    public TimeSpan Duration => End - Start;
    public bool IsEmpty => End == Start;

    // The overlap test. Memorise this shape - it is the same in C# and in SQL.
    public bool Overlaps(Interval other) => Start < other.End && other.Start < End;
    public bool Contains(DateTimeOffset t) => t >= Start && t < End;

    public Interval? Intersect(Interval o) {
        var s = Start > o.Start ? Start : o.Start;
        var e = End   < o.End   ? End   : o.End;
        return s < e ? new Interval(s, e) : null;
    }
}
```

``` sql
-- Same predicate in SQL. Note it is sargable on an index over (Start, End).
SELECT * FROM Bookings
WHERE Start < @requestEnd AND @requestStart < [End];

-- The half-open form also makes a whole day trivial and index-friendly
-- (compare §160):
WHERE CreatedAt >= @day AND CreatedAt < DATEADD(day, 1, @day);
```

``` csharp
// Merging a set of intervals - the sweep that underlies availability calendars,
// resource allocation, and log-window collapsing. O(n log n), one pass.
static List<Interval> Merge(IEnumerable<Interval> input) {
    var sorted = input.Where(i => !i.IsEmpty).OrderBy(i => i.Start).ToList();
    var result = new List<Interval>(sorted.Count);
    foreach (var iv in sorted) {
        if (result.Count > 0 && iv.Start <= result[^1].End)
            result[^1] = result[^1] with { End = iv.End > result[^1].End ? iv.End : result[^1].End };
        else result.Add(iv);
    }
    return result;
}
```

> **\[Tip\]**
>
> **Let the database enforce non-overlap.** PostgreSQL has range types and an exclusion constraint (`EXCLUDE USING gist (room WITH =, during WITH &&)`) that makes double-booking structurally impossible. SQL Server has no equivalent, so you need a serialisable transaction or an application-level lock keyed on the resource — a check-then-insert without one is a race, however careful the check ([Transactions and isolation levels](07-04-transactions-and-isolation.md#be-a100)).

<a id="be-m67"></a>

#### Recurring events

Never materialise an infinite series. Store the *rule*, plus its exceptions, and expand it lazily over a bounded window.

``` csharp
public sealed record Recurrence(
    DateTime LocalStart,          // wall-clock, unspecified kind
    string ZoneId,                // IANA - resolved at expansion time
    Frequency Freq,               // Daily, Weekly, Monthly, Yearly
    int Interval = 1,             // every N periods
    DayOfWeek[]? ByDay = null,
    DateTime? Until = null,
    int? Count = null,
    IReadOnlySet<DateOnly>? Exceptions = null);   // EXDATE: cancelled occurrences
```

This is essentially RFC 5545's `RRULE`, and matching that spec is worth it — it is what every calendar system speaks, and it has already answered the awkward questions (what does "monthly on the 31st" mean in February; what does "last weekday" mean).

``` csharp
public static IEnumerable<DateTimeOffset> Expand(
        Recurrence r, DateTimeOffset from, DateTimeOffset to) {
    var zone = TimeZoneInfo.FindSystemTimeZoneById(r.ZoneId);
    var cursor = r.LocalStart;
    int emitted = 0;

    while (cursor <= (r.Until ?? DateTime.MaxValue)) {
        if (r.Count is { } max && emitted >= max) yield break;

        var date = DateOnly.FromDateTime(cursor);
        if (r.Exceptions?.Contains(date) != true && Matches(r, cursor)) {
            // Resolve the instant per occurrence, so DST is applied correctly to each.
            var local = cursor;
            if (!zone.IsInvalidTime(local)) {
                var offset = zone.GetUtcOffset(local);
                var instant = new DateTimeOffset(local, offset);
                if (instant >= to) yield break;
                if (instant >= from) { yield return instant; emitted++; }
            }
        }
        cursor = Advance(r, cursor);
    }
}
```

> **\[Trap\]**
>
> **The offset must be recomputed per occurrence.** A weekly 09:00 meeting spans a DST transition twice a year; computing one offset at the series start and adding seven days repeatedly drifts the wall-clock time by an hour for half the year. Advance in *local* time and convert each occurrence separately — that is the whole reason the zone id is stored rather than an offset ([Time zones and DST in practice](#be-m65)).

> **\[Tip\]**
>
> **Querying "what is on next week" needs care.** You cannot index a rule. The workable pattern is a *materialised window*: expand every active recurrence into a concrete occurrences table covering, say, the next 12 months, refreshed by a background job and invalidated when a rule changes. Queries then hit an ordinary indexed table, and only the expansion job knows about recurrence at all.
