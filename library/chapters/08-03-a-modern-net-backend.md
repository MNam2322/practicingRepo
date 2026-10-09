---
id: "8-3"
number: "8.3"
title: "A modern .NET backend"
part: 8
part_title: "Building a backend"
words: 15148
minutes: 66
---

# 8.3 A modern .NET backend

> **Quick look.** How a modern ASP.NET Core backend is put together, from host to tests.
>
> - Cross-platform, runs Kestrel on the generic host, with DI, configuration, logging and health checks built in.
> - Middleware runs in registration order: exception handling first, authentication before authorization.
> - Minimal APIs and controllers compile to the same routing; the choice is organizational.
> - Configuration layers override each other; bind options with validation and ValidateOnStart. Know IOptions vs IOptionsSnapshot vs IOptionsMonitor.
> - Use IExceptionHandler and return Problem Details with a trace id, never stack traces.
> - Never expose entities over HTTP; map to DTOs.
> - Log with message templates, not interpolation; test the whole app in memory with WebApplicationFactory.
> - .NET Aspire wires local services, discovery, telemetry and a dashboard for multi-service apps.


### The big picture

If your last large .NET web project was ASP.NET MVC or Web API on the .NET Framework, or ServiceStack, modern ASP.NET Core will feel familiar in places and very different in others. It runs on Windows, Linux and macOS, in containers, and is among the fastest web frameworks in any language. Dependency injection, configuration, logging and health checks are built in rather than bolted on. A whole API can be a single readable `Program.cs`, and yet it scales up to large codebases.

This chapter walks through how a modern ASP.NET Core backend is put together. It covers the host that starts it, the pipeline of middleware each request passes through, and minimal APIs and controllers. It also covers how requests are bound to parameters, and results to responses. It then covers errors as Problem Details, structured logging, OpenAPI documents, health checks, testing the whole app in memory and packaging it as a container. The example is a small but complete API that runs, calls itself and prints what happened.

### Before you start

**Builds on:** 4.3 Composition and dependency injection, 1.7 The operating system, 6.6 HTTP, 2.8 Errors and exceptions, 8.2 API design, 8.1 What a backend does.

**Ideas explained later:**

- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Authentication** (10.2): Establishing who a caller is.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **Log level** (11.6): The importance of a log event, such as Debug, Information, Warning or Error.
- **Output caching** (8.6): Storing whole HTTP responses on the server and replaying them for matching requests.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **CORS** (9.7): Cross-Origin Resource Sharing: headers with which a server lets other origins read its responses.
- **Security headers** (10.4): HTTP response headers that switch on browser protections, such as CSP and HSTS.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, null operators, switch expressions and patterns, tuples, exceptions, using for cleanup, async and await, attributes, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why does the order of middleware registration matter?**

> Each middleware wraps everything registered after it. The exception handler must come first to catch errors from everything else, and authentication must come before authorization, which must come before the endpoints it protects.

**2. What is the difference between a minimal API and a controller?**

> A minimal API maps a route directly to a function with `MapGet`, `MapPost` and similar calls. A controller is a class whose action methods handle routes, with attributes for routing and binding. Both run on the same framework; minimal APIs are lighter and are the default in new templates.

**3. Where does a minimal API handler's parameter come from?**

> By default, from the route if its name matches a route segment, from the query string for simple types, from registered services if the type is in the DI container, and from the JSON body for other complex types. Attributes such as `[FromHeader]` and `[FromBody]` override that.

**4. Why write _logger.LogWarning("Flight {Flight} is full", number) instead of an interpolated string?**

> The template keeps `Flight` as a named, separately stored value. Log tools can then search and group by flight. An interpolated string bakes the value into plain text, and is formatted even when that log level is switched off.

**5. What does WebApplicationFactory do?**

> It starts your whole ASP.NET Core app in memory for tests, with its real pipeline, routing and DI, and gives you an `HttpClient` that talks to it without a network. You can replace services, such as the database, for the test.

**6. What does Kestrel do?**

> It is ASP.NET Core's built-in web server: it accepts connections, handles HTTP/1.1, HTTP/2 and HTTP/3, and passes requests into the pipeline. It can face the internet directly or sit behind a reverse proxy.

**7. What is the default port of an ASP.NET Core app in the official .NET 8 and later container images?**

> 8080, so the app can run as a non-root user, which may not listen on ports below 1024.

### What changed

Classic ASP.NET ran only on Windows, inside IIS, built on the `System.Web` library, with separate stacks for MVC, Web API and Web Forms. ASP.NET Core, first released in 2016, is a rewrite. It is open source and cross-platform, and one framework covers APIs, server-rendered pages, real-time connections and gRPC. It runs its own web server, Kestrel, inside your process, so it works the same on a laptop, in a container or behind any load balancer.

Features that used to need third-party libraries are now built in: dependency injection from Composition and dependency injection (4.3), configuration and options, logging, health checks, rate limiting, output caching, OpenAPI documents and a problem-details error format. The rest of this chapter shows how they fit together.

### The host and Program.cs

A modern app starts in `Program.cs`, and the structure is always the same. `WebApplication.CreateBuilder(args)` creates a builder that has already loaded configuration from `appsettings.json`, environment-specific files, environment variables and command-line arguments, and set up logging. You register services on `builder.Services`. `builder.Build()` creates the app. You then configure the request pipeline and endpoints on it, and `app.Run()` starts the server and waits until the process is told to stop.

Underneath is the .NET **generic host**: the part that owns configuration, logging, dependency injection and the lifetime of the app. It starts registered background services, listens for the shutdown signals from The operating system (1.7), and stops everything gracefully. Worker services with no web server at all use the same host.

**Kestrel** is the web server inside the host. It accepts TCP connections, handles TLS and HTTP/1.1, HTTP/2 and HTTP/3, and passes each request into your pipeline. It is fast enough to face the internet, though production deployments usually put a load balancer or reverse proxy in front, as HTTP (6.6) described.

### The request pipeline

Each request passes through a chain of components called **middleware**. Each piece receives the request, can do something, calls the next piece, and can do something again when the response comes back through it. The whole chain is the **request pipeline**.

Order matters, because each middleware wraps everything after it. A typical order starts with exception handling, so it catches errors from everything else. Then come HTTPS redirection and forwarded headers, then routing, which picks the endpoint, then CORS. Next is authentication, which works out who the caller is, then authorization, which checks what they may do. Rate limiting follows, and finally the endpoint itself. Get authentication and authorization in the wrong order and requests are checked before anyone knows who sent them.

You can write your own middleware as a lambda with `app.Use`, or as a class. Common uses are adding correlation ids, timing requests and adding security headers. Middleware can also short-circuit: answer the request itself without calling the rest, as a cache or a rate limiter does.

### Endpoints: minimal APIs

A **minimal API** maps a route and a method directly to a handler function: `app.MapGet("/flights/{number}", handler)`. It is the style new templates use, and it scales well when organized.

A **route template** such as `/flights/{number}` defines segments, with names in braces for values. A **route constraint** restricts a segment's form, such as `{id:int}` or `{code:length(3)}`, so non-matching requests fall through to a 404 instead of failing inside the handler. A **route group**, `app.MapGroup("/api/flights")`, gives many endpoints a shared prefix and shared settings, such as an authorization policy, tags for the OpenAPI document, or filters.

**Parameter binding** decides where each handler parameter comes from. A parameter whose name matches a route segment is read from the route. Simple types such as `int` and `string` otherwise come from the query string. Types registered in the DI container are injected. `HttpContext`, `HttpRequest`, `CancellationToken` and a few others are provided automatically. Any other complex type is read from the JSON body. Attributes such as `[FromQuery]`, `[FromHeader]` and `[FromBody]` make the source explicit, and `[AsParameters]` gathers many parameters into one type.

A handler returns a result. `TypedResults.Ok(value)`, `TypedResults.NotFound()` and `TypedResults.Created(uri, value)` produce the matching status codes. Declaring the return type as `Results<Ok<Flight>, NotFound>` lists every possible outcome in the signature, so the compiler checks the handler and the OpenAPI document shows exactly what the endpoint can return. These are **typed results**.

An **endpoint filter** runs code before and after a handler, like middleware for one endpoint or group: validation, logging or checks shared by several endpoints.

### Endpoints: controllers

Controllers remain fully supported. A controller is a class, usually marked `[ApiController]`, whose public methods, called actions, handle requests. Attributes such as `[HttpGet("{number}")]` define routes, and constructor injection supplies services. `[ApiController]` adds conventions: automatic 400 responses when model validation fails, and binding complex types from the body.

Many large existing codebases use controllers, and they suit teams that like grouping endpoints in classes. Minimal APIs have less ceremony and slightly better performance, and they work with Native AOT. Either is a reasonable choice; the pipeline, DI, configuration and results underneath are the same.

### Seeing it for real

This program is a small but complete API: a repository registered in DI, custom middleware, a route group with typed results, Problem Details for errors, structured logging, health checks and a generated OpenAPI document. It starts, calls itself and prints what happened.

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.AspNetCore.OpenApi@10.0.0
// flightsapi.cs — run with: dotnet run flightsapi.cs
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5059");
builder.Logging.ClearProviders().AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton<IFlightRepository, InMemoryFlightRepository>();   // swap for an EF Core version later
builder.Services.AddProblemDetails();                        // errors as RFC 9457 Problem Details
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();                               // describes the endpoints as an OpenAPI document

var app = builder.Build();

// The pipeline: each middleware wraps everything registered after it
app.UseExceptionHandler();                                   // 1. unhandled exceptions become 500 Problem Details
app.UseStatusCodePages();                                    // 2. empty error responses get a Problem Details body
app.Use(async (context, next) =>                             // 3. our own middleware: correlation id and timing
{
    string id = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N")[..12];
    context.Response.Headers["X-Correlation-Id"] = id;       // echoed back so client and server logs can be matched
    var sw = Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Server-Timing"] = $"app;dur={sw.Elapsed.TotalMilliseconds:F1}";
        return Task.CompletedTask;
    });
    await next(context);                                     // everything after this middleware runs here
});

app.MapOpenApi();                                            // GET /openapi/v1.json
app.MapHealthChecks("/health");

var flights = app.MapGroup("/api/flights").WithTags("Flights");   // a shared prefix and shared metadata
flights.MapGet("/{number}", Results<Ok<Flight>, NotFound> (string number, IFlightRepository repo) =>
    repo.Find(number) is { } flight ? TypedResults.Ok(flight) : TypedResults.NotFound());
flights.MapPost("/{number}/bookings",
    Results<Created<Booking>, NotFound, Conflict<ProblemDetails>> (string number, BookingRequest request, IFlightRepository repo, ILogger<Program> log) =>
{
    if (repo.Find(number) is null) return TypedResults.NotFound();
    if (!repo.TryBook(number, request.Passenger, out var booking))
    {
        log.LogWarning("Flight {Flight} is full; rejected {Passenger}", number, request.Passenger);   // a template, not interpolation
        return TypedResults.Conflict(new ProblemDetails { Title = "Flight is full", Status = StatusCodes.Status409Conflict });
    }
    return TypedResults.Created($"/api/flights/{number}/bookings/{booking.Seat}", booking);
});
app.MapGet("/api/boom", () => { throw new InvalidOperationException("something broke"); });

await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5059") };

using (var r = await http.GetAsync("/api/flights/BA117"))
    Console.WriteLine($"{(int)r.StatusCode}, correlation id {r.Headers.GetValues("X-Correlation-Id").First()}, {r.Headers.GetValues("Server-Timing").First()}");
// 200, correlation id 3f9c0a..., app;dur=0.4

foreach (string who in new[] { "Ada", "Grace" })
{
    using var r = await http.PostAsJsonAsync("/api/flights/BA117/bookings", new { passenger = who });
    Console.WriteLine($"{who}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
}
// Ada: 201 {"seat":1,"passenger":"Ada"}
// ... warn: Program[0] Flight BA117 is full; rejected Grace      <- the structured log line
// Grace: 409 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Flight is full","status":409}

using (var r = await http.GetAsync("/api/flights/XX999"))
    Console.WriteLine($"{(int)r.StatusCode} {r.Content.Headers.ContentType}");     // 404 application/problem+json
using (var r = await http.GetAsync("/api/boom"))
    Console.WriteLine($"{(int)r.StatusCode} {JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title")}");
