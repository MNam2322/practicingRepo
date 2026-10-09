---
id: "2-8"
number: "2.8"
title: "Errors and exceptions"
part: 2
part_title: "From source code to running program"
words: 5988
minutes: 26
---

# 2.8 Errors and exceptions

> **Quick look.** How to report and handle failure so problems become clear log lines rather than corrupted data.
>
> - Bugs fail loudly, expected failures are normal outcomes, temporary failures can be retried.
> - An exception unwinds the stack running finally blocks until a matching catch.
> - Catch specific exceptions where you can act; catch everything only at boundaries; rethrow with throw;, never throw ex;.
> - Throwing costs microseconds: fine for the unexpected, terrible as control flow. Model expected failures with result types.
> - Async failures surface when the task is awaited; async void crashes the process.
> - Retry only operations safe to repeat, with growing randomized delays; never leak internals to API clients.


### The big picture

Things go wrong in every program: bugs, bad input, timeouts, full disks. How code reports and handles failure decides whether a problem becomes a clear message in a log or silently corrupted data.

This chapter sorts failures into kinds, explains how exceptions travel up through your code, and shows how to throw, catch and rethrow them well. Then it covers failure in real systems: asynchronous code, cancellation, retrying operations that fail temporarily without making things worse, and reporting errors from web APIs. It ends Part 2.

### Before you start

**Builds on:** 2.7 Memory management and garbage collection.

**Ideas explained later:**

- **Background job** (8.7): Work done outside the request that triggered it, so the user doesn't wait for it.
- **Synchronization context** (5.3): An object that decides where continuations run, such as back on a UI thread.
- **Thread pool** (5.2): A managed set of reusable worker threads that run queued work items.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Call stack** (3.8): The stack of frames for all method calls currently in progress.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.

**Tools and technologies named here:**

- **RFC** (6.6): Request for Comments: the numbered documents that define internet standards, such as RFC 9110 for HTTP.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, generic types, lambdas, LINQ, null operators, tuples, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between throw; and throw ex; inside a catch block?**

> `throw;` rethrows the same exception and keeps its original stack trace. `throw ex;` throws it again as if it started at that line, wiping out where it really came from. Use `throw;`.

**2. When does a finally block not run?**

> When the process dies before reaching it: killed by the OS, a stack overflow, `Environment.FailFast`, or a power cut. Normal returns, `break` and exceptions all run it.

**3. What does catch (Exception ex) when (...) do?**

> It is an exception filter. The catch block only runs if the condition is true. Otherwise the exception keeps travelling up as if this catch weren't there, with its stack untouched.

**4. Where should you catch Exception, the base type?**

> Only at boundaries: the top of a request, a message handler, a background job loop or `Main`. There you log it, report a failure and keep the rest of the system running. Deep inside code, catch only specific exceptions you can actually handle.

**5. An async void method throws. What happens?**

> There is no task to hold the exception, so it is raised on the synchronization context or thread pool and usually crashes the process. Use `async Task` everywhere except event handlers.

**6. Why add random jitter to retry delays?**

> If many clients fail at the same moment and all retry after exactly the same delay, they all hit the recovering service at once again. Random jitter spreads the retries out.

**7. Why must an operation be idempotent before you retry it automatically?**

> A request can succeed on the server while the response is lost. Retrying then does the work twice. An idempotent operation, one that gives the same result when repeated, is safe to retry.

**8. What happens to an exception thrown inside a BackgroundService in .NET 6 or later?**

> By default it stops the whole host and the app exits. Catch and log inside the service's loop if one failed iteration shouldn't bring everything down.

### Three kinds of failure

Not every error is the same kind of problem. Sorting them helps decide what to do.

**Bugs** are mistakes in the code: a null where there should be a value, an index out of range, a broken assumption. The right response is to fail loudly, fix the code and add a test. Catching and ignoring them only hides them.

**Expected failures** are part of normal operation: a user types a bad date, a file doesn't exist, a flight number isn't found. The code should handle them as ordinary outcomes and tell the caller clearly.

**Environmental failures** come from outside: a network drops, a database times out, a disk fills up. Some are **transient failures** that go away if you try again. Others need a person to fix something.

### Ways to report an error

Languages have tried several designs.

**Error codes.** The function returns a special value, such as −1 or null, or sets a global error variable. C and many operating system calls work this way. The weakness is that nothing forces the caller to check.

