---
id: "5-2"
number: "5.2"
title: "Threads and locks"
part: 5
part_title: "Concurrency"
words: 10124
minutes: 44
---

# 5.2 Threads and locks

> **Quick look.** Locks, atomics and thread-safe collections, and how to avoid deadlocks.
>
> - Lock on a private object, keep locked sections short, protect each piece of shared data with exactly one lock.
> - Deadlock needs a circular wait; taking locks in a fixed order prevents it.
> - lock can't be held across await; use SemaphoreSlim for async code.
> - Interlocked and compare-and-swap update single values roughly 10–20× faster than a lock.
> - ConcurrentDictionary makes single operations safe; combine steps with GetOrAdd (its factory can run more than once) and never use it as an unbounded cache.
> - volatile prevents caching but gives no atomicity; x64 hides reordering bugs that appear on ARM.
> - Thread pool starvation looks like low CPU with rising latency and queue length; the cause is almost always blocking on async code.


### The big picture

When threads must share data, they need a way to take turns safely. The main tool is the lock: only one thread at a time may hold it, so the code inside runs without interference.

Locks bring their own dangers, most famously deadlock, where threads wait for each other forever. This chapter covers locks and how to use them safely, atomic operations that need no lock, tools for limiting how many threads do something at once, and the thread-safe collections .NET provides. Each one is shown working in a single runnable file.

### Before you start

**Builds on:** 1.7 The operating system, 5.1 Why concurrency is hard, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Relationship** (7.5): How entities relate, expressed in a schema with foreign keys.
- **Resource** (8.2): A thing an API exposes at its own URL, such as a flight or a booking.
- **Backpressure** (5.4): A slow consumer making a fast producer slow down, to keep buffers from growing without limit.
- **Event loop** (5.4): A loop that takes ready callbacks from a queue and runs them one at a time, waiting for events when idle.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **HTTP request** (6.6): A message from client to server: method, path, headers and an optional body.
- **Unit of work** (7.7): An object that collects changes and writes them all together.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, collection expressions, lambdas, LINQ, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does the C# lock statement compile to?**

> A call that acquires the lock, a `try` block around your code and a `finally` that releases it. With a plain object it uses `Monitor.Enter` and `Monitor.Exit`. With the `Lock` type from .NET 9, it uses that type's faster `EnterScope`.

**2. Why can't you use await inside a lock block?**

> The code after the await may resume on a different thread, and a monitor must be released by the thread that took it. The compiler forbids it. For asynchronous code, use `SemaphoreSlim` with `WaitAsync`.

**3. What four conditions must all hold for a deadlock?**

> Mutual exclusion, hold and wait, no preemption, and a circular wait. Remove any one and deadlock is impossible. In practice, taking locks in a fixed order removes the circular wait.

**4. What does Interlocked.CompareExchange do?**

> It writes a new value only if the location still holds the value you expected, and returns what was there, all in one atomic step. Retrying it in a loop updates shared values without a lock.

**5. ConcurrentDictionary.GetOrAdd takes a factory. Can the factory run twice for the same key?**

> Yes. Two threads can both miss and both run the factory; only one result is stored. If the factory is expensive or has side effects, store `Lazy<T>` values so the work runs once.

**6. Why should you never lock on this, a type or a string?**

> Other code can lock on the same object without your knowledge, causing deadlocks or contention you can't see. Lock on a private object that only your class can reach.

**7. What is the difference between a Mutex and a lock?**

> A `Mutex` is an operating system object that can be named and shared between processes, and it is much slower. A `lock` works only within one process and is cheap when uncontended.

### Threads in .NET

The operating system (1.7) introduced threads: paths of execution inside a process that share its memory. .NET gives you several ways to use them.

The `Thread` class creates an operating system thread directly. Each one reserves stack memory, typically 1 MB or more, and costs tens of microseconds to create. A **foreground thread** keeps the process alive until it finishes. A background thread, with `IsBackground = true`, is stopped when the last foreground thread ends.

Creating a thread per job doesn't scale. The **thread pool** keeps a set of reusable worker threads and runs queued work items on them. `Task.Run`, `Parallel.For`, timers and the continuations of `await` all use it. The pool grows and shrinks automatically, adding threads when work queues up and removing idle ones.

In modern .NET, you rarely create `Thread` objects yourself. Use `Task.Run` for background work, `Parallel` for CPU-heavy loops and `async` code for waiting. The exception is a truly long-lived dedicated loop, where `TaskCreationOptions.LongRunning` or a dedicated thread avoids tying up a pool thread forever.

### Mutual exclusion

Why concurrency is hard (5.1) showed that critical sections must not run in two threads at once. Guaranteeing that is called **mutual exclusion**. The tool for it is a **lock**: before entering the critical section, a thread acquires the lock; if another thread holds it, the newcomer waits; on leaving, the thread releases it.

C#'s `lock` statement does this. The compiler wraps the block in `try` and `finally`, so the lock is released even if an exception is thrown. With an ordinary object, `lock` uses the **monitor** built into every .NET object. .NET 9 added a dedicated `System.Threading.Lock` type, which is faster and makes the intent clear. Use it for new code.

A lock also creates the happens-before relationship from the previous chapter. Everything written inside a lock by one thread is visible to the next thread that takes the same lock. That is why reading shared data needs the lock too, not just writing it.

A few rules keep locks safe.

Lock on a private object that only your class can reach. Never lock on `this`, on a `Type` or on a string: other code can lock on the same thing, and you get contention or deadlocks you can't see.

Keep the locked section short. Do the minimum inside: read or update the shared data and get out. Never do I/O, call a remote service, or call code you don't control, such as events and callbacks, while holding a lock.

Protect each piece of shared data with exactly one lock, and always use it. Document which lock protects what.

You can't `await` inside a `lock`. After an `await`, code may resume on another thread, and a monitor must be released by the thread that took it. For asynchronous critical sections, use `SemaphoreSlim(1, 1)` with `await WaitAsync()` and `Release()` in a `finally`.

### Deadlock

A **deadlock** is a group of threads each waiting for something another one holds, so none can ever continue. Thread 1 holds the gate lock and waits for the stand lock. Thread 2 holds the stand lock and waits for the gate lock. Both wait forever.

Deadlock needs four conditions at once. Mutual exclusion: a resource can be held by only one thread. Hold and wait: a thread holds one resource while waiting for another. No preemption: nothing can force a thread to give up what it holds. Circular wait: a cycle of threads each waiting on the next. Remove any one and deadlock can't happen.

**Lock ordering** is the most practical cure. If every thread that needs both locks always takes them in the same order, no cycle can form. Other defences help too. Hold only one lock at a time where possible. Use timeouts, with `Monitor.TryEnter` or `SemaphoreSlim.WaitAsync(timeout)`, so a stuck thread gives up and reports. And never call unknown code while holding a lock, because that code may take locks of its own.

A **livelock** is the restless cousin of deadlock. Threads aren't blocked; they keep reacting to each other and retrying, and none makes progress, like two people stepping aside the same way in a corridor. Random backoff, as in Errors and exceptions (2.8), breaks the symmetry.

### Contention

A lock that is rarely contested is cheap. **Contention** is threads competing for the same lock. Under heavy contention, threads spend their time waiting, and adding more threads makes things slower, not faster.

**Lock granularity** is how much data one lock protects. A single lock for everything is simple and safe, and becomes a bottleneck. One lock per item scales better, and is harder to get right and more prone to deadlocks. Start coarse. Split a lock only when measurements show contention on it.

