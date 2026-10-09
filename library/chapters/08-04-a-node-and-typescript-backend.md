---
id: "8-4"
number: "8.4"
title: "A Node and TypeScript backend"
part: 8
part_title: "Building a backend"
words: 4795
minutes: 21
---

# 8.4 A Node and TypeScript backend

> **Quick look.** The same kind of backend built with Node.js, Express and TypeScript, for comparison.
>
> - Node runs JavaScript on V8 with an event loop: great for I/O-bound backends, bad for CPU-heavy work, which blocks everyone.
> - Commit the lockfile, install with npm ci, and keep dependencies few: the supply chain is an attack target.
> - Prefer ES modules.
> - TypeScript types vanish at run time: validate every input with a schema and derive types from it.
> - Express, Fastify and NestJS provide middleware, routing and structure.
> - Handle unhandled promise rejections; keep state out of process memory when running several instances.


### The big picture

A large share of the world's backends run on Node.js, JavaScript outside the browser, usually written in TypeScript. Teams choose it for three reasons. The frontend and backend then share one language, and often one set of types. Its huge package ecosystem has a library for nearly everything. And its event loop, from The event loop (5.4), handles many waiting connections efficiently.

This chapter builds the same kind of backend as A modern .NET backend (8.3), with Express and TypeScript, so the two can be compared. It explains how Node runs code, how packages and lockfiles work and why they are a security concern, and the two module systems. It then covers the most important difference from C#: TypeScript's types disappear when the code runs, so incoming data must be checked at run time with a schema. The example server was run and type-checked for this chapter, and its comments show real output.

### Before you start

**Builds on:** 5.4 The event loop, 8.3 A modern .NET backend, 2.2 Compilers, interpreters and JIT, 2.3 Types, 7.4 Transactions and isolation, 8.1 What a backend does.

**Ideas explained later:**

- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.
- **Single source of truth** (9.4): One owner for each piece of state, from which everything else is derived.
- **Serverless** (11.5): Running code that the platform starts on demand, scales automatically and bills per use.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why can't TypeScript types alone protect a server from bad request bodies?**

> Types exist only at compile time and are removed before the code runs. A request body is whatever the client sent. It must be checked at run time, for example with a schema library such as Zod, which can also produce the TypeScript type.

**2. What is a lockfile, and why commit it?**

> `package-lock.json` records the exact version of every package installed, including indirect ones. Committing it, and installing with `npm ci`, gives every machine and build exactly the same dependencies.

**3. What does ^4.18.0 allow in package.json?**

> Any version from 4.18.0 up to, but not including, 5.0.0: new features and fixes, but no breaking major version, following semantic versioning. `~4.18.0` allows only patch releases, 4.18.x.

**4. What is the difference between CommonJS and ES modules?**

> CommonJS is Node's original module system, with `require()` and `module.exports`. ES modules are the JavaScript standard, with `import` and `export`, used by browsers too. A package's `"type": "module"` setting makes `.js` files ES modules.

**5. How do you run TypeScript on a recent version of Node without compiling it first?**

> Just run `node server.ts`. Since Node 22.18 and 23.6, Node strips the type annotations and runs the rest. It doesn't check the types, and it only supports syntax that can be erased, so no enums or namespaces.

**6. Is flight.booked++ in an Express handler safe from races?**

> Within one Node process, yes: the handler runs to completion on the single JavaScript thread. Across several processes or servers, no. Shared state must then live in a database that makes the check and the change atomic.

**7. What happens by default in current Node versions when a promise is rejected and nobody handles it?**

> The process crashes with an error. Since Node 15, unhandled rejections are treated as fatal, so every promise needs a `catch` or an `await` inside a `try`.

### Node.js

**Node.js**, created by Ryan Dahl in 2009, runs JavaScript outside the browser. It combines Google's **V8** JavaScript engine, the one inside Chrome, which compiles JavaScript to machine code with a JIT as Compilers, interpreters and JIT (2.2) described, with libuv, the event loop and I/O library from The event loop (5.4).

Your JavaScript runs on one thread. Network and file I/O happen in the background and come back as callbacks, promises and `await`. That model is excellent for backends that mostly wait on databases and other services. A single process can hold many thousands of connections. It is weak for CPU-heavy work, which blocks every other request, as that chapter showed. CPU work goes to worker threads or to separate services, and a busy server runs one Node process per core, either with the built-in cluster module or, more often today, as several containers behind a load balancer.

