---
id: "3-3"
number: "3.3"
title: "Hash tables"
part: 3
part_title: "Data structures and algorithms"
words: 6429
minutes: 28
---

# 3.3 Hash tables

> **Quick look.** Hash tables find items by computing where they should be, which is why Dictionary and HashSet are so fast.
>
> - A key's hash picks a bucket and only that bucket is searched: near O(1) on average.
> - Equal keys must have equal hash codes, and keys must never change while stored.
> - String hash codes are randomized per process: never persist GetHashCode results; use a stable hash instead.
> - Pass an explicit comparer for string keys; use TryGetValue and TryAdd to avoid double lookups.
> - hash(key) % N remaps most keys when N changes; consistent hashing avoids that.
> - Turning a List.Contains inside a loop into a HashSet lookup is the single highest-value refactor in many codebases.


### The big picture

Finding something in a list means checking items one by one. A hash table finds it almost instantly, however many items it holds, by computing where the item should be instead of searching for it.

`Dictionary<TKey, TValue>` and `HashSet<T>` are hash tables, among the most used structures in all of programming. This chapter shows how they work inside by building a small one, why their speed depends on a good hash function, how attackers can exploit a bad one, and the everyday habits that keep dictionaries fast and correct.

### Before you start

**Builds on:** 2.4 Values, variables and references.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Comparer** (3.7): An object or function that decides the order of two items.
- **Binary search** (3.7): Finding a value in sorted data by repeatedly halving the range. O(log n).
- **HTTP request** (6.6): A message from client to server: method, path, headers and an optional body.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, LINQ, null operators, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. How does a hash table find a key without searching?**

> It runs the key through a hash function to get a number, and uses that number to pick a bucket. Only the few entries in that bucket need comparing. On average that is O(1).

**2. What is a collision, and how does .NET's Dictionary handle it?**

> Two keys landing in the same bucket. .NET's `Dictionary` keeps a chain of entries per bucket and compares keys along the chain. Other designs, called open addressing, probe for another free slot instead.

**3. Why is "LHR".GetHashCode() different every time you run your program?**

> .NET randomizes string hashing per process. It stops attackers from sending thousands of keys that they know will collide, which would slow a server's dictionaries to O(n) per operation.

**4. Can you store string hash codes in a database to use later?**

> No. They change between processes and between .NET versions. For anything saved or shared between machines, use a stable hash such as SHA-256 or xxHash.

**5. How do you make a Dictionary treat "LHR" and "lhr" as the same key?**

> Pass a comparer when creating it: `new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)`. The comparer supplies both the hash code and the equality check.

**6. Why is if (d.ContainsKey(k)) x = d[k]; wasteful?**

> It hashes and searches twice. `d.TryGetValue(k, out var x)` does one lookup.

**7. Is the iteration order of a Dictionary guaranteed?**

> No. It usually follows insertion order while nothing has been removed, but that is an accident of the implementation. Use a sorted structure, or sort when enumerating, if order matters.

**8. What does a Bloom filter tell you?**

> Either "definitely not present" or "possibly present". It uses very little memory and never gives a false "not present", so it is used to skip expensive lookups for keys that don't exist.

### Lookup without searching

Finding a value in a list means checking items one by one: O(n). Sorted data allows binary search: O(log n). A **hash table** does better still. On average it finds a key in O(1), however many keys it holds.

The trick is to compute where a key should be, instead of searching for it.

### Hash functions and buckets

A hash table keeps an array of slots called **buckets**. To store a key, it runs the key through a **hash function**, which turns it into a number. The number, taken modulo the number of buckets, picks a bucket. The key and its value go there. To look the key up later, it computes the same hash and goes straight to the same bucket.

A good hash function has three properties. Equal keys always give equal hashes. Different keys usually give different hashes, spread evenly over the whole range. And it is fast.

In .NET, the hash function is a type's `GetHashCode` method, and equality is its `Equals` method. They must agree, as Values, variables and references (2.4) explained: equal objects must have equal hash codes.

### Collisions

There are far more possible keys than buckets, so different keys sometimes land in the same bucket. That is a **collision**. Every hash table needs a way to handle it.

**Chaining** keeps a small list of entries per bucket. A lookup goes to the bucket and compares keys along its chain. .NET's `Dictionary` works this way, but stores all entries in one array and links them by index rather than by separate list objects, which keeps memory compact.

**Open addressing** keeps every entry directly in the bucket array. If a slot is taken, it probes the next slot, or one chosen by a second hash, until it finds a free one. Python's `dict` and many high-performance C++ tables use open addressing.

Either way, lookups stay fast only while chains or probe sequences stay short.

### Load factor and resizing

