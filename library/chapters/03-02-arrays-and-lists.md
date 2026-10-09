---
id: "3-2"
number: "3.2"
title: "Arrays and lists"
part: 3
part_title: "Data structures and algorithms"
words: 6587
minutes: 29
---

# 3.2 Arrays and lists

> **Quick look.** How arrays and lists sit in memory, and which collection to choose.
>
> - Arrays store items side by side: indexing is instant and scanning is cache-friendly.
> - List<T> is an array plus a count that doubles when full; adding at the end is cheap, inserting at the front is not.
> - LinkedList only wins when you already hold node references; otherwise it is slower than List.
> - Stacks give the newest item, queues the oldest (implemented as a ring buffer).
> - Frozen collections are fastest for data built once at start-up; immutable collections trade speed for safe sharing.
> - List and Dictionary cover about 90% of real code; pre-size when you know the count.


### The big picture

Almost every program keeps lists of things, and how those lists are laid out in memory decides what is fast and what is slow.

This chapter starts with the array, the simplest and most important structure: items side by side in memory. It then explains `List<T>`, which grows an array behind the scenes, linked lists, which textbooks often recommend and which are usually slower in practice, and stacks and queues, which restrict how items go in and out. You will finish knowing which collection to choose, what each operation costs, and which collection types to use in method signatures.

### Before you start

**Builds on:** 1.5 Memory, 3.1 Measuring cost with Big-O, 2.4 Values, variables and references.

**Ideas explained later:**

- **Balanced tree** (3.4): A tree whose height stays close to log n, whatever order values arrive in.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **LRU** (8.6): Least recently used: evicting whichever entry has gone longest without being read.
- **Call stack** (3.8): The stack of frames for all method calls currently in progress.
- **Depth-first search** (3.6): Exploring a graph as deep as possible before backing up, using a stack.
- **Breadth-first search** (3.6): Exploring a graph ring by ring using a queue. Finds paths with the fewest edges.
- **Access pattern** (7.6): A specific question an application asks of its data, used to drive data design.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, collection expressions, generic types, lambdas, LINQ. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why is reading array[i] O(1) whatever the size of the array?**

> The elements sit side by side in memory, all the same size. The address of element i is the start address plus i times the element size. One multiplication and one addition find it.

**2. What does List<T>.Insert(0, x) cost, and why?**

> O(n). Every existing element must move one place to the right to make room. Adding at the end is amortized O(1).

**3. When is a linked list the right choice in .NET?**

> Rarely. It is only better when you hold references to nodes and insert or remove next to them often. For almost everything else, `List<T>` is faster because it is cache-friendly, even for operations that are O(n) on paper.

**4. What is the difference between int[,] and int[][]?**

> `int[,]` is a rectangular 2D array: one block of memory, every row the same length. `int[][]` is a jagged array: an array of separate arrays, each of which can have its own length.

**5. Stack<T> and Queue<T>: which gives back the oldest item first?**

> `Queue<T>`. It is first in, first out. `Stack<T>` is last in, first out, giving back the newest item.

**6. How does Queue<T> remove from the front in O(1) without shifting everything?**

> It is a ring buffer. It keeps an index to the front and an index to the back inside an array, and both wrap around to the start when they reach the end.

**7. Why can ImmutableList<T> be slower to index than List<T>?**

> It is built as a balanced tree, so that "changing" it can share most of the old structure. Indexing is O(log n) instead of O(1). `ImmutableArray<T>` indexes in O(1) but copies everything on each change.

### Arrays

An **array** is a fixed number of elements of the same type, stored side by side in **contiguous memory**: one unbroken block. An `int[1000]` is 4,000 bytes in a row, plus a small header.

That layout gives arrays their strengths. Reading `array[i]` is O(1), because the address is the start plus *i* times the element size. The **index** is just an offset. Walking an array from start to end reads memory in order, which the cache and prefetcher love, as Memory (1.5) explained. Arrays of structs keep the structs themselves inside the array, with no pointers to follow.

The weakness is that the size is fixed when the array is created. To grow it, you allocate a bigger array and copy everything over.

Every access in C# is bounds-checked. Reading `array[1000]` on a 1,000-element array throws `IndexOutOfRangeException` instead of reading whatever memory comes next.

### Arrays with more dimensions

C# has two kinds of multi-dimensional array.

A **rectangular array**, `int[,]`, is one block of memory with fixed rows and columns. `grid[r, c]` finds its element by arithmetic. Every row has the same length.

A **jagged array**, `int[][]`, is an array of separate arrays. Each row is its own object and can have its own length. Access needs two lookups, one for the row and one for the element. Jagged arrays are often faster in practice, because the JIT optimizes one-dimensional arrays better and each row can be handled as a simple array or span.

### Dynamic arrays

Most code doesn't know its final size in advance. A **dynamic array** solves this. It keeps an internal array that is bigger than needed. The number of slots in that internal array is its **capacity**; the number in use is its count. Adding writes into the next free slot. When the slots run out, it allocates a bigger array and copies across.

