---
id: "3-9"
number: "3.9"
title: "Specialised structures and data that does not fit"
part: 3
part_title: "Data structures and algorithms"
words: 5869
minutes: 26
---

# 3.9 Specialised structures and data that does not fit

> **Quick look.** Specialised structures, and techniques for data that is bigger than memory or arrives as a stream.
>
> - LRU cache = Dictionary for lookup plus LinkedList for ordering; ring buffers give bounded, allocation-free windows.
> - Bloom filters say "definitely not present" exactly and "probably present" with a tunable error rate.
> - For streams: top-k with a heap, external merge sort, Welford's algorithm for accurate variance.
> - HyperLogLog counts distinct values in about 12–16 KB within 1–2%; count-min sketch estimates frequencies.
> - Reservoir sampling picks a uniform random sample in one pass; percentiles need histograms, and percentiles don't average.
> - Random is predictable: use a cryptographic generator for anything security-related; never expose sequential ids publicly.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: An LRU cache for recently viewed flights

Each instance should keep the 10,000 most recently viewed flight details in memory, evicting the least recently used.

*Usually: let MemoryCache do it*

```csharp
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);

var flight = await cache.GetOrCreateAsync($"flight:{id}", async e =>
{
    e.Size = 1;
    e.SlidingExpiration = TimeSpan.FromMinutes(10);
    e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);   // cap: never stale forever
    return await db.Flights.AsNoTracking().FirstAsync(f => f.Id == id, ct);
});
```

> **Lesson:** Know how LRU works (dictionary plus linked list), but reach for the framework cache with a size limit first.

#### Example 2: Counting unique visitors without storing them

Analytics wants daily unique visitors across 40 million page views, merged across servers.

*HyperLogLog in Redis*

```csharp
// each server, per page view:
await redis.HyperLogLogAddAsync($"uv:{DateTime.UtcNow:yyyyMMdd}", visitorId);

// reporting: about 12 KB per day key, error around 1%, and keys merge across days
long today = await redis.HyperLogLogLengthAsync($"uv:{DateTime.UtcNow:yyyyMMdd}");
```

> **Lesson:** When exact answers cost memory proportional to the data, approximate structures give useful answers in fixed space.

### Implementing Them Yourself

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Implementing Them Yourself</p>

<p class="pd">Understanding is easier from the inside. These are simplified but faithful.</p>

<a id="be-b289"></a>

#### LRU cache

The structure that requires two collections cooperating, and the one place `LinkedList<T>` genuinely earns its place:

``` csharp
public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();   // head = most recent

    public LruCache(int capacity)
    {
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (!_map.TryGetValue(key, out var node)) { value = default!; return false; }
        _order.Remove(node);                  // O(1) — we hold the node
        _order.AddFirst(node);                // mark most-recently-used
        value = node.Value.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            existing.Value = (key, value);
            _order.AddFirst(existing);
            return;
        }
        if (_map.Count >= _capacity)
        {
            var lru = _order.Last!;           // least recently used
            _order.RemoveLast();
            _map.Remove(lru.Value.Key);
        }
        _map[key] = _order.AddFirst((key, value));
    }
}
```

**The dictionary gives O(1) lookup; the linked list gives O(1) reordering.** Neither can do both alone — this is Fact 2 resolved by combining two structures with complementary trade-offs. And it's the only common case where holding node references makes `LinkedList<T>` correct.

For the eviction-policy context — LRU versus LFU versus W-TinyLFU, and why LRU fails on scans — see the caching reference, Part I.2.

<a id="be-b290"></a>

#### Ring buffer

``` csharp
public class RingBuffer<T>
{
    private readonly T[] _buf;
    private int _head, _tail, _count;

    public RingBuffer(int capacity) => _buf = new T[capacity];
    public int Count => _count;

    public bool TryWrite(T item)
    {
        if (_count == _buf.Length) return false;              // full
        _buf[_tail] = item;
        _tail = (_tail + 1) % _buf.Length;
        _count++;
        return true;
    }

    public bool TryRead(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = _buf[_head];
        _buf[_head] = default!;                                // don't retain references
        _head = (_head + 1) % _buf.Length;
        _count--;
        return true;
    }
}
```

Fixed memory, no allocation after construction, perfect locality. This is what `Queue<T>` uses internally, and what a bounded `Channel<T>` uses to provide backpressure.

If the capacity is a power of two, replace `% length` with `& (length - 1)` — a mask instead of a division, and measurably faster in tight loops.

<a id="be-b291"></a>

#### Bloom filter

