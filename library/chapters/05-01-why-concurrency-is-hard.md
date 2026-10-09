---
id: "5-1"
number: "5.1"
title: "Why concurrency is hard"
part: 5
part_title: "Concurrency"
words: 5406
minutes: 24
---

# 5.1 Why concurrency is hard

> **Quick look.** Why sharing data between threads creates timing bugs that may appear once in a million runs.
>
> - Concurrency means many tasks in progress; parallelism means running at the same instant; asynchrony means not holding a thread while waiting.
> - Threads interleave unpredictably, so shared data needs protection.
> - count++ and check-then-act sequences race, losing updates or acting on stale checks.
> - Without synchronization a write may never become visible to another thread; large values can be read half-written.
> - Blocking a thread to wait wastes it; a thread pool of 32 threads can't wait on 1,000 requests.
> - The safest design shares no mutable data at all; passing tests don't prove thread safety.


### The big picture

Modern software does many things at once: a server handles thousands of requests, an app stays responsive while it saves, a batch job uses every core. The moment two pieces of work share data, a new kind of bug appears: one that depends on exact timing and may show up once in a million runs.

This chapter explains why. It shows how threads interleave, why a simple `count++` can lose updates, why one thread may never see another thread's change, and how values can be read half-written. A runnable file makes each problem happen in front of you. Every later chapter in this part builds on these ideas.

### Before you start

**Builds on:** 1.7 The operating system, 1.5 Memory, 1.4 The processor, 4.4 Immutability and pure functions.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Relationship** (7.5): How entities relate, expressed in a schema with foreign keys.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Event loop** (5.4): A loop that takes ready callbacks from a queue and runs them one at a time, waiting for events when idle.

**The code uses:** variables and var, string interpolation, classes and properties, lambdas, LINQ, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between concurrency and parallelism?**

> Concurrency is dealing with several things in progress at once, which may take turns on one core. Parallelism is actually doing several things at the same instant on several cores. A web server handling a thousand waiting requests on four threads is concurrent; a sum split across eight cores is parallel.

**2. Why can count++ from two threads lose updates?**

> It is three steps: read the value, add one, write it back. Both threads can read the same old value, each add one, and each write the same new value. One increment disappears.

**3. What is a check-then-act race? Give an example.**

> Code checks a condition and then acts on it, but the condition changes in between. Two threads both see one seat left, both book it, and the flight is overbooked.

**4. A loop waits for another thread to set a bool field to true, and in Release builds it never stops. Why?**

> Without any synchronization, the JIT may read the field once and keep the value in a register for the whole loop. The other thread's write is never seen. `Volatile.Read`, a lock or a proper signal fixes it.

**5. Can reading a 16-byte struct while another thread writes it give a mix of old and new values?**

> Yes. Only reads and writes up to the machine's word size, 8 bytes on a 64-bit machine, are guaranteed to happen in one piece. Larger values can be torn: half old, half new.

**6. Why are concurrency bugs so hard to reproduce?**

> They depend on the exact timing of threads, which changes with load, hardware, debugging and logging. A bug may appear once in a million runs and vanish as soon as you add a breakpoint or a log line.

### Doing several things at once

A server handles many requests at the same time. A desktop app keeps its window responsive while it saves a file. A data job uses every core to finish faster. All of these involve more than one thing in progress at once.

**Concurrency** is structuring a program so that several tasks can be in progress at the same time. They may take turns on one core, switching back and forth, or run on several. **Parallelism** is actually running several tasks at the same instant on different cores, as Parallelism (5.5) covers. Concurrency is about dealing with many things. Parallelism is about doing many things. A program can be concurrent without being parallel, and most server code is mainly concurrent: it spends its time waiting for databases and networks, not computing.

Both bring one new kind of problem. When two tasks share data, the order in which their steps happen is no longer fixed.

### Interleaving

Within one thread, statements run in the order you wrote them. Across threads, they can interleave in any order. The scheduler from The operating system (1.7) can pause a thread between any two instructions and run another one. On a multi-core machine, both threads really run at once.

