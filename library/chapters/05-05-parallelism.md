---
id: "5-5"
number: "5.5"
title: "Parallelism"
part: 5
part_title: "Concurrency"
words: 7496
minutes: 33
---

# 5.5 Parallelism

> **Quick look.** Splitting CPU work across cores, and when that doesn't help.
>
> - Data parallelism splits the same work; task parallelism runs different work at once.
> - Parallel.For and PLINQ split work; give each worker its own subtotal and combine at the end.
> - Use Parallel.ForEachAsync or throttled tasks for I/O, not CPU parallelism.
> - Amdahl's law: the part that can't be split caps the speedup.
> - Don't parallelize inside a busy web server; parallel floating-point sums won't match to the last digit.
> - Bounded Channel<T> gives producer/consumer pipelines with backpressure; actors avoid locks by owning their state.


### The big picture

Processors stopped getting much faster around 2005 and got more cores instead. A program that runs on one thread uses one core, however many the machine has. Parallelism means splitting work across cores so it finishes sooner.

This chapter shows how .NET does that with parallel loops and PLINQ, how to combine results without slowing everything down, and the law that limits how much faster any job can get. Just as important, it explains when parallelism doesn't help, including inside a busy web server. It ends Part 5.

### Before you start

**Builds on:** 1.4 The processor, 5.1 Why concurrency is hard, 5.3 Async and await, 5.2 Threads and locks, 1.5 Memory, 4.4 Immutability and pure functions.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Throughput** (6.1): The rate a transfer actually achieves after overheads and sharing.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **Availability** (10.1): A system works when legitimate users need it.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Local state** (9.4): State that belongs to one component, such as whether a menu is open.
- **Bandwidth** (6.1): How much data a path can carry per second, usually quoted in bits per second.
- **Machine learning** (13.1): Building programs whose behaviour is learned from examples rather than written as rules.

**Tools and technologies named here:**

- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, lambdas, LINQ, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does Amdahl's law say?**

> The part of a job that can't be parallelized limits the total speedup. If 10% must run serially, no number of cores can make it more than 10 times faster, and 16 cores give only about 6.4 times.

**2. How should several threads add up a total in Parallel.For?**

> Each thread keeps its own local subtotal, and the subtotals are combined once at the end. Updating one shared total on every iteration forces every core to fight over the same memory, even with `Interlocked`.

**3. Should you use Parallel.For inside an ASP.NET Core request handler?**

> Usually not. A busy server is already using every core for different requests. Parallelizing inside each request adds overhead and competes with other requests, lowering total throughput.

**4. Why is Parallel.ForEach a poor fit for making 1,000 HTTP calls?**

> It is designed for CPU work and blocks pool threads while each call waits. `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism`, or tasks throttled by a semaphore, handle I/O without holding threads.

**5. Why can a parallel sum of doubles give a slightly different answer each run?**

> Floating-point addition isn't exactly associative: the order of additions affects rounding. Parallel code combines partial sums in different orders from run to run.

**6. What is work stealing?**

> Each worker thread keeps its own queue of work. When a thread runs out, it takes work from the back of another thread's queue. It balances load without a central queue that every thread fights over.

**7. What is an embarrassingly parallel problem?**

> One whose pieces are completely independent, so they can run in parallel with no coordination: resizing many images, checking many numbers for primality, rendering many frames.

### Why parallelism

The processor (1.4) explained that clock speeds stopped rising around 2005. Chips got more cores instead. A laptop has 8 to 16, a server 32 to 128 or more. A program that runs on one thread uses one of them, however many there are.

**Parallelism** is running parts of a job at the same instant on several cores, to finish sooner. Why concurrency is hard (5.1) separated it from concurrency, which is about many things being in progress. Parallelism is about speed on CPU-bound work, the kind from Async and await (5.3) that keeps a processor busy rather than waiting.

### Two kinds of parallelism

**Data parallelism** applies the same operation to many pieces of data at once. Check two million numbers for primality, resize ten thousand images, total a billion sales figures. Split the data, give each core a share, combine the results. This is by far the most common and useful kind.

**Task parallelism** runs different operations at the same time. Compute route options while pricing the fares while checking seat availability. Each is a separate task, and they combine at the end.

A problem whose pieces are completely independent, needing no communication between them, is called **embarrassingly parallel**. The name is a joke: it is embarrassingly easy. These problems are the best candidates, and they are common.

### Parallel loops

.NET's `Parallel` class runs loops across cores. `Parallel.For` works over a range of numbers. `Parallel.ForEach` works over a collection. Both split the work into chunks, run the chunks on pool threads and wait until everything is done.

Splitting the work well is called **partitioning**. Chunks that are too small waste time on scheduling. Chunks that are too large leave some cores idle while one finishes its oversized share. The `Parallel` class and the thread pool handle this with **work stealing**. Each worker thread has its own queue. A thread that runs out of work takes some from the back of another thread's queue. Load evens out without every thread fighting over one shared queue.

The body of a parallel loop runs on many threads at once, so everything from Threads and locks (5.2) applies. Anything shared must be thread-safe. The best parallel loops share nothing while they run.

### Reductions

Many parallel jobs end by combining results: a sum, a count, a maximum, a merged list. That combining step is a **reduction**.

The naive way updates one shared total in every iteration. Even with `Interlocked`, every core then fights over the same cache line, as Memory (1.5) described with false sharing. The loop is correct and much slower than it should be.

The right way gives each worker its own local subtotal. The worker adds into it with no sharing at all, and the subtotals are combined once per worker at the end. `Parallel.For` has an overload built for this: a function to create each worker's local state, the loop body that updates it, and a function to combine it at the end. PLINQ does this automatically for `Sum`, `Count`, `Aggregate` and similar operators.

### PLINQ