### Atomic operations without locks

For single values, the `Interlocked` class offers operations that the processor performs atomically. `Increment`, `Decrement` and `Add` update numbers. `Exchange` swaps in a new value and returns the old one. `CompareExchange` is the most powerful: it writes a new value only if the location still holds the value you expected.

That last one is **compare-and-swap** (CAS). It is the foundation of **lock-free** algorithms. Read the current value, compute a new one, and try to swap it in with `CompareExchange`. If another thread changed the value in the meantime, the swap fails, and you retry with the fresh value. No thread ever waits for a lock; at worst it retries. The `RecordMax` method below uses this pattern.

Lock-free code is fast under contention and extremely hard to get right beyond simple cases. Use the lock-free collections .NET provides rather than writing your own.

### Other coordination tools

.NET has a toolbox of synchronization types for different jobs.

A **semaphore** allows up to *n* threads in at once instead of one. `SemaphoreSlim` is the everyday version, with `WaitAsync` for async code. It is the standard way to limit concurrency: "at most three calls to the fare system at a time".

A **reader-writer lock** lets many readers in together, or one writer alone. `ReaderWriterLockSlim` helps when reads vastly outnumber writes and the locked sections are long enough to matter. For short sections, a plain lock is usually faster.

`ManualResetEventSlim` lets threads wait until a signal is set. `CountdownEvent` waits until a count reaches zero. `Barrier` makes a group of threads wait until all of them reach the same point.

A **mutex**, the `Mutex` class, is an operating system object. A named mutex can be shared between processes, for example to allow only one copy of a program to run. It is far slower than `lock` and only needed across processes.

```csharp
// locks.cs — run with: dotnet run locks.cs
using System.Collections.Concurrent;
using System.Linq;

// 1. A lock turns check-then-act into one step
var seatMap = new SeatMap(seats: 1);
var bookings = Enumerable.Range(0, 20).Select(i => Task.Run(() => seatMap.TryBook($"passenger {i}"))).ToArray();
Task.WaitAll(bookings);
Console.WriteLine($"successful bookings: {bookings.Count(t => t.Result)}");   // 1

// 2. Deadlock: two locks taken in opposite orders
object gateLock = new(), standLock = new();
bool Assign(object first, object second, string who)
{
    lock (first)
    {
        Thread.Sleep(50);                                      // give the other thread time to take its first lock
        if (!Monitor.TryEnter(second, 500)) { Console.WriteLine($"{who}: gave up waiting, deadlock"); return false; }
        try { return true; } finally { Monitor.Exit(second); }
    }
}
Task.WaitAll(Task.Run(() => Assign(gateLock, standLock, "thread 1")),
             Task.Run(() => Assign(standLock, gateLock, "thread 2")));
// thread 1: gave up waiting, deadlock
// thread 2: gave up waiting, deadlock
var ordered = new[] { Task.Run(() => Assign(gateLock, standLock, "thread 3")), Task.Run(() => Assign(gateLock, standLock, "thread 4")) };
Task.WaitAll(ordered);
Console.WriteLine($"same order, both succeeded: {ordered.All(t => t.Result)}");   // True

// 3. Compare-and-swap: a shared maximum without a lock
long maxDelay = 0;
Parallel.For(0, 1000, i => RecordMax(ref maxDelay, (i * 37) % 997));
Console.WriteLine(maxDelay);                                   // 996

// 4. A semaphore limits how many run at once
var fareSystem = new SemaphoreSlim(3);
long running = 0, peak = 0;
await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
{
    await fareSystem.WaitAsync();
    try
    {
        RecordMax(ref peak, Interlocked.Increment(ref running));
        await Task.Delay(50);                                  // pretend to call the fare system
    }
    finally { Interlocked.Decrement(ref running); fareSystem.Release(); }
}));
Console.WriteLine($"peak concurrent calls: {peak}");           // 3

// 5. A concurrent collection: safe updates without your own lock
var counts = new ConcurrentDictionary<string, int>();
string[] codes = ["LHR", "JFK", "LHR", "SIN"];
Parallel.For(0, 40_000, i => counts.AddOrUpdate(codes[i % 4], 1, (_, n) => n + 1));
Console.WriteLine(string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
// JFK=10000, LHR=20000, SIN=10000

// 6. Producer and consumer with a bounded queue
using var queue = new BlockingCollection<int>(boundedCapacity: 5);   // the producer waits when 5 are queued
var producer = Task.Run(() => { for (int i = 1; i <= 20; i++) queue.Add(i); queue.CompleteAdding(); });
int sum = 0;
var consumer = Task.Run(() => { foreach (int item in queue.GetConsumingEnumerable()) sum += item; });
await Task.WhenAll(producer, consumer);
Console.WriteLine(sum);                                        // 210

static void RecordMax(ref long target, long value)
{
    long current = Volatile.Read(ref target);
    while (value > current)
    {
        long seen = Interlocked.CompareExchange(ref target, value, current);   // write only if still 'current'
        if (seen == current) return;                           // our write won
        current = seen;                                        // someone else changed it: compare again
    }
}

sealed class SeatMap(int seats)
{
    readonly Lock sync = new();                                // .NET 9 dedicated lock type
    readonly List<string> passengers = [];

    public bool TryBook(string name)
    {
        lock (sync)                                            // one thread at a time in here
        {
            if (passengers.Count >= seats) return false;       // check...
            passengers.Add(name);                              // ...and act, with nothing in between
            return true;
        }
    }
}
```

### Concurrent collections

`System.Collections.Concurrent` provides collections that many threads can use at once without your own locks. `ConcurrentDictionary<TKey, TValue>` uses fine-grained locking for writes and lock-free reads. `ConcurrentQueue<T>` and `ConcurrentStack<T>` are lock-free. `ConcurrentBag<T>` suits cases where the same threads both add and remove items. `BlockingCollection<T>` wraps one of these with blocking and a size limit.

Thread-safe collections make each single operation safe. They don't make a sequence of operations safe. `if (!dict.ContainsKey(k)) dict[k] = v;` is still check-then-act. Use the combined operations: `TryAdd`, `GetOrAdd`, `AddOrUpdate`, `TryRemove` and `TryUpdate`.

One detail catches people out. `GetOrAdd(key, factory)` can call the factory more than once for the same key when threads race, although only one result is kept. If the factory is expensive or has side effects, such as opening a connection, store `Lazy<T>` values, so the real work runs only once.

### Producer and consumer

A common structure is the **producer-consumer** pattern. Some threads produce work items and put them in a queue. Other threads take items off and process them. The queue decouples the two sides, and each side can run at its own pace.

A **bounded queue** has a maximum size. When it is full, producers wait. That stops a fast producer from filling memory when consumers fall behind, a principle called backpressure, covered in The event loop (5.4). `BlockingCollection<T>` does this for threads. For asynchronous code, `System.Threading.Channels` is the modern choice, as Async and await (5.3) shows.

### Knowing what is thread-safe

Every type's documentation says what is safe to share. The .NET docs usually say something like "public static members are thread-safe; instance members are not guaranteed to be". That means: don't share one instance between threads unless you add locking yourself.

A few common types are designed to be shared. `HttpClient` is safe for concurrent requests. Immutable types are always safe to read. Concurrent collections are safe per operation. EF Core's `DbContext` is not safe at all: it must be used by one thread at a time, which is why it is registered as scoped.

### In short

