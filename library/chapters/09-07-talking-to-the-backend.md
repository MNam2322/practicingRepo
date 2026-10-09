---
id: "9-7"
number: "9.7"
title: "Talking to the backend"
part: 9
part_title: "Building a frontend"
words: 5030
minutes: 22
---

# 9.7 Talking to the backend

> **Quick look.** The frontend's side of the conversation with a slow, unreliable backend.
>
> - fetch resolves for every HTTP status: check response.ok and convert Problem Details into errors in one wrapper.
> - Give every call a timeout and cancel stale ones with AbortController; only the latest request may update the screen.
> - Show loading, empty and error states locally rather than failing the whole page.
> - Let a server-state library cache, deduplicate, refresh and retry; use optimistic updates with rollback.
> - Retry only temporary failures of safe requests.
> - Configure CORS with explicit origins (or serve from one origin); never keep tokens in localStorage. Push live updates with SSE, WebSockets or SignalR.


### The big picture

Nearly every screen in a modern web app is a view of data that lives on a server. The flight list, the seat map and the booking summary are all server state, fetched over the network, as Components and state (9.4) described. And the network is slow, unreliable and unpredictable in exactly the ways Layers and packets (6.1) listed. Responses arrive late, fail halfway, or arrive in a different order from the requests.

This chapter covers the frontend's side of the conversation with the backend. It explains `fetch`, and why it doesn't treat a 404 as an error. It covers timeouts and cancellation, responses that arrive out of order, and turning Problem Details into useful messages. It then covers caching server state with a data library, retries, the CORS rules that decide which origins may call your API, and live updates with polling, server-sent events, WebSockets and SignalR. The example runs a small server and a client together in one file with Node, so every behaviour in it is real. Its comments show the output of an actual run. This chapter ends Part 9.

### Before you start

**Builds on:** 9.4 Components and state, 6.1 Layers and packets, 8.5 Validation and errors at the boundary, 9.3 TypeScript, 8.2 API design, 5.1 Why concurrency is hard.

**Ideas explained later:**

- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Authentication** (10.2): Establishing who a caller is.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **Session cookie** (10.2): A cookie that identifies a signed-in session.
- **Cross-site request forgery** (10.4): Making a user's browser send an unwanted, authenticated request to a site from another site. Often shortened to CSRF.
- **Access token** (10.3): A credential a client sends to an API, saying what it may do there.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.

**Tools and technologies named here:**

**The code uses:** lambdas. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Does fetch reject its promise when the server returns 500?**

> No. `fetch` only rejects when the request couldn't be completed at all, such as a network failure, a blocked CORS request or an abort. Any HTTP response, including 404 and 500, resolves normally, so code must check `response.ok` or the status.

**2. How do you give a fetch call a timeout?**

> Pass a signal: `fetch(url, { signal: AbortSignal.timeout(5000) })`. When the time is up, the request is aborted and the promise rejects with a `TimeoutError`. `AbortSignal.any` combines a timeout with a manual cancel signal.

**3. A search box sends a request on every keystroke. What can go wrong, and how do you fix it?**

> An earlier, slower response can arrive after a later one and overwrite it, so the results don't match the text. Cancel the previous request when starting a new one, or ignore responses that aren't for the latest request.

**4. What is CORS, and whose job is it to configure it?**

> Cross-Origin Resource Sharing: HTTP headers with which a server says which other origins may read its responses. The browser enforces it, but only the server can grant access, by sending headers such as `Access-Control-Allow-Origin`.

**5. What is a preflight request?**

> An automatic `OPTIONS` request the browser sends before a cross-origin request that isn't "simple", for example one with a JSON body or a custom header, to ask the server whether the real request is allowed.

**6. When should a frontend retry a failed request automatically?**

> When the failure is likely temporary, such as a network error, 502, 503 or 504, and the request is safe to repeat: a GET, or a POST protected by an idempotency key. Use backoff, and respect `Retry-After`.

**7. When would you choose server-sent events over WebSockets?**

> When updates only flow from server to client, such as live departure boards or notifications. Server-sent events are plain HTTP, reconnect automatically and pass through proxies easily. WebSockets are for two-way, high-frequency messaging.

### The network is not a function call

A call to the backend looks like a function call, and it isn't one. It can take a millisecond or thirty seconds. It can fail after the server has already done the work. Two calls made in one order can complete in the other. The user can leave the page while it is still running. Code that treats `await api(...)` like a local method call works in development and breaks on a train with a weak signal.

