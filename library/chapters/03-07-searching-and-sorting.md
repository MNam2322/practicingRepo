---
id: "3-7"
number: "3.7"
title: "Searching and sorting"
part: 3
part_title: "Data structures and algorithms"
words: 5341
minutes: 23
---

# 3.7 Searching and sorting

> **Quick look.** Searching and sorting: what .NET does for you and the details that still matter.
>
> - Binary search finds items in sorted data in O(log n) and also finds range boundaries.
> - Comparison sorts can't beat O(n log n); counting and radix sorts can for small integer keys.
> - Array.Sort (introsort) is not stable; LINQ OrderBy is stable.
> - Comparers must be consistent or sorting throws or returns garbage; never compare by subtraction.
> - Don't sort to find one item; sort data bigger than memory with external merge sort.
> - Search engines avoid scanning by using inverted indexes.


### The big picture

Finding things and putting them in order are two of the oldest problems in computing, and they are linked: sorted data can be searched far faster.

You will rarely write a sort yourself, because .NET's are excellent. But you use them constantly, and their details matter. Do equal items keep their order? What must a comparer promise? And why does searching data under the wrong assumptions silently give wrong answers? This chapter explains binary search and its many uses, the main sorting algorithms and their trade-offs, and what .NET actually does when you call `Array.Sort`.

### Before you start

**Builds on:** 3.3 Hash tables, 3.5 Heaps and priority queues, 1.3 Text is numbers too.

**Ideas explained later:**

- **Git** (11.1): The distributed version control system used almost everywhere, created in 2005.
- **Bisect** (11.1): Finding the commit that introduced a problem by binary search through history.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Version control** (11.1): Recording the history of files so past states can be recovered and changes combined and explained.
- **Access pattern** (7.6): A specific question an application asks of its data, used to drive data design.
- **Divide and conquer** (3.8): Splitting a problem into independent parts, solving each, and combining the results.
- **Dynamic programming** (3.8): Solving a problem by combining stored answers to overlapping smaller subproblems.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **SQL Server** (7.2): Microsoft's relational database product.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.

**The code uses:** variables and var, methods and local functions, string interpolation, collection expressions, lambdas, LINQ, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What two things must be true of data for binary search to work?**

> It must be sorted by the key you search on, and you must be able to jump to any position in O(1), as with an array or list. Binary search on a linked list gains nothing.

**2. Array.BinarySearch returns a negative number. What does it mean?**

> The value isn't there. The bitwise complement of the result, `~result`, is the index where it would have to be inserted to keep the array sorted.

**3. Why is mid = (lo + hi) / 2 a bug?**

> For very large arrays, `lo + hi` can overflow `int` and go negative. `lo + (hi - lo) / 2` gives the same answer without overflowing. This bug sat in Java's standard library for nine years.

**4. Is Array.Sort stable? Is LINQ's OrderBy?**

> `Array.Sort` and `List.Sort` are not stable: equal items can end up in any order. `OrderBy` is stable: equal items keep their original order.

**5. What algorithm does .NET's Array.Sort use?**

> Introsort. It runs quicksort, switches to heap sort if the recursion gets too deep, and uses insertion sort for small ranges. That gives O(n log n) in the worst case with quicksort's speed in the usual case.

**6. When is insertion sort a good choice?**

> For small arrays, a few dozen items, and for data that is already nearly sorted. It is O(n²) in general but close to O(n) when few items are out of place.

**7. How can you sort a million delays measured in whole minutes from 0 to 599 faster than O(n log n)?**

> Counting sort. Count how many times each minute value occurs in one pass, then write the values out in order. That is O(n + k), where k is the number of possible values.

**8. What does git bisect have to do with binary search?**

> It is binary search over commit history. You mark one good and one bad commit, and it keeps testing the commit halfway between until it finds the first bad one.

### Why search and sort

Almost every program finds things and puts things in order. Show departures by time. Find a booking by reference. Match today's flights against yesterday's. Merge two lists. Report the ten worst delays.

Searching and sorting are closely linked. Sorted data can be searched in O(log n) instead of O(n), and many problems become simple once the data is sorted. This chapter covers how both work, what the library does for you, and where the traps are.

### Linear search