Lock on a private object, keep locked sections short and protect each piece of shared data with exactly one lock. Deadlock needs a circular wait, which taking locks in a fixed order prevents. `Interlocked` operations and compare-and-swap update single values without a lock. Semaphores limit how many threads do something at once. Concurrent collections make single operations safe, but combined steps must use methods like `GetOrAdd`, and bounded queues stop fast producers overwhelming slow consumers.

### What goes wrong

**Locking on public objects.** `lock (this)` or `lock (typeof(X))` lets outside code interfere. Lock on a private `Lock` or object.

**Slow work inside a lock.** A database call or HTTP request inside a lock makes every other thread wait for the network. Copy what you need inside the lock, do the slow work outside.

**Inconsistent lock order.** Two code paths take the same two locks in different orders, and the app freezes under load. Define an order and follow it everywhere.

**Unlocked reads of locked data.** Writes are locked but reads aren't, so readers see stale or half-updated state. Read under the same lock.

**Check-then-act on concurrent collections.** `ContainsKey` followed by an add races. Use `GetOrAdd` or `TryAdd`.

**Expensive `GetOrAdd` factories.** Two threads both create the same connection or load the same file. Wrap values in `Lazy<T>`.

**Sharing a `DbContext` between threads.** EF Core throws "A second operation was started on this context instance" or corrupts its tracking. Create one per unit of work.

**Blocking thread pool threads.** Many threads waiting on locks or I/O starve the pool and the whole app slows down. Use async waiting, as the next chapter explains.

### Cost model

Taking and releasing an uncontended lock costs around 20 nanoseconds. When a thread must actually wait, it may spin briefly and then sleep, which costs microseconds including the context switch. `Interlocked` operations cost a few nanoseconds uncontended and tens to hundreds when many cores fight over the same cache line. A `Mutex` costs about a microsecond per acquisition, because it goes through the kernel. Creating a thread costs tens of microseconds and a megabyte or so of reserved stack; queuing work to the thread pool costs well under a microsecond.

### In other languages (optional)

**JavaScript.** There are no locks for ordinary code, because it runs on one thread. Worker threads coordinate through `Atomics` operations on shared memory, including `Atomics.wait` and `Atomics.notify`.

**Python.** The `threading` module has `Lock`, `RLock`, `Semaphore` and `Condition`, used with `with` blocks. `queue.Queue` is a thread-safe producer-consumer queue.

**Java.** `synchronized` blocks are the equivalent of `lock`. `java.util.concurrent` has locks, atomics, concurrent maps and blocking queues, much like .NET's.

**Go.** `sync.Mutex` and `sync.RWMutex` exist, but channels are the preferred way to coordinate.

**SQL.** Databases lock rows and tables to isolate transactions, and detect deadlocks themselves by choosing a victim transaction to roll back. SQL Server reports this as error 1205, and the right response is to retry.

### Key terms

- **Foreground thread**: A thread that keeps the process alive until it finishes.
- **Thread pool**: A managed set of reusable worker threads that run queued work items.
- **Mutual exclusion**: Guaranteeing that only one thread at a time runs a critical section.
- **Lock**: A mechanism that lets only one thread at a time hold it, used to protect shared data.
- **Monitor**: The lock built into every .NET object, used by the lock statement with ordinary objects.
- **Deadlock**: Threads each waiting for something another holds, so none can continue.
- **Lock ordering**: Always taking multiple locks in the same fixed order, which prevents deadlock.
- **Livelock**: Threads that keep reacting to each other and retrying without any making progress.
- **Contention**: Several threads competing for the same lock or memory location.
- **Lock granularity**: How much data one lock protects.
- **Compare-and-swap**: An atomic operation that writes a new value only if the location still holds an expected value.
- **Lock-free**: An algorithm that coordinates threads with atomic operations instead of locks.
- **Semaphore**: A lock that allows up to a fixed number of threads in at once.
- **Reader-writer lock**: A lock that lets many readers in together, or one writer alone.
- **Mutex**: An operating system lock that can be named and shared between processes.
- **Concurrent collection**: A collection designed for safe use by many threads at once.
- **Producer-consumer**: A pattern where some threads add work to a queue and others take it off and process it.
- **Bounded queue**: A queue with a maximum size, which makes producers wait when it is full.

### Where this leads

Waiting without blocking a thread is in Async and await (5.3). Concurrency without threads or locks is in The event loop (5.4). Splitting work across cores is in Parallelism (5.5). Database locks and deadlocks are in Transactions and isolation (7.4). Locks across machines, which are much harder, come up in Distributed systems basics (12.2).

### Check yourself

1. Remove the `lock` from `SeatMap.TryBook` in `locks.cs` and run it several times. Count how often more than one booking succeeds.
2. Fix the deadlock between threads 1 and 2 by making `Assign` always take the two locks in a fixed order, whatever order they are passed in.
3. Replace `SemaphoreSlim(3)` with `SemaphoreSlim(1)` and explain the new peak and the change in total time.
4. Use `ConcurrentDictionary<string, Lazy<string>>` so that an expensive factory that prints "loading" runs only once per key, even when 20 tasks request the same key at once.
5. Write a counter class that is safe for many threads, twice: once with a lock and once with `Interlocked`. Time both with 8 threads each doing a million increments.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Two transfers deadlocked each other

Moving loyalty points A→B and B→A at the same moment hangs both threads forever.

*Always lock in a fixed order*

```csharp
void Transfer(Account from, Account to, int points)
{
    var (first, second) = from.Id < to.Id ? (from, to) : (to, from);   // global order by id
    lock (first.Gate)
    lock (second.Gate)
    {
        from.Points -= points;
        to.Points += points;
    }
}
```

> **Lesson:** Deadlock needs a circular wait. A single global order for taking locks makes the cycle impossible.

#### Example 2: Limiting calls to a partner API

The partner allows 10 concurrent requests. A batch job fires 500 at once and gets throttled.

*SemaphoreSlim works across await*

```csharp
var gate = new SemaphoreSlim(10);
var tasks = bookings.Select(async b =>
{
    await gate.WaitAsync(ct);
    try { return await partner.ConfirmAsync(b, ct); }
    finally { gate.Release(); }        // released on success and failure
});
var results = await Task.WhenAll(tasks);
```

> **Lesson:** `lock` can't span an await. SemaphoreSlim limits concurrency in async code; always release in finally.

### Concurrency Primitives & Threading

<p class="src">From <em>.NET Backend Deep Dive</em> · Concurrency Primitives & Threading</p>

<a id="dn-39"></a>

#### `lock` doesn't work with async

```csharp
lock (_gate)
{
    await SomethingAsync();   // COMPILE ERROR: can't await inside a lock
}
```

The deeper reason: `lock` (Monitor) has **thread affinity** — whichever thread entered must exit. Async can resume on a different thread, so the release could never happen correctly.

```csharp
// SemaphoreSlim(1,1) = an async-friendly mutex
private readonly SemaphoreSlim _gate = new(1, 1);

await _gate.WaitAsync(ct);
try
{
    await SomethingAsync();
}
finally { _gate.Release(); }   // MUST be in finally, or you deadlock forever
```

> .NET 9 adds `System.Threading.Lock` (faster than `lock(object)`) but it's still **not** async-compatible.

<a id="dn-40"></a>

#### Interlocked — when you only need to count

```csharp
private long _count;

Interlocked.Increment(ref _count);              // atomic, lock-free, extremely fast
Interlocked.Add(ref _count, 5);
Interlocked.Exchange(ref _flag, 1);
var old = Interlocked.CompareExchange(ref _state, newValue, expected);  // CAS

// Lock-free lazy init:
if (_instance is null)
    Interlocked.CompareExchange(ref _instance, new Heavy(), null);
```

