---
id: "2-2"
number: "2.2"
title: "Compilers, interpreters and JIT"
part: 2
part_title: "From source code to running program"
words: 11256
minutes: 49
---

# 2.2 Compilers, interpreters and JIT

> **Quick look.** How C# becomes machine code: compiled to IL at build time, then JIT-compiled and improved while it runs.
>
> - The C# compiler emits IL plus metadata in assemblies; the JIT compiles each method on its first call.
> - That first call is why the first request after a deploy is slow (cold start).
> - Tiered compilation compiles fast first, then recompiles hot methods with full optimization using real profile data (dynamic PGO).
> - ReadyToRun and Native AOT move work to build time for faster start-up, but trimming can't see reflection and breaks it.
> - Source generators replace runtime reflection with compile-time code: faster and AOT-friendly.
> - Only measure speed in Release builds, after warm-up; treat trim and AOT warnings as errors.
> - Native interop (P/Invoke) is where the runtime's safety guarantees stop; wrap handles in SafeHandle.


### The big picture

Your C# code doesn't run as you wrote it. Before it can run, it has to become machine code for the processor it is on. There are two basic ways to do that: translate everything ahead of time, or translate and run it piece by piece.

.NET does a bit of both. It compiles C# into a portable in-between form when you build, then turns that into machine code while the program runs, and improves it as the program keeps running. This chapter explains that journey, why the first request after a deploy is slow, what Native AOT and trimming trade away, and why you never measure speed in a Debug build.

### Before you start

**Builds on:** 1.4 The processor, 2.1 What a programming language is, 1.7 The operating system.

**Ideas explained later:**

- **Performance** (12.4): How quickly a system does its work at a given load.
- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.
- **Serverless** (11.5): Running code that the platform starts on demand, scales automatically and bills per use.
- **BenchmarkDotNet** (12.4): The standard .NET library for writing and running benchmarks.
- **Project file** (11.2): The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.

**Tools and technologies named here:**

- **Deploy** (11.4): To install a new version of software where it will run, such as on production servers.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, tuples. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What does the C# compiler actually produce?**

> Intermediate language (IL) and metadata, packed into a .NET assembly, usually a `.dll`. Not machine code. The runtime's JIT compiler turns IL into machine code when a method first runs.

**2. What is tiered compilation?**

> The JIT first compiles each method quickly with few optimizations, called tier 0. Methods that run often are compiled again with full optimizations, called tier 1. Start-up is fast and hot code still ends up fast.

**3. What does Native AOT give you, and what does it cost?**

> It compiles the whole app to machine code at build time. You get fast start-up, lower memory and no need for a runtime install. You lose the JIT, some reflection and dynamic code loading, and you must fix trimming warnings.

**4. Why is the first request to a freshly started .NET web app slower than the rest?**

> The first request runs many methods for the first time, so they are JIT-compiled then. Assemblies are loaded, caches are empty and connection pools are cold. Later requests reuse all of that.

**5. What is trimming?**

> Removing code the app never uses from the published output, to make it smaller. It works by analysing which code is reachable, so code reached only through reflection can be removed by mistake.

**6. Why should you never measure performance in a Debug build?**

> Debug builds turn off most optimizations, keep variables alive longer and add extra instructions to help the debugger. Timings can be many times slower than Release and misleading about where time goes.

**7. What does the TypeScript compiler do with your types?**

> It checks them, then removes them. The output is plain JavaScript with no type information. Types have no effect at run time.

### Two ways to run a program

A processor only runs machine code for its own instruction set, as The processor (1.4) showed. Source code has to become machine code somehow. There are two basic approaches, and most real systems mix them.

A **compiler** translates the whole program ahead of time into another form, usually machine code. You run the result, not the source.

An **interpreter** reads the program and carries it out directly, piece by piece, while it runs. `calc.cs` in What a programming language is (2.1) is a tiny interpreter: it walks a tree and computes as it goes.

### Compiling ahead of time

C, C++, Rust and Go compile **ahead of time** (AOT). The compiler turns each source file into an object file of machine code. A **linker** then joins the object files and the libraries they use into one executable. The result runs directly on the processor, with no translator at run time.

Libraries can be joined in two ways. **Static linking** copies the library code into your executable. **Dynamic linking** leaves it in a separate shared file, a `.dll` on Windows or a `.so` on Linux, loaded when the program starts. Dynamic linking saves space and allows updates, and it is also where "missing DLL" errors come from.

The advantages of AOT are start-up speed and predictability. The compiler can spend a long time optimizing, because it only runs once. The costs are slower builds and an executable tied to one processor family and one operating system.

### Interpreting

An interpreter is simpler to build and more flexible. You can run code the moment you write it. You can load new code while the program runs. Error messages can point straight at source lines.

The cost is speed. Every operation passes through the interpreter's own logic each time it runs. A loop that runs a million times is decoded and dispatched a million times.

### The middle way: bytecode and virtual machines

Most modern platforms split the difference. The compiler translates source into **bytecode**: compact instructions for an imaginary machine. A **virtual machine** then runs the bytecode. That virtual machine is software, not the hypervisor kind from The operating system (1.7).

Java compiles to JVM bytecode. Python compiles to its own bytecode, cached in `.pyc` files. .NET compiles to **intermediate language**, called IL, MSIL or CIL.

Bytecode is portable. The same `.dll` runs on Windows, Linux and macOS, on x64 and ARM64. The platform's runtime deals with the differences.

### How .NET runs your code

Here is the full path from C# to running instructions.

The C# compiler, Roslyn, reads your source and produces IL plus **metadata**. Metadata describes every type, method and field in detail. Together they are stored in a **.NET assembly**, a `.dll` file. This assembly metadata is what lets tools and reflection list the types and members of code they have never seen. Assemblies are what NuGet packages contain and what your project references.

When the program starts, the runtime loads the assemblies it needs. The first time a method is called, the **JIT compiler** (just-in-time) translates that method's IL into machine code for the current processor. The machine code is kept in memory, so the next call goes straight to it.

You can see a method's IL from inside a program.

```csharp
// il.cs — run with: dotnet run -c Release il.cs
using System.Diagnostics;
using System.Reflection;

byte[] il = typeof(Maths).GetMethod(nameof(Maths.Add))!.GetMethodBody()!.GetILAsByteArray()!;
Console.WriteLine(Convert.ToHexString(il));   // 0203582A in a Release build
// 02 = ldarg.0   push the first argument
// 03 = ldarg.1   push the second argument
// 58 = add       pop two values, push their sum
// 2A = ret       return the value on top of the stack

// The first call includes JIT compilation; later calls don't.
var sw = Stopwatch.StartNew();
Maths.Work(1000);
Console.WriteLine($"first call:  {sw.Elapsed.TotalMicroseconds:F0} µs");
sw.Restart();
Maths.Work(1000);
Console.WriteLine($"second call: {sw.Elapsed.TotalMicroseconds:F0} µs");

static class Maths
{
    public static int Add(int a, int b) => a + b;

    public static long Work(int n)
    {
        long total = 0;
        for (int i = 0; i < n; i++) total += Add(i, i);
        return total;
    }
}
```

IL is a **stack machine**. Instructions push values onto a stack and pop them off, instead of naming registers. The JIT maps that stack onto real registers when it produces machine code.

Tools like ILSpy and sharplab.io show IL as readable text. They can also turn IL back into C#, which is called **decompiling**. Anyone with your `.dll` can read your code this way, so never put secrets in it.

### Tiered compilation

A JIT faces a trade-off. Optimizing well takes time, and the program is waiting. .NET solves this with **tiered compilation**.

At **tier 0**, a method is compiled quickly with almost no optimization. Start-up stays fast. The runtime counts calls. When a method has run about 30 times, it is compiled again at **tier 1**, with full optimization, in the background. The next call uses the fast version.

A method stuck in a long loop can't wait to be called again. **On-stack replacement** (OSR) swaps in optimized code in the middle of the running loop.

Since .NET 8, the runtime also records how the tier-0 code actually behaves. Which branches are taken? Which concrete types show up at interface calls? The tier-1 compile uses that information. This is **profile-guided optimization**, and .NET calls its version Dynamic PGO. A JIT can optimize for the program's real behaviour, which an AOT compiler can only guess at.

### Pre-compiling .NET code

Two options move work from run time to build time.

**ReadyToRun** (R2R) stores pre-compiled machine code in the assembly next to the IL. Start-up is faster because most methods don't need the JIT. The JIT still runs later for hot methods, to produce better tier-1 code. Much of .NET's own library ships as ReadyToRun.