So a program with two threads doesn't have one execution path. It has a huge number of possible interleavings, and it must be correct for all of them. Most interleavings are harmless. The bugs hide in the few that aren't, and those may happen once in a million runs.

### Race conditions

A **race condition** is a bug where the result depends on the timing of threads. Two kinds make up most real cases.

**Read-modify-write.** `count++` looks like one operation. It is three: read `count` into a register, add one, write it back. Two threads can both read 41, both compute 42 and both write 42. Two increments happened and the count went up by one. That is a **lost update**. The same happens with `total += amount`, with `list.Add` on a `List<T>`, and with any update built from a read followed by a write.

**Check-then-act.** Code checks a condition, then acts on the result. In between, another thread changes the condition. "If there is a seat left, book it" lets two threads both see one seat and both book it. "If the key isn't in the dictionary, add it" lets two threads both add. "If the file doesn't exist, create it" lets two processes both create it.

A **data race** is the lowest-level form: two threads access the same memory at the same time, at least one of them writes, and nothing orders the accesses. Data races can produce not just wrong answers but corrupted structures. A `Dictionary` changed by two threads at once can end up with broken internal chains and loop forever on the next lookup.

### Atomicity

An **atomic operation** happens all at once from the point of view of every other thread. No thread can see it half done.

On a 64-bit machine, reading or writing a properly aligned value of up to 8 bytes, such as an `int`, a `long`, a `double` or a reference, is atomic. Nothing else is guaranteed. A `decimal` is 16 bytes. A struct with two `long` fields is 16 bytes. Another thread can read one half before the write and the other half after it. That is a **torn read**.

And atomic reads and writes still don't make read-modify-write atomic. For that you need special operations, such as `Interlocked.Increment`, or a lock, both covered in Threads and locks (5.2).

### Critical sections

A **critical section** is a piece of code that reads and changes shared data and must not run in more than one thread at a time. "Check the seat count and decrement it" is a critical section. So is "find the key and add it if missing".

The basic cure for races is to identify each critical section and make sure only one thread can be inside it at a time, usually with a lock. The hard parts are finding every critical section, keeping them small, and not creating new problems like deadlocks.

### Visibility

There is a subtler problem than interleaving. Even when threads take turns perfectly, one thread may not see what another wrote.

Memory (1.5) and The processor (1.4) explained why. Each core has its own caches. Processors reorder memory operations. The JIT keeps values in registers instead of re-reading memory. Within one thread, all of this is invisible. Across threads, it means a write can take effect later than it appears to, or reads and writes can be seen in a different order than the code says.

The classic symptom is a loop that waits for a flag. One thread spins on `while (!stop) { }`. Another sets `stop = true`. In an optimized build, the JIT may read `stop` once, before the loop, and never again. The loop runs forever.

The fix is to tell the compiler and processor that order matters here. Locks do that. So do `Volatile.Read` and `Volatile.Write`, `Interlocked` operations, and higher-level tools such as `ManualResetEventSlim`, `Task` and `CancellationToken`. Each creates a **happens-before** relationship: everything one thread did before the synchronizing operation is guaranteed to be visible to another thread after it.