The **load factor** is the number of entries divided by the number of buckets. As it rises, collisions become more common and lookups slow down.

So hash tables grow. When the load factor passes a limit, the table allocates a bigger bucket array, about twice the size, and reinserts every entry at its new position. This is **rehashing**. Like `List<T>` growth, it is expensive once in a while and O(1) amortized. .NET's `Dictionary` uses prime-numbered sizes, which spread weak hash values more evenly. If you know how many entries you will add, pass the capacity to the constructor to skip the intermediate resizes.

### When O(1) becomes O(n)

A hash table's O(1) is an average that depends on the hash function spreading keys well. If many keys share a hash code, they all pile into one bucket and each operation scans a long chain. At worst, every key collides, and the table becomes a slow linked list.

That happens by accident with a poor `GetHashCode`, for example one that returns a constant or uses only a field that is the same for most objects.

It can also happen on purpose. In 2011, researchers showed that most web frameworks could be brought down by an HTTP request containing thousands of form fields whose names were chosen to collide. The server spent minutes building one dictionary. This is **hash flooding**. Since .NET Core, string hashing is randomized: each process picks a secret random seed, so attackers can't predict which strings collide.

```csharp
// hashing.cs — run with: dotnet run -c Release hashing.cs
using System.Collections.Frozen;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

// 1. A tiny hash table with chaining
var map = new ChainedMap<string, string>(bucketCount: 8);
map.Set("LHR", "London Heathrow");
map.Set("JFK", "New York JFK");
map.Set("SIN", "Singapore Changi");
Console.WriteLine(map.Get("JFK"));                  // New York JFK
Console.WriteLine(map.Get("CDG") ?? "not found");   // not found

// 2. String hash codes change between runs
Console.WriteLine("LHR".GetHashCode());             // a different number every time you run this

// 3. The comparer decides what "same key" means
var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LHR" };
Console.WriteLine(codes.Contains("lhr"));           // True

// 4. Count with one lookup per word instead of two
var counts = new Dictionary<string, int>();
foreach (string word in "delayed on time delayed cancelled delayed".Split(' '))
    CollectionsMarshal.GetValueRefOrAddDefault(counts, word, out _)++;
Console.WriteLine(string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
// delayed=3, on=1, time=1, cancelled=1

// 5. A bad hash function turns O(1) into O(n)
Console.WriteLine($"good hash: {Fill(20_000, i => new GoodKey(i))} ms");
Console.WriteLine($"bad hash:  {Fill(20_000, i => new BadKey(i))} ms");   // hundreds of times slower

// 6. Frozen: built once, tuned for reading
FrozenDictionary<string, string> airports = new Dictionary<string, string>
{
    ["LHR"] = "London Heathrow",
    ["JFK"] = "New York JFK",
}.ToFrozenDictionary();
Console.WriteLine(airports["LHR"]);                 // London Heathrow

static long Fill<T>(int n, Func<int, T> make) where T : notnull
{
    var sw = Stopwatch.StartNew();
    var set = new HashSet<T>();
    for (int i = 0; i < n; i++) set.Add(make(i));
    return sw.ElapsedMilliseconds;
}

readonly record struct GoodKey(int Id);

readonly record struct BadKey(int Id)
{
    public override int GetHashCode() => 42;        // every key collides
}

sealed class ChainedMap<TKey, TValue>(int bucketCount) where TKey : notnull
{
    readonly List<(TKey Key, TValue Value)>?[] buckets = new List<(TKey, TValue)>?[bucketCount];

    int BucketFor(TKey key) => (int)((uint)key.GetHashCode() % (uint)buckets.Length);

    public void Set(TKey key, TValue value)
    {
        var bucket = buckets[BucketFor(key)] ??= new();
        for (int i = 0; i < bucket.Count; i++)
            if (EqualityComparer<TKey>.Default.Equals(bucket[i].Key, key)) { bucket[i] = (key, value); return; }
        bucket.Add((key, value));                   // new key: add to this bucket's chain
    }

    public TValue? Get(TKey key)
    {
        var bucket = buckets[BucketFor(key)];
        if (bucket is null) return default;
        foreach (var (k, v) in bucket)
            if (EqualityComparer<TKey>.Default.Equals(k, key)) return v;
        return default;
    }
}
```

The toy `ChainedMap` never resizes. Try the exercise at the end that adds rehashing.

### Hash codes are not stable

Because of randomization, a string's hash code is different in every process. Hash codes of other types can change between .NET versions too. Never store them in a database, send them to another machine, or use them to decide which server or file a key belongs to.

For those jobs, use a **stable hash**: a function defined to give the same result everywhere, forever. SHA-256 is stable and hard to attack, and slower. xxHash, in the `System.IO.Hashing` package, is stable and very fast, but not designed against attackers.