**Native AOT** compiles the entire app to one native executable at publish time. There is no JIT and no IL at run time. Start-up drops to a few tens of milliseconds and memory use falls. It suits command-line tools, serverless functions and small services.

Native AOT has limits. Code can't be generated or loaded at run time. Reflection works only for what the compiler can see. The app must be **trimmed**: the build removes all code it can prove is never used. Code that finds types by name at run time can break. The build reports these risks as warnings, and you must fix them. Libraries that rely heavily on reflection may not work.

### Build configurations

A project normally has two configurations.

**Debug** turns off most optimizations. Variables stay alive until the end of their scope, so the debugger can show them. Code runs close to how it is written, which makes stepping through it easy.

**Release** turns on optimizations. The JIT inlines methods, removes unused variables, keeps values in registers and reorders work. The machine code can look very different from the source.

Always measure performance in Release. Always run benchmarks with a tool such as BenchmarkDotNet, which handles warm-up, tiering and statistics for you.

### Warnings, analyzers and source generators

The compiler reports two kinds of problems. **Errors** stop the build. **Warnings** don't, but they usually point at real bugs. Setting `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in the project file makes every warning stop the build. On new projects, this keeps warnings from piling up.

**Analyzers** are extra checks that plug into the compiler. .NET ships many, for performance, reliability, security and style. They run in the editor and in the build.

**Source generators** run during compilation and write extra C# code into your project. `[GeneratedRegex]`, `System.Text.Json` source generation and `[LibraryImport]` for calling native code all use them. They replace work that used to happen through reflection at run time, which makes code faster and friendly to trimming and Native AOT.

### Transpilers

A **transpiler** is a compiler whose output is another high-level language. The TypeScript compiler is the best-known example. It checks your types and then writes plain JavaScript with the types removed.

Tools such as esbuild and SWC do the same job much faster, but only strip types and don't check them. Many projects use a fast tool for builds and run `tsc` separately just for type checking.

Because the code that runs differs from the code you wrote, transpilers produce **source maps**. A source map links each position in the output back to the original source. Browsers and debuggers use it to show your TypeScript in stack traces and breakpoints.

### In short

A compiler translates ahead of time, and an interpreter runs code directly. The C# compiler produces intermediate language (IL) in assemblies, and the JIT compiler turns IL into machine code the first time each method runs. Tiered compilation compiles quickly first and optimizes busy code later, using its real behaviour. ReadyToRun and Native AOT move work to build time for faster start-up, with limits on reflection. Measure speed in Release builds only, treat warnings seriously, and remember that TypeScript's types vanish when it becomes JavaScript.

### What goes wrong

**Measuring cold starts as normal speed.** The first requests after a deploy are slow because of JIT, loading and empty caches. Warm up services before sending them real traffic, and exclude warm-up from benchmarks.

**Benchmarking Debug builds.** Numbers are meaningless and often point at the wrong hotspot. Use Release and BenchmarkDotNet.

**Trimming away code that reflection needs.** A trimmed or Native AOT app fails at run time with "type not found" or a missing serializer. Treat trimming warnings as errors, and prefer source generators to reflection.

**Secrets in compiled code.** Connection strings and keys embedded in a `.dll` can be read by anyone with a decompiler. Keep them out of the build entirely.

**Warnings ignored for years.** A project with 800 warnings hides the one that matters. Fix them, or turn on warnings as errors for new code.

**Type-stripping builds that don't type-check.** A TypeScript build with esbuild alone ships type errors to production. Run `tsc --noEmit` in the pipeline too.

### Cost model

JIT-compiling one method typically takes from tens of microseconds to a few milliseconds, depending on size and tier. A normal .NET web app starts in a few hundred milliseconds. With Native AOT, a small app can start in tens of milliseconds and use noticeably less memory. ReadyToRun sits in between. A tree-walking interpreter is often tens of times slower than compiled code. A good bytecode interpreter narrows that gap, and a good JIT can match AOT code or beat it using run-time profiles.

### In other languages (optional)

**JavaScript.** Engines like V8 parse source, compile it to bytecode and interpret it, then JIT-compile hot functions with increasingly aggressive optimizations. If assumptions break, for example a function suddenly receives a different type, the engine falls back to slower code. This is called deoptimization.

**TypeScript.** Types exist only at compile time. To check data at run time, such as an API response, you need a validation library like Zod.

**Python.** CPython compiles to bytecode and interprets it. Python 3.13 added an experimental JIT. PyPy is an alternative Python with a mature JIT.

**Java.** The HotSpot JVM uses tiered JIT compilation, much like .NET. GraalVM Native Image does ahead-of-time compilation with the same kinds of limits as Native AOT.

**Go and Rust.** Compile ahead of time to a single executable. Go builds very fast. Rust builds more slowly and spends the time on heavy optimization and safety checks.

**SQL.** The database compiles each query into an execution plan and caches the plan for reuse. That is one reason parameterized queries are faster than building new query text each time.

### Key terms

- **Compiler**: A program that translates source code ahead of time into another form, usually machine code or bytecode.
- **Interpreter**: A program that carries out source code or bytecode directly while it runs.
- **Machine code**: Instructions encoded as numbers, ready for a specific processor to run.
- **Linker**: A tool that joins compiled pieces and libraries into one executable.
- **Dynamic linking**: Loading a shared library when a program starts, instead of copying it into the executable.
- **Bytecode**: Compact instructions for a virtual machine, run by an interpreter or JIT.
- **Intermediate language**: The bytecode .NET compilers produce. Also called IL, MSIL or CIL.
- **.NET assembly**: A `.dll` or `.exe` holding IL and metadata. The unit .NET loads and references.
- **Assembly metadata**: A detailed description of every type and member in an assembly, used by the runtime and tools.
- **JIT compiler**: A compiler that turns bytecode into machine code while the program runs, just before it is needed.
- **Tiered compilation**: Compiling a method quickly first, then again with full optimization once it proves to be hot.
- **Profile-guided optimization**: Optimizing code based on how it actually behaved when run. Dynamic PGO does this in the .NET JIT.
- **ReadyToRun**: Pre-compiled machine code stored inside a .NET assembly to speed up start-up.
- **Native AOT**: Compiling a whole .NET app to one native executable at publish time, with no JIT.
- **Trimming**: Removing unused code from a published app to make it smaller.
- **Source generator**: A compiler plug-in that writes extra C# code during the build.
- **Transpiler**: A compiler whose output is another high-level language, such as TypeScript to JavaScript.
- **Source map**: A file linking positions in generated code back to the original source.

### Where this leads

The types the compiler checks are in Types (2.3). How the runtime manages memory is in Memory management and garbage collection (2.7). Project files, packages and build tools are in Builds and dependencies (11.2). Measuring performance properly is in Scaling and performance (12.4). Why `DOTNET_JitDisasm` prints several versions of one method now makes sense: go back to The processor (1.4) and look again.

### Check yourself

1. Run `il.cs` in Debug and in Release. Compare the IL bytes of `Add` and explain any difference.
2. Change `Add` in `il.cs` to `a * b + 1`. Predict the IL bytes before running it. Multiply is `5A` and `ldc.i4.1` is `17`.
3. Create a new console app, publish it with `dotnet publish -c Release -p:PublishAot=true`, and compare its start-up time and file size with a normal publish.
4. Open a `.dll` from one of your projects in ILSpy. Find a method and compare the decompiled C# with your source.
5. Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to a copy of a real project. Count how many warnings stop the build.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The first request after every deploy takes 4 seconds

A latency alert fires right after each deploy, then clears. The first requests pay for JIT compilation and cold caches.

*Warm up and precompile*

```csharp
// csproj: <PublishReadyToRun>true</PublishReadyToRun>  (precompiled code, faster start)
// and keep tiered PGO on (default) for steady-state speed.

builder.Services.AddHostedService<WarmUp>();
sealed class WarmUp(IServiceProvider sp) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDb>();
        await db.Flights.AsNoTracking().Take(1).ToListAsync(ct);   // opens pool, builds EF model
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- Report readiness only after warm-up, so the load balancer doesn't send traffic to a cold instance.

> **Lesson:** Cold start is real: JIT, connection pools and model building all happen on first use. Do them before taking traffic.

#### Example 2: Trimming removed a type the serializer needed

A Native AOT build of a CLI tool works in Debug but throws at runtime when reading its settings JSON.

**What goes wrong.** Reflection-based System.Text.Json can't see types the trimmer removed.

*Source-generated serialization*

```csharp
[JsonSerializable(typeof(ToolSettings))]
internal partial class SettingsContext : JsonSerializerContext { }

