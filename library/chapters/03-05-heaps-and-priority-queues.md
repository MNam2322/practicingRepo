---
id: "3-5"
number: "3.5"
title: "Heaps and priority queues"
part: 3
part_title: "Data structures and algorithms"
words: 4353
minutes: 19
---

# 3.5 Heaps and priority queues

> **Quick look.** Priority queues hand back the most urgent item next, using a heap stored in a plain array.
>
> - A binary heap gives O(log n) add and remove and O(1) peek, without keeping everything sorted.
> - .NET's PriorityQueue returns the smallest priority first and doesn't keep ties in arrival order.
> - You can't change a priority in place; re-add the item instead.
> - Top-k over huge data: keep a min-heap of size k, O(n log k) time and O(k) memory.
> - Heaps power scheduling, timers and merging sorted streams.


### The big picture

Many problems boil down to "what is the most urgent thing next?": the next timer to fire, the next job to run, the aircraft that should land first. A priority queue answers that question, and a heap is the compact structure behind it.

A heap keeps the most urgent item at the front without keeping everything sorted, which makes it far cheaper than a sorted list. This chapter shows how a heap works inside an ordinary array, how .NET's `PriorityQueue` behaves, and several practical uses, including finding the top items in huge data with very little memory.

### Before you start

**Builds on:** 3.2 Arrays and lists, 1.5 Memory, 1.7 The operating system.

**Ideas explained later:**

- **Sequence number** (6.3): The position of a byte in a TCP stream, used to order data and detect gaps.
- **Controller** (8.3): A class whose action methods handle HTTP requests, with attributes for routing.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Dijkstra's algorithm** (3.6): Finding shortest paths with non-negative weights by always expanding the closest unvisited vertex.
- **Access pattern** (7.6): A specific question an application asks of its data, used to drive data design.
- **Quicksort** (3.7): Sorting by partitioning around a pivot and sorting each side. Fast in practice, O(n²) at worst.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **Comparer** (3.7): An object or function that decides the order of two items.

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, collection expressions, generic types, lambdas, LINQ, null operators, tuples, exceptions. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does a priority queue do that a normal queue doesn't?**

> It always hands back the item with the highest priority, usually meaning the smallest priority value, whatever order items were added in. A normal queue hands them back in arrival order.

**2. What are the costs of the main operations on a binary heap?**

> Adding and removing the top are O(log n). Looking at the top without removing it is O(1). Building a heap from n items at once is O(n).

**3. In a heap stored in an array, where are the children of the item at index i?**

> At `2i + 1` and `2i + 2`. Its parent is at `(i - 1) / 2`. No pointers are needed.

**4. Is a heap's array sorted?**

> No. It only guarantees that every parent is smaller than or equal to its children, which is enough to keep the minimum at the front.

**5. Two items in a PriorityQueue have the same priority. Which comes out first?**

> Either. .NET's `PriorityQueue` is not stable. If arrival order matters for ties, add a sequence number to the priority.

**6. How do you find the 10 largest values in a stream of a billion numbers with little memory?**

> Keep a min-heap of the 10 largest so far. For each new number, add it, and if the heap has 11 items, remove the smallest. Memory stays at 10 items, and the cost is O(n log 10).

**7. How do you change the priority of an item already in a PriorityQueue?**

> There is no fast way. The usual approach is lazy deletion: add the item again with its new priority, and skip stale entries when they come out. .NET 9 added a `Remove` method, but it is O(n).

### The problem

Many programs need "the most urgent thing next". An operating system scheduler picks the thread that should run next. A timer system needs the timer that fires soonest. An approach controller sequences aircraft by urgency and arrival time. A route finder expands the closest unexplored airport first, as Graphs (3.6) will show.

A **priority queue** is a collection built for this. You add items with a priority. You take out the item with the best priority. By convention in .NET, the best priority is the smallest value.

### Simple approaches

A sorted list keeps the best item at the front, so taking it is O(1). But every insert must find its place and shift the rest, which is O(n).

