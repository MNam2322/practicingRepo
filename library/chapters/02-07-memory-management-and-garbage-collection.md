---
id: "2-7"
number: "2.7"
title: "Memory management and garbage collection"
part: 2
part_title: "From source code to running program"
words: 10001
minutes: 43
---

# 2.7 Memory management and garbage collection

> **Quick look.** How the garbage collector decides what to free, when it pauses you, and how you still leak memory.
>
> - The GC frees everything it can't reach from roots (locals, statics, handles).
> - Generations make most collections cheap because most objects die young; gen 2 collections cause latency spikes.
> - Cost scales with objects that survive, not with garbage, so avoid mid-life objects.
> - Objects of 85,000 bytes or more go to the Large Object Heap, which fragments.
> - Server GC suits web apps and respects container memory limits.
> - Leaks come from references you forgot: event handlers, unbounded caches, statics.
> - Span, ArrayPool and pooling cut allocations in hot paths; never call GC.Collect() in production code.


### The big picture

Programs create objects constantly, and something has to free their memory once they are no longer needed. In C#, that something is the garbage collector: the part of the .NET runtime that periodically finds objects nothing can reach any more and reclaims their memory.

It works so well that most code can ignore it. This chapter explains how it decides what is garbage, why it groups objects by age, when it pauses your program, and how programs still leak memory despite it. That is the knowledge you need when a service's memory keeps climbing, or its response times spike for no obvious reason.

### Before you start

**Builds on:** 1.5 Memory, 2.6 Scope and lifetime.

**Ideas explained later:**

- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Throughput** (6.1): The rate a transfer actually achieves after overheads and sharing.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **HTTP request** (6.6): A message from client to server: method, path, headers and an optional body.
- **Latency** (6.1): How long one piece of data takes to travel from one end to the other.
- **Project file** (11.2): The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, classes and properties, null operators. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. How does a tracing garbage collector decide what is garbage?**

> It starts from roots, such as local variables in running methods and static fields, and follows every reference. Anything it can reach is live. Everything else is garbage.

**2. Why does .NET divide the heap into generations?**

> Most objects die young. Collecting only the newest objects, generation 0, is quick and frees most of the garbage. Older generations are collected less often.

**3. What is the weakness of reference counting on its own?**

> Cycles. Two objects that refer to each other keep each other's count above zero forever, even when nothing else can reach them. Reference-counted systems add weak references or a separate cycle collector.

**4. Can a .NET program leak memory?**

> Yes. The collector only frees what can't be reached. An event handler never removed, a static list that only grows or a cache with no limit keeps objects reachable forever.

**5. What does a finalizer cost?**

> An object with a finalizer survives at least one extra collection, because its finalizer must run first on a separate thread. That promotes it to an older generation. Finalizers are a safety net, not a cleanup plan.

**6. What is the difference between workstation and server GC?**

> Workstation GC uses one heap and aims for small pauses and low memory, suited to desktop apps. Server GC uses one heap per core and several collector threads, aiming for throughput. ASP.NET Core apps use server GC by default.

**7. Should you call GC.Collect() to free memory?**

> Almost never. The collector tunes itself, and forcing collections usually makes things slower by promoting live objects. Exceptions are benchmarks and a few special cases, such as right after loading a large one-time data set.

### The problem

Programs create objects on the heap constantly. Something must free each one when it is no longer needed. Memory (1.5) showed the choices: the programmer frees memory by hand, the compiler works it out from ownership rules, or the runtime finds unused memory automatically.

Freeing by hand causes two kinds of bugs. Forget to free, and memory leaks. Free too early, and code later reads memory that now belongs to something else. Automatic memory management removes the second kind entirely and most of the first.

### Reference counting

The simplest automatic scheme gives every object a counter. Each new reference to the object adds one. Each reference that goes away subtracts one. When the count reaches zero, the object is freed immediately.

This is **reference counting**. Swift, Objective-C, Python and C++'s `shared_ptr` use it. Memory is freed promptly and predictably. The costs are updating counters on every assignment, which is slow when threads share objects, and cycles. If object A refers to B and B refers to A, both counts stay at one forever, even when nothing else can reach either. This is a **reference cycle**. Swift makes programmers mark some references as weak to break cycles. Python adds a separate collector that looks for them.

### Tracing collection

.NET, Java, JavaScript and Go use **tracing garbage collection** instead. The collector doesn't track references as they change. From time to time, it finds out what is still in use.

It starts from the **GC roots**: references the program can use directly. Roots include local variables in every running method on every thread, static fields, CPU registers and handles held by the runtime. From the roots, the collector follows every reference to every object, and from those to further objects, marking each one it reaches. Anything it reaches is **reachable**, and therefore live. Anything unmarked is garbage.

This is the **mark** phase. Then the collector reclaims the unmarked memory, called the **sweep**. The .NET collector usually also **compacts**: it slides live objects together to remove the gaps, and updates every reference to point at the new locations. Compaction keeps free memory in one block, so the next allocation is just a pointer move.

Cycles are no problem for a tracing collector. Objects that only refer to each other are simply not reached from any root.

### Generations

Marking every object on a large heap takes time. Real programs have a useful pattern: most objects die very young. A string built for one log message, a temporary list inside a method, the objects for one HTTP request. Objects that survive a while tend to live a long time. This observation is called the **generational hypothesis**.

The .NET heap is split into **generations** to exploit it. New objects go into **generation 0**. A gen 0 collection examines only those new objects, which is fast, and usually finds most of them dead. Survivors are **promoted** to generation 1, and survivors of gen 1 collections move to generation 2. Gen 0 collections happen often, gen 1 less often, and full gen 2 collections rarely.

To make this work, the collector must know about references from old objects to young ones, because those are roots for a young collection. The runtime records every write of a reference into an older object, using a small piece of code called a **write barrier**. This is one reason reference writes cost a little more than integer writes.

Two special areas sit beside the generations. The **large object heap**, described in Memory (1.5), holds objects of 85,000 bytes or more and is collected with gen 2. The **pinned object heap**, added in .NET 5, holds objects that must never move.

```csharp
// generations.cs — run with: dotnet run generations.cs
var obj = new object();
Console.WriteLine(GC.GetGeneration(obj));   // 0: brand new
GC.Collect();
Console.WriteLine(GC.GetGeneration(obj));   // 1: survived one collection
GC.Collect();
Console.WriteLine(GC.GetGeneration(obj));   // 2: survived two

var big = new byte[100_000];
Console.WriteLine(GC.GetGeneration(big));   // 2: large objects are treated as gen 2 from the start

for (int i = 0; i < 1_000_000; i++) { var temp = new byte[64]; }   // lots of short-lived garbage
Console.WriteLine($"collections so far: gen0 {GC.CollectionCount(0)}, gen1 {GC.CollectionCount(1)}, gen2 {GC.CollectionCount(2)}");

var info = GC.GetGCMemoryInfo();
Console.WriteLine($"heap {info.HeapSizeBytes / 1024} KiB, time paused so far {info.PauseTimePercentage}%");
Console.WriteLine($"server GC: {System.Runtime.GCSettings.IsServerGC}");
GC.KeepAlive(obj);
```

### Allocation

Allocating in .NET is very cheap. Each thread gets a small block of gen 0 memory of its own, its **allocation context**. Allocating an object moves a pointer forward by the object's size and clears the memory. Only when the block runs out does the thread ask for more, and only when gen 0 as a whole is full does a collection start.