Every call needs answers to the same few questions: how long to wait, what to show meanwhile, what to do with an error, whether to retry, and what happens if a newer call overtakes it.

### fetch

The **Fetch API** is the browser's built-in way to make HTTP requests, available in Node.js too. `fetch(url, options)` returns a promise of a `Response`. The options set the method, headers, body, and whether to send cookies. The response gives the status, headers and a body that can be read as JSON, text or a stream.

`fetch` has one behaviour that surprises almost everyone. Its promise only rejects when no response arrived at all: a network failure, a request blocked by CORS, or an abort. Any HTTP response, 404 and 500 included, resolves normally. So every call must check `response.ok`, which is true for statuses 200 to 299, and handle the rest. Most apps wrap `fetch` in a small function that does this once, as the example's `api` function does. It turns error responses with Problem Details bodies, from Validation and errors at the boundary (8.5), into exceptions carrying the status, title, type and trace id.

The body that comes back is whatever the server sent. A typed wrapper such as `api<Flight[]>()` only tells the compiler what to expect, as TypeScript (9.3) warned. Validate responses with a schema where a mismatch would be costly, and generate the types from the backend's OpenAPI document, from API design (8.2), so they at least start out correct.

### Timeouts and cancellation

Browsers don't give up on a slow request quickly by default; a call to a stuck server can hang for minutes. Give every call a limit. An **AbortController** produces a signal that can cancel a request: pass `controller.signal` to `fetch`, and `controller.abort()` cancels it, rejecting the promise with an `AbortError`. `AbortSignal.timeout(ms)` creates a signal that aborts itself after a delay, rejecting with a `TimeoutError`, and `AbortSignal.any([a, b])` combines signals, so a request stops for whichever comes first.

Cancellation matters beyond timeouts. When a user leaves a screen, its requests should stop, so they don't waste bandwidth or update a component that no longer exists. Frameworks and data libraries pass abort signals for you, and React effects and Angular's `takeUntilDestroyed` give a place to cancel.

### Responses out of order

A search box that fetches results as the user types sends a request for "s", then one for "sy". If the "s" request is slower, its response arrives second, and the page shows results for "s" under a box that says "sy". That is a **stale response**: an answer to a question the user is no longer asking. It is the race condition from Why concurrency is hard (5.1), in the browser.

The fix is to make sure only the latest request can update the screen. Abort the previous request when starting a new one, as the example's `search` function does, or tag each request and ignore responses that aren't for the latest tag. RxJS's `switchMap`, from Angular today (9.6), does the first automatically. Debouncing the input, as in React today (9.5), also cuts the number of requests.

### Loading, errors and empty results

Every piece of server state is in one of several conditions: not yet loaded, loading, loaded with data, loaded but empty, or failed. Each needs something on screen. A spinner or placeholder while loading, laid out at the final size so the page doesn't jump, as How a browser shows a page (9.1) warned. A clear message and a retry button on failure, using the Problem Details title rather than a raw status code, with the trace id available so support can find the server's logs. A helpful empty state, "No flights match", rather than a blank space.

Failures should be as local as possible: a failed seat map shouldn't blank the whole booking page.

### Seeing it for real

This file starts a small backend with Node's `http` module, then acts as a browser-style client against it. It shows the error wrapper, a timeout, the out-of-order search problem and its fix, a retry after a temporary failure, and a stream of server-sent events. It runs with Node, which strips the types, and passes a strict type check. On the first attempt, the error class used constructor parameter properties, and Node refused to run it: they aren't erasable syntax, exactly as A Node and TypeScript backend (8.4) warned. The version below declares the fields instead.

