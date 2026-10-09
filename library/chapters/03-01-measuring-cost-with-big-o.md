---
id: "3-1"
number: "3.1"
title: "Measuring cost with Big-O"
part: 3
part_title: "Data structures and algorithms"
words: 4079
minutes: 18
---

# 3.1 Measuring cost with Big-O

> **Quick look.** Big-O describes how cost grows with data size, so you can predict slow code before the data grows.
>
> - Keep only the fastest-growing term: constant, log n, n, n log n, n², exponential.
> - At scale the gap between growth classes dwarfs everything else.
> - Nested loops multiply; a search call inside a loop (List.Contains, a query) hides a quadratic cost.
> - Amortized O(1) is precise: List doubling makes occasional expensive steps average out.
> - Big-O ignores caches: a contiguous array often beats a "faster" structure for small sizes. Choose the shape with Big-O, then measure.


### The big picture

Code that runs instantly with a hundred test records can take hours with a million real ones. Usually each step isn't slow; the number of steps grows much faster than the data.

Big-O notation is the standard way to describe that growth, so you can predict how code will behave before the data grows. This chapter explains Big-O in plain terms, with real numbers for each kind of growth, and shows how to spot hidden costs in ordinary code, such as a lookup inside a loop. It also shows how to measure growth yourself with a simple timing experiment.

### Before you start

**Builds on:** 1.3 Text is numbers too, 1.5 Memory.

**Ideas explained later:**

- **Capacity** (3.2): The number of slots in a dynamic array's internal array, including unused ones.
- **Binary search** (3.7): Finding a value in sorted data by repeatedly halving the range. O(log n).
- **Insertion sort** (3.7): Sorting by sliding each item left into place among the already-sorted items.
- **Merge sort** (3.7): Sorting by splitting in halves, sorting each, and merging. Stable and always O(n log n).
- **Cache hit** (8.6): A lookup that finds the value in the cache.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Lower bound** (3.7): The first position in sorted data whose value is not less than a target.
- **Hash table** (3.3): A structure that stores entries in buckets chosen by hashing their keys, giving O(1) lookups on average.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, lambdas, LINQ, null operators. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does O(n²) actually tell you?**

> How the cost grows as the input grows. Double the input and the work roughly quadruples. It says nothing about the exact time, and it ignores constant factors and small inputs.

**2. Why is f(n) = 3n² + 500n + 20 simply O(n²)?**

> For large n, the n² term dominates everything else. Big-O keeps only the dominant term and drops constant multipliers.

**3. A loop calls list.Contains(x) for each of n items. What is the total cost?**

> O(n²). `List<T>.Contains` scans the list, which is O(n), and it runs n times. A `HashSet<T>` makes each check O(1) on average, and the whole loop O(n).

**4. List<T>.Add sometimes copies the whole list. Why is it still called O(1)?**

> Because the cost is amortized. The list doubles its capacity when full, so copies become rarer as it grows. Averaged over many adds, each one costs a constant amount.

**5. Roughly how many steps does a binary search take over a million sorted items?**

> About 20. Each step halves the range, and 2²⁰ is just over a million. That is O(log n).

**6. An algorithm takes 1 second for n = 1000 and 4 seconds for n = 2000. What is its likely complexity?**

> O(n²). Doubling n multiplied the time by 4, which is 2². A doubling experiment like this is a quick way to estimate complexity from measurements.

**7. When can an O(n²) algorithm beat an O(n log n) one?**

> For small inputs. Big-O hides constant factors, memory behaviour and setup costs. Insertion sort beats merge sort on a dozen items, which is why real sort routines switch to it for small ranges.

### Why measure growth

Part 1 gave you costs in nanoseconds: a cache hit, a memory read, a disk seek. Those tell you how long one operation takes. They don't tell you how a program will behave when the data grows from a thousand records to ten million.

Code that is instant in testing can take hours in production. Usually the reason isn't that each step is slow. It is that the number of steps grows much faster than the data. **Big-O notation** is the standard way to describe that growth.

### Counting steps

Start by counting how many basic steps an algorithm takes for an input of size *n*. A basic step is something that takes a roughly fixed time: an addition, a comparison, reading an array element.

Finding the largest number in an array looks at each element once. That is *n* steps. Checking whether an array has any duplicates by comparing every pair takes about *n* × *n* / 2 steps. Looking up an element by index takes one step, however big the array is.

The exact counts are messy and depend on details. What matters is their shape as *n* grows.