### Equality comparers

A hash table needs two operations on keys: a hash code and an equality check. An **equality comparer**, `IEqualityComparer<T>`, supplies both. Passing one to a `Dictionary` or `HashSet` changes what counts as the same key.

`StringComparer.OrdinalIgnoreCase` makes "LHR" and "lhr" one key. You can write your own comparer to match objects by an id, or to compare only some fields, without changing the type itself.

### Dictionary and HashSet in practice

`Dictionary<TKey, TValue>` maps keys to values. `HashSet<T>` stores keys only and answers "is this in the set?". `HashSet<T>` also has fast set operations: `UnionWith`, `IntersectWith`, `ExceptWith` and `IsSubsetOf`.

A few habits make them faster and safer. Use `TryGetValue` instead of `ContainsKey` followed by the indexer, which searches twice. Use `TryAdd` when a key may already be there; `Add` throws on duplicates. `CollectionsMarshal.GetValueRefOrAddDefault` gives a reference to the value slot, adding it if needed, so counting or accumulating needs one lookup. Don't rely on iteration order.

Only use keys that never change while stored. A key that changes after being added lands in the wrong bucket and can no longer be found.

For multiple threads, `ConcurrentDictionary<TKey, TValue>` allows safe updates without a global lock, as covered in Threads and locks (5.2).

### Frozen collections

Many dictionaries are built once and then only read: configuration, lookup tables, airport codes. .NET 8 added `FrozenDictionary<TKey, TValue>` and `FrozenSet<T>` for this. Creating one is slower, because it analyses the keys and picks the fastest layout and hashing strategy for exactly that set. After that, lookups are faster than a normal `Dictionary`. A **frozen collection** can never be changed.

### Sorted alternatives

A hash table has no order. When you need keys in order, or all keys between two values, use a tree-based structure such as `SortedDictionary<TKey, TValue>` or `SortedSet<T>`. Their operations are O(log n) instead of O(1), and they keep keys sorted at all times, as Trees (3.4) explains.

### Bloom filters

Sometimes you need to know quickly whether a key might exist before doing an expensive lookup, such as a disk read or a network call. A **Bloom filter** answers this with very little memory.

It is an array of bits, all starting at zero, plus several hash functions. Adding a key sets the bit chosen by each hash function. Checking a key looks at those same bits. If any bit is zero, the key was definitely never added. If all are one, the key was probably added, but other keys might have set those bits.

So a Bloom filter can give false "maybe" answers, never false "no" answers. A filter with a 1% false-positive rate needs about 10 bits per key, whatever the size of the keys. Databases use them to skip reading files that can't contain a key.

### Spreading keys across machines

The same idea of hashing a key to pick a slot works across machines: hash the key to pick which server stores it. Plain modulo has a problem. Adding one server to ten changes the result of `hash % servers` for most keys, and almost everything must move.

**Consistent hashing** fixes this. Servers and keys are both hashed onto a circle, and each key belongs to the next server along the circle. Adding a server moves only the keys between it and its neighbour. Distributed caches and databases use this, as Distributed systems basics (12.2) covers.

### In short

A hash table turns a key into a number, uses it to pick a bucket and searches only that bucket, giving near-instant lookups on average. Collisions are normal; too many make it slow. Equal keys must have equal hash codes, and keys must never change while stored. String hash codes change between runs, so never save them. Choose comparers deliberately, use `TryGetValue` and `TryAdd`, and use frozen collections for data built once and read often.

### What goes wrong

**Mutable keys.** A key's fields change after insertion, and the entry can't be found or removed. Use immutable key types.

**Poor `GetHashCode`.** A hash based on a field that is mostly the same, or a constant, makes every operation scan a long chain. Use `HashCode.Combine` over the fields used by `Equals`, or use records.

**Persisting hash codes.** Code that stores `GetHashCode()` results, or uses them to choose shards, breaks after a restart or upgrade. Use a stable hash.

**Culture-sensitive or case-sensitive keys by accident.** User-entered codes like "lhr" miss the "LHR" entry. Choose the comparer deliberately, usually an ordinal one.

**Depending on enumeration order.** Output order changes after a remove, a resize or a .NET upgrade, breaking tests or reports. Sort explicitly.

**Double lookups.** `ContainsKey` then the indexer, or `ContainsKey` then `Add`, costs two searches. Use `TryGetValue` and `TryAdd`.

**Unbounded dictionaries as caches.** A static dictionary used as a cache grows until the process runs out of memory. Use a cache with limits, as covered in Caching (8.6).

### Cost model