**PLINQ**, Parallel LINQ, runs LINQ queries across cores. Add `.AsParallel()` to a query and its operators run on several threads. `Enumerable.Range(0, n).AsParallel().Count(IsPrime)` counts in parallel.

By default, PLINQ doesn't keep the order of results, because keeping order costs time. Add `.AsOrdered()` when order matters. `.WithDegreeOfParallelism(n)` limits the number of threads. `.ForAll(action)` runs an action on each result in parallel without merging them back. Lambdas in a PLINQ query must not have side effects on shared data; the pure functions from Immutability and pure functions (4.4) are exactly what it needs.

### Parallel I/O

`Parallel.For` and `Parallel.ForEach` are for CPU work. Given I/O work, they block pool threads while each operation waits. For many I/O operations with a limit on how many run at once, use `Parallel.ForEachAsync`, added in .NET 6. It awaits each operation instead of blocking, and its `MaxDegreeOfParallelism` caps how many are in flight.

```csharp
// parallel.cs — run with: dotnet run -c Release parallel.cs
using System.Diagnostics;
using System.Linq;

const int Limit = 2_000_000;
Console.WriteLine($"cores available: {Environment.ProcessorCount}");

// 1. Sequential baseline
var sw = Stopwatch.StartNew();
int sequential = 0;
for (int n = 0; n < Limit; n++) if (IsPrime(n)) sequential++;
double seqMs = sw.Elapsed.TotalMilliseconds;
Console.WriteLine($"sequential:     {sequential:N0} primes in {seqMs:F0} ms");   // 148,933 primes

// 2. Parallel.For with a local subtotal per worker, combined once at the end
sw.Restart();
int parallel = 0;
Parallel.For(0, Limit,
    () => 0,                                            // each worker starts its own count
    (n, _, local) => IsPrime(n) ? local + 1 : local,    // nothing shared inside the loop
    local => Interlocked.Add(ref parallel, local));     // combine once per worker
double parMs = sw.Elapsed.TotalMilliseconds;
Console.WriteLine($"Parallel.For:   {parallel:N0} primes in {parMs:F0} ms, speedup x{seqMs / parMs:F1}");

// 3. PLINQ: the same question as a parallel query
sw.Restart();
int plinq = Enumerable.Range(0, Limit).AsParallel().Count(IsPrime);
Console.WriteLine($"PLINQ:          {plinq:N0} primes in {sw.Elapsed.TotalMilliseconds:F0} ms");

// 4. The slow way: one shared counter touched by every iteration
sw.Restart();
int contended = 0;
Parallel.For(0, Limit, n => Interlocked.Add(ref contended, IsPrime(n) ? 1 : 0));
Console.WriteLine($"shared counter: {contended:N0} primes in {sw.Elapsed.TotalMilliseconds:F0} ms");   // correct, and slower

// 5. Parallel I/O with a limit
sw.Restart();
string[] routes = Enumerable.Range(1, 20).Select(i => $"route {i}").ToArray();
await Parallel.ForEachAsync(routes, new ParallelOptions { MaxDegreeOfParallelism = 5 },
    async (route, ct) => await Task.Delay(100, ct));    // stands in for an API call
Console.WriteLine($"20 calls, 5 at a time: {sw.ElapsedMilliseconds} ms");   // about 400

// 6. Amdahl's law: the serial part sets the ceiling
foreach (double serial in new[] { 0.05, 0.10 })
    Console.WriteLine($"serial part {serial * 100:F0}%: " +
        string.Join(", ", new[] { 4, 16, 64 }.Select(cores => $"{cores} cores x{1 / (serial + (1 - serial) / cores):F2}")) +
        $", limit x{1 / serial:F0}");
// serial part 5%: 4 cores x3.48, 16 cores x9.14, 64 cores x15.42, limit x20
// serial part 10%: 4 cores x3.08, 16 cores x6.40, 64 cores x8.77, limit x10

static bool IsPrime(int n)
{
    if (n < 2) return false;
    if (n % 2 == 0) return n == 2;
    for (int d = 3; (long)d * d <= n; d += 2)
        if (n % d == 0) return false;
    return true;
}
```

### Amdahl's law

Almost every job has some part that can't run in parallel: reading the input, merging the results, a step that depends on the one before. In 1967, Gene Amdahl pointed out what that means. If a fraction *s* of the work is serial, the best possible **speedup** on *n* cores is 1 / (*s* + (1 − *s*) / *n*). As *n* grows without limit, the speedup approaches 1 / *s*.

That is **Amdahl's law**, and its numbers are sobering. With just 5% serial work, 16 cores give about 9 times the speed, not 16, and no number of cores gets past 20. With 10% serial work, the ceiling is 10. The example prints the figures.

**Parallel efficiency** is the speedup divided by the number of cores. 9 times faster on 16 cores is 57% efficient. Efficiency usually falls as you add cores.

A more hopeful view came from John Gustafson in 1988. In practice, people use more cores to solve bigger problems in the same time, not the same problem faster. As the data grows, the parallel part usually grows while the serial part stays about the same, so the serial fraction shrinks. That is **Gustafson's law**, and it is why large data processing scales well.

### What else limits speedup

Serial code isn't the only limit.

**Overhead.** Splitting work, scheduling it on threads and combining results all cost time. For tiny pieces of work, a few microseconds or less each, overhead can make the parallel version slower than the plain loop. Make each chunk substantial.

**Memory bandwidth.** All cores share the same path to main memory. A loop that does little arithmetic per byte, such as summing a huge array, is limited by how fast memory can deliver data, not by how many cores compute. Beyond a few cores, it stops speeding up.

**Shared state.** Contended locks, contended atomics and false sharing serialize threads that were meant to run in parallel.

**Hardware threads.** Two vCPUs are often two hyperthreads of one core, as The processor (1.4) explained, and give much less than twice the throughput for CPU-heavy work.