``` csharp
public class BloomFilter
{
    private readonly bool[] _bits;      // a BitArray or ulong[] in production
    private readonly int _hashCount;

    public BloomFilter(int expectedItems, double falsePositiveRate = 0.01)
    {
        int m = (int)(-expectedItems * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2)));
        _bits = new bool[m];
        _hashCount = Math.Max(1, (int)(m / (double)expectedItems * Math.Log(2)));
    }

    private IEnumerable<int> Positions(string item)
    {
        int h1 = item.GetHashCode();
        int h2 = HashCode.Combine(h1, 0x9E3779B9);
        for (int i = 0; i < _hashCount; i++)
            yield return (int)((uint)(h1 + i * h2) % (uint)_bits.Length);   // double hashing
    }

    public void Add(string item) { foreach (var p in Positions(item)) _bits[p] = true; }

    public bool MightContain(string item)
    {
        foreach (var p in Positions(item)) if (!_bits[p]) return false;
        return true;      // "probably" — never a false negative, sometimes a false positive
    }
}
```

**False positives, never false negatives.** So a "no" is definitive and a "yes" needs confirming. That asymmetry is exactly what makes it useful in front of an expensive store: it rejects definitely-absent keys without a lookup. See the caching reference, Part III.2, for the cache-penetration use.

------------------------------------------------------------------------

### Structures you build yourself

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Structures you build yourself</p>

<a id="be-m114"></a>

#### When to build your own

The BCL covers the common cases well enough that hand-rolling a collection is usually a mistake. There are four situations where it is not.

1.  **The access pattern has no BCL match.** Prefix search, approximate membership, fixed-capacity overwrite, "which group is this in" — none of these are expressible efficiently with `Dictionary` or `List`.
2.  **The constant factor matters and the shape is known.** A fixed-size ring buffer over a `T[]` beats `Queue<T>` when you never want it to grow and never want an allocation.
3.  **Memory is the constraint.** A bitset holds ten million booleans in 1.25 MB; a `HashSet<int>` holding the same information costs roughly 100 MB.
4.  **You need a guarantee the BCL does not give.** Bounded memory, no allocation after warm-up, deterministic eviction, lock-free reads.