An unsorted list makes inserts O(1). But finding the best item means scanning everything, which is O(n).

Either way, a mix of *n* inserts and removals costs O(n²). A heap brings both operations down to O(log n).

### Binary heaps

A **binary heap** is a binary tree with two properties.

The **shape property**: the tree is a **complete binary tree**. Every level is full except possibly the last, which fills from left to right. So the tree is as short as possible, with height about log₂ *n*.

The **heap property**: every parent is smaller than or equal to its children. So the smallest item is always at the root. This is a **min-heap**. A **max-heap** flips the rule and keeps the largest at the root.

Notice what the heap property doesn't say. There is no order between siblings or cousins. A heap is only "sorted enough" to find the minimum, and that looser rule is what makes it cheap to maintain.

### A tree without pointers

Because the tree is complete, it fits perfectly into an array, level by level, with no gaps. The root is at index 0. For the item at index *i*, the left child is at 2*i* + 1, the right child at 2*i* + 2, and the parent at (*i* − 1) / 2, rounding down.

No nodes, no references: just arithmetic on indexes. The whole heap is one contiguous array, which the cache handles well, as Arrays and lists (3.2) explained.

### Adding and removing

**Adding** puts the new item at the end of the array, which keeps the shape complete. It may be smaller than its parent, breaking the heap property. So it swaps with its parent, and again, until its parent is smaller or it reaches the root. This is **sift up**. It moves at most the height of the tree: O(log n).

**Removing the top** takes the root out. The last item in the array moves into the root's place, which keeps the shape complete. It is probably too large for the top, so it swaps with its smaller child, and again, until both children are larger or it reaches the bottom. This is **sift down**, also O(log n).

**Peeking** at the top is just reading index 0: O(1).

**Building** a heap from *n* items by adding them one at a time costs O(n log n). A cleverer method, called **heapify**, puts all the items in the array and then sifts down every parent, starting from the last one and working back to the root. Most nodes are near the bottom and move only a step or two, so the total is O(n). `PriorityQueue`'s constructor and `EnqueueRange` use it.