`List<T>` is .NET's dynamic array. Its **growth factor** is 2: each time it fills up, the capacity doubles. Measuring cost with Big-O (3.1) showed why this makes `Add` amortized O(1).

Everything else follows from the layout. Reading and writing by index are O(1). Adding or removing at the end is O(1), amortized for adds. Inserting or removing anywhere else is O(n), because every element after that point has to shift. `Contains`, `IndexOf` and `Remove(item)` scan, so they are O(n) too.

If you know roughly how many items you will add, pass the capacity to the constructor: `new List<Flight>(expectedCount)`. That skips all the intermediate copies. `TrimExcess` shrinks the capacity afterwards if the list will stay as it is.

### Linked lists

A **linked list** stores each element in its own small object, called a **node**. Each node holds the value and a reference to the next node. In a doubly linked list, it also holds a reference to the previous one. .NET's `LinkedList<T>` is doubly linked.

If you already hold a reference to a node, inserting or removing next to it is O(1): just change a few references. Nothing shifts.

Everything else is worse. Finding the *i*-th element means walking *i* nodes from the start, so it is O(n). Each node is a separate heap object with 16 bytes of overhead plus two references, so a `LinkedList<int>` uses around ten times the memory of a `List<int>`. Walking the list follows references to nodes scattered across the heap, missing the cache on almost every step.

Textbooks often recommend linked lists for frequent insertions. On modern hardware, a `List<T>` that shifts elements with a fast memory copy usually wins anyway for anything but very large lists. Linked structures earn their place when nodes are referenced from elsewhere, as in an LRU cache that moves items to the front of a list as they are used.

### Stacks and queues

Two restricted lists come up constantly.

A **stack** gives back items in **LIFO** order: last in, first out. You push onto the top and pop from the top. Undo histories, the call stack from Memory (1.5), expression evaluation and depth-first search all work this way. `Stack<T>` is built on an array, and push and pop are O(1).

A **queue** gives back items in **FIFO** order: first in, first out. You enqueue at the back and dequeue from the front. Print jobs, message processing, breadth-first search and anything handled "in order of arrival" work this way. `Queue<T>` does both in O(1).

A queue can't shift every element when it removes from the front, or dequeue would be O(n). Instead it is a **ring buffer**: a fixed array plus an index to the front and a count. Both ends move forward and wrap around to the start of the array when they reach the end. When the array fills up, the queue grows it like a list.

A **deque**, pronounced "deck", is a double-ended queue: fast adding and removing at both ends. .NET has no general-purpose deque type, but a ring buffer is easy to extend into one.

```csharp
// lists.cs — run with: dotnet run -c Release lists.cs
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

// Inserting at the front shifts everything; adding at the end doesn't
const int N = 50_000;
var sw = Stopwatch.StartNew();
var front = new List<int>();
for (int i = 0; i < N; i++) front.Insert(0, i);
Console.WriteLine($"insert at front: {sw.ElapsedMilliseconds} ms");   // O(n) each, O(n²) in total
sw.Restart();
var back = new List<int>(N);                                          // capacity known: no regrowth
for (int i = 0; i < N; i++) back.Add(i);
Console.WriteLine($"add at end:      {sw.ElapsedMilliseconds} ms");   // O(1) each

// Walking a linked list chases pointers; walking a list streams through memory
var linked = new LinkedList<int>(Enumerable.Range(0, 5_000_000));
var list = Enumerable.Range(0, 5_000_000).ToList();
sw.Restart(); long a = 0; foreach (int x in linked) a += x;
Console.WriteLine($"sum LinkedList:  {sw.ElapsedMilliseconds} ms");
sw.Restart(); long b = 0; foreach (int x in list) b += x;
Console.WriteLine($"sum List:        {sw.ElapsedMilliseconds} ms   (same total: {a == b})");

// Remove many items in one pass instead of calling Remove in a loop
var delays = new List<int> { 5, 70, 12, 95, 3 };
delays.RemoveAll(d => d < 10);
Console.WriteLine(string.Join(",", delays));                          // 70,12,95

// Stack: last in, first out
var undo = new Stack<string>();
undo.Push("set gate A10");
undo.Push("set gate B12");
Console.WriteLine(undo.Pop());                                        // set gate B12

// Queue: first in, first out
var boarding = new Queue<string>(["rows 30-40", "rows 20-29", "rows 1-19"]);
Console.WriteLine(boarding.Dequeue());                                // rows 30-40

// A fixed-size ring buffer that keeps only the latest readings
var track = new RingBuffer<int>(3);
foreach (int altitude in new[] { 1000, 2000, 3000, 4000, 5000 }) track.Add(altitude);
Console.WriteLine(string.Join(",", track.Items()));                   // 3000,4000,5000

// A span over the list's own array: no copying
Span<int> view = CollectionsMarshal.AsSpan(back);
view[0] = 999;
Console.WriteLine(back[0]);                                           // 999

// Jagged: rows of different lengths
int[][] seatsPerRow = [new int[4], new int[6], new int[6]];
Console.WriteLine(string.Join(",", seatsPerRow.Select(r => r.Length)));  // 4,6,6

sealed class RingBuffer<T>(int capacity)
{
    readonly T[] items = new T[capacity];
    int start, count;

    public void Add(T item)
    {
        items[(start + count) % items.Length] = item;
        if (count < items.Length) count++;
        else start = (start + 1) % items.Length;      // full: the oldest item was just overwritten
    }

    public IEnumerable<T> Items()
    {
        for (int i = 0; i < count; i++) yield return items[(start + i) % items.Length];
    }
}
```

