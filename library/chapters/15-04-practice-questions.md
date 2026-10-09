---
id: "15-4"
number: "15.4"
title: "Practice questions"
part: 15
part_title: "Reference"
words: 19830
minutes: 86
---

# 15.4 Practice questions

> **Quick look.** 307 practice questions with hidden answers, for self-testing.
>
> - Grouped into decks: C# and .NET, platform, design patterns, JavaScript and TypeScript, databases, Linux, data and LINQ, practical, APIs and security, plus one question for each casebook case (4.9, 7.9).
> - Answer each in your head, then open it to compare.
> - Good for a quick review before an interview or after reading a part.


Questions with hidden answers, taken from the Backend Engineering volume's practice decks. Try each one before opening the answer.

### C# and .NET

#### Language & Runtime

<details><summary>What is the difference between a value type and a reference type in terms of <em>what the variable holds</em>?</summary>

<p>A value-type variable holds the data itself; a reference-type variable holds a reference (address) to data elsewhere. Assignment copies the data for value types, and copies the reference &mdash; not the object &mdash; for reference types.</p>

</details>

<details><summary>Nullable reference types: does <code>string</code> (non-nullable) guarantee the value won't be null at runtime?</summary>

<p>No. It is a claim checked by the compiler's flow analysis, not enforced by the runtime. Anything crossing a boundary the compiler cannot analyse &mdash; JSON deserialisation, EF materialisation, reflection, unannotated libraries &mdash; can still put a null there. Validate at the edges.</p>

</details>

<details><summary>What does the null-forgiving operator <code>!</code> actually do?</summary>

<p>Nothing at runtime. It silences the analyser &mdash; a note saying &ldquo;trust me&rdquo;. Acceptable after a test assertion; a code smell in a request path.</p>

</details>

<details><summary>Why is <code>List&lt;Dog&gt;</code> not assignable to <code>List&lt;Animal&gt;</code>, while <code>IEnumerable&lt;Dog&gt;</code> is assignable to <code>IEnumerable&lt;Animal&gt;</code>?</summary>

<p>Variance exists only on interfaces and delegates, and only when the type parameter is used in one direction. <code>IEnumerable&lt;out T&gt;</code> only produces T, so covariance is safe. <code>List&lt;T&gt;</code> both produces and consumes &mdash; if it were covariant you could Add a Cat to a List&lt;Dog&gt;.</p>

</details>

<details><summary>What do static abstract interface members (C# 11) enable that generics could not do before?</summary>

<p>Calling static members through a type parameter &mdash; e.g. <code>T.Zero</code> or <code>T.Parse</code> &mdash; which is what makes generic math (<code>where T : INumber&lt;T&gt;</code>) possible with no boxing and no reflection.</p>

</details>

<details><summary>Same lambda body assigned to <code>Func&lt;T,bool&gt;</code> versus <code>Expression&lt;Func&lt;T,bool&gt;&gt;</code> — what is the difference?</summary>

<p><code>Func</code> compiles to executable code (fast, opaque). <code>Expression</code> compiles to a data structure describing the code, which another library can read and translate. That is why the same <code>Where</code> clause becomes SQL on IQueryable and runs in memory on IEnumerable.</p>

</details>

<details><summary>A multicast delegate with a return value is invoked. Which handler's result do you get?</summary>

<p>Only the last one; the rest are silently discarded. Also, one throwing handler aborts the remaining invocations. If every subscriber matters, iterate <code>GetInvocationList()</code> and aggregate yourself.</p>

</details>

<details><summary>Why is <code>async void</code> dangerous outside event handlers?</summary>

<p>Its exceptions cannot be caught by the caller &mdash; there is no Task to observe &mdash; so they reach the runtime and take the process down. Keep the body inside try/catch, or route to an awaitable method.</p>

</details>

<details><summary>In a switch expression, why does arm order matter, and what does the compiler catch?</summary>

<p>Arms are evaluated top to bottom; the first match wins, so specific patterns must precede general ones. The compiler detects subsumed (unreachable) arms, but exhaustiveness is only fully enforced for closed shapes &mdash; an enum can hold undeclared values, so always include <code>_</code> for real data.</p>

</details>

<details><summary>Why does <code>{ Total: &gt; Limit }</code> fail to compile when Limit is a property?</summary>

<p>Relational patterns require a compile-time constant on the right. Use a <code>when</code> guard for anything computed: <code>var x when x.Total &gt; Limit(x.Customer)</code>.</p>

</details>

<details><summary>Why is reflection slow, and what is the standard fix?</summary>

<p>Each call re-resolves metadata, boxes arguments and runs access checks &mdash; hundreds of ns versus ~1 ns direct. Fix: resolve once at start-up, compile into a delegate (Expression.Lambda(...).Compile()), and cache it statically. That is what serializers and DI containers do internally.</p>

</details>

<details><summary>What does reflection break in a trimmed or Native AOT build?</summary>

<p>The trimmer removes members nothing appears to reference, so reflection over invisible types fails only at runtime in the published build. Annotate with <code>[DynamicallyAccessedMembers]</code> or use source generators &mdash; and test the published artefact, not the debug run.</p>

</details>

<details><summary>Why is <code>result += line</code> in a loop pathological, and what are the alternatives?</summary>

<p>Strings are immutable, so each += copies the whole accumulated string &mdash; quadratic work. Use StringBuilder (linear), or <code>string.Create</code> / Span when the final length is known (zero intermediates).</p>

</details>

<details><summary><code>string.Length</code> counts what, exactly?</summary>

<p>UTF-16 code units, not characters. An emoji is typically 2 units (a surrogate pair), so naive truncation can split it and produce invalid text.</p>

</details>

<details><summary>Why must you never persist the result of <code>string.GetHashCode()</code>?</summary>

<p>It is randomised per process by default, so a value stored today will not match tomorrow's run. Use SHA-256 for integrity or an explicit stable algorithm for sharding.</p>

</details>

<details><summary>State the three rules that keep a GetHashCode/Equals pair honest.</summary>

<p>1) Equal objects must return equal hashes (the reverse need not hold). 2) The hash must not change while the object is in a hash-based collection. 3) Both must be cheap, because they run on every lookup.</p>

</details>

#### Under the Hood

<details><summary>Describe tiered compilation and what dynamic PGO adds.</summary>

<p>Tier 0 is a fast, minimally-optimised JIT with instrumentation counters. Once a method is hot (~30 calls) it is re-jitted at Tier 1 using the collected profile &mdash; devirtualising interface calls that always hit one type, inlining them, and laying out branches hot-path-first. Cost: a warm-up window on first traffic.</p>

</details>

<details><summary>What does Native AOT give up in exchange for instant start-up?</summary>

<p>No JIT means no tiering and no dynamic PGO (peak throughput can be lower), and no runtime reflection &mdash; JSON, DI and anything dynamic must be source-generated. Not all libraries are compatible.</p>

</details>

<details><summary>What replaced AppDomain for plugin isolation, and what makes unloading fail?</summary>

<p>AssemblyLoadContext, with <code>isCollectible: true</code>. Unloading fails when any reference into the plugin survives &mdash; a static field, an event handler, a cached delegate &mdash; because it pins the whole context in memory.</p>

</details>

<details><summary>What does the compiler generate for an <code>async</code> method, and why do stack traces show MoveNext?</summary>

<p>A state machine struct implementing IAsyncStateMachine: parameters and locals crossing an await become fields, and each await is a case in a switch inside <code>MoveNext</code>. The runtime resumes by calling MoveNext, so that is what appears in traces.</p>

</details>

<details><summary>An awaited operation has already completed. What does it cost?</summary>

<p>Roughly a branch. The generated code checks <code>IsCompleted</code> first and, if true, skips suspension entirely &mdash; no state machine box, no continuation scheduling. That fast path is why async is cheap on cache hits.</p>

</details>

<details><summary>How does a logger scope or trace id survive a thread hop across an await?</summary>

<p>ExecutionContext is captured at the await and restored on resume; <code>AsyncLocal&lt;T&gt;</code> is the handle into it. Flow is copy-on-write downward only &mdash; a child's write is not visible to the parent.</p>

</details>

<details><summary>State the rules for using <code>ValueTask</code>, and when it is worth it.</summary>

<p>Await it exactly once; never await twice, never store it, never WhenAll without <code>.AsTask()</code>. Worth it only on hot paths where synchronous completion dominates (caches, buffered readers). Public APIs and most app code should use Task.</p>

</details>

<details><summary>Walk the synchronisation decision tree for shared mutable state.</summary>

<p>Avoid sharing (per-request scope, immutable) &rarr; single value/flag: Interlocked &rarr; collection: Concurrent* &rarr; compound invariant across fields: lock &rarr; async inside the critical section: SemaphoreSlim(1,1) &rarr; many readers/rare writers: ReaderWriterLockSlim (measure first).</p>

</details>

<details><summary>Why can't you use <code>lock</code> around an <code>await</code>, and what replaces it?</summary>

<p>A monitor is thread-affine and the continuation may resume on a different thread, so the compiler forbids it. Use <code>SemaphoreSlim(1,1)</code> with <code>await WaitAsync()</code> / <code>Release()</code> in a finally.</p>

</details>

<details><summary>Why is <code>if (!dict.ContainsKey(k)) dict.Add(k, v)</code> unsafe under concurrency?</summary>

<p>Time-of-check to time-of-use: another thread can pass the same check before either adds, corrupting the buckets. Use ConcurrentDictionary.GetOrAdd (noting the factory may run twice, so keep it side-effect free).</p>

</details>

<details><summary>When is <code>volatile</code> needed, and what is the better default?</summary>

<p>Only for lock-free flags where one thread publishes and another polls &mdash; it prevents reordering being observed. Data guarded by a lock is already safe (lock entry/exit are full fences). For lazy init, prefer <code>Lazy&lt;T&gt;</code> over hand-rolled double-checked locking.</p>

</details>

<details><summary>Why can a <code>Span&lt;T&gt;</code> not be a field or cross an await?</summary>

<p>It is a ref struct that may point into the stack; letting it outlive the stack frame would create a dangling reference. Use <code>Memory&lt;T&gt;</code> for storage or async, and take <code>.Span</code> at the point of use.</p>

</details>

<details><summary>What problem does System.IO.Pipelines solve that raw Streams do not?</summary>

<p>Buffer management for partial reads: the Pipe owns pooled buffers, hands you a ReadOnlySequence, and lets you say what you consumed versus what you examined &mdash; so incomplete messages are retained without you stitching arrays. It is what Kestrel is built on.</p>

</details>

<details><summary>What does pinning cost the GC, and how do you avoid the cost for long-lived buffers?</summary>

<p>Pinned objects cannot be moved, so the heap cannot compact around them &mdash; fragmentation. Pin briefly (fixed blocks, stack spans), or allocate on the pinned object heap with <code>GC.AllocateArray&lt;T&gt;(n, pinned: true)</code>.</p>

</details>

#### ASP.NET Core

<details><summary>Give the correct middleware order and one reason for each placement.</summary>

<p>ExceptionHandler (outermost, catches all) &rarr; HSTS/HttpsRedirection (cheap rejections first) &rarr; StaticFiles (short-circuits) &rarr; UseRouting (endpoint <em>selected</em>) &rarr; CORS/Authentication/Authorization (need endpoint metadata) &rarr; RateLimiter (after authn, so limits are per user) &rarr; OutputCache &rarr; Map* (endpoint executes).</p>

</details>

<details><summary>Why does a breakpoint in your action never hit when model binding fails?</summary>

<p>Binding failure produces a 400 with a ValidationProblemDetails body <em>before</em> the handler runs, and it is not an exception, so nothing appears in logs. Read the response body &mdash; the failing property is named there.</p>

</details>

<details><summary>What are the three DI lifetimes, and what is a captive dependency?</summary>

<p>Singleton (one per app), Scoped (one per request), Transient (one per resolution). A captive dependency is a longer-lived service holding a shorter-lived one &mdash; e.g. a singleton capturing a scoped DbContext &mdash; which keeps the first request's instance alive for everyone. Enable scope validation in development.</p>

</details>

<details><summary>Why should exception detail never reach the HTTP response?</summary>

<p>Stack traces name your types, file paths and dependency versions &mdash; reconnaissance for an attacker and noise for the client. Log the exception with a trace id; return the id plus a sentence in a ProblemDetails body.</p>

</details>

<details><summary>Where should validation live: at the edge or in the domain?</summary>

<p>Both, and they are not duplication. Edge validation (attributes, FluentValidation) protects the API contract and rejects malformed input early. Domain invariants protect the aggregate regardless of caller &mdash; only that one survives a new entry point.</p>

</details>

<details><summary>Why map to a DTO <em>inside</em> the LINQ query rather than after ToListAsync?</summary>

<p>Inside, EF translates the projection into the SQL column list &mdash; often collapsing a wide entity load plus includes into one narrow join. After materialisation the data has already crossed the wire and no mapper can undo it.</p>

</details>

<details><summary>Offset paging versus keyset paging: state the trade.</summary>

<p>Offset (skip/take) allows jumping to any page but degrades with depth (the engine still walks skipped rows) and is unstable under concurrent inserts (duplicates/skips). Keyset pages by the last row seen: constant cost, stable, but next/previous only. Use keyset for anything unbounded or public.</p>

</details>

<details><summary>Why is returning a total count on every page expensive, and what do you do instead?</summary>

<p>It is a second full aggregate over the filtered set, often costing more than the page itself. Return it only on request (<code>?includeTotal=true</code>), cache it, or return an approximation.</p>

</details>

<details><summary>A client retries a POST after a timeout. How do you prevent a double charge?</summary>