```csharp
// heaps.cs — run with: dotnet run heaps.cs
using System.Linq;

// 1. A hand-written min-heap
var heap = new MinHeap<int>();
foreach (int x in new[] { 5, 3, 8, 1, 9, 2 }) heap.Push(x);
Console.WriteLine(string.Join(",", heap.Items));   // 1,3,2,5,9,8: not sorted, but each parent <= its children
var drained = new List<int>();
while (heap.Count > 0) drained.Add(heap.Pop());
Console.WriteLine(string.Join(",", drained));      // 1,2,3,5,8,9

// 2. The built-in PriorityQueue, with a composite priority
var landing = new PriorityQueue<string, (int Urgency, TimeOnly Eta)>();
landing.Enqueue("BA117", (2, new TimeOnly(14, 5)));
landing.Enqueue("AF1680", (2, new TimeOnly(13, 58)));
landing.Enqueue("EK1", (0, new TimeOnly(14, 20)));    // fuel emergency: most urgent
landing.Enqueue("LH900", (1, new TimeOnly(14, 10)));  // medical emergency
while (landing.TryDequeue(out string? flight, out var p))
    Console.WriteLine($"{flight}: urgency {p.Urgency}, eta {p.Eta:HH:mm}");
// EK1, LH900, AF1680, BA117: urgency first, then earliest arrival

// 3. Top 5 of a million values, keeping only 5 in memory
var rng = new Random(7);
int[] delays = Enumerable.Range(0, 1_000_000).Select(_ => rng.Next(0, 600)).ToArray();
var top = new PriorityQueue<int, int>();             // a min-heap of the largest values so far
foreach (int d in delays)
{
    top.Enqueue(d, d);
    if (top.Count > 5) top.Dequeue();                // drop the smallest of the six
}
int[] topFive = top.UnorderedItems.Select(x => x.Element).OrderDescending().ToArray();
Console.WriteLine(topFive.SequenceEqual(delays.OrderDescending().Take(5)));   // True

// 4. Merge already-sorted lists: departure times in minutes from three terminals
int[][] boards = [[605, 740, 915], [610, 700, 1200], [555, 1005]];
var next = new PriorityQueue<(int Board, int Index), int>();
for (int b = 0; b < boards.Length; b++) next.Enqueue((b, 0), boards[b][0]);
var merged = new List<int>();
while (next.TryDequeue(out var pos, out int time))
{
    merged.Add(time);
    if (pos.Index + 1 < boards[pos.Board].Length)
        next.Enqueue((pos.Board, pos.Index + 1), boards[pos.Board][pos.Index + 1]);
}
Console.WriteLine(string.Join(",", merged));         // 555,605,610,700,740,915,1005,1200

sealed class MinHeap<T>(IComparer<T>? comparer = null)
{
    readonly List<T> items = [];
    readonly IComparer<T> cmp = comparer ?? Comparer<T>.Default;

    public int Count => items.Count;
    public IReadOnlyList<T> Items => items;
    public T Peek() => items.Count > 0 ? items[0] : throw new InvalidOperationException("The heap is empty.");

    public void Push(T item)
    {
        items.Add(item);                                   // keep the shape: add at the end
        int i = items.Count - 1;
        while (i > 0)                                      // sift up
        {
            int parent = (i - 1) / 2;
            if (cmp.Compare(items[i], items[parent]) >= 0) break;
            (items[i], items[parent]) = (items[parent], items[i]);
            i = parent;
        }
    }

    public T Pop()
    {
        T top = Peek();
        T last = items[^1];
        items.RemoveAt(items.Count - 1);
        if (items.Count == 0) return top;
        items[0] = last;                                   // keep the shape: move the last item to the root
        int i = 0;
        while (true)                                       // sift down
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < items.Count && cmp.Compare(items[left], items[smallest]) < 0) smallest = left;
            if (right < items.Count && cmp.Compare(items[right], items[smallest]) < 0) smallest = right;
            if (smallest == i) break;
            (items[i], items[smallest]) = (items[smallest], items[i]);
            i = smallest;
        }
        return top;
    }
}
```

### PriorityQueue in .NET

.NET 6 added `PriorityQueue<TElement, TPriority>`. It is a min-heap, built on an array, with four children per node instead of two. A wider tree is shallower, which means fewer cache misses when sifting down.

The element and its priority are separate, so the same kind of item can be queued with different priorities. Any comparable type can be a priority. Tuples compare field by field, which gives multi-level priorities for free, as the landing example shows. Pass a custom `IComparer<TPriority>` for a max-heap or any other order.

It has three limits worth knowing. It is not stable: items with equal priority come out in no particular order. If arrival order should break ties, add a sequence number as the last part of the priority. Its `UnorderedItems` really are unordered. And there is no fast way to change an item's priority.

### Priorities that change

Some algorithms need to lower an item's priority while it waits, for example when a shorter route to an airport is found. Classic heaps support a **decrease-key** operation, but it needs to know where the item sits in the array, which `PriorityQueue` doesn't expose.

The practical answer is **lazy deletion**. Don't find and update the old entry. Add the item again with its new priority. When an item comes out of the queue, check whether it is stale, for example because a better entry for it was already processed, and skip it if so. The queue holds some junk for a while, which costs a little memory and a few extra removals. .NET 9 added a `Remove` method, but it searches the whole array and is O(n).

### Where heaps are used

**Scheduling.** Operating system schedulers, job queues and retry queues ordered by "run at" time.

**Timers.** Keeping many timers in a heap ordered by expiry time. Very large timer systems use timer wheels instead, which bucket timers by time slot.

**Shortest paths.** Dijkstra's algorithm and A* use a priority queue of places to explore, as Graphs (3.6) shows.

**Simulation.** A discrete-event simulation keeps future events in a priority queue by time and always processes the earliest.