// fail: ... ExceptionHandlerMiddleware[1] An unhandled exception has occurred ...   <- the full exception, in the server's log
// 500 An error occurred while processing your request.   <- all the client sees: no stack trace leaks out
Console.WriteLine(await http.GetStringAsync("/health"));    // Healthy
var openApi = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
Console.WriteLine(string.Join(", ", openApi.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name)));
// /api/flights/{number}, /api/flights/{number}/bookings, /api/boom
await app.StopAsync();

interface IFlightRepository
{
    Flight? Find(string number);
    bool TryBook(string number, string passenger, out Booking booking);
}

sealed class InMemoryFlightRepository : IFlightRepository
{
    readonly Lock sync = new();
    readonly Dictionary<string, Flight> flights = new() { ["BA117"] = new Flight("BA117", "LHR", "JFK", Seats: 1, Booked: 0) };

    public Flight? Find(string number) { lock (sync) return flights.GetValueOrDefault(number); }

    public bool TryBook(string number, string passenger, out Booking booking)
    {
        lock (sync)
        {
            var flight = flights[number];
            booking = new Booking(flight.Booked + 1, passenger);
            if (flight.Booked >= flight.Seats) return false;
            flights[number] = flight with { Booked = flight.Booked + 1 };
            return true;
        }
    }
}

record Flight(string Number, string Origin, string Destination, int Seats, int Booked);
record Booking(int Seat, string Passenger);
record BookingRequest(string Passenger);
```

### Errors and Problem Details

A backend must never show clients a stack trace, and should report every error in one consistent shape. ASP.NET Core does both with little code. `AddProblemDetails()` registers the Problem Details format from Errors and exceptions (2.8). `UseExceptionHandler()` catches any unhandled exception, logs it, and returns a 500 in that format, with a trace id the client can quote but no internal details. `UseStatusCodePages()` gives empty error responses, such as a bare 404, the same format.

For expected failures, return the right result from the handler, as the example does with 404 and 409. For exceptions you want to map to specific responses, implement `IExceptionHandler`, added in .NET 8, and register it; it can turn, say, a `DbUpdateConcurrencyException` into a 409. The developer exception page, with full details, belongs only in the Development environment. Validation and errors at the boundary (8.5) goes further.

### Logging

`ILogger<T>` is injected wherever you need it. Calls take a message template with named placeholders and separate values: `LogWarning("Flight {Flight} is full; rejected {Passenger}", number, passenger)`. This is **structured logging**: the values travel as named fields, not just as text. Log systems can then filter and group by `Flight`, which a formatted string would make impossible. Templates are also only formatted when the log level is enabled, so switched-off debug logging costs almost nothing. Don't use string interpolation in log calls; analyzers flag it.

Log levels run from Trace and Debug through Information and Warning to Error and Critical. Configuration sets the minimum level per category, so you can turn on debug logging for one namespace without flooding the rest. Where logs go, and how they connect to metrics and traces, is covered in Observability (11.6).

### Other built-in pieces

Several more features come built in, each a line or two to enable.

OpenAPI documents come from `AddOpenApi()` and `MapOpenApi()`, as in the example, describing every endpoint for documentation and client generation, as API design (8.2) explained. Tools such as Scalar or Swagger UI can display the document as an interactive page. Health checks come from `AddHealthChecks()`, with checks for databases and other dependencies available as packages, and separate endpoints for liveness and readiness, as What a backend does (8.1) described. Rate limiting comes from `AddRateLimiter()` with fixed-window, sliding-window, token-bucket and concurrency policies. Output caching and response compression are covered in Caching (8.6). Authentication with JWT bearer tokens or cookies, and authorization policies with `RequireAuthorization()`, are covered in Authentication and authorization (10.2).

Outgoing HTTP calls should go through `IHttpClientFactory`, often as typed clients registered with `AddHttpClient<TClient>()`, which handles connection pooling and DNS refresh, as TCP and UDP (6.3) and DNS (6.4) explained, and lets you add resilience policies. A DbContext is registered with `AddDbContext`, scoped per request, as ORMs and EF Core (7.7) described.

### Testing the whole app

Unit tests cover the business logic. To test endpoints, routing, binding, filters and error handling together, ASP.NET Core provides **WebApplicationFactory**, in the `Microsoft.AspNetCore.Mvc.Testing` package. It starts the real application in memory and gives the test an `HttpClient` that talks to it with no network. Tests can replace services, for example pointing the DbContext at a Testcontainers database from Testing (4.6).

```csharp
// In an xUnit test project that references the API project.
// Add "public partial class Program { }" at the end of the API's Program.cs so the test can see it.
public class FlightsApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Unknown_flight_returns_problem_details()
    {
        HttpClient client = factory.CreateClient();                  // the whole app, in memory
        HttpResponseMessage response = await client.GetAsync("/api/flights/XX999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
```

### Running it in production

A few habits matter once the app leaves your laptop.

Be async all the way. Kestrel disallows synchronous reads and writes of request and response bodies by default, because blocking threads starves the server, as Async and await (5.3) explained. Accept a `CancellationToken` in handlers that do I/O; it is cancelled when the client disconnects.

Build container images directly with `dotnet publish -t:PublishContainer`, which produces an image without a Dockerfile, using the official .NET base images. Those images listen on port 8080 and run as a non-root user by default, as The operating system (1.7) mentioned. Container details are in Containers and Docker (11.3).

Behind a load balancer or proxy, use the forwarded headers middleware so the app sees the real client address and scheme, trusting only known proxies, as HTTP (6.6) warned. Handle shutdown gracefully: the host stops accepting new requests on SIGTERM and lets in-flight ones finish, within a configurable timeout.

For fast start-up and small images, minimal APIs support Native AOT, from Compilers, interpreters and JIT (2.2), with `WebApplication.CreateSlimBuilder()`. And for running an app together with its database, cache and other services locally, Microsoft's Aspire tooling starts and wires them together and shows their logs and traces in one dashboard.

### In short

ASP.NET Core is cross-platform, runs its own Kestrel server on the generic host, and builds in dependency injection, configuration, logging and health checks. Each request flows through middleware in registration order, so put exception handling first and authentication before authorization. Minimal APIs map routes to handlers, with parameters bound from the route, query, services or JSON body, and typed results listing every outcome; controllers remain a valid alternative. Return Problem Details for every error and never stack traces, log with templates, and generate an OpenAPI document. Test the whole app in memory with WebApplicationFactory, stay async, and ship as a container.

### What goes wrong

**Middleware in the wrong order.** Authorization runs before authentication and rejects everyone, or the exception handler sits after the endpoints and never catches anything. Follow the standard order and keep it in one place.

**Stack traces reaching clients.** The developer exception page is left on in production and leaks internals. Use it only in Development, and return Problem Details everywhere else.

**Interpolated log messages.** `LogInformation($"Booked {id}")` loses the structured field and formats even when logging is off. Use message templates.

**Business logic in handlers.** Rules written inline in endpoints get duplicated and are hard to test. Keep handlers thin and call services.

**Blocking calls in handlers.** `.Result` on a task or synchronous database calls starve the thread pool under load. Use async throughout.

**Singletons holding scoped services.** A singleton that takes a DbContext keeps one context for every request. Keep scope validation on, as Scope and lifetime (2.6) described.

**New HttpClient per request.** Outgoing calls exhaust ports under load. Use `IHttpClientFactory`.

**Ignoring the client's cancellation token.** Abandoned requests keep running expensive queries. Pass `CancellationToken` into every async call.

### Cost model

ASP.NET Core's own overhead per request, routing, binding and serialization, is typically in the tens of microseconds, and a single instance can serve tens of thousands of simple requests per second on ordinary hardware. Middleware costs a function call each unless it does I/O. Structured logging costs little when a level is disabled and more when every request writes several enabled log lines, so choose production levels deliberately. A normal app starts in hundreds of milliseconds; a Native AOT minimal API can start in tens, which matters for scaling out quickly and for serverless hosting.

### In other languages (optional)

**JavaScript and TypeScript.** Express, Fastify and NestJS use the same middleware idea, with the order-matters rule, as A Node and TypeScript backend (8.4) shows. NestJS is closest to ASP.NET Core's structure, with dependency injection and controllers.

**Python.** FastAPI has dependency injection, OpenAPI generation from type hints and middleware; Django has middleware and a full framework around it.

**Java.** Spring Boot has filters, controllers, dependency injection, Actuator health endpoints and OpenAPI support through libraries, very much like ASP.NET Core.

**Go.** `net/http` handlers are wrapped by middleware functions, the same pattern at its most minimal.

### Key terms

- **Generic host**: The .NET component that owns configuration, logging, dependency injection and app lifetime.
- **Kestrel**: ASP.NET Core's built-in web server.
- **Middleware**: A component in the request pipeline that handles a request, calls the next component and handles the response.
- **Request pipeline**: The ordered chain of middleware every request passes through.
- **Minimal API**: An endpoint defined by mapping a route and method directly to a handler function.
- **Route template**: A pattern such as /flights/{number} that matches request paths and captures values.
- **Route constraint**: A rule on a route segment, such as {id:int}, that must hold for the route to match.
- **Route group**: A set of endpoints sharing a route prefix and settings.
- **Parameter binding**: Filling a handler's parameters from the route, query, headers, body or services.
- **Typed results**: Result types such as Ok&lt;T&gt; and NotFound that list every outcome in an endpoint's signature.
- **Endpoint filter**: Code that runs before and after the handler of one endpoint or group.
- **Controller**: A class whose action methods handle HTTP requests, with attributes for routing.
- **Structured logging**: Logging with named fields for values, not just formatted text, so logs can be searched by field.
- **WebApplicationFactory**: A test helper that runs a whole ASP.NET Core app in memory and provides an HttpClient to call it.

### Where this leads

The same backend built with Node.js and TypeScript is in A Node and TypeScript backend (8.4). Validating requests and mapping errors is in Validation and errors at the boundary (8.5). Output caching and distributed caches are in Caching (8.6), and hosted background services in Background work and queues (8.7). Authentication and authorization are in Authentication and authorization (10.2), and packaging and running the app are in Containers and Docker (11.3).

### Check yourself

1. In `flightsapi.cs`, move the correlation-id middleware before `UseExceptionHandler`, call `/api/boom`, and check whether the 500 response still carries the header. Explain why.
2. Add a route constraint so `/api/flights/{number}` only matches numbers of two letters followed by one to four digits, and test a non-matching path.
3. Register an `IExceptionHandler` that turns `InvalidOperationException` into a 409 Problem Details, and call `/api/boom` again.
4. Add an endpoint filter to the flights group that rejects requests without an `X-Api-Key` header with 401.
5. Create a test project with `WebApplicationFactory` for a real API you work on, and write one test for a 404 and one for a successful call.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A typo in production config found at 3 am

`Smtp:Hostt` in appsettings.Production.json causes a NullReferenceException hours after deploy, on the first password reset.

*Validate options at start-up*

```csharp
public sealed class SmtpOptions
{
    [Required] public string Host { get; init; } = "";
    [Range(1, 65535)] public int Port { get; init; } = 587;
}

builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection("Smtp"))
    .ValidateDataAnnotations()
    .ValidateOnStart();          // the pod fails readiness immediately instead of hours later
```

> **Lesson:** Fail at start-up, not at 3 am. Bind configuration to typed options and validate on start.

#### Example 2: One error format for every failure

Errors come back as HTML pages, stack traces or ad-hoc JSON depending on where they happen.

*ProblemDetails with a trace id*

```csharp
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();   // maps known exceptions to 4xx

var app = builder.Build();
app.UseExceptionHandler();      // first in the pipeline
app.UseStatusCodePages();
```

> **Lesson:** Return Problem Details for every error, with a trace id users can quote and no internals.

### Configuration & the Options Pattern

<p class="src">From <em>.NET Backend Deep Dive</em> · Configuration & the Options Pattern</p>

<a id="dn-96"></a>

#### Configuration precedence

Later sources override earlier ones. The default order in `WebApplication.CreateBuilder`:

```
appsettings.json
appsettings.{Environment}.json
User secrets            (Development only)
Environment variables
Command-line arguments
```

```bash
# Nested keys use __ in environment variables (: doesn't work on Linux)
export ConnectionStrings__Default="Server=..."
export Payment__RetryCount=5
```

<a id="dn-97"></a>

#### IOptions vs IOptionsSnapshot vs IOptionsMonitor

A very common interview question, and the three genuinely differ:

| | Lifetime | Reloads on change? | Use in |
|---|---|---|---|
| `IOptions<T>` | Singleton | **No** — bound once at startup | Anything, when config never changes |
| `IOptionsSnapshot<T>` | **Scoped** | Yes, once per request | Controllers, scoped services |
| `IOptionsMonitor<T>` | Singleton | Yes, with a change callback | **Singletons and background services** |

```csharp
// IOptionsSnapshot CANNOT be injected into a singleton - captive dependency.
// A BackgroundService needs IOptionsMonitor:
public class Worker : BackgroundService
{
    private readonly IOptionsMonitor<WorkerOptions> _opts;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _opts.OnChange(o => _logger.LogInformation("Config reloaded: {@Options}", o));

        while (!ct.IsCancellationRequested)
        {
            var current = _opts.CurrentValue;          // re-read every iteration
            await Task.Delay(current.Interval, ct);
        }
    }
}
```

<a id="dn-98"></a>

#### Binding with validation — fail at startup, not at 3am

```csharp
public class PaymentOptions
{
    public const string Section = "Payment";