<p>Idempotency key: the client sends a key it generates once per logical operation; the server atomically claims it (unique constraint / INSERT ON CONFLICT), stores the response before returning, and replays the stored response on retry. Different payload with the same key &rarr; 422.</p>

</details>

<details><summary>Which HTTP verbs are idempotent by definition, and which is the risky one?</summary>

<p>GET, PUT and DELETE are idempotent; POST is not. Either make the client generate the id and use PUT, or add an idempotency key. Retry policies must therefore be configured per operation, not globally.</p>

</details>

<details><summary>Name the four rate-limiting algorithms and the typical default choice.</summary>

<p>Fixed window (simple, bursts at the boundary), sliding window (smoother), token bucket (sustained rate + burst headroom &mdash; the usual best default), and concurrency (N in flight, for protecting a bounded resource).</p>

</details>

<details><summary>Why is rate limiting by IP alone unreliable, and what should you partition by?</summary>

<p>Proxies, NAT and mobile networks put thousands of users behind one address, and one user can rotate addresses. Partition by the strongest identity available &mdash; API key, tenant, then user &mdash; with IP as a last resort, and configure ForwardedHeaders correctly.</p>

</details>

<details><summary>In-process rate limiter with five replicas: what is the effective limit?</summary>

<p>Five times what you configured. In-process limiters protect that instance (thread pool, connection pool); contractual quotas need a shared counter at the gateway or in Redis. Most systems use both.</p>

</details>

#### Data

<details><summary>What is the N+1 problem and how do you detect it?</summary>

<p>One query loads N parents, then lazy loading (or a loop) issues one query per parent. Detect it by logging SQL in development or watching query counts in traces; fix with Include, a projection, or a split query.</p>

</details>

<details><summary>When should you use ExecuteUpdate/ExecuteDelete instead of load-modify-save?</summary>

<p>For bulk maintenance where you do not need the entities: it becomes one SQL statement with no materialisation and no tracking. Caveat: it bypasses change tracking, interceptors and domain events, so it is wrong for aggregate invariants.</p>

</details>

<details><summary>What can an EF SaveChangesInterceptor do for you?</summary>

<p>Cross-cutting persistence concerns in one place: audit stamps (CreatedAt/UpdatedAt), soft delete (turn Deleted into Modified with a DeletedAt), and staging outbox messages in the same transaction. A DbCommandInterceptor can additionally log, deny or rewrite raw SQL.</p>

</details>

<details><summary>How does a global query filter help multi-tenancy, and where does it leak?</summary>

<p><code>HasQueryFilter(o =&gt; o.TenantId == tenant.Id)</code> scopes every query on that entity automatically, so a forgotten WHERE cannot leak data. It leaks through <code>IgnoreQueryFilters()</code> (admin/reporting paths) and any raw SQL or Dapper query, which bypass filters entirely.</p>

</details>

<details><summary>Why are compiled queries worth it, and where?</summary>

<p>They skip expression-tree translation on every call. Worth it for the two or three hottest queries in a hot path; elsewhere they are noise that hurts readability.</p>

</details>

<details><summary>What is the dual-write problem, and how does the outbox solve it?</summary>

<p>&ldquo;Save the row AND publish the event&rdquo; spans two systems; a crash between them loses one. The outbox writes the event into the same database transaction as the row, and a relay publishes it afterwards &mdash; giving at-least-once delivery, which means consumers must be idempotent.</p>

</details>

<details><summary>Optimistic versus pessimistic concurrency: when do you use each?</summary>

<p>Optimistic (a rowversion/timestamp checked on update, retry on conflict) suits low-contention web workloads. Pessimistic (explicit locks) suits short, high-contention critical sections where a retry storm would be worse. Optimistic is the default for HTTP because there is no session to hold a lock.</p>

</details>

<details><summary>What does a covering index give you that a plain index does not?</summary>

<p>All the columns the query needs are in the index itself, so the engine never touches the base table (no key lookup). That is what turns a keyset-paging query from a sort of the whole table into a seek.</p>

</details>

#### Architecture

<details><summary>State the dependency rule, and how you make it structural rather than aspirational.</summary>

<p>Dependencies point inward: handlers &rarr; services &rarr; repositories &rarr; infrastructure; the domain depends on nothing. Enforce it with project references (the Domain project has none) plus an architecture test (NetArchTest) &mdash; then a violation is a compiler or CI error, not a review argument.</p>

</details>

<details><summary>When is a repository over EF Core a genuine layer, and when is it a pass-through?</summary>

<p>Genuine when it adds something: multi-model queries, caching, a second data source, or a domain-shaped API. A pass-through of one-line forwards adds indirection for nothing &mdash; DbContext already is a Unit of Work and DbSet already is a repository.</p>

</details>

<details><summary>Why should a decorator's lifetime match the thing it decorates?</summary>

<p>Because shrinking lifetime creates a captive dependency: a singleton decorator holding a scoped inner service pins the first request's instance &mdash; and its DbContext and data &mdash; for every later user.</p>

</details>

<details><summary>Modular monolith versus microservices: what actually decides?</summary>

<p>Whether you need independent deployability for organisational reasons. Microservices buy team autonomy, independent scaling and failure isolation, and cost network calls, no cross-service transactions, distributed debugging and per-service infrastructure. One team on one product almost never needs them.</p>

</details>

<details><summary>Publish facts or send commands over a broker — which, and why?</summary>

<p>Publish facts (<code>OrderPlaced</code>), not commands (<code>SendEmail</code>). Facts let new consumers appear without touching the publisher; commands smuggle coupling back in through the broker.</p>

</details>

<details><summary>Why is saga compensation not the same as rollback?</summary>

<p>Compensating actions are new transactions that leave visible traces &mdash; a cancelled order, a refund line &mdash; and some steps cannot be undone at all (an email that was sent). Order the saga so the irreversible step is last, and design the intermediate states into the domain.</p>

</details>

<details><summary>What does a circuit breaker prevent that retries alone make worse?</summary>

<p>Cascading failure. Without it, every thread waits its full timeout on a sick dependency until the pool drains and your service dies too. The breaker fails fast during the outage, degrades one feature, then half-opens to probe recovery. Retries without a breaker amplify the load on something already failing.</p>

</details>

<details><summary>When is it safe to retry, and why is jitter mandatory?</summary>

<p>Only idempotent operations and transient errors (timeouts, 5xx, 429). Retrying a POST that timed out after the server processed it double-charges. Jitter is mandatory because synchronised retries from many instances are a self-inflicted DDoS.</p>

</details>

<details><summary>gRPC deadlines: what makes them better than a local timeout?</summary>

<p>The deadline travels as metadata, so each hop inherits the <em>remaining</em> budget instead of starting a fresh timeout &mdash; no more &ldquo;three services each wait 30 s so the user waits 90&rdquo;. Expiry surfaces as a first-class DeadlineExceeded status, visible in traces.</p>

</details>

#### Security & Ops

<details><summary>Why must multi-instance deployments share the Data Protection key ring?</summary>

<p>Keys are per-machine by default, so cookies, antiforgery tokens and anything else protected by one instance cannot be read by another &mdash; users get random logouts. Persist keys to a shared store (Redis, blob, database) and set the same <code>SetApplicationName</code>.</p>

</details>

<details><summary>Refresh token rotation with reuse detection: what does it protect against, and how?</summary>

<p>Token theft. Each refresh consumes the token and issues a new one in the same family. If a consumed token is presented again, that is a signal a copy exists &mdash; so you revoke the whole family. The attacker and the victim are both logged out; the victim simply logs in again.</p>

</details>

<details><summary>What does claims transformation solve that putting roles in the IdP token does not?</summary>

<p>It keeps your authorization data in your database while the IdP stays generic. Roles are enriched per request, so a permission change applies on the next request rather than the next login &mdash; and the token stays small.</p>

</details>

<details><summary>Why is <code>FromSqlRaw($"...{input}")</code> dangerous while <code>FromSql($"...{input}")</code> is not?</summary>

<p>FromSqlRaw takes an already-built string &mdash; the interpolation happens before EF sees it, so the value is concatenated into SQL. FromSql uses an interpolated-string handler that converts each hole into a parameter.</p>

</details>

<details><summary>Where does personal data leak <em>outside</em> the database?</summary>

<p>Exception messages containing request bodies, trace attributes, log lines, caches, retained broker messages, search indexes, analytics warehouses, support exports and backups. Classification plus structured logging that logs ids rather than people is the systematic defence.</p>

</details>

<details><summary>Which test doubles do you actually need if your services take dependencies as parameters?</summary>

<p>Usually none from a mocking library &mdash; hand-written fakes (in-memory repository, test clock, test bus) built through the same composition root are simpler to read and do not break on refactors. Reserve mocks for verifying interactions you genuinely care about.</p>

</details>

<details><summary>What does mutation testing tell you that coverage does not?</summary>

<p>Whether your assertions would <em>notice</em> a bug. It mutates the code (flipping comparisons, removing calls) and reruns the suite; surviving mutants are lines your tests execute but do not check. Run it on the domain layer, occasionally, and read the survivors rather than the score.</p>

</details>

<details><summary>Your pods are OOMKilled in a loop but the app has no leak. What is the likely cause?</summary>

<p>Server GC sizes its heap from the container limit and collects lazily &mdash; healthy behaviour that looks like a leak. Tune <code>DOTNET_GCHeapHardLimitPercent</code> (leave room for non-GC memory), and consider workstation GC on small pods.</p>

</details>

<details><summary>What does graceful shutdown require, end to end?</summary>