Node has a new major version every six months. Even-numbered versions become long-term support releases, maintained for about three years. Production systems should run an LTS version.

### Packages and npm

**npm**, the Node package manager, installs libraries from the npm registry, the largest package repository of any language. A project's **package.json** lists its name, its dependencies with version ranges, development-only dependencies such as test tools, and scripts such as `npm test` and `npm start`.

Version ranges follow **semantic versioning**: `major.minor.patch`, where a new major version may break things, a minor version adds features compatibly, and a patch only fixes bugs. `^4.18.0` accepts any 4.x from 4.18.0 on; `~4.18.0` accepts only 4.18.x patches.

Ranges mean two installs on different days can produce different trees of packages, including the packages your packages depend on. A **lockfile**, `package-lock.json`, records the exact version of every package installed. Commit it, and install with `npm ci` in builds, which installs exactly what the lockfile says and fails if it doesn't match `package.json`. pnpm and Yarn are popular alternative package managers with their own lockfiles.

A typical Node project pulls in hundreds of packages written by strangers, and each can run code when it is installed. That makes npm a target for a **supply chain attack**: publishing a malicious package, or taking over a popular one, so that it runs in thousands of builds. Real incidents have included typo-named lookalike packages, compromised maintainer accounts, and in 2018 a popular package whose new maintainer added code to steal cryptocurrency wallets. Keep dependencies few, commit lockfiles, review what new packages bring in, run `npm audit` or an equivalent scanner, and treat build machines as sensitive. The same risks apply to NuGet, as Builds and dependencies (11.2) discusses.

### Two module systems

JavaScript grew two ways of splitting code into modules, and Node supports both.

**CommonJS** was Node's original system: `const express = require("express")` and `module.exports = ...`. Modules load synchronously when first required.

**ES modules** are the JavaScript standard, also used by browsers: `import express from "express"` and `export`. They load asynchronously, can be analysed without running, and support top-level `await`.

New code should use ES modules. Setting `"type": "module"` in `package.json` makes `.js` files ES modules. The two systems can use each other's code, with some awkwardness, which is why older packages and older tutorials can be confusing.

### TypeScript on the server

TypeScript adds static types to JavaScript, as Types (2.3) described: structural, checked at compile time and removed before the code runs. Most serious Node backends use it.

There are two ways to run it. The traditional way compiles TypeScript to JavaScript with `tsc`, or a faster tool such as esbuild, and runs the output. The newer way runs TypeScript files directly. Since Node 22.18 and 23.6, `node server.ts` works: Node performs **type stripping**, deleting the type annotations and running what is left. Deno and Bun, two newer JavaScript runtimes, run TypeScript directly too.

Type stripping only works with **erasable syntax**: TypeScript that becomes valid JavaScript when the types are simply removed. Type annotations, interfaces and type aliases are erasable. `enum`, `namespace` and constructor parameter properties are not, because they generate code. Setting `erasableSyntaxOnly` in `tsconfig.json` makes the compiler reject them, and `verbatimModuleSyntax` requires type-only imports to say `import { type Request }`, so the stripped code doesn't try to import something that doesn't exist at run time.

Neither Node nor esbuild checks the types. Run `tsc --noEmit` separately, locally and in the build pipeline, or type errors ship to production, as Compilers, interpreters and JIT (2.2) warned.

### Types vanish at run time

This is the most important difference from C#. In ASP.NET Core, the framework binds a JSON body to a `BookingRequest` record and fails if the shape is wrong. In TypeScript, declaring `const body: BookingRequest = req.body` only tells the compiler to trust you. At run time, `req.body` is whatever the client sent: a missing field, a number where a string belongs, or an object with extra fields you didn't expect.

So every value that crosses a boundary must be checked at run time. Schema libraries such as Zod, Valibot and TypeBox do this: you describe the shape once, as a schema, and the library both validates data against it and gives you the matching TypeScript type with `z.infer`. The schema becomes the single source of truth for the check and the type. Validation and errors at the boundary (8.5) develops this idea for any language.

### Frameworks

Node has a built-in `node:http` module, on which every framework is built. Few people use it directly for applications.

