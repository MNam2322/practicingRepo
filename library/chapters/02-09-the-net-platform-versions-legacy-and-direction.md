---
id: "2-9"
number: "2.9"
title: "The .NET platform: versions, legacy and direction"
part: 2
part_title: "From source code to running program"
words: 8368
minutes: 36
---

# 2.9 The .NET platform: versions, legacy and direction

> **Quick look.** Which .NET to target, how legacy .NET Framework differs, and where the platform is heading.
>
> - Even-numbered releases are LTS (three years); upgrade one version at a time and run the full integration suite.
> - .NET Framework 4.8.1 is final: Windows-only, machine-wide, security fixes only.
> - Legacy apps bring System.Web, AppDomains, the GAC, binding redirects, web.config and IIS app-pool recycling.
> - debug="true" in a production web.config is one of the highest-impact single-line fixes.
> - Migrate incrementally: inventory with the Upgrade Assistant, convert project files, then move piece by piece.
> - The direction is "do less at runtime": source generators, AOT, vectorized libraries.
> - Several popular libraries went commercial in 2024–2025; know the alternatives.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Upgrading .NET 6 to .NET 10 without drama

A service is on an out-of-support runtime and the security team wants it moved.

*One hop at a time*

```bash
# 1. global.json pins the SDK; bump TargetFramework one LTS at a time: net6.0 -> net8.0 -> net10.0
# 2. Upgrade packages first, read the breaking-changes page for each hop
dotnet list package --outdated
dotnet list package --vulnerable --include-transitive
# 3. Run the integration suite, not just unit tests: EF query translation and
#    System.Text.Json behaviour are where most breakage hides
dotnet test --filter Category=Integration
```

> **Lesson:** Upgrade LTS to LTS in small hops with the integration suite as the gate, as routine work rather than a project.

#### Example 2: A legacy .NET Framework site that never timed out

An old ASP.NET site has requests stuck for 20 minutes and slow page compilation after every deploy.

*web.config*

```markup
<system.web>
  <!-- debug="true" disables request timeouts and batch compilation -->
  <compilation debug="false" targetFramework="4.8" />
  <httpRuntime targetFramework="4.8" executionTimeout="110" />
</system.web>
```

> **Lesson:** On legacy Framework apps, check debug="false" first: it is one of the highest-impact single-line fixes.

### Platform Versions: What to Target and Why

<p class="src">From <em>.NET Backend Deep Dive</em> · Platform Versions: What to Target and Why</p>

<a id="dn-232"></a>

#### The support calendar

Everything earlier in this document is version-independent in principle, but the concrete APIs assume a reasonably current runtime. This is the landscape as of late 2026.

| Version | Released | Track | Supported until |
|---|---|---|---|
| .NET 8 | Nov 2023 | LTS | **Nov 10, 2026** |
| .NET 9 | Nov 2024 | STS | **Nov 10, 2026** |
| **.NET 10** | Nov 11, 2025 | **LTS** | **Nov 10, 2028** |
| .NET 11 | Nov 2026 (projected) | STS | ~Nov 2028 |

Even-numbered releases are LTS (three years); odd-numbered are STS (now 24 months, extended from 18).

**The actionable fact right now: .NET 8 and .NET 9 both reach end of support in November 2026.** If you are on either, .NET 10 is the migration target, and the window is short. "End of support" means no security patches — for most organizations that converts an upgrade from a technical preference into a compliance obligation.

For a service you keep deploying, taking the latest release (LTS or STS) is usually right. For something shipped to customers and rarely touched, LTS.

<a id="dn-233"></a>

#### What .NET 10 changes that matters for this document

- **JIT improvements**: struct arguments passed in registers rather than through memory, and graph-based loop inversion — both reduce the cost of the `readonly record struct` patterns in Part 34.2 and Part 20.1.
- **`WebSocketStream`**: the WebSocket protocol behind the familiar `Stream` abstraction, which simplifies the real-time code in Part 36.
- **Async `ZipArchive` APIs** — one fewer sync-over-async trap (Part 8.2).
- **Post-quantum cryptography**: ML-KEM, ML-DSA and SLH-DSA APIs (relevant to Part 52 if you have long-lived encrypted data — "harvest now, decrypt later" is the threat model).
- **EF Core 10**: vector search support (`VECTOR_DISTANCE()`) for semantic search and RAG workloads against SQL Server 2025 and Azure SQL, plus native use of the SQL Server 2025 JSON type.
- **`dnx`** for running .NET tools directly, and the SDK can now **run a single C# file without a project** — genuinely useful for the diagnostic scripts in Part 13.
- **Breaking change to note**: `WithOpenApi` is removed; use the current OpenAPI generator.

<a id="dn-234"></a>

#### C# 14 features you'll actually use

**`field` keyword** — a property accessor body without a hand-written backing field. This closes a fifteen-year-old annoyance:

```csharp
// Before
private string _message = "";
public string Message
{
    get => _message;
    set => _message = value ?? throw new ArgumentNullException(nameof(value));
}

// C# 14
public string Message
{
    get;
    set => field = value ?? throw new ArgumentNullException(nameof(value));
}
```

**Extension members** — extension *properties*, static members and operators, not just methods:

```csharp
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public bool IsEmpty => !source.Any();            // extension PROPERTY
        public T? SecondOrDefault() => source.Skip(1).FirstOrDefault();
    }

    extension<T>(IEnumerable<T>)                          // static extension members
    {
        public static IEnumerable<T> Empty => [];
    }
}

if (orders.IsEmpty) return NotFound();
```

**Null-conditional assignment** — `?.` and `?[]` now work on the left-hand side:

```csharp
// Before
if (customer is not null) customer.Order = GetCurrentOrder();

// C# 14
customer?.Order = GetCurrentOrder();
cart?.Total += lineTotal;              // compound assignment too
```

The right-hand side is not evaluated when the target is null — worth knowing if it has side effects.

**Implicit span conversions** mean `ProcessReadings(array)` now binds to a `ReadOnlySpan<int>` parameter without `.AsSpan()`, which removes friction from the allocation-avoidance patterns in Part 20.1.

Also: `partial` constructors and events, and user-defined compound assignment operators (`+=` implemented directly, avoiding an intermediate copy for large value types — relevant to the `Money` type in Part 34.2).

<a id="dn-235"></a>

#### How to upgrade without drama

```bash
dotnet tool install -g upgrade-assistant
upgrade-assistant upgrade ./MyApi.csproj

# Then, in order of what actually breaks things:
dotnet build -warnaserror        # analyzer changes surface here first
dotnet test                      # integration tests catch EF translation changes
```

Sequence that works: bump `TargetFramework` one version at a time (not 6 → 10 in one jump), read the **breaking changes** page for each hop, upgrade packages before the framework where possible, and run the full integration suite — not just unit tests — because most breakage is in EF query translation and serialization behavior rather than compilation.

Do it **before** the support deadline, not during the month of it. An upgrade under time pressure is how teams end up skipping the test pass.

---

### .NET Framework Essentials

<p class="src">From <em>Backend Engineering</em> · C# & .NET · .NET Framework Essentials</p>

<p class="pd">Enough of the old platform to work on it confidently and to understand what changes when you move off it. Deliberately compact — this is orientation, not a reference for building new Framework applications.</p>

<a id="be-a50"></a>

#### What .NET Framework is, and how it differs

**Windows-only, machine-wide, and frozen.** 4.8.1 is the final version; it receives security fixes and nothing else. It is installed once per machine and shared by every application, which is the root of most of its differences from modern .NET.

