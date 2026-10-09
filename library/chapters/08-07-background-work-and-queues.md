---
id: "8-7"
number: "8.7"
title: "Background work and queues"
part: 8
part_title: "Building a backend"
words: 7639
minutes: 33
---

# 8.7 Background work and queues

> **Quick look.** Moving work the user doesn't need to wait for out of the request, without losing it.
>
> - Hosted services and channels suit in-process work that can be lost on restart.
> - Work that must happen needs a durable queue, fed through an outbox in the same transaction as the data.
> - Brokers deliver at least once: make consumers idempotent by recording message ids with the work.
> - Retry transient failures with backoff; move poison messages to a dead-letter queue and alert on it.
> - BackgroundService is a singleton: create a scope per iteration and catch exceptions, or the worker dies silently.
> - Scheduled jobs must run once across instances (lock or leader), in UTC, and be safe to repeat; DST breaks local-time schedules.


### The big picture

A passenger who books a flight needs an answer in under a second: booked, or not. They don't need to wait while the backend emails a confirmation, generates a PDF itinerary, updates the loyalty system and notifies the airport. Doing all of that inside the request makes it slow, and makes it fail whenever any one of those systems is slow or down. So backends move such work out of the request: they record that it needs doing, reply straight away, and do it in the background.

This chapter covers how. It starts with background work inside the process, then moves to durable message queues, which survive crashes and let work move between services. The hard part is that messages can be delivered more than once, or fail repeatedly, so the chapter explains delivery guarantees, consumers that handle duplicates safely, retries with backoff, and dead-letter queues for messages that never succeed. It ends with scheduled jobs. The example runs two workers against a queue with duplicates and failures, so you can watch each technique do its job. It ends Part 8.

### Before you start

**Builds on:** 8.1 What a backend does, 8.3 A modern .NET backend, 5.3 Async and await, 2.8 Errors and exceptions, 7.4 Transactions and isolation, 8.2 API design.

**Ideas explained later:**

- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Lease** (12.2): A lock that expires after a set time unless renewed.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Kubernetes** (11.3): The dominant container orchestrator, which reconciles declared desired state across a cluster.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Monitoring** (11.6): Watching a system for known problems, such as an error rate above a threshold.

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, LINQ, switch expressions and patterns, tuples, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why isn't a Channel inside your process enough for sending booking confirmations?**

> It lives in memory. If the process crashes or restarts during a deployment, every queued message is lost. Work that must happen needs a durable queue, or an outbox table, that survives restarts.

**2. What do at-most-once, at-least-once and exactly-once delivery mean?**

> At-most-once: a message may be lost but never repeated. At-least-once: it is never lost but may be delivered more than once. Exactly-once can't be guaranteed across a network in general; systems get the same effect with at-least-once delivery and consumers that ignore duplicates.

**3. What is an idempotent consumer?**

> One that can receive the same message twice with the same result as once, usually by recording each message id it has processed, in the same transaction as the work, and skipping ids it has seen.

**4. What is a dead-letter queue?**

> A separate queue where messages go after failing too many times. They stop blocking the main queue, and people can inspect, fix and replay them.

**5. What is a visibility timeout?**

> When a consumer takes a message, the queue hides it from others for a set time. If the consumer doesn't confirm success within that time, because it crashed or took too long, the message becomes visible again and is redelivered.

**6. You run three instances of a service with a nightly report job. What goes wrong, and how do you prevent it?**

> All three run the job, producing three reports or triple the work. Use a scheduler that coordinates across instances, a distributed lock, or run scheduled jobs in one dedicated place.

**7. What is the difference between a queue and a topic?**

> In a queue, each message goes to one consumer, which suits sharing work between workers. In a topic, each message goes to every subscriber, which suits announcing events to several services.

### Why move work out of the request

What a backend does (8.1) gave every request a time budget. Work that the user doesn't need to see finished before getting an answer shouldn't spend that budget. Sending emails and push notifications, generating documents, resizing images, updating search indexes, calling slow partner systems and recalculating statistics are all typical **background jobs**.

Moving them out does more than save time. The request no longer fails because the email server is down; the email is simply sent a little later. Work that arrives in bursts can be processed at a steady pace. And failed work can be retried without the user retrying the whole request.

### Background work inside the process

The simplest form runs inside the same application. In .NET, a **hosted service** is a class the generic host starts with the app and stops when the app shuts down, from A modern .NET backend (8.3). Deriving from `BackgroundService` and overriding one method, `ExecuteAsync`, is enough:

```csharp
// Registered with builder.Services.AddHostedService<ConfirmationSender>();
sealed class ConfirmationSender(Channel<Booking> queue, ILogger<ConfirmationSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Booking booking in queue.Reader.ReadAllAsync(stoppingToken))   // ends when the app stops
        {
            try { await SendEmailAsync(booking, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Confirmation for {BookingId} failed", booking.Id);  // one failure mustn't stop the loop
            }
        }
    }

    Task SendEmailAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;   // stands in for the real work
}
```

A request handler writes to the channel, from Async and await (5.3), and returns immediately. The service reads from it and does the work. A bounded channel applies backpressure if work arrives faster than it is done. Catching exceptions inside the loop matters: since .NET 6, an exception that escapes `ExecuteAsync` stops the whole host by default, as Errors and exceptions (2.8) noted.

The weakness is that the channel lives in memory. A crash, or even a normal deployment, loses whatever was queued. That is acceptable for work that can be recomputed, such as warming a cache. For work that must happen, such as a confirmation email or a refund, the queue must be durable.

### Durable queues and brokers