### Big-O

Big-O keeps only the part of the count that dominates for large *n*, and drops constant multipliers.

An algorithm that takes 3*n*² + 500*n* + 20 steps is **O(n²)**, said "order n squared". For large *n*, the *n*² part swamps the rest, and the 3 in front doesn't change the shape of the curve. That biggest part is the **dominant term**.

Formally, O(f(n)) is an upper bound: the cost grows no faster than f(n), times some constant, once *n* is big enough. People also use Big-Θ (theta) for a tight bound and Big-Ω (omega) for a lower bound. In everyday speech, "O(n)" usually means "grows like n", and this guide uses it that way.

Big-O can describe time or memory. **Time complexity** counts steps. **Space complexity** counts extra memory the algorithm needs beyond its input.

### The common classes

A handful of shapes cover almost everything you will meet.

**O(1), constant time.** The cost doesn't depend on *n*. Reading `array[i]`. Looking up a key in a hash table, on average.

**O(log n), logarithmic time.** Each step cuts the problem down by a constant fraction, usually half. Binary search. Looking up a key in a balanced tree. The logarithm grows very slowly: log₂ of a billion is about 30.

**O(n), linear time.** Touch every item once. Summing an array. Scanning a list for a value.

**O(n log n).** Split the problem in halves, handle each half, combine in linear time. Good sorting algorithms. In practice it behaves almost like linear.

**O(n²), quadratic time.** Compare every item with every other. Nested loops over the same data. Fine for hundreds of items, painful for tens of thousands, impossible for millions.

**O(2ⁿ), exponential time.** Try every combination. Each extra item doubles the work. Only tiny inputs are feasible.

**O(n!), factorial.** Try every ordering. Worse still. Visiting 20 airports in every possible order means 2.4 × 10¹⁸ routes.

| Class | n = 10 | n = 1,000 | n = 1,000,000 |
|---|---:|---:|---:|
| O(1) | 1 | 1 | 1 |
| O(log n) | 3 | 10 | 20 |
| O(n) | 10 | 1,000 | 1,000,000 |
| O(n log n) | 33 | 10,000 | 20,000,000 |
| O(n²) | 100 | 1,000,000 | 1,000,000,000,000 |
| O(2ⁿ) | 1,024 | about 10³⁰¹ | unthinkable |

At one nanosecond per step, the O(n²) column for a million items is 1,000 seconds, about 17 minutes. The O(n log n) column is 20 milliseconds.

### Worst, average and best case

The same algorithm can cost different amounts on different inputs of the same size.

Searching a list for a value costs one step if it is first, *n* steps if it is last or missing. The **worst case** is O(n). The **average case**, over random positions, is about *n*/2 steps, which is still O(n).

Worst cases matter when inputs can be chosen by someone hostile, or when a slow response is unacceptable. Quicksort is O(n log n) on average and O(n²) in its worst case, which is why real implementations guard against it. Hash tables are O(1) on average and O(n) in the worst case, which is why .NET randomizes string hashing, as Hash tables (3.3) explains.

### Amortized cost

Some operations are usually cheap and occasionally expensive. `List<T>.Add` normally writes one element. When the internal array is full, it allocates a new array twice the size and copies everything across.

Because the capacity doubles, the expensive copies get rarer as the list grows. Adding *n* items causes copies of 4, 8, 16 and so on up to *n* elements, which add up to less than 2*n*. Spread over *n* adds, that is a constant amount per add. This is **amortized cost**: O(1) amortized, even though a single add can be O(n).

Amortized is a guarantee about the total, not about each call. A latency-sensitive loop can still notice the occasional big copy. If you know the final size, pass it as the list's capacity and avoid the copies entirely.

### Finding the cost in real code

Big-O is most useful for spotting problems in code you read. A few rules cover most cases.

A loop over *n* items costs *n* times the cost of its body. Nested loops multiply. Two loops one after the other add, and the bigger one wins. A call inside a loop counts as part of the body, so you need to know what each call costs.

That last rule is where problems hide. `list.Contains`, `list.IndexOf`, `list.Remove` and LINQ's `Any`, `First`, `Count` with a condition, and `Where` all scan. Inside another loop, they turn O(n) code into O(n²). String concatenation in a loop is O(n²), as Text is numbers too (1.3) showed. A database query inside a loop is O(n) round trips, each costing milliseconds; this is the "N + 1 query" problem covered in ORMs and EF Core (7.7).

### Measuring instead of guessing