|                | .NET Framework 4.8                                  | .NET 9                                       |
|----------------|-----------------------------------------------------|----------------------------------------------|
| Deployment     | Machine-wide install, in-place upgrades             | Side by side, or self-contained with the app |
| Platform       | Windows only                                        | Windows, Linux, macOS                        |
| Web host       | IIS + `System.Web`                                  | Kestrel, optionally behind IIS/nginx         |
| Isolation unit | AppDomain                                           | Process, or `AssemblyLoadContext`            |
| Config         | `web.config` / `app.config` XML                     | Layered providers: JSON, env vars, vault     |
| Packages       | `packages.config` + `lib/` folders                  | `PackageReference`, transitive by default    |
| Project file   | Verbose, every file listed                          | SDK-style, globbed                           |
| DI             | Third-party (Ninject, Autofac, Unity)               | Built in                                     |
| Async          | Has a `SynchronizationContext` — deadlocks are real | None in ASP.NET Core                         |
| C# ceiling     | ~7.3 in practice                                    | 13                                           |
| Performance    | Frozen since 2019                                   | Improves every November                      |

What still transfers completely: C# itself, LINQ, async/await semantics, the type system, generics, and every design pattern in this guide. What does not: hosting, configuration, DI wiring, and anything touching `System.Web`.

> **\[Why it matters\]** Knowing which category a piece of knowledge falls into is what makes a migration estimable. “We use LINQ heavily” is a non-issue; “we use `HttpContext.Current` in the business layer” is weeks of work.

###### The differences that actually block a migration

|                   | .NET Framework 4.8                               | Modern .NET                             |
|-------------------|--------------------------------------------------|-----------------------------------------|
| Installation      | **In-place, machine-wide**                       | Side-by-side, or app-local              |
| Upgrading         | Affects every application on the server          | Per application                         |
| Platform          | Windows only                                     | Windows, Linux, macOS                   |
| Shared assemblies | GAC                                              | None — each app carries its own         |
| Configuration     | `web.config` / `app.config` XML                  | `IConfiguration`, layered providers     |
| Web stack         | System.Web, tightly coupled to IIS               | ASP.NET Core, host-agnostic             |
| Isolation         | AppDomains                                       | `AssemblyLoadContext`, or processes     |
| Async model       | Retrofitted; `SynchronizationContext` everywhere | Async-first; no context in ASP.NET Core |
| Support           | **Supported indefinitely** with Windows          | Fixed LTS/STS windows                   |
| Development       | Security fixes only                              | Active                                  |

``` text
  the honest position on migration

  .NET Framework 4.8 is NOT deprecated and NOT end-of-life. It ships with
  Windows and is supported for as long as the OS is. There is no deadline.

  so migrate for BENEFIT, not for fear:
    performance          typically 2-5x on the same hardware
    Linux + containers   smaller, cheaper, denser hosting
    the ecosystem        new libraries are modern-only
    hiring               fewer people want to work on it
    async throughput     no SynchronizationContext deadlocks

  and NOT migrating is a legitimate decision for a stable internal system
  that is not being actively developed. The wrong move is a rewrite
  justified by a deadline that does not exist.
```

| Blocker                        | Severity    | Path                                                 |
|--------------------------------|-------------|------------------------------------------------------|
| `System.Web` dependency        | **High**    | The main work. `HttpContext` is not portable         |
| WCF services (server)          | **High**    | CoreWCF, or re-expose as gRPC/HTTP                   |
| WCF clients                    | Medium      | `System.ServiceModel` client packages exist          |
| Remoting, AppDomains           | **High**    | No equivalent. Redesign around processes             |
| `System.Drawing`               | Medium      | Unsupported on Linux — ImageSharp, SkiaSharp         |
| COM interop, Office automation | **High**    | Unsupported server-side anyway; replace the approach |
| Web Forms                      | **Highest** | No migration path. A rewrite of the UI layer         |
| MSMQ                           | Medium      | A real broker — usually an improvement               |
| Third-party libraries          | Varies      | **Check first.** This is the most common hard stop   |

<a id="be-a51"></a>

#### The System.Web request pipeline

Classic ASP.NET processes a request through a fixed sequence of application events. Modules subscribe to those events; the handler produces the response. This is the ancestor of ASP.NET Core middleware, and the mapping is close enough to be useful.

``` text
  IIS receives the request
    -> ASP.NET pipeline events, in order (abbreviated from ~19):

       BeginRequest
       AuthenticateRequest          <- who are you
       PostAuthenticateRequest
       AuthorizeRequest             <- are you allowed
       ResolveRequestCache          <- output cache can short-circuit here
       MapRequestHandler            <- pick the IHttpHandler (MVC, Web API, .aspx, ServiceStack)
       AcquireRequestState          <- session loaded HERE (and it serialises requests per session)
       PreRequestHandlerExecute
         [ HANDLER RUNS: controller action / ServiceStack service / page lifecycle ]
       PostRequestHandlerExecute
       ReleaseRequestState
       UpdateRequestCache
       LogRequest
       EndRequest

  modules (IHttpModule) hook any of these. Order among modules for the same event
  is registration order in web.config.
```

| Classic               | Modern equivalent                                        |
|-----------------------|----------------------------------------------------------|
| `IHttpModule`         | Middleware                                               |
| `IHttpHandler`        | Endpoint / `RequestDelegate`                             |
| `Global.asax`         | `Program.cs`                                             |
| `HttpContext.Current` | Injected `IHttpContextAccessor` — and preferably nothing |
| `Server.MapPath`      | `IWebHostEnvironment.ContentRootPath`                    |
| `Session`             | Distributed cache; explicit, and off by default          |
| `Application_Start`   | Host builder plus `IHostedService`                       |
| ASP.NET output cache  | Output caching middleware                                |

> **\[Trap\]**
>
> **Common trap.** `HttpContext.Current` is a static backed by the thread's `CallContext`. It works, until you `await` and resume on a thread that has no context, or you call the code from a background thread — then it is null. Every legacy codebase has a class deep in the business layer that reads it, and finding those is the first real task of any migration. Session is worse: acquiring session state takes an exclusive lock per session, so two concurrent requests from one user execute serially.

###### HttpApplication events, and mapping them to middleware

``` text
  the classic pipeline - ~19 events, fired in order, on every request

    BeginRequest
    AuthenticateRequest        -> UseAuthentication()
    AuthorizeRequest           -> UseAuthorization()
    ResolveRequestCache        -> output caching middleware
    MapRequestHandler          -> UseRouting()
    AcquireRequestState        -> session middleware
    PreRequestHandlerExecute
      >>> HANDLER (.aspx page, MVC controller, .ashx)
    PostRequestHandlerExecute
    ReleaseRequestState
    UpdateRequestCache
    LogRequest
    EndRequest

  MODULES subscribe to these events and run for EVERY request.
  HANDLERS produce the response for a matched path.

  the middleware equivalent collapses all of it into one ordered chain,
  where each component decides whether to call the next. Simpler, and
  the ordering is explicit in code rather than implicit in an event name.
```

``` csharp
// an HttpModule and its middleware equivalent, side by side

// FRAMEWORK
public class TimingModule : IHttpModule
{
    public void Init(HttpApplication app)
    {
        app.BeginRequest += (s, e) => HttpContext.Current.Items["start"] = Stopwatch.GetTimestamp();
        app.EndRequest   += (s, e) => { /* log elapsed */ };
    }
    public void Dispose() { }
}
// registered in web.config <system.webServer><modules>

// MODERN - one method, explicit before/after, no event names
public class TimingMiddleware(RequestDelegate next, ILogger<TimingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var start = Stopwatch.GetTimestamp();
        await next(ctx);                                     // <- everything downstream
        log.LogInformation("{Path} took {Ms}ms", ctx.Request.Path,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
```