**Exceptions.** The function stops and throws an object describing the problem. The error travels up the call stack until some code catches it. It can't be ignored silently. C#, Java, Python and JavaScript use exceptions as their main mechanism.

**Result types.** The function returns a value that is either a success holding the result or a failure holding the error. The type system makes the caller handle both. Rust's `Result`, F#'s `Result` and Go's paired return values `(value, err)` follow this style.

C# uses exceptions for bugs and environmental failures. For expected failures, the library often uses the `TryX` pattern, such as `int.TryParse`, which returns `false` instead of throwing. Some teams also use a result type for business outcomes, as shown below.

### How exceptions work

An **exception** is an object, deriving from `System.Exception`, that describes a failure. `throw` starts it on its way.

The runtime then looks for a handler. It checks the current method for a `try` block with a matching `catch`. If there is none, it leaves the method and checks the caller, and so on up the stack. Leaving methods this way is called **stack unwinding**. Every `finally` block on the way runs, so cleanup still happens. `using` blocks are `finally` blocks underneath, so their `Dispose` runs too.

The exception records a **stack trace**: the chain of method calls from where it was thrown. With debug symbols (`.pdb` files) deployed, it includes file names and line numbers. A good stack trace is the most valuable thing an exception carries.

If no handler is found anywhere, the exception is an **unhandled exception**. In a console app, the process prints it and exits with a failure code. In ASP.NET Core, the framework catches it at the edge of the request and returns a 500 response. In a `BackgroundService`, it stops the whole app by default.

```csharp
// errors.cs — run with: dotnet run errors.cs
try
{
    Console.WriteLine("start");
    ParseAltitude("FL350");
    Console.WriteLine("never printed");
}
catch (FormatException ex)
{
    Console.WriteLine($"caught: {ex.Message}");
}
finally
{
    Console.WriteLine("finally runs either way");
}
// start, caught: 'FL350' is not a whole number of feet., finally runs either way

try { ParseAltitude("-200"); }
catch (ArgumentOutOfRangeException ex) { Console.WriteLine(ex.Message.Split('\n')[0]); }
// feet ('-200') must be a non-negative value. (Parameter 'feet')

foreach (var (name, action) in new (string, Action)[] { ("throw;", RethrowGood), ("throw ex;", RethrowBad) })
{
    try { action(); }
    catch (Exception ex) { Console.WriteLine($"{name,-10} trace mentions Level2: {ex.StackTrace!.Contains("Level2")}"); }
}
// throw;     trace mentions Level2: True
// throw ex;  trace mentions Level2: False

try { LoadSchedule("missing.csv"); }
catch (ScheduleException ex) { Console.WriteLine($"{ex.Message} Cause: {ex.InnerException!.GetType().Name}"); }
// Could not load schedule 'missing.csv'. Cause: FileNotFoundException

static int ParseAltitude(string text)
{
    ArgumentNullException.ThrowIfNull(text);
    if (!int.TryParse(text, out int feet))
        throw new FormatException($"'{text}' is not a whole number of feet.");
    ArgumentOutOfRangeException.ThrowIfNegative(feet);
    return feet;
}

static void Level2() => throw new InvalidOperationException("deep failure");
static void RethrowGood() { try { Level2(); } catch { throw; } }
static void RethrowBad() { try { Level2(); } catch (Exception ex) { throw ex; } }

static string[] LoadSchedule(string path)
{
    try { return File.ReadAllLines(path); }
    catch (IOException ex) { throw new ScheduleException($"Could not load schedule '{path}'.", ex); }
}

sealed class ScheduleException(string message, Exception inner) : Exception(message, inner);
```

### Throwing well

Throw when a method can't do what its name promises. Check arguments at the top with guard clauses. .NET 6 and later have helpers that write good messages for you: `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative`, `ThrowIfGreaterThan` and others.

Pick the most specific built-in type that fits. `ArgumentException` and its relatives mean the caller passed something wrong. `InvalidOperationException` means the object isn't in a state where this call makes sense. `NotSupportedException` means the operation will never work on this type. `TimeoutException` and `IOException` describe environmental failures.

Write messages for the person who will read the log at 3 a.m. Say what failed and include the values involved, but never passwords, tokens or personal data.

When you catch one exception and throw another, pass the original as the **inner exception**. The chain then shows both what went wrong at your level and the underlying cause. Create your own exception types only when callers need to catch that specific case.

### Catching well