**Linear search** checks each item in turn until it finds a match or runs out. It is O(n). It works on any data, sorted or not, and on anything enumerable. For small collections, or a single search, it is often the right answer. `Contains`, `IndexOf`, `Find` and LINQ's `First` and `Any` are all linear searches.

The problem is repeated searching. A thousand linear searches over a million items is a billion steps. Either sort the data and use binary search, or build a hash table from Hash tables (3.3).

### Binary search

**Binary search** finds a value in sorted data by halving the range at each step. Look at the middle item. If it is the value, you're done. If the value is smaller, it can only be in the left half; otherwise it can only be in the right half. Repeat on that half.

Each step halves the range, so a million items need about 20 steps, and a billion about 30. It needs two things: the data must be sorted by the key you search on, and you must be able to jump straight to the middle, as with an array or list.

Binary search is famously easy to get slightly wrong. The boundaries are off-by-one traps: is the upper bound included or not? The midpoint can overflow: `(lo + hi) / 2` breaks when `lo + hi` exceeds `int.MaxValue`. That exact bug sat in Java's standard library from 1997 until 2006. Write it as `lo + (hi - lo) / 2`, or better, use the library.

`Array.BinarySearch` and `List<T>.BinarySearch` return the index if the value is found. If it isn't, they return a negative number. Its bitwise complement, `~result`, is the index where the value would go to keep the order. That makes it easy to insert into a sorted list.

### Lower bound and ranges

Often the question isn't "is this value here?" but "where does the range start?". Which is the first flight departing at or after 09:00? This is the **lower bound**: the first position whose value is not less than the target. Binary search finds it in O(log n), and it works even if the exact value isn't present. The upper end of a range is found the same way. Everything between the two positions is in the range.

This is exactly how a database uses an index to answer `WHERE DepartureTime >= '09:00' AND DepartureTime < '11:00'`, as Indexes and query plans (7.3) explains.

### Binary search on the answer

Binary search works on more than arrays. It works on any yes-or-no question whose answer flips once as a number grows. Such a question is a **monotonic predicate**: false, false, false, then true from some point on, and never back to false.

"Can the day's schedule fit on *g* gates?" is false for too few gates and true from some number up. Binary search over *g* finds the smallest number that works, calling the check only about log₂ of the range times.

`git bisect` is the same idea over time. A bug appeared somewhere in 1,000 commits. Test the middle commit, keep the half that contains the change from good to bad, and after about 10 tests you have the commit that broke it, as Version control with Git (11.1) shows.

### How fast can sorting be?

Most sorting algorithms work only by comparing pairs of items: is a before b? These are **comparison sorts**. There is a proven limit on them. Sorting *n* items by comparisons alone needs at least about *n* log₂ *n* comparisons in the worst case. So O(n log n) is the best a comparison sort can do. The algorithms below either reach that limit or trade it for other benefits.

### Insertion sort

**Insertion sort** works the way many people sort playing cards. Take each item in turn and slide it left past larger items until it sits in the right place among those already sorted.

It is O(n²) in general, because each item may slide past all earlier ones. But it has real strengths. It is tiny, with very low overhead. It is stable. It sorts in place. And on nearly sorted data, each item slides only a little, so it runs in close to O(n). Real sort routines use it for small ranges.

### Merge sort

**Merge sort** divides and conquers. Split the array in half. Sort each half, by the same method. Merge the two sorted halves by repeatedly taking the smaller front item, as in the merging example in Heaps and priority queues (3.5).

It is O(n log n) in every case, never worse. It is stable. Its weakness is memory: the usual version needs O(n) extra space for merging. It also suits data that doesn't fit in memory. Sort chunks that do fit, write them to disk, then merge the sorted chunks. That is an **external sort**, and databases use it for large `ORDER BY` operations.

### Quicksort

**Quicksort** also divides and conquers, the other way round. Pick one item as the **pivot**. Rearrange the array so everything smaller than the pivot comes before it and everything larger comes after. This step is a **partition**. The pivot is now in its final place. Sort the two sides the same way.

With good pivots, each partition splits the range roughly in half, giving O(n log n). It sorts in place, and its access pattern is cache-friendly, so in practice it is usually the fastest general comparison sort.

With bad pivots, it collapses. Always picking the first item as the pivot on already sorted data splits off one item at a time, and the cost becomes O(n²). Implementations defend against this by choosing the median of the first, middle and last items, or a random pivot. It is also not stable.