var settings = JsonSerializer.Deserialize(json, SettingsContext.Default.ToolSettings);
// csproj: <PublishAot>true</PublishAot>  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
// Fix every IL2xxx/IL3xxx warning; they predict exactly these runtime failures.
```

> **Lesson:** Trim and AOT warnings are not advisory. Replace reflection with source generators and test the published build, not Debug.

### Async/Await in Depth

<p class="src">From <em>.NET Backend Deep Dive</em> · Async/Await in Depth</p>

<a id="dn-33"></a>

#### JIT and tiered compilation

1. IL (bytecode) is produced at build time.
2. The first time a method is called, the **JIT** compiles IL to machine code. This is why the first request is slow (**cold start**).
3. **Tiered compilation**: tier 0 compiles fast with few optimizations. A method called more than ~30 times is **re-JITted** at tier 1 with full optimization (inlining, loop hoisting, devirtualization).
4. **OSR (on-stack replacement)**: a long-running loop stuck in tier 0 gets upgraded mid-execution.
5. **Dynamic PGO** (on by default since .NET 8): the runtime collects real profiles and optimizes hot paths — often better than static optimization.
6. **ReadyToRun (R2R)** or **AOT** to cut cold start:

```xml
<PublishReadyToRun>true</PublishReadyToRun>
<TieredPGO>true</TieredPGO>
<!-- Native AOT: millisecond startup, no JIT, but no reflection/dynamic code -->
<PublishAot>true</PublishAot>
```

**Relation to async:** the state machine's `MoveNext` usually isn't inlined, so very small async methods called in tight loops can be slower than sync ones — which is exactly why the `IsCompleted` fast path and `ValueTask` exist.

**Relation to benchmarking:** always warm up before measuring, or you're timing tier-0 code. BenchmarkDotNet handles this automatically.

---

### AOT, Trimming & Source Generators

<p class="src">From <em>.NET Backend Deep Dive</em> · AOT, Trimming & Source Generators</p>

<a id="dn-161"></a>

#### What Native AOT buys and costs

```xml
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
<StripSymbols>true</StripSymbols>
```

| Gain | Cost |
|---|---|
| Startup in milliseconds, not seconds | **No runtime reflection over unreferenced code** |
| Much lower memory | **No `Assembly.Load`, no `Emit`, no dynamic proxies** |
| Single native binary, no runtime install | Longer build, platform-specific output |
| Smaller attack surface | Many libraries simply don't work |

Concretely: EF Core's runtime model building, Castle-based dynamic proxies (used by several mocking and AOP libraries), reflection-based JSON, and anything scanning assemblies at startup are all problems. Minimal APIs plus source-generated JSON are the supported path.

Where it's genuinely worth it: **Lambda and other serverless functions** (cold start is the dominant cost — Part 9.2), CLI tools, and sidecars. For a long-running API pod, startup time rarely matters enough to justify the constraints; `PublishReadyToRun` gives you most of the startup win with none of them.

<a id="dn-162"></a>

#### Trimming

```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>full</TrimMode>
```

The trimmer removes unreferenced IL — and it can't see reflection. The failure mode is nasty: the app builds, publishes, starts, and then throws `MissingMethodException` on a code path nobody tested. **Always enable the analyzers** (`<EnableTrimAnalyzer>`), treat `IL2xxx` warnings as errors, and run your full integration suite against the trimmed output, not the debug build.

<a id="dn-163"></a>

#### Source generators

Compile-time code generation with no runtime reflection cost — the general escape hatch from the AOT constraints:

```csharp
[JsonSerializable(typeof(OrderDto))]                       // System.Text.Json
internal partial class AppJson : JsonSerializerContext { }

[GeneratedRegex(@"^\d{4}$")]                               // Regex
private static partial Regex Code();

[LoggerMessage(Level = LogLevel.Warning,                   // logging
    Message = "Order {OrderId} rejected: {Reason}")]
static partial void LogRejected(ILogger logger, Guid orderId, string reason);
```

`LoggerMessage` is worth adopting even without AOT: it avoids boxing the arguments and skips formatting entirely when the level is disabled — a measurable win on hot paths where `LogDebug` is called and discarded.

Others worth knowing: `[GeneratedComInterface]`, `LibraryImport` (the AOT-safe successor to `DllImport`), and Mapperly or similar for compile-time object mapping — which also removes the runtime cost and startup scan of reflection-based mappers.

---

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a12"></a>

#### The .NET landscape in one page

There are three things people mean by “.NET”, and conflating them causes most of the confusion in job specs and Stack Overflow answers.

| Name                 | What it is                                                                 | Status                     |
|----------------------|----------------------------------------------------------------------------|----------------------------|
| .NET Framework 4.8   | Windows-only, in-place-installed runtime. Last version ever.               | Maintained, never enhanced |
| .NET Core → .NET 5–9 | Cross-platform, side-by-side, open source rewrite. Just called `.NET` now. | Where all work happens     |
| .NET Standard        | A contract describing an API surface both could implement.                 | Legacy bridge only         |

Modern .NET ships on a yearly cadence each November. Even-numbered releases (8, 10) are LTS with three years of support; odd ones (9, 11) get eighteen months. Pick LTS for anything you do not want to touch.

##### What actually happens when you build and run C#

``` text
  C# is NOT compiled to machine code by the compiler. Two steps, not one.

  Program.cs
      |
   [ Roslyn, at BUILD time ]
      |
      v
  MyApp.dll  -- contains IL (Intermediate Language), not x64 instructions.
                a portable, CPU-neutral instruction set, plus METADATA
                describing every type, method and field.
      |
   [ the CLR, at RUN time ]
      |
   [ JIT: Just-In-Time compilation ]   the first time each METHOD is called,
      |                                its IL is compiled to real machine
      v                                code for THIS CPU, and cached.
  x64 / arm64 machine code

  which is why:
    - the same .dll runs on Windows, Linux, x64 and arm64
    - the FIRST call to a method is slower than the rest
    - a decompiler can reconstruct readable C# from a .dll
    - the runtime can inspect types at run time (reflection), because
      the metadata is right there in the file
    - "managed" means: the CLR controls execution - memory, type safety,
      exceptions - rather than your code talking to the CPU directly
```

The word **managed** is worth pinning down, because it recurs constantly. Managed code runs under the CLR, which allocates and reclaims its memory, enforces type safety, and handles exceptions. Unmanaged code — a C library, an operating system call — does none of that for you. The boundary between the two is where `P/Invoke`, marshalling, and most memory-safety bugs live.

##### What to install, and what to target

| You want to…                            | Install                                        | Target                               |
|-----------------------------------------|------------------------------------------------|--------------------------------------|
| Build anything                          | The **SDK** — it contains the runtime          | —                                    |
| Only run a published app                | The **runtime** (`aspnetcore-runtime` for web) | —                                    |
| Start a new service                     | Latest SDK                                     | Latest **LTS** (`net10.0`)           |
| Publish a library others consume        | Latest SDK                                     | Oldest LTS you support, *and* latest |
| Work on an existing Framework app       | Visual Studio + the .NET Framework dev pack    | `net48`                              |
| Share code between Framework and modern | Latest SDK                                     | `netstandard2.0`                     |

> **\[Note\]**
>
> **The three-question orientation.** **Which runtime?** Modern .NET, unless the app is pinned to Framework. **Which version?** The latest LTS, unless you need something only an STS has. **Which target framework moniker?** Whatever goes in `<TargetFramework>` — `net10.0` for new work, `net48` for Framework, `netstandard2.0` only for a library that must serve both. Nearly every “which .NET is this?” confusion resolves to one of those three.

###### Version support, and choosing what to target

``` text
  the release cadence, which is fixed and predictable

    every NOVEMBER, one release
      EVEN numbers  = LTS  (Long Term Support)   3 years
      ODD numbers   = STS  (Standard Term)       18 months

    .NET 8   LTS   Nov 2023  ->  Nov 2026
    .NET 9   STS   Nov 2024  ->  May 2026
    .NET 10  LTS   Nov 2025  ->  Nov 2028
    .NET 11  STS   Nov 2026  ->  ...

  patch releases arrive on the second Tuesday of each month.
  "support" means SECURITY PATCHES. Out of support = no CVE fixes,
  which for most organisations is a compliance problem, not a preference.
