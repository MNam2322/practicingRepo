---
id: "8-6"
number: "8.6"
title: "Caching"
part: 8
part_title: "Building a backend"
words: 19936
minutes: 87
---

# 8.6 Caching

> **Quick look.** Caching: reuse recent answers to save work, without serving wrong data.
>
> - Cache data that is read often, expensive to produce and fine slightly stale; never for decisions that must be exact.
> - In-process caches are fastest, Redis is shared, HybridCache (.NET 9) combines both with stampede protection and tags.
> - Cache-aside is the default; every entry needs an expiry and every cache a size limit.
> - Prevent stampedes with single-flight loading and jittered TTLs; cache "not found" to stop penetration.
> - Put everything that changes the answer in the key (tenant, user, locale, version); versioned keys avoid invalidation.
> - HTTP: no-cache means revalidate, no-store means don't store; private vs public is a security boundary; set Vary correctly.
> - Cache DTOs, never entities; never cache per-user data under a shared key.
> - The system must survive the cache failing; don't cache to hide a slow query you haven't indexed.


### The big picture

The fastest work is work you don't do. If a thousand requests a minute ask for the fare between London and New York, and the fare changes twice a day, asking the database a thousand times is waste. A cache keeps a recent answer close at hand and reuses it. You have met caches throughout this guide: the processor's caches in Memory (1.5), the operating system's page cache in Storage and files (1.6), the database's buffer pool in Why databases exist (7.1), and HTTP caches and CDNs in HTTP (6.6). They all make the same trade: speed, paid for with memory and the risk of serving something out of date.

This chapter is about caching in your own backend. It covers what to cache and where: in the process, in a shared store such as Redis, or both at once. It then covers the standard patterns for reading and writing through a cache, expiry and eviction, and the two classic problems, stale data after a change and a stampede of requests when a popular entry expires. The example builds a small cache with each of these behaviours so you can watch them happen.

### Before you start

**Builds on:** 1.5 Memory, 1.6 Storage and files, 7.1 Why databases exist, 6.6 HTTP, 7.4 Transactions and isolation, 7.6 Non-relational stores.

**Ideas explained later:**

- **Availability** (10.1): A system works when legitimate users need it.
- **Message broker** (8.7): The server that hosts queues and topics and delivers messages.
- **Incident** (12.5): An unplanned disruption that affects users or threatens to.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Load shedding** (12.5): Rejecting some work quickly under overload so the rest stays fast.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, generic types, lambdas, LINQ, tuples, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the cache-aside pattern?**

> The application checks the cache first. On a miss, it loads the value from the source, stores it in the cache and returns it. The cache itself knows nothing about the source.

**2. What is a cache stampede, and how do you prevent one?**

> Many requests miss the same popular key at the same moment, for example when it expires, and all of them hit the database at once. Prevent it by letting only one request load each key while the others wait for its result, by refreshing entries before they expire, and by adding random jitter to expiry times.

**3. What is the difference between an in-memory cache and a distributed cache?**

> An in-memory cache lives inside one process: extremely fast, but separate on every instance and lost on restart. A distributed cache, such as Redis, is shared by all instances and survives restarts, at the cost of a network round trip and serialization.

**4. What does LRU eviction mean?**

> When the cache is full, it removes the least recently used entry to make room. Entries that are read often stay; ones nobody has touched lately go.

**5. Why can MemoryCache with a SizeLimit throw when you add an entry?**

> Once a size limit is set, every entry must declare its own size. Adding one without a size throws, because the cache can't tell whether it fits.

**6. What must a cache key include?**

> Everything that changes the answer: the resource id, and also the tenant, the user or role if the result is personalized, the language, and any filter or paging parameters. A key that leaves one out serves one user's data to another.

**7. Should seat availability for a booking decision come from a cache?**

> No. Displaying a roughly current seat map from a cache is fine, but the booking itself must check and change the real data atomically. Caches are for reads that tolerate being slightly old.

### Why cache

A **cache** is a store of copies of data kept closer to where it is needed, so that repeated reads are faster and cheaper. Reading a value from memory in your own process takes nanoseconds. Reading it from a cache server in the same data centre takes a fraction of a millisecond. Reading it from a database with a query that joins three tables can take several milliseconds, and much more under load. The latency table in Memory (1.5) is the reason caches exist.

Caching does three things for a backend. It makes responses faster. It reduces load on the database and other services, often by a large factor, which delays the day you need a bigger database. And it can keep a service answering, with slightly old data, while a dependency is slow or down.

A lookup that finds the value is a **cache hit**. One that doesn't is a **cache miss**, and falls through to the source. The share of lookups that are hits is the **hit ratio**. A cache with a 95% hit ratio sends one request in twenty to the database; one with 50% only halves the load. Hit ratio depends on how much the cache holds compared with how much data is actually in use, the **working set**.

### What to cache

Good candidates are read much more often than they change, expensive to produce, and acceptable if slightly out of date. Reference data such as airports, aircraft types and currencies is ideal. Fares, schedules and search results often suit short expiry times. Responses that are the same for everyone are easier than personalized ones.

Some data shouldn't be cached for decisions, only for display. A seat map shown to a browsing passenger can come from a cache that is a few seconds old. The booking itself must check and change the real data in the database atomically, as Transactions and isolation (7.4) described.

### Where to cache

There are several places, and systems often use more than one.

An in-process cache keeps values in the application's own memory, such as .NET's `IMemoryCache`. Nothing is faster. But every instance has its own copy, so instances can disagree, a restart empties it, and memory spent on the cache is memory the app can't use for anything else.

A **distributed cache** is a separate service shared by every instance, usually Redis, from Non-relational stores (7.6). All instances see the same values, and the cache survives app restarts. Each read costs a network round trip and turning bytes back into objects. In ASP.NET Core, `IDistributedCache` is the simple interface to it.

A two-level cache combines both: a small in-memory level in front of a shared distributed level. .NET 9 added **HybridCache**, which does exactly this, loads each key only once when many requests miss at the same time, and supports removing groups of entries by tag.

HTTP caching happens outside your code: in browsers, proxies and CDNs, controlled by the `Cache-Control` and `ETag` headers from HTTP (6.6). **Output caching** in ASP.NET Core stores whole responses on the server and replays them for matching requests, without running the endpoint at all.

### Patterns

**Cache-aside** is the most common pattern, and the one the example uses. The application checks the cache; on a miss, it loads from the source, stores the result in the cache and returns it. The cache stays simple and knows nothing about the database.

A **read-through cache** hides the source instead: the application only ever asks the cache, and the cache loads missing values itself using a loader it was given. HybridCache's `GetOrCreateAsync` works this way.

On writes, a **write-through cache** updates the cache and the source together, so reads right after a write see the new value. A **write-behind cache** updates the cache immediately and writes to the source later, in the background. Writes are fast, but anything not yet written is lost if the cache fails, so it is rarely right for business data.

Refresh-ahead reloads popular entries shortly before they expire, so users rarely see a miss at all.

### Expiry and eviction

Cached data must eventually go. There are two separate reasons.

Expiry removes entries that may be out of date. **Absolute expiration** removes an entry at a fixed time after it was stored, such as five minutes. **Sliding expiration** removes it after a period without any reads, so popular entries stay; combine it with an absolute limit, or a popular entry is never refreshed at all.

**Eviction** removes entries because the cache is full. The most common policy is **LRU**, least recently used: remove whichever entry has gone longest without being read. LRU is a classic use of a linked list together with a hash table, as Arrays and lists (3.2) mentioned. The dictionary finds an entry in O(1), and the list moves it to the front in O(1) each time it is used.

Every cache needs a size limit. An unbounded cache is the memory leak from Memory management and garbage collection (2.7). In .NET, `MemoryCache` has a `SizeLimit` option, but it uses sizes you assign, not bytes: once a limit is set, every entry must declare a size, or adding it throws.

### Invalidation

Phil Karlton's famous line is that there are only two hard things in computer science: cache invalidation and naming things. **Cache invalidation** means making sure cached copies don't keep serving data after the source changes.

The simplest strategy is a short expiry time: accept that data may be up to, say, 60 seconds old. The next is to remove or update the cached entry whenever the application writes the data. With several instances, each with its own in-memory cache, a write on one instance must tell the others, typically through a message on Redis or a message broker. Versioned keys, which include a version number that changes on every write, avoid removing entries at all: new reads simply use a new key.

Ordering causes subtle bugs. Suppose one request reads the old value from the database, then another writes a new value and invalidates the cache, and then the first request stores its old value in the cache. The cache now holds stale data until it expires. Short expiry times limit how long such mistakes last, which is why even carefully invalidated caches should still expire.

### Stampedes

A popular key expires. In the next few milliseconds, hundreds of requests miss it at the same time, and every one of them queries the database. A **cache stampede**, also called a thundering herd, can overload a database that handles the normal load easily, and it tends to happen at the busiest moments.

The defences work together. **Single-flight** loading lets only the first request for a missing key load it, while the others wait for the same result, often with a per-key lock or a shared task, as the example shows; HybridCache does this for you. **Stale-while-revalidate** keeps serving the old value for a short while after it expires, while one request refreshes it in the background. Adding random jitter to expiry times stops many keys cached at the same moment from all expiring together. And warming a cache after a deployment or restart keeps it from starting empty under full load.

Caching "not found" results briefly, the negative caching from DNS (6.4), stops repeated requests for missing keys from reaching the database every time.

### Seeing it for real

This program builds a small cache-aside cache over a slow fake database, and shows hits, expiry, a stampede with and without single-flight loading, stale data and invalidation, and an LRU cache evicting entries.

```csharp
// caching.cs — run with: dotnet run caching.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

var db = new SlowFareDatabase();                              // 50 ms per query, and it counts its queries

// 1. Cache-aside with expiry
var fares = new FareCache(expiry: TimeSpan.FromMilliseconds(300));
for (int i = 0; i < 3; i++) Console.WriteLine(await Timed(() => fares.GetAsync("LHR-JFK", db.LoadFareAsync)));
await Task.Delay(400);                                        // wait for the entry to expire
Console.WriteLine($"after expiry: {await Timed(() => fares.GetAsync("LHR-JFK", db.LoadFareAsync))}");
Console.WriteLine($"database queries: {db.Queries}, hit ratio {fares.HitRatio:P0}");
// 450 in about 50 ms   (miss)
// 450 in 0 ms          (hit)
// 450 in 0 ms          (hit)
// after expiry: 450 in about 50 ms
// database queries: 2, hit ratio 50%

// 2. A stampede: 100 requests at once for a key nobody has loaded yet
db.Reset();
var unprotected = new FareCache(TimeSpan.FromMinutes(5), singleFlight: false);
await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => unprotected.GetAsync("LHR-SIN", db.LoadFareAsync)));
Console.WriteLine($"no protection: {db.Queries} database queries");        // 100
db.Reset();
var protectedCache = new FareCache(TimeSpan.FromMinutes(5), singleFlight: true);
await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => protectedCache.GetAsync("LHR-SIN", db.LoadFareAsync)));
Console.WriteLine($"single-flight: {db.Queries} database query");         // 1: the other 99 waited for its result

// 3. Stale data, and invalidation on write
var longLived = new FareCache(TimeSpan.FromMinutes(5));
await longLived.GetAsync("LHR-JFK", db.LoadFareAsync);      // cached: 450
db.SetFare("LHR-JFK", 480m);                                  // the fare changes in the database
Console.WriteLine($"cached: {await longLived.GetAsync("LHR-JFK", db.LoadFareAsync)}");      // 450: stale
longLived.Invalidate("LHR-JFK");                              // what a write path should do
Console.WriteLine($"after invalidation: {await longLived.GetAsync("LHR-JFK", db.LoadFareAsync)}");   // 480

// 4. LRU eviction: a full cache drops whatever was used longest ago
var airports = new LruCache<string, string>(capacity: 3);
airports.Set("LHR", "London Heathrow");
airports.Set("JFK", "New York JFK");
airports.Set("SIN", "Singapore Changi");
airports.TryGet("LHR", out _);                                // reading LHR makes it the most recently used
airports.Set("DXB", "Dubai");                                 // full: something must go
Console.WriteLine(string.Join(", ", airports.Keys));          // DXB, LHR, SIN: JFK was least recently used
Console.WriteLine(airports.TryGet("JFK", out _));             // False

static async Task<string> Timed(Func<Task<decimal>> work)
{
    var sw = Stopwatch.StartNew();
    decimal value = await work();
    return $"{value} in {sw.ElapsedMilliseconds} ms";
}

sealed class SlowFareDatabase
{
    readonly ConcurrentDictionary<string, decimal> fares = new() { ["LHR-JFK"] = 450m, ["LHR-SIN"] = 900m };
    int queries;
    public int Queries => queries;
    public void Reset() => queries = 0;
    public void SetFare(string route, decimal fare) => fares[route] = fare;

    public async Task<decimal> LoadFareAsync(string route)
    {
        Interlocked.Increment(ref queries);
        await Task.Delay(50);                                 // stands in for a slow query
        return fares[route];
    }
}

sealed class FareCache(TimeSpan expiry, bool singleFlight = true)
{
    readonly ConcurrentDictionary<string, (Lazy<Task<decimal>> Load, DateTime Expires)> entries = new();
    int hits, lookups;
    public double HitRatio => lookups == 0 ? 0 : (double)hits / lookups;

    public Task<decimal> GetAsync(string key, Func<string, Task<decimal>> load)
    {
        Interlocked.Increment(ref lookups);
        DateTime now = DateTime.UtcNow;
        if (entries.TryGetValue(key, out var entry) && entry.Expires > now)
        {
            Interlocked.Increment(ref hits);
            return entry.Load.Value;                          // a hit, or a load already in progress
        }
        if (!singleFlight) return LoadAndStore(key, load);    // every miss queries the source
        var fresh = (Load: new Lazy<Task<decimal>>(() => load(key)), Expires: now + expiry);
        var winner = entries.AddOrUpdate(key, fresh, (_, old) => old.Expires > now ? old : fresh);   // one entry per key wins
        return winner.Load.Value;                             // everyone awaits the same load
    }

    public void Invalidate(string key) => entries.TryRemove(key, out _);

    async Task<decimal> LoadAndStore(string key, Func<string, Task<decimal>> load)
    {
        decimal value = await load(key);
        entries[key] = (new Lazy<Task<decimal>>(Task.FromResult(value)), DateTime.UtcNow + expiry);
        return value;
    }
}

sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> map = new();
    readonly LinkedList<(TKey Key, TValue Value)> order = new();   // most recently used at the front
    public IEnumerable<TKey> Keys => order.Select(e => e.Key);

    public bool TryGet(TKey key, out TValue value)
    {
        if (map.TryGetValue(key, out var node))
        {
            order.Remove(node);
            order.AddFirst(node);                             // O(1) move to the front: what the linked list is for
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (map.TryGetValue(key, out var existing)) order.Remove(existing);
        else if (map.Count == capacity) { map.Remove(order.Last!.Value.Key); order.RemoveLast(); }   // evict the least recently used
        map[key] = order.AddFirst((key, value));
    }
}
```

