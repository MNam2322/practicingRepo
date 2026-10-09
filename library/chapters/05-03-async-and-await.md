---
id: "5-3"
number: "5.3"
title: "Async and await"
part: 5
part_title: "Concurrency"
words: 18874
minutes: 82
---

# 5.3 Async and await

> **Quick look.** How async and await let a few threads keep thousands of operations in progress, and the mistakes that stall servers.
>
> - await pauses a method without blocking its thread; the compiler rewrites it into a state machine.
> - Async doesn't make one call faster; it lets the process have more calls in flight.
> - Never block with .Result or .Wait(): it deadlocks UI and classic ASP.NET apps and starves servers. Go async all the way.
> - Start independent operations together and await Task.WhenAll; throttle large fan-outs.
> - Pass CancellationToken everywhere; cancellation is cooperative and is not a failure.
> - Avoid async void and fire-and-forget; use a bounded Channel and a background service instead.
> - ValueTask only for hot paths that usually complete synchronously, with strict rules.
> - DbContext is not thread-safe: one operation at a time per context.


### The big picture

Most server code spends its time waiting: for a database, for another service, for a file. If each wait held a thread, a busy server would need thousands of threads, and threads are expensive.

Async and await let code wait without holding a thread, so a few threads can keep thousands of operations in progress. This chapter explains what really happens at an await, where the code continues afterwards, and the mistakes that turn async code into deadlocks and stalled servers. It also covers cancellation, streams of results, channels and the other tools of everyday async code.

### Before you start

**Builds on:** 5.2 Threads and locks, 2.5 Control flow and functions, 2.6 Scope and lifetime, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **UI thread** (5.4): The single thread that runs a user interface's event loop and alone may touch its controls.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Event loop** (5.4): A loop that takes ready callbacks from a queue and runs them one at a time, waiting for events when idle.
- **Controller** (8.3): A class whose action methods handle HTTP requests, with attributes for routing.
- **Client** (8.1): A program that sends requests to a server.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, collection expressions, lambdas, LINQ, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. While a method is paused at await on a network call, which thread is waiting?**

> None. The method's state is saved on the heap, the thread returns to the pool to do other work, and the operating system signals when the data arrives. A thread then picks up the method where it left off.

**2. What does await actually do when the task isn't finished yet?**

> It registers the rest of the method as a continuation on the task, then returns an unfinished task to the caller. When the awaited task completes, the continuation is scheduled to run.

**3. Why does calling .Result on a task in a desktop app's button handler often freeze the app?**

> The UI thread blocks waiting for the task. The task's continuation needs the UI thread to finish, because of the synchronization context. Each waits for the other: a deadlock.

**4. What is thread pool starvation, and what usually causes it?**

> All pool threads are blocked, so queued work, including async continuations, can't run. The usual cause is blocking on async code with `.Result`, `.Wait()` or `Thread.Sleep` in server code. Requests pile up while CPU use stays low.

**5. What is the difference between WaitAsync(timeout) and cancelling with a token?**

> `WaitAsync` stops you waiting, but the operation keeps running in the background. Cancelling with a token asks the operation itself to stop, if it supports cancellation.

**6. When should library code use ConfigureAwait(false)?**

> On awaits in general-purpose libraries, so continuations don't try to return to a caller's UI or other synchronization context. ASP.NET Core has no synchronization context, so application code there doesn't need it.

**7. What is IAsyncEnumerable<T> for?**

> A sequence whose items arrive over time, each possibly needing an await, such as rows streamed from a database or messages from a feed. You consume it with `await foreach`.

**8. Why should you not wrap work in Task.Run inside an ASP.NET Core request?**

> It moves the work from one pool thread to another, adding overhead without freeing anything. Task.Run helps in desktop apps, to keep CPU work off the UI thread.

### The problem is waiting

Most server code spends most of its time waiting. It waits for a database to answer, for another service to respond, for a file to be read. That work is **I/O-bound**: its speed is set by the time devices and networks take, not by the processor. Work whose speed is set by computation, such as compressing an image or calculating routes, is **CPU-bound**.

A **blocking call** makes the calling thread wait until the result is ready. The thread does nothing, yet it still holds its stack memory and a place in the thread pool. Threads and locks (5.2) showed that threads are expensive. A server handling 10,000 slow requests with blocking calls would need 10,000 threads.

**Asynchronous programming** lets a program start an operation, go and do other things, and come back when the result is ready. No thread sits waiting. A few threads can then keep thousands of operations in flight.

### Tasks

In .NET, an operation that will finish in the future is represented by a **Task**. A `Task` finishes with no result. A `Task<T>` finishes with a value of type `T`. A task is in one of three final states: completed successfully, faulted with an exception, or cancelled.

A method that starts I/O returns a task straight away, before the I/O is done. `File.ReadAllTextAsync`, `HttpClient.GetAsync` and `DbContext.SaveChangesAsync` all work this way. By convention, their names end in `Async`.

### What await does

The `await` keyword is how you use a task's result without blocking.

When the code reaches `await someTask`, it checks whether the task is already finished. If it is, the method just carries on. If it isn't, three things happen. The rest of the method is registered as a **continuation** on the task: "when you finish, run this". The method returns an unfinished task of its own to its caller. And the thread goes back to the pool, free for other work.

When the awaited operation finishes, the operating system tells the runtime, which queues the continuation. A pool thread picks it up and the method continues from exactly where it paused, with all its local variables intact. The caller, if it awaited too, continues after that.

For I/O, no thread waits at all while the operation is in progress. The operating system's own asynchronous I/O does the waiting, through mechanisms called I/O completion ports on Windows and epoll on Linux, as The event loop (5.4) describes.

### The state machine

The compiler makes this possible by rewriting every `async` method into an **async state machine**, much like it does for iterators in Control flow and functions (2.5). Each `await` becomes a numbered point where the method can pause. Local variables move into fields of the state machine object, so they survive while no thread is running the method. That is also why a large buffer held across an `await` stays alive, as Scope and lifetime (2.6) noted.

```csharp
// async.cs — run with: dotnet run async.cs
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

// 1. Waiting one after another versus waiting together
var sw = Stopwatch.StartNew();
await LoadFare("LHR-JFK"); await LoadFare("LHR-SIN"); await LoadFare("LHR-DXB");
Console.WriteLine($"one after another: {sw.ElapsedMilliseconds} ms");               // about 600
sw.Restart();
decimal[] fares = await Task.WhenAll(LoadFare("LHR-JFK"), LoadFare("LHR-SIN"), LoadFare("LHR-DXB"));
Console.WriteLine($"together: {sw.ElapsedMilliseconds} ms, total {fares.Sum()}");  // about 200, total 210

// 2. No thread waits during an await
Console.WriteLine($"before await: thread {Environment.CurrentManagedThreadId}");
await Task.Delay(50);
Console.WriteLine($"after await:  thread {Environment.CurrentManagedThreadId}");   // often a different thread

// 3. Blocking on async code wastes pool threads
sw.Restart();
await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => Task.Delay(100).Wait())));
Console.WriteLine($"100 blocking waits: {sw.ElapsedMilliseconds} ms");   // usually far more than 100
sw.Restart();
await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Delay(100)));
Console.WriteLine($"100 async waits:    {sw.ElapsedMilliseconds} ms");   // about 100

// 4. Stop waiting, or stop the work
var slow = LoadFare("LHR-SYD", delayMs: 5000);
try { await slow.WaitAsync(TimeSpan.FromMilliseconds(300)); }
catch (TimeoutException) { Console.WriteLine($"stopped waiting; the lookup is still running: {!slow.IsCompleted}"); }   // True
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
try { await LoadFare("LHR-SYD", delayMs: 5000, cts.Token); }
catch (OperationCanceledException) { Console.WriteLine("cancelled: the lookup itself stopped"); }

// 5. An async stream: results as they arrive
await foreach (string update in DepartureUpdates())
    Console.WriteLine(update);                    // BA117 boarding, QF1 delayed, SQ321 departed, 100 ms apart

// 6. A bounded channel between a producer and a consumer
var channel = Channel.CreateBounded<int>(capacity: 5);
var producer = Task.Run(async () =>
{
    for (int i = 1; i <= 20; i++) await channel.Writer.WriteAsync(i);   // waits whenever 5 are queued
    channel.Writer.Complete();
});
int total = 0;
await foreach (int item in channel.Reader.ReadAllAsync()) total += item;
await producer;
Console.WriteLine(total);                        // 210

// 7. Turning an old callback API into a task
Console.WriteLine(await WhenGateReady("B12"));  // gate B12 ready

static async Task<decimal> LoadFare(string route, int delayMs = 200, CancellationToken ct = default)
{
    await Task.Delay(delayMs, ct);               // stands in for a network call
    return route.Length * 10m;
}

static async IAsyncEnumerable<string> DepartureUpdates([EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (string update in new[] { "BA117 boarding", "QF1 delayed", "SQ321 departed" })
    {
        await Task.Delay(100, ct);
        yield return update;
    }
}

static Task<string> WhenGateReady(string gate)
{
    var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var timer = new Timer(_ => tcs.TrySetResult($"gate {gate} ready"), null, 100, Timeout.Infinite);   // callback style
    tcs.Task.ContinueWith(_ => timer.Dispose());  // keeps the timer referenced until it fires
    return tcs.Task;
}
```

### Where the continuation runs

After an `await`, which thread runs the rest of the method? It depends on the **synchronization context** that was current when the `await` began.

Desktop UI frameworks such as WPF, Windows Forms and .NET MAUI have one. Only the UI thread may touch the window, so their context sends continuations back to the UI thread. That is what lets a button handler await a download and then update a label safely.

ASP.NET Core and console apps have no synchronization context. Continuations run on any pool thread. So the thread ID often changes across an `await`, as the example shows, and code must not assume it stays on one thread. `AsyncLocal` values do flow across, as Scope and lifetime (2.6) explained.

`ConfigureAwait(false)` on an await says "I don't need to come back to the original context". General-purpose libraries should use it on their internal awaits, so they never try to return to a caller's UI thread. Application code in ASP.NET Core doesn't need it.

### Async all the way

Once a method is asynchronous, its callers should be too, all the way up to the entry point: the controller action, the message handler, `Main`. Mixing styles causes two classic failures.

**Sync over async** means blocking on a task with `.Result`, `.Wait()` or `GetAwaiter().GetResult()`. In an app with a synchronization context, it can deadlock. The UI thread blocks waiting for the task, and the task's continuation is waiting for the UI thread.

In a server, sync over async causes **thread pool starvation**. Each blocked request holds a pool thread. When all threads are blocked, nothing can run, not even the continuations that would unblock them. The pool adds threads slowly, about one or two per second once past its minimum. Requests queue up, response times climb to seconds, and CPU use stays low. The example's blocking loop shows a small version of it. The fix is to await instead of block, everywhere.

The opposite mistake, **async over sync**, wraps blocking code in `Task.Run` and calls it async. In ASP.NET Core this moves the blocking from one pool thread to another and helps nothing. In a desktop app, `Task.Run` is the right tool for moving CPU-heavy work off the UI thread.

### Doing several things at once

`await` alone runs operations one after another. To run them concurrently, start them all first and then wait for all of them. `Task.WhenAll` finishes when every task has finished. `Task.WhenAny` finishes when the first one does, which suits "whichever server answers first" or custom timeouts. `Task.WhenEach`, added in .NET 9, gives you each task as it completes.

Starting a thousand requests at once may overwhelm the service you are calling. Limit how many are in flight with `SemaphoreSlim`, as Threads and locks (5.2) showed, or with `Parallel.ForEachAsync` and its `MaxDegreeOfParallelism`, covered in Parallelism (5.5).

### Cancellation and timeouts

Errors and exceptions (2.8) introduced cancellation tokens. In async code they are essential. Accept a `CancellationToken` in every async method that does I/O, and pass it down to every call that accepts one. ASP.NET Core gives each request a token that is cancelled if the client disconnects.

Cancellation is cooperative: code must check the token or pass it to something that does. `CancellationTokenSource.CancelAfter` turns it into a timeout. `Task.WaitAsync(timeout)` is different. It only stops you waiting; the operation continues in the background, still holding whatever it holds. Prefer real cancellation where the operation supports it.

### Async streams and channels

An **async stream**, `IAsyncEnumerable<T>`, is a sequence whose items arrive over time, each one possibly needing an `await`. An async iterator produces one with `yield return` inside an `async` method. You consume it with `await foreach`. EF Core can stream query results this way, handling each row as it arrives instead of loading all of them into memory.

A **channel**, from `System.Threading.Channels`, is an asynchronous producer-consumer queue. Writers call `WriteAsync`; readers call `ReadAsync` or loop over `ReadAllAsync`. A bounded channel makes writers wait when it is full, which applies backpressure without blocking any thread. Channels are the standard way to hand work from request handlers to a background worker, as Background work and queues (8.7) shows.

### Bridging old APIs

Some APIs report completion through callbacks or events instead of tasks. A **TaskCompletionSource** creates a task that you complete by hand, calling `SetResult`, `SetException` or `SetCanceled` when the callback fires. Always create it with `TaskCreationOptions.RunContinuationsAsynchronously`. Without that flag, the awaiting code runs inside the callback, on whatever thread fired it, which can cause deadlocks and strange ordering.

### ValueTask

Every `async Task` method that doesn't complete synchronously allocates a task object. For methods that usually finish without waiting, such as a read from a buffer that is often already full, that allocation can matter in hot paths. **ValueTask** avoids it when the result is already available.

It comes with rules. Await a `ValueTask` exactly once. Don't await it concurrently from two places. Don't call `.Result` on it before it completes. When in doubt, use `Task`; `ValueTask` is an optimization for library code in hot paths.

### Fire and forget

Starting a task without awaiting it is called **fire and forget**. It looks convenient: `_ = SendEmailAsync(...)`, and the request returns immediately. It is almost always a mistake in server code. Any exception is lost silently. The task may still be running when the request's scoped services, such as the `DbContext`, are disposed. And if the app shuts down, the work is simply abandoned.

Hand background work to something built for it: a channel read by a `BackgroundService`, or a durable queue, as Background work and queues (8.7) describes.

### In short

I/O-bound work waits; CPU-bound work computes. `await` pauses a method without blocking its thread, saving its state until the task completes. Start independent operations together and await them with `WhenAll`. Never block on async code with `.Result` or `.Wait()`: it deadlocks desktop apps and starves servers. Pass cancellation tokens everywhere, use channels for background work, and avoid `async void` and fire-and-forget.

### What goes wrong

**`.Result` and `.Wait()` on tasks.** Deadlocks in UI apps and thread pool starvation in servers. Await instead, all the way up.

**`async void`.** Exceptions crash the process, and callers can't know when the work finished. Return `Task`, except in UI event handlers.

**Sequential awaits for independent work.** Three calls awaited one by one take three times as long as needed. Start them together and use `Task.WhenAll`.

**Unbounded concurrency.** `Task.WhenAll` over 10,000 HTTP calls overloads the target service or exhausts sockets. Limit the number in flight.