A `Dictionary` lookup with a good hash typically takes 10 to 30 nanoseconds for small keys, most of it hashing the key and one or two cache misses. Hashing a string costs time proportional to its length. Each entry costs roughly 24 bytes or more of overhead plus the key and value, so a dictionary uses several times the memory of an array of the same data. Rehashing briefly doubles memory use while both arrays exist. A frozen dictionary is typically faster to read than a normal one and much slower to create.

### In other languages (optional)

**JavaScript and TypeScript.** `Map` and `Set` are hash tables that keep insertion order, as the language guarantees. Plain objects work as string-keyed maps, but inherit properties from their prototype; `Object.create(null)` or `Map` avoids surprises. Object keys in a `Map` compare by identity.

**Python.** `dict` and `set` are open-addressing hash tables. Since Python 3.7, `dict` guarantees insertion order. String hashing is randomized per process, like .NET. Keys must be hashable, which rules out lists.

**Java.** `HashMap` switches a bucket from a linked list to a small balanced tree when it gets long, which limits the damage of collisions.

**SQL.** Hash joins and hash aggregates in query plans build hash tables in memory. Some databases offer hash indexes for exact-match lookups, while ordinary indexes are B-trees that also support ranges.

### Key terms

- **Hash table**: A structure that stores entries in buckets chosen by hashing their keys, giving O(1) lookups on average.
- **Hash function**: A function that turns a key into a number, giving equal keys equal numbers and spreading different keys evenly.
- **Bucket**: One slot of a hash table's array, chosen by a key's hash.
- **Collision**: Two different keys landing in the same bucket.
- **Chaining**: Handling collisions by keeping a chain of entries in each bucket.
- **Open addressing**: Handling collisions by probing for another free slot in the same array.
- **Load factor**: The number of entries divided by the number of buckets.
- **Rehashing**: Growing a hash table and reinserting every entry at its new position.
- **Hash flooding**: An attack that sends many keys chosen to collide, making hash tables slow.
- **Stable hash**: A hash function defined to give the same result on every machine and every run.
- **Equality comparer**: An object supplying the hash code and equality check a collection uses for keys.
- **Frozen collection**: A read-only set or dictionary, optimized for its exact keys when it is created.
- **Bloom filter**: A compact bit array that says a key is definitely absent or possibly present.
- **Consistent hashing**: Placing keys and servers on a circle so that adding a server moves only a few keys.

### Where this leads

Ordered alternatives to hash tables are in Trees (3.4). Hash tables inside query plans appear in Indexes and query plans (7.3). Key-value databases, which are hash tables across machines, are in Non-relational stores (7.6). Caches with limits and expiry are in Caching (8.6). Cryptographic hashes, which are built for different goals, are in Secrets and cryptography basics (10.5).

### Check yourself

1. Add rehashing to `ChainedMap`: track the count, and when it exceeds the bucket count, double the buckets and reinsert everything.
2. Add a `Count` of the longest chain to `ChainedMap`. Insert 1,000 keys with 8 buckets and without rehashing, and look at how long the chains get.
3. Write an `IEqualityComparer<Flight>` that treats two flights as equal if they have the same number and date. Use it in a `HashSet<Flight>`.
4. Rewrite a `ContainsKey` followed by the indexer in real code as `TryGetValue`.
5. Run `hashing.cs` three times and compare the hash codes printed. Then explain why a stable hash is needed to split data across servers.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A dictionary key that changed after insertion

A cache keyed by a `RouteKey` class can't find entries after someone updates the key's `Cabin` field.

*Make keys immutable values*

```csharp
// Before: class RouteKey { public string From; public string To; public string Cabin; }  (mutable, reference equality)
public readonly record struct RouteKey(string From, string To, string Cabin);   // value equality + correct GetHashCode

var fares = new Dictionary<RouteKey, decimal> { [new("LHR", "JFK", "Y")] = 420m };
fares.TryGetValue(new("LHR", "JFK", "Y"), out var f);   // found: equal values, equal hash
```

> **Lesson:** Keys must be immutable and implement equality and hashing over the same fields. Records do both for you.

#### Example 2: Persisted hash codes that changed after restart

A sharding scheme stores `name.GetHashCode() % 16` in the database. After a restart, rows map to different shards.

**What goes wrong.** String hash codes are randomized per process.

*Use a stable hash*

```csharp
using System.IO.Hashing;   // System.IO.Hashing package

static int ShardOf(string key, int shards)
{
    var hash = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(key));   // same on every machine and run
    return (int)(hash % (ulong)shards);
}
```

> **Lesson:** GetHashCode is only for in-memory tables. Anything persisted or shared between processes needs a stable hash.

### The Collections

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Collections</p>

<a id="be-b277"></a>

#### Dictionary<K,V>

The most important collection to actually understand. Two arrays:

``` text
buckets: int[]        — index into entries, +1 (0 means empty)
entries: Entry[]      — { int hashCode; int next; TKey key; TValue value; }
```

Lookup:

1.  `hash = comparer.GetHashCode(key) & 0x7FFFFFFF`
2.  `bucket = buckets[hash % size]`
3.  Walk the chain via `entry.next`, comparing `hashCode` first (cheap) then `key`
4.  Found, or chain ends

Chaining is by **index into the entries array**, not by pointer — so the whole chain lives in one contiguous array and stays cache-friendly. This is why .NET's dictionary outperforms naive pointer-chained hash tables.

What follows from the implementation:

- **Capacity is prime-sized** and grows to the next prime past double. Pre-size with `new Dictionary<K,V>(count)`.
- **Order is not defined.** It usually looks like insertion order until you remove something — removed slots go on a free list and get reused out of order. Never rely on it.
- **A bad `GetHashCode` degrades lookup to O(n).** If everything hashes to one bucket, you've built a linked list.
- **String keys get randomized hashing** if a bucket's collision count exceeds a threshold, defending against hash-flooding DoS. This is why you should pass `StringComparer.Ordinal` explicitly when keys are trusted and machine-generated — it's faster.
- `TryGetValue` does one lookup; `ContainsKey` followed by `[key]` does two.
- `CollectionsMarshal.GetValueRefOrAddDefault` gives a `ref` to the value slot for update-in-place without a second lookup.

``` csharp
// idiomatic, one lookup
if (dict.TryGetValue(key, out var v)) { }

// increment without two lookups
ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
count++;
```

<a id="be-b278"></a>

#### HashSet<T>

A dictionary without values. Same mechanics, same caveats.

`Add` returns `bool` — `true` if it was new. That's often exactly the deduplication check you want, in one operation.

Set operations mutate in place: `UnionWith`, `IntersectWith`, `ExceptWith`, `SymmetricExceptWith`, plus non-mutating `IsSubsetOf`, `Overlaps`. `IntersectWith` against another `HashSet` with the same comparer is optimized; against an arbitrary `IEnumerable` it isn't.

### Implementing Them Yourself

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Implementing Them Yourself</p>

<p class="pd">Understanding is easier from the inside. These are simplified but faithful.</p>

<a id="be-b285"></a>

#### Hash table

``` csharp
public class HashMap<TKey, TValue> where TKey : notnull
{
    private struct Entry { public int Hash; public int Next; public TKey Key; public TValue Value; }

    private int[] _buckets;          // 1-based index into _entries; 0 = empty
    private Entry[] _entries;
    private int _count;

    public HashMap(int capacity = 8)
    {
        _buckets = new int[capacity];
        _entries = new Entry[capacity];
    }

    private int BucketOf(int hash) => (int)((uint)hash % (uint)_buckets.Length);

    public bool TryGetValue(TKey key, out TValue value)
    {
        int hash = key.GetHashCode() & 0x7FFFFFFF;
        for (int i = _buckets[BucketOf(hash)] - 1; i >= 0; i = _entries[i].Next)
        {
            // compare the cached hash first — cheap reject before the real comparison
            if (_entries[i].Hash == hash && EqualityComparer<TKey>.Default.Equals(_entries[i].Key, key))
            {
                value = _entries[i].Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (TryGetValue(key, out _)) { Update(key, value); return; }
        if (_count == _entries.Length) Resize();

        int hash = key.GetHashCode() & 0x7FFFFFFF;
        int b = BucketOf(hash);
        _entries[_count] = new Entry { Hash = hash, Next = _buckets[b] - 1, Key = key, Value = value };
        _buckets[b] = _count + 1;      // 1-based so that 0 can mean "empty"
        _count++;
    }

    private void Resize()
    {
        var entries = new Entry[_entries.Length * 2];
        Array.Copy(_entries, entries, _count);
        _buckets = new int[entries.Length];
        for (int i = 0; i < _count; i++)          // rehash everything
        {
            int b = BucketOf(entries[i].Hash);
            entries[i].Next = _buckets[b] - 1;
            _buckets[b] = i + 1;
        }
        _entries = entries;
    }
}
```

The three ideas that make this fast: **entries live in one flat array** so chains stay cache-local; **the hash is cached** in the entry so a mismatch rejects without calling `Equals`; and **buckets are 1-based** so a zeroed array already means empty.

**Chaining versus open addressing.** This is chaining: collisions form a chain of entries linked by index. The alternative, **open addressing**, stores everything directly in one array — on collision it *probes* for another free slot.