```csharp
// races.cs — run with: dotnet run -c Release races.cs
// The exact numbers change from run to run. That is the point.
using System.Linq;

// 1. Lost updates: four threads, a million increments each
int unsafeCount = 0, safeCount = 0;
var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
{
    for (int i = 0; i < 1_000_000; i++)
    {
        unsafeCount++;                             // read, add, write: three steps
        Interlocked.Increment(ref safeCount);      // one atomic step
    }
})).ToList();
threads.ForEach(t => t.Start());
threads.ForEach(t => t.Join());
Console.WriteLine($"plain ++: {unsafeCount:N0}");   // usually well under 4,000,000 with several cores; one core can hide the race
Console.WriteLine($"atomic:   {safeCount:N0}");     // always 4,000,000

// 2. Check-then-act: one seat, two passengers
int seatsLeft = 1, booked = 0;
void TryBook()
{
    if (seatsLeft > 0)                             // check
    {
        Thread.Sleep(10);                          // widen the gap between check and act
        seatsLeft--;                               // act
        Interlocked.Increment(ref booked);
    }
}
var a = new Thread(TryBook); var b = new Thread(TryBook);
a.Start(); b.Start(); a.Join(); b.Join();
Console.WriteLine($"booked {booked}, seats left {seatsLeft}");   // booked 2, seats left -1: overbooked

// 3. Visibility: a loop that may never see the flag change
var flag = new Flag();
var spinner = new Thread(() => { while (!flag.Stop) { } }) { IsBackground = true };
spinner.Start();
Thread.Sleep(100);
flag.Stop = true;
Console.WriteLine(spinner.Join(1000) ? "spinner saw the change" : "spinner never saw the change");
// In Release builds this often prints "never saw the change". Use Volatile.Read or a proper signal.

// 4. Torn reads: a 16-byte value is not written in one piece
var box = new PairBox();
long torn = 0;
var writer = new Thread(() => { for (long i = 0; ; i++) box.Value = (i, i); }) { IsBackground = true };
writer.Start();
var until = DateTime.UtcNow.AddMilliseconds(200);
while (DateTime.UtcNow < until)
{
    var (x, y) = box.Value;                        // may read one half old, one half new
    if (x != y) torn++;
}
Console.WriteLine($"torn reads seen: {torn:N0}");  // usually more than zero with several cores

sealed class Flag { public bool Stop; }
sealed class PairBox { public (long, long) Value; }
```

### Shared-nothing designs

The surest way to avoid these problems is not to share mutable data at all. **Shared-nothing** designs give each thread or task its own data, and have them communicate by passing messages or immutable values.

Several patterns follow from this. Give each thread its own local counter and add them up at the end. Pass work to other threads through a queue, handing over ownership of each item. Keep shared state immutable and replace it whole, as Immutability and pure functions (4.4) described. Keep request-handling code free of static mutable fields. Let one dedicated thread or actor own a piece of state, and have everyone else send it requests.

When data really must be shared and changed, use the tools in the next chapter, and keep the shared part as small as you can.

### Why these bugs are so hard

Concurrency bugs have unpleasant properties. They depend on timing, which changes with load, hardware and the number of cores. They often disappear under a debugger, or when you add a log line, because both change the timing. A bug that vanishes when you look for it is sometimes called a **Heisenbug**, after the physicist Heisenberg. Tests pass thousands of times and the bug appears in production on a busy day.

So you can't test your way to correct concurrent code. Reason about it instead. For every piece of shared mutable data, be able to say which lock or mechanism protects it. If you can't, it is probably a bug.

### In short

Concurrency means several tasks in progress at once; parallelism means actually running them at the same instant. Threads interleave unpredictably, so shared data needs protection. Read-modify-write and check-then-act sequences race, losing updates or acting on checks that are no longer true. A write may not be visible to other threads without synchronization, and values larger than 8 bytes can be read half-written. The safest design shares no changeable data at all.

### What goes wrong

**Non-thread-safe collections shared between threads.** `List<T>` and `Dictionary<TKey, TValue>` changed by several threads lose items, throw odd exceptions or corrupt themselves and hang. Use concurrent collections or a lock.

**Check-then-act on shared state.** Overbooked seats, duplicate records and double-sent emails. Make the check and the act one atomic step: a lock, `ConcurrentDictionary.GetOrAdd`, or a database constraint.

**Static mutable fields in server code.** One request's data leaks into another's. Keep per-request data in scoped services or local variables.

**Spinning on plain fields.** Wait loops that never end in Release builds. Use `ManualResetEventSlim`, `SemaphoreSlim`, `Task` or a `CancellationToken` instead of a hand-made flag.

**Torn values.** Reading a `decimal`, `DateTimeOffset` or multi-field struct while another thread writes it gives values that never existed. Protect them with a lock, or share an immutable object by reference.

**Assuming tests prove thread safety.** A race that shows up once in a million runs passes every test. Reason about each shared piece of data.

### Cost model