Taking a lock for a single `++` is waste. `Interlocked` is roughly 10–20× faster.

<a id="dn-42"></a>

#### `ConcurrentDictionary` — the `GetOrAdd` trap

```csharp
// The factory CAN run multiple times under contention, even though only one result wins
var conn = _dict.GetOrAdd(key, k => new ExpensiveConnection(k));
// -> extra connections created, and the losers are NEVER disposed -> leak
```

```csharp
// Lazy guarantees the factory runs exactly once
var lazy = _dict.GetOrAdd(key, k => new Lazy<ExpensiveConnection>(
    () => new ExpensiveConnection(k), LazyThreadSafetyMode.ExecutionAndPublication));
var conn = lazy.Value;
```

Other traps:

- `dict.Count` and `dict.ToArray()` lock every bucket — slow; avoid on hot paths.
- `foreach` gives a weakly consistent snapshot; you may see a partially updated view.
- It **never evicts** → using it as a cache is a memory leak. Use `IMemoryCache` instead.

<a id="dn-44"></a>

#### Thread pool — tuning and starvation symptoms

```csharp
ThreadPool.GetMinThreads(out var w, out var io);
// Default min = core count. Beyond that the CLR injects only ~1-2 threads per second.
// If your code blocks threads (.Result, long locks, Thread.Sleep), the queue grows
// faster than threads are injected.
```

**Symptoms of thread pool starvation:**

- Low CPU (20–30%) with a p99 latency spike and request timeouts.
- `dotnet-counters`: `threadpool-queue-length` climbing, `threadpool-thread-count` creeping up slowly.

**The cause is almost always** `.Result` / `.Wait()`, sync-over-async inside a third-party library, or a long-held lock.

```csharp
// A band-aid, not a fix
ThreadPool.SetMinThreads(200, 200);
```

<a id="dn-45"></a>

#### `AsyncLocal<T>` vs `ThreadLocal<T>`

```csharp
// ThreadLocal: per THREAD -> wrong for async (continuations change threads)
// AsyncLocal: per LOGICAL CALL CONTEXT -> flows across awaits
private static readonly AsyncLocal<string> _correlationId = new();

// This is exactly how Activity.Current and ILogger scopes work
```

**Careful:** values flow **downward** (into child tasks), not upward. Setting it in a child doesn't affect the parent.

### The .NET Memory Model & Lock-Free Code

<p class="src">From <em>.NET Backend Deep Dive</em> · The .NET Memory Model & Lock-Free Code</p>

<a id="dn-211"></a>

#### The problem: nobody executes your code in order

Part 11 covered synchronization primitives. This is the layer beneath them: what the CPU and the JIT are allowed to reorder, and why "it works on my machine" is a particularly dangerous phrase here.

Three separate actors reorder memory operations:

1. **The C# compiler** — minor reordering.
2. **The JIT** — hoists reads out of loops, keeps values in registers, eliminates "redundant" reads.
3. **The CPU** — store buffers, out-of-order execution, per-core cache coherency delays.

```csharp
// Thread A
_data = 42;
_ready = true;

// Thread B
if (_ready)
    Console.WriteLine(_data);   // CAN PRINT 0
```

Nothing here is a bug in the usual sense. Thread B can observe `_ready == true` before it observes `_data == 42`, because the two writes may become visible in either order.

**x86/x64 has a strong memory model** and mostly hides this. **ARM (Apple Silicon, AWS Graviton, most mobile) is weakly ordered** and does not. This is why a race that never reproduced on developer laptops and x64 CI suddenly appears when you move to Graviton instances to save money — a genuinely common modern surprise.

<a id="dn-212"></a>

#### `volatile` — narrower than people think

```csharp
private volatile bool _stop;   // read = acquire, write = release

// Thread A
while (!_stop) { DoWork(); }   // without volatile, the JIT may hoist the read
                               // out of the loop -> infinite loop in Release only

// Thread B
_stop = true;
```

`volatile` prevents the JIT from caching the value in a register and constrains reordering around that access. What it does **not** give you is atomicity of compound operations:

```csharp
private volatile int _count;
_count++;              // STILL a race: read, add, write - three steps
```

For that you need `Interlocked.Increment`. `volatile` also can't be applied to `long`/`double` on 32-bit, and it's a per-field property, so `volatile` on a reference doesn't make the object it points to safe.

**The practical rule: use `volatile` only for a simple flag, and prefer `Interlocked` or a lock for anything else.** If your reasoning requires a diagram of memory barriers, use a lock — the cost of an uncontended lock (~20ns) is almost never the actual problem.

<a id="dn-213"></a>

#### `Interlocked` and the CAS loop

```csharp
Interlocked.Increment(ref _count);          // atomic, full fence
Interlocked.Exchange(ref _ref, newValue);
Interlocked.CompareExchange(ref _v, newV, expected);   // the foundation of lock-free code

// Read-modify-write without a lock, safely
int original, updated;
do
{
    original = _value;
    updated  = Transform(original);
} while (Interlocked.CompareExchange(ref _value, updated, original) != original);
```

That loop is the shape of essentially all lock-free algorithms: compute a candidate, then swap it in only if nothing changed underneath you, else retry.

**The ABA problem:** CAS compares a *value*, not a history. If it changes A → B → A between your read and your swap, the CAS succeeds even though the world moved. It rarely bites with counters and bites hard with pointer-based structures — which is one more reason to use the concurrent collections in the BCL rather than writing your own.

<a id="dn-214"></a>

#### Double-checked locking, done correctly

```csharp
// Just use Lazy<T>. This is the whole section's real advice.
private static readonly Lazy<Config> _config =
    new(() => Load(), LazyThreadSafetyMode.ExecutionAndPublication);

// If you insist on hand-rolling it, the volatile is NOT optional:
private static volatile Config? _instance;
private static readonly object _gate = new();

public static Config Instance
{
    get
    {
        if (_instance is null)                  // fast path, no lock
            lock (_gate)
                if (_instance is null)          // re-check under the lock
                    _instance = new Config();   // volatile write publishes it safely
        return _instance;
    }
}
```

Without `volatile`, another thread can observe a **non-null reference to a partially constructed object** — the reference write can become visible before the constructor's writes. Notoriously, this is benign on x86 and broken on ARM.

<a id="dn-215"></a>

#### What actually makes an object safe to share

In order of preference:

1. **Immutability.** An object that never changes after construction needs no synchronization at all. This is why `record`, `readonly`, and frozen collections (Part 11.8) matter beyond style.
2. **Confinement.** Don't share it. Give each thread its own copy — the reason `IDbContextFactory` exists (Part 3.1).
3. **A concurrent collection** from the BCL, whose authors have already thought about all of the above.
4. **A lock**, held briefly, around a small critical section.
5. **Lock-free code**, only with benchmarks proving it's worth it.

> Note that `readonly` on a field prevents *reassignment*, not mutation of what it points to. `private readonly List<T> _items` is not thread-safe in the slightest.

##### Interview answer
> "The compiler, JIT and CPU can all reorder memory operations, so two threads can disagree about the order of writes. x64 hides most of it; ARM doesn't, which is how these bugs surface when you move to Graviton. `volatile` stops the JIT caching a value and constrains reordering but gives no atomicity — `_count++` is still a race. I reach for immutability or confinement first, then `Interlocked` or a lock, and only write lock-free code with a benchmark justifying it."

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a24"></a>

