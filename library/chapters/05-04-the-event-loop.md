---
id: "5-4"
number: "5.4"
title: "The event loop"
part: 5
part_title: "Concurrency"
words: 4544
minutes: 20
---

# 5.4 The event loop

> **Quick look.** How a single thread can serve thousands of connections with an event loop, as in Node.js and every UI.
>
> - An event loop runs one callback at a time to completion, then waits for the OS to report more ready I/O.
> - No data races inside the loop, but any slow callback freezes everything.
> - JavaScript runs all microtasks (promise callbacks) before the next timer or I/O task.
> - Check-then-act races still happen across awaits.
> - Languages solve waiting differently: C# and JS transform functions into state machines, Go and Java 21 use cheap stacks, older code used callbacks.
> - Use bounded buffers for backpressure.


### The big picture

A single thread can serve thousands of connections, as long as it never waits. That is how Node.js servers work, how every web page's JavaScript runs, and how every desktop window stays responsive.

The mechanism is an event loop: a loop that takes the next piece of ready work, runs it to the end, and then waits for the operating system to report more. This chapter explains the loop, why it makes some bugs impossible and others worse, why one slow piece of code can freeze everything, and how .NET's own async code relates to it. You will build a working event loop in C#.

### Before you start

**Builds on:** 5.3 Async and await, 5.2 Threads and locks, 1.3 Text is numbers too, 5.1 Why concurrency is hard.

**Ideas explained later:**

- **Node.js** (8.4): A runtime that runs JavaScript outside the browser, combining the V8 engine with an event loop.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Monitoring** (11.6): Watching a system for known problems, such as an error rate above a threshold.
- **Health check** (8.1): An endpoint that reports whether an instance is working, for load balancers and orchestrators.
- **DNS** (6.4): Domain Name System: the distributed directory that turns names into addresses and other records.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, classes and properties, generic types, lambdas, null operators, tuples, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. How can one thread serve thousands of network connections?**

> It never waits on any one of them. It asks the operating system to report which connections have data or have finished an operation, handles each ready event quickly, and goes back to waiting for the next. That loop is an event loop.

**2. What does run-to-completion mean?**

> Each callback the loop runs finishes before the next one starts. Nothing can interrupt it partway, so there are no data races within the loop's own code. The price is that a slow callback delays everything else.

**3. In JavaScript, a setTimeout of 0 and a resolved promise's then are both scheduled. Which callback runs first?**

> The promise callback. It is a microtask, and all queued microtasks run before the loop moves on to the next task, such as a timer.

**4. Why does a CPU-heavy calculation in a Node.js request handler slow down every other request?**

> All JavaScript runs on one thread. While the calculation runs, the loop can't process any other events: no new requests, no timers, no responses. Move heavy work to a worker thread or another service.

**5. Can single-threaded async code still have race conditions?**

> Yes. Code can check a value, await something, and then act on a value that another callback changed during the await. There are no data races, but check-then-act races remain.

**6. What is backpressure?**

> A way for a slow consumer to make a fast producer slow down, such as a bounded queue that makes writers wait, or a stream that stops reading from the network when its buffer is full. Without it, buffers grow until memory runs out.

**7. Why does a desktop app show "Not Responding" when you do slow work in a button handler?**

> The UI thread runs an event loop. While your handler runs, it can't process paint, input or other messages, so the window stops updating and the OS notices it isn't pumping messages.

### One thread, many tasks

Async and await (5.3) showed that waiting doesn't need a thread. Take that idea to its limit and you get a design with a single thread that handles everything. It works on one small piece of work at a time, and whenever a piece would have to wait, it moves on to another that is ready. Node.js serves heavy web traffic this way. Every browser tab runs its JavaScript this way. Every desktop UI thread works this way.

The mechanism is called an **event loop**.

### The loop

An event loop keeps a queue of work that is ready to run. Each item is a **callback**: a function to call when something has happened. The loop does four things, forever.