A **message queue** is a durable list of messages: producers add them, consumers take them, and the queue keeps them safely on disk until they are processed. A **message broker** is the server that hosts queues and delivers messages. Common ones are RabbitMQ, Azure Service Bus and Amazon SQS. Apache Kafka works differently: it stores messages as an ordered, replayable log, which consumers read at their own position, and suits streams of events at very high volume.

Brokers usually offer two shapes. In a queue, each message is delivered to one consumer, which suits sharing work among several workers. In a **topic**, each message is delivered to every subscriber, which suits announcing that something happened, such as "booking created", to the email, loyalty and analytics services at once. Messaging and events (12.3) covers designing systems around events.

To get a message into a queue reliably along with a database change, use the transactional outbox from Transactions and isolation (7.4): write the message to an outbox table in the same transaction, and let a relay publish it.

### Delivery guarantees

Networks and processes fail at the worst moments, so a broker has to decide what happens when a consumer takes a message and then goes quiet.

Most brokers use a **visibility timeout**, also called a lock or lease. When a consumer receives a message, the broker hides it from other consumers for a set time. The consumer acknowledges it once processing succeeds, and the broker deletes it. If no acknowledgement arrives in time, because the consumer crashed, hung or simply took too long, the message becomes visible again and is delivered once more.

That gives **at-least-once delivery**: a message is never lost, but may be delivered more than once. A consumer that did the work and then crashed before acknowledging will see the message again. So will one whose work took longer than the timeout. At-least-once is what almost every broker provides by default.

The alternative, **at-most-once delivery**, acknowledges before doing the work. Nothing is ever repeated, but a crash in the middle loses the message. It suits only work that doesn't matter much.

What everyone wants is exactly-once. Across a network, a broker can't guarantee it in general: it can never be sure whether a silent consumer finished. Systems achieve **exactly-once processing** instead: at-least-once delivery, plus consumers that recognize and skip duplicates, so the effect happens exactly once.

### Idempotent consumers

An **idempotent consumer** gives the same result whether a message arrives once or five times, applying the idempotency idea from Errors and exceptions (2.8) to messages.

Some work is naturally idempotent: "set the gate for BA117 to B12" can be applied twice harmlessly. Most isn't: "send a confirmation email" or "add 500 loyalty points" does damage when repeated. For those, give every message a unique id when it is created, and have the consumer record each id it processes, in a table with a unique constraint, in the same database transaction as the work itself. A duplicate then either finds its id already recorded and is skipped, or fails the unique constraint and rolls back. Recording the id separately from the work, or after it, reopens the gap for duplicates.

Work that leaves the database, such as sending an email through a provider, can't join a database transaction. Pass the message id to the provider as its idempotency key, from API design (8.2), where the provider supports one.

### Retries and dead letters

A message whose processing fails is usually retried, and many failures are transient, as Errors and exceptions (2.8) explained: a timeout, a deadlock, a partner briefly unavailable. Retry with exponential backoff, so a struggling dependency isn't hammered.

Some messages will never succeed, whatever happens: a booking for a flight that doesn't exist, or a body the consumer can't parse. Such a **poison message** would be retried forever, wasting capacity, and in a strictly ordered queue it blocks everything behind it. So after a set number of attempts, the message is moved to a **dead-letter queue**: a separate queue for messages that couldn't be processed. There they wait for a person to look at them, fix the cause and replay them. A growing dead-letter queue is one of the most important alerts a system has, because each message in it is work that didn't happen.

### Competing consumers

Several workers reading from the same queue share the work between them automatically, each taking the next available message. These are **competing consumers**. Adding workers increases throughput, and a crashed worker's messages are redelivered to the others after their visibility timeout. Many platforms can scale the number of workers by queue length.

The cost is ordering. With several consumers, messages are processed roughly, not strictly, in order: message 2 can finish before message 1. Sometimes order matters for related messages, such as all changes to one booking. For that, brokers offer ordered partitions or sessions keyed by an id. Messages with the same key go to one consumer in order, while different keys still spread across consumers.

### Seeing it for real

This program runs two competing workers against a queue that contains a duplicate delivery, a message that fails once and one that always fails. It shows idempotent skipping, retries with backoff and a dead-letter queue. The queue is an in-memory channel, so the program runs anywhere; with a real broker, redelivery and dead-lettering are done by the broker itself.