**Oversubscription.** Running more CPU-bound threads than there are cores causes constant context switching. It happens with nested parallel loops, and with parallel loops inside a web server that is already busy with other requests.

### When not to parallelize

Parallelism makes a single job finish sooner. It doesn't increase how much total work a busy machine can do; it only reshuffles it.

So inside a web server under load, parallelizing each request usually hurts. Every core is already busy with other requests. Parallel loops add overhead and make requests compete. Parallelism helps on a server when there are few requests, each with heavy computation, such as a nightly report or a batch job.

Don't parallelize I/O-bound work with CPU tools; use async. Don't parallelize small or fast work. Don't parallelize code whose pieces must share and update common state. And don't parallelize before measuring that the serial version is too slow.

### Parallelism within a core, and beyond

Parallelism happens at other levels too.

Within one core, SIMD instructions process several values in one step, as The processor (1.4) showed. .NET's `Vector<T>` and the `TensorPrimitives` class in the System.Numerics.Tensors package use SIMD for common numeric operations. SIMD and multiple cores combine: split the data across cores, and use SIMD within each.

A GPU runs thousands of simple threads at once. Machine learning, image processing and large simulations use GPUs for data parallelism on a scale no CPU can match.

Across machines, frameworks spread data parallel jobs over clusters. The idea goes back to **MapReduce**, described by Google in 2004: a map step processes pieces of data independently on many machines, and a reduce step combines the results. Apache Spark and cloud data warehouses work on the same principle. Scaling and performance (12.4) returns to this.

### Getting the same answer

Parallel code must give the right answer every time. Two things can quietly break that.

Floating-point addition isn't exactly associative, as Numbers inside the machine (1.2) showed. A parallel sum of doubles adds partial sums in different orders on different runs, and the last digits can change. For money, use `decimal` or integers. For science, accept a tolerance, or combine partial sums in a fixed order.

Anything that depends on order, such as "the first item that matches", needs care. `Parallel.For` offers `ParallelLoopState.Break` and `Stop` for early exit, and PLINQ has `AsOrdered`. Without them, "first" means "first one any thread happened to find".

### In short

Data parallelism splits the same work across cores; task parallelism runs different work at once. `Parallel.For` and PLINQ do the splitting: give each worker its own subtotal and combine them at the end. Use `Parallel.ForEachAsync` or throttled tasks for I/O. Amdahl's law says the part that can't be split caps the speedup, and overhead, memory bandwidth and contention reduce it further. Don't parallelize inside busy servers, and don't expect parallel floating-point sums to match to the last digit.

### What goes wrong

**A shared accumulator in every iteration.** Correct results, poor speedup, because every core fights over one cache line. Use per-worker local state and combine at the end.

**Non-thread-safe collections in a parallel loop.** Adding to a `List<T>` from `Parallel.ForEach` loses items or throws. Return results from PLINQ, use a concurrent collection, or collect per worker.

**Parallelizing tiny work.** Splitting a loop of cheap operations costs more in overhead than it saves. Measure, and parallelize only chunks of real work.

**CPU parallelism for I/O.** `Parallel.ForEach` over HTTP calls blocks pool threads and can starve the pool. Use `Parallel.ForEachAsync` or throttled tasks.

**Parallel loops in busy web servers.** Throughput falls and latency rises under load. Keep request handling sequential unless requests are rare and heavy.

**Nested parallelism.** A parallel loop calling code that runs its own parallel loop oversubscribes the machine. Parallelize at one level, usually the outermost.

**Expecting exact floating-point results.** A parallel total differs in its last digits between runs, and a test that compares with `==` flickers. Use tolerances or a deterministic order.

**Swallowed failures.** An exception in one iteration stops the loop and surfaces as an `AggregateException` holding every failure. Catch that type, and look at all the inner exceptions.

### Cost model

Starting a `Parallel.For` costs a few microseconds, and each chunk of work a little more for scheduling. So each iteration, or chunk, should do at least tens of microseconds of work for parallelism to pay. For embarrassingly parallel CPU work, expect speedups close to the number of physical cores and less from hyperthreads. Memory-bound loops often stop improving after 4 to 8 cores. The serial fraction caps everything: 5% serial means at most 20 times, however many cores.

### In other languages (optional)

**JavaScript.** One thread per event loop, so parallelism means worker threads, each with its own engine, communicating by messages or `SharedArrayBuffer`. In browsers, WebGPU gives access to the GPU.

**Python.** The global interpreter lock stops pure Python code from running on several cores in one process. `multiprocessing` and `concurrent.futures.ProcessPoolExecutor` use separate processes instead. NumPy and similar libraries release the lock and run native code in parallel. Free-threaded builds, optional since Python 3.13, remove the lock.

**Java.** Parallel streams correspond to PLINQ, and the fork/join pool uses work stealing like .NET's thread pool.

**Go.** Goroutines spread across all cores automatically. The `GOMAXPROCS` setting controls how many run at once.

**SQL.** Databases parallelize large queries themselves. SQL Server's query plans show parallel operators, controlled by the max degree of parallelism setting.

### Key terms

- **Parallelism**: Running parts of a job at the same instant on several cores to finish sooner.
- **Data parallelism**: Applying the same operation to many pieces of data at once.
- **Task parallelism**: Running different operations at the same time.
- **Embarrassingly parallel**: A problem whose pieces are completely independent and need no coordination.
- **Partitioning**: Splitting work into chunks for different workers.
- **Work stealing**: Idle workers taking queued work from busy workers' queues to balance load.
- **Reduction**: Combining many partial results into one, such as a sum or a maximum.
- **PLINQ**: Parallel LINQ, which runs LINQ queries across several cores with AsParallel.
- **Speedup**: How many times faster a job runs in parallel than sequentially.
- **Parallel efficiency**: Speedup divided by the number of cores used.
- **Amdahl's law**: The rule that the serial fraction of a job limits its maximum parallel speedup.
- **Gustafson's law**: The observation that larger problems shrink the serial fraction, so parallelism scales better with data size.
- **Memory bandwidth**: How fast main memory can deliver data, a limit on loops that do little work per byte.
- **Oversubscription**: Running more CPU-bound threads than cores, causing wasteful context switching.
- **MapReduce**: Processing data on many machines with an independent map step and a combining reduce step.