Take the next callback from the queue. Run it until it finishes. When the queue is empty, ask the operating system to wait for events: data arriving on a connection, a timer expiring, a file read completing. When events arrive, queue their callbacks and start again.

A callback never waits. If it needs data from the network, it starts the operation and registers another callback for when the data arrives. Then it returns, and the loop moves on.

### Run to completion

The loop runs each callback to the end before starting the next. This is **run-to-completion**. Nothing interrupts a callback halfway through. Within the loop's own code, there are no data races, no torn reads and no need for locks, because only one piece of code runs at a time.

That is a large simplification compared with Threads and locks (5.2). It has one hard rule in return: callbacks must be short. A callback that computes for a second, or makes a blocking call, stops the whole loop for that second. No other request is handled, no timer fires, no screen repaints.

### Non-blocking I/O

The loop depends on the operating system being able to wait for many things at once. **Non-blocking I/O** lets a program start a read or write that returns immediately, instead of waiting. The OS then reports when operations can proceed or have finished.

Different systems provide this differently. Linux has epoll, BSD and macOS have kqueue, and Windows has I/O completion ports. With epoll, one call can wait for events on tens of thousands of connections at once and returns only the ones that are ready. A loop built on this is called the **reactor pattern**: wait for readiness, then react.

**libuv** is the C library that gives Node.js one event loop on top of all of these. .NET does the same job internally: its sockets and file streams sit on I/O completion ports on Windows and epoll on Linux, and completions wake the continuations from Async and await (5.3). The difference is that .NET runs those continuations on a pool of threads rather than a single one.

### From callbacks to async and await

Early JavaScript used plain callbacks for everything. Each step took a function to call when it finished, and sequences of steps nested inside each other, drifting further right with every step and handling errors separately at each level. Developers called it **callback hell**.

A **promise** is an object standing for a result that will arrive later, the JavaScript equivalent of a `Task`. Promises can be chained and combined, and errors flow through the chain to one handler. ES2017 then added `async` and `await` on top of promises, which lets asynchronous code read like ordinary sequential code. C# went through the same history, from callbacks and events to tasks to `async` and `await` in 2012.

### Microtasks and tasks

JavaScript's loop has two queues, and the difference affects the order things happen in.

The ordinary **task queue** holds callbacks from timers, I/O and user events. A **microtask** is a smaller unit, queued mainly by promises when they settle and by `queueMicrotask`. After each task, and after the main script, the loop runs every queued microtask, including any queued by other microtasks, before it takes the next task. In a browser, rendering happens between tasks.

```javascript
// order.js — run with: node order.js, or paste into a browser console
console.log("1: script starts");
setTimeout(() => console.log("5: timer callback, a task"), 0);
Promise.resolve().then(() => console.log("3: promise callback, a microtask"));
queueMicrotask(() => console.log("4: another microtask"));
console.log("2: script ends");
// 1, 2, 3, 4, 5: the script finishes first, then every microtask, then the next task
```

A zero-millisecond timer doesn't run immediately. It runs after the current code and all microtasks, and only then.

### Blocking the loop

Anything slow that runs on the loop thread holds everything up.

```javascript
// block.js — run with: node block.js
const start = Date.now();
setTimeout(() => console.log(`timer due at 10 ms ran at ${Date.now() - start} ms`), 10);
while (Date.now() - start < 300) { }   // 300 ms of busy work on the loop thread
// timer due at 10 ms ran at about 300 ms
```

In a server, the same happens to every request. Common culprits are heavy calculations, synchronous file calls such as Node's `readFileSync` in a request handler, parsing a very large JSON body, and regular expressions that backtrack badly, as Text is numbers too (1.3) warned. The time a callback waits beyond when it should have run is called **event loop lag**. Monitoring it is the most direct health check for a Node.js service.