```csharp
// queues.cs — run with: dotnet run queues.cs
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;

var queue = Channel.CreateUnbounded<Message>();
var deadLetters = new ConcurrentQueue<Message>();
var processed = new ConcurrentDictionary<string, bool>();    // ids already handled: the consumer's idempotency record
var sent = new ConcurrentBag<string>();
var failuresSoFar = new ConcurrentDictionary<string, int>();
int outstanding;                                              // deliveries not yet finished, for a clean stop
var allDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

Message[] incoming =
[
    new("m1", "BA117 Ada"),
    new("m2", "QF1 Grace"),       // fails once, then succeeds when retried
    new("m1", "BA117 Ada"),       // the same message again: at-least-once delivery can do this
    new("m3", "XX999 Linus"),     // can never succeed: ends in the dead-letter queue
    new("m4", "SQ321 Alan"),
];
outstanding = incoming.Length;
foreach (var message in incoming) await queue.Writer.WriteAsync(message);

using var stop = new CancellationTokenSource();
Task[] workers = Enumerable.Range(1, 2).Select(n => Task.Run(() => Worker($"worker {n}", stop.Token))).ToArray();   // competing consumers
await allDone.Task;                                           // wait until every delivery has finished
stop.Cancel();                                                // graceful shutdown: workers leave their loops
await Task.WhenAll(workers);

Console.WriteLine($"confirmations sent: {string.Join(", ", sent.Order())}");   // BA117 Ada, QF1 Grace, SQ321 Alan
Console.WriteLine($"dead letters: {string.Join(", ", deadLetters.Select(m => m.Id))}");   // m3
// A typical run, from the two workers (the exact order varies):
//   worker 1: m1 done on attempt 1
//   worker 2: m2 failed (mail server timed out); retrying in 50 ms
//   worker 1: m1 already handled, skipped
//   worker 2: m3 failed (unknown flight); retrying in 50 ms
//   worker 1: m4 done on attempt 1
//   worker 2: m2 done on attempt 2
//   worker 1: m3 failed (unknown flight); retrying in 100 ms
//   worker 2: m3 dead-lettered after 3 attempts
//   worker 1: stopping
//   worker 2: stopping

async Task Worker(string name, CancellationToken ct)
{
    try
    {
        await foreach (Message message in queue.Reader.ReadAllAsync(ct))
        {
            if (!processed.TryAdd(message.Id, true)) { Console.WriteLine($"{name}: {message.Id} already handled, skipped"); Finished(); continue; }
            try
            {
                await SendConfirmationAsync(message);        // the real work
                Console.WriteLine($"{name}: {message.Id} done on attempt {message.Attempt}");
                Finished();
            }
            catch (Exception ex)
            {
                processed.TryRemove(message.Id, out _);       // it wasn't handled after all: allow a retry
                if (message.Attempt >= 3)
                {
                    deadLetters.Enqueue(message);             // give up, but keep it for a person to inspect
                    Console.WriteLine($"{name}: {message.Id} dead-lettered after {message.Attempt} attempts");
                    Finished();
                }
                else
                {
                    var delay = TimeSpan.FromMilliseconds(50 * Math.Pow(2, message.Attempt - 1));   // exponential backoff
                    Console.WriteLine($"{name}: {message.Id} failed ({ex.Message}); retrying in {delay.TotalMilliseconds} ms");
                    _ = Task.Delay(delay).ContinueWith(_ => queue.Writer.TryWrite(message with { Attempt = message.Attempt + 1 }));
                }
            }
        }
    }
    catch (OperationCanceledException) { Console.WriteLine($"{name}: stopping"); }
}

async Task SendConfirmationAsync(Message message)
{
    await Task.Delay(20);                                     // stands in for calling a mail service
    if (message.Body.StartsWith("XX")) throw new InvalidOperationException("unknown flight");
    if (message.Body.StartsWith("QF1") && failuresSoFar.AddOrUpdate(message.Id, 1, (_, n) => n + 1) == 1)
        throw new TimeoutException("mail server timed out");  // a transient failure, first time only
    sent.Add(message.Body);
}

void Finished() { if (Interlocked.Decrement(ref outstanding) == 0) allDone.TrySetResult(); }

record Message(string Id, string Body, int Attempt = 1);
```

Notice what the idempotency record protects against: the second copy of `m1` is skipped, so Ada gets one confirmation, not two. In a real system that record would be a database table, written in the same transaction as the work.

### Scheduled jobs

Some work runs on a timetable rather than in response to a message: nightly reports, clearing expired seat holds every minute, monthly statements. A **scheduled job** runs at set times, usually described by a **cron expression**, a compact schedule format from Unix systems: `0 2 * * *` means "every day at 02:00". Libraries such as Quartz.NET and Hangfire run scheduled jobs in .NET; cloud platforms and Kubernetes offer them too.

Three problems come up. First, with several instances of a service, each would run the job. Run it in one place, or have instances take a **distributed lock**: a lock held in a shared store, such as a database or Redis, so that only one instance runs the job. Second, time zones: "02:00 local time" happens twice, or not at all, on the nights clocks change, as Numbers inside the machine (1.2) warned, so schedule in UTC unless the business really means local time. Third, missed runs: if the system was down at 02:00, decide whether the job should run late or wait for the next day, and make jobs safe to run twice, because sometimes they will.

### Watching the queues

Background work fails quietly. Nobody sees a confirmation email that was never sent. So monitor the queues themselves: the number of messages waiting, the age of the oldest message, processing time, the failure rate and the size of the dead-letter queue. The age of the oldest message is often the best single signal: a queue whose oldest message is growing older is falling behind, whatever its length. Observability (11.6) covers collecting these measurements.

### In short

Move work the user doesn't need to wait for out of the request: it makes responses fast and isolates failures. Hosted services and channels suit in-process work that can be lost; work that must happen needs a durable message queue, fed through an outbox. Brokers deliver at least once, so messages can arrive twice; make consumers idempotent by recording message ids in the same transaction as the work. Retry transient failures with backoff, move poison messages to a dead-letter queue and alert on it. Competing consumers scale out at the cost of strict order. Run scheduled jobs once, in UTC, and make them safe to repeat.

### What goes wrong

**Important work in memory.** A deployment restarts the app and queued confirmations are lost. Use a durable queue or an outbox for anything that must happen.

**Duplicate side effects.** A redelivered message sends a second email or adds points twice. Make consumers idempotent, recording message ids with the work in one transaction.

**Poison messages retried forever.** One bad message consumes a worker endlessly, or blocks an ordered queue. Limit attempts and dead-letter.

**Nobody watching the dead-letter queue.** Thousands of failed refunds sit unprocessed for weeks. Alert on its size, and have a process for replaying messages.

**Work longer than the visibility timeout.** A slow job is redelivered to a second worker while the first is still running it. Extend the lock while working, or keep jobs short.

**Background loops that die silently.** An exception escapes a `BackgroundService` and stops the host, or is swallowed and stops the loop. Catch per item, log, and keep going.