```typescript
// client.ts: run with: node client.ts   (a tiny backend and a browser-style client in one file)
import { createServer } from "node:http";

// ---- A small backend to talk to ---------------------------------------------------------------
let flakyCalls = 0;
const server = createServer((req, res) => {
  const url = new URL(req.url!, "http://localhost");
  const json = (status: number, body: unknown, type = "application/json") => {
    res.writeHead(status, { "Content-Type": type }).end(JSON.stringify(body));
  };
  if (url.pathname === "/api/flights") {
    const search = url.searchParams.get("search") ?? "";
    const delay = search === "s" ? 300 : 50;              // the first, shorter search is slower to answer
    setTimeout(() => json(200, ["Sydney", "Singapore", "San Francisco"].filter(c => c.toLowerCase().startsWith(search))), delay);
  } else if (url.pathname === "/api/bookings" && req.method === "POST") {
    json(409, { type: "https://example.com/problems/flight-full", title: "Flight is full", status: 409, traceId: "00-4bf9" }, "application/problem+json");
  } else if (url.pathname === "/api/slow") {
    setTimeout(() => json(200, { late: true }), 2000);
  } else if (url.pathname === "/api/flaky") {
    if (++flakyCalls === 1) json(503, { title: "Service Unavailable", status: 503 }, "application/problem+json");
    else json(200, { gate: "B12" });
  } else if (url.pathname === "/api/departures/live") {
    res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
    let n = 0;
    const timer = setInterval(() => {
      res.write(`event: gate\ndata: ${JSON.stringify({ flight: "BA117", gate: ["A10", "B12", "C3"][n] })}\n\n`);
      if (++n === 3) { clearInterval(timer); res.end(); }
    }, 50);
  } else json(404, { title: "Not Found", status: 404 }, "application/problem+json");
});
await new Promise<void>(resolve => server.listen(0, "127.0.0.1", resolve));
const base = `http://127.0.0.1:${(server.address() as { port: number }).port}`;

// ---- The client -------------------------------------------------------------------------------
type Problem = { title?: string; type?: string; traceId?: string };           // the Problem Details shape from 2.8

class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem;
  constructor(status: number, problem: Problem) {                              // plain fields: erasable syntax, as in 8.4
    super(problem.title ?? `HTTP ${status}`);
    this.status = status;
    this.problem = problem;
  }
}

async function api<T>(path: string, init: RequestInit & { timeoutMs?: number } = {}): Promise<T> {
  const timeout = AbortSignal.timeout(init.timeoutMs ?? 5000);                 // every call gets a time limit
  const signal = init.signal ? AbortSignal.any([init.signal, timeout]) : timeout;   // and can also be cancelled
  const response = await fetch(base + path, { ...init, signal, headers: { Accept: "application/json", ...init.headers } });
  const body = response.status === 204 ? undefined : await response.json();
  if (!response.ok) throw new ApiError(response.status, body ?? {});         // fetch only rejects on network failure
  return body as T;                                                            // real code would validate here, as in 8.5
}

// 1. Errors: a 409 is not an exception to fetch, so the wrapper turns Problem Details into one
try { await api("/api/bookings", { method: "POST", body: JSON.stringify({ flight: "BA117" }) }); }
catch (e) { if (e instanceof ApiError) console.log(`booking failed: ${e.status} ${e.message} (trace ${e.problem.traceId})`); }
// booking failed: 409 Flight is full (trace 00-4bf9)

// 2. Timeouts: give up on a slow call instead of waiting forever
try { await api("/api/slow", { timeoutMs: 200 }); }
catch (e) { console.log(`slow call: ${(e as Error).name}`); }              // slow call: TimeoutError

// 3. Out-of-order responses: the user types "s" then "sy"; the answer to "s" arrives last
let shown: string[] = [];
await Promise.all(["s", "sy"].map(async q => { shown = await api<string[]>(`/api/flights?search=${q}`); }));
console.log(`naive: shows ${JSON.stringify(shown)} for "sy"`);                // naive: shows ["Sydney","Singapore","San Francisco"]: stale
let current: AbortController | undefined;
async function search(q: string) {
  current?.abort();                                                            // cancel the previous search
  current = new AbortController();
  try { shown = await api<string[]>(`/api/flights?search=${q}`, { signal: current.signal }); }
  catch (e) { if ((e as Error).name !== "AbortError") throw e; }              // a cancelled search is not an error
}
await Promise.all([search("s"), search("sy")]);
console.log(`latest wins: shows ${JSON.stringify(shown)}`);                   // latest wins: shows ["Sydney"]

// 4. Retrying a safe request after a temporary failure, with a short backoff
async function withRetry<T>(call: () => Promise<T>, attempts = 3): Promise<T> {
  for (let i = 1; ; i++) {
    try { return await call(); }
    catch (e) {
      const retryable = e instanceof ApiError && [502, 503, 504].includes(e.status);
      if (!retryable || i === attempts) throw e;
      console.log(`attempt ${i} got ${(e as ApiError).status}, retrying`);
      await new Promise(r => setTimeout(r, 100 * 2 ** (i - 1)));
    }
  }
}
console.log(await withRetry(() => api<{ gate: string }>("/api/flaky")));      // attempt 1 got 503, retrying / { gate: 'B12' }

