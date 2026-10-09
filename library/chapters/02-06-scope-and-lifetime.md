---
id: "2-6"
number: "2.6"
title: "Scope and lifetime"
part: 2
part_title: "From source code to running program"
words: 4472
minutes: 19
---

# 2.6 Scope and lifetime

> **Quick look.** Scope (where a name is visible) versus lifetime (how long things exist), plus resources the GC doesn't manage.
>
> - An object lives as long as something can reach it.
> - Static fields are shared by the whole process; AsyncLocal gives each operation its own value.
> - Files, connections and locks need deterministic cleanup with IDisposable and using.
> - Each resource needs one clear owner; don't dispose what you don't own.
> - Finalizers are only a safety net, and they make objects more expensive.
> - In dependency injection, a service must never depend on something shorter-lived than itself (captive dependency).


### The big picture

Two separate questions hide behind "how long does this last?". Scope is about your source code: where a name can be used. Lifetime is about the running program: how long the thing actually exists in memory. They usually line up, and the bugs come from where they don't.

This chapter explains both, then a third thing the garbage collector doesn't look after: resources such as files, database connections and locks, which must be released on time. It ends with lifetimes in dependency injection, where getting them wrong causes some of the most confusing bugs in ASP.NET Core apps.

### Before you start

**Builds on:** 1.5 Memory, 2.4 Values, variables and references, 1.7 The operating system.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **Finalizer** (2.7): A method that runs before an object's memory is reclaimed, as a safety net for native resources.
- **Garbage collection** (2.7): Automatically finding and freeing memory the program can no longer use.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Resource** (8.2): A thing an API exposes at its own URL, such as a flight or a booking.
- **HTTP request** (6.6): A message from client to server: method, path, headers and an optional body.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, generic types, null operators, tuples, exceptions, using for cleanup, async and await, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between scope and lifetime?**

> Scope is where in the source code a name can be used. Lifetime is how long the thing it names exists while the program runs. A local variable's scope is its block, but an object it refers to can live much longer.

**2. What does a using statement guarantee?**

> That `Dispose` is called when the block ends, whether it ends normally or with an exception. It is a `try`/`finally` written for you.

**3. A System.Threading.Timer created in a local variable stops firing after a while. Why?**

> Nothing refers to it any more, so the garbage collector collects it. In Release builds, a local stops counting as a reference after its last use, even before the method ends. Keep the timer in a field.

**4. What is the difference between singleton, scoped and transient lifetimes in .NET dependency injection?**

> Singleton: one instance for the whole app. Scoped: one instance per scope, which in ASP.NET Core means per request. Transient: a new instance every time one is asked for.

**5. What is a captive dependency?**

> A longer-lived service holding a shorter-lived one, such as a singleton holding a scoped `DbContext`. The scoped service is then kept alive and shared far beyond its intended scope.

**6. How is AsyncLocal different from a static field?**

> A static field has one value for the whole process, shared by every request and thread. An `AsyncLocal<T>` value flows with the current asynchronous operation, so concurrent requests each see their own.

**7. Why does the garbage collector not close your files for you?**

> It frees memory, not other resources, and only when it decides to run. A file handle or database connection could stay open for a long time. Resources need deterministic cleanup with `Dispose`.

### Two different questions

**Scope** is about the source code. It is the region of the program where a name means something. Outside its scope, the name doesn't exist, and using it doesn't compile.

**Lifetime** is about running code. It is how long the thing a name refers to actually exists in memory.

The two often line up, but not always. A local variable goes out of scope at the end of its block. An object it referred to can live on, if something else still refers to it. And an object can die before its variable's scope ends, as a later section shows.

### Scopes in C#

C# scopes nest inside each other.

A **block**, anything between `{` and `}`, is a scope. Variables declared inside it can't be seen outside it. Loop variables live in their loop. A method's parameters and locals live in the method. A type's members live in the type. Namespaces group types.