### Where this leads

This ends Part 5. Part 6 leaves the single machine, starting with Layers and packets (6.1). Parallel query plans come back in Indexes and query plans (7.3). Scaling across many machines, and measuring where time goes, is in Scaling and performance (12.4). Distributed coordination, the hardest form of concurrency, is in Distributed systems basics (12.2).

### Check yourself

1. Run `parallel.cs` and note the speedup. Then set `DOTNET_PROCESSOR_COUNT=2` in the environment and run again. Explain the new numbers.
2. Change `Limit` to 20,000 and compare the sequential and parallel times. Find roughly where parallelism stops paying on your machine.
3. Sum a million random doubles with `AsParallel().Sum()` several times, and compare the results to the last digit with a plain `Sum()`.
4. Write a parallel loop that finds all primes below 2,000,000 and collects them into a list, first with `List<int>` and a lock, then with PLINQ and `ToList`. Compare speed and code.
5. Measure how much of a real batch job in your work is serial, for example reading input and writing output. Use Amdahl's law to estimate the best speedup on 8 cores.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Resizing 10,000 boarding-pass images

A batch job resizes images one at a time and takes 40 minutes on an 8-core machine.

*Parallel CPU work, bounded*

```csharp
var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
await Parallel.ForEachAsync(Directory.EnumerateFiles(inputDir, "*.png"), options, async (file, token) =>
{
    using var image = await Image.LoadAsync(file, token);           // ImageSharp
    image.Mutate(x => x.Resize(600, 0));
    await image.SaveAsync(Path.Combine(outputDir, Path.GetFileName(file)), token);
});
```

> **Lesson:** CPU-bound batch work scales with cores. Bound the parallelism and keep each item independent.

#### Example 2: A bounded pipeline for an import

Reading a file is fast, validating is slow, and the reader fills memory with rows waiting to be validated.

*Channel with backpressure*

```csharp
var channel = Channel.CreateBounded<Row>(new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.Wait });

var producer = Task.Run(async () =>
{
    try { await foreach (var row in ReadRowsAsync(path, ct)) await channel.Writer.WriteAsync(row, ct); }
    finally { channel.Writer.Complete(); }                    // always complete, even on failure
});

var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
{
    await foreach (var row in channel.Reader.ReadAllAsync(ct)) await ValidateAndSaveAsync(row, ct);
}));
await Task.WhenAll(consumers.Append(producer));
```

> **Lesson:** A bounded channel makes a fast producer wait for slow consumers, so memory stays flat.

### Concurrency Primitives & Threading

<p class="src">From <em>.NET Backend Deep Dive</em> · Concurrency Primitives & Threading</p>

<a id="dn-41"></a>

#### `Channel<T>` — the modern producer/consumer

The async-era replacement for `BlockingCollection`, and extremely useful for background processing.

```csharp
// Registration
var channel = Channel.CreateBounded<EmailJob>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,   // backpressure: producers wait when full
    SingleReader = false,
    SingleWriter = false
});
builder.Services.AddSingleton(channel);
builder.Services.AddSingleton(channel.Reader);
builder.Services.AddSingleton(channel.Writer);
```

```csharp
// Producer (in a controller) - returns immediately, doesn't wait for the email
await _writer.WriteAsync(new EmailJob(userId, template), ct);
return Accepted();

// Consumer (BackgroundService)
protected override async Task ExecuteAsync(CancellationToken ct)
{
    // Several workers reading the same channel
    var workers = Enumerable.Range(0, 4).Select(async _ =>
    {
        await foreach (var job in _reader.ReadAllAsync(ct))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            try { await sender.SendAsync(job, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Email job failed {@Job}", job); }
        }
    });
    await Task.WhenAll(workers);
}
```

**Bounded vs unbounded:** always use **bounded**. Unbounded is a memory leak waiting for a producer that outpaces its consumer. Bounded gives you **backpressure** for free.

**Caveat:** channels are in-memory. A restart loses everything queued. Anything that matters (payments, orders) belongs in a durable queue plus an outbox.

### LINQ

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · LINQ</p>

<a id="be-m153"></a>

#### PLINQ: when parallel helps

``` csharp
var results = source.AsParallel()
                    .WithDegreeOfParallelism(4)
                    .Where(Expensive)
                    .Select(Transform)
                    .ToList();               // order NOT preserved
source.AsParallel().AsOrdered()              // preserves order, costs buffering
```

PLINQ partitions the source across thread-pool threads. It pays off only when **all** of these hold:

- the work per element is CPU-bound and substantial (microseconds, not nanoseconds);
- there are enough elements to amortise partitioning and merging (thousands, not dozens);
- the delegates are pure and thread-safe;
- the surrounding code is not already saturating the CPU with other requests.

> **\[Trap\]**
>
> **On a web server, PLINQ is usually wrong.** ASP.NET already uses every core to serve concurrent requests. Parallelising within a request steals threads from other requests and generally reduces total throughput while improving one latency number. Use it in batch jobs, importers, and CLI tools.

> **\[Tip\]**
>
> **For I/O-bound work, use async, not PLINQ.** `Parallel.ForEachAsync` (.NET 6+) with a bounded `MaxDegreeOfParallelism` is the right tool for "call this API for 10,000 items", and it does not block threads while waiting.

``` csharp
await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (id, ct) => await ProcessAsync(id, ct));
```

### Concurrency over shared data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Concurrency over shared data</p>

<a id="be-m135"></a>