#### Concurrency primitives: locks, channels, and parallelism

Distinguish two problems. *Concurrency* is dealing with many things in flight (I/O bound → async). *Parallelism* is doing many things at once on many cores (CPU bound → threads).

| Tool                      | For                                                                   |
|---------------------------|-----------------------------------------------------------------------|
| `lock` / `Monitor`        | Short critical sections, synchronous only — you cannot `await` inside |
| `SemaphoreSlim`           | Async-safe mutual exclusion (`await sem.WaitAsync()`) and throttling  |
| `Interlocked`             | Lock-free counters and compare-and-swap                               |
| `Concurrent*` collections | Shared dictionaries/queues without your own locking                   |
| `Channel<T>`              | Async producer/consumer pipelines with backpressure                   |
| `Parallel.ForEachAsync`   | Bounded fan-out over a collection                                     |

``` csharp
var channel = Channel.CreateBounded<WorkItem>(1000);   // backpressure when full

// producer
await channel.Writer.WriteAsync(item, ct);

// consumer
await foreach (var item in channel.Reader.ReadAllAsync(ct))
    await ProcessAsync(item, ct);

// bounded fan-out
await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (url, ct) => await _http.GetAsync(url, ct));
```

###### How it actually works

What a `lock` actually costs, and why the alternatives exist.

| Operation                             | Rough cost | Notes                                   |
|---------------------------------------|------------|-----------------------------------------|
| Uncontended `lock`                    | ~20 ns     | A compare-and-swap on the object header |
| Contended `lock`, brief               | ~100 ns    | Spins briefly before blocking           |
| Contended, blocking                   | ~1–10 µs   | Kernel transition and a context switch  |
| `Interlocked.Increment`               | ~5 ns      | One atomic instruction, no lock at all  |
| `SemaphoreSlim.WaitAsync` uncontended | ~30 ns     | No thread blocked when contended        |

The important number is not the uncontended case — it is that a blocked thread pool thread cannot do anything else, and the pool only injects one or two replacements per second. Ten threads blocked on a lock held across a 200 ms database call is a ten-second recovery, not a 200 ms one.

``` csharp
// this compiles, and it is a serious bug
lock (_gate)
{
    var data = _db.Query();     // synchronous I/O inside a lock
    _cache[key] = data;         // every other thread is blocked for the whole round trip
}

// async-safe version
await _gate.WaitAsync(ct);      // SemaphoreSlim(1,1) - the thread is released, not blocked
try
{
    var data = await _db.QueryAsync(ct);
    _cache[key] = data;
}
finally { _gate.Release(); }
```

###### Channels: the pattern to reach for first

Most hand-rolled producer/consumer code with a `ConcurrentQueue`, a `ManualResetEvent` and a polling loop is a worse version of `Channel<T>`. Channels give you async waiting, backpressure and clean completion semantics.

``` csharp
public sealed class IngestPipeline : BackgroundService
{
    private readonly Channel<Reading> _channel = Channel.CreateBounded<Reading>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,   // producer awaits instead of dropping
            SingleReader = false, SingleWriter = false
        });

    public ValueTask EnqueueAsync(Reading r, CancellationToken ct) =>
        _channel.Writer.WriteAsync(r, ct);            // applies backpressure when full

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // four independent consumers over one channel
        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await foreach (var batch in _channel.Reader
                               .ReadAllAsync(ct)
                               .Buffer(500, TimeSpan.FromSeconds(2)))   // batch for efficiency
                await _sink.WriteBatchAsync(batch, ct);
        }, ct));

        await Task.WhenAll(workers);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _channel.Writer.Complete();          // no more writes; readers drain then finish
        await base.StopAsync(ct);
    }
}
```

`BoundedChannelFullMode` is the design decision that matters. `Wait` propagates backpressure to the producer, which is usually correct. `DropOldest` suits metrics and telemetry where fresh data beats complete data. An unbounded channel has no backpressure at all, which means a slow consumer turns into an out-of-memory kill rather than a slowdown — almost never what you want in a service.

###### What this looks like when it breaks

Three concurrency bugs that survive code review because they look correct.

``` csharp
// 1. ConcurrentDictionary.GetOrAdd runs the factory more than once under contention
var conn = _pool.GetOrAdd(key, k => CreateExpensiveConnection(k));
// two threads race, two connections are created, one is silently discarded and never disposed.
// fix: store a Lazy<T>, so only the value factory races and only one runs.
var conn2 = _pool.GetOrAdd(key,
    k => new Lazy<Connection>(() => CreateExpensiveConnection(k))).Value;

// 2. check-then-act is not atomic even on a concurrent collection
if (!_dict.ContainsKey(k)) _dict.Add(k, v);       // another thread can add between the two
_dict.TryAdd(k, v);                               // the atomic version

// 3. a non-volatile bool as a stop flag
private bool _stop;                               // may be cached in a register forever
while (!_stop) { }                                // can loop until the process dies
// fix: CancellationToken, or Volatile.Read / Interlocked
```

<a id="be-a38"></a>

#### The memory model, volatile and lock-free code

The concurrency primitives section covers what to use. This is the layer underneath: what guarantees the runtime actually gives you about the order in which one thread's writes become visible to another. You will rarely write lock-free code, and you need this to understand why the code you do write is correct.

##### The problem: nobody executes your code in order

``` text
  three independent reorderings, all legal

  compiler   may hoist a repeated read out of a loop into a register
  JIT        may reorder independent instructions
  CPU        may execute out of order, and store buffers delay writes becoming visible

  thread A                       thread B
    _data = 42;                    while (!_ready) { }
    _ready = true;                  Console.WriteLine(_data);

  without barriers, B can legally observe _ready == true and _data == 0,
  because the two writes may become visible in either order.
  On x86 this is rare (strong-ish model); on ARM it happens readily -
  which is why "it works on my laptop, fails on the ARM node" is a real bug class.
```

| Construct                | Guarantee                                                        |
|--------------------------|------------------------------------------------------------------|
| `volatile` read          | Acquire — no later read or write moves before it                 |
| `volatile` write         | Release — no earlier read or write moves after it                |
| `Volatile.Read/Write`    | The same, as explicit method calls; preferred for clarity        |
| `Interlocked.*`          | Full barrier, and the operation is atomic                        |
| `lock` / `Monitor`       | Full barrier on enter and exit, plus mutual exclusion            |
| `Thread.MemoryBarrier()` | Full barrier, no atomicity — almost never what you want directly |

``` csharp
// the fix, minimally
private volatile bool _ready;          // or: Volatile.Write(ref _ready, true)

// note what volatile does NOT give you: atomicity of compound operations
private volatile int _counter;
_counter++;                            // STILL a race: read, add, write are three steps
Interlocked.Increment(ref _counter);   // atomic
```

###### Atomicity guarantees, and the tearing you can actually hit

Reads and writes of reference types and of primitives up to the platform's native word size are atomic — you never observe half a value. Anything wider can *tear*.

| Type                                       | Atomic?           | Note                                                          |
|--------------------------------------------|-------------------|---------------------------------------------------------------|
| `bool`, `int`, `uint`, `float`, references | Yes               | On all supported platforms                                    |
| `long`, `double`                           | Only on 64-bit    | On 32-bit, use `Interlocked.Read`                             |
| `decimal`                                  | **No** — 128 bits | Can tear. Guard it                                            |
| Any struct larger than a word              | **No**            | A `Guid` or a multi-field struct can be observed half-written |