Catch an exception only if you can do something useful about it: retry, use a fallback, translate it into a clearer error or report it at a boundary. Otherwise let it travel.

Catch specific types. `catch (Exception)` deep in the code also catches bugs, hides them and leaves the program in an unknown state.

An **exception filter**, `catch (IOException ex) when (IsTransient(ex))`, decides before unwinding whether this handler applies. If the condition is false, the exception continues untouched, and a debugger or crash dump still shows the exact state where it was thrown.

To pass an exception on after logging or cleanup, **rethrow** with `throw;`, never `throw ex;`, as the example shows.

Log an exception once, at the place that handles it. Logging at every level produces the same error ten times and buries the real story.

### Exceptions in asynchronous code

An `async Task` method never throws directly to its caller. The exception is stored in the returned task, and rethrown when someone awaits it. If nobody awaits the task, the exception is never seen.

`Task.WhenAll` collects the failures of all its tasks. `await` rethrows only the first one. The task's `Exception` property is an `AggregateException` holding all of them. Blocking on a task with `.Result` or `.Wait()` wraps failures in `AggregateException` too, and risks deadlocks, as Async and await (5.3) explains.

`async void` methods have no task. An exception thrown from one crashes the process. Use `async void` only for UI event handlers, which the framework requires.

### Cancellation

Long operations in .NET accept a `CancellationToken`. When the token is cancelled, because the user gave up, the HTTP request was aborted or a timeout passed, the operation stops by throwing `OperationCanceledException`.

Cancellation is not a failure. Don't log it as an error, and don't retry it. Pass tokens all the way down, into database calls and HTTP calls, so the work below stops too.

### Transient failures and retries

A timeout or a dropped connection often succeeds on a second try. Retrying automatically makes a system much more robust, if it is done carefully.

Wait between attempts, and wait longer each time, doubling the delay: this is **exponential backoff**. Add a random amount to each delay, called **jitter**, so that many clients don't all retry at the same instant. Limit the number of attempts. Retry only failures that are likely to be transient, never bugs or validation errors.

Most importantly, retry only operations that are **idempotent**, meaning that doing them twice has the same effect as doing them once. Reading data is idempotent. "Set gate to B12" is idempotent. "Charge the card" and "add a passenger" are not, unless the server recognizes a repeated request by a unique request id.

In real applications, don't write retry loops by hand. Use `Microsoft.Extensions.Http.Resilience` or Polly, which add retries, timeouts and circuit breakers to `HttpClient`. The broader picture is in Reliability (12.5).