A plain increment costs well under a nanosecond. An `Interlocked.Increment` on a value nobody else is touching costs a few nanoseconds. When several cores fight over the same cache line, every atomic operation or lock costs tens to hundreds of nanoseconds, because the line must move between cores. That is why heavily shared counters scale badly and per-thread counters scale well. The cost of a concurrency bug in production, measured in investigation time, is often days.

### In other languages (optional)

**JavaScript.** Your code runs on one thread, so data races on ordinary objects can't happen. Logical races still can: two async operations both check a value, both await something, and both act. Worker threads share memory only through `SharedArrayBuffer` and `Atomics`.

**Python.** The global interpreter lock makes each bytecode step atomic in standard builds, but `count += 1` is several steps and can still lose updates between threads.

**Go.** Encourages "share memory by communicating": pass data through channels instead of sharing it. `go test -race` detects data races at run time.

**Rust.** The compiler rejects most data races: it won't let two threads hold mutable access to the same data at once.

**SQL.** Databases face the same problems between concurrent transactions, and solve them with locks and isolation levels, as Transactions and isolation (7.4) explains.

### Key terms

- **Concurrency**: Structuring a program so that several tasks can be in progress at the same time.
- **Interleaving**: The order in which the steps of several threads end up running relative to each other.
- **Race condition**: A bug where the result depends on the timing of threads.
- **Read-modify-write**: Reading a value, computing a new one and writing it back. Not atomic unless made so.
- **Lost update**: A change that disappears because another thread overwrote it with a value based on stale data.
- **Check-then-act**: Checking a condition and acting on it, when the condition can change in between.
- **Data race**: Two threads accessing the same memory at once, at least one writing, with nothing ordering them.
- **Atomic operation**: An operation that other threads can only see as not started or fully done.
- **Torn read**: Reading a value while it is being written, getting part of the old value and part of the new.
- **Critical section**: Code that changes shared data and must not run in more than one thread at a time.
- **Visibility**: Whether a write made by one thread can be seen by another.
- **Happens-before**: A guarantee that everything done before a synchronizing operation is visible after it on another thread.
- **Shared-nothing**: A design where threads don't share mutable data and communicate by passing messages or values.
- **Heisenbug**: A bug that disappears or changes when you try to observe it, typical of timing problems.

### Where this leads

Locks, atomics and thread-safe collections are in Threads and locks (5.2). Doing many things at once without extra threads is in Async and await (5.3). Single-threaded concurrency is in The event loop (5.4). Using many cores for speed is in Parallelism (5.5). The same problems between database transactions are in Transactions and isolation (7.4), and between machines in Distributed systems basics (12.2).

### Check yourself

1. Run `races.cs` several times in Debug and in Release. Record how the plain counter and the spinner behave in each, and explain the differences.
2. Fix the booking race in `races.cs` with a `lock` around the check and the act. Run it and confirm only one booking succeeds.
3. Change `Flag.Stop` to be read with `Volatile.Read(ref flag.Stop)` inside the loop, and check that the spinner now stops.
4. Replace the tuple in `PairBox` with an immutable `record Pair(long X, long Y)` shared by reference. Explain why the torn reads disappear.
5. Find a static field in a web application you know. Decide whether it is safe when two requests run at once.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Seats sold twice during a flash sale

Two customers click "Buy" on the last seat within the same millisecond. Both get confirmations.

**What goes wrong.** Check-then-act: both requests read `available = 1`, both decide to sell, both write.

*Before*

```csharp
var flight = await db.Flights.FindAsync(id);
if (flight.SeatsLeft > 0) { flight.SeatsLeft--; await db.SaveChangesAsync(); }   // race window
```

*After: one atomic statement decides*

```csharp
int updated = await db.Flights
    .Where(f => f.Id == id && f.SeatsLeft > 0)
    .ExecuteUpdateAsync(s => s.SetProperty(f => f.SeatsLeft, f => f.SeatsLeft - 1), ct);

if (updated == 0) return Results.Conflict(new { error = "sold_out" });
```

> **Lesson:** When two requests read, decide and write separately, both can win. Let one atomic statement or a constraint decide.

#### Example 2: A counter that lost updates under load