// 5. Live updates with server-sent events: one long response, one message per event
const live = await fetch(base + "/api/departures/live");
const decoder = new TextDecoderStream();
let buffer = "";
for await (const chunk of live.body!.pipeThrough(decoder)) {
  buffer += chunk;
  let end: number;
  while ((end = buffer.indexOf("\n\n")) >= 0) {                               // a blank line ends each event
    const event = buffer.slice(0, end); buffer = buffer.slice(end + 2);
    const data = event.split("\n").find(line => line.startsWith("data: "))!.slice(6);
    console.log(`live: ${data}`);
  }
}
// live: {"flight":"BA117","gate":"A10"}   then B12, then C3
server.close();
```

In the naive version, both searches write to the same variable, and whichever answers last wins, which here is the older, slower one. In the fixed version, starting the "sy" search aborts the "s" request, so its late answer never arrives.

### Server state libraries

Hand-writing loading flags, caching, retries and refreshing for every request quickly becomes repetitive and inconsistent. A **server-state library** does it for you. TanStack Query, available for React, Angular, Vue and others, is the most widely used, and SWR and RTK Query are common in React apps. Angular's resources, from Angular today (9.6), cover similar ground.

Each piece of data is fetched under a **query key**, such as `["departures", "LHR"]`, which works like a cache key from Caching (8.6). The library then provides several things. It caches results by key and deduplicates identical requests made at the same time, the single-flight idea again. It returns cached data immediately and refetches in the background when data is older than a configured freshness time: stale-while-revalidate, applied in the browser. It refetches when the window regains focus or the network reconnects. It retries failed queries with backoff. And it offers mutations for writes, with invalidation of the queries a write affects, so the booking list refreshes after a new booking.

An **optimistic update** shows the expected result of a change immediately, before the server confirms it, such as marking a seat as held the moment it is clicked. If the server rejects the change, the update is rolled back and an error shown. It makes apps feel instant, and it suits changes that almost always succeed. It is the interface version of optimistic concurrency from Transactions and isolation (7.4). React's `useOptimistic` and the libraries' mutation helpers handle the rollback.

### Retries

Some failures are temporary: a dropped connection, a 502 from a proxy, a 503 from a server restarting during a deployment. Retrying these once or twice, with a short exponential backoff, from Errors and exceptions (2.8), hides many brief problems from users. Respect a `Retry-After` header when the server sends one, especially with 429 and 503.

Only retry requests that are safe to repeat. GET, PUT and DELETE are idempotent. A POST that creates or charges something needs an idempotency key, from API design (8.2), so a retry after a lost response doesn't book twice. Never retry client errors such as 400, 401, 403 or 409; they will fail the same way again.

### CORS

The same-origin policy, from How a browser shows a page (9.1), stops a page's scripts from reading responses from another origin. That is a problem when the frontend is served from `https://www.airline.example` and the API lives at `https://api.airline.example`, which is a different origin. **CORS**, Cross-Origin Resource Sharing, is the standard way for the API to say which other origins may read its responses. It is a set of HTTP response headers the server sends, such as `Access-Control-Allow-Origin: https://www.airline.example`, and the browser enforces them.

Some requests are more than a simple GET or form post, such as any request with a JSON body, an `Authorization` header or a custom header. For those, the browser first sends a **preflight request**: an `OPTIONS` request asking whether the real one is allowed, with `Access-Control-Request-Method` and `Access-Control-Request-Headers`. Only if the server's answer permits it does the real request go out. Preflight answers can be cached with `Access-Control-Max-Age`.

A request that sends cookies across origins, a credentialed request made with `credentials: "include"`, needs the server to send `Access-Control-Allow-Credentials: true` and name the exact origin; the wildcard `*` isn't accepted with credentials. In ASP.NET Core, CORS is a policy registered with `AddCors` and applied with `UseCors` in the pipeline from A modern .NET backend (8.3):

```csharp
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins("https://www.airline.example")                 // an explicit allowlist, never "any origin" with credentials
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Content-Type", "Authorization", "Idempotency-Key")
    .AllowCredentials()));
// ...
app.UseCors("web");                                               // after routing, before authentication and the endpoints
```

CORS errors appear in the browser console, and the fix is always on the server. Two things are worth remembering. CORS protects users' browsers, not your API: other programs, such as scripts and servers, ignore it completely, so it is no substitute for authentication. And the simplest way to avoid it is not to need it: serve the frontend and the API from the same origin, through a reverse proxy or a backend for frontend, from A Node and TypeScript backend (8.4).

### Who is calling