The cures are to break work into small pieces, move CPU-heavy work to a **worker thread** or a separate service, and use only non-blocking I/O on the loop.

### Node.js in more detail

Node.js runs your JavaScript on one thread, with an event loop provided by libuv. Network I/O uses the operating system's non-blocking mechanisms directly. Some operations have no good non-blocking form on every OS, such as many file system calls, DNS lookups through the system resolver, and some crypto and compression. libuv runs those on a small thread pool of its own, four threads by default, and posts the results back to the loop.

For CPU-heavy JavaScript, the `worker_threads` module starts extra threads, each with its own separate JavaScript engine and event loop. Workers communicate by passing messages, following the shared-nothing idea from Why concurrency is hard (5.1).

### UI threads are event loops

Every desktop and mobile UI framework has a **UI thread** running an event loop, often called a message loop. Windows sends it messages for clicks, key presses and repaints. WPF calls its loop the Dispatcher. Android calls it the main looper. The browser's main thread plays the same role for web pages.

Only the UI thread may touch the user interface, which avoids races on the controls. And anything slow on it freezes the window. When a Windows app doesn't process messages for about five seconds, the system marks it "Not Responding". The rule is the same as for servers: keep the loop's callbacks short, and move slow work elsewhere, then post the result back to the UI thread.

That posting back is exactly what the synchronization context from Async and await (5.3) does. This program builds a tiny event loop in C# to show it.

```csharp
// eventloop.cs — run with: dotnet run eventloop.cs
using System.Collections.Concurrent;
using System.Diagnostics;

var loop = new SingleThreadLoop();
loop.Run(async () =>
{
    int loopThread = Environment.CurrentManagedThreadId;
    Console.WriteLine($"start on thread {loopThread}");

    await Task.Delay(50);                                   // the continuation is posted back to the loop
    Console.WriteLine($"after await, same thread: {Environment.CurrentManagedThreadId == loopThread}");   // True

    await Task.WhenAll(Handle("A"), Handle("B"));           // interleave at awaits, never mid-step
    // A step 1, B step 1, then A step 2 and B step 2

    var sw = Stopwatch.StartNew();
    Task timer = Task.Delay(100).ContinueWith(
        _ => Console.WriteLine($"100 ms timer ran after {sw.ElapsedMilliseconds} ms"),
        TaskScheduler.FromCurrentSynchronizationContext()); // its callback must run on the loop
    Thread.Sleep(500);                                      // blocking the loop thread
    await timer;                                            // 100 ms timer ran after about 500 ms
});

async Task Handle(string name)
{
    Console.WriteLine($"{name} step 1");
    await Task.Delay(20);
    Console.WriteLine($"{name} step 2");
}

sealed class SingleThreadLoop : SynchronizationContext
{
    readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();

    public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));   // "run this soon"

    public void Run(Func<Task> main)
    {
        SetSynchronizationContext(this);                    // awaits on this thread now post back here
        Task done = main();
        done.ContinueWith(_ => queue.CompleteAdding(), TaskScheduler.Default);
        foreach (var (callback, state) in queue.GetConsumingEnumerable())
            callback(state);                                // take one, run it to completion, repeat
        done.GetAwaiter().GetResult();                      // rethrow any exception from main
    }
}
```

### Races without threads

Run-to-completion removes data races, not every race. An `await` is a point where other callbacks can run. Code that checks a value, awaits, and then acts on it has the same check-then-act problem as threaded code.

A seat check, then `await` a database call, then booking the seat, lets another request book the same seat during the await. On one thread, the bug is rarer and easier to reason about, but it is still there. The fixes are the same as before: make the check and the act one step, often in the database with a constraint or a conditional update.

### Backpressure

In any pipeline, one stage can produce faster than the next consumes. A server reads uploads faster than it can write them to storage. A producer queues messages faster than workers handle them. Without limits, the buffer between them grows until memory runs out.