**Express**, from 2010, is the oldest and most widely used. It is small: routing, middleware and helpers for requests and responses. Express 5, released in 2024 after a very long wait, finally catches errors from `async` handlers automatically; Express 4 needed wrappers for that. **Fastify** is faster, validates requests against JSON schemas and is organized around plugins. **NestJS** adds structure on top of Express or Fastify, with modules, dependency injection, decorators and controllers, and feels close to ASP.NET Core. Hono is a very small framework that also runs on edge and serverless platforms.

Express middleware works like ASP.NET Core's: functions that receive the request, the response and a `next` function, called in registration order. Error handling is middleware too, recognized by having four parameters, `(err, req, res, next)`, and registered last.

### Seeing it for real

This is the same kind of API as in A modern .NET backend (8.3): a health check, a flight lookup, a booking endpoint that refuses overbooking and invalid input, consistent error bodies, and an error handler that hides internal details. It calls itself when it starts. It was run with Node 22 and checked with a strict TypeScript configuration for this chapter.

```json
{
  "type": "module",
  "dependencies": { "express": "^5.2.1", "zod": "^4.6.5" },
  "devDependencies": { "@types/express": "^5.0.0", "@types/node": "^22.0.0", "typescript": "^7.0.0" }
}
```

```typescript
// server.ts: npm install, then run with: node server.ts   (Node 22.18 or later runs TypeScript directly)
import express, { type NextFunction, type Request, type Response } from "express";
import { z } from "zod";

type Flight = { number: string; origin: string; destination: string; seats: number; booked: number };
const flights = new Map<string, Flight>([
  ["BA117", { number: "BA117", origin: "LHR", destination: "JFK", seats: 1, booked: 0 }],
]);

// A schema checks data at run time; the TypeScript type is derived from it, so the two can't drift apart
const BookingRequest = z.object({ passenger: z.string().trim().min(1).max(100) });
type BookingRequest = z.infer<typeof BookingRequest>;

const app = express();
app.use(express.json({ limit: "10kb" }));                            // parse JSON bodies, refuse huge ones

app.use((req: Request, res: Response, next: NextFunction) => {       // middleware, as in 8.3: a correlation id
  res.setHeader("X-Correlation-Id", req.header("X-Correlation-Id") ?? crypto.randomUUID().slice(0, 12));
  next();
});

app.get("/health", (_req, res) => { res.json({ status: "healthy" }); });

app.get("/api/flights/:number", (req, res) => {
  const flight = flights.get(req.params.number);
  if (!flight) { res.status(404).type("application/problem+json").json({ title: "Not Found", status: 404 }); return; }
  res.json(flight);
});

app.post("/api/flights/:number/bookings", (req, res) => {
  const flight = flights.get(req.params.number);
  if (!flight) { res.status(404).json({ title: "Not Found", status: 404 }); return; }
  const parsed = BookingRequest.safeParse(req.body);                // until checked, the body is just "unknown"
  if (!parsed.success) {
    const errors = parsed.error.issues.map(i => `${i.path.join(".")}: ${i.message}`);
    res.status(400).json({ title: "Invalid request", status: 400, errors });
    return;
  }
  const request: BookingRequest = parsed.data;                       // from here on, fully typed
  if (flight.booked >= flight.seats) { res.status(409).json({ title: "Flight is full", status: 409 }); return; }
  flight.booked++;                                                    // safe: nothing else runs until this callback ends
  res.status(201).location(`/api/flights/${flight.number}/bookings/${flight.booked}`)
     .json({ seat: flight.booked, passenger: request.passenger });
});

app.get("/api/boom", async () => { throw new Error("something broke"); });   // Express 5 catches rejected promises

app.use((err: Error, _req: Request, res: Response, _next: NextFunction) => {  // error middleware has four parameters
  console.error(`logged on the server: ${err.message}`);
  res.status(500).json({ title: "An error occurred while processing your request.", status: 500 });
});

const server = app.listen(0, "127.0.0.1", async () => {              // port 0: the operating system picks a free port
  const { port } = server.address() as { port: number };
  const call = async (method: string, path: string, body?: unknown) => {
    const response = await fetch(`http://127.0.0.1:${port}${path}`, {
      method,
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    console.log(`${method} ${path}: ${response.status} ${await response.text()}`);
  };
  await call("GET", "/health");
  await call("GET", "/api/flights/BA117");
  await call("POST", "/api/flights/BA117/bookings", { passenger: "Ada" });
  await call("POST", "/api/flights/BA117/bookings", { passenger: "Grace" });
  await call("POST", "/api/flights/BA117/bookings", { passenger: "   " });
  await call("POST", "/api/flights/BA117/bookings", { name: "Linus" });
  await call("GET", "/api/flights/XX999");
  await call("GET", "/api/boom");
  server.close();
});
// GET /health: 200 {"status":"healthy"}
// GET /api/flights/BA117: 200 {"number":"BA117","origin":"LHR","destination":"JFK","seats":1,"booked":0}
// POST /api/flights/BA117/bookings: 201 {"seat":1,"passenger":"Ada"}
// POST /api/flights/BA117/bookings: 409 {"title":"Flight is full","status":409}
// POST /api/flights/BA117/bookings: 400 {... "errors":["passenger: Too small: expected string to have >=1 characters"]}
// POST /api/flights/BA117/bookings: 400 {... "errors":["passenger: Invalid input: expected string, received undefined"]}
// GET /api/flights/XX999: 404 {"title":"Not Found","status":404}
// logged on the server: something broke
// GET /api/boom: 500 {"title":"An error occurred while processing your request.","status":500}
```

Notice two lines. The `"   "` passenger is rejected because the schema trims it first and then requires at least one character. And `flight.booked++` is safe here only because one Node process owns the data. With several processes or servers, the count must live in a database with an atomic update, as Transactions and isolation (7.4) showed.

### Concurrency in Node

One JavaScript thread per process means no data races, as The event loop (5.4) explained, but not no races. Code that reads a value, awaits something, and then acts on the value can be overtaken by another request during the await. And as soon as there are two instances, in-memory state isn't shared at all.

Two failure modes need constant attention. A CPU-heavy task in a handler, such as a large report, image resizing or an expensive regular expression, stalls every other request on that process; watch the event loop lag and move such work to workers. And a rejected promise that nothing handles now crashes the process by default, since Node 15. Await every promise, or attach a `catch`, and add process-level handlers only for logging before exiting.

### Running it in production

Much of What a backend does (8.1) applies unchanged. Read configuration from environment variables through `process.env`, validated with a schema at start-up. Log structured JSON to standard output, with a library such as pino. Expose health endpoints. Handle SIGTERM by calling `server.close()`, which stops accepting new connections and lets in-flight requests finish, then exit. Run as containers, one Node process each, with as many replicas as needed, rather than managing processes by hand.

### Node or .NET?

Both are excellent backend platforms, and the choice usually rests on the team and the system rather than on raw capability.

For I/O-bound APIs, both handle high load. For CPU-heavy work, ASP.NET Core is typically much faster, because C# is statically compiled by the JIT with value types and real threads across all cores, where each Node process runs JavaScript on one core. C#'s type system is checked by the runtime too: a `BookingRequest` really is a `BookingRequest`. TypeScript's types are advice to the compiler that vanishes at run time, so validation discipline matters more. Node shares a language and types with web frontends, starts quickly, and has the largest ecosystem, with the supply-chain care that implies.

A common arrangement uses both: .NET for core business services, and a small TypeScript **backend for frontend** (BFF) owned by the web team, which shapes data for its own screens and calls the core services.

### In short

Node.js runs JavaScript on V8 with an event loop: excellent for I/O-bound backends, poor for CPU-heavy work, which blocks everyone. npm installs packages by semantic version ranges; commit the lockfile, install with `npm ci`, and keep dependencies few, because the supply chain is an attack target. Prefer ES modules. TypeScript can run directly on recent Node through type stripping, with erasable syntax only, but types are never checked at run time, so validate every input with a schema and derive the types from it. Express, Fastify and NestJS provide middleware, routing and structure, and production habits match any backend.

### What goes wrong

**Trusting TypeScript types at the boundary.** A body typed as `BookingRequest` arrives with the wrong shape and the code fails deep inside, or stores garbage. Validate with a schema before using any input.

**No type checking in the pipeline.** Code runs through type stripping or esbuild, and type errors reach production. Run `tsc --noEmit` in every build.

**Floating dependencies.** Builds without a committed lockfile pick up new, possibly broken or malicious, versions. Commit `package-lock.json` and use `npm ci`.

**Too many dependencies.** A trivial helper pulls in dozens of transitive packages, each a security and maintenance risk. Prefer the standard library and well-maintained packages.

**Blocking the event loop.** Synchronous file calls or heavy computation in handlers stall every request. Use async APIs and move CPU work to workers.

**Unhandled rejections.** A forgotten `await` or missing `catch` crashes the whole server. Await everything and lint for floating promises.

**In-memory state with several instances.** Counts and sessions kept in a `Map` diverge across processes. Keep shared state in a database or cache.

### Cost model

A Node process starts in tens to hundreds of milliseconds and uses tens of megabytes of memory at rest. A simple Express handler costs tens of microseconds; Fastify is often a few times faster than Express for the framework's own overhead. Each process uses at most about one core for JavaScript, so throughput scales by adding processes. CPU-bound work is typically several times slower than equivalent C#. JSON parsing is fast, but a single large payload is parsed on the event loop thread and blocks it, so limit body sizes.

### In other languages (optional)

**Deno and Bun.** Alternative JavaScript runtimes that run TypeScript directly, include built-in tooling such as test runners and formatters, and support most npm packages. Deno also restricts file and network access unless permissions are granted.

**C#.** ASP.NET Core's model binding and validation give run-time checks that match the declared types, as A modern .NET backend (8.3) showed.

**Python.** FastAPI and Pydantic take the same approach as Zod: types declared once and checked at run time.

**Serverless.** Node is the most common language for functions on AWS Lambda, Azure Functions and edge platforms, because it starts quickly.

### Key terms

- **Node.js**: A runtime that runs JavaScript outside the browser, combining the V8 engine with an event loop.
- **V8**: Google's JavaScript engine, used by Chrome and Node.js.
- **npm**: Node's package manager, and the registry of packages it installs from.
- **package.json**: A Node project's manifest: its name, dependencies with version ranges, and scripts.
- **Semantic versioning**: Numbering versions major.minor.patch, where only a new major version may break compatibility.
- **Lockfile**: A file recording the exact version of every installed package, for reproducible installs.
- **Supply chain attack**: An attack that gets malicious code into your build through a dependency or build tool.
- **CommonJS**: Node's original module system, with require and module.exports.
- **ES module**: A module using the standard JavaScript import and export syntax.
- **Type stripping**: Running TypeScript by deleting its type annotations, without checking them.
- **Erasable syntax**: TypeScript syntax that leaves valid JavaScript when the types are removed.
- **Express**: The oldest and most widely used Node.js web framework, built around middleware.
- **Fastify**: A fast Node.js web framework organized around plugins and JSON schemas.
- **NestJS**: A structured Node.js framework with modules, dependency injection and controllers.
- **Unhandled rejection**: A rejected promise that no code handles. It crashes current versions of Node by default.
- **Backend for frontend**: A small backend owned by a frontend team that shapes data for its own screens. Often shortened to BFF.

### Where this leads

Validation, error contracts and parsing untrusted input in any language are in Validation and errors at the boundary (8.5). TypeScript's type system in depth is in TypeScript (9.3), and modern JavaScript in Modern JavaScript (9.2). Dependencies, lockfiles and builds across languages are in Builds and dependencies (11.2).

### Check yourself

1. Add a `seats` field to the booking schema in `server.ts` that must be an integer from 1 to 9, and show the error for `seats: 0`.
2. Add a route that blocks the event loop for two seconds with a busy loop, call it and `/health` at the same time, and measure how long the health check takes.
3. Remove the `type` keyword from the `Request` import and run the file. Explain the error.
4. Run `npm ls --all` in the example project and count the packages installed. Then run `npm audit`.
5. Rewrite the booking endpoint so the seat count is decremented with a single atomic update in a SQLite or PostgreSQL database instead of a `Map`.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: TypeScript types that lied about a request

An Express handler types `req.body as CreateOrder`, but a client sends `quantity: "5"` and stock math breaks.

*Validate with a schema, derive the type*

```typescript
import { z } from "zod";