```csharp
// resilience.cs — run with: dotnet run resilience.cs
using System.Linq;

// 1. Retry with exponential backoff and jitter
int calls = 0;
string result = await Retry(async () =>
{
    calls++;
    await Task.Delay(10);
    if (calls < 3) throw new TimeoutException($"attempt {calls} timed out");
    return "schedule loaded";
}, maxAttempts: 5);
Console.WriteLine($"{result} after {calls} attempts");
// attempt 1 timed out; retrying in about 100 ms
// attempt 2 timed out; retrying in about 200 ms
// schedule loaded after 3 attempts

// 2. Several failures at once
async Task FeedA() { await Task.Delay(10); throw new TimeoutException("feed A timed out"); }
async Task FeedB() { await Task.Delay(20); throw new IOException("feed B disconnected"); }
Task all = Task.WhenAll(FeedA(), FeedB());
try { await all; }
catch (Exception ex) { Console.WriteLine($"await rethrew: {ex.GetType().Name}"); }   // TimeoutException
Console.WriteLine(string.Join("; ", all.Exception!.InnerExceptions.Select(e => e.Message)));
// feed A timed out; feed B disconnected

// 3. Cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token); }
catch (OperationCanceledException) { Console.WriteLine("cancelled after 100 ms"); }

// 4. Expected failures as values
foreach (string input in new[] { "35000", "FL350" })
{
    Result<int> r = TryParseFeet(input);
    Console.WriteLine(r.Ok ? $"{r.Value} ft" : $"rejected: {r.Error}");
}
// 35000 ft, rejected: 'FL350' is not a number of feet

static async Task<T> Retry<T>(Func<Task<T>> action, int maxAttempts)
{
    for (int attempt = 1; ; attempt++)
    {
        try { return await action(); }
        catch (TimeoutException ex) when (attempt < maxAttempts)   // on the last attempt, let it escape
        {
            double ms = 100 * Math.Pow(2, attempt - 1) * (0.8 + 0.4 * Random.Shared.NextDouble());
            Console.WriteLine($"{ex.Message}; retrying in about {Math.Round(ms / 100) * 100} ms");
            await Task.Delay(TimeSpan.FromMilliseconds(ms));
        }
    }
}

static Result<int> TryParseFeet(string text) =>
    int.TryParse(text, out int feet) && feet >= 0
        ? Result<int>.Success(feet)
        : Result<int>.Failure($"'{text}' is not a number of feet");

readonly record struct Result<T>(bool Ok, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

### Failing fast

Sometimes the safest response is to stop. If the program finds its own data corrupted or a basic assumption broken, carrying on can make things worse, such as writing bad data to a database. To **fail fast** means crashing at once, loudly, where the problem is visible.

An **assertion** states something that must be true. `Debug.Assert(condition)` checks it in Debug builds only. `UnreachableException`, added in .NET 7, marks code that should be impossible to reach. `Environment.FailFast` ends the process immediately, skipping `finally` blocks, and writes an event to the system log.

Failing fast also applies at start-up. An app with missing configuration should refuse to start, with a clear message, rather than start and fail on the first request. .NET's options validation with `ValidateOnStart` does this.

### Errors in web APIs

A web API must turn failures into responses a client understands. The standard format is **Problem Details**, defined in RFC 9457: a JSON body with a type, title, status and detail. ASP.NET Core can produce it for you. Never send stack traces or internal messages to clients; they help attackers. Log the details on the server with a correlation id, and return the id to the client. The details are in Validation and errors at the boundary (8.5).

### In short

Bugs should fail loudly, expected failures should be handled as normal outcomes, and temporary failures can be retried. An exception unwinds the call stack, running `finally` blocks, until a matching `catch`. Catch specific exceptions where you can act, catch everything only at boundaries, and rethrow with `throw;`. Failures in async code surface when the task is awaited, and `async void` crashes the process. Retry only operations that are safe to repeat, with growing, randomized delays.

### What goes wrong

**Swallowing exceptions.** `catch { }` or `catch (Exception) { }` hides bugs until data is corrupted. If you really must continue, log the exception with full details.

**`throw ex;`.** The stack trace now starts in the catch block, and the real origin is lost. Use `throw;`.

**Exceptions for normal control flow.** Throwing on every "not found" in a hot path wastes time and floods the logs. Use `TryX` methods or result values for expected outcomes.

**Retrying non-idempotent operations.** A timed-out payment request is retried and the customer is charged twice. Make operations idempotent with request ids before adding retries.

**Retry storms.** Every client retries immediately and at the same rate, and a struggling service never recovers. Use backoff, jitter and a cap on attempts.

**Unobserved task failures.** A task is started and never awaited, and its exception disappears. Always await tasks, or attach explicit error handling.

**Leaking internals to clients.** Stack traces and SQL errors in API responses reveal how the system is built. Return Problem Details with a correlation id instead.

**Logging cancellation as errors.** Users closing their browser fill the error log. Handle `OperationCanceledException` separately.

### Cost model

A `try` block with no exception thrown costs almost nothing. Throwing and catching is far more expensive than returning a value, typically several microseconds and more with deep stacks, because the runtime walks the stack and builds the trace. .NET 9 rewrote exception handling and made it several times faster, but the rule stays the same: exceptions are for exceptional cases. Retries cost time by design. With backoff starting at 100 ms and three retries, a failing call can take close to a second before giving up, which must fit inside the caller's own timeout.

### In other languages (optional)

**JavaScript and TypeScript.** Anything can be thrown, even a string, so a `catch` can't assume it received an `Error`. In TypeScript the caught value has type `unknown`. A rejected promise that nobody handles triggers an `unhandledrejection` event, and Node.js exits by default.

**Python.** Uses `try`/`except`/`else`/`finally`. `raise ... from err` chains exceptions, like C#'s inner exception. Python leans on exceptions even for normal cases, a style called "easier to ask forgiveness than permission".

**Java.** Has checked exceptions, which a method must declare or handle. Newer Java frameworks mostly avoid them in favour of unchecked exceptions.

**Go.** Functions return an `error` value alongside the result, and callers check `if err != nil`. `panic` exists for true bugs.

**Rust.** `Result<T, E>` for recoverable errors, with the `?` operator to pass them up. `panic!` for bugs.

**SQL Server.** `TRY...CATCH` blocks in T-SQL. `XACT_ABORT ON` makes most errors roll back the whole transaction, which is the safe default.

### Key terms

- **Exception**: An object describing a failure, thrown to stop the current method and travel up the call stack.
- **Stack unwinding**: Leaving methods one by one, running their finally blocks, while looking for a matching catch.
- **Stack trace**: The chain of method calls recorded where an exception was thrown.
- **Unhandled exception**: An exception that no catch block handles. It usually ends the process or the request.
- **Error code**: A special return value that signals failure, such as −1 or null.
- **Result type**: A return value that is either a success holding a result or a failure holding an error.
- **Inner exception**: The original exception stored inside a new one, preserving the underlying cause.
- **Exception filter**: A `when` condition on a catch block that decides, before unwinding, whether it applies.
- **Rethrow**: Passing a caught exception on with `throw;`, keeping its original stack trace.
- **Transient failure**: A failure likely to go away if the operation is tried again, such as a timeout.
- **Exponential backoff**: Waiting longer between each retry, typically doubling the delay each time.
- **Jitter**: A random amount added to retry delays so that many clients don't retry at the same moment.
- **Idempotent**: Having the same effect whether done once or several times, which makes an operation safe to retry.
- **CancellationToken**: A .NET object that signals an operation should stop, which it does by throwing OperationCanceledException.
- **Fail fast**: Stopping immediately and loudly when something is badly wrong, instead of carrying on.
- **Assertion**: A check that something must be true, used to catch bugs early.
- **Problem Details**: The standard JSON format for error responses from web APIs, defined in RFC 9457.

### Where this leads

This ends Part 2. Part 3 starts measuring the cost of algorithms in Measuring cost with Big-O (3.1). Tests that prove errors are handled are in Testing (4.6). Tasks, awaiting and cancellation in depth are in Async and await (5.3). Turning failures into API responses is in Validation and errors at the boundary (8.5). Timeouts, circuit breakers and graceful degradation are in Reliability (12.5).

### Check yourself

1. Change `Retry` in `resilience.cs` so that it accepts a `CancellationToken` and stops waiting between attempts when it is cancelled.
2. Set `maxAttempts` to 2 in `resilience.cs` and explain why the program now crashes, and at which line.
3. Write `SetGate(string flight, string gate)` and `AddPassenger(string flight, string name)` as API operations. Decide which is idempotent, and how you would make the other one safe to retry.
4. Find a `catch (Exception)` in a codebase you know. Decide whether it is at a boundary, and if not, what it should catch instead.
5. Run `errors.cs` in Release and in Debug. Check whether the stack trace lines change, and find out whether your deployments include `.pdb` files.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: An exception swallowed until data went missing

Loyalty points occasionally don't appear. The code catches all exceptions and logs nothing.

*Before*

```csharp
try { await loyalty.AddPointsAsync(booking); } catch { }
```

*After*

```csharp
try
{
    await loyalty.AddPointsAsync(booking, ct);
}
catch (HttpRequestException ex) when (ex.StatusCode is >= HttpStatusCode.InternalServerError)
{
    logger.LogWarning(ex, "Loyalty unavailable for booking {BookingId}; queued for retry", booking.Id);
    await outbox.EnqueueAsync(new AwardPoints(booking.Id), ct);   // expected, temporary: retry later
}
// anything else is a bug: let it propagate to the global handler
```

> **Lesson:** Catch only what you can handle, where you can handle it. Turn temporary failures into retries and let bugs fail loudly.

#### Example 2: Expected failures as results, not exceptions

Seat selection throws `SeatTakenException` thousands of times an hour during sales, and CPU climbs.

*Model the expected outcome*

```csharp
public abstract record SeatResult
{
    public sealed record Reserved(string Seat) : SeatResult;
    public sealed record Taken(string[] Alternatives) : SeatResult;
}