**Backpressure** is the consumer pushing back: making the producer slow down to its pace. Node.js streams stop reading from a socket when their buffer passes a high-water mark. TCP does the same between machines with its window, as TCP and UDP (6.3) explains. In .NET, a bounded `Channel` makes writers wait when it is full, as Async and await (5.3) showed. Wherever data flows through a buffer, ask what happens when the far side is slow.

### Actors

The **actor model** extends the event loop idea to many independent units. Each actor owns its own state and a mailbox of messages. It handles one message at a time, run to completion, so its state needs no locks. Actors communicate only by sending each other messages. Erlang is built on this model. In .NET, Akka.NET and Microsoft Orleans provide actors, and Orleans can spread them across a cluster of machines.

### In short

An event loop runs one callback at a time to completion, then waits for the operating system to report that more I/O is ready. That removes data races within the loop, but any slow callback stalls everything else. JavaScript runs all microtasks, such as promise callbacks, before the next timer or I/O task. UI threads are event loops, so slow work there freezes the window. Check-then-act races still happen across awaits, and bounded buffers provide backpressure.

### What goes wrong

**Blocking the loop.** A synchronous file read, a heavy calculation or a huge `JSON.parse` in a Node.js handler stalls every request. Keep loop callbacks short and move heavy work to workers.

**Slow work on the UI thread.** The window freezes and the OS marks it "Not Responding". Await I/O, and move CPU work off the UI thread with `Task.Run`.

**Check, await, act.** A single-threaded service still double-books when two requests interleave at an await. Make the check and the change one atomic step.

**Unbounded buffers.** Queues between fast producers and slow consumers grow until the process runs out of memory. Use bounded queues, streams with high-water marks, or channels.

**Assuming timers are exact.** A 10 ms timer runs after the current callback and every microtask, and later still if the loop is busy. Never rely on timers for precise scheduling.

**Starving the task queue with microtasks.** Code that keeps queueing new microtasks from microtasks prevents timers, I/O and rendering from ever running.

### Cost model

Dispatching one callback costs well under a microsecond. One epoll or completion port wait can report events for thousands of connections at once, so an event loop handles tens of thousands of mostly idle connections with a single thread and little memory per connection. The limit is CPU on that one thread: a Node.js process uses at most about one core for JavaScript, so busy servers run one process per core behind a load balancer or in a cluster. Event loop lag above tens of milliseconds means callbacks are too slow.

### In other languages (optional)

**JavaScript and TypeScript.** Event loops everywhere: the browser main thread, web workers, Node.js, Deno and Bun. `setImmediate` and `process.nextTick` in Node.js add further queues with their own ordering.

**Python.** `asyncio` runs an event loop on one thread. `uvloop` replaces it with one built on libuv for speed.

**.NET.** ASP.NET Core doesn't run your code on a single loop thread. Continuations run on the thread pool, so request code can use every core but must be thread-safe. UI frameworks and Blazor in the browser do use single-threaded loops.

**Go.** The runtime's network poller is an event loop hidden under goroutines. Code looks blocking; the runtime parks the goroutine and serves others.

**Java.** Netty and Vert.x provide event-loop servers. Virtual threads now offer a blocking style on a similar foundation.

### Key terms

- **Event loop**: A loop that takes ready callbacks from a queue and runs them one at a time, waiting for events when idle.
- **Callback**: A function passed to be called later, when something has happened.
- **Run-to-completion**: Each callback finishing before the next one starts, with no interruption partway.
- **Non-blocking I/O**: I/O calls that return immediately, with the OS reporting later when data is ready or the operation finished.
- **Reactor pattern**: Waiting for the OS to report which connections are ready, then reacting to each.
- **libuv**: The C library that provides Node.js's event loop and its thread pool.
- **Callback hell**: Deeply nested callbacks that make asynchronous code hard to read and handle errors in.
- **Promise**: The JavaScript object standing for a result that will arrive later.
- **Microtask**: A small unit of work, mainly promise callbacks, run after each task before the next one.
- **Task queue**: The event loop's queue of ordinary callbacks from timers, I/O and user events.
- **Event loop lag**: How late callbacks run compared with when they should, a measure of a busy loop.
- **Worker thread**: An extra thread, with its own loop, used to run CPU-heavy work away from the main loop.
- **UI thread**: The single thread that runs a user interface's event loop and alone may touch its controls.
- **Backpressure**: A slow consumer making a fast producer slow down, to keep buffers from growing without limit.
- **Actor model**: Independent units that own their state, handle one message at a time and communicate only by messages.