An in-memory request counter shows 9,412 after 10,000 parallel requests.

*Before / after*

```csharp
private static long _count;
// _count++;                         // read, add, write: three steps, threads overwrite each other
Interlocked.Increment(ref _count);    // one atomic CPU instruction
```

> **Lesson:** Even `x++` is read-modify-write. Shared counters need Interlocked or a lock.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a21"></a>

#### Threads, blocking, and what “concurrent” means

The next few sections are about `async`, and every explanation of it assumes you already know what a thread is and why blocking one matters. If you do, skip ahead. If not, this is the piece that makes the rest make sense.

##### A thread is a worker following instructions

``` text
  a PROCESS is your running program: its memory, its files, its sockets.
  a THREAD is one worker inside it, executing instructions one at a time.

    process: MyApp.exe
      +-- thread 1   currently running: OrderService.Place()
      +-- thread 2   currently running: Logger.Flush()
      +-- thread 3   currently running: nothing - WAITING on the database

  a CPU CORE can run one thread at any instant. The operating system
  rapidly switches between threads so that many appear to run at once.

  each thread has its OWN STACK (about 1 MB) and shares the process's HEAP.
  that sharing is the whole reason concurrent programming is hard: two
  threads touching the same object at the same time can interleave in
  ways neither author imagined.
```

##### Blocking: a worker standing still

``` text
  var data = File.ReadAllText("big.csv");     // a BLOCKING call

  what the thread does:
    ask the OS to read the file
    ...
    ...   wait.  The thread is ALIVE, OCCUPIED, and doing NOTHING.
    ...   it cannot serve another request. It cannot be reused.
    ...   ~10 ms for a disk. ~50 ms for a database. ~200 ms for an API.
    receive the data
    continue

  in that time a modern CPU core could have executed roughly a HUNDRED
  MILLION instructions. The thread is holding ~1 MB of stack and a slot
  in the thread pool to accomplish precisely nothing.
```

This is the entire problem `async` exists to solve. It is **not** about making one operation faster — a database query takes exactly as long either way. It is about not *occupying a worker* while you wait for something outside your process to answer.

##### The thread pool, and how it runs out

``` text
  creating a thread costs ~1 MB and a system call, so .NET keeps a POOL
  and hands them out.

  a web server: one thread per request being processed.

  BLOCKING VERSION                     ASYNC VERSION
    100 concurrent requests              100 concurrent requests
    each blocked 200 ms on the DB        each AWAITING the DB
    -> 100 threads occupied              -> the thread is RETURNED to the pool
    -> pool exhausted                       while waiting, and picks up
    -> request 101 QUEUES                   another request
    -> latency climbs for everyone       -> a handful of threads serve all 100

  and the pool grows only SLOWLY when starved - roughly one or two threads
  per second - so a burst of blocking calls produces a latency cliff that
  takes minutes to recover from. That is THREAD POOL STARVATION, and it
  is the single most common way a healthy .NET service stops responding.
```

##### Concurrency is not parallelism

|                               | Concurrency                               | Parallelism                                  |
|-------------------------------|-------------------------------------------|----------------------------------------------|
| Means                         | Several things *in progress*, interleaved | Several things executing at the same instant |
| Analogy                       | One chef alternating between three pans   | Three chefs, one pan each                    |
| Needs multiple cores          | No                                        | **Yes**                                      |
| What `async` gives you        | **This**                                  | No                                           |
| What `Parallel.For` gives you | —                                         | **This**                                     |
| Right for                     | Waiting on I/O — databases, HTTP, files   | CPU-bound work — maths, image processing     |

``` csharp
// the distinction, in code

// I/O-bound: the thread is released while waiting. ASYNC is the answer.
var user  = await db.Users.FindAsync(id);          // waiting on the database
var quote = await http.GetFromJsonAsync<Quote>(url); // waiting on the network

// CPU-bound: there is nothing to wait FOR - the CPU is genuinely busy.
// async does NOT help. Parallelism might.
var hashes = source.AsParallel().Select(Hash).ToArray();

// and the mistake: wrapping CPU work in Task.Run inside a web request
await Task.Run(() => ExpensiveCalculation());
// this does not make it faster. It moves the work to ANOTHER pool thread
// and adds scheduling overhead - so the server does the same work with
// one more thread involved.
```