<p>On SIGTERM: fail the readiness probe so the load balancer stops routing, stop accepting new work while finishing in-flight requests (ShutdownTimeout below the orchestrator's grace period), and have BackgroundServices honour the stopping token and checkpoint or requeue work rather than losing it.</p>

</details>

<details><summary>Symptom-to-tool: process hung, memory growing, latency spikes, mystery restart.</summary>

<p>Hung/100% CPU &rarr; dotnet-dump collect then analyze/clrstacks. Memory growing &rarr; dotnet-gcdump and read the retention graph. Latency spikes &rarr; dotnet-trace (gc-verbose) plus the trace view. Mystery restart &rarr; kubectl describe pod: OOMKilled or exit code.</p>

</details>

<details><summary>How do you adopt TreatWarningsAsErrors in a large existing codebase without it being reverted?</summary>

<p>Ratchet, do not flip: baseline the current state, apply it to new projects first, raise one rule at a time to warning then error, and keep <code>dotnet format --verify-no-changes</code> in CI. A standard nobody can build against is not a standard.</p>

</details>

### Platform

#### Networking

<details><summary>Walk the TCP three-way handshake and say what it costs.</summary>

<p>SYN &rarr; SYN-ACK &rarr; ACK: one full round trip before any data moves, plus another one or two for TLS. That is why connection reuse (keep-alive, pooling) matters more than bandwidth for small requests, and why a new connection per call is a latency bug.</p>

</details>

<details><summary>A private server has no public IP. How does it reach the internet, and how does the reply find it?</summary>

<p>Through NAT: the gateway rewrites the source address and port, records the mapping in a table, and rewrites replies back. Outbound works; inbound does not, because there is no mapping until the internal host initiates &mdash; that is why inbound needs port forwarding or a load balancer.</p>

</details>

<details><summary>You changed a DNS record and some users still hit the old server. Why?</summary>

<p>TTL. Resolvers and clients cache the old answer until it expires, and some ignore low TTLs. Lower the TTL <em>before</em> a planned migration, and never treat DNS as an instant switch &mdash; it is eventually consistent by design.</p>

</details>

<details><summary>When is UDP or QUIC the better choice than TCP?</summary>

<p>When head-of-line blocking or handshake latency hurts more than reliability helps: real-time media, DNS, and QUIC's case &mdash; multiplexed streams that do not block each other, plus 0-RTT resumption and connection migration across networks (a phone switching from Wi-Fi to cellular keeps the session).</p>

</details>

<details><summary>What changed about optimisation advice once HTTP/2 arrived?</summary>

<p>Concatenation, sprites and domain sharding became harmful: multiplexing means many small files share one connection with better caching granularity, and sharding forces extra connections and handshakes. Header compression also made minimising header count much less important.</p>

</details>

<details><summary>Explain the three most common TLS errors and what each actually means.</summary>

<p>&ldquo;Certificate has expired&rdquo; &mdash; renewal failed; alert at 30/14/7 days. &ldquo;Unable to get local issuer certificate&rdquo; &mdash; a missing intermediate in the chain the server sends, or the client's trust store lacks the root. &ldquo;Hostname mismatch&rdquo; &mdash; the requested name is not in the certificate's SAN list.</p>

</details>

<details><summary>WebSocket versus SSE versus polling: pick one for a live progress bar, and say why.</summary>

<p>SSE. It is one-directional (server &rarr; client), stays plain HTTP so proxies and auth work unchanged, and has automatic reconnect built in. WebSocket is for full duplex (chat, collaboration); polling is the fallback of last resort.</p>

</details>

<details><summary>Why do WebSocket connections silently die roughly a minute after going idle?</summary>

<p>Load balancers and proxies close idle sockets (commonly 60&ndash;350 s). Send application-level pings below the shortest timeout in the path, and reconnect with backoff &mdash; otherwise a deploy triggers a thundering herd of simultaneous reconnects.</p>

</details>

<details><summary>Why does a realtime tier need a backplane once you run more than one instance?</summary>

<p>A user's socket lives on exactly one instance, but the request to message them can arrive at any of them. A backplane (Redis pub/sub, a broker, a managed service) broadcasts to all instances so whichever holds the socket delivers it.</p>

</details>

#### Servers & Caching

<details><summary>What does a reverse proxy change about your application's assumptions?</summary>

<p>The app no longer sees the client directly: remote IP, scheme and host become the proxy's unless forwarded headers are configured and trusted. It also becomes the place for TLS termination, compression, rate limiting, caching and buffering &mdash; and a new source of timeouts.</p>

</details>

<details><summary>L4 versus L7 load balancing: what can each do?</summary>

<p>L4 forwards TCP without reading the payload &mdash; fast, protocol-agnostic, no visibility. L7 parses HTTP, so it can route by path or header, retry idempotent requests, do sticky sessions, terminate TLS and emit per-route metrics &mdash; at higher cost per request.</p>

</details>

<details><summary>Why avoid session affinity, and what do you do instead?</summary>

<p>It defeats even load distribution, breaks on instance loss (users lose state), and makes deploys disruptive. Instead make instances stateless: put session state in a shared store (Redis) or in a signed token the client carries.</p>

</details>

<details><summary>Name the four caching patterns and the one you will use most.</summary>

<p>Cache-aside (read through your code, populate on miss) &mdash; the default; read-through; write-through; write-behind. Cache-aside dominates because it needs no special store support and keeps the failure mode simple: a cache outage degrades to slow, not broken.</p>

</details>

<details><summary>What is a cache stampede and how do you prevent it?</summary>

<p>A popular key expires and hundreds of concurrent requests all miss and all hit the origin at once. Prevent with a per-key lock or single-flight (one caller populates, the rest wait), staggered/jittered TTLs, and serving stale while revalidating.</p>

</details>

#### Linux & Containers

<details><summary>Why does PID 1 matter in a container?</summary>

<p>PID 1 has special signal semantics: it does not get default signal handlers, so a process that ignores SIGTERM never shuts down gracefully and gets SIGKILLed after the grace period. It is also responsible for reaping zombies. Use an init (or a runtime that handles it) if your entrypoint spawns children.</p>

</details>

<details><summary>Your container was killed with no application error. Where do you look?</summary>

<p>The OOM killer via cgroup limits &mdash; <code>kubectl describe pod</code> shows OOMKilled and the exit code. Memory limits count the whole process, not just the managed heap, so caches, native buffers and the runtime itself must fit too.</p>

</details>

<details><summary>Explain Docker layer caching and why layer order matters.</summary>

<p>Each instruction produces a layer keyed by its inputs; a change invalidates that layer and everything after it. So copy dependency manifests and restore packages <em>before</em> copying source &mdash; otherwise every source edit re-downloads all dependencies.</p>

</details>

<details><summary>What does a multi-stage build buy you?</summary>

<p>The build tools, SDK and intermediate artefacts stay in an earlier stage; the final image contains only the runtime and published output. Smaller image, faster pulls, and a much smaller attack surface (no compilers or package managers in production).</p>

</details>

<details><summary>Liveness versus readiness probes: what does each mean, and what is the classic mistake?</summary>

<p>Readiness = &ldquo;can I take traffic right now&rdquo;; liveness = &ldquo;am I unrecoverable, restart me&rdquo;. The classic mistake is a liveness probe that checks a dependency &mdash; a slow database then restarts every pod, turning a degradation into an outage.</p>

</details>

#### Distributed

<details><summary>State CAP honestly, and why PACELC is more useful in practice.</summary>

<p>CAP: during a network partition you choose availability or consistency. PACELC adds the case you actually live in most of the time &mdash; <em>else</em>, with no partition, you still trade latency against consistency. Most systems are PA/EL or PC/EC, and the everyday cost is latency, not partitions.</p>

</details>

<details><summary>Why is exactly-once delivery not achievable, and what do you do instead?</summary>

<p>A sender cannot distinguish a lost message from a lost acknowledgement, so it must choose to retry (at-least-once) or not (at-most-once). You get exactly-once <em>processing</em> by making consumers idempotent &mdash; dedupe on a message id, or make the operation naturally repeatable.</p>

</details>

<details><summary>Queue versus log: what is the actual difference?</summary>

<p>A queue delivers a message to one consumer and removes it; ordering and replay are limited. A log (Kafka-style) keeps an ordered, retained sequence that many consumers read independently at their own offsets &mdash; so replay and multiple independent readers are natural.</p>

</details>

<details><summary>What does backpressure mean in a pipeline, and what happens without it?</summary>

<p>The consumer's slowness must propagate to the producer. Without it, an unbounded queue turns overload into an out-of-memory crash. A bounded channel that makes the producer wait converts a crash into graceful slowdown.</p>

</details>

<details><summary>Why does clock skew make timestamps unsafe for ordering across machines?</summary>

<p>Machine clocks drift and NTP corrections can jump backwards, so &ldquo;later timestamp&rdquo; does not mean &ldquo;happened after&rdquo;. Use sequence numbers, logical clocks, or a single authority for ordering; timestamps are for humans and approximate windows.</p>

</details>

#### Deployment & Ops

<details><summary>Why build the artefact once and promote it, rather than rebuilding per environment?</summary>

<p>Otherwise the thing you tested is not the thing you shipped, and &ldquo;it passed in staging&rdquo; means nothing. One immutable artefact identified by digest makes rollback a pointer change and makes &ldquo;what is running?&rdquo; answerable exactly.</p>

</details>

<details><summary>What does zero-downtime deployment actually require, beyond a rolling update?</summary>

<p>Backwards-compatible database changes (expand/contract, never rename in place), backwards-compatible API contracts for the overlap window, readiness probes that fail during drain so traffic stops before shutdown, and in-flight requests allowed to finish inside the grace period.</p>

</details>

<details><summary>Blue-green versus canary: what does each optimise for?</summary>

<p>Blue-green swaps all traffic between two full environments &mdash; instant rollback, double the infrastructure, and no partial exposure. Canary shifts a small percentage first and watches metrics &mdash; cheaper, catches real-traffic problems, but needs good telemetry and takes longer.</p>

</details>

<details><summary>Describe a bottleneck-finding method that terminates.</summary>

<p>1) State the problem as a number against a target. 2) Ask who/what is affected (one endpoint, instance, tenant, region?) to halve the space. 3) Split client from server timing. 4) Account for server time with traces and find the widest span. 5) Check saturation (CPU, memory, disk await, pool usage, queue depth) before optimising code. Each step either answers or eliminates a layer.</p>

</details>

<details><summary>Which signal answers which question: logs, metrics, traces?</summary>

<p>Metrics: is it healthy, and what is the trend? Traces: where did the time go for this class of request? Logs: what exactly happened to this one request? Reaching for the wrong one is why investigations stall &mdash; and why log volume is usually the biggest bill.</p>

</details>

<details><summary>What makes an alert trustworthy?</summary>

<p>It fires on user-visible symptoms (error rate, latency against an SLO) rather than causes (CPU 80%), it is actionable with a linked runbook, and it is rare enough that people still read it. Alerts nobody acts on train the team to ignore the ones that matter.</p>

</details>

<details><summary>Why is an untested backup not a backup?</summary>

<p>The two failures that actually happen are a job that has been silently failing for months, and a restore that works but takes eleven hours when the business assumed one. Only an actual restore reveals either &mdash; so schedule it, time it, and record the number as your real RTO.</p>

</details>

<details><summary>Give a sane escalation ladder for failure-injection practice.</summary>

<p>1) Tabletop walk-through of a scenario. 2) Game day: restore last night's backup into a scratch environment. 3) Dependency failure: block egress to one third party in staging. 4) Instance loss in production during business hours. 5) Zone drain &mdash; only once 1&ndash;4 pass reliably. Always define the abort condition first.</p>

</details>

<details><summary>What is the operational argument for workload identity over stored credentials?</summary>

<p>A credential you never store cannot leak, expire unnoticed or be committed. It also shrinks the blast radius: a compromised container yields a short-lived token scoped to one resource instead of a long-lived connection string valid everywhere.</p>

</details>

### Design patterns

#### Foundations

<details><summary>What is a &ldquo;force&rdquo; in pattern language, and why does it matter more than the pattern name?</summary>

<p>A force is a pressure acting on the design. Patterns exist because forces conflict &mdash; otherwise the obvious solution would always be right. So the productive question is never &ldquo;is that the right pattern?&rdquo; but &ldquo;which force are we responding to, and is it actually present?&rdquo; If nobody can name the pressure, the pattern is decoration.</p>

</details>

<details><summary>Give the practical test for when to introduce an abstraction.</summary>

<p>Rule of three: duplicate twice, abstract on the third occurrence, when you can finally see which parts vary. Also ask: has this changed more than twice for the same reason? Do the variants have genuinely different logic? Are variants added by people who cannot edit this file? If no &mdash; you do not need the extension point yet.</p>

</details>

<details><summary>What does the Open/Closed principle mean in practice, and what is the usual misreading?</summary>

<p>Open for extension, closed for modification: new behaviour arrives by adding code, not editing existing code. The misreading is treating it as a mandate to make everything extensible up front &mdash; which produces speculative interfaces with one implementation. Apply it along the axis that has actually changed.</p>

</details>

<details><summary>How does the Liskov substitution principle show up as a real bug?</summary>

<p>A subtype that strengthens preconditions or weakens postconditions &mdash; e.g. an override that throws for input the base accepted, or a read-only collection subclassing a mutable one. Code written against the base then breaks for that subtype, and the failure appears far from the class that caused it.</p>

</details>

#### Creational

<details><summary>Why has Prototype largely dissolved in modern C#?</summary>

<p>Records with <code>with</code> expressions give you copy-with-changes for free, and object initialisers make the desired starting state easy to express. Building a <code>Clone()</code> hierarchy usually reimplements what the language already provides.</p>

</details>

<details><summary>When is Abstract Factory justified, and why is it over-applied?</summary>

<p>Only when several products must be consistent <em>as a family</em> (all the widgets from one theme, all the adapters for one database dialect). If products can vary independently, separate factories are simpler and the extra layer is pure cost.</p>

</details>

<details><summary>How does a DI container change the Singleton pattern?</summary>

<p>It removes the need for it. Registering a service as a singleton gives you one instance with none of the classic drawbacks &mdash; the static <code>Instance</code> property, the untestable global, the hidden dependency. In C#, module-level statics and container lifetimes cover the legitimate cases.</p>

</details>

<details><summary>Which classic patterns did dependency injection absorb, and which are unaffected?</summary>

<p>Patterns that exist to hide construction &mdash; Factory Method, Abstract Factory, Singleton, Service Locator &mdash; are largely absorbed, because the container does construction. Patterns that structure behaviour &mdash; Strategy, Decorator, State, Command, Observer &mdash; are unaffected, because DI never addressed that.</p>

</details>

#### Structural

<details><summary>What is the real function of an Anti-Corruption Layer?</summary>

<p>Containment rather than translation. It is Adapter applied to an entire integration, and its purpose is to stop a partner's model &mdash; their field names, nulls and enum values &mdash; from propagating inward, so their next API change is a change at one boundary rather than through your domain.</p>

</details>

<details><summary>Adapter versus Facade: what distinguishes them?</summary>

<p>Adapter changes an interface to one the client expects (same functionality, different shape). Facade simplifies a subsystem behind one door (less functionality exposed, orchestration hidden). Adapter is about compatibility; Facade is about complexity.</p>

</details>

<details><summary>What is Null Object, and where does .NET already use it?</summary>

<p>Return a do-nothing implementation instead of null so callers stop branching. <code>NullLogger</code> is the canonical example; an empty collection is the same idea. It also removes the need for a mock in tests.</p>

</details>

<details><summary>When does Decorator stop paying for itself?</summary>

<p>Around the third layer: stack traces become unreadable and breakpoints hard to place. Caching, resilience and logging earn the indirection; business rules do not &mdash; those belong in the service where a reader will look for them.</p>

</details>

#### Behavioural

<details><summary>What problem does the State pattern actually cure?</summary>

<p>Status checks scattered across many methods &mdash; <code>if (status == ...)</code> repeated everywhere, each one a chance to forget a case. Moving transitions into one place makes illegal transitions impossible rather than merely discouraged.</p>

</details>

<details><summary>How does Strategy usually appear in idiomatic C#?</summary>

<p>As a keyed dictionary of functions or a DI-registered set of implementations selected by key, replacing a switch that keeps growing. With <code>satisfies</code>-style exhaustiveness (or an enum-keyed record), adding a variant becomes a compile error until you supply its strategy.</p>

</details>

<details><summary>Command and Chain of Responsibility: where do you already use both daily?</summary>

<p>Command: any job-queue message, Redux-style action or MediatR request &mdash; the action reified as data so it can be queued, logged, retried or replayed. Chain of Responsibility: the middleware pipeline, where each handler may handle or pass along.</p>

</details>

<details><summary>What is the memory-leak risk in Observer, and how do you avoid it?</summary>

<p>The subject holds references to subscribers, so a subscriber that never unsubscribes cannot be collected &mdash; and in long-lived apps that accumulates. Return an unsubscribe handle (or use IDisposable subscriptions) and make unsubscription part of the component lifecycle.</p>

</details>

#### Domain

<details><summary>Entity versus Value Object: what is the distinguishing question?</summary>

<p>Identity. An entity is the same thing over time even as its attributes change (an Order keeps its id); a value object is defined entirely by its values (Money, Email, DateRange) and is interchangeable when equal. Value objects should be immutable and validate on construction.</p>

</details>

<details><summary>What is an anaemic domain model, and what is the concrete cost?</summary>

<p>Entities that are property bags with rules living in services. The cost is that every service must remember every rule &mdash; recalculate the total, check the status, validate the lines &mdash; and eventually one will not. A rich model makes invalid states unreachable because the rules are inside the object that owns them.</p>

