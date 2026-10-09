# The Programming Library: Quick look

Every chapter of the library in order, in a few lines each. About 50 minutes for all 108 chapters. Each heading matches a chapter in the full library, where the details, code and examples are.

## Part 0: Start here

### 0.1 Reading the code in this guide

**A phrasebook for the modern C# syntax used in every example, so the symbols never hide the idea.**

- Examples are single-file programs: save as name.cs and run dotnet run name.cs with the .NET 10 SDK.
- Code runs from the top; type declarations go at the bottom of the file.
- var lets the compiler infer a fixed type; => reads as "gives"; $"..." inserts values into text.
- ?. returns null instead of crashing on null; ?? supplies a fallback value.
- LINQ chains read left to right like a pipeline: Where, Select, Count.
- Classic slips: = versus ==, the three meanings of =>, the two meanings of using, forgetting await, integer division.

### 0.2 Backend development in one sitting

**The whole of backend work in one sitting: a few ideas, ten recurring problems, and the rest is vocabulary.**

- Almost all cost, failure and design effort is spent crossing boundaries: process, network, database.
- Shared mutable state is what makes systems hard; consistency is a set of per-operation dials, not one switch.
- A remote call has three outcomes: success, failure, or success you never heard about. Retries and idempotency keys exist for the third.
- Every framework does the same steps: listen, route, run middleware, bind input, inject dependencies, talk to the database.
- N+1 queries cause more slow endpoints than anything else; turn on ORM query logging before you need it.
- Optimistic concurrency (a version column checked on update) is the default for user-editable data.
- Default architecture: a modular monolith with boundaries enforced in the build.
- Mock at the boundary (payment provider, clock), not inside your own code; put a trace id on every log line and error response.

## Part 1: The machine

### 1.1 Bits and bytes

**Everything is stored as bits; learn how rows of 1s and 0s become numbers, flags and bytes.**