#### Channels and async pipelines

`Channel<T>` is the modern async producer/consumer queue. It replaces `BlockingCollection`, and unlike it, waiting consumers do not block a thread.

``` csharp
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(1000) {
    FullMode = BoundedChannelFullMode.Wait,   // backpressure: producers slow down
    SingleReader = false, SingleWriter = false
});

// Producer
await channel.Writer.WriteAsync(item, ct);    // waits if full - this IS the backpressure
channel.Writer.Complete();                     // signals "no more items"

// Consumers - start several; each drains the same channel
await Parallel.ForEachAsync(
    channel.Reader.ReadAllAsync(ct),
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (item, token) => await ProcessAsync(item, token));
```

**Bounded is the default you want.** An unbounded channel with a producer faster than its consumers is a memory leak that looks like a working system until the process dies. A bound converts that into backpressure — the producer waits, and the queue depth becomes an observable metric rather than an invisible growth curve.

``` csharp
// A two-stage pipeline: read -> transform -> write, each stage independently parallel.
static ChannelReader<TOut> Stage<TIn, TOut>(
        ChannelReader<TIn> input, Func<TIn, CancellationToken, ValueTask<TOut>> work,
        int parallelism, CancellationToken ct) {
    var output = Channel.CreateBounded<TOut>(parallelism * 4);
    _ = Task.Run(async () => {
        try {
            await Parallel.ForEachAsync(input.ReadAllAsync(ct),
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
                async (item, token) => await output.Writer.WriteAsync(await work(item, token), token));
            output.Writer.Complete();
        } catch (Exception ex) { output.Writer.Complete(ex); }   // propagate, don't swallow
    }, ct);
    return output.Reader;
}
```

> **\[Trap\]**
>
> **Always complete the writer, including on failure.** `Complete(exception)` makes every consumer's `ReadAllAsync` throw that exception. Forgetting it leaves consumers awaiting forever — a hang with no stack trace and no CPU usage, which is one of the hardest failure modes to diagnose in production. Wrap the producer in try/catch/finally and complete in every path.

> **\[Tip\]**
>
> `BoundedChannelFullMode.DropOldest` gives you a lock-free, thread-safe ring buffer with async semantics — useful for telemetry and metrics where losing the oldest sample is preferable to slowing the producer ([Ring buffer: bounded, allocation-free](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m116)).

Worked example: [Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299)

Goes further: [Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249)

### Concurrency Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Concurrency Patterns</p>

<p class="pd">Patterns for work happening at the same time. Structures that make concurrency tractable, rather than locks scattered through a codebase.</p>

<a id="be-h776"></a>

#### Producer-Consumer

**Intent:** decouple the rate of production from the rate of consumption, with a bounded buffer between them. The most useful concurrency pattern, and the one that makes backpressure possible.

``` csharp
// System.Threading.Channels is the modern .NET implementation, and it is
// what you should reach for rather than BlockingCollection.
var channel = Channel.CreateBounded<Order>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,   // <- BACKPRESSURE. See below.
    SingleReader = false,
    SingleWriter = false,
});

// producer
await foreach (var order in source.ReadAllAsync(ct))
    await channel.Writer.WriteAsync(order, ct);   // BLOCKS when full
channel.Writer.Complete();

// consumers - several, competing for items
await Parallel.ForEachAsync(
    channel.Reader.ReadAllAsync(ct),
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (order, token) => await ProcessAsync(order, token));
```

``` text
  WHY BOUNDED, and this is the decision that matters

    UNBOUNDED
      producer 5,000/s -> [ queue ] -> consumer 1,000/s

      the queue grows by 4,000 items per second. Nothing errors.
      Everything looks healthy. An hour later there are 14 million
      items in memory and the process is OOM-killed.

    BOUNDED with FullMode.Wait
      the producer BLOCKS when the buffer is full.
      -> the slowdown propagates BACKWARDS to the source, which is
         exactly what you want. That propagation IS backpressure.

    BOUNDED with FullMode.DropOldest / DropWrite
      -> lossy, and appropriate for metrics or telemetry where a
         stale sample is worthless anyway.

  choosing unbounded is choosing to convert a throughput mismatch into
  an out-of-memory crash, later, under load.
```

| Mechanism                           | Use for                                                      |
|-------------------------------------|--------------------------------------------------------------|
| `Channel<T>`                        | In-process, async, with backpressure. **The default**        |
| `BlockingCollection<T>`             | Legacy synchronous code                                      |
| `ConcurrentQueue<T>`                | A queue with no coordination — you handle waiting            |
| A message broker                    | Cross-process, durable, survives a restart                   |
| A database table with `SKIP LOCKED` | Durable, transactional with your data, no new infrastructure |

###### Completion, cancellation and errors — the parts examples omit

``` csharp
// the three things a naive example gets wrong: completing the channel,
// propagating a producer failure, and not swallowing consumer exceptions.
var channel = Channel.CreateBounded<Order>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait,
});

// PRODUCER - complete in a finally, and pass the exception along
var producer = Task.Run(async () =>
{
    try
    {
        await foreach (var o in source.ReadAllAsync(ct))
            await channel.Writer.WriteAsync(o, ct);
        channel.Writer.Complete();                       // normal completion
    }
    catch (Exception ex)
    {
        channel.Writer.Complete(ex);                     // FAULT the channel
        // without this, consumers wait forever for items that will never
        // arrive, and the process hangs with no error anywhere.
        throw;
    }
}, ct);

// CONSUMERS - ReadAllAsync RETHROWS the fault, so failure propagates
var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
{
    await foreach (var order in channel.Reader.ReadAllAsync(ct))
    {
        try { await ProcessAsync(order, ct); }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed {OrderId}", order.Id);
            await deadLetter.WriteAsync(order, ct);      // do not kill the consumer
        }
    }
}, ct)).ToArray();

await Task.WhenAll([producer, .. consumers]);
```

