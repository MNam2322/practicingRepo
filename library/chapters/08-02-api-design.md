---
id: "8-2"
number: "8.2"
title: "API design"
part: 8
part_title: "Building a backend"
words: 16048
minutes: 70
---

# 8.2 API design

> **Quick look.** Designing web APIs that are pleasant to use and safe to evolve, because an API is a promise.
>
> - Organize around resources with plural URLs; let methods and status codes keep their standard meanings.
> - 4xx is the client's fault and shouldn't page anyone; 5xx is yours and should.
> - JSON conventions: UTC ISO times, exact money with a currency, large ids as strings.
> - Page every collection (cursors preferred), cap page size server-side, and make total counts opt-in.
> - Idempotency keys make creation safe to retry; ETags with If-Match stop silent overwrites.
> - Additive changes need no new version; version (in the URL path) only for removals, renames and type changes, and deprecate with a date.
> - Return Problem Details (RFC 9457) for errors; long jobs return 202 with a status URL.
> - Choose gRPC for chatty internal calls, GraphQL when clients need flexible shapes, webhooks for events across organizations.


### The big picture

An API is a promise. Once other teams, partners or mobile apps build against your endpoints, every name, field and status code you chose becomes something they depend on, and changing it can break software you don't control. A mobile app on a customer's phone may keep calling last year's version of your API for years.

This chapter is about designing web APIs that are pleasant to use and safe to evolve. It covers resources and URLs, the JSON format, and what each method and status code should mean. It then covers the things that separate a robust API from a fragile one: paging through large results, retries that can't create duplicates, protection against overwriting someone else's change, and versioning. It also covers documenting the contract with OpenAPI, and the main alternatives to plain HTTP and JSON: gRPC, GraphQL and webhooks.

### Before you start

**Builds on:** 6.6 HTTP, 4.2 Abstraction and interfaces, 1.2 Numbers inside the machine, 7.3 Indexes and query plans, 7.4 Transactions and isolation, 7.5 Schema design and migrations.

**Ideas explained later:**

- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **YAML** (11.4): A text format for structured data that uses indentation, used for pipelines and many configuration files.
- **Authentication** (10.2): Establishing who a caller is.
- **Middleware** (8.3): A component in the request pipeline that handles a request, calls the next component and handles the response.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **CORS** (9.7): Cross-Origin Resource Sharing: headers with which a server lets other origins read its responses.
- **Polling** (9.7): Asking the server for changes at a regular interval.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, records, generic types, lambdas, LINQ, null operators, tuples, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Which of these API changes are breaking: adding an optional response field, renaming a field, adding a required request field, adding a new endpoint?**

> Renaming a field and adding a required request field break existing clients. Adding an optional response field or a new endpoint doesn't, as long as clients ignore fields they don't know.

**2. How can a client safely retry a POST that timed out?**

> Send an idempotency key, a unique value per logical operation, in a header. The server remembers the key and the result, and returns the same result for a repeat instead of doing the work again.

**3. How does an API stop one client from overwriting another's change?**

> It returns an ETag with each resource. The client sends it back in an `If-Match` header when updating. If the resource has changed since, the server refuses with 412 Precondition Failed.

**4. Why is cursor pagination usually better than page numbers for large lists?**

> A cursor marks where the last page ended, so the next page is a quick index seek, and items don't shift between pages when data is added. Page numbers need the database to skip rows, and results move as data changes.

**5. How should money and times be represented in a JSON API?**

> Times as ISO 8601 strings in UTC, such as `2026-09-28T08:25:00Z`. Money as a decimal string or an integer in the smallest unit, always with a currency code, never as a floating-point number.

**6. When is gRPC a better choice than JSON over HTTP?**

> For calls between your own services, where both sides can share the contract file: it is compact, fast, strictly typed and supports streaming. JSON over HTTP is easier for browsers, partners and debugging.

**7. What is OpenAPI?**

> A standard format for describing an HTTP API: its endpoints, parameters, request and response bodies, and status codes. Tools use it to generate documentation, client code and tests.

### What an API is

An **API**, application programming interface, is the set of operations one piece of software offers to others, and the rules for using them. Every library has one. A web API offers its operations over HTTP, as HTTP (6.6) described, to frontends, mobile apps, other services and partners.

An API is a contract, in the sense of Abstraction and interfaces (4.2): callers depend on its shape, its behaviour and its errors. Internal APIs, used only by your own team, can change with coordinated releases. Public and partner APIs can't: once published, they must keep working for clients you can't update. That makes good design at the start unusually valuable.

### Styles

Three broad styles are common.

**REST**, representational state transfer, comes from Roy Fielding's 2000 dissertation describing the architecture of the web. Its core ideas are that everything is a **resource** with its own URL, that the standard HTTP methods mean the same thing everywhere, and that each request stands alone. In practice, "REST API" usually means JSON over HTTP organized around resources, and that is what this chapter mostly covers.

RPC style, for remote procedure call, organizes an API around actions instead: `POST /bookFlight`, `POST /cancelBooking`. It is simple and maps directly to code, but loses the shared meaning of methods and status codes. **gRPC** is Google's modern, efficient version of RPC, covered later in this chapter.

**GraphQL**, also covered later, lets the client write a query saying exactly which fields it wants.

### Resources and URLs

A resource-oriented API names things, not actions. Collections have plural nouns, and each item has its own URL beneath its collection:

```text
GET    /flights                       list flights (filtered, paged)
GET    /flights/BA117-2026-09-28      one flight
POST   /bookings                      create a booking
GET    /bookings/7f3a9c               one booking
PUT    /bookings/7f3a9c/passenger     replace the passenger details
DELETE /bookings/7f3a9c               cancel it
GET    /flights/BA117-2026-09-28/bookings   bookings on one flight
```

Nest URLs only to show ownership, and only one level deep; `/airlines/BA/flights/117/bookings/7f3a9c/seats/12A` is hard to use and to change. Operations that aren't naturally a create, read, replace or delete can become resources too: `POST /bookings/7f3a9c/cancellation` records a cancellation. Treat ids as opaque strings, so you can change how they are generated without breaking anyone.

The methods and status codes from HTTP (6.6) carry the meaning. GET reads, and is safe to repeat. POST creates, and returns 201 Created with a `Location` header pointing at the new resource. PUT replaces and DELETE removes, and both are idempotent. Validation failures return 400 or 422 with details, missing things 404, and conflicts with the current state 409, such as booking a full flight.

### JSON

**JSON**, JavaScript Object Notation, is the usual format for API bodies. It has objects in braces, arrays in square brackets, strings, numbers, `true`, `false` and `null`, and nothing else. That small set needs a few conventions.

Use one naming style everywhere; camelCase is the most common and is what ASP.NET Core produces by default. Write times as ISO 8601 strings in UTC, `"2026-09-28T08:25:00Z"`, or with an explicit offset, never as local times without one, for the reasons in Numbers inside the machine (1.2). Write money as a decimal string, `"450.00"`, or as an integer number of the smallest unit, `45000` pence, always with a currency code; JSON numbers are usually read as floating point. Send large integer ids as strings, because JavaScript clients lose precision above 2⁵³. Write enumerations as readable strings, `"boarding"`, rather than numbers whose meaning lives only in your code.

Decide what `null` and a missing field mean, and stay consistent. A common rule is that responses always include every field, using `null` for no value, and that clients must ignore fields they don't recognize.

### Lists, filters and pages

Any collection that can grow must be paged. Returning every booking in one response works in testing and fails in production.

Page-number paging, with `?page=5&size=20`, is simple and lets users jump to a page. It becomes slow on deep pages and unstable when data changes, because rows shift between pages, as Indexes and query plans (7.3) explained.

**Cursor pagination** returns a **cursor** with each page: an opaque token marking where the page ended. The client asks for the next page with `?after=<cursor>`. The server turns the cursor into a keyset condition that an index can seek to. Pages stay fast and stable, at the cost of not jumping to an arbitrary page. Always cap the page size on the server, whatever the client asks for.

Filtering and sorting usually go in the query string: `GET /flights?origin=LHR&date=2026-09-28&sort=departs`. Allow only the filters and sort orders you have indexes for, rather than arbitrary ones.

### Retries without duplicates

Networks fail in the worst possible way: a request can reach the server, do its work, and have its response lost. The client can't tell whether the booking happened. Retrying a GET or PUT is harmless, because they are idempotent. Retrying a POST can create a second booking and charge the card twice.

An **idempotency key** fixes this. The client generates a unique value for each logical operation, such as a GUID when the user presses "Pay", and sends it in a header, commonly `Idempotency-Key`. The server stores each key with the result of the first request. A repeat with the same key gets the stored result, without doing the work again. Payment providers popularized this, and any API that creates or charges should support it. Store the keys durably, with a unique constraint, and expire them after a sensible period.

### Protecting against lost updates

The lost update from Transactions and isolation (7.4) happens across APIs too. Two agents open the same booking, both edit it, and the second save silently replaces the first.

HTTP has a standard answer. When the server returns a resource, it includes an ETag, from HTTP (6.6), that identifies the version, such as `"3"`. When the client updates, it sends the ETag back in an `If-Match` header. The server compares it with the current version. If they differ, it refuses with 412 Precondition Failed, and the client reloads and decides what to do. It is optimistic concurrency, carried over HTTP.

### Seeing it for real

This program runs a small API with cursor pagination, idempotent creation and ETag checks, then calls it as a client.

```csharp
#:sdk Microsoft.NET.Sdk.Web
// api.cs — run with: dotnet run api.cs
using System.Collections.Concurrent;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5058");
var app = builder.Build();

var flights = Enumerable.Range(1, 5).Select(i => new FlightDto($"BA{100 + i}", "LHR", "JFK")).ToList();   // BA101 to BA105
var bookings = new ConcurrentDictionary<string, BookingDto>();
var seenKeys = new ConcurrentDictionary<string, BookingDto>();
int nextId = 0;

// Cursor pagination: the cursor is the last flight number of the previous page
app.MapGet("/flights", (int? limit, string? after) =>
{
    int take = Math.Clamp(limit ?? 2, 1, 100);                   // always cap the page size
    var page = flights.Where(f => after is null || string.CompareOrdinal(f.Number, after) > 0)
                      .OrderBy(f => f.Number, StringComparer.Ordinal)
                      .Take(take + 1).ToList();                  // one extra row says whether there is more
    string? next = page.Count > take ? page[take - 1].Number : null;
    return Results.Ok(new { items = page.Take(take).Select(f => f.Number), nextCursor = next });
});

// Creation with an idempotency key: a retry gets the original result, not a second booking
app.MapPost("/bookings", (NewBooking request, HttpRequest http) =>
{
    var created = new BookingDto($"B{Interlocked.Increment(ref nextId)}", request.Flight, request.Passenger, Version: 1);
    string? key = http.Headers["Idempotency-Key"];
    var winner = key is null ? created : seenKeys.GetOrAdd(key, created);   // the first request with this key wins
    if (ReferenceEquals(winner, created)) bookings[created.Id] = created;   // only the first one is stored
    return Results.Created($"/bookings/{winner.Id}", winner);
});

// Reads return an ETag; updates must send it back in If-Match
app.MapGet("/bookings/{id}", (string id, HttpResponse response) =>
{
    if (!bookings.TryGetValue(id, out var booking)) return Results.NotFound();
    response.Headers.ETag = $"\"{booking.Version}\"";
    return Results.Ok(booking);
});
app.MapPut("/bookings/{id}/passenger", (string id, NewName body, HttpRequest http) =>
{
    if (!bookings.TryGetValue(id, out var current)) return Results.NotFound();
    if (http.Headers.IfMatch.ToString() != $"\"{current.Version}\"")
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);   // changed since you read it
    var updated = current with { Passenger = body.Passenger, Version = current.Version + 1 };
    bookings[id] = updated;                                      // a real store would compare-and-swap, as in 7.4
    return Results.Ok(updated);
});

await app.StartAsync();
using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5058") };

string? cursor = null;
do
{
    var page = await client.GetFromJsonAsync<PageDto>($"/flights?limit=2{(cursor is null ? "" : $"&after={cursor}")}");
    Console.WriteLine($"page: {string.Join(", ", page!.Items)}  next: {page.NextCursor ?? "none"}");
    cursor = page.NextCursor;
} while (cursor is not null);
// page: BA101, BA102  next: BA102
// page: BA103, BA104  next: BA104
// page: BA105  next: none

for (int attempt = 1; attempt <= 2; attempt++)                   // the same request sent twice, as after a timeout
{
    var post = new HttpRequestMessage(HttpMethod.Post, "/bookings") { Content = JsonContent.Create(new { flight = "BA101", passenger = "Ada" }) };
    post.Headers.Add("Idempotency-Key", "pay-7c1e");
    using var created = await client.SendAsync(post);
    Console.WriteLine($"attempt {attempt}: {(int)created.StatusCode} {created.Headers.Location}");
}
Console.WriteLine($"bookings stored: {bookings.Count}");        // attempt 1: 201 /bookings/B1, attempt 2: 201 /bookings/B1, bookings stored: 1

using var read = await client.GetAsync("/bookings/B1");
string etag = read.Headers.ETag!.Tag;                            // "1"
foreach (string name in new[] { "Ada Lovelace", "A. Lovelace" })  // the second update still sends the old ETag
{
    var put = new HttpRequestMessage(HttpMethod.Put, "/bookings/B1/passenger") { Content = JsonContent.Create(new { passenger = name }) };
    put.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
    using var result = await client.SendAsync(put);
    Console.WriteLine($"update to {name}: {(int)result.StatusCode}");
}
// update to Ada Lovelace: 200
// update to A. Lovelace: 412: the booking changed since that ETag was read
await app.StopAsync();

record FlightDto(string Number, string Origin, string Destination);
record BookingDto(string Id, string Flight, string Passenger, int Version);
record NewBooking(string Flight, string Passenger);
record NewName(string Passenger);
record PageDto(List<string> Items, string? NextCursor);
```