So the cost of an object isn't in creating it. It is in the collections that the garbage triggers, and in anything that survives and has to be copied and promoted.

### Pauses

To mark and move objects safely, the collector must stop the program's threads, at least for part of the work. This is a **stop-the-world pause**. Gen 0 and gen 1 collections are usually short, well under a millisecond to a few milliseconds.

Full gen 2 collections could take much longer on a big heap. .NET avoids most of that with **background GC**: gen 2 marking runs on a separate thread while the program keeps going, and only short pauses are needed at the start and end. Young collections can still happen during a background gen 2 collection.

For most services, GC pauses are a small part of latency. They matter for trading systems, games and real-time audio, where a pause of a few milliseconds is visible.

### Choosing a mode

.NET offers two main modes.

**Workstation GC** uses one heap and aims to keep pauses short and memory low. Desktop apps and console tools use it by default.

**Server GC** creates one heap and one collector thread per core. It collects in parallel and uses more memory in exchange for higher throughput. ASP.NET Core apps use it by default. Since .NET 9, server GC also adapts its heap count to the app's actual load by default, a feature called DATAS, which keeps memory use closer to what a small service really needs.

You can switch with `<ServerGarbageCollection>true</ServerGarbageCollection>` in the project file or the `DOTNET_gcServer` environment variable. In containers, the collector reads the memory limit and sizes itself to fit. `DOTNET_GCHeapHardLimit` sets an explicit cap.

### Finalizers

A class can declare a **finalizer**, written `~ClassName()`, which runs before the object's memory is reclaimed. It exists to release native resources if someone forgot to call `Dispose`.

Finalizers are expensive and unpredictable. An object with a finalizer can't be freed in the collection that finds it dead. It is put on a queue, its finalizer runs later on a dedicated thread, and the memory is freed in a following collection. By then the object has been promoted, often to gen 2. Finalizers also run in no particular order, possibly long after the object died, possibly never if the process exits.

In modern .NET you almost never write one. Wrap native handles in `SafeHandle`, which has a carefully written finalizer, and implement `IDisposable` as shown in Scope and lifetime (2.6). Call `GC.SuppressFinalize(this)` in `Dispose` if your type does have a finalizer, so the extra cost is skipped once cleanup has happened.

### Pinning

Compaction moves objects. Native code, or hardware doing direct memory access, needs addresses that stay put. **Pinning** tells the collector not to move an object for a while. The `fixed` statement pins for the length of a block. `GCHandle.Alloc` with `Pinned` pins until you release it.

Pinned objects block compaction around them and leave gaps. Pin briefly, or allocate long-lived buffers on the pinned object heap with `GC.AllocateArray<T>(length, pinned: true)`.

### Weak references

A **weak reference** refers to an object without keeping it alive. If the only remaining references are weak, the collector frees the object, and the weak reference then gives back null. .NET has `WeakReference<T>`. `ConditionalWeakTable` attaches extra data to objects without keeping them alive.

Weak references are sometimes suggested for caches. They make poor caches, because a collection can empty the cache at any moment, often right when it is most needed. Use a cache with an explicit size limit and expiry, such as `MemoryCache`, as covered in Caching (8.6).

### Leaks with a garbage collector

A **memory leak** in a collected language is memory that stays reachable although the program will never use it again. The collector is working correctly; the program still holds the reference.

```csharp
// leak.cs — run with: dotnet run leak.cs
var feed = new FlightFeed();

for (int i = 0; i < 10_000; i++)
{
    var screen = new DepartureScreen();          // meant to be temporary
    feed.Changed += screen.OnChanged;            // but the feed now holds a reference to it
}
GC.Collect();
Console.WriteLine($"after leak: {GC.GetTotalMemory(true) / 1024 / 1024} MiB in use, {feed.Subscribers} subscribers");

feed.Clear();                                    // drop the references
GC.Collect();
Console.WriteLine($"after fix:  {GC.GetTotalMemory(true) / 1024 / 1024} MiB in use, {feed.Subscribers} subscribers");

class FlightFeed
{
    public event Action? Changed;
    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    public void Clear() => Changed = null;
}

class DepartureScreen
{
    readonly byte[] pixels = new byte[4096];     // 4 KB each, so the leak is visible
    public void OnChanged() => pixels[0]++;
}
```

The common sources are always the same. Events whose handlers are never removed. Static collections that only grow. Caches with no limit. Closures stored in long-lived places. Timers that are never disposed.

### Diagnosing memory problems

.NET has good tools for this.

`dotnet-counters` shows live figures for a running process: heap size, collections per second, allocation rate and time spent in GC. `dotnet-gcdump` captures a snapshot of every object on the heap, which Visual Studio or PerfView can open to show what holds what. `dotnet-trace` records detailed events, including every collection and its pause. In code, `GC.GetGCMemoryInfo()` and the `System.Runtime` metrics give the same numbers.

A steadily rising gen 2 size, with a sawtooth that never comes back down, is the signature of a leak. Take two heap snapshots some minutes apart and compare which types grew.

### In short

The collector starts from roots, such as local variables and static fields, and frees every object it can't reach from them. Generations make most collections cheap, because most objects die young. Allocating is fast; the cost is in collections and in objects that survive them. Server GC suits web apps, and the collector respects container memory limits. Leaks happen when something still refers to objects you no longer need, such as unremoved event handlers and caches without limits.

### What goes wrong

**Event handler leaks.** Short-lived objects subscribe to a long-lived publisher and never unsubscribe. Unsubscribe in `Dispose`, or have the publisher hold weak references.

**Calling `GC.Collect()` in production code.** Forced collections promote live objects, cost full pauses and usually make things slower. Remove them unless a measurement proves they help.

**Mid-life crisis.** Objects that live just long enough to be promoted to gen 2, such as per-request caches kept for a few seconds, then die, make expensive full collections frequent. Either keep objects very short-lived or truly long-lived.

**Finalizers for cleanup.** Resources stay held until some unknown later time, and every finalizable object lives longer than it should. Use `IDisposable` and `SafeHandle`.

**Long pinning.** Buffers pinned for a long time fragment the heap. Use the pinned object heap for long-lived buffers.

**Container memory set too tight.** The collector sizes itself to the limit, but a limit barely above the working set means constant collections and eventually an out-of-memory kill. Leave headroom and watch the numbers.

### Cost model

Allocation is a few nanoseconds. A gen 0 collection typically costs well under a millisecond and is proportional to the number of live young objects, not the dead ones, so garbage is cheap and survivors are what cost. Full collections scale with the whole live heap and can take tens of milliseconds or more on large heaps, mostly in the background. Writing a reference into an object costs a little extra for the write barrier. Each finalizable object costs an extra collection's worth of lifetime. As a rough guide, a healthy service spends a few percent of its time in GC or less.

### In other languages (optional)

**JavaScript.** V8 uses a generational, mostly concurrent collector with young and old spaces, much like .NET. The same leak patterns apply: forgotten event listeners, growing maps and closures held by timers. `WeakMap` and `WeakRef` provide weak references. Browser developer tools take heap snapshots.

**Python.** CPython uses reference counting, so most objects are freed immediately. A separate cycle collector runs from time to time to find reference cycles. `gc.collect()` runs it by hand.