### Introsort: what .NET actually uses

.NET's `Array.Sort`, `List<T>.Sort` and `Span<T>.Sort` use **introsort**, short for introspective sort. It starts with quicksort, using median-of-three pivots. It watches its own recursion depth. If the depth passes about 2 log₂ *n*, a sign that pivots are going badly, it switches to heap sort for that range, which is O(n log n) whatever the input. And for ranges of 16 items or fewer, it uses insertion sort.

The result is quicksort's speed in the usual case with a guaranteed O(n log n) worst case. The price is that it isn't stable.

### Stability

A **stable sort** keeps items with equal keys in their original order. That matters when you sort in stages or by several keys. Sort flights by departure time, then stable-sort by gate, and within each gate the flights stay in time order. With an unstable sort, the time order within each gate is lost.

LINQ's `OrderBy` and `OrderByDescending` are stable. `Array.Sort` and `List<T>.Sort` are not. For several keys, the clearest approach is to sort once with all of them: `OrderBy(f => f.Gate).ThenBy(f => f.Departure)`.

```csharp
// sorting.cs — run with: dotnet run -c Release sorting.cs
using System.Buffers;
using System.Diagnostics;
using System.Linq;

// 1. Binary search, and what a miss returns
int[] times = [555, 605, 610, 700, 740, 915, 1005, 1200];     // departures, minutes after midnight
Console.WriteLine(Array.BinarySearch(times, 740));            // 4: found
int miss = Array.BinarySearch(times, 720);
Console.WriteLine($"{miss} → insert at {~miss}");             // -5 → insert at 4
Console.WriteLine(LowerBound(times, 600));                    // 1: first departure at or after 600 is 605

// 2. Binary search on the answer: fewest gates that fit the schedule
(int Start, int End)[] turns = [(0, 50), (10, 60), (20, 70), (55, 100), (65, 120), (75, 90)];
int lo = 1, hi = turns.Length;
while (lo < hi)
{
    int mid = lo + (hi - lo) / 2;                             // no overflow
    if (Fits(turns, mid)) hi = mid; else lo = mid + 1;
}
Console.WriteLine($"fewest gates: {lo}");                     // fewest gates: 3

// 3. Our own sorts agree with the library
var rng = new Random(1);
int[] data = Enumerable.Range(0, 20_000).Select(_ => rng.Next(1_000_000)).ToArray();
int[] expected = data.Order().ToArray();
Console.WriteLine(InsertionSorted(data).SequenceEqual(expected));   // True
Console.WriteLine(MergeSorted(data).SequenceEqual(expected));       // True

// 4. Insertion sort loves nearly sorted data
int[] nearly = (int[])expected.Clone();
for (int i = 0; i < 10; i++) { int a = rng.Next(nearly.Length), b = rng.Next(nearly.Length); (nearly[a], nearly[b]) = (nearly[b], nearly[a]); }
Console.WriteLine($"insertion sort, random:        {Time(() => InsertionSorted(data))} ms");
Console.WriteLine($"insertion sort, nearly sorted: {Time(() => InsertionSorted(nearly))} ms");   // far faster
Console.WriteLine($"Array.Sort, random:            {Time(() => { var c = (int[])data.Clone(); Array.Sort(c); return c; })} ms");

// 5. Stability
var flights = Enumerable.Range(0, 100).Select(i => (Delay: i % 3, Seq: i)).ToArray();
bool TiesInOrder(IEnumerable<(int Delay, int Seq)> s) =>
    s.Zip(s.Skip(1)).All(p => p.First.Delay != p.Second.Delay || p.First.Seq < p.Second.Seq);
Console.WriteLine($"OrderBy kept ties in order:    {TiesInOrder(flights.OrderBy(f => f.Delay))}");   // True
var copy = (ValueTuple<int, int>[])flights.Clone();
Array.Sort(copy, (x, y) => x.Item1.CompareTo(y.Item1));
Console.WriteLine($"Array.Sort kept ties in order: {TiesInOrder(copy.Select(t => (t.Item1, t.Item2)))}");   // usually False

// 6. Counting sort: O(n + k) for small integer keys
int[] delays = Enumerable.Range(0, 1_000_000).Select(_ => rng.Next(600)).ToArray();
Console.WriteLine(CountingSorted(delays, 600).SequenceEqual(delays.Order()));   // True

// 7. Searching text for any of several characters, vectorized
var separators = SearchValues.Create(",;|");
Console.WriteLine("LHR,JFK;SIN".AsSpan().IndexOfAny(separators));   // 3

static int LowerBound(int[] a, int target)
{
    int lo = 0, hi = a.Length;                                // search [lo, hi)
    while (lo < hi)
    {
        int mid = lo + (hi - lo) / 2;
        if (a[mid] < target) lo = mid + 1; else hi = mid;
    }
    return lo;
}

static bool Fits((int Start, int End)[] turns, int gates)    // true from some number of gates upward
{
    var busyUntil = new PriorityQueue<int, int>();
    foreach (var (start, end) in turns.OrderBy(t => t.Start))
    {
        if (busyUntil.Count > 0 && busyUntil.Peek() <= start) busyUntil.Dequeue();   // reuse a free gate
        else if (busyUntil.Count == gates) return false;       // every gate is still busy
        busyUntil.Enqueue(end, end);
    }
    return true;
}

static int[] InsertionSorted(int[] input)
{
    int[] a = (int[])input.Clone();
    for (int i = 1; i < a.Length; i++)
    {
        int item = a[i], j = i - 1;
        while (j >= 0 && a[j] > item) { a[j + 1] = a[j]; j--; }   // slide larger items right
        a[j + 1] = item;
    }
    return a;
}

static int[] MergeSorted(int[] input)
{
    if (input.Length <= 1) return input;
    int[] left = MergeSorted(input[..(input.Length / 2)]);
    int[] right = MergeSorted(input[(input.Length / 2)..]);
    int[] result = new int[input.Length];
    int l = 0, r = 0, k = 0;
    while (l < left.Length && r < right.Length)
        result[k++] = left[l] <= right[r] ? left[l++] : right[r++];   // <= keeps it stable
    while (l < left.Length) result[k++] = left[l++];
    while (r < right.Length) result[k++] = right[r++];
    return result;
}

static int[] CountingSorted(int[] input, int range)
{
    int[] counts = new int[range];
    foreach (int x in input) counts[x]++;
    int[] result = new int[input.Length];
    int k = 0;
    for (int value = 0; value < range; value++)
        for (int c = 0; c < counts[value]; c++) result[k++] = value;
    return result;
}

static long Time(Func<int[]> work)
{
    var sw = Stopwatch.StartNew();
    work();
    return sw.ElapsedMilliseconds;
}
```