- **Linear probing** — try `i+1`, `i+2`, … Excellent cache behaviour (the next slot is usually in the same cache line) but prone to **clustering**: occupied runs grow and merge, and every probe through a cluster is wasted work.
- **Quadratic probing** — try `i+1`, `i+4`, `i+9`, … Breaks up clusters, worse locality.
- **Robin Hood hashing** — on insert, if the key you're placing has probed further from its ideal slot than the key currently sitting there, swap them. It "steals from the rich to give to the poor," which equalizes probe distances and dramatically shortens the worst case. Used by Rust's `HashMap`.

**Deletion is what makes open addressing hard.** You cannot just empty a slot: a probe sequence that passed through it would now stop early and report "not found" for a key that's still present further along. So you write a **tombstone** — a marker meaning "empty, but keep probing." Tombstones accumulate, degrade lookups, and require periodic rebuilds to clear.

**Load factor** is occupancy: entries ÷ capacity. Both schemes degrade as it rises, but open addressing degrades *catastrophically* past ~80%, because probe sequences lengthen without bound as free slots vanish. Chaining degrades gracefully — chains just get longer. .NET resizes at a load factor of 1.0, which is only viable because it chains.

.NET chose chaining; Rust and Swift chose open addressing. Both are defensible: chaining is predictable and delete-friendly, open addressing is more compact and cache-efficient when kept sparse.

**Why prime capacities.** The bucket index is `hash % capacity`. If capacity is a power of two, `% capacity` keeps only the low bits — so any hash function whose low bits are poorly distributed collides badly. A classic case: hash codes that are multiples of 4 (common when hashing pointer-like or aligned values) map to only a quarter of the buckets with a power-of-two capacity.

A prime capacity mixes in the high bits too, so a mediocre `GetHashCode` still spreads. The cost is that `%` by a non-constant is a division, which is slow — modern .NET works around this with `fastmod`, computing the remainder via multiplication instead. Implementations that use power-of-two capacities (so they can mask instead of divide) compensate by scrambling the hash first, which is what Java's `HashMap` does.

### Collections

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Collections</p>

<a id="be-m100"></a>

#### Dictionary: buckets, entries, collisions

Not a classic bucket-of-linked-nodes design. .NET's `Dictionary<K,V>` uses two arrays:

- `int[] buckets` — index into `entries`, offset by one so 0 means empty.
- `Entry[] entries` — each entry is `{ int hashCode; int next; TKey key; TValue value; }`, appended in insertion order.

Collisions chain through the `next` index inside the same `entries` array — no per-node allocation, and good cache behaviour.

``` text
// Lookup:
// 1. hash = comparer.GetHashCode(key) & 0x7FFFFFFF
// 2. bucket = hash % buckets.Length     (or a fast-mod multiply on 64-bit)
// 3. walk the chain, comparing cached hashCode FIRST (cheap int compare),
//    only calling Equals when the hashes match
```

Because entries are appended, enumeration order happens to be insertion order *until you remove something* — removed slots go on a free list and get reused. **Never depend on dictionary order.**

``` csharp
var d = new Dictionary<string, int>(capacity: 1000, StringComparer.Ordinal);

// One lookup instead of two or three:
if (d.TryGetValue(k, out var v)) { }                    // instead of ContainsKey + [k]
ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(d, k, out bool existed);
slot++;                                                  // in-place increment, 1 hash
```

> **\[Tip\]**
>
> **Always pass a comparer for string keys.** The default for `string` is a randomised, collision-resistant comparer. `StringComparer.Ordinal` is measurably faster and is what you want unless the keys are attacker-controlled.

<a id="be-m101"></a>

#### Hash codes: what makes a good one

A hash function's job is to spread keys evenly across buckets. A bad one degrades a dictionary from O(1) to O(n) without any error message — you just see the CPU graph rise.

``` csharp
// Bad: every Point on a diagonal collides
public override int GetHashCode() => X ^ Y;

// Bad: constant - technically correct, catastrophically slow
public override int GetHashCode() => 0;

// Good:
public override int GetHashCode() => HashCode.Combine(X, Y);

// For a variable number of fields:
var hc = new HashCode();
foreach (var item in items) hc.Add(item);
return hc.ToHashCode();
```

`HashCode.Combine` uses xxHash32 with a **per-process random seed**. Two consequences:

- Hash values differ between runs. Never persist a `GetHashCode()` result, never send it over the wire, never use it as a cache key across processes or as a database value. Use a stable hash you control (e.g. an explicit FNV-1a or a cryptographic hash) for those.
- It defends against hash-flooding DoS, where an attacker sends keys that all collide.

> **\[Why it matters\]**
>
> **String hashing.** `string.GetHashCode()` is randomised per process for the same reason. `string.GetHashCode(ReadOnlySpan<char>, StringComparison)` lets you hash a slice without allocating a substring — useful for keyed lookups over a parsed buffer.

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

<a id="be-m102"></a>