**Java.** Offers several collectors. G1 is the default. ZGC and Shenandoah keep pauses under a millisecond even on very large heaps, at some cost in throughput.

**Go.** Uses a concurrent collector that does not move objects and is tuned for short pauses. The `GOGC` setting trades memory for less collection work.

**Rust and C++.** No collector. Rust frees memory when its single owner goes out of scope, checked at compile time. C++ uses destructors, smart pointers and manual management.

### Key terms

- **Garbage collection**: Automatically finding and freeing memory the program can no longer use.
- **Reference counting**: Keeping a count of references to each object and freeing it when the count reaches zero.
- **Reference cycle**: Objects that refer to each other, keeping each other's reference counts above zero.
- **Tracing garbage collection**: Finding live objects by following references from the roots, and freeing the rest.
- **GC root**: A reference the program can use directly, such as a local variable in a running method or a static field.
- **Reachable**: Able to be found by following references from a root. Reachable objects are kept.
- **Mark**: The phase of collection that finds and marks every reachable object.
- **Compaction**: Sliding live objects together to remove gaps, and updating references to match.
- **Generational hypothesis**: The observation that most objects die young, which generational collectors exploit.
- **Generation**: A group of objects of similar age. .NET uses generations 0, 1 and 2.
- **Write barrier**: A small piece of code run on reference writes so the collector can track old-to-young references.
- **Allocation context**: A thread's private block of fresh memory, from which it allocates by moving a pointer.
- **Stop-the-world pause**: A moment when the collector halts all program threads to do its work safely.
- **Background GC**: Doing most of a full collection on a separate thread while the program keeps running.
- **Server GC**: The .NET collector mode with one heap per core, tuned for throughput. The default for ASP.NET Core.
- **Finalizer**: A method that runs before an object's memory is reclaimed, as a safety net for native resources.
- **Pinning**: Stopping the collector from moving an object, so native code can use its address.
- **Weak reference**: A reference that doesn't keep its object alive.
- **Memory leak**: Memory that stays in use although the program will never need it again.

### Where this leads

What happens when things fail, including out-of-memory errors, is in Errors and exceptions (2.8). Choosing data structures that allocate less is in Arrays and lists (3.2). Caches with limits are in Caching (8.6). Watching GC metrics in production is in Observability (11.6). Finding and fixing slow code, including allocation-heavy code, is in Scaling and performance (12.4).

### Check yourself

1. Run `leak.cs`, then change `DepartureScreen` to implement `IDisposable` and unsubscribe itself. Dispose each screen in the loop and compare the memory figures.
2. Run `generations.cs` with `DOTNET_gcServer=1` set in the environment. Compare the collection counts and heap size with the default.
3. Install `dotnet-counters` with `dotnet tool install -g dotnet-counters` and watch the GC figures of a running ASP.NET Core app while you send it requests.
4. Explain why a gen 0 collection of a heap full of garbage can be faster than one where most objects survive.
5. Write a small cache using `WeakReference<T>`. Fill it, allocate a lot of garbage, and count how many entries survive.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Event handlers that kept every screen alive

A long-running worker subscribes each job object to a static `PriceChanged` event and never unsubscribes. Memory climbs by every job ever created.

*Unsubscribe with the job's lifetime*

```csharp
public sealed class Job : IDisposable
{
    public Job() => PriceFeed.PriceChanged += OnPrice;
    void OnPrice(object? s, PriceEventArgs e) { /* ... */ }
    public void Dispose() => PriceFeed.PriceChanged -= OnPrice;   // the static event no longer roots the job
}
```

> **Lesson:** An event publisher keeps every subscriber reachable. Unsubscribe when the subscriber's work ends.

#### Example 2: p99 spikes from gen 2 collections

An API's p50 is 20 ms but p99 jumps to 600 ms every few minutes. `dotnet-counters` shows gen 2 GCs at the same moments.

*Find and remove mid-life allocations*

```bash
dotnet-counters monitor -p <pid> --counters System.Runtime[gen-2-gc-count,gc-heap-size,time-in-gc,loh-size]
# loh-size climbing: large byte[] buffers allocated per request
```

*Rent large buffers instead*

```csharp
var buffer = ArrayPool<byte>.Shared.Rent(256 * 1024);
try { /* read file chunk into buffer */ }
finally { ArrayPool<byte>.Shared.Return(buffer); }
```

> **Lesson:** GC cost scales with surviving and large objects. Pool large buffers and keep per-request objects short-lived.

### CLR: GC, Records, Boxing

<p class="src">From <em>.NET Backend Deep Dive</em> · CLR: GC, Records, Boxing</p>

<a id="dn-25"></a>

#### Garbage Collector

##### The generational hypothesis
"Most objects die young." The GC splits the heap into:

- **Gen 0**: newly allocated objects. Collected often, very fast (<1ms).
- **Gen 1**: survivors of gen 0. A buffer zone.
- **Gen 2**: long-lived objects. Collected rarely but **expensively** (potentially hundreds of milliseconds).
- **LOH (Large Object Heap)**: objects ≥ **85,000 bytes** (big arrays, big strings). Collected with gen 2 and **not compacted by default** → fragmentation.
- **POH**: pinned object heap (.NET 5+).

##### Workstation vs Server GC

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>   <!-- default true for ASP.NET Core -->
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

- **Server GC**: one heap and one GC thread per core → high throughput, more memory. For servers.
- **Workstation GC**: single heap, lower latency, less memory. For desktops and small containers.
- In containers, set a memory limit; .NET reads the cgroup limit to size the heap.

##### "More GC saves memory but costs performance" — exactly right, and that's the whole trade
The GC trades **memory against CPU and latency**:

- Collect more often → lower RAM, but more CPU and more **pauses** (a blocking gen 2 collection freezes all threads → bad p99).
- Collect less often → faster, but higher RAM and OOM risk in containers.

**The real problem is almost never "the GC is slow" — it's that your code allocates too much.** Ways to allocate less:

```csharp
// 1) Span/Memory - slice strings without allocating
ReadOnlySpan<char> span = input.AsSpan(0, 10);   // instead of input.Substring(0, 10)

// 2) ArrayPool - reuse large buffers
var buffer = ArrayPool<byte>.Shared.Rent(8192);
try { /* use buffer */ }
finally { ArrayPool<byte>.Shared.Return(buffer); }

// 3) StringBuilder instead of += in a loop
//    s += x inside a 1000-iteration loop = 1000 garbage strings

// 4) structs for small, immutable, short-lived values (<= ~16 bytes)
readonly record struct Point(double X, double Y);

// 5) static lambdas -> no captured closure (every closure is a heap object)
items.Where(static x => x.IsActive);

// 6) Avoid LINQ on hot paths (each Where/Select allocates an iterator)

// 7) Intern or cache frequently repeated strings
```

**Measure before optimizing:**

```bash
dotnet-counters monitor --process-id 1234 System.Runtime
# Watch: gen-0/1/2-gc-count, gc-heap-size, alloc-rate, time-in-gc, gen-2-size
```

`% time in GC` above 10% means you have a problem. Use BenchmarkDotNet with `[MemoryDiagnoser]` to measure per-method allocations.

### The Memory Model in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · The Memory Model in Depth</p>

<a id="dn-91"></a>

#### GC phases, end to end