You can estimate complexity by experiment. Time the code at some size *n*, then at 2*n*, 4*n* and so on. Look at how much the time grows per doubling. Doubling with O(n) doubles the time. With O(n²), it quadruples. With O(n log n), a little more than doubles. With O(log n), it barely changes. This is a **doubling experiment**.

```csharp
// growth.cs — run with: dotnet run -c Release growth.cs
using System.Diagnostics;
using System.Linq;

Console.WriteLine("n        nested loops   hash set");
double? lastSlow = null, lastFast = null;
foreach (int n in new[] { 4_000, 8_000, 16_000, 32_000 })
{
    int[] data = Enumerable.Range(0, n).ToArray();   // no duplicates: the worst case for both
    double slow = Time(() => HasDuplicateSlow(data));
    double fast = Time(() => HasDuplicateFast(data));
    string ratio = lastSlow is null ? "" : $"   (x{slow / lastSlow.Value:F1} and x{fast / lastFast!.Value:F1})";
    Console.WriteLine($"{n,-8} {slow,8:F1} ms   {fast,8:F2} ms{ratio}");
    lastSlow = slow; lastFast = fast;
}
// Each doubling multiplies the nested-loop time by about 4 and the hash-set time by about 2.

var list = new List<int>();
int lastCapacity = -1;
for (int i = 0; i < 100; i++)
{
    list.Add(i);
    if (list.Capacity != lastCapacity) { Console.Write($"{list.Capacity} "); lastCapacity = list.Capacity; }
}
Console.WriteLine();   // 4 8 16 32 64 128: the capacity doubles, so copies get rarer

static bool HasDuplicateSlow(int[] a)              // O(n²): compares every pair
{
    for (int i = 0; i < a.Length; i++)
        for (int j = i + 1; j < a.Length; j++)
            if (a[i] == a[j]) return true;
    return false;
}

static bool HasDuplicateFast(int[] a)              // O(n): one pass with a hash set
{
    var seen = new HashSet<int>();
    foreach (int x in a)
        if (!seen.Add(x)) return true;             // Add returns false if x was already there
    return false;
}

static double Time(Action work)
{
    work();                                        // warm up so JIT tiers don't distort the numbers
    var sw = Stopwatch.StartNew();
    work();
    return sw.Elapsed.TotalMilliseconds;
}
```

### What Big-O hides

Big-O is a model, and it leaves things out on purpose.

**Constant factors.** An O(n) algorithm that does 100 operations per item is slower than an O(n log n) one doing 2 per item until *n* reaches about 2⁵⁰.

**Memory behaviour.** Every array read counts as one step, but Memory (1.5) showed that a cache hit and a cache miss differ by a factor of 100. A linked list and an array are both O(n) to scan, and the array can be many times faster.

**Small inputs.** For a few dozen items, the simplest algorithm usually wins. Real sort routines switch to insertion sort for small ranges for exactly this reason.

**The real input.** Worst cases may never happen in your data, or may happen all the time.

So use Big-O to choose the right shape and rule out disasters. Then measure to choose between options of the same shape.

### In short

Big-O describes how cost grows with the size of the input, keeping only the fastest-growing part. The common classes are constant, logarithmic, linear, n log n, quadratic and exponential, and at scale the difference between them dwarfs everything else. Nested loops multiply their costs, and a searching call inside a loop hides a quadratic cost. Amortized costs average out occasional expensive steps. Use Big-O to choose the right shape, then measure to choose between options.

### What goes wrong

**Hidden linear calls in loops.** `Contains`, `IndexOf`, `Remove`, `Any` and `First` on a `List` inside a loop make the whole thing quadratic. Build a `HashSet` or `Dictionary` once before the loop.

**Testing only with small data.** Code that handles 100 test records in milliseconds takes an hour on 100,000. Test with realistic volumes, or at least run a doubling experiment.

**Queries in loops.** Loading related data one row at a time turns one database call into thousands. Load in bulk or use a join.

**Worst cases chosen by attackers.** Input shaped to hit a worst case, such as keys designed to collide in a hash table or a regular expression that backtracks exponentially, can take a service down with little traffic.

**Over-applying Big-O.** Replacing a clear loop over 20 items with a clever structure makes code harder to read and no faster. Optimize where the data is big and the code is hot.

### Cost model