### Evolving without breaking

APIs change. The goal is to change them without breaking the clients already using them.

A **backward-compatible change** keeps every existing client working. Adding a new endpoint, adding an optional request field, and adding a field to a response are compatible, provided clients ignore fields they don't know. That tolerance is sometimes called Postel's law, "be liberal in what you accept", and client code should follow it: never fail on an unknown field.

A **breaking change** makes some existing client fail or misbehave. Removing or renaming a field, changing its type or meaning, adding a required request field, changing an error code, tightening validation, or changing a default all break someone. The expand-contract approach from Schema design and migrations (7.5) applies here too: add the new field alongside the old, move clients over, and remove the old one only when nobody uses it.

When a breaking change is unavoidable, **API versioning** lets old and new behaviour run side by side. The common approaches are a version in the URL, `/v2/bookings`, which is visible and simple; a version in a header or query parameter; or a version in the media type of the `Accept` header. URL versioning is the most common and the easiest to route and debug. Whatever the scheme, announce deprecations early, include a `Deprecation` or `Sunset` header on responses from old versions, and measure who still calls them before turning them off.

### Describing the contract

An API needs a precise, machine-readable description. **OpenAPI**, formerly known as Swagger, is the standard format: a JSON or YAML document listing every endpoint, parameter, request and response body, status code and authentication scheme. From it, tools generate documentation pages, client libraries in many languages, mock servers and tests.

It can be written first, with code generated from it, called contract-first, or generated from the code, called code-first. ASP.NET Core produces OpenAPI documents from minimal APIs and controllers with its built-in `Microsoft.AspNetCore.OpenApi` package. Whichever way, keep the document in source control, review changes to it like code, and check for accidental breaking changes in the pipeline.

### Limits and protection

A public API needs limits. **Rate limiting** caps how many requests a client may make in a period, answering 429 Too Many Requests with a `Retry-After` header when the cap is exceeded. It protects the service from overload, runaway scripts and abuse. ASP.NET Core has rate limiting middleware built in. Limit request body sizes, page sizes and the complexity of filters too. Authentication and authorization are covered in Authentication and authorization (10.2), and browser-specific rules such as CORS in Common web vulnerabilities (10.4).

### Long-running operations

Some operations take longer than a client should wait: generating a manifest, processing a bulk upload. Don't hold the request open for minutes. Accept the work and return 202 Accepted with a `Location` header pointing at a status resource. The client checks that resource, or the server calls the client back when the job is done. Background work and queues (8.7) covers doing the work.

### Other shapes of API

**gRPC**, open-sourced by Google in 2015, defines services and messages in a `.proto` file using **Protocol Buffers**, a compact binary format. Tools generate client and server code in many languages from that file. It runs over HTTP/2, supports streaming in both directions, and is fast and strictly typed. It suits calls between your own services. It is awkward for browsers and harder to inspect by hand than JSON. .NET has excellent gRPC support built in.

**GraphQL**, released by Facebook in 2015, exposes one endpoint and a typed schema. The client sends a query naming exactly the fields and related data it wants, and gets exactly that back. It suits frontends that need many different shapes of data from one backend. The costs are harder HTTP caching, since everything is a POST to one URL, and the risk that one clever query triggers expensive work or an N+1 pattern on the server, as ORMs and EF Core (7.7) described. Query cost limits and batching are essential.

A **webhook** reverses the direction: when something happens, your server sends an HTTP request to a URL the client registered, such as "booking cancelled". It avoids clients polling for changes. Webhooks need retries, signatures so receivers can verify the sender, and receivers that handle duplicates.

For continuous updates to browsers, such as live departure boards, WebSockets and server-sent events keep a connection open, as Talking to the backend (9.7) covers.

### In short

An API is a contract that is hard to change once published. Organize it around resources with plural collection URLs, and let methods and status codes carry their standard meanings. Use JSON conventions consistently: UTC ISO times, exact money with a currency, large ids as strings. Page every collection, preferably with cursors, and cap page sizes. Make creation safe to retry with idempotency keys, and protect updates with ETags and `If-Match`. Evolve compatibly, with tolerant clients, expand-contract and versioning when needed. Describe the contract with OpenAPI, and choose gRPC, GraphQL or webhooks where they fit better.

### What goes wrong

**Unbounded lists.** An endpoint returns every booking, and one day that is two million rows. Page every collection and cap page sizes.

**Duplicate creations on retry.** A client retries a timed-out POST and a customer is charged twice. Support idempotency keys for anything that creates or charges.

**Silent overwrites.** Two clients update the same resource and one change vanishes. Use ETags with `If-Match`, and return 412 on stale updates.

**Accidental breaking changes.** A field is renamed "for consistency" and a partner's integration breaks overnight. Diff the OpenAPI document in the pipeline and treat breaking changes as a versioning decision.

**Clients that break on new fields.** Strict deserializers reject responses with unknown fields, so every addition becomes a breaking change. Configure clients to ignore unknown fields.

**Ambiguous money and time.** Fares as JSON floats and times without offsets cause rounding errors and time zone bugs across every client. Fix the conventions on day one.

**200 for everything.** Errors returned with status 200 and a message in the body break retries, caches and monitoring. Use real status codes and Problem Details.

### Cost model

Every API call costs at least one round trip, so the number of calls a client needs for one screen matters more than the size of each. An API that needs ten calls to show a booking is slow on mobile networks; one designed around the screen's needs makes one or two. JSON parsing and serialization typically cost microseconds per kilobyte, and compression cuts large bodies several times over. gRPC's binary format is several times smaller and faster to process than JSON, which matters at high call rates between services. Idempotency and ETag checks cost one lookup each, cheap insurance against duplicates and lost updates.

### In other languages (optional)

**JavaScript and TypeScript.** OpenAPI documents generate typed clients with tools such as openapi-typescript, and tRPC shares types between a TypeScript server and client without a separate contract. Browsers call APIs with `fetch`.

**Python.** FastAPI generates OpenAPI documents from type hints automatically.

**Java.** Spring generates OpenAPI with springdoc; gRPC and GraphQL are well supported.

**Every language.** OpenAPI and Protocol Buffer files are language-neutral, which is their point: one contract, generated clients in each consumer's language.

### Key terms

- **API**: Application programming interface: the operations software offers to other software, and the rules for using them.
- **REST**: An architectural style for APIs built around resources with URLs and the standard HTTP methods.
- **Resource**: A thing an API exposes at its own URL, such as a flight or a booking.
- **JSON**: JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Cursor**: An opaque token marking where a page of results ended.
- **Cursor pagination**: Paging by passing back the cursor of the previous page, rather than a page number.
- **Idempotency key**: A unique value per logical operation that lets a server recognize and safely answer a repeated request.
- **Backward-compatible change**: A change to an API that keeps every existing client working.
- **Breaking change**: A change to an API that makes some existing clients fail or misbehave.
- **API versioning**: Running several versions of an API side by side so breaking changes don't affect existing clients.
- **OpenAPI**: The standard machine-readable format for describing an HTTP API.
- **Rate limiting**: Capping how many requests a client may make in a period.
- **gRPC**: A high-performance RPC framework using Protocol Buffers over HTTP/2.
- **Protocol Buffers**: A compact binary format for structured data, defined by .proto schema files.
- **GraphQL**: An API style where clients query a typed schema for exactly the fields they want.
- **Webhook**: An HTTP request a server sends to a client-registered URL when an event happens.

### Where this leads

Building these endpoints properly in ASP.NET Core is in A modern .NET backend (8.3), and in Node.js in A Node and TypeScript backend (8.4). Validating requests and returning consistent errors is in Validation and errors at the boundary (8.5). Securing APIs is in Authentication and authorization (10.2), and calling them from a frontend is in Talking to the backend (9.7).

### Check yourself

1. Add filtering by origin to `/flights` in `api.cs`, and make sure the cursor still works when a filter is applied.
2. Send two POSTs with the same idempotency key at the same moment with `Task.WhenAll`, and confirm only one booking is stored.
3. Make `/bookings/{id}/passenger` return 428 Precondition Required when the client sends no `If-Match` header at all.
4. Design the URLs, methods and status codes for seat selection: listing a flight's seat map, holding a seat for ten minutes, confirming it and releasing it.
5. Take an API you know and list three changes someone might want to make. Classify each as compatible or breaking, and plan how to make the breaking one safely.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Paging that skipped and repeated rows

A partner exports bookings with `?page=N`. New bookings arriving during the export cause duplicates and gaps.

*Cursor pagination*

```csharp
app.MapGet("/bookings", async (string? after, int limit, BookingDb db, CancellationToken ct) =>
{
    limit = Math.Clamp(limit, 1, 200);
    var cursor = Cursor.Decode(after);                     // (CreatedAt, Id) of the last row seen
    var q = db.Bookings.AsNoTracking();
    if (cursor is not null)
        q = q.Where(b => b.CreatedAt > cursor.CreatedAt || (b.CreatedAt == cursor.CreatedAt && b.Id > cursor.Id));
    var rows = await q.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id).Take(limit + 1).ToListAsync(ct);
    var next = rows.Count > limit ? Cursor.Encode(rows[limit - 1]) : null;
    return Results.Ok(new { items = rows.Take(limit), next });
});
```

> **Lesson:** Offset paging breaks when data changes. Page by a unique, ordered cursor and cap the page size.

#### Example 2: A report that took three minutes

Generating an annual statement takes three minutes; the API gateway times out at 29 seconds.

*Accept, then poll*

```csharp
app.MapPost("/statements", async (StatementRequest req, IJobQueue jobs, CancellationToken ct) =>
{
    var jobId = await jobs.EnqueueAsync(req, ct);
    return Results.Accepted($"/statements/jobs/{jobId}", new { jobId, status = "queued" });
});
app.MapGet("/statements/jobs/{id}", async (Guid id, IJobQueue jobs) =>
    await jobs.GetAsync(id) is { } job
        ? Results.Ok(new { job.Status, download = job.Status == "done" ? $"/statements/{id}.pdf" : null })
        : Results.NotFound());
```

> **Lesson:** Long work doesn't fit in a request. Return 202 Accepted with a status URL and do the work in the background.

### API Design & Versioning

<p class="src">From <em>.NET Backend Deep Dive</em> · API Design & Versioning</p>

<a id="dn-109"></a>

#### Status codes people get wrong

| Code | Means | Common mistake |
|---|---|---|
| 200 | Success with a body | Returning 200 with `{"error": ...}` inside — clients can't detect failure |
| 201 | Created | Should include a `Location` header |
| 202 | Accepted, work is queued | Correct answer for anything longer than a gateway timeout |
| 204 | Success, no body | Correct for DELETE and most PUTs |
| 400 | Malformed / invalid input | Used for business-rule failures that deserve 409 or 422 |
| 401 | Not authenticated | Confused with 403 |
| 403 | Authenticated but not permitted | Confused with 401 |
| 404 | Not found | Also the right answer for "exists but you may not know that" |
| 409 | Conflict — concurrency, duplicate | Frequently returned as 400 |
| 422 | Well-formed but semantically invalid | Optional; pick 400 or 422 and be consistent |
| 429 | Rate limited | Must include `Retry-After` |
| 503 | Temporarily unavailable | Signals "retry me"; 500 does not |