var result = await seats.TryReserveAsync(flight, "12A", ct);
return result switch
{
    SeatResult.Reserved r => Results.Ok(r),
    SeatResult.Taken t    => Results.Conflict(new { error = "seat_taken", t.Alternatives }),
    _ => throw new UnreachableException(),
};
```

> **Lesson:** Exceptions are for the unexpected. Ordinary business outcomes belong in the return type.

### Exception & Failure Cheat Sheet

<p class="src">From <em>.NET Backend Deep Dive</em> · Exception & Failure Cheat Sheet</p>

<a id="dn-63"></a>

| Exception / symptom | Almost always means | First thing to check |
|---|---|---|
| `InvalidOperationException: A second operation was started on this context` | Shared `DbContext` across concurrent tasks | `Task.WhenAll` around EF calls (Part 3.1) |
| `ObjectDisposedException` on `DbContext` | Used after its scope closed | Something escaped a `using` scope, or fire-and-forget work captured a scoped service |
| `Timeout expired ... obtaining a connection from the pool` | Connection pool exhaustion, **not** a slow DB | Undisposed contexts, unbounded parallelism, long transactions (Part 13.D) |
| `DbUpdateConcurrencyException` | Optimistic concurrency conflict | Reload and retry, or surface a 409 |
| `DbUpdateException` with inner SQL error | Constraint violation | Unique index — often soft-delete related (Part 2.1) |
| `TaskCanceledException` on HTTP | Your timeout **or** caller cancellation | Filter on `ct.IsCancellationRequested` to tell them apart |
| `AggregateException` | Someone used `.Result`/`.Wait()` | Replace with `await` |
| `StackOverflowException`, process gone, no logs | Infinite recursion | Self-referencing property, `ToString` loop, deep data (Part 20.5) |
| `OutOfMemoryException` | GC ran and everything is still reachable | `dumpheap -stat` → `gcroot` |
| Exit code 137, `OOMKilled` | Kernel killed you at the cgroup limit | Container memory limit vs Server GC (Part 20.4) |
| Exit code 139 | Segfault — native code or a corrupted runtime state | Native interop, or a runtime bug |
| Low CPU, requests queuing, rising latency | Thread pool starvation | Sync-over-async somewhere (Part 13.C) |
| Latency fine at p50, terrible at p99 | GC gen 2, lock contention, or a hot-key cache miss | `% time in GC`, then traces |
| Works locally, 401 in production | Clock skew, wrong audience, or missing scheme order | `UseAuthentication` before `UseAuthorization` (Part 22.1) |
| Intermittently stale data | Per-instance `IMemoryCache` across pods | Move to distributed/hybrid cache (Part 13.E) |
| CORS error in browser, clean 401 in logs | Middleware ordering | `UseCors` before `UseAuthorization` |
| Background job runs twice | Multiple replicas, no distributed lock | Leader election or `sp_getapplock` (Part 3.3) |
| Worker silently stops, app healthy | Exception escaped `ExecuteAsync` | try/catch inside the loop (Part 3.3) |

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a32"></a>

#### Exceptions: cost, design and handling

Throwing an exception captures a stack trace and unwinds frames; it costs on the order of microseconds, which is thousands of times a normal return. That is irrelevant for genuinely exceptional cases and disastrous as control flow in a loop.

``` csharp
// exception filters run BEFORE the stack unwinds - better diagnostics, no rethrow
try { await CallAsync(); }
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return null;
}