The single-flight cache stores a `Lazy<Task<decimal>>` per key. Creating a `Lazy` does nothing, so even if two threads race to add an entry, only the winner's load ever runs, and every request awaits that one task. Two details are left out for brevity and matter in real code: remove entries whose load failed, so an error isn't cached until expiry, and make the LRU cache thread-safe with a lock.

### Keys

A **cache key** must capture everything that changes the answer. For a fare, that might be origin, destination, date, cabin, currency and sales channel. For a personalized response, it must include the user, or their role or tenant. Leave one out, and one user's data is served to another, which is both a bug and a data leak. Multi-tenant systems, from Schema design and migrations (7.5), should put the tenant id first in every key.

Keep keys readable, such as `fare:LHR:JFK:2026-09-28:Y:GBP`, so they can be inspected in Redis during an incident. Version them, `v2:fare:...`, when the cached shape changes, so a deployment doesn't read entries written by the old code.

### Caches can fail too

A distributed cache is one more service that can be slow or down. Treat it as an optimization, never as the source of truth. If it fails, the application should fall back to the source, slower, rather than fail. A cache read should have a short timeout: a cache that takes 500 ms to answer is worse than no cache.

Check what the source can take without the cache. If the database can only survive with a 95% hit ratio, a cache outage takes the database down with it. Rate limits, load shedding and warming the cache on start-up are the protections, as Reliability (12.5) discusses.

### In short

A cache keeps copies of data close at hand, trading memory and freshness for speed and lower load. Cache data that is read often, expensive to produce and tolerant of being slightly old, and never use cached data for decisions that must be exact. In-process caches are fastest; distributed caches such as Redis are shared; HybridCache combines both. Cache-aside is the usual pattern. Every entry needs an expiry and every cache a size limit, with LRU the usual eviction policy. Invalidate on writes and still expire, prevent stampedes with single-flight loading and jitter, put everything that changes the answer in the key, and survive the cache failing.

### What goes wrong

**Keys that miss a dimension.** A response cached without the user or tenant in its key is served to someone else. Include every input that changes the answer.

**Unbounded caches.** A dictionary used as a cache grows until the process runs out of memory. Set size limits and expiry on every cache.

**Stampedes on expiry.** A popular entry expires at peak time and the database is flooded. Use single-flight loading, stale-while-revalidate and jittered expiry.

**Stale data after writes.** A fare changes and the old one is served for an hour. Invalidate on write, and keep expiry short enough to limit mistakes.

**Caching errors.** A failed load is cached, and every request gets the failure until expiry. Don't cache faulted results, or cache them only very briefly.

**The cache as the source of truth.** Data that exists only in Redis is lost when it restarts. Keep authoritative data in a durable store.

**A cache the system can't live without.** The cache goes down and the database collapses under the full load. Plan for a cold or missing cache.

**Per-instance caches that disagree.** Two instances serve different values for the same key for minutes. Use a distributed or two-level cache, or broadcast invalidations.

### Cost model

An in-process cache hit costs tens of nanoseconds. A Redis hit in the same data centre costs a network round trip, typically 0.2 to 1 ms, plus serialization, which grows with the size of the value. A miss costs the cache check plus the full load. With a hit ratio of h, the average cost is roughly h times the hit cost plus (1 − h) times the miss cost, so raising a hit ratio from 90% to 99% cuts database load tenfold. Memory is the price: every cached value takes space, and in-process caches add to garbage collection work.

### In other languages (optional)

**JavaScript and TypeScript.** `lru-cache` is the standard in-process LRU cache for Node.js, and clients such as ioredis and node-redis talk to Redis. Browsers cache HTTP responses themselves, and service workers can add application-controlled caching.

**Python.** `functools.lru_cache` memoizes function results in memory, and redis-py or cachetools cover the rest.

**Java.** Caffeine is a high-performance in-process cache, often behind Spring's caching annotations.

**Everywhere.** The hard parts, keys, invalidation and stampedes, are the same in every language, because they are about data, not code.

### Key terms

- **Cache**: A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Cache hit**: A lookup that finds the value in the cache.
- **Cache miss**: A lookup that doesn't find the value, so it must be loaded from the source.
- **Hit ratio**: The share of lookups that are hits.
- **Working set**: The data actually in use over a period, which a cache must hold to be effective.
- **Distributed cache**: A cache shared by all instances of an application, running as a separate service such as Redis.
- **HybridCache**: .NET's two-level cache, combining in-memory and distributed levels with built-in stampede protection.
- **Output caching**: Storing whole HTTP responses on the server and replaying them for matching requests.
- **Cache-aside**: Checking the cache, and on a miss loading from the source and storing the result.
- **Read-through cache**: A cache that loads missing values from the source itself, using a loader it was given.
- **Write-through cache**: A cache updated together with the source on every write.
- **Write-behind cache**: A cache that accepts writes immediately and writes them to the source later.
- **Absolute expiration**: Removing a cache entry at a fixed time after it was stored.
- **Sliding expiration**: Removing a cache entry after a period in which nothing read it.
- **Eviction**: Removing entries because the cache is full.
- **LRU**: Least recently used: evicting whichever entry has gone longest without being read.
- **Cache invalidation**: Removing or updating cached copies when the source data changes.
- **Cache stampede**: Many requests missing the same key at once and all loading it from the source.
- **Single-flight**: Letting only one request load a missing key while others wait for its result.
- **Stale-while-revalidate**: Serving an expired entry briefly while it is refreshed in the background.
- **Cache key**: The identifier a cached value is stored under, which must include everything that changes the value.

### Where this leads

Moving slow work out of requests entirely is in Background work and queues (8.7). Caching in the browser and data fetching libraries are in Talking to the backend (9.7). Keeping caches in step across services with events is in Messaging and events (12.3), and surviving a failed cache or overloaded database is in Reliability (12.5). Measuring hit ratios and latency in production is in Observability (11.6).

### Check yourself

1. Make `LoadFareAsync` in `caching.cs` throw for one route, and show that the protected cache keeps returning the failure. Then fix `GetAsync` so a failed load is removed from the cache.
2. Add random jitter of up to 20% to the expiry time in `FareCache`, and explain what problem it prevents.
3. Replace `FareCache` in an ASP.NET Core app with `HybridCache` from the `Microsoft.Extensions.Caching.Hybrid` package and its `GetOrCreateAsync` method.
4. Design cache keys for a flight search that depends on origin, destination, date, passenger count, cabin, currency and the customer's loyalty tier.
5. For one cache in a system you know, find its expiry, its size limit and what invalidates it, and what happens to the database if it is emptied at peak time.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The 9 am stampede

The "today's departures" cache expires at 09:00 for every instance. 2,000 requests miss at once and the database pins at 100%.

*HybridCache: single-flight plus jitter*

```csharp
builder.Services.AddHybridCache();

app.MapGet("/departures/today", async (HybridCache cache, DepartureQueries q, CancellationToken ct) =>
    await cache.GetOrCreateAsync("departures:today:v2",
        async token => await q.TodayAsync(token),            // runs once per key, others wait for it
        new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(Random.Shared.Next(0, 60)),
            LocalCacheExpiration = TimeSpan.FromSeconds(30),
        },
        tags: ["departures"], cancellationToken: ct));
```

> **Lesson:** Hot keys need single-flight loading and jittered expiry, or every miss hits the database at once.

#### Example 2: A cached response served to the wrong customer

A CDN caches `/me/bookings` as public, and customers see each other's bookings.

*Mark personal responses private*

```csharp
app.MapGet("/me/bookings", async (HttpContext http, ...) =>
{
    http.Response.Headers.CacheControl = "private, no-store";   // never in a shared cache
    ...
}).RequireAuthorization();
// public, cacheable responses must not vary by user; put the tenant/user in every server-side cache key too
```

> **Lesson:** Shared caches key on the URL. Anything personal must be private, and cache keys must include every input.

### Caching

<p class="src">From <em>.NET Backend Deep Dive</em> · Caching</p>

<a id="dn-21"></a>

#### IMemoryCache vs IDistributedCache

| | **IMemoryCache** | **IDistributedCache** |
|---|---|---|
| Storage | The process's own RAM | Redis / SQL Server / NCache |
| Speed | Nanoseconds (an object reference) | ~0.5–2ms (network + deserialization) |
| Stores | **Live .NET objects**, no serialization | `byte[]` — serialization required |
| Multiple instances | **Each instance has its own copy → divergent data** | Shared and consistent |
| Survives restart | No | Yes |
| Limit | The pod's RAM → OOM risk | The size of your Redis |

The classic scaling failure: three pods, each with its own `IMemoryCache`. You update a record and invalidate on pod A; pods B and C keep serving stale data. The user refreshes and sees the answer flip back and forth.

```csharp
// IMemoryCache
_cache.Set("user:1", user, new MemoryCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
    SlidingExpiration = TimeSpan.FromMinutes(2),
    Size = 1,                            // required if you configured SizeLimit
    Priority = CacheItemPriority.Normal
});

// IDistributedCache (Redis)
builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = cs;
    o.InstanceName = "orderapi:";
});
await _dist.SetStringAsync("user:1", JsonSerializer.Serialize(user),
    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
```

<a id="dn-22"></a>

#### Cache stampede (thundering herd)

**The scenario:** the key `top-products` expires at 12:00:00. At that exact moment 5,000 requests arrive. All 5,000 miss, all 5,000 fire the same expensive query, and the database falls over.

Three variants of the problem:

1. **Expiry stampede** (above).
2. **Cold start**: after a deploy the cache is empty and all traffic hits the database.
3. **Synchronized expiry**: 10,000 keys loaded at startup all expire ten minutes later, together.

**Mitigations:**

```csharp
// 1) Per-key locking (in-process) - the simplest fix
private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
{
    if (_cache.TryGetValue(key, out T? cached)) return cached!;

    var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync();
    try
    {
        if (_cache.TryGetValue(key, out cached)) return cached!;   // double-check
        var value = await factory();
        _cache.Set(key, value, Jitter(ttl));
        return value;
    }
    finally { gate.Release(); }
}

// 2) Jitter - spread expiry times so keys don't die together
static TimeSpan Jitter(TimeSpan ttl) =>
    ttl + TimeSpan.FromSeconds(Random.Shared.Next(0, (int)(ttl.TotalSeconds * 0.2)));

// 3) Distributed lock (Redis) when you have many pods
//    SET lock:key <token> NX PX 5000  -> only one pod refreshes

// 4) Stale-while-revalidate: serve the old value immediately, refresh in the background.
//    Store a "soft expiry" earlier than the "hard expiry".
```

<a id="dn-23"></a>

#### HybridCache (.NET 9+) — solves all of the above

`HybridCache` (package `Microsoft.Extensions.Caching.Hybrid`) is a two-tier abstraction:

- **L1** = in-memory (fast)
- **L2** = distributed / Redis (consistent)
- **Built-in stampede protection**: concurrent requests for the same key invoke the factory once; the rest await that result.
- **Tag-based invalidation**: evict groups of keys.

```csharp
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration           = TimeSpan.FromMinutes(10),   // L2 (distributed)
        LocalCacheExpiration = TimeSpan.FromMinutes(2)     // L1 (memory) - shorter, less staleness
    };
});
```

```csharp
public class ProductService
{
    private readonly HybridCache _cache;
    private readonly IProductRepo _repo;

    public async Task<Product?> GetAsync(int id, CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(
            $"product:{id}",
            (id, _repo),                                   // state -> avoids closure allocation
            static async (state, ct) => await state._repo.GetAsync(state.id, ct),
            tags: ["products", $"product:{id}"],
            cancellationToken: ct);
    }

    public async Task UpdateAsync(Product p, CancellationToken ct)
    {
        await _repo.UpdateAsync(p, ct);
        await _cache.RemoveAsync($"product:{p.Id}", ct);
        // or evict the whole group:
        await _cache.RemoveByTagAsync("products", ct);
    }
}
```

To keep every pod's L1 in sync on invalidation, configure a backplane (Redis pub/sub) through the L2 implementation.

<a id="dn-24"></a>

#### Caching strategies

| Pattern | How it works | Use when |
|---|---|---|
| **Cache-aside** (lazy) | App reads the cache, and on a miss queries the DB and populates it | The default, 95% of cases |
| **Read-through** | The cache layer loads for you | When your library supports it (HybridCache) |
| **Write-through** | Write to DB and cache together | Reads immediately after writes must be fresh |
| **Write-behind** | Write the cache first, flush to the DB later | Very high write throughput, tolerable data-loss risk |
| **Refresh-ahead** | Refresh before expiry | Hot keys with expensive queries |

**Principles:**

- A cache is an **optimization, not a source of truth**. If Redis dies, the app must still work — just slower.
- **Invalidation is harder than caching.** Prefer short TTLs over elaborate invalidation logic.
- **Never cache per-user data under a shared key** — it leaks one user's data to another. This is a real, common security bug.
- Track your **hit ratio**. Below 80% and the cache is costing more than it saves.

---

### HTTP Caching & CDN

<p class="src">From <em>.NET Backend Deep Dive</em> · HTTP Caching & CDN</p>

<a id="dn-170"></a>

#### The headers that matter

Part 6 is server-side caching. This is the layer *in front* of your service — often the cheapest performance win available, because the fastest request is the one that never reaches you.

```http
Cache-Control: public, max-age=300, stale-while-revalidate=60
ETag: "a7f3c9e1"
Vary: Accept-Encoding, Authorization
```

| Directive | Meaning |
|---|---|
| `public` | Any cache may store it (CDN, proxy) |
| `private` | **Browser only** — required for anything user-specific |
| `no-store` | Never persist. For genuinely sensitive responses |
| `no-cache` | Store it, but always revalidate before use (not "don't cache") |
| `max-age=N` | Fresh for N seconds |
| `s-maxage=N` | Overrides `max-age` **for shared caches only** — let the CDN hold it longer than the browser |
| `stale-while-revalidate=N` | Serve stale instantly, refresh in the background |
| `must-revalidate` | Never serve stale, even if the origin is down |

**`no-cache` does not mean "don't cache."** That's `no-store`. This confusion causes both accidental caching of private data and pointless cache misses.

<a id="dn-171"></a>

#### Conditional requests — bandwidth for free