    [Required, Url]           public string BaseUrl { get; init; } = "";
    [Range(1, 10)]            public int RetryCount { get; init; }
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; init; }
}

builder.Services
    .AddOptions<PaymentOptions>()
    .Bind(builder.Configuration.GetSection(PaymentOptions.Section))
    .ValidateDataAnnotations()
    .Validate(o => o.RetryCount * o.Timeout.TotalSeconds < 30, "Retry budget exceeds 30s")
    .ValidateOnStart();       // <- throws at startup instead of on first use
```

`ValidateOnStart()` is the important line. Without it, a typo in `appsettings.Production.json` surfaces as a `NullReferenceException` on the first request that touches that code path — possibly hours after deploy, possibly only for one endpoint.

### Middleware, Error Handling & the Request Pipeline

<p class="src">From <em>.NET Backend Deep Dive</em> · Middleware, Error Handling & the Request Pipeline</p>

<a id="dn-100"></a>

#### Order is behavior

The pipeline is a chain of delegates; each can act before and after the next. **Order changes correctness, not just style.**

```csharp
app.UseExceptionHandler();        // outermost - must wrap everything below
app.UseHsts();
app.UseHttpsRedirection();
app.UseStaticFiles();             // before routing: skips auth for static assets
app.UseRouting();                 // decides WHICH endpoint - required before auth
app.UseCors();                    // after routing, before auth
app.UseRateLimiter();
app.UseAuthentication();          // who are you
app.UseAuthorization();           // may you  - MUST be after authentication
app.MapControllers();             // terminal
```

Classic ordering bugs:

- `UseAuthorization()` before `UseAuthentication()` → `User` is always anonymous, everything 401s.
- `UseCors()` after `UseAuthorization()` → preflight `OPTIONS` gets rejected before CORS headers are added, and the browser reports a CORS error while your logs show a clean 401.
- Exception handler registered late → exceptions thrown by earlier middleware escape unhandled.

<a id="dn-101"></a>

#### Writing middleware

```csharp
public class CorrelationIdMiddleware
{
    private const string Header = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    // Scoped dependencies are injected per-INVOKE, not in the constructor
    // (the middleware instance itself is effectively a singleton)
    public async Task InvokeAsync(HttpContext ctx, ILogger<CorrelationIdMiddleware> log)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault() ?? Guid.NewGuid().ToString();
        ctx.Response.Headers[Header] = id;

        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await _next(ctx);
    }
}
```

> Injecting a scoped service into a middleware **constructor** is a captive dependency and one of the most common ASP.NET Core bugs. Put it on `InvokeAsync` instead.

<a id="dn-102"></a>

#### Centralized error handling with ProblemDetails

Stop writing try/catch in controllers. Use `IExceptionHandler` (.NET 8+):

```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException      => (StatusCodes.Status400BadRequest, "Validation failed"),
            NotFoundException        => (StatusCodes.Status404NotFound, "Resource not found"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict"),
            OperationCanceledException when ctx.RequestAborted.IsCancellationRequested
                                     => (499, "Client closed request"),
            _                        => (StatusCodes.Status500InternalServerError, "Server error")
        };

        if (status >= 500)
            _logger.LogError(ex, "Unhandled exception");   // log 5xx, not 4xx

        var problem = new ProblemDetails
        {
            Status = status,
            Title  = title,
            // NEVER leak ex.Message or the stack trace to clients in production
            Detail = _env.IsDevelopment() ? ex.ToString() : null,
            Instance = ctx.Request.Path,
            Extensions = { ["traceId"] = Activity.Current?.TraceId.ToString() }
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
```

Returning the `traceId` is the single highest-value detail: a user pastes it into a support ticket and you jump straight to that trace.

<a id="dn-103"></a>

#### What not to do

```csharp
// Swallowing - the failure is now invisible
try { await DoAsync(); } catch (Exception) { }

// Destroys the original stack trace
catch (Exception ex) { throw ex; }        // WRONG
catch (Exception ex) { throw; }           // right
catch (Exception ex) { throw new AppException("...", ex); }  // right - preserves inner

// Catching what you can't handle just to log it, at every layer,
// produces the same exception logged five times.
// Log ONCE, at the boundary.
```

Filtering is usually better than catching:

```csharp
catch (SqlException ex) when (ex.Number is 1205)   // deadlock victim only
{
    await RetryAsync();
}
```

---

### Two stacks up close

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Two stacks up close</p>

<a id="be-z53"></a>

#### .NET up close

A thread-pool runtime with real types at runtime. Those two facts explain almost everything about how the stack feels.

``` yaml
WHAT THE PLATFORM GIVES YOU
  DI, configuration, logging, hosting, health checks, HTTP clients —
    in the box, one lifecycle, no assembly required
  types at runtime, so model binding, validation, serialisation, and
    ORM mapping all work without a separate schema ((see: Binding: raw input to typed arguments))
  genuine multi-threading, so CPU work parallelises for free
  EF Core, which is the strongest mainstream ORM for WRITES:
    change tracking over an object graph, one SaveChanges, one
    transaction
  date and time primitives that make an entire bug class
    unrepresentable: DateTimeOffset, DateOnly, TimeOnly, TimeProvider
    ((see: Time: three clocks, none of them trustworthy))
```

``` yaml
WHAT IT LEAVES TO YOU, AND WHAT BITES

  1. SHARED MUTABLE STATE IS POSSIBLE. Real threads means real races.
     A static Dictionary mutated from handlers is a data race that
     works in development and corrupts under load ((see: State is the thing that makes it hard)).

  2. SYNC-OVER-ASYNC. .Result or .GetAwaiter().GetResult() parks a
     pool thread. At load, every thread parks, requests queue, and the
     symptom looks exactly like a slow database: CPU idle, latency
     high, queue growing ((see: Async, and the mistake every stack shares)).

  3. LIFETIME CAPTURE. A singleton holding a scoped DbContext is THE
     .NET-specific bug. The context is used after disposal, its
     connection never returns to the pool, and it retains every entity
     it ever loaded ((see: Object lifetimes: the bug that only shows under load)).
     Fix: inject IServiceScopeFactory and create a scope per unit of work.

  4. EF HIDES QUERY COUNT. Four tidy lines can issue fifty-one queries.
     Log commands in development; assert the count in tests
     ((see: Seeing what your ORM actually does)).

  5. CHANGE TRACKING IN BATCHES. One long-lived context accumulates
     every entity, and change detection slows with each one. A fresh
     context per batch; AsNoTracking on every read-only query.
```

``` csharp
// The five habits from §39, in .NET:

// bind to input types
app.MapPost("/orders", async (CreateOrderRequest req, IPlaceOrder uc, CancellationToken ct)
    => Results.Ok(await uc.HandleAsync(req.ToCommand(), ct)));

// one projected query
await db.Orders.AsNoTracking()
    .Where(o => o.TenantId == tenant.Id)                  // tenant as PREDICATE
    .Select(o => new OrderRow(o.Id, o.Total, o.Customer.Name))
    .ToListAsync(ct);

// intent in the same transaction
db.Orders.Add(order);
db.Outbox.Add(OutboxMessage.For(new OrderPlaced(order.Id)));
await db.SaveChangesAsync(ct);                            // both, or neither

// a timeout on every outbound call
services.AddHttpClient<IStockApi, StockApi>(c => c.Timeout = TimeSpan.FromSeconds(5))
        .AddStandardResilienceHandler();                  // retries, breaker, jitter

// inject the clock
services.AddSingleton(TimeProvider.System);
```

> **\[Why it matters\]** **Where .NET is genuinely strong, stated plainly.** Complex relational domain models, transactional writes across an object graph, CPU-bound work, background processing in the same process as the API, and anything where date and time correctness matters.
>
> Where it costs you: a smaller library ecosystem for niche problems, and a hiring market concentrated in enterprise rather than startups. Neither is a technical weakness ([Node and TypeScript up close](08-04-a-node-and-typescript-backend.md#be-z54)).

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a127"></a>

#### Minimal APIs and controllers

Both compile down to the same endpoint routing. Minimal APIs are terser and slightly faster; controllers give you conventions, filters and model binding attributes that pay off in large surfaces.

``` csharp
// minimal API with a typed result union
var orders = app.MapGroup("/orders").RequireAuthorization().WithTags("Orders");

orders.MapGet("/{id:guid}", async Task<Results<Ok<OrderResponse>, NotFound>> (
        Guid id, IOrderQueries q, CancellationToken ct) =>
    await q.GetAsync(id, ct) is { } dto ? TypedResults.Ok(dto) : TypedResults.NotFound());

orders.MapPost("/", async (PlaceOrder cmd, IMediator m, CancellationToken ct) =>
{
    var id = await m.Send(cmd, ct);
    return TypedResults.CreatedAtRoute("GetOrder", new { id });
});
```

``` csharp
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}", Name = "GetOrder")]
    [ProducesResponseType<OrderResponse>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => await mediator.Send(new GetOrder(id), ct) is { } dto ? Ok(dto) : NotFound();
}
```

`[ApiController]` is doing real work: automatic 400 on model-state failure, binding source inference, and ProblemDetails responses. Without it you write all three by hand.

###### Organising minimal APIs so they do not become one enormous Program.cs

The usual objection to minimal APIs is that they do not scale past twenty endpoints. They do, with one convention: an endpoint module per feature area, discovered and mapped automatically.

``` csharp
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder app);
}

// registration by convention, once
public static void MapModules(this WebApplication app)
{
    foreach (var t in typeof(Program).Assembly.GetTypes()
                 .Where(t => typeof(IEndpointModule).IsAssignableFrom(t)
                          && t is { IsAbstract: false, IsInterface: false }))
        ((IEndpointModule)Activator.CreateInstance(t)!).MapEndpoints(app);
}
```

``` csharp
public sealed class OrderEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v{version:apiVersion}/orders")
                   .WithTags("Orders")
                   .RequireAuthorization()
                   .AddEndpointFilter<ValidationFilter>()
                   .WithOpenApi();

        g.MapGet("/",           List).WithName("ListOrders").CacheOutput(p => p.Expire(TimeSpan.FromSeconds(30)));
        g.MapGet("/{id:guid}",  Get).WithName("GetOrder");
        g.MapPost("/",          Place).RequireAuthorization("Orders.Write");
        g.MapDelete("/{id:guid}", Cancel).RequireAuthorization("Orders.Write");
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> Get(
        Guid id, IOrderQueries q, CancellationToken ct) =>
        await q.GetAsync(id, ct) is { } dto ? TypedResults.Ok(dto) : TypedResults.NotFound();
}
```

``` csharp
// endpoint filters are the minimal-API equivalent of action filters
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx,
                                                EndpointFilterDelegate next)
    {
        foreach (var arg in ctx.Arguments)
        {
            if (arg is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(arg.GetType());
            if (ctx.HttpContext.RequestServices.GetService(validatorType) is IValidator v)
            {
                var result = await v.ValidateAsync(new ValidationContext<object>(arg));
                if (!result.IsValid) return TypedResults.ValidationProblem(result.ToDictionary());
            }
        }
        return await next(ctx);
    }
}
```

`Results<Ok<T>, NotFound>` is worth adopting for its own sake: it is a discriminated union, so the OpenAPI document is generated correctly without `[ProducesResponseType]` attributes, and the compiler stops you returning a status the signature does not declare.

###### Performance difference, and whether it matters

|                                               | Minimal API                              | Controller                                        |
|-----------------------------------------------|------------------------------------------|---------------------------------------------------|
| Requests/sec (trivial endpoint, same machine) | Baseline                                 | ~10–15% lower                                     |
| Allocations per request                       | Lower                                    | Higher — filters, model state, action descriptors |
| Startup cost                                  | Lower                                    | Assembly scanning for controllers                 |
| AOT compatible                                | Yes, with the request delegate generator | Not fully                                         |

For an endpoint that does any real work — a database query, an HTTP call — the difference disappears into the noise, because the framework overhead is a fraction of a millisecond against tens of milliseconds of I/O. Choose on ergonomics, not throughput, unless you are building a gateway or a very hot internal service where the framework genuinely is the workload.

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a57"></a>

#### Projects, SDKs, target frameworks and NuGet

The modern SDK-style csproj is a few lines long, includes files by convention, and treats NuGet references as first-class. Comparing it to a .NET Framework csproj is the fastest way to feel the difference.

``` xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="8.0.3" />
  </ItemGroup>