### Where this leads

Using every core for CPU-bound work is in Parallelism (5.5). TCP's own form of backpressure is in TCP and UDP (6.3). The browser's event loop and rendering are in How a browser shows a page (9.1). A Node.js backend is built in A Node and TypeScript backend (8.4). Actors across machines and message-based systems are in Messaging and events (12.3).

### Check yourself

1. Add a third `Handle("C")` with a longer delay to `eventloop.cs`, and predict the order of the output lines before running it.
2. Replace `Thread.Sleep(500)` in `eventloop.cs` with `await Task.Delay(500)`, and explain why the timer now runs on time.
3. In `order.js`, add a `setTimeout` inside the promise callback. Predict where its output appears.
4. Write a small Node.js HTTP server that computes a large Fibonacci number in one route. Request that route and a simple route at the same time, and measure the simple route's delay.
5. Write a check-then-act race in single-threaded async code: two calls that both check a shared seat count, await `Task.Delay`, then book. Fix it.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A Node API frozen by one big JSON parse

A Node service handling thousands of requests stalls for 2 seconds whenever someone uploads a 50 MB manifest.

*Move CPU work off the event loop*

```typescript
import { Worker } from "node:worker_threads";

function parseInWorker(text: string): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const w = new Worker(new URL("./parse-worker.js", import.meta.url), { workerData: text });
    w.once("message", resolve);
    w.once("error", reject);
  });
}
// better still: stream-parse the upload, or move the job to a queue worker
```

> **Lesson:** On an event loop, any long synchronous task blocks every request. Keep CPU-heavy work off the loop.

#### Example 2: A race across an await in single-threaded code

A browser cart "applies a coupon once" but double-clicks apply it twice, even though JavaScript is single-threaded.

*Guard across the await*

```typescript
let applying: Promise<void> | null = null;

async function applyCoupon(code: string) {
  if (applying) return applying;            // second click reuses the in-flight request
  applying = (async () => {
    try { await api.post("/cart/coupon", { code }); }
    finally { applying = null; }
  })();
  return applying;
}
// and make the server idempotent: one coupon per cart, enforced by a constraint
```

> **Lesson:** No data races inside the loop doesn't mean no races: state can change between check and act across an await.

### How Other Languages Solve This

<p class="src">From <em>Backend Engineering</em> · async / await · How Other Languages Solve This</p>

<p class="pd">Same problem, different trade-offs. Seeing the alternatives makes C#&#x27;s choices legible rather than arbitrary. 
 Every runtime faces the identical question from Part I:  a paused operation&#x27;s state can&#x27;t live on a stack, so where does it go?  There are three answers.</p>

<a id="be-c382"></a>

#### Answer 1: Move it to the heap (C#, JavaScript, Rust, Python)

Compiler transforms the function into a resumable object. What C# does.