``` text
  HttpContext.Current - the single biggest portability blocker

  in System.Web it is a THREAD-STATIC ambient value, available anywhere:

      var user = HttpContext.Current.User;      // from ANY layer, any depth

  which is why Framework codebases reach into it from repositories, from
  domain services, from static helpers. It is invisible coupling, and it
  makes those classes untestable and unusable outside a web request.

  ASP.NET Core deliberately has NO equivalent. The replacement:

      IHttpContextAccessor      injected, explicit, and still discouraged
      -> better: capture what you need at the BOUNDARY and pass it inward
                 as a parameter or a scoped context object.

  migrating this is usually the largest single piece of the work, and it
  is also the change that most improves the codebase independently of
  whether the migration ever completes.
```

<a id="be-a52"></a>

#### AppDomains, the GAC and assembly binding

Three mechanisms that exist to solve problems modern .NET solves differently, and that you will still meet in any Framework codebase.

##### AppDomains

An in-process isolation boundary with its own assemblies, statics and security policy. Their purpose was loading and unloading code without restarting the process, and isolating a crash. Modern .NET dropped them entirely: use a separate process for isolation, or a collectible `AssemblyLoadContext` for unloadable plugins. In practice, the Framework code you will see uses them for plugin hosts and for IIS's own app-domain-per-site model — including the app domain recycle that discards in-memory state.

##### The GAC

A machine-wide store of strong-named assemblies, so multiple applications share one copy. It caused as many problems as it solved: a shared upgrade could break an unrelated application, and ‘works on my machine’ often meant ‘my GAC differs from the server's’. Modern .NET has no equivalent, by design — dependencies travel with the application.

##### Binding redirects

``` text
<!-- web.config: "whatever version anyone asked for, load 12.0.3" -->
<runtime>
  <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
    <dependentAssembly>
      <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" />
      <bindingRedirect oldVersion="0.0.0.0-12.0.0.0" newVersion="12.0.3" />
    </dependentAssembly>
  </assemblyBinding>
</runtime>
```

Because `packages.config` does not resolve transitive dependencies, two packages wanting different versions of a third produce a runtime `FileLoadException`, and the fix is a hand-maintained redirect. A Framework solution of any age has dozens of these, they drift out of sync between projects, and they are a leading cause of ‘it builds but throws on startup’.

> **\[Note\]**
>
> **Diagnosing binding failures.** Enable Fusion logging (`HKLM\Software\Microsoft\Fusion\EnableLog = 1`) to get a per-assembly report of every probing path tried. It is verbose and it answers the question definitively, which no amount of guessing does. Turn it off afterwards — it writes a lot of files.

###### Binding redirects, and diagnosing a load failure

``` text
<!-- the file every Framework developer has fought with -->
<runtime>
  <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
    <dependentAssembly>
      <assemblyIdentity name="Newtonsoft.Json"
                        publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
      <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
    </dependentAssembly>
  </assemblyBinding>
</runtime>

<!-- why they exist: Framework binds to the EXACT version compiled against.
     LibraryA wants 9.0.0, LibraryB wants 13.0.0, only one can load.
     The redirect says "whatever anyone asks for, give them 13".
     
     it is a promise you cannot verify. If 13 removed an API that A used,
     you get MissingMethodException at runtime.
     
     modern .NET removed the whole mechanism: NuGet resolves one version
     at RESTORE time and deps.json records it. Same underlying risk,
     surfaced earlier and visibly. -->
```

| Diagnostic                            | Tool                                                                             |
|---------------------------------------|----------------------------------------------------------------------------------|
| Which assembly was loaded, from where | Fusion log viewer (`fuslogvw.exe`), or `AppDomain.CurrentDomain.GetAssemblies()` |
| Enable fusion logging                 | `HKLM\SOFTWARE\Microsoft\Fusion`: `EnableLog=1`, `ForceLog=1`, `LogPath`         |
| What is in the GAC                    | `gacutil /l`, or browse `C:\Windows\Microsoft.NET\assembly`                      |
| Why a redirect is needed              | Build warning MSB3247, or `AutoGenerateBindingRedirects`                         |
| Modern equivalent                     | `dotnet nuget why`, `dotnet list package --include-transitive`                   |

``` text
  APPDOMAINS - and why they are gone

  what they gave you
    isolation inside one process: separate static state, separate assemblies
    UNLOADING: load a plugin, run it, unload it, reclaim the memory
    a security boundary (which was never actually reliable)

  why they were removed from modern .NET
    the implementation was deeply tied to the Windows CLR
    cross-domain calls required marshalling - slow and full of sharp edges
    the security boundary was not one; processes are the real boundary

  replacements
    isolation       -> a separate PROCESS. Container, sidecar, or child process.
    plugins         -> AssemblyLoadContext (collectible, in theory)
    unloading       -> genuinely hard. Assume it does not happen.
    security        -> OS-level: users, containers, sandboxes

  if a Framework app uses AppDomains for plugin isolation, that is a
  redesign, not a port - and usually toward "run it as a separate process".
```

<a id="be-a53"></a>

#### web.config and the configuration model

``` xml
<configuration>
  <configSections>
    <section name="billing" type="Acme.Config.BillingSection, Acme.Core" />
  </configSections>

  <appSettings>
    <add key="Feature.NewPricing" value="true" />
  </appSettings>

  <connectionStrings>
    <add name="Raven" connectionString="Url=http://raven:8080;Database=legacydb" />
  </connectionStrings>

  <system.web>
    <compilation debug="false" targetFramework="4.8" />   <!-- debug="true" in prod is a bug -->
    <httpRuntime maxRequestLength="30720" executionTimeout="110" />
    <sessionState mode="StateServer" timeout="20" />
  </system.web>

  <system.webServer>
    <modules>
      <add name="CorrelationModule" type="Acme.Web.CorrelationModule, Acme.Web" />
    </modules>
  </system.webServer>
</configuration>
```

| Classic mechanism                        | Modern equivalent                    | Note                                                |
|------------------------------------------|--------------------------------------|-----------------------------------------------------|
| `ConfigurationManager.AppSettings["x"]`  | `IConfiguration` / `IOptions<T>`     | Static access is the thing that makes it untestable |
| `ConfigurationSection` classes           | Bound POCOs with validation          | Far less code                                       |
| `web.config` transforms per build config | Environment-specific JSON + env vars | One binary, many environments                       |
| `machine.config`                         | Nothing — and that is an improvement | Machine-wide config was a deployment hazard         |
| `aspnet_regiis -pe` encrypted sections   | Key Vault, workload identity         | Encrypted config still lives on the box             |

- `debug="true"` in production disables request timeouts, disables batch compilation of views, and prevents JIT optimisation. It is one of the highest-impact single-line fixes in a legacy Framework app.
- `executionTimeout` only applies when `debug="false"`, which is a source of ‘requests never time out in production’ confusion.
- `ServicePointManager.DefaultConnectionLimit` defaults to **2** per host on Framework. Any application making concurrent outbound HTTP calls needs this raised, or requests serialise two at a time. Modern .NET defaults to effectively unlimited.
- `ServicePointManager.SecurityProtocol` may need TLS 1.2 set explicitly on older targets, or handshakes fail against modern servers.

###### Transforms, encryption, and mapping to IConfiguration