A collection is triggered when the **gen 0 allocation budget** is exhausted (dynamically tuned, hundreds of KB to a few MB), on `GC.Collect()`, or on an OS low-memory notification. Which generation gets collected is decided at that moment.

| Phase | What happens |
|---|---|
| **1. Suspension** | All managed threads must reach a **safe point** — somewhere the runtime has accurate information about which registers and stack slots hold references. Threads in JIT'd code are hijacked (return address patched) or wait at a poll; threads inside native calls are left alone since they can't touch managed references. **This is the "GC pause."** |
| **2. Mark** | Trace the object graph from the roots, setting a mark bit on everything reachable. Unmarked = garbage. The GC never touches dead objects. |
| **3. Plan** | Simulate the result and decide, per generation: compact or sweep? |
| **4. Relocate** | Rewrite every reference — stack slots, registers, object fields — to the object's future address. Done *before* moving, so nothing dangles. |
| **5. Compact** | `memcpy` survivors down to close gaps. Afterwards allocation is a pointer bump. |
| **6. Sweep** *(instead of 4+5)* | Don't move anything; thread the gaps into a free list. Cheaper, leaves fragmentation. |
| **7. Resume** | Threads restart; survivors are now one generation older. |

**The critical consequence: cost scales with survivors, not with garbage.** A gen 0 full of dead objects is nearly free to collect. Gen 2 is expensive precisely because almost everything in it is alive.

##### The roots
- References on thread stacks and in CPU registers
- `static` fields
- GC handles (`GCHandle`, pinned objects, interop references)
- The **finalization queue** — an object awaiting finalization is a root, which is exactly why it survives
- The **card table**, for any collection below gen 2

That last one makes generational GC possible at all. If gen 0 is collected alone, how does the GC know a gen 2 object holds the only reference to a gen 0 object? A **write barrier** — a few instructions the JIT emits on every reference-field assignment — marks a "card" (~256 bytes) as dirty. The gen 0 mark phase then scans only dirty cards instead of all of gen 2.

<a id="dn-92"></a>

#### The three generations

| | **Gen 0** | **Gen 1** | **Gen 2** (+ LOH) |
|---|---|---|---|
| Contains | Brand-new objects | Gen 0 survivors | Gen 1 survivors, statics, caches; **LOH: ≥85KB, allocated straight here** |
| Frequency | Very often | Occasionally | Rarely |
| Cost | <1 ms | ~1–5 ms | 10–500 ms — **the p99 culprit** |
| Compacted | Yes | Yes | Blocking gen 2 yes; **background gen 2 only sweeps**; LOH never, by default |

**Gen 1's purpose** is the part people skip: it's a buffer, not a stage. Objects that barely survived a gen 0 collection — often only because they were mid-construction or momentarily referenced — get a second chance to die cheaply before entering the expensive generation.

**Objects age by surviving collections, not by elapsed time.** So anything holding a reference across a GC (a cache, a static list, a captive `DbContext`, a finalizer) actively pushes objects toward gen 2. Avoiding *promotion* matters more than avoiding allocation.

##### Background GC
A blocking gen 2 over a multi-GB heap can pause for hundreds of milliseconds. Background GC splits it:

```
[short pause] -> concurrent mark (app still running) -> [short pause] -> sweep -> resume
```

Gen 0/1 collections can even run during the concurrent mark. The trade: background gen 2 only **sweeps**, so fragmentation accumulates until a blocking collection happens.

##### Server GC
One heap and one dedicated GC thread **per logical core**, with parallel marking and work stealing. Higher throughput, but total pause is bounded by the slowest thread and memory usage is much higher — which is exactly why Server GC inside a small container hits the cgroup limit and gets OOMKilled far earlier than people expect.

##### Finalization costs an extra round trip

```
GC #1: unreachable, but has a finalizer
       -> moved to the freachable queue (which is a ROOT) -> SURVIVES, gets promoted
       -> the finalizer thread runs ~Finalize() later
GC #2: now genuinely unreachable -> collected
```

So a finalizable object is **guaranteed at least one promotion**, and if it dies in gen 1 it lands in gen 2. `GC.SuppressFinalize(this)` inside `Dispose()` removes it from the queue — that single line is the entire reason it appears in the dispose pattern.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a26"></a>

#### Garbage collection and deterministic cleanup

The GC is generational: new objects go to gen 0, survivors are promoted to gen 1 then gen 2. Gen 0 collections are cheap and frequent; gen 2 collections walk the whole heap and are the ones that show up as latency spikes. Objects over 85 KB go straight to the Large Object Heap, which is not compacted by default and fragments.

| Mode                    | Behaviour                                         | Use for                        |
|-------------------------|---------------------------------------------------|--------------------------------|
| Workstation             | Single heap, tuned for responsiveness             | Desktop apps, small containers |
| Server                  | One heap and thread per core, higher throughput   | Servers with ≥2 cores          |
| Concurrent / background | Gen 2 collected mostly in parallel with your code | Default; keeps pauses short    |

The GC handles *memory*. It does not handle file handles, sockets, or database connections — that is what `IDisposable` is for. Finalizers are a last-resort safety net that costs an extra GC cycle; almost never write one.

``` csharp
await using var conn = new SqlConnection(cs);      // IAsyncDisposable
using var stream = File.OpenRead(path);            // IDisposable

// classes owning disposables should be disposable themselves
public sealed class Client : IAsyncDisposable
{
    private readonly HttpClient _http;
    public ValueTask DisposeAsync() { _http.Dispose(); return ValueTask.CompletedTask; }
}
```

> **\[Trap\]**
>
> **Common trap.** Leaks in .NET are almost always *rooted references*: static collections that only grow, event handlers never unsubscribed, or captured closures held by a long-lived timer.

###### How it actually works

A collection is not a scan of the heap for garbage. It is the opposite: the GC finds everything *reachable* and treats the rest as free space, which is why collection cost scales with surviving objects rather than dead ones.

``` text
  1  SUSPEND   all managed threads are stopped at safe points
  2  MARK      walk from the roots, marking everything reachable
                 roots = thread stacks, CPU registers, statics,
                         GC handles, finalization queue, loader heap
  3  PLAN      decide whether to sweep (leave gaps) or compact (move objects)
  4  RELOCATE  if compacting: move survivors together, fix up every reference
  5  RESUME

  cost is driven by SURVIVORS, not by garbage.
  a gen 0 where 99% is dead is nearly free.
  a gen 2 where a large cache survives is expensive, every time.
```

This inverts the usual intuition. Allocating a million short-lived objects that all die is cheap — the gen 0 budget is a few megabytes and collecting it costs microseconds. Holding a million objects alive in a cache is what costs you: they get promoted to gen 2, and every gen 2 collection has to mark all of them again.

###### Reading the numbers

``` text
dotnet-counters monitor -n MyApi --counters System.Runtime
```

| Counter            | Healthy                            | Worrying                                                         |
|--------------------|------------------------------------|------------------------------------------------------------------|
| `gen-0-gc-count`   | Frequent — tens per second is fine | Only if allocation rate is a problem elsewhere                   |
| `gen-2-gc-count`   | A few per minute at most           | Several per second → something large is surviving                |
| `time-in-gc`       | Under ~5%                          | Over 20% → the app is mostly collecting, not working             |
| `loh-size`         | Stable                             | Growing steadily → large arrays or strings are being retained    |
| `alloc-rate`       | Whatever your workload needs       | Compare before/after a change — it is the best regression signal |
| `gc-fragmentation` | Low                                | High with a stable heap size → LOH fragmentation                 |