// preserve the stack when rethrowing
catch (Exception ex)
{
    _log.LogError(ex, "Import failed for {File}", name);
    throw;                 // NOT  throw ex;  - that resets the stack trace
}

// expected failures on a hot path: return, do not throw
public readonly record struct Result<T>(bool Ok, T? Value, string? Error);
```

- Catch what you can actually handle. A `catch (Exception)` that logs and continues turns a crash into corrupt data.
- Never swallow silently. An empty catch block is the hardest bug class to diagnose in production.
- Custom exceptions should carry structured data, not just a formatted message.
- `OperationCanceledException` is not an error — do not log it as one, or your error rate will be dominated by users closing tabs.
- `ArgumentNullException.ThrowIfNull(x)` and friends are the modern, allocation-free guard clauses.

###### How it actually works

Where the microseconds go when you throw:

| Step                                                     | Approximate cost                               |
|----------------------------------------------------------|------------------------------------------------|
| Allocate the exception object                            | Trivial                                        |
| Capture the stack trace                                  | The bulk of it — walks and records every frame |
| First-chance notification to debuggers/profilers         | Significant when a debugger is attached        |
| Two-pass unwind: find a handler, then run finally blocks | Proportional to frame depth                    |
| Total, deep stack, no debugger                           | ~5–50 µs                                       |
| Total, with a debugger attached                          | Can exceed 1 ms                                |

The practical threshold: a few thousand exceptions per second will show up on a CPU profile and slow the whole process. A few per second is invisible. This is why validation-as-exceptions is fine on a form submission and disastrous inside a per-row import loop.

``` csharp
// measured pattern: 100,000 rows, 30% invalid
// exceptions:  ~1.4 s, dominated by stack capture
// TryParse:    ~12 ms
foreach (var raw in rows)
{
    if (!decimal.TryParse(raw.Amount, NumberStyles.Number,
                          CultureInfo.InvariantCulture, out var amount))
    {
        errors.Add(new RowError(raw.LineNumber, "Amount is not a number."));
        continue;
    }
    ...
}
```

###### Designing an exception hierarchy that is actually useful

``` csharp
public abstract class AppException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;                      // stable, machine-readable
    public IDictionary<string, object?> Context { get; } = new Dictionary<string, object?>();
}