``` text
<!-- Web.Release.config - build-time XML transformation -->
<configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
  <connectionStrings>
    <add name="Default" connectionString="Server=prod;..."
         xdt:Transform="SetAttributes" xdt:Locator="Match(name)" />
  </connectionStrings>
  <system.web>
    <compilation xdt:Transform="RemoveAttributes(debug)" />
  </system.web>
</configuration>

<!-- the problem with this model: configuration is baked in at BUILD time,
     so you need one build per environment - which violates
     build-once-deploy-everywhere, and means you did not test what you shipped. -->
```

| Framework                                | Modern equivalent                                      |
|------------------------------------------|--------------------------------------------------------|
| `<appSettings>`                          | `appsettings.json` + environment variables             |
| `<connectionStrings>`                    | `ConnectionStrings` section, or a secret store         |
| `ConfigurationManager.AppSettings["x"]`  | `IConfiguration`, or `IOptions<T>`                     |
| Custom `ConfigurationSection`            | A POCO bound with `Configuration.Bind()`               |
| Web.config transforms                    | Environment variables per environment — **no rebuild** |
| `aspnet_regiis -pe` (section encryption) | Key Vault, Secrets Manager, or a managed identity      |
| `<location path>` inheritance            | Ordered configuration providers                        |

``` csharp
// the migration shim: keep the same call sites working while you move
public static class LegacyConfig
{
    private static IConfiguration _config = null!;
    public static void Initialise(IConfiguration c) => _config = c;
    public static string? AppSetting(string key) => _config[$"AppSettings:{key}"];
}
// then a find-and-replace of ConfigurationManager.AppSettings["X"]
// -> LegacyConfig.AppSetting("X"), and the underlying source is now layered
// configuration. Not beautiful, and it unblocks everything else.

// the destination: strongly typed, validated at STARTUP
builder.Services.AddOptions<BillingOptions>()
    .Bind(builder.Configuration.GetSection("Billing"))
    .ValidateDataAnnotations()
    .ValidateOnStart();          // fails to boot on bad config, rather than at
                                 // the first request that happens to need it
```

> **\[Trap\]**
>
> **Common trap.** `web.config` inheritance is a genuine trap during migration: a `web.config` in a subdirectory inherits from its parent, from the site root, and from machine-level `machine.config`. A setting you cannot find in the application may be coming from any of them. `appcmd list config /section:...` shows the *effective* merged configuration, which is the only reliable way to see what is actually in force.

<a id="be-a54"></a>

#### IIS hosting, app pools and recycling

``` text
  request -> http.sys (kernel) -> IIS worker process w3wp.exe
                                    application pool
                                      app domain per application
                                        ASP.NET pipeline -> your code

  in-process vs out-of-process, once ASP.NET Core is involved:

  IIS + ANCM in-process    w3wp hosts CoreCLR directly. Fastest. One app per pool.
  IIS + ANCM out-of-proc   IIS reverse-proxies to Kestrel on a local port. Slower, more flexible.
```

| Setting               | Default                 | Consider                                                                                                                         |
|-----------------------|-------------------------|----------------------------------------------------------------------------------------------------------------------------------|
| Regular time interval | 1740 min (29 h)         | **0** — disable it; use a fixed schedule instead. The default recycle happens at an unpredictable time and drops in-memory state |
| Idle time-out         | 20 min                  | 0 for an always-on app; otherwise the first request after a quiet period pays full startup                                       |
| Start mode            | OnDemand                | AlwaysRunning, with Application Initialization to warm it                                                                        |
| Max worker processes  | 1                       | Keep at 1 unless the app is genuinely stateless — a web garden breaks in-memory session and caches                               |
| Rapid-fail protection | 5 crashes / 5 min       | Leave on; a crash loop should stop, not hammer the database                                                                      |
| Overlapped recycle    | On                      | The new worker starts before the old one drains — but two processes briefly run, which breaks singleton assumptions              |
| Identity              | ApplicationPoolIdentity | Use it; grant database access to the machine account rather than storing a password                                              |

``` text
<!-- warm the app so the first user after a recycle does not pay the JIT and startup cost -->
<applicationInitialization doAppInitAfterRestart="true">
  <add initializationPage="/health" />
</applicationInitialization>
```

> **\[Note\]**
>
> **The recycle is the state boundary.** Anything held in a static, in `HttpRuntime.Cache`, or in in-process session is gone on recycle — and a recycle can happen because of the 29-hour timer, a `web.config` edit, a memory threshold, or a deployment. Legacy applications that ‘randomly log everyone out’ are almost always describing this. Moving session and cache to Redis or a state server is both a legacy fix and a prerequisite for scaling out.

###### In-process vs out-of-process, and how recycling interacts with .NET

``` text
  ASP.NET Core on IIS - two hosting models, and they behave differently

  IN-PROCESS (the default, and faster)
    IIS  ->  ASP.NET Core Module  ->  CoreCLR loaded INSIDE w3wp.exe
    - your app runs in the IIS worker process
    - IIS Http Server (IISHttpServer), NOT Kestrel
    - ~2-3x higher throughput: no loopback socket hop
    - the app pool identity IS your process identity
    - an app pool recycle kills YOUR process

  OUT-OF-PROCESS
    IIS  ->  ASP.NET Core Module  ->  proxies over HTTP to dotnet.exe (Kestrel)
    - your app is a separate process on a dynamic localhost port
    - IIS is a pure reverse proxy
    - an extra loopback hop per request
    - useful when you need Kestrel-specific behaviour, or process isolation

  <aspNetCore processPath="dotnet" arguments=".\MyApp.dll"
              hostingModel="inprocess" />
```

| Recycling event       | What your app sees                           | What you must handle                                     |
|-----------------------|----------------------------------------------|----------------------------------------------------------|
| Regular time interval | `ApplicationStopping`, then the process ends | Drain gracefully; do not rely on in-memory state         |
| Memory limit reached  | Same                                         | Investigate the leak; recycling is masking it            |
| `web.config` touched  | Immediate recycle                            | Do not edit config to “restart” during traffic           |
| Idle timeout          | Process shuts down entirely                  | Set to 0 for an API; enable Always On                    |
| Rapid-fail protection | **Pool disabled — 503 for everything**       | Alert on it; it does not self-recover                    |
| Overlapped recycle    | Two instances briefly live                   | Singletons, file locks and scheduled jobs may double-run |

``` csharp
// wire .NET's lifetime events to the pool's behaviour, so a recycle drains
public class GracefulShutdown(IHostApplicationLifetime life, ILogger<GracefulShutdown> log)
    : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        life.ApplicationStopping.Register(() =>
        {
            log.LogInformation("Draining - stop accepting, finish in flight");
            // fail readiness first if something is load-balancing you
        });
        life.ApplicationStopped.Register(() => log.LogInformation("Stopped"));
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// and give the shutdown enough time - the default is FIVE SECONDS
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
// IIS's own shutdownTimeLimit (default 90s) must also exceed it, or the ASP.NET
// Core Module kills the process mid-drain regardless of what you configured.
```

> **\[Trap\]**
>
> **Common trap.** Overlapped recycling means two instances of your application are briefly live at once. Anything assuming singleton-per-machine breaks quietly: an in-process scheduler fires twice, a file lock is contended, a cache warm-up runs concurrently. If you run background jobs in the web process — and Framework applications very often do — a recycle is a duplicate-execution event. That is one of the stronger arguments for moving scheduled work out into its own service.

<a id="be-a55"></a>

#### ServiceStack and Ninject, mapped to modern .NET

Two libraries at the centre of a lot of Framework codebases. The mapping matters because it tells you which concepts survive a migration unchanged and which have no equivalent.