```csharp
var etag = $"\"{order.RowVersion.ToHexString()}\"";

if (Request.Headers.IfNoneMatch == etag)
    return StatusCode(StatusCodes.Status304NotModified);   // no body sent

Response.Headers.ETag = etag;
Response.Headers.CacheControl = "private, max-age=30";
return Ok(dto);
```

A 304 carries headers only. On a mobile client polling a list endpoint, that's the difference between 200KB and 200 bytes per poll.

**ETags also give you optimistic concurrency over HTTP** — the same mechanism as `RowVersion` in EF (Part 5.4):

```csharp
// Client sends the ETag it read; the server rejects a stale write
if (Request.Headers.IfMatch != currentEtag)
    return StatusCode(StatusCodes.Status412PreconditionFailed);
```

This is how you prevent lost updates in a REST API without inventing a version field.

<a id="dn-172"></a>

#### The cache key, and how it leaks data

A shared cache keys on **method + URL + the headers named in `Vary`**. Two failures follow:

- **Forgetting `Vary: Authorization`** on a `public` response means user A's data gets served to user B from the CDN. This is a real, repeatedly-observed production incident, not a theoretical one. Anything user-specific must be `private`, or `Vary` correctly.
- **`Vary: User-Agent`** effectively disables caching — there are thousands of distinct values, so the hit rate collapses.

Query-string handling matters too: tracking parameters (`utm_*`) create distinct keys for identical content. Configure the CDN to ignore them.

<a id="dn-173"></a>

#### What to cache where

| Content | Strategy |
|---|---|
| Static assets with hashed filenames (`app.a7f3c9.js`) | `public, max-age=31536000, immutable` — cache forever, the hash is the version |
| `index.html` / entry points | `no-cache` — must revalidate, or users run old code against a new API |
| Public reference data (countries, product catalog) | `public, s-maxage=3600` at the CDN |
| User-specific API responses | `private, max-age=0` + ETag |
| Anything with a token in it | `no-store` |

**Invalidation is the same hard problem as Part 6.4, plus propagation delay.** Prefer content-hashed URLs (a new URL needs no invalidation) over purging. When you must purge, remember a global CDN purge is typically seconds-to-minutes, not instant.

---

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a101"></a>

#### Caching: layers, keys and invalidation

Cache to remove work, not to hide a slow query you have not looked at. Add caching after you have measured, and always with an expiry.

``` text
  browser cache  ->  CDN  ->  response cache  ->  in-memory (IMemoryCache)
                                              ->  distributed (Redis)
                                                            ->  database

  closer to the user = faster and cheaper, but staler and harder to invalidate
```

``` csharp
// distributed cache-aside with a stampede guard
public async Task<Product?> GetAsync(int id, CancellationToken ct)
{
    var key = $"product:v2:{id}";
    if (await _cache.GetStringAsync(key, ct) is { } json)
        return JsonSerializer.Deserialize<Product>(json);

    await _gate.WaitAsync(ct);                       // one loader per process
    try
    {
        if (await _cache.GetStringAsync(key, ct) is { } j2)     // double-check
            return JsonSerializer.Deserialize<Product>(j2);

        var p = await _db.Products.FindAsync([id], ct);
        if (p is not null)
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(p),
                new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
        return p;
    }
    finally { _gate.Release(); }
}
```

| Problem          | Symptom                                        | Mitigation                                   |
|------------------|------------------------------------------------|----------------------------------------------|
| Stampede         | Cache expires, 500 requests hit the DB at once | Lock per key, or stale-while-revalidate      |
| Stale data       | User updates something and sees the old value  | Evict on write; short TTLs; version the key  |
| Unbounded growth | Memory climbs until the pod is OOM-killed      | Size limits and eviction policy              |
| Key collisions   | Wrong tenant's data returned                   | Include tenant and schema version in the key |

> **\[Note\]**
>
> **HybridCache.** .NET 9 ships `HybridCache`, which combines an in-process L1 with a distributed L2 and handles stampede protection and tagging for you. Prefer it over hand-rolling the above.

###### Worked example

Cache invalidation strategies, ranked by how much trouble they cause in practice.

| Strategy                  | Staleness                   | Complexity | When to use it                                                       |
|---------------------------|-----------------------------|------------|----------------------------------------------------------------------|
| Short TTL only            | Up to the TTL               | None       | The default. A 30-second TTL solves more problems than people expect |
| Evict on write            | None, within one instance   | Low        | Single writer, or a pub/sub fan-out to other instances               |
| Version in the key        | None — old keys just expire | Low        | Deploy-scoped data, reference tables, schema changes                 |
| Tag-based eviction        | None                        | Medium     | One write invalidates many derived entries                           |
| Write-through             | None                        | Medium     | The cache is the read path and must never be stale                   |
| Event-driven invalidation | Near-none                   | High       | Distributed, multiple writers, cross-service                         |

``` csharp
// HybridCache does L1 + L2, stampede protection and tagging in one API
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration      = TimeSpan.FromMinutes(10),   // L2 (distributed)
        LocalCacheExpiration = TimeSpan.FromMinutes(1) // L1 (in-process, shorter on purpose)
    };
});

public async ValueTask<Product> GetAsync(int id, CancellationToken ct) =>
    await _cache.GetOrCreateAsync(
        $"product:{id}",
        async token => await _db.Products.FindAsync([id], token),
        tags: ["products", $"category:{categoryId}"],
        cancellationToken: ct);

// one write invalidates everything derived from that category, everywhere
await _cache.RemoveByTagAsync($"category:{categoryId}", ct);
```

The L1 expiry being shorter than L2 is deliberate and worth understanding: L1 is per-process and cannot be invalidated from another node, so it must age out quickly. L2 is shared, so it can be invalidated centrally and can live longer. That asymmetry is exactly what a hand-rolled two-level cache usually gets wrong.

###### Key design, and the bugs that come from getting it wrong

``` csharp
// a key that will cause an incident
var key = $"user:{userId}";

// a key that will not
var key2 = $"v3:{tenantId}:user:{userId}:profile";
//          ^    ^          ^            ^
//          |    |          |            what this is
//          |    |          the entity and its id
//          |    tenant isolation - never optional in multi-tenant systems
//          schema version - bump it and every old entry becomes unreachable,
//          which is your zero-downtime cache migration story
```

| Missing from the key | The incident it causes                                                          |
|----------------------|---------------------------------------------------------------------------------|
| Tenant               | Cross-tenant data exposure — reportable                                         |
| Version              | A deploy changes the DTO shape and deserialization throws on every cached entry |
| Culture / locale     | A user sees another language                                                    |
| User or role         | A user sees data they are not authorized for                                    |
| Query parameters     | Page 2 returns page 1's results                                                 |

A corollary: never cache a response body that varies by authorization without the identity in the key. Output caching and CDN caching are especially dangerous here, because the cache is in front of your authorization middleware entirely.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · Caching · Ground Floor</p>

<a id="be-f559"></a>

#### Why caching works at all

Two independent kinds of unevenness.

**Access costs vary by orders of magnitude.** Same table as the async doc, because it's the same hierarchy:

| Store                          | Latency  | Ratio to L1  |
|--------------------------------|----------|--------------|
| CPU register                   | ~0.3 ns  | 1×           |
| L1 cache                       | ~1 ns    | 3×           |
| L3 cache                       | ~15 ns   | 50×          |
| Main memory                    | ~80 ns   | 250×         |
| In-process cache (hash lookup) | ~100 ns  | 300×         |
| Redis, same datacenter         | ~0.5 ms  | 1,500,000×   |
| SSD random read                | ~100 µs  | 300,000×     |
| Database query (indexed)       | ~1–10 ms | 3,000,000×   |
| Cross-region network           | ~80 ms   | 250,000,000× |

Note something counterintuitive: **Redis is slower than local disk.** A network hop to another machine's RAM costs more than reading your own SSD. Redis wins over a database not because RAM beats disk, but because a `GET` beats query parsing, planning, locking, and joins. If you're caching something computed in 50 µs, Redis makes it *slower*.

**Access frequencies are skewed.** Real workloads follow a power law — roughly Zipf. The most popular item gets ~2× the traffic of the second, ~3× the third, and so on. In practice: **the top 1% of items often serve 30–50% of requests, and the top 20% serve 80%.**

This is what makes a small cache viable. You don't need to hold everything; you need to hold the head of the distribution.

**The corollary is the most useful diagnostic in this document:** if your access pattern is uniform — random UUID lookups, one-shot analytics scans, unique per-user reports — **caching cannot help**, no matter how large the cache. No skew, no locality, no hits. Before adding a cache, ask whether the distribution has a head.

<a id="be-f560"></a>

#### The cost model

Average latency with a cache:

``` text
avg = hit_rate × cache_latency + (1 − hit_rate) × (cache_latency + source_latency)
    ≈ source_latency × (1 − hit_rate)        when cache_latency is negligible
```

The important consequence is that **improvement scales with the miss rate, not the hit rate**, so gains are wildly non-linear at the top end:

| Hit rate | Miss rate | Avg latency (source = 10 ms) |
|----------|-----------|------------------------------|
| 0%       | 100%      | 10 ms                        |
| 50%      | 50%       | 5.0 ms                       |
| 90%      | 10%       | 1.0 ms                       |
| 95%      | 5%        | 0.5 ms                       |
| 99%      | 1%        | 0.1 ms                       |
| 99.9%    | 0.1%      | 0.01 ms                      |

Going from 50% → 90% halves latency 5×. Going from 90% → 99% halves it another 10×. **Every 9 you add to the hit rate is worth as much as all the previous ones combined.** This is why obsessing over the last few percent is rational, and why a cache at 60% hit rate is usually a sign of a wrong key design rather than a small cache.

But also: **the tail is what users feel.** A 99% hit rate means 1 in 100 requests pays full price. If your p99 SLA matters, that miss is your p99. Caching improves the average dramatically and the tail not at all — sometimes it makes the tail *worse*, because a miss now costs the cache lookup plus the source.

<a id="be-f561"></a>

#### What a cache actually is

Strip away the products and every cache is the same four things:

1.  **A key→value map**, usually a hash table
2.  **A bound** — memory, entry count, or both
3.  **An eviction policy** — what to drop when full
4.  **An expiry policy** — when an entry stops being trustworthy

Everything else is packaging. `Dictionary<K,V>` plus a size limit plus a rule is a cache. Redis is that, over a network, with data structures and persistence bolted on.

The distinctions that matter operationally:

|                         | In-process            | Distributed       | HTTP/CDN                 |
|-------------------------|-----------------------|-------------------|--------------------------|
| Latency                 | ~100 ns               | ~0.5 ms           | 0 (no request at all)    |
| Shared across instances | ❌                    | ✅                | ✅                       |
| Survives restart        | ❌                    | ✅                | ✅                       |
| Serialization cost      | none                  | every access      | every access             |
| Invalidation            | hard (N copies)       | easy (one place)  | hardest (no control)     |
| Failure mode            | none — it's just gone | dependency outage | stale content everywhere |

<a id="be-f562"></a>

#### The original cache: your CPU

Worth two paragraphs because it's where all the intuitions come from and because it silently governs your code's speed.

When the CPU reads one byte from RAM, it fetches a whole **64-byte cache line**. So reading an array sequentially gets 63 bytes free; reading a linked list scattered across the heap pays full price every time. This is why an array of structs outperforms a list of objects by 5–10× on the same algorithm — same big-O, radically different cache behavior.

**False sharing** is the sharp edge: two threads writing to different variables that happen to sit in the same 64-byte line will invalidate each other's cache line on every write, serializing threads that share nothing logically. The fix is padding — and it's a real, measurable problem in high-throughput counters.

The lesson that generalizes upward: **caches reward locality and punish randomness**, at every level from silicon to CDN.

<a id="be-f563"></a>

#### The two hard problems

> There are only two hard things in Computer Science: cache invalidation and naming things.
>
> — Phil Karlton

The joke is precise, and both halves are about caches:

- **Naming things** is the cache key. What counts as "the same data"? Get it wrong and you either miss constantly (keys too specific) or serve the wrong data to the wrong user (keys too broad). The second is a security incident, not a bug.
- **Invalidation** is Fact 2. Knowing *when* a copy became wrong requires knowing about every write path everywhere, which is exactly the coupling caching was supposed to avoid.

Everything in Parts II and III is one of these two.

------------------------------------------------------------------------

### Anatomy

<p class="src">From <em>Backend Engineering</em> · Caching · Anatomy</p>

<a id="be-f564"></a>

#### Keys

The key defines identity. Design it before anything else.

**Include everything that changes the value.** The classic failure:

``` yaml
key: "user:profile:42"           → but the response differs by locale
```

User 42 in French gets the English cached copy. The fix is to include the varying dimension:

``` yaml
key: "user:profile:42:fr-FR:v3"
```

**Include a version.** `:v3` above is the single highest-leverage trick in caching, covered properly in II.4: you never invalidate, you change the key.

**Exclude everything that doesn't.** Every extra dimension multiplies the key space and divides your hit rate. A key including a request ID has a hit rate of exactly zero.

**Namespace it.** `user:42` and `order:42` must not collide. A prefix convention (`entity:id:variant:version`) makes keys greppable, scannable, and safe to bulk-delete by pattern.

**Never put unbounded user input in a key.** `search:{query}` where query is free text means an attacker can fill your cache with garbage, evicting everything real. Hash it, cap the length, or allow-list.

**Watch the security boundary.** The most serious caching bug class is a key that omits the identity dimension:

``` yaml
key: "dashboard"                 → every user gets the first user's dashboard
```

For anything user-specific, the user or tenant ID belongs in the key — or the response must not be cached in a shared cache at all. This is the same bug as an HTTP `Cache-Control: public` on an authenticated response, and it leaks data across accounts.

<a id="be-f565"></a>

#### Eviction policies

When the cache is full, something must go.

**Belady's optimal** — evict whatever will be used furthest in the future. Provably optimal, requires knowing the future, so it exists only as a benchmark ceiling.

**LRU (least recently used)** — evict the coldest by last access. Good default, matches temporal locality. Weakness: **scan resistance.** One pass over a million rows evicts your entire working set, because each scanned item is momentarily "most recent."

**LFU (least frequently used)** — evict by access count. Handles scans well. Weakness: **cache pollution** — an item popular last month keeps a slot forever. Needs aging/decay to be usable.

**FIFO** — evict oldest inserted. Simple, ignores usage, generally worse than LRU. Occasionally right when items have uniform value.

**Random** — evict anything. Sounds bad; performs surprisingly close to LRU, with no bookkeeping. Used where metadata cost matters.

**W-TinyLFU** — the modern answer, used by Caffeine (Java) and increasingly elsewhere. A small LRU admission window in front of a frequency-based main cache, with a compact probabilistic frequency sketch (Count-Min) and periodic aging. Gets LFU's scan resistance without LFU's pollution, at a few bits of overhead per item. If your library offers it, take it.