</Project>
```

Use `Directory.Build.props` at the repo root to set shared properties once, and `Directory.Packages.props` with `ManagePackageVersionsCentrally` so twelve projects cannot drift onto twelve versions of the same package.

| Command                             | Does                            |
|-------------------------------------|---------------------------------|
| `dotnet new sln` / `dotnet sln add` | Create and populate a solution  |
| `dotnet restore / build / test`     | The CI trio                     |
| `dotnet publish -c Release`         | Produce deployable output       |
| `dotnet list package --vulnerable`  | Security audit; run it in CI    |
| `dotnet format`                     | Apply .editorconfig style rules |

###### Build configuration that scales past three projects

``` text
<!-- Directory.Build.props at the repository root - applied to EVERY project
     automatically, before its own contents. Stop copy-pasting settings. -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <Deterministic>true</Deterministic>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  </ItemGroup>
</Project>

<!-- Directory.Build.targets applies AFTER, for overriding -->
<!-- Directory.Packages.props: CENTRAL package version management -->
```

``` text
<!-- Directory.Packages.props - one place for every version in the solution -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>
</Project>

<!-- individual projects then reference WITHOUT a version -->
<PackageReference Include="Serilog.AspNetCore" />

<!-- this eliminates the commonest solution-level bug: two projects on two
     versions of the same package, resolving to whichever wins at runtime. -->
```

| Concept                  | Meaning                                     | Note                                                              |
|--------------------------|---------------------------------------------|-------------------------------------------------------------------|
| `PrivateAssets="all"`    | Do not flow to consumers                    | For analyzers, build tools, source generators                     |
| `ProjectReference`       | A reference to source in the solution       | Transitively flows by default                                     |
| Floating version `8.0.*` | Latest matching at restore                  | **Non-reproducible builds.** Avoid outside prototypes             |
| `packages.lock.json`     | Pins the whole transitive graph             | Enable with `RestorePackagesWithLockFile`                         |
| `--locked-mode`          | Restore fails if the lock file would change | Use in CI — catches unintended upgrades                           |
| `nuget.config`           | Feeds and their order                       | Pin feeds; a public feed shadowing a private one is a real attack |

``` bash
# reproducible restore in CI - this pair is the one that matters
dotnet restore --locked-mode
dotnet build --no-restore -c Release

# audit for known vulnerabilities, and fail the build on them
dotnet list package --vulnerable --include-transitive
dotnet restore /p:NuGetAuditMode=all /p:NuGetAuditLevel=moderate

# and know what you actually shipped
dotnet list package --include-transitive --format json > sbom-input.json
```

> **\[Trap\]**
>
> **Common trap.** **Dependency confusion** is a live supply-chain attack: if your `nuget.config` lists a public feed alongside a private one, an attacker who publishes a package to nuget.org with the same name as one of your internal packages may have it resolved instead — and it executes at build time. Use `<packageSourceMapping>` to bind each package prefix to exactly one feed, and reserve your internal prefixes on the public registry.

<a id="be-a59"></a>

#### Configuration and the options pattern

Configuration is a layered key/value store. Later providers override earlier ones, so the same binary reads appsettings on a dev laptop and environment variables in Kubernetes with no code change.

``` text
  appsettings.json
    -> appsettings.{Environment}.json
      -> user secrets (Development only)
        -> environment variables      (Logging__LogLevel__Default=Debug)
          -> command line args
            -> Key Vault / AppConfig  (added explicitly)
```

``` csharp
public sealed class SmtpOptions
{
    public const string Section = "Smtp";
    [Required] public string Host { get; init; } = "";
    [Range(1, 65535)] public int Port { get; init; } = 25;
}

builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();          // fail at boot, not at 3am on first send

// consume
public class Mailer(IOptions<SmtpOptions> opts) { var host = opts.Value.Host; }
```

| Interface             | Reads config                | Lifetime  |
|-----------------------|-----------------------------|-----------|
| `IOptions<T>`         | Once at startup             | Singleton |
| `IOptionsSnapshot<T>` | Once per request            | Scoped    |
| `IOptionsMonitor<T>`  | Live, with change callbacks | Singleton |

> **\[Note\]**
>
> **Never commit secrets.** Use `dotnet user-secrets` locally, and a managed store (Azure Key Vault, AWS Secrets Manager, Kubernetes secrets, Vault) everywhere else.

###### Worked example

Configuration binding trips people up because the layering rules are simple but invisible. Here is the same setting, resolved four ways:

``` bash
// appsettings.json
{ "Smtp": { "Host": "localhost", "Port": 25, "Timeouts": { "SendSeconds": 30 } } }

// appsettings.Production.json          -> overrides Host only
{ "Smtp": { "Host": "smtp.internal" } }

// environment variable                 -> double underscore = nesting, wins over both
Smtp__Timeouts__SendSeconds=10

// command line                         -> wins over everything
dotnet run --Smtp:Port=587

// final effective value in Production with that env var and arg:
//   Host = smtp.internal, Port = 587, Timeouts.SendSeconds = 10
```

| Symptom                                         | Cause                                                           |
|-------------------------------------------------|-----------------------------------------------------------------|
| Value is null in the container but fine locally | Colon separator in an env var name; Linux needs `__`            |
| Whole options object is default                 | Section name typo — binding a missing section silently succeeds |
| Array only partially overridden                 | Arrays merge by index; providers do not replace the whole array |
| Change to appsettings not picked up             | `IOptions<T>` is a snapshot; use `IOptionsMonitor`              |
| Works in the IDE, fails in CI                   | User secrets are Development-only by design                     |

The silent-empty-section failure is the one that costs hours. `ValidateOnStart()` with data annotations turns it into a startup exception naming the exact property, which is the difference between a five-second fix and an afternoon.

###### Validation and hot reload, properly wired

``` csharp
public sealed class RetryOptions
{
    public const string Section = "Retry";

    [Range(0, 10)]   public int MaxAttempts { get; init; } = 3;
    [Range(1, 60_000)] public int BaseDelayMs { get; init; } = 200;
    [Required, Url]  public string DeadLetterUrl { get; init; } = "";
}

builder.Services.AddOptions<RetryOptions>()
    .Bind(builder.Configuration.GetSection(RetryOptions.Section))
    .ValidateDataAnnotations()
    .Validate(o => o.BaseDelayMs * o.MaxAttempts < 30_000,
              "Total retry budget must stay under 30 seconds.")
    .ValidateOnStart();

// react to changes without a restart
public sealed class RetryPolicyHolder : IDisposable
{
    private readonly IDisposable? _sub;
    private volatile ResiliencePipeline _pipeline;

    public RetryPolicyHolder(IOptionsMonitor<RetryOptions> monitor)
    {
        _pipeline = Build(monitor.CurrentValue);
        _sub = monitor.OnChange(o => _pipeline = Build(o));   // rebuild on config change
    }

    public ResiliencePipeline Current => _pipeline;
    public void Dispose() => _sub?.Dispose();
}
```

> **\[Trap\]**
>
> **Common trap.** `OnChange` can fire two or three times for a single file save, because the file watcher sees the editor's intermediate writes. Debounce anything expensive, and make the handler idempotent.

<a id="be-a62"></a>

#### The middleware pipeline

An ASP.NET Core request passes through an ordered chain of delegates. Each one can act before calling `next`, after it, or short-circuit and never call it at all. Order is the whole game.

``` text
  request  ->  Exception handler
            ->  HSTS / HTTPS redirect
            ->  Static files
            ->  Routing
            ->  CORS
            ->  Authentication      (who are you?)
            ->  Authorization       (are you allowed?)
            ->  Rate limiting
            ->  Endpoint  ->  your handler
  response <-  ... unwinds back through every layer
```

``` csharp
app.UseExceptionHandler("/error");
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("default");
app.UseAuthentication();
app.UseAuthorization();          // must come AFTER authentication
app.MapControllers();

// custom middleware
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    await next(ctx);
    ctx.Response.Headers["X-Elapsed-Ms"] = sw.ElapsedMilliseconds.ToString();
});
```

> **\[Trap\]**
>
> **Common trap.** Writing to `Response.Headers` after `await next()` throws if the response already started streaming. Use `OnStarting` callbacks for anything that must land on the wire.

###### How it actually works

The pipeline is built by composing delegates in reverse order at startup, once. There is no per-request list walk — by the time a request arrives it is a single nested delegate chain.

``` csharp
// what app.Use(...) builds
public delegate Task RequestDelegate(HttpContext context);

// registration order:  A, B, C, terminal
// composition happens in REVERSE:
RequestDelegate app = terminal;
app = ctx => C(ctx, app);
app = ctx => B(ctx, app);
app = ctx => A(ctx, app);     // the final delegate you invoke per request

// so execution nests like this:
//   A pre  -> B pre  -> C pre  -> terminal -> C post -> B post -> A post
```

``` csharp
// a middleware that demonstrates both halves and short-circuiting
app.Use(async (ctx, next) =>
{
    var sw = ValueStopwatch.StartNew();                    // BEFORE

    if (ctx.Request.Headers.ContentLength > MaxBytes)
    {
        ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;                                             // SHORT-CIRCUIT: next is never called
    }

    // response headers must be set before the body starts streaming
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers["Server-Timing"] = $"app;dur={sw.Elapsed.TotalMilliseconds:0.0}";
        return Task.CompletedTask;
    });

    try { await next(ctx); }                                // DOWNSTREAM
    finally
    {
        _metrics.Record(ctx.Request.Path, ctx.Response.StatusCode, sw.Elapsed);   // AFTER
    }
});
```

###### Order mistakes and their exact symptoms

| Wrong order                                   | Symptom                                                                                    |
|-----------------------------------------------|--------------------------------------------------------------------------------------------|
| `UseAuthorization` before `UseAuthentication` | Every request is anonymous; all `[Authorize]` endpoints return 401 even with a valid token |
| `UseAuthorization` before `UseRouting`        | Policies are silently not applied — endpoint metadata does not exist yet                   |
| `UseCors` after `UseAuthorization`            | Pre-flight `OPTIONS` requests get 401 instead of the CORS headers                          |
| `UseExceptionHandler` not first               | Exceptions thrown in earlier middleware escape as a raw 500 with a stack trace             |
| `UseStaticFiles` after auth                   | Every image and stylesheet goes through the auth pipeline — slow, and often broken         |
| `UseForwardedHeaders` late                    | Redirect URIs use `http`, remote IP is the proxy's, rate limits bucket everyone together   |

The canonical order, worth memorising because it is asked in interviews and because getting it wrong is subtle: exception handler, HSTS, HTTPS redirection, forwarded headers, static files, routing, CORS, authentication, authorization, rate limiting, endpoints.

<a id="be-a67"></a>

#### DTOs, mapping and API contracts

Never expose domain entities directly over HTTP. Lazy-loaded navigation properties serialise into infinite loops or gigantic payloads, and every internal rename silently becomes a breaking API change.

``` csharp
public record OrderResponse(Guid Id, string Status, decimal Total, DateTimeOffset PlacedAt);

// projection: only the columns you need reach the database
var dto = await db.Orders
    .Where(o => o.Id == id)
    .Select(o => new OrderResponse(o.Id, o.Status.ToString(),
                                   o.Lines.Sum(l => l.Price * l.Qty), o.PlacedAt))
    .SingleOrDefaultAsync(ct);
```

**Hand-written mapping**

- Compile-time safe; refactoring catches it
- Debuggable and greppable
- Verbose for wide types

**AutoMapper / Mapperly**

- Less boilerplate
- AutoMapper is reflection-based and now commercially licensed
- Mapperly is a source generator: real code, no runtime cost

###### Project in the query, not after it

Where you map decides how much data crosses the wire. Mapping *inside* the LINQ query lets EF translate the selection into the SQL column list — often turning a wide entity load plus includes into one narrow join. Mapping after `ToListAsync()` materialises everything first, and no mapper can undo that.

``` csharp
// ✗ loads every column of Order + every Line, then throws most away
var orders = await db.Orders.Include(o => o.Lines).ToListAsync(ct);
return orders.Select(Map.ToSummary).ToList();