##### ServiceStack

| ServiceStack concept                      | ASP.NET Core equivalent                         | Migration note                                                                    |
|-------------------------------------------|-------------------------------------------------|-----------------------------------------------------------------------------------|
| Request DTO + `[Route]`                   | Minimal API or controller with a route template | The message-based design maps well; DTOs often port as-is                         |
| `Service` class with `Any/Get/Post`       | An endpoint handler, or a MediatR-style handler | One service per operation is already close to vertical slices                     |
| Request / response filters                | Middleware or endpoint filters                  | Direct equivalent                                                                 |
| Validation via `AbstractValidator`        | FluentValidation — same library                 | Usually a straight copy                                                           |
| Built-in IoC (Funq)                       | `IServiceCollection`                            | Registrations translate; lifetimes map cleanly                                    |
| `JsonSerializer` (ServiceStack.Text)      | `System.Text.Json`                              | **Watch out:** different null, date and casing defaults — contract-test every DTO |
| Plugins (`AuthFeature`, `OpenApiFeature`) | Authentication schemes, OpenAPI generation      | Reimplement rather than port                                                      |
| `IReturn<T>` typed clients                | Refit / Kiota generated clients                 | The typed-client ergonomics are recoverable                                       |

The genuinely good news: ServiceStack's request-DTO-per-operation design is much closer to modern minimal APIs and CQRS handlers than classic MVC controllers are. A ServiceStack service is already a single-responsibility handler with an explicit input contract, which is exactly the shape you want on the other side.

##### Ninject

``` csharp
// Ninject module                              ->  modern equivalent
Bind<IOrderRepo>().To<OrderRepo>()
    .InRequestScope();                         //  services.AddScoped<IOrderRepo, OrderRepo>()

Bind<IClock>().To<SystemClock>()
    .InSingletonScope();                       //  services.AddSingleton<IClock, SystemClock>()

Bind<IMapper>().To<Mapper>()
    .InTransientScope();                       //  services.AddTransient<IMapper, Mapper>()

Bind<IFeed>().To<BloombergFeed>()
    .Named("bloomberg");                       //  services.AddKeyedScoped<IFeed, BloombergFeed>("bloomberg")

Bind<IRepo>().ToMethod(ctx => Factory.Make())  //  services.AddScoped<IRepo>(sp => Factory.Make())

Bind<IThing>().To<Thing>()
    .WhenInjectedInto<Consumer>();             //  no equivalent - restructure, or use Autofac
```

| Ninject feature                          | MS.DI support                                           |
|------------------------------------------|---------------------------------------------------------|
| Constructor injection                    | Yes                                                     |
| Named bindings                           | Yes — keyed services (.NET 8+)                          |
| Property / setter injection              | **No** — deliberately. Convert to constructor injection |
| Interception / AOP                       | **No** — use decorators, or Autofac/Castle              |
| Conditional binding (`WhenInjectedInto`) | **No** — usually a design smell; refactor               |
| Convention-based scanning                | Via Scrutor                                             |
| Child kernels                            | Scopes                                                  |

###### The semantic differences that break a mechanical port

| Ninject                    | Microsoft.Extensions.DependencyInjection  | Difference that bites                                                                                               |
|----------------------------|-------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| `Bind<IFoo>().To<Foo>()`   | `AddTransient<IFoo, Foo>()`               | —                                                                                                                   |
| `.InSingletonScope()`      | `AddSingleton`                            | —                                                                                                                   |
| `.InRequestScope()`        | `AddScoped`                               | —                                                                                                                   |
| **Implicit self-binding**  | **Not supported**                         | Ninject resolves an unregistered concrete class automatically. MS.DI throws. Expect a wave of missing registrations |
| `.WhenInjectedInto<T>()`   | Keyed services, or a factory              | Contextual binding has no direct equivalent                                                                         |
| Property injection         | **Not supported**                         | Constructor injection only. A real refactor in places                                                               |
| `.ToMethod(ctx => ...)`    | `AddScoped(sp => ...)`                    | Similar, but no `ctx.Request`                                                                                       |
| Interception / proxies     | **Not built in**                          | Scrutor decorators, or Castle DynamicProxy                                                                          |
| Modules (`NinjectModule`)  | Extension methods on `IServiceCollection` | Convention, not a framework feature                                                                                 |
| `kernel.Get<T>()` anywhere | `IServiceProvider`, discouraged           | Service-locator calls must become constructor parameters                                                            |

``` csharp
// the two that cause the most work

// 1. IMPLICIT SELF-BINDING. This works in Ninject with no registration:
public class OrderService(IOrderRepository repo) { }
kernel.Get<OrderService>();          // Ninject: fine
provider.GetRequiredService<OrderService>();  // MS.DI: throws

// so a port produces dozens of "No service for type X has been registered".
// mitigate with an explicit registration pass, or Scrutor's scanning:
services.Scan(s => s.FromAssemblyOf<OrderService>()
    .AddClasses(c => c.InNamespaces("Acme.Services"))
    .AsSelfWithInterfaces()
    .WithScopedLifetime());

// 2. DECORATORS - Ninject interception vs Scrutor
services.AddScoped<IOrderService, OrderService>();
services.Decorate<IOrderService, CachingOrderService>();
services.Decorate<IOrderService, LoggingOrderService>();
// resolution order: Logging -> Caching -> OrderService
```

| ServiceStack                     | ASP.NET Core equivalent                             |
|----------------------------------|-----------------------------------------------------|
| `Service` base class             | Controller, or a minimal API handler                |
| Request DTO + `[Route]`          | Route attribute, or `MapPost` with a parameter type |
| `IReturn<T>`                     | The return type — typed clients come from OpenAPI   |
| Request/response filters         | Middleware, or endpoint filters                     |
| `IRequest.Items`                 | `HttpContext.Items`                                 |
| Built-in auth providers          | ASP.NET Core authentication handlers                |
| `JsConfig`                       | `JsonSerializerOptions`                             |
| AutoQuery                        | OData, or hand-written filtering                    |
| Built-in Redis / caching clients | `StackExchange.Redis`, `IDistributedCache`          |

The design point worth carrying across: ServiceStack's **message-based** model — one request DTO, one response DTO, one handler — maps very cleanly onto minimal APIs with a mediator, and arguably more cleanly than onto controllers. If the existing code is already shaped that way, resist the urge to reorganise it into controllers during the port; keep the shape and change only the framework.

<a id="be-a56"></a>

#### The migration toolchain

1.  **Inventory first.** Run the .NET Upgrade Assistant's analysis and the API Portability Analyzer. You get a per-project report of unsupported APIs, which is the difference between a guess and an estimate.
2.  **Convert project files** with `try-convert` or the Upgrade Assistant, still targeting `net48`. SDK-style projects on Framework work fine, and this step alone removes the binding-redirect problem.
3.  **Migrate `packages.config` to `PackageReference`**, then to central package management. Transitive resolution eliminates most version conflicts.
4.  **Multi-target** the leaf libraries: `<TargetFrameworks>net48;net9.0</TargetFrameworks>`. Now the same source builds for both, and `#if NET48` isolates the differences.
5.  **Push `System.Web` to the edges.** Anything referencing `HttpContext.Current`, `Session`, or `Server.MapPath` must be refactored to take parameters. This is the bulk of the real work and it is worth doing before any hosting change.
6.  **Then move the host.** With the domain and infrastructure multi-targeted, the new ASP.NET Core host is a comparatively small project that references them.