**Shadowing** means declaring a name in an inner scope that hides the same name in an outer scope. C# forbids it for locals: you can't declare a local `total` inside a block if the method already has a local `total`. That rule prevents a whole class of confusing bugs. A local can still hide a field of the same name, which is why code uses `this.name` or an underscore prefix for fields.

### Access modifiers

Scope decides where a name exists. **Access modifiers** decide who may use it.

`private` members are visible only inside their own type. `protected` adds types derived from it. `internal` means anywhere in the same assembly. `public` means anywhere. `file`, added in C# 11, limits a type to the single source file it is declared in. Source generators use it to add helper types without clashing with yours.

Start with the most restricted access that works, and open it up only when needed. Every `public` member is a promise to other code that is hard to take back.

### The lifetime of locals

A local variable lives in a stack frame or a register, as Memory (1.5) described. Its storage is reclaimed when the method returns.

In optimized code, the JIT is smarter than that. A local stops counting as a reference after its last use, even if its scope continues. An object that only a finished local referred to can be garbage collected while the method is still running.

This rarely matters, but it bites in one classic case. A timer, or an object with a finalizer, is created in a local and never used again. It gets collected, and stops working. Keep such objects in a field, or call `GC.KeepAlive(obj)` at the point where it must still be alive.

### The lifetime of objects

A heap object lives as long as something can still reach it. Local variables, static fields and other live objects all count. When nothing can reach it, it becomes garbage, and the collector reclaims its memory at some later point. How the collector decides is covered in Memory management and garbage collection (2.7).

This makes an object's lifetime independent of any scope. A list created in one method and returned lives on in the caller. A lambda that captured a local variable keeps that variable alive, because the compiler moved it into a hidden heap object, as Values, variables and references (2.4) explained.

### Static state

A **static field** belongs to the type, not to any instance. There is one copy for the whole process. It lives from the first time the type is used until the process ends.

That makes static state easy to reach and hard to control. Every thread and every request shares it. Tests that change it affect other tests. Values put into a static cache stay there forever unless something removes them.

Use static fields for true constants and for carefully designed shared services, such as a single shared `HttpClient`. Avoid them for anything that belongs to one user, one request or one operation.

### Per-thread and per-operation state

Sometimes you need a value that is global in reach but separate for each piece of concurrent work.

**Thread-local storage** gives each thread its own copy of a variable. In .NET, mark a static field with `[ThreadStatic]` or use `ThreadLocal<T>`. It works for code that stays on one thread.

Asynchronous code doesn't stay on one thread. After an `await`, the method may continue on a different thread, as Async and await (5.3) explains. **AsyncLocal** values flow with the logical operation instead of the thread. ASP.NET Core uses this idea to track the current request, and logging and tracing libraries use it to carry correlation ids.

```csharp
// ambient.cs — run with: dotnet run ambient.cs
var requestId = new AsyncLocal<string?>();

await Task.WhenAll(Handle("A"), Handle("B"), Handle("C"));

async Task Handle(string id)
{
    requestId.Value = id;          // flows with this operation only
    Shared.Current = id;           // one value for the whole process
    await Task.Delay(Random.Shared.Next(10, 50));
    Console.WriteLine($"request {id}: AsyncLocal sees {requestId.Value}, static field sees {Shared.Current}");
}
// AsyncLocal always matches the request. The static field shows whichever request wrote last.

static class Shared { public static string? Current; }
```

### Memory is not the only resource

The garbage collector manages memory. Programs use other things too: open files, network connections, database connections, locks, operating system handles and native memory. These are **resources**, and they are scarce. A database pool might allow 100 connections. An operating system limits open files, as The operating system (1.7) showed.

The collector doesn't know how scarce they are, and it runs when memory is short, not when connections are. Waiting for it to clean up a connection could take minutes. Resources need **deterministic cleanup**: released at a known point in the code, as soon as they are no longer needed.

### IDisposable and using

.NET's pattern for deterministic cleanup is **IDisposable**. A type that holds a resource implements a `Dispose` method that releases it. `FileStream`, `SqlConnection`, `HttpResponseMessage` and many others do.