**Top-k.** Finding the k largest items with a size-k min-heap costs O(n log k) time and O(k) memory. That is the pattern in the example, and it works on data too large to sort or even to hold.

**Merging.** Combining k sorted streams by always taking the smallest front item, as databases do when sorting data that doesn't fit in memory.

**Running median.** Keeping a max-heap of the lower half of the values and a min-heap of the upper half gives the median of a stream in O(log n) per new value.

### Heap sort

A heap also sorts. Heapify the array in O(n), then repeatedly move the top to the end of the array and sift down the rest. The result is **heap sort**: O(n log n) in every case, with no extra memory. It isn't stable, and its jumping access pattern is unfriendly to the cache, so it is usually slower than quicksort in practice. .NET's `Array.Sort` falls back to it when quicksort is going badly, as Searching and sorting (3.7) explains.

### Two different heaps

The heap in this chapter has nothing to do with the heap in Memory (1.5). One is a data structure; the other is a region of memory for objects. They share a name by historical accident.

### In short

A priority queue always hands back the best item next. A binary heap does this with O(log n) adds and removals and O(1) peeks, stored in a plain array with no pointers. It isn't sorted; it only guarantees that each parent comes before its children. .NET's `PriorityQueue` gives the smallest priority first, doesn't keep ties in arrival order, and can't change a priority in place, so re-add items instead. Heaps make top-k, merging and scheduling efficient.

### What goes wrong

**Relying on arrival order for ties.** Two items with the same priority come out in a different order than they went in, and a "first come, first served" rule breaks. Add a sequence number to the priority.

**Changing a priority after enqueueing.** If the priority is a mutable object and you change it in place, the heap property breaks silently and the queue returns wrong answers. Treat priorities as immutable and use lazy deletion.

**Treating `UnorderedItems` as sorted.** It is the raw heap array. Sort it, or dequeue, if you need order.

**Sorting everything to take a few.** `OrderBy(...).Take(10)` over millions of items sorts far more than needed. A size-10 heap is cheaper in time and memory.

**Min when you wanted max.** `PriorityQueue` returns the smallest priority. For "highest first", pass a reversed comparer or negate numeric priorities.

### Cost model

| Operation | Binary heap | Sorted list | Unsorted list |
|---|---|---|---|
| Add | O(log n) | O(n) | O(1) |
| Remove the minimum | O(log n) | O(1) | O(n) |
| Peek at the minimum | O(1) | O(1) | O(n) |
| Build from n items | O(n) | O(n log n) | O(n) |
| Change a priority | O(n) to find, or lazy | O(n) | O(1) |

With a million items, each add or remove moves through about 10 levels of a four-child heap, typically well under a microsecond. Top-k over n items costs O(n log k), which for small k is close to a single pass over the data.

### In other languages (optional)

**JavaScript and TypeScript.** There is no built-in priority queue. Libraries provide one, or you write a small heap like `MinHeap` above.

**Python.** The `heapq` module turns a plain list into a min-heap, with `heappush`, `heappop` and `nlargest` for top-k. Tuples of `(priority, counter, item)` are the usual trick for stable ordering.

**Java.** `PriorityQueue` is a binary min-heap.

**Go.** The `container/heap` package provides heap operations over any type that implements a small interface.

**SQL.** `ORDER BY ... OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY`, or `TOP 10` in SQL Server, often runs as a top-k sort in the query plan, keeping only the best rows in memory.

### Key terms

- **Priority queue**: A collection that always hands back the item with the best priority, the smallest value by convention.
- **Binary heap**: A complete binary tree, stored in an array, where every parent is no larger than its children.
- **Complete binary tree**: A binary tree where every level is full except possibly the last, which fills from the left.
- **Heap property**: The rule that every parent is ordered before its children, so the best item is at the root.
- **Min-heap**: A heap with the smallest item at the root.
- **Max-heap**: A heap with the largest item at the root.
- **Sift up**: Swapping a new item with its parent until the heap property holds again.
- **Sift down**: Swapping an item with its smaller child until the heap property holds again.
- **Heapify**: Building a heap from an unordered array in O(n) by sifting down every parent.
- **Decrease-key**: Lowering an item's priority while it stays in the heap. Not supported directly by PriorityQueue.
- **Lazy deletion**: Adding an item again with a new priority and skipping stale entries when they come out.
- **Top-k**: Finding the k largest or smallest items, efficiently done with a size-k heap.
- **Heap sort**: Sorting by building a heap and repeatedly moving its top to the end. O(n log n) and not stable.
- **Running median**: The median of a stream so far, maintained with two heaps as values arrive.