| Failure                           | Symptom                   | Cause                                           |
|-----------------------------------|---------------------------|-------------------------------------------------|
| Consumers hang at shutdown        | The process never exits   | `Writer.Complete()` never called                |
| A producer error is swallowed     | Silent partial processing | `Complete(ex)` not used                         |
| One bad item kills a consumer     | Throughput silently drops | No per-item `try/catch`                         |
| Memory grows without bound        | OOM under load            | Unbounded channel                               |
| The producer never blocks         | Backpressure absent       | `FullMode.DropWrite` chosen by accident         |
| Cancellation does not take effect | Shutdown times out        | Token not passed to `WriteAsync`/`ReadAllAsync` |

A design note on error policy: **an item-level failure must not terminate the consumer**, and a *systemic* failure must. Catching everything per item means a database outage produces a million logged failures and an empty result; catching nothing means one malformed record halts the pipeline. The usual resolution is a circuit breaker around the consumer — per-item failures dead-letter, and a burst of consecutive failures stops the consumer deliberately.

<a id="be-h777"></a>

#### Pipeline and Worker Pool

##### Pipeline

**Intent:** break processing into stages connected by queues, so each stage runs concurrently and can be scaled independently. Each stage is a producer for the next.

``` csharp
// each stage reads from one channel and writes to the next
static ChannelReader<Parsed> ParseStage(ChannelReader<Raw> input, CancellationToken ct)
{
    var output = Channel.CreateBounded<Parsed>(100);
    _ = Task.Run(async () =>
    {
        await foreach (var raw in input.ReadAllAsync(ct))
            await output.Writer.WriteAsync(Parse(raw), ct);
        output.Writer.Complete();
    }, ct);
    return output.Reader;
}

var parsed    = ParseStage(readStage, ct);
var validated = ValidateStage(parsed, ct);
var saved     = SaveStage(validated, ct);

// the value: each stage has its own concurrency and its own bounded
// buffer, so a slow stage does not force the fast ones to allocate
// unboundedly - the backpressure propagates all the way to the source.
```

##### Worker pool

**Intent:** a fixed set of workers pulling from a shared queue. It bounds concurrency, which is the point — unbounded parallelism is how you exhaust a connection pool or a downstream service.

``` csharp
// bounded parallelism over an async source - the shape to reach for
await Parallel.ForEachAsync(items, new ParallelOptions
{
    MaxDegreeOfParallelism = 8,      // BOUNDED. Never leave this unset for
                                     // work that touches a database or an API.
    CancellationToken = ct,
}, async (item, token) => await ProcessAsync(item, token));

// the anti-pattern this replaces, and it appears constantly:
var tasks = items.Select(ProcessAsync);      // 10,000 concurrent operations
await Task.WhenAll(tasks);                    // ...all at once
// -> exhausts the connection pool, overwhelms the downstream service,
//    and the failure looks like the DEPENDENCY being broken.
```

| Concurrency limit should be       | Because                                              |
|-----------------------------------|------------------------------------------------------|
| Roughly the downstream's capacity | More just queues at the far end, with worse latency  |
| Bounded by your connection pool   | Otherwise the pool becomes the bottleneck, invisibly |
| Configurable                      | The right value differs per environment              |
| Measured, not guessed             | The knee is where latency rises; find it             |

###### A complete import pipeline, with backpressure and error isolation

``` csharp
// a realistic three-stage pipeline: read a supplier's CSV, validate and
// enrich, then upsert. Each stage bounded, each stage isolated.
public sealed class SupplierCatalogueImporter(
    IProductLookup lookup, AppDbContext db, ILogger<SupplierCatalogueImporter> log)
{
    public async Task<ImportReport> RunAsync(Stream csv, CancellationToken ct)
    {
        var parsed    = Channel.CreateBounded<RawRow>(new BoundedChannelOptions(1_000)
                                { FullMode = BoundedChannelFullMode.Wait });
        var validated = Channel.CreateBounded<ProductUpsert>(new BoundedChannelOptions(500)
                                { FullMode = BoundedChannelFullMode.Wait });

        var report = new ImportReport();

        // STAGE 1 - parse. One producer, and it must COMPLETE the channel.
        var parse = Task.Run(async () =>
        {
            try
            {
                using var reader = new StreamReader(csv);
                await foreach (var row in ReadRowsAsync(reader, ct))
                    await parsed.Writer.WriteAsync(row, ct);   // BLOCKS when full
                parsed.Writer.Complete();
            }
            catch (Exception ex)
            {
                parsed.Writer.Complete(ex);   // FAULT it, or consumers wait forever
                throw;
            }
        }, ct);

        // STAGE 2 - validate and enrich. Several workers, bounded.
        var validate = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    parsed.Reader.ReadAllAsync(ct),
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
                    async (row, token) =>
                    {
                        try
                        {
                            var upsert = await ValidateAndEnrichAsync(row, token);
                            await validated.Writer.WriteAsync(upsert, token);
                        }
                        catch (ValidationException ve)
                        {
                            // ITEM-level failure must not kill the stage
                            report.RecordRejected(row.LineNumber, ve.Message);
                        }
                    });
                validated.Writer.Complete();
            }
            catch (Exception ex) { validated.Writer.Complete(ex); throw; }
        }, ct);

        // STAGE 3 - persist in BATCHES. One writer, because the DbContext
        // is not thread-safe and batching is what makes this fast.
        var persist = Task.Run(async () =>
        {
            var batch = new List<ProductUpsert>(capacity: 500);
            await foreach (var item in validated.Reader.ReadAllAsync(ct))
            {
                batch.Add(item);
                if (batch.Count == 500) { await FlushAsync(batch, report, ct); batch.Clear(); }
            }
            if (batch.Count > 0) await FlushAsync(batch, report, ct);
        }, ct);

        await Task.WhenAll(parse, validate, persist);
        return report;
    }
}
```