`CollectionsMarshal.AsSpan` gives direct access to a list's internal array. It is fast and unsafe in one way: if the list grows while you hold the span, the span still points at the old array.

### Immutable collections

An **immutable collection** can't be changed after it is created. Methods like `Add` return a new collection and leave the original alone. That makes immutable collections safe to share between threads and to hand out without defensive copies, as Values, variables and references (2.4) discussed.

.NET's `System.Collections.Immutable` has two main list types, with opposite trade-offs. `ImmutableArray<T>` is a plain array underneath. Indexing and iterating are as fast as an array, but every `Add` copies the whole thing, so it is O(n). It suits data built once and read often. `ImmutableList<T>` is a balanced tree underneath. A new version shares most of its nodes with the old one, so `Add` and `SetItem` are O(log n), and so is indexing. It suits data that changes a little at a time.

To build either one efficiently, use a builder: `ImmutableArray.CreateBuilder<T>()`, add everything, then call `ToImmutable()` once.

### Choosing a collection type

When a method takes or returns a collection, the type you choose is a promise.

`IEnumerable<T>` promises only that it can be walked through, once, perhaps lazily. `IReadOnlyCollection<T>` adds a count. `IReadOnlyList<T>` adds indexing. `ICollection<T>` and `IList<T>` add changing. Return the most specific read-only type that is honest. Accept the most general type that works. A method that only loops over its input should take `IEnumerable<T>`; a method that needs the count or indexes should say so, rather than calling `Count()` and `ElementAt` on an `IEnumerable<T>`, which may scan it.

For concrete types, start with arrays and `List<T>`. Use `Stack<T>`, `Queue<T>` and `PriorityQueue<TElement, TPriority>` when the access pattern matches. Use `HashSet<T>` and `Dictionary<TKey, TValue>` for lookups, covered in Hash tables (3.3). Use sorted structures when order matters, covered in Trees (3.4).

### In short

Arrays store items side by side, so reading by position is instant and scanning is cache-friendly. `List<T>` is a growable array: adding at the end is cheap, inserting at the front is not. Linked lists only win when you hold references to their nodes and insert next to them; otherwise they are slower. Stacks give back the newest item first and queues the oldest, using a ring buffer. Immutable collections trade some speed for safety when data is shared.

### What goes wrong

**`Remove` or `Insert` in a loop.** Removing items one by one from a big list shifts the rest each time, which is quadratic. Use `RemoveAll`, or build a new list of the items to keep.

**Using a list as a queue.** `list.RemoveAt(0)` shifts everything. Use `Queue<T>`.

**Choosing `LinkedList<T>` for speed.** It is almost always slower and uses far more memory. Measure before choosing it.

**Accepting `IEnumerable<T>` and enumerating it several times.** If the caller passed a lazy query or a database result, each pass runs it again. Materialize it once, or accept `IReadOnlyList<T>`.

**Holding a span over a list that grows.** Writes through an old span silently go to the discarded array. Don't add to a list while a span from `CollectionsMarshal.AsSpan` is in use.

**`ImmutableArray<T>.Add` in a loop.** Each call copies the whole array, which is quadratic. Use a builder.

### Cost model

| Operation | Array | List&lt;T&gt; | LinkedList&lt;T&gt; |
|---|---|---|---|
| Read or write by index | O(1) | O(1) | O(n) |
| Add at end | not possible | O(1) amortized | O(1) |
| Insert or remove at front | not possible | O(n) | O(1) |
| Insert or remove next to a known node | not possible | O(n) | O(1) |
| Search for a value | O(n) | O(n) | O(n) |
| Memory per int | 4 bytes | 4 to 8 bytes | about 40 bytes |

The O(n) shifts in `List<T>` use fast bulk memory copies, moving gigabytes per second. Scanning a list is limited by memory bandwidth and is many times faster than scanning a linked list of the same size.

### In other languages (optional)

**JavaScript and TypeScript.** Arrays are dynamic arrays. `push` and `pop` at the end are fast; `shift` and `unshift` at the front are O(n). Typed arrays such as `Float64Array` are fixed-size and contiguous, like C# arrays of numbers.

**Python.** `list` is a dynamic array of references. `collections.deque` is a double-ended queue with O(1) at both ends. The `array` module and NumPy store raw values contiguously.

**Java.** `ArrayList` is the dynamic array. `ArrayDeque` is a ring-buffer deque and is the recommended stack and queue.

**SQL.** Tables are not ordered lists. Without `ORDER BY`, rows can come back in any order, even if they usually come back in insertion order.

### Key terms