The 4xx/5xx split matters operationally: **4xx is the client's fault and should not page anyone; 5xx is yours and should.** Alerting on a mixed error rate produces noise from bad clients.

<a id="dn-110"></a>

#### Pagination contract

```jsonc
// Offset - simple, but slow at depth and unstable if rows are inserted
GET /orders?page=3&pageSize=50

// Keyset/cursor - fast at any depth, stable. Opaque cursor so you can change the key.
GET /orders?cursor=eyJpZCI6MTIzNH0&limit=50
{
  "items": [...],
  "nextCursor": "eyJpZCI6MTI4NH0",   // null when exhausted
  "hasMore": true
}
```

Deliberately omitting a total count is often correct: `COUNT(*)` over a large filtered set can cost more than the page itself. Offer it as an opt-in parameter if the UI truly needs it.

Always cap `limit` server-side. `?limit=1000000` is a denial-of-service vector.

<a id="dn-111"></a>

#### Versioning

```csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;              // advertises supported versions in a header
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
});
```

| Strategy | Example | Trade-off |
|---|---|---|
| URL segment | `/v1/orders` | Most visible and cache-friendly; the pragmatic default |
| Header | `api-version: 1.0` | Cleaner URLs, harder to test in a browser, breaks naive caching |
| Media type | `Accept: application/vnd.x.v1+json` | Purist REST; almost nobody enjoys it |

**Additive changes don't need a version.** Adding an optional field, adding an endpoint, adding an enum value that clients ignore — all safe if clients tolerate unknown fields. Reserve versions for removals, renames, type changes and semantic changes.

<a id="dn-112"></a>

#### Minimal APIs vs controllers

| | Minimal APIs | Controllers |
|---|---|---|
| Overhead | Lower — no action-invoker pipeline | Slightly higher |
| Best for | Small services, high-throughput endpoints, AOT | Large surfaces with shared filters, conventions, model binding |
| Organization | Needs discipline — group into extension methods per feature | Built-in structure |

```csharp
// Group endpoints so minimal APIs don't become one 900-line file
var orders = app.MapGroup("/v1/orders")
                .RequireAuthorization()
                .WithTags("Orders")
                .AddEndpointFilter<ValidationFilter>();

orders.MapGet("/{id:int}", GetOrder).WithName("GetOrder");
orders.MapPost("/", CreateOrder);
```

Both compile to the same routing infrastructure — this is an organizational choice, not a performance one, except at extreme throughput or under Native AOT (where minimal APIs plus source-generated JSON are the supported path).

<a id="dn-113"></a>

#### Long-running work

Anything that can exceed the gateway timeout (Part 9.3) needs the async pattern:

```
POST /reports          -> 202 Accepted + Location: /reports/{id}
GET  /reports/{id}     -> 200 {"status":"running"} ... then {"status":"done","url":"..."}
```

Combine with an idempotency key (Part 12.3) so a client retry doesn't start a second job.

---

### Architecture: Layers, CQRS, Multi-tenancy

<p class="src">From <em>.NET Backend Deep Dive</em> · Architecture: Layers, CQRS, Multi-tenancy</p>

<a id="dn-134"></a>

#### gRPC vs REST vs GraphQL

| | Use when | Avoid when |
|---|---|---|
| **REST/JSON** | Public APIs, browser clients, broad interoperability | Extremely chatty internal service-to-service calls |
| **gRPC** | Internal service-to-service, streaming, strict contracts, low latency (binary + HTTP/2 multiplexing) | Browsers (needs grpc-web), public APIs, human debuggability |
| **GraphQL** | Many heterogeneous clients needing different field sets | You control both ends and REST already fits — N+1 and query-cost control become your problem |

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a126"></a>

#### REST, resources, and status codes

REST is a set of constraints, not a rulebook, but the conventions matter because they let clients, proxies and caches behave sensibly without knowing your domain.

| Verb   | Semantics                                 | Safe? | Idempotent? |
|--------|-------------------------------------------|-------|-------------|
| GET    | Read a resource                           | Yes   | Yes         |
| POST   | Create, or an action that is not a create | No    | No          |
| PUT    | Replace the whole resource at a known URI | No    | Yes         |
| PATCH  | Partial update                            | No    | Usually not |
| DELETE | Remove                                    | No    | Yes         |

| Status                | Use for                                                         |
|-----------------------|-----------------------------------------------------------------|
| 200 / 201 / 204       | OK / created (with `Location`) / no content                     |
| 400 / 422             | Malformed request / syntactically fine but semantically invalid |
| 401 / 403             | Not authenticated / authenticated but not allowed               |
| 404 / 409             | Not found / conflict, including concurrency and duplicates      |
| 429                   | Rate limited — always send `Retry-After`                        |
| 500 / 502 / 503 / 504 | Your bug / bad upstream / overloaded / upstream timeout         |

> **\[Note\]**
>
> **Design tips that survive contact with reality.** Plural nouns for collections, no verbs in paths, cursor pagination over offset for large sets, `ETag` plus `If-None-Match` for cheap conditional GETs, and a stable error body shape (RFC 9457 ProblemDetails) for every non-2xx.

###### Worked example

Pagination is the API design decision that is hardest to change later, and offset paging is the wrong default for anything that grows.

``` text
// OFFSET paging: simple, and quietly broken
GET /orders?page=500&size=50
-- SELECT ... ORDER BY PlacedAt DESC OFFSET 24950 ROWS FETCH NEXT 50 ROWS ONLY
-- the database must READ AND DISCARD 24,950 rows to return 50.
-- page 1 is instant; page 500 takes seconds; page 5,000 times out.
-- worse: if a new order arrives between page 1 and page 2, every subsequent page
--        shifts by one and the user sees a duplicate or misses a record entirely.

// CURSOR paging: constant time, and stable under concurrent writes
GET /orders?limit=50
GET /orders?limit=50&after=eyJwbGFjZWRBdCI6IjIwMjYtMDctMjdUMDk6MTQ6MDBaIiwiaWQiOiI0NzExIn0
```

``` csharp
public async Task<Page<OrderListItem>> ListAsync(Cursor? after, int limit, CancellationToken ct)
{
    var q = _db.Orders.AsNoTracking().OrderByDescending(o => o.PlacedAt).ThenByDescending(o => o.Id);

    if (after is not null)                          // keyset predicate: uses the index directly
        q = q.Where(o => o.PlacedAt < after.PlacedAt
                      || (o.PlacedAt == after.PlacedAt && o.Id.CompareTo(after.Id) < 0));

    var rows = await q.Take(limit + 1)              // fetch one extra to know if there is more
                      .Select(o => new OrderListItem(o.Id, o.PlacedAt, o.Total))
                      .ToListAsync(ct);

    var hasMore = rows.Count > limit;
    if (hasMore) rows.RemoveAt(limit);

    return new Page<OrderListItem>(rows,
        next: hasMore ? Cursor.Encode(rows[^1].PlacedAt, rows[^1].Id) : null);
}
```

The tie-breaker on `Id` is essential: without it, two orders with the same timestamp sit on a page boundary and one of them is silently skipped forever. Any keyset cursor must sort on a combination that is unique, which in practice means appending the primary key.

|                       | Offset                                 | Cursor                             |
|-----------------------|----------------------------------------|------------------------------------|
| Deep page performance | Degrades linearly                      | Constant                           |
| Stable under inserts  | No                                     | Yes                                |
| Jump to page 47       | Yes                                    | No — only next/previous            |
| Total count           | Easy (but often expensive)             | Usually omitted or approximate     |
| Right for             | Admin tables under a few thousand rows | Feeds, exports, anything unbounded |

###### Conditional requests and concurrency over HTTP

``` csharp
// GET: let the client skip the transfer entirely when nothing changed
[HttpGet("{id:guid}")]
public async Task<IResult> Get(Guid id, CancellationToken ct)
{
    var order = await _q.GetAsync(id, ct);
    if (order is null) return Results.NotFound();

    var etag = new EntityTagHeaderValue($"\"{Convert.ToBase64String(order.RowVersion)}\"");

    if (Request.GetTypedHeaders().IfNoneMatch?.Any(t => t.Compare(etag, false)) == true)
        return Results.StatusCode(StatusCodes.Status304NotModified);   // no body sent

    Response.GetTypedHeaders().ETag = etag;
    return Results.Ok(order);
}

// PUT: refuse the write if someone else changed it since the client read it
[HttpPut("{id:guid}")]
public async Task<IResult> Put(Guid id, UpdateOrder cmd, CancellationToken ct)
{
    var ifMatch = Request.GetTypedHeaders().IfMatch.FirstOrDefault();
    if (ifMatch is null) return Results.StatusCode(StatusCodes.Status428PreconditionRequired);

    try { await _handler.HandleAsync(id, cmd, Decode(ifMatch), ct); }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed,
            title: "The resource was modified by someone else. Re-read it and retry.");
    }
    return Results.NoContent();
}
```

This gives you optimistic concurrency over plain HTTP with no custom protocol: the ETag is the version token, `If-Match` is the check, and 412 is the lost-update rejection. Clients that ignore it still work; clients that honour it get correctness. Requiring `If-Match` with a 428 turns ‘last write wins’ from a silent default into an explicit client decision.

<a id="be-a128"></a>

#### OpenAPI, versioning and contracts

The OpenAPI document is your API's contract. .NET 9 generates it natively via `AddOpenApi()`; Swashbuckle and NSwag remain popular for the richer UI and client generation. Publish the document as a build artifact so consumers can generate clients and CI can diff it for breaking changes.

``` csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;                     // api-supported-versions header
}).AddApiExplorer(o => o.GroupNameFormat = "'v'VVV");
```

| Versioning style | Looks like                          | Notes                                    |
|------------------|-------------------------------------|------------------------------------------|
| URL segment      | `/api/v2/orders`                    | Most visible, easiest to route and cache |
| Query string     | `/api/orders?api-version=2`         | Easy to add, easy to forget              |
| Header           | `api-version: 2`                    | Clean URLs, harder to test in a browser  |
| Media type       | `Accept: application/vnd.x.v2+json` | Purist, rare in practice                 |

##### What counts as breaking

Removing or renaming a field, tightening validation, changing a type, adding a required request field, or changing an error contract. Adding an optional field is safe *if* your consumers ignore unknown properties — which is why tolerant readers are a contract requirement, not a nicety.

###### Versioning strategies, and what actually breaks consumers

| Strategy          | Looks like                             | Trade                                                                       |
|-------------------|----------------------------------------|-----------------------------------------------------------------------------|
| **URL path**      | `/v2/orders`                           | Obvious, cacheable, greppable. Duplicates routes. **The pragmatic default** |
| **Query string**  | `/orders?api-version=2`                | Easy to add; easy to omit; muddles caching                                  |
| **Header**        | `api-version: 2`                       | Clean URLs; invisible in a browser, harder to debug and to curl             |
| **Media type**    | `Accept: application/vnd.acme.v2+json` | Most “correct”; most friction for consumers                                 |
| **No versioning** | —                                      | Only viable if you control every consumer and can deploy in lockstep        |

``` csharp
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;          // emits api-supported-versions headers
    o.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("api-version"));   // accept both
}).AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});

var v = app.NewApiVersionSet().HasApiVersion(1.0).HasApiVersion(2.0).Build();
app.MapGet("/v{version:apiVersion}/orders/{id}", GetV1).WithApiVersionSet(v)
   .MapToApiVersion(1.0);
app.MapGet("/v{version:apiVersion}/orders/{id}", GetV2).WithApiVersionSet(v)
   .MapToApiVersion(2.0);
```

``` text
  THE ASYMMETRY THAT SHOULD SHAPE YOUR CONTRACTS

    you can always ADD.     you can essentially never REMOVE.

  SAFE (additive)                    BREAKING
    a new OPTIONAL response field      removing a field
    a new OPTIONAL request field       renaming a field
    a new endpoint                     changing a type (int -> string)
    relaxing a validation rule         making an optional field required
    a new value in an OUTPUT enum*     tightening validation
                                       changing a status code's meaning
                                       changing what an existing field MEANS
                                       changing default ordering

  * still risky: any consumer switching exhaustively, or deserialising into
    a strict enum, breaks. Treat as breaking unless you know every consumer.

  and the one people forget entirely: CHANGING THE MEANING of a field
  without changing its name or type. `status` going from "Open/Closed" to
  "Open/Pending/Closed" is a breaking change with an identical schema.
```

``` bash
# catch breaks mechanically, in CI, rather than in a consumer's incident
dotnet tool install -g Microsoft.OpenApi.Kiota
npx @redocly/cli diff baseline-openapi.json ./artifacts/openapi.json --fail-on breaking

# and commit the generated document, so a diff appears in the pull request.
# a reviewer reading a C# diff cannot see that a DTO change removed a field
# from the public contract. A reviewer reading an OpenAPI diff can.
```