### Where this leads

Priority queues drive shortest-path search in Graphs (3.6). Heap sort and its place inside introsort are in Searching and sorting (3.7). Job queues ordered by time appear in Background work and queues (8.7). How an operating system picks the next thread was in The operating system (1.7).

### Check yourself

1. Add a constructor to `MinHeap<T>` that takes a whole array and builds the heap with heapify. Check that it gives the same pops as pushing one at a time.
2. Make the landing queue stable: add two flights with the same urgency and time, and make sure they come out in the order they were added.
3. Use `MinHeap<T>` with a reversed comparer to make a max-heap, and use it to find the 3 smallest values of an array.
4. Implement a running median over a stream of delays using two `PriorityQueue` objects. Print the median after each new value.
5. Write heap sort in place on an `int[]` using the index formulas from this chapter. Compare its speed with `Array.Sort` on a million items.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Processing the most urgent rebooking first

When a flight is cancelled, passengers with the tightest onward connections must be rebooked first.

*PriorityQueue*

```csharp
var queue = new PriorityQueue<Passenger, TimeSpan>();
foreach (var p in affected) queue.Enqueue(p, p.ConnectionDeparture - now);   // smallest priority first

while (queue.TryDequeue(out var p, out var slack))
    await rebooker.RebookAsync(p, ct);
// Priorities can't be changed in place: if slack changes, enqueue again and skip stale entries.
```

> **Lesson:** A heap hands back the most urgent item in O(log n) without keeping everything sorted.

#### Example 2: Top 10 routes by revenue from a 50-million-row stream

You need the ten best routes from a file far bigger than memory.

*A min-heap of size k*

```csharp
var top = new PriorityQueue<RouteRevenue, decimal>();
foreach (var r in ReadRoutes(path))                 // streamed, never all in memory
{
    top.Enqueue(r, r.Revenue);
    if (top.Count > 10) top.Dequeue();              // drop the smallest
}
var best = top.UnorderedItems.Select(x => x.Element).OrderByDescending(r => r.Revenue).ToList();
```

> **Lesson:** Top-k needs O(k) memory and O(n log k) time with a heap; sorting everything is unnecessary.

### The Collections

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Collections</p>

<a id="be-b280"></a>

#### Queue, Stack, LinkedList, PriorityQueue

``` text
Queue<T>   // circular buffer over an array. Enqueue/Dequeue/Peek all O(1).
Stack<T>   // array + index. Push/Pop/Peek all O(1).
```

`LinkedList<T>` is a doubly-linked list of heap nodes. O(1) insert/remove *given a node reference*, O(n) to find that node, and terrible cache locality — every traversal step is a pointer chase into a random location. In practice it is almost always slower than a `List<T>`, even for middle insertion, until n is large and you already hold the node. Its real use case is an LRU cache, where you hold node references in a dictionary.

``` csharp
// PriorityQueue<TElement, TPriority> (.NET 6+) - array-backed 4-ary min-heap
var pq = new PriorityQueue<Job, int>();
pq.Enqueue(job, priority);          // O(log n)
var next = pq.Dequeue();            // O(log n), lowest priority value first
pq.EnqueueDequeue(job, p);          // cheaper than Enqueue-then-Dequeue
```

> **\[Trap\]**
>
> **Trap.** `PriorityQueue` is **not stable** — equal priorities come out in arbitrary order. If insertion order matters for ties, make the priority a tuple: `(priority, sequenceNumber)`. It also has no `Remove` or update-priority operation; the standard workaround is to enqueue a new entry and skip stale ones on dequeue.