- **Array**: A fixed number of elements of one type, stored side by side in memory.
- **Contiguous memory**: One unbroken block of memory, with elements next to each other.
- **Index**: The position of an element in an array or list, counting from zero.
- **Jagged array**: An array of separate arrays, where each row can have its own length.
- **Dynamic array**: A growable list built on an array with spare room. List&lt;T&gt; is one.
- **Capacity**: The number of slots in a dynamic array's internal array, including unused ones.
- **Growth factor**: How much a dynamic array multiplies its capacity by when it fills up. 2 for List&lt;T&gt;.
- **Linked list**: A list of separate nodes, each holding a value and a reference to the next.
- **Node**: One element of a linked structure, holding a value and references to its neighbours.
- **LIFO**: Last in, first out. The order in which a stack gives items back.
- **FIFO**: First in, first out. The order in which a queue gives items back.
- **Ring buffer**: A fixed array used in a circle, with front and back indexes that wrap around.
- **Deque**: A double-ended queue, with fast adding and removing at both ends.
- **Immutable collection**: A collection that can't be changed after creation. "Changing" it returns a new one.

### Where this leads

Fast lookups by key are in Hash tables (3.3). Sorted data and O(log n) operations are in Trees (3.4). Priority queues are in Heaps and priority queues (3.5). Queues drive breadth-first search in Graphs (3.6). Searching and sorting arrays is in Searching and sorting (3.7). Sharing collections between threads is in Threads and locks (5.2).

### Check yourself

1. Add `RemoveOldest()` and `Peek()` to `RingBuffer<T>` in `lists.cs`, and a `Count` property.
2. Remove every even number from a `List<int>` of a million items, first with `Remove` in a loop, then with `RemoveAll`. Time both.
3. Write a method that takes `IEnumerable<int>`, prints the count and then the sum. Call it with a `Where` query whose lambda prints each item, and count how many times each item is printed.
4. Fill an `ImmutableArray<int>` with 100,000 items using `Add` in a loop, then using a builder. Time both.
5. Explain why a `List<Flight>` where `Flight` is a class is less cache-friendly than a `List<FlightStruct>` of small structs.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Using a List as a queue

A notification worker calls `pending.RemoveAt(0)` on a list of 500,000 items. Throughput collapses as the backlog grows.

*Before*

```csharp
var next = pending[0]; pending.RemoveAt(0);   // shifts every remaining element: O(n)
```

*After*

```csharp
var pending = new Queue<Notification>(capacity: 500_000);
while (pending.TryDequeue(out var next)) Send(next);   // O(1), ring buffer inside
```

> **Lesson:** Removing from the front of a List is linear. Use Queue for first-in-first-out work.

#### Example 2: A lookup table built once at start-up

Every request checks the country of an IBAN prefix against a table of 80 codes that never changes while running.

*FrozenDictionary*

```csharp
using System.Collections.Frozen;

public static class Countries
{
    public static readonly FrozenDictionary<string, string> ByCode =
        LoadCodes().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);   // slow to build, fastest to read
}
if (Countries.ByCode.TryGetValue(iban[..2], out var name)) { /* ... */ }
```

> **Lesson:** For data built once and read millions of times, frozen collections give the fastest reads and are thread-safe.

### Concurrency Primitives & Threading

<p class="src">From <em>.NET Backend Deep Dive</em> · Concurrency Primitives & Threading</p>

<a id="dn-46"></a>

#### Frozen and immutable collections (.NET 8+)

```csharp
// Read constantly, never written (lookup tables, config, permission maps)
private static readonly FrozenDictionary<string, int> Map =
    source.ToFrozenDictionary();   // slower to build, ~2x faster to read than Dictionary

private static readonly FrozenSet<string> Allowed = list.ToFrozenSet();
```

Ideal for data loaded at startup and only read afterwards — fast and inherently thread-safe.

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a30"></a>

#### Choosing the right collection

| Need                    | Type                                | Lookup        | Notes                                           |
|-------------------------|-------------------------------------|---------------|-------------------------------------------------|
| Indexed, growable list  | `List<T>`                           | O(1) by index | Resizes by doubling; set capacity if known      |
| Key lookup              | `Dictionary<K,V>`                   | O(1) average  | Pass a comparer for string keys                 |
| Uniqueness              | `HashSet<T>`                        | O(1) average  | Turns an O(n²) contains-loop into O(n)          |
| Sorted order maintained | `SortedDictionary` / `SortedSet`    | O(log n)      | Tree-based; slower than a hash                  |
| FIFO / LIFO             | `Queue<T>` / `Stack<T>`             | O(1)          | Do not simulate with a List                     |
| Priority                | `PriorityQueue<T,P>`                | O(log n)      | Built in since .NET 6                           |
| Shared across threads   | `ConcurrentDictionary`              | O(1)          | `GetOrAdd`'s factory can run more than once     |
| Read-only exposure      | `IReadOnlyList`, `FrozenDictionary` | O(1)          | Frozen: build once, read forever, fastest reads |