public sealed class DomainRuleException(string code, string message)
    : AppException(code, message);                            // -> 409, user-fixable

public sealed class NotFoundException(string resource, object id)
    : AppException("not_found", $"{resource} '{id}' was not found.");   // -> 404

public sealed class DependencyException(string service, Exception inner)
    : AppException("upstream_failure", $"{service} is unavailable.", inner);  // -> 502, retryable

// throwing with context that survives into the log
throw new DomainRuleException("order.already_shipped", "A shipped order cannot be modified.")
      { Context = { ["orderId"] = order.Id, ["status"] = order.Status } };
```

- A stable `Code` is what clients branch on. Message text is for humans and can be reworded or translated freely.
- Structured `Context` beats string interpolation, because the logging pipeline can index the fields.
- One base type per *response category* lets the exception handler map to a status code with a single switch, instead of a growing chain of `is` checks.
- Never put personal data in the message — it ends up in logs, error trackers and sometimes in the client response.

``` csharp
// and then a single translation point
private static (int Status, string Title) Map(Exception ex) => ex switch
{
    NotFoundException      => (404, "Not found"),
    DomainRuleException    => (409, "Business rule violated"),
    ValidationException    => (422, "Validation failed"),
    DependencyException    => (502, "Upstream service unavailable"),
    OperationCanceledException => (499, "Client closed request"),
    _                      => (500, "Unexpected error")
};
```

### How .NET actually runs

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How .NET actually runs</p>

<a id="be-m44"></a>

#### Exceptions: what they actually cost

The asymmetry matters: **`try`/`catch` costs essentially nothing when nothing throws** — the JIT emits exception-handling tables consumed only during unwinding, not instructions on the happy path. **Throwing** is genuinely expensive: allocate the exception, capture the stack trace, walk frames looking for a handler, run every `finally` on the way.

``` text
// Order-of-magnitude, not measurements:
//   normal return              ~1 ns
//   try/catch, no throw        ~1 ns  (free)
//   throw + catch, shallow     ~5-20 us
//   throw + catch, deep stack  ~50 us+
// A throw is roughly 10,000x a return. In a loop, that is the whole runtime.
```

``` csharp
// Exceptions as control flow - the anti-pattern:
foreach (var s in lines) {
    try { results.Add(int.Parse(s)); }
    catch (FormatException) { /* skip */ }        // 20 us per bad line
}

// The Try pattern exists for exactly this:
foreach (var s in lines)
    if (int.TryParse(s, out var v)) results.Add(v);   // no exception, no cost
```

**Where exceptions are right:** genuinely exceptional conditions that the immediate caller cannot handle sensibly — a corrupt file, a lost connection, a violated invariant. **Where they are wrong:** anything expected often enough to appear in a loop — validation failures, cache misses, parse failures, "not found".

``` csharp
// Exception filters run BEFORE any unwinding, so the original stack is intact
// when the filter evaluates - useful for logging without rethrowing:
catch (SqlException ex) when (IsTransient(ex)) { await RetryAsync(); }

// throw;  preserves the stack trace.
// throw ex;  RESETS it, discarding where the error actually came from.
// Almost every 'throw ex;' in a codebase is a bug.
```

> **\[Tip\]**
>
> **For expected failures in a data layer, model the outcome.** A `bool TryGet(out T)`, a nullable return, or a small result type turns "did this work?" into something the compiler checks and the profiler never sees — and it removes the temptation to catch broadly, which is how real errors get swallowed ([Value objects and parsing at the boundary](08-05-validation-and-errors-at-the-boundary.md#be-m266)).

Goes further: [Errors: ProblemDetails and failure contracts](08-02-api-design.md#be-m242)

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a72"></a>

#### Result types and modelling failure

Exceptions are for the unexpected. “This email is already registered” is entirely expected, so encoding it as an exception means using stack unwinding as an if-statement — expensive, and invisible in the method signature.

``` csharp
public readonly record struct Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T v) => new(v, null);
    public static Result<T> Fail(Error e) => new(default, e);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail) =>
        IsSuccess ? ok(Value!) : fail(Error!);
}