### Comparers

Every comparison sort relies on a way to compare two items. In .NET that is `IComparable<T>` on the type itself, or a separate `IComparer<T>`, or a `Comparison<T>` delegate. A **comparer** returns a negative number if the first item comes first, zero if they are equal, and a positive number otherwise.

A comparer must be consistent, or sorting misbehaves. If a comes before b, then b must come after a. If a comes before b and b before c, then a must come before c. Comparing an item with itself must give zero. And the answer must not change during the sort, so never compare by something random or by a value another thread is changing.

Break these rules and results become nonsense. `Array.Sort` may throw `InvalidOperationException` saying the comparer "returns inconsistent results", or quietly produce a jumbled array. Two easy ways to break them by accident are comparing doubles that include NaN with `<`, and writing `a - b` as the comparison, which overflows for large values. Use `a.CompareTo(b)` instead.

For text, the comparer also decides between ordinal and culture order, as Text is numbers too (1.3) explained.

### Sorting without comparing

The O(n log n) limit only applies to comparison sorts. When keys are small integers, you can do better.

**Counting sort** counts how many times each possible key occurs, then writes out that many copies of each key in order. For *n* items and *k* possible keys, it costs O(n + k). Delays in whole minutes, ages, and scores out of 100 all suit it.

**Radix sort** extends this to bigger keys by sorting on one digit or byte at a time, from the least significant up, using a stable counting sort for each pass. For 32-bit integers, four passes over bytes sort any amount of data in O(n) time. It is used for very large numeric data sets and inside some databases.

### Searching text