``` text
<!-- multi-targeting, with per-framework differences isolated -->
<PropertyGroup>
  <TargetFrameworks>net48;net9.0</TargetFrameworks>
  <LangVersion>latest</LangVersion>          <!-- modern C# on Framework: mostly works -->
</PropertyGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'net48'">
  <Reference Include="System.Configuration" />
  <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="9.0.0" />
</ItemGroup>
```

``` csharp
#if NET48
    var connection = ConfigurationManager.ConnectionStrings["Raven"].ConnectionString;
#else
    var connection = _configuration.GetConnectionString("Raven")!;
#endif
```

| Blocker                                  | Options                                                                    |
|------------------------------------------|----------------------------------------------------------------------------|
| `System.Web` types in business logic     | Refactor to parameters — unavoidable, and the highest-value work           |
| WCF service host                         | CoreWCF, or reimplement as gRPC / REST                                     |
| WCF client                               | `dotnet-svcutil` generates a modern client                                 |
| `AppDomain` plugin loading               | `AssemblyLoadContext`, or separate processes                               |
| MSMQ                                     | RabbitMQ, Azure Service Bus, or Kafka                                      |
| Web Forms (.aspx)                        | No migration path — rewrite the UI, or keep it behind a proxy indefinitely |
| `System.Drawing`                         | ImageSharp or SkiaSharp                                                    |
| COM interop / Windows-only APIs          | Stays on Windows; modern .NET on Windows still supports most of it         |
| Third-party library with no .NET 9 build | The real blocker — find it early, because the answer may be ‘replace it’   |

> **\[Note\]**
>
> **The incremental path that actually ships.** YARP in front, routing everything to the legacy app; then extract one capability at a time into a modern service that shares the multi-targeted domain libraries. The alternative — a parallel rewrite that must reach feature parity before anything goes live — is the failure mode this whole approach exists to avoid.

###### The tools, in the order you would actually use them

1.  **Inventory first.** `dotnet tool install -g upgrade-assistant`, then `upgrade-assistant analyze`. It reports incompatible APIs and packages per project. Do this before promising anything.
2.  **Check third-party packages** against the target framework. This is the most common hard stop, and it is a five-minute check that saves weeks.
3.  **Convert project files** to SDK style with `try-convert` — while *staying* on `net48`. This alone is worth doing: shorter files, glob includes, `PackageReference`.
4.  **Move to `PackageReference`** from `packages.config`, then add central package management.
5.  **Extract framework-independent code** into `netstandard2.0` libraries — domain logic, validation, DTOs. This is valuable even if the migration stalls.
6.  **Then port project by project**, leaves first, following the dependency graph upward.
7.  **Run both side by side** behind a proxy, moving routes across incrementally.

``` bash
# the analysis pass - run this before writing a plan
upgrade-assistant analyze .\MyLegacy.sln
dotnet tool install -g try-convert && try-convert -p .\MyProject.csproj

# API compatibility, as a build-time check
<PackageReference Include="Microsoft.DotNet.ApiCompat.Task" PrivateAssets="all" />
# or the analyzer that flags platform-specific APIs
<PackageReference Include="Microsoft.DotNet.Analyzers.Compatibility" PrivateAssets="all" />

# and the shim that unblocks a surprising amount:
# Windows-only APIs still available on modern .NET (Windows hosts only)
<PackageReference Include="Microsoft.Windows.Compatibility" Version="10.*" />
# registry, WMI, ACLs, System.Drawing, ODBC, performance counters, EventLog
```

``` text
  THE STRANGLER PROXY - how both run at once

                          YARP (or IIS ARR, or a cloud LB)
                                 |
              +------------------+-------------------+
              |                                      |
        /orders/*  -> NEW ASP.NET Core         everything else
                      (net10, Linux container)  -> LEGACY (net48, IIS)

  route by route, the proxy shifts traffic. Both share the SAME DATABASE
  during the overlap, which is what makes it incremental rather than a
  big-bang cutover.

  YARP is the natural choice because it is .NET, configurable from
  IConfiguration, and can be changed without a redeploy.
```

``` text
// YARP as the strangler proxy - configuration only, no code
{
  "ReverseProxy": {
    "Routes": {
      "orders-new":  { "ClusterId": "new",    "Match": { "Path": "/orders/{**rest}" } },
      "everything":  { "ClusterId": "legacy", "Match": { "Path": "{**catch-all}" },
                       "Order": 100 }
    },
    "Clusters": {
      "new":    { "Destinations": { "d1": { "Address": "http://orders-api:8080/" } } },
      "legacy": { "Destinations": { "d1": { "Address": "http://legacy-iis/" } } }
    }
  }
}
// moving a route is a configuration change. Rolling back is the same change,
// reversed, in seconds - which is what makes the increments genuinely safe.
```

> **\[Note\]**
>
> **Sequence the work so it pays off even if it stops.** Every step above — SDK-style projects, `PackageReference`, extracting domain logic into `netstandard2.0`, removing `HttpContext.Current` from lower layers — improves the existing codebase whether or not the port ever finishes. Migrations get cancelled; that ordering means a cancelled one still leaves you better off.

### Where .NET Is Heading

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Where .NET Is Heading</p>

<p class="pd">What has actually changed in the .NET ecosystem recently, what the community has converged on, and where to point your learning so it is still relevant in three years.</p>

<a id="be-a225"></a>

#### The runtime's direction: performance and ahead-of-time

Every release since .NET 6 has been dominated by performance work, and the theme is consistent: do less at runtime. Dynamic PGO turned interface dispatch into something the JIT can usually devirtualise; vectorisation moved into the BCL so ordinary `string` and LINQ operations use SIMD; and Native AOT removes the JIT entirely for workloads that want it.

| Direction                              | What it means for you                                                                   |
|----------------------------------------|-----------------------------------------------------------------------------------------|
| Native AOT maturing                    | Serverless and CLI tools start in milliseconds; ASP.NET Core minimal APIs are supported |
| Source generators replacing reflection | JSON, logging, regex, config binding, DI — all compile-time now                         |
| Trimming becoming realistic            | Smaller images, but reflection-heavy libraries still block it                           |
| Vectorisation throughout the BCL       | Free speedups on string, span, LINQ and collection operations                           |
| Better container awareness             | GC and thread pool respect cgroup limits far better than they did                       |

The practical consequence is that *upgrading is a performance feature*. Moving a service from .NET 6 to .NET 9 typically produces a double-digit percentage improvement in throughput and memory for no code change, which is a much better return than most optimisation work you could do by hand.

###### What AOT actually costs, concretely

| Native AOT gives                          | Native AOT takes                                                          |
|-------------------------------------------|---------------------------------------------------------------------------|
| Startup in single-digit milliseconds      | **No runtime code generation** — no `Reflection.Emit`, no dynamic proxies |
| Much lower memory — no JIT, smaller heap  | Limited reflection; everything must be statically discoverable            |
| Small self-contained binaries             | Longer build times, and per-platform builds                               |
| No warm-up — peak performance immediately | **Peak throughput can be slightly lower** than a warmed JIT               |
| A smaller attack surface                  | Some libraries simply do not work                                         |

``` text
  WHAT BREAKS, and why

  the trimmer and AOT compiler must SEE every type and method that will
  be used. Anything resolved by NAME at run time is invisible to them.

    reflection over types             Activator.CreateInstance(Type.GetType(s))
    dynamic proxies                   Castle DynamicProxy, some mocking
    runtime serialisation             the non-source-generated path
    assembly scanning DI              "register everything implementing IFoo"
    EF Core model building            improving, still partial
    expression tree compilation       Expression.Compile()

  the replacement for all of them is the same thing: SOURCE GENERATORS.
  the work moves from run time to build time, where the compiler can
  see it - which is exactly the direction the whole platform is moving.
```