**Scheduled jobs on every instance.** Three instances send three copies of every nightly report. Coordinate with a distributed lock or a single scheduler.

**Schedules in local time.** A job at 01:30 local time runs twice or not at all on clock-change nights. Schedule in UTC.

### Cost model

Writing a message to a durable broker costs a network round trip and a disk write on the broker, typically a few milliseconds, and far less than doing the work inside the request. In-process channels cost microseconds. Throughput scales with the number of competing consumers until the work's own dependency, often the database, becomes the limit. Each retry adds its backoff delay to that message's completion time, and an idempotency check adds one indexed lookup per message. The cheapest reliability measure of all is monitoring queue age, which catches problems before customers do.

### In other languages (optional)

**JavaScript and TypeScript.** BullMQ runs job queues on Redis for Node.js, with retries, delays and scheduled jobs. Broker clients exist for RabbitMQ, SQS, Service Bus and Kafka.

**Python.** Celery is the long-standing task queue, usually backed by RabbitMQ or Redis. Newer options include Dramatiq and RQ.

**Java.** Spring supports RabbitMQ, Kafka and JMS listeners, with retry and dead-letter configuration.

**Serverless.** Azure Functions, AWS Lambda and similar platforms can run code directly when a message arrives, handling scaling, retries and dead-lettering for you, with the same need for idempotent handlers.

### Key terms

- **Background job**: Work done outside the request that triggered it, so the user doesn't wait for it.
- **Hosted service**: A class the .NET host starts with the app and stops when the app shuts down.
- **Message queue**: A durable list of messages that producers add to and consumers take from.
- **Message broker**: The server that hosts queues and topics and delivers messages.
- **Topic**: A channel where each message is delivered to every subscriber.
- **Visibility timeout**: How long a received message stays hidden from other consumers before it is redelivered.
- **At-least-once delivery**: A guarantee that messages are never lost, but may be delivered more than once.
- **At-most-once delivery**: A guarantee that messages are never repeated, but may be lost.
- **Exactly-once processing**: Getting each message's effect exactly once, through at-least-once delivery and duplicate detection.
- **Idempotent consumer**: A consumer that gives the same result whether a message arrives once or several times.
- **Poison message**: A message that fails every time it is processed.
- **Dead-letter queue**: A queue for messages that failed too many times, kept for inspection and replay.
- **Competing consumers**: Several workers reading one queue and sharing its messages.
- **Scheduled job**: Work that runs at set times rather than in response to a message.
- **Cron expression**: A compact format describing a schedule, such as 0 2 * * * for every day at 02:00.
- **Distributed lock**: A lock held in a shared store so that only one instance across many does something.

### Where this leads

This ends Part 8. Part 9 turns to the frontend, starting with How a browser shows a page (9.1). Designing systems around events, sagas and the outbox is in Messaging and events (12.3). Timeouts, retries and circuit breakers across services are in Reliability (12.5), and monitoring queues is part of Observability (11.6).

### Check yourself

1. In `queues.cs`, change the maximum attempts from 3 to 5 and predict the log lines for `m3` before running it.
2. Remove the idempotency record and run the program. Count the confirmations sent, and explain which one is a duplicate.
3. Add a third worker and run the program several times. Note whether the order of completions changes, and whether any result changes.
4. Write a `BackgroundService` in an ASP.NET Core app that reads booking ids from a bounded `Channel` and logs each one, with an endpoint that writes to it.
5. For a system you know, list the work done inside requests that could move to the background, and for each one decide whether losing it on a crash would matter.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Confirmation emails lost on restart

Emails are queued in an in-memory Channel. A deploy restarts the pod and 300 confirmations are never sent.

*Outbox in the same transaction*

```csharp
db.Bookings.Add(booking);
db.Outbox.Add(OutboxMessage.From(new SendConfirmation(booking.Id, booking.Email)));
await db.SaveChangesAsync(ct);      // booking and message commit together or not at all

// a background relay reads unsent outbox rows, publishes them, marks them sent;
// the email consumer records message ids so a re-delivery doesn't send twice
```

> **Lesson:** Work that must happen needs durable storage. An outbox ties the message to the data change.

#### Example 2: A nightly job that ran three times

A cleanup job uses an in-process timer. With three replicas, it runs three times and conflicts with itself.

*A database lease*

```sql
-- one row per job; only the instance that wins the update runs it
UPDATE JobLeases
SET    Owner = @instance, LeaseUntil = DATEADD(minute, 30, SYSUTCDATETIME())
WHERE  Job = 'nightly-cleanup' AND (LeaseUntil < SYSUTCDATETIME() OR Owner = @instance);
-- @@ROWCOUNT = 1 -> this instance runs; the job itself must still be safe to repeat
```

> **Lesson:** Scheduled work on several instances needs coordination, and the work itself must tolerate a repeat.

### Dependency Injection & Concurrency

<p class="src">From <em>.NET Backend Deep Dive</em> · Dependency Injection & Concurrency</p>

<a id="dn-10"></a>

#### BackgroundService is a Singleton — how do you use Scoped services?

`IHostedService` is registered as a **singleton** and constructed at startup, before any request exists. So you cannot inject `DbContext` directly.

```csharp
// WRONG
public class OrderWorker : BackgroundService
{
    private readonly AppDbContext _db;                 // captive dependency
    public OrderWorker(AppDbContext db) => _db = db;   // throws if ValidateScopes = true
}
```