> **\[Note\]**
>
> **Deprecate loudly and with a date.** Announce it, emit `Deprecation` and `Sunset` headers, log every call to the old version with the caller's identity, and give a removal date months out. Then, before removing, look at the logs: there is almost always one integration nobody remembered. Removing a version without that log is how you find out who your consumers were.

<a id="be-a129"></a>

#### API versioning in ASP.NET Core

The strategy question — whether to version at all, and where to put the version — is a design decision. This is the mechanical half: what the framework gives you once you have decided.

``` csharp
// Asp.Versioning.Http + Asp.Versioning.Mvc.ApiExplorer
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;   // old clients keep working
    o.ReportApiVersions = true;                      // api-supported-versions header
    o.ApiVersionReader = ApiVersionReader.Combine(   // accept several styles
        new UrlSegmentApiVersionReader(),            // /v1/orders
        new HeaderApiVersionReader("api-version"),   // header
        new MediaTypeApiVersionReader("v"));         // Accept: ...;v=1.0
})
.AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";                    // v1, v1.1
    o.SubstituteApiVersionInUrl = true;              // /v{version}/... in docs
});

// minimal APIs: declare a version set, then map onto it
var orders = app.NewVersionedApi("Orders");
var v1 = orders.MapGroup("/api/v{version:apiVersion}/orders").HasApiVersion(1.0);
var v2 = orders.MapGroup("/api/v{version:apiVersion}/orders").HasApiVersion(2.0);

v1.MapGet("/{id:guid}", GetOrderV1);
v2.MapGet("/{id:guid}", GetOrderV2);      // new shape, old one still served

// controllers: the same thing as attributes
[ApiVersion(1.0)]
[ApiVersion(2.0)]
[Route("api/v{version:apiVersion}/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:guid}"), MapToApiVersion(1.0)] public Task<IResult> GetV1(Guid id) => ...;
    [HttpGet("{id:guid}"), MapToApiVersion(2.0)] public Task<IResult> GetV2(Guid id) => ...;
}
```

Retiring a version is the part teams forget to plan. Announce it in the response before you enforce it, so clients discover the deadline from traffic rather than from an outage.

``` csharp
// mark deprecated, and tell callers when it dies (RFC 8594)
[ApiVersion(1.0, Deprecated = true)]

app.Use(async (ctx, next) =>
{
    await next(ctx);
    var requested = ctx.GetRequestedApiVersion();
    if (requested?.MajorVersion == 1)
    {
        ctx.Response.Headers["Sunset"] = "Wed, 31 Dec 2026 23:59:59 GMT";
        ctx.Response.Headers["Link"]   =
            "<https://docs.example.com/migrate-v2>; rel=\"deprecation\"";
    }
});

// one Swagger document per discovered version
app.UseSwaggerUI(o =>
{
    foreach (var d in app.DescribeApiVersions())
        o.SwaggerEndpoint($"/swagger/{d.GroupName}/swagger.json", d.GroupName.ToUpperInvariant());
});
```

> **\[Trap\]**
>
> **Common trap.** Every live version is a code path you must keep testing, and versions multiply faster than they retire. Prefer additive change — new optional fields, new endpoints — and reserve a version bump for genuinely breaking shape changes. Two supported versions is a normal steady state; five means nobody ever finished a migration.

> **\[Why it matters\]** Versioning is what lets you ship without coordinating a release with every consumer — and the header work (`api-supported-versions`, `Sunset`) is what turns a deprecation from an incident into a calendar entry. It is also the first question asked in any public-API design review.

<a id="be-a130"></a>

#### Pagination, filtering and sorting

Every collection endpoint becomes a performance incident the day a customer has 200,000 rows. The API contract you choose — not the database — decides whether that day arrives.

|                  | Offset (`skip`/`take`)                                           | Keyset (cursor)                                  |
|------------------|------------------------------------------------------------------|--------------------------------------------------|
| Query cost       | Degrades with depth: the engine still walks the skipped rows     | Constant: an index seek to the last key          |
| Stability        | Rows inserted mid-paging shift the window — duplicates and skips | Stable: the cursor names a position, not a count |
| Jump to page 500 | Yes                                                              | No — next/previous only                          |
| Use for          | Admin grids, small bounded sets                                  | Feeds, exports, anything unbounded or public     |

``` csharp
// keyset: page by the LAST row seen, not by how many you skipped
public async Task<Page<OrderDto>> ListAsync(Cursor? after, int size,
                                             CancellationToken ct)
{
    size = Math.Clamp(size, 1, 100);                 // never trust the client

    var q = db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenant.Id);

    if (after is not null)                            // composite tiebreaker:
        q = q.Where(o => o.PlacedAt < after.PlacedAt  // PlacedAt is not unique,
                      || (o.PlacedAt == after.PlacedAt // so Id breaks the tie
                          && o.Id < after.Id));

    var rows = await q
        .OrderByDescending(o => o.PlacedAt).ThenByDescending(o => o.Id)
        .Take(size + 1)                               // +1 = "is there more?"
        .Select(o => new OrderDto(o.Id, o.PlacedAt, o.Total))
        .ToListAsync(ct);

    var hasMore = rows.Count > size;
    if (hasMore) rows.RemoveAt(size);
    return new Page<OrderDto>(rows, hasMore ? Cursor.From(rows[^1]) : null);
}
// index required: (TenantId, PlacedAt DESC, Id DESC) — without it,
// keyset paging is just a sort of the whole table with extra steps
```

Filtering and sorting need the same discipline. Accept an allow-list of sortable fields mapped to expressions; never interpolate a client string into `OrderBy` or raw SQL, and never expose a general query language you are not prepared to defend.

``` csharp
private static readonly Dictionary<string, Expression<Func<Order, object>>> Sortable =
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["placedAt"] = o => o.PlacedAt,
        ["total"]    = o => o.Total,
        ["status"]   = o => o.Status,
    };

if (!Sortable.TryGetValue(request.SortBy ?? "placedAt", out var key))
    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        { ["sortBy"] = [$"Unknown sort field '{request.SortBy}'."] });
```

> **\[Trap\]**
>
> **Common trap.** Returning a total count on every page is a second full-table aggregate, and on a large table it costs more than the page itself. Return it only when asked (`?includeTotal=true`), cache it, or return an approximate count — users almost never need the exact number.

> **\[Why it matters\]** Unbounded collection endpoints are the single most common cause of “the API got slow after we onboarded a big customer”. A hard maximum page size, a keyset cursor and a covering index turn that class of incident into a non-event, and all three are cheapest to add before the first client ships.

<a id="be-a132"></a>

#### Idempotent HTTP APIs and safe retries

Any client with a retry policy — and every resilient client has one — will eventually send the same request twice. A network timeout is indistinguishable from a slow success: the caller does not know whether the payment went through, so it asks again. Idempotency is what makes the second attempt harmless.

###### Which verbs are safe already

GET, PUT and DELETE are idempotent by definition: applying them repeatedly leaves the same state. POST is not, which is why it carries all the risk. The two ways out are to make the operation naturally idempotent — let the client generate the resource id and use PUT — or to give the client an *idempotency key* and remember what you did with it.

``` csharp
// the client sends a key it generates once per logical operation and
// reuses on every retry of that same operation
app.MapPost("/payments", async (PaymentRequest body,
    [FromHeader(Name = "Idempotency-Key")] string? key,
    IdempotencyStore store, IPaymentService payments, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(key))
        return Results.Problem("Idempotency-Key header is required", statusCode: 400);

    // atomic claim: INSERT ... ON CONFLICT DO NOTHING (or a unique index)
    var claim = await store.TryClaimAsync(key, body.Fingerprint(), ct);

    if (claim.AlreadyCompleted)                      // replay of a finished call
        return Results.Json(claim.StoredResponse, statusCode: claim.StatusCode);

    if (claim.InFlight)                              // the first call is still running
        return Results.StatusCode(409);              // client should retry later

    if (claim.PayloadDiffers)                        // same key, different body
        return Results.Problem("Key reused with a different payload", statusCode: 422);

    var result = await payments.ChargeAsync(body, ct);
    await store.CompleteAsync(key, result, 201, ct); // store BEFORE responding
    return Results.Created($"/payments/{result.Id}", result);
});
```

Two details make or break this. The claim must be atomic — a unique constraint on the key, not a read-then-write — or two concurrent retries both pass the check. And the response must be stored before it is returned, or a crash after the charge but before the write leaves the key unresolved.

| Layer                | Mechanism                                                                 |
|----------------------|---------------------------------------------------------------------------|
| HTTP write endpoints | Idempotency-Key header + a claim table with a TTL (24h is typical)        |
| Message consumers    | Inbox table keyed by MessageId — the same pattern, different transport    |
| Database writes      | Natural keys and unique constraints: let the engine reject the duplicate  |
| Outbound calls       | Pass your key downstream; most payment and messaging providers accept one |

> **\[Trap\]**
>
> **Common trap.** Retrying a non-idempotent POST is the classic double-charge. Resilience policies must therefore be configured per operation, not globally: retry GETs freely, retry POSTs only when an idempotency key is in play, and prefer a circuit breaker over blind retries for everything else.

### What an API Is

<p class="src">From <em>Backend Engineering</em> · APIs · What an API Is</p>

<a id="be-e503"></a>

#### The definition, stripped down

**An API is a contract that lets one program use another without knowing how it works.**

That's it. Every API — a library, a syscall, a REST endpoint — is the same thing: a published set of operations, their inputs, their outputs, and their meanings, with the implementation hidden behind it.

The `List<T>.Add` method is an API. So is `open()` in libc. Nothing about the definition requires a network. What networks add is **the two facts at the top of this document**: you must serialize the contract into bytes, and the channel can fail.

<a id="be-e504"></a>

#### Why the boundary is the point

The contract is what lets both sides change independently. The provider can rewrite its storage layer, swap languages, or split into ten services — as long as the contract holds, callers don't notice.

That freedom is exactly what a breaking change destroys, which is why versioning (Part XI) is a large topic for something that sounds like bookkeeping.

The flip side: **the contract is the hard part to change and the easy part to get wrong.** Implementations are cheap to fix; a contract with a thousand consumers is not.

<a id="be-e505"></a>

#### The families

| Style         | Shape                    | Best at                             | Cost                             |
|---------------|--------------------------|-------------------------------------|----------------------------------|
| **REST**      | resources + HTTP verbs   | public APIs, CRUD, caching          | over/under-fetching, chatty      |
| **RPC**       | remote function calls    | internal actions                    | tighter coupling                 |
| **gRPC**      | RPC + protobuf + HTTP/2  | service-to-service, streaming       | binary, needs tooling            |
| **GraphQL**   | client-specified queries | many clients, varied needs          | caching hard, N+1 risk, complex  |
| **SOAP**      | XML envelopes + WSDL     | legacy enterprise, formal contracts | verbose, heavy                   |
| **Webhooks**  | server calls *you*       | events, avoiding polling            | needs a public endpoint, retries |
| **SSE**       | server → client stream   | live updates, one-way               | one-directional                  |
| **WebSocket** | full-duplex socket       | chat, games, live collaboration     | stateful, scaling is harder      |

Most real systems mix these. A public REST API, gRPC between internal services, webhooks for events, WebSockets for live UI.

<a id="be-e506"></a>

#### The uncomfortable truth about "REST"

Roy Fielding's dissertation defines REST by constraints: client-server, stateless, cacheable, layered, uniform interface, and — the one everyone skips — **hypermedia as the engine of application state (HATEOAS)**, meaning responses contain the links describing what you can do next.

Almost nothing called REST does this. What the industry means by "REST" is: **JSON over HTTP, with nouns in URLs and verbs as methods.** That's fine and it's useful — but be aware the word is used loosely, and someone will eventually tell you your API "isn't really REST." They're technically correct and it usually doesn't matter.

The parts genuinely worth keeping:

- **Statelessness** — each request carries everything needed. No server-side session affinity. This is what makes horizontal scaling trivial: any instance can serve any request.
- **Uniform interface** — the same verbs mean the same things everywhere, so intermediaries (caches, proxies, gateways) can act without understanding your domain.
- **Cacheability** — a `GET` is cacheable by anything in the path. Enormous leverage, mostly unused.

------------------------------------------------------------------------

### Designing the Interface

<p class="src">From <em>Backend Engineering</em> · APIs · Designing the Interface</p>

<a id="be-e507"></a>

#### Resources and URLs