Finding a substring in text looks simple, but good implementations are clever. Algorithms like Boyer-Moore skip ahead by several characters when they find a mismatch, instead of checking every position. .NET's `IndexOf` uses SIMD to check many characters at once. `SearchValues<T>`, added in .NET 8, precomputes a fast search for any of a set of characters or strings.

Searching a large collection of documents for words is a different problem. Scanning every document for every search is far too slow. Search engines build an **inverted index** instead: for every word, the list of documents that contain it, much like the index at the back of a book. Elasticsearch, OpenSearch and the full-text search features of SQL Server and PostgreSQL all work this way.

### In short

Binary search finds items in sorted data in O(log n), and also finds where ranges start and the smallest answer that works. Sorts that compare pairs can't beat O(n log n), while counting and radix sorts can for small integer keys. `Array.Sort` uses introsort and doesn't keep equal items in order; LINQ's `OrderBy` does. Comparers must be consistent, or sorting fails or returns garbage. Search engines avoid scanning by using inverted indexes.

### What goes wrong

**Binary search on unsorted data.** It returns wrong answers without any error. Sort first, by the same key and comparer you search with.

**Midpoint overflow.** `(lo + hi) / 2` fails on very large arrays. Use `lo + (hi - lo) / 2`, or the library.

**Relying on stability that isn't there.** Sorting with `List.Sort` in several passes scrambles the earlier order. Use `OrderBy` with `ThenBy`, or a single comparer that covers every key.

**Subtraction comparers.** `(a, b) => a - b` overflows for large values and sorts wrongly. Use `a.CompareTo(b)`.

**Inconsistent comparers.** Comparing by a random value, by a changing value, or through NaN makes sorts throw or return garbage. Keep comparers pure and total.

**Sorting to find one item.** Sorting a million items to take the minimum costs O(n log n) where O(n) would do. Use `Min`, `MaxBy`, or a heap for the top few.

**Sorting in a loop.** Re-sorting the whole list after each insert is O(n log n) per insert. Insert at the position binary search gives, or use a sorted collection.

### Cost model

| Algorithm | Typical | Worst | Extra memory | Stable |
|---|---|---|---|---|
| Linear search | O(n) | O(n) | O(1) | |
| Binary search | O(log n) | O(log n) | O(1) | |
| Insertion sort | O(n²), near O(n) if nearly sorted | O(n²) | O(1) | yes |
| Merge sort | O(n log n) | O(n log n) | O(n) | yes |
| Quicksort | O(n log n) | O(n²) | O(log n) | no |
| Heap sort | O(n log n) | O(n log n) | O(1) | no |
| Introsort (Array.Sort) | O(n log n) | O(n log n) | O(log n) | no |
| Counting sort | O(n + k) | O(n + k) | O(n + k) | yes |

On current hardware, `Array.Sort` handles about a million integers in well under 100 milliseconds. `OrderBy` is slower, because it computes keys and sorts an index array to stay stable, but usually fast enough.

### In other languages (optional)

**JavaScript and TypeScript.** `Array.prototype.sort` has been required to be stable since ES2019. By default it converts items to strings and compares those, so `[10, 9, 1].sort()` gives `[1, 10, 9]`. Always pass a comparer for numbers: `(a, b) => a - b`, which is safe for ordinary numbers because doubles don't overflow. There is no built-in binary search.

**Python.** `sorted` and `list.sort` use Timsort, a merge sort that exploits runs already in order. They are stable. Sorting by several keys uses a tuple key: `sorted(flights, key=lambda f: (f.gate, f.time))`. The `bisect` module provides binary search.

**Java.** Sorts objects with a stable Timsort and primitives with an unstable dual-pivot quicksort.

**SQL.** `ORDER BY` without a unique key gives no guaranteed order among ties, even between runs of the same query. Add a tie-breaker column, such as the primary key, whenever order matters, especially for paging.

### Key terms