A `using` statement calls `Dispose` for you when the block ends, even if an exception is thrown. A **using declaration**, `using var file = ...;`, does the same at the end of the enclosing block, with no extra braces. Objects are disposed in reverse order of creation. `await using` does the same for `IAsyncDisposable`, whose cleanup is asynchronous.

```csharp
// cleanup.cs — run with: dotnet run cleanup.cs
using (var outer = new Resource("outer"))
{
    using var inner = new Resource("inner");   // disposed at the end of this block
    Console.WriteLine("working");
}
// working, inner disposed, outer disposed

try
{
    using var r = new Resource("guarded");
    throw new InvalidOperationException("something failed");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"caught: {ex.Message}");   // after "guarded disposed"
}

sealed class Resource(string name) : IDisposable
{
    bool disposed;
    public void Dispose()
    {
        if (disposed) return;                    // Dispose must be safe to call twice
        disposed = true;
        Console.WriteLine($"{name} disposed");
    }
}
```

### Ownership

Every disposable object needs exactly one **owner**: the code responsible for disposing it. Usually whoever creates it owns it. Problems come from confusion about who that is.

A `StreamReader` wrapped around a stream disposes the stream when the reader is disposed, unless you pass `leaveOpen: true`. A method that receives a disposable as a parameter normally shouldn't dispose it; the caller still owns it. A method that returns one hands ownership to the caller.

With dependency injection, the container owns what it creates and disposes it at the end of its lifetime. Don't dispose injected services yourself.

### Lifetimes in dependency injection

**Dependency injection** means a class receives the objects it depends on, usually through its constructor, instead of creating them. A container creates everything and wires it together. How and why this works is covered in Composition and dependency injection (4.3). Here the point is lifetimes.

Every registration has a **service lifetime**. A **singleton** is created once and shared for the whole life of the app. A **scoped service** is created once per scope. ASP.NET Core creates a scope for every HTTP request, so a scoped service is one per request. EF Core's `DbContext` is registered as scoped by default. A **transient service** is created new every time something asks for it.

The rule is that a service may depend only on services that live at least as long as it does. A singleton that takes a scoped service in its constructor keeps that one instance forever and shares it across all requests and threads. This is a **captive dependency**. With a `DbContext`, it causes threading errors, stale data and memory growth.

The container can check this. ASP.NET Core turns on scope validation in the Development environment. It then throws when a singleton consumes a scoped service, or when a scoped service is resolved outside any scope.

```csharp
#:package Microsoft.Extensions.DependencyInjection@10.0.0
// lifetimes.cs — run with: dotnet run lifetimes.cs
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<Clock>();
services.AddScoped<RequestLog>();
services.AddTransient<Handler>();
using var provider = services.BuildServiceProvider(validateScopes: true);

for (int request = 1; request <= 2; request++)
{
    using var scope = provider.CreateScope();          // one scope per request, as ASP.NET Core does
    var h1 = scope.ServiceProvider.GetRequiredService<Handler>();
    var h2 = scope.ServiceProvider.GetRequiredService<Handler>();
    Console.WriteLine($"request {request}: handlers {h1.Id} and {h2.Id}, log {h1.Log.Id} and {h2.Log.Id}, clock {h1.Clock.Id}");
}
// request 1: handlers 1 and 2, log 1 and 1, clock 1
// log 1 disposed
// request 2: handlers 3 and 4, log 2 and 2, clock 1
// log 2 disposed

try { provider.GetRequiredService<RequestLog>(); }
catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
// Cannot resolve scoped service 'RequestLog' from root provider.

class Clock { static int next; public int Id { get; } = ++next; }

class RequestLog : IDisposable
{
    static int next;
    public int Id { get; } = ++next;
    public void Dispose() => Console.WriteLine($"log {Id} disposed");
}

class Handler(Clock clock, RequestLog log)
{
    static int next;
    public int Id { get; } = ++next;
    public Clock Clock => clock;
    public RequestLog Log => log;
}
```