Most API calls need to say who the user is. Browser apps do this in two main ways, covered in detail in Authentication and authorization (10.2). With cookies, the server sets an `HttpOnly` session cookie that scripts can't read, and the browser attaches it automatically, which needs the cookie rules from HTTP (6.6) and protection against cross-site request forgery. With bearer tokens, the app attaches an access token to each request in an `Authorization` header. A token stored where scripts can read it, such as `localStorage`, can be stolen by any cross-site scripting flaw, as Common web vulnerabilities (10.4) explains. That is why current guidance for browser apps favours a backend for frontend that keeps tokens on the server and gives the browser only an `HttpOnly` cookie.

### Live updates

Some screens must change without the user doing anything: a departure board, a chat, a booking whose status changes. There are four main ways to push updates to a browser.

**Polling** asks the server for changes at an interval, such as every 30 seconds. It is simple and works everywhere, but it is always slightly out of date, and it costs requests even when nothing has changed.

**Long polling** sends a request that the server holds open until something changes or a timeout passes, then answers; the client immediately asks again. Updates arrive quickly, at the cost of many held connections.

**Server-sent events** (SSE) keep one HTTP response open and stream events down it as text, each ending in a blank line, as the example shows. Browsers read them with the built-in `EventSource`, which reconnects automatically and tells the server the last event it received. They only flow from server to client, which suits most live displays.

A **WebSocket** upgrades an HTTP connection into a two-way channel for messages in both directions, suited to chat, collaborative editing and games. WebSockets need more care with proxies, load balancers, reconnection and authentication.

In .NET, **SignalR** wraps these transports. The server defines hubs whose methods clients call, and pushes messages to all clients, groups or individual users. The client library picks WebSockets, server-sent events or long polling automatically. Running several server instances needs a backplane, such as Redis, or the Azure SignalR Service, so a message sent from one instance reaches clients connected to another. That is the stateless-service problem from What a backend does (8.1) again: connections are state.

### Files and large payloads

Uploads use `FormData` with a file input, sent as a multipart request. For large files, a common pattern avoids passing them through your API at all. The API returns a short-lived **pre-signed URL** that lets the browser upload directly to object storage, from Storage and files (1.6), and the API is told when it is done. Downloads of large reports work the same way in reverse.

For users on poor connections, a **service worker**, a script the browser runs in the background between the page and the network, can cache responses, serve pages offline and retry requests later. It is what turns a site into a progressive web app that works offline.

### In short

Calls to the backend are slow, fallible and unordered. `fetch` resolves for every HTTP response, so check `response.ok` and turn Problem Details into errors in one wrapper. Give every call a timeout and cancel it when it is no longer needed, with AbortController signals. Make sure only the latest request updates the screen. Show loading, empty and error states locally. Let a server-state library cache, deduplicate, refresh and retry, and use optimistic updates with rollback for changes that usually succeed. Retry only temporary failures of safe requests. CORS is configured on the server with explicit origins, and avoided altogether by serving from one origin. Push live updates with server-sent events, WebSockets or SignalR.

### What goes wrong

**Treating 404 and 500 as success.** Code reads an error body as data because `fetch` didn't reject. Check `response.ok` in one shared wrapper.

**No timeouts.** A stuck request leaves a spinner turning forever. Put a time limit on every call.

**Stale responses.** A slow earlier request overwrites newer results. Cancel superseded requests, or ignore responses that aren't for the latest one.

**Whole-page failure.** One failed widget blanks the screen. Handle errors per piece of data, with a retry.

**Retrying unsafe requests.** A retried POST creates a second booking. Retry only idempotent requests, or use idempotency keys.

**CORS fixed with a wildcard.** `AllowAnyOrigin` with credentials, or reflecting any origin back, lets any site make authenticated calls on users' behalf. Allow a specific list of origins.

**Tokens in `localStorage`.** Any injected script can read and steal them. Prefer `HttpOnly` cookies or a backend for frontend.

**Polling too often.** Thousands of clients polling every second load the server with requests that mostly find nothing new. Use longer intervals, server-sent events or WebSockets.

### Cost model

A request's cost to the user is mostly round trips: 20 to 300 ms each on mobile networks, so a screen that needs five sequential calls feels slow however fast the server is. Run independent requests in parallel, and design APIs so one screen needs few calls. A preflight adds a full extra round trip before each non-simple cross-origin request unless cached, which same-origin serving avoids entirely. Caching server state in the browser turns repeat visits into instant renders. Polling costs one request per client per interval whether or not anything changed; server-sent events and WebSockets cost one held connection per client, which servers can hold by the tens of thousands.

### In other languages (optional)