``` text
  WHY EACH DECISION IS WHAT IT IS

    BOUNDED CHANNELS (1,000 and 500)
      a 2 GB supplier file cannot be held in memory. With FullMode.Wait,
      the parser BLOCKS when the validators fall behind, and that
      backpressure propagates all the way to the file read.
      unbounded here = OOM on a large file, discovered in production.

    PARALLELISM 8 ON STAGE 2, ONE ON STAGE 3
      stage 2 does lookups - I/O bound, benefits from concurrency.
      stage 3 writes through ONE DbContext, which is not thread-safe.
      matching parallelism to the constraint, not to the core count.

    BATCHING IN STAGE 3
      500 individual SaveChanges calls = 500 round trips.
      one batched call = one round trip. This is usually the single
      largest factor in import throughput.

    Complete(ex) ON FAILURE
      without it, a parser crash leaves consumers awaiting items that
      will never arrive. The task hangs, no error surfaces, and the
      import appears to run forever.

    ITEM-LEVEL try/catch
      one malformed row out of 400,000 must not fail the import. It
      is recorded and skipped. But a DATABASE outage should stop -
      hence the catch is on ValidationException specifically, not on
      Exception.
```

| Tuning knob          | Set from                                                        |
|----------------------|-----------------------------------------------------------------|
| Channel capacity     | Memory budget ÷ item size                                       |
| Stage parallelism    | The constraint of that stage — I/O, pool size, or thread safety |
| Batch size           | Round-trip cost vs transaction size and lock duration           |
| Where to catch       | Item-level for data faults; let systemic faults propagate       |
| Bounded vs unbounded | **Always bounded**, unless the source is known-small            |

<a id="be-h778"></a>

#### Actor model

**Intent:** an object owns some state, nobody else may touch it, and all interaction is by message processed one at a time. The consequence: **no locks are needed for that state, ever**.

``` text
  what it actually solves

    PER-ENTITY CONCURRENCY

      one actor per order, per device, per game session, per user.

      concurrent requests for THE SAME entity queue naturally, because
      the actor processes one message at a time.
      requests for DIFFERENT entities run fully in parallel.

      -> you get correct per-entity serialisation with no locking code,
         and full parallelism across entities, from one property.

  what it does NOT solve
    the state still has to be PERSISTED somewhere
    a cluster still has to be operated
    debugging becomes message-flow archaeology
```

``` csharp
// Orleans "virtual actors": you never create or destroy a grain. You
// address one by id and the runtime activates it on demand, somewhere.
public class OrderGrain([PersistentState("order")] IPersistentState<OrderState> state)
    : Grain, IOrderGrain
{
    public async Task<Result> AddLineAsync(LineDto line)
    {
        // NO LOCKING. Orleans guarantees single-threaded execution per grain,
        // so two concurrent callers for THIS order are serialised and two
        // callers for DIFFERENT orders run in parallel.
        if (state.State.Status != "Draft") return Error.Conflict();
        state.State.Lines.Add(line);
        await state.WriteStateAsync();
        return Result.Ok();
    }
}

var grain = grainFactory.GetGrain<IOrderGrain>(orderId);   // location is the
await grain.AddLineAsync(line);                             // runtime's problem
```

| Actors fit                                           | Actors are overkill                             |
|------------------------------------------------------|-------------------------------------------------|
| High contention on individual entities               | A database row lock handles it fine             |
| Many small stateful entities — IoT, gaming, sessions | Stateless request/response CRUD                 |
| State worth keeping hot in memory                    | State that must be durable and queryable anyway |
| Complex per-entity state machines                    | A status column and three transitions           |
| Location transparency across a cluster               | One process is enough                           |

> **\[Note\]**
>
> **Ask what the database already gives you.** Much of what actors provide — serialised access to one entity, consistent state, durability — a relational database provides with a row lock or an optimistic concurrency token, with no new runtime to operate. Actors earn their place when the entity count is very large, the state is expensive to reload, or the per-entity logic is a genuine state machine.

###### Persistence, and what happens when a grain moves

``` text
  THE PART EXAMPLES OMIT: an actor's state must survive

    an actor holds state IN MEMORY. That is the performance benefit.
    the node dies -> the state is gone unless it was persisted.

  THREE STRATEGIES

    WRITE-THROUGH        persist on every state change
      + no loss
      - every operation pays a write. The in-memory benefit is
        reduced to avoiding the READ.

    WRITE-BEHIND         persist periodically or on deactivation
      + fast
      - LOSS on an ungraceful shutdown, and "ungraceful" includes an
        OOM kill or a node failure - the cases you most need to survive

    EVENT-SOURCED        append events; rebuild state on activation
      + no loss, full history
      - activation cost grows with the stream, so you need SNAPSHOTS

  and the activation cost matters operationally: a grain that has not
  been used is activated ON DEMAND, which means the FIRST call after
  a restart or a rebalance pays the full load. With 100,000 grains
  reactivating after a deployment, that is a stampede against the
  store - the same shape as a cache stampede, for the same reason.
```

``` csharp
public class OrderGrain([PersistentState("order", "orders-store")]
                        IPersistentState<OrderState> state) : Grain, IOrderGrain
{
    public async Task<Result> AddLineAsync(LineDto line)
    {
        if (state.State.Status != "Draft") return Error.Conflict();
        state.State.Lines.Add(line);
        await state.WriteStateAsync();          // WRITE-THROUGH
        return Result.Ok();
    }

    // write-behind: persist on deactivation instead
    public override async Task OnDeactivateAsync(DeactivationReason reason,
                                                 CancellationToken ct)
    {
        if (_dirty) await state.WriteStateAsync();
        await base.OnDeactivateAsync(reason, ct);
        // NOTE: this does NOT run on a process crash or an OOM kill.
        // write-behind trades durability for speed, and the failure
        // modes it loses to are exactly the unplanned ones.
    }
}
```