The first line is a file-level directive. It tells `dotnet run` to fetch a NuGet package for this single-file program.

### Lifetimes across await

An `async` method is compiled into a state machine, like an iterator. When it pauses at an `await`, its local variables are moved into that state machine object on the heap. They live until the method finishes, however long the awaited work takes. A local holding a large buffer across a slow network call keeps that buffer alive the whole time.

### In short

Scope is where a name is visible; lifetime is how long its storage or object exists. An object lives as long as something can reach it, which can be longer or shorter than you expect. Static fields are shared by the whole process, while `AsyncLocal` gives each operation its own value. Resources need deterministic cleanup with `IDisposable` and `using`, and each needs one clear owner. In dependency injection, a service must not depend on anything shorter-lived than itself.

### What goes wrong

**Forgetting to dispose.** Connections, file handles or HTTP responses leak until a pool runs dry or the process runs out of handles. Use `using` for every `IDisposable` you create and own. The .NET analyzers flag many missed cases.

**Disposing something you don't own.** A method disposes a stream its caller still needs, or code disposes an injected service. The next use throws `ObjectDisposedException`.

**Captive dependencies.** A singleton holds a `DbContext` or another scoped service, causing threading errors and stale data. Keep scope validation on. If a singleton really needs scoped work, inject `IServiceScopeFactory` and create a scope for each unit of work.

**Per-request data in static fields.** One user's data appears in another user's response. Use scoped services or `AsyncLocal` instead.

**Objects collected too early.** A timer in a local stops firing in Release builds. Keep it in a field.

**Static caches that only grow.** Memory climbs until the process is killed. Give every cache a size limit or expiry.

### Cost model

Scope costs nothing at run time; it is a compile-time idea. A `using` block costs a `try`/`finally`, which is nearly free when no exception is thrown. Resolving a service from the container takes tens to hundreds of nanoseconds. Transient services allocate on every resolve, singletons never. Resources are where the real costs hide: opening a new database connection can take milliseconds, which is why connection pools exist and why prompt disposal returns connections to the pool quickly.

### In other languages (optional)

**JavaScript and TypeScript.** `let` and `const` are block-scoped. The older `var` is scoped to the whole function, and its declaration is moved to the top of the function, called hoisting. Newer versions add `using` declarations with `Symbol.dispose`, the same idea as C#'s. Node.js has `AsyncLocalStorage`, the equivalent of `AsyncLocal`.

**Python.** Blocks don't create scopes; only functions, classes and modules do. A variable assigned inside an `if` is visible after it. The `with` statement is Python's `using`, driven by `__enter__` and `__exit__`. `contextvars` plays the role of `AsyncLocal`.

**Java.** `try (var file = ...)` is the equivalent of `using`, called try-with-resources, for types that implement `AutoCloseable`.

**Rust and C++.** Objects are destroyed exactly when their owner goes out of scope, with cleanup code run automatically. This is called RAII, "resource acquisition is initialization". It makes cleanup deterministic for memory and resources alike.

### Key terms

- **Scope**: The region of source code where a name can be used.
- **Lifetime**: How long a variable's storage or an object actually exists while the program runs.
- **Shadowing**: Declaring a name in an inner scope that hides the same name in an outer scope.
- **Access modifier**: A keyword such as private, internal or public that controls who may use a member or type.
- **Static field**: A field that belongs to a type rather than an instance. One copy exists for the whole process.
- **Thread-local storage**: A variable with a separate copy for each thread.
- **AsyncLocal**: A .NET type whose value flows with the current asynchronous operation instead of the thread.
- **Deterministic cleanup**: Releasing a resource at a known point in the code, rather than whenever the garbage collector runs.
- **IDisposable**: The .NET interface with a Dispose method that releases a resource.
- **Using declaration**: `using var x = ...;` which disposes x at the end of the enclosing block.
- **Owner**: The code responsible for disposing a disposable object.
- **Dependency injection**: Giving a class the objects it depends on from outside, usually through its constructor.
- **Service lifetime**: How long a dependency injection container keeps an instance: singleton, scoped or transient.
- **Singleton**: A service with one instance for the whole life of the app.
- **Scoped service**: A service with one instance per scope, which in ASP.NET Core means per request.
- **Transient service**: A service created new every time it is requested.
- **Captive dependency**: A long-lived service holding a shorter-lived one, keeping it alive beyond its intended scope.