// the signature now tells the truth about what can happen
public async Task<Result<Guid>> RegisterAsync(string email, CancellationToken ct)
{
    if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        return Result<Guid>.Fail(Error.Conflict("users.duplicate", "Email already registered."));

    ...
    return Result<Guid>.Ok(user.Id);
}

// one mapping at the edge
return result.Match(
    ok:   id => TypedResults.CreatedAtRoute("GetUser", new { id }),
    fail: e  => e.ToProblemDetails());
```

> **\[Note\]**
>
> **Where to draw the line.** Results for expected domain outcomes; exceptions for programmer errors, infrastructure failure and anything that must abort a transaction. Mixing both deliberately is normal — the mistake is having no rule at all, so that every developer guesses differently.

Libraries worth knowing rather than rolling your own: `ErrorOr`, `FluentResults`, `OneOf`, and `LanguageExt` if the team is comfortable going further functional.

###### The decision, and the ergonomics problem in C#

| Use an exception                                   | Use a Result                                          |
|----------------------------------------------------|-------------------------------------------------------|
| Genuinely exceptional — a bug, a broken dependency | An expected outcome — not found, invalid, conflict    |
| The caller cannot sensibly handle it               | The caller must decide what to do                     |
| Thrown deep, handled far above                     | Handled immediately by the caller                     |
| Programming errors: null argument, invalid state   | Business outcomes: insufficient stock, limit exceeded |
| You want a stack trace                             | The stack trace is noise                              |
| Rare                                               | Common enough that the cost matters                   |

``` csharp
// the cost that motivates Result: a thrown exception is roughly
// 5,000-20,000x a normal return, because of stack capture and unwinding.
// once per request: irrelevant. Once per row over 100,000 rows: fatal.

public readonly record struct Result<T>
{
    private readonly T? _value;
    public bool IsSuccess { get; }
    public Error? Error { get; }

    private Result(T value) { _value = value; IsSuccess = true;  Error = null; }
    private Result(Error e) { _value = default; IsSuccess = false; Error = e; }

    public static Result<T> Ok(T v) => new(v);
    public static implicit operator Result<T>(T v) => new(v);       // ergonomics
    public static implicit operator Result<T>(Error e) => new(e);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> fail)
        => IsSuccess ? ok(_value!) : fail(Error!);
}

public async Task<Result<Order>> GetAsync(Guid id, CancellationToken ct)
{
    var order = await db.Orders.FindAsync([id], ct);
    if (order is null) return Error.NotFound("Order", id);          // implicit
    if (order.TenantId != _ctx.TenantId) return Error.Forbidden();
    return order;                                                   // implicit
}

// one translation at the boundary
return result.Match(
    ok:   o => Results.Ok(mapper.ToDto(o)),
    fail: e => e.ToProblemDetails());
```

``` text
  the honest limitation: C# has no discriminated unions.

  F#     type Result = Ok of Order | NotFound | Forbidden
         match r with | Ok o -> ... | NotFound -> ...
         THE COMPILER CHECKS EXHAUSTIVENESS.

  C#     Result<T> with an Error object.
         nothing forces you to handle every error KIND.
         nothing stops you reading .Value on a failure.

  partial mitigations
    a closed hierarchy of sealed records + a switch EXPRESSION
      -> the compiler warns on unhandled cases
    OneOf<T0,T1,T2>  -> exhaustive Match, verbose signatures
    make the value unreachable without Match
      -> removes the read-on-failure bug

  discriminated unions are a proposed C# feature. Until then, Result is a
  convention the compiler mostly does not enforce - which argues for using
  it selectively rather than everywhere.
```

> **\[Note\]**
>
> **A workable rule.** Result types in the *application* layer, where outcomes are business decisions the caller must branch on. Exceptions for infrastructure failure and programming errors. Do not convert the whole codebase — a system returning `Result` from methods that cannot fail has added ceremony without adding information.