As a rough budget at about a nanosecond per simple step, one second of work allows about a billion O(n) steps, about 30 million items for O(n log n), about 30,000 items for O(n²) and about 30 items for O(2ⁿ). Real steps are often slower, especially with cache misses or allocations, so divide by 10 for safety. A web request that must answer in 100 milliseconds can therefore afford O(n²) only on inputs of a few thousand items at most.

### In other languages (optional)

**JavaScript.** `array.includes`, `indexOf` and `find` scan the array, just like `List.Contains`. Use a `Set` or `Map` for repeated lookups. `array.shift()` removes the first element and is O(n), because every other element moves down.

**Python.** `x in some_list` is O(n); `x in some_set` is O(1) on average. `list.insert(0, x)` and `list.pop(0)` are O(n); `collections.deque` does both ends in O(1).

**SQL.** The database picks the algorithm for you, based on indexes and statistics. A lookup through an index is O(log n); a scan of a table without one is O(n); a join without useful indexes can be O(n × m). The query plan shows which it chose, as covered in Indexes and query plans (7.3).

### Key terms

- **Big-O notation**: A way to describe how an algorithm's cost grows with input size, keeping only the dominant term.
- **Dominant term**: The part of a cost formula that grows fastest and swamps the rest for large inputs.
- **Time complexity**: How the number of steps an algorithm takes grows with the size of its input.
- **Space complexity**: How the extra memory an algorithm needs grows with the size of its input.
- **Constant time**: O(1). The cost doesn't depend on the input size.
- **Logarithmic time**: O(log n). Each step cuts the problem down by a fraction, as in binary search.
- **Linear time**: O(n). The cost grows in proportion to the input.
- **Quadratic time**: O(n²). The cost grows with the square of the input, typically from nested loops.
- **Exponential time**: O(2ⁿ). Each extra item doubles the work.
- **Worst case**: The most expensive input of a given size.
- **Average case**: The expected cost over typical or random inputs of a given size.
- **Amortized cost**: The average cost per operation over a long sequence, even if single operations are sometimes expensive.
- **Doubling experiment**: Timing code at sizes n, 2n, 4n and so on to estimate its complexity from how the time grows.

### Where this leads

The costs of arrays, lists and their operations are in Arrays and lists (3.2). How hash tables reach O(1) is in Hash tables (3.3). Where O(log n) comes from is in Trees (3.4) and Searching and sorting (3.7). Taming exponential problems is in Recursion and dynamic programming (3.8). The database version of all this is in Indexes and query plans (7.3), and measuring real systems is in Scaling and performance (12.4).

### Check yourself

1. Give the complexity of each: summing a 2D array of n × n; finding the maximum of a list, then removing it; sorting a list, then taking its first element.
2. Extend `growth.cs` with a version of `HasDuplicate` that sorts a copy of the array and then compares neighbours. Predict its growth, then measure it.
3. Rewrite this to be O(n): `foreach (var p in passengers) if (vips.Any(v => v.Id == p.Id)) Upgrade(p);`
4. In `growth.cs`, create the list with `new List<int>(100)` and check that the capacity never changes.
5. Find a loop in real code that calls a method you haven't read. Look up what that method costs, and work out the loop's total cost.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The duplicate check that took 40 minutes

An import compares 200,000 new bookings against 2 million existing references with `List.Contains` inside a loop.

**What goes wrong.** That is 200,000 × 2,000,000 comparisons: O(n × m).

*Before*

```csharp
var dupes = incoming.Where(b => existingRefs.Contains(b.Ref)).ToList();   // List<string>: linear scan each time
```

*After*

```csharp
var existing = existingRefs.ToHashSet(StringComparer.Ordinal);          // one pass to build
var dupes = incoming.Where(b => existing.Contains(b.Ref)).ToList();       // O(1) per lookup
```

> **Lesson:** A search inside a loop hides a quadratic cost. Convert the inner collection to a HashSet or dictionary first.

#### Example 2: Testing growth before it reaches production

A seat-optimizer feels fine with test data. You want to know how it behaves at 10× the size.

*A quick growth experiment*

```csharp
foreach (var n in new[] { 1_000, 2_000, 4_000, 8_000 })
{
    var data = Generate(n);
    var sw = Stopwatch.StartNew();
    Optimize(data);
    Console.WriteLine($"{n,6}: {sw.ElapsedMilliseconds} ms");
}
// time roughly doubling per row: linear. Quadrupling: quadratic — fix it before launch.
```

> **Lesson:** Double the input a few times and watch the ratio. It tells you the growth class without any theory.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b233"></a>

#### Amortized, and why it isn't hand-waving