**Angular.** `HttpClient` returns observables, interceptors add headers and handle errors centrally, and `switchMap` cancels superseded requests, as Angular today (9.6) showed.

**Blazor.** Components call the backend with `HttpClient`, the same class as on the server, and SignalR has a first-class .NET client.

**Mobile apps.** The same concerns apply, more so: networks are worse, apps are suspended in the background, and offline handling is expected. Native HTTP clients, WebSockets and push notifications play the same roles.

### Key terms

- **Fetch API**: The browser's built-in function for making HTTP requests, returning a promise of a response.
- **AbortController**: An object whose signal cancels requests and other async work when aborted.
- **Stale response**: A response to an earlier request that arrives after a newer one and would show outdated results.
- **Server-state library**: A library that fetches, caches, deduplicates, refreshes and retries server data for the frontend.
- **Query key**: The key under which a server-state library caches one piece of fetched data.
- **Optimistic update**: Showing the expected result of a change immediately, and rolling it back if the server rejects it.
- **CORS**: Cross-Origin Resource Sharing: headers with which a server lets other origins read its responses.
- **Preflight request**: An automatic OPTIONS request a browser sends to ask whether a cross-origin request is allowed.
- **Polling**: Asking the server for changes at a regular interval.
- **Long polling**: Holding a request open on the server until there is news, then immediately asking again.
- **Server-sent events**: A one-way stream of text events from server to browser over one long HTTP response. Often shortened to SSE.
- **WebSocket**: A two-way message channel between browser and server, upgraded from an HTTP connection.
- **SignalR**: The .NET library for real-time messaging, choosing between WebSockets, server-sent events and long polling.
- **Pre-signed URL**: A short-lived URL that grants permission to upload or download one object directly from storage.
- **Service worker**: A script a browser runs in the background between a page and the network, able to cache and work offline.

### Where this leads

This ends Part 9. Part 10 turns to security, starting with Thinking like an attacker (10.1). How users sign in and how requests prove who they come from is in Authentication and authorization (10.2). Cross-site scripting, cross-site request forgery and CORS mistakes are in Common web vulnerabilities (10.4). Timeouts, retries and backoff across whole systems are in Reliability (12.5).

### Check yourself

1. In `client.ts`, make `/api/flaky` fail three times, and show that `withRetry` gives up with the 503 after its last attempt.
2. Add a `Retry-After: 1` header to the 503 response and make `withRetry` wait for that many seconds when it is present.
3. Change the naive search so it ignores stale responses by comparing a request counter, without using AbortController.
4. Call an API on another origin from a page served locally, read the CORS error in the browser console, and fix it by configuring CORS on the server.
5. In a frontend you know, find a screen that loads server data. Check what it shows while loading, when the request fails, and when the result is empty.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A 500 treated as success

The booking page shows "Saved!" even when the server returned 500, because fetch only rejects on network errors.

*One wrapper for every call*

```typescript
export async function api<T>(url: string, init: RequestInit = {}, timeoutMs = 10_000): Promise<T> {
  const res = await fetch(url, { ...init, signal: AbortSignal.timeout(timeoutMs),
    headers: { "Content-Type": "application/json", ...init.headers } });
  if (!res.ok) {
    const problem = await res.json().catch(() => ({ title: res.statusText }));
    throw Object.assign(new Error(problem.title ?? "Request failed"), { status: res.status, problem });
  }
  return res.status === 204 ? (undefined as T) : res.json();
}
```

> **Lesson:** fetch resolves for any HTTP status. Check response.ok, add a timeout, and turn Problem Details into errors in one place.

#### Example 2: An optimistic seat selection

Selecting a seat waits 600 ms for the server before turning blue, so users click twice.

*Update first, roll back on failure*

```typescript
const qc = useQueryClient();
const select = useMutation({
  mutationFn: (seat: string) => api(`/api/bookings/${id}/seat`, { method: "PUT", body: JSON.stringify({ seat }) }),
  onMutate: async (seat) => {
    await qc.cancelQueries({ queryKey: ["booking", id] });
    const prev = qc.getQueryData<Booking>(["booking", id]);
    qc.setQueryData<Booking>(["booking", id], b => b && { ...b, seat });
    return { prev };
  },
  onError: (_e, _seat, ctx) => qc.setQueryData(["booking", id], ctx?.prev),   // roll back
  onSettled: () => qc.invalidateQueries({ queryKey: ["booking", id] }),
});
```

> **Lesson:** For changes that usually succeed, update the screen immediately and roll back if the server says no.