#### HashSet and set operations

Same machinery as `Dictionary` without the value field. `Add`, `Remove`, `Contains` are O(1) average.

``` csharp
var seen = new HashSet<int>();
if (!seen.Add(id) ) { /* duplicate - Add returns false, no second lookup needed */ }

a.UnionWith(b);         // a = a ∪ b, in place
a.IntersectWith(b);     // a = a ∩ b
a.ExceptWith(b);        // a = a \ b
a.SymmetricExceptWith(b);
a.IsSubsetOf(b); a.Overlaps(b); a.SetEquals(b);
```

These mutate in place and are O(n+m); the LINQ equivalents (`Union`, `Intersect`, `Except`) are deferred, non-mutating, and build a new set internally.

> **\[Tip\]**
>
> **The single highest-value refactor in most codebases.** Replacing `list.Contains(x)` inside a loop with a `HashSet` lookup turns O(n·m) into O(n+m). If you see a nested loop over two collections, or `.Any(x => x.Id == y.Id)` inside a `foreach`, this is the fix.

``` csharp
// Before: 10,000 x 10,000 = 100M comparisons
var missing = all.Where(x => !existing.Any(e => e.Id == x.Id)).ToList();

// After: 20,000 operations
var existingIds = existing.Select(e => e.Id).ToHashSet();
var missing = all.Where(x => !existingIds.Contains(x.Id)).ToList();
```

<a id="be-m105"></a>

#### Lookup and ILookup

A `Lookup` is an immutable one-to-many dictionary: a key maps to a *sequence*, and missing keys return an empty sequence rather than throwing.

``` csharp
ILookup<int, Order> byCustomer = orders.ToLookup(o => o.CustomerId);

foreach (var o in byCustomer[999]) { }   // no exception if 999 is absent - empty
Console.WriteLine(byCustomer.Contains(5));
Console.WriteLine(byCustomer.Count);      // number of distinct keys
```

Compare the three ways to group:

| API                                | Execution                          | Missing key    | Mutable |
|------------------------------------|------------------------------------|----------------|---------|
| `GroupBy`                          | deferred, re-runs each enumeration | absent         | no      |
| `ToLookup`                         | **immediate**, materialised once   | empty sequence | no      |
| `ToDictionary(k, g => g.ToList())` | immediate                          | *throws*       | yes     |

> **\[Tip\]**
>
> **Use it for join-in-memory.** When you have loaded two sets and need to correlate them, `ToLookup` on the "many" side and a lookup per item on the "one" side is clearer and faster than a nested `Where`, and it handles the zero-match case without a null check.

### Identity, randomness, and hashing

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Identity, randomness, and hashing</p>

<a id="be-m75"></a>

#### Non-cryptographic hashing

`GetHashCode` is for in-memory hash tables only: it is 32 bits, randomised per process, and unspecified across versions ([Hash codes: what makes a good one](#be-m101)). When you need a hash that is *stable* — across processes, machines, restarts, or persisted to disk — use a real algorithm.

``` csharp
// System.IO.Hashing - fast, stable, non-cryptographic
using System.IO.Hashing;

ulong h  = XxHash64.HashToUInt64(bytes);              // ~10 GB/s, excellent distribution
byte[] a = XxHash128.Hash(bytes);                     // 128-bit, negligible collision risk
uint crc = Crc32.HashToUInt32(bytes);                 // checksums, not distribution
ulong s  = XxHash3.HashToUInt64(bytes);               // fastest for short inputs

// Streaming, for data larger than memory:
var hasher = new XxHash64();
await foreach (var chunk in stream) hasher.Append(chunk.Span);
ulong result = hasher.GetCurrentHashAsUInt64();
```

| Need                            | Use                                      | Not                                                 |
|---------------------------------|------------------------------------------|-----------------------------------------------------|
| in-memory dictionary key        | `HashCode.Combine`                       | anything persisted                                  |
| shard/partition assignment      | `XxHash64`                               | `GetHashCode` — differs per process                 |
| cache key from a complex object | `XxHash128` or SHA-256 of canonical JSON | `GetHashCode`                                       |
| detecting accidental corruption | `Crc32`                                  | anything, if the threat is malicious                |
| content identity / dedup        | SHA-256                                  | MD5, SHA-1 — both broken                            |
| password storage                | PBKDF2 / Argon2                          | any fast hash ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)) |
| message authentication          | HMAC-SHA256                              | a bare hash of key+message                          |

> **\[Trap\]**
>
> **Non-cryptographic means "an attacker can construct collisions".** xxHash is superb for distribution and useless as a security primitive. If an adversary chooses the input — a filename, an ETag, an integrity check on an upload — you need SHA-256 or HMAC, not a checksum. Conversely, using SHA-256 to pick a shard wastes a hundred times the CPU for no benefit.

