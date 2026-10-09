---
id: "3-10"
number: "3.10"
title: "Collections and LINQ in .NET"
part: 3
part_title: "Data structures and algorithms"
words: 6347
minutes: 28
---

# 3.10 Collections and LINQ in .NET

> **Quick look.** How .NET collections and LINQ really behave, and where they cost time or memory.
>
> - Most LINQ operators are lazy: nothing runs until you enumerate, and it runs again each time.
> - IEnumerable runs in memory; IQueryable is translated to SQL. Mixing them up pulls whole tables into memory.
> - Streaming operators (Where, Select) hold nothing; buffering ones (OrderBy, GroupBy) consume the whole source.
> - Prefer Sum, Max, Count and CountBy over Aggregate; use Single only when duplicates mean corruption.
> - Each operator allocates enumerators and delegates; in hot paths, measure with MemoryDiagnoser.
> - Fix accidental O(n²) first, client-side evaluation second, redundant passes third, allocations last.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: IEnumerable that pulled a whole table into memory

A repository returns `IEnumerable<Order>` and the caller adds `.Where(o => o.CustomerId == id)`. The database log shows `SELECT * FROM Orders` with no WHERE.

*Before*

```csharp
IEnumerable<Order> All() => db.Orders;                    // later filters run in memory
var mine = repo.All().Where(o => o.CustomerId == id).ToList();
```

*After*

```csharp
Task<List<OrderSummary>> ForCustomer(int customerId, CancellationToken ct) =>
    db.Orders.Where(o => o.CustomerId == customerId)        // stays IQueryable: becomes SQL
             .Select(o => new OrderSummary(o.Id, o.Total))
             .ToListAsync(ct);
```

> **Lesson:** IEnumerable runs in memory, IQueryable becomes SQL. Filter before the boundary, and return results rather than open queries.

#### Example 2: Counting bookings per route in one pass

A report groups bookings by route only to count them, materializing every group.

*CountBy (.NET 9)*

```csharp
var perRoute = bookings.CountBy(b => b.Route);   // one dictionary of counters
foreach (var (route, count) in perRoute.OrderByDescending(kv => kv.Value).Take(5))
    Console.WriteLine($"{route}: {count}");
```

> **Lesson:** Pick the operator that matches the question; CountBy and AggregateBy avoid building groups you only count.

### Dependency Injection & Concurrency

<p class="src">From <em>.NET Backend Deep Dive</em> · Dependency Injection & Concurrency</p>

<a id="dn-11"></a>

#### LINQ: aggregating data

##### `Aggregate` — fold/reduce

```csharp
// Seed plus an accumulator function
var total = orders.Aggregate(0m, (sum, o) => sum + o.Total);

// With a result selector
var avg = orders.Aggregate(
    (Count: 0, Sum: 0m),
    (acc, o) => (acc.Count + 1, acc.Sum + o.Total),
    acc => acc.Count == 0 ? 0 : acc.Sum / acc.Count);

// Where it genuinely shines: composing a dynamic predicate
Expression<Func<Order, bool>> predicate = filters
    .Aggregate<Filter, Expression<Func<Order, bool>>>(
        o => true,
        (acc, f) => acc.And(f.ToExpression()));
```

> In practice, 90% of the time `Sum`/`Max`/`Count` reads better than `Aggregate`. Reach for `Aggregate` only when no built-in reduction fits.

##### Group + aggregate (the common shape)

```csharp
// Runs IN THE DATABASE - EF translates this to GROUP BY
var stats = await db.Orders
    .Where(o => o.CreatedAt >= from)
    .GroupBy(o => new { o.CustomerId, Month = o.CreatedAt.Month })
    .Select(g => new CustomerMonthStats
    {
        CustomerId = g.Key.CustomerId,
        Month      = g.Key.Month,
        OrderCount = g.Count(),
        Revenue    = g.Sum(o => o.Total),
        AvgTicket  = g.Average(o => o.Total),
        MaxOrder   = g.Max(o => o.Total),
        FirstAt    = g.Min(o => o.CreatedAt)
    })
    .OrderByDescending(s => s.Revenue)
    .Take(100)
    .ToListAsync();
```