```csharp
// RIGHT: inject IServiceScopeFactory, create a scope PER ITERATION
public class OrderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderWorker> _logger;

    public OrderWorker(IServiceScopeFactory f, ILogger<OrderWorker> l)
        => (_scopeFactory, _logger) = (f, l);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                // Fresh scope each tick: clean DbContext, empty ChangeTracker
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<IOrderProcessor>();
                await processor.ProcessPendingAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;                       // normal shutdown
            }
            catch (Exception ex)
            {
                // NEVER let an exception escape ExecuteAsync - the worker dies silently
                _logger.LogError(ex, "Worker iteration failed");
            }
        }
    }
}
```

**The killers of BackgroundService:**

1. **An exception escaping `ExecuteAsync` stops the worker permanently** while the app keeps serving traffic as if nothing happened. Catch inside the loop. (.NET 6+ has `HostOptions.BackgroundServiceExceptionBehavior` — set `StopHost` if you'd rather crash and let the orchestrator restart you.)
2. **Not creating a fresh scope each iteration** → a `DbContext` that lives for days, an ever-growing change tracker, stale cached data.
3. **Multiple replicas in Kubernetes** → the job runs several times. You need a distributed lock (Redis `SET NX PX`, or SQL Server's `sp_getapplock`) or leader election.
4. **Blocking in `StartAsync`** → the app never finishes starting. Long work belongs in `ExecuteAsync`.

##### `IServiceProviderFactory` — a completely different thing
- `IServiceScopeFactory`: creates **scopes** at runtime (what you need for background services).
- `IServiceProviderFactory<TBuilder>`: the extension point for **replacing the DI container entirely** (Autofac, Lamar) when the host is built.

```csharp
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(b =>
{
    b.RegisterModule<MyAutofacModule>();      // decorators, interceptors, assembly scanning...
});
```

##### Interview answer
> "`BackgroundService` is a singleton, so injecting a scoped service creates a captive dependency. I inject `IServiceScopeFactory` and call `CreateAsyncScope()` on each iteration to get a clean `DbContext`. I also wrap the loop body in try/catch, because an exception escaping `ExecuteAsync` kills the worker silently, and I add a distributed lock when running multiple replicas."

---

### Background Jobs & Scheduling

<p class="src">From <em>.NET Backend Deep Dive</em> · Background Jobs & Scheduling</p>

<a id="dn-149"></a>

#### Choosing the mechanism

| Need | Use |
|---|---|
| Poll or loop inside the app | `BackgroundService` (Part 3.3) |
| In-process producer/consumer | `Channel<T>` (Part 11.3) |
| Durable, retryable, must survive restarts | **Queue + consumer** (Part 4) |
| Scheduled/cron, with a dashboard and history | **Hangfire** or **Quartz.NET** |
| Cluster-wide cron, decoupled from the app | Kubernetes `CronJob` |

The deciding question is **durability**: `BackgroundService` and `Channel<T>` lose everything on restart. Anything a customer would notice going missing needs a persistent store behind it.

<a id="dn-150"></a>

#### Hangfire in practice

```csharp
builder.Services.AddHangfire(c => c.UseSqlServerStorage(cs));
builder.Services.AddHangfireServer(o => o.WorkerCount = 4);

// Fire-and-forget, survives restart because it's persisted first
_jobs.Enqueue<IEmailSender>(s => s.SendAsync(orderId, null!));

// Delayed and recurring
_jobs.Schedule<IReminder>(s => s.RemindAsync(orderId), TimeSpan.FromHours(24));
RecurringJob.AddOrUpdate<IReporting>("nightly", s => s.RunAsync(), Cron.Daily(2));
```

Points that matter:

- Arguments are **serialized**, so pass ids, not entities or contexts.
- Retries are automatic and at-least-once — **jobs must be idempotent**, same as message consumers.
- `DisableConcurrentExecution` prevents overlap when a run exceeds its interval.
- Secure the dashboard; by default it's an unauthenticated admin surface exposing job arguments.

<a id="dn-151"></a>

#### Scheduling correctness

**Cron expressions run in a time zone.** Unspecified usually means UTC — "daily at 02:00" then drifts by an hour twice a year relative to local expectations, and a job scheduled at 02:30 local may run twice or not at all across a DST boundary (Part 32.1).

**Overlap:** if the job takes 70 minutes and runs hourly, you eventually have several copies competing. Guard with a lock or `DisableConcurrentExecution`.

**Multiple replicas:** a plain `BackgroundService` runs on every pod. Use a distributed lock, a leader election, or a job server designed for it.

**Catch-up:** if the app was down at 02:00, should the job run at 02:15 on startup or skip to tomorrow? Decide deliberately — both are defensible, and silence here is how a month-end billing run gets missed.

**Make jobs resumable:** process in batches with a checkpoint, so a restart resumes rather than starting over or double-charging.

---

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a61"></a>

#### The generic host and background services

Every modern .NET app — web API, worker, console daemon — is a `IHost`: a DI container plus configuration plus logging plus a set of `IHostedService` implementations with a managed start/stop lifecycle.

``` csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<OutboxPublisher>();
var app = builder.Build();
app.Run();

public sealed class OutboxPublisher(IServiceScopeFactory scopes, ILogger<OutboxPublisher> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();          // fresh DbContext each tick
            try { await scope.ServiceProvider
                       .GetRequiredService<IOutbox>().PublishPendingAsync(stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Outbox pass failed"); }
        }
    }
}
```

> **\[Note\]**
>
> **Graceful shutdown.** On SIGTERM the host signals `stoppingToken`, stops accepting new work and waits up to `ShutdownTimeout` (default 30s). Honour the token or Kubernetes will `SIGKILL` you mid-transaction.

> **\[Trap\]**
>
> **Common trap.** An unhandled exception escaping `ExecuteAsync` stops the service silently in older versions and kills the host in .NET 6+. Always wrap the loop body in try/catch.

###### BackgroundService, and the ways it fails silently

``` csharp
public class ImportWorker(IServiceScopeFactory scopes, ILogger<ImportWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // everything before the FIRST await runs SYNCHRONOUSLY during host
        // startup and delays the entire application from listening.
        await Task.Yield();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // a BackgroundService is a SINGLETON and cannot inject scoped
                // services. Create a scope per unit of work:
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ProcessBatchAsync(db, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;                              // normal shutdown, not an error
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Batch failed; continuing");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);      // back off
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}

// since .NET 6, an unhandled exception in ExecuteAsync STOPS THE HOST.
// before that it was swallowed silently, which was worse - the worker
// simply stopped and nothing said so.
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
```

| Mistake                                           | Symptom                                                |
|---------------------------------------------------|--------------------------------------------------------|
| Blocking before the first `await`                 | Startup hangs; readiness never passes                  |
| Injecting a scoped service                        | Throws at startup, or a captive `DbContext`            |
| No per-iteration `catch`                          | One transient failure stops the worker forever         |
| Treating `OperationCanceledException` as an error | A spurious error on every shutdown                     |
| Ignoring the `CancellationToken`                  | Shutdown waits the full timeout, then hard-kills       |
| Running it inside the web process                 | Every replica runs the job — N instances, N executions |

``` text
  the lifecycle, and what runs when

    IHostedService.StartAsync          in registration order
      -> BackgroundService.ExecuteAsync starts here, and is NOT awaited
    ApplicationStarted
       ... running ...
    ApplicationStopping                first signal of shutdown
    IHostedService.StopAsync           in REVERSE registration order
      -> the ExecuteAsync token is cancelled; StopAsync waits for it
    ApplicationStopped

  ShutdownTimeout defaults to FIVE SECONDS. A worker mid-batch is cancelled
  and then abandoned. Raise it, and make batches interruptible:

    builder.Host.ConfigureHostOptions(o =>
        o.ShutdownTimeout = TimeSpan.FromSeconds(30));
```

For scheduled rather than continuous work, resist writing a timer loop. A timer in a web process fires on every replica, does not survive a restart, and keeps no history. Use a real scheduler — Quartz.NET or Hangfire with a database store — or push it to the platform as a Kubernetes CronJob, so scheduling is infrastructure rather than application code.

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a141"></a>

#### Background jobs and scheduling

| Tool                      | Shape                                     | Use for                                          |
|---------------------------|-------------------------------------------|--------------------------------------------------|
| `BackgroundService`       | In-process loop                           | Outbox relay, polling, cache warm-up             |
| Hangfire                  | Jobs persisted in SQL, retries, dashboard | Fire-and-forget and delayed jobs with visibility |
| Quartz.NET                | Rich cron scheduling, clustering          | Complex recurring schedules                      |
| Broker + delayed delivery | Broker holds the message                  | Retries and timeouts within a saga               |
| Kubernetes CronJob        | Separate pod on a schedule                | Nightly batch, reports, cleanup                  |

> **\[Trap\]**
>
> **Common trap.** If you run three replicas, a naive in-process timer runs the job three times. Either elect a leader (distributed lock in Redis or the database), use a broker so only one consumer wins the message, or move the schedule out to a CronJob that starts a single pod.

``` csharp
// simple database-backed leader election
var acquired = await _db.Database.ExecuteSqlAsync(
    $@"UPDATE job_locks SET owner = {instanceId}, expires_at = {DateTime.UtcNow.AddMinutes(5)}
       WHERE name = 'nightly-invoice' AND (expires_at < {DateTime.UtcNow} OR owner = {instanceId})");
if (acquired == 0) return;      // someone else has it
```

###### Choosing a mechanism, and the duplicate-execution problem

| Mechanism                   | Survives restart      | Multi-instance safe          | History                   | Fits                                   |
|-----------------------------|-----------------------|------------------------------|---------------------------|----------------------------------------|
| `Task.Run` fire-and-forget  | **No**                | N/A                          | No                        | **Nothing.** Work is lost on shutdown  |
| `BackgroundService` + timer | No                    | **No** — every replica fires | No                        | Simple polling in a single instance    |
| `PeriodicTimer`             | No                    | No                           | No                        | As above, with cleaner cancellation    |
| **Hangfire**                | Yes — database-backed | Yes                          | **Yes, with a dashboard** | The pragmatic default for .NET         |
| **Quartz.NET**              | Yes                   | Yes, with clustering         | Limited                   | Complex schedules, cron semantics      |
| **A queue + consumer**      | Yes                   | Yes                          | Broker-dependent          | Event-driven work                      |
| **Kubernetes CronJob**      | Yes                   | Yes                          | Pod logs                  | Scheduling as infrastructure, not code |
| **Azure Functions timer**   | Yes                   | Yes — singleton by default   | Yes                       | Serverless estates                     |

``` text
  THE DUPLICATE EXECUTION PROBLEM

    a PeriodicTimer in your web application, three replicas:

      03:00  replica 1 fires the nightly invoice run
      03:00  replica 2 fires the nightly invoice run
      03:00  replica 3 fires the nightly invoice run

    three sets of invoices. Nobody notices until finance does.

  and the related one, from the platform guide: IIS OVERLAPPED RECYCLING
  briefly runs two instances of the same application, so even a
  single-replica deployment can double-fire during a recycle.

  the three fixes
    1  DO NOT run scheduled work in the web process. A separate worker
       deployment with replicas: 1, or a platform CronJob.
    2  A DISTRIBUTED LOCK, so only one instance proceeds.
    3  IDEMPOTENT jobs, so running twice is harmless. Best, and hardest.
```

``` csharp
// a distributed lock via the database - no extra infrastructure
public async Task<bool> TryRunAsync(string jobName, Func<Task> work, CancellationToken ct)
{
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync(ct);

    // a session-level advisory lock: held until the connection closes,
    // released automatically if the process dies. That last part matters -
    // a lock table needs a lease and expiry; this does not.
    var key = (long)jobName.GetHashCode();
    var acquired = await conn.ExecuteScalarAsync<bool>(
        "SELECT pg_try_advisory_lock(@key)", new { key });

    if (!acquired) return false;                 // another instance has it
    try { await work(); }
    finally { await conn.ExecuteAsync("SELECT pg_advisory_unlock(@key)", new { key }); }
    return true;
}
```

``` csharp
// PeriodicTimer: the modern loop, if you do run one in-process
public class CleanupWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(ct))     // clean cancellation
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICleanup>().RunAsync(ct);
            }
            catch (Exception ex) { /* log and continue - do NOT let it escape */ }
        }
    }
}
// note: PeriodicTimer does NOT overlap. If the work takes longer than the
// interval, the next tick simply happens late. A System.Threading.Timer WOULD
// overlap, and that is a classic source of concurrent runs of the same job.
```

<a id="be-a155"></a>

#### Scheduling correctness: cron, DST and catch-up

“Run this every night at 02:00” hides more edge cases than almost any other requirement in backend software. Most of them appear twice a year, in production, at two in the morning.

##### The DST problem, precisely

``` text
  Europe/Amsterdam, a job scheduled for 02:30 local

  MARCH (spring forward)   01:59:59 -> 03:00:00.  02:30 DOES NOT EXIST.
                           -> the job does not run at all that day.

  OCTOBER (fall back)      02:59:59 -> 02:00:00.  02:30 HAPPENS TWICE.
                           -> the job runs twice, and if it is not idempotent
                              you have double-billed everyone.

  and the same schedule in UTC has neither problem, but drifts an hour against
  local business hours twice a year - which may itself be wrong for a payroll run.
```

| Requirement                   | Schedule in                     | Reason                                                    |
|-------------------------------|---------------------------------|-----------------------------------------------------------|
| “Every 15 minutes”            | UTC / an interval               | No local meaning; DST is irrelevant                       |
| “Overnight, off-peak”         | UTC, in the DST gap-free hours  | Pick 03:30 UTC and stop thinking about it                 |
| “09:00 on a business day”     | Local time with an IANA zone id | Business meaning is local; the engine must handle the gap |
| “End of the accounting month” | Local time, with a calendar     | Month end differs by jurisdiction and by fiscal year      |
| Per-tenant schedules          | Each tenant's own zone          | One job per tenant, not one job iterating tenants         |

``` csharp
// scheduling in a specific zone, handling the gap and the overlap explicitly
private static DateTimeOffset NextOccurrence(TimeOnly localTime, TimeZoneInfo zone,
                                             DateTimeOffset after)
{
    var date = TimeZoneInfo.ConvertTime(after, zone).Date;
    for (var i = 0; i < 8; i++)
    {
        var candidate = date.AddDays(i).Add(localTime.ToTimeSpan());

        if (zone.IsInvalidTime(candidate))          // the spring-forward gap
            candidate = candidate.AddHours(1);      // run an hour later, deterministically

        var offset = zone.IsAmbiguousTime(candidate)                 // the autumn overlap
            ? zone.GetAmbiguousTimeOffsets(candidate).Max()          // pick ONE, always the same
            : zone.GetUtcOffset(candidate);

        var result = new DateTimeOffset(candidate, offset);
        if (result > after) return result;
    }
    throw new InvalidOperationException("No occurrence found.");
}
```

###### Catch-up, drift and overlap

| Failure                    | Cause                                                                     | Handling                                                                                                                                    |
|----------------------------|---------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| Missed runs after downtime | The service was down at 02:00                                             | Decide the policy: skip, run once on recovery, or run every missed occurrence. Kubernetes CronJob exposes this as `startingDeadlineSeconds` |
| Timer drift                | `Task.Delay(interval)` in a loop accumulates the work duration each cycle | `PeriodicTimer`, which ticks on a fixed cadence, or compute the next absolute time                                                          |
| Overlapping runs           | The 02:00 run is still going at 03:00                                     | `concurrencyPolicy: Forbid`, or a lease the job must hold                                                                                   |
| N replicas, N runs         | An in-process timer in every pod                                          | A distributed lease, a CronJob, or a broker with a single consumer                                                                          |
| Clock skew between nodes   | Two nodes disagree by seconds                                             | NTP; and never derive correctness from local wall-clock comparisons across nodes                                                            |

``` csharp
// drift: the difference between two loops that look identical
while (!ct.IsCancellationRequested)          // DRIFTS
{
    await DoWorkAsync(ct);                   // takes 4s
    await Task.Delay(TimeSpan.FromMinutes(1), ct);   // fires every 64s, then 68s...
}

using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));    // does NOT drift
while (await timer.WaitForNextTickAsync(ct))
    await DoWorkAsync(ct);                   // ticks on the minute regardless of duration
```

``` text
# Kubernetes CronJob: the three settings that define the semantics
apiVersion: batch/v1
kind: CronJob
metadata: { name: nightly-invoicing }
spec:
  schedule: "30 3 * * *"
  timeZone: "Europe/Amsterdam"        # k8s 1.27+ - handles DST for you
  concurrencyPolicy: Forbid           # never overlap with a still-running job
  startingDeadlineSeconds: 3600       # if we missed the window by more than an hour, skip it
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 5
  jobTemplate:
    spec:
      backoffLimit: 2
      activeDeadlineSeconds: 7200     # kill a run that hangs, so tomorrow's is not blocked
```

> **\[Note\]**
>
> **The property that makes all of this survivable.** Make every scheduled job **idempotent and period-scoped**: it takes the period it is processing as an explicit input, and running it twice for the same period is a no-op. `InvoiceMonth(2026, 7)` can be retried, replayed, run twice during a DST overlap, and re-run manually after a failure, all safely. `InvoiceLastMonth()` — deriving the period from the current clock — can do none of those things, and it is how a duplicate billing run happens.

### How an application is wired

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How an application is wired</p>

<a id="be-m234"></a>

#### Background services and scopes outside a request

Outside a request there is no ambient scope, so anything scoped must be created explicitly — and a background service is a singleton, so it cannot simply inject a `DbContext`.

``` csharp
public sealed class OutboxDispatcher(
        IServiceScopeFactory scopes,
        ILogger<OutboxDispatcher> log,
        TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // Yield promptly: ExecuteAsync runs during StartAsync, so blocking
        // here delays the whole host from starting.
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                // A FRESH SCOPE PER ITERATION - bounded tracking, bounded memory
                // (§300)
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                await DispatchBatchAsync(db, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;                          // shutting down: not an error
            }
            catch (Exception ex) {
                // Swallow-and-continue, deliberately: an unhandled exception
                // from a BackgroundService STOPS THE HOST by default on .NET 6+.
                log.LogError(ex, "Outbox dispatch failed; continuing");
            }
        }
    }
}
```

> **\[Trap\]**
>
> **Three ways background services fail quietly.** Not creating a scope per iteration, so one `DbContext` accumulates tracked entities forever. Not observing `stoppingToken`, so shutdown hangs until it is force-killed. And letting an exception escape, which on .NET 6+ tears down the entire application — correct as a default, and a surprise if you assumed the worker would just restart.

> **\[Tip\]**
>
> **Idempotency is not optional here.** A background worker will be killed mid-batch during a deploy, a scale-in, or a node failure — and it will run the same work again on restart. Design every iteration to converge when repeated: claim rows with a status update, use an idempotency key, and make the batch restartable from a watermark ([Backfills without downtime](07-05-schema-design-and-migrations.md#be-m221), [Consistency across stores](07-06-non-relational-stores.md#be-m210)).

Recipe: [Recipe: a queue worker that does not lose work](#be-m289)

### Recipes: code you will actually write

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Recipes: code you will actually write</p>

<a id="be-m289"></a>

#### Recipe: a queue worker that does not lose work

``` csharp
public sealed class OrderProcessor(
        IServiceScopeFactory scopes, TimeProvider time,
        ILogger<OrderProcessor> log) : BackgroundService {

    private static readonly Counter<long> Processed =
        Meter.CreateCounter<long>("orders.processed");

    protected override async Task ExecuteAsync(CancellationToken stopping) {
        await Task.Yield();                          // do not block host startup

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        while (await timer.WaitForNextTickAsync(stopping)) {
            try { await RunBatchAsync(stopping); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            catch (Exception ex) {
                // An escaping exception STOPS THE HOST on .NET 6+
                // (§234). Log and continue.
                log.LogError(ex, "Batch failed; continuing");
            }
        }
    }

    private async Task RunBatchAsync(CancellationToken ct) {
        // Fresh scope per batch: bounded tracking, bounded memory
        // (§300).
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = time.GetUtcNow();

        // CLAIM with an expiry, so a killed instance's work is reclaimed.
        var claimId = Guid.NewGuid();
        var claimed = await db.PendingOrders
            .Where(o => o.Status == Pending && o.NextAttemptUtc <= now)
            .OrderBy(o => o.Id).Take(100)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, Claimed)
                .SetProperty(o => o.ClaimId, claimId)
                .SetProperty(o => o.ClaimExpiresUtc, now.AddMinutes(5)), ct);
        if (claimed == 0) return;

        var batch = await db.PendingOrders.Where(o => o.ClaimId == claimId).ToListAsync(ct);

        foreach (var item in batch) {
            ct.ThrowIfCancellationRequested();       // check per item, not per element
            try {
                await ProcessAsync(item, ct);        // must be idempotent
                item.Status = Done;
                Processed.Add(1, new KeyValuePair<string, object?>("result", "ok"));
            }
            catch (Exception ex) {
                item.Attempts++;
                item.Status = item.Attempts >= 5 ? DeadLetter : Pending;
                item.NextAttemptUtc = now + Backoff(item.Attempts);   // with jitter
                item.LastError = ex.Message[..Math.Min(500, ex.Message.Length)];
                log.LogWarning(ex, "Item {Id} attempt {N} failed", item.Id, item.Attempts);
            }
            await db.SaveChangesAsync(ct);           // per item: a crash loses at most one
        }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt) * (0.8 + Random.Shared.NextDouble() * 0.4));
}
```

> **\[Tip\]**
>
> **The dead-letter status is the part people omit.** Without it, a permanently-failing item retries forever, consumes the batch every cycle, and starves everything behind it. Cap the attempts, move it aside, and alert on the dead-letter count — that count is the metric that tells you something is wrong ([Metrics: what to measure](11-06-observability.md#be-e540)).

> **\[Trap\]**
>
> **Claims must expire.** If an instance is killed while holding a claim with no expiry, those items are stranded permanently and nobody notices — exactly the silent failure in [Case: the outbox that lost messages](14-03-worked-cases-from-production.md#be-m312). An expiry plus a reclaim query is three lines and removes the entire class.