- **Linear search**: Checking items one by one until a match is found. O(n).
- **Binary search**: Finding a value in sorted data by repeatedly halving the range. O(log n).
- **Lower bound**: The first position in sorted data whose value is not less than a target.
- **Monotonic predicate**: A yes-or-no question whose answer switches once as a number grows and never switches back.
- **Comparison sort**: A sort that works only by comparing pairs of items. At best O(n log n).
- **Insertion sort**: Sorting by sliding each item left into place among the already-sorted items.
- **Merge sort**: Sorting by splitting in halves, sorting each, and merging. Stable and always O(n log n).
- **External sort**: Sorting data too large for memory by sorting chunks on disk and merging them.
- **Quicksort**: Sorting by partitioning around a pivot and sorting each side. Fast in practice, O(n²) at worst.
- **Pivot**: The item quicksort partitions around.
- **Partition**: Rearranging items so those smaller than the pivot come before it and larger ones after.
- **Introsort**: Quicksort that switches to heap sort when recursion gets deep and to insertion sort for small ranges. Used by Array.Sort.
- **Stable sort**: A sort that keeps items with equal keys in their original order.
- **Comparer**: An object or function that decides the order of two items.
- **Counting sort**: Sorting small integer keys by counting each value's occurrences. O(n + k).
- **Radix sort**: Sorting by one digit or byte at a time using stable counting sorts.
- **Inverted index**: A map from each word to the documents containing it, used by search engines.

### Where this leads

Divide and conquer as a general technique is in Recursion and dynamic programming (3.8). Sorted indexes and range scans are in Indexes and query plans (7.3). Paging through sorted results reliably is in API design (8.2). `git bisect` is in Version control with Git (11.1). Choosing search engines and document stores is in Non-relational stores (7.6).

### Check yourself

1. Write `UpperBound`, the first position whose value is greater than the target, and use it with `LowerBound` to count how many departures fall between 600 and 1000.
2. Write quicksort with the first item as the pivot. Time it on random data, then on already sorted data, and explain the difference.
3. Sort a list of flights by gate and then by departure time, first with two `List.Sort` calls, then with `OrderBy` and `ThenBy`. Show where the first approach goes wrong.
4. Write a radix sort for non-negative `int` values that makes four passes over bytes. Compare it with `Array.Sort` on ten million items.
5. Use binary search on the answer to find the smallest cruising speed, in whole knots, that covers 5,000 nautical miles in under 11 hours.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A sort comparer that crashed with large values

Sorting bookings by a long `Score` with `(a, b) => (int)(a.Score - b.Score)` produces a garbage order and occasionally throws.

*Never compare by subtraction*

```csharp
bookings.Sort((a, b) => a.Score.CompareTo(b.Score));                 // correct for any range
var ordered = bookings.OrderBy(b => b.Score).ThenBy(b => b.Id).ToList(); // stable, with a tiebreaker
```

> **Lesson:** Subtraction overflows and breaks the comparer contract. Use CompareTo, and add a tiebreaker for a deterministic order.

#### Example 2: Finding the price bracket for a weight

Baggage fees are defined by weight thresholds. Find the right bracket for 23.5 kg.

*Binary search on thresholds*

```csharp
decimal[] limits = [10, 20, 23, 32];      // upper bounds in kg, sorted
decimal[] fees   = [0, 15, 30, 60, 120];  // fees[i] applies up to limits[i]; last is over 32

decimal FeeFor(decimal kg)
{
    int i = Array.BinarySearch(limits, kg);
    if (i < 0) i = ~i;                   // first limit >= kg
    return fees[i];
}
FeeFor(23.5m);   // 60
```

> **Lesson:** Binary search finds boundaries, not just exact matches: ~index is where a missing value would go.

### Structures you build yourself

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Structures you build yourself</p>

<a id="be-m115"></a>

#### Binary search and its variants

A sorted `List<T>` plus binary search is often the right answer where people reach for a tree or a dictionary: it is O(log n) to search, O(1) to index, contiguous in memory, and it has no per-node overhead.

``` csharp
int i = list.BinarySearch(target);
// >= 0  : an index of a matching element (NOT necessarily the first)
//  < 0  : the bitwise complement of the insertion point
int insertAt = i >= 0 ? i : ~i;
list.Insert(insertAt, item);         // keeps the list sorted
```

The BCL gives you "find any match". What you usually want is **lower bound** (first index ≥ target) or **upper bound** (first index \> target), which give you first-match, last-match, and range queries.