**JavaScript** is the closest relative — same `async`/`await`, same promise-as-handle model. The difference is the runtime: JS has **one thread and an event loop**. There is no `ConfigureAwait` because there is nowhere else to resume; every continuation goes to the same thread. This makes deadlock-by-blocking structurally impossible (you can't block the only thread without freezing everything), and makes CPU work catastrophic — a 200 ms computation freezes all requests, not just one.

The microtask/macrotask split is JS's version of continuation scheduling: promise callbacks (microtasks) drain completely before the next timer or I/O callback (macrotask). C#'s equivalent decision is inline-vs-queue.

**Rust** goes furthest. `async fn` produces a state machine that is **inert** — it does nothing until polled. No runtime is built in; you choose one (Tokio, async-std). This means zero-cost abstraction (no allocation unless you box it) and the famous consequence: **futures are lazy**, so an un-awaited future in Rust simply never runs, where an un-awaited Task in C# is already running.

**Python** uses `async`/`await` over generators, with an explicit event loop you must run (`asyncio.run`). Single-threaded per loop, like JS, largely because of the GIL. `await` on a non-async function blocks the whole loop — the same failure as blocking in JS.

<a id="be-c383"></a>

#### Answer 2: Give it a cheap stack (Go, Java 21+)

Don't restructure the code. Make stacks so cheap that keeping one per operation is fine.

**Go's goroutines** start with a **2 KB growable stack** — 500× smaller than a thread's 1 MB reservation — and are multiplexed onto OS threads by the runtime scheduler. You write ordinary blocking code:

``` text
resp, err := http.Get(url)   // "blocks" the goroutine, not the thread
```

The runtime intercepts the blocking call, parks the goroutine, and runs another on the same thread. **No `async` keyword, no function coloring, no `await`.** The cost: it's the runtime's scheduler, not the OS's, and you can't opt out; and stack growth means copying, which occasionally surprises.

**Java's virtual threads** (Project Loom, Java 21) are the same idea retrofitted onto an existing ecosystem. Existing blocking code becomes scalable without rewriting, which is a genuinely remarkable outcome for a 30-year-old language. Caveat: `synchronized` blocks pin a virtual thread to its carrier, undoing the benefit — the equivalent of C#'s "one `.Result` ruins it."

<a id="be-c384"></a>

#### Answer 3: Callbacks (Node pre-2015, C, old .NET)

Manual splitting. What we all did before, and what `async`/`await` mechanizes. Covered in II.1.

<a id="be-c385"></a>

#### The comparison

|                    | C#/.NET             | JavaScript         | Go                | Java 21+          | Rust              |
|--------------------|---------------------|--------------------|-------------------|-------------------|-------------------|
| Mechanism          | state machine       | state machine      | growable stacks   | growable stacks   | state machine     |
| Threads            | pool, many          | one                | many, multiplexed | many, multiplexed | pool, many        |
| Keyword needed     | `async`/`await`     | `async`/`await`    | none              | none              | `async`/`await`   |
| Function coloring  | yes                 | yes                | **no**            | **no**            | yes               |
| Starts eagerly     | **yes**             | yes                | n/a               | n/a               | **no**            |
| Blocking risk      | starvation/deadlock | freezes everything | rare              | pinning           | runtime-dependent |
| Cost per operation | ~100–200 B heap     | closure            | ~2 KB stack       | ~1 KB stack       | 0 if not boxed    |

<a id="be-c386"></a>

#### What this tells you about C

**Function coloring is the price of the state machine approach.** `async` is contagious upward — a leaf can't be async unless its callers are — which is why migrating a legacy codebase is a chain problem rather than a local edit. Go and Java avoid this entirely by not having the keyword. That's a real advantage, and it's why "async all the way" is advice C# needs and Go doesn't.

**Eager start is a genuine design choice, and unusual.** C# runs the method body immediately up to the first suspension. Rust doesn't run anything until polled. The C# choice means a dropped Task is still running (and its exception is still stored); the Rust choice means a dropped future never happened. Both are defensible; C#'s is more surprising.

**Deadlock-by-blocking is specific to having both a `SynchronizationContext` and multiple threads.** JS can't have it (one thread), Go can't have it (no contexts). It's the intersection that produces it — which is why ASP.NET Core removing the context eliminated an entire bug class.

------------------------------------------------------------------------