</details>

<details><summary>Why raise domain events instead of calling the side effect directly?</summary>

<p>The entity records a fact (<code>OrderPlaced</code>) without knowing about email, analytics or search indexing. Dispatch happens after commit, so the aggregate stays free of infrastructure and new reactions can be added without touching it.</p>

</details>

#### Distributed

<details><summary>What changes about design once the call can fail?</summary>

<p>Every remote call has three outcomes, not two: success, failure, and unknown (timeout). The unknown case is what forces idempotency, retries with backoff, circuit breakers, and reconciliation &mdash; and it is why patterns that assume in-process semantics (shared transactions, synchronous chains) break across a network.</p>

</details>

<details><summary>What is an idempotent receiver, and what does it need to store?</summary>

<p>A consumer that can process the same message twice with the same result. It stores processed message ids (an inbox table) and skips duplicates, or performs an operation that is naturally repeatable &mdash; because at-least-once delivery guarantees duplicates will arrive.</p>

</details>

<details><summary>Strangler Fig: what problem does it solve and what is the key mechanic?</summary>

<p>Replacing a legacy system without a big-bang rewrite. A facade (gateway/proxy) sits in front of the old system and routes selected calls to new implementations, growing until the old system is dead. The mechanic that makes it work is that the routing point is a single place you control.</p>

</details>

#### Anti-patterns

<details><summary>Name three structural smells and the fix for each.</summary>

<p>God object (one class with many reasons to change) &rarr; split along change reasons. Speculative generality (an interface with one implementation, 'in case') &rarr; delete it until the second implementation exists. Anaemic wrapper (a layer of one-line pass-throughs) &rarr; remove the layer; the underlying API already is it.</p>

</details>

<details><summary>What is the meta-rule for applying patterns?</summary>

<p>Patterns are vocabulary, not virtue. The question is never &ldquo;which pattern can I use here?&rdquo; but &ldquo;what varies, and what should stay stable while it does?&rdquo; If nothing varies yet, a plain function and a TODO is the right design.</p>

</details>

### JavaScript and TypeScript

#### JS Foundations

<details><summary>Explain the JavaScript prototype chain in one sentence, and what <code>class</code> changed.</summary>

<p>Every object has a hidden link to another object (its prototype); property lookup walks that chain until it finds the key or hits null. <code>class</code> changed nothing about the mechanism &mdash; it is syntax over prototypes, plus stricter rules (no hoisting of the body, methods non-enumerable, must be called with new).</p>

</details>

<details><summary>What determines the value of <code>this</code> in a normal function versus an arrow function?</summary>

<p>A normal function's <code>this</code> is set by the call site (method call, plain call, call/apply/bind, new). An arrow function has no <code>this</code> of its own &mdash; it closes over the enclosing lexical scope, which is why arrows are correct for callbacks and wrong for object methods and prototypes.</p>

</details>

<details><summary>What is a closure, and what is the classic bug it causes in loops?</summary>

<p>A function plus the variable environment it was created in, so inner functions keep access to outer variables after the outer call returns. Classic bug: <code>var</code> in a loop shares one binding, so every callback sees the final value; <code>let</code> creates a fresh binding per iteration and fixes it.</p>

</details>

<details><summary>Explain the temporal dead zone.</summary>

<p><code>let</code> and <code>const</code> bindings are hoisted but uninitialised; referencing them before the declaration throws a ReferenceError rather than yielding undefined. That window is the temporal dead zone, and it is why <code>let</code> catches use-before-declare bugs that <code>var</code> hides.</p>

</details>

<details><summary>Why does <code>0.1 + 0.2 !== 0.3</code>, and what do you use for money?</summary>

<p>IEEE-754 doubles cannot represent those decimals exactly, so the sum lands slightly off. For money, use integer minor units (cents) or a decimal library &mdash; and on the server, C# <code>decimal</code>.</p>

</details>

<details><summary>Shallow versus deep copy: what does spread actually do?</summary>

<p>Spread and Object.assign copy one level &mdash; nested objects are still shared references, so mutating a nested field affects both copies. For deep copies use <code>structuredClone()</code> (which also handles Maps, Sets and Dates) rather than the JSON round trip, which loses undefined, functions and Dates.</p>

</details>

#### Event Loop

<details><summary>Describe the event loop, and where microtasks fit.</summary>

<p>The call stack runs to completion; when it empties, all queued microtasks (promise callbacks, queueMicrotask) run &mdash; including ones they add &mdash; and only then does the loop take the next macrotask (timer, I/O, event). That is why a promise chain resolves before a setTimeout(0), and why an infinite microtask loop starves rendering entirely.</p>

</details>

<details><summary>Why can <code>await</code> inside a loop be a performance bug, and what is the fix?</summary>

<p>It serialises independent operations &mdash; each iteration waits for the previous. If they do not depend on each other, start them all and await together with <code>Promise.all</code> (or allSettled when partial failure is acceptable).</p>

</details>

<details><summary>Promise.all vs allSettled vs race vs any &mdash; when do you use each?</summary>

<p>all: need every result, fail fast on the first rejection. allSettled: want every outcome including failures. race: first to settle wins, including a rejection &mdash; used for timeouts. any: first <em>fulfilment</em> wins, rejecting only if all fail.</p>

</details>

<details><summary>What is an unhandled promise rejection, and why does it matter in Node?</summary>

<p>A rejected promise with no catch handler attached by the time it settles. In browsers it logs; in Node it terminates the process by default since v15. Always attach a handler, or await inside a try/catch &mdash; and never leave a fire-and-forget promise floating.</p>

</details>

#### TypeScript

<details><summary>What does <code>strictNullChecks</code> change, and why is it the flag that matters most?</summary>

<p>Without it, null and undefined are assignable to every type, so the type system silently lies about the most common runtime error. With it, nullability becomes explicit (<code>string | null</code>) and the compiler forces you to narrow before use.</p>

</details>

<details><summary>Structural typing: what does it mean that TypeScript types are structural?</summary>

<p>Compatibility is by shape, not by declared name &mdash; an object with the right members satisfies an interface without implementing it. That makes types cheap to apply to existing data, but means two distinct concepts with the same shape are interchangeable, which is what branded types exist to prevent.</p>

</details>

<details><summary><code>unknown</code> versus <code>any</code>: what is the practical difference?</summary>

<p><code>any</code> disables checking and spreads silently. <code>unknown</code> accepts anything but permits nothing until you narrow it, so it is the correct type for parsed JSON, caught errors and third-party payloads &mdash; the compiler forces validation at the boundary.</p>

</details>

<details><summary>What is a type guard, and how do you write a reusable one?</summary>

<p>An expression that narrows a type in a scope: <code>typeof</code>, <code>instanceof</code>, <code>in</code>, or a user-defined predicate returning <code>x is T</code>. Reusable version: <code>function isUser(x: unknown): x is User { ... }</code> &mdash; but the body must actually validate, because the predicate is a promise the compiler trusts.</p>

</details>

<details><summary>What are discriminated unions and why are they the workhorse of TS modelling?</summary>

<p>A union of object types sharing a literal tag field. Switching on the tag narrows to the exact member, and adding a variant produces errors everywhere the switch is no longer exhaustive &mdash; making illegal states unrepresentable and refactors compiler-guided.</p>

</details>

<details><summary>Types erase at runtime. What follows from that?</summary>

<p>No runtime validation, no reflection over types, no <code>instanceof</code> for interfaces. Anything crossing a boundary &mdash; API responses, localStorage, form data &mdash; must be validated by real code (Zod or equivalent), with the type inferred from the schema rather than declared separately.</p>

</details>

#### Browser

<details><summary>Reflow versus repaint: which is expensive and what triggers each?</summary>

<p>Reflow (layout) recomputes geometry and is the expensive one &mdash; triggered by changing size, position, or reading layout properties like offsetHeight. Repaint only redraws pixels (colour, shadow). Transform and opacity can skip both by staying on the compositor.</p>

</details>

<details><summary>What is layout thrashing and how do you avoid it?</summary>

<p>Alternating reads and writes of layout in a loop, forcing a synchronous reflow on every read. Batch all reads first, then all writes &mdash; or use <code>requestAnimationFrame</code> to group the writes.</p>

</details>

<details><summary>How do event bubbling, capturing and delegation relate?</summary>

<p>An event travels down (capture) to the target then back up (bubble). Delegation exploits the bubble phase: one listener on a parent handles events from many children, using <code>event.target</code> &mdash; fewer listeners, and it works for elements added later.</p>

</details>

<details><summary>Which storage do you use for an auth token, and why not localStorage?</summary>

<p>An httpOnly, Secure, SameSite cookie &mdash; script cannot read it, so an XSS cannot exfiltrate it. localStorage is readable by any script on the page, which turns any XSS into full account takeover.</p>

</details>

<details><summary>What is CORS actually protecting, and where is it enforced?</summary>

<p>It protects the <em>user's</em> credentials against a malicious site making authenticated cross-origin requests. It is enforced by the browser, not the server &mdash; so CORS headers are not a server security mechanism, and anything not a browser (curl, a server) ignores them entirely.</p>

</details>

#### Frameworks

<details><summary>What problem do all modern front-end frameworks actually solve?</summary>

<p>Keeping the DOM in sync with state without you writing the transitions by hand. They differ in <em>how</em> they detect change &mdash; virtual DOM diffing, fine-grained signals, or compile-time tracking &mdash; but the value proposition is the same: describe the UI as a function of state.</p>

</details>

<details><summary>Virtual DOM versus signals: what is the trade?</summary>

<p>A virtual DOM re-runs the component and diffs the output &mdash; simple mental model, work proportional to the tree. Signals track dependencies at the value level and update only the affected DOM nodes &mdash; less work per change and no diffing, at the cost of a reactivity system you have to understand.</p>

</details>

#### React

<details><summary>Why do the rules of hooks exist?</summary>

<p>Hooks are stored per component in call order, not by name. Calling them conditionally or in a loop shifts the order between renders, so state associates with the wrong hook. That is the entire reason for &ldquo;top level only, same order every render&rdquo;.</p>

</details>

<details><summary>What is a stale closure in React, and how do you avoid it?</summary>

<p>A callback captures the values from the render in which it was created; if it survives (a subscription, a timer, a memoised handler with a wrong dependency array) it keeps reading old state. Fix with the functional updater (<code>setX(prev => ...)</code>), correct dependencies, or a ref for genuinely mutable state.</p>

</details>

<details><summary>When does memoisation actually help, and when does it hurt?</summary>

<p>It helps when a child is expensive and its props are genuinely stable, or when a value feeds a dependency array. It hurts when the comparison plus allocation costs more than the render it avoids &mdash; which is most small components. Measure with the profiler rather than memoising defensively.</p>

</details>

<details><summary>Server state versus client state: why is the distinction useful?</summary>

<p>Server state is a cache of someone else's data &mdash; it goes stale, needs refetching, deduplication and invalidation. Client state is genuinely yours (form input, toggles). Conflating them is why apps grow giant global stores; a query library owns the first, useState owns the second.</p>

</details>

#### Performance

<details><summary>What do the Core Web Vitals measure, and what usually fixes each?</summary>

<p>LCP &mdash; when the main content appears (fix: server response, image loading, critical CSS). INP &mdash; responsiveness to interaction (fix: break up long tasks, less JS). CLS &mdash; unexpected layout shift (fix: reserve space for images, fonts and ads).</p>

</details>

<details><summary>Why does bundle size matter more than transfer size?</summary>

<p>Transfer is compressed, but parsing, compiling and executing JavaScript happen on the uncompressed code &mdash; and on a mid-range phone that dominates. A 300&nbsp;KB transferred bundle can cost seconds of main-thread time before anything is interactive.</p>

</details>

#### Security

<details><summary>How does XSS happen in a framework that escapes by default, and what stops it?</summary>

<p>Through the escape hatches: <code>dangerouslySetInnerHTML</code>, <code>v-html</code>, direct innerHTML, and injecting user data into URLs (<code>javascript:</code>) or inline styles. Sanitise HTML with a real sanitiser, validate URL schemes, and add a Content-Security-Policy as the second line of defence.</p>

</details>

#### Testing

<details><summary>What should a front-end test assert, and what makes tests brittle?</summary>

<p>Assert behaviour the user can observe &mdash; text, roles, what happens after an interaction. Brittleness comes from asserting implementation: class names, internal state, component structure. Query by role and label, and the test survives refactors and checks accessibility as a side effect.</p>

</details>

#### Accessibility

<details><summary>What does 'use a button' actually buy you over a clickable div?</summary>

<p>Keyboard focus, Enter and Space activation, the correct role for screen readers, disabled semantics and focus-visible styling &mdash; all of which you would otherwise reimplement, usually incompletely. Semantic HTML is the cheapest accessibility work available.</p>

</details>

### Databases

#### Modelling

<details><summary>What do the first three normal forms actually require?</summary>

<p>1NF: atomic values, no repeating groups. 2NF: no partial dependency on part of a composite key. 3NF: no transitive dependency &mdash; non-key columns depend on the key, the whole key and nothing but the key. In practice, model to 3NF and denormalise deliberately with a measured reason.</p>

</details>

<details><summary>Natural versus surrogate keys: state the trade honestly.</summary>

<p>Natural keys carry meaning and remove a join for lookups, but meaning changes &mdash; emails, ISBNs and tax numbers all get reissued or corrected, cascading through every foreign key. Surrogate keys are stable and opaque, at the cost of an extra unique constraint to protect the real identity.</p>

</details>

<details><summary>Why can a random UUID primary key hurt write performance?</summary>