> **\[Trap\]**
>
> **The bar is high, and it is correctness, not cleverness.** A custom structure needs the equality contract right ([Equality: ==, Equals, GetHashCode, IEquatable](02-03-types.md#be-m56)), the growth policy right, the enumerator right, and thread-safety documented. `Dictionary<K,V>` has had two decades of scrutiny; your version has had an afternoon. Write one only when you can name the specific property the BCL cannot give you, and test it against the BCL equivalent on random operation sequences.

``` csharp
// The test that actually catches bugs in a custom structure: differential testing
// against a known-correct reference implementation.
[Fact] public void MatchesReference() {
    var mine = new MyStructure<int>(); var reference = new List<int>();
    var rng = new Random(12345);                       // fixed seed = reproducible
    for (int i = 0; i < 100_000; i++) {
        switch (rng.Next(3)) {
            case 0: var v = rng.Next(1000); mine.Add(v); reference.Add(v); break;
            case 1 when reference.Count > 0:
                var idx = rng.Next(reference.Count);
                mine.Remove(reference[idx]); reference.RemoveAt(idx); break;
            default:
                Assert.Equal(reference.OrderBy(x => x), mine.OrderBy(x => x)); break;
        }
    }
}
```

<a id="be-m116"></a>

#### Ring buffer: bounded, allocation-free

A fixed-capacity queue that overwrites its oldest element when full. The right structure for "last N log lines", "last 60 seconds of samples", or any bounded window — because it never grows, never allocates after construction, and never needs eviction logic.

``` csharp
public sealed class RingBuffer<T> {
    private readonly T[] _items;
    private int _head;          // index of the oldest element
    private int _count;

    public RingBuffer(int capacity) {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
    }

    public int Count => _count;
    public int Capacity => _items.Length;
    public bool IsFull => _count == _items.Length;

    /// <summary>Appends, overwriting the oldest element when full.</summary>
    public void Add(T item) {
        int tail = (_head + _count) % _items.Length;
        _items[tail] = item;
        if (_count == _items.Length) _head = (_head + 1) % _items.Length;  // overwrite
        else _count++;
    }

    public T this[int i] {
        get {
            if ((uint)i >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(i));
            return _items[(_head + i) % _items.Length];
        }
    }

    /// <summary>Struct enumerator: foreach over this allocates nothing.</summary>
    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator {
        private readonly RingBuffer<T> _b; private int _i;
        internal Enumerator(RingBuffer<T> b) { _b = b; _i = -1; }
        public T Current => _b[_i];
        public bool MoveNext() => ++_i < _b._count;
    }
}
```

> **\[Tip\]**
>
> **Two refinements worth knowing.** If the capacity is a power of two, replace `% _items.Length` with `& (_items.Length - 1)` — a mask is far cheaper than a division, and this is the standard trick in high-throughput ring buffers. And if `T` is a reference type, null out the slot you overwrite (`_items[tail] = item` already does) so the buffer does not keep old objects alive past their window ([The GC in one page](02-07-memory-management-and-garbage-collection.md#be-b239)).

> **\[Trap\]**
>
> **Not thread-safe.** Concurrent `Add` corrupts `_head` and `_count` independently, producing torn state that no exception reports. For a multi-producer or multi-consumer version, use `Channel<T>` with `BoundedChannelFullMode.DropOldest`, which gives the same semantics with correct synchronisation ([Channels and async pipelines](05-05-parallelism.md#be-m135)).

<a id="be-m117"></a>

#### LRU cache: where LinkedList finally earns its place

An LRU cache needs three O(1) operations: look up by key, move an entry to the front, and evict from the back. A dictionary gives the first; a doubly-linked list gives the other two — but only because the dictionary hands you the node directly, so you never traverse. This is the one case where `LinkedList<T>` is the right choice ([Queue, Stack, LinkedList, PriorityQueue](03-05-heaps-and-priority-queues.md#be-b280)).

``` csharp
public sealed class LruCache<TKey, TValue> where TKey : notnull {
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _order = new();   // front = most recent
    private readonly Lock _gate = new();                 // .NET 9 Lock type

    private sealed record Entry(TKey Key, TValue Value);

    public LruCache(int capacity, IEqualityComparer<TKey>? cmp = null) {
        _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException();
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity, cmp);
    }

    public bool TryGet(TKey key, out TValue value) {
        lock (_gate) {
            if (_map.TryGetValue(key, out var node)) {
                _order.Remove(node);                     // O(1) - we hold the node
                _order.AddFirst(node);                   // O(1)
                value = node.Value.Value; return true;
            }
            value = default!; return false;
        }
    }

    public void Set(TKey key, TValue value) {
        lock (_gate) {
            if (_map.TryGetValue(key, out var existing)) {
                _order.Remove(existing);
                _map[key] = _order.AddFirst(new Entry(key, value));
                return;
            }
            if (_map.Count == _capacity) {
                var victim = _order.Last!;               // least recently used
                _order.RemoveLast();
                _map.Remove(victim.Value.Key);
            }
            _map[key] = _order.AddFirst(new Entry(key, value));
        }
    }
}
```

> **\[Why it matters\]**
>
> **Before you use this, consider not using it.** `IMemoryCache` already gives you size limits, expiration, eviction callbacks, and thread safety, and its eviction is approximate-LRU with a background scan rather than exact — which is almost always fine. Write your own when you need *exact* LRU semantics, a hard allocation budget, or a per-key cost model that `IMemoryCache`'s size accounting cannot express ([Caching layers](08-06-caching.md#be-m282)).

> **\[Tip\]**
>
> **Contention.** Every read takes the lock, because every read mutates the recency order. Under heavy concurrent reads that lock is the bottleneck. The standard fix is to batch recency updates: record accesses into a small per-thread buffer and apply them to the list periodically, accepting slightly stale ordering — which is exactly what production caches like Caffeine and `MemoryCache` do.

Worked example: [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)

<a id="be-m119"></a>

#### Bloom filter: probabilistic membership

A bit array plus *k* hash functions. "Definitely not present" is exact; "probably present" has a tunable false-positive rate. It uses roughly **10 bits per element for a 1% error rate**, regardless of how large the elements are — a set of ten million URLs costs about 12 MB instead of gigabytes.

``` csharp
public sealed class BloomFilter<T> {
    private readonly ulong[] _bits;
    private readonly int _bitCount, _hashCount;

    /// <param name="n">Expected number of items.</param>
    /// <param name="p">Target false-positive probability, e.g. 0.01.</param>
    public BloomFilter(int n, double p = 0.01) {
        _bitCount  = (int)Math.Ceiling(-n * Math.Log(p) / (Math.Log(2) * Math.Log(2)));
        _hashCount = Math.Max(1, (int)Math.Round(_bitCount / (double)n * Math.Log(2)));
        _bits = new ulong[(_bitCount + 63) / 64];
    }

    public void Add(T item) {
        foreach (var bit in Positions(item)) _bits[bit >> 6] |= 1UL << (bit & 63);
    }

    /// <summary>False means definitely absent. True means probably present.</summary>
    public bool MightContain(T item) {
        foreach (var bit in Positions(item))
            if ((_bits[bit >> 6] & (1UL << (bit & 63))) == 0) return false;
        return true;
    }

    // Kirsch-Mitzenmacher: k hashes derived from two, with no loss of accuracy.
    private IEnumerable<int> Positions(T item) {
        int h1 = item!.GetHashCode();
        int h2 = HashCode.Combine(h1, 0x9E3779B9);
        for (int i = 0; i < _hashCount; i++)
            yield return (int)((uint)(h1 + i * h2) % (uint)_bitCount);
    }
}
```

**Where it earns its place:** skipping an expensive lookup. Check the filter first; on "definitely not present" you avoid a database or network call entirely, and on "probably present" you pay the normal cost. A 1% false-positive rate means you avoid 99% of the lookups that would have missed.

``` csharp
if (!_seen.MightContain(url)) { Process(url); _seen.Add(url); }   // no I/O on the fast path
else if (!await _db.ExistsAsync(url)) { Process(url); _seen.Add(url); }
```

> **\[Trap\]**
>
> **Constraints to accept up front.** You cannot remove an item (clearing bits would break other members — use a counting Bloom filter if you must). You cannot enumerate it. You cannot resize it; sizing is a one-time decision, and exceeding `n` degrades the error rate quickly. And it depends on `GetHashCode`, which is randomised per process for strings — so a Bloom filter cannot be serialised and shared across processes unless you supply your own stable hash ([Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)).

### Algorithms for data that does not fit

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Algorithms for data that does not fit</p>

<a id="be-m123"></a>

#### When data does not fit in memory

Every technique in this part exists because of one constraint: the data is larger than RAM, or arrives as a stream you can only read once. That constraint rules out most of what the rest of the guide assumes — you cannot sort it, you cannot put it in a `HashSet`, you often cannot even count its distinct values exactly.

| Question                | In memory                      | At scale                                                               |
|-------------------------|--------------------------------|------------------------------------------------------------------------|
| top 10 by value         | `OrderByDescending().Take(10)` | bounded min-heap — [Top-K without sorting](03-05-heaps-and-priority-queues.md#be-m124)                   |
| sort everything         | `List.Sort()`                  | external merge sort — [External merge sort](03-07-searching-and-sorting.md#be-m125)                  |
| mean and variance       | two passes                     | Welford, one pass — [Streaming aggregates done accurately](#be-m126)   |
| count distinct          | `HashSet.Count`                | HyperLogLog — [Approximate distinct counts](#be-m127)                  |
| how often does X occur  | `Dictionary<T,int>`            | count-min sketch — [Approximate frequency: count-min sketch](#be-m128) |
| a random sample         | shuffle and take               | reservoir sampling — [Reservoir sampling](#be-m129)                    |
| the 99th percentile     | sort and index                 | fixed histogram — [Quantiles and histograms](#be-m130)                 |
| have I seen this before | `HashSet`                      | Bloom filter — [Bloom filter: probabilistic membership](#be-m119)      |

> **\[Why it matters\]**
>
> **The common trade.** Nearly all of these buy bounded memory with *approximation*, and the error is usually characterisable — "within 2%, 99 times out of 100". For monitoring, capacity planning, analytics, and anomaly detection, a 2% error on a number you can compute in 12 KB is dramatically more useful than an exact number you cannot compute at all. For billing, it is unacceptable. Know which side of that line you are on before choosing.

> **\[Tip\]**
>
> **Check first whether the database can do it.** A window function, a `GROUP BY` with an index behind it, or `APPROX_COUNT_DISTINCT` (SQL Server 2019+, itself a HyperLogLog) runs where the data already lives and moves nothing over the network ([Window functions](07-02-the-relational-model-and-sql.md#be-d414), [Aggregation: GROUP BY, HAVING, GROUPING SETS](07-02-the-relational-model-and-sql.md#be-d411)). These algorithms are for data that is streaming past you, not sitting in a table you could query.

<a id="be-m126"></a>

#### Streaming aggregates done accurately

The textbook variance formula — `E[x²] - E[x]²` — subtracts two large, nearly equal numbers and catastrophically loses precision. Welford's algorithm is one pass, numerically stable, and barely longer.

``` csharp
public struct RunningStats {
    private long _n; private double _mean, _m2, _min, _max;

    public RunningStats() { _min = double.PositiveInfinity; _max = double.NegativeInfinity; }

    public void Add(double x) {
        _n++;
        double delta = x - _mean;
        _mean += delta / _n;
        _m2   += delta * (x - _mean);          // uses the UPDATED mean - this is the trick
        if (x < _min) _min = x;
        if (x > _max) _max = x;
    }

    public long   Count    => _n;
    public double Mean     => _n > 0 ? _mean : double.NaN;
    public double Variance => _n > 1 ? _m2 / (_n - 1) : double.NaN;   // sample variance
    public double StdDev   => Math.Sqrt(Variance);
    public double Min      => _min;
    public double Max      => _max;

    // Parallel-friendly: combine partial results from separate threads or shards.
    public static RunningStats Merge(in RunningStats a, in RunningStats b) {
        if (a._n == 0) return b;
        if (b._n == 0) return a;
        long n = a._n + b._n;
        double d = b._mean - a._mean;
        return new RunningStats {
            _n = n,
            _mean = a._mean + d * b._n / n,
            _m2 = a._m2 + b._m2 + d * d * a._n * b._n / n,
            _min = Math.Min(a._min, b._min), _max = Math.Max(a._max, b._max)
        };
    }
}
```

**Summation has the same problem.** Adding a million small values to a large running total loses the low bits of every addition. Kahan (compensated) summation tracks the lost remainder.

``` csharp
static double KahanSum(ReadOnlySpan<double> values) {
    double sum = 0, c = 0;                     // c carries the running error
    foreach (var v in values) {
        double y = v - c;
        double t = sum + y;
        c = (t - sum) - y;                     // recover what the addition dropped
        sum = t;
    }
    return sum;
}
```

> **\[Trap\]**
>
> **None of this applies to money.** `decimal` is exact for decimal fractions, so compensated summation is unnecessary and misleading there ([decimal: money and precision](01-02-numbers-inside-the-machine.md#be-m47)). These techniques are for `double`-valued measurements — latencies, sensor readings, prices in a scientific sense — where the values span many orders of magnitude. Using `double` for currency is the underlying bug that makes people reach for them.

<a id="be-m127"></a>

#### Approximate distinct counts

Counting distinct values exactly costs memory proportional to the number of distinct values. HyperLogLog costs a fixed 12–16 KB and answers within roughly 1–2% for cardinalities from tens to billions.

The intuition: hash each item, look at how many leading zeros the hash has. Seeing a hash with 20 leading zeros suggests you have seen around 2<sup>20</sup> distinct items, because that is how rare such a hash is. Averaging that estimate across many independent buckets — harmonically, to blunt outliers — makes it accurate.

``` csharp
public sealed class HyperLogLog {
    private readonly byte[] _registers;
    private readonly int _b;                          // 2^b registers; b=14 -> 16 KB

    public HyperLogLog(int b = 14) { _b = b; _registers = new byte[1 << b]; }

    public void Add<T>(T item) {
        ulong h = Hash(item);
        int index = (int)(h >> (64 - _b));            // top b bits pick the register
        byte rank = (byte)(BitOperations.TrailingZeroCount(h | (1UL << (64 - _b))) + 1);
        if (rank > _registers[index]) _registers[index] = rank;   // keep the max only
    }

    public long Count() {
        int m = _registers.Length;
        double alpha = 0.7213 / (1 + 1.079 / m);
        double sum = 0; int zeros = 0;
        foreach (var r in _registers) { sum += 1.0 / (1UL << r); if (r == 0) zeros++; }
        double est = alpha * m * m / sum;
        if (est <= 2.5 * m && zeros > 0)              // small-range correction
            est = m * Math.Log((double)m / zeros);
        return (long)Math.Round(est);
    }

    /// <summary>Mergeable: union of two sets is the element-wise max.</summary>
    public void Merge(HyperLogLog other) {
        for (int i = 0; i < _registers.Length; i++)
            _registers[i] = Math.Max(_registers[i], other._registers[i]);
    }
}
```

> **\[Tip\]**
>
> **Mergeability is the real superpower.** Because the union of two sketches is an element-wise max, you can compute distinct visitors per hour independently on every server, ship 16 KB per hour per server, and get an exact-union distinct count for any time range — day, week, arbitrary window — without ever storing a visitor id. Exact counting cannot do that at any memory budget. Redis exposes this directly as `PFADD`/`PFCOUNT`/`PFMERGE` ([Redis: the data structures](07-06-non-relational-stores.md#be-m206)).

> **\[Trap\]**
>
> **It only estimates cardinality.** You cannot ask whether a specific item is in the set (use a Bloom filter — [Bloom filter: probabilistic membership](#be-m119)), you cannot remove items, and you cannot enumerate it. And because it depends on hashing, the hash must be stable across processes if sketches are merged from different machines — `string.GetHashCode()` is not ([Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)).

<a id="be-m128"></a>

#### Approximate frequency: count-min sketch

"How many times have I seen X?" over a stream with millions of distinct keys. A `Dictionary<string,int>` grows without bound; a count-min sketch is a fixed 2-D array of counters that never grows and **never underestimates**.

``` csharp
public sealed class CountMinSketch {
    private readonly int[,] _counts;
    private readonly int _width, _depth;

    /// <param name="epsilon">Error tolerance as a fraction of total count, e.g. 0.001.</param>
    /// <param name="delta">Probability the error exceeds it, e.g. 0.01.</param>
    public CountMinSketch(double epsilon = 0.001, double delta = 0.01) {
        _width  = (int)Math.Ceiling(Math.E / epsilon);        // ~2,718 for 0.1%
        _depth  = (int)Math.Ceiling(Math.Log(1 / delta));     // ~5
        _counts = new int[_depth, _width];                    // ~54 KB total
    }

    public void Add(string key, int count = 1) {
        for (int d = 0; d < _depth; d++)
            _counts[d, Bucket(key, d)] += count;
    }

    /// <summary>Never under-counts. May over-count by at most epsilon * total.</summary>
    public int Estimate(string key) {
        int min = int.MaxValue;
        for (int d = 0; d < _depth; d++)
            min = Math.Min(min, _counts[d, Bucket(key, d)]);  // the MIN kills collisions
        return min;
    }

    private int Bucket(string key, int d) =>
        (int)((uint)HashCode.Combine(key, d) % (uint)_width);
}
```

Why taking the minimum works: every row hashes differently, so a key collides with different neighbours in each row. Every counter it touches is at least its true count (collisions only add), so the smallest of them is the tightest upper bound.

``` csharp
// Heavy hitters: pair the sketch with a small top-K heap of candidate keys.
sketch.Add(key);
var estimate = sketch.Estimate(key);
if (estimate > threshold) heavyHitters.Offer(key, estimate);   // §124
```

> **\[Why it matters\]**
>
> **Where this earns its keep.** Rate limiting by API key across millions of keys, detecting hot cache keys or hot database partitions, finding the top search terms in a stream, and spotting abuse patterns — all cases where you need approximate counts for very many keys and cannot afford a counter each. The one-sided error is what makes it usable: "at most X" is a safe basis for a throttling decision in a way that "roughly X" would not be.

> **\[Trap\]**
>
> **Rare keys are the least accurate.** The error bound is proportional to the *total* stream count, so a key seen 3 times in a stream of 10 million with ε=0.001 may be reported as 10,000. The sketch is trustworthy for heavy hitters and useless for the tail. Never use it to answer questions about individual low-frequency items.

<a id="be-m129"></a>

#### Reservoir sampling

Take a uniformly random sample of *k* items from a stream of unknown length, in one pass, holding only *k* items. Every element ends up with exactly the same probability of being in the sample, without ever knowing *n*.

``` csharp
public static T[] Sample<T>(IEnumerable<T> source, int k, Random? rng = null) {
    rng ??= Random.Shared;
    var reservoir = new T[k];
    int i = 0;

    foreach (var item in source) {
        if (i < k) reservoir[i] = item;              // fill the reservoir first
        else {
            int j = rng.Next(i + 1);                 // uniform in [0, i]
            if (j < k) reservoir[j] = item;          // replace with probability k/(i+1)
        }
        i++;
    }
    return i < k ? reservoir[..i] : reservoir;
}
```

The invariant: after processing *i* items, every one of them is in the reservoir with probability *k/i*. The *(i+1)*-th item is accepted with probability *k/(i+1)*, and each existing member survives with exactly the complementary probability — so the invariant holds at every step, including the last.

``` csharp
// Weighted variant (A-Res): keep the k largest values of u^(1/w),
// where u is uniform in (0,1] and w is the item's weight.
public static T[] WeightedSample<T>(IEnumerable<T> source, int k, Func<T, double> weight) {
    var heap = new PriorityQueue<T, double>(k);      // min-heap on the key
    foreach (var item in source) {
        double key = Math.Pow(Random.Shared.NextDouble(), 1.0 / weight(item));
        if (heap.Count < k) heap.Enqueue(item, key);
        else if (key > heap.Peek().Priority) heap.DequeueEnqueue(item, key);
    }
    return heap.UnorderedItems.Select(x => x.Element).ToArray();
}
```

> **\[Tip\]**
>
> **Practical uses.** Sampling log lines for debugging without biasing toward the start of the file. Building a representative test dataset from production without loading production. Trace sampling in observability pipelines. And validating a migration: sample 1,000 rows uniformly from both old and new tables and compare, which is far more informative than checking the first 1,000 of each.

> **\[Trap\]**
>
> **`Random` is not thread-safe** and a shared instance used concurrently silently returns correlated or repeated values — which quietly destroys the uniformity you are relying on. Use `Random.Shared` (thread-safe since .NET 6) or one instance per thread. For anything security-relevant, use `RandomNumberGenerator` instead ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)).

Goes deeper: [Shuffling and sampling correctly](#be-m74)

<a id="be-m130"></a>

#### Quantiles and histograms

"What is the p99 latency?" cannot be answered by an average, and computing it exactly means keeping every sample. A fixed-bucket histogram bounds memory and answers percentiles to within the bucket width.

``` csharp
public sealed class LatencyHistogram {
    // Log-spaced buckets: fine resolution where the values are, coarse in the tail.
    private static readonly long[] Bounds =
        [1, 2, 5, 10, 20, 50, 100, 200, 500, 1_000, 2_000, 5_000, 10_000, long.MaxValue];
    private readonly long[] _counts = new long[Bounds.Length];
    private long _total;

    public void Record(long ms) {
        int i = Array.BinarySearch(Bounds, ms);      // §115
        if (i < 0) i = ~i;
        Interlocked.Increment(ref _counts[i]);       // lock-free §132
        Interlocked.Increment(ref _total);
    }

    /// <summary>Upper bound of the bucket containing the requested quantile.</summary>
    public long Quantile(double q) {
        long target = (long)Math.Ceiling(q * Interlocked.Read(ref _total));
        long seen = 0;
        for (int i = 0; i < _counts.Length; i++) {
            seen += Interlocked.Read(ref _counts[i]);
            if (seen >= target) return Bounds[i];
        }
        return Bounds[^1];
    }
}
```

| Approach               | Memory            | Accuracy                | Mergeable |
|------------------------|-------------------|-------------------------|-----------|
| keep all samples       | O(n)              | exact                   | yes       |
| fixed-bucket histogram | O(buckets)        | bucket width            | **yes**   |
| HdrHistogram           | O(precision)      | configurable, high      | yes       |
| t-digest               | O(compression)    | excellent at the tails  | yes       |
| P² algorithm           | O(1) per quantile | good, one quantile only | no        |

> **\[Trap\]**
>
> **Percentiles do not average.** The p99 of the union of two servers' data is not the mean of their two p99s — that computation is meaningless and it is what most naive dashboards display. You must merge the *histograms*, then compute the quantile from the merged distribution. This is the single most common statistics error in production monitoring, and it is why the metrics libraries expose histogram types rather than pre-computed percentiles.

> **\[Tip\]**
>
> `System.Diagnostics.Metrics.Histogram<T>` is the built-in, OpenTelemetry-compatible version — record to it and let the collector own bucketing and merging. Write your own only when you need percentiles inside the process, for a decision like adaptive concurrency or load shedding.

Worked example: [Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)

Applied in: [Metrics: what to measure](11-06-observability.md#be-e540)

### Identity, randomness, and hashing

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Identity, randomness, and hashing</p>

<a id="be-m73"></a>

#### Random: what it is and what it is not

`Random` is a *pseudo*-random generator: a deterministic function of internal state. Given the seed, every value it will ever produce is predictable. That is a feature for tests and a vulnerability everywhere else.

``` csharp
Random.Shared.Next(100);              // thread-safe (.NET 6+), use this by default
new Random(12345);                    // fixed seed: reproducible, for tests
new Random();                         // auto-seeded per instance since .NET Core

// SECURITY: never Random for anything an attacker benefits from predicting -
// tokens, password resets, session ids, nonces, shuffling a real card game.
RandomNumberGenerator.GetBytes(32);
RandomNumberGenerator.GetInt32(0, 100);          // unbiased, cryptographic
RandomNumberGenerator.GetHexString(16);          // .NET 9
```

> **\[Trap\]**
>
> **Modulo introduces bias.** `rng.Next() % 100` is not uniform: `int.MaxValue` is not a multiple of 100, so the low values occur slightly more often. The bias is small for small ranges and large for big ones. `Random.Next(0, 100)` and `RandomNumberGenerator.GetInt32` both handle rejection sampling internally — use them rather than taking a remainder yourself.

``` text
// Older code that shares one Random across threads is subtly broken:
static readonly Random _shared = new();          // NOT thread-safe pre-.NET 6
// Concurrent calls corrupt the internal state and can return long runs of
// zeros - which silently destroys the uniformity anything downstream assumes.
// Fix: Random.Shared, or [ThreadStatic], or one instance per operation.
```

``` csharp
// Non-uniform distributions, when you need them:
double u1 = Random.Shared.NextDouble(), u2 = Random.Shared.NextDouble();
double gaussian = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);  // Box-Muller
double exponential = -Math.Log(1 - Random.Shared.NextDouble()) / rate;         // for jitter
```

> **\[Tip\]**
>
> **Seed deliberately in tests.** A fixed seed makes a failing property test reproducible; an unseeded one makes it a ghost. Log the seed on failure so a random-seed run can be replayed exactly — that gives you both broad coverage and reproducibility ([Contract tests for equality and comparers](04-06-testing.md#be-m225)).

<a id="be-m74"></a>

#### Shuffling and sampling correctly

There is exactly one correct in-place shuffle, and two popular wrong ones.

``` csharp
// CORRECT - Fisher-Yates. Every permutation equally likely, O(n), no allocation.
static void Shuffle<T>(Span<T> items, Random rng) {
    for (int i = items.Length - 1; i > 0; i--) {
        int j = rng.Next(i + 1);              // NOTE: inclusive of i
        (items[i], items[j]) = (items[j], items[i]);
    }
}

// .NET 8 ships it - prefer this:
Random.Shared.Shuffle(span);
RandomNumberGenerator.Shuffle(span);          // cryptographic variant
```

``` csharp
// WRONG 1 - "naive shuffle": j drawn from the whole range each time.
for (int i = 0; i < n; i++) Swap(i, rng.Next(n));
// Produces n^n equally likely outcomes over n! permutations. n^n is not
// divisible by n!, so some permutations are strictly more likely than others.
// It LOOKS random and it is measurably biased.

// WRONG 2 - the LINQ folklore:
items.OrderBy(_ => Guid.NewGuid());
items.OrderBy(_ => rng.Next());
// A comparison sort with a key that is re-evaluated is not a valid comparer.
// It allocates n keys, costs O(n log n), can throw
// "IComparer.Compare() returns inconsistent results", and the distribution
// is not uniform (§145).
```

``` csharp
// Picking k of n without shuffling the whole thing - partial Fisher-Yates:
static T[] Pick<T>(T[] source, int k, Random rng) {
    var copy = (T[])source.Clone();
    for (int i = 0; i < k; i++) {
        int j = rng.Next(i, copy.Length);
        (copy[i], copy[j]) = (copy[j], copy[i]);
    }
    return copy[..k];                          // O(k), not O(n)
}
// From a stream of unknown length, use reservoir sampling (§129).
// From a database, ORDER BY NEWID() sorts the whole table - use TABLESAMPLE
// or a keyset jump instead on anything large.
```

> **\[Trap\]**
>
> **A "random" order that must be stable is not a shuffle.** Randomising search results per request means a user paging through them sees duplicates and gaps — the same class of bug as a non-deterministic `ORDER BY` ([Pagination: OFFSET vs keyset](07-07-orms-and-ef-core.md#be-m161)). Derive a per-session seed, store it, and order by a deterministic hash of `(seed, id)` so the sequence is stable for that user.

<a id="be-m78"></a>

#### Generating identifiers

| Scheme                   | Size   | Sortable | Client-assignable | Cost                                                                              |
|--------------------------|--------|----------|-------------------|-----------------------------------------------------------------------------------|
| `IDENTITY` / sequence    | 8 B    | yes      | no                | a round trip before you know the id                                               |
| Hi/Lo (block allocation) | 8 B    | yes      | yes               | gaps on restart                                                                   |
| Guid v4                  | 16 B   | no       | yes               | index fragmentation ([B-tree anatomy: levels, splits, and fill factor](07-03-indexes-and-query-plans.md#be-m196)) |
| **Guid v7 / ULID**       | 16 B   | yes      | yes               | leaks creation time                                                               |
| Snowflake                | 8 B    | yes      | yes               | needs unique node ids and a clock                                                 |
| Natural key              | varies | domain   | yes               | it will change, and then everything breaks                                        |

``` csharp
Guid.CreateVersion7();                    // .NET 9: 48-bit ms timestamp + 74 random bits.
// Time-ordered, so inserts append and the B-tree stays dense - all the
// benefits of a sequential key with client-side assignment.

// EF Core: keep the sequential surrogate as the clustered key and expose
// a Guid publicly, so external ids never leak row counts:
e.HasKey(o => o.Id);                                  // bigint, clustered
e.HasIndex(o => o.PublicId).IsUnique();               // Guid, nonclustered
e.Property(o => o.PublicId).HasDefaultValueSql("NEWID()");
```

``` csharp
// Snowflake: 64 bits that fit a bigint and encode their own origin.
//   1 unused | 41 bits ms since epoch | 10 bits node | 12 bits sequence
//   -> 69 years of range, 1024 nodes, 4096 ids per node per millisecond.
long Next() {
    lock (_gate) {
        long now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (now < _lastMs) throw new InvalidOperationException("Clock moved backwards");
        _sequence = now == _lastMs ? (_sequence + 1) & 0xFFF : 0;
        if (_sequence == 0) while ((now = Now()) <= _lastMs) { }   // wait out the ms
        _lastMs = now;
        return ((now - Epoch) << 22) | ((long)_nodeId << 12) | _sequence;
    }
}
```

> **\[Trap\]**
>
> **Sequential ids leak information.** Exposing `/orders/1042` tells any customer roughly how many orders you have processed, and lets them enumerate resources by incrementing — which is only stopped by authorisation checks you had better have. Use an opaque public id (Guid, ULID, or an HMAC of the internal id) at the API surface and keep the sequential key internal.

> **\[Trap\]**
>
> **Natural keys change.** Email addresses, SKUs, national ids, ISBNs, tax numbers — every one of them is "guaranteed stable" until the day it is not, and by then it is a foreign key in fourteen tables. Use a surrogate key for identity and put a unique constraint on the natural key for correctness ([Constraints: what the database guarantees](07-02-the-relational-model-and-sql.md#be-m188)).

Goes further: [Keys, sequences, and value generation](07-07-orms-and-ef-core.md#be-m178)