const CreateOrder = z.object({
  productId: z.string().uuid(),
  quantity: z.number().int().min(1).max(20),
});
type CreateOrder = z.infer<typeof CreateOrder>;

app.post("/orders", (req, res) => {
  const parsed = CreateOrder.safeParse(req.body);
  if (!parsed.success) return res.status(400).json({ errors: parsed.error.flatten() });
  createOrder(parsed.data);           // typed and actually checked
});
```

> **Lesson:** TypeScript types vanish at runtime. Validate every input with a schema and derive the type from it.

#### Example 2: A dependency update that broke production

A build pulled a new minor version of a transitive package that changed behaviour; nobody had changed code.

*Reproducible installs*

```bash
# commit package-lock.json, and in CI/Docker install exactly what it says:
npm ci --ignore-scripts
npm audit --omit=dev --audit-level=high
# update deliberately via Renovate/Dependabot pull requests, reviewed like code
```

> **Lesson:** Commit the lockfile and install with npm ci, so builds don't change underneath you.

### Two stacks up close

<p class="src">From <em>Backend Engineering</em> · Backend Development: A Working Guide · Two stacks up close</p>

<a id="be-z54"></a>

#### Node and TypeScript up close

A single-threaded event loop with types that vanish at compile time. Those two facts explain almost everything about how this stack feels, and they are the inverse of the previous section.

``` text
WHAT THE PLATFORM GIVES YOU
  no shared-state bugs in your own code — nothing runs concurrently,
    so races between your handlers are impossible ((see: State is the thing that makes it hard))
  excellent I/O concurrency at low memory: thousands of in-flight
    requests on a handful of threads
  one language across front and back, and one set of types if you
    generate them from a schema
  the largest library ecosystem in existence
  fast startup, which suits serverless and short-lived processes