``` csharp
// tearing, concretely - two threads writing different values
private Guid _current;                     // 16 bytes

// thread A: _current = guidA;
// thread B: _current = guidB;
// thread C can read the first 8 bytes of A and the last 8 bytes of B:
//   a Guid that neither thread ever wrote, and that exists nowhere else.

// fix: swap an immutable reference instead of mutating a wide value
private volatile ImmutableConfig _config;          // reference write IS atomic
_config = new ImmutableConfig(...);                // publish a whole new object
// readers see either the old object or the new one, never a mixture.
```

That last pattern — publish an immutable object by swapping a reference — is the single most useful lock-free technique in ordinary application code. It is how you hot-reload configuration, rebuild a lookup table, or swap a compiled policy with no lock on the read path at all.

###### Compare-and-swap, and the two traps

``` csharp
// the canonical CAS loop: read, compute, publish only if nothing changed
public void AddTag(string tag)
{
    ImmutableHashSet<string> original, updated;
    do
    {
        original = _tags;                                    // volatile read
        updated  = original.Add(tag);                        // no mutation of shared state
    }
    while (Interlocked.CompareExchange(ref _tags, updated, original) != original);
    //     "set _tags to updated, but ONLY if it is still original"
    //     if another thread won the race, loop and recompute against the new value
}

// max-tracking without a lock
public void RecordLatency(long ms)
{
    long current;
    while (ms > (current = Volatile.Read(ref _maxMs)))
        Interlocked.CompareExchange(ref _maxMs, ms, current);
}
```

- **The loop body must be idempotent and side-effect free.** It may run several times. Sending an email inside a CAS loop sends several emails.
- **Contention makes it worse, not better.** Under heavy contention every thread spins and recomputes; a `lock` is often faster because losers sleep instead of burning CPU.
- **The ABA problem.** A value can change from A to B and back to A between your read and your compare, and CAS cannot tell. With immutable object references this is usually harmless; with pooled or recycled objects it is not.
- **False sharing.** Two hot counters in adjacent fields land on the same 64-byte cache line, so every write by one core invalidates the other's cache. Pad them, or use `[StructLayout]` with explicit offsets — this can cost an order of magnitude on a hot path.

``` csharp
// false sharing, and the padding that fixes it
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedCounter
{
    [FieldOffset(0)]  public long Value;      // one counter per cache line
}
private readonly PaddedCounter[] _perCore = new PaddedCounter[Environment.ProcessorCount];
```

> **\[Note\]**
>
> **When to reach for any of this.** When profiling shows lock contention on a genuinely hot path, and not before. Correct lock-free code is hard to write, harder to review, and nearly impossible to test for the races it is meant to prevent. The read-mostly immutable-swap pattern is the exception: it is simple, obviously correct, and worth using widely.

### Under the Hood

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Under the Hood</p>

<p class="pd">What actually happens between dotnet run and a byte on the wire. Assembly loading, the JIT, the thread pool, Kestrel, routing, model binding, filters and the DI container — the machinery that makes everything above it feel like magic until it breaks.</p>

<a id="be-a44"></a>

#### The thread pool and how it starves

The Thread pool — see Terminology"\>thread pool has a global queue plus a local work-stealing queue per worker. Tasks queued from inside a pool thread go on that thread's local queue (LIFO, better cache locality); everything else goes global.

``` text
  worker 1  [local queue]  <- Task.Run from inside worker 1 lands here (LIFO)
  worker 2  [local queue]  <- steals from the tail of others when idle
  worker 3  [local queue]
                 |
            [ global queue ]  <- work queued from non-pool threads

  IO completion ports / epoll deliver completed I/O -> continuations queued here
```

The pool grows slowly on purpose: after the initial count (roughly the core count), it injects at most one or two threads per second while a hill-climbing algorithm decides whether more threads actually improve throughput. That deliberate slowness is what makes starvation so painful.

##### Thread pool starvation

1.  Code somewhere blocks a pool thread — `.Result`, `.Wait()`, `lock` held across I/O, or a synchronous file read.
2.  Under load, all workers end up blocked waiting on things that need *other* pool threads to complete.
3.  The pool injects one new thread per second, which is immediately consumed the same way.
4.  Latency climbs, then requests time out, while CPU sits near idle — the signature symptom.
5.  Diagnose with `dotnet-counters`: `threadpool-queue-length` growing while CPU is low.

``` csharp
// the fix is always the same: never block, all the way down
await using var stream = File.OpenRead(path);       // async file handle
var data = await JsonSerializer.DeserializeAsync<T>(stream, ct);

// if you MUST bridge sync -> async at a boundary, do it once, at the top,
// on a dedicated thread - never inside a request handler
```

> **\[Note\]**
>
> **async — see Terminology"\>Sync over async in libraries.** If you own a library, ship a real async path rather than `Task.Run(() => SyncMethod())`. Wrapping sync work in a task does not make it async; it just moves the blocking to a pool thread you do not control.

###### Worked example

A starvation incident, reconstructed from the counters. An API doing 300 requests per second suddenly reports p99 latency of 30 seconds and a rising rate of timeouts, while CPU sits at 12%.

``` text
  minute 0   threadpool-thread-count   16      queue-length     0      cpu 12%
  minute 1   threadpool-thread-count   17      queue-length    40      cpu 12%
  minute 2   threadpool-thread-count   19      queue-length   310      cpu 11%
  minute 3   threadpool-thread-count   21      queue-length  1,240     cpu 11%
  minute 5   threadpool-thread-count   25      queue-length  4,900     cpu 10%

  CPU flat + queue growing + thread count creeping by ~1/second
  = starvation, not overload. The pool is injecting threads as fast as it will,
    and each new one is immediately consumed by the same blocking call.
```

The tell is the combination: an overloaded service pins the CPU, a starved one does not. If CPU is low and the queue is growing, threads are blocked on something rather than working.

``` csharp
// the culprit, found in a shared library three levels down
public string GetSetting(string key)
{
    return _configService.GetSettingAsync(key).GetAwaiter().GetResult();   // <- here
}
// called once per request, holding a pool thread for the duration of an HTTP call
// to the config service. At 300 rps with a 90 ms config call, that is ~27 threads
// permanently blocked - more than the pool will grow to in the first half minute.
```

1.  Confirm with `dotnet-counters`: low CPU, growing `threadpool-queue-length`.
2.  Capture a dump: `dotnet-dump collect -p <pid>`.
3.  In `dotnet-dump analyze`, run `clrstack -all` and look for many threads sharing one stack shape.
4.  Any stack containing `GetAwaiter().GetResult()`, `.Result`, `Task.Wait`, `Monitor.Enter` or a synchronous `Read` is your answer.
5.  Fix the blocking call. As a stopgap only, raise `ThreadPool.SetMinThreads` so injection is instant rather than one per second.

``` csharp
// stopgap, not a fix - buys time while the real cause is removed
ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
```

###### Why the pool grows so slowly, and what changed

The hill-climbing algorithm measures throughput, adds a thread, and measures again. If throughput did not improve, it backs off. This is correct for CPU-bound work — more threads than cores makes things worse — and pathological for blocked threads, where the pool cannot tell the difference between ‘busy’ and ‘stuck’.

.NET 6 introduced a managed thread pool implementation with better blocking detection, and .NET 9 improved injection heuristics further, so modern runtimes recover from a transient blocking spike faster than .NET Framework did. The underlying advice is unchanged: the pool is a poor place to block, and the fix is always upstream of the pool.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a76"></a>

#### Ambient context and AsyncLocal