| Actor concern             | Reality                                                                |
|---------------------------|------------------------------------------------------------------------|
| Single-threaded per actor | True — and it means one slow call blocks that actor's queue entirely   |
| Location transparency     | True — and a call may now be a network hop, with network failure modes |
| No locking needed         | For *that actor's* state. Anything shared still needs it               |
| Actors scale horizontally | True — if the key distribution is even. A hot actor is a hot partition |
| Reentrancy                | Off by default. A grain calling a grain that calls back **deadlocks**  |
| Timers and reminders      | Timers die with the activation; reminders are durable                  |

The reentrancy row is the most common Orleans bug. Grain A calls grain B, which calls back into A — and A is still blocked on its first call, so the second waits forever. Mark grains `[Reentrant]` only when you have reasoned about the interleaving, and prefer designs where calls flow in one direction.

<a id="be-h779"></a>

#### Async patterns, and the ones that are traps

| Pattern              | Shape                                   | Use for                                         |
|----------------------|-----------------------------------------|-------------------------------------------------|
| **Task-based async** | `Task<T> DoAsync(CancellationToken ct)` | Everything. The standard                        |
| **Async streams**    | `IAsyncEnumerable<T>`                   | Streaming results without buffering all of them |
| **Cancellation**     | `CancellationToken` threaded through    | Every operation that can be abandoned           |
| **Fan-out / fan-in** | `Task.WhenAll` over **bounded** work    | Independent parallel calls                      |
| **Async lazy**       | `Lazy<Task<T>>`                         | One-time async initialisation, shared           |
| **Periodic work**    | `PeriodicTimer`                         | Loops that must not overlap                     |

``` csharp
// the four that reliably cause production incidents

// 1. SYNC OVER ASYNC - deadlock, or thread-pool starvation
var result = GetAsync().Result;          // never
GetAsync().Wait();                        // never
// async all the way down. If you cannot, you have found a design problem.

// 2. UNBOUNDED FAN-OUT
await Task.WhenAll(items.Select(ProcessAsync));   // 10,000 at once
// -> Parallel.ForEachAsync with MaxDegreeOfParallelism

// 3. FIRE AND FORGET
_ = DoWorkAsync();                        // exceptions vanish; shutdown kills it
// -> a hosted service, a channel, or a real background job system

// 4. ASYNC VOID
public async void Handler(object s, EventArgs e)  // exceptions CRASH the process
// -> async Task, always, except for genuine event handlers - and even
//    then, wrap the body in try/catch.
```

``` text
  ASYNC LAZY - the one worth knowing, because the naive version is wrong

    the problem: expensive async initialisation, needed by many callers,
    must happen exactly once.

    WRONG: a bool flag and an if - two callers can both see false.

    RIGHT:
      private readonly Lazy<Task<Config>> _config =
          new(() => LoadConfigAsync(), LazyThreadSafetyMode.ExecutionAndPublication);

      public Task<Config> GetAsync() => _config.Value;

    Lazy guarantees the factory runs ONCE; the Task is then shared by
    every caller, and every caller awaits the same result.

    note: if the task FAILS, the failure is cached too. For anything
    that should be retried, reset the Lazy on failure or use a
    dedicated async-lazy type that handles it.
```

> **\[Note\]**
>
> **The one-sentence version.** `await` means *“this will take a while and I am waiting on something else — give my worker to somebody else”*. Every trap above comes from breaking that: blocking holds the worker, unbounded fan-out takes every worker, fire-and-forget abandons the result, and `async void` loses the exception.

###### Cancellation done properly, and where tokens get dropped

``` csharp
// a token is only useful if it reaches the thing that can be cancelled.
// it is dropped silently at every layer that forgets to pass it.

public async Task<Order?> GetAsync(Guid id, CancellationToken ct)
{
    // WRONG - the token stops here, and the query runs to completion
    var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);

    // RIGHT
    var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    // and every layer down: HttpClient, streams, Task.Delay, channels
    await _http.GetAsync(url, ct);
    await stream.ReadAsync(buffer, ct);
    await Task.Delay(1000, ct);
    await channel.Writer.WriteAsync(item, ct);
}

// combining a caller's token with a local timeout
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(5));
await DoWorkAsync(cts.Token);        // cancels on EITHER the caller
                                     // cancelling OR 5 seconds elapsing
```

``` text
  WHERE CANCELLATION SHOULD AND SHOULD NOT REACH

    SHOULD
      any I/O - database, HTTP, file, socket
      long loops - check ct.IsCancellationRequested each iteration
      queue waits and channel reads
      Task.Delay, always

    SHOULD NOT
      COMPENSATION and CLEANUP.
        cancelling the cleanup after a failure leaves the system in a
        worse state than the failure did.

          catch
          {
              await CompensateAsync(CancellationToken.None);   // deliberate
          }

      the final write of a unit of work that has already had side effects
      logging and telemetry for a failure

  and the shutdown consideration: in a hosted service, the token is
  cancelled at shutdown. Work that MUST complete needs either a
  separate token or a longer ShutdownTimeout - the default is FIVE
  SECONDS, after which the process is killed regardless.
```

| Symptom                                          | Cause                                                                         |
|--------------------------------------------------|-------------------------------------------------------------------------------|
| Cancelling has no effect                         | The token was not passed to the I/O call                                      |
| `OperationCanceledException` logged as an error  | Normal shutdown treated as a failure — filter on `ct.IsCancellationRequested` |
| Shutdown takes the full timeout, then hard-kills | A loop not checking the token                                                 |
| A half-completed operation after cancellation    | Cancellation between two writes with no transaction                           |
| Cleanup skipped on cancellation                  | `ct` passed to compensation — use `CancellationToken.None`                    |