### Where this leads

How the collector decides an object is garbage is in Memory management and garbage collection (2.7). Cleaning up when things fail is in Errors and exceptions (2.8). Designing with dependency injection is in Composition and dependency injection (4.3). Why async code changes threads is in Async and await (5.3). `DbContext` lifetime in practice is in ORMs and EF Core (7.7).

### Check yourself

1. Register a singleton `Report` that takes `RequestLog` in its constructor in `lifetimes.cs`. Resolve it and read the error message.
2. Fix that design by giving `Report` an `IServiceScopeFactory` and creating a scope inside its method.
3. Change `ambient.cs` to use `[ThreadStatic]` instead of `AsyncLocal`. Run it several times and explain the output.
4. Write a class that owns a `FileStream` and implements `IDisposable` correctly. Check that disposing it twice is safe.
5. Search a codebase you know for `new SqlConnection`, `new HttpClient` and `File.Open`. Check that each one has a clear owner that disposes it, or a good reason not to.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A singleton cache that held a DbContext forever

A `FareCache` registered as a singleton takes `BookingDb` in its constructor. Prices never update and memory grows.

**What goes wrong.** Captive dependency: the scoped DbContext lives as long as the singleton.

*Create a scope per refresh*

```csharp
public sealed class FareCache(IServiceScopeFactory scopes)
{
    public async Task RefreshAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDb>();
        _fares = await db.Fares.AsNoTracking().ToDictionaryAsync(f => f.Route, ct);
    }
    private Dictionary<string, Fare> _fares = new();
}
// builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = true; o.ValidateOnBuild = true; });
```

> **Lesson:** A service must never hold something shorter-lived than itself. Turn on scope validation so the container tells you.

#### Example 2: Connections leaking from a report

After a few hundred report runs, the pool is exhausted: "Timeout expired... obtaining a connection from the pool".

*Dispose deterministically*

```csharp
await using var conn = new SqlConnection(cs);
await conn.OpenAsync(ct);
await using var cmd = new SqlCommand("SELECT ...", conn);
await using var reader = await cmd.ExecuteReaderAsync(ct);
while (await reader.ReadAsync(ct)) { /* ... */ }
// every object is returned or closed here, even if an exception is thrown
```

> **Lesson:** The GC doesn't release connections, files or locks on time. Give each resource one owner and wrap it in using.

### Async/Await in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · Async/Await in Depth</p>

<a id="dn-32"></a>

#### Unmanaged resources and Dispose

**Managed** = handled by the GC (ordinary .NET objects). **Unmanaged** = invisible to the GC: file handles, sockets, database connections, native memory (`Marshal.AllocHGlobal`), GDI handles, native libraries.

```csharp
public sealed class FileProcessor : IDisposable, IAsyncDisposable
{
    private readonly FileStream _stream;      // a managed wrapper around a native handle
    private IntPtr _nativeBuffer;             // genuinely unmanaged
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _stream.Dispose();                                 // managed cleanup
        if (_nativeBuffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_nativeBuffer);            // unmanaged cleanup
        _disposed = true;
        GC.SuppressFinalize(this);                         // skip the finalizer
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();                      // flush I/O without blocking a thread
        Dispose();
    }
}
```

**Finalizers (`~ClassName`)** are only a safety net for callers who forget to dispose. They're expensive — a finalizable object survives at least one extra GC cycle and gets promoted. **The modern approach is `SafeHandle`; don't hand-write finalizers.**

```csharp
using var conn = new SqlConnection(cs);           // using declaration
await using var scope = _factory.CreateAsyncScope();
```

**Special case — `HttpClient`:**