// ✓ SELECT Id, PlacedAt, COUNT(*), SUM(...) — nothing else leaves the DB
return await db.Orders
    .Where(o => o.CustomerId == id)
    .OrderByDescending(o => o.PlacedAt)
    .Select(o => new OrderSummary(
        o.Id, o.PlacedAt, o.Lines.Count, o.Lines.Sum(l => l.Subtotal)))
    .Take(20)
    .ToListAsync(ct);

// DTOs as records: value equality for free, immutable by default,
// and a constructor the JSON source generator can bind to
public sealed record OrderSummary(Guid Id, DateTime PlacedAt,
                                  int LineCount, decimal Total);
```

> **\[Trap\]**
>
> **Common trap.** A DTO is a published contract. Renaming a property, tightening a type, or removing a field breaks every consumer that has already shipped — and JSON gives you no compiler to catch it. Additive changes are safe; anything else needs a new version. Decide the versioning strategy (URL segment, header, or media type) before the first external client, not after.

### Under the Hood

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Under the Hood</p>

<p class="pd">What actually happens between dotnet run and a byte on the wire. Assembly loading, the JIT, the thread pool, Kestrel, routing, model binding, filters and the DI container — the machinery that makes everything above it feel like magic until it breaks.</p>

<a id="be-a45"></a>

#### Kestrel: from socket to HttpContext

Kestrel is built on `System.IO.Pipelines`, a buffer-management library designed to parse network data with no intermediate allocations. Understanding the shape helps when you are chasing latency or memory.

``` text
  accept loop
    -> connection accepted, ConnectionContext created
    -> connection middleware runs   (TLS handshake happens here)
    -> protocol selected via ALPN   (http/1.1 or h2)

  per request
    -> read from the PipeReader  (pooled memory, no byte[] per read)
    -> parse the request line and headers into pooled structures
    -> build HttpContext  (itself pooled and reset between requests)
    -> hand to the application delegate  =  your middleware pipeline
    -> your code writes to the PipeWriter; Kestrel flushes and applies backpressure
    -> reset and reuse the context; keep-alive holds the connection open
```

| Kestrel limit              | Default     | Why you would change it                            |
|----------------------------|-------------|----------------------------------------------------|
| `MaxConcurrentConnections` | Unlimited   | Protect memory on a busy edge service              |
| `MaxRequestBodySize`       | ~28.6 MB    | File uploads, or lock it down to reject abuse      |
| `KeepAliveTimeout`         | 130 s       | Match your load balancer's idle timeout            |
| `RequestHeadersTimeout`    | 30 s        | Slowloris-style attacks                            |
| `MinRequestBodyDataRate`   | 240 bytes/s | Drop clients that trickle data to hold connections |

> **\[Trap\]**
>
> **Common trap.** `HttpContext` is pooled and reset after the response completes. Capturing it — or `Request`, `User`, or a header string — in a fire-and-forget background task means you will read another user's request. Copy what you need into locals before the pipeline unwinds.

###### How it actually works

Why pipelines instead of streams. A stream-based parser has to answer an awkward question on every read: the data you received might contain half a header, or three whole requests. Handling that with `Stream.Read` means either copying leftovers into a growing buffer yourself, or reading one byte at a time.

``` csharp
// the shape of a pipelines-based read loop
while (true)
{
    ReadResult result = await reader.ReadAsync(ct);
    ReadOnlySequence<byte> buffer = result.Buffer;

    while (TryParseRequestLine(ref buffer, out var line))    // consume what is complete
        Handle(line);

    // tell the pipe: I consumed up to X, but I EXAMINED up to Y.
    // if consumed == examined and nothing parsed, the next ReadAsync waits for MORE data
    reader.AdvanceTo(buffer.Start, buffer.End);

    if (result.IsCompleted) break;
}
```

The `ReadOnlySequence<byte>` can span multiple discontiguous memory segments, so a header split across two TCP packets is parsed without any copying or reassembly. The memory itself comes from a pool, so a sustained 10,000 requests per second allocates essentially nothing for the transport layer. This is the core reason Kestrel benchmarks the way it does.

Backpressure comes for free: if your application writes to the `PipeWriter` faster than the client can read, `FlushAsync` stops completing, which naturally slows your handler instead of buffering an unbounded amount in memory.

###### What this looks like when it breaks

``` csharp
// the pooling bug, in full
app.MapPost("/orders", async (HttpContext ctx, IBus bus) =>
{
    var user = ctx.User;                       // captured reference
    var correlationId = ctx.TraceIdentifier;

    _ = Task.Run(async () =>                   // fire and forget
    {
        await Task.Delay(2000);
        await bus.PublishAsync(new Audit(user.Identity!.Name!, correlationId));
        //                                ^^^^ by now, ctx has been RESET and reused.
        //                                user may belong to a completely different request.
    });

    return Results.Accepted();
});

// correct: copy the values BEFORE the response completes
var userName = ctx.User.Identity?.Name ?? "anonymous";     // a string, safely detached
var traceId  = ctx.TraceIdentifier;
_ = Task.Run(async () => { ... use userName, traceId ... });
```

The reason this is so hard to catch is that it works perfectly under low load. The context is only reused when another request arrives, so it passes every local test and every staging smoke test, then produces cross-user data in production at peak. Any background work started from a request should receive plain immutable values, or better, a message on a queue.

<a id="be-a46"></a>

#### Routing and endpoint resolution

Routing runs in two halves. `UseRouting` matches the request to an endpoint and puts it on the context; `UseEndpoints` (or the terminal `Map*` call) executes it. Everything between the two — authorization, CORS, rate limiting — can therefore inspect the endpoint's metadata before it runs.

``` text
  UseRouting
    build/lookup in the DFA-based route matcher   (a prefix tree, not a regex loop)
      -> candidate set
      -> apply constraints  ({id:guid}, {page:int:min(1)})
      -> apply HTTP method and content-type matching
      -> select by precedence: literal > constrained param > catch-all
    HttpContext.SetEndpoint(match)

  ...auth / cors / rate limiting read endpoint.Metadata here...

  UseEndpoints
    invoke endpoint.RequestDelegate
```

``` csharp
// metadata is how attributes reach the middleware that needs them
app.MapGet("/reports/{id:guid}", Handler)
   .RequireAuthorization("Reports.Read")   // adds AuthorizeAttribute metadata
   .WithName("GetReport")
   .CacheOutput(p => p.Expire(TimeSpan.FromMinutes(5)))
   .RequireRateLimiting("reports");

// inspect it from custom middleware
var endpoint = ctx.GetEndpoint();
var audit = endpoint?.Metadata.GetMetadata<AuditAttribute>();
```

> **\[Trap\]**
>
> **Common trap.** Middleware registered *before* `UseRouting` cannot see the endpoint — `GetEndpoint()` returns null. This is the actual reason authorization must come after routing, and it is why moving a `Use...` call one line can silently disable it.

###### How a URL becomes an endpoint

``` text
  routing runs in TWO middleware stages, and the gap between them matters

    UseRouting()        MATCHES the request to an Endpoint.
      |                 sets HttpContext.GetEndpoint()
      |                 does NOT execute it
      |
    [ middleware here can SEE which endpoint was selected ]
      |                 -> this is why UseAuthorization() sits BETWEEN them:
      |                    it reads the endpoint's [Authorize] METADATA
      |
    UseEndpoints() / auto   EXECUTES the endpoint

  put authorization before UseRouting and it has no endpoint to inspect,
  so attribute-based policies silently do nothing. This ordering bug
  produces an app that appears to work and is entirely unprotected.
```

``` csharp
// both controllers and minimal APIs compile down to the SAME Endpoint object:
//   a RequestDelegate + a metadata collection
// which is why filters, auth, CORS and OpenAPI work identically for both.

app.MapGet("/orders/{id:long}", (long id, IOrderService svc) => svc.Get(id))
   .RequireAuthorization("OrdersRead")     // metadata
   .WithName("GetOrder")                   // metadata
   .Produces<OrderDto>(200)                // metadata
   .CacheOutput(p => p.Expire(TimeSpan.FromMinutes(1)));

// inspect what was registered - useful when a route "does not match"
var sources = app.Services.GetRequiredService<EndpointDataSource>();
foreach (var e in sources.Endpoints) Console.WriteLine(e.DisplayName);
```

| Precedence, most specific first | Example                                    |
|---------------------------------|--------------------------------------------|
| Literal segments                | `/orders/export` beats `/orders/{id}`      |
| Constrained parameters          | `{id:long}` beats `{id}`                   |
| Unconstrained parameters        | `{id}`                                     |
| Catch-all                       | `{**rest}` — always last                   |
| Explicit `Order`                | Overrides all of the above — use sparingly |

Route constraints are matching rules, **not validation**. `{id:long}` means “this route only matches if the segment parses as a long”; a non-numeric value produces a **404, not a 400**, because no endpoint matched. That is frequently the wrong response for an API, and it is why explicit validation still belongs in the handler.

> **\[Trap\]**
>
> **Common trap.** Ambiguous routes throw `AmbiguousMatchException` at *request* time, not startup — so a route conflict introduced by a merge passes every build and fails only when someone hits that URL. Enumerating `EndpointDataSource` in a startup test and asserting on the route table is cheap insurance.

<a id="be-a47"></a>

#### Model binding, validation and the MVC pipeline

Model binding turns an HTTP request into method arguments by walking a set of value providers in order. When it produces a surprising null, the cause is almost always which provider was consulted.

| Source   | Attribute        | Reads from                                       |
|----------|------------------|--------------------------------------------------|
| Route    | `[FromRoute]`    | Route template values                            |
| Query    | `[FromQuery]`    | Query string                                     |
| Body     | `[FromBody]`     | Request body, deserialized — only one per action |
| Form     | `[FromForm]`     | `application/x-www-form-urlencoded` or multipart |
| Header   | `[FromHeader]`   | Request headers                                  |
| Services | `[FromServices]` | The DI container                                 |

With `[ApiController]`, inference kicks in: complex types come from the body, simple types from route or query, and anything registered in DI from services. Minimal APIs use the same rules with a source-generated request delegate rather than reflection.

``` text
  MVC action pipeline (each stage can short-circuit)

  Authorization filters
    -> Resource filters        (caching, before model binding)
      -> MODEL BINDING + VALIDATION
        -> Action filters      (around your method)
          -> [ your action ]
        -> Action filters (after)
      -> Exception filters     (only if something threw)
    -> Result filters
      -> RESULT EXECUTION      (serialize, set status, write body)
```

**Use middleware when**

- It applies to every request, including static files
- You need to run before routing
- You are dealing with the raw request or response

**Use a filter when**

- It only applies to MVC actions or a group
- You need the bound model or action metadata
- You want to short-circuit with an `IActionResult`

###### Binding sources, and the overposting vulnerability

| Source         | Attribute                  | Bound from                                     |
|----------------|----------------------------|------------------------------------------------|
| Route          | `[FromRoute]`              | URL path segments                              |
| Query          | `[FromQuery]`              | Query string                                   |
| Body           | `[FromBody]`               | Request body — **only one per action**         |
| Form           | `[FromForm]`               | `application/x-www-form-urlencoded`, multipart |
| Header         | `[FromHeader]`             | Request headers                                |
| Services       | `[FromServices]`           | DI — inferred automatically in minimal APIs    |
| Keyed services | `[FromKeyedServices("x")]` | DI, by key                                     |

``` csharp
// MVC infers the source when you do not say. The rules differ from minimal APIs:
//   MVC:     complex types default to BODY only for [ApiController]; otherwise form/query
//   Minimal: complex types default to BODY; simple types to route then query
// mixing the two mental models is a common source of "the parameter is always null".

// THE OVERPOSTING VULNERABILITY - binding straight to an entity
[HttpPost]
public async Task<IActionResult> Update(long id, Order order)   // <- the ENTITY
{
    // an attacker posts: { "total": 0, "status": "Paid", "tenantId": 999 }
    // every settable property binds, including ones the UI never shows.
    _db.Update(order);
    await _db.SaveChangesAsync();
}

// the fix is the DTO you should have had anyway - it is an allow-list by construction
public record UpdateOrderRequest(string ShippingNote, int Quantity);