``` csharp
// a lookup table built at startup and never changed
private static readonly FrozenDictionary<string, Currency> Currencies =
    LoadAll().ToFrozenDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

// the O(n^2) that hides in innocent-looking code
foreach (var a in listA)
    if (listB.Contains(a))       // O(n) each time -> O(n*m) overall
        ...
var setB = listB.ToHashSet();    // one pass, then O(1) lookups
```

> **\[Why it matters\]** Collection choice is the cheapest performance decision you will ever make: it is a one-line change at development time and a rewrite once the data grows.

###### How it actually works

Why `Dictionary` lookup is O(1) and what makes it degrade.

``` text
  Add("orders", v)
    h = comparer.GetHashCode("orders")        -> 0x8F2A31C4
    bucket = h % buckets.Length               -> index 7
    entries[7] chains any collisions in a linked list

  lookup: hash -> bucket -> walk the (usually 0-1 element) chain -> compare with Equals

  degrades to O(n) when every key hashes to the same bucket.
  growth: when count exceeds capacity, allocate a bigger array and REHASH EVERYTHING.
```

``` csharp
// a dictionary that will be filled with 100k entries
var d = new Dictionary<string, Row>(capacity: 100_000, StringComparer.Ordinal);
// without the capacity: ~17 resizes, each allocating and rehashing everything already added.
// without the comparer: the default string comparer uses randomized hashing (good for
// hash-flooding resistance) but is slower than Ordinal for keys you control.
```

The same argument applies to `List<T>`: it doubles from 4, so building a 100,000-element list without a capacity performs about 15 array allocations totalling roughly twice the final size in copying. One constructor argument removes all of it.

###### Complexity in practice, with the caveats

| Operation        | List            | Dictionary     | HashSet        | SortedDictionary | Frozen        |
|------------------|-----------------|----------------|----------------|------------------|---------------|
| Lookup by key    | O(n)            | O(1)           | O(1)           | O(log n)         | O(1), fastest |
| Lookup by index  | O(1)            | —              | —              | —                | —             |
| Add              | O(1) amortised  | O(1) amortised | O(1) amortised | O(log n)         | Immutable     |
| Remove           | O(n)            | O(1)           | O(1)           | O(log n)         | Immutable     |
| Iterate in order | Insertion order | Undefined      | Undefined      | Sorted           | Undefined     |
| Memory overhead  | Lowest          | ~2–3× entries  | ~2–3×          | Highest          | Compact       |

The caveat that matters: for very small collections — under about ten elements — a `List<T>` with a linear scan often beats a `Dictionary`, because it is contiguous, cache-friendly and skips hashing entirely. Big-O describes growth, not constant factors, and most collections in real code are tiny.

``` csharp
// enumerating a List<T> via the concrete type uses a struct enumerator: no allocation
foreach (var x in myList) { }

// via the interface, it is boxed: one allocation per loop
IEnumerable<T> e = myList;
foreach (var x in e) { }

// which is why hot-path signatures often take the concrete type,
// or use CollectionsMarshal.AsSpan(list) for the fastest possible iteration
foreach (var x in CollectionsMarshal.AsSpan(myList)) { }
```

### The Collections

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Collections</p>

<a id="be-b275"></a>

#### The map

| Need                                   | Use                         | Lookup      | Insert     | Memory  | Ordered     |
|----------------------------------------|-----------------------------|-------------|------------|---------|-------------|
| Indexed sequence                       | `List<T>`                   | O(1) idx    | O(1)\* end | low     | insertion   |
| Key → value                            | `Dictionary<K,V>`           | O(1)        | O(1)\*     | medium  | no          |
| Uniqueness                             | `HashSet<T>`                | O(1)        | O(1)\*     | medium  | no          |
| Sorted by key                          | `SortedDictionary<K,V>`     | O(log n)    | O(log n)   | high    | yes         |
| Sorted, read-heavy                     | `SortedList<K,V>`           | O(log n)    | O(n)       | **low** | yes         |
| Sorted unique values                   | `SortedSet<T>`              | O(log n)    | O(log n)   | high    | yes         |
| FIFO                                   | `Queue<T>`                  | —           | O(1)\*     | low     | yes         |
| LIFO                                   | `Stack<T>`                  | —           | O(1)\*     | low     | yes         |
| Priority order                         | `PriorityQueue<T,P>`        | —           | O(log n)   | low     | by priority |
| Many inserts/removes at held positions | `LinkedList<T>`             | O(n)        | O(1)       | high    | yes         |
| Read-only, built once                  | `FrozenDictionary<K,V>`     | **fastest** | —          | medium  | no          |
| Shared, immutable                      | `ImmutableArray<T>`         | O(1)        | O(n)       | low     | insertion   |
| Thread-safe map                        | `ConcurrentDictionary<K,V>` | O(1)        | O(1)\*     | high    | no          |

\* amortized

<a id="be-b276"></a>

#### List<T>

A `T[]` plus a count. That's the whole thing.

**Growth doubles**: 0 → 4 → 8 → 16 → … Each growth allocates a new array and copies. Adding 1,000 items without pre-sizing performs 9 allocations and copies ~2,000 elements in total.

``` csharp
var list = new List<T>(expectedCount);      // one allocation, no copying
```