- A bit is on or off; 8 bits make a byte with 256 possible patterns.
- Hex is shorthand for binary: each hex digit is exactly four bits (#FF8800 is three bytes).
- Bitwise operators let one integer hold many yes/no flags; flag values must be powers of two.
- Byte order (endianness) matters whenever numbers become raw bytes on disk or the wire.
- KB (1,000) and KiB (1,024) differ by several percent, which explains file sizes that don't add up.
- Bitsets store set membership at one bit per value, far denser than bool arrays or HashSet.

### 1.2 Numbers inside the machine

**How numbers are stored, where each type breaks, and which type to use for money, measurements and time.**

- Integers have fixed ranges and wrap silently on overflow unless you use checked arithmetic.
- double is fast but approximate (0.1 can't be stored exactly): never compare with ==, never use it for money.
- decimal is exact for base-10 values and is the money type; store the currency with every amount.
- Rounding is a business decision: .NET defaults to banker's rounding. Round once, at the end, through one helper.
- Splitting amounts must not lose cents: give the remainder to someone explicitly so the parts still sum to the total.
- Store and send times as UTC (DateTimeOffset for instants), convert to local only for display, and inject the clock (TimeProvider).
- Use half-open intervals [start, end); store recurring events as a rule, not an expanded list.
- Treat ids as strings: JavaScript loses precision on integers past 2^53.

### 1.3 Text is numbers too

**Text is numbers in an agreed table plus an encoding, and most text bugs come from forgetting one of the two.**

- Unicode gives every character a code point; UTF-8 and UTF-16 turn code points into bytes.
- C# strings are UTF-16, so string.Length counts 16-bit units, not visible characters (emoji take two).
- Compare identifiers and keys with ordinal comparison; use culture-aware comparison only for text shown to people (the Turkish-I bug).
- Always pass a culture when parsing or formatting; Linux containers may run with invariant globalization.
- Use StringBuilder in loops, spans for allocation-free parsing, and normalize text before searching.
- Escape for the exact destination (URL, HTML, JSON) and use parameters for SQL.
- Put a timeout on any regex run on user input to avoid ReDoS; never build a regex from user input.

### 1.4 The processor

**What the CPU actually does with your code, and why memory access usually decides speed.**

- The processor fetches, decodes and executes simple instructions on values in registers.
- Loops and branches are jumps; small methods get inlined by the JIT.
- Machine code is specific to an instruction set (x64, ARM64): images and native dependencies must match.
- Modern chips overlap, predict and vectorize (SIMD) work, and have many cores.
- Waiting on memory costs far more than arithmetic; sequential access beats random access by about two orders of magnitude.
- Moving to ARM can expose race conditions that x64 hid.

### 1.5 Memory

**How memory is organized into stack and heap, and why layout and access order shape performance.**

- Memory is a row of numbered bytes; each method call gets a stack frame, freed on return.
- Objects that outlive a call live on the heap, managed by the garbage collector.
- Structs are copied on assignment, classes are shared by reference. A struct lives inline wherever its container lives, not always on the stack.
- Caches load memory in 64-byte lines, so walking data in order is much faster than jumping around.
- Every .NET object carries header overhead; boxing a value type allocates a hidden heap object.
- Stack overflow kills the process and can't be caught; out-of-memory means something is still holding references.

### 1.6 Storage and files

**How files and storage really work, and why "saved" doesn't always mean safe.**

- Writes pass through caches; data survives a power cut only after it is flushed to the device.
- Replace a file safely by writing a temporary copy and renaming it over the original.
- Stream large files instead of loading them into memory; build paths with Path.Combine.
- Linux file names are case-sensitive; handles are a limited resource ("too many open files").
- Block, file and object storage are different; never run a database on network file storage.
- Replication copies mistakes instantly; only tested backups let you go back.

### 1.7 The operating system

**How the operating system shares the machine, and the everyday behaviour that follows from it.**

- Programs ask the kernel for everything through system calls, which cross a costly boundary.
- A process has its own private memory; threads inside it share that memory.
- Programs communicate through arguments, environment variables, standard streams and exit codes.
- Services must handle SIGTERM to shut down cleanly; exit code 137 means the process was killed (often for memory).
- A container is an ordinary process with isolation and limits; a VM runs a whole separate OS.
- Container memory limits kill the process rather than slowing it down.

## Part 2: From source code to running program

### 2.1 What a programming language is

**What a programming language is, and how a tool turns text into meaning.**

- A language has syntax (rules for valid code) and semantics (what valid code means).
- Tools work in stages: split into tokens, parse into a syntax tree, check names and types, produce output.
- Operator precedence is built into the grammar, which is why it surprises people.
- Languages mix styles: imperative, object-oriented, functional, declarative.
- The language, its runtime and its standard library are separate; most learning time goes on the library.
- Don't hand-roll parsers for real formats, and don't build code by gluing strings together.

### 2.2 Compilers, interpreters and JIT

**How C# becomes machine code: compiled to IL at build time, then JIT-compiled and improved while it runs.**

- The C# compiler emits IL plus metadata in assemblies; the JIT compiles each method on its first call.
- That first call is why the first request after a deploy is slow (cold start).
- Tiered compilation compiles fast first, then recompiles hot methods with full optimization using real profile data (dynamic PGO).
- ReadyToRun and Native AOT move work to build time for faster start-up, but trimming can't see reflection and breaks it.
- Source generators replace runtime reflection with compile-time code: faster and AOT-friendly.
- Only measure speed in Release builds, after warm-up; treat trim and AOT warnings as errors.
- Native interop (P/Invoke) is where the runtime's safety guarantees stop; wrap handles in SafeHandle.

### 2.3 Types

**Types tell the program what bits mean and let the compiler catch mistakes before the code runs.**

- Static typing checks at compile time; var still gives a fixed type.
- Nullable reference types are compile-time warnings only: data from JSON, EF or reflection can still be null.
- Generics in .NET are reified: one type-safe implementation with no boxing for value types.
- Use small wrapper types for ids and units so mix-ups become compile errors (avoid primitive obsession).
- Structs only for small, immutable values; mutable structs are a documented trap.
- If you override Equals you must override GetHashCode, and dictionary keys must never change.
- record class still allocates on the heap; use readonly record struct for small values.
- Random v4 Guids make poor clustered database keys; prefer sequential ids.

### 2.4 Values, variables and references

**What exactly gets copied or compared when you assign, pass or compare a value.**

- Assignment and argument passing copy what is in the variable; for a class that is a reference, so both names share one object.
- ref, out and in pass the variable itself instead of a copy.
- Classes compare by identity unless they override equality; records and strings compare by value.
- Equal objects must have equal hash codes, and keys must not change while stored.
- Lambdas capture variables, not values, which bites in loops.
- Boxing copies a value type into a heap object: an allocation plus future GC work.

### 2.5 Control flow and functions

**Choices, loops and functions in modern C#, and the traps in each.**

- && and || stop evaluating as soon as the answer is known.
- Switch expressions and patterns test types, values, properties and list shapes in one step.
- Changing a collection inside foreach throws.
- LINQ queries and iterators run only when enumerated, and again every time you enumerate.
- Lambdas and delegates pass behaviour as a value; a lambda that captures variables allocates each time.
- Events are the most common managed memory leak: always unsubscribe.

### 2.6 Scope and lifetime

**Scope (where a name is visible) versus lifetime (how long things exist), plus resources the GC doesn't manage.**

- An object lives as long as something can reach it.
- Static fields are shared by the whole process; AsyncLocal gives each operation its own value.
- Files, connections and locks need deterministic cleanup with IDisposable and using.
- Each resource needs one clear owner; don't dispose what you don't own.
- Finalizers are only a safety net, and they make objects more expensive.
- In dependency injection, a service must never depend on something shorter-lived than itself (captive dependency).

### 2.7 Memory management and garbage collection

**How the garbage collector decides what to free, when it pauses you, and how you still leak memory.**

- The GC frees everything it can't reach from roots (locals, statics, handles).
- Generations make most collections cheap because most objects die young; gen 2 collections cause latency spikes.
- Cost scales with objects that survive, not with garbage, so avoid mid-life objects.
- Objects of 85,000 bytes or more go to the Large Object Heap, which fragments.
- Server GC suits web apps and respects container memory limits.
- Leaks come from references you forgot: event handlers, unbounded caches, statics.
- Span, ArrayPool and pooling cut allocations in hot paths; never call GC.Collect() in production code.

### 2.8 Errors and exceptions

**How to report and handle failure so problems become clear log lines rather than corrupted data.**

- Bugs fail loudly, expected failures are normal outcomes, temporary failures can be retried.
- An exception unwinds the stack running finally blocks until a matching catch.
- Catch specific exceptions where you can act; catch everything only at boundaries; rethrow with throw;, never throw ex;.
- Throwing costs microseconds: fine for the unexpected, terrible as control flow. Model expected failures with result types.
- Async failures surface when the task is awaited; async void crashes the process.
- Retry only operations safe to repeat, with growing randomized delays; never leak internals to API clients.

### 2.9 The .NET platform: versions, legacy and direction

**Which .NET to target, how legacy .NET Framework differs, and where the platform is heading.**

- Even-numbered releases are LTS (three years); upgrade one version at a time and run the full integration suite.
- .NET Framework 4.8.1 is final: Windows-only, machine-wide, security fixes only.
- Legacy apps bring System.Web, AppDomains, the GAC, binding redirects, web.config and IIS app-pool recycling.
- debug="true" in a production web.config is one of the highest-impact single-line fixes.
- Migrate incrementally: inventory with the Upgrade Assistant, convert project files, then move piece by piece.
- The direction is "do less at runtime": source generators, AOT, vectorized libraries.
- Several popular libraries went commercial in 2024–2025; know the alternatives.

## Part 3: Data structures and algorithms

### 3.1 Measuring cost with Big-O

**Big-O describes how cost grows with data size, so you can predict slow code before the data grows.**

- Keep only the fastest-growing term: constant, log n, n, n log n, n², exponential.
- At scale the gap between growth classes dwarfs everything else.
- Nested loops multiply; a search call inside a loop (List.Contains, a query) hides a quadratic cost.
- Amortized O(1) is precise: List doubling makes occasional expensive steps average out.
- Big-O ignores caches: a contiguous array often beats a "faster" structure for small sizes. Choose the shape with Big-O, then measure.

### 3.2 Arrays and lists

**How arrays and lists sit in memory, and which collection to choose.**

- Arrays store items side by side: indexing is instant and scanning is cache-friendly.
- List<T> is an array plus a count that doubles when full; adding at the end is cheap, inserting at the front is not.
- LinkedList only wins when you already hold node references; otherwise it is slower than List.
- Stacks give the newest item, queues the oldest (implemented as a ring buffer).
- Frozen collections are fastest for data built once at start-up; immutable collections trade speed for safe sharing.
- List and Dictionary cover about 90% of real code; pre-size when you know the count.

### 3.3 Hash tables

**Hash tables find items by computing where they should be, which is why Dictionary and HashSet are so fast.**

- A key's hash picks a bucket and only that bucket is searched: near O(1) on average.
- Equal keys must have equal hash codes, and keys must never change while stored.
- String hash codes are randomized per process: never persist GetHashCode results; use a stable hash instead.
- Pass an explicit comparer for string keys; use TryGetValue and TryAdd to avoid double lookups.
- hash(key) % N remaps most keys when N changes; consistent hashing avoids that.
- Turning a List.Contains inside a loop into a HashSet lookup is the single highest-value refactor in many codebases.

### 3.4 Trees

**Trees keep data in order, from SortedSet to the B-trees inside every database index.**

- A binary search tree discards half the data per step, but only while balanced; sorted input turns it into a chain.
- SortedSet and SortedDictionary are self-balancing red-black trees.
- SortedList is better when you build once and read often; SortedDictionary when inserts and reads interleave.
- Trees can be walked depth-first (pre, in, post order) or level by level.
- Databases use wide, shallow B-trees so a lookup takes a handful of disk reads.
- Tries answer prefix queries (autocomplete) in time proportional to the key length.

### 3.5 Heaps and priority queues

**Priority queues hand back the most urgent item next, using a heap stored in a plain array.**

- A binary heap gives O(log n) add and remove and O(1) peek, without keeping everything sorted.
- .NET's PriorityQueue returns the smallest priority first and doesn't keep ties in arrival order.
- You can't change a priority in place; re-add the item instead.
- Top-k over huge data: keep a min-heap of size k, O(n log k) time and O(k) memory.
- Heaps power scheduling, timers and merging sorted streams.

### 3.6 Graphs

**Graphs model connections, and a few classic algorithms answer most questions about them.**

- A graph is vertices joined by edges, which may be directed and weighted; store most as adjacency lists.
- Breadth-first search finds paths with the fewest edges; depth-first search answers reachability and cycle questions.
- Dijkstra finds shortest weighted paths when no weight is negative; A* speeds it up with a good estimate.
- Topological sort orders dependencies (builds, migrations) and detects cycles.
- Always keep a visited set; union-find groups items transitively (customer de-duplication).

### 3.7 Searching and sorting

**Searching and sorting: what .NET does for you and the details that still matter.**

- Binary search finds items in sorted data in O(log n) and also finds range boundaries.
- Comparison sorts can't beat O(n log n); counting and radix sorts can for small integer keys.
- Array.Sort (introsort) is not stable; LINQ OrderBy is stable.
- Comparers must be consistent or sorting throws or returns garbage; never compare by subtraction.
- Don't sort to find one item; sort data bigger than memory with external merge sort.
- Search engines avoid scanning by using inverted indexes.

### 3.8 Recursion and dynamic programming

**Recursion for self-similar problems, and dynamic programming to stop repeating work.**

- A recursive function needs a base case and must shrink the problem every call.
- C# doesn't guarantee tail calls; deep recursion on real data needs a loop with an explicit stack.
- Memoization stores results of pure functions so repeated work disappears.
- Dynamic programming builds answers from smaller stored answers (edit distance, knapsack).
- Greedy choices are fast but sometimes wrong; NP-hard problems need solvers and heuristics.

### 3.9 Specialised structures and data that does not fit

**Specialised structures, and techniques for data that is bigger than memory or arrives as a stream.**

- LRU cache = Dictionary for lookup plus LinkedList for ordering; ring buffers give bounded, allocation-free windows.
- Bloom filters say "definitely not present" exactly and "probably present" with a tunable error rate.
- For streams: top-k with a heap, external merge sort, Welford's algorithm for accurate variance.
- HyperLogLog counts distinct values in about 12–16 KB within 1–2%; count-min sketch estimates frequencies.
- Reservoir sampling picks a uniform random sample in one pass; percentiles need histograms, and percentiles don't average.
- Random is predictable: use a cryptographic generator for anything security-related; never expose sequential ids publicly.

### 3.10 Collections and LINQ in .NET

**How .NET collections and LINQ really behave, and where they cost time or memory.**

- Most LINQ operators are lazy: nothing runs until you enumerate, and it runs again each time.
- IEnumerable runs in memory; IQueryable is translated to SQL. Mixing them up pulls whole tables into memory.
- Streaming operators (Where, Select) hold nothing; buffering ones (OrderBy, GroupBy) consume the whole source.
- Prefer Sum, Max, Count and CountBy over Aggregate; use Single only when duplicates mean corruption.
- Each operator allocates enumerators and delegates; in hot paths, measure with MemoryDiagnoser.
- Fix accidental O(n²) first, client-side evaluation second, redundant passes third, allocations last.

## Part 4: Designing programs

### 4.1 Procedural, object-oriented and functional styles

**Procedural, object-oriented and functional styles, and how good C# mixes them.**

- Procedural separates data and procedures; OO bundles them; functional transforms unchanging values.
- Virtual methods make new types easy to add; records with pattern matching make new operations easy to add.
- Keep business rules with the data they protect (avoid anemic models with scattered rules).
- Records for data, classes for things with identity and rules, pure static functions for calculations.
- Avoid deep inheritance hierarchies and switch statements on type codes.

### 4.2 Abstraction and interfaces

**Abstraction: hiding detail behind contracts that callers can rely on.**

- Hide each design decision likely to change inside one module.
- A contract covers preconditions, results, invariants, errors and cost.
- Interfaces state contracts; abstract classes share real code through inheritance.
- Interfaces are most useful as seams at the edges of a system, not around everything.
- Every abstraction leaks a little; never hide expensive or remote work behind a cheap-looking call.
- The wrong abstraction costs more than duplication; don't abstract too early.

### 4.3 Composition and dependency injection

**Build behaviour by composing objects, and wire them with dependency injection.**

- Favour composition over inheritance; inheritance is for substitutability, not reuse.
- Decorators add behaviour (caching, retry, logging), adapters make classes fit, strategies pass in what varies.
- Take dependencies through constructors and wire everything in one place; avoid the service locator.
- Lifetimes: singleton, scoped (per request), transient. Never inject a scoped service into a singleton (captive dependency).
- BackgroundService is a singleton: create a scope with IServiceScopeFactory for each unit of work.
- Factories (IHttpClientFactory, IDbContextFactory, keyed services) cover the cases constructor injection can't.
- Validate registrations at start-up; a huge constructor means the class does too much.

### 4.4 Immutability and pure functions

**Keep change under control with pure functions and immutable data.**

- A pure function's result depends only on its inputs and it has no side effects.
- Pass time and other hidden inputs in, and logic becomes pure and testable.
- Immutable data is safe to share; changes create new versions cheaply through structural sharing.
- Records give shallow value equality: a record holding a List compares that list by reference.
- Value objects validate themselves on creation; publish read-mostly shared data as immutable snapshots.
- Keep decisions in a pure core and input/output in a thin outer shell.

### 4.5 Design principles that hold up

**Rules of thumb that keep code cheap to change, and the signs it is getting expensive.**

- Low coupling between modules, high cohesion within them.
- SOLID: one reason to change; extend without editing; subtypes keep their base's promises; small interfaces; depend on abstractions owned by the higher-level code.
- DRY is about repeated knowledge, not repeated-looking text; YAGNI: don't build for needs you don't have.
- Rule of three: abstract only after the third occurrence shows what actually varies.
- Refactor in small tested steps, never mixed with behaviour changes; pay down tech debt steadily.
- Warning signs: god classes, boolean flags that switch behaviour, train-wreck call chains.

### 4.6 Testing

**Tests make change safe; this is what to test, how, and how to keep the suite trustworthy.**

- Arrange, act, assert, one behaviour per test, named so a failure explains itself.
- Many fast unit tests, fewer integration tests against real infrastructure, a few end-to-end tests.
- Use stand-ins only at the edges; test through public behaviour, not internal calls.
- Don't use EF's InMemory provider: test against a real database with Testcontainers.
- Flaky tests come from hidden inputs (time, randomness, shared state); quarantine or delete them immediately. Inject TimeProvider.
- Coverage shows what ran, not whether bugs would be caught; mutation testing and query-count assertions catch more.
- Contract tests (Pact) prove services still agree; BenchmarkDotNet's Allocated column matters as much as Mean.

### 4.7 Design patterns

**Design patterns as a shared vocabulary of trade-offs, and when not to use them.**

- A pattern is a named solution plus its consequences; read the costs before using one.
- Creational: Builder and Object Pool still earn their place; DI absorbed most of the rest; Singleton is mostly a DI lifetime now.
- Structural: Adapter changes shape, Decorator adds behaviour, Proxy controls access, Facade simplifies a subsystem, Composite treats one and many alike.
- Behavioural: Strategy (almost free with DI), State, Command, Chain of Responsibility, Observer vs Mediator.
- Repository and Unit of Work: DbContext already is both; wrap it only if you need the boundary.
- Work backwards from the symptom to the pattern, and reach patterns by refactoring, not up-front design (avoid pattern fever).

### 4.8 Anti-patterns

**Recurring mistakes, used as a review checklist.**

- Structural: god classes, feature envy, classes that should be split by field usage.
- Behavioural: boolean parameters (use named arguments or enums), hidden temporal coupling.
- Abstraction: layers and interfaces added for their own sake, often by experienced teams.
- Data and distributed: the shared database between services, chatty calls, distributed monoliths.
- The .NET catalogue: sync-over-async, N+1, captive dependencies, new HttpClient per call, unbounded caches.

### 4.9 Design patterns casebook

**Eighteen design patterns applied to real production problems, each with before and after code, what it cost and when not to use it.**

- Factory over keyed DI services picks a notification channel per tenant without if/else at every call site.
- Builders with sensible defaults stop test arrange blocks breaking on every new constructor parameter.
- Adapter hides two payment gateways behind one port; the money and error mapping is the part to test.
- Decorators add audit and timing to every command handler without editing one; Proxy makes an expensive renderer lazy and reusable.
- Facade gives one checkout entry point so the API, admin tool and import cannot drift apart.
- Strategy, Template Method and State replace growing switches and contradictory status flags.
- Chain of Responsibility (DelegatingHandler), Command, domain events and a small mediator pipeline remove copy-pasted cross-cutting code.
- Every case ends with the cost: several say a plain switch, a method or a nullable value would have been the better choice.

## Part 5: Concurrency

### 5.1 Why concurrency is hard

**Why sharing data between threads creates timing bugs that may appear once in a million runs.**

- Concurrency means many tasks in progress; parallelism means running at the same instant; asynchrony means not holding a thread while waiting.
- Threads interleave unpredictably, so shared data needs protection.
- count++ and check-then-act sequences race, losing updates or acting on stale checks.
- Without synchronization a write may never become visible to another thread; large values can be read half-written.
- Blocking a thread to wait wastes it; a thread pool of 32 threads can't wait on 1,000 requests.
- The safest design shares no mutable data at all; passing tests don't prove thread safety.

### 5.2 Threads and locks

**Locks, atomics and thread-safe collections, and how to avoid deadlocks.**

- Lock on a private object, keep locked sections short, protect each piece of shared data with exactly one lock.
- Deadlock needs a circular wait; taking locks in a fixed order prevents it.
- lock can't be held across await; use SemaphoreSlim for async code.
- Interlocked and compare-and-swap update single values roughly 10–20× faster than a lock.
- ConcurrentDictionary makes single operations safe; combine steps with GetOrAdd (its factory can run more than once) and never use it as an unbounded cache.
- volatile prevents caching but gives no atomicity; x64 hides reordering bugs that appear on ARM.
- Thread pool starvation looks like low CPU with rising latency and queue length; the cause is almost always blocking on async code.

### 5.3 Async and await

**How async and await let a few threads keep thousands of operations in progress, and the mistakes that stall servers.**

- await pauses a method without blocking its thread; the compiler rewrites it into a state machine.
- Async doesn't make one call faster; it lets the process have more calls in flight.
- Never block with .Result or .Wait(): it deadlocks UI and classic ASP.NET apps and starves servers. Go async all the way.
- Start independent operations together and await Task.WhenAll; throttle large fan-outs.
- Pass CancellationToken everywhere; cancellation is cooperative and is not a failure.
- Avoid async void and fire-and-forget; use a bounded Channel and a background service instead.
- ValueTask only for hot paths that usually complete synchronously, with strict rules.
- DbContext is not thread-safe: one operation at a time per context.

### 5.4 The event loop

**How a single thread can serve thousands of connections with an event loop, as in Node.js and every UI.**

- An event loop runs one callback at a time to completion, then waits for the OS to report more ready I/O.
- No data races inside the loop, but any slow callback freezes everything.
- JavaScript runs all microtasks (promise callbacks) before the next timer or I/O task.
- Check-then-act races still happen across awaits.
- Languages solve waiting differently: C# and JS transform functions into state machines, Go and Java 21 use cheap stacks, older code used callbacks.
- Use bounded buffers for backpressure.

### 5.5 Parallelism

**Splitting CPU work across cores, and when that doesn't help.**

- Data parallelism splits the same work; task parallelism runs different work at once.
- Parallel.For and PLINQ split work; give each worker its own subtotal and combine at the end.
- Use Parallel.ForEachAsync or throttled tasks for I/O, not CPU parallelism.
- Amdahl's law: the part that can't be split caps the speedup.
- Don't parallelize inside a busy web server; parallel floating-point sums won't match to the last digit.
- Bounded Channel<T> gives producer/consumer pipelines with backpressure; actors avoid locks by owning their state.

## Part 6: Networks

### 6.1 Layers and packets

**How data travels between machines as packets in layers of envelopes.**

- Data moves as packets, each with a header and a payload.
- Four layers: link (local network), internet (routing), transport (program to program), application (HTTP).
- Each layer wraps the data from the layer above in its own header.
- Latency and bandwidth are separate limits; round trips usually matter more than bandwidth.
- TCP turns packet loss into latency rather than errors; connection reuse is the biggest client-side lever.

### 6.2 IP addresses and routing

**IP addresses, subnets and how packets are routed hop by hop.**

- IPv4 addresses are 32 bits (and ran out); IPv6 are 128 bits.
- A prefix like /24 says how many leading bits name the network.
- Loopback is this machine; private ranges are internal; listening on 0.0.0.0 means every interface (needed in containers).
- Routers pick the most specific matching route; TTL stops loops.
- NAT lets private machines share one public address; idle connections can be dropped by NAT.
- Put only the load balancer in a public subnet; plan CIDR ranges so networks never overlap.

### 6.3 TCP and UDP

**TCP gives a reliable ordered byte stream; UDP sends fast datagrams with no guarantees.**

- Ports identify programs; a connection is identified by both ends' addresses and ports.
- TCP carries bytes, not messages, so every protocol on top needs framing; one read is not one message.
- Each connection costs a handshake round trip and starts slowly, which is why connection pools exist.
- "Connection refused" means nothing is listening; a timeout means something swallowed the packets.
- UDP fits when late data is worthless (video, metrics, game state); QUIC builds HTTP/3 on it.
- Round-trip time sets a floor: under 1 ms in a datacentre, 150–250 ms between continents.

### 6.4 DNS

**DNS turns names into addresses through a distributed, heavily cached directory.**

- Recursive resolvers walk a tree of zones from the root and cache every answer.
- Record types: A/AAAA for addresses, CNAME for aliases, MX for mail, TXT for text.
- Every record has a TTL; lower it before planned changes, because cached copies outlive edits.
- Pooled HTTP connections never re-resolve: set PooledConnectionLifetime or use IHttpClientFactory.
- DNS failures often look like something else, and DNS is not instant failover.

### 6.5 TLS

**TLS gives connections confidentiality, integrity and proof of the server's identity.**

- A short asymmetric handshake agrees a key and proves identity; fast symmetric encryption carries the data.
- A certificate binds a public key to a domain name, signed up a chain to a trusted root.
- TLS 1.3 needs one round trip and always gives forward secrecy.
- It hides content, not the fact that you connected; SNI reveals the host name.
- Automate renewal and monitor expiry from outside: expired certificates cause many avoidable outages.
- Never disable certificate validation, even in staging; mTLS adds client identity.

### 6.6 HTTP

**HTTP, the request/response protocol behind the web and almost every API.**

- A request has a method, path, headers and optional body; a response has a status code, headers and body.
- GET and HEAD are safe; PUT and DELETE are idempotent; POST is neither, so it can't be retried blindly.
- 4xx means the client is at fault, 5xx the server.
- Cookies add state; Cache-Control and ETags avoid repeated transfers.
- HTTP/1.1 keeps connections open, HTTP/2 multiplexes many requests on one connection, HTTP/3 runs over QUIC.
- A network call costs roughly a million times a method call, which drives most API design.

### 6.7 Servers, proxies, load balancers and diagnosing the network

**What sits between clients and your code: web servers, reverse proxies, load balancers, and how to debug the network.**

- Kestrel usually sits behind nginx, IIS, Envoy, an ingress or a cloud load balancer that terminates TLS.
- Trust X-Forwarded-For only from known proxies, or clients can forge their IP.
- L4 balancers route connections, L7 route requests; HTTP/2 or gRPC needs L7 or one pod gets all traffic.
- Avoid sticky sessions; keep session state in Redis.
- Every outbound call needs a timeout shorter than its caller's; rate-limit to protect the origin.
- Diagnose layer by layer: DNS, TCP connect, TLS, then HTTP.
- Deny egress by default and block the cloud metadata address (169.254.169.254) from apps first.

### 6.8 Real-time: WebSockets, SSE and SignalR

**Pushing data to clients with WebSockets, Server-Sent Events and SignalR.**

- Three options in rising capability and cost: polling, SSE, WebSockets (SignalR picks for you).
- A WebSocket is a long-lived connection pinned to one server, which breaks the stateless model.
- With more than one server you need a backplane (such as Redis), or messages reach only some clients.
- Connections are a capacity resource; reconnection is normal, so resynchronize state after reconnect.
- Realtime is an optimisation, never the source of truth: clients must be able to recover by querying.

## Part 7: Storing data

### 7.1 Why databases exist

**Why databases exist: storing data safely while many users read and change it at once.**

- A database groups changes into all-or-nothing transactions, survives crashes and enforces rules on every change.
- It answers queries without scanning everything, using indexes.
- Underneath: fixed-size pages, a buffer pool in memory, and a write-ahead log written before any change.
- Relational is the default; SQLite is embedded in your process, SQL Server and Postgres run as services.
- Don't read, change and write back in the application; let the database do it in one statement.
- Keep heavy reports off the live operational database.

### 7.2 The relational model and SQL

**The relational model and the parts of SQL that trip up experienced developers.**

- Tables of typed columns, a primary key per row and foreign keys between tables, so each fact lives in one place.
- Logical order is FROM, WHERE, GROUP BY, HAVING, SELECT, ORDER BY, which explains where aliases work.
- Inner joins keep matches, left joins keep every left row; one-to-many joins multiply rows.
- A condition on the right table in WHERE silently turns a left join into an inner join.
- NULL means unknown: use IS NULL, COALESCE and NOT EXISTS.
- Window functions handle rankings, running totals and top-N per group.
- Think in sets, not loops; do upserts in one statement; always pass values as parameters.

### 7.3 Indexes and query plans

**Indexes and query plans: the single most effective performance fix in most applications.**

- An index is a sorted B-tree copy of some columns with pointers to rows, so the database seeks instead of scanning.
- Composite indexes follow the leftmost-prefix rule: put equality columns before range columns.
- Covering indexes (INCLUDE columns) avoid key lookups.
- Functions on a column or type mismatches stop an index being used.
- Read actual plans: look for scans, key lookups, and estimated rows far from actual rows.
- SQL Server caches a plan from the first parameter values (parameter sniffing), which can make one plan bad for others.
- Databases read 8 KB pages; every index costs writes and memory, so add only what real queries need.
- Index foreign keys; update statistics after big loads; avoid deep OFFSET paging.

### 7.4 Transactions and isolation

**Transactions make changes all-or-nothing; isolation decides what concurrent work can see.**

- ACID: atomicity, consistency (declared constraints hold), isolation, durability.
- Read committed, the usual default, still allows non-repeatable reads, phantoms and lost updates.
- Snapshot and serializable prevent more anomalies at a higher cost; write skew is the subtle one.
- Enable READ_COMMITTED_SNAPSHOT instead of sprinkling NOLOCK.
- Check-then-act is a race: let a constraint or a single statement enforce the rule.
- Default to optimistic concurrency with a version column; pessimistic locks only for hot rows, kept short.
- Keep transactions short, retry deadlocks, and avoid distributed transactions; use an outbox to pair a change with a message.

### 7.5 Schema design and migrations

**Designing a schema that stays correct, and changing it while the system keeps running.**

- A schema outlives the code: make invalid data impossible with keys and constraints.
- Normalize so each fact lives once; denormalize deliberately when measurement shows reads need it.
- Prefer surrogate keys, keep natural identifiers unique, avoid random GUIDs as clustered keys, and don't expose sequential ids.
- Exact types for money, UTC for instants; constraints enforce rules for every writer.
- Change live systems with expand and contract so old and new code both work; batch backfills.
- Don't run Database.Migrate() at start-up with several instances; run reviewed migrations from the pipeline.
- Soft delete, history (temporal tables) and multi-tenancy each have costs; a global query filter is not a security boundary.

### 7.6 Non-relational stores

**Non-relational stores: what each kind is good at, and what it gives up.**

- Key-value for fast lookups, documents for nested data shaped by access patterns, wide-column for huge write loads.
- Graph databases follow relationships, time-series stores handle measurements, search engines do forgiving full-text queries.
- Design documents around aggregates; unbounded arrays inside documents are a classic failure.
- Redis is a data-structure server on a single thread: avoid big keys and hot keys; separate caches from sessions.
- DynamoDB and Cosmos DB need access patterns designed first; hot partition keys throttle you.
- Replicas lag and shards need an even key. Start relational: most "we need NoSQL" cases are really "we need an index".
- Every extra store adds backups, restores, runbooks and consistency boundaries.

### 7.7 ORMs and EF Core

**ORMs and EF Core: huge time savers and the most common source of .NET database slowness.**

- DbContext is a short-lived unit of work with a change tracker; use one per request, never share it across threads.
- LINQ on a DbSet becomes SQL when enumerated; untranslatable expressions throw.
- Project to DTOs with Select, beware N+1 loops and cartesian explosions from multiple Includes (use split queries).
- AsNoTracking for reads; ExecuteUpdate and ExecuteDelete for bulk changes, but they bypass interceptors.
- Interceptors stamp audit columns; global query filters implement soft delete (not applied to raw SQL).
- FromSql with interpolation is safe; FromSqlRaw with interpolation is SQL injection.
- Configure decimal precision and string lengths: the defaults truncate money and create nvarchar(max).
- Use Dapper alongside EF for hot reads when you want to write the SQL yourself.

### 7.8 Running databases: operations, scaling and the modern landscape

**Running databases in production: backups, replication, scaling and today's options.**

- Replicas are not backups: a replica applies a bad DELETE in milliseconds. Test point-in-time restores.
- Scale in order: query and index fixes, connection pooling, caching, read replicas, partitioning, sharding (most expensive).
- Replica lag breaks read-your-writes: read from the primary briefly after a write.
- Connection count, not query cost, is what usually brings a database down under load.
- Postgres needs vacuum; transaction ID wraparound causes emergency outages.
- Separate analytics from OLTP; partition tables that grow forever by time.
- Default to PostgreSQL unless there is a specific reason; consider distributed SQL before hand-rolled sharding.

### 7.9 Database optimisation casebook

**Eighteen slow or broken database workloads on SQL Server and PostgreSQL: the evidence, the fix, the numbers after, and the cost.**

- Find the query with the highest total cost, read the actual plan, and compare estimated with actual rows before adding anything.
- Composite indexes: equality columns first, the range or sort column last.
- Filtered and partial indexes only match a literal predicate; a parameter hides them on SQL Server.
- Type mismatches (nvarchar parameter, varchar column), OR across columns, scalar UDFs and JSON lookups each defeat an index in their own way.
- Exact counts for pagination are expensive; fetch one extra row, cap the count, or use an estimate.
- Parameter sniffing and stale statistics after a bulk load cause most "same query, suddenly slow" incidents.
- Delete in batches or drop whole partitions; update hot counters with one conditional statement; use RCSI, not NOLOCK.
- Random GUID keys, bloat from non-HOT updates and unused indexes all slow writes; measure before you drop anything.

## Part 8: Building a backend

### 8.1 What a backend does

**What belongs on the server, and how one request travels through a backend.**

- The backend holds shared data, secrets and rules, because clients can't be trusted.
- A request passes routing, authentication, validation, business logic, data access and response writing; each step can fail or add time.
- Organize code in layers with dependencies pointing inward, in vertical slices by feature, or a mix.
- Keep instances stateless, configured from the environment, with liveness and readiness checks.
- Middleware order is the contract; graceful shutdown needs grace period > host timeout > longest request.
- Give each request a time budget, a timeout on every outgoing call, and watch percentiles, not averages.

### 8.2 API design

**Designing web APIs that are pleasant to use and safe to evolve, because an API is a promise.**

- Organize around resources with plural URLs; let methods and status codes keep their standard meanings.
- 4xx is the client's fault and shouldn't page anyone; 5xx is yours and should.
- JSON conventions: UTC ISO times, exact money with a currency, large ids as strings.
- Page every collection (cursors preferred), cap page size server-side, and make total counts opt-in.
- Idempotency keys make creation safe to retry; ETags with If-Match stop silent overwrites.
- Additive changes need no new version; version (in the URL path) only for removals, renames and type changes, and deprecate with a date.
- Return Problem Details (RFC 9457) for errors; long jobs return 202 with a status URL.
- Choose gRPC for chatty internal calls, GraphQL when clients need flexible shapes, webhooks for events across organizations.

### 8.3 A modern .NET backend

**How a modern ASP.NET Core backend is put together, from host to tests.**

- Cross-platform, runs Kestrel on the generic host, with DI, configuration, logging and health checks built in.
- Middleware runs in registration order: exception handling first, authentication before authorization.
- Minimal APIs and controllers compile to the same routing; the choice is organizational.
- Configuration layers override each other; bind options with validation and ValidateOnStart. Know IOptions vs IOptionsSnapshot vs IOptionsMonitor.
- Use IExceptionHandler and return Problem Details with a trace id, never stack traces.
- Never expose entities over HTTP; map to DTOs.
- Log with message templates, not interpolation; test the whole app in memory with WebApplicationFactory.
- .NET Aspire wires local services, discovery, telemetry and a dashboard for multi-service apps.

### 8.4 A Node and TypeScript backend

**The same kind of backend built with Node.js, Express and TypeScript, for comparison.**

- Node runs JavaScript on V8 with an event loop: great for I/O-bound backends, bad for CPU-heavy work, which blocks everyone.
- Commit the lockfile, install with npm ci, and keep dependencies few: the supply chain is an attack target.
- Prefer ES modules.
- TypeScript types vanish at run time: validate every input with a schema and derive types from it.
- Express, Fastify and NestJS provide middleware, routing and structure.
- Handle unhandled promise rejections; keep state out of process memory when running several instances.

### 8.5 Validation and errors at the boundary

**Check everything that crosses a trust boundary once, on arrival, and fail with one clear error contract.**

- Requests, messages, files and partner responses are all untrusted input.
- Check shape at the edge, business rules in the domain against current data, invariants inside the type.
- Parse input into types that can only hold valid values (value objects): allowlists, canonicalize first, then check, with size limits.
- Report every field error at once.
- Bind requests to dedicated DTOs, never to stored entities, to prevent over-posting.
- One machine-readable error format with stable codes, no internals, and a correlation id that also appears in the logs.

### 8.6 Caching

**Caching: reuse recent answers to save work, without serving wrong data.**

- Cache data that is read often, expensive to produce and fine slightly stale; never for decisions that must be exact.
- In-process caches are fastest, Redis is shared, HybridCache (.NET 9) combines both with stampede protection and tags.
- Cache-aside is the default; every entry needs an expiry and every cache a size limit.
- Prevent stampedes with single-flight loading and jittered TTLs; cache "not found" to stop penetration.
- Put everything that changes the answer in the key (tenant, user, locale, version); versioned keys avoid invalidation.
- HTTP: no-cache means revalidate, no-store means don't store; private vs public is a security boundary; set Vary correctly.
- Cache DTOs, never entities; never cache per-user data under a shared key.
- The system must survive the cache failing; don't cache to hide a slow query you haven't indexed.

### 8.7 Background work and queues

**Moving work the user doesn't need to wait for out of the request, without losing it.**

- Hosted services and channels suit in-process work that can be lost on restart.
- Work that must happen needs a durable queue, fed through an outbox in the same transaction as the data.
- Brokers deliver at least once: make consumers idempotent by recording message ids with the work.
- Retry transient failures with backoff; move poison messages to a dead-letter queue and alert on it.
- BackgroundService is a singleton: create a scope per iteration and catch exceptions, or the worker dies silently.
- Scheduled jobs must run once across instances (lock or leader), in UTC, and be safe to repeat; DST breaks local-time schedules.

### 8.8 Serialization and data formats

**Turning objects into bytes and back: JSON, Protocol Buffers, CSV and documents.**

- System.Text.Json is strict where Newtonsoft was lenient; most migration pain comes from that.
- Reuse JsonSerializerOptions; a new instance per call is a common performance bug.
- Source-generated serialization is required for AOT and faster everywhere.
- Serializing EF entities with cycles breaks: return DTOs.
- Protobuf compatibility depends on never reusing field numbers; schema registries enforce event contracts.
- CSV is harder than Split(','): use a real parser and guard against formula injection.
- Stream large exports and documents instead of building them in memory.

### 8.9 Integrations: HTTP clients, resilience, webhooks and files

**Calling other systems reliably: HTTP clients, resilience, webhooks, uploads and imports.**

- new HttpClient per call exhausts sockets; one static client caches stale DNS. IHttpClientFactory with typed clients fixes both.
- Use the standard resilience handler (retry, breaker, timeout); per-attempt timeouts must be well under the total.
- Stream large responses with ResponseHeadersRead.
- Sign webhooks with HMAC over the raw body plus a timestamp; verify in constant time; dedupe by event id.
- Upload large files directly to object storage with presigned URLs; never trust the extension or content type.
- Import big files by streaming into a staging table, then merging in one statement.
- Inside your own system use a broker; webhooks are for crossing organizational boundaries. Use claim checks for large payloads.

## Part 9: Building a frontend

### 9.1 How a browser shows a page

**How a browser turns HTML into pixels, and why pages feel slow or janky.**

- HTML becomes the DOM, CSS rules compute styles, then layout, paint and compositing produce pixels.
- JavaScript, input and drawing share one main thread; each frame has about 16 ms.
- Batch layout reads and writes; animate transform and opacity; load scripts as modules or with defer.
- Use textContent so data never becomes HTML (XSS); the same-origin policy keeps sites apart.
- Proper HTML elements build the accessibility tree; clickable divs don't.
- Pages can be server-rendered, single-page apps, or both with hydration.

### 9.2 Modern JavaScript

**Modern JavaScript for a C# developer, focusing on where it differs and bites.**

- Primitives are copied, objects are shared.
- Use ===, know the six falsy values, and use ?? rather than || for defaults.
- Spread and destructuring copy shallowly.
- map and filter build new arrays; prefer toSorted to in-place sort, and pass a comparer for numbers.
- this depends on how a function is called: use arrow functions for callbacks.
- Promises and async/await mirror tasks; never leave a promise floating. Use modules, a bundler, a linter and a formatter.

### 9.3 TypeScript

**TypeScript adds structural static types to JavaScript, checked at compile time only.**

- Types check your code, not the data that arrives: validate outside data and type it as unknown.
- Let inference work; annotate parameters and public returns.
- Unions with narrowing model closed sets; a never check makes switches exhaustive.
- Brands keep look-alike ids apart; generics with keyof and utility types build precise types.
- Avoid any; treat as and ! as unchecked promises; use as const and satisfies.
- Turn on strict and noUncheckedIndexedAccess; generate API types from the backend's OpenAPI contract.

### 9.4 Components and state

**Declarative UI built from components, and where each kind of state should live.**

- Describe what the screen shows for the current state; the framework updates the DOM.
- Components take props in and send events out, composed into a tree.
- Place state deliberately: local, shared, server state (a cache), URL state for anything linkable, form state.
- Store the minimum and derive the rest, with one source of truth.
- Data flows down, events flow up; lift state to a common parent when siblings share it.
- Replace objects and arrays instead of mutating; frameworks detect change by re-rendering or with signals. Always validate on the server too.

### 9.5 React today

**React as it is written today: components, hooks and React 19.**

- Components are functions from props and state to JSX.
- Setting state schedules a re-render with a new snapshot; replace, don't mutate.
- Keys give list items stable identity; index keys break changing lists.
- Hooks must run in the same order every render; effects are only for the world outside React.
- Derive values during render and handle actions in event handlers; use a data library instead of fetching in effects.
- React 19 adds actions, useActionState and useOptimistic; the React Compiler memoizes automatically. Next.js adds server components; plain SPAs start from Vite.

### 9.6 Angular today

**Modern Angular: Google's complete TypeScript framework.**

- Standalone components replace NgModules; built-in @if, @for (with track) and @switch replace *ngIf and *ngFor.
- State lives in signals, computed signals and effects; signal inputs and outputs connect components.
- Services are injected with inject(), much like .NET's container.
- HttpClient returns RxJS observables; bridge with toSignal or resources, and avoid nested subscriptions.
- Zoneless change detection is the default from Angular 21.
- @defer and lazy routes load code on demand; the CLI builds, tests and migrates.

### 9.7 Talking to the backend

**The frontend's side of the conversation with a slow, unreliable backend.**

- fetch resolves for every HTTP status: check response.ok and convert Problem Details into errors in one wrapper.
- Give every call a timeout and cancel stale ones with AbortController; only the latest request may update the screen.
- Show loading, empty and error states locally rather than failing the whole page.
- Let a server-state library cache, deduplicate, refresh and retry; use optimistic updates with rollback.
- Retry only temporary failures of safe requests.
- Configure CORS with explicit origins (or serve from one origin); never keep tokens in localStorage. Push live updates with SSE, WebSockets or SignalR.

## Part 10: Security

### 10.1 Thinking like an attacker

**Security as a way of thinking: how could someone misuse this?**

- Security protects confidentiality, integrity and availability.
- Most attackers are automated and opportunistic, so basic hygiene stops most of them.
- Every input, endpoint, dependency, tool and person is attack surface; shrink it.
- Threat model at design time: draw data flows and trust boundaries, walk through STRIDE, decide a defence for each threat.
- Least privilege, defence in depth, fail secure, secure defaults; never rely on obscurity; assume breach.
- Watch for account enumeration, unlimited guessing, missing ownership checks and verbose errors; build detection and patching into normal work.

### 10.2 Authentication and authorization

**Authentication proves who a caller is; authorization decides what they may do, on every request.**

- Store passwords with a slow salted hash; add MFA; prefer passkeys, which defeat phishing.
- Session cookies must be HttpOnly, Secure and SameSite; rotate on sign-in, time out, end on the server.
- In ASP.NET Core the caller is a ClaimsPrincipal; authentication middleware runs before authorization.
- IDOR (missing ownership checks) is the most common serious API flaw: scope every query by user or tenant.
- Use named policies plus resource-based checks; deny by default with a fallback policy.
- Validate JWT audience and issuer; the default 5-minute ClockSkew extends token life.
- Return 404 rather than 403 for resources the caller shouldn't know exist; enforce on the server, never only in the UI.

### 10.3 OAuth 2 and OpenID Connect

**Delegate sign-in to an identity provider with OAuth 2.0 and OpenID Connect.**

- Apps never handle passwords and users get single sign-on.
- OAuth issues access tokens to call APIs within scopes; OIDC adds ID tokens and discovery.
- Use authorization code flow with PKCE and a state check for user apps; client credentials for services.
- Access tokens are short-lived and audience-bound; ID tokens are for the client only; rotate and guard refresh tokens.
- APIs check signature (with an allowed algorithm), issuer, audience, expiry and scopes; let the framework do it.
- Keep tokens out of browser storage, ideally behind a backend-for-frontend.
- Single logout must end the app session, the IdP session and other apps' sessions (back-channel logout).

### 10.4 Common web vulnerabilities

**The small number of ways web apps are attacked, and the standard defence for each.**

- Broken access control: deny by default and check ownership on every request.
- Injection (SQL, shell commands, XSS): keep data apart from code with parameters, argument lists and output encoding.
- Add a strict Content Security Policy and security headers (nosniff, HSTS, Referrer-Policy).
- CSRF: SameSite cookies, anti-forgery tokens and origin checks.
- SSRF: allowlist destinations; resolve and check file paths; allow only local redirects.
- Bind to input DTOs to stop over-posting; deserialize only known types.
- Built-in rate limiting is per instance unless backed by a shared store; keep dependencies patched.

### 10.5 Secrets and cryptography basics

**The few cryptographic building blocks you need, and the rules that come with each.**

- Never design your own crypto; use standard building blocks.
- Hashes (SHA-256) fingerprint data; HMAC adds a key to prove origin; compare secrets in constant time.
- Passwords: Argon2id, or PBKDF2 with about 600,000 iterations; rehash as parameters rise.
- Encrypt with authenticated encryption (AES-GCM) and never reuse a nonce with a key.
- Use RandomNumberGenerator for secrets, not Random; Base64 is encoding, not encryption.
- Keep keys in a vault, use envelope encryption, and support two keys during rotation.
- Persist and share the Data Protection key ring, or every deploy logs users out.
- No secrets in code, images or logs; prefer managed identities so there is no secret at all; rotate on any leak.

### 10.6 Privacy, compliance and the supply chain

**Personal data, compliance and the software supply chain.**

- Don't collect what you don't need; isolate PII in one place so erasure touches one table.
- Publish ids, not names and emails, in events; plan account deletion across caches, logs, backups and search.
- Set retention on logs, traces, backups and topics; residency is an architectural constraint.
- The app should never connect as the database owner; use least-privilege roles and row-level security for tenants.
- Disk encryption protects files, not data from a compromised app credential.
- Supply chain: lock files, vulnerable-package build gates, secret scanning, SBOMs, signed images.
- The boring controls (MFA, patches, least privilege, no standing access, immutable backups) prevent most breaches.

## Part 11: Shipping software

### 11.1 Version control with Git

**Git's small underlying model, and the everyday commands that follow from it.**

- Git stores content-addressed blobs, trees and commits that form a graph.
- Branches and tags are labels on commits; HEAD is what you have checked out.
- Commit small, well-described snapshots; merge through reviewed pull requests.
- Rebase replays commits as new ones: fine for your own work, never for shared history.
- Undo with restore and reset locally, revert for shared history; the reflog is your safety net.
- Find changes with log, blame and bisect; never commit secrets or build output; integrate small changes often.

### 11.2 Builds and dependencies

**How .NET builds work and how to keep dependencies under control.**

- The SDK runs MSBuild over short SDK-style projects; share settings in Directory.Build.props and pin the SDK in global.json.
- Treat warnings as errors and enable recommended analyzers.
- NuGet installs one version per package per app; Central Package Management keeps versions in one file.
- Commit lock files, restore in locked mode, avoid floating versions.
- Audit transitive dependencies, automate updates, produce an SBOM, and use package source mapping against dependency confusion.
- Build once, deploy many; tag artifacts so you always know what is running.

### 11.3 Containers and Docker

**Packaging a program with everything it needs into a container image.**

- Images are content-addressed layers; containers are running instances.
- Multi-stage Dockerfile: build with the SDK image, run on a small runtime or chiseled image as non-root.
- Copy the project file and restore before copying source so the restore layer stays cached; use .dockerignore.
- A secret deleted in a later layer is still in the image: never bake secrets in.
- Configure through environment variables (Section__Key), keep data in volumes, log to stdout, set resource limits.
- Server GC with a small memory limit can get OOM-killed; cap the heap if needed.
- Deploy by digest or never-reused tags; use Compose locally; simpler platforms often beat Kubernetes.

### 11.4 CI/CD pipelines

**Pipelines that build, test and release every small change the same way.**

- CI builds and tests every merge; delivery keeps every change releasable; deployment releases automatically.
- Check each PR: locked restore, warnings as errors, tests, analysis and scans; keep it fast and free of flaky tests.
- Build one artifact and promote it by digest through environments; smoke-test after each deploy.
- Rolling deploys need old and new code to coexist, so run expand-contract migrations first.
- Liveness must not check the database, or one blip restarts every pod; add a short preStop pause.
- Feature flags separate deploy from release; delete flags when done.
- Use federated identity, least-privilege pipelines and pinned actions; track DORA metrics.

### 11.5 Cloud basics

**Renting computing on demand: services, regions, identity, infrastructure as code and cost.**

- IaaS, PaaS, SaaS and serverless; pick the least-control compute that works.
- The provider secures the cloud; you secure data, identities, configuration and code.
- Spread across availability zones; keep services on private endpoints in virtual networks.
- Identity is the boundary: narrow role assignments and managed identities between services.
- Define everything as code (Bicep, Terraform); the plan output is the review.
- Serverless still needs idempotency and has cold starts; API gateways cap timeouts (often 29 s) and payload size.
- Block public access on object storage and use short-lived presigned URLs.
- Tag everything, set budgets, watch egress, and track cost per request.

### 11.6 Observability

**Explaining production behaviour from what the system recorded: logs, metrics and traces.**

- Logs: structured events with message templates, levels and correlation ids; keep secrets and personal data out.
- Metrics: cheap counters, gauges and histograms; keep label cardinality low (no user ids or raw URLs).
- Use percentiles from histograms, never averages; averaging percentiles across instances is meaningless.
- Traces follow a request across services as spans joined by the traceparent header; sample at high volume.
- OpenTelemetry standardizes all three; in .NET: ILogger, ActivitySource and Meter.
- Measure RED for services, USE for resources, plus business metrics; define SLOs and alert on burn rate.
- Alert on user-facing symptoms with runbooks; put the trace id in every log line and error response.

### 11.7 Kubernetes and orchestration

**Kubernetes: the objects you actually use, and whether you need it at all.**

- Ask first whether you need it; simpler platforms are often better.
- Always set resource requests; set memory limits; be careful with CPU limits, which throttle.
- Startup, readiness and liveness probes answer different questions; liveness must not check dependencies.
- Grace period must exceed the app's shutdown timeout; add a PodDisruptionBudget and maxUnavailable: 0.
- An empty Endpoints list usually means no pod passes readiness, not a network problem.
- Template config with Helm or Kustomize; adopt GitOps; scale queue workers with KEDA, not CPU.

### 11.8 Linux for backend developers

**Enough Linux to run, debug and operate backend services.**

- Linux is a kernel; everything is a file; one tree from /, no drive letters.
- Redirection and pipes compose small tools; > truncates the file before the command runs.
- Quote variables and use -- before file names; test destructive commands with ls or find first.
- Permissions: a directory without x blocks everything inside; the docker group equals root.
- SIGTERM asks politely, SIGKILL doesn't; the OOM killer leaves evidence only in dmesg.
- In containers bind to 0.0.0.0, not 127.0.0.1; check both host and cloud firewalls.
- systemd runs services and journalctl reads their logs; cron has an almost empty environment.
- Use LF line endings for scripts; SSH keys need 600 permissions.

### 11.9 Windows Server and IIS

**Running .NET on IIS and Windows Server, and modernizing it.**

- IIS hosts apps in application pools; the recycle is the state boundary.
- Default recycling (every 29 hours, config edits, memory limits) drops in-memory state and sessions.
- Set debug="false" in production and use Application Initialization to warm up after recycles.
- Windows equivalents: PowerShell remoting, scheduled tasks, event logs.
- Modernize in steps: monitoring, automated deployment and central logs first, porting later.

## Part 12: Systems and architecture

### 12.1 Monoliths, modules and services

**The real architectural decision is where boundaries go and how firmly they're kept, not how many services you run.**

- Monoliths are simple but decay into a big ball of mud without discipline.
- A modular monolith keeps one deployment with enforced module boundaries and exclusive data ownership; it is often the destination, not a waypoint.
- Microservices buy independent deployment, scaling and failure isolation, at the cost of networks, partial failure and eventual consistency.
- Done badly you get a distributed monolith: all of the cost, none of the independence.
- Draw boundaries around bounded contexts; each service owns its data and nobody else reads its tables.
- Synchronous chains multiply latency and divide availability (three services at 99.9% give 99.7%).
- Dependencies point inward to the domain (layered, clean, hexagonal); vertical slices organize by feature.
- Change shape gradually with the strangler fig and anti-corruption layers; never a big-bang rewrite.

### 12.2 Distributed systems basics

**What changes when parts fail independently, messages get lost and clocks disagree.**

- Systems fail partially, and a slow part looks exactly like a dead one: a timeout tells you nothing about what happened.
- Make every operation safe to retry (idempotent).
- Quorums with W + R > N guarantee reading the latest write.
- CAP: during a partition choose consistency or availability; otherwise the trade is latency (PACELC).
- Read-your-writes and monotonic reads are the guarantees users notice.
- Never order events across machines by wall-clock time; last-write-wins silently discards data.
- Consensus needs majorities; distributed locks need fencing tokens. Use proven infrastructure, never homemade consensus.

### 12.3 Messaging and events

**Messaging and events: decoupling services in time, failure and knowledge.**

- Commands ask one handler to act; events state past-tense facts for any number of listeners.
- Publishing outside the transaction loses events: use a transactional outbox or change data capture.
- Delivery is at least once, so consumers must be idempotent (record processed message ids).
- Ordering holds only per partition or key; choose the key carefully to avoid hot partitions.
- Kafka is a replayable log, RabbitMQ a router (set prefetch, ack after processing), SQS is simple (watch the visibility timeout).
- Sagas replace distributed transactions with local steps and compensations, by choreography or orchestration.
- Alert on dead-letter queue depth and message age; a database table with SKIP LOCKED is a fine queue at moderate volume.
- Event sourcing and CQRS are powerful but expensive; use them only where they pay.

### 12.4 Scaling and performance

**Performance and scalability: knowing where the time goes, and why latency suddenly explodes.**

- Measure before optimizing: profilers, BenchmarkDotNet, load tests and production telemetry.
- Time usually goes, in order, to unnecessary queries, missing caches, serial I/O chains and blocking, before code.
- Queueing: latency multiplies as utilization nears 100%, so plan capacity at acceptable latency with headroom.
- Little's law, L = λW, links concurrency, throughput and latency.
- Load test with realistic data volume and distribution; the output is the bottleneck, not a number.
- Scale stateless tiers out; scale the database by doing less work, caching, replicas, read models and partitioning.
- Rate limiting protects you from callers; queues level load; share as little as possible between nodes.

### 12.5 Reliability

**Reliability: serving users well while parts fail, and recovering fast when you can't.**

- Measure with SLIs from the user's side, set SLOs, manage the error budget and alert on burn rate.
- Every call gets a timeout from the latency budget; retry only idempotent, transient failures, with backoff and jitter, at one layer.
- Circuit breakers fail fast and need a fallback; bulkheads isolate pools per dependency; shed load past the knee.
- Most incidents follow changes: release gradually.
- Incidents: clear roles, mitigation before diagnosis, ask "what changed?", blameless postmortems.
- RPO and RTO are business decisions; an untested backup is not a backup; keep immutable backups in a separate account.
- Spread across zones and test failover; multi-region only when the business pays for it.

### 12.6 Domain modeling and application architecture

**Modeling the business in code: aggregates, value objects, domain events, CQRS and multi-tenancy.**

- An aggregate is one consistency boundary: one root, one transaction, small, and referencing others by id.
- Value objects and private setters make invariants impossible to break, which deletes defensive code.
- Anemic models are fine for simple CRUD; put rich models where the complexity actually is.
- Domain events decouple side effects; never send email or call APIs inside the transaction.
- CQRS separates read and write models and needs neither event sourcing nor a second database.
- Mediator pipelines give one place for cross-cutting concerns; specifications name reusable query predicates.
- Multi-tenancy: enforce the tenant in one place and test that tenant A can never read tenant B's data.
- Model lifecycles as explicit state machines.

## Part 13: Working with AI tools

### 13.1 How coding assistants work

**What is inside an AI coding assistant, and why it fails the way it does.**

- Assistants are large language models trained to predict the next token, then tuned to follow instructions.
- Text is split into tokens; limits and costs are counted in tokens.
- The model sees only its context window: instructions, conversation, files and tool results.
- Output is sampled and plausible, not checked: invented APIs and packages, plus a knowledge cutoff.
- Retrieval uses embeddings; agents loop through tool calls; MCP connects tools.
- Prompt injection means agents need least privilege, sandboxes and approval for risky actions; keep secrets out of prompts.
- Adding AI to a .NET app is mostly ordinary integration work: HTTP calls, vector stores, retries and cost control.

### 13.2 Using them without losing skill

**Using assistants heavily while keeping your own skills.**

- Generated code is your code: read everything and merge nothing you can't explain.
- Resist automation bias; verify with the compiler, strict types, analyzers and running the code.
- Write tests from the requirement: tests generated with the code share its assumptions.
- Give context: goals, versions, existing code, conventions, errors and a repository instructions file; ask for a plan first.
- Bound agents with least privilege, sandboxes, approvals, small commits and human review; check suggested packages exist.
- Build skill: try first, ask for explanations and reviews, and practise without assistance.

## Part 14: Practice: recipes, worked builds and real cases

### 14.1 Recipes: code you will actually write

**Ready-to-use recipes for the code you write most often.**

- Paginated list endpoint: tenant first in the predicate and index, half-open date ranges, a sort allow-list, a unique tiebreaker, projection to DTOs.
- Idempotent command handler: look up the idempotency key, let a unique constraint decide concurrent duplicates, write the outbox in the same transaction.
- Report queries: route them to a replica, keep them sargable, and bound their date range.
- Related recipes live in their topic chapters: CSV import (8.9), resilient client (8.9), cached read (8.6), queue worker (8.7), file upload (8.9), concurrent-safe update (7.4).

### 14.2 Worked builds

**Complete features built end to end, from requirement to tests.**

- Order placement: validation protects the contract; stock and SKU checks are domain rules that need data.
- Idempotency as an endpoint filter: claim the key with a unique constraint, never SELECT-then-INSERT.
- Domain rules, optimistic concurrency and the outbox in one transaction; on retry, clear the tracker and reload, with a cap.
- The test that matters is the oversell test: real concurrency against a real database.
- Webhooks: verify the signature (FixedTimeEquals) before parsing, store, then process. Uploads: presigned, check magic bytes, serve from a separate domain.
- Rescuing a slow endpoint: measure first, compute in the database, add the index the plan asks for (online), cache last.
- Outbox relay uses the outbox row id as the message id; workflows need compensations; scheduled jobs need idempotent work, not just a lock.
- Tenant isolation beyond query filters (row-level security); rename a column safely in four expand-contract deploys.

### 14.3 Worked cases from production

**Real production incidents worked through: symptom, diagnosis, fix and lesson.**

- Order list 80 ms → 4 s: 101 queries for 50 rows (lazy-loading N+1). Count commands per request.
- Wrong report totals: a one-to-many join multiplied parent amounts before summing.
- Page 500 takes 8 s: OFFSET paging cost plus a non-unique sort that skipped and repeated rows; keyset paging fixed both.
- Dashboard melts the database at 9 am: synchronized cache expiry under concurrency; one query plus stampede protection and jitter.
- Four-hour import: a list search inside a loop, SaveChanges per row and 1.2 M tracked entities.
- Wrong tenant's data in a cache: a mis-scoped query poisoned a cache entry with no expiry.
- Sale-day collapse: three small faults compounding; adding pods made connection limits worse.
- Others: the service split along entities (N+1 across services), an ARM-only race, parameter sniffing with data skew, a non-reversible rename, an outbox losing 0.2% of messages.
- Order service design: 5,000 orders/minute is about 83 requests per second, which fits a few pods and one database. Do the arithmetic first.

### 14.4 Production diagnostics playbook

**A diagnostics playbook for common production symptoms.**

- p99 up, p50 normal: gen 2 GC pauses, lock contention or a hot-key stampede.
- Memory climbs until OOM-killed: unbounded static caches, captive DbContext, unsubscribed events.
- Low CPU but requests queue: thread pool starvation from sync-over-async.
- "Timeout obtaining a connection from the pool": pool exhaustion (default 100), not a slow database.
- Data flips between fresh and stale on refresh: per-instance IMemoryCache across pods.
- Database CPU spikes after a deploy: cold-cache stampede.
- Tools: dotnet-counters, dotnet-dump, dotnet-trace, curl -v, browser network timing; on a Linux box check the boring things first (disk, memory, deleted-but-open files).

### 14.5 What actually goes wrong: field guide and checklists

**What actually goes wrong in production, ranked, plus review checklists.**

- The top ten by frequency × damage: N+1 queries, sync-over-async, per-instance caches after scaling out, unbounded growth, non-idempotent message handling, missing ownership checks (IDOR), check-then-act races, breaking schema changes in rolling deploys, database checks in liveness probes, untested backups.
- Almost all appear only under conditions missing on a laptop: concurrency, scale, many instances, real data volume, partial failure, time.
- Three questions before merging: What if this runs twice? What if there are a hundred at once? What if the thing I call is down or slow?
- PR checklist: no .Result or .Wait(), tokens flow to every call, no async void, bounded queries, DTOs at the boundary.
- A trap index collects all 214 traps flagged across the library, each linked to its explanation.

### 14.6 Career, team practice and interviews

**Study priorities, team practices and interview preparation.**

- Study first: N+1, DbContext thread safety, BackgroundService scopes, async state machine, memory cache vs Redis.
- 50 short interview answers cover async, EF Core, DI, GC, caching, messaging, security and cloud.
- System design: clarify requirements, compute the load, then design; "this fits on three pods and one Postgres" is often the right answer.
- Debugging questions test method: narrate the funnel and name real tools.
- Behavioral answers: situation, your action, measured result; have a real failure ready.
- Team practices: ADRs record alternatives and consequences; reviews focus on design and risk; pay tech debt alongside feature work.

## Part 15: Reference

### 15.1 Cheat sheets and compressed summaries

**Compressed summaries and the numbers worth memorizing.**

- Latency orders of magnitude: one Redis call is about 10,000 memory-cache reads; an N+1 over 100 rows wastes 100–1,000 ms.
- Limits worth knowing: ~1 MB thread stack, 85,000-byte LOH threshold, 100 pooled connections per connection string, ~2,100 SQL Server parameters, 5-second default shutdown timeout, 5-minute JWT clock skew, 29-second API Gateway timeout.
- Diagnostic command cheat sheet and a "which should I use?" decision index.
- One-paragraph compressions of the types, APIs and caching guides.

### 15.2 Terminology by domain

**Terminology tables grouped by domain, so related terms sit side by side.**

- Data shapes: DTO, entity, value object, aggregate, view model, projection.
- Architecture, runtime, DI, async, data access, messaging, operations and testing vocabularies.
- C# language terms and every acronym expanded.
- Platform terms: networking, HTTP and TLS, proxies, Linux, containers, storage, deployment.
- Pattern, design-principle, domain and distributed-systems vocabularies.
- Example of a distinction that matters: a stub supplies data, a mock asserts a call was made.

### 15.3 Glossary A–Z

**Glossaries from all the guides.**

- The full A–Z glossary of 1,449 terms is on the Glossary page, each linked to the chapter that explains it.
- This chapter adds the shorter glossaries from the .NET Deep Dive and the Backend volume's books (types, async, APIs, caching).

### 15.4 Practice questions

**307 practice questions with hidden answers, for self-testing.**

- Grouped into decks: C# and .NET, platform, design patterns, JavaScript and TypeScript, databases, Linux, data and LINQ, practical, APIs and security, plus one question for each casebook case (4.9, 7.9).
- Answer each in your head, then open it to compare.
- Good for a quick review before an interview or after reading a part.