**ARC** — adaptively balances recency and frequency. Excellent; historically encumbered by patents, which is why it's rarer than it deserves.

**TTL-only** — no size-based eviction at all, entries just expire. This is not an eviction policy, it's the absence of one, and it means **your cache is unbounded**. Fine for a fixed small key space, a memory leak otherwise.

Practical guidance: use LRU unless you have scans, use W-TinyLFU if available, and always set a size bound even when you have TTLs.

<a id="be-f566"></a>

#### Sizing

Hit rate versus cache size is a **logarithmic** curve: steep at first, then flat. Doubling from 1% to 2% of the working set might take you 40% → 60%; doubling from 40% to 80% might take you 92% → 94%.

This means the useful question isn't "how big should the cache be" but "where is the knee?" Measure it: run with several sizes and plot. Most systems find that a cache holding **5–20% of the working set achieves 80–95% hit rate**, and everything beyond is expensive marginal return.

Sizing traps:

- **Counting entries, not bytes.** 10,000 entries is meaningless if entries range from 100 bytes to 10 MB. Bound by memory where you can.
- **Forgetting overhead.** A .NET object has a 16-byte header; a `string` key of 30 chars is ~90 bytes; a `MemoryCache` entry carries its own metadata. Small values can be 3× their nominal size.
- **Ignoring GC pressure.** A large in-process cache full of long-lived objects is, by definition, a large gen-2 heap. It shifts cost from latency to GC pauses. This is the main argument for out-of-process caches at large sizes.

<a id="be-f567"></a>

#### What to measure

| Metric                  | Why                                                              |
|-------------------------|------------------------------------------------------------------|
| **Hit rate**            | the headline; measure per key-prefix, not globally               |
| **Miss latency**        | what a miss actually costs — the number that justifies the cache |
| **Eviction rate**       | high evictions + low hit rate = undersized                       |
| **Expiry vs eviction**  | expiring is healthy; evicting means pressure                     |
| **Memory used / limit** | for headroom                                                     |
| **Key cardinality**     | growing without bound = key design bug                           |
| **Stampede count**      | concurrent misses on the same key                                |

**Measure hit rate per key prefix.** A global 85% can hide a 99% on one hot prefix and 20% on the endpoint you're actually trying to fix. Aggregate hit rate is the caching equivalent of an average latency.

------------------------------------------------------------------------

### One Request Through Every Layer

<p class="src">From <em>Backend Engineering</em> · Caching · One Request Through Every Layer</p>

<a id="be-f568"></a>

#### One Request Through Every Layer

A user loads a product page. Here is every cache it can hit, in order, with what each costs and what each can get wrong.

**Layer 1 — Browser memory cache.** ~0 ms. Assets from this page load already in RAM. Lives until the tab closes. Can be wrong only within one page session.

**Layer 2 — Service worker.** ~1 ms. JavaScript you wrote, running before the network. Total control, which means total responsibility: a bad service worker can serve a broken app persistently and survive reloads. The one cache layer that can brick a site.

**Layer 3 — Browser HTTP disk cache.** ~5 ms. Governed by `Cache-Control` from the response that stored it. Survives restarts. **You cannot purge it** — once a browser has cached something for a year, it's cached for a year. This is why content hashing exists (Part IX.2).

**Layer 4 — CDN edge, ~20 ms away.** Shared across all users near that POP. A hit here means the request never crosses an ocean. Can be purged, usually within seconds.

**Layer 5 — Origin shield / tiered cache.** One regional node in front of your origin, so 200 edge POPs missing simultaneously produce one origin request instead of 200.

**Layer 6 — Reverse proxy (nginx, Varnish).** ~1 ms from origin. Often does compression and micro-caching.

**Layer 7 — Application in-process cache.** ~100 ns. Fastest thing available, and **per instance** — with 10 pods you have 10 divergent copies.

**Layer 8 — Redis.** ~0.5 ms. Shared across instances. Survives deploys. The cost is serialization on every access and a network dependency.

**Layer 9 — Database buffer pool.** ~1 ms. The database's own cache of pages in RAM, managed for you. Usually the reason a "slow" query is fast the second time.

**Layer 10 — Disk.** ~100 µs–10 ms. The actual source of truth.

**The full-miss path** touches every layer: ~250 ms. **The full-hit path** at layer 1: ~0 ms.

Two things worth extracting from this.

**Each layer multiplies protection.** If every layer achieves an unimpressive 80% hit rate, only 0.8¹⁰ ≈ **0.01% of requests** reach disk. Layered mediocre caches beat one excellent cache.

**Each layer is another divergence window, and they compose badly.** Publish a fix, purge the CDN, and users still see the old page for a year because a browser cached it with `max-age=31536000`. The layer you control least is the one closest to the user. **Cache duration should generally decrease as you get closer to the user, unless the content is immutable and content-addressed** — which is exactly the pattern in Part IX.2.

------------------------------------------------------------------------

### Staleness and Invalidation

<p class="src">From <em>Backend Engineering</em> · Caching · Staleness and Invalidation</p>

<p class="pd">All of this is Fact 2: the copy can be wrong.</p>

<a id="be-f569"></a>

#### The divergence window

From the moment the source changes until every copy reflects it, the system is serving wrong data. That interval is the **divergence window**, and the only real decision in caching is how long you accept it.

Be explicit. "How stale can this be?" has a defensible answer per data type:

| Data                | Acceptable staleness                       |
|---------------------|--------------------------------------------|
| Account balance     | 0 — don't cache, or cache per-request only |
| Permissions / roles | seconds — revocation must take effect      |
| Product price       | seconds to a minute                        |
| Product description | minutes                                    |
| Category tree       | hours                                      |
| Country list        | days                                       |
| Hashed static asset | forever — it's immutable by construction   |

Notice the last row. If content can't change, staleness is impossible. **Making things immutable is the only way to actually eliminate the problem** rather than bound it — which is why content-addressed keys are the strongest pattern in this document.

<a id="be-f570"></a>

#### TTL

The blunt instrument, and the right default.

Its virtue is that it needs no coordination: no write path has to know the cache exists. Its vice is that it's wrong in both directions — data that didn't change still expires (wasted misses), and data that changed stays served (staleness).

**Always jitter your TTLs.** Setting every entry to exactly 300 seconds means everything cached during a traffic spike expires together, producing a synchronized stampede five minutes later (III.3):

``` csharp
var ttl = TimeSpan.FromSeconds(300 * (0.9 + Random.Shared.NextDouble() * 0.2));  // ±10%
```

**Absolute vs sliding.** Absolute expiry ("300s from creation") bounds staleness. Sliding expiry ("300s from last access") keeps hot items forever — which means it does **not** bound staleness at all, and a popular stale entry can persist indefinitely. Use sliding only for things that never change; combine with an absolute cap otherwise.

<a id="be-f571"></a>

#### Explicit invalidation

Delete the entry when the underlying data changes. Precise, and hard for exactly the reason Karlton meant: it requires every write path to know about every cached representation.

``` csharp
await _db.SaveAsync(user);
await _cache.RemoveAsync($"user:{user.Id}");
await _cache.RemoveAsync($"user:{user.Id}:profile");
await _cache.RemoveAsync($"team:{user.TeamId}:members");   // ← the one you'll forget
```

The third line is the problem. Derived and aggregate keys are invisible from the write site, and the failure is silent — nobody gets an error, some users just see old data.

Mitigations: centralize invalidation next to the write (a domain event handler rather than scattered calls), use key prefixes so you can invalidate families, and **keep a TTL as a backstop** so a missed invalidation self-heals rather than persisting forever.

**In a distributed system, invalidation is itself a distributed operation** and can fail. If your cache delete is fire-and-forget after a database commit, some fraction will not happen. Which brings us to the most reliable alternative.

<a id="be-f572"></a>

#### Versioned keys — don't invalidate, change the name

The strongest idea in this document.

Instead of deleting `user:42`, write to `user:42:v7`. The old entry is now unreachable and will be evicted naturally.

``` text
user:42:v6   ← old, orphaned, evicted eventually
user:42:v7   ← current
```

Why it's better:

- **Atomic.** The version bump publishes the new value; there's no window where the cache is empty and everyone stampedes the database.
- **No delete to fail.** You never depend on a purge succeeding.
- **Works across every layer at once.** Change the key and CDN, browser, Redis, and in-process caches all miss simultaneously and correctly.
- **Rollback is free.** Point the version back.

The version can be a row version/ETag from the database, a global counter, a content hash, or a deploy ID.

This is precisely the frontend's `main.a1b2c3.js` pattern (IX.2), the same idea applied to assets. When people say "cache invalidation is hard," the practical answer is usually **stop invalidating and start versioning.**

<a id="be-f573"></a>

#### Write patterns

| Pattern                | Write goes to                      | Reads                         | Trade-off                                       |
|------------------------|------------------------------------|-------------------------------|-------------------------------------------------|
| **Cache-aside** (lazy) | DB only; cache filled on read miss | app checks cache, then DB     | simple, dominant; first read is always a miss   |
| **Read-through**       | DB only                            | cache library fetches on miss | same as above, hidden in the library            |
| **Write-through**      | cache **and** DB, synchronously    | always hot                    | writes slower; caches data nobody reads         |
| **Write-behind**       | cache now, DB async later          | always hot                    | fastest writes; **data loss if the cache dies** |
| **Write-around**       | DB only, cache untouched           | fills on next read            | good for write-heavy data rarely read           |
| **Refresh-ahead**      | —                                  | refreshed before expiry       | no user-facing miss; wasted work on cold items  |

**Cache-aside is the default and the right starting point.** It's the pattern in nine of ten systems:

``` csharp
var cached = await _cache.GetAsync(key);
if (cached is not null) return cached;

var value = await _db.LoadAsync(id);
await _cache.SetAsync(key, value, ttl);
return value;
```

Write-behind deserves a warning: it makes the cache the system of record between the write and the flush. If Redis restarts in that window, acknowledged writes are gone. Only acceptable with persistence, replication, and an explicit tolerance for loss.

<a id="be-f574"></a>

#### The cache-aside race

The code above has a real bug, and it's subtle enough that most implementations carry it.

``` text
1. Reader   : GET cache user:42        → miss
2. Reader   : SELECT from DB           → V1
3. Writer   : UPDATE DB                → V2
4. Writer   : DELETE cache user:42     → no-op, key isn't there yet
5. Reader   : SET cache user:42 = V1   → stale value, cached for the full TTL
```

The reader writes a value it read *before* the update, *after* the invalidation. The cache now holds V1 until TTL expiry — potentially hours.

The window is small but nonzero, and it's exactly the kind of bug that appears once a week under load and can't be reproduced.

Mitigations, roughly in order of practicality:

- **Short TTL.** Bounds the damage. Usually enough.
- **Delayed double delete.** Delete, write to the DB, wait a beat, delete again. Ugly, effective, widely used.
- **Versioned keys (II.4).** The race disappears — the reader writes to `v6`, which nobody reads anymore.
- **CAS / conditional set.** Only set if the version you read is still current.
- **Change-data-capture.** Invalidate from the database's replication log rather than from application code. Correct and heavier; this is what large systems converge on.

<a id="be-f575"></a>

#### The dual-write problem

More general than the cache-aside race: **you are writing to two systems without a transaction across them.**

``` csharp
await _db.SaveAsync(order);      // succeeds
await _cache.RemoveAsync(key);   // process crashes here
```

The database moved on, the cache didn't, and nothing will ever fix it except the TTL.