Some values need to be available everywhere in a call chain without being threaded through every signature: the current user, the tenant, a correlation id, a transaction. Passing them explicitly is purest, and past a certain depth it becomes forty parameters of noise. `AsyncLocal<T>` is the mechanism that makes an ambient value flow correctly across `await` boundaries.

``` text
  ThreadStatic  -> tied to a thread. An await can resume on a DIFFERENT thread. Broken.
  AsyncLocal    -> tied to the EXECUTION CONTEXT, which flows across awaits and Task.Run.

  request start   AsyncLocal.Value = { tenant: "acme", correlation: "abc" }
    -> await Db()          (thread 5 -> thread 9)  value still visible
      -> await Http()      (thread 9 -> thread 3)  value still visible
        -> Task.Run(...)                            value COPIED into the new context
           mutating a mutable object here affects the parent; reassigning does not
```

``` csharp
public sealed class TenantContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string TenantId =>
        Current.Value ?? throw new InvalidOperationException("No tenant in scope.");

    public IDisposable Begin(string tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Restore(() => Current.Value = previous);   // restores on dispose
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

// middleware sets it once per request
app.Use(async (ctx, next) =>
{
    var tenant = ctx.User.FindFirst("tenant_id")?.Value;
    if (tenant is null) { ctx.Response.StatusCode = 403; return; }

    using var _ = _tenants.Begin(tenant);
    await next(ctx);
});

// a background job sets it per work item
foreach (var job in batch)
{
    using var _ = _tenants.Begin(job.TenantId);
    await _handler.RunAsync(job, ct);
}
```

###### Where this is right, and where it is a service locator in disguise

| Good ambient value                                            | Bad ambient value                                             |
|---------------------------------------------------------------|---------------------------------------------------------------|
| Correlation / trace id                                        | Anything a method's behaviour depends on for business reasons |
| Tenant id, in a system where every operation is tenant-scoped | A repository or a service — that is a service locator         |
| Current user, for auditing                                    | Configuration — inject `IOptions`                             |
| An ambient transaction, at the infrastructure layer           | A feature flag that changes what a method computes            |
| Culture and time zone for formatting                          | Anything you would want to vary in a unit test                |

The test is whether the value is *context* or *dependency*. Context describes the situation the code is running in and is uniform across the whole call tree. A dependency is something the method collaborates with, and hiding it makes the code untestable and unreadable — which is exactly the objection to service locators.

> **\[Trap\]**
>
> **Common trap.** `AsyncLocal` does not flow *out* of a child context. Setting the value inside a `Task.Run` and expecting the caller to see it will silently do nothing. It also does not flow across a message broker, an HTTP call or a process boundary — those need explicit propagation in headers, which is precisely what OpenTelemetry's `traceparent` does.

Note that ASP.NET Core's `IHttpContextAccessor` is built on exactly this mechanism, and inherits the same caveat: capture the values you need before starting background work, because the context is pooled and will be reset.

### The Collections

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · The Collections</p>

<a id="be-b282"></a>

#### Concurrent collections

| Type                        | Notes                                                                        |
|-----------------------------|------------------------------------------------------------------------------|
| `ConcurrentDictionary<K,V>` | lock-free reads; writes use striped locks                                    |
| `ConcurrentQueue<T>`        | lock-free, segmented                                                         |
| `ConcurrentStack<T>`        | lock-free, linked                                                            |
| `ConcurrentBag<T>`          | thread-local storage with stealing; **only** for same-thread produce/consume |
| `BlockingCollection<T>`     | blocking wrapper — **avoid in async code**, use `Channel<T>`                 |

**The `ConcurrentDictionary` trap everyone hits:**

``` csharp
var v = dict.GetOrAdd(key, k => Expensive(k));
```

`GetOrAdd` guarantees **one value is stored**, not that the factory runs once. Under contention it may run on several threads simultaneously, with all but one result discarded. If the factory is expensive or has side effects, wrap it:

``` csharp
var v = dict.GetOrAdd(key, k => new Lazy<T>(() => Expensive(k))).Value;
```

Only one `Lazy` is materialized, so the expensive work happens once. This is the same single-flight problem as cache stampedes — see the caching reference, Part III.1.

Also: `Count` acquires **all** locks and is O(n); avoid it in hot paths. Enumeration is a moving snapshot, not a consistent one.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · async / await · Ground Floor</p>

<a id="be-c311"></a>

#### The .NET thread pool, in detail

"The pool has 32 threads" was a simplification. The real structure matters for diagnosis.

**Two kinds of threads.**

- **Worker threads** run your queued work: `Task.Run`, timer callbacks, and continuations that get scheduled rather than inlined.
- **I/O completion threads** service completions arriving from IOCP (Windows) or the epoll loop (Linux). These are the threads that pick up "your socket read finished" and run the continuation.

`ThreadPool.GetAvailableThreads(out worker, out io)` reports both. Starvation usually shows in workers, but blocking inside an I/O completion callback starves the other pool and is nastier because it's less familiar.

**Queues.** There's one global queue plus a **local queue per worker thread**. A thread queuing work from inside a pool thread pushes to its own local queue (LIFO, cache-friendly — the data is likely still hot). Idle threads **work-steal** from other threads' local queues (FIFO from the far end, to grab the oldest and least cache-relevant item). This is why fine-grained tasks scale better than you'd naively expect.

**Thread injection, and why starvation looks like a slow leak.** The pool starts with `MinThreads` (default: `Environment.ProcessorCount`). Below that count it creates threads instantly. **Above it, injection is throttled to roughly one or two new threads per second**, governed by a **hill-climbing** algorithm that watches throughput and decides whether more threads are actually helping.

That throttle is the reason starvation has its distinctive signature. Traffic arrives, all threads block, and the pool responds by adding *one thread per second* to a deficit of hundreds. So you see:

- Latency climbing steadily over 30–60 seconds rather than spiking instantly
- Thread count creeping upward on the counter, never catching up
- CPU flat and low throughout
- Recovery taking minutes after load drops, because those injected threads linger

`ThreadPool.SetMinThreads(200, 200)` raises the instant-creation floor and masks the symptom immediately. It's a tourniquet: you're paying 200 MB of stacks to avoid fixing the blocking call. Use it to survive an incident, then find the `.Result`.

**On modern .NET the pool is written in C#** (`Portable Thread Pool`), not native, which is why the counters are so detailed and why the behavior is now identical across platforms.

------------------------------------------------------------------------

### How a computer runs a program

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How a computer runs a program</p>

<a id="be-m7"></a>

#### Threads, scheduling, and context switches