``` csharp
// First index where key >= target. Returns list.Count if none.
static int LowerBound<T>(ReadOnlySpan<T> sorted, T target) where T : IComparable<T> {
    int lo = 0, hi = sorted.Length;
    while (lo < hi) {
        int mid = lo + ((hi - lo) >> 1);              // not (lo+hi)/2 - overflow
        if (sorted[mid].CompareTo(target) < 0) lo = mid + 1;
        else hi = mid;
    }
    return lo;
}

// First index where key > target.
static int UpperBound<T>(ReadOnlySpan<T> sorted, T target) where T : IComparable<T> {
    int lo = 0, hi = sorted.Length;
    while (lo < hi) {
        int mid = lo + ((hi - lo) >> 1);
        if (sorted[mid].CompareTo(target) <= 0) lo = mid + 1;
        else hi = mid;
    }
    return lo;
}

// Range query: everything in [from, to) in one slice, no allocation.
var slice = sorted[LowerBound(sorted, from)..LowerBound(sorted, to)];
```

> **\[Trap\]**
>
> **Three ways to get this wrong.** `(lo + hi) / 2` overflows past ~1 billion elements ([Integral types, overflow, checked and unchecked](01-02-numbers-inside-the-machine.md#be-m45)). `BinarySearch` on an unsorted list returns garbage silently — no exception. And the comparer used to search must be the *same* comparer used to sort; passing a different one produces wrong answers with no diagnostic.

> **\[Tip\]**
>
> **The interpolation shortcut.** For a large sorted array of roughly uniform numeric keys, guessing the position by linear interpolation instead of halving converges in O(log log n). It is rarely worth the complexity, but it is the reason a timestamp index over evenly-spaced events can be searched in two or three probes rather than twenty.

Worked example: [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)

### Algorithms for data that does not fit

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Algorithms for data that does not fit</p>

<a id="be-m125"></a>

#### External merge sort

Sort more data than fits in memory in two phases: split into memory-sized runs, sort and spill each one, then merge all runs with a heap holding one element per run.

``` csharp
// Phase 1 - sort and spill fixed-size runs.
static async Task<List<string>> SpillRunsAsync(
        IAsyncEnumerable<Record> source, int runSize, CancellationToken ct) {
    var runs = new List<string>();
    var buffer = new List<Record>(runSize);

    await foreach (var r in source.WithCancellation(ct)) {
        buffer.Add(r);
        if (buffer.Count < runSize) continue;
        runs.Add(await WriteRunAsync(buffer, ct));
        buffer.Clear();
    }
    if (buffer.Count > 0) runs.Add(await WriteRunAsync(buffer, ct));
    return runs;

    static async Task<string> WriteRunAsync(List<Record> b, CancellationToken ct) {
        b.Sort(RecordComparer.Instance);                  // in-memory, introsort
        var path = Path.GetTempFileName();
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        foreach (var r in b) await r.WriteToAsync(fs, ct);
        return path;
    }
}
```

``` csharp
// Phase 2 - k-way merge. Memory is one buffered reader per run, nothing more.
static async IAsyncEnumerable<Record> MergeAsync(
        List<string> runs, [EnumeratorCancellation] CancellationToken ct = default) {
    var readers = runs.Select(RunReader.Open).ToList();
    var heap = new PriorityQueue<int, Record>(readers.Count, RecordComparer.Instance);

    for (int i = 0; i < readers.Count; i++)
        if (await readers[i].TryReadAsync(ct) is { } first) heap.Enqueue(i, first);

    while (heap.TryDequeue(out int idx, out var record)) {
        yield return record;
        if (await readers[idx].TryReadAsync(ct) is { } next) heap.Enqueue(idx, next);
    }
    foreach (var r in readers) r.Dispose();
    foreach (var p in runs) File.Delete(p);
}
```

> **\[Tip\]**
>
> **Two knobs.** Run size sets peak memory and the number of runs; a larger run means fewer merge inputs. Merge fan-in is bounded by open file handles and by how much buffer each reader needs — past a few hundred runs, do a multi-pass merge instead. The classic tuning is: choose the largest run that fits comfortably in memory, then merge everything in one pass.

> **\[Why it matters\]**
>
> **Before you write this.** `ORDER BY` in the database does exactly this, tuned by people who do nothing else, with the data already local. Reach for an external sort when the data is not in a database — log files, an export from a third party, a stream you are joining offline — or when the sort key is something the database cannot express ([Set-based thinking, temp tables, and TVPs](07-02-the-relational-model-and-sql.md#be-m191)).

Worked example: [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)