The general solutions are the same as in distributed systems generally: the **outbox pattern** (write the invalidation intent in the same DB transaction, publish it asynchronously), or **CDC** (derive invalidations from the DB's write-ahead log, so they cannot be missed). Both make invalidation eventually reliable rather than best-effort.

For most systems, the honest answer is: use a TTL as a backstop and accept a bounded window. Reach for outbox/CDC when the window's cost exceeds the complexity.

------------------------------------------------------------------------

### Failure Modes

<p class="src">From <em>Backend Engineering</em> · Caching · Failure Modes</p>

<p class="pd">These are the ways caches take down systems. Almost all of them appear only under load, which is why they surprise people in production and never in staging.</p>

<a id="be-f576"></a>

#### Stampede (dogpile, thundering herd)

A hot key expires. 500 concurrent requests miss simultaneously. All 500 hit the database with the same query.

The database, sized for the *cached* load, now takes 500× that. It slows, so requests queue, so more arrive, so it slows further. The cache made you *more* fragile, because your capacity planning assumed the cache was there.

**Request coalescing (single-flight)** is the primary fix — one caller computes, the rest wait for that result:

``` csharp
private readonly ConcurrentDictionary<string, Lazy<Task<T>>> _inflight = new();

public Task<T> GetAsync(string key, Func<Task<T>> factory) =>
    _inflight.GetOrAdd(key, _ => new Lazy<Task<T>>(async () =>
    {
        try { return await factory(); }
        finally { _inflight.TryRemove(key, out _); }
    })).Value;
```

`Lazy<T>` matters here: `GetOrAdd`'s factory can run on multiple threads, but only one `Lazy` value is ever materialized, so the expensive work happens once. In .NET 9+, `HybridCache` does this for you.

Other mitigations:

- **Probabilistic early expiration (XFetch).** Each reader independently decides to refresh slightly before expiry, with probability rising as expiry approaches. One reader usually refreshes early and alone; no lock needed.
- **Serve stale while revalidating.** Return the expired value immediately, refresh in the background. Users never wait; this is `stale-while-revalidate` (VII.5) applied server-side.
- **Distributed lock** across instances. Correct, but adds a dependency and a failure mode of its own (V.7).

<a id="be-f577"></a>

#### Penetration — caching misses

Requests for keys that don't exist bypass the cache entirely and hit the database every time. If an attacker requests random IDs, your cache is irrelevant and your database takes 100% of the load. This is a cheap and effective DoS.

Fixes:

- **Cache the negative result.** Store a null/sentinel with a short TTL (30–60s). Must be a distinct sentinel, not an absent key, or you can't tell "known missing" from "not cached."
- **Bloom filter** in front. A compact probabilistic set of all valid IDs. It can produce false positives (says present when absent) but never false negatives, so a "no" is definitive and rejects instantly.
- **Validate the key shape.** If IDs are UUIDs, reject anything that isn't one before touching any store.

<a id="be-f578"></a>

#### Avalanche — synchronized expiry

Many keys expire at the same instant, usually because they were all created together — a deploy, a cache warm-up, a traffic spike, or a restart. Everything misses at once.

Also: **the cache itself going down.** Redis restarts, and 100% of traffic hits the database simultaneously. If the database can't serve uncached load, the site is down until the cache refills — and it can't refill because the database is saturated. This is a self-sustaining outage.

Fixes: **jitter every TTL** (II.2), warm caches gradually rather than all at once, and — most importantly — **know whether your system survives a cold cache.** Test it. If the answer is no, the cache isn't an optimization; it's a load-bearing dependency, and it needs the availability engineering of one (V.6).

<a id="be-f579"></a>

#### Hot keys

One key gets a disproportionate share of traffic — a viral post, a global config, a celebrity account. Even in Redis, one key lives on one shard, so that shard saturates while others idle. Sharding does not help, because the key is atomic.

Fixes: **replicate the hot key locally** in each app instance with a short TTL (this is where in-process caching genuinely shines), or **split the key** into `config:1..N` variants and pick randomly, or put a small local cache in front of the distributed one — the two-tier pattern in IV.4.

<a id="be-f580"></a>

#### Poisoning

Wrong data enters the cache and is served to everyone until expiry.

Common causes: a key missing a dimension (user, tenant, locale) so one user's data is served to all; a partial failure cached as if it were a real result; or an upstream error page cached with a 200 status.

Rules: **never cache a response you didn't fully validate**, never cache 5xx responses, cache 404s only briefly and deliberately, and treat any key-design change as a security-relevant review.

<a id="be-f581"></a>

#### Unbounded growth

A cache with TTLs but no size limit is a memory leak with a delay. Growth is bounded only by key cardinality, and key cardinality is usually bounded by user input.

Always set a maximum. In .NET's `MemoryCache` this means both `SizeLimit` on the cache **and** a `Size` on every entry — omit the latter and entries are unbounded regardless of the limit. In Redis, set `maxmemory` and a `maxmemory-policy`; the default `noeviction` means writes start *failing* when full rather than making room, which surprises people badly.

<a id="be-f582"></a>

#### Cold start

After a deploy or restart, an empty cache means every request is a miss. Latency spikes, and if load is high enough you get an avalanche.

Mitigations: warm critical keys on startup (gradually, jittered), use a distributed cache that survives instance restarts, roll deploys so only a fraction of instances are cold, or keep a persistent local cache tier.

<a id="be-f583"></a>

#### When the cache becomes load-bearing

The most important thing in this section is a question rather than a technique:

> **If the cache disappeared right now, would the system survive?**

If yes, it's an optimization. Fail open — on a cache error, go to the source, log it, move on.

If no, it is a **critical dependency wearing an optimization's clothes**, and it needs replication, failover, monitoring, and a tested recovery plan. Many teams discover which one they have during an incident.

Failing open is the right default, but note the trap: if the cache is down and every request falls through, you've converted a cache outage into a database outage. **Fail open plus a circuit breaker plus load shedding** is the honest configuration — serve stale, shed excess, degrade deliberately.

------------------------------------------------------------------------

### Backend: In-Process Caching

<p class="src">From <em>Backend Engineering</em> · Caching · Backend: In-Process Caching</p>

<a id="be-f584"></a>

#### What it's for

Fastest possible: a hash lookup, no serialization, no network. ~100 ns.

The cost is that it's **per instance**. With 10 pods you have 10 caches, 10 divergence windows, and no way to invalidate them centrally without a message bus. Memory is multiplied by instance count.

Right for: small, hot, slow-changing data — configuration, feature flags, reference tables, compiled objects (regexes, parsed schemas, serializer metadata), and hot-key replication (III.4).

Wrong for: anything user-specific at scale (cardinality explodes), anything requiring prompt invalidation, anything large.

<a id="be-f585"></a>

#### .NET specifics

``` csharp
services.AddMemoryCache(o =>
{
    o.SizeLimit = 10_000;                       // meaningless without per-entry Size
    o.CompactionPercentage = 0.25;              // evict 25% when full
});

var value = await _cache.GetOrCreateAsync(key, async entry =>
{
    entry.Size = 1;                             // REQUIRED when SizeLimit is set
    entry.AbsoluteExpirationRelativeToNow = Jittered(TimeSpan.FromMinutes(5));
    entry.SetPriority(CacheItemPriority.Normal);
    return await LoadAsync();
});
```

Things that bite:

- **`SizeLimit` without per-entry `Size` does nothing.** The cache stays unbounded. Worse, if `SizeLimit` is set and an entry has no `Size`, that entry is silently not cached at all.
- **`GetOrCreateAsync` is not atomic.** Concurrent callers all run the factory. It does not coalesce. If the factory is expensive, you have III.1 inside a single process — use `HybridCache` or the `Lazy` pattern above.
- **Expiry is lazy.** Entries are removed on access or during scans, so memory isn't reclaimed at the expiry instant. Reported size lags reality.
- **Eviction callbacks run on a thread pool thread**, after the fact. Don't do anything slow or blocking there.
- **`MemoryCache` holds references.** Cached object graphs are gen-2 roots. A cache of 100k rich objects is a GC problem, not a memory problem.

<a id="be-f586"></a>

#### HybridCache (.NET 9+)

The successor, and it exists because everyone was hand-rolling the same two-tier pattern:

``` csharp
services.AddHybridCache(o => o.DefaultEntryOptions = new()
{
    LocalCacheExpiration = TimeSpan.FromMinutes(1),
    Expiration = TimeSpan.FromMinutes(10)
});

var user = await _cache.GetOrCreateAsync(
    $"user:{id}",
    async ct => await _db.LoadAsync(id, ct),
    tags: [$"user:{id}", "users"],
    cancellationToken: ct);
```

What you get for free: **L1 (in-process) + L2 (distributed) in one API**, built-in stampede protection via request coalescing, tag-based invalidation, and configurable serialization. If you're on .NET 9 or later, this replaces most hand-written caching layers and removes the two most common bugs (no coalescing, no size bound).

<a id="be-f587"></a>

#### The two-tier pattern

Even without `HybridCache`, this is the shape worth knowing:

``` text
request → L1 in-process (1 min TTL, ~100 ns)
        → L2 Redis        (10 min TTL, ~0.5 ms)
        → database
```

L1 absorbs hot keys and protects Redis from hot-key saturation (III.4). L2 gives cross-instance sharing and survives restarts. The short L1 TTL bounds the divergence between instances to something you choose.

The subtlety: **you now have two divergence windows stacked.** Invalidating L2 doesn't touch L1, so a purge takes effect only after the L1 TTL elapses. Keep L1 TTLs short, or publish invalidations over pub/sub to all instances.

------------------------------------------------------------------------

### Backend: Distributed Caching

<p class="src">From <em>Backend Engineering</em> · Caching · Backend: Distributed Caching</p>

<a id="be-f588"></a>

#### Redis and Memcached

**Memcached** — pure key/value strings, multi-threaded, no persistence, no replication. Simple and very fast. Chosen when you want exactly a cache and nothing else.

**Redis** — data structures (strings, hashes, lists, sets, sorted sets, streams, bitmaps, HyperLogLog), optional persistence, replication, cluster mode, pub/sub, Lua scripting, transactions. The default choice, because the extra structures turn out to be useful (rate limiters, leaderboards, queues, locks).

**Redis command execution is single-threaded.** This is a feature — commands are atomic without locks — and a hazard: one slow command blocks *everything*. `KEYS *` on a million-key instance is a multi-second full stop for every client. Use `SCAN`, which is cursor-based and incremental. (Redis 6+ uses threads for I/O, but command execution remains serialized.)

<a id="be-f589"></a>

#### Serialization

Every distributed cache access serializes on write and deserializes on read. For small objects this often **dominates** the network time.

- `System.Text.Json` — fast, readable, debuggable, interoperable. The sane default.
- MessagePack / protobuf — 2–5× faster and much smaller. Worth it for high throughput; costs you the ability to inspect values with `redis-cli`.
- Binary formatters — do not. `BinaryFormatter` is removed in modern .NET for remote-code-execution reasons.

Two practical notes: **cache the DTO, not the entity** (entities carry lazy-loading proxies and change trackers that serialize badly or not at all), and **version your payloads**, because a deployed schema change against an existing cache means old bytes hitting new code. Include a version in the key (II.4) so a deploy naturally misses rather than deserializes into garbage.

<a id="be-f590"></a>

#### Redis operational settings

``` text
maxmemory 4gb
maxmemory-policy allkeys-lru
```

`maxmemory-policy` options, and the default is the dangerous one:

| Policy           | Behavior                                                          |
|------------------|-------------------------------------------------------------------|
| `noeviction`     | **default** — writes fail with an error when full                 |
| `allkeys-lru`    | evict any key, least recently used — **usual choice for a cache** |
| `allkeys-lfu`    | evict any key, least frequently used                              |
| `volatile-lru`   | evict only keys that have a TTL                                   |
| `volatile-ttl`   | evict the soonest-to-expire                                       |
| `allkeys-random` | evict anything                                                    |

If you use Redis purely as a cache, `allkeys-lru` (or `allkeys-lfu`) is what you want. `noeviction` turns a full cache into write failures, which usually surfaces as a confusing error deep in application code.

**Expiry is both lazy and active.** Redis removes an expired key when it's accessed, and separately samples random keys ~10×/second to expire them. So expired keys can occupy memory for a while, and `used_memory` overstates live data.

**Persistence:** RDB (periodic snapshots) and AOF (append-only log). For a pure cache, you may want neither — persistence costs fork memory and latency spikes. Turn it on when losing the cache is unacceptable (III.8).

**Pipelining and batching.** Round trips dominate. 100 sequential `GET`s at 0.5 ms is 50 ms; one `MGET` is 0.5 ms. Use `MGET`/`MSET`, pipeline where you can, and treat a loop containing an `await` on Redis as a design smell.

<a id="be-f591"></a>

#### Client-side concerns (.NET)

`StackExchange.Redis` multiplexes all commands over a **single connection**. Consequences:

- **One `ConnectionMultiplexer` per application**, shared, long-lived. Creating them per request is a serious bug.
- **A blocking call under load starves the thread pool**, and the resulting `RedisTimeoutException` message *includes thread pool statistics*. When you see one, read those numbers first — the cause is usually starvation in your own process, not Redis being slow. (See the async reference, Part X, on diagnosing starvation.)
- **Set explicit timeouts** and treat Redis as a fallible dependency: try/catch around every access, fail open to the source.

<a id="be-f592"></a>

#### Sharding and consistent hashing

Beyond one machine, keys are distributed across nodes. Naive `hash(key) % N` remaps **almost every key** when N changes — adding one node invalidates the whole cache and produces an instant avalanche.

**Consistent hashing** places nodes and keys on a ring; adding a node remaps only ~1/N of keys. Redis Cluster uses a related scheme: 16,384 fixed hash slots assigned to nodes, so resharding moves slots rather than rehashing everything.

**Multi-key operations only work within a slot.** `MGET a b c` fails in cluster mode if those keys live on different nodes. **Hash tags** force co-location — `user:{42}:profile` and `user:{42}:settings` hash only on the braced part, landing on the same slot. Use them deliberately for keys you'll fetch together; overuse creates hot slots.

<a id="be-f593"></a>

#### Availability

If the cache is load-bearing (III.8), it needs the same treatment as a database: replicas, automatic failover (Sentinel or Cluster), monitoring on memory, evictions, and latency, and a **tested** cold-start plan.

If it isn't load-bearing, the requirement is simpler and often skipped: **the application must work with the cache completely gone.** Test that by turning it off in staging under load. This is the single most valuable caching test you can run and almost nobody runs it.

<a id="be-f594"></a>

#### Distributed locks — proceed carefully

Redis is often used for locking (e.g. to prevent stampedes across instances):

``` text
SET lock:resource <random-token> NX PX 30000
```

`NX` (only if absent) plus `PX` (expiry) plus a **random token** so only the owner can release, via a Lua compare-and-delete. Never `DEL` unconditionally — you'll release someone else's lock after your own expired.

**Redlock** extends this across multiple independent Redis nodes. It is genuinely contested: Martin Kleppmann's critique is that no lock built on timeouts is safe for correctness, because a process can pause (GC, VM suspend) past its lease and resume believing it still holds the lock; Salvatore Sanfilippo's response argues it's adequate under stated assumptions.

The practical resolution: **Redis locks are fine for efficiency, not for correctness.** Preventing a duplicate cache refresh — fine, the worst case is wasted work. Preventing a double payment — no; use a database transaction, a unique constraint, or a fencing token.

------------------------------------------------------------------------

### Database Caching

<p class="src">From <em>Backend Engineering</em> · Caching · Database Caching</p>

<a id="be-f595"></a>

#### Database Caching

The layer people forget, because it works without being asked.

**Buffer pool / page cache.** Every database keeps hot pages in RAM. This is why a query is slow once and fast after. Sizing it correctly is usually a bigger win than any application cache: a database with a buffer pool that fits its working set may not need an application cache at all. Check `innodb_buffer_pool_size`, `shared_buffers`, or the SQL Server buffer cache hit ratio before adding Redis.

**Query plan cache.** Parsing and planning is real work; databases cache plans keyed by statement text. This is a strong argument for parameterized queries — beyond SQL injection, string-concatenated queries produce a unique statement per value, so every execution re-plans and the plan cache fills with single-use entries. (The opposite risk, parameter sniffing, is when one cached plan is wrong for a different parameter distribution.)

**Materialized views.** A precomputed, stored query result. A cache with a refresh policy, managed by the database, and the right answer for expensive aggregates. The trade-off is the usual one: refresh cost versus staleness.

**ORM second-level caches.** NHibernate and RavenDB have them; EF Core deliberately doesn't ship one. They cache entities across sessions, and they are a classic source of confusing staleness because invalidation is implicit. Understand the eviction rules before enabling one.

**Read replicas** are effectively a cache with replication lag as the divergence window — which is why read-your-own-writes (API reference, V.6) is a caching problem in disguise.

------------------------------------------------------------------------

### HTTP Caching

<p class="src">From <em>Backend Engineering</em> · Caching · HTTP Caching</p>

<p class="pd">The most powerful cache available, because a hit means  no request is made at all . It&#x27;s also the most misconfigured, because the headers are subtle and the failure is silent.</p>

<a id="be-f596"></a>

#### The model

Two mechanisms, and understanding that they're distinct clears up most confusion:

- **Freshness** — "this is good until T." No request is made. Zero latency.
- **Validation** — "is my copy still good?" A request is made, but the response can be a bodiless `304 Not Modified`. Saves bandwidth, not latency.

A cached entry starts fresh, becomes stale, and then requires validation. Fresh is dramatically better; validation is the fallback.

<a id="be-f597"></a>

#### Cache-Control

``` yaml
Cache-Control: public, max-age=3600
Cache-Control: private, max-age=60
Cache-Control: no-cache
Cache-Control: no-store
Cache-Control: public, max-age=31536000, immutable
Cache-Control: max-age=60, stale-while-revalidate=300, stale-if-error=86400
Cache-Control: public, max-age=60, s-maxage=600
```

| Directive                  | Meaning                                              |
|----------------------------|------------------------------------------------------|
| `public`                   | any cache may store it, including shared/CDN         |
| `private`                  | browser only — **never** a shared cache              |
| `max-age=N`                | fresh for N seconds                                  |
| `s-maxage=N`               | overrides `max-age` for **shared** caches only       |
| `no-cache`                 | may store, **must revalidate** before each use       |
| `no-store`                 | never write to any cache, anywhere                   |
| `must-revalidate`          | once stale, never serve without validating           |
| `immutable`                | won't change — don't even revalidate on reload       |
| `stale-while-revalidate=N` | serve stale up to N s while refreshing in background |
| `stale-if-error=N`         | serve stale up to N s if the origin is failing       |

**`no-cache` does not mean "don't cache."** It means "store it, but check with me before using it." The one that means don't store is `no-store`. This is the single most common misunderstanding in HTTP caching, and getting it backwards either leaks private data into caches or destroys your hit rate.

**`private` vs `public` is a security boundary.** An authenticated response marked `public` can be stored by a CDN and served to a different user. This is a real, recurring, serious incident class — the same bug as omitting the user from a cache key (I.1).

<a id="be-f598"></a>

#### Validators

``` yaml
ETag: "a1b2c3"                              strong — byte-identical
ETag: W/"a1b2c3"                            weak — semantically equivalent
Last-Modified: Sun, 17 Aug 2026 14:30:00 GMT
```

The flow:

``` yaml
→ GET /users/42
← 200 OK, ETag: "v3", Cache-Control: max-age=60

  (61 seconds later — stale, so validate)

→ GET /users/42
  If-None-Match: "v3"
← 304 Not Modified          (no body)
```

ETags beat `Last-Modified`: second resolution is too coarse for fast-changing resources, and "modified" isn't the same as "different" — a rebuild can touch a file without changing its content.

Weak ETags (`W/`) are right when representations are semantically equal but not byte-identical (whitespace, compression variance). Note that **range requests require strong ETags**, so weak ones break partial content.

Cheap ETag: hash the response body, or use a row version/`updated_at` from the database. Note that generating an ETag by rendering the full response saves bandwidth but not server work — if the rendering is the expensive part, cache upstream of it too.

<a id="be-f599"></a>

#### Vary — the cache key you forgot you had

``` yaml
Vary: Accept-Encoding
Vary: Accept-Language, Accept-Encoding
```

`Vary` tells caches which request headers are part of the cache key. Get it wrong in either direction and you have a bug:

- **Missing `Vary: Accept-Encoding`** → a gzipped response served to a client that can't decompress it.
- **`Vary: User-Agent`** → thousands of UA strings means thousands of cache entries for one resource, and a hit rate near zero. Almost always a mistake.
- **Missing `Vary: Authorization`** on anything user-specific → one user's data served to another. Better still: mark it `private` and don't rely on `Vary` for security.

`Vary: *` means "never reusable." Effectively `no-store` with extra steps.

<a id="be-f600"></a>

#### stale-while-revalidate

The best directive most people don't use:

``` yaml
Cache-Control: max-age=60, stale-while-revalidate=300
```

For 60 seconds, serve from cache. For the next 300, **serve the stale copy immediately and refresh in the background.** Users never wait for a revalidation, and the origin sees one request instead of a stampede.

`stale-if-error=86400` is its companion: if the origin returns 5xx or is unreachable, keep serving the stale copy for a day rather than showing an error. This converts an origin outage into slightly outdated content — usually a very good trade.

<a id="be-f601"></a>

#### Common mistakes

- Caching authenticated responses as `public`
- Using `no-cache` when you meant `no-store` (or the reverse)
- No caching headers at all — behavior is then heuristic and varies by browser and CDN
- `Vary: User-Agent`, destroying hit rate
- Long `max-age` on HTML, so users can't get updates
- Caching a `POST` response, or caching any response with a `Set-Cookie` header in a shared cache
- Forgetting that **`max-age` is not purgeable** — once a browser holds it, you have no recall

------------------------------------------------------------------------

### CDN and Edge

<p class="src">From <em>Backend Engineering</em> · Caching · CDN and Edge</p>

<a id="be-f602"></a>

#### What it does

A CDN puts copies of your content in hundreds of POPs worldwide. A user in Jakarta hits a Jakarta node instead of your origin in Frankfurt: 15 ms instead of 200 ms, and your origin sees a fraction of the traffic.

Beyond caching, CDNs terminate TLS near the user (saving handshake round trips), absorb DDoS traffic, and increasingly run compute at the edge.

<a id="be-f603"></a>

#### The edge cache key

By default: host + path + query string. Everything about it is worth tuning.

- **Query parameter handling.** Tracking parameters (`?utm_source=…`) create a distinct cache entry per campaign for identical content. Strip or allow-list query params — this alone can transform a hit rate.
- **Normalize.** Sort query parameters, lowercase the host, strip fragments.
- **Cookies destroy caching.** Most CDNs won't cache a response with `Set-Cookie`, and if you vary on the `Cookie` header, every user has a unique key. Serve static assets from a cookieless path or domain.
- **`s-maxage`** lets you cache aggressively at the CDN while keeping browsers on a short leash — the right shape, since you can purge the CDN and cannot purge browsers.

<a id="be-f604"></a>

#### Purging

- **By URL** — precise, requires knowing every affected URL.
- **By tag / surrogate key** — attach tags to responses (`Surrogate-Key: product-42 category-shoes`), purge by tag. This is the good one, and it's the CDN equivalent of tag-based invalidation.
- **By wildcard/prefix** — broad, slower.
- **Purge everything** — the emergency button. Expect an origin stampede immediately after; do it during low traffic if you can.

Propagation is typically seconds. Design for the window.

<a id="be-f605"></a>

#### Tiered caching and origin shield

Without it, 200 POPs missing simultaneously produce 200 origin requests. With an **origin shield**, edges fetch through one regional parent, so the origin sees one. Turn it on — it converts a distributed stampede into a single request, and it's usually a checkbox.

<a id="be-f606"></a>

#### Edge compute

Cloudflare Workers, Lambda@Edge, Fastly Compute. Lets you customize cache keys, do auth checks, personalize at the edge, and serve stale deliberately. The relevant caching use: **cache a page for everyone and inject the personalized fragment at the edge**, rather than marking the whole page private. That's often the difference between a 5% and a 95% hit rate on a logged-in site.

------------------------------------------------------------------------

### Frontend: Browser and Assets

<p class="src">From <em>Backend Engineering</em> · Caching · Frontend: Browser and Assets</p>

<a id="be-f607"></a>

#### The browser's cache layers

More than one, and they behave differently:

- **Memory cache** — this tab, this session. Instant, dies on close.
- **HTTP disk cache** — governed by `Cache-Control`. Survives restarts. **You cannot purge it.**
- **Service worker Cache API** — under your control, programmatic, persistent.
- **back/forward cache (bfcache)** — the entire page state frozen for instant back-navigation. Broken by `no-store` on the document and by `unload` handlers.
- **Preload/prefetch cache** — short-lived, for speculative fetches.

The one to internalize: **the disk cache is beyond your reach.** If you send `max-age=31536000` on a file, some users have it for a year and no deploy will dislodge it.

<a id="be-f608"></a>

#### Content hashing — the pattern that solves it

The reason the previous point isn't fatal:

``` csharp
main.a1b2c3d4.js      Cache-Control: public, max-age=31536000, immutable
app.9f8e7d6c.css      Cache-Control: public, max-age=31536000, immutable
index.html            Cache-Control: no-cache
```

The filename contains a hash of the content. Change the content, the name changes, and it's a **different URL** — so there is no invalidation problem. The old file is never requested again.

**This is versioned keys (II.4) applied to assets**, and it's the same idea in a different costume. It gives you the ideal combination: assets cached forever with zero staleness risk.

The pivot is the HTML. `index.html` is `no-cache` — always revalidated, tiny, and it's what points at the hashed filenames. One small validated request unlocks a year of cached assets.

The failure mode: **caching `index.html` for a long time.** Users then hold an old HTML referencing old asset names indefinitely — and if you've since deleted those assets, the app is broken with no way for them to recover. This is the most common frontend caching incident.

Every modern bundler does the hashing. What you must get right is the header split: **immutable for hashed assets, `no-cache` for the entry document.**

<a id="be-f609"></a>

#### Service workers

A proxy you write, running between the page and the network. Total control, which includes the ability to permanently break your own site — a service worker that caches a broken bundle will keep serving it across reloads.

Standard strategies:

| Strategy                   | Behavior                       | Use for                           |
|----------------------------|--------------------------------|-----------------------------------|
| **Cache-first**            | cache, else network            | hashed assets, fonts              |
| **Network-first**          | network, fall back to cache    | API data, HTML                    |
| **Stale-while-revalidate** | serve cache now, update behind | avatars, feeds, non-critical data |
| **Network-only**           | never cache                    | mutations, auth                   |
| **Cache-only**             | never network                  | precached app shell               |

``` csharp
// stale-while-revalidate
self.addEventListener('fetch', e => {
  e.respondWith(caches.open('v1').then(async cache => {
    const hit = await cache.match(e.request);
    const fresh = fetch(e.request).then(r => { cache.put(e.request, r.clone()); return r; });
    return hit || fresh;                       // instant if cached, refresh regardless
  }));
});
```

Rules that prevent the bad outcome: **version your cache names** and delete old ones in `activate`; **never cache opaque cross-origin responses** blindly (you can't read their status, so you may cache an error); always ship a kill switch — a way to unregister the worker and clear caches remotely.

Workbox handles most of this correctly; hand-rolling is where sites get bricked.

<a id="be-f610"></a>

#### Browser storage as a cache

| Store            | Size  | Sync            | Notes                                   |
|------------------|-------|-----------------|-----------------------------------------|
| `localStorage`   | ~5 MB | **synchronous** | blocks the main thread; strings only    |
| `sessionStorage` | ~5 MB | synchronous     | per tab                                 |
| **IndexedDB**    | large | async           | the right choice for real data          |
| Cache API        | large | async           | for `Response` objects                  |
| Cookies          | 4 KB  | —               | **sent on every request** — not storage |

`localStorage` is synchronous and therefore blocks rendering — acceptable for a few small values, wrong for datasets. Use IndexedDB (via `idb` or Dexie) for anything substantial.

**Never store tokens in `localStorage`.** Any XSS reads it. `HttpOnly` cookies for auth — see the API reference, Part IV.2.

Always store a **timestamp and a schema version** with cached client data. Without the version, a deploy leaves old-shaped data in users' browsers and your new code crashes on it — for those users, permanently, until they clear storage.

------------------------------------------------------------------------

### Frontend: Server State

<p class="src">From <em>Backend Engineering</em> · Caching · Frontend: Server State</p>

<a id="be-f611"></a>

#### The real problem

Data fetched from a server isn't application state; it's a **cached copy of remote state that's changing without you**. Treating it like local state — fetch once into a store, mutate manually, hope — is the source of most frontend data bugs.

This reframing is why React Query, SWR, and Apollo exist. They are cache libraries with a UI binding, not data-fetching libraries, and their vocabulary is the vocabulary of this document.

<a id="be-f612"></a>

#### The model

``` csharp
const { data, isLoading, isStale } = useQuery({
  queryKey: ['user', userId],
  queryFn: () => fetchUser(userId),
  staleTime: 60_000,      // fresh for 1 min — no refetch
  gcTime: 300_000,        // keep unused data 5 min, then discard
});
```

Two timers people confuse constantly:

- **`staleTime`** — how long data is considered **fresh**. While fresh, no refetch happens at all. This is your divergence window, and it defaults to `0`, meaning "always stale, refetch on every trigger."
- **`gcTime`** — how long **unused** data stays in memory before being dropped. Affects whether remounting shows cached data instantly or a loading spinner.

The default behavior is deliberate stale-while-revalidate: show cached data immediately, refetch in the background, update when it arrives. Users see instant UI; correctness catches up. Same idea as VII.5, on the client.

**`staleTime: 0` on data that changes hourly is the most common misconfiguration** — it refetches on every window focus, mount, and reconnect for no benefit.

<a id="be-f613"></a>

#### Keys and invalidation

Same rules as I.1. The key is a serializable array, and **every parameter affecting the result must be in it**:

``` yaml
queryKey: ['orders', { status, page, sort }]   // all of it
```

Invalidation is hierarchical by prefix:

``` text
queryClient.invalidateQueries({ queryKey: ['orders'] });        // all order queries
queryClient.invalidateQueries({ queryKey: ['orders', id] });    // one
```

This is why prefix structure matters — it's the client-side version of key namespacing.

<a id="be-f614"></a>

#### Optimistic updates

Update the UI before the server confirms, roll back on failure:

``` csharp
useMutation({
  mutationFn: updateTodo,
  onMutate: async (next) => {
    await queryClient.cancelQueries({ queryKey: ['todos'] });   // stop in-flight refetches
    const prev = queryClient.getQueryData(['todos']);
    queryClient.setQueryData(['todos'], old => merge(old, next));
    return { prev };                                            // rollback context
  },
  onError: (_e, _v, ctx) => queryClient.setQueryData(['todos'], ctx.prev),
  onSettled: () => queryClient.invalidateQueries({ queryKey: ['todos'] }),
});
```

`cancelQueries` is the line people omit: without it, an in-flight refetch can land *after* your optimistic update and overwrite it with pre-mutation data.

<a id="be-f615"></a>

#### Normalized vs document caches

**Document cache** (React Query, SWR) — stores whole responses under a key. Simple and predictable. Cost: the same entity appearing in two responses is stored twice and can disagree until both refetch.

**Normalized cache** (Apollo, Relay, RTK Query with normalization) — splits responses into entities by ID, so updating a user updates it everywhere at once. Consistent, and considerably more machinery: you need stable IDs, type policies, and merge rules, and cache misconfiguration produces confusing partial-data bugs.

Choose document caching by default. Choose normalized when the same entities genuinely appear across many views and inconsistency is visible to users.

------------------------------------------------------------------------

### What to Cache

<p class="src">From <em>Backend Engineering</em> · Caching · What to Cache</p>

<a id="be-f616"></a>

#### The decision

Cache when **all** hold:

1.  Reading is expensive relative to a cache lookup
2.  The same data is read repeatedly (there's a head to the distribution)
3.  Some staleness is acceptable
4.  Reads outnumber writes, usually by a lot

Don't cache when:

- The computation is cheap — a hash lookup plus serialization can exceed the work you're avoiding
- Access is uniformly random — no skew, no hits (0.1)
- Data must be exact — balances, inventory at checkout, permission decisions
- Writes are frequent relative to reads — you'll spend more on invalidation than you save
- The data is per-request unique

<a id="be-f617"></a>

#### Anti-patterns

**Caching to hide a slow query.** A missing index is a 10 ms fix; a cache is a permanent architectural commitment with new failure modes. Profile first. Caching an unindexed query means every cache miss still takes 4 seconds, and cold starts are catastrophic.

**Caching authorization decisions.** Cache the *inputs* (a user's roles, briefly), not the *verdict*. A cached "yes" survives a revocation, and stale authorization is a security failure, not a performance one.

**Caching without measuring.** If you can't state the hit rate, you don't know whether the cache is doing anything. Plenty of production caches sit near 0% and pure cost.

**Caching mutable objects by reference.** In-process caches hand out the *same instance* to every caller. One caller mutating it corrupts it for everyone, invisibly. Cache immutable types, or clone on read.

**One TTL for everything.** A single `CACHE_TTL = 300` constant means the country list expires as often as the price. TTL is a per-data-type decision (II.1).

**Caching an error.** Especially in HTTP, where a 500 with permissive headers can be cached at the edge and served to everyone.

<a id="be-f618"></a>

#### Warming

A cold cache is a real operational event (III.7). Warming is how you avoid taking the hit in production traffic.

**Eager warming** — populate known-hot keys at startup, before accepting traffic. Right for a small, enumerable set: config, feature flags, reference data. In ASP.NET Core this belongs in `IHostedService.StartAsync`, and the readiness probe should not report ready until it finishes (API reference, VId.4).

**Lazy warming with a staged rollout** — deploy one instance at a time so only a fraction of traffic ever hits a cold cache. Usually enough, and it's free if you're already doing rolling deploys.

**Replay warming** — capture the top N keys from production logs, replay them against the new instance before it takes traffic. The most accurate, since it warms what's actually hot rather than what you guessed.

**Shadow warming** — a new instance receives mirrored traffic without serving responses. Expensive, but the only approach that works when the hot set is large and unpredictable.

Two rules regardless of approach: **warm gradually** — a startup loop hammering the database at full speed is the avalanche you were trying to prevent — and **jitter the TTLs you write**, or everything you just warmed expires simultaneously later.

<a id="be-f619"></a>

#### Multi-tenancy

Shared caches serving many customers, which changes the risk profile.

**The tenant belongs in every key.** `product:42` in a shared cache serves tenant A's product to tenant B. This is the same class of bug as I.1's missing dimension and the API reference's BOLA, and it's a data breach rather than a correctness bug. Put the tenant in a prefix so it's visible and bulk-purgeable: `t:acme:product:42`.

**Noisy neighbours.** One tenant with a huge working set evicts everyone else's hot data under LRU. Options: per-tenant memory budgets where your cache supports them, separate Redis databases or instances for large tenants, or an eviction policy that considers the tenant.

**Per-tenant hit rates.** A global 90% can hide one large customer sitting at 20%. Tag cache metrics by tenant tier — not by tenant ID, which explodes metric cardinality.

**Purging by tenant** should be one operation. Prefix keys so `t:acme:*` can be scanned and removed, or keep a per-tenant version counter and bump it (II.4) — which is instant and needs no scan.

<a id="be-f620"></a>

#### Is it worth it?

Caching has a cost that rarely appears in the proposal: a Redis cluster to run, a new failure mode, invalidation logic in every write path, and a class of bug that only appears under load.

Estimate before building:

``` text
saved  = requests/sec × hit_rate × (source_latency − cache_latency)
cost   = infrastructure + engineering + incident risk
```

Concretely: 100 req/s at 90% hit rate against a 20 ms query saves ~1.8 seconds of database time per second — meaningful. The same numbers against a 200 µs query save 18 ms/s, which is nothing, and you've added a network dependency to obtain it.

The cheaper alternatives worth exhausting first:

- **An index.** Frequently a 10 ms fix for the query you were about to cache.
- **A bigger database buffer pool.** The database's own cache is free, correct, and already invalidated properly (Part VI).
- **Fixing an N+1.** One query instead of 101 beats caching 101 queries.
- **A read replica.** Eventual consistency you already understand, no new invalidation logic.

Cache when the read genuinely dominates and the alternatives are exhausted. **A cache added to hide a fixable problem makes that problem permanent and harder to see**, because the profile now looks fine and the real cost only surfaces on a cold start.

------------------------------------------------------------------------

### Diagnostics

<p class="src">From <em>Backend Engineering</em> · Caching · Diagnostics</p>

<a id="be-f621"></a>

#### Symptom → cause

| Symptom                                   | Likely cause                                                          |
|-------------------------------------------|-----------------------------------------------------------------------|
| Low hit rate, high eviction               | undersized cache, or key cardinality too high                         |
| Low hit rate, low eviction                | keys too specific, or TTL too short                                   |
| Users see stale data after a write        | missed invalidation, or a layer above (browser/CDN)                   |
| Some users stale, others not              | per-instance in-process caches diverging                              |
| Database spikes every N minutes           | synchronized TTL expiry — no jitter                                   |
| Database spikes on deploy                 | cold cache                                                            |
| One Redis shard hot, others idle          | hot key                                                               |
| Redis timeouts, Redis CPU low             | thread pool starvation in the app                                     |
| Redis memory full, writes failing         | `maxmemory-policy noeviction`                                         |
| Wrong user's data shown                   | missing dimension in the cache key, or `public` on a private response |
| Fixed a bug, users still see it           | browser `max-age`, or a service worker                                |
| Site broken after deploy, only some users | cached `index.html` referencing deleted assets                        |
| Cache down = site down                    | it was load-bearing all along                                         |
| Memory grows until OOM                    | no size bound                                                         |

<a id="be-f622"></a>

#### Measuring properly

``` csharp
_metrics.Add(hit ? "cache.hit" : "cache.miss", new("prefix", KeyPrefix(key)));
```

Tag by key prefix, not globally. Also track **miss latency separately** — that's the number that tells you what the cache is worth.

Redis inspection:

``` text
INFO stats            → keyspace_hits / keyspace_misses
INFO memory           → used_memory, evicted_keys, maxmemory_policy
--bigkeys             → find oversized values
--hotkeys             → find hot keys (needs LFU)
SCAN 0 MATCH user:*   → never KEYS in production
SLOWLOG GET 10        → commands blocking the single thread
```

HTTP: browser devtools shows `(from disk cache)` / `(from memory cache)` per request, and the `Age` header tells you how long a shared cache has held a response. CDN providers expose hit/miss in a response header (`CF-Cache-Status`, `X-Cache`).

------------------------------------------------------------------------

### Caching

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Caching</p>

<p class="pd">Caching as a discipline rather than a header. Where each layer belongs, the four patterns, why invalidation is the hard part, and operating the cache you chose.</p>

<a id="be-g651"></a>

#### The cache hierarchy, and what each layer is for

“Add a cache” is not one decision. There are six places a value can be cached, each with a different lifetime, a different blast radius when it goes stale, and a different person who can clear it.

``` text
  request path, outermost first

  browser cache          per user. YOU CANNOT CLEAR IT. Lives until max-age.
      |
  CDN / edge             per region. Purgeable, eventually. Seconds to minutes.
      |
  reverse proxy          per instance. Clearable by restart.
      |
  application in-process per PROCESS. N instances = N copies, diverging.
      |
  distributed cache      shared. One copy. Clearable. A network hop away.
      |
  database buffer pool   automatic. You do not manage it, and it is
      |                  usually already doing most of the work.
  disk

  the rule that follows: the FURTHER OUT you cache, the faster and cheaper
  the hit - and the LESS CONTROL you have over correctness.
```

| Layer                       | Latency of a hit | Who can invalidate | Suits                                         |
|-----------------------------|------------------|--------------------|-----------------------------------------------|
| Browser                     | 0 ms             | **Nobody**         | Content-hashed assets, immutable responses    |
| CDN / edge                  | ~10–30 ms        | You, eventually    | Public, cacheable responses; static content   |
| Reverse proxy               | \< 1 ms          | You, per instance  | Repeat responses at one site                  |
| In-process (`IMemoryCache`) | ~100 ns          | That process only  | Reference data, config, hot lookups           |
| Distributed (Redis)         | ~0.5–2 ms        | You, globally      | Session state, shared computed values         |
| Database buffer pool        | ~100 ns          | Automatic          | Everything — it is already caching your pages |

> **\[Note\]**
>
> **Check the database's own cache first.** A database with a 99% buffer-pool hit ratio is already serving almost every read from memory. Putting Redis in front of it buys you a network hop saved and CPU offloaded — not I/O avoided, because there was none. Measure the hit ratio before adding a caching tier; the answer is often that the expensive part was the query plan, not the storage.

<a id="be-g652"></a>

#### The four caching patterns

``` text
  CACHE-ASIDE (lazy loading) - the default, and what you almost always want

    read:   look in cache -> miss -> read the source -> WRITE to cache -> return
    write:  write the source -> INVALIDATE the cache entry

    + only cached data is data someone asked for
    + a cache outage degrades to slow, not broken
    - the first request after a write always misses

  READ-THROUGH

    the cache itself loads on miss; the application only ever talks to the cache.
    + the loading logic lives in one place
    - the cache becomes a dependency on the read path, not an optimisation

  WRITE-THROUGH

    write:  write the cache AND the source, synchronously
    + the cache is never stale
    - every write pays both costs, and a cache failure fails the write

  WRITE-BEHIND (write-back)

    write:  write the cache, return, flush to the source later
    + fastest writes
    - DATA LOSS if the cache dies before flushing. Rarely acceptable
      for a system of record.
```

``` csharp
// cache-aside, done properly. The details matter more than the shape.
public async Task<Product?> GetAsync(long id, CancellationToken ct)
{
    var key = $"product:v3:{tenantId}:{id}";
    //          ^^^^^^^^ ^^^ ^^^^^^^^
    //          type     schema version  TENANT - see the key design notes below

    if (cache.TryGetValue(key, out Product? hit)) return hit;

    var product = await db.Products.FindAsync([id], ct);
    if (product is null) return null;       // consider caching the MISS too - see below

    cache.Set(key, product, new MemoryCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),   // a HARD ceiling
        Size = 1,                                                     // needs SizeLimit set
    });
    return product;
}
```

| Expiration choice | Behaviour                     | Use for                                                      |
|-------------------|-------------------------------|--------------------------------------------------------------|
| **Absolute**      | Expires N after being written | **Always set one.** A ceiling on staleness                   |
| **Sliding**       | Expires N after last access   | Session-like data. **Never alone** — a hot key never expires |
| **Both**          | Sliding, capped by absolute   | The usual correct answer                                     |
| **None**          | Until evicted or restarted    | Only for genuinely immutable data                            |

> **\[Trap\]**
>
> **Common trap.** A sliding expiration with no absolute cap means a frequently-read key is *never* refreshed. The most popular product on the site is the one showing a price from three weeks ago — and because it is popular, nobody notices it is stale rather than simply cached. Always pair sliding with an absolute ceiling.

<a id="be-g653"></a>

#### Invalidation, and the failure modes

Caching is easy; invalidation is the whole problem. These are the five failures that actually occur, in rough order of how often they bite.

``` text
  1  CACHE STAMPEDE (thundering herd)

     a hot key expires. 500 concurrent requests all miss, and all 500
     query the database simultaneously.
     -> the database, which was fine, now falls over
     -> and it falls over precisely BECAUSE you added a cache

     fixes
       a lock per key: one loader, the rest wait for its result
       stale-while-revalidate: serve the old value, refresh in background
       jittered expiry: do not let 10,000 keys expire in the same second
       pre-warm hot keys before they expire

  2  STALE AFTER WRITE
     write the database, forget to invalidate. The user sees their own
     change not applied. -> invalidate in the SAME code path as the write.

  3  KEY COLLISION ACROSS TENANTS
     "product:42" - whose product 42? A cross-tenant data leak caused by
     a cache key. -> ALWAYS include the tenant in the key.

  4  STALE FORMAT AFTER DEPLOY
     you change the shape of the cached object. Old entries deserialise
     into the new type and produce nulls or throw.
     -> put a SCHEMA VERSION in the key. Bump it on any shape change.

  5  UNBOUNDED GROWTH
     no size limit, no expiry. Memory climbs until the process is OOM-killed.
     -> a size limit AND an eviction policy, always.
```

``` csharp
// stampede protection: one loader per key, the rest await the same task
private readonly ConcurrentDictionary<string, Lazy<Task<Product?>>> _inflight = new();

public Task<Product?> GetAsync(long id, CancellationToken ct)
{
    var key = Key(id);
    if (cache.TryGetValue(key, out Product? hit)) return Task.FromResult(hit);

    // GetOrAdd guarantees one Lazy per key; Lazy guarantees one execution
    var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<Product?>>(async () =>
    {
        try
        {
            var p = await LoadAndCacheAsync(id, ct);
            return p;
        }
        finally { _inflight.TryRemove(key, out _); }
    }));
    return lazy.Value;
}
// 500 concurrent misses -> ONE database query, 500 awaiters.
// HybridCache (.NET 9+) does this for you, and also handles the
// in-process + distributed two-tier case.
```

| Key design rule                          | Why                                                                |
|------------------------------------------|--------------------------------------------------------------------|
| Include the **tenant**                   | Otherwise a cache key is a cross-tenant leak                       |
| Include a **schema version**             | A deploy changing the object shape does not poison old entries     |
| Include the **type**                     | `product:42` and `order:42` must not collide                       |
| Include anything the value **varies by** | Locale, currency, permission level — the same `Vary` logic as HTTP |
| Keep it deterministic and short          | Built from the same inputs, every time, by every caller            |
| Never include user input unescaped       | Key injection, and unbounded key growth                            |

> **\[Note\]**
>
> **Cache the miss, deliberately.** If a lookup for a non-existent id is common — a bot scanning ids, a broken client — then not caching misses means every one of those requests reaches the database. Cache a sentinel “not found” with a *short* TTL. This is negative caching, and it is the difference between absorbing a scan and being taken down by one.

###### A two-tier cache, and the stampede guard in full

``` csharp
// HybridCache (.NET 9+) does the two-tier pattern and stampede protection
// for you - which is the main reason to use it over IMemoryCache directly.
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),       // DISTRIBUTED tier (Redis)
        LocalCacheExpiration = TimeSpan.FromMinutes(1),  // IN-PROCESS tier
    };
});
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisCs);

public async Task<Product?> GetAsync(long id, CancellationToken ct) =>
    await cache.GetOrCreateAsync(
        $"product:v3:{tenantId}:{id}",
        async token => await db.Products.FindAsync([id], token),
        cancellationToken: ct);
// concurrent callers for the SAME key -> ONE database call, all awaiting it.
```

``` text
  WHY TWO TIERS, with the arithmetic

    L1 in-process   ~100 ns    per instance. N instances = N copies.
    L2 Redis        ~1 ms      shared. One copy.
    database        ~20 ms

    with 10 instances and a 1-minute L1 expiry:
      an item can be up to 60 SECONDS STALE on any instance, and
      different instances can show DIFFERENT values simultaneously.

    that divergence is the price of the L1 tier, and it must be a
    conscious decision:
      product name      -> fine
      feature flag      -> fine, with a short L1
      price             -> probably not
      permissions       -> NO. Use L2 only, or no cache.

    the mitigation when you need both speed and freshness: Redis
    pub/sub to invalidate L1 across instances on write. HybridCache
    supports this via a backplane, and it turns "60 seconds stale" into
    "stale until the message arrives", typically milliseconds.
```

``` csharp
// if you are on .NET 8 or earlier, the stampede guard is yours to write
private readonly ConcurrentDictionary<string, Lazy<Task<Product?>>> _inflight = new();

public Task<Product?> GetAsync(long id, CancellationToken ct)
{
    var key = Key(id);
    if (_cache.TryGetValue(key, out Product? hit)) return Task.FromResult(hit);

    // GetOrAdd guarantees one Lazy per key; Lazy guarantees one execution.
    var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<Product?>>(async () =>
    {
        try     { return await LoadAndCacheAsync(id, ct); }
        finally { _inflight.TryRemove(key, out _); }
    }));
    return lazy.Value;
}
// 500 concurrent misses on a hot key -> ONE query, 500 awaiters.
// without this, the cache EXPIRING is what takes the database down.
```

> **\[Trap\]**
>
> **Common trap.** Jitter your expirations. Ten thousand keys written during a deployment all expire in the same second sixty minutes later, producing a synchronised stampede that looks exactly like a traffic spike and has no external cause. Add a random 10–20% to every TTL — one line, and it removes an entire class of recurring incident.

### HTTP & the Web Stack

<p class="src">From <em>Backend Engineering</em> · Backend Platform · HTTP & the Web Stack</p>

<p class="pd">HTTP and TLS at the level you need to debug them: what the headers actually control, how caching is negotiated, and what a certificate chain really is.</p>

<a id="be-g643"></a>

#### Caching: the headers that actually decide

``` text
  the decision, in order

  1  is there a fresh cached copy?          Cache-Control max-age / Expires
       yes -> serve it. NO REQUEST IS MADE AT ALL.
  2  is there a stale copy with a validator? ETag / Last-Modified
       yes -> conditional request:
              If-None-Match / If-Modified-Since
              server replies 304 (cheap) or 200 with a new body
  3  otherwise -> full request

  the first case is the valuable one: zero network. The second still costs
  a round trip, but saves the payload.
```

| Directive                   | Effect                                                            |
|-----------------------------|-------------------------------------------------------------------|
| `max-age=3600`              | Fresh for an hour. No request during that time                    |
| `s-maxage=86400`            | As above, but for *shared* caches (CDN) only                      |
| `no-cache`                  | **May cache, but must revalidate every time.** Not ‘do not cache’ |
| `no-store`                  | Do not write it down at all. This is the one for sensitive data   |
| `private`                   | Browser may cache; shared caches may not                          |
| `public`                    | Any cache may store it, even with an `Authorization` header       |
| `immutable`                 | Never revalidate — for content-hashed asset URLs                  |
| `stale-while-revalidate=60` | Serve stale immediately, refresh in the background                |
| `must-revalidate`           | Once stale, do not serve without checking                         |

``` yaml
# the two-tier strategy that covers almost every web application

# 1. HTML: never cached, so a deploy is picked up immediately
Cache-Control: no-cache

# 2. hashed static assets: cached forever, because the URL changes on change
#    /assets/app.9f3c2a1b.js
Cache-Control: public, max-age=31536000, immutable

# APIs: usually private and short, or not cached at all
Cache-Control: private, max-age=0, must-revalidate

# and the header people forget, which causes wrong-content-served bugs:
Vary: Accept-Encoding, Accept-Language, Authorization
# without Vary, a CDN can serve a German response to an English user,
# or one user's authenticated response to another.
```

> **\[Trap\]**
>
> **Common trap.** Omitting `Vary` on a response that differs by header is how a CDN serves one customer's data to another. If a response depends on `Authorization`, either mark it `private` or list the header in `Vary` — and prefer `private`, because `Vary: Authorization` makes the cache nearly useless anyway.

###### Invalidation, and the purge problem

``` text
  the two strategies, and why one is far better

  PURGE-BASED           content lives at a stable URL; on change, tell the
                        CDN to purge it.
                        -> purges are eventually consistent across edges
                        -> some clients have it cached locally anyway
                        -> at scale, purge APIs are rate-limited
                        -> "I purged and it is still stale" is a common ticket

  VERSIONED URL         the URL contains a content hash.
                        /assets/app.9f3c2a1b.js
                        -> a change produces a NEW URL. Nothing to invalidate.
                        -> the old URL stays cached and stays correct,
                           so in-flight page loads do not break
                        -> cache forever, immutable

  use versioned URLs for anything you control the references to.
  reserve purging for content whose URL must stay stable (an HTML page,
  an API response, an image referenced by third parties).
```

``` csharp
# the stale-chunk problem, which versioning creates and you must handle:
#
#   user loads index.html  -> references app.9f3c.js
#   you deploy             -> now app.7a1b.js
#   user navigates, the SPA lazy-loads a chunk  -> 404, app crashes
#
# fixes, use both:
#   1. keep OLD assets available for a deploy generation or two -
#      do not delete on deploy
#   2. detect chunk-load failures and reload the page
window.addEventListener('vite:preloadError', () => window.location.reload());
```

| Cache layer          | Invalidation                      | Lag                                  |
|----------------------|-----------------------------------|--------------------------------------|
| Browser              | None — you cannot reach it        | Until `max-age` expires              |
| CDN                  | Purge API, or a surrogate-key tag | Seconds to minutes across edges      |
| Reverse proxy        | Purge module, or restart          | Immediate on that node only          |
| Application cache    | Explicit eviction on write        | Immediate, if every writer remembers |
| Database buffer pool | Automatic                         | N/A                                  |

**Surrogate keys** are the underused feature: tag responses with keys (`Surrogate-Key: product-4471 category-audio`), then purge by key rather than by URL. One product update purges every page that mentions it, without you tracking which pages those are.

### Optimization in practice

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Optimization in practice</p>

<a id="be-m282"></a>

#### Caching layers

| Layer                       | Latency  | Scope                 | Use for                                             |
|-----------------------------|----------|-----------------------|-----------------------------------------------------|
| a field / `static readonly` | ns       | process               | immutable reference data, compiled regexes          |
| `IMemoryCache`              | ~100 ns  | process               | hot per-node data with a TTL                        |
| `HybridCache` (.NET 9)      | ns → ms  | process + distributed | **the new default**: L1+L2 with stampede protection |
| Redis / `IDistributedCache` | 0.2–2 ms | cluster               | shared session, cross-node coherence                |
| database                    | 1–50 ms  | truth                 | —                                                   |

``` csharp
// The classic stampede: 100 concurrent requests all miss, all hit the database.
// HybridCache handles this for you; with IMemoryCache, gate it yourself:
private static readonly ConcurrentDictionary<string, Lazy<Task<T>>> _inflight = new();

Task<T> GetAsync(string key) =>
    _inflight.GetOrAdd(key, k => new Lazy<Task<T>>(() => LoadAsync(k))).Value;
```

**Invalidation strategies**, in increasing order of complexity: absolute TTL (simplest, always correct within the window); sliding TTL (keeps hot items, risks staleness forever); explicit eviction on write (correct, but must cover every write path); versioned keys (`order:{id}:v{version}` — no eviction needed, old entries expire naturally).

> **\[Trap\]**
>
> **Cache what is expensive and stable, not what is convenient.** Caching a cheap query saves nothing and adds a coherence bug. Caching mutable data without an invalidation path produces the worst class of production issue: correct-looking wrong answers. And always cap `IMemoryCache` with `SizeLimit` plus per-entry sizes — an unbounded in-process cache is a memory leak with a nicer name.

Worked example: [Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)

First principles: [Where the time actually goes](12-04-scaling-and-performance.md#be-g705)

Recipe: [Recipe: a cached read with invalidation](#be-m288)

### LINQ, messaging and caching

<p class="src">From <em>Backend Engineering</em> · Practical Answers · LINQ, messaging and caching</p>

<p class="pd">Aggregating data without a second query, the difference between a queue and a bus, and the cache stack from in-process to distributed.</p>

<a id="be-q15"></a>

#### Caching: memory, distributed and HybridCache

|                         | IMemoryCache             | IDistributedCache      | HybridCache (.NET 9)             |
|-------------------------|--------------------------|------------------------|----------------------------------|
| Where                   | This process’s heap      | Redis / SQL / external | Both: L1 memory + L2 distributed |
| Speed                   | Nanoseconds              | A network round trip   | L1 speed on hits                 |
| Shared across instances | No                       | Yes                    | Yes (L2)                         |
| Survives restart        | No                       | Yes                    | L2 only                          |
| Serialisation           | None — object references | You serialise          | Handled for you                  |
| Stampede protection     | No                       | No                     | **Yes, built in**                |
| Tag invalidation        | No                       | No                     | **Yes**                          |

``` csharp
// the old two-level pattern, written by hand — and its bugs
public async Task<Product?> GetAsync(int id, CancellationToken ct)
{
    if (_memory.TryGetValue(key, out Product? p)) return p;       // L1
    var bytes = await _distributed.GetAsync(key, ct);             // L2
    if (bytes is not null) { … }
    p = await _db.Products.FindAsync([id], ct);                   // origin
    // ↑ 500 concurrent misses = 500 identical database queries (stampede)
    …
}

// HybridCache: one call, both layers, stampede handled
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration      = TimeSpan.FromMinutes(10),   // L2
        LocalCacheExpiration = TimeSpan.FromMinutes(1) // L1 — keep it SHORT
    };
});
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisCs);

public class ProductService(HybridCache cache, AppDb db)
{
    public ValueTask<Product?> GetAsync(int id, CancellationToken ct) =>
        cache.GetOrCreateAsync($"product:{id}",
            async token => await db.Products.FindAsync([id], token),
            tags: ["products", $"tenant:{_tenant.Id}"],
            cancellationToken: ct);
    // concurrent callers for the same key: ONE executes, the rest await it

    public ValueTask InvalidateAsync(int id, CancellationToken ct) =>
        cache.RemoveAsync($"product:{id}", ct);

    public ValueTask InvalidateAllProductsAsync(CancellationToken ct) =>
        cache.RemoveByTagAsync("products", ct);      // ← tag invalidation
}
```

> **\[Trap\]**
>
> **Common trap.** L1 is per instance, so a write on instance A leaves instance B serving stale data until its local entry expires. That is why `LocalCacheExpiration` should be short (seconds to a minute) even when the distributed entry lives for ten minutes — the local layer is for absorbing bursts, not for correctness.

> **\[Trap\]**
>
> **Common trap.** Cache what is **expensive and stable**. Caching a cheap indexed lookup adds a network hop and an invalidation bug for no gain; caching per-user data with a global key leaks one user’s data to another. Always include the tenant and user in the key when the value depends on them.

### Recipes: code you will actually write

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Recipes: code you will actually write</p>

<a id="be-m288"></a>

#### Recipe: a cached read with invalidation

``` csharp
public sealed class ProductCatalog(
        HybridCache cache, IDbContextFactory<AppDb> factory, TimeProvider time) {

    private static string Key(int tenantId, int categoryId)
        => $"the legacy system:v4:t{tenantId}:catalog:{categoryId}";     // app:version:tenant:entity:id

    public async Task<IReadOnlyList<ProductDto>> GetAsync(
            int tenantId, int categoryId, CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            Key(tenantId, categoryId),
            (tenantId, categoryId),
            static async (state, token) => {
                await using var db = await Factory.CreateDbContextAsync(token);
                return await db.Products.AsNoTracking()
                    .Where(p => p.TenantId == state.tenantId && p.CategoryId == state.categoryId)
                    .Select(ProductDto.Projection)
                    .ToListAsync(token);
            },
            new HybridCacheEntryOptions {
                Expiration = Jitter(TimeSpan.FromMinutes(15)),      // L2, distributed
                LocalCacheExpiration = Jitter(TimeSpan.FromMinutes(2))  // L1, in-process
            },
            tags: [$"catalog:t{tenantId}"],
            cancellationToken: ct);

    // Spread expiry so 40 tenants do not all rebuild in the same second
    // (§297).
    private static TimeSpan Jitter(TimeSpan baseline) =>
        baseline * (0.9 + Random.Shared.NextDouble() * 0.2);

    // Invalidate on write - remove, never overwrite (§210).
    public async Task InvalidateAsync(int tenantId, CancellationToken ct) =>
        await cache.RemoveByTagAsync($"catalog:t{tenantId}", ct);
}
```

``` csharp
// Wire invalidation to the write path, not to a timer:
public async Task UpdateProductAsync(Product p, CancellationToken ct) {
    await db.SaveChangesAsync(ct);                   // commit FIRST
    await _catalog.InvalidateAsync(p.TenantId, ct);  // then evict
}
// This order matters: evicting before the commit lets a concurrent read
// repopulate the cache with the OLD value and keep it for the full TTL.
```

| Property                         | Why                                                                                               |
|----------------------------------|---------------------------------------------------------------------------------------------------|
| Tenant in the key                | the alternative is a cross-tenant leak ([Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)) |
| `v4` in the key                  | a shape change makes old entries unreachable, no migration                                        |
| Jittered TTL                     | prevents synchronised rebuild                                                                     |
| `static` lambda + state tuple    | no closure allocation per call ([Closures and delegate allocation](02-05-control-flow-and-functions.md#be-m280))                     |
| Tags                             | invalidate a group without enumerating keys ([Redis: the data structures](07-06-non-relational-stores.md#be-m206))              |
| Own `DbContext` per factory call | the cache callback has no ambient scope ([Service lifetimes and the captive dependency](04-03-composition-and-dependency-injection.md#be-a58)) |

> **\[Trap\]**
>
> **Cache DTOs, never entities.** A tracked entity carries navigation references and possibly a proxy; serializing it either fails or drags half the object graph across the wire and into Redis memory ([Cache key design and value format](07-06-non-relational-stores.md#be-m207)).