<a id="be-m76"></a>

#### Checksums and content addressing

Two distinct jobs that both produce a hash: detecting that bytes changed, and *naming* bytes by their content.

``` csharp
// Content addressing: the identifier IS the hash. Same bytes -> same name,
// forever, on any machine. Deduplication becomes free.
static string ContentId(ReadOnlySpan<byte> data) =>
    Convert.ToHexStringLower(SHA256.HashData(data));

// Upload: if the blob already exists, skip the transfer entirely.
var id = ContentId(fileBytes);
if (!await _blobs.ExistsAsync(id)) await _blobs.PutAsync(id, fileBytes);
await _db.Attachments.AddAsync(new Attachment { OrderId = orderId, BlobId = id });
// Two users uploading the same 20 MB PDF store one copy and one extra row.
```

``` csharp
// Integrity across a boundary: verify what you received is what was sent.
await using var fs = File.OpenRead(path);
byte[] actual = await SHA256.HashDataAsync(fs, ct);
if (!CryptographicOperations.FixedTimeEquals(actual, expected))
    throw new InvalidDataException("Checksum mismatch");
// FixedTimeEquals, not SequenceEqual - a byte-by-byte compare leaks timing
// information when the expected value is a secret (§227).
```

``` csharp
// Change detection without comparing whole rows - a cheap ETag or sync marker:
var version = XxHash64.HashToUInt64(MemoryMarshal.AsBytes(stackalloc long[] {
    order.Id, order.Total.GetHashCode(), order.CreatedAt.Ticks }));
// For HTTP, prefer a rowversion column: the database maintains it,
// it is monotonic, and it costs nothing to read (§169).
```

> **\[Tip\]**
>
> **Content addressing changes what "delete" means.** If two rows reference the same blob id, deleting one must not delete the blob. Reference-count it, or sweep unreferenced blobs on a schedule — and make the sweep conservative, because deleting a blob someone is mid-upload of is unrecoverable. This is the same reachability problem the GC solves, at a different scale ([The heap, and why it needs a collector](02-07-memory-management-and-garbage-collection.md#be-m24)).

> **\[Trap\]**
>
> **Do not use a checksum where you need a version.** A hash tells you two things differ; it cannot tell you which is newer. Concurrency control needs an ordering — a `rowversion`, a sequence, or a timestamp with a tiebreaker. Using a content hash as a concurrency token means two clients that make the same edit are indistinguishable from one that made none.

Recipe: [Recipe: a validated, deduplicated file upload](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m290)

<a id="be-m77"></a>

#### Consistent hashing and partition assignment

Assigning keys to N nodes with `hash(key) % N` works until N changes. Adding one node to a cluster of ten remaps roughly **90% of all keys** — a total cache invalidation, or a full data reshuffle.

``` csharp
// Rendezvous (highest random weight) hashing: simpler than a hash ring,
// better balanced, and it moves only the minimum on membership change.
static string Owner(string key, IReadOnlyList<string> nodes) {
    string best = nodes[0]; ulong bestScore = 0;
    foreach (var node in nodes) {
        ulong score = XxHash64.HashToUInt64(
            Encoding.UTF8.GetBytes(key + "\u0000" + node));
        if (score > bestScore) { bestScore = score; best = node; }
    }
    return best;
}
// Remove a node and only ITS keys move - every other key keeps the same owner,
// because their winning score was never affected by that node.
```

| Scheme           | Keys moved when N changes | Balance             | Complexity              |
|------------------|---------------------------|---------------------|-------------------------|
| `hash % N`       | ~all                      | perfect             | trivial                 |
| Hash ring        | ~1/N                      | needs virtual nodes | moderate                |
| Rendezvous (HRW) | ~1/N                      | good, no tuning     | simple                  |
| Fixed slot table | controlled                | explicit            | needs a mapping service |

> **\[Tip\]**
>
> **The fixed-slot approach is what production systems usually pick.** Hash keys into a large fixed number of slots — Redis Cluster uses 16,384 — and maintain a slot-to-node map separately. The hash function never changes, rebalancing is a map update plus a data move for the affected slots, and you can move slots deliberately to relieve a hot node rather than hoping the hash spreads evenly ([Read replicas and read/write splitting](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m264)).

> **\[Trap\]**
>
> **Balanced keys are not balanced load.** Consistent hashing distributes *keys* evenly; it says nothing about traffic. One celebrity tenant, one hot product, one runaway API consumer will concentrate on a single node regardless. Detect hot keys with a frequency sketch ([Approximate frequency: count-min sketch](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m128)), then handle them specifically — replicate them, or give them their own slot.