```csharp
// WRONG: using var http = new HttpClient();  -> socket exhaustion (TIME_WAIT)
// ALSO WRONG: one static HttpClient forever -> never picks up DNS changes
// RIGHT: IHttpClientFactory
builder.Services.AddHttpClient<IPaymentApi, PaymentApi>(c =>
{
    c.BaseAddress = new Uri("https://payment.internal");
    c.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler();     // retry + circuit breaker + timeout (Polly)
```

### Members, Inheritance, and Lifetime

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Members, Inheritance, and Lifetime</p>

<p class="pd">Mechanisms that decide how types behave, several of which have surprising failure modes.</p>

<a id="be-b260"></a>

#### IDisposable and lifetime

The GC reclaims **memory**. It knows nothing about file handles, sockets, database connections, or native allocations. Those need explicit release.

``` csharp
public sealed class Connection : IDisposable
{
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;               // must be idempotent
        _handle.Dispose();
        _disposed = true;
    }
}

using var c = new Connection();               // disposed at end of scope
await using var a = new AsyncConnection();    // calls DisposeAsync
```

Rules that matter:

- **`Dispose` must be safe to call more than once.** `using` plus an explicit call is common.
- **`Dispose` is not called automatically.** Miss it and you leak the handle until the finalizer runs, if there is one.
- **Finalizers are a last resort.** They delay collection — a finalizable object survives at least one extra GC and goes on a queue — and they run on a separate thread in unspecified order at an unpredictable time. Only write one if you directly own unmanaged memory, and then use `SafeHandle` instead, which handles it correctly for you.
- **`GC.SuppressFinalize(this)`** in `Dispose` tells the GC to skip the finalizer, since you've already cleaned up. Only needed if you have one.
- **The full Dispose pattern** (`protected virtual Dispose(bool)`) exists for unsealed classes with unmanaged resources. Most types should be `sealed` with a simple `Dispose` instead.

Disposal interacts with async: an `IDisposable` held across an `await` stays alive for the entire wait, because it's a field on the state machine. A database connection held across an unrelated HTTP call is a pooled connection removed from circulation for that whole duration — see the async reference, Part II.7.

------------------------------------------------------------------------

### Runtime: memory, types and the GC

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Runtime: memory, types and the GC</p>

<p class="pd">Where allocations come from, what the collector actually does, and the type-level choices that decide how much work it has to do.</p>

<a id="be-q19"></a>

#### Unmanaged resources, IDisposable and the finalizer trap

The GC manages *memory*. It knows nothing about file handles, sockets, database connections or native allocations — those are unmanaged resources, and releasing them is your job.

``` csharp
// using: deterministic release at the end of the scope
await using var conn = new NpgsqlConnection(cs);   // async dispose
using var file = File.OpenRead(path);
using var http = new HttpClient();                 // ← except: DON'T. See below.

// implementing it: the standard shape for a type that owns something native
public sealed class NativeBuffer : IDisposable
{
    private IntPtr _handle = Marshal.AllocHGlobal(1024);
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        Marshal.FreeHGlobal(_handle);
        _handle = IntPtr.Zero;
        _disposed = true;
        GC.SuppressFinalize(this);      // we cleaned up: skip the finalizer
    }

    ~NativeBuffer() => Dispose();       // safety net only, NOT the plan
}
// better still: use SafeHandle, which handles all of this correctly
```

> **\[Trap\]**
>
> **Common trap.** A finalizer is not a cleanup strategy — it is a last resort with real cost. Objects with finalizers survive their first collection (they go on the finalization queue), get promoted, and are released only after the finalizer thread runs at an unpredictable time. If your type can be disposed deterministically, prefer `SafeHandle` and no finalizer at all.

> **\[Trap\]**
>
> **Common trap.** `new HttpClient()` per request is the famous counter-example: disposing it leaves the socket in `TIME_WAIT`, and under load you exhaust ephemeral ports. Use `IHttpClientFactory`, which pools the handlers and rotates them to pick up DNS changes — a long-lived static `HttpClient` fixes the port problem but caches DNS forever.