Past 85,000 bytes the backing arrays land on the LOH (0.6), so pre-sizing large lists avoids a series of large-object allocations.

Details worth knowing:

- `Insert(0, x)` and `RemoveAt(0)` are **O(n)** — they shift everything. Use `Queue<T>` for FIFO.
- `Remove(item)` is O(n): a scan plus a shift.
- `Contains` is a linear scan. Past ~30 elements with repeated lookups, use a `HashSet<T>`.
- **`foreach` over a concrete `List<T>` uses a struct enumerator and allocates nothing.** `foreach` over the same list typed as `IEnumerable<T>` boxes that enumerator. This is why hot loops should use concrete types.
- `Capacity` never shrinks on removal. `TrimExcess()` if you dropped a lot.
- Mutating during `foreach` throws `InvalidOperationException` — iterate a copy, or loop backwards by index when removing.
- `CollectionsMarshal.AsSpan(list)` gives a `Span<T>` over the backing array for allocation-free bulk work (and is unsafe if the list is modified while you hold it).

<a id="be-b281"></a>

#### Frozen and immutable

``` csharp
private static readonly FrozenDictionary<string, Handler> Handlers =
    BuildHandlers().ToFrozenDictionary(StringComparer.Ordinal);
```

**`FrozenDictionary` / `FrozenSet`** (.NET 8) — expensive to construct, then the fastest reads available. The builder analyses the actual keys and picks a specialized strategy (length-based, character-position-based for strings, perfect hashing for small sets). Use for lookup tables built once at startup and read forever. Wrong for anything constructed per request.

**`ImmutableArray<T>`** — a struct wrapping an array. O(1) indexed read, **O(n) for any modification** (it copies). Ideal for small collections shared across threads.

**`ImmutableList<T>`** — a balanced tree. O(log n) read *and* modify. Better than `ImmutableArray` for large collections modified often; much worse for indexed reads.

**Structural sharing** is what makes that possible. Adding to an immutable tree doesn't copy the tree — it creates new nodes only along the path from the root to the insertion point, and **reuses every untouched subtree by reference**:

``` yaml
original:   A            after Add:   A'          A' and A share
           / \                       / \          the entire left
          B   C                     B   C'        subtree B — no copy
             / \                       /             D   E                     D   E'
```

For a tree of a million items, adding one allocates ~20 new nodes rather than a million. Both versions remain valid and immutable simultaneously, which is why persistent data structures can be shared across threads with no locking at all.

`ImmutableArray<T>` cannot do this — it's a flat array, so any modification copies all n elements. That's the trade: O(1) indexing and perfect locality, versus O(log n) everything and cheap modification.

**`ImmutableArray<T>` has a null trap**: it's a struct, so `default(ImmutableArray<T>)` has a null backing array and throws on use. Check `.IsDefault`, and never rely on field initialization to give you an empty one — use `ImmutableArray<T>.Empty`.

Use the `.Builder` types when constructing in a loop; otherwise each `Add` allocates.

<a id="be-b283"></a>

#### Read-only views versus copies

``` csharp
IReadOnlyList<T> view = list;                    // a VIEW — caller sees later mutations
var copy = list.ToList();                        // a snapshot
var wrapper = list.AsReadOnly();                 // wrapper, still a view
ImmutableArray<T> immutable = list.ToImmutableArray();  // genuinely immutable
```

`IReadOnlyList<T>` means "you can't change it through this reference." It does **not** mean the collection can't change. Returning a read-only view of a mutable private field still lets callers observe mid-mutation states. If that matters, return a copy or an immutable type.

------------------------------------------------------------------------

### Implementing Them Yourself

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Implementing Them Yourself</p>

<p class="pd">Understanding is easier from the inside. These are simplified but faithful.</p>

<a id="be-b284"></a>

#### Dynamic array

``` csharp
public class DynamicArray<T>
{
    private T[] _items = Array.Empty<T>();
    public int Count { get; private set; }

    public void Add(T item)
    {
        if (Count == _items.Length) Grow();
        _items[Count++] = item;
    }

    private void Grow()
    {
        int next = _items.Length == 0 ? 4 : _items.Length * 2;
        var bigger = new T[next];
        Array.Copy(_items, bigger, Count);
        _items = bigger;
    }

    public T this[int i]
    {
        get { if ((uint)i >= (uint)Count) throw new IndexOutOfRangeException(); return _items[i]; }
        set { if ((uint)i >= (uint)Count) throw new IndexOutOfRangeException(); _items[i] = value; }
    }

    public void RemoveAt(int i)
    {
        Count--;
        Array.Copy(_items, i + 1, _items, i, Count - i);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            _items[Count] = default!;              // release the reference, or you leak
    }
}
```

Two details from the real implementation. **Doubling is what makes `Add` amortized O(1)**: growing by a constant instead would make n appends O(n²). And **clearing the vacated slot matters** — leaving a stale reference keeps an object alive forever. The `IsReferenceOrContainsReferences` check lets the JIT delete that branch entirely for value types.

The `(uint)i >= (uint)Count` idiom is one comparison instead of two: a negative index becomes a huge unsigned value and fails the same check.