**Warning:** a `GroupBy` whose `Select` is **not** an aggregate (e.g. selecting the group's elements) **cannot be translated to SQL** by EF Core — it either pulls everything client-side or throws. When you need that shape:

```csharp
// Fetch the minimum data, then group in memory
var rows = await db.Orders
    .Where(o => o.CreatedAt >= from)
    .Select(o => new { o.CustomerId, o.Total, o.CreatedAt })
    .ToListAsync();

var grouped = rows
    .GroupBy(r => r.CustomerId)
    .ToDictionary(g => g.Key, g => g.ToList());
```

##### In-memory operators worth knowing

```csharp
orders.ToLookup(o => o.CustomerId);                 // 1-to-many, duplicate keys fine
orders.ToDictionary(o => o.Id);                     // 1-to-1, duplicate key throws
orders.DistinctBy(o => o.CustomerId);               // .NET 6+
orders.Chunk(500);                                  // batching
orders.GroupJoin(payments, o => o.Id, p => p.OrderId, (o, ps) => new { o, ps });
orders.SelectMany(o => o.Items);                    // flatten
orders.OrderBy(o => o.Priority).ThenByDescending(o => o.CreatedAt);
orders.Sum(o => o.Total);                           // decimal - no int overflow surprises
```

**Deferred execution — the trap:**

```csharp
var q = db.Orders.Where(o => o.Total > 100);   // nothing has run yet
var c = q.Count();                             // query #1
var l = q.ToList();                            // query #2 (!)
// If you need it more than once, materialize once: var list = q.ToList();
```

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a19"></a>

#### LINQ and deferred execution

LINQ is a set of extension methods over `IEnumerable<T>` plus a query syntax that compiles down to them. The critical behaviour is that most operators are lazy: they build a pipeline and execute nothing until you enumerate.

``` csharp
var q = orders.Where(o => o.Total > 100)   // nothing runs yet
              .Select(o => o.CustomerId);

foreach (var id in q) { }   // runs now
var list = q.ToList();      // runs AGAIN - second full pass
```

**IEnumerable\<T\> (LINQ to Objects)**

- Runs in memory, in your process
- Takes `Func<T,bool>` — a compiled delegate
- Whatever you write, it will work

**IQueryable\<T\> (LINQ to Entities)**

- Translated to SQL by the provider
- Takes `Expression<Func<T,bool>>` — a parsed tree
- Unsupported constructs throw or silently fall back to client evaluation

> **\[Trap\]**
>
> **Common trap.** Calling `.ToList()` too early collapses an `IQueryable` into memory — the rest of your `Where` clauses then run in C# after pulling the whole table across the network.

###### How it actually works

`Where` does not filter anything. It constructs a `WhereEnumerableIterator` that holds a reference to the source and the predicate. Nothing runs until something calls `MoveNext`.

``` csharp
// this, conceptually, is what .Where().Select() builds
public static IEnumerable<T> Where<T>(this IEnumerable<T> src, Func<T,bool> pred)
{
    foreach (var item in src)          // pulls one item from the source
        if (pred(item))
            yield return item;         // hands it on, then pauses here
}

// so a chain is a PULL pipeline, one element at a time, not three passes over the data
var q = numbers.Where(IsEven).Select(Square).Take(3);

// MoveNext on Take -> asks Select -> asks Where -> asks numbers
// nothing beyond the 3rd match is ever evaluated
```

This is why `Take` after an expensive `Select` is cheap, why `First()` on a million-element query can return instantly, and why a chain of ten operators makes one pass over the data rather than ten.

###### What this looks like when it breaks

Three ways deferred execution bites in production.

``` csharp
// 1. The query captures a variable, not its value
var results = new List<IEnumerable<Order>>();
foreach (var id in customerIds)
    results.Add(orders.Where(o => o.CustomerId == id));   // captures the LOOP VARIABLE
// in C# 5+ foreach variables are per-iteration so this is now safe,
// but the same bug with a 'for' loop index is still very much alive.

// 2. Multiple enumeration re-runs everything
IEnumerable<Order> Expensive() => LoadFromApi().Where(o => o.IsActive);
var q = Expensive();
if (q.Any())                    // full API call + filter
    Process(q.First());         // full API call + filter AGAIN
var count = q.Count();          // and again

// 3. The source changed underneath the query
var pending = orders.Where(o => o.Status == Pending);
orders.Clear();
foreach (var o in pending) { }  // InvalidOperationException, or silently empty
```

The rule that avoids all three: an `IEnumerable<T>` returned from a method is a *recipe*, not a result. If a caller might enumerate it more than once, or if the source is mutable, materialise it with `ToList()` at the boundary and return `IReadOnlyList<T>`. The type then tells the caller which one it is.

###### Operators worth knowing that people re-implement by hand

| Operator                     | Does                             | Replaces                                                 |
|------------------------------|----------------------------------|----------------------------------------------------------|
| `GroupJoin`                  | Left outer join producing groups | Nested loops with a dictionary                           |
| `SelectMany`                 | Flatten nested collections       | Two nested foreach loops with a list                     |
| `Zip`                        | Pairwise combine two sequences   | An index-based for loop                                  |
| `Chunk(n)`                   | Split into batches of n          | Manual batching loops for bulk inserts                   |
| `DistinctBy / MaxBy / MinBy` | By a key selector                | `OrderBy().First()`, which is O(n log n) instead of O(n) |
| `ToLookup`                   | One-to-many dictionary, eagerly  | `GroupBy().ToDictionary(g => g.Key, g => g.ToList())`    |
| `Aggregate`                  | Fold with an accumulator         | A loop with a running variable                           |

``` csharp
// batching for a bulk insert, in one line
foreach (var batch in rows.Chunk(1000))
    await BulkInsertAsync(batch, ct);

// O(n) instead of O(n log n)
var newest = orders.MaxBy(o => o.PlacedAt);

// a lookup is the right answer far more often than GroupBy
var linesByOrder = allLines.ToLookup(l => l.OrderId);
foreach (var o in orders)
    Process(o, linesByOrder[o.Id]);      // empty sequence for misses, never null
```

### Enumeration and LINQ

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Enumeration and LINQ</p>

<a id="be-b292"></a>

#### Iterators are a state machine

``` csharp
IEnumerable<int> Evens(int max)
{
    for (int i = 0; i <= max; i += 2)
        yield return i;
}
```

The compiler generates a class implementing `IEnumerable<int>` and `IEnumerator<int>`, hoisting `i` and `max` into fields and adding a state field. `MoveNext()` resumes at the right place.

If that sounds familiar, it should: **this is exactly the transformation `async`/`await` uses.** Locals become fields on a heap object, a bookmark records the position, and one resumable method jumps back in. Iterators came first; async reused the machinery. Both exist because *a stack frame cannot be paused*.

Consequences that follow directly:

- **Nothing runs until you enumerate.** Calling `Evens(10)` executes no code at all.
- **Argument validation is therefore deferred** — a `throw` at the top of an iterator fires on the first `MoveNext`, not at the call. Split it:

``` csharp
public IEnumerable<int> Take(int n)
{
    if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));   // eager
    return Core(n);
    static IEnumerable<int> Core(int n) { for (int i = 0; i < n; i++) yield return i; }
}
```

- **`ref struct`s (including `Span<T>`) can't cross a `yield return`**, for the same heap-field reason as `await`.

<a id="be-b293"></a>

#### Deferred execution and its traps

``` csharp
var query = list.Where(x => x.IsActive);     // nothing has happened
list.Add(newItem);
var result = query.ToList();                  // NOW it runs — includes newItem
```

**Multiple enumeration** is the expensive version:

``` csharp
var q = source.Where(Expensive);
if (q.Any()) foreach (var x in q) { }         // runs the whole pipeline TWICE
```

Against a database, that's two round trips. Against a network stream, it may fail entirely on the second pass. **Materialize once with `ToList()` when you'll use it more than once** — and don't materialize when you'll use it once, because that allocates for nothing.

**Closure capture in loops** was fixed for `foreach` in C# 5 but is still live for `for`:

``` csharp
var actions = new List<Action>();
for (int i = 0; i < 3; i++) actions.Add(() => Console.Write(i));   // prints 3,3,3
for (int i = 0; i < 3; i++) { int copy = i; actions.Add(() => Console.Write(copy)); }  // 0,1,2
```

<a id="be-b294"></a>

#### What LINQ costs

Each operator allocates an enumerator object and adds a delegate invocation per element. For a pipeline over a million items, that's real.

``` csharp
// allocates: Where enumerator, Select enumerator, delegates, result list
var r = items.Where(x => x.Ok).Select(x => x.Name).ToList();

// allocates: the result list
var r = new List<string>(items.Count);
foreach (var x in items) if (x.Ok) r.Add(x.Name);
```

Roughly 2–5× on tight numeric loops. **Which does not mean avoid LINQ** — clarity is worth more than nanoseconds almost everywhere. Rewrite when a profiler points at it, not before.

Specific things worth knowing:

- `Count()` on an `IEnumerable` may enumerate everything; the `Count` *property* on a `List<T>` is O(1). `TryGetNonEnumeratedCount` (.NET 6) checks without enumerating.
- `First()` throws on empty; `FirstOrDefault()` returns `default`, which for a value type is `0` and not obviously "missing". `FirstOrDefault(x => ..., fallback)` (.NET 6) lets you specify.
- `OrderBy` buffers everything — it cannot stream.
- `Any()` short-circuits; `Count() > 0` does not.
- LINQ's `Where(...).First()` is fine; `ToList().First()` materializes the whole set first.

<a id="be-b295"></a>

#### IEnumerable versus IQueryable

Same query syntax, completely different machinery.

|                     | `IEnumerable<T>`                   | `IQueryable<T>`                                    |
|---------------------|------------------------------------|----------------------------------------------------|
| Lambda becomes      | a compiled delegate `Func<T,bool>` | an `Expression<Func<T,bool>>` — a data tree        |
| Where does it run   | in your process                    | wherever the provider sends it (SQL, OData…)       |
| What can it express | any C# at all                      | only what the provider can translate               |
| Failure mode        | none                               | translation exception, or silent client evaluation |

``` csharp
IQueryable<Order> q = db.Orders;

q.Where(o => o.Total > 100)          // -> WHERE Total > 100   (server)
 .AsEnumerable()                     // <-- the boundary
 .Where(o => IsSpecial(o))           // -> runs in memory      (client)
```

> **\[Trap\]**
>
> **The single most expensive LINQ mistake:** crossing the boundary accidentally and pulling a whole table into memory.

``` csharp
// ALL of these force full materialisation before filtering:
db.Orders.ToList().Where(o => o.Total > 100)
db.Orders.AsEnumerable().Where(...)
db.Orders.Where(o => MyHelper(o))          // EF Core 3.0+ THROWS instead - good
foreach (var o in db.Orders) if (...)      // enumerating the DbSet directly

// The type of the variable decides. This is a silent downgrade:
IEnumerable<Order> orders = db.Orders;     // still IQueryable at runtime, but
orders = orders.Where(o => o.Total > 100); // binds to Enumerable.Where -> client-side
```

> **\[Tip\]**
>
> **Rule.** Keep repository return types as `IQueryable<T>` only while you intend composition to reach SQL, and switch to a materialised `IReadOnlyList<T>` the moment you are done. Never type a database query variable as `IEnumerable<T>` "just to be general".

Worked example: [Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)

### Performance

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Performance</p>

<a id="be-b296"></a>

#### The rules that actually matter

**Pre-size collections.** One allocation instead of log₂(n), and no LOH churn on large ones.

**Use concrete types in hot loops.** `foreach` over `List<T>` uses a struct enumerator and allocates nothing; over `IEnumerable<T>` it boxes.

**Avoid boxing.** Generics, constrained generics, and `IEquatable<T>` on structs.

**Reuse buffers.** `ArrayPool<T>` for large or frequent buffers; `stackalloc` for small ones.

**Prefer `Span<T>` for slicing.** `Substring` and `Array.Copy` allocate; slicing a span doesn't.

**Pick the structure by access pattern, not by name.** Repeated `Contains` over a `List` past ~30 items should be a `HashSet`.

**`StringComparer.Ordinal` on string-keyed dictionaries** when keys are machine-facing — faster and culture-independent.

<a id="be-b297"></a>

#### Measuring

``` csharp
[MemoryDiagnoser]
public class Bench
{
    private readonly List<int> _data = Enumerable.Range(0, 1000).ToList();

    [Benchmark(Baseline = true)]
    public int Loop() { int s = 0; foreach (var x in _data) s += x; return s; }

    [Benchmark]
    public int Linq() => _data.Sum();
}
```

`[MemoryDiagnoser]` is the important attribute — for collections work, allocations usually matter more than nanoseconds.

Things that invalidate a benchmark: measuring in `Debug` (no optimizations), letting the JIT eliminate dead code (always return or consume the result), forgetting warmup so you measure JIT compilation, and benchmarking a size that isn't your real size (Fact 3 — the winner changes with n).

------------------------------------------------------------------------

### LINQ

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · LINQ</p>

<a id="be-m138"></a>

#### What LINQ actually compiles to

Query syntax is pure syntactic sugar. The compiler rewrites it into method calls before any type checking of the query happens.

``` csharp
from o in orders
where o.Total > 100
orderby o.Date
select new { o.Id, o.Total }

// becomes exactly:
orders.Where(o => o.Total > 100)
      .OrderBy(o => o.Date)
      .Select(o => new { o.Id, o.Total })
```

The methods themselves are extension methods on `IEnumerable<T>` (in `System.Linq.Enumerable`) or on `IQueryable<T>` (in `System.Linq.Queryable`). Overload resolution picks based on the static type of the source — which is the whole mechanism behind [IEnumerable vs IQueryable](#be-b295).

Each operator returns a small object wrapping the previous one. A three-operator chain builds three objects; nothing runs until enumeration.

``` sql
// Query syntax is worth using for exactly two things:
// 1. joins - much more readable
from o in orders
join c in customers on o.CustomerId equals c.Id
select new { o.Id, c.Name }

// 2. 'let' - naming an intermediate value
from o in orders
let tax = o.Total * 0.1m
where tax > 5
select new { o.Id, tax }
// (note: 'let' compiles to a Select producing a transparent identifier -
//  an extra projection and an extra allocation per element)
```

<a id="be-m139"></a>

#### Deferred execution and multiple enumeration

Most LINQ operators are lazy: calling them builds a pipeline object and runs nothing. The work happens when something enumerates — a `foreach`, a `ToList`, a `Count`, a `First`.

``` csharp
var q = orders.Where(o => { Console.WriteLine("checking"); return o.Total > 0; });
Console.WriteLine("built");     // prints "built" first - nothing has run
var list = q.ToList();          // now "checking" prints n times
```

**Two consequences that cause real bugs.**

*1. Multiple enumeration re-runs everything.*

``` csharp
var expensive = source.Where(Costly).Select(Transform);
if (expensive.Any())                       // pass 1
    foreach (var x in expensive) { }       // pass 2 - all the work again
Console.WriteLine(expensive.Count());      // pass 3
// Fix: materialise once.
var results = source.Where(Costly).Select(Transform).ToList();
```

*2. Captured variables are read at enumeration time, not at definition time.*

``` csharp
int threshold = 10;
var q = items.Where(i => i.Value > threshold);
threshold = 1000;
var r = q.ToList();          // filters on 1000, not 10
```

> **\[Trap\]**
>
> **The worst version of this** is a deferred query over a database context that is disposed before enumeration — you get `ObjectDisposedException` from a line nowhere near the query. Any method returning `IEnumerable<T>` from inside a `using` scope should materialise before returning.

> **\[Tip\]**
>
> Enable analyzer/ReSharper warning "possible multiple enumeration of IEnumerable". It catches this class of bug statically.

Runtime context: [A request, end to end](05-03-async-and-await.md#be-c328)

<a id="be-m141"></a>

#### Streaming vs buffering operators

A **streaming** operator yields each element as it arrives and holds nothing. A **buffering** operator must consume the entire source before it can produce its first result — that is where memory spikes come from.

| Streaming                                  | Buffering (consumes everything first)                     |
|--------------------------------------------|-----------------------------------------------------------|
| `Where`, `Select`, `SelectMany`            | `OrderBy`, `OrderByDescending`, `ThenBy`                  |
| `Take`, `Skip`, `TakeWhile`, `SkipWhile`   | `GroupBy`, `ToLookup`                                     |
| `Concat`, `Zip`, `Chunk`                   | `Reverse`                                                 |
| `Cast`, `OfType`                           | `Join`, `GroupJoin` (buffers the *inner*)                 |
| `Distinct`\* , `Union`\*, `Except`\*       | `ToList`, `ToArray`, `ToDictionary`                       |
| `Any`, `First`, `Contains` (short-circuit) | `Count`, `Sum`, `Average`, `Max` (full pass, O(1) memory) |

\* streams output but accumulates a set of everything seen, so memory grows with distinct values.

``` csharp
// This reads the entire 10M-row file into memory before yielding anything:
File.ReadLines(path).OrderBy(l => l).Take(10);

// This does not:
File.ReadLines(path).Take(10).OrderBy(l => l);

// Order matters enormously. Filter and limit BEFORE you sort or group.
```

> **\[Why it matters\]**
>
> **Why `OrderBy` must buffer:** it cannot know the smallest element until it has seen the last one. Same for `GroupBy` and any group's completeness, and for `Reverse`. There is no way around it in the general case — but you can shrink what it has to buffer.

<a id="be-m142"></a>

#### The operator catalogue by cost

| Operator                                      | Time       | Extra memory | Note                                    |
|-----------------------------------------------|------------|--------------|-----------------------------------------|
| `Where` / `Select`                            | O(n)       | O(1)         | fused when chained on arrays/lists      |
| `SelectMany`                                  | O(total)   | O(1)         | flattens; watch the multiplication      |
| `OrderBy`                                     | O(n log n) | O(n)         | buffers + allocates a key array         |
| `GroupBy` / `ToLookup`                        | O(n)       | O(n)         | builds a full hash lookup               |
| `Join`                                        | O(n+m)     | O(m)         | hash join on the inner sequence         |
| `GroupJoin`                                   | O(n+m)     | O(m)         | left-join shape                         |
| `Distinct` / `Union` / `Except` / `Intersect` | O(n)       | O(distinct)  | internal `HashSet`                      |
| `Contains` / `Any` / `First`                  | O(n) worst | O(1)         | short-circuits                          |
| `Count`                                       | O(1)       | O(1)         | *if* source is `ICollection`; else O(n) |
| `ElementAt`                                   | O(1)       | O(1)         | *if* source is `IList`; else O(n)       |
| `Last`                                        | O(1)       | O(1)         | *if* source is `IList`; else O(n)       |
| `Reverse`                                     | O(n)       | O(n)         | full copy                               |
| `Sum` / `Min` / `Max` / `Average`             | O(n)       | O(1)         | vectorised for primitives in .NET 8+    |

> **\[Tip\]**
>
> **The "if source is" cases matter.** LINQ checks at runtime whether the source implements `ICollection<T>` or `IList<T>` and takes a fast path. Wrapping a list in a `Select` destroys that — `list.Select(x => x).Count()` is O(n), while `list.Count()` is O(1). In .NET 9 many of these fast paths were extended to preserve counts through `Select` and `Skip`/`Take`.

<a id="be-m143"></a>

#### Join, GroupJoin, GroupBy in memory

`Enumerable.Join` is a **hash join**: it fully enumerates the inner sequence into a `Lookup`, then streams the outer, probing per element. So the *inner* sequence should be the smaller one.

``` csharp
// inner (customers) gets buffered into a Lookup keyed by Id
orders.Join(customers, o => o.CustomerId, c => c.Id, (o, c) => new { o, c });
```

`GroupJoin` produces a left-join shape: every outer element, paired with a (possibly empty) sequence of matches. Combined with `SelectMany` and `DefaultIfEmpty`, it is the standard LINQ left join:

``` sql
from o in orders
join c in customers on o.CustomerId equals c.Id into g
from c in g.DefaultIfEmpty()
select new { o.Id, Name = c?.Name ?? "(none)" };
```

`GroupBy` materialises a `Lookup` internally, then yields `IGrouping<K,T>` objects. Each grouping holds a real array of its elements, so the whole source is in memory at once.

> **\[Trap\]**
>
> **The deferred-GroupBy tax.** `GroupBy` is deferred, so enumerating the result twice rebuilds the entire lookup. If you iterate groups more than once — common when you compute a count and then loop — use `ToLookup` or `.ToList()` the groups.

``` csharp
// This scans and hashes the source twice:
var g = items.GroupBy(x => x.Key);
Console.WriteLine(g.Count());
foreach (var grp in g) { }

// Once:
var g = items.ToLookup(x => x.Key);
```

<a id="be-m144"></a>

#### First, Single, Any, Count: the ones people get wrong

| Method              | Empty     | Multiple      | Scans                                 |
|---------------------|-----------|---------------|---------------------------------------|
| `First()`           | throws    | returns first | 1 element                             |
| `FirstOrDefault()`  | `default` | returns first | 1 element                             |
| `Single()`          | throws    | **throws**    | **2 elements** (to verify uniqueness) |
| `SingleOrDefault()` | `default` | throws        | 2 elements                            |
| `Any()`             | `false`   | `true`        | 1 element                             |
| `Count() > 0`       | `false`   | `true`        | **all elements**                      |

``` text
if (query.Count() > 0)   // BAD: counts everything
if (query.Any())         // GOOD: stops at the first hit
```

**Use `Single` deliberately.** It is a runtime assertion that exactly one row matches — valuable when looking up by a primary key, because a duplicate indicates data corruption you want to hear about. It is wasteful when you already know the key is unique and you are in a hot path, and it is wrong when zero matches is a normal case.

> **\[Trap\]**
>
> **Trap on value types.** `FirstOrDefault()` on an `IEnumerable<int>` returns `0` for "not found" — indistinguishable from a real zero. Same for `DateTime` (returns `0001-01-01`) and any struct. Use `.Cast<int?>().FirstOrDefault()`, or since .NET 6 pass an explicit default: `FirstOrDefault(-1)`.

> **\[Tip\]**
>
> **In EF:** `Any()` translates to `EXISTS`, `Count()` to `COUNT(*)`, `Single()` to `TOP 2`, `First()` to `TOP 1`. All four are cheap on the server; the difference is smaller than in memory but still real. See [Any vs Count vs Exists in SQL](07-07-orms-and-ef-core.md#be-m163).

<a id="be-m145"></a>

#### OrderBy: stability, ThenBy, comparers

`OrderBy` buffers the source, extracts all keys into an array, then sorts an *index map* — which is what makes it stable and why it never calls your key selector more than once per element.

``` csharp
items.OrderBy(x => x.Last).ThenBy(x => x.First).ThenByDescending(x => x.Date);
// ThenBy composes into ONE comparison. This is not the same as:
items.OrderBy(x => x.Last).OrderBy(x => x.First);  // WRONG - second sort wins entirely
```

Custom ordering:

``` csharp
names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
items.Order();                    // .NET 7+: sort by the elements themselves
items.OrderDescending();
```

**Cost.** O(n log n) time, O(n) memory for the buffer plus O(n) for the key array. For "the top 10 of a million", sorting everything is wasteful — but LINQ's `OrderBy().Take(n)` has had a partial-sort fast path since .NET Core 2.0, so it is O(n) for small n rather than a full sort. That optimisation does *not* apply if you insert anything between the two.

> **\[Trap\]**
>
> **Trap.** A key selector with side effects or non-determinism (a `Random`, a mutable field, `DateTime.Now`) violates the comparison contract and can throw `InvalidOperationException: IComparer.Compare() method returns inconsistent results`. To shuffle, generate a stable random key per element first, then order by it.

<a id="be-m146"></a>

#### Select vs SelectMany

``` csharp
// Select: 1 in, 1 out. Shape: IEnumerable<IEnumerable<T>> if the selector returns a sequence
orders.Select(o => o.Lines);        // IEnumerable<List<Line>>

// SelectMany: 1 in, many out. Flattens.
orders.SelectMany(o => o.Lines);    // IEnumerable<Line>

// With a result selector, you keep the parent:
orders.SelectMany(o => o.Lines, (o, l) => new { o.Id, l.Sku, l.Qty });
```

In query syntax, a second `from` clause *is* `SelectMany`:

``` sql
from o in orders
from l in o.Lines
select new { o.Id, l.Sku }
```

`Select` has an indexed overload that is easy to forget:

``` csharp
items.Select((x, i) => $"{i}: {x}");
items.Where((x, i) => i % 2 == 0);
items.Index();                     // .NET 9: yields (Index, Item) tuples
```

> **\[Trap\]**
>
> **Cartesian trap.** `SelectMany` over two unrelated sequences produces a cross product: 1,000 × 1,000 is a million rows. In EF this is exactly how an unfiltered join or a multi-`Include` query explodes ([Include, split queries, and cartesian explosion](07-07-orms-and-ef-core.md#be-m159)). Always make sure the inner sequence is *correlated* with the outer element.

<a id="be-m147"></a>

#### Aggregate, and when to write a loop

``` csharp
// Three overloads:
items.Aggregate((acc, x) => acc + x);                       // no seed - throws if empty
items.Aggregate(0, (acc, x) => acc + x);                    // with seed
items.Aggregate(0, (acc, x) => acc + x, acc => acc / 2);    // + result selector
```

`Aggregate` is the general fold. It is also, in most codebases, the point at which a LINQ chain stops being more readable than a loop.

``` csharp
// Clever, and harder to read than the alternative:
var result = items.Aggregate(new StringBuilder(), (sb, x) => sb.Append(x).Append(','));

// Just write it:
var sb = new StringBuilder();
foreach (var x in items) sb.Append(x).Append(',');
```

> **\[Why it matters\]** **The honest guidance.** LINQ is a readability tool with a modest, predictable performance cost (delegate calls that the JIT often cannot inline, plus a few allocations per chain). Use it freely for clarity. Rewrite it as a loop when (a) profiling shows the chain is hot, (b) you need early exit with complex conditions, (c) you are accumulating multiple values at once, or (d) the LINQ version needs a comment to explain it.
>
> A hand-written loop is typically 2–5x faster than an equivalent LINQ chain over an in-memory collection. On a chain that runs 10 times per request, that is nothing. On one that runs a million times, it is everything.

``` csharp
// Accumulating several things at once - a loop is both faster and clearer
int count = 0; decimal total = 0m; decimal max = 0m;
foreach (var o in orders) { count++; total += o.Total; if (o.Total > max) max = o.Total; }
// vs. three separate LINQ passes over the same data
```

<a id="be-m148"></a>

#### LINQ allocation profile

What a single `list.Where(x => x.A > k).Select(x => x.B).ToList()` allocates:

1.  A **display class** for the closure over `k` (one heap object) — unless the lambda captures nothing, in which case the delegate is cached in a static.
2.  Two **delegate** objects (also cached if no capture).
3.  One **iterator object** per operator (LINQ fuses `Where`+`Select` on lists and arrays into a single `WhereSelectListIterator`, so often just one).
4.  The result `List<T>` plus its growth arrays.

``` csharp
// Captures nothing: delegate is cached in a static field, zero allocation per call
items.Where(x => x.IsActive);

// Captures 'k': allocates a display class + delegate on EVERY call
items.Where(x => x.Value > k);

// Avoid the capture by passing state through the operator where available:
items.Where(static x => x.IsActive);   // 'static' lambda: compiler error if it captures
```

> **\[Tip\]**
>
> **Use `static` lambdas liberally.** Marking a lambda `static` costs nothing and turns "did I accidentally capture something?" from a profiling question into a compile error. Especially valuable in hot paths and in expression trees.

> **\[Why it matters\]**
>
> **Perspective.** These are gen-0 allocations that die immediately. In a web request handler doing database I/O, they are unmeasurable. In a per-frame or per-message loop, they add up to real GC pressure. Know which one you are in before optimising.

Foundations: [A worked trace: where everything actually goes](01-05-memory.md#be-m26)

<a id="be-m149"></a>

#### Modern operators worth knowing

``` csharp
// .NET 6
items.Chunk(1000);                    // IEnumerable<T[]> - batching, replaces manual paging
items.DistinctBy(x => x.Email);       // keeps the FIRST of each key
items.MaxBy(x => x.Score);            // the element, not the score
items.MinBy(x => x.Score);
items.ExceptBy(others, x => x.Id);
items.IntersectBy(ids, x => x.Id);
items.UnionBy(others, x => x.Id);
items.TryGetNonEnumeratedCount(out int n);  // O(1) count without enumerating, or false
items.FirstOrDefault(fallbackValue);  // explicit default instead of default(T)
items.ElementAtOrDefault(^1);         // Index/Range support

// .NET 7
items.Order(); items.OrderDescending();

// .NET 9
items.CountBy(x => x.Category);       // IEnumerable<KeyValuePair<K,int>> - one pass
items.AggregateBy(x => x.Cat, 0m, (acc, x) => acc + x.Total);
items.Index();                        // (int Index, T Item) tuples
```

`CountBy` deserves attention: `GroupBy(k).Select(g => new { g.Key, Count = g.Count() })` materialises every group's elements just to count them. `CountBy` does one pass with a single dictionary of counters — dramatically less memory on large sources.

``` csharp
// Batching for bulk DB work - the single most useful of these in practice
foreach (var batch in idsToDelete.Chunk(500))
    await db.Items.Where(i => batch.Contains(i.Id)).ExecuteDeleteAsync();
```

<a id="be-m150"></a>

#### Custom operators with yield return

``` csharp
public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
{
    foreach (var item in source)
        if (item is not null) yield return item;
}
```

`yield return` compiles into a state-machine class implementing both `IEnumerable<T>` and `IEnumerator<T>`. Locals become fields; the method body becomes a `switch` on a state field inside `MoveNext`.

**Argument validation is the classic gotcha.** Nothing in an iterator method runs until the first `MoveNext`, including your null checks:

``` csharp
// BAD: the exception surfaces at enumeration, not at the call site
public static IEnumerable<T> Take2<T>(IEnumerable<T> src) {
    if (src is null) throw new ArgumentNullException(nameof(src));  // deferred!
    foreach (var x in src) yield return x;
}

// GOOD: split into an eager wrapper + a private iterator
public static IEnumerable<T> Take2<T>(IEnumerable<T> src) {
    ArgumentNullException.ThrowIfNull(src);
    return Iterator(src);
    static IEnumerable<T> Iterator(IEnumerable<T> s) { foreach (var x in s) yield return x; }
}
```

> **\[Tip\]**
>
> **Also worth knowing:** a `try/finally` around a `yield return` runs its `finally` when the enumerator is disposed — including on an early `break`. That is how `File.ReadLines` closes its handle. But if a caller abandons the enumerator without disposing (rare, but possible via manual `MoveNext`), the `finally` never runs.

<a id="be-m151"></a>

#### Expression trees: how IQueryable works

When a lambda is assigned to `Expression<Func<...>>` instead of `Func<...>`, the compiler emits *code that builds a data structure describing the lambda*, rather than the lambda's IL.

``` csharp
Func<Order, bool>             f = o => o.Total > 100;   // compiled method
Expression<Func<Order, bool>> e = o => o.Total > 100;   // a tree you can inspect

// e is: Lambda(Body: GreaterThan(
//          MemberAccess(Parameter("o"), Order.Total),
//          Constant(100)))
Console.WriteLine(e.Body.NodeType);   // GreaterThan
```

EF Core walks this tree and emits SQL. That is why an `IQueryable` lambda can only contain constructs the provider understands — a call to your own method is just a `MethodCallExpression` with no SQL equivalent.

``` csharp
// Composing filters dynamically:
Expression<Func<Order, bool>> pred = o => true;
if (minTotal.HasValue) pred = pred.And(o => o.Total >= minTotal);  // via LinqKit / PredicateBuilder

// Or just compose the IQueryable - simpler and provider-friendly:
var q = db.Orders.AsQueryable();
if (minTotal.HasValue) q = q.Where(o => o.Total >= minTotal);
if (status is not null) q = q.Where(o => o.Status == status);
```

> **\[Tip\]**
>
> **Reusable filters that translate.** Put a shared predicate in a `static Expression<Func<T,bool>>` field and apply it with `.Where(Predicate)` — EF can translate that. A `static bool IsActive(Order o)` method cannot be translated. For projections, the same trick works with `Expression<Func<Order, OrderDto>>`, which is how you share a DTO mapping across queries without leaving the server.

> **\[Trap\]**
>
> **Cost.** Building and compiling expression trees at runtime (`e.Compile()`) is slow — hundreds of microseconds. Cache compiled delegates. EF caches query plans keyed by the tree's shape, which is why *parameterising* rather than embedding constants matters ([Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160)).

<a id="be-m152"></a>

#### Rewriting LINQ for speed: concrete patterns

``` csharp
// 1. Nested lookup -> hash set  (O(n*m) -> O(n+m))
list.Where(x => other.Any(y => y.Id == x.Id))
list.Where(x => otherIds.Contains(x.Id))          // otherIds = other.Select(y=>y.Id).ToHashSet()

// 2. Count-then-check -> Any
if (q.Count() > 0)                 =>   if (q.Any())
if (q.Count() == 0)                =>   if (!q.Any())

// 3. Multiple passes -> one pass
var a = items.Count(x => x.Ok); var b = items.Sum(x => x.Total);
// -> single foreach accumulating both

// 4. Filter before sort, before project
items.OrderBy(k).Where(p).Take(10)  =>  items.Where(p).OrderBy(k).Take(10)

// 5. Materialise once
var q = src.Where(p);  q.Count(); foreach (var x in q) {}
// -> var list = src.Where(p).ToList();

// 6. GroupBy-to-count -> CountBy  (.NET 9)
items.GroupBy(k).ToDictionary(g => g.Key, g => g.Count())
items.CountBy(k)

// 7. ToList().Count -> Count()   (avoid materialising just to count)
// 8. ToList() before a foreach you only run once - drop it, unless deferred
//    execution would re-query or the source is a DbContext being disposed
// 9. Contains on a List inside a loop -> HashSet (this one, again, is #1 in practice)
```

> **\[Why it matters\]**
>
> **Ordering of effort.** Fixing an accidental O(n²) is worth thousands of times more than shaving allocations off a correct O(n) chain. Look for nested iteration first, an accidental client-side database evaluation second, redundant passes third, and allocations last.

<a id="be-m154"></a>

#### IAsyncEnumerable and await foreach

Streams results asynchronously without buffering the whole set — the natural shape for reading a large result set or a message stream.

``` csharp
public async IAsyncEnumerable<Order> StreamAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var o in db.Orders.AsAsyncEnumerable().WithCancellation(ct))
        yield return o;
}

await foreach (var order in StreamAsync(ct))
    Process(order);
```

`System.Linq.Async` (the `Ix.NET` package) provides `WhereAwait`, `SelectAwait`, and the rest. EF Core ships its own async terminals: `ToListAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`, `SumAsync`.

> **\[Trap\]**
>
> **Trap.** Streaming from EF holds the `DbDataReader` — and therefore the connection — open for the entire duration of your processing. If you do slow per-row work, you are pinning a pool connection the whole time. Prefer `ToListAsync` for bounded result sets and reserve streaming for genuinely large ones where you also process quickly. Also note: an open reader means you cannot issue another query on the same context concurrently.

> **\[Tip\]**
>
> `ConfigureAwait(false)` has an `await foreach` equivalent: `await foreach (var x in src.ConfigureAwait(false))`. Use it in library code exactly as you would elsewhere.

Worked example: [Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299)

### LINQ, messaging and caching

<p class="src">From <em>Backend Engineering</em> · Practical Answers · LINQ, messaging and caching</p>

<p class="pd">Aggregating data without a second query, the difference between a queue and a bus, and the cache stack from in-process to distributed.</p>

<a id="be-q13"></a>

#### Aggregating with LINQ: in the database or in memory

The first question for any aggregate is *where it runs*. On `IQueryable` it becomes SQL; on `IEnumerable` it runs in your process on data you already paid to transfer. The operators look identical, which is why this is such a common performance mistake.

``` csharp
// ---- in the database: one row comes back ----
var stats = await db.Orders
    .Where(o => o.TenantId == id && o.PlacedAt >= from)
    .GroupBy(o => o.Status)
    .Select(g => new StatusStat(
        g.Key,
        g.Count(),
        g.Sum(o => o.Total),
        g.Average(o => o.Total),
        g.Max(o => o.PlacedAt)))
    .ToListAsync(ct);            // → SELECT Status, COUNT(*), SUM(Total)… GROUP BY

// ---- in memory: only when the set is already loaded and small ----
var byCountry = orders                         // a List<Order>, already fetched
    .GroupBy(o => o.Customer.Country)
    .ToDictionary(g => g.Key, g => g.Sum(o => o.Total));

// ---- Aggregate: the general fold, for when there is no built-in ----
var running = orders
    .OrderBy(o => o.PlacedAt)
    .Aggregate(new List<(DateTime At, decimal Cumulative)>(), (acc, o) =>
    {
        var last = acc.Count > 0 ? acc[^1].Cumulative : 0m;
        acc.Add((o.PlacedAt, last + o.Total));      // running total
        return acc;
    });

// readable alternatives that beat Aggregate most of the time
var total   = orders.Sum(o => o.Total);
var busiest = orders.MaxBy(o => o.Total);           // .NET 6+
var byDay   = orders.CountBy(o => o.PlacedAt.Date); // .NET 9+
var top3    = orders.OrderByDescending(o => o.Total).Take(3);
```

``` csharp
// the trap that produces a silent full-table scan
var big = await db.Orders
    .AsEnumerable()                     // ← everything crosses the wire HERE
    .Where(o => Score(o) > 0.5)         // C# method: cannot be translated
    .SumAsync(o => o.Total);

// fix: filter in the database first, compute in memory second
var candidates = await db.Orders
    .Where(o => o.TenantId == id && o.Total > 100)   // translated
    .Select(o => new { o.Id, o.Total, o.Metrics })   // narrow projection
    .ToListAsync(ct);
var scored = candidates.Where(o => Score(o) > 0.5).Sum(o => o.Total);
```

> **\[Trap\]**
>
> **Common trap.** `Count()` on an `IEnumerable` enumerates the whole sequence — calling it inside a loop is quadratic. And `GroupBy` in EF Core only translates when the selector projects to aggregates; group by a computed expression and EF silently evaluates the grouping client-side after pulling every row (older versions did this without warning — newer ones throw, which is kinder).