> **\[Note\]**
>
> **The one sentence to carry into the next sections.** `await` means *“this will take a while and I am waiting on something else — give my worker to somebody else, and wake me when the answer arrives.”* Everything about state machines, `SynchronizationContext` and `ValueTask` is machinery in service of that one idea.

###### Watching thread-pool starvation happen

``` text
# the counters that show it, live
dotnet-counters monitor -p <pid> System.Runtime
#   threadpool-thread-count       climbing SLOWLY (~1-2/sec) = starvation
#   threadpool-queue-length       > 0 sustained = work is WAITING for a thread
#   threadpool-completed-items-count
#
# the signature of starvation, and it is unmistakable once seen:
#   queue-length rising, thread-count crawling upward, CPU LOW.
#   low CPU is the tell - the threads are not busy, they are BLOCKED.
```

``` text
  WHY RECOVERY IS SO SLOW

    the thread pool has a minimum (usually = core count). Beyond that it
    injects new threads at roughly ONE OR TWO PER SECOND, deliberately -
    because thread injection is expensive and usually the wrong answer.

    so:
      t=0     100 requests arrive, each blocks 200 ms on a sync call
      t=0     12 pool threads occupied. 88 requests QUEUE.
      t=1s    13 threads. 87 still queued. More requests have arrived.
      t=10s   22 threads. The queue is now longer than it was.

    the queue grows faster than the pool. Latency climbs to seconds,
    then to timeouts, and it does NOT recover until the arrival rate
    drops - which is why this presents as a cliff rather than a slope.

    and the classic trigger is a single sync-over-async call on a hot
    path, often added deliberately to "avoid changing the signature".
```

``` csharp
// the three sync-over-async forms, all of which cause this
var r = GetAsync().Result;          // blocks
GetAsync().Wait();                   // blocks
var r = GetAsync().GetAwaiter().GetResult();   // blocks, and looks deliberate

// the emergency mitigation - raises the FLOOR so injection is not needed
ThreadPool.SetMinThreads(200, 200);
// this is a plaster, not a fix. It hides starvation by pre-creating
// threads, and each one costs ~1 MB of stack. Use it to survive an
// incident, then remove the blocking call.

// the actual fix: async all the way down. If a signature cannot change,
// that is a design problem to solve, not to work around.
```

| Symptom                                      | Distinguishes it from                          |
|----------------------------------------------|------------------------------------------------|
| High latency, **low CPU**                    | CPU saturation, where CPU is high              |
| `threadpool-queue-length` \> 0 sustained     | A slow dependency, where the queue stays empty |
| Recovery takes minutes after load drops      | A transient spike, which recovers immediately  |
| Worse with more traffic, not proportional    | Linear degradation                             |
| Health checks time out while the app is “up” | A crash, where the process is gone             |

### The Foundation

<p class="src">From <em>Backend Engineering</em> · async / await · The Foundation</p>

<a id="be-c313"></a>

#### What a thread is

One path of execution: a position in the code (instruction pointer), its own registers, and its own stack. That bundle is the thread's **context**.

More threads than cores means the OS **time-slices** — run A briefly, save A's context, load B's, run B. That save-and-swap is a **context switch**. Beyond the bookkeeping cost, the CPU caches were full of A's data; B now runs cache-cold and spends its first stretch fetching from main memory. Largely invisible in a profiler, entirely real.

Each thread also needs a stack, reserved up front because the OS can't predict call depth. Windows default: **1 MB**. Mostly reserved address space rather than committed physical memory — but every page you touch is committed for good.

<a id="be-c314"></a>

#### What blocking means

``` text
string name = Database.Query(sql);   // 50ms
```

The thread tells the OS *"I'm waiting on this socket, don't schedule me."* The OS marks it **blocked** and stops giving it CPU. Zero CPU burned — no spinning, no polling.

But its stack is still there, fully intact:

``` text
┌─────────────────────────────┐
│ GetUserName          ← top  │
│   id    = 42                │
│   sql   = "SELECT name..."  │
│   name  = (not set yet)     │
│   return to → line 391      │
├─────────────────────────────┤
│ HandleRequest               │
├─────────────────────────────┤
│ ProcessSocket               │
└─────────────────────────────┘
```

It has to be. That frame is the only place in the universe that knows `id` is 42 and which line comes next.

**The thread is trapped — not because it has work to do, but because it's the only thing holding that piece of paper.**

<a id="be-c315"></a>

#### The capacity failure

1,000 requests arrive in one second. The pool has 32 threads.

- Requests 1–32 get threads. All 32 sleep on the database.
- Request 33 queues. So do 34–1000.

Open Task Manager. **CPU: 3%.**

Nothing is slow. The database answers in its usual 50ms. Every component is healthy. The server serves nobody.

That's **thread pool starvation**: throughput collapses, latency spikes, CPU idles. It looks like a mystery precisely because no individual piece is broken.

You can't fix it with more threads. Creating one costs a few hundred microseconds (too slow per-request, which is why pools exist), and pools are sized near core count deliberately — past that, extra threads add context switches and cache pressure until the machine spends more time swapping than executing.

<a id="be-c316"></a>

#### The absurdity

**The OS does not need a thread to wait.**

Hand the kernel a socket read and it writes down "socket 42, notify this when data arrives," then forgets you. No thread stands guard. The NIC receives a packet, raises an interrupt, the kernel matches the note. IOCP on Windows; epoll or io_uring on Linux. Zero threads involved in the waiting.

So the waiting is already free. The megabyte is spent **entirely on remembering `id = 42` and which line to return to.**

That's the waste. Not the wait — the bookkeeping.

<a id="be-c317"></a>

#### The one idea

Why does waiting cost a thread?

**Because the only way we know to remember "where we are in a function" is a stack frame, and a stack frame requires a thread to hold it.** LIFO means you can't set it aside. Keep the whole thread, or lose your place.

Run the test: suppose stacks cost 1 KB instead of 1 MB. Does the problem go away? **No.** The thread still can't leave, because leaving means unwinding, and unwinding destroys the frame. You'd have a cheaper version of the same trap.

So the fix isn't "use less memory." It's **use memory that can be set down and picked up later.**

Look back at the comparison table in I.1 and the two properties in the last rows:

- A heap object **outlives the function that created it** — so it survives the frame being unwound.
- The heap is **shared across threads** — so a different thread can pick it up later.

Those two properties are exactly, and only, what's needed. The stack has neither, by construction. That's not an oversight in stack design; LIFO ordering and per-thread ownership are what make it fast. The stack is the wrong storage for something that has to be *paused*, and no amount of tuning changes that.

``` csharp
class WhereWeWere
{
    public int id;          // 42
    public string sql;
    public string name;
    public int nextStep;    // 1 = "resume after Query"
}
```

Hand that to the database layer, say "call me back with it," and **return** — free to serve request \#33. 50ms later, any free thread picks up the object, reads `nextStep`, and continues.

The request is *in progress* for 50ms and *occupies a thread* for maybe 0.1ms. 32 threads now serve 1,000 concurrent requests, because at any instant 990-odd are just objects on the heap.

Cheaper is a side effect. **Detachable is the point.**

<a id="be-c318"></a>

#### Three words that are not synonyms

- **Parallelism** — multiple things executing at once. Needs multiple cores. CPU throughput.
- **Concurrency** — multiple things in flight, progressing interleaved. No cores required.
- **Asynchrony** — not occupying a thread while waiting.

`async`/`await` is the third. It buys concurrency without threads.

It does **not** make any single request faster. It decouples *how many requests are in flight* from *how many threads exist*.

It also does **nothing** for thread safety. Threads corrupting each other's data is shared mutable state — fixed with locks, immutability, `Interlocked`, concurrent collections. Async arguably makes that *harder*, because your method now resumes on a different thread than it started on, so any thread-affinity assumption silently breaks.

------------------------------------------------------------------------