The Large Object Heap threshold is 85,000 bytes, which is smaller than people assume: a `byte[]` of 85 KB, a `string` of about 42,500 characters, or a `List<T>` that grew past that during doubling. The LOH is collected only with gen 2 and is not compacted by default, so a pattern of repeatedly allocating differently-sized large buffers fragments it until allocations fail despite plenty of free memory.

``` csharp
// force one compaction after a known large-allocation phase (rare, deliberate)
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

// far better: do not create the problem
using var ms = _recyclableStreamManager.GetStream();   // pooled, LOH-free buffers
var rented = ArrayPool<byte>.Shared.Rent(100_000);     // pooled, reused
```

###### What this looks like when it breaks

A realistic leak investigation, in the order you would actually do it.

1.  **Confirm it is managed memory.** Compare working set with the GC heap size counter. If the process is growing but the GC heap is flat, you are looking at native memory — unmanaged handles, a native library, or fragmentation.
2.  **Capture two heap snapshots** ten minutes apart under steady load: `dotnet-gcdump collect -p <pid>`.
3.  **Diff them** and sort by instance count growth. The type at the top is rarely the culprit — it is usually `string` or `byte[]`. Look for the first *domain* type in the list.
4.  **Find the retention path** from a GC root to that object. This is the actual answer: it names the static field, event, cache or timer that is holding it.
5.  **Confirm the fix** by repeating the two-snapshot test. Do not trust that it looks better.

| Retention path ends at                | Almost certainly                                          |
|---------------------------------------|-----------------------------------------------------------|
| A static `Dictionary` or `List`       | A cache with no eviction policy                           |
| `EventHandler` on a long-lived object | A subscriber that never unsubscribed                      |
| `Timer` or `Task` continuation        | A captured closure holding a whole service graph          |
| `DbContext` change tracker            | A context living far longer than one unit of work         |
| `HttpContext` or `ClaimsPrincipal`    | Request state captured by fire-and-forget background work |

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Ground Floor</p>

<a id="be-b239"></a>

#### GC in one page

.NET uses a generational, mark-and-compact collector.

- **Gen 0** — brand-new small objects. Collected very often, very cheaply. Most objects die here; that is the whole design bet.
- **Gen 1** — survived one collection. A buffer between short-lived and long-lived.
- **Gen 2** — survived twice. Collected rarely and expensively; a full gen 2 collection walks the whole heap.
- **LOH** (Large Object Heap) — objects **≥ 85,000 bytes**. Allocated directly here, collected with gen 2, and *not compacted by default*, so it fragments.
- **POH** (Pinned Object Heap, .NET 5+) — for deliberately pinned buffers, keeping them out of the main heap.

Two modes matter operationally: **Workstation** (lower latency, one heap) and **Server** (one heap and one GC thread per core, much higher throughput, more memory). ASP.NET Core defaults to Server GC when more than one core is present. Both support background gen 2 collection.

> **\[Why it matters\]**
>
> **What this means for your code.** The cost model is: *allocation is cheap, survival is expensive*. A million objects that die in gen 0 cost almost nothing. Ten thousand objects that get promoted to gen 2 because they sit in a cache, a static, or a long-running request cost a lot. Optimising for allocation count is usually the wrong target; optimising for *promotion* is the right one.

``` csharp
// A 100,000-element array of any reference type is 800,000 bytes on x64
// - already on the LOH, before you allocate a single element.
var buffer = new string[100_000];
```