```

| Situation                         | Target                                      | Reasoning                                                |
|-----------------------------------|---------------------------------------------|----------------------------------------------------------|
| New line-of-business application  | Latest **LTS**                              | Three years of patches; one upgrade per two years        |
| Library published to NuGet        | The oldest LTS you can support, plus latest | Multi-target so consumers are not forced forward         |
| You need a feature only in an STS | STS, and plan the hop to the next LTS       | STS → next LTS is designed to be a small step            |
| Existing .NET Framework 4.8 app   | It is still supported indefinitely          | No forced deadline — migrate for benefit, not for panic  |
| Container base images             | Match the SDK exactly                       | A mismatched runtime image is a class of startup failure |

``` csharp
<!-- multi-targeting a library: one build, several consumers -->
<PropertyGroup>
  <TargetFrameworks>net8.0;net10.0;netstandard2.0</TargetFrameworks>
  <LangVersion>latest</LangVersion>
</PropertyGroup>

<!-- conditional code per target, when APIs genuinely differ -->
#if NET8_0_OR_GREATER
    await foreach (var x in source.WithCancellation(ct)) { }
#else
    foreach (var x in source) { }
#endif

<!-- and pin the SDK so every machine and CI agent builds identically -->
<!-- global.json, at the repository root -->
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

The practical upgrade rule: **major version upgrades within modern .NET are usually a one-line change plus a test run.** The compatibility story since .NET 5 is genuinely good, and the friction is almost always a third-party package that has not published a matching target — check your dependency list before you check the release notes.

### Under the Hood

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Under the Hood</p>

<p class="pd">What actually happens between dotnet run and a byte on the wire. Assembly loading, the JIT, the thread pool, Kestrel, routing, model binding, filters and the DI container — the machinery that makes everything above it feel like magic until it breaks.</p>

<a id="be-a40"></a>

#### From dotnet run to a listening socket

A .NET application does not start at `Main`. By the time your first line runs, a considerable amount of native and managed bootstrapping has already happened.

``` text
   1  apphost / dotnet muxer  reads  MyApp.runtimeconfig.json
                              -> which framework version, which GC mode, which switches
   2  hostfxr  resolves the framework  (or uses the self-contained copy)
   3  hostpolicy  builds the TPA list from  MyApp.deps.json
                              -> the exact set of assemblies allowed to load
   4  coreclr.dll / libcoreclr.so  initialises
                              -> GC heaps, thread pool, AppDomain, type system
   5  the entry assembly is loaded, Main's IL is JIT-compiled, Main runs
   6  WebApplication.CreateBuilder  -> configuration, logging, DI registration
   7  builder.Build()               -> the container is built and frozen
   8  app.Run()                     -> Kestrel binds the socket, host waits on SIGTERM
```

| File                   | Contains                                              | Debug it when                                               |
|------------------------|-------------------------------------------------------|-------------------------------------------------------------|
| `*.runtimeconfig.json` | Framework version, GC/tiering switches, feature flags | “You must install .NET x” on a machine that has it          |
| `*.deps.json`          | Full dependency graph and asset paths                 | `FileNotFoundException` for an assembly that is right there |
| `DOTNET_ROOT`          | Where to find shared frameworks                       | Works in the IDE, fails from the terminal                   |

``` json
{
  "runtimeOptions": {
    "tfm": "net9.0",
    "framework": { "name": "Microsoft.AspNetCore.App", "version": "9.0.0" },
    "configProperties": {
      "System.GC.Server": true,
      "System.GC.Concurrent": true,
      "System.Runtime.TieredPGO": true,
      "System.Globalization.Invariant": false
    }
  }
}
```

> **\[Why it matters\]** Almost every “works on my machine” deployment failure lives in these three lines of the bootstrap: a missing framework, a deps.json that does not match what was copied, or a missing ICU library in a slim container image.

###### The startup sequence, and where the milliseconds go

``` text
  what actually executes, in order

  1  APPHOST            MyApp.exe - a native shim generated at build time.
                        Its only job: find and load hostfxr.
  2  HOSTFXR            reads MyApp.runtimeconfig.json
                        -> which framework version? roll-forward policy?
                        resolves an installed shared framework, or self-contained
  3  HOSTPOLICY         reads MyApp.deps.json
                        -> builds the TRUSTED PLATFORM ASSEMBLIES list:
                           every dll this app is allowed to load, resolved
                           from the app dir, NuGet cache and shared framework
  4  CORECLR            starts: GC heaps, thread pool, type system
  5  Main               your entry point. JIT compiles it first.
  6  HostBuilder        configuration providers, DI registrations, logging
  7  StartAsync         hosted services start, Kestrel BINDS the socket
  8  first request      JIT compiles the whole request path at TIER 0

  a typical cold start, roughly:
     host resolution + CLR init   ~30-60 ms
     DI container build           ~5-50 ms   (grows with registration count)
     first-request JIT            ~50-200 ms (tier 0, unoptimised)
     steady state reached         after ~30 calls per hot method (tier 1)
```

``` text
// runtimeconfig.json - what hostfxr reads. Worth knowing when a container
// fails at startup with "framework not found".
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" },
    "rollForward": "latestPatch",     // Minor | LatestMinor | Major | Disable
    "configProperties": {
      "System.GC.Server": true,        // server GC: more throughput, more memory
      "System.GC.Concurrent": true,
      "System.Runtime.TieredPGO": true
    }
  }
}
```

| Startup cost            | Cause                                                 | Reduce by                                        |
|-------------------------|-------------------------------------------------------|--------------------------------------------------|
| Framework resolution    | Shared-framework lookup on disk                       | Self-contained or AOT if cold start matters      |
| Assembly loading        | Many small assemblies, each opened and mapped         | Fewer, larger assemblies; ReadyToRun             |
| JIT at tier 0           | Every method compiled unoptimised on first call       | ReadyToRun precompiles; AOT eliminates it        |
| DI container build      | Hundreds of registrations, assembly scanning          | Avoid scanning; register explicitly              |
| Configuration providers | Remote sources (Key Vault, App Config) hit at startup | Cache; fail fast; consider lazy                  |
| EF Core model build     | The model is built on first use, not at startup       | Compiled models (`dotnet ef dbcontext optimize`) |

``` text
# measure it rather than guessing
dotnet-counters monitor --process-id <pid> \
  System.Runtime Microsoft.AspNetCore.Hosting

# ReadyToRun: precompile IL to native at BUILD time. Keeps the JIT for
# hot paths but removes most tier-0 cost. ~2-3x faster startup, larger binary.
<PublishReadyToRun>true</PublishReadyToRun>

# Native AOT: no JIT at all. Millisecond startup, small memory.
# Cost: no runtime code generation, limited reflection, some libraries unusable.
<PublishAot>true</PublishAot>
```

<a id="be-a41"></a>

#### Assemblies, IL and the loading process

C# compiles to IL — a stack-based intermediate language — plus metadata describing every type, method and attribute. The assembly is a PE file containing both. Nothing is machine code until the JIT touches it.

``` csharp
// C#
public int Add(int a, int b) => a + b;

// IL
IL_0000: ldarg.1        // push a
IL_0001: ldarg.2        // push b
IL_0002: add
IL_0003: ret
```

##### How an assembly gets found

1.  The JIT hits a reference to a type in another assembly and asks the runtime to resolve it.
2.  The default `AssemblyLoadContext` checks what is already loaded.
3.  It consults the TPA list built from `deps.json` — an explicit list, not a directory scan.
4.  If still unresolved, the `Resolving` event fires; this is where plugin hosts hook in.
5.  Failure throws `FileNotFoundException` or `FileLoadException` at the moment of first use, not at startup.

`AssemblyLoadContext` replaced AppDomains. It gives you isolation and, crucially, *unloadability* — a collectible ALC can be torn down at runtime, which is how plugin systems and hot reload work on modern .NET.

``` csharp
var alc = new AssemblyLoadContext("plugin-v2", isCollectible: true);
var asm = alc.LoadFromAssemblyPath(pluginPath);
var type = asm.GetType("Plugin.Entry")!;
// ... use it, drop every reference ...
alc.Unload();      // memory reclaimed after the next GC
```

> **\[Trap\]**
>
> **Common trap.** Assembly binding failures are lazy. A method that references a missing assembly loads and JITs fine until the first call. That is why a deployment can pass a smoke test and fail on an obscure endpoint an hour later.

###### Assembly resolution, and the version conflicts it produces