[HttpPost]
public async Task<IActionResult> Update(long id, UpdateOrderRequest req)
{
    var order = await _db.Orders.FindAsync(id);
    if (order is null) return NotFound();
    if (order.TenantId != _ctx.TenantId) return Forbid();   // authorise the ROW
    order.ShippingNote = req.ShippingNote;                  // only what is allowed
    order.Quantity = req.Quantity;
    await _db.SaveChangesAsync();
    return NoContent();
}
```

``` text
  the MVC filter pipeline, in order - and where to put what

    Authorization filter      is this caller allowed here at all?
      |
    Resource filter           caching, short-circuiting. Runs BEFORE binding.
      |
    MODEL BINDING             + automatic validation
      |
    Action filter             before/after the action method
      |
      ACTION METHOD
      |
    Exception filter          only if something threw
      |
    Result filter             before/after the result executes

  minimal APIs have ENDPOINT FILTERS instead - one simpler concept covering
  roughly what action and result filters did:

    app.MapPost("/orders", Handler)
       .AddEndpointFilter<ValidationFilter<CreateOrder>>();
```

``` csharp
// [ApiController] changes several behaviours at once - know what you opted into
[ApiController]
public class OrdersController : ControllerBase { }
//  - automatic 400 with a ValidationProblemDetails when ModelState is invalid
//    (so you never write `if (!ModelState.IsValid)` again)
//  - complex parameters inferred as [FromBody]
//  - attribute routing REQUIRED
//  - ProblemDetails for error status codes

// disable the automatic 400 if you want to shape the response yourself
builder.Services.Configure<ApiBehaviorOptions>(o =>
    o.SuppressModelStateInvalidFilter = true);
```

### API surfaces in .NET

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · API surfaces in .NET</p>

<a id="be-m243"></a>

#### Auth, rate limiting, and output caching

``` csharp
// AuthN: who are you.   AuthZ: are you allowed.  Two separate middlewares,
// in that order (§231).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        o.Authority = "https://login.example.com";
        o.TokenValidationParameters = new() {
            ValidateAudience = true, ValidAudience = "orders-api",
            ValidateIssuer = true, ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)      // default is FIVE MINUTES
        };
    });