**Not passing cancellation tokens.** A client disconnects and the server keeps running an expensive query for nobody. Accept and pass tokens everywhere.

**Fire and forget in requests.** Background work fails silently or crashes on disposed services. Use a channel and a background service.

**`TaskCompletionSource` without `RunContinuationsAsynchronously`.** Awaiting code runs inline on the completing thread, causing re-entrancy bugs and deadlocks. Always pass the flag.

**Blocking calls hidden in async methods.** A method named `...Async` that calls `Thread.Sleep` or synchronous database APIs still blocks a thread. Use the async versions all the way down.

### Cost model

An `await` on an already-completed task costs a few nanoseconds. An `await` that really suspends costs roughly a microsecond or less, including allocating the state machine and task the first time, and scheduling the continuation. That is tiny compared with the milliseconds of the I/O it waits for, and far cheaper than a blocked thread, which holds about a megabyte of stack. A server that awaits its I/O can hold tens of thousands of requests in flight on a few dozen threads. The thread pool adds threads slowly when starved, about one or two per second, which is why starvation turns milliseconds into seconds.

### In other languages (optional)

**JavaScript and TypeScript.** `async` and `await` work on promises, the equivalent of tasks. Everything runs on one thread, so continuations always come back to the same thread, via the event loop covered in the next chapter. `Promise.all`, `Promise.race` and `Promise.allSettled` match `WhenAll` and `WhenAny`. `AbortController` plays the role of cancellation tokens.

**Python.** `asyncio` provides `async` and `await` over an event loop. Calling blocking functions inside async code stalls every task on that loop.

**Java.** `CompletableFuture` is the task equivalent. Virtual threads, added in Java 21, take another approach: blocking code that is cheap because the runtime parks lightweight threads instead of OS threads.

**Go.** Goroutines block freely, and the runtime multiplexes them over a few OS threads, so there is no separate async syntax.

**Rust.** `async` functions produce futures that do nothing until a runtime such as Tokio polls them.

### Key terms

- **I/O-bound**: Work whose speed is limited by waiting for devices or networks.
- **CPU-bound**: Work whose speed is limited by the processor.
- **Blocking call**: A call that makes its thread wait, doing nothing, until the result is ready.
- **Asynchronous programming**: Starting operations and continuing when they finish, without threads waiting in between.
- **Task**: The .NET object representing an operation that will complete in the future, possibly with a result.
- **Await**: Pausing a method until a task finishes, without blocking its thread.
- **Continuation**: The code that runs after an awaited task completes.
- **Async state machine**: The compiler-generated object that stores an async method's position and locals between awaits.
- **Synchronization context**: An object that decides where continuations run, such as back on a UI thread.
- **ConfigureAwait**: A way to tell an await whether it needs to resume on the original synchronization context.
- **Sync over async**: Blocking on asynchronous code with .Result or .Wait(). Causes deadlocks and starvation.
- **Thread pool starvation**: All pool threads blocked, so queued work and continuations can't run.
- **Async stream**: A sequence whose items arrive over time, consumed with await foreach. IAsyncEnumerable in .NET.
- **Channel**: An asynchronous producer-consumer queue, optionally bounded.
- **TaskCompletionSource**: An object that creates a task you complete by hand, used to wrap callback-based APIs.
- **ValueTask**: A task-like type that avoids an allocation when the result is already available.
- **Fire and forget**: Starting a task without awaiting it or handling its outcome.

### Where this leads

How one thread can serve many waiting operations is in The event loop (5.4). CPU-bound work across cores is in Parallelism (5.5). Async I/O over the network is in TCP and UDP (6.3). Async database access is in ORMs and EF Core (7.7). A fully async request pipeline is in A modern .NET backend (8.3), and background processing with channels is in Background work and queues (8.7).

### Check yourself

1. Change the "together" section of `async.cs` to use `Task.WhenAny`, and print which route answered first. Give each route a different delay.
2. Raise the blocking loop in `async.cs` from 100 to 500 tasks, and compare the timings. Explain the growth.
3. Pass a `CancellationToken` into `DepartureUpdates` that cancels after 150 ms, and handle the exception cleanly.
4. Change the channel to `Channel.CreateBounded<int>(1)` and add a `Console.WriteLine` in the producer. Observe how producer and consumer take turns.
5. Search a codebase you know for `.Result`, `.Wait()` and `async void`. For each one, decide whether it is safe, and how to remove it if not.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Thread pool starvation from one .Result

At 9 am the API slows to 30-second responses while CPU sits at 15%. A dump shows hundreds of threads blocked in `GetAwaiter().GetResult()`.

*Before*

```csharp
public Customer Get(int id) => _client.GetCustomerAsync(id).Result;   // blocks a pool thread while waiting
```

*After: async all the way*

```csharp
public async Task<Customer> GetAsync(int id, CancellationToken ct) =>
    await _client.GetCustomerAsync(id, ct);
// csproj: <WarningsAsErrors>CA1849;CA2016</WarningsAsErrors> finds blocking calls and unforwarded tokens
```

> **Lesson:** Blocking on async code takes threads out of the pool. Under load the pool starves while the CPU looks idle.

#### Example 2: Three independent calls made one after another

The trip page loads flight, hotel and car rental sequentially: 300 + 250 + 200 ms.

*Start together, await together*

```csharp
var flightTask = flights.GetAsync(tripId, ct);
var hotelTask  = hotels.GetAsync(tripId, ct);
var carTask    = cars.GetAsync(tripId, ct);
await Task.WhenAll(flightTask, hotelTask, carTask);              // about 300 ms total

var page = new TripPage(flightTask.Result, hotelTask.Result, carTask.Result);   // already completed: safe
// Note: these use separate HttpClients. Never run parallel queries on one DbContext.
```

> **Lesson:** Independent I/O can overlap. Start the tasks first, then await them together.

### Async/Await in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · Async/Await in Depth</p>

<a id="dn-28"></a>

#### What the compiler turns an async method into

```csharp
public async Task<int> GetAsync()
{
    var a = await FetchAsync();     // suspension point 1
    var b = await ParseAsync(a);    // suspension point 2
    return a + b;
}
```

The compiler generates a **state machine** (a struct named something like `<GetAsync>d__0`) implementing `IAsyncStateMachine`:

```csharp
struct StateMachine : IAsyncStateMachine
{
    public int _state;                           // -1 = not started/finished, 0,1 = awaiting nth
    public AsyncTaskMethodBuilder<int> _builder; // creates and completes the Task
    public int _a, _b;                           // locals become FIELDS (they survive awaits)
    private TaskAwaiter<int> _awaiter;

    public void MoveNext()
    {
        switch (_state)
        {
            case -1:  // first entry
                _awaiter = FetchAsync().GetAwaiter();
                if (!_awaiter.IsCompleted)        // <-- FAST PATH check
                {
                    _state = 0;
                    _builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this); // BOXES onto the heap
                    return;                        // RETURNS THE THREAD TO THE POOL
                }
                goto case 0;
            case 0:
                _a = _awaiter.GetResult();
                ...
        }
    }
}
```

##### Four things that follow from this

**1. Fast path — if the task is already complete, nothing async happens.**
When `IsCompleted == true` (reading from a buffer, a cache hit), the code runs **fully synchronously**: no allocation, no thread switch. This is precisely why `ValueTask` exists.

**2. The state machine is a struct and is only boxed when it actually has to suspend.**
An async method that always completes synchronously is nearly allocation-free. Only a real suspension copies the state machine to the heap.

**3. `await` does NOT create a thread.**
It registers a continuation and returns the thread to the pool. When the I/O completes, an I/O completion port grabs a pool thread to resume `MoveNext`. Async **reduces** thread count; it doesn't increase it.

**4. The SynchronizationContext decides where you resume.**

- WinForms/WPF: the continuation returns to the UI thread.
- Classic ASP.NET: returns to the request context.
- **ASP.NET Core has no SynchronizationContext** → continuations run on any thread pool thread.

<a id="dn-29"></a>

#### `.Result` / `.Wait()` — why they're dangerous

##### Deadlock (classic ASP.NET, WPF, WinForms)

```csharp
public ActionResult Index()
{
    var data = GetDataAsync().Result;   // the UI/request thread BLOCKS, holding the context
    return View(data);
}
async Task<string> GetDataAsync()
{
    await _http.GetStringAsync(url);    // wants to resume on that context
    return "done";                      // ...but that thread is blocked on .Result
}                                        // => PERMANENT DEADLOCK
```