``` text
  MODERN .NET                          .NET FRAMEWORK

  deps.json lists every assembly       the GAC: a machine-wide store
  and its exact path                   probing paths + binding redirects
    |                                    |
  AssemblyLoadContext.Default          AppDomain + Fusion
    |                                    |
  no GAC, no machine-wide state        machine-wide, shared, versioned
  app-local, side-by-side by default   in-place upgrades affect everyone

  the practical consequence: modern .NET has NO binding redirects, because
  each app carries its own resolved closure. The whole class of
  "works on my machine, wrong assembly version in production" mostly
  disappeared - and reappears in a different form, below.
```

``` bash
// the modern version of the problem: DIAMOND DEPENDENCIES
//
//   YourApp  ->  LibraryA  ->  Newtonsoft.Json 12.0.0
//            ->  LibraryB  ->  Newtonsoft.Json 13.0.1
//
// NuGet resolves ONE version for the whole graph: the HIGHEST, 13.0.1.
// LibraryA was compiled against 12 but loads 13. Usually fine.
// It breaks when 13 removed or changed something A used -> MissingMethodException
// at RUNTIME, not build time.

// see what was actually chosen, and why
dotnet list package --include-transitive
dotnet nuget why MyApp Newtonsoft.Json

// and pin it deliberately when it matters
<PackageReference Include="Newtonsoft.Json" VersionOverride="13.0.3" />
```

``` csharp
// AssemblyLoadContext: the replacement for AppDomains, for plugin isolation
public class PluginContext(string path) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _r = new(path);
    protected override Assembly? Load(AssemblyName name)
    {
        var p = _r.ResolveAssemblyToPath(name);
        return p is null ? null : LoadFromAssemblyPath(p);   // null = fall back to Default
    }
}

// isCollectible: true means the context CAN be unloaded - but only when every
// reference to anything in it is gone, including delegates, open generic
// instantiations and cached Types. In practice unloading is difficult to
// achieve; assume the plugin stays loaded and design accordingly.
```

| Symptom                                            | Cause                                                                             |
|----------------------------------------------------|-----------------------------------------------------------------------------------|
| `FileNotFoundException` on an assembly that exists | Not listed in `deps.json` — usually a copy-local or publish issue                 |
| `MissingMethodException` at runtime                | Compiled against one version, loaded another                                      |
| Two versions of a type that ‘is’ the same          | Loaded into different `AssemblyLoadContext`s — type identity includes the context |
| Works in dev, fails in a single-file publish       | Assembly loaded by path or by reflection from disk                                |
| Works locally, breaks with trimming                | Reflection the trimmer could not see                                              |

> **\[Trap\]**
>
> **Common trap.** Type identity in .NET is (assembly name, version, culture, public key, *load context*). Two assemblies loaded into different contexts produce types that are *not* equal, so a cast fails with a message that reads like nonsense — *“cannot convert Order to Order”*. If you see that, you have a load-context problem, not a code problem.

<a id="be-a42"></a>

#### The JIT, tiered compilation and dynamic PGO

Methods are compiled on first call. Modern .NET compiles twice: quickly at tier 0 so startup is fast, then again at tier 1 with full optimisation once a method proves hot — using profiling data collected from the tier 0 version.

``` text
  first call        method compiled at TIER 0
                    minimal optimisation, fast to produce
                    instrumented: counts calls, records which types actually appear

  ~30 calls         promoted to TIER 1
                    inlining, bounds-check elimination, register allocation,
                    devirtualisation and guarded devirtualisation from the PGO data

  long loop         ON-STACK REPLACEMENT swaps the running frame to tier 1
                    (so a method that never returns still gets optimised)
```

Dynamic PGO, on by default since .NET 8, is why interface dispatch is often free in practice: if 99% of calls to `IRepository.Get` land on `SqlRepository`, the JIT emits a type check plus a direct inlined call, with a fallback for the rare case.

| Knob                                | Effect                                   | When to touch it                          |
|-------------------------------------|------------------------------------------|-------------------------------------------|
| `TieredCompilation=false`           | Straight to fully optimised code         | Benchmarking; steady-state-only workloads |
| `TieredCompilationQuickJitForLoops` | Controls tier 0 for loop methods         | Rarely; OSR made it mostly obsolete       |
| ReadyToRun                          | Ships pre-compiled tier-0-quality code   | Cold-start-sensitive services             |
| Native AOT                          | No JIT at all, everything compiled ahead | CLI tools, serverless, tiny containers    |

> **\[Why it matters\]** This explains a real production observation: the first thirty seconds after a pod starts are slower and hotter, and load balancers that send full traffic immediately will see elevated p99. A warm-up request in your readiness probe fixes it.

###### Worked example

What tiering looks like on a real endpoint, measured. A minimal API returning a serialized DTO, hit with a constant 500 requests per second from a cold start:

``` text
  time      p50      what is happening
  ------------------------------------------------------------------------
  0-2 s     14 ms    everything JITs at tier 0; also first-time assembly loads,
                     first EF model build, first JWKS fetch, first connection open
  2-10 s     6 ms    hot methods promoted to tier 1; PGO data being collected
  10-30 s    3.1 ms  guarded devirtualisation kicks in on the hot interface calls
  30 s+      2.8 ms  steady state
```

The first two seconds are five times slower than steady state, and none of it is your code being wrong. This is why a pod that passes its readiness probe instantly and receives full traffic immediately produces a p99 spike on every single deployment — and why the spike disappears if you send the pod a few hundred warm-up requests before marking it ready.

``` csharp
// a warm-up hosted service: exercise the real paths before serving traffic
public sealed class Warmup(IServiceScopeFactory scopes, ILogger<Warmup> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        await db.Database.CanConnectAsync(ct);          // opens a pooled connection
        _ = await db.Orders.AsNoTracking().Take(1).ToListAsync(ct);  // builds the EF model + JIT
        _ = JsonSerializer.Serialize(OrderResponse.Sample);          // JITs the serializer

        log.LogInformation("Warm-up complete");
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Building the EF Core model is often the single largest cold-start cost — hundreds of milliseconds for a large model, once per process. A compiled model (`dotnet ef dbcontext optimize`) moves that to compile time and can cut startup dramatically on model-heavy services.

###### Reading the generated code when it matters

``` csharp
# see what the JIT produced for a specific method
DOTNET_JitDisasm="Add" dotnet run

# or, far more practically, use BenchmarkDotNet's disassembly diagnoser
[DisassemblyDiagnoser(printSource: true, maxDepth: 2)]
[MemoryDiagnoser]
public class Bench { ... }
```

You will rarely need this, but knowing it exists changes how you argue about performance. Two things it commonly reveals: that a method you assumed was inlined is not (usually because it is too large, has a loop, or contains a try/catch), and that bounds checks you expected to be eliminated are still present because the loop condition does not match the pattern the JIT recognises.

``` csharp
// the JIT eliminates the bounds check here - the pattern is recognised
for (int i = 0; i < array.Length; i++) sum += array[i];

// it cannot here - the length is captured, so it might change
int len = array.Length;
for (int i = 0; i < len; i++) sum += array[i];      // bounds check retained

// spans make the intent explicit and are reliably optimised
foreach (var x in array.AsSpan()) sum += x;
```

<a id="be-a43"></a>

#### Native interop: P/Invoke, marshalling and handles

Sooner or later something has no managed equivalent — a vendor SDK, an imaging or crypto library, a device driver, a Win32 call the legacy app depended on. P/Invoke is the bridge, and it is where the runtime’s safety guarantees stop.

``` csharp
// modern: LibraryImport is a SOURCE GENERATOR — it emits the marshalling
// code at compile time, so it works under Native AOT and is trimmable
[LibraryImport("libsodium", EntryPoint = "crypto_generichash",
               SetLastError = true)]
private static partial int Hash(
    Span<byte> output, nuint outLen,
    ReadOnlySpan<byte> input, ulong inLen,
    ReadOnlySpan<byte> key, nuint keyLen);

// legacy: DllImport uses runtime marshalling (reflection-based)
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern SafeFileHandle CreateFile(
    string path, uint access, uint share, IntPtr security,
    uint creation, uint flags, IntPtr template);

// SafeHandle, not IntPtr: the finalizer releases the OS resource even
// if your code throws, and it blocks handle recycling attacks
using var handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ,
                              IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
if (handle.IsInvalid)
    throw new Win32Exception(Marshal.GetLastWin32Error());