### Collections

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Collections</p>

<a id="be-m98"></a>

#### Arrays: layout, covariance, jagged vs multidimensional

An array is a heap object with a length header followed by elements laid out contiguously. For value types the elements are inline; for reference types they are pointers. Contiguity is why arrays are fast: sequential access is cache-friendly and prefetchable.

``` text
int[] a = new int[3];        // 8 header + 8 mt + 8 length + 12 data, padded = 40 bytes
int[,] grid = new int[3,4];  // rectangular: one object, row-major, bounds-checked twice
int[][] jag = new int[3][];  // jagged: 4 objects, one indirection, but faster indexing
```

**Jagged usually beats rectangular** in .NET, because the JIT optimises single-dimension indexing (including eliminating bounds checks in `for (i = 0; i < a.Length; i++)`) but does much less for multidimensional arrays.

**Array covariance is a runtime hole** inherited from pre-generics Java compatibility:

``` text
object[] objs = new string[3];   // compiles: string[] is assignable to object[]
objs[0] = 42;                    // ArrayTypeMismatchException at runtime
```

Because of this, every write to a reference-type array pays a covariance check. Writes to `List<T>` do not (generics are properly invariant), though `List<T>` adds its own indirection.

> **\[Tip\]**
>
> **Bounds-check elimination.** The JIT removes the bounds check when the loop bound is literally `array.Length`. Caching the length in a local (`int n = a.Length;`) can *defeat* the optimisation. Write the idiomatic loop.

<a id="be-m99"></a>

#### List of T: growth, capacity, and pre-sizing

`List<T>` is a wrapper over a `T[]` plus a count. When full it allocates a new array of **double** the capacity (starting at 4) and copies. Growth is amortised O(1) but the copies are real, and the discarded arrays are garbage.

``` csharp
var list = new List<int>();          // capacity 0
for (int i = 0; i < 1000; i++) list.Add(i);
// allocates arrays of size 4,8,16,...,1024 - 9 allocations, ~2000 elements copied

var sized = new List<int>(1000);     // 1 allocation, 0 copies
```

| Operation                      | Cost                            |
|--------------------------------|---------------------------------|
| `list[i]`                      | O(1)                            |
| `Add`                          | O(1) amortised                  |
| `Insert(0, x)` / `RemoveAt(0)` | **O(n)** — shifts everything    |
| `Remove(x)`                    | O(n) — linear search then shift |
| `Contains` / `IndexOf`         | O(n)                            |
| `BinarySearch`                 | O(log n) — *only if sorted*     |

``` csharp
// Zero-copy access to the backing array (.NET 5+), for hot read loops:
foreach (ref var item in CollectionsMarshal.AsSpan(list)) item.Count++;

// Setting Count directly to fill from a source (.NET 8+):
CollectionsMarshal.SetCount(list, n);
```

> **\[Trap\]**
>
> **Trap.** Removing items inside a `foreach` throws `InvalidOperationException`. Removing inside a forward `for` loop silently skips elements. Iterate *backwards*, or use `list.RemoveAll(predicate)`, which does a single compacting pass in O(n).

<a id="be-m107"></a>

#### Immutable collections

`System.Collections.Immutable` gives persistent data structures: every "mutation" returns a new collection that shares most of its structure with the old one.

``` csharp
ImmutableList<int> a = ImmutableList.Create(1, 2, 3);
ImmutableList<int> b = a.Add(4);   // a is unchanged; b shares the tree with a
```

| Type                       | Structure                 | Indexing                      | Add              |
|----------------------------|---------------------------|-------------------------------|------------------|
| `ImmutableArray<T>`        | a wrapped `T[]`, a struct | **O(1)**, as fast as an array | O(n) — full copy |
| `ImmutableList<T>`         | AVL tree                  | O(log n)                      | O(log n)         |
| `ImmutableDictionary<K,V>` | AVL tree                  | O(log n)                      | O(log n)         |

``` csharp
// Batch changes through a builder to avoid an allocation per step:
var builder = ImmutableArray.CreateBuilder<int>(capacity);
foreach (var x in source) builder.Add(x);
ImmutableArray<int> result = builder.MoveToImmutable();
```

> **\[Why it matters\]**
>
> **When they earn their keep.** Configuration or reference data that is read constantly and swapped occasionally: build the new version, then a single reference assignment publishes it atomically to all readers, with no locks and no torn reads. That is a much cleaner pattern than a `ReaderWriterLockSlim` around a mutable dictionary.

> **\[Trap\]**
>
> **Trap.** `ImmutableArray<T>` is a struct wrapping an array, so `default(ImmutableArray<T>)` has a null inner array and throws on almost any use. Check `IsDefault` or always initialise with `ImmutableArray<T>.Empty`.

<a id="be-m108"></a>

#### FrozenDictionary and FrozenSet

.NET 8 added collections optimised for the "build once at startup, read millions of times" case. Construction is expensive — it analyses the keys and picks a specialised implementation (length-based dispatch, substring hashing, perfect-hash-like layouts) — and reads are meaningfully faster than `Dictionary`.