The band-aid is `ConfigureAwait(false)` (don't capture the context); the real fix is **async all the way**.

##### Thread pool starvation (ASP.NET Core)
No deadlock, but every `.Result` **occupies a thread pool thread**. Under load the pool drains, and the runtime injects new threads only slowly (roughly 1–2 per second), so latency spikes and requests time out en masse. The signature symptom: **low CPU with a long request queue**.

##### Also
- `.Result` wraps exceptions in `AggregateException` (hard to read); `await` rethrows the original.

**Rules:**

```csharp
// AVOID: .Result, .Wait(), .GetAwaiter().GetResult()
// DO: async all the way, from controller down to repository
// DO: ConfigureAwait(false) on every await inside a library
// DO: async void ONLY for event handlers - exceptions in async void
//     can't be caught and will crash the process
```

<a id="dn-30"></a>

#### Task vs ValueTask

| | `Task<T>` | `ValueTask<T>` |
|---|---|---|
| Kind | A class → **always allocates** (except `Task.CompletedTask` and cached small ints) | A struct → **no allocation** when completing synchronously |
| Awaiting twice | Safe | **Not allowed** — undefined behavior |
| `Task.WhenAll` | Works | Requires `.AsTask()` first |
| `.Result` before completion | Blocks | **Invalid** |
| Use for | The default; public APIs | Hot paths that usually complete synchronously (cache hits) |

```csharp
// The canonical ValueTask case: a cache hit allocates nothing
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
        return new ValueTask<User>(user);          // zero allocation

    return new ValueTask<User>(LoadFromDbAsync(id));  // only the slow path allocates
}
```

**Rules for ValueTask:**

```csharp
var vt = GetUserAsync(1);
var a = await vt;
var b = await vt;          // WRONG - may return garbage or throw

var task = GetUserAsync(1).AsTask();   // convert if you need to await more than once
```

`IAsyncEnumerable<T>` and `IAsyncDisposable` use `ValueTask` for exactly this allocation reason.

<a id="dn-31"></a>

#### Task.WhenAll / WhenAny / Parallel

```csharp
// Run independent I/O-bound work concurrently
var userTask   = _userApi.GetAsync(id, ct);
var ordersTask = _orderApi.GetAsync(id, ct);
var creditTask = _creditApi.GetAsync(id, ct);
await Task.WhenAll(userTask, ordersTask, creditTask);
var dto = new Dto(await userTask, await ordersTask, await creditTask);
```

**Exception trap:** `WhenAll` waits for **all** tasks to finish before throwing, and `await` surfaces only the **first** exception. To see them all:

```csharp
var all = Task.WhenAll(t1, t2, t3);
try { await all; }
catch { foreach (var e in all.Exception!.InnerExceptions) Log.Error(e, "..."); }
```

**Timeouts:**

```csharp
// .NET 6+
await task.WaitAsync(TimeSpan.FromSeconds(5), ct);
```

**Bounding concurrency (essential when calling external APIs):**

```csharp
// .NET 6+
await Parallel.ForEachAsync(items, new ParallelOptions
{
    MaxDegreeOfParallelism = 10,
    CancellationToken = ct
}, async (item, token) => await ProcessAsync(item, token));

// Don't do: items.Select(ProcessAsync) over 10,000 items -> 10,000 concurrent requests
```

| Use | For |
|---|---|
| `Task.WhenAll` / `await` | **I/O-bound**: databases, HTTP, files |
| `Parallel.For/ForEach` | **CPU-bound**: computation across cores |
| `Task.Run` | Moving CPU-bound work off the current thread (never for I/O) |

### Concurrency Primitives & Threading

<p class="src">From <em>.NET Backend Deep Dive</em> · Concurrency Primitives & Threading</p>

<a id="dn-43"></a>

#### CancellationToken — propagate it or it's decoration

```csharp
// Accepting a token and not passing it on makes it useless
public async Task<List<Order>> GetAsync(CancellationToken ct)
    => await _db.Orders.ToListAsync();          // ct dropped

// Pass it ALL the way down
public async Task<List<Order>> GetAsync(CancellationToken ct)
    => await _db.Orders.ToListAsync(ct);
```

```csharp
// Combine cancellation sources: client abort + your own timeout
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    httpContext.RequestAborted, timeout.Token);

await _api.CallAsync(linked.Token);

// Distinguish: did the user leave, or did we time out?
catch (OperationCanceledException) when (timeout.IsCancellationRequested)
{ throw new TimeoutException("Upstream too slow"); }
catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
{ return; }   // the client is gone; don't log this as an error
```

**In background services**, never use `RequestAborted`; use the `stoppingToken` from `ExecuteAsync` so graceful shutdown works.

**Where not to cancel:** once a write has started (a partial `SaveChanges`), cancelling can leave half-applied state. Put cancellation checks **before** the transaction, not inside it.

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a22"></a>

#### async/await: what the compiler actually does

`async`/`await` is not multithreading. It is a compiler transformation that rewrites your method into a state machine so the thread can be released while an I/O operation is in flight, then resumed on a continuation when it completes.

``` text
  await HttpClient.GetAsync(url)

  [thread 5] --start--> issue socket read --> RETURNS TO POOL
                                              (thread does other work)
             ...network... 40 ms ...
  [thread 9] <--continuation scheduled-- OS signals data ready
             resume after the await, locals restored from state machine
```

The payoff is throughput, not latency. One request still takes 40 ms; but 10,000 concurrent requests need a handful of threads instead of 10,000, and thread stacks are 1 MB each.

``` csharp
// async all the way down
public async Task<Order> GetAsync(Guid id, CancellationToken ct)
{
    var dto  = await _http.GetFromJsonAsync<OrderDto>($"/orders/{id}", ct);
    var rows = await _db.Lines.Where(l => l.OrderId == id).ToListAsync(ct);
    return Map(dto, rows);
}
```

> **\[Trap\]**
>
> **Common trap.** `.Result` and `.Wait()` on an async call deadlock in any context with a synchronisation context (classic ASP.NET, WPF). ASP.NET Core removed the sync context so it “works”, but it still burns a thread-pool thread and destroys scalability under load.

###### How it actually works

Here is the transformation, concretely. This method:

``` csharp
public async Task<int> GetLengthAsync(string url)
{
    var body = await _http.GetStringAsync(url);
    return body.Length;
}
```

becomes roughly this — a struct implementing a state machine, plus a builder that owns the returned Task:

``` csharp
private struct StateMachine : IAsyncStateMachine
{
    public int _state;                       // -1 = not started, 0 = at first await, -2 = done
    public AsyncTaskMethodBuilder<int> _builder;
    public MyClass _this;
    public string _url;                      // hoisted parameter
    private TaskAwaiter<string> _awaiter;    // hoisted across the await

    public void MoveNext()
    {
        try
        {
            string body;
            if (_state != 0)
            {
                _awaiter = _this._http.GetStringAsync(_url).GetAwaiter();
                if (!_awaiter.IsCompleted)          // FAST PATH: if already done, no yield at all
                {
                    _state = 0;
                    _builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this);
                    return;                          // <- the thread leaves here
                }
            }
            body = _awaiter.GetResult();             // <- and re-enters here later
            _state = -2;
            _builder.SetResult(body.Length);
        }
        catch (Exception ex) { _state = -2; _builder.SetException(ex); }
    }
}
```

- Every local that lives across an `await` becomes a field — that is why they survive the suspension, and why capturing large objects across awaits keeps them alive.
- The state machine is a *struct* and stays on the stack until the first real suspension, then is boxed onto the heap. A method whose awaits always complete synchronously allocates nothing.
- Exceptions are captured into the Task, not thrown at the call site — which is why an un-awaited Task swallows failures silently.
- There is no thread here at all. The continuation runs on whichever thread the completion arrives on.

###### ConfigureAwait, and why the advice differs

`await` captures the current `SynchronizationContext` and resumes on it. In a UI app that means the message loop, which is essential for touching controls. In classic ASP.NET it was the request context, which is where the classic deadlock came from. ASP.NET Core has no synchronization context at all, so there is nothing to capture.

| You are writing                           | Guidance                                                                      |
|-------------------------------------------|-------------------------------------------------------------------------------|
| ASP.NET Core application code             | Do nothing. `ConfigureAwait(false)` is noise                                  |
| A library used by anyone                  | `ConfigureAwait(false)` on every await — you cannot know the caller's context |
| WPF / WinForms / MAUI                     | Capture by default; `false` only in non-UI helpers                            |
| Anything that might run on .NET Framework | Treat it as a library: use `false`                                            |

``` csharp
// the deadlock, precisely, in a context-capturing environment:
public ActionResult Index()
{
    var data = GetDataAsync().Result;    // thread T holds the request context, blocks
    return View(data);
}
async Task<string> GetDataAsync()
{
    await _http.GetStringAsync(url);     // wants to resume ON the captured context
    return "done";                       // context is held by the blocked thread T
}                                        // -> permanent deadlock, one thread lost per request
```

###### Patterns that come up constantly

``` csharp
// run several independent calls concurrently instead of sequentially
var customerTask = _customers.GetAsync(id, ct);
var ordersTask   = _orders.GetForCustomerAsync(id, ct);
var creditTask   = _credit.CheckAsync(id, ct);
await Task.WhenAll(customerTask, ordersTask, creditTask);   // 3 x 100ms -> ~100ms total
var vm = new Dashboard(await customerTask, await ordersTask, await creditTask);

// bound the concurrency when the list is long
using var gate = new SemaphoreSlim(10);
var tasks = urls.Select(async url =>
{
    await gate.WaitAsync(ct);
    try { return await _http.GetStringAsync(url, ct); }
    finally { gate.Release(); }
});
var all = await Task.WhenAll(tasks);

// timeout without cancelling the underlying work incorrectly
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(3));
var result = await SlowAsync(cts.Token);

// fire and forget, done safely (there is no truly safe version; this is the least bad)
_ = Task.Run(async () =>
{
    try { await BackgroundAsync(CancellationToken.None); }
    catch (Exception ex) { _log.LogError(ex, "Background work failed"); }
});
```

> **\[Trap\]**
>
> **Common trap.** `Task.WhenAll` throws only the *first* exception when awaited, even if five tasks failed. To see them all, await it into a variable and inspect `task.Exception.InnerExceptions`. Silent loss of the other four is a real and common diagnostic gap.

<a id="be-a23"></a>

#### Task, ValueTask, and cancellation

A `Task` is a heap allocation representing a future result. When a method usually completes synchronously — a cache hit, a buffered read — that allocation is pure waste, which is what `ValueTask<T>` avoids.

| Type              | Use when                                  | Rules                                     |
|-------------------|-------------------------------------------|-------------------------------------------|
| `Task`            | Default for everything                    | Awaitable many times, safe to store       |
| `ValueTask<T>`    | Hot path, usually completes synchronously | Await exactly once, never block on it     |
| `Task.FromResult` | Sync value from an async signature        | No allocation for cached common values    |
| `async void`      | Only event handlers                       | Exceptions crash the process, uncatchable |

Cancellation is cooperative. A `CancellationToken` does not stop anything; you must pass it down and check it. Accept one as the last parameter of every async method you write and thread it all the way through — retro-fitting cancellation later is miserable.

``` csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
cts.CancelAfter(TimeSpan.FromSeconds(5));

try { await DoWorkAsync(cts.Token); }
catch (OperationCanceledException) when (cts.IsCancellationRequested) { /* our timeout */ }
```

###### Worked example

The case where `ValueTask` actually matters: a cache with a 95% hit rate on a hot path.

``` csharp
// Task version: allocates a Task object on EVERY call, even cache hits
public async Task<Product> GetAsync(int id, CancellationToken ct)
{
    if (_cache.TryGetValue(id, out var p)) return p;    // still allocates the Task
    return await LoadAsync(id, ct);
}

// ValueTask version: cache hits allocate nothing at all
public ValueTask<Product> GetAsync(int id, CancellationToken ct)
{
    if (_cache.TryGetValue(id, out var p))
        return new ValueTask<Product>(p);               // wraps the value in a struct
    return new ValueTask<Product>(LoadAsync(id, ct));   // slow path pays the usual cost
}
```

At 50,000 calls per second with a 95% hit rate, the Task version allocates roughly 47,500 objects per second of about 80 bytes each — near 4 MB/s of pure garbage that exists only to say ‘here is a value I already had’. That is a gen 0 collection every second or two, for nothing. The `ValueTask` version allocates on the 5% miss path only.

> **\[Note\]**
>
> **Do not reach for it by default.** For a method that awaits I/O and returns once per request, the Task allocation is irrelevant and `ValueTask` only adds constraints. The rule is: measure an allocation problem first, and only then convert the specific hot method.

###### The ValueTask rules, and why they exist

A `ValueTask` may be backed by an `IValueTaskSource` that gets *reused* after completion. That is the whole point — it is how `Socket.ReceiveAsync` and `Channel` avoid per-operation allocation. It also means the object underneath your ValueTask may already have been recycled for someone else's operation.

| Rule                                      | What happens if you break it                                               |
|-------------------------------------------|----------------------------------------------------------------------------|
| Await it exactly once                     | You read another operation's result, or get an `InvalidOperationException` |
| Do not call `.Result` before it completes | Blocks, or throws — there is no synchronous wait support                   |
| Do not store it in a field or collection  | By the time you await it, the backing source has been reused               |
| Do not pass it to `Task.WhenAll`          | Call `.AsTask()` first, which allocates — and that is fine                 |

``` csharp
// need it twice? convert once, then treat it as a Task
Task<Product> t = GetAsync(id, ct).AsTask();
await Task.WhenAll(t, otherTask);
var product = await t;
```

###### Cancellation done properly

``` csharp
// 1. accept it, last parameter, default only on public API surfaces
public async Task<Report> BuildAsync(Query q, CancellationToken ct = default)

// 2. pass it to EVERY async call you make - the single most common omission
var rows = await _db.Rows.Where(...).ToListAsync(ct);
var extra = await _http.GetAsync(url, ct);

// 3. check it in your own loops, especially CPU-bound ones
foreach (var row in rows)
{
    ct.ThrowIfCancellationRequested();
    Process(row);
}

// 4. combine sources: caller cancellation, a timeout, and app shutdown
using var cts = CancellationTokenSource.CreateLinkedTokenSource(
    httpContext.RequestAborted, appLifetime.ApplicationStopping);
cts.CancelAfter(TimeSpan.FromSeconds(30));

// 5. distinguish YOUR cancellation from the caller's when handling it
try { await WorkAsync(cts.Token); }
catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
{ /* the client went away - do not log as an error */ }
catch (OperationCanceledException)
{ /* our own timeout - this IS worth logging */ }
```

One subtlety worth internalising: a cancelled operation that has already mutated state is not rolled back. Cancellation stops future work; it does not undo past work. Anything that must be all-or-nothing needs a transaction or a compensating action, exactly as with any other failure.

<a id="be-a39"></a>

#### IAsyncEnumerable and streaming data

A `Task<List<T>>` gives you everything at once, after waiting for all of it, in memory. `IAsyncEnumerable<T>` gives you items as they arrive. For anything large, long-running, or unbounded, the difference is whether the service stays up.

``` csharp
public async IAsyncEnumerable<Reading> ReadAllAsync(
    string path, [EnumeratorCancellation] CancellationToken ct = default)
{
    using var reader = new StreamReader(path);
    while (await reader.ReadLineAsync(ct) is { } line)
    {
        if (TryParse(line, out var reading))
            yield return reading;                  // hands one item on, then pauses here
    }
}

// consumption reads like foreach, with an await
await foreach (var reading in ReadAllAsync(path, ct))
    await _sink.WriteAsync(reading, ct);

// memory: one line at a time, regardless of a 200 GB file
```

The `[EnumeratorCancellation]` attribute is not optional decoration. Without it, the token passed to the method is captured at call time but the token supplied via `WithCancellation` at enumeration time is ignored, so cancellation silently does nothing. It is the single most common mistake with async streams.

###### Streaming through the whole request path

``` csharp
// database -> HTTP response, never fully in memory at any point
app.MapGet("/exports/readings", (AppDb db, DateOnly from, CancellationToken ct) =>
{
    var rows = db.Readings.AsNoTracking()
        .Where(r => r.TakenOn >= from)
        .OrderBy(r => r.TakenOn)
        .Select(r => new ReadingDto(r.DeviceId, r.TakenOn, r.Value))
        .AsAsyncEnumerable();                   // EF streams rows off the reader

    return TypedResults.Ok(rows);               // System.Text.Json streams the array out
});
```

``` text
  materialised                              streamed

  SELECT ... -> 4,000,000 rows              SELECT ... -> reader open
    -> List<Dto> in memory (~2 GB)            -> read row, map, serialise, flush
    -> serialise entire array                 -> read row, map, serialise, flush
    -> write response                         -> ...
  peak memory: ~2 GB per concurrent          peak memory: a few KB per request
  time to first byte: ~40 s                  time to first byte: ~20 ms
  10 concurrent requests: OOM kill           10 concurrent requests: fine
```

| Constraint of streaming                                   | Consequence                                                                      |
|-----------------------------------------------------------|----------------------------------------------------------------------------------|
| The database connection stays open for the whole response | A slow client holds a connection. Cap concurrency, set `MinResponseDataRate`     |
| You cannot set a status code after the first byte         | A failure mid-stream produces a truncated 200. Validate before you start writing |
| No `Content-Length`                                       | Chunked encoding; clients cannot show a progress bar                             |
| Buffering proxies may defeat it                           | Check nginx `proxy_buffering` and gateway behaviour                              |
| Retrying is not possible                                  | A failed export must restart from the beginning, or be checkpointed              |

Because of the mid-stream failure problem, very large exports are often better as an async request-reply job writing to blob storage: the client polls, gets a link, and a failure is a clean job status rather than a half-written download.

###### Operators, channels and merging

``` csharp
// System.Linq.Async gives the familiar operators over async sequences
var recent = await ReadAllAsync(path, ct)
    .Where(r => r.Value > threshold)
    .Select(r => r with { Value = Calibrate(r.Value) })
    .Take(1000)
    .ToListAsync(ct);

// batching: the single most useful operator for a streaming pipeline
await foreach (var batch in ReadAllAsync(path, ct).Buffer(500))
    await _db.BulkInsertAsync(batch, ct);        // 500 rows per round trip, not 1

// bounded parallelism over a stream, .NET 6+
await Parallel.ForEachAsync(ReadAllAsync(path, ct),
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (reading, token) => await _enricher.ProcessAsync(reading, token));

// a Channel exposed as a stream: the bridge from push to pull
public IAsyncEnumerable<Event> Subscribe(CancellationToken ct) =>
    _channel.Reader.ReadAllAsync(ct);
```

**Use IAsyncEnumerable when**

- The sequence is large or unbounded
- Items are useful as they arrive
- Memory must stay flat regardless of volume
- You are wrapping a push source (channel, socket, broker)

**Use Task\<IReadOnlyList\<T\>\> when**

- The result is small and bounded
- Callers will enumerate it more than once
- You need `Count` before processing
- The result must be a single atomic snapshot

> **\[Trap\]**
>
> **Common trap.** An `IAsyncEnumerable` returned from a method that owns a `DbContext` or a connection keeps that resource alive until enumeration completes — and if the caller abandons the loop with a `break`, `DisposeAsync` on the enumerator is what releases it. That happens correctly with `await foreach` and not at all if someone captures the enumerator manually. Never let a streaming sequence escape the scope that owns its connection.

### The Machinery

<p class="src">From <em>Backend Engineering</em> · async / await · The Machinery</p>

<a id="be-c319"></a>

#### The compiler does the relocation

``` csharp
public async Task<string> GetUserName(int id)
{
    string sql = "SELECT name FROM users WHERE id = " + id;
    string name = await Database.QueryAsync(sql);
    return name.ToUpper();
}
```

**The CLR has never heard of `await`.** This is not a runtime feature — it's a source-to-source transformation. The compiler hoists every local that must survive into fields of a heap object, adds a bookmark field, and rewrites the body so it can be entered repeatedly and jump to the right place. Ordinary IL, ordinary runtime.

Before `await`, we did this by hand with callbacks — same trick, closures instead of state machines. Three things made it miserable, and `await` fixes each: control flow inverted into nested lambdas (loops and conditionals across a wait point were near-impossible); `try`/`catch` broke, being stack-based while the callback runs on a different stack; and no composition, since every library invented its own callback shape.

<a id="be-c320"></a>

#### The generated code

``` csharp
[AsyncStateMachine(typeof(<GetUserName>d__0))]
public Task<string> GetUserName(int id)
{
    var sm = new <GetUserName>d__0();
    sm.id = id;
    sm.<>t__builder = AsyncTaskMethodBuilder<string>.Create();
    sm.<>1__state = -1;
    sm.<>t__builder.Start(ref sm);   // runs MoveNext SYNCHRONOUSLY on the caller's thread
    return sm.<>t__builder.Task;
}

struct <GetUserName>d__0 : IAsyncStateMachine
{
    public int <>1__state;                        // the bookmark
    public AsyncTaskMethodBuilder<string> <>t__builder;
    public int id;                                // parameter -> field
    private string <sql>5__1;                     // local     -> field
    private string <name>5__2;                    // local     -> field
    private TaskAwaiter<string> <>u__1;           // awaiter   -> field

    public void MoveNext()
    {
        string result;
        try
        {
            TaskAwaiter<string> awaiter;
            if (<>1__state != 0)                       // first entry
            {
                <sql>5__1 = "SELECT name FROM users WHERE id = " + id;
                awaiter = Database.QueryAsync(<sql>5__1).GetAwaiter();
                if (!awaiter.IsCompleted)               // ← FAST PATH check
                {
                    <>1__state = 0;                     // drop the bookmark
                    <>u__1 = awaiter;
                    <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                    return;                             // ← suspension IS a return
                }
            }
            else                                        // resumed entry
            {
                awaiter = <>u__1;
                <>1__state = -1;
            }
            <name>5__2 = awaiter.GetResult();
            result = <name>5__2.ToUpper();
        }
        catch (Exception e)
        {
            <>1__state = -2;
            <>t__builder.SetException(e);   // exception goes INTO the Task
            return;
        }
        <>1__state = -2;
        <>t__builder.SetResult(result);
    }
}
```

`id`, `sql`, `name` are the hoisted locals. `<>1__state` is `nextStep`. `MoveNext` is the resumable entry point, called once per episode.

Your method is no longer a method. It's an object you poke repeatedly, advancing a little each time.

Note the struct: in Release builds the state machine is a struct, boxed onto the heap only if it actually suspends. In Debug it's a class, so Edit-and-Continue works.

<a id="be-c321"></a>

#### Three consequences

**Suspension is just `return`.** The physical frame is destroyed; the thread is released completely. Locals survive only because they became fields. This is why async stack traces are *reconstructed logical chains* rather than real frames, and why in a memory dump you inspect state machine objects, not stacks.

**It starts synchronously.** `Start` calls `MoveNext` on the caller's thread. Marking a method `async` does not make it begin elsewhere. It runs right up to the first await that actually has to wait.

**The fast path costs nothing.** If `IsCompleted` is already `true` — cached value, buffered read, completed `ValueTask` — the machine never suspends. It stays a struct on the stack, no boxing, no delegate, and the builder returns a cached `Task` (`Task.CompletedTask`, cached `true`/`false`, small ints). Zero allocations. Heap boxing happens only at the **first real suspension**.

Corollary: a method whose every await completes synchronously runs start-to-finish on the calling thread. If you're relying on `await` to "get off the request thread," you're relying on nothing.

<a id="be-c322"></a>

#### What a Task is

**Not a thread. Not a unit of work.** A handle to something that will finish, with two jobs: carry the outcome (result or exception), and let interested parties register for notification.

An async method returns it *immediately*, at first suspension, when nothing has been produced. A promise slip, not a result.

Task states: `Created`, `WaitingForActivation`, `Running`, `RanToCompletion`, `Faulted`, `Canceled`. A Task from an async method starts in `WaitingForActivation` — nothing "runs" it; something completes it.

<a id="be-c323"></a>

#### The awaitable pattern

`await x` works if `x` (or an extension method) has `GetAwaiter()` returning something with:

- `bool IsCompleted { get; }`
- `T GetResult()` / `void GetResult()`
- `INotifyCompletion.OnCompleted(Action)`, preferably `ICriticalNotifyCompletion.UnsafeOnCompleted`

No interface, no base class — purely structural, resolved at compile time. That's how `Task`, `ValueTask`, `YieldAwaitable` (`Task.Yield()`), `TaskAwaiter`, and custom awaitables all plug in identically.

You can make anything awaitable:

``` csharp
public static TaskAwaiter GetAwaiter(this TimeSpan delay)
    => Task.Delay(delay).GetAwaiter();

await TimeSpan.FromSeconds(2);   // now legal
```

<a id="be-c324"></a>

#### Task vs Task.Run vs TaskCompletionSource

Three different things people conflate:

**True async I/O** — no thread at all while waiting. `HttpClient.GetAsync`, `SqlCommand.ExecuteReaderAsync`, `FileStream` opened with `useAsync: true`. This is the real win.

**`Task.Run`** — moves *synchronous CPU work* to a pool thread. A thread is consumed for the whole duration. Legitimate for CPU work on a UI app (keeps the UI responsive). On a server it buys nothing — you've just paid a hop to move work from one pool thread to another.

**`TaskCompletionSource<T>`** — a Task you complete manually. The bridge for wrapping callback-based or event-based APIs:

``` csharp
public Task<string> WaitForMessageAsync()
{
    var tcs = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);   // ← important, see IX.4
    _client.MessageReceived += (s, e) => tcs.TrySetResult(e.Message);
    return tcs.Task;
}
```

**Fake async** is the anti-pattern: `Task.Run(() => SyncMethod())` exposed as an async API. It consumes a thread the whole time while *looking* like it doesn't. A library doing this is lying to its callers.

<a id="be-c325"></a>

#### Harder shapes: loops, try/finally, using

The two-state example is the easy case. Seeing what the compiler does with real control flow removes the remaining mystery.

**A loop with an await inside** becomes a state that jumps back:

``` csharp
async Task ProcessAllAsync(List<int> ids)
{
    foreach (var id in ids)
        await ProcessAsync(id);
}
```

The enumerator is hoisted to a field (it must survive every suspension), and `MoveNext` looks roughly like:

``` csharp
case -1:  <>7__wrap1 = ids.GetEnumerator();      // enumerator -> field
          goto LoopCheck;
case 0:   awaiter = <>u__1;                       // resumed
          <>1__state = -1;
          awaiter.GetResult();
          goto LoopCheck;                         // ← back to the top
LoopCheck: if (!<>7__wrap1.MoveNext()) goto Done;
           <id>5__2 = <>7__wrap1.Current;
           awaiter = ProcessAsync(<id>5__2).GetAwaiter();
           if (!awaiter.IsCompleted) { <>1__state = 0; ... return; }
           goto LoopCheck;
```

One state, entered N+1 times. This is why a loop of 10,000 awaits allocates *one* state machine, not 10,000 — though it will suspend and resume 10,000 times, each with a scheduling hop.

**`try`/`finally` across an await** is where it gets interesting. C# guarantees `finally` runs — but `MoveNext` returns at every suspension, and a `return` inside a `try` would normally trigger `finally`. That would be wrong: you're pausing, not leaving.

The compiler solves it by tracking a **disposal mode**. The suspension `return` is emitted *outside* the protected region, and the generated code sets a flag so the exit path knows whether this is a real exit (run `finally`) or a suspension (don't). You'll see `<>s__N` temporaries and jumps that look like spaghetti in a decompiler; that's what they're doing.

Practical consequence: `using` across an await works correctly, but **the disposable object is a field on the heap object for the whole await**. A `SqlConnection` held across a 200 ms external HTTP call ties up a pooled connection for 200 ms of pure waiting — correct code, terrible resource usage. Scope your `using` blocks tightly around the thing that actually needs them.

**`try`/`catch` around an await** requires the awaiter to live *outside* the try block (it's a field anyway), and `GetResult()` is called inside it — which is exactly what makes `catch` work across a suspension despite the original stack being gone.

**`await` inside a `lock`** doesn't compile, and now you can see the real reason: `Monitor` is thread-affine, entry and exit must be on the same thread, and the whole point of suspension is that resumption might be on a different one.

**`await` in a `catch` or `finally` block** is legal since C# 6, and generates additional states plus `ExceptionDispatchInfo` juggling to preserve the in-flight exception across the suspension.

<a id="be-c326"></a>

#### Who actually calls MoveNext — the full chain

Tracing the wiring end to end, because "it registers a callback" hides four moving parts.

**1. The builder boxes the state machine.**

``` csharp
<>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
```

On the first suspension, `AsyncTaskMethodBuilder` allocates an `AsyncStateMachineBox<TStateMachine>` on the heap and copies the struct into it. That box *is* the `Task` — a clever optimization: one allocation serves as both the state holder and the Task object, rather than two.

**2. It builds the continuation delegate.** The box implements `IThreadPoolWorkItem`, and its `MoveNext` is what gets invoked. `ExecutionContext` is captured here (so `AsyncLocal` values flow), independent of `ConfigureAwait`.

**3. The awaiter registers it.** `TaskAwaiter.UnsafeOnCompleted` hands the delegate to the awaited Task, which stores it in its `m_continuationObject` field. A Task with one continuation stores it directly; with several, it allocates a list.

`OnCompleted` vs `UnsafeOnCompleted`: the "unsafe" version skips capturing `ExecutionContext` a *second* time, because the builder already did. That's the only difference. Custom awaiters should implement `ICriticalNotifyCompletion` to get this.

**4. Completion fires it.** When the underlying operation finishes — an IOCP callback, a timer, `SetResult` on a `TaskCompletionSource` — the Task transitions to completed and walks its continuation list. For each one it decides: **run inline on this thread, or queue to the pool?**

That decision is where `ConfigureAwait`, `SynchronizationContext`, `TaskScheduler`, and `RunContinuationsAsynchronously` all interact. Then `MoveNext` runs, reads `<>1__state`, and jumps to the right label.

Two things worth taking from this:

- **`MoveNext` is called by whoever completed the operation.** Not by a scheduler, not by a loop. There is no async runtime polling anything. It's a direct call, on some thread you didn't choose.
- **The Task and the state machine are the same object** in the common case. When you see `AsyncStateMachineBox` in a heap dump, you're looking at both.

<a id="be-c327"></a>

#### Chained async methods and the "async all the way" cost

``` csharp
async Task<A> Level1() => await Level2();
async Task<B> Level2() => await Level3();
async Task<C> Level3() => await IoAsync();
```

Each level is its own state machine with its own box and its own Task. One real suspension deep in `Level3` means **three** boxes allocated and **three** `MoveNext` calls on resumption, cascading upward — `IoAsync` completes → `Level3.MoveNext` → sets its Task's result → `Level2.MoveNext` → and so on.

Two practical notes.

If a level does nothing but await and return, drop the `async`:

``` csharp
Task<A> Level1() => Level2();   // elision — no state machine, no box, no Task
```

This is **elision**, and it's free performance. The caveats: you lose the exception-wrapping (exceptions from the synchronous part now throw synchronously), you can't use `using`/`try` around it, and stack traces lose that frame — which is why some teams ban it for debuggability. Fair trade either way; know you're making it.

Second: this cascade is why deep async call chains show up in profiles as allocation. It's not any single await being expensive. It's fifteen layers each paying ~120 bytes.

------------------------------------------------------------------------

### One Request, End to End

<p class="src">From <em>Backend Engineering</em> · async / await · One Request, End to End</p>

<a id="be-c328"></a>

#### One Request, End to End

Everything above, as a single timeline. ASP.NET Core, one request, one database call. Times are illustrative; the ordering is real.

``` csharp
[HttpGet("/user/{id}")]
public async Task<string> Get(int id)
{
    var name = await _db.QueryAsync(id);   // 50ms
    return name.ToUpper();
}
```

**t = 0 µs — request arrives.** An IOCP thread picks up the socket read completion for the incoming HTTP bytes. Kestrel parses the request, routes it, and calls your `Get(42)`. Note: **this is already a continuation** — Kestrel's own state machine resumed on this thread. Your controller never runs on a "request thread" that existed before the request.

**t = 1 µs — the state machine is created.** `Get` is not the method you wrote. The generated stub allocates `<Get>d__0` **as a struct on the stack**, stores `id = 42` in a field, creates the builder, and calls `Start(ref sm)` — which calls `MoveNext` **synchronously, on this same IOCP thread**.

**t = 2 µs — `MoveNext` runs, first entry.** `<>1__state` is `-1`, so it takes the first branch. It calls `_db.QueryAsync(42)`, which itself is an async method that runs synchronously until *its* first real await — building the command, writing bytes to the socket. That write is a syscall; the kernel records "socket 42, notify on reply" and returns.

`QueryAsync` returns an incomplete `Task<string>`. Back in your `MoveNext`, `awaiter.IsCompleted` is **false**.

**t = 3 µs — suspension.**

- `<>1__state = 0` — the bookmark.
- `AwaitUnsafeOnCompleted` boxes the struct onto the heap as an `AsyncStateMachineBox`. That box is also your Task.
- `ExecutionContext` is captured (so any `AsyncLocal` — correlation ID, `IHttpContextAccessor` — will flow).
- The box is registered as `QueryAsync`'s task's continuation.
- **`MoveNext` returns.**
- The stub returns the Task to Kestrel, which also suspends, also returns.

**The IOCP thread's stack is now completely unwound.** It goes back to the pool and picks up the next request. Elapsed thread time: about 3 microseconds.

**t = 3 µs to 50 ms — nothing.** Zero threads involved. Your entire request state is one heap object of ~150 bytes, reachable only from the kernel's IOCP registration and the database driver's pending-operation table. The CPU is serving other requests. If you took a dump right now, `dumpasync` would find your state machine; `clrstack` on every thread would show no trace of your request whatsoever.

**t = 50 ms — the reply arrives.** The NIC raises a hardware interrupt. The driver's handler matches the packet to socket 42 and posts a completion packet to the IOCP port.

**t = 50 ms + 5 µs — a completion thread picks it up.** Some pool thread — call it \#12, almost certainly not \#7 — dequeues the completion. The database driver parses the response and calls `SetResult("nam")` on `QueryAsync`'s internal `TaskCompletionSource`.

**t = 50 ms + 6 µs — the continuation cascade.** That Task walks its continuation list and finds your box. Decision point: inline or queue? On ASP.NET Core there's no `SynchronizationContext`, so the default is **inline on thread \#12**. `ExecutionContext` is restored first, so `AsyncLocal` values reappear.

`MoveNext` is called. `<>1__state == 0`, so it jumps to the resume label, pulls the awaiter from `<>u__1`, calls `GetResult()` → `"nam"`. Runs `.ToUpper()`. Calls `SetResult("NAM")` on its own builder.

That completes *your* Task, which fires *Kestrel's* continuation, which writes the response.

**Total: 50 ms of latency, ~10 µs of thread occupancy, on two different threads.**

Now change one line:

``` csharp
var name = _db.QueryAsync(id).Result;   // ← blocking
```

**t = 2 µs.** Same until here. Then `.Result` sees an incomplete Task and calls into `Monitor.Wait`. Syscall, context switch, thread \#7 enters the **Blocked** state and is removed from the scheduler's consideration entirely.

**t = 2 µs to 50 ms.** Thread \#7 sits there. Zero CPU. One megabyte of reserved stack, a scheduler entry, and a pool slot — occupied for the sole purpose of remembering `id = 42`.

At 1,000 requests/second with a 50 ms call, Little's Law gives you 50 concurrent in-flight requests, so you need 50 blocked threads steadily. The pool starts at `ProcessorCount` and injects **one to two per second**. On an 8-core box you'll be short ~42 threads and will take roughly 30 seconds to climb out — during which every queued request waits behind the deficit. Latency climbs from 50 ms into the seconds. CPU stays at 3%.

Add a `SynchronizationContext` — classic ASP.NET, the same code — and it doesn't degrade at all. It hangs, immediately and permanently, because the continuation that would complete the Task is `Post`ed to the context that thread \#7 is holding while it waits for that very Task.

**Same one-line change. Three completely different failure modes depending on the platform.** That's why the mechanism is worth knowing rather than the rule.

------------------------------------------------------------------------

### Scheduling and Context

<p class="src">From <em>Backend Engineering</em> · async / await · Scheduling and Context</p>

<p class="pd">This is the part that produces real production bugs.</p>

<a id="be-c329"></a>

#### Where the continuation runs

Once the state machine registers as a callback, something pokes it: an IOCP/epoll completion, a timer, another task finishing. On which thread?

`TaskAwaiter.OnCompleted` captures, in order:

1.  `SynchronizationContext.Current` if non-null → the continuation is `Post`ed there.
2.  Otherwise `TaskScheduler.Current` if not `Default`.
3.  Otherwise → thread pool, **or inlined on whichever thread called `SetResult`**.

`ConfigureAwait(false)` skips 1–2.

<a id="be-c330"></a>

#### The deadlock

``` csharp
public ActionResult Index()
{
    var data = GetUserName(42).Result;   // hangs forever
}
```

`AspNetSynchronizationContext` (and the UI thread, equivalently) permits one thread at a time in the logical context. `.Result` blocks the request thread while holding it. The continuation is `Post`ed to that same context and can never run. The block waits for the continuation; the continuation waits for the block.

This is **deterministic**, not flaky. Same for `.Wait()`, `.GetAwaiter().GetResult()`, WinForms, WPF.

Under ASP.NET Core there's no `SynchronizationContext`, so this specific deadlock disappears — but sync-over-async still burns a thread and still starves the pool under load. Removing the deadlock does not make it safe.

<a id="be-c331"></a>

#### The lost context

On classic ASP.NET, that same `SynchronizationContext` is what restores `HttpContext.Current` on resumption. Add `ConfigureAwait(false)` and:

``` csharp
var data = await FetchAsync().ConfigureAwait(false);
var user = HttpContext.Current.User;   // NullReferenceException
```

Half-migrated code where someone sprinkled `ConfigureAwait(false)` in "for performance" produces NREs that look nondeterministic and aren't.

<a id="be-c332"></a>

#### The distinction people always conflate

**Where you resume** and **what ambient state travels with you** are different mechanisms.

- `SynchronizationContext` → *which thread* runs your continuation. Controlled by `ConfigureAwait`.
- `ExecutionContext` → *ambient values* that flow with you: `AsyncLocal<T>`, impersonation, `CallContext`, security context. Flowed by the builder **regardless of `ConfigureAwait`.**

`ConfigureAwait(false)` changes the thread you land on. It never turns off ambient-value flow.

(You *can* suppress `ExecutionContext` flow with `ExecutionContext.SuppressFlow()`, but that's a rare, deliberate act — usually for fire-and-forget background work that shouldn't inherit request-scoped ambient state.)

<a id="be-c333"></a>

#### AsyncLocal — the correct tool, with a trap

`AsyncLocal<T>` gives you per-logical-flow state that survives awaits. It's how correlation IDs, tenant context, and activity tracing work.

The trap: **it's copy-on-write, propagating downward only.**

``` csharp
static AsyncLocal<string> _tenant = new();

async Task Outer()
{
    _tenant.Value = "acme";
    await Inner();
    Console.WriteLine(_tenant.Value);   // "acme" — Inner's change did NOT propagate up
}

async Task Inner()
{
    _tenant.Value = "globex";           // affects Inner and its children only
}
```

Set it in the parent before spawning children. Mutating a *reference type's contents* held in an AsyncLocal does propagate everywhere (shared object), which is a different and often surprising behavior.

<a id="be-c334"></a>

#### ConfigureAwait — the actual rule

| Context                          | Recommendation                                             |
|----------------------------------|------------------------------------------------------------|
| Library code, any platform       | `ConfigureAwait(false)` everywhere                         |
| ASP.NET Core app code            | Unnecessary — no SynchronizationContext exists             |
| Classic ASP.NET app code         | Omit it if you touch `HttpContext.Current` after the await |
| UI app code (WinForms/WPF/MAUI)  | Omit it if you touch controls after the await              |
| UI app internals not touching UI | `ConfigureAwait(false)`                                    |

Note it's per-await, not per-method. `ConfigureAwait(false)` on one await doesn't affect subsequent ones — though in practice once you've left the context you tend not to get back onto it, so a single omission in a chain is the common bug.

.NET 8 added `ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)` and `.ForceYielding` for finer control; `ContinueOnCapturedContext` is the flag corresponding to the old boolean.

<a id="be-c335"></a>

#### The deadlock taxonomy

The classic sync-over-async hang is one of six. Recognizing the others saves days.

**1. Sync-over-async under a context.** `.Result` on the UI thread or classic ASP.NET. Covered in III.2. Deterministic.

**2. `SemaphoreSlim` self-reentrancy.** Unlike `lock`, it is not reentrant:

``` csharp
await _mutex.WaitAsync();
try { await HelperAsync(); }   // Helper also does _mutex.WaitAsync() → hangs on itself
finally { _mutex.Release(); }
```

Fix: never call outward from inside a held async lock, or use a reentrancy-aware wrapper keyed on an `AsyncLocal` token.

**3. Missing `Release` on the exception path.** `await _mutex.WaitAsync()` without `try/finally` — one exception and the semaphore is permanently down a count. Manifests as a hang *later*, after the request that broke it is long gone. Always `finally`.

**4. Deadlock through the thread pool.** No lock involved at all:

``` csharp
var tasks = items.Select(i => Task.Run(() => Blocking(i)));   // 100 items
await Task.WhenAll(tasks);
```

If those blocking calls themselves wait on pool-scheduled work, and every pool thread is consumed by the outer items, nothing can make progress. The pool's slow injection means it *eventually* crawls out — or doesn't, if the count exceeds `MaxThreads`. Classic in nested `Parallel` + async code.

**5. Circular task dependencies.** Task A awaits B; B awaits A. Rare in straight-line code, common with lazily-initialized singletons that resolve each other, or DI graphs doing async init.

**6. Continuation starvation via inlining.** A continuation runs inline on the completing thread while holding a lock the completer needs next. Subtle, load-dependent, and the reason `TaskCreationOptions.RunContinuationsAsynchronously` exists.

**Diagnosing any of them:** take a dump under the hang. `clrstack` on all threads shows who's *blocked* (case 1–4 will show `Monitor.Wait`, `SemaphoreSlim.Wait`, or `WaitHandle`); `dumpasync` shows who's *suspended* (cases 5–6). A hang where `clrstack` shows nothing interesting and `dumpasync` shows a lot is a logical dependency problem, not a blocking one.

**The structural fix for all six:** don't block, don't hold a lock across an outward call, and give the pool no chance to be the bottleneck.

<a id="be-c336"></a>

#### Migration relevance

ASP.NET Core has **no `SynchronizationContext` at all** — continuations always go to the thread pool. Porting from Framework:

- Deadlocks from `.Result` disappear (but the thread waste doesn't).
- Context restoration you were silently depending on also disappears.
- `HttpContext.Current` doesn't exist; it's `IHttpContextAccessor`, which is backed by `AsyncLocal<T>` and therefore *does* flow across awaits regardless of `ConfigureAwait`.

Behavior changes in both directions. Neither direction is announced by the compiler.

------------------------------------------------------------------------

### Exceptions

<p class="src">From <em>Backend Engineering</em> · async / await · Exceptions</p>

<a id="be-c337"></a>

#### Why the mechanism has to change

`throw` walks up the call stack looking for a handler. But when resumed code fails, **the stack that called you no longer exists** — it was dismantled at suspension. There's nothing above to unwind into.

So the generated code catches it and calls `SetException`, storing it in the Task. It travels through the task graph, not the stack. When someone awaits, `GetResult()` rethrows via `ExceptionDispatchInfo.Throw` on *their* stack, at *their* await point, original trace preserved and stitched with `--- End of stack trace from previous location ---`.

<a id="be-c338"></a>

#### Everything that follows

**A `try`/`catch` around an un-awaited call catches nothing.**

``` csharp
try { DoWorkAsync(); }        // no await
catch (Exception) { }         // never fires — the exception went into a dropped Task
```

**Even pre-await exceptions go into the Task.**

``` csharp
async Task Validate(string s)
{
    if (s == null) throw new ArgumentNullException();   // NOT thrown synchronously
    await Something();
}
```

For argument validation you want thrown at call time, split it:

``` csharp
public Task ValidateAsync(string s)
{
    if (s is null) throw new ArgumentNullException(nameof(s));   // synchronous
    return ValidateCore(s);
    static async Task ValidateCore(string s) { await Something(); }
}
```

**`await` throws the first exception. `.Wait()`/`.Result` throw `AggregateException`.** Same failure, two shapes. To see all of them:

``` csharp
try { await Task.WhenAll(tasks); }
catch (Exception)
{
    var all = Task.WhenAll(tasks).Exception;   // AggregateException with everything
}
```

`WhenAll` genuinely can fail multiple ways; `await` shows you only the first. Inspect `.Exception` when you need the full picture.

**`async void` kills the process.** `AsyncVoidMethodBuilder` has no Task to store into, so it rethrows on the captured `SynchronizationContext` or, failing that, a pool thread — unhandled → **process termination**. No way to await it, no way to catch it from outside. Event handlers only, and wrap the body in try/catch.

**`async` lambdas passed to `Action`-typed parameters silently become `async void`.**

``` csharp
list.ForEach(async x => await ProcessAsync(x));   // async void ×N, unobservable
timer.Elapsed += async (s, e) => await DoAsync(); // async void
```

Check the delegate type. `Func<Task>` is fine; `Action` is not.

<a id="be-c339"></a>

#### Unobserved exceptions

A faulted Task nobody awaits used to crash the process on finalization (.NET 4.0). Since 4.5 it's swallowed silently — arguably worse. Hook it in production:

``` csharp
TaskScheduler.UnobservedTaskException += (s, e) =>
{
    logger.LogError(e.Exception, "Unobserved task exception");
    e.SetObserved();
};
```

Note this fires at GC time, so it's a late, low-fidelity signal. It's a canary, not a strategy.

<a id="be-c340"></a>

#### Fire-and-forget, done properly

If you truly want to not await something:

``` csharp
_ = Task.Run(async () =>
{
    try { await BackgroundWorkAsync(ct); }
    catch (OperationCanceledException) { }
    catch (Exception ex) { logger.LogError(ex, "Background work failed"); }
});
```

Better on ASP.NET Core: use `IHostedService` / `BackgroundService`, or a queue with `IHostApplicationLifetime` awareness. Raw fire-and-forget in a web request is work the host doesn't know about, so shutdown will cut it off mid-flight.

------------------------------------------------------------------------

### Cancellation

<p class="src">From <em>Backend Engineering</em> · async / await · Cancellation</p>

<a id="be-c341"></a>

#### It's cooperative, always

Nothing forcibly stops a running task. `CancellationToken` is a signal; code must check it. There is no "kill this task."

``` csharp
public async Task<Data> FetchAsync(CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();                  // check at entry
    var raw = await _http.GetAsync(url, ct);            // pass down
    foreach (var item in raw.Items)
    {
        ct.ThrowIfCancellationRequested();              // check in loops
        Process(item);
    }
}
```

Rules: accept a token in every async public method; pass it to every call that accepts one; check it in loops and between long steps. A token you accept and ignore is worse than not accepting one, because callers assume it works.

<a id="be-c342"></a>

#### Sources, linking, timeouts

``` csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    requestAborted, cts.Token, _shutdownToken);

await DoWorkAsync(linked.Token);   // cancels if ANY source fires
```

.NET 8 added `CancellationTokenSource.CancelAsync()` — the sync `Cancel()` runs callbacks inline, which can deadlock if a callback needs the calling thread.

`WaitAsync` (.NET 6+) applies a timeout to any task without touching its implementation:

``` csharp
await someTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
```

Note: this abandons the wait, it does **not** stop the underlying work. The task keeps running.

<a id="be-c343"></a>

#### Cancellation is not failure

``` csharp
try { await FetchAsync(ct); }
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // expected — user navigated away, request aborted, shutdown
}
catch (Exception ex)
{
    logger.LogError(ex, "Real failure");
}
```

The `when (ct.IsCancellationRequested)` filter matters. An `OperationCanceledException` from a *different* token — an internal timeout, say — is a genuine failure from your caller's perspective, and swallowing it hides real timeouts.

`TaskCanceledException` derives from `OperationCanceledException`; catch the base.

In ASP.NET Core, `HttpContext.RequestAborted` fires when the client disconnects. Honoring it is free throughput — you stop doing work nobody will read.

<a id="be-c344"></a>

#### Registration and cleanup

``` csharp
using var reg = ct.Register(() => socket.Close());   // dispose to avoid leaks
```

Callbacks on a long-lived token accumulate if you never unregister. This is a real leak source in long-running services holding an application-lifetime token.

<a id="be-c345"></a>

#### Timeouts, layered correctly

Timeouts compound in ways nobody plans, and the usual result is that your outermost timeout is meaningless.

A single HTTP call through a resilience pipeline may have: a connect timeout, a per-attempt timeout, a retry policy (×3), an overall pipeline timeout, `HttpClient.Timeout`, your own `CancellationTokenSource`, the server's own timeout, and a load balancer idle timeout. If per-attempt is 10 s and you retry 3 times, your "10 second timeout" is 30+ seconds. If `HttpClient.Timeout` is 100 s and your CTS is 5 s, the CTS wins — but the connection may not be released until the former fires.

The discipline:

``` text
total budget  >  (per-attempt × attempts) + backoff  >  per-attempt  >  connect
```

State the budget once, at the top, and derive downward:

``` csharp
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    budget.Token, httpContext.RequestAborted, _lifetime.ApplicationStopping);

await _client.GetAsync(url, linked.Token);
```

Three sources, one token, and the operation dies on whichever fires first: your budget, the client hanging up, or the process shutting down. All three are things you want to stop for.

**`WaitAsync` abandons, it doesn't cancel:**

``` csharp
await someTask.WaitAsync(TimeSpan.FromSeconds(5));   // you stop waiting
```

The underlying work keeps running, keeps holding its connection, and will eventually complete into a Task nobody observes. Use it only when the operation is genuinely fire-and-forgettable, or pair it with a real token.

**Deadline propagation.** In a call chain A→B→C, the naive approach gives each hop a fresh 10 s timeout, so C can be working long after A gave up. Pass the *remaining* budget instead — a deadline rather than a duration. gRPC does this natively; over HTTP you'd send a `grpc-timeout`-style header or an absolute deadline. Without it, a slow dependency causes work amplification exactly when you're already overloaded.

------------------------------------------------------------------------

### Concurrency Patterns

<p class="src">From <em>Backend Engineering</em> · async / await · Concurrency Patterns</p>

<a id="be-c346"></a>

#### Sequential vs concurrent

``` csharp
// 300ms total — sequential, usually a bug
var a = await GetAAsync();
var b = await GetBAsync();
var c = await GetCAsync();

// 100ms total — concurrent
var ta = GetAAsync();
var tb = GetBAsync();
var tc = GetCAsync();
await Task.WhenAll(ta, tb, tc);
var (a, b, c) = (ta.Result, tb.Result, tc.Result);   // safe: already complete
```

Start them all, then await. `.Result` on an already-completed task is fine and won't deadlock — but `await ta` reads better and is harder to get wrong later.

<a id="be-c347"></a>

#### WhenAll, WhenAny, WhenEach

``` csharp
var results = await Task.WhenAll(tasks);            // all, or aggregate failure

var first = await Task.WhenAny(tasks);              // first to complete (incl. fail)
var winner = await first;                           // unwrap

await foreach (var t in Task.WhenEach(tasks))       // .NET 9: results as they land
    Handle(await t);
```

`WhenAny` gotcha: the losers keep running. If they can fail, you've created unobserved exceptions. Either cancel them, or attach a continuation to observe.

`WhenAll` gotcha: **it does not throttle.** `Task.WhenAll(tenThousandUrls.Select(FetchAsync))` opens ten thousand concurrent requests, exhausts connection pools, and gets you rate-limited. See next section.

<a id="be-c348"></a>

#### Throttling — the thing everyone skips

**`SemaphoreSlim` — classic:**

``` csharp
var gate = new SemaphoreSlim(10);
var tasks = urls.Select(async url =>
{
    await gate.WaitAsync(ct);
    try { return await FetchAsync(url, ct); }
    finally { gate.Release(); }
});
var results = await Task.WhenAll(tasks);
```

Use `WaitAsync`, never `Wait()` — the sync version blocks a thread, reintroducing the original problem.

**`Parallel.ForEachAsync` (.NET 6+) — usually better:**

``` csharp
await Parallel.ForEachAsync(urls,
    new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
    async (url, token) => await ProcessAsync(url, token));
```

Do **not** use `Parallel.ForEach` (no Async) with async bodies. It takes an `Action`, so your body becomes `async void`, the loop returns immediately, and exceptions kill the process.

**Rate limiting (.NET 7+), for requests-per-window rather than concurrency:**

``` csharp
var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 100, TokensPerPeriod = 10,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 50
});
using var lease = await limiter.AcquireAsync(1, ct);
if (lease.IsAcquired) await CallApiAsync(ct);
```

<a id="be-c349"></a>

#### Channels — producer/consumer with backpressure

``` csharp
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,   // ← backpressure
    SingleReader = false, SingleWriter = false
});

// Producer — blocks (asynchronously) when full
await channel.Writer.WriteAsync(item, ct);
channel.Writer.Complete();

// Consumer
await foreach (var item in channel.Reader.ReadAllAsync(ct))
    await ProcessAsync(item, ct);
```

**Bounded, always.** `CreateUnbounded` means a producer faster than its consumer grows the queue until OOM. `FullMode.Wait` is what makes the producer feel the consumer's pain — that's backpressure, and it's the difference between a system that degrades and one that falls over.

`BlockingCollection<T>` is the old synchronous equivalent. Don't use it in async code — `Take()` blocks a thread.

<a id="be-c350"></a>

#### Async coordination primitives

`lock`/`Monitor` **cannot** be held across an await — it's thread-affine and the compiler rejects `await` inside a `lock` body. Use:

- `SemaphoreSlim(1,1)` with `WaitAsync` — an async mutex
- `AsyncLock` from AsyncEx or your own wrapper
- `Nito.AsyncEx` for `AsyncManualResetEvent`, `AsyncCountdownEvent`, etc.

.NET 9 added `System.Threading.Lock`, which is a better *synchronous* lock — it still can't span an await.

``` csharp
private readonly SemaphoreSlim _mutex = new(1, 1);

async Task CriticalAsync()
{
    await _mutex.WaitAsync(ct);
    try { await DoAsync(); }
    finally { _mutex.Release(); }
}
```

Beware: unlike `lock`, this is **not reentrant**. A recursive call deadlocks against itself.

<a id="be-c351"></a>

#### Retry and resilience

Don't hand-roll. `Microsoft.Extensions.Http.Resilience` (Polly v8) via `HttpClientFactory`:

``` text
builder.Services.AddHttpClient<MyClient>()
    .AddStandardResilienceHandler();   // retry + circuit breaker + timeout + hedging
```

Hand-rolled retry loops usually lack jitter (causing thundering herds), retry non-idempotent operations, and multiply timeouts unexpectedly. If you must, exponential backoff with jitter and a cap.

------------------------------------------------------------------------

### Streaming: IAsyncEnumerable

<p class="src">From <em>Backend Engineering</em> · async / await · Streaming: IAsyncEnumerable</p>

<a id="be-c352"></a>

#### Streaming: IAsyncEnumerable

For results arriving over time, `Task<List<T>>` forces you to buffer everything before anyone sees anything.

``` csharp
public async IAsyncEnumerable<Row> ReadRowsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await using var conn = new SqlConnection(_cs);
    await conn.OpenAsync(ct);
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
        yield return Map(reader);
}

await foreach (var row in ReadRowsAsync(ct))
    Process(row);
```

Notes:

- **`[EnumeratorCancellation]`** is required for the token to reach the generator when consumers use `WithCancellation`. Without it, your token is silently ignored — a very common bug.
- `.ConfigureAwait(false)` on the `await foreach` itself: `await foreach (var x in src.ConfigureAwait(false))`.
- Constant memory regardless of result count. This is how you stream a million-row export without a gigabyte of `List<T>`.
- In ASP.NET Core, returning `IAsyncEnumerable<T>` from a controller streams the JSON response.
- Combine with channels for fan-out, or `System.Linq.Async` for LINQ operators.

Cost: one `MoveNextAsync` state machine transition per element. For tight in-memory loops over already-materialized data this is pure overhead — use it for genuinely incremental sources.

------------------------------------------------------------------------

### Async in Awkward Places

<p class="src">From <em>Backend Engineering</em> · async / await · Async in Awkward Places</p>

<a id="be-c353"></a>

#### Constructors

Constructors can't be async. Options, in order of preference:

``` csharp
// Static factory — usually right
public class Service
{
    private Service(Config c) { _c = c; }
    public static async Task<Service> CreateAsync(CancellationToken ct)
    {
        var c = await LoadConfigAsync(ct);
        return new Service(c);
    }
}

// Lazy async initialization — for DI-constructed singletons
private readonly AsyncLazy<Config> _config = new(() => LoadConfigAsync());
```

Do not do async work in a constructor via `.Result`. That's the deadlock, in the place hardest to debug.

For hosted services, `IHostedService.StartAsync` is the designed hook.

<a id="be-c354"></a>

#### Disposal

``` csharp
public class Connection : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await _stream.FlushAsync();
        await _socket.CloseAsync();
    }
}

await using var conn = new Connection();
```

Implement both `IDisposable` and `IAsyncDisposable` if consumers may use either. `await using` calls `DisposeAsync`; plain `using` calls `Dispose`. DI containers dispose async services correctly only if the scope is disposed with `await using` — `IServiceScope` vs `AsyncServiceScope` matters.

<a id="be-c355"></a>

#### Properties, indexers, events

No async properties. Use a method: `GetValueAsync()`. A property that does I/O is a design mistake anyway — property access should look cheap because callers assume it is.

Events: `EventHandler` returns void, so async handlers are `async void`. For anything where you need to await handlers, use `Func<T, Task>` and invoke via `WhenAll` over the invocation list.

<a id="be-c356"></a>

#### Interfaces and inheritance

`async` is an implementation detail, never part of a signature. Interfaces declare `Task<T> DoAsync()`; implementations may or may not be `async`. An implementation that has nothing to await should just return:

``` csharp
public Task<int> GetAsync() => Task.FromResult(42);   // no state machine at all
```

Adding `async` there is pure overhead — you'd allocate a state machine to wrap a value you already have.

<a id="be-c357"></a>

#### LINQ

`Where(async x => await Check(x))` doesn't compile the way you'd hope — the lambda returns `Task<bool>`, which isn't `bool`. Options:

``` csharp
// Evaluate first
var checks = await Task.WhenAll(items.Select(async x => (x, ok: await CheckAsync(x))));
var kept = checks.Where(t => t.ok).Select(t => t.x);

// Or System.Linq.Async
var kept = await items.ToAsyncEnumerable()
    .WhereAwait(async x => await CheckAsync(x))
    .ToListAsync();
```

Also watch deferred execution: a lazy LINQ chain over an async source can start enumerating after your scope disposed the connection.

------------------------------------------------------------------------

### Performance

<p class="src">From <em>Backend Engineering</em> · async / await · Performance</p>

<a id="be-c358"></a>

#### The real costs

Per async method that actually suspends: one heap allocation for the boxed state machine, one for the Task (unless cached), plus a continuation delegate. Roughly 100–200 bytes and a scheduling hop.

That's cheap compared to a millisecond of I/O and expensive compared to a nanosecond of arithmetic. The rule follows directly: **async is for operations where the wait dominates.** Don't `await` your way through a tight computational loop.

<a id="be-c359"></a>

#### ValueTask

`ValueTask<T>` avoids the Task allocation when the operation frequently completes synchronously — buffered reads, cache hits.

**The rules are strict:**

- Await it **exactly once**. Never twice.
- Never `.Result` it (unless `IsCompletedSuccessfully` is true).
- Never store it, cache it, or pass it around.
- Never `WhenAll` a collection of them — call `.AsTask()` first.

`IValueTaskSource` implementations **recycle their backing object**. A second await reads a *different operation's* result. The corruption is silent, nondeterministic, and produces bugs that look supernatural.

Use `ValueTask` when: you're writing a hot-path library method that usually completes synchronously, and you've measured. Otherwise use `Task` — it's forgiving, and forgiving is worth more than a hundred bytes in application code.

<a id="be-c360"></a>

#### Pooling

``` text
[MethodImpl(MethodImplOptions.AggressiveInlining)]
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
```

Or globally: `<TieredPGO>` and `DOTNET_TieredPGO`, plus `System.Threading.Tasks.ValueTaskSourceOnCompletedFlags`. Realistically: measure before reaching for any of this. The default allocator handles gen0 churn well, and premature ValueTask pooling has caused more bugs than it has saved milliseconds.

<a id="be-c361"></a>

#### Continuation inlining

When a task completes, its continuation may run **inline on the completing thread** rather than being queued. That's usually good (no hop), occasionally catastrophic: if your continuation does 200ms of work, you've just stalled an I/O completion thread or another library's internals.

`TaskCompletionSource` defaults to inlining. Almost always pass `TaskCreationOptions.RunContinuationsAsynchronously` — this is why. Without it, `tcs.SetResult()` runs arbitrary caller code on your thread, inside whatever lock you might be holding.

<a id="be-c362"></a>

#### When not to use async

- CPU-bound work with no waiting. Nothing to release.
- Simple in-process computation.
- Low-traffic internal tools where sync is simpler and adequate.
- Anywhere `Task.Run` is being used to "make it async" — that's just a thread hop.

The win requires that in-flight count vastly exceeds core count. That's the public-facing web server case, the gateway case, the high-fanout aggregator case. It's not every case.

------------------------------------------------------------------------

### Real-World Integration

<p class="src">From <em>Backend Engineering</em> · async / await · Real-World Integration</p>

<p class="pd">Where async meets the things it actually talks to. Most production async bugs live here rather than in the language.</p>

<a id="be-c363"></a>

#### HttpClient

Two failure modes, and they're opposites, which is why people keep hitting one while avoiding the other.

**Socket exhaustion** — `new HttpClient()` per request. Disposing it doesn't release the socket; TCP holds it in `TIME_WAIT` for up to 240 seconds. Under load you exhaust ephemeral ports and get `SocketException: Only one usage of each socket address...`. The symptom appears minutes after traffic starts, and often on a *different* service on the same box.

**DNS staleness** — the naive fix, a `static readonly HttpClient`, never re-resolves DNS. Point a hostname at a new IP and your service keeps hitting the old one indefinitely. Brutal during failover, which is exactly when you need it not to be.

The correct answer handles both:

``` csharp
services.AddHttpClient<MyClient>(c => c.BaseAddress = new Uri("https://api.example.com"))
    .SetHandlerLifetime(TimeSpan.FromMinutes(2))     // rotates handlers → picks up DNS
    .AddStandardResilienceHandler();
```

Or, without DI, a single `SocketsHttpHandler` with `PooledConnectionLifetime` set.

**Streaming large responses.** `GetAsync` buffers the entire body into memory by default:

``` csharp
using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
using var stream = await resp.Content.ReadAsStreamAsync(ct);
await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
```

`ResponseHeadersRead` returns as soon as headers arrive. For a 200 MB download this is the difference between constant memory and an LOH allocation that lands straight in gen 2.

**`HttpClient.Timeout` applies to the whole operation** including body read. With `ResponseHeadersRead` and a slow consumer, you can time out mid-stream on a healthy connection.

<a id="be-c364"></a>

#### Databases and sessions

**The single most common async database bug:** `DbContext` is not thread-safe. Neither is RavenDB's `IAsyncDocumentSession`, nor most ORM session objects. One operation at a time, period.

``` csharp
// BROKEN — concurrent use of one context
var tasks = ids.Select(id => _db.Users.FirstOrDefaultAsync(u => u.Id == id));
await Task.WhenAll(tasks);   // "A second operation was started on this context"
```

This is a real concurrency bug, not a framework annoyance: the context has mutable change-tracking state that concurrent operations corrupt. Either sequence the calls, or create a context per parallel branch via `IDbContextFactory<T>`:

``` csharp
await Parallel.ForEachAsync(ids, async (id, ct) =>
{
    await using var db = await _factory.CreateDbContextAsync(ct);
    await db.Users.FindAsync([id], ct);
});
```

**Connection lifetime is the resource that actually runs out.** A connection held across an unrelated await is a pooled connection removed from circulation for the duration:

``` csharp
// BAD
using var conn = await OpenAsync();
var data = await conn.QueryAsync(...);          // needs the connection
var enriched = await _http.GetAsync(url);       // does NOT — 200ms of holding it hostage
await conn.ExecuteAsync(...);
```

Default pool size is 100. At 50 req/s with a 200 ms external call inside the connection scope, you're at the limit. Split the scopes.

**Transactions across awaits** work, but the transaction holds database locks for the entire wall-clock duration of every await inside it. An HTTP call inside a transaction is a lock held across the internet. Never do it.

**Cancellation and transactions:** cancelling mid-transaction leaves the connection in an indeterminate state. Most providers handle rollback, but the connection is typically discarded from the pool rather than reused. High cancellation rates thus quietly degrade pool health.

**Redis (StackExchange):** multiplexes everything over one connection, so a synchronous call under load blocks a thread while the multiplexer keeps working — and the resulting `RedisTimeoutException` reports *thread pool statistics in its message*. When you see one, read those numbers before blaming Redis; it's usually starvation elsewhere in your process. The caching reference, Part V.4, covers the Redis side.

<a id="be-c365"></a>

#### Graceful shutdown

Containers and orchestrators send SIGTERM and then kill you — typically 30 seconds later in Kubernetes. Work in flight when the process dies is work that silently vanished.

``` csharp
public class Worker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stopping))
        {
            try { await ProcessAsync(item, stopping); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                await _channel.Writer.WriteAsync(item, CancellationToken.None); // requeue
                break;
            }
        }
    }
}
```

Points that matter:

- `IHostApplicationLifetime.ApplicationStopping` fires first — stop *accepting* work. `ApplicationStopped` fires after hosted services complete.
- The default shutdown timeout is **5 seconds**. If your drain takes longer, raise it (`HostOptions.ShutdownTimeout`) *and* make sure it's below the orchestrator's grace period, or you'll be killed mid-drain anyway.
- Fire-and-forget work started inside a request is invisible to the host and will be cut off. This is the strongest practical argument for queueing instead.
- Use `CancellationToken.None` deliberately for cleanup, requeue, and telemetry flush — code that must run *because* you're shutting down shouldn't be cancelled by the shutdown token.

<a id="be-c366"></a>

#### Long-lived connections

SignalR hubs, WebSockets, gRPC streams: a connection open for hours, with a state machine parked on it the whole time.

- The parked state machine and everything it references survives every gen-0 and gen-1 collection and lands in gen 2. Ten thousand connections × a few KB of captured state each is a gen-2 heap you didn't plan.
- Keep per-connection captured state minimal. Reference an ID, look up the heavy object when you resume — don't hold it across the wait.
- SignalR hub methods are async and a single hub instance is per-invocation, but any shared state you touch is genuinely concurrent.
- Backpressure applies to *writes* too: a client that stops reading makes your writes block, so an unbounded outbound queue per connection is an OOM waiting for a slow client. Bound it and drop or disconnect.

<a id="be-c367"></a>

#### Desktop and console

Included for completeness, since the rules invert:

**UI apps (WinForms/WPF/MAUI)** have a `SynchronizationContext` bound to the UI thread. Here `await` *without* `ConfigureAwait(false)` is what lets you touch controls after resuming — that's the feature, not an oversight. `Task.Run` is genuinely useful for CPU work, because there's a specific thread you're protecting. `async void` is legitimate for event handlers, with a try/catch inside.

**Console apps** have no `SynchronizationContext`; `static async Task Main` is supported and the runtime awaits it. A console app that returns from `Main` while background work is in flight simply exits — nothing waits for un-awaited tasks.

------------------------------------------------------------------------

### Diagnostics

<p class="src">From <em>Backend Engineering</em> · async / await · Diagnostics</p>

<a id="be-c368"></a>

#### Recognizing thread pool starvation

**Signature:** latency spikes, throughput collapse, **CPU low**. Everything looks healthy in isolation.

``` text
dotnet-counters monitor -p <pid> --counters System.Runtime
```

Watch `ThreadPool Thread Count` (climbing steadily = injection fighting starvation, it grows ~1–2 threads/second) and `ThreadPool Queue Length` (large and growing = requests waiting for threads).

In code:

``` csharp
ThreadPool.GetAvailableThreads(out var worker, out var io);
ThreadPool.GetMaxThreads(out var maxW, out var maxIo);
// worker near 0 while CPU is low → starvation
```

**Cause is almost always sync-over-async somewhere** — one `.Result` deep in a dependency, one blocking `SemaphoreSlim.Wait()`, one `File.ReadAllText` in a hot path.

Mitigation while you find it: `ThreadPool.SetMinThreads` raises the no-delay injection floor. That's a tourniquet, not a cure.

<a id="be-c369"></a>

#### Dump analysis

Async state is on the heap, so dumps are the right tool:

``` text
dotnet-dump collect -p <pid>
dotnet-dump analyze core_dump

> dumpasync                # all async state machines and their logical chains
> dumpasync --stats        # grouped by type
> clrstack -a              # native/managed stacks
> dumpheap -stat -type StateMachineBox
```

`dumpasync` reconstructs the logical async call chain from the heap — the thing the physical stacks no longer show you. If you see hundreds of state machines all parked in the same method, that's your bottleneck.

<a id="be-c370"></a>

#### Live debugging

- **Parallel Stacks window** (Tasks view) in Visual Studio shows logical async chains, not just threads.
- **Tasks window** lists in-flight tasks and their state.
- `Debugger.NotifyOfCrossThreadDependency` fires when you evaluate a blocking call in a watch window — that's the debugger warning you the evaluation is itself a deadlock risk.
- Async stack traces in exceptions are reconstructed; `--- End of stack trace from previous location ---` marks each await boundary crossed.

<a id="be-c371"></a>

#### Tracing across awaits

`System.Diagnostics.Activity` (OpenTelemetry) flows via `AsyncLocal`, so spans survive awaits automatically:

``` csharp
using var activity = _source.StartActivity("FetchUser");
activity?.SetTag("user.id", id);
var user = await FetchAsync(id, ct);   // span still current after resume
```

This is the practical answer to "how do I correlate logs across an async operation." Don't roll your own with `[ThreadStatic]` — that breaks the moment you resume on a different thread, which is always.

<a id="be-c372"></a>

#### Analyzers — cheapest win available

Turn on:

- **CA2007** — await without ConfigureAwait (library projects)
- **CA2012** — ValueTask misuse
- **CA1849** — sync call in an async method
- **VSTHRD002/103/100** — Microsoft.VisualStudio.Threading.Analyzers: sync-over-async, async void, sync method in async context

These catch the majority of production async bugs mechanically. In a legacy codebase, enable them as warnings, not errors, and burn down the list.

<a id="be-c373"></a>

#### Symptom → cause

| Symptom                                                     | Likely cause                                                            | Where to look                                                      |
|-------------------------------------------------------------|-------------------------------------------------------------------------|--------------------------------------------------------------------|
| Hangs forever, single request, no exception                 | Sync-over-async under a `SynchronizationContext`                        | `.Result`/`.Wait()` on the request path; classic ASP.NET or UI     |
| Latency climbs over ~30s under load, CPU low                | Thread pool starvation from blocking                                    | `ThreadPool Queue Length` counter; `dumpasync` for parked machines |
| Works alone, fails under load                               | Starvation, or a shared non-thread-safe object                          | Load test with counters attached                                   |
| `NullReferenceException` after an await, intermittent       | `ConfigureAwait(false)` + `HttpContext.Current` / thread-affine state   | Grep `ConfigureAwait(false)` in app-layer code on Framework        |
| Process dies with no logged exception                       | `async void` threw                                                      | Search `async void`, and `async` lambdas bound to `Action`         |
| Exception vanishes entirely                                 | Un-awaited Task, dropped on the floor                                   | `TaskScheduler.UnobservedTaskException`; CA2012/VSTHRD110          |
| Wrong data returned, nondeterministic                       | `ValueTask` awaited twice, or shared mutable state across continuations | CA2012; audit anything `[ThreadStatic]`                            |
| Memory grows with concurrency, GC gen2 heavy                | Large objects captured across long awaits                               | `dumpheap -stat`, look for `AsyncStateMachineBox` roots            |
| Connection pool exhausted / timeouts from DB                | `using` scope spanning an unrelated await                               | Grep for `SqlConnection`/session held across HTTP calls            |
| `OperationCanceledException` in production logs as an error | Client disconnects treated as failures                                  | Filter on `ct.IsCancellationRequested`                             |
| Unbounded memory under burst traffic                        | Unbounded channel or unthrottled `WhenAll`                              | `CreateUnbounded`, `Select(...)` into `WhenAll`                    |
| Actor stops processing messages (Akka)                      | `await` inside `ReceiveAsync` stalling the mailbox                      | Convert to `PipeTo`                                                |

### Legacy .NET Migration

<p class="src">From <em>Backend Engineering</em> · async / await · Legacy .NET Migration</p>

<p class="pd">Directly relevant to Framework + ServiceStack + Akka.NET + RavenDB + Ninject.</p>

<a id="be-c375"></a>

#### The order of operations

Async is contagious upward — a leaf can't be async unless its callers are. So migrate **top-down at the entry point, or bottom-up with an unbroken chain**. What you cannot do is convert the leaf and bridge with `.Result` in the middle; that's the deadlock plus starvation.

Expand-and-contract for a shared service:

1.  Add `DoWorkAsync()` alongside `DoWork()`. Implement the async one properly.
2.  Reimplement the sync one as a *legacy shim* — and accept that the shim is a blocking call. On Framework, `Task.Run(() => DoWorkAsync()).GetAwaiter().GetResult()` avoids the SynchronizationContext deadlock at the cost of a thread. Ugly, deliberate, temporary.
3.  Migrate callers to the async version, one at a time.
4.  Delete the sync version.

Never invert this: don't implement async as `Task.Run(() => Sync())`. That's fake async, and it hides the thread cost from everyone downstream.

<a id="be-c376"></a>

#### Interop with pre-Task async patterns

Framework codebases predate TAP and are full of two older patterns. You'll need to bridge them.

**APM (Begin/End), the .NET 1.x pattern:**

``` csharp
IAsyncResult BeginRead(byte[] buf, int off, int cnt, AsyncCallback cb, object state);
int EndRead(IAsyncResult ar);
```

Wrap it with `FromAsync` — this is real async, not a thread:

``` csharp
Task<int> ReadAsync(Stream s, byte[] buf) =>
    Task.Factory.FromAsync(s.BeginRead, s.EndRead, buf, 0, buf.Length, null);
```

**EAP (event-based), the .NET 2.0 pattern** — `DownloadStringAsync` + `DownloadStringCompleted`. Wrap with `TaskCompletionSource`:

``` csharp
Task<string> DownloadAsync(WebClient c, Uri uri)
{
    var tcs = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    DownloadStringCompletedEventHandler h = null;
    h = (s, e) =>
    {
        c.DownloadStringCompleted -= h;                       // unsubscribe, or leak
        if (e.Cancelled) tcs.TrySetCanceled();
        else if (e.Error != null) tcs.TrySetException(e.Error);
        else tcs.TrySetResult(e.Result);
    };
    c.DownloadStringCompleted += h;
    c.DownloadStringAsync(uri);
    return tcs.Task;
}
```

Always `TrySetX` rather than `SetX` — a duplicate completion throws `InvalidOperationException` from a callback with nowhere to go. And always unsubscribe; forgetting is a classic memory leak in long-lived clients.

**`Task.Factory.StartNew` vs `Task.Run` — the legacy trap.** Old code uses `StartNew` everywhere. Its defaults are wrong for modern use:

|                | `StartNew` default          | `Task.Run`              |
|----------------|-----------------------------|-------------------------|
| Scheduler      | `TaskScheduler.Current` (!) | always `Default`        |
| Child tasks    | can attach to parent        | `DenyChildAttach`       |
| Async delegate | returns `Task<Task>`        | unwrapped automatically |

The scheduler default is the dangerous one: inside a custom `TaskScheduler` — which is exactly what a UI or an actor dispatcher installs — `StartNew` inherits it, so your "background work" queues onto the UI thread or the actor's dispatcher.

And the async-delegate case silently doesn't wait:

``` csharp
var t = Task.Factory.StartNew(async () => await WorkAsync());  // Task<Task>
await t;                                                        // completes immediately!
await t.Unwrap();                                               // correct
await Task.Run(async () => await WorkAsync());                  // correct, simpler
```

Rule: `Task.Run` unless you specifically need `LongRunning` (which creates a dedicated thread outside the pool — appropriate for a genuinely infinite loop, not for anything else).

**`ThreadPool.QueueUserWorkItem`** in legacy code has no way to report completion or failure. Migrate to `Task.Run` with observed exceptions.

**`ConfigureAwait` in a Framework library that must also run on Core:** always `false`. It's a no-op on Core and required on Framework.

<a id="be-c377"></a>

#### ServiceStack on Framework

- Async service methods must return `Task`/`Task<object>`; ServiceStack awaits them within `AspNetSynchronizationContext`.
- `HttpContext.Current` restoration depends on that context — `ConfigureAwait(false)` in a service method that later touches request state is the NRE described in III.3.
- Filters and request/response pipelines have both sync and async variants; a sync filter calling `.Result` on an async dependency deadlocks the same way.
- Verify `<httpRuntime targetFramework="4.5" />` in web.config. Below 4.5, ASP.NET uses the legacy SynchronizationContext with materially different (worse) async behavior.

<a id="be-c378"></a>

#### Akka.NET

Actors are the sharp edge here. An actor processes one message at a time — that's the guarantee the whole model rests on.

- `await` inside `ReceiveAsync` **suspends the actor's message processing** for that mailbox. The actor doesn't process other messages while awaiting. That's often what you want, but it means a slow await stalls the mailbox.
- Actor state must not be touched from a continuation that escaped the actor's context. `PipeTo` is the safe pattern:

``` csharp
Receive<Fetch>(msg =>
{
    _service.FetchAsync(msg.Id)
        .PipeTo(Self, sender: Sender);   // result comes back as a message
});
Receive<FetchResult>(r => { _state = r.Value; });   // safe: normal message handling
```

- Never `.Result` inside an actor. You block a dispatcher thread, and Akka's dispatcher pool is finite and shared.
- `ReceiveAsync` + `ConfigureAwait(false)` breaks the actor context restoration. Don't.

<a id="be-c379"></a>

#### RavenDB, Redis, Ninject

- **RavenDB** exposes `IAsyncDocumentSession`. The sync session is a wrapper; use the async one in async code paths.
- **StackExchange.Redis** multiplexes over a single connection. Sync calls on it under load are a classic starvation source, and its own timeouts get blamed for what is actually thread pool exhaustion. Its exception messages include thread pool stats — read them.
- **Ninject** has no async resolution and no scope-flow across awaits comparable to modern DI's `AsyncServiceScope`. Anything relying on `[ThreadStatic]` or `HttpContext`-based scoping (`InRequestScope`) will misbehave once your code resumes on a different thread. This is one of the concrete semantic gaps when moving to `Microsoft.Extensions.DependencyInjection`, whose scopes flow via `AsyncLocal` and behave correctly.

<a id="be-c380"></a>

#### What changes on ASP.NET Core

|                         | Framework                             | Core                                                 |
|-------------------------|---------------------------------------|------------------------------------------------------|
| SynchronizationContext  | `AspNetSynchronizationContext`        | none                                                 |
| `.Result` in request    | deadlock                              | no deadlock, still starves                           |
| `ConfigureAwait(false)` | needed in libs, dangerous in app code | no-op                                                |
| `HttpContext.Current`   | context-restored                      | doesn't exist; `IHttpContextAccessor` via AsyncLocal |
| DI scope across await   | fragile                               | flows correctly                                      |

Removing the deadlock is not the same as making the code correct. Sync-over-async ported to Core stops hanging and starts silently limiting throughput — arguably harder to notice.

------------------------------------------------------------------------

### Extending the Machinery

<p class="src">From <em>Backend Engineering</em> · async / await · Extending the Machinery</p>

<p class="pd">You will rarely write these. Reading them closes the loop on Part III, because it shows that  SynchronizationContext  is not magic — it&#x27;s a two-method class.</p>

<a id="be-c387"></a>

#### The whole of SynchronizationContext

``` csharp
public class SingleThreadedContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback, object)> _queue = new();

    public override void Post(SendOrPostCallback d, object state)
        => _queue.Add((d, state));                    // queue for my thread

    public override void Send(SendOrPostCallback d, object state)
        => throw new NotSupportedException();          // synchronous variant

    public void RunLoop()
    {
        SynchronizationContext.SetSynchronizationContext(this);
        foreach (var (d, state) in _queue.GetConsumingEnumerable())
            d(state);                                  // run continuations here
    }
}
```

That's it. `Post` means "run this, wherever I decide that is."

Now the deadlock is obvious rather than mysterious. The UI context's `Post` queues to the UI thread's message loop. If the UI thread is inside `.Result`, it is not running the loop, so the queued continuation is never dequeued, so the Task never completes, so `.Result` never returns. **Two lines of code explain the entire failure.**

`AspNetSynchronizationContext` does the same thing over a logical request context with a one-at-a-time lock — hence the same outcome.

<a id="be-c388"></a>

#### Custom TaskScheduler

A `TaskScheduler` decides where queued tasks run:

``` csharp
public class LimitedScheduler : TaskScheduler
{
    protected override void QueueTask(Task task) { /* into my queue */ }
    protected override bool TryExecuteTaskInline(Task task, bool wasQueued)
        => false;                                      // never inline
    protected override IEnumerable<Task> GetScheduledTasks() { /* for debugger */ }
}
```

`ConcurrentExclusiveSchedulerPair` in the BCL gives you a reader/writer pair out of the box — concurrent tasks that never overlap with exclusive ones — which is a neat, underused primitive.

Relevant trap from XI.2: `Task.Factory.StartNew` defaults to `TaskScheduler.Current`, so inside a custom scheduler it inherits it. `Task.Run` always uses `Default`. This is the whole reason `Task.Run` exists.

<a id="be-c389"></a>

#### Custom awaitables

Anything with the right shape is awaitable. A useful example — moving execution deliberately:

``` csharp
public struct ThreadPoolSwitch : INotifyCompletion
{
    public ThreadPoolSwitch GetAwaiter() => this;
    public bool IsCompleted => false;                  // always suspend
    public void OnCompleted(Action k) => ThreadPool.QueueUserWorkItem(_ => k());
    public void GetResult() { }
}

// usage
await new ThreadPoolSwitch();   // everything after this line is on the pool
```

`IsCompleted => false` forces a suspension; `OnCompleted` chooses the destination. That's the entire contract. `Task.Yield()` is exactly this pattern in the BCL — it forces a yield to the scheduler, which is how you break up a long synchronous loop or escape a caller's stack.

<a id="be-c390"></a>

#### Custom method builders

`[AsyncMethodBuilder(typeof(MyBuilder))]` lets a type be the *return type* of an async method. This is how `ValueTask`, `IAsyncEnumerable`, and `UniTask` (Unity's allocation-free alternative) exist at all. Rarely needed, but it's the answer to "how is `async ValueTask` even possible when the compiler only knows about Task."

<a id="be-c391"></a>

#### Benchmarking async correctly

``` csharp
[MemoryDiagnoser]
public class Bench
{
    [Benchmark] public async Task Path() => await _sut.DoAsync();
}
```

BenchmarkDotNet handles async methods natively. Three things to get right:

- **`[MemoryDiagnoser]` is the point.** For async, allocations usually matter more than nanoseconds.
- **Benchmark the suspending path deliberately.** A mock returning `Task.FromResult` takes the fast path and never allocates a state machine, so you'll measure the wrong thing. Force a real yield.
- **Microbenchmarks don't show starvation.** The failure mode that actually hurts only appears with concurrency at scale. Load-test for that; benchmark for allocation.

------------------------------------------------------------------------

### Concurrency over shared data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Concurrency over shared data</p>

<a id="be-m136"></a>

#### Async data access: starvation and deadlock

Async exists so that a thread is not held while waiting on I/O. Every place you block on an async call, you throw that benefit away and take a thread out of circulation.

``` csharp
// Every one of these blocks a thread pool thread for the duration of a database call:
var rows = _db.Orders.ToListAsync().Result;
var rows = _db.Orders.ToListAsync().GetAwaiter().GetResult();
someTask.Wait();
Task.Run(() => AsyncMethod()).Result;
```

**Thread pool starvation** is the result. The pool injects new threads only slowly (roughly one or two per second by default), so a burst of blocked threads produces a latency cliff: response times jump from 20 ms to several seconds, CPU sits low, and nothing in the logs explains it. The tell is `ThreadPool.ThreadCount` climbing while `ThreadPool.QueueLength` stays high.

``` text
// Watch for it directly:
dotnet-counters monitor --counters System.Runtime --process-id <pid>
// threadpool-thread-count rising + threadpool-queue-length > 0 sustained = starvation.
```

**Deadlock** is the sharper version, on any framework with a synchronization context (classic ASP.NET, WPF, WinForms): the blocked thread holds the context, and the continuation needs that same context to resume. Neither can proceed. ASP.NET Core has no synchronization context, so it starves rather than deadlocks — which is why "we migrated and it got slower under load" is a common story.

``` csharp
// Async all the way. If a sync entry point is unavoidable, isolate it at the
// boundary and document why - never inside a library or a request path.
public async Task<IActionResult> Get(CancellationToken ct) {
    var rows = await _db.Orders.ToListAsync(ct);
    return Ok(rows);
}

// In library code, decouple the continuation from any captured context:
await SomethingAsync().ConfigureAwait(false);
```

> **\[Trap\]**
>
> **Async over sync is not a fix.** Wrapping a blocking call in `Task.Run` does not create asynchrony — it moves the blocking onto a pool thread, which is strictly worse under load because you have added a queueing hop. If a driver only offers sync APIs, either accept the blocking on a dedicated thread with its own bounded concurrency, or replace the driver.

> **\[Tip\]** **Async is not parallelism.** `await` in a loop is sequential. To issue independent queries concurrently, start them and then await together — but respect the connection pool limit (default 100 for SQL Server), or you will convert a thread bottleneck into a connection bottleneck.
>
> ``` csharp
> var a = _db.Orders.CountAsync(ct);        // note: no await yet
> var b = _db.Customers.CountAsync(ct);     // ERROR with EF: one DbContext is NOT
> await Task.WhenAll(a, b);                 // thread-safe for concurrent operations.
> // Use a separate context per concurrent query, via IDbContextFactory.
> ```

Worked example: [Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)

First principles: [Devices, interrupts, and why I/O is different](01-07-the-operating-system.md#be-m10)

<a id="be-m137"></a>

#### Cancellation and timeouts

A cancellation token that is accepted and ignored is worse than none — it advertises a guarantee the code does not provide. Every async data method should take one and pass it all the way down.

``` csharp
public async Task<List<OrderRow>> GetAsync(int tenantId, CancellationToken ct) {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));      // request cancel OR 5s, whichever first

    return await _db.Orders
        .Where(o => o.TenantId == tenantId)
        .Select(OrderRow.Projection)
        .ToListAsync(timeout.Token);
}
```

``` csharp
// Distinguish "the caller went away" from "we timed out" - they need different handling:
try { return await GetAsync(id, ct); }
catch (OperationCanceledException) when (ct.IsCancellationRequested) {
    throw;                                             // client disconnected: not an error
}
catch (OperationCanceledException) {
    _logger.LogWarning("Query exceeded budget for tenant {Tenant}", id);
    throw new TimeoutException("Order query timed out");
}
```

**Cancellation is cooperative.** It only takes effect where someone checks. In a CPU-bound loop, check it yourself at a sensible granularity — per batch, not per element, since `ThrowIfCancellationRequested` is not free.

``` csharp
foreach (var batch in rows.Chunk(1000)) {
    ct.ThrowIfCancellationRequested();
    Process(batch);
}
```

> **\[Trap\]**
>
> **Cancelling a database call does not undo it.** Cancellation aborts the command and, on most providers, tears down the connection — but a write that already committed stays committed, and an aborted write inside a transaction rolls back only when the transaction does. Design writes to be idempotent so a cancelled-then-retried operation converges ([Consistency across stores](07-06-non-relational-stores.md#be-m210)).

> **\[Tip\]**
>
> **Two timeouts, not one.** The provider's `CommandTimeout` bounds a single statement; a linked `CancellationTokenSource` bounds the whole logical operation including retries and multiple round trips. Set both, and make the operation budget the smaller of the two so you fail at the level you can reason about.

Worked example: [Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299)

### Async, in depth

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Async, in depth</p>

<p class="pd">What the compiler generates, what the JIT does with it, why .Result deadlocks, and when ValueTask earns its rules.</p>

<a id="be-q20"></a>

#### What the compiler and JIT actually do with async

``` csharp
public async Task<int> GetLengthAsync(string url)
{
    var response = await _http.GetStringAsync(url);
    return response.Length;
}
```

``` text
compiler → a state machine struct; JIT → machine code for MoveNext

struct d__3 : IAsyncStateMachine
{
    public int      <>1__state;      // -1 start, 0 after first await
    public AsyncTaskMethodBuilder <>t__builder;
    public string   url;             // parameters  → fields
    private TaskAwaiter <>u__1;   // locals crossing an await → fields

    void MoveNext()
    {
        switch (<>1__state)
        {
          case -1:
            awaiter = _http.GetStringAsync(url).GetAwaiter();
            if (!awaiter.IsCompleted)          // ← THE FAST PATH CHECK
            {                                   //   already done? never suspend
                <>1__state = 0;
                <>t__builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
                return;                         //   THREAD IS RELEASED HERE
            }
            goto case 0;
          case 0:
            var response = awaiter.GetResult(); //   resumed — possibly on a
            <>t__builder.SetResult(response.Length);   //   DIFFERENT thread
            return;
        }
    }
}
```

Three consequences worth internalising:

| Fact                                                       | Consequence                                                                               |
|------------------------------------------------------------|-------------------------------------------------------------------------------------------|
| The struct is boxed only when the method actually suspends | An await that completes synchronously costs about a branch — async on cache hits is cheap |
| Locals crossing an await become fields                     | A large local (a big array, many captures) is kept alive for the whole async operation    |
| Resumption happens via `MoveNext`                          | That is why stack traces show `MoveNext` frames, and why the “caller” is not on the stack |

The JIT then treats `MoveNext` as an ordinary method: tier 0 first, tier 1 with dynamic PGO once it is hot, inlining the awaiter calls and often devirtualising the builder. Nothing about async is special to the JIT — the magic is entirely in what the compiler emitted.

<a id="be-q21"></a>

#### .Result and .Wait(): the deadlock, precisely

``` csharp
// classic ASP.NET (Framework) / WPF / WinForms — a SynchronizationContext exists
public IActionResult Get()
{
    var data = _service.GetDataAsync().Result;   // ← thread blocks HERE, holding
    return Ok(data);                              //   the request context
}

// inside GetDataAsync:
//   await _http.GetAsync(...)   captures the SynchronizationContext
//   when the HTTP call completes, the continuation is POSTED back to that
//   context — which requires the very thread that is blocked on .Result
//   → the continuation waits for the thread; the thread waits for the
//     continuation. Deadlock. No exception, no timeout, just a hung request.
```

``` text
why ASP.NET Core "fixed" it — and why you still must not do it

ASP.NET Core has NO SynchronizationContext, so the continuation runs on any
thread-pool thread and the deadlock above does not occur.

But .Result still:
  • blocks a thread-pool thread for the entire I/O duration
  • under load, starves the pool → the injection rate is ~1-2 threads/second
    → latency climbs, then everything times out at once
  • wraps exceptions in AggregateException, so your catch blocks miss them

So: no deadlock, but a throughput cliff and worse error handling.
```

``` csharp
// ✓ await all the way down — the only correct answer
public async Task<IActionResult> Get(CancellationToken ct)
    => Ok(await _service.GetDataAsync(ct));

// if you are FORCED to bridge (a constructor, a legacy interface):
var data = Task.Run(() => _service.GetDataAsync()).GetAwaiter().GetResult();
//  ↑ runs on a pool thread with no captured context, and GetAwaiter()
//    unwraps the exception properly. Still blocks a thread — a last resort.

// ConfigureAwait(false) in LIBRARY code: do not capture a context you
// do not need. In an ASP.NET Core app it is a no-op; in a library that
// might be consumed by WPF it prevents the deadlock above.
var result = await _http.GetStringAsync(url).ConfigureAwait(false);
```

> **\[Trap\]**
>
> **Common trap.** `async void` is the other half of this trap: exceptions cannot be observed by the caller and crash the process. The only legitimate use is an event handler, and even then the body belongs inside a try/catch.

<a id="be-q22"></a>

#### Task, ValueTask and WhenAll in practice

``` csharp
// Task: a class. One allocation per async call that actually suspends.
//       Safe to await twice, store, pass around, WhenAll.
public async Task<User?> GetAsync(int id) { … }

// ValueTask: a struct. Zero allocation when the value is already available.
//            Await it EXACTLY ONCE. Never store it. Never WhenAll it directly.
public ValueTask<User?> GetCachedAsync(int id)
{
    if (_cache.TryGetValue(id, out var u))
        return new ValueTask<User?>(u);        // no allocation at all
    return new ValueTask<User?>(LoadAsync(id));
}

// converting when you need Task semantics
await Task.WhenAll(a.AsTask(), b.AsTask());
```

``` csharp
// WhenAll: run independent work concurrently
var userTask   = _users.GetAsync(id, ct);
var ordersTask = _orders.GetForUserAsync(id, ct);
var prefsTask  = _prefs.GetAsync(id, ct);
await Task.WhenAll(userTask, ordersTask, prefsTask);
var vm = new ProfileVm(await userTask, await ordersTask, await prefsTask);

// ⚠ WhenAll throws only the FIRST exception. To see them all:
var all = Task.WhenAll(tasks);
try { await all; }
catch { foreach (var e in all.Exception!.InnerExceptions) log.LogError(e, "…"); }

// bounded concurrency — do NOT WhenAll 10,000 tasks against a database
await Parallel.ForEachAsync(ids,
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (id, token) => await ProcessAsync(id, token));

// .NET 9: WhenEach — handle results as they arrive
await foreach (var finished in Task.WhenEach(tasks))
    Handle(await finished);

// timeouts, the modern way
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(5));
var result = await _http.GetAsync(url, cts.Token);
```

> **\[Trap\]**
>
> **Common trap.** `Task.WhenAll` starts nothing — the tasks are already running the moment they were created. That is why `WhenAll` over calls that share a `DbContext` throws, and why creating 10,000 tasks in a loop before the `WhenAll` can flood a connection pool or a downstream API before you ever await.