Model **nouns**, act with **verbs**:

``` sql
GET    /users              list
POST   /users              create
GET    /users/42           read
PUT    /users/42           replace
PATCH  /users/42           partial update
DELETE /users/42           remove
GET    /users/42/orders    sub-collection
```

Conventions that reduce argument:

- **Plural nouns.** `/users/42`, not `/user/42`. Consistency beats grammar.
- **No verbs in paths.** `/getUser` duplicates what the method already says. Exception below.
- **Lowercase, hyphens.** `/purchase-orders`, not `/purchaseOrders`. Paths are case-sensitive; hostnames aren't.
- **Shallow nesting.** `/users/42/orders/7` is fine; `/users/42/orders/7/items/3/tax` is a maintenance problem. Once a resource has its own ID, expose it at top level: `/order-items/3`.
- **IDs are opaque.** Never expose sequential database keys publicly — they leak volume ("we have 43 customers") and enable enumeration. UUIDs or hashids.

**When the resource model doesn't fit:** some operations aren't CRUD. `POST /orders/42/cancel`, `POST /sessions` (login), `POST /reports/generate`. Purists will object. Ship it — a clear verb endpoint beats a contorted noun. The alternative, `PATCH /orders/42 {"status":"cancelled"}`, hides real business logic behind what looks like a field assignment.

<a id="be-e508"></a>

#### Collections: pagination, filtering, sorting

**Never return an unbounded collection.** It works for months and then someone has 400,000 rows.

**Offset pagination** — simple, familiar, wrong at scale:

``` text
GET /users?page=3&per_page=50
```

`OFFSET 100000` makes the database count and discard 100,000 rows. And inserts during paging cause items to shift, so you skip or duplicate rows across pages.

**Cursor pagination** — correct, slightly less convenient:

``` text
GET /users?limit=50&after=eyJpZCI6NDJ9
{ "data": [...], "next_cursor": "eyJpZCI6OTJ9", "has_more": true }
```

The cursor encodes "where I left off" (typically the last sort key). Constant-time, stable under concurrent writes. No random access to page 47 — usually nobody needs it.

Rule: offset for admin tables where users jump to a page; cursor for feeds, exports, and anything large.

**Filtering and sorting:**

``` text
GET /users?status=active&created_after=2026-01-01&sort=-created_at&fields=id,name
```

Leading `-` for descending. `fields` for sparse responses. Whatever you support, **document it and validate it** — an unvalidated `sort` parameter interpolated into SQL is an injection hole, and an unindexed filter column is a table scan someone can trigger at will.

<a id="be-e509"></a>

#### Request and response bodies

**Be consistent about envelopes.** Either always wrap or never:

``` json
{ "data": {...}, "meta": {...} }
```

Wrapping gives you somewhere to put pagination and metadata. Not wrapping is cleaner for simple cases. Pick one; mixing is the actual sin.

**Field naming:** `snake_case` is most common in public APIs; `camelCase` matches JS and .NET conventions. Either works. Configure the serializer globally and never think about it again.

**Nulls vs absence.** These are different and clients notice:

- Field absent → not provided / not applicable
- Field `null` → explicitly empty

This matters enormously for `PATCH`: is `{"nickname": null}` "clear the nickname" and `{}` "leave it alone"? Decide and document. JSON Merge Patch (RFC 7386) defines `null` as delete; JSON Patch (RFC 6902) uses explicit ops and avoids the ambiguity entirely.

**Dates: ISO 8601, UTC, always.** `2026-08-17T14:30:00Z`. Not Unix timestamps (unreadable, and second-precision is often wrong), not local times (which timezone?), not `MM/DD/YYYY` (ambiguous with the rest of the world). If a local time genuinely matters — a calendar appointment — send the instant *and* the IANA timezone name (`Europe/Amsterdam`), not the offset, because offsets change with DST.

**Money: never floating point.** `0.1 + 0.2 != 0.3` in IEEE 754. Send an integer of minor units with an explicit currency:

``` json
{ "amount": 1999, "currency": "EUR" }
```

Or a decimal string. Never `19.99` as a JSON number.

**Big integers:** JavaScript numbers are IEEE 754 doubles, exact only to 2^53. A 64-bit ID sent as a JSON number silently corrupts in any JS client. Send IDs as strings.

**Booleans in URLs:** `?active=true` is fine; `?active=1` is ambiguous. Be consistent.

<a id="be-e510"></a>

#### Errors

Return a machine-readable body, not just a status code. RFC 9457 (Problem Details) is the standard, and .NET produces it natively:

``` json
{
  "type": "https://api.example.com/errors/insufficient-funds",
  "title": "Insufficient funds",
  "status": 402,
  "detail": "Account balance 15.00 EUR is below the requested 50.00 EUR.",
  "instance": "/accounts/42/withdrawals",
  "traceId": "00-a1b2c3-d4e5f6-01",
  "errors": {
    "amount": ["must not exceed available balance"]
  }
}
```

Design rules:

- **A stable machine-readable code.** Clients must branch on something that isn't an English string. `type` (a URI) or a `code` field.
- **Human text for developers**, not end users. The client decides what its users see; you can't know their language or context.
- **Include a trace ID.** The single highest-value field in the whole response. It's what makes "it failed at 14:32" into a log query.
- **Never leak internals.** Stack traces, SQL, file paths, and internal hostnames in a 500 response are reconnaissance for an attacker.
- **Validation errors: return all of them at once.** Returning one field at a time turns form submission into a guessing game.

------------------------------------------------------------------------

### Beyond CRUD

<p class="src">From <em>Backend Engineering</em> · APIs · Beyond CRUD</p>

<p class="pd">REST over JSON covers most of what you&#x27;ll build. These are the cases where it doesn&#x27;t.</p>

<a id="be-e511"></a>

#### File upload and download

Files break the assumptions of a JSON API: they're large, slow, and don't belong in memory.

**Small uploads (\< a few MB)** — `multipart/form-data`, straightforward. Always cap the size at the server, not just in application code, and validate the content type by **inspecting the bytes**, not by trusting the declared `Content-Type` or the filename extension. Both are attacker-controlled.

**Large uploads — don't proxy them through your API.** Issue a **presigned URL** and let the client upload directly to blob storage:

``` text
POST /uploads          → { "url": "https://s3...?X-Amz-Signature=...", "key": "u/abc" }
PUT  <that url>        → client uploads straight to storage, your service is not involved
POST /documents        → { "storageKey": "u/abc" }  — you record the reference
```

Your API never touches the bytes. No memory pressure, no request-timeout problems, no bandwidth cost through your service. This is the standard pattern and it's worth adopting early — retrofitting it after you've built a proxying upload endpoint is painful.

**Resumable uploads** for anything where a dropped connection matters: chunked with an upload ID, and a `HEAD`-style endpoint reporting how much has arrived. The `tus` protocol standardizes this.

**Downloads:** presigned URLs again, or stream with `Content-Disposition`. Support **range requests** (`Accept-Ranges: bytes`, `206 Partial Content`) for media and for resumability — note range requests require *strong* ETags (VI.1).

Never buffer a whole file server-side. In .NET that means streaming rather than `IFormFile.CopyToAsync` into a `MemoryStream`, and disabling the form-value length limit deliberately rather than raising it blindly.

<a id="be-e512"></a>

#### Bulk and batch operations

One request that acts on many items. The hard question is not the shape — it's **what happens when item 47 fails.**

Three defensible answers, and you must pick one and document it:

- **All-or-nothing.** One transaction, any failure rolls back everything. Simple to reason about, but a single bad row blocks the batch.
- **Best-effort with per-item results.** Process everything, report each outcome. The usual choice for imports.
- **Stop-on-first-error.** Rarely what anyone wants.

For best-effort, `207 Multi-Status` exists, though a `200` with a structured body is more widely understood:

``` json
{
  "succeeded": 48,
  "failed": 2,
  "results": [
    { "index": 0, "status": 201, "id": "ord_1" },
    { "index": 47, "status": 422, "error": { "code": "invalid_sku", "detail": "SKU not found" } }
  ]
}
```

Rules: **always echo an index or client-supplied reference**, so callers can correlate results with inputs. **Cap the batch size** and document it — an uncapped batch endpoint is an unbounded resource consumption vulnerability (VIc.1). **Make the whole batch idempotent** with a single `Idempotency-Key`, since retrying a partially-applied batch is exactly the scenario idempotency exists for.

For very large jobs, don't do it synchronously at all — accept the batch, return `202` with a job ID, and expose progress (VI.5).

<a id="be-e513"></a>

#### gRPC

Binary RPC over HTTP/2, with `.proto` files as the contract and generated clients on both sides.

``` csharp
service Orders {
  rpc Get (GetRequest) returns (Order);
  rpc Watch (WatchRequest) returns (stream OrderEvent);   // server streaming
}
message GetRequest { string id = 1; }
```

Four call shapes: unary, server-streaming, client-streaming, and bidirectional. The streaming ones are the real differentiator — over REST you'd be polling or reaching for WebSockets.

**Where it wins:** internal service-to-service. Protobuf is typically 3–10× smaller and considerably faster to serialize than JSON; the generated client means no hand-written HTTP plumbing; the `.proto` is a contract that *cannot* drift from the implementation, because both are generated from it. Deadlines propagate natively — the thing you have to build by hand over HTTP (V.5).

**Where it doesn't:** browsers can't speak gRPC directly (gRPC-Web needs a proxy and drops some streaming), it's not human-readable in a log or a curl session, and some corporate proxies still mishandle HTTP/2.

**Schema evolution** is the part to get right: **never reuse a field number.** Removing a field means marking it `reserved`, or a future field with that number will silently deserialize old data into the wrong property. Add fields with new numbers; they're optional by default in proto3.

Practical shape: gRPC inside the mesh, REST at the public edge. In .NET, `Grpc.AspNetCore` for servers and `GrpcChannel` with a shared, long-lived channel for clients (it multiplexes over HTTP/2, so per-call channels are the `HttpClient` mistake again).

<a id="be-e514"></a>

#### GraphQL

One endpoint; the client declares the shape it wants.

``` text
query { order(id: "42") { total customer { name email } items { sku qty } } }
```

**The problem it solves:** over-fetching (mobile downloading forty fields to show three) and under-fetching (the N+1 round trips of III.2/VI.2). One request, exactly the fields needed, across multiple resources. With many heterogeneous clients — web, iOS, Android, partners — this is a real win, and it's why GraphQL exists at companies with exactly that shape.

**What it costs, and none of these are minor:**

- **The N+1 moves to your server.** Resolving `items { supplier { name } }` naively issues one query per item. **DataLoader** batches within a request tick — it's not optional, it's the thing that makes GraphQL viable.
- **HTTP caching stops working.** Everything is a `POST` to `/graphql` with a different body, so CDNs, browser caches, and `ETag`s are all inapplicable. You replace them with application-level caching, persisted queries (hash the query, `GET` by hash, and caching returns), or an edge layer that understands GraphQL.
- **Unbounded query cost.** A client can request a deeply nested graph and consume your database. **Depth limits, complexity scoring, and an allow-list of persisted queries in production** are mandatory, not hardening.
- **Errors are `200`.** Failures live in an `errors` array. Every monitoring tool that alerts on status codes sees a healthy service while everything fails.
- **Authorization gets harder.** It's per-field across a graph, not per-endpoint. Easy to expose a field through a path nobody reviewed.

Choose it when you genuinely have many clients with divergent needs. Don't choose it because REST felt verbose — you're trading a well-understood set of problems for a less-understood one.

<a id="be-e515"></a>

#### Multi-tenancy

One system serving many customers, which changes the shape of nearly everything.

**Where the tenant comes from — pick one and enforce it globally:**

- **From the token** (a `tenant_id` claim) — safest. The client cannot assert a tenant it wasn't issued.
- **From the subdomain** (`acme.api.example.com`) — friendly, but must still be cross-checked against the token.
- **From a path or header** (`/tenants/acme/...`) — explicit, and **dangerous if you trust it without verification.** A client sending someone else's tenant ID must get a 403, not data.

**Enforce isolation in one place.** A repository-level filter, a `HasQueryFilter`, or row-level security in the database — never a `WHERE tenant_id = @t` that each developer must remember. One forgotten clause is a cross-tenant data breach, and it's the same failure as BOLA (IV.3) at a larger blast radius.

**Everything keyed must include the tenant.** Cache keys, rate limit buckets, idempotency keys, background job queues, log fields, metric dimensions. A cache key missing the tenant serves one customer's data to another — see the caching reference on key design.