builder.Services.AddAuthorization(o => {
    o.AddPolicy("orders:write", p => p.RequireClaim("scope", "orders.write"));
    o.FallbackPolicy = new AuthorizationPolicyBuilder()   // secure by default:
        .RequireAuthenticatedUser().Build();               // opt OUT with [AllowAnonymous]
});
```

``` csharp
// Rate limiting (built in since .NET 7) - partition per client, not globally.
builder.Services.AddRateLimiter(o => {
    o.AddPolicy("per-tenant", ctx => RateLimitPartition.GetTokenBucketLimiter(
        ctx.User.FindFirstValue("tenant_id") ?? ctx.Connection.RemoteIpAddress!.ToString(),
        _ => new TokenBucketRateLimiterOptions {
            TokenLimit = 100, TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
app.UseRateLimiter();
```

``` csharp
// Output caching: cache the RESPONSE, so the handler never runs.
builder.Services.AddOutputCache(o =>
    o.AddPolicy("catalog", b => b.Expire(TimeSpan.FromMinutes(5))
                                 .SetVaryByQuery("page", "category")
                                 .Tag("catalog")));
app.MapGet("/catalog", GetCatalog).CacheOutput("catalog");
await cacheStore.EvictByTagAsync("catalog", ct);        // invalidate on write
```

> **\[Trap\]**
>
> **Never cache a response that varies by user without varying the key on the user** — it is the tenant-leak bug at the HTTP layer, and a shared proxy will happily serve one customer's data to another ([Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)). Authenticated endpoints should generally set `Cache-Control: private, no-store` unless you have thought carefully about the key.

> **\[Tip\]**
>
> **The default `ClockSkew` of five minutes surprises people:** a revoked or expired token keeps working for up to five extra minutes. Tighten it if your clocks are synchronised, and remember that JWTs are not revocable by design — short lifetimes plus a refresh flow are the mechanism, not a blocklist you check on every request.

### .NET Aspire

<p class="src">From <em>Backend Engineering</em> · C# & .NET · .NET Aspire</p>

<p class="pd">Microsoft&#x27;s opinionated stack for building, running and observing multi-service .NET applications. It is the biggest change to how .NET solutions are composed since the generic host, and it is worth learning properly rather than as a demo.</p>

<a id="be-a173"></a>

#### What Aspire actually is

Aspire is three things that ship together and are often confused with each other.

| Piece                   | What it does                                                                                                   | Runs where                                                   |
|-------------------------|----------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------|
| **App model / AppHost** | A C# project describing every service, database, broker and their relationships                                | Development, and as the source for deployment manifests      |
| **Integrations**        | NuGet packages pairing a resource (hosting) with a preconfigured client (health checks, telemetry, resilience) | Both                                                         |
| **Dashboard**           | Live logs, traces, metrics and console output for the whole system in one browser tab                          | Development, and optionally standalone in other environments |

What it is *not*: a runtime, a framework your code depends on, a service mesh, or a deployment target. Your services remain ordinary ASP.NET Core projects that run perfectly well without Aspire. That is the key design decision — it composes your application rather than infecting it.

> **\[Why it matters\]** The problem it solves is real and boring: running fifteen services, three databases, a broker and a cache locally, with correct connection strings, and being able to see what happened across all of them. Every team of a certain size builds a worse version of this out of shell scripts and Compose files.

###### What it is, and the four things it is not

``` text
  ASPIRE IS: a C# program that describes your system, plus a set of
  conventions and integrations built on top of that description.

    AppHost/Program.cs        <- an ordinary console app. Run it, and it
                                 starts your services and their dependencies.
      |
      +-- launches your projects
      +-- starts containers (postgres, redis, rabbitmq) via Docker
      +-- WIRES THEM TOGETHER: injects connection strings and URLs as
      |   environment variables, so nothing is hardcoded
      +-- serves a dashboard showing logs, traces and metrics for all of it
      |
      +-- and can EMIT a deployment manifest for a target platform

  IT IS NOT
    a runtime         your app is unchanged; it reads env vars as always
    a deployment target  it generates manifests; something else deploys them
    a framework       you can remove it and the services still run
    required          every piece of it is something you could write yourself
```

``` csharp
// the AppHost - and note that this is just C#, with a debugger and types
var builder = DistributedApplication.CreateBuilder(args);

var db    = builder.AddPostgres("pg").WithDataVolume().AddDatabase("appdb");
var cache = builder.AddRedis("cache");
var queue = builder.AddRabbitMQ("mq");

var api = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(db)          // -> ConnectionStrings__appdb
    .WithReference(cache)       // -> ConnectionStrings__cache
    .WithReference(queue)
    .WaitFor(db);               // do not start until postgres is healthy

builder.AddProject<Projects.Orders_Worker>("worker")
    .WithReference(db).WithReference(queue);

builder.AddNpmApp("web", "../web").WithReference(api).WithHttpEndpoint(env: "PORT");

builder.Build().Run();
```

The honest framing: Aspire's real contribution is **removing the docker-compose file, the tangle of environment variables, and the hour of OpenTelemetry wiring that every team writes independently**. It replaces them with typed C# and a set of defaults. That is genuinely valuable and it is not magic — everything it does, you could do by hand, and knowing what it does by hand is what makes it debuggable.

<a id="be-a174"></a>

#### The app model

The AppHost is a normal console project whose `Program.cs` declares resources and the references between them. It is C#, so it has types, loops, conditionals and refactoring.

``` csharp
var builder = DistributedApplication.CreateBuilder(args);

// parameters and secrets
var sqlPassword = builder.AddParameter("sql-password", secret: true);

// infrastructure resources
var sql    = builder.AddSqlServer("sql", sqlPassword)
                    .WithDataVolume()                     // survives restarts
                    .AddDatabase("ordersdb");

var mongo  = builder.AddMongoDB("mongo").AddDatabase("catalog");
var cache  = builder.AddRedis("cache").WithRedisCommander();
var bus    = builder.AddRabbitMQ("bus").WithManagementPlugin();
var blobs  = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs("documents");

// projects
var orders = builder.AddProject<Projects.Orders_Api>("orders-api")
                    .WithReference(sql).WaitFor(sql)
                    .WithReference(cache)
                    .WithReference(bus)
                    .WithReplicas(2);

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api")
                     .WithReference(mongo).WaitFor(mongo);

// a non-.NET service, because real systems have them
var legacy = builder.AddContainer("legacy-app", "registry.internal/legacy-app", "4.8")
                    .WithHttpEndpoint(port: 8081, targetPort: 80);

builder.AddNpmApp("web", "../web-frontend", "dev")
       .WithReference(orders).WithReference(catalog)
       .WithHttpEndpoint(env: "PORT")
       .WithExternalHttpEndpoints();

builder.Build().Run();
```

| Call                          | Meaning                                                             |
|-------------------------------|---------------------------------------------------------------------|
| `WithReference(x)`            | Inject x's connection info into this project's configuration        |
| `WaitFor(x)`                  | Do not start until x reports healthy                                |
| `WithDataVolume()`            | Persist the container's data between runs                           |
| `WithReplicas(n)`             | Run n instances locally — catches single-instance assumptions early |
| `WithExternalHttpEndpoints()` | Expose publicly when deployed                                       |
| `AddParameter(secret: true)`  | A value supplied per environment, never in source                   |

> **\[Note\]**
>
> **Run it with replicas from day one.** Two replicas locally surfaces every in-memory cache, in-process scheduler and singleton assumption *before* it becomes a production incident. This alone justifies the AppHost for a lot of teams.

###### Structuring an AppHost that stays readable past ten services

A flat `Program.cs` is fine for five resources and unmanageable at thirty. Because it is ordinary C#, the fix is ordinary C#: extension methods per concern.

``` csharp
var builder = DistributedApplication.CreateBuilder(args);

var infra    = builder.AddInfrastructure();          // databases, cache, broker, storage
var platform = builder.AddPlatformServices(infra);   // identity, notifications, files
var domain   = builder.AddDomainServices(infra, platform);
builder.AddFrontends(domain);

builder.Build().Run();
```

``` csharp
public static class InfrastructureExtensions
{
    public static Infra AddInfrastructure(this IDistributedApplicationBuilder b)
    {
        var pwd = b.AddParameter("db-password", secret: true);

        var sql = b.AddSqlServer("sql", pwd)
                   .WithDataVolume()
                   .WithLifetime(ContainerLifetime.Persistent);   // survives F5

        var cache = b.AddRedis("cache").WithRedisInsight();
        var bus   = b.AddRabbitMQ("bus").WithManagementPlugin().WithDataVolume();

        // conditional: use a real cloud resource when a connection string is supplied
        var blobs = b.ExecutionContext.IsPublishMode
            ? b.AddAzureStorage("storage").AddBlobs("documents")
            : b.AddAzureStorage("storage").RunAsEmulator().AddBlobs("documents");

        return new Infra(sql, cache, bus, blobs);
    }
}

public record Infra(
    IResourceBuilder<SqlServerServerResource> Sql,
    IResourceBuilder<RedisResource> Cache,
    IResourceBuilder<RabbitMQServerResource> Bus,
    IResourceBuilder<AzureBlobStorageResource> Blobs);
```

| Helper                            | Does                                                                    |
|-----------------------------------|-------------------------------------------------------------------------|
| `ExecutionContext.IsRunMode`      | True during `dotnet run` — use emulators and dev volumes                |
| `ExecutionContext.IsPublishMode`  | True when generating a manifest — bind to real resources                |
| `WithLifetime(Persistent)`        | Container survives between debug sessions — a large quality-of-life win |
| `WithEnvironment(name, resource)` | Inject an arbitrary value or endpoint into a project                    |
| `WithHttpHealthCheck("/health")`  | Makes `WaitFor` meaningful for your own services                        |
| `PublishAsConnectionString()`     | Treat a dev container as an externally-provided resource in production  |

###### Adding a legacy .NET Framework app to the model

``` csharp
// A .NET Framework app runs on Windows/IIS and cannot be a Projects.* reference, but it can still
// participate in the topology and, more importantly, in the traces.
var legacy = builder.AddContainer("legacy-app", "registry.internal/legacy-app", "4.8.2")
    .WithHttpEndpoint(port: 8081, targetPort: 80, name: "http")
    .WithEnvironment("ConnectionStrings__Raven", ravenConnection)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", builder.Configuration["DashboardOtlp"])
    .WithEnvironment("OTEL_SERVICE_NAME", "legacy-app");

// or, if it runs on the host rather than in a container
var legacyLocal = builder.AddExecutable("legacy-app", "iisexpress.exe",
        workingDirectory: "../legacy",
        args: ["/path:C:\\src\\LegacyApp", "/port:8081"]);

// the new service, and the facade in front of both
var orders = builder.AddProject<Projects.Orders_Api>("orders-api").WithReference(sql);

builder.AddProject<Projects.Gateway>("gateway")
       .WithReference(orders)
       .WithEnvironment("Legacy__Url", legacy.GetEndpoint("http"))
       .WithExternalHttpEndpoints();
```

<a id="be-a175"></a>

#### Integrations: hosting side and client side

Each integration comes in a pair. The *hosting* package runs in the AppHost and knows how to start the resource; the *client* package goes in your service and configures the SDK with health checks, OpenTelemetry, logging and retries already wired.

``` csharp
// AppHost (hosting side)
var pg = builder.AddPostgres("pg").AddDatabase("orders");

// Orders.Api (client side) - one line replaces ~20 of setup
builder.AddNpgsqlDbContext<OrdersDb>("orders");
// you now get: connection string from config, retry on failure,
//              a /health check, EF Core traces and metrics on the OTLP exporter
```

| Resource                      | AppHost                              | Service                                           |
|-------------------------------|--------------------------------------|---------------------------------------------------|
| SQL Server                    | `AddSqlServer().AddDatabase()`       | `AddSqlServerDbContext<T>()`                      |
| PostgreSQL                    | `AddPostgres().AddDatabase()`        | `AddNpgsqlDbContext<T>()`                         |
| MongoDB                       | `AddMongoDB().AddDatabase()`         | `AddMongoDBClient()`                              |
| Redis                         | `AddRedis()`                         | `AddRedisDistributedCache()` / `AddRedisClient()` |
| RabbitMQ                      | `AddRabbitMQ()`                      | `AddRabbitMQClient()`                             |
| Kafka                         | `AddKafka()`                         | `AddKafkaProducer/Consumer()`                     |
| Azure Service Bus             | `AddAzureServiceBus()`               | `AddAzureServiceBusClient()`                      |
| Cosmos DB                     | `AddAzureCosmosDB().RunAsEmulator()` | `AddAzureCosmosClient()`                          |
| Elasticsearch, Qdrant, Milvus | `AddElasticsearch()`, `AddQdrant()`  | Matching client packages                          |

Anything without an official integration is still expressible: `AddContainer` for an arbitrary image, `AddExecutable` for a process, `AddConnectionString` for something that already exists elsewhere. You are never blocked by the catalogue.

###### Two halves, and why they are separate packages

``` text
  every integration comes as a PAIR, and confusing them is the usual
  first-hour mistake.

  HOSTING package        Aspire.Hosting.PostgreSQL
    referenced by        the APPHOST only
    what it does         starts a container, exposes a connection string
                         as a resource other projects can .WithReference()

  CLIENT package         Aspire.Npgsql.EntityFrameworkCore.PostgreSQL
    referenced by        the SERVICE that connects
    what it does         registers DbContext + health check + OpenTelemetry
                         tracing + a resilience policy, reading the
                         connection string by NAME from configuration

  so:   AppHost.csproj  -> Aspire.Hosting.PostgreSQL
        Orders.Api.csproj -> Aspire.Npgsql.EntityFrameworkCore.PostgreSQL

  put the hosting package in the service and nothing happens.
  put the client package in the AppHost and nothing happens.
```

``` csharp
// the service side - one line replaces about thirty
builder.AddNpgsqlDbContext<AppDbContext>("appdb");
//                                        ^^^^^^^ the RESOURCE NAME from the AppHost

// what that one line actually registered:
//   AddDbContext with the connection string from ConnectionStrings__appdb
//   a health check on that connection, tagged "ready"
//   OpenTelemetry tracing for EF Core commands
//   OpenTelemetry metrics for the connection pool
//   a retry policy for transient failures
//   command timeout and pool settings from configuration

// and you can override any of it
builder.AddNpgsqlDbContext<AppDbContext>("appdb",
    settings => { settings.DisableRetry = true; settings.CommandTimeout = 5; },
    options  => options.UseSnakeCaseNamingConvention());
```

> **\[Note\]**
>
> **Read one integration's source once.** The client integrations are small and they are the clearest documentation of what “correctly configured” means for that technology — which health check, which metrics, which resilience defaults. Even if you never adopt Aspire, reading `AddNpgsqlDbContext` tells you what you should have been registering by hand.

<a id="be-a176"></a>

#### Service discovery and configuration flow

This is the part that removes the most day-to-day friction. Referencing a resource injects environment variables that the client integration and the service-discovery layer read automatically, so no service ever hardcodes a host, port or connection string.

``` text
  AppHost                                    Orders.Api process env
  -----------------------------------------------------------------------
  AddPostgres("pg").AddDatabase("orders")  -> ConnectionStrings__orders=Host=localhost;Port=54923;...
  AddRedis("cache")                        -> ConnectionStrings__cache=localhost:61234
  orders.WithReference(catalog)            -> services__catalog-api__https__0=https://localhost:7284

  In the service:
     builder.AddNpgsqlDbContext<OrdersDb>("orders");         // reads the first
     builder.Services.AddHttpClient<ICatalog, Catalog>(c =>
         c.BaseAddress = new("https+http://catalog-api"));   // resolves the third
```

The `https+http://catalog-api` scheme is service discovery: prefer HTTPS, fall back to HTTP, and resolve the name through whatever provider is configured. Locally that is the Aspire-injected config; in Kubernetes it can be DNS; in Azure Container Apps it is the platform's internal names. The code does not change.

> **\[Trap\]**
>
> **Common trap.** Ports are assigned dynamically each run unless you pin them. That is deliberate — it stops anyone from hardcoding `localhost:5001` — but it does mean external tools like Postman collections should target the discovered endpoint rather than a fixed port.

###### How a connection string actually reaches your service

``` text
  follow one value end to end - this is the whole mechanism

  1  AppHost declares a resource
       var db = builder.AddPostgres("pg").AddDatabase("appdb");

  2  AppHost starts the container and learns its real port
       postgres is now on localhost:54321 (randomised)

  3  a project takes a reference
       .WithReference(db)

  4  Aspire sets an ENVIRONMENT VARIABLE on that project's process
       ConnectionStrings__appdb=Host=localhost;Port=54321;Database=appdb;...

  5  your service reads it through ORDINARY IConfiguration
       builder.AddNpgsqlDbContext<AppDbContext>("appdb");
       -> looks up ConnectionStrings:appdb

  there is no magic layer. It is environment variables and IConfiguration,
  which is why the same service runs unchanged in Kubernetes with the
  variable set by a Secret instead.
```

``` csharp
// service-to-service discovery works the same way, via a scheme prefix
builder.Services.AddHttpClient<IBillingClient, BillingClient>(c =>
    c.BaseAddress = new Uri("https+http://billing-api"));
//                          ^^^^^^^^^^^^ "prefer https, fall back to http"
//                                       ^^^^^^^^^^^ the RESOURCE NAME

// Aspire sets:
//   services__billing-api__https__0=https://localhost:7231
//   services__billing-api__http__0=http://localhost:5231
// and Microsoft.Extensions.ServiceDiscovery resolves the scheme+name
// against those. In Kubernetes the same package resolves via DNS instead.
```

| Symptom                                         | Cause                                                                                    |
|-------------------------------------------------|------------------------------------------------------------------------------------------|
| `Connection string 'x' not found`               | The resource name in the AppHost does not match the string in `Add...`                   |
| The service starts before its database is ready | Missing `.WaitFor(db)`                                                                   |
| Works in the AppHost, fails when run alone      | The environment variables only exist under the AppHost — supply them another way         |
| `https+http://` not resolving                   | The service-discovery package is not registered — usually a missing ServiceDefaults call |
| A different port every run                      | Expected. Pin with `.WithHostPort()` if something external needs it                      |

<a id="be-a177"></a>

#### Service defaults: the part you would have written anyway

`Aspire.ServiceDefaults` is a shared project generated into your solution, not a NuGet black box. You can and should read it and modify it — it is your standard, not Microsoft's.

``` csharp
public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    builder.ConfigureOpenTelemetry();          // traces, metrics, logs -> OTLP
    builder.AddDefaultHealthChecks();          // /health and /alive
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();   // retry, breaker, timeout, hedging
        http.AddServiceDiscovery();
    });
    return builder;
}

// every service: one line in Program.cs
builder.AddServiceDefaults();
app.MapDefaultEndpoints();
```

The value is uniformity. Twelve teams otherwise produce twelve different opinions about retry counts, health endpoint paths and trace attribute names, and your dashboards end up unable to compare services. This is a governance tool disguised as a helper method.

> **\[Note\]**
>
> **Useful even without the AppHost.** You can add the service defaults project to an existing solution and get consistent telemetry and resilience without adopting the app model at all. It is the cheapest possible first step into Aspire.

###### What is in it, and why you should own it

``` csharp
// ServiceDefaults is a project template, not a package. It is YOUR code,
// generated once, and you are expected to edit it.
public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder b)
{
    b.ConfigureOpenTelemetry();       // tracing + metrics + logging, exporting OTLP
    b.AddDefaultHealthChecks();       // /health/live and /health/ready wiring
    b.Services.AddServiceDiscovery(); // resolves https+http:// names

    b.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();   // retry, circuit breaker, timeouts
        http.AddServiceDiscovery();
    });
    return b;
}

// and in each service, one line:
builder.AddServiceDefaults();
```

| It gives you                                         | What you would otherwise write                         |
|------------------------------------------------------|--------------------------------------------------------|
| OpenTelemetry tracing, metrics, logging              | ~40 lines of instrumentation registration per service  |
| ASP.NET Core, HttpClient and runtime instrumentation | Remembering each package and its `Add*Instrumentation` |
| Health check endpoints, correctly split              | Liveness vs readiness, and the tag filtering           |
| Standard resilience on every `HttpClient`            | A Polly pipeline, per client, consistently             |
| Service discovery resolution                         | Configuration binding and a resolver                   |

> **\[Note\]**
>
> **Treat it as your platform library.** Because it is a template rather than a package, it is the natural place for *your* conventions too — a correlation-id middleware, your standard `ProblemDetails` handler, your logging enrichment, your authentication defaults. Many teams keep ServiceDefaults after deciding against the rest of Aspire, and that is a perfectly coherent outcome: it was always the highest-value part.

<a id="be-a178"></a>

#### The dashboard

Every service's console output, structured logs, distributed traces, metrics and environment in one place, correlated. In practice this is what converts people — the first time you click a slow request and see the span tree cross four services into a database query, the local debugging experience is transformed.

| View            | Shows                                                   | Use it to                                       |
|-----------------|---------------------------------------------------------|-------------------------------------------------|
| Resources       | Every service and container, state, endpoints, env vars | Confirm what config a service actually received |
| Console logs    | Raw stdout per resource                                 | Startup failures and container errors           |
| Structured logs | Filterable by property, level and resource              | Find one order's journey across services        |
| Traces          | Span waterfall across services and dependencies         | See where the 800 ms actually went              |
| Metrics         | Live counters and histograms per service                | Watch the effect of a change in real time       |

The dashboard is an OTLP receiver, which means it works with anything that speaks OpenTelemetry — including non-.NET services and, with a little configuration, a legacy app you have instrumented. It can also be run as a standalone container pointed at a shared environment.

###### What it shows, and using it without Aspire

| View            | Answers                                                              |
|-----------------|----------------------------------------------------------------------|
| Resources       | What is running, its state, its endpoints, its environment variables |
| Console logs    | Raw stdout per resource — including containers                       |
| Structured logs | Filterable by level, by resource, by any log property                |
| **Traces**      | One request across every service, with a span waterfall              |
| Metrics         | Every OpenTelemetry meter, including your own                        |

The traces view is the one that changes how you work. A request crossing three services and a database shows as a single waterfall, with the width of each span visible — so “why is this slow?” is answered by looking rather than by adding logging and redeploying.

``` bash
# the dashboard is a STANDALONE CONTAINER that accepts any OTLP traffic.
# you can use it with no Aspire, no AppHost, and no .NET at all.
docker run --rm -it \
  -p 18888:18888 -p 4317:18889 \
  -e DASHBOARD__FRONTEND__AUTHMODE=Unsecured \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest

# then point ANY OpenTelemetry exporter at localhost:4317
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# this is genuinely the fastest way to get a local trace view for a Python,
# Node or Java service. Worth knowing independently of .NET.
```

> **\[Trap\]**
>
> **Common trap.** The dashboard is a **development** tool. It holds telemetry in memory, has a bounded retention, and its default unsecured mode grants anyone who reaches it full visibility of your environment variables — which include connection strings and secrets. Never expose it outside a development machine without authentication, and do not treat it as your production observability platform.

<a id="be-a179"></a>

#### Aspire with relational databases

The database story is where Aspire saves the most setup: a real containerised engine per developer, seeded, with migrations applied, and no shared dev database that everyone breaks.

``` csharp
// AppHost
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume()
                 .WithLifetime(ContainerLifetime.Persistent);   // survives F5 cycles

var db = sql.AddDatabase("ordersdb");

// a one-shot migration project, run before the API starts
var migrator = builder.AddProject<Projects.Orders_MigrationService>("migrator")
                      .WithReference(db).WaitFor(db);

builder.AddProject<Projects.Orders_Api>("orders-api")
       .WithReference(db)
       .WaitForCompletion(migrator);      // API starts only after migrations succeed
```

``` csharp
// Orders.MigrationService - a BackgroundService that migrates then exits
public class Worker(IServiceProvider sp, IHostApplicationLifetime life) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDb>();
        await db.Database.MigrateAsync(ct);
        await Seed.RunAsync(db, ct);
        life.StopApplication();
    }
}
```

This pattern is worth copying even outside Aspire: migrations as a separate, single-instance process that must complete before the application starts is exactly the shape you want as a Kubernetes init container or pipeline stage. Aspire just makes you build it early, on your laptop, where it is cheap to get right.

###### Container lifecycle, persistence and migrations

``` csharp
var pg = builder.AddPostgres("pg")
    .WithDataVolume("orders-pgdata")       // survives AppHost restarts
    .WithPgAdmin()                         // a management UI container, free
    .WithLifetime(ContainerLifetime.Persistent);  // survives between DEBUG SESSIONS

var db = pg.AddDatabase("appdb");

// migrations: a dedicated one-shot project, not on service startup
var migrator = builder.AddProject<Projects.Orders_MigrationService>("migrator")
    .WithReference(db).WaitFor(db);

builder.AddProject<Projects.Orders_Api>("api")
    .WithReference(db)
    .WaitForCompletion(migrator);          // the API waits until migration finishes
```

| Choice                     | Effect                                                                     |
|----------------------------|----------------------------------------------------------------------------|
| No volume                  | A clean database every run — good for tests, painful for development       |
| `WithDataVolume()`         | Data persists; you keep your seeded state                                  |
| `WithLifetime(Persistent)` | The container is not stopped when the AppHost stops — much faster restarts |
| `WithBindMount()`          | Data in a host folder you can inspect or reset by hand                     |
| `AddConnectionString()`    | Point at an *existing* database instead of a container                     |

> **\[Trap\]**
>
> **Common trap.** Running migrations on service startup is convenient in development and a genuine hazard in production: with three replicas, three instances race to migrate, most migration tools take an advisory lock, and two block for the whole duration and then fail their readiness probe — turning a deploy into an outage. Use a separate migration project, exactly as above, and gate the rollout on its completion. The Aspire shape here happens to be the correct production shape too.

<a id="be-a180"></a>

#### Aspire with NoSQL, caches and brokers

``` csharp
// document store
var mongo   = builder.AddMongoDB("mongo").WithMongoExpress().AddDatabase("catalog");

// Cosmos DB against the local emulator, real service when deployed
var cosmos  = builder.AddAzureCosmosDB("cosmos")
                     .RunAsEmulator(e => e.WithDataVolume());
var cosmosDb = cosmos.AddCosmosDatabase("orders").AddContainer("events", "/tenantId");

// cache with a GUI, and a vector store for embeddings
var cache   = builder.AddRedis("cache").WithRedisInsight();
var vectors = builder.AddQdrant("qdrant").WithDataVolume();

// messaging
var kafka   = builder.AddKafka("kafka").WithKafkaUI();

builder.AddProject<Projects.Catalog_Api>("catalog")
       .WithReference(mongo).WithReference(cache)
       .WithReference(vectors).WithReference(kafka)
       .WaitFor(mongo).WaitFor(kafka);
```

`RunAsEmulator()` versus `AsExisting()` is the important distinction for cloud resources: run a local emulator during development, and bind to the real provisioned resource when the manifest is published. The service code and its configuration key are identical in both cases.

| Local development need                | Aspire answer                                               |
|---------------------------------------|-------------------------------------------------------------|
| A clean database per feature branch   | Drop the volume; the container is recreated on next run     |
| Inspect what is in Redis or Mongo     | `WithRedisInsight()`, `WithMongoExpress()`                  |
| Test a Kafka consumer group rebalance | `WithReplicas(3)` on the consumer project                   |
| Reproduce a Cosmos partition-key bug  | Emulator with the same container and key path as production |

###### Emulators, and where they differ from the real thing

``` csharp
var cache  = builder.AddRedis("cache").WithRedisInsight();
var mongo  = builder.AddMongoDB("mongo").AddDatabase("catalog");
var mq     = builder.AddRabbitMQ("mq").WithManagementPlugin();
var kafka  = builder.AddKafka("kafka").WithKafkaUI();
var blobs  = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs("files");
var cosmos = builder.AddAzureCosmosDB("cosmos").RunAsEmulator().AddDatabase("db");
//                                              ^^^^^^^^^^^^^^ local container in
//                                              development, the real service when
//                                              deployed - one line, both modes
```

| Emulator                         | Fidelity      | Watch for                                                                        |
|----------------------------------|---------------|----------------------------------------------------------------------------------|
| Redis (real container)           | **Identical** | Nothing — it is Redis                                                            |
| RabbitMQ, Kafka, Mongo, Postgres | **Identical** | Nothing — real software in a container                                           |
| Azurite (Storage)                | High          | Some newer APIs and a few auth modes are unimplemented                           |
| Cosmos DB emulator               | Moderate      | **RU limits, indexing and consistency levels behave differently.** Slow to start |
| Azure Service Bus emulator       | Moderate      | Sessions, scheduling and DLQ semantics can differ                                |
| No emulator (Key Vault, Entra)   | —             | Use a real dev instance, or abstract behind an interface                         |

The distinction that matters: for Redis, RabbitMQ, Kafka, Mongo and Postgres, Aspire runs the *actual software* in a container, so local behaviour is production behaviour. For cloud-proprietary services it runs an *emulator*, and emulator fidelity is genuinely variable — Cosmos in particular behaves differently enough on throughput and consistency that anything performance-sensitive must be verified against the real service.

<a id="be-a182"></a>

#### Deploying an Aspire application

Aspire's output for deployment is a *manifest*: a JSON description of every resource, its image or project, its environment variables and its bindings. Tools consume that manifest to produce real infrastructure.

``` bash
dotnet run --project AppHost -- --publisher manifest --output-path manifest.json

# Azure Developer CLI: provisions Container Apps, databases, Key Vault, monitoring
azd init
azd up

# Aspir8: generate Kubernetes manifests instead
aspirate generate --output-format kustomize
```

| Target                  | Tool                                             | Notes                                                |
|-------------------------|--------------------------------------------------|------------------------------------------------------|
| Azure Container Apps    | `azd up`                                         | The best-supported path; provisions dependencies too |
| Kubernetes              | Aspir8, or hand-written charts from the manifest | You keep control of Helm, HPAs, network policy       |
| Docker Compose          | Compose publisher                                | Small deployments and demo environments              |
| Existing infrastructure | Read the manifest, feed your own IaC             | Perfectly reasonable — the manifest is just data     |

> **\[Note\]**
>
> **The AppHost does not ship.** It is a development and composition tool. In production your services run as ordinary containers with configuration supplied by the platform. If a service cannot start without the AppHost, something has gone wrong — that dependency should never exist.

> **\[Trap\]**
>
> **Common trap.** Do not treat the generated infrastructure as the final word. Production needs network policy, backup policy, private endpoints, resource limits and cost controls that no generator will infer for you. Generate once, then own the IaC.

###### What the manifest contains, and why that matters

``` json
{
  "resources": {
    "sql": { "type": "container.v0", "image": "mcr.microsoft.com/mssql/server:2022-latest",
             "env": { "SA_PASSWORD": "{sql-password.value}" },
             "bindings": { "tcp": { "scheme": "tcp", "targetPort": 1433 } } },

    "ordersdb": { "type": "value.v0",
                  "connectionString": "Server={sql.bindings.tcp.host},{sql.bindings.tcp.port};..." },

    "orders-api": {
      "type": "project.v0",
      "path": "../Orders.Api/Orders.Api.csproj",
      "env": {
        "ConnectionStrings__ordersdb": "{ordersdb.connectionString}",
        "services__catalog-api__https__0": "{catalog-api.bindings.https.url}"
      },
      "bindings": { "http":  { "scheme": "http",  "protocol": "tcp", "transport": "http" },
                    "https": { "scheme": "https", "protocol": "tcp", "transport": "http" } }
    }
  }
}
```

The important property is that this is *declarative data*, not a deployment. It tells you every service, every dependency edge, every environment variable and every port — which is exactly the information your Terraform, Helm charts or Bicep needs, and exactly the information that is otherwise scattered across a dozen YAML files maintained by hand and drifting apart.

1.  **Generate the manifest in CI** on every build and commit it as an artifact. It becomes a reviewable diff of your topology.
2.  **Diff it against the previous build** and fail the pipeline on unexpected changes — a new outbound dependency should be a conscious decision, not a surprise.
3.  **Feed it to whichever generator you use**, or write a small transformer to your own chart format. It is JSON; this is not hard.
4.  **Keep production concerns in your IaC**, not in the AppHost: replica counts, autoscaling, network policy, backup schedules, private endpoints and resource limits are not development concerns and should not be inferred.

> **\[Trap\]**
>
> **Common trap.** Do not let the AppHost become a second source of truth for production configuration. The moment someone adds `if (IsPublishMode) replicas = 12`, you have two places that describe production and they will diverge. The AppHost describes *what* the system is made of; the IaC describes *how much* of it to run and under what policy.

<a id="be-a183"></a>

#### When to adopt Aspire, and when not to

**Adopt it when**

- More than two or three processes must run together
- New developers take days to get an environment working
- Telemetry is inconsistent across services
- You are starting a greenfield distributed system
- You want service discovery without inventing your own

**Hold off when**

- One API and one database — Compose is simpler
- The team is deep in an established Kubernetes-native workflow that already works
- Your services are mostly not .NET (it works, but you get less of the benefit)
- Nobody has capacity to own another moving part

##### A low-risk adoption path

1.  Add only the `ServiceDefaults` project and call it from each service. Consistent telemetry, no other change.
2.  Add an AppHost that references the services you already have. Nothing about them changes.
3.  Replace your Compose file's databases and broker with Aspire resources; delete the Compose file.
4.  Turn on `WithReplicas(2)` and fix everything it breaks — those were latent production bugs.
5.  Add one Aspire integration test for your most important cross-service flow.
6.  Only then consider using the manifest for deployment; keep your existing pipeline until it is clearly worse.

###### An honest assessment

**Adopt it when**

- Several services plus containers to run locally
- New development, or a service being extracted
- The team keeps rewriting the same telemetry wiring
- Onboarding is currently a day of environment setup
- You want one local trace view without buying one

**Do not when**

- One service and one database — compose is fine
- A .NET Framework estate — it does not apply
- Your deployment is already solved and you would not use the manifests
- The team has no container runtime on their machines
- You would adopt it for deployment rather than for development

``` text
  the adoption path that carries the least risk, in order

  1  ServiceDefaults only
       copy the template into your solution. Take the OpenTelemetry,
       health checks and resilience defaults. NO AppHost.
       -> most of the value, essentially no commitment.

  2  the DASHBOARD standalone
       run the container, point OTLP at it. Local traces, no Aspire.

  3  an AppHost for LOCAL DEVELOPMENT
       replace docker-compose. Keep deploying exactly as you do now.
       -> Aspire is now a dev tool, and removing it is deleting a project.

  4  Aspire-generated deployment manifests
       the largest commitment, and the one to be most cautious about:
       your deployment now depends on Aspire's model of your system.

  most teams should stop at 3. Steps 1 and 2 are close to free.
```

The strategic caveat worth stating plainly: steps 1 through 3 leave you with ordinary .NET projects reading ordinary configuration, so backing out costs a day. Step 4 couples your deployment pipeline to Aspire's manifest model, and that is a decision with a much longer shadow. Adopt the development experience enthusiastically and the deployment story deliberately.