```

###### What marshalling actually costs

Crossing the boundary means fixing (pinning) managed memory so the GC cannot move it, converting types the native side does not understand, and switching the thread’s GC mode. Blittable types — `int`, `double`, structs of blittable fields, and spans over them — need no conversion and are nearly free. Strings, arrays of non-blittable structs, and `bool` all require copying or transformation. A call in a loop that marshals a string each time can cost more in conversion than the native work it performs.

> **\[Trap\]**
>
> **Common trap.** Pinning long-lived buffers fragments the heap: the GC cannot compact around a pinned object. Pin briefly (`fixed` blocks, spans over stack memory) or allocate from the pinned object heap (`GC.AllocateArray<T>(n, pinned: true)`) for buffers the native side keeps.

> **\[Trap\]**
>
> **Common trap.** An unhandled exception thrown across the native boundary corrupts the stack, and a mismatched calling convention or struct layout produces silent memory corruption rather than a clean error. Wrap every P/Invoke in a thin managed facade, test it under load, and treat a crash with no stack trace as a signal to look here first.

> **\[Why it matters\]** Interop is also the migration escape hatch. When a legacy component cannot be ported — a licensed native library, a decades-old algorithm nobody will re-certify — wrapping it behind a managed facade lets the new system move forward without waiting for a rewrite that may never come.

### How .NET actually runs

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How .NET actually runs</p>

<a id="be-m37"></a>

#### Source to machine code: the whole pipeline

C# is compiled twice. Roslyn turns source into a platform-neutral intermediate form at build time; the runtime turns that into machine code at execution time. Nearly every runtime behaviour in this part follows from the split.

``` text
  Program.cs ──► Roslyn (csc) ──► MyApp.dll         BUILD TIME
                                  ├── IL (bytecode)
                                  ├── metadata (types, members, refs)
                                  └── resources

  MyApp.dll ──► host starts runtime ──► assembly loaded lazily     RUN TIME
                                    ──► type loaded on first use
                                    ──► method JIT-compiled on first CALL
                                    ──► machine code cached, reused
                                    ──► hot methods RE-compiled, better
```

Two consequences worth carrying:

- **Nothing is compiled to machine code until it is used.** A method never called is never JIT-compiled. This is why startup cost is proportional to *code paths exercised*, not to assembly size, and why the first request to an endpoint is always the slowest.
- **The final optimiser sees the actual machine and the actual runtime types.** It knows the CPU's instruction set, whether a class was ever subclassed, and which type really flowed through an interface. That is information no ahead-of-time compiler has — and it is why `sealed` and generic constraints help ([Following a reference: dereference and dispatch](01-05-memory.md#be-m29)).

``` csharp
int Add(int a, int b) => a + b;

// IL - a stack machine, platform neutral, no registers:
//   ldarg.0
//   ldarg.1
//   add
//   ret

// x64 after JIT - registers, one instruction:
//   lea eax, [rcx+rdx]
//   ret
```

> **\[Tip\]**
>
> **See it yourself.** sharplab.io shows the IL and the JIT assembly for any snippet side by side. Setting `DOTNET_JitDisasm=MethodName` dumps the emitted code for a real process. Reading the output for one method you thought you understood is a fast way to calibrate what the JIT does and does not do ([Seeing it yourself: measuring layout and allocation](01-05-memory.md#be-m36)).

First principles: [Machine code, assembly, and compilers](01-04-the-processor.md#be-m4)

<a id="be-m38"></a>

#### Assemblies, IL, and metadata

A `.dll` is a PE file — the same container format as a native Windows binary — but its payload is IL plus **metadata**: a set of relational tables describing every type, method, field, and external reference.

``` text
MyApp.dll
 ├── PE headers                  (so the OS recognises the file)
 ├── CLI header                  (entry point, flags, metadata location)
 ├── Metadata tables
 │    ├── TypeDef      types defined here
 │    ├── MethodDef    methods, with signatures and IL offsets
 │    ├── Field        fields, with types and layout hints
 │    ├── TypeRef      types referenced from elsewhere
 │    ├── AssemblyRef  assemblies this one depends on, WITH VERSIONS
 │    └── CustomAttribute  every attribute you wrote
 ├── IL method bodies
 └── Resources, strong-name signature (optional)
```

Metadata is why .NET reflection is so capable: the type system is fully described in the file, so `typeof(T).GetProperties()` is a table read rather than a guess. It is also why *everything* that reads types at runtime — serializers, ORMs, DI containers, mappers — can work on types they have never seen ([Source generation vs reflection](#be-m64)).

``` csharp
// Everything the runtime knows about your code is inspectable:
var asm = typeof(Order).Assembly;
asm.GetName().Version;                    // from the metadata, not the file name
asm.GetReferencedAssemblies();            // the AssemblyRef table
typeof(Order).GetCustomAttributes();      // the CustomAttribute table

// And the JIT reads it too: field offsets, method table layout, and the
// generic instantiations it must specialise all come from here
// (§58).
```

> **\[Tip\]**
>
> **Tools worth having.** ILSpy or dnSpy decompile an assembly back to readable C# — invaluable on a legacy platform when the source for a dependency is missing or the NuGet package does not match what is deployed. `ildasm` shows raw IL, which is where you go to settle "does this actually box?" ([Boxing and unboxing](01-05-memory.md#be-m33)).

> **\[Trap\]**
>
> **The version in metadata is not the file name.** Assembly identity is name + version + culture + public key, and a binding failure reports the *requested* identity, not the file you think you shipped. On a legacy solution with mixed package versions, always check the actual `AssemblyVersion` of what loaded, not the folder contents.

<a id="be-m39"></a>

#### Loading: probing, versions, and contexts

.NET Core removed the GAC. Dependency resolution is now driven by two files emitted next to your application:

``` text
MyApp.deps.json          // the full dependency graph, with exact versions
                         // and which file satisfies each reference
MyApp.runtimeconfig.json // target framework, GC mode, feature switches
```

``` json
{
  "configProperties": {
    "System.GC.Server": true,               // Server GC (§34)
    "System.GC.Concurrent": true,
    "System.Globalization.Invariant": false
  }
}
```

Assemblies load into an **`AssemblyLoadContext`**. The default one holds your application; additional ones let you load a second copy of the same assembly — the mechanism behind plugin systems and, when marked collectible, unloadable code.

``` csharp
var alc = new AssemblyLoadContext("plugin", isCollectible: true);
var asm = alc.LoadFromAssemblyPath(path);
// ... use it ...
alc.Unload();     // eligible for collection once NOTHING references anything
                  // inside it - one leaked delegate or type reference and the
                  // whole context stays alive forever (§24)
```

> **\[Trap\]**
>
> **Diamond dependency conflicts are resolved by unification, not by magic.** If two packages want different versions of the same library, the build picks one and binds both to it. That is usually fine and occasionally produces a `MissingMethodException` at runtime — a method that existed at compile time against one version and does not exist in the one that loaded. The symptom appears far from the cause; check `deps.json` and the actual loaded version first ([Assemblies, IL, and metadata](#be-m38)).

> **\[Tip\]**
>
> **Two types with the same name can coexist** if they came from different load contexts, and they are *not* assignable to each other. The resulting "cannot cast `Order` to `Order`" is one of the most confusing errors in .NET, and it always means two contexts. Plugin hosts avoid it by having the host own the shared contract assembly and letting plugins reference it rather than carry their own copy.

<a id="be-m40"></a>

#### The JIT: tiers, OSR, and dynamic PGO

Compiling every method with full optimisation would make startup slow; compiling nothing well would make steady state slow. Tiered compilation does both.

| Stage         | What happens                                                                             |
|---------------|------------------------------------------------------------------------------------------|
| **Tier 0**    | first call — compiled fast, barely optimised, with counting instrumentation              |
| Call counting | after ~30 calls the method is queued for re-compilation                                  |
| **Tier 1**    | fully optimised, using the profile gathered at tier 0; swapped in on a background thread |
| **OSR**       | on-stack replacement: a long-running loop stuck in tier 0 is upgraded *mid-execution*    |

**Dynamic PGO** (on by default since .NET 8) is what makes tier 1 genuinely better rather than merely more optimised. Tier 0 records which concrete types actually flowed through interface calls and which branches were taken; tier 1 uses that to emit guarded devirtualization — a direct, inlinable call for the type that shows up 95% of the time, with a fallback for the rest.

``` csharp
// Written as an interface call, so nominally not inlinable:
foreach (var h in handlers) h.Handle(order);