**Noisy neighbours.** One tenant's bulk import shouldn't degrade everyone. Per-tenant rate limits and concurrency caps, not just global ones. Consider a separate queue or worker pool for large jobs.

**Per-tenant observability.** When a customer says "it's slow," a global p99 can't answer. Tag metrics with the tenant — carefully, since high-cardinality tags are expensive; usually a bucketed tier rather than every ID.

------------------------------------------------------------------------

### Evolution and Versioning

<p class="src">From <em>Backend Engineering</em> · APIs · Evolution and Versioning</p>

<a id="be-e545"></a>

#### What actually breaks a client

**Breaking:**

- Removing or renaming a field or endpoint
- Changing a type (`"42"` → `42`)
- Adding a required request field
- Tightening validation
- Changing a status code for the same condition
- Changing default behavior or ordering

**Non-breaking (usually):**

- Adding an optional request field
- Adding a response field
- Adding a new endpoint
- Adding a new optional query parameter

That "usually" earns its place. Clients that deserialize strictly — rejecting unknown fields — break when you add one. Clients that assert on exact response shapes in tests break on anything. **Publish a compatibility policy** telling consumers what you reserve the right to change, and tell them to ignore unknown fields.

<a id="be-e546"></a>

#### Where to put the version

| Strategy    | Example                               | Trade-off                                          |
|-------------|---------------------------------------|----------------------------------------------------|
| URL path    | `/v1/users`                           | obvious, cacheable, most common; "unRESTful"       |
| Header      | `API-Version: 1`                      | clean URLs; invisible, harder to test in a browser |
| Media type  | `Accept: application/vnd.api.v1+json` | most correct; least ergonomic                      |
| Query param | `/users?v=1`                          | easy; messy with caching                           |

**Use the URL path.** It's visible, curl-able, cache-friendly, and universally understood. The theoretical objections don't survive contact with an on-call engineer trying to reproduce a bug.

<a id="be-e547"></a>

#### Strategies that scale better than versioning

- **Additive-only.** Never remove, only add. Costs schema clutter, buys years of stability. This is how most large APIs actually survive.
- **Expand and contract.** Add the new field, populate both, migrate clients, remove the old one after a deprecation window.
- **Feature flags per consumer** rather than a global version bump.

**Deprecation, done properly:**

``` yaml
Deprecation: Sun, 01 Nov 2026 00:00:00 GMT
Sunset: Sun, 01 Feb 2027 00:00:00 GMT
Link: <https://docs.example.com/migrate-v2>; rel="deprecation"
```

Plus: announce it, measure who's still calling it, contact them directly, and only then remove. A version you can't retire is a version you'll maintain forever — so track usage per version per client from day one, or you'll never be able to prove it's safe to delete.

------------------------------------------------------------------------

### Contracts and Documentation

<p class="src">From <em>Backend Engineering</em> · APIs · Contracts and Documentation</p>

<a id="be-e548"></a>

#### OpenAPI

A machine-readable description of your HTTP API. It generates client SDKs, server stubs, interactive docs, mock servers, and request validation — from one file.

Two directions:

- **Code-first** — generate the spec from your implementation (Swashbuckle, NSwag, ServiceStack's built-in support). Easy, always in sync, but the spec inherits whatever your code happens to do.
- **Design-first** — write the spec, then implement against it. Better contracts, better parallel work between teams, more discipline required.

Either way: **the spec belongs in version control and in CI.** A spec that drifts from reality is worse than none, because people trust it.

<a id="be-e549"></a>

#### What documentation must contain

Beyond the generated reference:

- **Authentication**, with a complete worked example
- **A copy-pasteable first request** that works in under five minutes
- **Every error code** and what to do about each
- **Rate limits**, stated numerically
- **Pagination**, with an example of following a cursor
- **Changelog** and deprecation notices
- **Idempotency semantics** for anything that mutates

The test: can someone unfamiliar make a successful call in five minutes, using only the docs? Everything else is secondary to that.

<a id="be-e550"></a>

#### Contract testing

Unit tests don't catch "the provider changed a field name." Integration tests catch it late and flakily.

**Consumer-driven contracts** (Pact and similar): consumers declare what they depend on; the provider's CI verifies it still holds. Breaking changes fail the provider's build instead of the consumer's production.

Cheaper alternatives that still catch most of it: schema validation in CI, and snapshot tests over serialized responses.

------------------------------------------------------------------------

### Building APIs in .NET

<p class="src">From <em>Backend Engineering</em> · APIs · Building APIs in .NET</p>

<a id="be-e551"></a>

#### The landscape

**ASP.NET Core** is the default. Two flavors:

- **Minimal APIs** — terse, fast, good for small services
- **Controllers** — filters, model binding, conventions; better for large surface areas

**ServiceStack** — message-based rather than controller-based. Request DTOs *are* the contract, with automatic routing, serialization, validation, and OpenAPI derived from them. Runs on both Framework and Core, which makes it a common bridge in legacy migrations.

**gRPC** — for internal service-to-service. `.proto` files, generated clients, HTTP/2, binary framing, and real streaming. Roughly an order of magnitude less serialization overhead than JSON. Requires HTTP/2 end to end, which some proxies still complicate.

<a id="be-e552"></a>

#### The things worth getting right

**Validation.** FluentValidation or data annotations, returning Problem Details with all failures at once. ServiceStack has this built in; ASP.NET Core produces `ValidationProblemDetails` automatically from model state.

**Global exception handling.** One place that maps exception types to status codes, logs with a trace ID, and never leaks internals:

``` csharp
app.UseExceptionHandler();                    // → ProblemDetails
builder.Services.AddProblemDetails();
```

**Correlation.** Emit and propagate a trace ID on every request and every log line. `Activity`/OpenTelemetry does this automatically and flows across awaits (via `AsyncLocal`). This is what turns "an error at 14:32" into a single query.

**Health checks.** `/health/live` (am I running?) and `/health/ready` (can I serve traffic?) are separate questions — orchestrators need both, and conflating them causes restart loops during dependency outages.

**Cancellation.** Accept `CancellationToken` in every endpoint and pass it down. ASP.NET Core binds `HttpContext.RequestAborted` automatically, so honoring it means you stop working the moment a client disconnects — free throughput under load.

**Serialization.** `System.Text.Json` is the default and is significantly faster than Newtonsoft. Configure naming policy, null handling, and enum-as-string once, globally. Watch out: STJ is case-sensitive by default in some paths and stricter than Newtonsoft about numbers-as-strings — a common surprise when migrating.

<a id="be-e553"></a>

#### Consuming APIs

- `IHttpClientFactory` with typed clients — solves socket exhaustion and DNS staleness together
- `AddStandardResilienceHandler()` for retry + breaker + timeout in one line
- Explicit timeout per call, derived from a budget (Part V.5)
- `HttpCompletionOption.ResponseHeadersRead` for large responses
- Deserialize from the stream, not from an intermediate string
- Generate clients from OpenAPI (NSwag, Kiota) rather than hand-writing them

------------------------------------------------------------------------

### API surfaces in .NET

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · API surfaces in .NET</p>

<a id="be-m236"></a>

#### The API styles, and choosing one

"API" covers at least eight genuinely different shapes, all implementable in .NET. They differ in who initiates, how the contract is expressed, and how many round trips a task takes.

| Style              | .NET implementation                      | Contract           | Best for                                                                             |
|--------------------|------------------------------------------|--------------------|--------------------------------------------------------------------------------------|
| **HTTP / REST**    | ASP.NET Core (controllers, minimal APIs) | OpenAPI            | public APIs, browsers, anything                                                      |
| **RPC over HTTP**  | ASP.NET Core, ServiceStack               | DTOs               | internal service calls, action-shaped operations                                     |
| **gRPC**           | `Grpc.AspNetCore`                        | `.proto`           | service-to-service, streaming, polyglot                                              |
| **GraphQL**        | HotChocolate                             | SDL schema         | many client shapes over one graph                                                    |
| **OData**          | `Microsoft.AspNetCore.OData`             | metadata document  | generic query surfaces, admin tooling                                                |
| **Real-time push** | SignalR, raw WebSockets, SSE             | hub methods        | notifications, dashboards, collaboration                                             |
| **Messaging**      | MassTransit, NServiceBus, Azure SB       | message schemas    | async work, decoupling ([Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249)) |
| **Webhooks**       | plain outbound HTTP                      | documented payload | notifying *other people's* systems                                                   |
| **SOAP**           | CoreWCF                                  | WSDL               | legacy integration only                                                              |

> **\[Tip\]**
>
> **The choice is usually made by the caller, not by you.** A browser wants HTTP+JSON. A partner integration wants HTTP+JSON with an OpenAPI document. Your own services can afford gRPC's tooling cost and get typed contracts and streaming for it. Mobile clients on poor networks benefit from fewer, fatter calls — which is an argument for GraphQL or a BFF ([Gateways, BFFs, and service discovery](12-01-monoliths-modules-and-services.md#be-m251)), not for a chattier REST surface.

> **\[Why it matters\]**
>
> **A note on ServiceStack,** since plenty of .NET systems run on it: its model is message-based rather than resource-based — one request DTO, one response DTO, routes attached to the DTO. That is closer to RPC than to REST, and it is a coherent design, not a deficient one. The relevant point when modernising is that a request-DTO-per-operation maps very cleanly onto minimal-API endpoints or onto gRPC methods, so a migration is mostly transport plumbing rather than redesign.

<a id="be-m238"></a>

#### Designing a resource API

``` sql
GET    /orders?status=open&page=2       list        200
GET    /orders/1042                     read        200 | 404
POST   /orders                          create      201 + Location header
PUT    /orders/1042                     replace     200 | 204 | 404
PATCH  /orders/1042                     partial     200 | 204 | 404
DELETE /orders/1042                     remove      204 | 404
POST   /orders/1042/cancel              action      200 | 409
```

The last line matters: **not everything is a resource.** "Cancel this order" is a state transition with its own rules, and modelling it as `PATCH {status: "cancelled"}` throws away the ability to validate it as an operation, to require different permissions, and to reject it with a meaningful conflict. A sub-resource action endpoint is better design than a contorted `PATCH`.

| Property       | Meaning                     | Which verbs            |
|----------------|-----------------------------|------------------------|
| **Safe**       | no observable change        | `GET`, `HEAD`          |
| **Idempotent** | N identical calls == 1 call | `GET`, `PUT`, `DELETE` |
| Neither        | each call is a new effect   | `POST`, `PATCH`        |

``` csharp
// Make POST idempotent explicitly - clients retry, and networks lie about
// whether a request arrived (§14).
[HttpPost("orders")]
public async Task<IActionResult> Place(
        [FromHeader(Name = "Idempotency-Key")] string key,
        PlaceOrderCommand cmd, CancellationToken ct) {
    if (await _store.TryGetResultAsync(key, ct) is { } prior)
        return Ok(prior);                     // same key -> same response, no new order
    var result = await _handler.HandleAsync(cmd, ct);
    await _store.SaveAsync(key, result, TimeSpan.FromHours(24), ct);
    return CreatedAtRoute("GetOrder", new { id = result.Id }, result);
}
```

> **\[Trap\]**
>
> **Status codes carry contract meaning; do not improvise.** `400` the request is malformed. `401` not authenticated. `403` authenticated but not permitted. `404` not found — also the right answer for "exists but you may not know it exists". `409` a conflict with current state. `422` well-formed but semantically invalid. `429` rate limited. Returning `200` with `{"success": false}` defeats every client library, proxy, and monitoring tool that reads status codes ([Errors: ProblemDetails and failure contracts](#be-m242)).

Recipe: [Recipe: an idempotent command handler](14-01-recipes-code-you-will-actually-write.md#be-m286)

<a id="be-m239"></a>

#### Request and response contracts

The types crossing your API boundary are a published contract, not an implementation detail. They should never be your entities.

``` csharp
// NEVER expose an entity directly:
[HttpGet] public Task<List<Order>> Get() => _db.Orders.ToListAsync();
// - leaks every column, including ones added later
// - serialises navigation properties, possibly cyclically
// - couples your schema to your public contract: a rename is a breaking change
// - and it fetches everything (§157)
```

``` csharp
public sealed record OrderDto(long Id, string Customer, decimal Total, DateTimeOffset CreatedAt);

public sealed record PlaceOrderCommand {
    public required long CustomerId { get; init; }
    public required IReadOnlyList<LineDto> Lines { get; init; }
    public string? Note { get; init; }
}
```

``` csharp
// Validate at the edge, once, and return all the failures together.
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand> {
    public PlaceOrderValidator() {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 500)
            .WithMessage("At most 500 lines per order");
        RuleForEach(x => x.Lines).ChildRules(l => {
            l.RuleFor(x => x.Qty).InclusiveBetween(1, 10_000);
            l.RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        });
    }
}
```

> **\[Tip\]**
>
> **Bound every collection in a request.** An unbounded `Lines` array is a denial-of-service vector and a memory risk before any business rule runs. The same applies to page size, batch size, and upload length — cap them in the contract and reject rather than truncate, so the client learns ([Case: the export that timed out at 50,000 rows](14-03-worked-cases-from-production.md#be-m299)).

> **\[Trap\]**
>
> **Model binding is an attack surface.** Binding directly to a type with more properties than the client should set — `IsAdmin`, `TenantId`, `Status` — is over-posting: the client sets it and you save it. A dedicated command type with only the settable fields makes it structurally impossible, which is far more reliable than remembering to ignore fields ([Multi-tenancy: three strategies](07-05-schema-design-and-migrations.md#be-m212)).

Recipe: [Recipe: a concurrent-safe update from an API](07-04-transactions-and-isolation.md#be-m292)

<a id="be-m241"></a>

#### Versioning an API

| Strategy     | Looks like                          | Trade-off                                      |
|--------------|-------------------------------------|------------------------------------------------|
| URL path     | `/v1/orders`                        | obvious, cacheable; duplicates routes          |
| Query string | `/orders?api-version=1`             | easy to default; easy to forget                |
| Header       | `api-version: 1`                    | clean URLs; invisible in a browser and in logs |
| Media type   | `Accept: application/vnd.x.v1+json` | most correct; least used                       |

``` csharp
builder.Services.AddApiVersioning(o => {
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;               // advertise supported versions in headers
    o.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(), new HeaderApiVersionReader("api-version"));
});
```

**The cheapest version is the one you do not create.** Most changes can be made additively, and additive changes need no new version at all — the same compatibility rules as any serialized contract ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223)):

``` csharp
Non-breaking: add an optional request field; add a response field;
              add an endpoint; relax a validation rule; add an enum value
              IF clients were told to tolerate unknown values.

Breaking:     remove or rename a field; make an optional field required;
              change a type or its meaning; tighten validation;
              change a status code; change default sort order or page size.
```

> **\[Trap\]**
>
> **Version the contract, not the implementation.** Copying a controller to `V2` and diverging the logic produces two codebases that drift. Keep one implementation and map to the versioned shape at the edge — usually a small translation layer that upgrades a v1 request to the internal command and downgrades the internal result to the v1 response ([Versioning data contracts](07-05-schema-design-and-migrations.md#be-m223)).

> **\[Tip\]**
>
> **Publish a deprecation policy and honour it.** Announce, emit a `Deprecation`/`Sunset` header, log which clients still call the old version, contact them, then remove. Without the telemetry step you can never actually delete anything, and an API you cannot delete from is one that only ever grows.

<a id="be-m242"></a>

#### Errors: ProblemDetails and failure contracts

Errors are part of the contract. RFC 9457 `ProblemDetails` is the standard shape, and ASP.NET Core produces it by default for framework-generated failures.

``` text
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json

{
  "type":     "https://api.example.com/errors/insufficient-stock",
  "title":    "Insufficient stock",
  "status":   422,
  "detail":   "SKU A-1042 has 3 available, 10 requested.",
  "instance": "/orders",
  "traceId":  "00-4bf92f...-01",          // correlate with your traces
  "errors":   { "lines[0].qty": ["Only 3 available"] }
}
```

``` csharp
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx => {
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
});

app.UseExceptionHandler();      // converts unhandled exceptions to ProblemDetails
app.UseStatusCodePages();       // gives bare status codes a body too
```

``` csharp
// Map domain failures to status codes in ONE place, not in every endpoint:
public sealed class DomainExceptionHandler : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct) {
        var (status, title) = ex switch {
            NotFoundException      => (404, "Not found"),
            ConcurrencyException   => (409, "The record was modified by someone else"),
            ValidationException    => (422, "Validation failed"),
            RateLimitException     => (429, "Too many requests"),
            _                      => (500, "An unexpected error occurred")
        };
        if (status == 500) _log.LogError(ex, "Unhandled");     // log ONLY the unexpected
        await Results.Problem(title: title, statusCode: status).ExecuteAsync(ctx);
        return true;
    }
}
```

> **\[Trap\]**
>
> **Never return exception details to a caller.** A stack trace names your types, file paths, framework versions, and often connection strings or SQL. Log the detail with a correlation id and return the id — the caller can quote it to support, and an attacker learns nothing ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)).

> **\[Tip\]**
>
> **Distinguish expected from unexpected in your logging.** A 404 or a validation failure is a normal outcome and should not be an error log — otherwise your error rate is dominated by users typing bad input and real incidents disappear into it ([Exceptions: what they actually cost](02-08-errors-and-exceptions.md#be-m44)).

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a135"></a>

#### gRPC and binary contracts

gRPC uses HTTP/2 and Protocol Buffers: a schema file generates strongly typed client and server code, payloads are compact binary, and one connection multiplexes many concurrent calls. It is the default choice for chatty internal service-to-service traffic.

``` csharp
service Orders {
  rpc GetOrder    (GetOrderRequest) returns (OrderReply);
  rpc StreamEvents(StreamRequest)   returns (stream OrderEvent);   // server streaming
}

message GetOrderRequest { string id = 1; }
message OrderReply { string id = 1; double total = 2; string status = 3; }
```

Field numbers are the contract, not the names. Never reuse a number, never change a type; add new optional fields with new numbers and mark removed ones `reserved`. That is what makes rolling upgrades safe.

**Choose gRPC**

- Internal, high-volume, low-latency calls
- You want a generated, type-safe client
- Streaming in either direction
- Polyglot services sharing one schema

**Choose REST/JSON**

- Public or partner-facing APIs
- Browsers without a proxy (needs gRPC-Web)
- Human-debuggable with curl
- Caching and CDN semantics matter

###### Worked example

Schema evolution is the whole reason protobuf's field numbers exist, and it is worth seeing what is and is not safe.

``` csharp
// v1, deployed everywhere
message Order {
  string id       = 1;
  double total    = 2;
  string status   = 3;
}

// v2 - SAFE changes
message Order {
  string id            = 1;
  double total         = 2;
  string status        = 3;
  string currency      = 4;      // ADD a new field with a NEW number: old clients ignore it
  reserved 5;                    // reserve numbers you plan to use, or have removed
  reserved "old_field";
}

// v3 - UNSAFE changes, each with its own failure mode
message Order {
  string id       = 1;
  int64  total    = 2;   // CHANGED TYPE: old clients decode garbage, silently
  string state    = 3;   // renamed: harmless on the wire (numbers matter), but breaks
                         //          generated code for every consumer at compile time
  string currency = 3;   // REUSED a number: catastrophic - two meanings for one field
}
```

| Change                         | Wire compatible?                  | Note                                                 |
|--------------------------------|-----------------------------------|------------------------------------------------------|
| Add a field with a new number  | Yes                               | The safe way to evolve                               |
| Remove a field                 | Yes, if you `reserved` the number | Otherwise someone reuses it later                    |
| Rename a field                 | Yes                               | Numbers are the contract; but generated code changes |
| Change `int32` ↔ `int64`       | Yes, for values that fit          | Both are varint                                      |
| Change `int` ↔ `string`        | **No**                            | Different wire types; silent corruption              |
| Change `optional` ↔ `repeated` | **No**                            | Decodes incorrectly                                  |
| Reuse a retired number         | **No**                            | The single worst thing you can do to a proto file    |

The operational discipline that follows: check the `.proto` files into a shared repository, review changes there rather than in each service, and run a compatibility linter (Buf) in CI that fails the build on a breaking change. This is cheaper than every alternative, and it makes the contract a genuinely reviewed artifact rather than an accident of whoever edited it last.

###### Streaming, and the deadline that everyone forgets

``` csharp
// server streaming: push results as they are produced, no polling
public override async Task StreamEvents(StreamRequest request,
    IServerStreamWriter<OrderEvent> responseStream, ServerCallContext context)
{
    await foreach (var e in _events.SubscribeAsync(request.OrderId, context.CancellationToken))
    {
        await responseStream.WriteAsync(e);          // backpressure applies automatically
        if (context.CancellationToken.IsCancellationRequested) break;
    }
}

// client side
using var call = client.StreamEvents(new StreamRequest { OrderId = id },
                                     deadline: DateTime.UtcNow.AddMinutes(5));
await foreach (var e in call.ResponseStream.ReadAllAsync(ct))
    Handle(e);
```

Deadlines in gRPC are propagated across hops, which is a genuine advantage over HTTP timeouts. If service A sets a five-second deadline and calls B, which calls C, then C receives the *remaining* deadline and will abandon work that nobody is waiting for. Not setting a deadline means the call can hang indefinitely — there is no default.

``` csharp
// set a default deadline for every call via an interceptor
public class DeadlineInterceptor(TimeSpan defaultDeadline) : Interceptor
{
    public override AsyncUnaryCall<TRes> AsyncUnaryCall<TReq, TRes>(
        TReq request, ClientInterceptorContext<TReq, TRes> ctx,
        AsyncUnaryCallContinuation<TReq, TRes> continuation)
    {
        if (ctx.Options.Deadline is null)
            ctx = new(ctx.Method, ctx.Host,
                      ctx.Options.WithDeadline(DateTime.UtcNow.Add(defaultDeadline)));
        return continuation(request, ctx);
    }
}
```

<a id="be-a143"></a>

#### GraphQL with Hot Chocolate

GraphQL gives the client control over the response shape: one endpoint, one query, exactly the fields asked for. It trades REST's cacheability and simplicity for flexibility, and it shifts complexity from the client to the server.

``` csharp
public class Query
{
    [UseProjection] [UseFiltering] [UseSorting]
    public IQueryable<Order> GetOrders(AppDb db) => db.Orders;
}

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddProjections().AddFiltering().AddSorting()
    .AddMaxExecutionDepthRule(8)          // stop pathological nested queries
    .ModifyRequestOptions(o => o.Complexity.Enable = true);
```

`[UseProjection]` is the important one: Hot Chocolate rewrites the LINQ query so only the requested columns reach the database, which is what stops GraphQL from being an N+1 generator. Where projection is not possible, use DataLoader to batch.

| Concern                | Answer                                                                      |
|------------------------|-----------------------------------------------------------------------------|
| N+1 queries            | Projections and DataLoader batching                                         |
| Malicious deep queries | Depth limits, complexity scoring, persisted queries                         |
| Caching                | No HTTP caching — cache inside resolvers, or use persisted queries with GET |
| Versioning             | Deprecate fields rather than versioning the schema                          |
| Authorization          | Per-field, not just per-endpoint — more work than REST                      |

**GraphQL earns its place when**

- Many different clients need different shapes
- Mobile bandwidth and round trips matter
- The graph is genuinely graph-shaped
- One team owns the schema

**REST is still better when**

- The API is public and must be simple
- HTTP caching and CDNs matter
- The surface is small and stable
- You want ordinary observability and rate limiting

###### The N+1 problem, and the security surface REST does not have

``` csharp
// GraphQL's defining problem: one query, N database round trips.
// this resolver is called ONCE PER ORDER in the result set.
public class OrderType : ObjectType<Order>
{
    [BindMember(nameof(Order.CustomerId))]
    public async Task<Customer> GetCustomer([Parent] Order order,
                                            ICustomerRepo repo)
        => await repo.GetAsync(order.CustomerId);     // 100 orders = 100 queries
}

// DataLoader batches them into one call per TICK
public class CustomerBatchDataLoader(ICustomerRepo repo, IBatchScheduler s,
                                     DataLoaderOptions o)
    : BatchDataLoader<Guid, Customer>(s, o)
{
    protected override async Task<IReadOnlyDictionary<Guid, Customer>>
        LoadBatchAsync(IReadOnlyList<Guid> keys, CancellationToken ct)
        => await repo.GetManyAsync(keys, ct);         // ONE query, WHERE id = ANY(...)
}

public async Task<Customer> GetCustomer([Parent] Order o,
                                        CustomerBatchDataLoader loader)
    => await loader.LoadAsync(o.CustomerId);
// 100 orders -> 1 query. DataLoader is not optional; it is the thing that
// makes GraphQL viable at all.
```

``` text
  THE SECURITY SURFACE REST DOES NOT HAVE

  in REST, the server decides what a request costs.
  in GraphQL, the CLIENT does. That is the feature, and the risk.

    query {
      orders {
        customer { orders { customer { orders { ... } } } }
      }
    }

  a legal query that is exponentially expensive. Nothing in the schema
  prevents it. Without limits, ONE request takes the service down.

  the mandatory defences
    DEPTH LIMIT           reject beyond N levels
    COMPLEXITY / COST     assign a cost per field; reject over a budget
    PAGINATION REQUIRED   no unbounded list fields, ever
    PERSISTED QUERIES     production clients send a HASH of an
                          allow-listed query. Arbitrary queries are
                          refused entirely. -> the strongest control.
    INTROSPECTION OFF     in production, unless the API is public
    TIMEOUTS              per request, as everywhere else
```

``` csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddProjections().AddFiltering().AddSorting()
    .ModifyRequestOptions(o => o.ExecutionTimeout = TimeSpan.FromSeconds(10))
    .AddMaxExecutionDepthRule(8)
    .ModifyCostOptions(o =>
    {
        o.MaxFieldCost = 1_000;
        o.MaxTypeCost = 1_000;
        o.EnforceCostLimits = true;
    })
    .UsePersistedOperationPipeline()          // production: hash-only
    .ModifyOptions(o => o.EnableDefer = true);

// and authorisation belongs on FIELDS, not just on the endpoint -
// a single query can traverse into data the caller may not see.
[Authorize(Policy = "OrdersRead")]
public IQueryable<Order> GetOrders([Service] AppDbContext db) =>
    db.Orders.Where(o => o.TenantId == _ctx.TenantId);   // filter, ALWAYS
```

**GraphQL earns its cost when**

- Many varied clients with different field needs
- Mobile clients where payload size genuinely matters
- A federated graph over several services
- Rapidly changing frontend requirements
- You have someone who will own the schema

**REST is the better default when**

- One or two known clients
- HTTP caching matters — GraphQL largely forfeits it
- The surface is simple CRUD
- The team is small
- Operational simplicity outweighs client flexibility

<a id="be-a146"></a>

#### Designing a client library for your API

If other teams consume your service, the SDK is part of your product. A good one removes an entire class of support questions.

- Ship a typed client generated from OpenAPI (Kiota, NSwag, Refit) rather than hand-written HTTP.
- Register through `IHttpClientFactory` with an `AddXyzClient()` extension method, so consumers get resilience and telemetry by default.
- Surface errors as typed exceptions or results carrying the ProblemDetails body — not a raw status code.
- Accept and propagate `CancellationToken` on every method.
- Version the package alongside the API, and document the compatibility matrix.
- Never bake in a base URL, a timeout, or a retry policy the consumer cannot override.

``` csharp
public static IServiceCollection AddBillingClient(
    this IServiceCollection services, Action<BillingClientOptions> configure)
{
    services.Configure(configure);
    services.AddHttpClient<IBillingClient, BillingClient>((sp, c) =>
    {
        var o = sp.GetRequiredService<IOptions<BillingClientOptions>>().Value;
        c.BaseAddress = o.BaseAddress;
        c.Timeout = o.Timeout;
    })
    .AddStandardResilienceHandler();
    return services;
}
```

###### What consumers actually need from you

``` csharp
// the shape that ages well
public interface IOrdersClient
{
    Task<OrderDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<OrderDto> CreateAsync(CreateOrderRequest req, string idempotencyKey,
                               CancellationToken ct = default);
    IAsyncEnumerable<OrderDto> ListAsync(OrderQuery query,
                                         CancellationToken ct = default);
}

// four things that matter and are frequently missed:
// 1. CancellationToken on EVERY method, with a default
// 2. an idempotency key parameter that is REQUIRED, not optional,
//    for anything non-idempotent
// 3. IAsyncEnumerable for collections, so pagination is the library's
//    problem rather than every consumer's
// 4. typed exceptions carrying the ProblemDetails, not raw HTTP
public sealed class AcmeApiException(int status, ProblemDetails problem)
    : Exception(problem.Title)
{
    public int StatusCode { get; } = status;
    public ProblemDetails Problem { get; } = problem;
}
```

``` csharp
// pagination handled INSIDE the library - the consumer just enumerates
public async IAsyncEnumerable<OrderDto> ListAsync(OrderQuery q,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    string? cursor = null;
    do
    {
        var page = await GetPageAsync(q with { Cursor = cursor }, ct);
        foreach (var item in page.Items) yield return item;
        cursor = page.NextCursor;
    } while (cursor is not null && !ct.IsCancellationRequested);
}
// note keyset pagination, not offset: stable when rows are inserted between
// pages, and constant time regardless of depth.
```

| Decision                | Guidance                                                                                   |
|-------------------------|--------------------------------------------------------------------------------------------|
| Generate or hand-write? | Generate from OpenAPI (Kiota, NSwag) for breadth; hand-write the ergonomic surface over it |
| Bundle resilience?      | **No.** Expose `HttpClient` configuration and let the consumer own retry policy            |
| Register with DI        | Ship an `AddAcmeClient(this IServiceCollection, Action<Options>)` extension                |
| Logging                 | Accept an `ILogger`; never write to `Console`                                              |
| Target framework        | Multi-target — do not force consumers onto your version                                    |
| Versioning              | Semver strictly, and ship a codemod or migration note for breaking changes                 |
| Dependencies            | **Minimise ruthlessly.** Every one you take, every consumer takes                          |

> **\[Trap\]**
>
> **Common trap.** A client library that takes a heavy dependency — a specific Polly version, a JSON library, a logging framework — forces that version on every consumer, and conflicts with whatever they already use. This is the single most common reason a well-intentioned client library gets abandoned in favour of hand-written `HttpClient` calls. Depend on abstractions in the BCL, keep the dependency list near empty, and let the consumer bring their own everything.

<a id="be-a147"></a>

#### Async request-reply over HTTP

Some operations cannot complete inside a request: generating a large report, processing an upload, running a batch. Holding the HTTP connection open for two minutes fails against every load balancer, gateway timeout and mobile network in the path. The standard answer is to accept the work, return immediately, and let the client follow up.

``` text
  POST /exports                          -> 202 Accepted
                                            Location: /exports/9f2a/status
                                            Retry-After: 5

  GET /exports/9f2a/status               -> 200 { state: "running", progress: 0.4 }
                                            Retry-After: 5
  GET /exports/9f2a/status               -> 303 See Other
                                            Location: /exports/9f2a/result

  GET /exports/9f2a/result               -> 200 (or 302 to a pre-signed blob URL)

  failure case:
  GET /exports/9f2a/status               -> 200 { state: "failed",
                                                  error: { code: "...", message: "..." } }
    note: 200, not 500. The STATUS request succeeded; the job is what failed.
```

``` csharp
app.MapPost("/exports", async (ExportRequest req, IJobs jobs, HttpContext ctx, CancellationToken ct) =>
{
    var job = await jobs.EnqueueAsync(req, ctx.User.GetTenantId(), ct);

    ctx.Response.Headers.Location = $"/exports/{job.Id}/status";
    ctx.Response.Headers["Retry-After"] = "5";
    return Results.Accepted($"/exports/{job.Id}/status",
        new { id = job.Id, state = job.State.ToString() });
});

app.MapGet("/exports/{id}/status", async (Guid id, IJobs jobs, CancellationToken ct) =>
{
    var job = await jobs.FindAsync(id, ct);
    if (job is null) return Results.NotFound();

    return job.State switch
    {
        JobState.Succeeded => Results.Extensions.SeeOther($"/exports/{id}/result"),
        JobState.Failed    => Results.Ok(new { state = "failed", error = job.Error }),
        _                  => Results.Ok(new { state = job.State.ToString().ToLowerInvariant(),
                                               progress = job.Progress })
    };
});
```

###### The design decisions this pattern forces

| Decision                               | Options                                    | Guidance                                                                                       |
|----------------------------------------|--------------------------------------------|------------------------------------------------------------------------------------------------|
| Who generates the job id?              | Server, or client-supplied idempotency key | Accept a client key so a retried POST returns the *same* job rather than starting a second one |
| Where does status live?                | A table, or the job runner's own store     | A table you own — you need it for support queries anyway                                       |
| How does the client learn it finished? | Polling, webhook, SignalR, or SSE          | Polling always as the baseline; push as an optimisation                                        |
| What is the result?                    | Inline JSON, or a link to blob storage     | A pre-signed URL for anything large — do not proxy bytes                                       |
| How long is status retained?           | Hours to days                              | Long enough for support to investigate; then archive the record, delete the payload            |
| What about progress?                   | Percentage, or item counts                 | Coarse is fine; do not write to the database on every item                                     |

``` csharp
// idempotent submission: the same key returns the same job
public async Task<Job> EnqueueAsync(ExportRequest req, string tenantId, string idempotencyKey,
                                    CancellationToken ct)
{
    var existing = await _db.Jobs.FirstOrDefaultAsync(
        j => j.TenantId == tenantId && j.IdempotencyKey == idempotencyKey, ct);
    if (existing is not null) return existing;      // no duplicate work, no duplicate charge

    var job = Job.Create(req, tenantId, idempotencyKey, _time.GetUtcNow());
    _db.Jobs.Add(job);
    _db.Outbox.Add(OutboxMessage.From(new ExportRequested(job.Id), _time.GetUtcNow()));
    await _db.SaveChangesAsync(ct);                 // job + message, one transaction
    return job;
}
```

> **\[Trap\]**
>
> **Common trap.** A polling client with no `Retry-After` and no backoff will hammer the status endpoint every 100 ms. Send the header, honour it in your own SDK, and rate-limit the status endpoint — otherwise the polling traffic exceeds the real traffic, which is a genuinely common outcome.

<a id="be-a153"></a>

#### OData and query-capable APIs

OData lets clients express filtering, sorting, projection, paging and expansion in the URL. In enterprise .NET it is common and load-bearing — Dynamics, SharePoint, Business Central, SAP integrations and Power BI all speak it — and it is a genuine time-saver for internal admin surfaces where the alternative is fifty bespoke query endpoints.

``` text
GET /odata/Orders
    ?$filter=Status eq 'Open' and Total gt 1000 and PlacedAt ge 2026-07-01T00:00:00Z
    &$orderby=PlacedAt desc
    &$select=Id,Total,PlacedAt
    &$expand=Customer($select=Name)
    &$top=25&$skip=50&$count=true
```

``` csharp
var model = new ODataConventionModelBuilder();
model.EntitySet<Order>("Orders");
model.EntitySet<Customer>("Customers");

builder.Services.AddControllers().AddOData(o => o
    .AddRouteComponents("odata", model.GetEdmModel())
    .Select().Filter().OrderBy().Expand().Count().SetMaxTop(100));   // bound everything

[EnableQuery(MaxExpansionDepth = 2, MaxTop = 100,
             AllowedQueryOptions = AllowedQueryOptions.Select | AllowedQueryOptions.Filter
                                 | AllowedQueryOptions.OrderBy | AllowedQueryOptions.Top
                                 | AllowedQueryOptions.Skip   | AllowedQueryOptions.Count)]
public IQueryable<Order> Get()
    => _db.Orders.AsNoTracking().Where(o => o.TenantId == _tenant.TenantId);
    //                                 ^^^ the tenant filter is applied BEFORE OData sees it,
    //                                     so no query option can escape it
```

###### The risks, and how to make it safe

| Risk                                | Mitigation                                                                        |
|-------------------------------------|-----------------------------------------------------------------------------------|
| Unbounded result sets               | `SetMaxTop` and a server-driven default page size                                 |
| Arbitrary `$expand` depth           | `MaxExpansionDepth`; whitelist expandable navigations                             |
| Filtering on unindexed columns      | Restrict filterable properties with `[Filter("Status", "PlacedAt")]`              |
| Authorization bypass via projection | Never expose entities directly; project to a DTO whose shape is already safe      |
| Tenant leakage                      | Apply the tenant predicate in the controller before returning `IQueryable`        |
| Query cost explosion                | Log the generated SQL and duration per request; alert on outliers                 |
| Schema as public contract           | Expose an OData model over DTOs, not over EF entities, so refactoring is possible |

**OData is a good fit for**

- Internal admin and back-office grids
- Power BI, Excel and reporting-tool consumers
- Integrations with Microsoft enterprise products
- Read-only surfaces over a well-indexed model

**Avoid it for**

- Public APIs — you are exposing your data model
- Write operations of any complexity
- Anything where you need to guarantee query cost
- Mobile clients (GraphQL fits better)

The strategic point applies to GraphQL equally: any API that lets clients compose arbitrary queries has handed them the ability to write your database's worst query. That is acceptable behind authentication, on a well-indexed read model, with bounded options and per-request cost monitoring. It is not acceptable on an unbounded public endpoint over your transactional tables, and the difference is entirely in the constraints you configure rather than in the technology.