"Amortized O(1)" appears throughout and it is a precise claim, not a softening.

`List<T>.Add` is usually one array write — O(1). Occasionally the array is full, so it allocates a new one twice the size and copies everything — O(n). Calling that "O(1) amortized" means: **over any sequence of n appends, the total cost is O(n), so the average per operation is constant.**

The arithmetic, for capacity doubling from 1 to n:

``` text
copies = 1 + 2 + 4 + 8 + … + n/2  <  n
```

The doubling series sums to less than n, so n appends cost fewer than 2n operations total. Constant on average, even though individual calls spike.

**The doubling is load-bearing.** Grow by a fixed 10 instead and you resize n/10 times, each copying an average of n/2 elements — O(n²) overall. This is exactly why `string +=` in a loop is quadratic and `StringBuilder` isn't.

What amortized does *not* promise: that any individual call is fast. If one `Add` in ten thousand copies a 50 MB array, your p99 latency sees it. Amortized is a claim about totals, not about tails — which is why pre-sizing matters for latency even when the average is already fine.

<a id="be-b240"></a>

#### Why Big-O lies

The CPU never fetches one byte. It fetches a **64-byte cache line**. Reading a contiguous array gets ~16 ints for the price of one; chasing pointers through a `LinkedList<T>` pays a potential cache miss per node.

Real measured shape on modern hardware:

| Operation                         | Complexity | Reality                       |
|-----------------------------------|------------|-------------------------------|
| `List<T>` linear scan, n=1000     | O(n)       | often **faster**              |
| `LinkedList<T>` traversal, n=1000 | O(n)       | ~10× slower — pointer chasing |
| `Dictionary<K,V>` lookup          | O(1)       | ~20–50 ns, hash + indirection |
| `List<T>.Contains`, n\<20         | O(n)       | usually beats a Dictionary    |

**A `Dictionary` beats a linear scan somewhere around 10–50 elements**, depending on key type and comparison cost. Below that, the hash computation and the extra indirection cost more than just looking.

`LinkedList<T>` deserves its own note: its O(1) insert is real, but you need a node reference to use it, and getting one requires an O(n) traversal with a cache miss per step. **In practice `List<T>` beats it for almost everything**, including mid-list insertion at moderate sizes, because `Array.Copy` on contiguous memory is extraordinarily fast. Reach for `LinkedList<T>` only when you're holding node references already — an LRU cache is the canonical genuine use (VI.7).

------------------------------------------------------------------------

### Collections

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Collections</p>

<a id="be-m111"></a>

#### Complexity cheat sheet

| Collection              | Access by index | Search   | Insert                | Delete   | Extra memory    |
|-------------------------|-----------------|----------|-----------------------|----------|-----------------|
| `T[]`                   | O(1)            | O(n)     | n/a                   | n/a      | none            |
| `List<T>`               | O(1)            | O(n)     | O(1)\* end / O(n) mid | O(n)     | up to 2x        |
| `Dictionary<K,V>`       | n/a             | O(1)†    | O(1)\*                | O(1)     | ~2.5x           |
| `HashSet<T>`            | n/a             | O(1)†    | O(1)\*                | O(1)     | ~2x             |
| `SortedDictionary<K,V>` | n/a             | O(log n) | O(log n)              | O(log n) | node overhead   |
| `SortedList<K,V>`       | O(1)            | O(log n) | O(n)                  | O(n)     | up to 2x        |
| `Queue<T>` / `Stack<T>` | n/a             | O(n)     | O(1)\*                | O(1)     | up to 2x        |
| `LinkedList<T>`         | O(n)            | O(n)     | O(1)‡                 | O(1)‡    | 2 pointers/node |
| `ImmutableList<T>`      | O(log n)        | O(n)     | O(log n)              | O(log n) | tree nodes      |
| `ImmutableArray<T>`     | O(1)            | O(n)     | O(n)                  | O(n)     | none            |

\* amortised    † average; O(n) with a bad hash    ‡ given a node reference

> **\[Why it matters\]**
>
> **Read this table with a grain of salt.** Constant factors and cache behaviour routinely beat asymptotics at the sizes real code deals with. A linear scan of a 100-element array is faster than a dictionary lookup, because it is one cache line after another with no hashing. Below roughly 10–20 elements, a `List<T>` plus `IndexOf` usually beats a `Dictionary`. Measure at your actual n.

Foundations: [The memory hierarchy: registers to RAM](01-04-the-processor.md#be-m22)