Foundations: [The memory hierarchy: registers to RAM](01-04-the-processor.md#be-m22)

### Implementing Them Yourself

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Implementing Them Yourself</p>

<p class="pd">Understanding is easier from the inside. These are simplified but faithful.</p>

<a id="be-b288"></a>

#### Binary heap

``` csharp
public class MinHeap<T> where T : IComparable<T>
{
    private readonly List<T> _h = new();
    public int Count => _h.Count;

    public void Push(T item)
    {
        _h.Add(item);
        int i = _h.Count - 1;
        while (i > 0)                                  // sift up
        {
            int parent = (i - 1) / 2;
            if (_h[i].CompareTo(_h[parent]) >= 0) break;
            (_h[i], _h[parent]) = (_h[parent], _h[i]);
            i = parent;
        }
    }

    public T Pop()
    {
        var top = _h[0];
        _h[0] = _h[^1];
        _h.RemoveAt(_h.Count - 1);
        int i = 0;
        while (true)                                   // sift down
        {
            int l = 2 * i + 1, r = l + 1, small = i;
            if (l < _h.Count && _h[l].CompareTo(_h[small]) < 0) small = l;
            if (r < _h.Count && _h[r].CompareTo(_h[small]) < 0) small = r;
            if (small == i) break;
            (_h[i], _h[small]) = (_h[small], _h[i]);
            i = small;
        }
        return top;
    }
}
```

A complete binary tree stored in an array — children of `i` are at `2i+1` and `2i+2`, so there are no pointers and traversal is contiguous. This is what `PriorityQueue<,>` is built on.

### Algorithms for data that does not fit

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Algorithms for data that does not fit</p>

<a id="be-m124"></a>

#### Top-K without sorting

Sorting a million items to take ten is O(n log n) time and O(n) memory. Keeping a min-heap of size *k* is O(n log k) time and O(k) memory — and it works on a stream you never fully hold.

``` csharp
public static List<T> TopK<T>(IEnumerable<T> source, int k, Func<T, long> score) {
    // A MIN-heap of the best k so far. The root is the WEAKEST survivor,
    // which is exactly the one to evict when something better arrives.
    var heap = new PriorityQueue<T, long>(k);

    foreach (var item in source) {
        var s = score(item);
        if (heap.Count < k) heap.Enqueue(item, s);
        else if (s > heap.Peek().Priority)      // note: Peek() gives element + priority
            heap.DequeueEnqueue(item, s);       // single sift, cheaper than Dequeue+Enqueue
    }

    var result = new List<T>(heap.Count);
    while (heap.TryDequeue(out var item, out _)) result.Add(item);
    result.Reverse();                            // heap drains weakest-first
    return result;
}
```

| n = 10,000,000, k = 100 | Sort                | Heap                         |
|-------------------------|---------------------|------------------------------|
| Comparisons             | ~2.3×10<sup>8</sup> | ~10<sup>7</sup> + rare sifts |
| Memory                  | the whole set       | 100 items                    |
| Works on a stream       | no                  | yes                          |

> **\[Tip\]**
>
> **LINQ already does some of this.** `OrderBy().Take(n)` has had a partial-sort fast path since .NET Core 2.0, so for in-memory data it is often good enough and much clearer ([OrderBy: stability, ThenBy, comparers](03-10-collections-and-linq-in-net.md#be-m145)). Write the heap when the source is a stream, when *k* is fixed and small, or when you need top-K *per group* over many groups at once — where the LINQ version would materialise every group.

> **\[Trap\]**
>
> **`PriorityQueue` is not stable** ([Queue, Stack, LinkedList, PriorityQueue](#be-b280)). Equal scores come out in arbitrary order, so a "top 10" over ties is non-deterministic between runs. If that matters — and for anything a user sees, it does — make the priority a composite: `(score, -sequenceNumber)`, or break ties on the id.

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)