// Tier 1 with PGO, when OrderHandler dominates:
//   cmp   [rcx], METHODTABLE_OrderHandler
//   jne   FALLBACK
//   ...inlined body of OrderHandler.Handle...     <- the hot path is direct
// FALLBACK:
//   call  qword ptr [rax+0x40]
```

> **\[Trap\]**
>
> **This is why naive benchmarking lies.** A `Stopwatch` loop measures tier 0 code, then the transition, then tier 1 — blended into one number that describes nothing. BenchmarkDotNet warms up until the code has settled, which is most of why it exists. It is also why the *first* few thousand requests to a freshly deployed service are slower, and why comparing a cold instance to a warm one is meaningless ([Measure first: tools and method](12-04-scaling-and-performance.md#be-m275)).

> **\[Tip\]**
>
> Set `<TieredPGO>true</TieredPGO>` explicitly if you are on an older TFM, and consider `TieredCompilationQuickJitForLoops` defaults if you have a long-running compute loop entered exactly once — though OSR has made that far less of a problem than it used to be.

<a id="be-m41"></a>

#### Inlining and what stops it

Inlining replaces a call with the callee's body. It removes call overhead, but the real benefit is that it lets every other optimisation see across the boundary — constant folding, dead-code elimination, bounds-check removal, and struct copies all improve once the code is in one place.

``` csharp
// A property getter is a method call in IL:
public decimal Total => _total;
var sum = a.Total + b.Total;
// Inlined, this becomes two field loads. Not inlined, it is two calls.
// This is why "properties are free" is true - conditionally.
```

**What prevents inlining**, roughly in order of how often it surprises people:

- The method is too large. The JIT works to an IL-size budget; a method with a few dozen bytes of IL is a candidate, a hundred-line method is not.
- The call is virtual or through an interface and the JIT cannot prove the target ([Following a reference: dereference and dispatch](01-05-memory.md#be-m29)).
- The method has an explicit `[MethodImpl(MethodImplOptions.NoInlining)]`.
- It contains constructs the inliner declines — certain exception-handling shapes, `localloc`, explicit tail calls.
- It is a delegate invocation whose target is not known ([Closures and delegate allocation](02-05-control-flow-and-functions.md#be-m280)).

``` csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
// A HINT, not a command. Worth applying to genuinely tiny helpers on a hot
// path; worthless (and harmful to code size) applied broadly.

[MethodImpl(MethodImplOptions.NoInlining)]
private static void Log(string m) => ...;
// Occasionally useful: keeping a cold path OUT of a hot method keeps the
// hot method small enough to be inlined itself.
```

> **\[Why it matters\]**
>
> **Design consequence.** Small methods are not just readable — they are the shape the optimiser rewards. A "for performance" refactor that inlines everything into one 200-line method usually makes things slower, because it defeats the JIT's own inlining and hurts instruction cache locality. Write small methods and let the compiler flatten them ([The memory hierarchy: registers to RAM](01-04-the-processor.md#be-m22)).

<a id="be-m42"></a>

#### AOT, ReadyToRun, and trimming

| Mode          | Startup                | Steady state             | Constraints                                               |
|---------------|------------------------|--------------------------|-----------------------------------------------------------|
| JIT (default) | slowest                | best — PGO applies       | none                                                      |
| ReadyToRun    | much faster            | best — re-JITs to tier 1 | larger binary; per-architecture                           |
| Native AOT    | fastest — milliseconds | good, but no PGO         | no reflection-emit, no dynamic loading, trimming required |

``` xml
<PublishReadyToRun>true</PublishReadyToRun>    <!-- precompiled native code
                                                     alongside the IL -->
<PublishAot>true</PublishAot>                  <!-- no JIT at all -->
<PublishTrimmed>true</PublishTrimmed>          <!-- remove unreferenced code -->
```

**Trimming is a reachability analysis**, and it has exactly the same blind spot the GC does not: it follows static references, so anything reached only by *reflection* is invisible to it and gets removed.

``` csharp
// The trimmer cannot see this, so OrderDto's properties may be gone at runtime:
var dto = JsonSerializer.Deserialize(json, typeof(OrderDto));

// Which is why source generators exist - they make the reference static:
JsonSerializer.Deserialize(json, AppJsonContext.Default.OrderDto);
// (§208, §64)
```

> **\[Trap\]**
>
> **Trim and AOT warnings are not advisory.** `IL2026`/`IL3050` mean "this will break at runtime and the build cannot tell you where". Enable `<IsAotCompatible>true</IsAotCompatible>` on libraries so the analyzers run, and treat suppressions as a decision requiring a comment. A trimmed application that fails only on one code path in production is far worse than one that failed to build.

> **\[Why it matters\]**
>
> **Who should care.** Native AOT is transformative for CLI tools, serverless functions, and containers that scale from zero — startup goes from hundreds of milliseconds to single digits. It is usually the wrong choice for a long-running service, where the JIT's profile-guided steady state beats AOT and startup is amortised over days. ReadyToRun is the middle option that costs almost nothing to try.

<a id="be-m43"></a>

#### Type loading, statics, and initialization order

Types, like methods, are prepared lazily. The first use of a type triggers the runtime to build its **method table** — the layout, the vtable, the interface map — and to run its static initialiser.

``` csharp
class Config {
    // A static FIELD INITIALIZER: the type is marked 'beforefieldinit', so the
    // runtime may run it any time before the first static field access.
    public static readonly Config Instance = Load();
}

class Config2 {
    public static readonly Config2 Instance;
    // An explicit static CONSTRUCTOR removes beforefieldinit: initialisation
    // is now guaranteed to run exactly at first access, no earlier.
    // Slightly more predictable; slightly slower, because the JIT must
    // emit an initialisation check before static access.
    static Config2() { Instance = Load(); }
}
```

The runtime guarantees a static initialiser runs **once**, and it takes a lock to guarantee it — which makes `static readonly` a legitimate, allocation-free singleton with no double-checked locking ([Choosing a synchronization primitive](05-02-threads-and-locks.md#be-m133)).

> **\[Trap\]**
>
> **Static initialisers deadlock.** If `A`'s initialiser touches `B` and `B`'s touches `A`, and two threads enter from opposite sides, both block forever holding the other's type-initialisation lock. There is no timeout and no diagnostic beyond a hung process. Worse, an exception inside a static initialiser is wrapped in `TypeInitializationException` and the type is **permanently poisoned** — every subsequent use throws the same cached exception for the life of the process. Keep static initialisers trivial: no I/O, no configuration reads, no cross-type dependencies.

``` csharp
// Do NOT do work in a static initialiser:
static readonly HttpClient Client = new() { BaseAddress = Config.Load().Url };
// If Config.Load throws once - a missing file at startup - this type is dead
// for the entire process lifetime, and the error surfaces at a random call site.

// Prefer explicit, ordered initialisation you control, or Lazy<T>:
private static readonly Lazy<HttpClient> Client = new(() => Build());
```

### Language & Runtime

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Language & Runtime</p>

<p class="pd">The layer everything else sits on. If you are fuzzy about how the type system, async machinery or the garbage collector actually behave, every bug above this layer looks like magic.</p>

<a id="be-a28"></a>

#### Reflection, attributes and source generators

Reflection inspects and invokes types at runtime. It powers DI containers, serializers, ORMs and test runners — and it is slow, breaks trimming, and defeats AOT compilation. Source generators are the modern answer: run at compile time, emit real C#, cost nothing at runtime.

``` csharp
// runtime reflection - flexible, slow, invisible to the compiler
var handlers = asm.GetTypes()
   .Where(t => typeof(IHandler).IsAssignableFrom(t) && !t.IsAbstract);

// compile-time generation - fast, trimmable, debuggable
[JsonSerializable(typeof(OrderDto))]
internal partial class AppJsonContext : JsonSerializerContext { }

JsonSerializer.Serialize(order, AppJsonContext.Default.OrderDto);
```

Generators you will meet without writing one: `System.Text.Json` source generation, `[LoggerMessage]` for zero-allocation logging, regex source generation, and the generated clients in gRPC and Refit.

###### What reflection costs, and how source generators remove it

| Operation                            | Relative cost  | Mitigation                                       |
|--------------------------------------|----------------|--------------------------------------------------|
| Direct method call                   | 1×             | —                                                |
| Cached delegate from expression tree | ~1–2×          | Compile once, cache in a static                  |
| `MethodInfo.Invoke`                  | ~100–1000×     | Cache the `MethodInfo`; better, build a delegate |
| `Activator.CreateInstance`           | ~10–100×       | Cached compiled factory                          |
| `Type.GetProperties()`               | Very expensive | Cache the array — it is recomputed every call    |
| `GetCustomAttributes`                | Very expensive | Cache per type in a `ConcurrentDictionary`       |

``` csharp
// the standard trick: pay reflection ONCE, then call a delegate forever
static readonly ConcurrentDictionary<Type, Func<object>> _factories = new();