``` text
<!-- turn on the analyzers FIRST, before publishing AOT. They tell you at
     BUILD time what will break, rather than at run time in production. -->
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>     <!-- for libraries -->
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>
```

Where AOT is worth it today: **serverless functions, CLI tools, sidecars and short-lived containers**, where cold start dominates and the library surface is small. Where it is not, yet: a typical ASP.NET Core application using EF Core and a broad dependency set. The gap is closing steadily, and the useful move now is to enable the analyzers on new code so you do not accumulate AOT-hostile patterns you will have to unpick later.

<a id="be-a226"></a>

#### The licensing shake-up and what people use now

Between 2024 and 2025 several load-bearing open-source .NET libraries moved to commercial licences. It caused real disruption and it has settled into a clear set of alternatives worth knowing.

| Library          | Status                                             | What teams moved to                                                            |
|------------------|----------------------------------------------------|--------------------------------------------------------------------------------|
| MediatR          | Commercial for larger orgs                         | Wolverine, Brighter, or a ~40-line hand-rolled dispatcher                      |
| AutoMapper       | Commercial for larger orgs                         | Mapperly (source generator), or explicit mapping — often a genuine improvement |
| MassTransit      | v9 commercial                                      | Wolverine, NServiceBus, or staying on the last OSS version                     |
| Moq              | Reputational damage after the SponsorLink incident | NSubstitute, FakeItEasy                                                        |
| FluentAssertions | v8 commercial                                      | Shouldly, AwesomeAssertions, or plain xUnit asserts                            |

The broader lesson is worth internalising: a NuGet dependency is a supply-chain relationship, not a free resource. Before adopting one, ask who maintains it, how it is funded, and how hard it would be to remove. For thin abstractions — a mediator, a mapper, an assertion helper — writing the fifty lines yourself is often cheaper over five years than the migration you eventually do anyway.

> **\[Note\]**
>
> **A useful filter.** Prefer libraries in `Microsoft.Extensions.*` or the .NET Foundation for anything load-bearing, and treat everything else as replaceable. Keep third-party types out of your domain and public signatures, so a swap is confined to one adapter.

###### What changed, and how to reduce your exposure

A cluster of foundational .NET open-source libraries moved to commercial licensing over 2024–2025, which caught a lot of teams by surprise. The specific list matters less than the lesson, and the lesson is about dependency posture rather than about any one package.

| Package              | What changed                                  | Practical options                                                                            |
|----------------------|-----------------------------------------------|----------------------------------------------------------------------------------------------|
| **MediatR**          | Commercial licence for a recent major version | Stay on the last free version, a lightweight in-process dispatcher, or plain handler classes |
| **AutoMapper**       | Same change, same owner                       | **Mapperly** (source-generated), or hand-written mapping                                     |
| **MassTransit**      | Commercial from v9                            | Stay on v8, Wolverine, or the broker SDK directly                                            |
| **FluentAssertions** | Commercial from v8                            | Stay on v7, **Shouldly**, or built-in assertions                                             |
| **IdentityServer**   | Commercial since Duende                       | OpenIddict, Keycloak, or a managed identity provider                                         |
| **Moq**              | A telemetry episode damaged trust             | NSubstitute, FakeItEasy                                                                      |

``` text
  the lesson, which outlives any particular package

  ASK BEFORE ADOPTING
    who funds this, and how?          an unfunded critical dependency is
                                      a licence change waiting to happen
    how deeply would it embed?        MediatR touches every handler.
                                      A logging sink touches one file.
    what is the exit cost?            can I replace it in a day, or is it
                                      a quarter of work?
    is there a boring alternative?    the BCL, or fifty lines of my own

  and the structural defence: keep third-party types OUT of your domain.
  if MediatR's IRequest is implemented by 400 of your classes, you have
  no exit. If it is used by 400 handlers that all sit behind YOUR
  interface, you have one.
```

None of this is an argument against paying for software — a maintained library with a funded team is frequently better value than the alternative. It is an argument for knowing your exposure *before* the email arrives, and for keeping the deeply-embedded dependencies to a small, deliberately chosen set.

<a id="be-a228"></a>

#### Source generators replacing reflection

The consistent architectural move across the ecosystem is from “figure it out at runtime” to “generate the code at compile time”. Faster, AOT-compatible, trimmable, and debuggable, because the generated output is real C# you can step into.

| Was reflection                          | Now generated                                 |
|-----------------------------------------|-----------------------------------------------|
| `JsonSerializer` discovering properties | `JsonSerializerContext`                       |
| `ILogger` formatting the template       | `[LoggerMessage]`                             |
| `new Regex(...)` interpreting a pattern | `[GeneratedRegex]`                            |
| AutoMapper building maps                | Mapperly                                      |
| Config binder using reflection          | Generated binders in .NET 8+                  |
| `[ApiController]` reflection binding    | Minimal API request delegate generator        |
| Runtime mocking proxies                 | Compile-time test doubles in newer frameworks |

``` csharp
[GeneratedRegex(@"^[A-Z]{2}\d{6}$", RegexOptions.Compiled)]
private static partial Regex InvoiceNumber();

[LoggerMessage(EventId = 42, Level = LogLevel.Warning,
               Message = "Retrying {Operation}, attempt {Attempt}")]
static partial void LogRetry(ILogger logger, string operation, int attempt);
```

If you maintain internal libraries, this is where to invest: a small source generator that removes boilerplate across twenty services pays for itself quickly, and it teaches you the Roslyn API, which is broadly useful for analysers and automated refactoring of a legacy codebase.

###### The migration , in the order worth doing it

| Reflection-based                       | Source-generated replacement          | Gain                                       |
|----------------------------------------|---------------------------------------|--------------------------------------------|
| `JsonSerializer` default path          | `[JsonSerializable]` context          | Startup, allocation, AOT-safe              |
| `logger.LogInformation($"...")`        | `[LoggerMessage]`                     | No boxing, no formatting when disabled     |
| `new Regex(pattern)`                   | `[GeneratedRegex]`                    | Compiled at build; no runtime IL emit      |
| AutoMapper                             | Mapperly                              | **Compile-time errors** instead of runtime |
| Assembly-scanning DI                   | Explicit registration, or a generator | Trimmable, faster startup                  |
| `Enum.Parse` / `ToString` on hot paths | A generated switch                    | No reflection, no allocation               |
| Options binding by reflection          | `ConfigurationBinder` generator       | AOT-compatible                             |

``` csharp
// the three worth doing first, because they are nearly free

// 1. logging - mechanical, and it is faster even without AOT
[LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
    Message = "Order {OrderId} rejected: {Reason}")]
static partial void OrderRejected(ILogger logger, long orderId, string reason);

// 2. regex - a Regex created per call is a common, invisible cost
[GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)]
private static partial Regex IsoDate();

// 3. JSON - the largest startup win in most services
[JsonSerializable(typeof(OrderDto))]
[JsonSerializable(typeof(List<OrderDto>))]
internal partial class AppJson : JsonSerializerContext { }
```

> **\[Note\]**
>
> **The direction of travel is clear.** Every part of .NET that historically used reflection is acquiring a source-generated alternative, driven by Native AOT and trimming. If you are writing a *library* that scans types at startup, that design is now a liability for every consumer — it blocks AOT for all of them. Generating the code at compile time is more work to build and strictly better for everyone downstream, which is why the platform keeps choosing it.

<a id="be-a230"></a>

#### Language and platform features worth adopting now