<p>With a clustered index, random keys scatter inserts across the whole B-tree, causing page splits and poor cache locality. Sequential variants (UUIDv7, ULID, or a sequential GUID) keep inserts at the right edge and recover most of the loss.</p>

</details>

<details><summary>How do you model a hierarchy in SQL, and what is the trade?</summary>

<p>Adjacency list (parent_id) is simple and cheap to write, but needs a recursive CTE to traverse. Materialised path stores the ancestry as a string &mdash; fast subtree reads, expensive moves. Closure table stores every ancestor-descendant pair &mdash; fast queries both directions, more write amplification.</p>

</details>

#### Indexing

<details><summary>Why is a B-tree the default index structure rather than a hash?</summary>

<p>A hash index answers equality only. A B-tree answers equality, ranges, prefix matches and ordered scans, keeps data sorted for ORDER BY, and has predictable logarithmic depth &mdash; so one structure serves nearly every query shape.</p>

</details>

<details><summary>What does the leftmost-prefix rule mean for a composite index?</summary>

<p>An index on (a, b, c) can serve predicates on a, on (a, b) and on (a, b, c) &mdash; but not on b alone or (b, c), because the tree is sorted by a first. Column order is therefore a design decision, not an arbitrary one.</p>

</details>

<details><summary>What is a covering index and how do you recognise you need one?</summary>

<p>An index containing every column a query touches, so the engine never visits the table (no key lookup / heap fetch). You need one when the plan shows a large number of lookups after an otherwise-good seek; add the missing columns as INCLUDE columns.</p>

</details>

<details><summary>Why do more indexes make writes slower?</summary>

<p>Every insert, update of an indexed column, and delete must maintain each index &mdash; extra page writes, extra WAL/log volume, more locking and more storage. Indexes are a read-write trade; unused ones are pure cost, so audit for them.</p>

</details>

<details><summary>Give three ways a query silently stops using an index.</summary>

<p>Wrapping the column in a function (<code>WHERE YEAR(created) = 2026</code>), an implicit type conversion (string parameter against an int column, or nvarchar vs varchar), and leading wildcards (<code>LIKE '%term'</code>). All three destroy the sort order the index depends on.</p>

</details>

#### Query Processing

<details><summary>What is the difference between the estimated and actual plan, and why does it matter?</summary>

<p>The estimate is what the optimiser predicted from statistics; the actual shows what really happened. The gap is the diagnosis &mdash; a row estimate off by orders of magnitude usually means stale statistics, parameter sniffing or a non-sargable predicate, and it is why the engine picked a bad join strategy.</p>

</details>

<details><summary>Nested loop, hash join and merge join: when is each right?</summary>

<p>Nested loop: small outer input with an index on the inner side &mdash; great for selective lookups, catastrophic on large unindexed inputs. Hash join: large unsorted inputs, needs memory for the build side. Merge join: both inputs already sorted on the join key, typically by index order.</p>

</details>

<details><summary>What is parameter sniffing and what does it look like in production?</summary>

<p>The engine compiles a plan for the first parameter values and reuses it. A plan tuned for a selective value performs terribly for a common one (or vice versa), so the same query is fast for months then suddenly slow after a recompile &mdash; with no code change to blame.</p>

</details>

<details><summary>What makes a predicate non-sargable, and why does the term matter?</summary>

<p>Non-sargable means the engine cannot use an index seek &mdash; caused by functions on the column, implicit conversions, OR across different columns, or leading wildcards. Recognising it turns &ldquo;the database is slow&rdquo; into a specific, fixable line of SQL.</p>

</details>

#### Transactions

<details><summary>Name the four isolation levels and the anomaly each one still allows.</summary>

<p>Read Uncommitted &mdash; dirty reads. Read Committed &mdash; non-repeatable reads. Repeatable Read &mdash; phantoms (in some engines). Serializable &mdash; none, at the cost of blocking or serialization failures. Snapshot/MVCC gives repeatable reads without blocking readers, at the cost of version storage and write conflicts.</p>

</details>

<details><summary>What causes a deadlock, and what is the standard prevention?</summary>

<p>Two transactions acquire the same resources in different orders, each holding what the other needs. Prevention: always acquire in a consistent order, keep transactions short, touch fewer rows (better indexes mean fewer locks), and make the application retry on the deadlock error rather than treating it as fatal.</p>

</details>

<details><summary>MVCC: what does it buy, and what does it cost?</summary>

<p>Readers never block writers and writers never block readers, because each sees a consistent snapshot. The cost is version storage and cleanup &mdash; Postgres needs VACUUM to reclaim dead tuples, and a long-running transaction holds back that cleanup, causing bloat.</p>

</details>

<details><summary>What does a long-running transaction actually cost the system?</summary>

<p>It holds locks, pins the oldest snapshot (blocking version cleanup), grows the log because it cannot be truncated past an open transaction, and lengthens crash recovery. Open the transaction as late as possible, and never hold one across a network call or user think-time.</p>

</details>

#### Operations

<details><summary>What do WAL / the transaction log actually guarantee?</summary>

<p>Durability and crash recovery: changes are written to the log before the data pages, so a crash can be replayed to a consistent state. It is also the basis of replication and point-in-time restore &mdash; which is why log growth is an availability concern, not just a disk one.</p>

</details>

<details><summary>Define RPO and RTO and how they drive backup design.</summary>

<p>RPO is how much data you can afford to lose (drives backup and log-shipping frequency). RTO is how long you can be down (drives restore strategy &mdash; standby replica versus restoring a full backup). Both must be measured by an actual restore, not assumed.</p>

</details>

<details><summary>How do you add a NOT NULL column to a large hot table without downtime?</summary>

<p>Expand/contract: add it nullable (metadata-only in modern engines), backfill in batches with pauses, add a default and constraint once populated, deploy code that writes it, then enforce NOT NULL. Never a single ALTER that rewrites the table under load.</p>

</details>

<details><summary>Why does connection pooling matter more than it looks?</summary>

<p>Establishing a connection is expensive (handshake, auth, backend process or thread), and databases cap concurrent connections far lower than app instances assume. Without pooling &mdash; or with pools sized per instance times replicas &mdash; you exhaust the server's limit and everything fails at once.</p>

</details>

#### Scaling

<details><summary>Read replicas: what problem do they solve, and what problem do they create?</summary>

<p>They scale reads and isolate reporting from the primary. They create replication lag: a read immediately after a write may not see it. Route reads that must be current to the primary, or use read-your-writes handling &mdash; sticky sessions or a LSN/timestamp token.</p>

</details>

<details><summary>When is sharding the right answer, and what does it cost you?</summary>

<p>When a single primary cannot hold the write volume or the data set &mdash; and not before. It costs cross-shard joins and transactions, a shard key you can never easily change, rebalancing complexity, and every query needing to know its key. Vertical scale and read replicas first.</p>

</details>

<details><summary>What makes a good shard key?</summary>

<p>High cardinality, even distribution, and present in nearly every query so requests hit one shard. Tenant id is often ideal in B2B; a monotonically increasing id is the classic bad choice because every new write lands on the same shard.</p>

</details>

#### Security

<details><summary>How does row-level security differ from filtering in the application?</summary>

<p>RLS is enforced by the database on every query, including ad-hoc ones and raw SQL, so a forgotten WHERE cannot leak data. Application filtering is easier to reason about and debug but is only as reliable as the code path &mdash; which is why multi-tenant systems increasingly use both.</p>

</details>

#### Advanced

<details><summary>When is a materialised view the right tool, and what is the catch?</summary>

<p>When an expensive aggregate is read far more often than the underlying data changes. The catch is refresh: it is stale between refreshes, a full refresh can be as expensive as the original query, and concurrent refresh needs a unique index &mdash; so staleness must be acceptable to the business.</p>

</details>

<details><summary>What are JSON columns good for in a relational database, and where do they go wrong?</summary>

<p>Good for genuinely variable attributes on an otherwise-relational row &mdash; product specs, settings, third-party payloads &mdash; and they remain queryable and indexable (GIN/computed columns). They go wrong as a way to avoid modelling: relationships, constraints and typed queries all disappear, and you have rebuilt a document store inside your SQL database without any of the tooling.</p>

</details>

### Linux

#### Ground floor

<details><summary>What is the difference between the Linux kernel and a distribution?</summary>

<p>The kernel owns the hardware — scheduling, memory, and the only path to disks and network. A distribution is the kernel plus everything that makes it usable: shell, core utilities, package manager, init system, libraries. Ubuntu, Alpine and RHEL are distributions.</p>

</details>

<details><summary>Walk through what bash does between pressing Enter and the program running.</summary>

<p>Splits the line into words → expands variables, globs, command substitution and <code>~</code> → finds the program by walking <code>$PATH</code> left to right → forks itself and execs the binary → waits for it to exit. Everything after the lookup is an ordinary process.</p>

</details>

<details><summary><code>command not found</code> — what does it actually mean?</summary>

<p>The shell could not find that name in <code>$PATH</code>. It almost never means the software is missing. Installed to a directory not on the path, or a <code>$PATH</code> exported in your interactive shell that a systemd unit or cron job never sees.</p>

</details>

<details><summary>What does &ldquo;everything is a file&rdquo; buy you, and which two production problems come from it?</summary>

<p>One set of verbs (read/write) works on disks, sockets, terminals, processes and kernel settings, so tools compose across all of them. It also explains missing logs (the app wrote to stdout and nothing captured it) and &ldquo;too many open files&rdquo; — file-descriptor exhaustion, usually leaked sockets.</p>

</details>

#### Shell

<details><summary>Why is <code>command 2>&1 > file</code> wrong?</summary>

<p>Redirections are evaluated left to right. <code>2>&1</code> points stderr at the <em>current</em> stdout (the screen), and only then is stdout moved to the file. The correct order is <code>command > file 2>&1</code>.</p>

</details>

<details><summary>Why does <code>sort file.txt > file.txt</code> destroy the file?</summary>

<p>The shell truncates the redirect target when it parses the line — before sort even starts. Sort then reads an empty file. Write to a temporary name and move it into place.</p>

</details>

<details><summary>Give the shape of the pipeline that answers most log questions.</summary>

<p><code>… | sort | uniq -c | sort -rn | head</code> — extract the field you care about, group it, count it, rank it, take the top. Example: <code>awk '{print $1}' access.log | sort | uniq -c | sort -rn | head -10</code>.</p>

</details>

<details><summary>Why must you quote <code>"$file"</code>?</summary>

<p>Unquoted, the shell word-splits on whitespace — <code>rm $file</code> where the value is <code>my report.txt</code> tries to delete two files. Quote every variable, and use <code>--</code> to stop values that begin with a dash being read as options.</p>

</details>

<details><summary>Which startup files does a cron job or systemd unit read?</summary>

<p>None of them. Non-interactive, non-login shells read neither <code>~/.bashrc</code> nor <code>~/.bash_profile</code>, and often run under <code>/bin/sh</code> rather than bash. Use absolute paths, set environment explicitly, and redirect output to a log.</p>

</details>

#### Permissions

<details><summary>What does the execute bit mean on a directory?</summary>

<p>Permission to <em>enter</em> it and reach things inside — not to run it. A directory without <code>x</code> makes everything below unreachable regardless of file permissions, which is why &ldquo;Permission denied&rdquo; often traces to a parent directory. Check with <code>namei -l /full/path</code>.</p>

</details>

<details><summary>What do 644, 600, 755 and 700 mean, and when do you use each?</summary>

<p>644 rw-r--r-- config the world may read. 600 rw------- secrets and private keys. 755 rwxr-xr-x an executable or a directory everyone may enter. 700 rwx------ a private directory. SSH refuses keys that are not 600 with a 700 <code>~/.ssh</code>.</p>

</details>

<details><summary>Why is adding a user to the <code>docker</code> group equivalent to giving them root?</summary>

<p>They can start a container that mounts the host filesystem and edit anything on it. It is a genuine privilege escalation, not a convenience — grant it knowing that.</p>

</details>

#### Processes

<details><summary>SIGTERM versus SIGKILL — and why does it matter on every deploy?</summary>

<p>SIGTERM (15) asks the process to stop; it can catch the signal, finish in-flight work and flush. SIGKILL (9) is executed by the kernel and cannot be caught, so work is lost. Orchestrators send TERM, wait a grace period, then KILL — an app that ignores TERM drops requests on every deploy.</p>

</details>

<details><summary>A process is in state <code>D</code>. What does that mean and can you kill it?</summary>

<p>Uninterruptible sleep — stuck in a kernel call, almost always waiting on I/O such as a hung NFS mount or a failing disk. You cannot kill it, not even with <code>-9</code>. Fix the I/O.</p>

</details>

<details><summary><code>free -h</code> shows almost no free memory. Is that a problem?</summary>

<p>No. Linux uses spare RAM as disk cache deliberately — empty RAM is wasted RAM. Read the <b>available</b> column, which counts reclaimable cache. Panicking about low &ldquo;free&rdquo; is a rite of passage.</p>

</details>

<details><summary>Your app vanished with no exception and no stack trace. Where do you look?</summary>

<p>The OOM killer. Check <code>dmesg -T | grep -i "killed process"</code> or <code>journalctl -k</code>. In containers the limit is the cgroup limit, not host RAM — which is why a service dies at 512 MB on a machine with 64 GB free.</p>

</details>

#### Tools

<details><summary>Which grep flags matter most when reading a stack trace?</summary>

<p><code>-A n</code> lines after a match, <code>-B n</code> before, <code>-C n</code> either side. Also <code>-i</code> case-insensitive, <code>-v</code> invert, <code>-r</code> recursive, and <code>--line-buffered</code> when piping <code>tail -f</code> through grep, or output appears to stall.</p>