static object Create(Type t) => _factories.GetOrAdd(t, static type =>
    Expression.Lambda<Func<object>>(Expression.New(type)).Compile())();
// first call: build and compile an expression tree (~microseconds)
// every later call: a direct delegate invoke (~nanoseconds)

// this pattern is why mature libraries are fast despite being reflection-based.
// the naive version - Activator.CreateInstance(t) in a loop - is the slow one.
```

``` text
  SOURCE GENERATORS - reflection moved to compile time

  compilation pipeline

    your source  ->  [ generators run ]  ->  merged syntax tree  ->  IL
                          |
                     they READ your code and ADD new code.
                     they cannot MODIFY existing code.

  what this buys
    startup       no runtime scanning, no tree compiling
    throughput    a direct call instead of a reflective one
    AOT / trimming the trimmer can SEE the generated calls, so nothing
                  is removed by mistake
    debuggability the generated source is real code you can step into

  where you already meet them
    System.Text.Json      [JsonSerializable] contexts
    LoggerMessage         [LoggerMessage] source-generated logging
    Regex                 [GeneratedRegex]
    ASP.NET Core          minimal API request delegates
    Mapperly, Riok        compile-time object mapping
```

``` csharp
// before: reflection at runtime, allocates, cannot be trimmed
var json = JsonSerializer.Serialize(order);

// after: a generated contract, no reflection at all
[JsonSerializable(typeof(Order))]
internal partial class AppJsonContext : JsonSerializerContext { }

var json = JsonSerializer.Serialize(order, AppJsonContext.Default.Order);
// faster startup, less allocation, and Native AOT compatible.

// same idea for logging - no boxing, no string formatting unless it is enabled
[LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} failed: {Reason}")]
static partial void OrderFailed(ILogger logger, long orderId, string reason);
```

> **\[Note\]**
>
> **The direction of travel.** Every part of .NET that historically used reflection is being replaced by a source generator, driven by Native AOT and trimming. If you are writing a library that scans types at startup, that design is now a liability — it blocks AOT for every consumer. Generating the code at compile time is more work to build and strictly better for everyone downstream.

### Generics and modern data features

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Generics and modern data features</p>

<a id="be-m64"></a>

#### Source generation vs reflection

Reflection reads type metadata at runtime to discover shape. It is flexible and slow: a property read via `PropertyInfo.GetValue` is roughly 100x a direct field access, and every call allocates a boxed result for value types.

``` csharp
// Slow (per call): metadata lookup + boxing
var v = typeof(Order).GetProperty("Total")!.GetValue(order);

// Fast: compile the accessor once, cache the delegate
static readonly Func<Order, decimal> GetTotal =
    (Func<Order, decimal>)Delegate.CreateDelegate(
        typeof(Func<Order, decimal>), typeof(Order).GetProperty("Total")!.GetMethod!);
```

**Source generators** remove the runtime cost entirely by emitting the code at compile time. The ones worth adopting:

| Generator                                | Replaces                                          |
|------------------------------------------|---------------------------------------------------|
| `[JsonSerializable]` context             | reflection-based `System.Text.Json`               |
| `[GeneratedRegex]`                       | `new Regex(...)` interpretation / runtime IL emit |
| `[LoggerMessage]`                        | boxing and formatting in `ILogger` calls          |
| EF Core compiled models                  | model building at first query                     |
| `[GeneratedComInterface]`, LibraryImport | `DllImport` marshalling stubs                     |

``` csharp
[GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)]
private static partial Regex DatePattern();
// Emits a purpose-built matcher at compile time - no interpretation, no startup cost.
```

> **\[Tip\]**
>
> **The reflection you cannot avoid** — dependency injection, ORMs, serializers reading unknown types — should happen *once*, at startup, with the results cached in static fields. The failure mode is reflection inside a per-request or per-row loop; that is almost always the largest single item in a profile when it appears.

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

### Crossing boundaries

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Crossing boundaries</p>

<a id="be-m95"></a>

#### Marshalling to native code

Calling native code means moving data across a boundary where the GC does not apply and the layout rules differ. A type is **blittable** when its managed and unmanaged representations are byte-identical — those cross for free, by pointer. Everything else must be converted, allocated, copied, and freed.

| Blittable (free)                                     | Not blittable (copied)                            |
|------------------------------------------------------|---------------------------------------------------|
| `byte`, `sbyte`, `short`, `int`, `long` and unsigned | `bool` — 1 byte managed, 4-byte `BOOL` by default |
| `float`, `double`, `nint`, `IntPtr`                  | `char`, `string` — encoding conversion            |
| structs whose fields are all blittable               | `decimal`, `DateTime`, arrays of non-blittable    |
| arrays of blittable types (pinned)                   | classes, delegates                                |

``` csharp
// LibraryImport (.NET 7+) generates the marshalling code at COMPILE time:
// visible, debuggable, trimming- and AOT-safe. Prefer it over DllImport.
[LibraryImport("zlib", EntryPoint = "compress2",
               StringMarshalling = StringMarshalling.Utf8)]
private static partial int Compress(
    Span<byte> dest, ref nuint destLen,
    ReadOnlySpan<byte> source, nuint sourceLen, int level);

// Spans of blittable types are pinned and passed as pointers - zero copy.
```

``` csharp
[StructLayout(LayoutKind.Sequential)]     // REQUIRED for interop - the CLR is
struct NativeHeader {                      // otherwise free to reorder fields
    public int  Length;                    // (§31)
    public uint Flags;
    [MarshalAs(UnmanagedType.U1)] public bool IsFinal;   // force a 1-byte bool
}
```

> **\[Trap\]**
>
> **The GC moves objects; native code does not know that.** Passing a managed array by pointer works only because the marshaller pins it for the duration of the call. Store that pointer natively and use it after the call returns and it may point at reclaimed or relocated memory — a corruption bug with no exception and no reproducibility. If native code retains a buffer, allocate it on the pinned object heap, use `GCHandle.Alloc(..., Pinned)` with an explicit lifetime, or allocate unmanaged memory outright ([What a reference actually is](01-05-memory.md#be-m28)).

<a id="be-m96"></a>

#### Handles and lifetimes across the boundary

Native resources — file descriptors, sockets, window handles, library contexts — are integers the operating system understands. Holding one as a raw `IntPtr` is where interop bugs concentrate.

``` csharp
// The problem with IntPtr:
IntPtr handle = OpenThing();
DoWork(handle);                    // if this throws, the handle leaks
CloseThing(handle);
// Worse: after CloseThing, the OS may REUSE that integer for a different
// resource. A stale copy of the value now silently addresses someone
// else's file. This is a real, exploitable class of bug.
```

``` csharp
public sealed class ThingHandle : SafeHandleZeroOrMinusOneIsInvalid {
    private ThingHandle() : base(ownsHandle: true) { }
    protected override bool ReleaseHandle() => CloseThing(handle);

    [LibraryImport("thing")] private static partial bool CloseThing(IntPtr h);
}

[LibraryImport("thing")]
private static partial ThingHandle OpenThing();     // marshals directly to the handle

using var h = OpenThing();          // released deterministically, even on exception
```

`SafeHandle` gives you three things a raw pointer cannot: a **critical finalizer** that runs even if `Dispose` was forgotten, **reference counting** so the handle cannot be closed while a P/Invoke is mid-call, and **invalidation** so a closed handle can never be reused by accident.

``` csharp
// Keeping a managed object alive across a native call that stores a callback:
var callback = new NativeCallback(OnEvent);         // a delegate is an object
RegisterCallback(callback);
// ... the delegate must outlive the native registration, or the GC collects
// it and the native side calls into freed memory.
GC.KeepAlive(callback);             // or store it in a field for the duration
```

> **\[Why it matters\]**
>
> **The through-line from [The heap, and why it needs a collector](02-07-memory-management-and-garbage-collection.md#be-m24).** The GC reclaims memory by reachability, and it knows nothing about handles, sockets, or connections — those are scarce and must be released promptly by something that does. That is exactly what `IDisposable` and `SafeHandle` are for, and it is why "the GC will clean it up" is true for memory and false for everything else ([Connection pooling and exhaustion](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-d462)).

First principles: [Files, handles, and the page cache](01-06-storage-and-files.md#be-m9)