```

``` text
WHAT IT LEAVES TO YOU, AND WHAT BITES

  1. CPU WORK BLOCKS EVERYTHING. Not this request — every request in
     the process. A synchronous JSON parse of a large payload, a
     bcrypt round, an image resize, a big sort. Move it to a worker
     thread or another process ((see: The four concurrency models)).

  2. TYPES DO NOT EXIST AT RUNTIME. A TypeScript interface guarantees
     nothing about what arrived. Every boundary needs a runtime schema
     — Zod, Valibot, TypeBox — or an `any` in disguise reaches your
     domain ((see: Binding: raw input to typed arguments)).

  3. UNHANDLED REJECTIONS. A promise without a catch can terminate the
     process. Any fire-and-forget call is this bug plus lost work
     ((see: Work that outlives the request)).

  4. UNBOUNDED FAN-OUT. Promise.all over a collection you do not
     control is one of the few changes that can take down a shared
     dependency for other teams ((see: Async, and the mistake every stack shares)).

  5. NO FRAMEWORK OPINION. Express decides almost nothing, so your
     team supplies structure, DI, error handling, and validation
     conventions. Freedom, and a tax paid on every new service
     ((see: Where the logic lives)).

  6. DATE HANDLING IS WEAK. The built-in Date has no time zone support
     beyond the host's. Use Temporal where available, or date-fns-tz /
     Luxon, and never the host's local zone ((see: Time: three clocks, none of them trustworthy)).