</details>

#### Networking

<details><summary>Give the checklist for &ldquo;the service is down&rdquo;.</summary>

<p>1) Is the process alive (<code>systemctl status</code>)? 2) Is it listening (<code>ss -tulpn</code>)? 3) On the right interface — 127.0.0.1 is local-only? 4) Does it answer locally (<code>curl localhost</code>)? 5) Remotely (<code>nc -zv</code>)? If 4 works and 5 does not, it is firewall, security group, or a loopback-only bind.</p>

</details>

<details><summary>Why is <code>ASPNETCORE_URLS=http://localhost:8080</code> wrong in a container?</summary>

<p>It binds to loopback only, so the port is unreachable from outside the container no matter how you publish it. Bind to all interfaces: <code>http://+:8080</code> or <code>http://0.0.0.0:8080</code>.</p>

</details>

#### Services

<details><summary>You edited a systemd unit and restarted, but nothing changed. Why?</summary>

<p>systemd still has the old definition cached — you must run <code>systemctl daemon-reload</code> after editing a unit file. Also watch <code>Restart=always</code> without <code>StartLimitBurst</code>: a crash-looping app restarts forever instead of failing loudly.</p>

</details>

<details><summary>Disk is 100% full but <code>du</code> shows nothing large. What happened?</summary>

<p>A file was deleted while a process still had it open — the inode survives until the descriptor closes, so the space is not freed. Find it with <code>lsof | grep deleted</code>, then restart the process or truncate instead: <code>: > /var/log/app.log</code>. Also check <code>df -i</code>: inodes can run out while space remains.</p>

</details>

#### .NET on Linux

<details><summary>Name the Windows habits that break on Linux.</summary>

<p>Case-sensitive filenames (the number-one container bug), <code>/</code> not <code>\\</code>, no registry or DPAPI (Data Protection needs a shared key store), IANA time-zone ids, CRLF line endings breaking shell scripts, and a service account with no write access anywhere by default.</p>

</details>

<details><summary>What do the four characters <code>set -euo pipefail</code> prevent?</summary>

<p><code>-e</code> exit on error rather than continuing past a failed step; <code>-u</code> error on an undefined variable, catching typos before they delete something; <code>-o pipefail</code> a failure anywhere in a pipeline fails the line. Without them a script deploys the previous binary and reports success.</p>

</details>

### Data and LINQ

#### Collections

<details><summary>How do you choose between List, Dictionary, HashSet and Queue?</summary>

<p>By the operation you repeat most. Index or append → List (O(1) amortised). Lookup by key → Dictionary (O(1) average). Membership → HashSet. FIFO → Queue, LIFO → Stack. Ordered by priority → PriorityQueue. Choosing by familiarity rather than access pattern is how O(n) scans end up in hot loops.</p>

</details>

<details><summary>When is FrozenDictionary worth it?</summary>

<p>When a dictionary is built once and read enormously often — a lookup table populated at startup. Construction is slower, reads are meaningfully faster. Useless for anything mutated after creation.</p>

</details>

<details><summary>What is the hidden cost of <code>List&lt;T&gt;</code> growth, and how do you avoid it?</summary>

<p>It doubles capacity when full, allocating a new array and copying — so building a large list from scratch does O(log n) reallocations and copies. Pass the expected size to the constructor when you know it.</p>

</details>

<details><summary>ConcurrentDictionary.GetOrAdd — what is the subtlety?</summary>

<p>The value factory can run more than once under contention, though only one result is stored. So the factory must be side-effect free and cheap; if creation is expensive or must happen once, store a <code>Lazy&lt;T&gt;</code> instead.</p>

</details>

#### LINQ

<details><summary>What is deferred execution and where does it bite?</summary>

<p>LINQ operators build a query and do nothing until enumerated. Enumerating twice runs it twice (two database round trips); the captured variables are read at enumeration time, not at definition; and an exception surfaces at <code>ToList()</code>, far from the line that caused it.</p>

</details>

<details><summary>IEnumerable vs IQueryable — what actually differs?</summary>

<p>IEnumerable takes delegates and runs in your process on data already fetched. IQueryable takes expression trees, so a provider can translate them to SQL. Calling <code>AsEnumerable()</code> mid-query is the boundary — everything after it is in memory, and it is how a full table scan appears by accident.</p>

</details>

<details><summary>Why is <code>Count()</code> inside a loop dangerous on IEnumerable?</summary>

<p>It enumerates the whole sequence each call, turning a loop into quadratic work. Materialise once with <code>ToList()</code>, or use <code>Count</code> on a collection that stores it.</p>

</details>

<details><summary>How do you aggregate in the database rather than in memory?</summary>

<p>Keep the chain on IQueryable and project to aggregates: <code>.GroupBy(...).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) })</code> then <code>ToListAsync()</code>. If you call <code>ToList()</code> first, you have transferred every row to compute one number.</p>

</details>

#### Indexes

<details><summary>Why does <code>WHERE YEAR(created_at) = 2026</code> ignore the index?</summary>

<p>Wrapping the column in a function destroys the sort order the index depends on, so the engine cannot seek — it must evaluate the function for every row. Rewrite as a range: <code>created_at >= '2026-01-01' AND created_at &lt; '2027-01-01'</code>.</p>

</details>

<details><summary>What is the leftmost-prefix rule?</summary>

<p>An index on (a, b, c) serves predicates on a, (a, b), and (a, b, c) — but not b alone or (b, c), because the tree is sorted by a first. Column order is a design decision, not arbitrary.</p>

</details>

<details><summary>You see &ldquo;Rows Removed by Filter: 1,932,000&rdquo; in a plan. What does it tell you?</summary>

<p>The engine read 2.3M rows to keep 400k — a scan where a seek was possible. It is the clearest signal that the filtering columns need an index, ideally covering, so the query becomes an index-only scan with zero heap fetches.</p>

</details>

#### Memory

<details><summary>When does an object go on the Large Object Heap, and why care?</summary>

<p>At 85,000 bytes or more. The LOH is collected with gen 2 and is not compacted by default, so large short-lived arrays fragment it and force expensive full collections. Pool large buffers with <code>ArrayPool&lt;T&gt;</code> instead of allocating them per request.</p>

</details>

<details><summary>What does <code>Span&lt;T&gt;</code> let you avoid, and what can it not do?</summary>

<p>It gives a view over memory so you can slice and parse without allocating substrings or arrays. It is a ref struct: it cannot be a field, cannot be captured in a lambda, and cannot cross an await. Use <code>Memory&lt;T&gt;</code> when you need to store it or go async.</p>

</details>

<details><summary>Why is string concatenation in a loop quadratic?</summary>

<p>Strings are immutable, so every <code>+=</code> allocates a new string and copies everything accumulated so far. Use StringBuilder for unknown lengths, or <code>string.Create</code> when the final length is known.</p>

</details>

#### SQL

<details><summary>What does a covering index give you?</summary>

<p>Every column the query needs lives in the index, so the engine never touches the base table — no key lookups, far fewer buffers read. In Postgres it shows as an Index Only Scan with <code>Heap Fetches: 0</code>.</p>

</details>

### Practical

#### Logging

<details><summary>Why is <code>_logger.LogInformation($"Order {id} placed")</code> wrong?</summary>

<p>String interpolation bakes the values into the text, so you lose every queryable field and you pay the formatting cost even when the level is disabled. Use the message template with parameters — the template stays constant and OrderId becomes a field you can filter and aggregate on.</p>

</details>

<details><summary>What does a logging scope do, and where does it not flow?</summary>

<p>It attaches context (TenantId, TraceId) to every log written inside the block, riding on AsyncLocal. It does not reach work that starts later from a queue — a consumer must re-establish the scope from the message's own correlation id.</p>

</details>

<details><summary>Traces, metrics or logs — which answers which question?</summary>

<p>Metrics: is it healthy, what is the trend (and they are not sampled, so count with them). Traces: where did the time go for this class of request. Logs: what exactly happened to this one request. Computing a rate from sampled traces gives you the rate of the sample.</p>

</details>

#### EF Core

<details><summary>How do you make soft delete impossible to forget?</summary>

<p>Two mechanisms: a SaveChanges interceptor that flips <code>EntityState.Deleted</code> to <code>Modified</code> and sets IsDeleted, plus a loop in OnModelCreating that applies a global query filter to every entity implementing the marker interface — so current and future tables are covered.</p>

</details>

<details><summary>What bypasses EF interceptors and query filters entirely?</summary>

<p><code>ExecuteUpdate</code>, <code>ExecuteDelete</code>, raw SQL and Dapper — none of them go through SaveChanges or the change tracker. A bulk delete really deletes rows your soft-delete filter was protecting.</p>

</details>

<details><summary>Why is projecting inside the query better than mapping after ToListAsync?</summary>

<p>Inside, EF translates the projection into the SQL column list — often collapsing a wide entity load plus includes into one narrow join. After materialisation the data has already crossed the wire, and no mapper can undo that.</p>

</details>

#### DI

<details><summary>Three queries with <code>Task.WhenAll</code> on one DbContext throws. Why, and what are the fixes?</summary>

<p>DbContext is not thread-safe and allows one operation at a time. Fixes: run them sequentially (usually fast enough), combine into one query, or use <code>IDbContextFactory</code> so each task gets its own context — bearing in mind each takes a pooled connection.</p>

</details>

<details><summary>How do you use a scoped service inside a BackgroundService?</summary>

<p>You cannot inject it — the BackgroundService is a singleton and would capture it forever. Inject <code>IServiceScopeFactory</code> and create a scope per unit of work (per tick, per message), resolving the scoped services inside it and disposing at the end.</p>

</details>

<details><summary>What is a captive dependency?</summary>

<p>A longer-lived service holding a shorter-lived one — a singleton capturing a scoped DbContext. The first request's instance stays alive for every later user: stale data, tracked entities accumulating, and cross-request leakage. Scope validation catches it at startup.</p>

</details>

#### Async

<details><summary>Why is <code>.Result</code> still wrong in ASP.NET Core, which has no SynchronizationContext?</summary>

<p>No deadlock, but it blocks a thread-pool thread for the whole I/O duration. Under load the pool starves — new threads are injected only 1–2 per second — so latency climbs and everything times out together. It also wraps exceptions in AggregateException.</p>

</details>

<details><summary>State the rules for ValueTask.</summary>

<p>Await it exactly once. Never await twice, never store it, never pass it to WhenAll without <code>.AsTask()</code>. It is worth the rules only on hot paths where synchronous completion dominates — caches, buffered readers. Everything else returns Task.</p>

</details>

<details><summary>Does <code>Task.WhenAll</code> start the tasks?</summary>

<p>No — they are already running from the moment they were created. That is why WhenAll over calls sharing a DbContext throws, and why building 10,000 tasks in a loop can flood a connection pool before you ever await.</p>

</details>

#### Runtime

<details><summary>Why can reducing allocations make GC worse?</summary>

<p>Collection cost is proportional to <b>survivors</b>, not garbage. Pooling short-lived objects keeps them alive, promotes them to gen 2, and turns work the gen-0 collector did almost for free into full-collection tracing. Pool large buffers, not small DTOs.</p>

</details>

<details><summary>Where does boxing hide in ordinary code?</summary>

<p>Non-generic collections (ArrayList), <code>params object[]</code> APIs, <code>string.Format</code>, a struct implementing a non-generic interface, and <code>Dictionary&lt;string, object&gt;</code>. If a profile shows allocation you cannot account for, look for a <code>params object[]</code> in the hot path.</p>

</details>

<details><summary>Why is <code>new HttpClient()</code> per request wrong, and why is a static one also imperfect?</summary>

<p>Per request: disposing leaves sockets in TIME_WAIT and exhausts ephemeral ports under load. A single static instance fixes that but caches DNS forever. <code>IHttpClientFactory</code> pools handlers and rotates them, solving both.</p>

</details>

#### Cloud

<details><summary>Name three API Gateway limits that change your design.</summary>

<p>A hard request timeout (~29–30s) — long work must become 202 + polling; a payload cap (~10 MB) — uploads must use presigned URLs direct to storage; and per-account burst quotas. Knowing these three numbers up front prevents an architecture that cannot work.</p>

</details>

<details><summary>What must the server control when issuing a presigned upload URL?</summary>