| Feature                                | Version | Replaces                                         |
|----------------------------------------|---------|--------------------------------------------------|
| `TimeProvider`                         | .NET 8  | Hand-rolled `IClock` abstractions                |
| Keyed DI services                      | .NET 8  | Factory switch statements                        |
| `HybridCache`                          | .NET 9  | Hand-written two-level caches with stampede bugs |
| `System.Threading.Lock`                | .NET 9  | `lock (object)` — a real type, better codegen    |
| Collection expressions `[..]`          | C# 12   | `new List<T> { }` and array init noise           |
| Primary constructors                   | C# 12   | Boilerplate constructor plus field assignment    |
| Built-in rate limiting                 | .NET 7  | Third-party middleware                           |
| Output caching                         | .NET 7  | Ad-hoc response caching                          |
| `Microsoft.Extensions.Http.Resilience` | .NET 8  | Hand-wired Polly policies                        |
| Frozen collections                     | .NET 8  | Read-only dictionaries built at startup          |

``` csharp
// several of them at once, in code you would actually write
public sealed class PricingService(
    TimeProvider time,
    HybridCache cache,
    [FromKeyedServices("premium")] IDiscountPolicy discounts)
{
    private static readonly FrozenDictionary<string, decimal> VatRates = LoadRates();

    public ValueTask<decimal> QuoteAsync(Basket b, CancellationToken ct) =>
        cache.GetOrCreateAsync($"quote:{b.Hash}",
            async token => await Compute(b, time.GetUtcNow(), token),
            cancellationToken: ct);
}
```

###### Prioritised by return, not by novelty

| Adopt                                        | Because                                           | Effort                         |
|----------------------------------------------|---------------------------------------------------|--------------------------------|
| **Nullable reference types**                 | Eliminates a whole class of bug at compile time   | High on legacy, low on new     |
| **`record`** for DTOs and value objects      | Correct equality and immutability, free           | Trivial                        |
| **Primary constructors**                     | Removes constructor boilerplate for DI            | Trivial                        |
| **`required` members**                       | Better than `!` for mandatory properties          | Trivial                        |
| **Collection expressions** `[..]`            | Consistent, concise, and often faster             | Trivial                        |
| **Pattern matching + switch expressions**    | Exhaustiveness warnings on closed hierarchies     | Low                            |
| **`TimeProvider`**                           | Makes time testable without a wrapper of your own | Low                            |
| **Source generators** (logging, JSON, regex) | Faster, allocation-free, AOT-ready                | Low                            |
| **Keyed DI services**                        | Strategy selection without a factory class        | Low                            |
| **`System.Threading.Channels`**              | Correct producer/consumer with backpressure       | Low                            |
| **Static abstract members** / generic math   | Generic numeric code without boxing               | Moderate                       |
| **`Span<T>` and `ref struct`**               | Allocation-free parsing and slicing               | Moderate — only where measured |

``` csharp
// primary constructors + collection expressions + keyed services:
// the modern shape of an ordinary service class
public class OrderService(
    AppDbContext db,
    [FromKeyedServices("standard")] IPricingStrategy pricing,
    ILogger<OrderService> log)
{
    private static readonly string[] Terminal = ["Cancelled", "Refunded"];

    public async Task<Result<Guid>> PlaceAsync(CreateOrder cmd, CancellationToken ct)
    {
        if (Terminal.Contains(cmd.Status)) return Error.Conflict();
        ...
    }
}
// compare with the same class in C# 8: a constructor, four field assignments,
// four readonly fields, and a static array initialiser. Same behaviour.
```

A note on prioritisation: the top four rows are worth adopting across an entire codebase, mechanically, because they reduce boilerplate or eliminate bugs with essentially no downside. The bottom two are *performance* features and belong only where a profiler has shown they matter — `Span<T>` applied speculatively adds real complexity for a saving you have not measured.

<a id="be-a231"></a>

#### Staying current without drowning

- **Read the release notes properly, once a year.** The .NET release blog and the annual “Performance Improvements in .NET” post are the two highest-density sources in the ecosystem.
- **Upgrade on the LTS cadence and treat it as routine.** Teams that skip two versions turn a one-day upgrade into a one-quarter project.
- **Follow the runtime issues, not the influencers.** Design discussions in the dotnet GitHub repos tell you where things are going a year before the blog posts do.
- **Build one small thing with each new capability.** An afternoon with Native AOT or a source generator teaches you more than ten articles about it.
- **Distrust anything that only shows a to-do list.** Judge a pattern by how it handles concurrency, failure, migration and observability — every architecture looks clean at demo scale.

##### What will still be true in ten years

Almost everything specific in this guide will change. The framework will get a new hosting model, the container orchestrator will be replaced by something else, and half the library names will be different. What will not change is the underlying material: how a CPU cache and a network round trip differ by six orders of magnitude; that shared mutable state is the source of most concurrency bugs; that two systems cannot commit atomically without a protocol; that indexes are sorted structures; that every abstraction leaks under load; and that the hardest part of software is deciding where the boundaries go.

> **\[Note\]**
>
> **Where to spend your next hour.** Not on another framework. Pick the thing in your own system you cannot currently explain — why that endpoint is slow, what happens when the broker is down, where that connection string comes from — and find out. Depth on a real system compounds; breadth across tutorials does not.

###### A sustainable practice

| Signal                                  | Where                                             | Cadence                  |
|-----------------------------------------|---------------------------------------------------|--------------------------|
| What is actually shipping               | .NET Blog, release notes                          | Monthly                  |
| What broke for other people             | Public incident reports — Cloudflare, GitLab, AWS | One a month              |
| What the language is becoming           | C# language design notes, the roslyn repository   | Quarterly, if interested |
| What your dependencies are doing        | Release notes for your top ten packages           | When you upgrade         |
| What is genuinely new versus repackaged | Time. Wait six months on anything foundational    | Continuous               |

``` text
  a filter that works, applied to any new thing

  1  WHAT PROBLEM does this solve?
       if you cannot state it in one sentence, skip it.

  2  DO I HAVE THAT PROBLEM?
       not "could I imagine having it". Do I have it now.

  3  WHAT DOES IT COST?
       a dependency, a concept the team must learn, an upgrade path,
       an operational surface.

  4  WHAT IS THE EXIT?
       if this is abandoned or relicensed in two years, what happens?

  most new things fail at step 2, and that is the correct outcome.
  the ones that pass are worth real attention.
```

- **Depth beats breadth.** Knowing HTTP, SQL and your runtime properly transfers to every framework. Knowing fifteen frameworks shallowly transfers to none.
- **Learn the layer below the one you work in.** A backend developer who understands the database and the network diagnoses things nobody else on the team can.
- **Follow a small number of people, not a feed.** Signal per minute is far higher.
- **Let the ecosystem settle.** Adopting a foundational library in its first six months means being its beta tester and absorbing its breaking changes.
- **Write down what you learn.** A short note beats a bookmark. The reader will be you, eighteen months from now, having forgotten.
- **Accept that you cannot keep up, and stop trying.** Nobody is current on all of .NET. Be genuinely good at the parts your work touches, and know enough of the rest to recognise when to go and read.

> **\[Note\]**
>
> **The through-line of this whole guide.** Everything in it argues one thing from different directions: *there is a real system underneath the abstraction — bytes, pages, sockets, threads, processes — and it is free to ignore right up until it is not.* When something is slow, broken or insecure, the answer is nearly always one layer below where you were looking. Knowing which layer to drop to, and having the vocabulary to ask about it, is most of what separates someone who can fix a system from someone who can only build one.