Runtime context: [The JIT: tiers, OSR, and dynamic PGO](02-02-compilers-interpreters-and-jit.md#be-m40)

First principles: [Processes and virtual memory](01-07-the-operating-system.md#be-m6)

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · async / await · Ground Floor</p>

<a id="be-c307"></a>

#### Garbage collection, briefly

Heap memory is reclaimed automatically. The GC periodically determines which objects are still reachable from **roots** — stack locals, registers, static fields, GC handles — and frees everything else.

It's **generational**, on the observation that most objects die young:

- **Gen 0** — new allocations. Collected often, very cheaply.
- **Gen 1** — survived one collection. A buffer zone.
- **Gen 2** — survived several. Collected rarely and expensively.
- **LOH** — objects ≥ 85,000 bytes, allocated separately, collected with gen 2.

Three implications for async work:

- Allocation itself is nearly free — a pointer bump in the gen-0 nursery. The cost is collection, and gen-0 collection of dead objects is close to free too. So the per-await allocation is cheaper than it sounds.
- But a state machine that **survives** — because it's parked for 50 ms across many gen-0 collections — gets promoted to gen 1, then gen 2. Long-lived awaits mean gen-2 pressure, and gen-2 collections are the expensive kind.
- Anything a state machine field points at stays reachable for the whole await. Capture a 10 MB buffer as a local before an await and you've pinned 10 MB in memory for the duration, plus everything it references. This is the single most common cause of "why is my server holding so much memory" in async services.

### Foundations: how .NET stores data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Foundations: how .NET stores data</p>

<a id="be-m24"></a>

#### The heap, and why it needs a collector

The heap holds data whose lifetime does not nest inside a call. Allocation is nearly as cheap as the stack — the runtime keeps an *allocation pointer* at the end of the used region and bumps it forward:

``` csharp
var order = new Order();

//  gen 0 before:   [ obj ][ obj ][ obj ]│               free
//                                       ^ allocation pointer
//  gen 0 after:    [ obj ][ obj ][ obj ][ Order ]│      free
//                                                ^ moved forward by the size
//
// That is it: a bounds check, a pointer add, and zeroing the new bytes.
// Allocation is cheap. It is RECLAMATION that costs.
```

Reclamation is the hard part, because nothing in the program says when an object is finished. There are three classical answers, and .NET picks the third:

| Strategy                 | How                                       | Failure mode                                      |
|--------------------------|-------------------------------------------|---------------------------------------------------|
| Manual (`free`/`delete`) | the programmer says when                  | leaks, double-free, use-after-free                |
| Reference counting       | count inbound references, free at zero    | cycles leak; every copy costs an atomic increment |
| **Tracing GC**           | periodically find what is still reachable | pauses; non-deterministic timing                  |

**"Reachable" is the key idea, and this is where the stack comes back in.** The collector starts from a set of *roots* — things that are definitely live because the program can still reach them directly:

- every local and every register on every thread's stack;
- every `static` field;
- GC handles (pinned buffers, interop references);
- objects awaiting finalization.

From those roots it follows every reference, marking what it finds. Anything unmarked is unreachable by definition — no code path can ever touch it again — so its memory can be reused, without the program ever having said so.

``` text
  STACK (roots)                    HEAP
 ┌─────────────┐
 │ order  ─────┼──────────────►  ┌─ Order ──────────┐
 │ temp   ─────┼───────┐         │  Lines  ─────────┼──►  ┌─ List ────┐
 │ n = 42      │       │         │  Customer ───────┼─┐   │  [0] ─────┼──► Line
 └─────────────┘       │         └──────────────────┘ │   │  [1] ─────┼──► Line
                       │                              │   └───────────┘
                       └──────►  ┌─ StringBuilder ─┐  └─► ┌─ Customer ─┐
                                 └─────────────────┘      └────────────┘

                                 ┌─ Order (old) ────┐   ← nothing points here.
                                 └──────────────────┘     Unreachable = garbage.
```

Note what this means: **an object is garbage the moment nothing references it, but it is not *collected* until a collection runs.** Those are different times, and the gap is why memory usage is a sawtooth rather than a smooth curve, and why "the object should have been freed by now" is not a meaningful statement in .NET.

> **\[Why it matters\]**
>
> **The practical consequence, stated once.** Because the collector's work is proportional to what *survives* rather than to what was allocated, the cost model is inverted from what most people assume. A million objects that die immediately are nearly free — the collector never even looks at them. Ten thousand objects held alive by a static cache, a long-lived context, or an event subscription are expensive, permanently. [The GC in one page](#be-b239) develops this into the generational design that exploits it.

> **\[Trap\]**
>
> **`IDisposable` is unrelated to memory.** The GC handles memory; `Dispose` handles everything else — file handles, sockets, database connections, OS locks. Those are scarce, unmanaged resources the collector knows nothing about and cannot reclaim in time. Forgetting `using` on a `DbConnection` does not leak memory; it exhausts the connection pool ([Connection pooling and exhaustion](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462)).

### Runtime: memory, types and the GC

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Runtime: memory, types and the GC</p>

<p class="pd">Where allocations come from, what the collector actually does, and the type-level choices that decide how much work it has to do.</p>

<a id="be-q16"></a>

#### The GC: generations, and what “a lot of GC” costs you

``` text
the managed heap

 gen 0 ────► gen 1 ────► gen 2                    Large Object Heap (LOH)
 new objects  survivors   long-lived              anything ≥ 85,000 bytes
 collected    collected   collected rarely        collected with gen 2
 constantly   sometimes   (expensive)             not compacted by default

the generational bet: most objects die young.
gen 0 collection is cheap because almost everything in it is already garbage
— the collector copies out the few survivors and resets the rest wholesale.

promotion is the enemy. An object that survives to gen 2 is one you now
pay to trace on every full collection, for the life of the process.
```

``` text
// measure before theorising
dotnet-counters monitor -p PID --counters System.Runtime
//  gen-0-gc-count, gen-1, gen-2       ← gen 2 climbing = a real problem
//  alloc-rate                         ← bytes/sec you are asking the GC to handle
//  gc-heap-size, time-in-gc           ← >10% time-in-gc is a red flag

// the two GC modes, and why the choice matters in containers
// Workstation:  lower latency, single heap  — small pods, ≤2 cores
// Server:       one heap per core, higher throughput, more memory
// <ServerGarbageCollection>true</ServerGarbageCollection>
// DOTNET_GCHeapHardLimitPercent=75   ← leave room for non-GC memory
```

The cost of a collection is *not* proportional to garbage; it is proportional to **survivors**, because the collector traces live references. So the way to make GC cheap is not to allocate less at all costs — it is to make sure what you allocate dies quickly.

| Pattern                                         | Effect                                                |
|-------------------------------------------------|-------------------------------------------------------|
| Short-lived per-request objects                 | Ideal — dies in gen 0, nearly free                    |
| A static cache holding millions of objects      | Traced on every gen-2 collection, forever             |
| Large arrays (≥85 KB)                           | LOH: not compacted → fragmentation, gen-2 collections |
| Long-lived objects referencing short-lived ones | Cross-generation writes, more work per collection     |

> **\[Trap\]**
>
> **Common trap.** “Reduce allocations” is not automatically a win. Object pooling for short-lived objects often makes things *worse*: pooled objects survive, get promoted, and become gen-2 work that gen-0 would have handled for free. Pool large buffers (`ArrayPool`), not small DTOs.

<a id="be-q17"></a>

#### record versus class versus struct

|                | class              | record (class)                   | struct                      | record struct              |
|----------------|--------------------|----------------------------------|-----------------------------|----------------------------|
| Lives on       | Heap               | Heap                             | Stack / inline in its owner | Stack / inline             |
| Equality       | Reference          | **Value** (all members)          | Value (slow default)        | Value (generated, fast)    |
| Copy semantics | Reference copy     | Reference copy + `with`          | Full copy every assignment  | Full copy                  |
| Mutability     | Mutable by default | Immutable by convention (`init`) | Should be readonly          | Should be readonly         |
| Use for        | Entities, services | DTOs, messages, value objects    | Small values (≤16 bytes)    | Small values with equality |

``` csharp
// record: value equality, with-expressions and a ToString for free
public record OrderDto(Guid Id, DateTime PlacedAt, decimal Total);

var a = new OrderDto(id, when, 99m);
var b = new OrderDto(id, when, 99m);
a == b;                       // true — a class would say false
var c = a with { Total = 79m };          // non-destructive copy

// readonly record struct: no heap allocation at all
public readonly record struct Money(decimal Amount, string Currency);
public readonly record struct OrderId(Guid Value);   // strongly-typed id, free

// why "readonly" on a struct matters:
public struct Point { public int X; public void Bump() => X++; }
readonly Point _p;             // calling _p.Bump() silently copies FIRST,
                               // mutates the copy, and discards it.
// readonly struct makes that impossible to write by accident.
```

> **\[Trap\]**
>
> **Common trap.** A struct is copied on every assignment, every argument pass and every collection insertion. Above roughly 16–24 bytes that copying costs more than the allocation you avoided. And a `record` with a collection property has *broken* value equality: `List<T>` compares by reference, so two records with identical contents are unequal. Use an immutable collection or override the comparison.

<a id="be-q18"></a>

#### Boxing and unboxing: the invisible allocation

Boxing is wrapping a value type in a heap object so it can be treated as `object`. Every box is an allocation and a copy; unboxing is a type check plus a copy back. It is invisible in the source, which is why it shows up as unexplained allocation rate in a profile.

``` csharp
int x = 42;
object boxed = x;              // BOX: allocation + copy onto the heap
int y = (int)boxed;            // UNBOX: type check + copy back

// where it hides in real code
ArrayList list = new(); list.Add(42);            // boxes every value
Console.WriteLine("Value: " + 42);               // boxes for ToString via object
IComparable c = 42;                              // boxes: interface on a struct
Dictionary<string, object> bag = new() { ["n"] = 42 };   // boxes
_logger.LogInformation("Count {C}", 42);         // boxes into object[] params

// and where it does NOT
List<int> nums = new(); nums.Add(42);            // generic: no boxing
Console.WriteLine($"Value: {42}");               // interpolation handler: no box
_logger.LogInformation("Count {C}", 42);         // ← LoggerMessage source-gen
                                                 //   removes even this one
// enum in a switch, generic constraint, Span<int> — all boxing-free
```

``` csharp
// the classic hot-path boxing bug: struct + non-generic interface
public struct Temperature : IComparable
{
    public int Degrees;
    public int CompareTo(object? obj) => Degrees.CompareTo(((Temperature)obj!).Degrees);
}                                     // ↑ every comparison boxes BOTH sides
// sorting 100k of these allocates ~200k boxes

public struct Temperature2 : IComparable<Temperature2>   // generic version
{
    public int Degrees;
    public int CompareTo(Temperature2 other) => Degrees.CompareTo(other.Degrees);
}                                     // zero boxing
```

> **\[Trap\]**
>
> **Common trap.** `Enum.HasFlag` boxed on older runtimes and still surprises people; `string.Format` and any `params object[]` API box every value argument. If a profile shows allocation you cannot account for, look for a `params object[]` in the hot path first.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a25"></a>

#### Memory: Span, Memory, and pooling

`Span<T>` is a stack-only view over contiguous memory — an array, a slice of a string, native memory, or a `stackalloc` buffer. Slicing a span allocates nothing, which turns parsing loops from allocation-heavy to allocation-free.

``` csharp
ReadOnlySpan<char> line = "2026-07-27,EUR,1250.00";

var d = line[..10];               // no substring allocation
var rest = line[11..];
var comma = rest.IndexOf(',');
var ccy = rest[..comma];

// rent instead of allocate for large short-lived buffers
var buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
try { /* use buf */ }
finally { ArrayPool<byte>.Shared.Return(buf); }
```

`Span<T>` cannot be a field of a class, cannot be captured in a lambda, and cannot cross an `await`. When you need those, use `Memory<T>` and call `.Span` at the point of use.

> **\[Why it matters\]** This is the toolkit behind the last decade of .NET performance wins. You rarely write it in business code, but recognising it explains why `System.Text.Json` and Kestrel are as fast as they are.

###### Worked example

Parsing a 200 MB CSV, three ways, to make the allocation difference concrete.

``` csharp
// A. the obvious version: ~5 allocations per line
foreach (var line in File.ReadAllLines(path))       // whole file in memory, one string per line
{
    var parts = line.Split(',');                     // string[] + one string per field
    Add(new Reading(Guid.Parse(parts[0]), DateTime.Parse(parts[1]), double.Parse(parts[2])));
}
// 2 million lines -> ~10 million allocations, several GB of churn, constant gen 0 collections

// B. streaming, still allocating per field
foreach (var line in File.ReadLines(path)) { ... }   // file no longer fully in memory
                                                     // still ~4 allocations per line

// C. span-based: zero allocations in the parse loop
foreach (var line in File.ReadLines(path))
{
    ReadOnlySpan<char> s = line;
    var c1 = s.IndexOf(',');
    var c2 = s[(c1 + 1)..].IndexOf(',') + c1 + 1;

    var id    = Guid.Parse(s[..c1]);                 // parses directly from the span
    var taken = DateTime.Parse(s[(c1 + 1)..c2], CultureInfo.InvariantCulture);
    var value = double.Parse(s[(c2 + 1)..], CultureInfo.InvariantCulture);
    Add(new Reading(id, taken, value));
}
// one string per line (unavoidable here), zero for the fields
```

In practice version C typically runs two to four times faster than A on a large file, almost entirely because of what it does not do. Every `Parse` overload in the BCL has a span overload for exactly this reason, and `Utf8Parser` goes one step further by parsing straight from bytes with no UTF-16 conversion at all.

###### The type family, and which one to use

| Type              | Can live                    | Use for                                           |
|-------------------|-----------------------------|---------------------------------------------------|
| `Span<T>`         | Stack only — a `ref struct` | Synchronous parsing and slicing inside one method |
| `ReadOnlySpan<T>` | Stack only                  | The same, when you will not write                 |
| `Memory<T>`       | Anywhere, including fields  | Buffers that cross `await` boundaries             |
| `ArraySegment<T>` | Anywhere                    | Legacy APIs that predate Memory                   |
| `IMemoryOwner<T>` | Anywhere                    | When ownership and disposal must be transferred   |

``` csharp
// what a ref struct cannot do
class Holder { Span<byte> _s; }                  // compile error: cannot be a field
async Task X(Span<byte> s) { await Y(); }        // compile error: cannot cross an await
var f = () => span.Length;                       // compile error: cannot be captured

// so async code takes Memory<T> and converts at the point of use
async Task ReadAsync(Memory<byte> buffer, CancellationToken ct)
{
    var n = await _stream.ReadAsync(buffer, ct);
    Parse(buffer.Span[..n]);                     // Span only inside the synchronous part
}

// stackalloc for genuinely small, short-lived buffers
Span<char> scratch = stackalloc char[64];        // NEVER inside a loop - the stack does not unwind
// for anything bigger or size-dependent, rent instead
var rented = ArrayPool<byte>.Shared.Rent(size);
```

> **\[Trap\]**
>
> **Common trap.** `ArrayPool.Rent(1000)` may hand you an array of 1024, and it will contain whatever the previous renter left in it. Always track your own length and never assume `array.Length` is what you asked for — writing a rented buffer to a response without slicing is a real data-leak vector.

### Factories, Lifetimes & Object Creation

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Factories, Lifetimes & Object Creation</p>

<p class="pd">Almost every awkward question in a .NET codebase — ‘how do I get a DbContext in a singleton?’, ‘why is my HttpClient exhausting sockets?’, ‘how do I pick an implementation at runtime?’ — has the same answer: a factory. Here is the whole family.</p>

<a id="be-a89"></a>

#### Pooling: ObjectPool, ArrayPool, MemoryPool

Pooling trades a small amount of complexity for a large reduction in GC pressure. It is worth it for objects that are expensive to build, large, or created at very high rates — and it is a pessimisation everywhere else.

``` csharp
builder.Services.AddSingleton<ObjectPool<StringBuilder>>(sp =>
    new DefaultObjectPoolProvider().CreateStringBuilderPool(
        initialCapacity: 256, maximumRetainedCapacity: 8 * 1024));

public string Render(IEnumerable<Row> rows)
{
    var sb = _pool.Get();
    try
    {
        foreach (var r in rows) sb.Append(r.Name).Append(';');
        return sb.ToString();
    }
    finally { _pool.Return(sb); }     // the policy clears it before reuse
}
```

| Pool                     | Pools                                 | Notes                                                         |
|--------------------------|---------------------------------------|---------------------------------------------------------------|
| `ArrayPool<T>.Shared`    | Arrays by size bucket                 | Rented arrays are *not* cleared; may be larger than requested |
| `MemoryPool<T>`          | `IMemoryOwner<T>` blocks              | For APIs that hand memory ownership around                    |
| `ObjectPool<T>`          | Arbitrary objects with a reset policy | StringBuilder, parsers, buffers-with-state                    |
| `RecyclableMemoryStream` | MemoryStream buffers                  | Avoids the Large Object Heap entirely                         |

> **\[Trap\]**
>
> **Common trap.** Forgetting to return, returning twice, or keeping a reference after returning are all real bugs and none of them throw immediately. Always `try/finally`, and never let a rented buffer escape the method that rented it.

###### When pooling helps, and the bugs it introduces

| Pool                     | For                            | Worth it when                                         |
|--------------------------|--------------------------------|-------------------------------------------------------|
| `ArrayPool<T>`           | Temporary arrays               | Buffers over ~1 KB, allocated frequently              |
| `MemoryPool<T>`          | `Memory<T>` blocks             | Async pipelines, where a `Span` cannot cross an await |
| `ObjectPool<T>`          | Expensive-to-construct objects | `StringBuilder`, parsers, serialiser state            |
| `RecyclableMemoryStream` | `MemoryStream`                 | Large or frequent streams — avoids the LOH            |
| Connection pools         | Connections                    | Always — and it is done for you                       |

``` text
  WHY it helps: the Large Object Heap

    allocations >= 85,000 bytes go on the LOH.
    the LOH is collected only with a GEN 2 collection, and is NOT
    COMPACTED by default -> fragmentation.

    so a service allocating 100 KB buffers per request causes frequent
    gen 2 collections - the expensive ones - and a heap that grows even
    though nothing is retained.

    pooling removes the allocation entirely.
    below ~1 KB it is usually not worth it: gen 0 collection is extremely
    cheap, and the pool has its own overhead.
```

``` csharp
// the discipline: rent, use, return - in a finally, always
var buffer = ArrayPool<byte>.Shared.Rent(size);   // may return a LARGER array
try
{
    var span = buffer.AsSpan(0, size);            // so ALWAYS slice to your size
    await stream.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
    Process(span);
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}

// BUG 1: using the array after returning it.
//   another thread now owns it. You read someone else's data, or corrupt
//   theirs. Non-deterministic and very hard to reproduce.
// BUG 2: forgetting to return.
//   not a leak - the GC still collects it. But the pool never gets it back,
//   so you allocate fresh every time and have added complexity for a slowdown.
// BUG 3: assuming the length. Rent(1000) may return 1024.
//   buffer.Length is NOT your size. Track the size separately.
// BUG 4: not clearing sensitive data - the next renter sees your bytes.
ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
```

> **\[Note\]**
>
> **Measure before pooling.** Pooling adds a class of bug that is non-deterministic, cross-request, and extremely hard to reproduce — in exchange for allocation savings that are frequently invisible next to a database call. Profile first with `dotnet-counters` or a memory profiler, and pool only where allocation genuinely shows up in gen 2 or LOH numbers. In an ordinary web application it usually does not.

### Arrays and Spans

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Arrays and Spans</p>

<a id="be-b272"></a>

#### Arrays

``` csharp
int[] a = new int[10];
int[,] grid = new int[3, 4];        // rectangular — one allocation, contiguous
int[][] jag = new int[3][];         // jagged — 4 allocations, rows scattered
```

**Rectangular arrays are contiguous** but their indexing is slower (multiply-add, plus bounds checks the JIT optimizes less aggressively). **Jagged arrays are faster to index** and allow ragged rows, at the cost of an extra indirection per row and worse locality.

For performance-critical grids, the fastest option is usually a **single flat array with manual indexing**:

``` csharp
var data = new int[rows * cols];
ref int cell = ref data[r * cols + c];
```

**Bounds checks.** Every array access in C# is checked: the runtime verifies `0 <= i < Length` and throws `IndexOutOfRangeException` otherwise. That's a comparison and a branch on every single element access — the price of memory safety.

The JIT removes it when it can *prove* the index is in range. The pattern it recognizes is exactly this:

``` csharp
for (int i = 0; i < arr.Length; i++)      // check ELIMINATED — the loop bound proves it
    sum += arr[i];

int n = arr.Length;                        // may PREVENT elimination: the JIT can no
for (int i = 0; i < n; i++)                // longer see that n is tied to arr.Length
    sum += arr[i];
```

The second version is the "optimization" people write from habit, and it can be slower. Compare against `.Length` directly and let the JIT do its job.

The same recognition drives auto-vectorization: a clean, provably-in-range loop can be turned into SIMD instructions processing several elements at once. An opaque loop bound blocks that too.

<a id="be-b273"></a>

#### Span and Memory

``` text
Span<T>            // ref struct: pointer + length. Stack only.
ReadOnlySpan<T>    // immutable view
Memory<T>          // heap-safe equivalent; .Span materialises a Span
ReadOnlyMemory<T>
```

A span can point into a managed array, a stack buffer, native memory, or a string — one type over all three, with no copy.

``` csharp
int[] arr = new int[100];
Span<int> all   = arr;
Span<int> slice = arr.AsSpan(10, 20);   // no allocation, no copy
slice[0] = 5;                            // writes arr[10]

Span<byte> stack  = stackalloc byte[128];
ReadOnlySpan<char> str = "hello".AsSpan(1, 3);

// Useful span APIs:
span.Slice(a, b); span.CopyTo(dst); span.Fill(0); span.Clear();
span.IndexOf(x); span.Contains(x); span.SequenceEqual(other);
span.Sort(); span.Reverse(); span.BinarySearch(x);
MemoryMarshal.Cast<byte, int>(bytes);   // reinterpret without copying
```

Use `Memory<T>` when the buffer must survive an `await` or live in a field; convert to `Span<T>` at the point of synchronous use.

> **\[Why it matters\]**
>
> **The real payoff** is eliminating intermediate copies in parsing and serialisation: reading a request body into a pooled buffer and slicing through it, rather than materialising a string, splitting it into an array, and allocating a substring per field. On a high-throughput endpoint this is the difference between constant gen-0 churn and near-zero allocation.

> **\[Tip\]**
>
> `ArraySegment<T>` is the old, weaker version of this and shows up in legacy APIs; `ArraySegment<T>` converts implicitly to `Span<T>`, so you can modernise incrementally at the boundaries.

Worked example: [Case: the parser that allocated four gigabytes](14-03-worked-cases-from-production.md#be-m301)

<a id="be-b274"></a>

#### stackalloc and ArrayPool

``` text
Span<byte> buf = stackalloc byte[256];        // no allocation, freed on return
```

Rules: keep it small (**under ~1 KB**; the stack is 1 MB total and overflow is an uncatchable process kill), and **never inside a loop** — stack memory isn't reclaimed until the method returns, so a loop accumulates.

For larger or variable-size buffers, pool instead:

``` csharp
var pool = ArrayPool<byte>.Shared;
byte[] buf = pool.Rent(size);                 // may be LARGER than requested
try { var span = buf.AsSpan(0, size); /* use span, not buf.Length */ }
finally { pool.Return(buf, clearArray: true); }
```

Three ways to get this wrong: using `buf.Length` instead of the size you asked for (the rented array is often bigger and full of someone else's data); forgetting to return in a `finally`; and using the array after returning it. `clearArray: true` when the buffer held anything sensitive.

------------------------------------------------------------------------

### Collections

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Collections</p>

<a id="be-m113"></a>

#### Capacity, pre-sizing, and ArrayPool

Every growable collection doubles and copies. If you know or can estimate the final size, say so — it is a one-word change with a real effect.

``` csharp
new List<T>(expectedCount);
new Dictionary<K,V>(expectedCount, StringComparer.Ordinal);
new StringBuilder(expectedLength);
list.EnsureCapacity(n);           // .NET 6+, grows once to at least n
dict.EnsureCapacity(n);
list.TrimExcess();                // after a big shrink, release the slack
```

`ToList()` and `ToArray()` already use the source's `Count` when the source implements `ICollection<T>`, so they pre-size for free. Over a `Where` or a `yield` iterator they cannot, so they grow.

**`ArrayPool<T>`** reuses large arrays instead of allocating and collecting them:

``` csharp
byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);   // may return a LARGER array
try {
    int read = stream.Read(buffer, 0, 4096);          // so always track your own length
    Process(buffer.AsSpan(0, read));
}
finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: containsSecrets); }
```

> **\[Trap\]**
>
> **Traps.** Rent returns an array of *at least* the requested size — never use `buffer.Length` as your data length. Always `Return` in a `finally`. Never keep a reference after returning it, and never return the same array twice; both cause silent, extremely hard-to-diagnose data corruption. If you cannot guarantee the lifetime, do not pool.