A **thread** is one sequence of execution: a program counter, a set of register values, and a stack. A process starts with one and may create more. All threads in a process *share its address space* — same heap, same globals, same open files — which is what makes them useful and what makes them dangerous ([The memory model: what can be reordered](#be-m131)).

``` text
Process
 ├── address space (shared by all threads)
 │     heap, statics, loaded code, open handles
 ├── Thread 1: own stack, own registers  (§23)
 ├── Thread 2: own stack, own registers
 └── Thread 3: own stack, own registers
```

There are always more threads than cores, so the kernel **schedules**: each runnable thread gets a slice of a core (typically a few milliseconds), then is preempted and another runs. Switching means saving one thread's registers, loading another's, and switching page tables if the process changed.

``` text
A context switch costs roughly 1-10 microseconds directly, and more indirectly:
the new thread's data is not in the CPU caches, so it starts with a burst of
cache misses (§22). A thread that runs for 50us and then
blocks spends a significant fraction of its life switching.
```

**Threads are expensive objects.** Each reserves a stack — 1 MB by default on Windows — plus kernel bookkeeping. A thousand threads is a gigabyte of reserved stack and a scheduler with a thousand candidates to consider.

> **\[Why it matters\]**
>
> **This is the entire argument for `async`.** A thread blocked waiting for a database response is a megabyte of stack and a scheduler entry doing nothing at all. Asynchronous I/O lets one thread issue thousands of outstanding requests and process each as it completes, so concurrency is bounded by memory and by the remote system rather than by thread count. It is also why blocking on an async call is so damaging: it converts a cheap waiting request back into an expensive parked thread ([Async data access: starvation and deadlock](05-03-async-and-await.md#be-m136)).

### Concurrency over shared data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Concurrency over shared data</p>

<a id="be-m131"></a>

#### The memory model: what can be reordered

Your code is not the instruction stream. The C# compiler, the JIT, and the CPU may all reorder reads and writes, as long as a *single thread* cannot tell. Another thread absolutely can tell.

``` text
// Thread A                    // Thread B
_data = new Payload(...);      if (_ready)
_ready = true;                     Use(_data);   // may see a null or partly-built _data
```

Nothing in the language guarantees thread B sees those two writes in that order. On x86/x64 the hardware is strongly ordered so this usually works by accident; on ARM — Apple silicon, Graviton, most phones — it does not, which is why this class of bug appears when a service moves to ARM instances.

| Construct                            | Guarantee                                                            |
|--------------------------------------|----------------------------------------------------------------------|
| plain field read/write               | none across threads                                                  |
| `volatile` / `Volatile.Read`/`Write` | acquire on read, release on write — ordering only, **not atomicity** |
| `Interlocked.*`                      | atomic read-modify-write + full fence                                |
| `lock`                               | mutual exclusion + full fence on entry and exit                      |
| `Thread.MemoryBarrier()`             | full fence, no exclusion                                             |
| `static readonly` init               | the runtime guarantees safe publication                              |

> **\[Trap\]**
>
> **Torn reads.** Reads and writes are atomic only for types up to the platform word size — `int`, `bool`, references, and `long`/`double` on 64-bit. A `decimal`, a `DateTime`, a `Guid`, or any multi-field struct can be observed *half-updated* by another thread. There is no exception; you simply read a value that never existed. Use `Interlocked`, a lock, or publish an immutable object by reference instead.

> **\[Tip\]**
>
> **The rule that avoids all of this.** Do not share mutable state. Share immutable snapshots and swap the reference ([Immutable snapshot publishing](04-04-immutability-and-pure-functions.md#be-m134)), or move the data through a queue so only one thread owns it at a time ([Channels and async pipelines](05-05-parallelism.md#be-m135)). Lock-free reasoning is genuinely hard and the failure mode is silent corruption on hardware you may not be testing on.

Worked example: [Case: the lookup that got slower every week](14-03-worked-cases-from-production.md#be-m302)

First principles: [Threads, scheduling, and context switches](#be-m7)

<a id="be-m132"></a>

#### Interlocked, volatile, and what lock guarantees

``` csharp
// NOT atomic: read, add, write - three operations, any of which can interleave.
_counter++;

// Atomic:
Interlocked.Increment(ref _counter);
Interlocked.Add(ref _total, amount);
Interlocked.Exchange(ref _current, newValue);              // set, return old
Interlocked.CompareExchange(ref _current, newValue, expected);  // the CAS primitive

// .NET 9 adds the ones people kept writing by hand:
Interlocked.And(ref _flags, mask);
Interlocked.Or(ref _flags, mask);
```

**Compare-and-swap** is how every lock-free algorithm is built: read the current value, compute a new one, and swap it only if nobody changed it in between. If they did, retry.

``` csharp
// Lock-free "update this immutable value" - the general pattern.
static void UpdateMax(ref long location, long candidate) {
    long current;
    do {
        current = Volatile.Read(ref location);
        if (candidate <= current) return;
    } while (Interlocked.CompareExchange(ref location, candidate, current) != current);
}
```

``` csharp
// .NET 9 introduced a dedicated Lock type. Prefer it over lock(object):
private readonly Lock _gate = new();
lock (_gate) { /* ... */ }
using (_gate.EnterScope()) { /* ... */ }    // explicit scope form
```

> **\[Trap\]**
>
> **Never lock on these:** `this` (callers can lock your instance and deadlock you), a `Type` object (shared across the whole AppDomain), a `string` (interned literals are shared process-wide), or any object you also expose publicly. Use a dedicated private `Lock` or `object` field that nothing else can reach.

> **\[Trap\]**
>
> **Never `await` inside a `lock`.** It does not compile with the `Lock` type and it is a bug with `Monitor`: the lock is thread-affine, the continuation may resume on a different thread, and you get an exception on release or an unreleased lock. For async mutual exclusion use `SemaphoreSlim(1,1)` with `WaitAsync` ([Choosing a synchronization primitive](#be-m133)).

Worked example: [Case: the bug that only appeared on ARM](14-03-worked-cases-from-production.md#be-m309)

<a id="be-m133"></a>

#### Choosing a synchronization primitive

| Need                                | Use                                                             |
|-------------------------------------|-----------------------------------------------------------------|
| short critical section, synchronous | `lock` / `Lock`                                                 |
| critical section containing `await` | `SemaphoreSlim(1, 1)` + `WaitAsync`                             |
| limit concurrency to N              | `SemaphoreSlim(N, N)`                                           |
| a counter or flag                   | `Interlocked`                                                   |
| many readers, rare writers          | immutable snapshot swap — *not* a reader-writer lock            |
| one-time expensive initialisation   | `Lazy<T>` (`ExecutionAndPublication`)                           |
| one-time async initialisation       | `Lazy<Task<T>>`                                                 |
| shared mutable map                  | `ConcurrentDictionary` ([Concurrent collections](#be-b282))     |
| hand work to another stage          | `Channel<T>`                                                    |
| wait for a signal                   | `TaskCompletionSource`, `SemaphoreSlim`, `ManualResetEventSlim` |

``` csharp
// Async mutual exclusion - the standard shape:
private readonly SemaphoreSlim _gate = new(1, 1);

public async Task<Config> GetAsync(CancellationToken ct) {
    if (_cached is not null) return _cached;              // fast path, no wait
    await _gate.WaitAsync(ct);
    try {
        return _cached ??= await LoadAsync(ct);           // double-check inside
    }
    finally { _gate.Release(); }                          // ALWAYS in finally
}
```

> **\[Trap\]**
>
> **`ReaderWriterLockSlim` is almost never the right answer.** It is several times more expensive to acquire than a plain lock even in the uncontended read case, it has recursion and upgrade rules that produce deadlocks, and it does not work with `await`. If reads dominate, publish an immutable snapshot instead — readers then need no synchronisation at all ([Immutable snapshot publishing](04-04-immutability-and-pure-functions.md#be-m134)).

> **\[Tip\]**
>
> **`Lazy<T>` defaults matter.** `LazyThreadSafetyMode.ExecutionAndPublication` (the default for the parameterless constructor) guarantees the factory runs exactly once. `PublicationOnly` lets it run concurrently and keeps one result — cheaper, but wrong if the factory has side effects or creates something disposable. Know which you picked.

Runtime context: [Type loading, statics, and initialization order](02-02-compilers-interpreters-and-jit.md#be-m43)

First principles: [System calls: crossing into the kernel](01-07-the-operating-system.md#be-m8)