<p>The object key (never client-supplied — path traversal into another tenant's prefix), the content type and size limits, and a short expiry. Then verify after upload with a HEAD plus magic-byte check, because the client can upload something other than what it declared.</p>

</details>

#### Builds

<details><summary>How do you make an idempotency key claim actually atomic?</summary>

<p>Insert the key and let a unique constraint arbitrate, catching the violation — not SELECT-then-INSERT, which two simultaneous retries both pass. Store the response before returning it, and reject a reused key that arrives with a different payload fingerprint.</p>

</details>

<details><summary>Why must a webhook receiver verify, store, and return 200 fast?</summary>

<p>Processing inline means the provider times out and retries, so you handle the same event twice concurrently, and a downstream outage becomes a retry storm. Verify the signature over the raw body with a constant-time compare, dedupe on the event id, enqueue, and ACK in milliseconds.</p>

</details>

<details><summary>What order do you attack a slow endpoint in?</summary>

<p>Measure first. Then: reduce the work (compute in the database, project narrowly), index what remains (read the plan, build CONCURRENTLY), and only then cache — with invalidation designed in. Caching first delivers 'fast' while still transferring everything on every miss, plus stale data.</p>

</details>

<details><summary>Why must the outbox relay reuse the outbox row id as the message id?</summary>

<p>A crash between publishing and marking the row processed causes a republish. With a stable id the consumer's inbox recognises the duplicate; with a fresh id every retry looks like a new event and the at-least-once guarantee becomes at-least-once <em>processing</em>.</p>

</details>

<details><summary>Three replicas run the same timer. How do you make a nightly job safe?</summary>

<p>Exclusion plus idempotence. A Postgres advisory lock (or Redis SET NX) means one instance runs; a JobRuns row keyed by date means a repeat after a mid-run crash is harmless. A lock alone is not enough — the holder can die halfway.</p>

</details>

<details><summary>Why is renaming a column not safe in one deploy?</summary>

<p>During a rolling deploy two code versions run against one database. Expand (add the new column), dual-write, backfill in batches, switch reads, then contract (drop the old column) — four deploys, each independently rollback-safe.</p>

</details>

<details><summary>Why should a liveness probe never check the database?</summary>

<p>A database blip makes every pod report unhealthy, the orchestrator restarts them all at once, and a recovering database faces a thundering herd of cold instances. Liveness answers 'is this process wedged'; readiness answers 'can I serve traffic'.</p>

</details>

### APIs, caching and security

#### APIs

<details><summary>Design a POST endpoint that a client will retry. What are the four pieces?</summary>

<p>An <code>Idempotency-Key</code> header the client generates once per logical operation; an atomic claim via a unique constraint (not SELECT-then-INSERT); the response stored <em>before</em> it is returned; and a payload fingerprint so the same key with a different body is rejected with 422 rather than silently replayed.</p>

</details>

<details><summary>400 vs 401 vs 403 vs 404 vs 409 vs 422 — when is each right?</summary>

<p>400 malformed request. 401 not authenticated (we do not know who you are). 403 authenticated but not permitted. 404 not found — also the right answer for resources the caller may not see, to avoid confirming existence. 409 a conflicting concurrent change. 422 well-formed but semantically rejected (business rule).</p>

</details>

<details><summary>What belongs in an error response, and what must never?</summary>

<p>Belongs: a stable machine-readable <code>code</code>, a human-readable title, the trace id, and field-level detail for validation. Never: stack traces, inner exception messages, SQL, or connection details — they name your types, paths and versions, which is reconnaissance for an attacker.</p>

</details>

<details><summary>Why does offset paging degrade, and what replaces it?</summary>

<p>The engine still walks every skipped row, so deep pages get slower, and concurrent inserts shift the window producing duplicates and skips. Keyset paging filters by the last row seen — constant cost and stable — at the price of no random page jumps. Needs a composite index including a unique tiebreaker.</p>

</details>

<details><summary>What do ETags and conditional requests buy you?</summary>

<p>Bandwidth and correctness. <code>If-None-Match</code> lets the server answer 304 with no body when nothing changed; <code>If-Match</code> on writes gives optimistic concurrency over HTTP — the update is rejected with 412 if someone else changed the resource first.</p>

</details>

<details><summary>Where can an API version live, and what is the retirement discipline?</summary>

<p>URL segment (most visible), a header, or the media type. Whichever you choose: report supported versions in a response header, mark the old one deprecated, send a <code>Sunset</code> header with the retirement date plus a <code>Link</code> to the migration guide — and expect two live versions as a steady state, not five.</p>

</details>

<details><summary>Which rate-limiting algorithm is the usual default, and what must you partition by?</summary>

<p>Token bucket — it allows a burst while capping the sustained rate. Partition by the strongest identity available (API key, then tenant, then user), with IP as a last resort, because proxies and NAT put thousands of users behind one address. Always return <code>Retry-After</code>.</p>

</details>

<details><summary>Why should large uploads never go through your API?</summary>

<p>Gateways cap payloads (~10 MB) and requests (~29 s), and proxying bytes ties up your threads and bandwidth. Instead issue a presigned URL: the server authorises, chooses the key, constrains type and size, and the client PUTs directly to storage — then a completion call verifies with a HEAD plus magic-byte check.</p>

</details>

#### Caching

<details><summary>Describe cache-aside and why it is the default.</summary>

<p>Read from the cache; on a miss read the origin and populate; on writes invalidate. It needs no special store support and fails safe — a cache outage degrades to slow rather than broken. Read-through, write-through and write-behind push more responsibility into the cache layer.</p>

</details>

<details><summary>What is a cache stampede, and what are the three defences?</summary>

<p>A hot key expires and every concurrent request misses at once, all hitting the origin. Defences: single-flight (one caller populates, the rest await — what HybridCache does), jittered TTLs so keys do not expire together, and serve-stale-while-revalidating.</p>

</details>

<details><summary>Why does a two-level cache need a short L1 expiry?</summary>

<p>L1 is per instance, so a write on one instance leaves the others serving their own stale copies until their local entries expire. Keep L1 in seconds (absorbing bursts) even when L2 lives for minutes — the local layer is for load, not for correctness.</p>

</details>

<details><summary>What must a cache key always include, and what should you not cache?</summary>

<p>Every input the value depends on — tenant, user, locale, and a version prefix you can bump to invalidate everything. Do not cache cheap indexed lookups (you add a hop and an invalidation bug for nothing) or per-user data under a global key.</p>

</details>

<details><summary>Which is harder, expiry or invalidation — and how do you make invalidation manageable?</summary>

<p>Invalidation. Expiry is a number; invalidation requires knowing every key a write affects. Tag-based removal (<code>RemoveByTagAsync("tenant:42")</code>) driven by domain events keeps it tractable, because the write path publishes the fact and the cache layer reacts.</p>

</details>

<details><summary>What do the main HTTP cache directives mean?</summary>

<p><code>max-age</code> how long a client may reuse it; <code>s-maxage</code> the same for shared caches; <code>no-cache</code> revalidate before reuse (not 'do not store'); <code>no-store</code> never write it down; <code>private</code> browser only, never a CDN; <code>stale-while-revalidate</code> serve stale while refreshing in the background.</p>

</details>

#### Security

<details><summary>How does refresh-token rotation detect theft?</summary>

<p>Each refresh consumes the token and issues a new one in the same family. If a consumed token is presented again, a copy exists — so you revoke the entire family. Attacker and victim are both logged out; the victim simply logs in again.</p>

</details>

<details><summary>Where should a browser store an access token, and why not localStorage?</summary>

<p>An httpOnly, Secure, SameSite cookie — script cannot read it, so an XSS cannot exfiltrate it. localStorage is readable by any script on the page, which turns any XSS into full account takeover.</p>

</details>

<details><summary>What does CORS protect, and where is it enforced?</summary>

<p>It protects the <em>user's</em> credentials from a malicious site making authenticated cross-origin requests. The browser enforces it, not the server — so it is not a server-side security control, and anything that is not a browser ignores it entirely.</p>

</details>

<details><summary>How do you verify a webhook safely?</summary>

<p>Compute the HMAC over the <b>raw body</b> (not your deserialised DTO), compare with <code>FixedTimeEquals</code> — never <code>==</code>, whose early exit leaks timing — and check a timestamp within a short window to stop replays. Verify before deserialising: parsing untrusted JSON is avoidable attack surface.</p>

</details>

<details><summary>Which algorithms are acceptable for passwords, and what is the third verification state?</summary>

<p>Argon2id first choice, PBKDF2 (built in, FIPS-friendly) or bcrypt acceptable; never a fast hash. The state people miss is <code>SuccessRehashNeeded</code> — the only moment you legitimately hold the plaintext and can upgrade a hash stored with weaker parameters.</p>

</details>

<details><summary>Why is a presigned URL a credential?</summary>

<p>Anyone holding it has that exact access until it expires, and it cannot be revoked. So: expiry in minutes, never log it, keep it out of redirects where it lands in a Referer header, and let the server — never the client — choose the object key.</p>

</details>

<details><summary>Which EF Core call is injectable and which is not?</summary>

<p><code>FromSqlRaw($"...{input}")</code> is injectable — the string is built before EF sees it. <code>FromSql($"...{input}")</code> uses an interpolated-string handler that turns each hole into a parameter. With Dapper, always pass an anonymous parameter object.</p>

</details>

<details><summary>Where does personal data leak outside the database?</summary>

<p>Exception messages containing request bodies, trace attributes, log lines, caches, retained broker messages, search indexes, analytics warehouses, support exports and backups. Log ids rather than people, and classify fields so the decision is made once rather than per feature.</p>

</details>

### Casebook questions

One question for each case in the design patterns casebook (4.9) and the database optimisation casebook (7.9). Each answer links to its case.

#### Design patterns casebook

<details><summary>With keyed services, how does the factory pick a sender, and what happens if a channel has no registration?</summary>

<p>It calls <code>GetRequiredKeyedService&lt;INotificationSender&gt;(channel)</code> on the scope's provider. A missing key throws <code>InvalidOperationException</code> at runtime, so resolve every enum value once at startup to fail at boot instead. <a href="04-09-design-patterns-casebook.md#case-factory-keyed-senders">Read the case</a></p>

</details>

<details><summary>Why should <code>OrderBuilder.Shipped()</code> call <code>MarkPaid</code> and <code>Ship</code> instead of setting the status directly?</summary>

<p>Going through the aggregate's public methods keeps built objects in states production can reach, and when the legal sequence changes, only the builder has to change. <a href="04-09-design-patterns-casebook.md#case-builder-test-data">Read the case</a></p>

</details>

<details><summary>In an ASP.NET Core app, what usually plays the role of the concrete factory in Abstract Factory?</summary>

<p>One DI registration module per family, such as <code>AddCloudStorage()</code> and <code>AddOnPremStorage()</code>, selected by a single setting so that all members of a family are registered together. <a href="04-09-design-patterns-casebook.md#case-abstract-factory-storage-family">Read the case</a></p>

</details>

<details><summary>Why does the legacy adapter return <code>Failed(SafeToRetry: false)</code> on a timeout while the REST adapter returns <code>SafeToRetry: true</code>?</summary>

<p>The REST provider de-duplicates on the idempotency key, so a retry cannot charge twice. The legacy gateway has no idempotency, so a timed-out request may have succeeded and must be reconciled, not retried. <a href="04-09-design-patterns-casebook.md#case-adapter-payment-gateways">Read the case</a></p>

</details>

<details><summary>With Scrutor you call <code>Decorate(..., typeof(TimingHandler&lt;&gt;))</code> and then <code>Decorate(..., typeof(AuditingHandler&lt;&gt;))</code>. Which runs first on a request?</summary>

<p><code>AuditingHandler</code>. Each <code>Decorate</code> wraps what is already registered, so the last call is the outermost layer: Auditing, then Timing, then the real handler. <a href="04-09-design-patterns-casebook.md#case-decorator-command-audit">Read the case</a></p>

</details>

<details><summary>Why does the PDF proxy close a retired browser only when its in-flight count reaches zero?</summary>

<p>Recycling happens while other requests may still be rendering on that browser. Closing it at once would fail those renders, so the last request to finish on it closes it. <a href="04-09-design-patterns-casebook.md#case-proxy-pdf-renderer">Read the case</a></p>

</details>

<details><summary>What separates a checkout facade from a god service?</summary>

<p>A facade sequences one workflow over subsystems that keep their own rules and remain usable directly. A god service accumulates many workflows and the business logic itself. <a href="04-09-design-patterns-casebook.md#case-facade-checkout">Read the case</a></p>

</details>

<details><summary>In a composite rule tree, what does an empty <code>all</code> group evaluate to, and why validate against it?</summary>

<p>It is vacuously true, so the promotion would apply to everyone. Reject empty groups when a rule is saved. <a href="04-09-design-patterns-casebook.md#case-composite-discount-rules">Read the case</a></p>

</details>

<details><summary>Why is <code>string.Intern</code> the wrong way to de-duplicate strings in a nightly import?</summary>

<p>The intern pool is process-wide and its memory is unlikely to be released until the runtime exits, so every import's values accumulate. It also needs a <code>string</code>, so you allocate before you can de-duplicate. A per-import pool is collected when the import ends. <a href="04-09-design-patterns-casebook.md#case-flyweight-import-strings">Read the case</a></p>

</details>

<details><summary>How do you add a fifth export format in this design, and what stops two exporters claiming the same format?</summary>

<p>Write one class implementing <code>IReportExporter</code> and register it with <code>AddSingleton&lt;IReportExporter, ...&gt;()</code>. The registry receives it through <code>IEnumerable&lt;IReportExporter&gt;</code>, and its <code>ToDictionary</code> throws when the singleton registry is first resolved if two exporters share a <code>Format</code>. <a href="04-09-design-patterns-casebook.md#case-strategy-export-formats">Read the case</a></p>

</details>

<details><summary>In <code>PartnerImport&lt;TRow&gt;</code>, why is <code>RunAsync</code> non-virtual while <code>ParseAsync</code>, <code>Validate</code> and <code>MapToEntity</code> are abstract?</summary>

<p>The skeleton (validate, report rejected rows, batch, reconcile) must be identical for every partner, so no subclass may skip or reorder it. Only the partner-specific steps vary, and they are the only members a subclass can supply. <a href="04-09-design-patterns-casebook.md#case-template-method-partner-imports">Read the case</a></p>

</details>

<details><summary>Why did <code>Status != "Trial"</code> let the renewal job charge cancelled customers, and what replaced it?</summary>

<p>A deny-list lets through every state it does not name, and the cancel endpoint had set a flag without changing <code>Status</code>. The job now selects <code>StatesAllowing(BillingTrigger.Renew)</code> from the transition table, an allow-list, and <code>Status</code> is the only state. <a href="04-09-design-patterns-casebook.md#case-state-subscription-billing">Read the case</a></p>

</details>

<details><summary>Why must a handler added with <code>AddHttpMessageHandler&lt;T&gt;()</code> be registered as transient, and where does the cached token live?</summary>

<p>The factory builds a new chain every handler lifetime (two minutes by default) and sets each handler's <code>InnerHandler</code> once, so a reused instance throws <code>InvalidOperationException</code>. Shared state such as the token lives in a singleton that the transient handler depends on. <a href="04-09-design-patterns-casebook.md#case-chain-delegating-handlers">Read the case</a></p>

</details>

<details><summary>How does the reprice handler avoid applying +5% twice when its job is restarted after a crash?</summary>

<p>Each batch commits the new prices and their <code>PriceChange</code> history rows in one <code>SaveChangesAsync</code>, and the handler skips products that already have a history row for this job id, so a resumed job continues where it stopped. <a href="04-09-design-patterns-casebook.md#case-command-bulk-admin-actions">Read the case</a></p>

</details>

<details><summary>Which <code>OrderPlaced</code> handlers run before <code>SaveChangesAsync</code> commits, and which run after?</summary>

<p>Before the commit, in the same transaction: changes that must be true together with the order, such as loyalty points, plus writing the outbox row. After the commit, via the outbox: email, analytics, the warehouse webhook and Slack, so their failures can neither fail nor roll back checkout. <a href="04-09-design-patterns-casebook.md#case-observer-domain-events">Read the case</a></p>

</details>

<details><summary>Behaviours are registered Logging, then Validation, then Transaction. In what order do they run, and why is Transaction last?</summary>

<p>Logging is outermost and runs first, then Validation, then Transaction directly around the handler. An invalid request is rejected before any transaction opens, and the logged time includes validation. <a href="04-09-design-patterns-casebook.md#case-mediator-pipeline-behaviours">Read the case</a></p>

</details>

<details><summary><code>ForCheckoutAsync</code> returns <code>NoLoyaltyAccount.Instance</code> for a guest but throws for a member with no account row. Why not return the null object in both cases?</summary>

<p>For a guest, having no account is normal and should behave like an empty account. For a member it means a broken sign-up, and a null object there would hide the bug: the customer would silently earn nothing. <a href="04-09-design-patterns-casebook.md#case-null-object-loyalty">Read the case</a></p>

</details>

<details><summary>You add a <code>ContainsText</code> node. What happens with the classic Visitor, and what happens with the sealed-record switch version?</summary>

<p>With the Visitor, <code>IFilterVisitor&lt;T&gt;</code> gains <code>VisitContainsText</code>, so every visitor fails to compile until it handles the node. With the switch version everything still compiles, and the new node hits the <code>_ =&gt; throw</code> arm at runtime. <a href="04-09-design-patterns-casebook.md#case-visitor-filter-tree">Read the case</a></p>

</details>

#### Database optimisation casebook

<details><summary>For <code>WHERE TenantId = @t AND Status = @s AND IssuedAt &gt;= @from</code>, why does an index on <code>(TenantId, IssuedAt, Status)</code> read far more than one on <code>(TenantId, Status, IssuedAt)</code>?</summary>

<p>The seek can use TenantId equality and then the IssuedAt range, but inside that range Status values are scattered, so Status is only a residual filter on every row in the range. With Status before IssuedAt, the seek covers exactly the matching rows, already in IssuedAt order. <a href="07-09-database-optimisation-casebook.md#case-index-equality-before-range">Read the case</a></p>

</details>

<details><summary>Why doesn't SQL Server use a filtered index <code>WHERE Status = 0</code> for a query with <code>WHERE Status = @status</code>, and how do you fix it from EF Core?</summary>

<p>The cached plan must be valid for any @status, so the optimiser can't prove the filter holds. Make the value a literal: write the enum constant in the lambda (EF Core inlines constants but parameterises captured variables), wrap a variable in <code>EF.Constant()</code>, or use <code>OPTION (RECOMPILE)</code>. <a href="07-09-database-optimisation-casebook.md#case-filtered-index-job-queue">Read the case</a></p>

</details>

<details><summary>EF Core sends <code>@p nvarchar(4000)</code> for an indexed <code>varchar(32)</code> column in a <code>SQL_Latin1_General_CP1_CI_AS</code> database. What happens, and what is the fix?</summary>

<p>SQL Server converts the column to nvarchar on every row (<code>CONVERT_IMPLICIT</code> on the column side), so it can't seek and scans the index. Map the property with <code>.IsUnicode(false).HasMaxLength(32)</code> or <code>HasColumnType("varchar(32)")</code> so the parameter is varchar. (Under a Windows collation it can often still seek via GetRangeThroughConvert, at a higher cost.) <a href="07-09-database-optimisation-casebook.md#case-implicit-conversion-varchar">Read the case</a></p>

</details>

<details><summary>Why does <code>WHERE c.Email = @q OR c.Phone = @q OR o.OrderNumber = @q</code> over a Customers-Orders join scan both tables, even though each column is indexed?</summary>

<p>The OR spans two tables, so it can only be evaluated after the join, and no single index answers it. Rewrite it as a UNION (or EF Core <code>Concat</code> inside <code>Contains</code>) of branches that each filter one table and can seek. Within a single table, PostgreSQL can often combine the indexes itself with a BitmapOr. <a href="07-09-database-optimisation-casebook.md#case-or-across-columns-union">Read the case</a></p>

</details>

<details><summary>Name three cheaper alternatives to running <code>COUNT(*)</code> over 20M matching rows on every page of a grid.</summary>

<p>Fetch PageSize + 1 rows to know whether there is a next page; a capped count (<code>Take(10_001).CountAsync()</code>, i.e. <code>COUNT(*)</code> over <code>TOP (10001)</code> or <code>LIMIT 10001</code>) to show 'more than 10,000'; and estimates: <code>pg_class.reltuples</code> or the EXPLAIN row estimate in PostgreSQL, <code>sys.dm_db_partition_stats</code> for unfiltered totals in SQL Server. <a href="07-09-database-optimisation-casebook.md#case-count-for-pagination-totals">Read the case</a></p>

</details>

<details><summary>In PostgreSQL, which query forms can use an expression index on <code>((metadata-&gt;&gt;'channelOrderId'))</code>, and which can use a GIN <code>jsonb_path_ops</code> index on <code>metadata</code>?</summary>

<p>The expression index serves queries that repeat the expression, such as <code>metadata-&gt;&gt;'channelOrderId' = $1</code>, plus ranges and sorting on it. The jsonb_path_ops GIN index serves containment (<code>metadata @&gt; '{"channelOrderId": "..."}'</code>) and jsonpath <code>@?</code>/<code>@@</code> on any key, but not <code>-&gt;&gt;</code> comparisons or the <code>?</code> key-exists operator. <a href="07-09-database-optimisation-casebook.md#case-json-property-index">Read the case</a></p>

</details>

<details><summary>Why is <code>SELECT dbo.fn_CustomerTier(c.CustomerId) FROM Customers</code> slow when the UDF queries Orders, even on SQL Server 2022?</summary>

<p>Unless it is inlined, the scalar UDF runs once per row as a separate query, its reads are missing from STATISTICS IO, the optimiser can't merge it into a join, and the plan can't go parallel. Inlining needs compatibility level 150+ and a function that meets the restrictions (for example, no GETDATE or SYSUTCDATETIME). Rewrite it as an inline TVF used with CROSS APPLY, or join to a pre-aggregated set. <a href="07-09-database-optimisation-casebook.md#case-scalar-udf-per-row">Read the case</a></p>

</details>

<details><summary>Why does SELECT-then-INSERT 'get or create by email' create duplicates under concurrent requests, and what are the fixes in PostgreSQL and SQL Server?</summary>

<p>Both requests can see no row before either inserts; read committed locks nothing for a row that doesn't exist yet. PostgreSQL: <code>INSERT ... ON CONFLICT (email) DO UPDATE ... RETURNING id</code> (or DO NOTHING, then SELECT). SQL Server: in one transaction, <code>UPDATE ... WITH (UPDLOCK, SERIALIZABLE)</code>, then INSERT if <code>@@ROWCOUNT = 0</code> (or MERGE WITH (HOLDLOCK)). Both need a unique index; catch 2627/2601 or 23505 as a last resort. <a href="07-09-database-optimisation-casebook.md#case-upsert-race-conditions">Read the case</a></p>

</details>

<details><summary>A nightly report sorts 30M rows and spills to tempdb or disk. In what order should you try fixes?</summary>

<p>1) Aggregate or filter first so far fewer, narrower rows reach the sort. 2) Use an index that supplies the order, so no sort is needed. 3) For TOP N, let a Top N sort keep only N rows. 4) Only then memory: <code>work_mem</code> per session or role in PostgreSQL (never a large global value; it multiplies across nodes, workers and connections), memory grant feedback or grant limits in SQL Server. <a href="07-09-database-optimisation-casebook.md#case-sort-spill-memory-grant">Read the case</a></p>