```

``` csharp
// The five habits from §39, in TypeScript:

// bind to input types — a RUNTIME schema, because the type is erased
const CreateOrder = z.object({
  customerId: z.string().uuid(),
  lines: z.array(z.object({ sku: z.string().min(3), qty: z.number().int().min(1) }))
          .min(1).max(500),                              // ALWAYS bound it
});
type CreateOrder = z.infer<typeof CreateOrder>;           // one source of truth

// one projected query
const rows = await prisma.order.findMany({
  where: { tenantId },                                   // tenant as PREDICATE
  select: { id: true, total: true, customer: { select: { name: true } } },
});

// intent in the same transaction
await prisma.$transaction([
  prisma.order.create({ data: order }),
  prisma.outbox.create({ data: { type: 'order.placed', payload } }),
]);

// a timeout on every outbound call — fetch has NO default timeout
const res = await fetch(url, { signal: AbortSignal.timeout(5_000) });

// inject the clock
type Clock = { now(): Date };
const systemClock: Clock = { now: () => new Date() };
```

> **\[Why it matters\]** **Where Node is genuinely strong.** I/O fan-out across many services, BFF layers, real-time work, server-side rendering, teams already fluent in TypeScript, and anything where startup time matters.
>
> Where it costs you: CPU-bound work, complex transactional domain models, and the ongoing maintenance of a large dependency tree ([.NET up close](08-03-a-modern-net-backend.md#be-z53)).

> **\[Tip\]** **The comparison worth internalising.** The event loop bought safety by giving up CPU scaling; the thread pool bought CPU scaling by giving up safety. Neither is better, and knowing which trade you are inside tells you which mistakes to watch for.
>
> On Node: keep CPU out of the request path, and validate at every boundary. On .NET: never block on async, and be careful what a singleton holds. Those two lists barely overlap, and each is most of what goes wrong in its stack.