``` csharp
private static readonly FrozenDictionary<string, Handler> Routes =
    BuildRoutes().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

private static readonly FrozenSet<string> Reserved =
    new[] { "admin", "api", "www" }.ToFrozenSet(StringComparer.Ordinal);
```

Rough guidance on read-heavy string-keyed lookups: `FrozenDictionary` is often 1.5–3x faster than `Dictionary` for `TryGetValue`, with the gap largest for small-to-medium sets of string keys.

> **\[Tip\]**
>
> **Rule.** Use it for static route tables, permission maps, enum-to-string maps, country codes, feature flags loaded at startup — anything in a `static readonly` field. Do not use it for anything you rebuild per request; the construction cost dwarfs the savings.

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

<a id="be-m109"></a>

#### Collection interfaces and what to expose

``` text
IEnumerable<T>      // forward iteration only. May be lazy, infinite, or single-use.
IReadOnlyCollection<T>  // + Count
IReadOnlyList<T>    // + indexer
ICollection<T>      // + Add, Remove, Clear, Contains
IList<T>            // + indexer, Insert, RemoveAt
IQueryable<T>       // an expression tree; a provider translates it (see §140)
```

**Return types**, in decreasing order of caller freedom:

- `IEnumerable<T>` — honest about laziness, but invites multiple enumeration and hides whether the data is already in memory.
- `IReadOnlyList<T>` — **the usual best choice** for a method that returns materialised data. Signals "this is finished, you can count and index it, don't modify it".
- `List<T>` — fine internally; leaks mutability across an API boundary.

``` csharp
// Accept the widest, return the most informative:
public IReadOnlyList<Order> Filter(IEnumerable<Order> source) => source.Where(...).ToList();
```

> **\[Trap\]**
>
> **Trap.** `IReadOnlyList<T>` is not a defensive copy. If you return the same `List<T>` you hold, the caller can cast it back to `List<T>` and mutate your state. For true isolation return `.ToArray()`, an `ImmutableArray<T>`, or a `ReadOnlyCollection<T>` wrapper (which still reflects later changes to the underlying list — a view, not a snapshot).

<a id="be-m110"></a>

#### Choosing a collection: decision table

| If you need…                                     | Use                                                  |
|--------------------------------------------------|------------------------------------------------------|
| a fixed-size sequence, maximum speed and density | `T[]`                                                |
| a growable sequence — the default                | `List<T>`                                            |
| key → value lookup                               | `Dictionary<K,V>`                                    |
| key → value, built once, read constantly         | `FrozenDictionary<K,V>`                              |
| key → many values                                | `ILookup` via `ToLookup`                             |
| uniqueness / membership tests                    | `HashSet<T>`                                         |
| always-sorted with frequent inserts              | `SortedDictionary` / `SortedSet`                     |
| sorted, built once, then indexed                 | `SortedList`, or a sorted `List<T>` + `BinarySearch` |
| FIFO / LIFO                                      | `Queue<T>` / `Stack<T>`                              |
| "next most important item"                       | `PriorityQueue<T,TPriority>`                         |
| shared mutable cache across threads              | `ConcurrentDictionary<K,V>`                          |
| async producer/consumer                          | `Channel<T>`                                         |
| snapshot-safe shared config                      | `ImmutableDictionary` / `ImmutableArray`             |
| a window over existing memory                    | `Span<T>` / `Memory<T>`                              |
| a large reusable buffer                          | `ArrayPool<T>.Shared`                                |

> **\[Tip\]**
>
> **Default answer.** `List<T>` and `Dictionary<K,V>` cover about 90% of real code correctly. Reach past them when you have a measured reason, not a theoretical one.

<a id="be-m112"></a>

#### Enumerators, struct enumerators, and foreach

`foreach` is not defined in terms of `IEnumerable` — it is a *pattern match*. The compiler looks for any `GetEnumerator()` returning something with `Current` and `MoveNext()`, and only falls back to the interface if that fails.

``` csharp
foreach (var x in list) { }
// compiles to roughly:
List<int>.Enumerator e = list.GetEnumerator();   // a STRUCT - no allocation
try { while (e.MoveNext()) { var x = e.Current; } }
finally { ((IDisposable)e).Dispose(); }
```

This is why iterating a concrete `List<T>` allocates nothing, but iterating the same list through an `IEnumerable<T>` variable **boxes the struct enumerator** and adds an interface call per element.

``` csharp
void Sum(List<int> items)        => foreach (var i in items) { }  // 0 allocations
void Sum(IEnumerable<int> items) => foreach (var i in items) { }  // 1 boxed enumerator
```

Iterator methods (`yield return`) generate a state machine class. The first `GetEnumerator()` on the calling thread reuses the same object; subsequent ones allocate a fresh instance.

> **\[Tip\]**
>
> **Practical rule.** In a hot inner loop, type the parameter as the concrete collection or as `ReadOnlySpan<T>` — you get the struct enumerator, devirtualised calls, and often bounds-check elimination. Everywhere else, take `IEnumerable<T>` and don't think about it.