</details>

<details><summary>Why can a query that takes 40 ms for most tenants take 45 s for one, with no code change, and what are the honest fixes?</summary>

<p>The plan was compiled for a small tenant's sniffed value (seek, key lookups, nested loops) and reused for a tenant with 2M rows. Options: OPTION (RECOMPILE) (compile CPU per call), OPTIMIZE FOR UNKNOWN (one average plan), Query Store forcing (one permanent victim), PSP at compat 160 (may or may not apply), or a separate query text for big tenants. In PostgreSQL, watch generic plans after five executions and use plan_cache_mode = force_custom_plan where needed. <a href="07-09-database-optimisation-casebook.md#case-parameter-sniffing-tenant-skew">Read the case</a></p>

</details>

<details><summary>A nightly load adds 400,000 rows for yesterday to a 90M-row table, and the morning query for yesterday estimates 1 row. Why, and what is the fix?</summary>

<p>Yesterday lies past the histogram's last step (the ascending-key problem), and the auto-update threshold was not reached, or would only fire at the report's own compile. Make the statistics update a step of the load: UPDATE STATISTICS ... WITH FULLSCAN (or a sample) in SQL Server, ANALYZE in PostgreSQL. <a href="07-09-database-optimisation-casebook.md#case-stale-statistics-after-bulk-load">Read the case</a></p>

</details>

<details><summary>Why does one DELETE of 180M rows hurt even at 01:00, and what is the safer shape?</summary>

<p>It is one transaction: the log cannot truncate until it commits, it escalates to a table lock that blocks all writers, the replicas must redo all of it, and a cancel rolls back for as long again. Delete TOP (4000) (or LIMIT 5000 in PostgreSQL) through an index on the date, commit each batch, and pause when log reuse or replica lag falls behind. <a href="07-09-database-optimisation-casebook.md#case-batched-retention-delete">Read the case</a></p>

</details>

<details><summary>Why must the monthly job split the next partition while it is still empty?</summary>

<p>Splitting or merging empty partitions only changes metadata. Splitting a partition that holds rows moves them, fully logged, under a Sch-M lock that blocks the whole table. Keep an empty partition at the end and create the next one ahead of time (in PostgreSQL, a missing partition makes inserts fail). <a href="07-09-database-optimisation-casebook.md#case-partition-retention-drop">Read the case</a></p>

</details>

<details><summary>Why does UPDATE Stock SET Available = Available - @q WHERE Sku = @s AND Available &gt;= @q prevent overselling without REPEATABLE READ?</summary>

<p>The predicate is evaluated and the row changed under one exclusive row lock. A concurrent buyer waits, then re-reads the committed value and matches 0 rows if the stock ran out. The caller checks the affected-row count, and the lock lasts milliseconds because nothing slow happens inside the transaction. <a href="07-09-database-optimisation-casebook.md#case-hot-row-inventory">Read the case</a></p>

</details>

<details><summary>What does READ_COMMITTED_SNAPSHOT ON change for a report reading rows that checkout is updating, and what does it cost?</summary>

<p>The report stops taking shared locks: for a row with an uncommitted change it reads the last committed version, so neither side waits. Costs: version store space (tempdb, or the persistent version store with ADR), 14 bytes per modified row, exclusive access to switch it on, and code that relied on reads waiting for in-flight changes. <a href="07-09-database-optimisation-casebook.md#case-readers-block-writers-rcsi">Read the case</a></p>

</details>

<details><summary>Why is Guid.CreateVersion7() a good key for PostgreSQL but not for a SQL Server clustered index?</summary>

<p>PostgreSQL compares uuid bytes from the front, where v7 keeps its timestamp, so new values append to the B-tree. SQL Server compares uniqueidentifier starting with the last 6 bytes, which are random in v7, so inserts scatter and split pages. On SQL Server leave the key as Guid.Empty for EF Core's sequential generator, or use NEWSEQUENTIALID(). <a href="07-09-database-optimisation-casebook.md#case-random-guid-clustered-key">Read the case</a></p>

</details>

<details><summary>What two conditions make a PostgreSQL update HOT, and how do you check how many updates are?</summary>

<p>No indexed column changes, and the new version fits on the same page (leave room with fillfactor). Compare n_tup_hot_upd with n_tup_upd in pg_stat_user_tables, as a delta between two samples. <a href="07-09-database-optimisation-casebook.md#case-postgres-bloat-hot-updates">Read the case</a></p>

</details>

<details><summary>Why is a zero in sys.dm_db_index_usage_stats not enough to drop an index?</summary>

<p>The counters reset at restart and failover, and reads on a readable secondary are only counted there, so a quarterly report or a secondary-only dashboard looks unused. Unique and foreign-key-supporting indexes are needed even with no seeks. Collect over a full business cycle, then disable before dropping. <a href="07-09-database-optimisation-casebook.md#case-too-many-indexes">Read the case</a></p>

</details>
