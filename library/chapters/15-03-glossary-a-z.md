---
id: "15-3"
number: "15.3"
title: "Glossary A–Z"
part: 15
part_title: "Reference"
words: 27829
minutes: 121
---

# 15.3 Glossary A–Z

> **Quick look.** Glossaries from all the guides.
>
> - The full A–Z glossary of 1,449 terms is on the Glossary page, each linked to the chapter that explains it.
> - This chapter adds the shorter glossaries from the .NET Deep Dive and the Backend volume's books (types, async, APIs, caching).


### A–Z glossary

Every key term in the guide, with the chapter that explains it. Background terms name tools and technologies that appear before, or without, a chapter of their own.

### #

- **.gitignore** (11.1): A file of patterns for files Git should not track, such as build output.
- **.NET** (background; covered in 2.2): Microsoft's free, cross-platform platform for running C# and similar languages: the runtime, the standard library and the tools.
- **.NET assembly** (2.2): A `.dll` or `.exe` holding IL and metadata. The unit .NET loads and references.

### A

- **A record** (6.4): A DNS record giving an IPv4 address.
- **A* search** (3.6): A shortest-path search that uses a heuristic estimate to head toward the target.
- **AAAA record** (6.4): A DNS record giving an IPv6 address.
- **AbortController** (9.7): An object whose signal cancels requests and other async work when aborted.
- **Absolute expiration** (8.6): Removing a cache entry at a fixed time after it was stored.
- **Abstract class** (4.2): A class that can't be instantiated and exists to be inherited, possibly with shared code.
- **Abstract syntax tree** (2.1): A tree showing the structure of a program, with punctuation removed. Often shortened to AST.
- **Abstraction** (2.1): Hiding details so you can work with bigger ideas, such as names and lists instead of addresses and registers.
- **Acceptance criteria** (13.2): The conditions that must hold for a task to count as done, ideally expressed as tests.
- **Access modifier** (2.6): A keyword such as private, internal or public that controls who may use a member or type.
- **Access pattern** (7.6): A specific question an application asks of its data, used to drive data design.
- **Access token** (10.3): A credential a client sends to an API, saying what it may do there.
- **Accessibility tree** (9.1): The version of a page presented to screen readers and other assistive technologies.
- **ACID** (7.4): Atomicity, consistency, isolation and durability: the guarantees of a database transaction.
- **ACK**: see **Acknowledgement** (6.3)
- **Acknowledgement** (6.3): A message telling the sender how much of the stream has arrived. Often shortened to ACK.
- **Action** (9.5): In React 19, an async function whose pending state and result React tracks for you.
- **Actor model** (5.4): Independent units that own their state, handle one message at a time and communicate only by messages.
- **Adapter pattern** (4.3): Wrapping an object so that it fits an interface it doesn't implement itself.
- **Address** (1.5): The number that identifies one byte of memory.
- **Adjacency list** (3.6): Storing a graph as a list of neighbours for each vertex.
- **Adjacency matrix** (3.6): Storing a graph as a grid of all vertex pairs, marking which are connected.
- **AES** (10.5): The Advanced Encryption Standard, the symmetric cipher used almost everywhere.
- **Aggregate function** (7.2): A function that turns many rows into one value, such as COUNT, SUM or MAX.
- **AI** (background; covered in 13.1): Artificial intelligence: software that does tasks associated with human intelligence, today mostly through machine learning.
- **Alert** (11.6): A notification that something needs human attention.
- **Alias** (2.4): Another variable referring to the same object.
- **Alignment** (1.5): Placing a value at an address that is a multiple of its size, so it can be read quickly.
- **Allocation context** (2.7): A thread's private block of fresh memory, from which it allocates by moving a pointer.
- **Allowlist** (8.5): A definition of what valid input looks like, accepting only that.
- **Amdahl's law** (5.5): The rule that the serial fraction of a job limits its maximum parallel speedup.
- **Amortized cost** (3.1): The average cost per operation over a long sequence, even if single operations are sometimes expensive.
- **Anemic domain model** (4.1): Domain classes that only hold data, with the rules scattered in separate services.
- **Angular** (9.6): Google's complete TypeScript framework for web applications, a 2016 rewrite of AngularJS.
- **Angular CLI** (9.6): The ng command that creates, builds, tests and updates Angular projects.
- **Angular service** (9.6): A class marked @Injectable that Angular's dependency injection creates and provides.
- **AngularJS** (9.6): Google's original JavaScript framework from 2010, replaced by Angular and now retired.
- **Anti-corruption layer** (12.1): Adapters at a boundary that translate another system's model into your own.
- **Anti-forgery token** (10.4): A secret value included in forms and requests that another site can't know, used to stop CSRF.
- **any** (9.3): The type that switches type checking off for a value.
- **Anycast** (6.4): Giving many servers the same address, so each packet goes to the nearest one.
- **AOT**: see **Native AOT** (2.2)
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **API gateway** (12.1): A single entry point that routes client requests to services and handles authentication, limits and TLS.
- **API key** (10.2): A long secret value that identifies an application calling an API.
- **API versioning** (8.2): Running several versions of an API side by side so breaking changes don't affect existing clients.
- **Application layer** (6.1): The layer where programs speak their own protocols, such as HTTP and DNS.
- **Architecture test** (12.1): An automated test that fails when code breaks a structural rule, such as a forbidden dependency.
- **Argument** (0.1): The actual value passed to a method when calling it.
- **Arithmetic shift** (1.1): A right shift that copies the sign bit into the gap, so negative numbers stay negative.
- **ARM** (background; covered in 1.4): A family of processor designs used in phones, Apple silicon Macs and a growing number of servers.
- **ARM64** (background; covered in 1.4): The 64-bit ARM processor architecture, used by Apple silicon Macs, most phones and many cloud servers.
- **ARP** (6.2): Address Resolution Protocol, which finds the MAC address for an IPv4 address on a local network.
- **Arrange-act-assert** (4.6): The three steps of a test: set up, perform the action, check the outcome.
- **Array** (3.2): A fixed number of elements of one type, stored side by side in memory.
- **Arrow function** (9.2): A compact function, (a, b) => a + b, that uses the surrounding code's this.
- **as const** (9.3): An assertion that makes the compiler infer the narrowest, read-only type for a literal.
- **ASCII** (1.3): A 7-bit character table from the 1960s with 128 characters, covering English text.
- **Assembly language** (1.4): The human-readable form of machine code.
- **Assembly metadata** (2.2): A detailed description of every type and member in an assembly, used by the runtime and tools.
- **Assertion** (2.8): A check that something must be true, used to catch bugs early.
- **AST**: see **Abstract syntax tree** (2.1)
- **Asymmetric encryption** (6.5): Encryption using a key pair: a public key anyone may have and a private key kept secret.
- **Async state machine** (5.3): The compiler-generated object that stores an async method's position and locals between awaits.
- **Async stream** (5.3): A sequence whose items arrive over time, consumed with await foreach. IAsyncEnumerable in .NET.
- **Asynchronous programming** (5.3): Starting operations and continuing when they finish, without threads waiting in between.
- **AsyncLocal** (2.6): A .NET type whose value flows with the current asynchronous operation instead of the thread.
- **At-least-once delivery** (8.7): A guarantee that messages are never lost, but may be delivered more than once.
- **At-most-once delivery** (8.7): A guarantee that messages are never repeated, but may be lost.
- **Atomic operation** (5.1): An operation that other threads can only see as not started or fully done.
- **Atomic rename** (1.6): Replacing one file with another in a single step, so readers never see a mix.
- **Atomicity** (7.4): All of a transaction's changes happen, or none of them do.
- **Attack surface** (10.1): Every point where an attacker can try to get in or cause harm.
- **Attribute** (0.1): Extra information in square brackets attached to a declaration, such as `[Flags]`.
- **Audience** (10.3): Who a token is meant for, in its aud claim; other recipients must reject it.
- **Authenticated encryption** (10.5): Encryption that also detects any change to the ciphertext, such as AES-GCM.
- **Authentication** (10.2): Establishing who a caller is.
- **Authentication factor** (10.2): A kind of credential: something you know, something you have or something you are.
- **Authoritative name server** (6.4): A server that holds the official records for a zone.
- **Authorization** (10.2): Deciding whether a caller may perform an action on a resource.
- **Authorization code flow** (10.3): The OAuth flow where the browser brings back a one-time code that the app exchanges for tokens.
- **Authorization server** (10.3): The OAuth component that authenticates users and issues tokens, usually the identity provider.
- **Automation bias** (13.2): Trusting an automated system's output more than it deserves, and no longer checking it.
- **Automation complacency** (13.2): No longer actively monitoring automation because it is usually right, and so missing its failures.
- **Autonomous system** (6.2): A group of networks run by one organization, such as an ISP or cloud provider.
- **Autoscaling** (11.5): Adding and removing capacity automatically as load changes.
- **Availability** (10.1): A system works when legitimate users need it.
- **Availability zone** (11.5): One of several physically separate data centres within a region.
- **Average case** (3.1): The expected cost over typical or random inputs of a given size.
- **Await** (5.3): Pausing a method until a task finishes, without blocking its thread.
- **AWS** (11.5): Amazon Web Services, the largest cloud provider, launched in 2006.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Azure subscription** (11.5): An Azure billing and access boundary that holds resource groups.

### B

- **B-tree** (3.4): A shallow tree with many keys per node, sized to disk pages. Used for database indexes.
- **Backend** (8.1): The part of a system that runs on the organization's servers, holding data and enforcing rules.
- **Backend for frontend** (8.4): A small backend owned by a frontend team that shapes data for its own screens. Often shortened to BFF.
- **Backfill** (7.5): Filling a new column or table with data derived from existing data.
- **Background GC** (2.7): Doing most of a full collection on a separate thread while the program keeps running.
- **Background job** (8.7): Work done outside the request that triggered it, so the user doesn't wait for it.
- **Backpressure** (5.4): A slow consumer making a fast producer slow down, to keep buffers from growing without limit.
- **Backtracking** (3.8): Searching by making a choice, exploring, and undoing it to try the next choice.
- **Backup** (1.6): A separate copy that keeps older versions, so mistakes can be undone.
- **Backward-compatible change** (8.2): A change to an API that keeps every existing client working.
- **Balanced tree** (3.4): A tree whose height stays close to log n, whatever order values arrive in.
- **Bandwidth** (6.1): How much data a path can carry per second, usually quoted in bits per second.
- **Banker's rounding** (1.2): Rounding halfway values to the nearest even number. .NET's default.
- **Base case** (3.8): The case a recursive function answers directly, without recursing.
- **Base image** (11.3): The image a Dockerfile starts from, such as the official ASP.NET Core runtime.
- **Base64** (1.3): A way to write bytes as text using 64 safe characters. Every 3 bytes become 4 characters.
- **Bearer token** (10.2): A token sent in the Authorization header; whoever holds it is treated as its owner.
- **Benchmark** (12.4): A precise, repeatable measurement of a small piece of code in isolation.
- **BenchmarkDotNet** (12.4): The standard .NET library for writing and running benchmarks.
- **BFF**: see **Backend for frontend** (8.4)
- **BGP** (6.2): Border Gateway Protocol, which autonomous systems use to announce which addresses they can reach.
- **Bicep** (11.5): Microsoft's declarative language for defining Azure resources.
- **Big-O notation** (3.1): A way to describe how an algorithm's cost grows with input size, keeping only the dominant term.
- **Binary heap** (3.5): A complete binary tree, stored in an array, where every parent is no larger than its children.
- **Binary search** (3.7): Finding a value in sorted data by repeatedly halving the range. O(log n).
- **Binary search tree** (3.4): A binary tree where every left subtree holds smaller values and every right subtree larger ones.
- **Binary tree** (3.4): A tree where each node has at most two children, left and right.
- **Bisect** (11.1): Finding the commit that introduced a problem by binary search through history.
- **Bit** (1.1): One binary digit, 0 or 1. The smallest unit of information.
- **Bitwise operator** (1.1): An operator that works on each bit position separately: AND, OR, XOR, NOT and shifts.
- **Blameless postmortem** (12.5): A review after an incident that looks for system causes and fixes, not people to blame.
- **Blazor** (background): A .NET framework for building web user interfaces in C#, which can run in the browser through WebAssembly.
- **Block** (1.6): The unit in which a file system hands out space, usually 4 KB.
- **Blocking** (7.4): A transaction waiting for a lock held by another.
- **Blocking call** (5.3): A call that makes its thread wait, doing nothing, until the result is ready.
- **Bloom filter** (3.3): A compact bit array that says a key is definitely absent or possibly present.
- **Blue-green deployment** (11.4): Running the new version alongside the old one and switching all traffic over at once.
- **Bounded context** (12.1): A part of the domain within which each term has one precise meaning and model.
- **Bounded queue** (5.2): A queue with a maximum size, which makes producers wait when it is full.
- **Boxing** (1.5): Copying a value type into a new heap object so it can be treated as an object.
- **Branch prediction** (1.4): Guessing which way a conditional jump will go so the processor can keep working ahead.
- **Branded type** (9.3): A type with a fake property that exists only in the type system, keeping look-alike values apart.
- **Breadth-first search** (3.6): Exploring a graph ring by ring using a queue. Finds paths with the fewest edges.
- **Breaking change** (8.2): A change to an API that makes some existing clients fail or misbehave.
- **Broken access control** (10.4): Users being able to see or do things they shouldn't, because permission checks are missing or wrong.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Brute-force attack** (10.1): Trying many possible passwords or keys until one works.
- **Bucket** (3.3): One slot of a hash table's array, chosen by a key's hash.
- **Buffer overflow** (1.5): Writing past the end of a buffer into neighbouring memory. A classic security hole.
- **Buffer pool** (7.1): The database's memory cache of recently used pages.
- **Build** (11.2): Turning source code into artifacts, by restoring packages, compiling, analyzing and packaging.
- **Build artifact** (11.4): A file produced by a pipeline job and passed on, such as test results or a container image.
- **Build context** (11.3): The files sent to the image builder, filtered by .dockerignore.
- **Bulkhead** (12.5): A separate, limited pool of resources per dependency, so one can't exhaust everything.
- **Bundler** (9.2): A tool that combines modules and packages into a few optimized files for the browser.
- **Burn rate** (12.5): How fast an error budget is being used, the basis for reliability alerts.
- **Business logic** (8.1): The rules and calculations that are actually about the business, separate from HTTP and storage.
- **Business logic abuse** (10.1): Misusing features exactly as built, in ways their designers didn't intend.
- **Byte** (1.1): Eight bits. The smallest unit most computers can address in memory.
- **Byte stream** (6.3): A continuous sequence of bytes with no built-in message boundaries.
- **Byte-pair encoding** (13.1): Learning a token vocabulary by repeatedly merging the most frequent adjacent pair of pieces.
- **Bytecode** (2.2): Compact instructions for a virtual machine, run by an interpreter or JIT.

### C

- **C#** (background; covered in 0.1): The main programming language of .NET, and the language of this guide's examples.
- **CA**: see **Certificate authority** (6.5)
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Cache hit** (8.6): A lookup that finds the value in the cache.
- **Cache invalidation** (8.6): Removing or updating cached copies when the source data changes.
- **Cache key** (8.6): The identifier a cached value is stored under, which must include everything that changes the value.
- **Cache line** (1.5): The 64-byte block in which memory moves between RAM and the processor's caches.
- **Cache miss** (8.6): A lookup that doesn't find the value, so it must be loaded from the source.
- **Cache stampede** (8.6): Many requests missing the same key at once and all loading it from the source.
- **Cache-aside** (8.6): Checking the cache, and on a miss loading from the source and storing the result.
- **Call stack** (3.8): The stack of frames for all method calls currently in progress.
- **Callback** (5.4): A function passed to be called later, when something has happened.
- **Callback hell** (5.4): Deeply nested callbacks that make asynchronous code hard to read and handle errors in.
- **Calling convention** (1.4): The agreement about which registers and stack slots carry arguments and results.
- **Canary release** (11.4): Sending a small share of traffic to a new version first, and widening it only if it stays healthy.
- **CancellationToken** (2.8): A .NET object that signals an operation should stop, which it does by throwing OperationCanceledException.
- **Canonicalization** (8.5): Converting input to one standard form before checking or comparing it.
- **CAP theorem** (12.2): During a network partition, a system must choose between consistency and availability.
- **Capacity** (3.2): The number of slots in a dynamic array's internal array, including unused ones.
- **Capacity planning** (12.4): Deciding how much of each resource a system needs for expected load, with margin.
- **Captive dependency** (2.6): A long-lived service holding a shorter-lived one, keeping it alive beyond its intended scope.
- **Cardinality** (11.6): The number of distinct values a metric label can take, which multiplies the time series stored.
- **Cardinality estimate** (7.3): The optimizer's guess of how many rows a step of a plan will produce.
- **Cartesian explosion** (7.7): Rows multiplying when one query joins several one-to-many collections.
- **Cascade delete** (7.5): Deleting rows that refer to a deleted row, automatically.
- **Cast** (2.3): An explicit conversion to another type, which may fail or lose information.
- **CDN** (6.6): Content delivery network: caching servers in many cities that serve content close to users.
- **Central Package Management** (11.2): Keeping every package version for a repository in one Directory.Packages.props file.
- **Certificate** (6.5): A signed document linking a public key to one or more domain names.
- **Certificate authority** (6.5): An organization that verifies domain control and signs certificates. Often shortened to CA.
- **Certificate chain** (6.5): The sequence of certificates from a server's certificate up to a trusted root.
- **Chaining** (3.3): Handling collisions by keeping a chain of entries in each bucket.
- **Change data capture** (12.3): Turning committed database changes into events by reading the database's transaction log.
- **Change detection** (9.6): How a framework decides which parts of the view to update after something happens.
- **Change tracker** (7.7): The part of a DbContext that records loaded entities and their original values to detect changes.
- **Channel** (5.3): An asynchronous producer-consumer queue, optionally bounded.
- **Chaos engineering** (12.5): Deliberately injecting failures, in a controlled way, to test that a system copes.
- **Check constraint** (7.5): A rule that each row must satisfy, such as a fare not being negative.
- **Check-then-act** (5.1): Checking a condition and acting on it, when the condition can change in between.
- **Checkpoint** (7.1): The point at which changed pages in memory are written to the data files.
- **Checksum** (1.1): A small value computed from data, used to detect accidental damage. A CRC is a common kind.
- **Chunked transfer encoding** (6.6): Sending a body in pieces, each with its own length, so the total needn't be known in advance.
- **CI** (background; covered in 11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.
- **CI/CD pipeline** (11.4): The automated sequence of steps that takes a change from commit to production.
- **CIA triad** (10.1): Confidentiality, integrity and availability: the three properties security protects.
- **CIDR notation** (6.2): Writing a network as an address and a prefix length, such as 192.168.1.0/24.
- **Ciphertext** (6.5): Encrypted data, unreadable without the key.
- **Circuit breaker** (12.5): A guard that stops calling a failing dependency for a while, failing fast instead.
- **Claim** (10.2): A statement about a user, such as their name, role or tenant, carried with their identity.
- **Class** (4.1): A blueprint that declares the fields and methods of its objects.
- **Clickjacking** (10.4): Tricking users into clicking on your page shown invisibly inside another site's frame.
- **Client** (8.1): A program that sends requests to a server.
- **Client credentials flow** (10.3): The OAuth flow in which a service gets an access token as itself, with no user.
- **Client evaluation** (7.7): Running parts of a query in memory in .NET instead of in the database.
- **Client-server database** (7.1): A database that runs as a separate service that programs connect to over the network.
- **Clock cycle** (1.4): One tick of the processor's clock. At 4 GHz, 0.25 nanoseconds.
- **Clock skew** (12.2): The difference between two machines' clocks at the same moment.
- **Closure** (2.4): A function together with the variables it captured from its surroundings.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **CLR** (background; covered in 2.2): Common Language Runtime: the .NET runtime that loads, JIT-compiles and runs .NET code and manages its memory.
- **Clustered index** (7.3): In SQL Server, the table itself stored as a B+ tree in key order. One per table.
- **CNAME record** (6.4): A DNS record saying a name is an alias for another name.
- **Code coverage** (4.6): The share of code lines or branches that ran during the tests.
- **Code page** (1.3): One of many old 8-bit extensions of ASCII. Each filled the extra 128 slots differently.
- **Code point** (1.3): A character's number in the Unicode table, such as U+00E9 for é. C# calls it a Rune.
- **Code review** (13.2): Another person reading a change before it is merged, to check its correctness, design and safety.
- **Code smell** (4.5): A surface sign in code that suggests a deeper design problem.
- **Code unit** (1.3): One piece of an encoding: one byte in UTF-8, two bytes in UTF-16.
- **Coding agent** (13.1): An assistant that works in a loop of choosing actions, calling tools and reading their results.
- **Coercion** (2.3): Automatically converting a value to another type to make an operation work.
- **Cohesion** (4.5): How closely the parts inside one module belong together. Keep it high.
- **Collection expression** (0.1): Building a collection from items in square brackets, such as `[1, 2, 3]`.
- **Collision** (3.3): Two different keys landing in the same bucket.
- **Collision resistance** (10.5): The property that nobody can find two different inputs with the same hash.
- **Column** (7.2): One named, typed fact stored for every row of a table.
- **Command** (12.3): A message asking one recipient to do something, which it may refuse.
- **Command injection** (10.4): Injection into an operating system command, letting an attacker run other commands.
- **Command-query separation** (4.4): The rule that a method should either change state or return information, not both.
- **Commit** (7.4): Making all of a transaction's changes permanent at once.
- **Common table expression** (7.2): A named subquery introduced with WITH at the start of a statement. Often shortened to CTE.
- **CommonJS** (8.4): Node's original module system, with require and module.exports.
- **Compaction** (2.7): Sliding live objects together to remove gaps, and updating references to match.
- **Compare-and-swap** (5.2): An atomic operation that writes a new value only if the location still holds an expected value.
- **Comparer** (3.7): An object or function that decides the order of two items.
- **Comparison sort** (3.7): A sort that works only by comparing pairs of items. At best O(n log n).
- **Compensating action** (12.3): A step that semantically undoes an earlier step of a saga, such as a refund.
- **Competing consumers** (8.7): Several workers reading one queue and sharing its messages.
- **Compile-time constant** (2.4): A value fixed at compile time and copied into every place it is used. Declared with const.
- **Compiler** (2.2): A program that translates source code ahead of time into another form, usually machine code or bytecode.
- **Complete binary tree** (3.5): A binary tree where every level is full except possibly the last, which fills from the left.
- **Component** (9.4): A reusable piece of interface with inputs, outputs, markup and behaviour.
- **Component template** (9.6): The HTML with bindings that describes an Angular component's view.
- **Component tree** (9.4): The hierarchy of components that make up an interface.
- **Composite index** (7.3): An index on several columns, sorted by the first, then the second, and so on.
- **Compositing** (9.1): Combining painted layers into the final image, usually on the graphics processor.
- **Composition** (4.3): Building behaviour by giving an object other objects to work with.
- **Composition over inheritance** (4.3): The guideline to prefer combining objects over deriving classes when sharing behaviour.
- **Composition root** (4.3): The single place near the entry point where all objects are created and wired together.
- **Concurrency** (5.1): Structuring a program so that several tasks can be in progress at the same time.
- **Concurrency token** (7.7): A property checked in every update and delete, so conflicting changes are detected.
- **Concurrent collection** (5.2): A collection designed for safe use by many threads at once.
- **Conditional request** (6.6): A request that asks for a resource only if it has changed, answered with 304 when it hasn't.
- **Conditional type** (9.3): A type chosen by a test on another type, written with extends and ?.
- **Confidentiality** (10.1): Only authorized people and systems can read the data.
- **ConfigureAwait** (5.3): A way to tell an await whether it needs to resume on the original synchronization context.
- **Congestion control** (6.3): The sender limiting its rate to what the network in between can carry.
- **Connected component** (3.6): A group of vertices that can all reach each other and nothing outside the group.
- **Connection pool** (6.3): A set of open connections kept ready and reused, skipping setup costs.
- **Connection string** (7.1): Text telling a driver which server and database to connect to and how to log in.
- **Consensus** (12.2): Getting several machines to agree on a value despite failures and lost messages.
- **Consistency** (7.4): A transaction moves the database from one valid state to another, respecting its constraints.
- **Consistency model** (12.2): The guarantees a replicated system gives about what reads can see.
- **Consistent hashing** (3.3): Placing keys and servers on a circle so that adding a server moves only a few keys.
- **Constant time** (3.1): O(1). The cost doesn't depend on the input size.
- **Constant-time comparison** (10.5): Comparing values in a way that always takes the same time, so timing reveals nothing.
- **Constructor injection** (4.3): Receiving dependencies as constructor parameters.
- **Consumer group** (12.3): A set of consumers sharing one position in an event stream.
- **Container** (1.7): An ordinary process isolated with namespaces and limited with control groups.
- **Container image** (11.3): A read-only package of a program's filesystem and settings, from which containers are started.
- **Container orchestration** (11.3): Automatically running, healing, scaling and updating containers across many machines.
- **Container registry** (11.3): A service that stores and serves container images.
- **Content Security Policy** (10.4): A response header telling the browser which sources of scripts and other content a page may use. Often shortened to CSP.
- **Contention** (5.2): Several threads competing for the same lock or memory location.
- **Context switch** (1.7): Saving one thread's state and loading another's, so a core can run a different thread.
- **Context window** (13.1): Everything a model can see when generating, up to a fixed number of tokens.
- **Contiguous memory** (3.2): One unbroken block of memory, with elements next to each other.
- **Continuation** (5.3): The code that runs after an awaited task completes.
- **Continuous delivery** (11.4): Keeping every change that passes the pipeline ready to release at any time.
- **Continuous deployment** (11.4): Releasing every change that passes the pipeline to production automatically.
- **Continuous integration** (11.4): Merging small changes into main frequently, with every change built and tested automatically.
- **Contract** (4.2): Everything callers may rely on about a method or type, beyond its signature.
- **Control flow** (2.5): The order in which a program's statements run: sequence, choice and repetition.
- **Controlled input** (9.4): A form field whose value is held in component state and displayed from it.
- **Controller** (8.3): A class whose action methods handle HTTP requests, with attributes for routing.
- **Conway's law** (12.1): Organizations design systems that mirror their own communication structures.
- **Cookie** (6.6): A small value a server asks the browser to store and send back on later requests.
- **Copy-on-write** (4.4): Sharing data until the first change, then copying it all.
- **Core Web Vitals** (9.1): Three measures of how a page feels to users: loading, responsiveness and visual stability.
- **Correlation id** (8.5): An identifier attached to a request and passed through every system and log line it touches.
- **CORS** (9.7): Cross-Origin Resource Sharing: headers with which a server lets other origins read its responses.
- **Counter** (11.6): A metric that only increases, such as requests served.
- **Counting sort** (3.7): Sorting small integer keys by counting each value's occurrences. O(n + k).
- **Coupling** (4.5): How much one module depends on the details of another. Keep it low.
- **Covering index** (7.3): An index containing every column a query needs, so the table isn't read.
- **CPU** (1.4): The processor. It runs instructions one small step at a time.
- **CPU-bound** (5.3): Work whose speed is limited by the processor.
- **CQRS** (12.3): Command query responsibility segregation: separating the model that changes data from the models that read it.
- **Crash recovery** (7.1): Replaying and undoing logged changes after a crash so only committed work remains.
- **Credential** (10.2): Something a user presents to prove who they are, such as a password or a key.
- **Credential stuffing** (10.1): Trying username and password pairs leaked from other sites.
- **Critical rendering path** (9.1): The sequence of steps and resources needed before the browser can show the first pixels.
- **Critical section** (5.1): Code that changes shared data and must not run in more than one thread at a time.
- **CRLF** (background; covered in 1.3): Carriage return then line feed: the two-character line ending used by Windows and in HTTP headers.
- **Cron expression** (8.7): A compact format describing a schedule, such as 0 2 * * * for every day at 02:00.
- **Cross-site request forgery** (10.4): Making a user's browser send an unwanted, authenticated request to a site from another site. Often shortened to CSRF.
- **Cross-site scripting** (10.4): Injecting script into a web page so it runs in other users' browsers as part of the site. Often shortened to XSS.
- **Cryptographic hash** (10.5): A one-way function that turns data into a fixed-size fingerprint that changes completely if the data changes.
- **Cryptographically secure random number generator** (10.5): A generator whose output can't be predicted, suitable for keys and tokens. Often shortened to CSPRNG.
- **CSP**: see **Content Security Policy** (10.4)
- **CSPRNG**: see **Cryptographically secure random number generator** (10.5)
- **CSRF**: see **Cross-site request forgery** (10.4)
- **CSS** (9.1): Cascading Style Sheets: the language that describes a page's presentation and layout.
- **CSV** (background): Comma-separated values: a plain text table with one record per line and fields separated by commas.
- **CTE**: see **Common table expression** (7.2)
- **Cursor** (8.2): An opaque token marking where a page of results ended.
- **Cursor pagination** (8.2): Paging by passing back the cursor of the previous page, rather than a page number.
- **Custom hook** (9.5): A function starting with use that combines other hooks to share stateful logic.
- **Cycle** (3.6): A path that returns to its starting vertex.

### D

- **DAG**: see **Directed acyclic graph** (3.6)
- **Data parallelism** (5.5): Applying the same operation to many pieces of data at once.
- **Data race** (5.1): Two threads accessing the same memory at once, at least one writing, with nothing ordering them.
- **Data warehouse** (7.1): A database designed for analysis, usually loaded with copies of operational data.
- **Data-oriented design** (4.1): Organizing code around the layout and bulk processing of data, typically arrays of plain values.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Database index** (7.3): A separate sorted structure, usually a B+ tree, holding some columns with pointers back to rows.
- **Database management system** (7.1): The software that manages a database, such as SQL Server or PostgreSQL. Often shortened to DBMS.
- **Database normalization** (7.5): Structuring tables so that each fact is stored in exactly one place.
- **Database page** (7.1): A fixed-size block, typically 4 or 8 KB, in which a database stores rows and indexes.
- **Database per service** (12.1): The rule that each service or module owns its data and nothing else touches it directly.
- **Database statistics** (7.3): Summaries of the data in columns, used by the optimizer to estimate costs.
- **Datagram** (6.3): One self-contained UDP message.
- **DbContext** (7.7): EF Core's gateway to the database: a unit of work with a change tracker.
- **DBMS**: see **Database management system** (7.1)
- **DDD**: see **Domain-driven design** (12.1)
- **Dead-letter queue** (8.7): A queue for messages that failed too many times, kept for inspection and replay.
- **Deadlock** (5.2): Threads each waiting for something another holds, so none can continue.
- **Declaration file** (9.3): A .d.ts file describing a JavaScript module's types, with no code.
- **Declarative programming** (2.1): Describing the result you want and leaving the steps to the system. SQL is declarative.
- **Declarative UI** (9.4): Describing what the interface should show for the current state, and letting a framework update the DOM.
- **Deconstruction** (2.5): Taking a tuple or record apart into separate variables.
- **Decorator pattern** (4.3): Wrapping an object in another with the same interface to add behaviour around each call.
- **Decrease-key** (3.5): Lowering an item's priority while it stays in the heap. Not supported directly by PriorityQueue.
- **Default gateway** (6.2): The router a machine sends packets to when they are for another network.
- **Default interface method** (4.2): A method with a body declared inside an interface, inherited by implementing types.
- **Default route** (6.2): The route for every destination not matched by anything more specific: 0.0.0.0/0.
- **Default value** (2.4): The value a field or array element starts with: zero, false or null.
- **Defence in depth** (10.1): Layering independent protections so that one failing doesn't expose the system.
- **Defensive copy** (2.4): A copy handed out instead of internal data, so callers can't change the original.
- **Deferrable view** (9.6): A @defer block whose content and code are loaded later, on a trigger.
- **Deferred execution** (2.5): Building a query without running it. The work happens only when it is enumerated.
- **Definite assignment** (2.4): The C# rule that a local variable must be assigned on every path before it is read.
- **Delegate** (2.5): A type for references to methods, such as `Func`.
- **Delegation** (4.3): Passing a call on to an object you hold instead of handling it yourself.
- **Deliberate practice** (13.2): Practising just beyond your current ability, with feedback, to build skill.
- **Denormalization** (7.5): Deliberately duplicating data to make reads cheaper.
- **Dependency array** (9.5): The list of values whose change makes an effect run again.
- **Dependency confusion** (11.2): Getting a malicious public package installed in place of a private package with the same name.
- **Dependency injection** (2.6): Giving a class the objects it depends on from outside, usually through its constructor.
- **Dependency inversion principle** (4.5): High-level policy and low-level details should both depend on abstractions owned by the policy.
- **Deploy** (background; covered in 11.4): To install a new version of software where it will run, such as on production servers.
- **Deployment environment** (11.4): A place the software runs, such as staging or production, with its own settings, secrets and rules.
- **Depth-first search** (3.6): Exploring a graph as deep as possible before backing up, using a stack.
- **Deque** (3.2): A double-ended queue, with fast adding and removing at both ends.
- **Derived state** (9.4): Values computed from other state rather than stored.
- **Design pattern** (4.3): A named, reusable shape for solving a recurring design problem.
- **Design system** (9.4): A shared set of components, styles and rules used across an organization's products.
- **Destructuring** (9.2): Unpacking values from objects or arrays into variables, as in const { number } = flight.
- **Deterministic cleanup** (2.6): Releasing a resource at a known point in the code, rather than whenever the garbage collector runs.
- **Deterministic test** (4.6): A test that gives the same result on every run.
- **Devirtualization** (1.4): Replacing a virtual or interface call with a direct call once the actual type is known.
- **DHCP** (6.2): The protocol a device uses to get an address and network settings when it joins a network.
- **DI**: see **Dependency injection** (2.6)
- **DI container** (4.3): A library that creates objects and their dependencies from registrations.
- **Digital signature** (6.5): A value made with a private key that proves who signed some data and that it hasn't changed.
- **Dijkstra's algorithm** (3.6): Finding shortest paths with non-negative weights by always expanding the closest unvisited vertex.
- **Directed acyclic graph** (3.6): A directed graph with no cycles. Often shortened to DAG.
- **Directed graph** (3.6): A graph whose edges each point one way.
- **Dirty read** (7.4): Reading another transaction's changes before they are committed.
- **Disaster recovery** (12.5): The plans and tools for restoring service after a large failure, such as a lost region.
- **Discriminated union** (2.3): A type that is exactly one of a fixed set of cases, each with its own data.
- **Distributed cache** (8.6): A cache shared by all instances of an application, running as a separate service such as Redis.
- **Distributed lock** (8.7): A lock held in a shared store so that only one instance across many does something.
- **Distributed monolith** (12.1): Services that still can't change or deploy independently, combining the costs of both styles.
- **Distributed system** (12.2): Programs on several machines cooperating over a network to behave as one system.
- **Distributed tracing** (11.6): Following requests across service boundaries by passing trace context between them.
- **Distributed transaction** (7.4): A transaction that commits across several systems at once, using two-phase commit.
- **Distroless image** (11.3): A minimal base image with only what a program needs, without a shell or package manager.
- **Divide and conquer** (3.8): Splitting a problem into independent parts, solving each, and combining the results.
- **DLL** (background; covered in 2.2): Dynamic-link library: a file of compiled code that programs load when they run. .NET assemblies are usually .dll files.
- **DNS** (6.4): Domain Name System: the distributed directory that turns names into addresses and other records.
- **DNS delegation** (6.4): Passing responsibility for part of the DNS tree down to other servers.
- **DNS record** (6.4): One entry in a zone: a name, a type, a TTL and a value.
- **DNSSEC** (6.4): Digital signatures on DNS records, so forged answers can be detected.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.
- **Docker Compose** (11.3): A tool for defining and running several containers together from one YAML file.
- **Dockerfile** (11.3): A file of instructions that builds a container image.
- **Document database** (7.6): A store that keeps records as nested JSON-like documents.
- **DOM** (9.1): Document Object Model: the tree of objects a browser builds from a page's HTML.
- **Domain event** (12.3): A fact inside one bounded context, which its own code reacts to.
- **Domain name** (6.4): A name such as www.example.com, made of labels read from right to left.
- **Domain-driven design** (12.1): An approach to software design built around the business domain and its language. Often shortened to DDD.
- **Domain-specific language** (2.1): A language made for one kind of problem, such as SQL or regular expressions. Often shortened to DSL.
- **Dominant term** (3.1): The part of a cost formula that grows fastest and swamps the rest for large inputs.
- **DORA metrics** (11.4): Deployment frequency, lead time for changes, change failure rate and time to restore service.
- **Doubling experiment** (3.1): Timing code at sizes n, 2n, 4n and so on to estimate its complexity from how the time grows.
- **DRY** (4.5): Don't repeat yourself: each piece of knowledge should live in one place.
- **DSL**: see **Domain-specific language** (2.1)
- **DTO** (8.5): Data transfer object: a type made to carry exactly the data of one request or response.
- **Duck typing** (4.2): Accepting any object that has the needed members, regardless of its declared type.
- **Durability** (1.6): The guarantee that saved data survives a crash or power cut.
- **Dynamic array** (3.2): A growable list built on an array with spare room. List<T> is one.
- **Dynamic dispatch** (4.1): Choosing which method implementation to run based on an object's actual type at run time.
- **Dynamic linking** (2.2): Loading a shared library when a program starts, instead of copying it into the executable.
- **Dynamic programming** (3.8): Solving a problem by combining stored answers to overlapping smaller subproblems.
- **Dynamic typing** (2.3): Checking types while code runs, when each operation is attempted.

### E

- **Eager loading** (7.7): Loading related data in the same query, with Include.
- **EASA** (background): European Union Aviation Safety Agency: the EU's aviation regulator.
- **ECMA** (background): Ecma International, a standards body that publishes the standards for C# and JavaScript.
- **ECMAScript** (9.2): The official standard that defines JavaScript, with a new edition every year.
- **Edge** (3.6): A connection between two vertices, possibly with a direction and a weight.
- **Edit distance** (3.8): The fewest single-character insertions, deletions and substitutions between two strings.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **Egress** (11.5): Data leaving a provider's network, or crossing regions, which is usually charged.
- **Elasticity** (11.5): The ability to grow and shrink resources quickly with demand.
- **Embarrassingly parallel** (5.5): A problem whose pieces are completely independent and need no coordination.
- **Embedded database** (7.1): A database that runs as a library inside the program, storing data in a local file.
- **Encapsulation** (4.1): Hiding an object's data behind its methods so that its rules can't be bypassed.
- **Encoding** (1.3): A table of characters plus a rule for turning them into bytes.
- **Encrypted DNS** (6.4): DNS lookups sent over encrypted connections, as DNS over HTTPS or DNS over TLS.
- **Encryption** (6.5): Turning plaintext into ciphertext using a key, so only key holders can read it.
- **End-to-end test** (4.6): A test that drives the whole system the way a user would.
- **Endianness** (1.1): The order in which the bytes of a multi-byte number are stored. Little-endian puts the lowest byte first.
- **Endpoint** (6.3): An IP address and port together, such as 10.0.0.5:5432.
- **Endpoint filter** (8.3): Code that runs before and after the handler of one endpoint or group.
- **Entity** (7.5): A kind of thing the business deals with, usually stored as a table.
- **Enumerable** (2.5): Anything that can be walked through with foreach, by providing a GetEnumerator method.
- **Envelope encryption** (10.5): Encrypting data with a data key, and encrypting that data key with a master key kept in a vault.
- **Environment variable** (1.7): A named text value a process inherits from whatever started it.
- **Ephemeral port** (6.3): A temporary port the operating system gives a client for one connection.
- **Epoch** (1.2): The starting point a date or time is counted from, such as 1 January 1970 for Unix time.
- **Equality** (2.4): Whether two values should be treated as equal, even if they are separate objects.
- **Equality comparer** (3.3): An object supplying the hash code and equality check a collection uses for keys.
- **Erasable syntax** (8.4): TypeScript syntax that leaves valid JavaScript when the types are removed.
- **Error budget** (12.5): The failure an SLO allows, such as 0.1% of requests, spent on change and risk.
- **Error code** (2.8): A special return value that signals failure, such as −1 or null.
- **Error contract** (8.5): The consistent, documented shape and meaning of the errors an API returns.
- **ES module** (8.4): A module using the standard JavaScript import and export syntax.
- **Escaping** (1.3): Rewriting special characters so a target format reads them as plain text.
- **ETag** (6.6): An identifier for one version of a resource, used to check whether a cached copy is still current.
- **Event** (12.3): A message stating a fact that has already happened, for any number of listeners.
- **Event binding** (9.6): Running code when a DOM event fires, written (event)="statement".
- **Event delegation** (9.1): Handling events for many elements with one listener on a common parent.
- **Event loop** (5.4): A loop that takes ready callbacks from a queue and runs them one at a time, waiting for events when idle.
- **Event loop lag** (5.4): How late callbacks run compared with when they should, a measure of a busy loop.
- **Event notification** (12.3): A small event saying something changed, with an id, so consumers fetch details themselves.
- **Event sourcing** (12.3): Storing every change as an event and computing state by replaying them.
- **Event-carried state transfer** (12.3): An event that includes the data consumers need, so they don't call back.
- **Event-driven architecture** (12.3): A design where parts of a system react to each other's events instead of calling each other.
- **Eventual consistency** (7.6): The guarantee that copies converge once writes stop, though they may differ for a while.
- **Eviction** (8.6): Removing entries because the cache is full.
- **Exactly-once processing** (8.7): Getting each message's effect exactly once, through at-least-once delivery and duplicate detection.
- **Exception** (2.8): An object describing a failure, thrown to stop the current method and travel up the call stack.
- **Exception filter** (2.8): A `when` condition on a catch block that decides, before unwinding, whether it applies.
- **Exclusive lock** (7.4): A lock for writing that only one transaction can hold, keeping all others out.
- **Execution strategy** (7.7): EF Core's mechanism for retrying operations after transient database failures.
- **Exhaustiveness check** (9.3): Using never to make the compiler reject a switch that misses a case of a union.
- **Exit code** (1.7): The number a process returns when it ends. Zero means success.
- **Expand-contract** (7.5): Changing a live schema in steps: add the new structure, move data and code over, then remove the old.
- **Explicit interface implementation** (4.2): Implementing an interface member so that it is only visible through the interface.
- **Exponential backoff** (2.8): Waiting longer between each retry, typically doubling the delay each time.
- **Exponential time** (3.1): O(2ⁿ). Each extra item doubles the work.
- **Express** (8.4): The oldest and most widely used Node.js web framework, built around middleware.
- **Expression** (0.1): Any piece of code that produces a value, such as `2 + 2` or `name.Length`.
- **Expression problem** (4.1): The trade-off between making new data types easy to add and making new operations easy to add.
- **Expression tree** (3.4): A data structure describing a lambda's code, which libraries like EF Core translate into SQL.
- **Expression-bodied member** (0.1): A method or property written with `=>` and a single expression instead of a block.
- **Extension method** (2.5): A static method that can be called as if it were an instance method of another type.
- **External sort** (3.7): Sorting data too large for memory by sorting chunks on disk and merging them.

### F

- **Factory** (4.3): An object or function that creates other objects, often combining injected services with run-time values.
- **Fail fast** (2.8): Stopping immediately and loudly when something is badly wrong, instead of carrying on.
- **Fail secure** (10.1): Refusing, rather than allowing, when something goes wrong.
- **Failover** (12.5): Switching work from a failed component to a standby or replica.
- **Fake** (4.6): A working but simplified implementation, such as an in-memory repository.
- **Fallacies of distributed computing** (6.1): Eight false assumptions about networks, such as "the network is reliable" and "latency is zero".
- **Fallback** (12.5): What to do instead when a dependency fails, such as using cached data.
- **False sharing** (1.5): Threads slowing each other down by writing different variables in the same cache line.
- **Falsy** (9.2): A value that counts as false: false, 0, "", null, undefined or NaN.
- **Fast-forward merge** (11.1): A merge that just moves a branch label forward, because only one side had new commits.
- **Fastify** (8.4): A fast Node.js web framework organized around plugins and JSON schemas.
- **Feature flag** (11.4): A run-time switch that turns functionality on or off without redeploying.
- **Fencing token** (12.2): An increasing number issued with each lease, used by storage to reject stale holders' writes.
- **Fetch API** (9.7): The browser's built-in function for making HTTP requests, returning a promise of a response.
- **FIFO** (3.2): First in, first out. The order in which a queue gives items back.
- **File descriptor** (1.7): A number a process uses to refer to an open file, pipe or network connection. Windows calls it a handle.
- **File system** (1.6): The scheme that maps file names to the places on a device where their bytes live.
- **Finalizer** (2.7): A method that runs before an object's memory is reclaimed, as a safety net for native resources.
- **Fine-grained reactivity** (9.4): Tracking exactly which values each piece of UI reads, and updating only those pieces on change.
- **Fine-tuning** (13.1): Further training of a pretrained model on narrower examples, such as instructions and good answers.
- **Fire and forget** (5.3): Starting a task without awaiting it or handling its outcome.
- **Firewall** (6.2): Software or hardware that allows or blocks traffic according to rules.
- **First-class function** (4.1): A function that can be stored, passed and returned like any other value.
- **Flag** (1.1): A single bit used as a yes/no setting, often packed with others into one number.
- **Flags register** (1.4): A register whose bits record facts about the last result, such as zero or overflow.
- **Flaky test** (4.6): A test that passes and fails at random without any code change.
- **Floating point** (1.2): Scientific notation in base 2: a sign, an exponent and a fraction. Fast, but most decimal fractions are approximate.
- **Flow control** (6.3): The receiver limiting how much unacknowledged data the sender may have in flight.
- **Foreground thread** (5.2): A thread that keeps the process alive until it finishes.
- **Foreign key** (7.2): A column that refers to the primary key of another table.
- **Forward secrecy** (6.5): The property that stealing a long-term key later doesn't reveal past recorded traffic.
- **Fragile base class problem** (4.3): Safe-looking changes in a base class breaking derived classes that depended on its internals.
- **Frame** (6.1): A unit of data at the link layer.
- **Frame budget** (9.1): The time available to produce each frame, about 16.7 ms at 60 frames per second.
- **Framework** (background): A larger body of ready-made code that calls your code, giving an application its overall structure, such as ASP.NET Core.
- **Frontend** (8.1): The part of a system that runs on the user's device and presents it to people.
- **Frozen collection** (3.3): A read-only set or dictionary, optimized for its exact keys when it is created.
- **fsync** (1.6): The Linux call that pushes a file's data from the OS cache to the device.
- **Function** (2.5): A named, reusable piece of code that takes parameters and may return a value.
- **Functional core** (4.4): The pure part of a program that makes the decisions, taking and returning plain data.
- **Functional programming** (4.1): Building programs from functions that take values and return new values, avoiding changes to data.

### G

- **Garbage collection** (2.7): Automatically finding and freeing memory the program can no longer use.
- **Gauge** (11.6): A metric holding a current value that rises and falls, such as queue length.
- **GC**: see **Garbage collection** (2.7)
- **GC root** (2.7): A reference the program can use directly, such as a local variable in a running method or a static field.
- **GDPR** (background; covered in 10.1): General Data Protection Regulation: the EU law on how personal data is collected, stored and protected.
- **Generation** (2.7): A group of objects of similar age. .NET uses generations 0, 1 and 2.
- **Generational hypothesis** (2.7): The observation that most objects die young, which generational collectors exploit.
- **Generator function** (9.2): A function written with function* that produces values lazily with yield.
- **Generic host** (8.3): The .NET component that owns configuration, logging, dependency injection and app lifetime.
- **Generic math** (1.2): Number interfaces such as `INumber` that let one method work with every number type.
- **Generics** (2.3): Types and methods written once with type parameters, such as `List`.
- **Git** (11.1): The distributed version control system used almost everywhere, created in 2005.
- **Git branch** (11.1): A movable label pointing at a commit, moved forward by each new commit on it.
- **Git commit** (11.1): A snapshot of the whole project, with its parent commits, author, date and message, named by its hash.
- **Git remote** (11.1): A named link to another copy of the repository, usually called origin.
- **Git repository** (11.1): A project's files together with their complete Git history.
- **Global query filter** (7.7): A condition EF Core adds automatically to every query of an entity type.
- **Graceful degradation** (12.5): Offering a reduced service instead of failing completely.
- **Grammar** (2.1): The formal set of syntax rules for a language.
- **Graph** (3.6): A set of vertices connected by edges.
- **Graph database** (7.6): A store that keeps vertices and edges as data and queries paths between them.
- **Grapheme** (1.3): What a person sees as one character. It can be several code points. C# calls it a text element.
- **GraphQL** (8.2): An API style where clients query a typed schema for exactly the fields they want.
- **Greedy algorithm** (3.8): An algorithm that always takes the locally best choice and never reconsiders it.
- **Growth factor** (3.2): How much a dynamic array multiplies its capacity by when it fills up. 2 for List<T>.
- **gRPC** (8.2): A high-performance RPC framework using Protocol Buffers over HTTP/2.
- **Guard clause** (2.5): A check at the start of a method that returns or throws early, so the rest handles only the normal case.
- **GUID** (background; covered in 7.5): Globally unique identifier: a 128-bit value used as an id, written as 32 hexadecimal digits. Also called a UUID.
- **Gustafson's law** (5.5): The observation that larger problems shrink the serial fraction, so parallelism scales better with data size.

### H

- **Hallucination** (13.1): Fluent, plausible output that is false, such as an invented method or package.
- **Happens-before** (5.1): A guarantee that everything done before a synchronizing operation is visible after it on another thread.
- **Hash code** (2.4): A number computed from a value, used by hash-based collections to decide where to store it.
- **Hash flooding** (3.3): An attack that sends many keys chosen to collide, making hash tables slow.
- **Hash function** (3.3): A function that turns a key into a number, giving equal keys equal numbers and spreading different keys evenly.
- **Hash table** (3.3): A structure that stores entries in buckets chosen by hashing their keys, giving O(1) lookups on average.
- **HDD** (1.6): Hard disk drive. Stores data on spinning magnetic platters.
- **HEAD** (11.1): Git's reference to what is currently checked out, usually a branch.
- **Head-of-line blocking** (6.3): Later data waiting behind one lost piece, because delivery must stay in order.
- **Header** (6.1): Fixed-format information at the front of a packet or message, used by the network or protocol.
- **Headroom** (12.4): Spare capacity kept for spikes, failures and latency.
- **Health check** (8.1): An endpoint that reports whether an instance is working, for load balancers and orchestrators.
- **Heap** (1.5): A large memory area for data whose lifetime isn't tied to one method call.
- **Heap property** (3.5): The rule that every parent is ordered before its children, so the best item is at the root.
- **Heap sort** (3.5): Sorting by building a heap and repeatedly moving its top to the end. O(n log n) and not stable.
- **Heap table** (7.3): A table stored with no particular row order.
- **Heapify** (3.5): Building a heap from an unordered array in O(n) by sifting down every parent.
- **Height** (3.4): The number of nodes on the longest path from a tree's root to a leaf.
- **Heisenbug** (5.1): A bug that disappears or changes when you try to observe it, typical of timing problems.
- **Heuristic** (3.6): An estimate that guides a search, such as straight-line distance to the destination.
- **Hexadecimal** (1.1): Base 16, written with 0–9 and A–F. Two hex digits make one byte.
- **Higher-order function** (2.5): A function that takes or returns other functions. Most of LINQ is built from them.
- **Histogram** (11.6): A metric that counts measurements into value ranges, from which percentiles can be computed.
- **Hit ratio** (8.6): The share of lookups that are hits.
- **HMAC** (10.5): A message authentication code built from a hash function, such as HMAC-SHA256.
- **Hook** (9.5): A function starting with use that gives a component state or other React features.
- **Horizontal scaling** (7.6): Handling more load by adding more machines, each doing part of the work.
- **Hosted service** (8.7): A class the .NET host starts with the app and stops when the app shuts down.
- **Hosts file** (6.4): A small local file that maps names to addresses, checked before DNS.
- **Hot partition** (7.6): A shard or partition that receives far more load than the others.
- **HSTS** (6.5): HTTP Strict Transport Security: a header telling browsers to always use HTTPS for a site.
- **HTML** (9.1): HyperText Markup Language: the language that describes a page's content and structure.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **HTTP header** (6.6): A named value in a request or response, such as Content-Type.
- **HTTP method** (6.6): The action a request asks for, such as GET, POST, PUT or DELETE.
- **HTTP request** (6.6): A message from client to server: method, path, headers and an optional body.
- **HTTP response** (6.6): A message from server to client: status code, headers and an optional body.
- **HTTP/2** (6.6): A binary version of HTTP that runs many requests at once over one connection.
- **HTTP/3** (6.6): HTTP carried over QUIC.
- **HTTPS** (6.5): HTTP carried over TLS, normally on port 443.
- **Human in the loop** (13.2): Keeping a person responsible for reviewing and approving an automated system's important actions.
- **HybridCache** (8.6): .NET's two-level cache, combining in-memory and distributed levels with built-in stampede protection.
- **Hydration** (9.1): Attaching client-side JavaScript to server-rendered HTML so the page becomes interactive.

### I

- **I/O-bound** (5.3): Work whose speed is limited by waiting for devices or networks.
- **IaaS** (11.5): Infrastructure as a service: renting virtual machines, disks and networks.
- **IaC**: see **Infrastructure as code** (11.5)
- **IATA** (background): International Air Transport Association: the airline trade body that assigns airport codes such as LHR and airline codes such as BA.
- **ID token** (10.3): A JWT that tells the client app who signed in. Not for calling APIs.
- **IDE** (background): Integrated development environment: an editor with building, debugging and code help built in, such as Visual Studio or Rider.
- **Idempotency key** (8.2): A unique value per logical operation that lets a server recognize and safely answer a repeated request.
- **Idempotent** (2.8): Having the same effect whether done once or several times, which makes an operation safe to retry.
- **Idempotent consumer** (8.7): A consumer that gives the same result whether a message arrives once or several times.
- **Identity** (2.4): Whether two references point to the same object.
- **Identity provider** (10.3): A service whose job is signing users in and issuing proof of who they are.
- **IDisposable** (2.6): The .NET interface with a Dispose method that releases a resource.
- **IEEE** (background): The Institute of Electrical and Electronics Engineers, a standards body; its IEEE 754 standard defines floating-point numbers.
- **IIS** (background; covered in 8.3): Internet Information Services: Microsoft's web server for Windows, which hosted classic ASP.NET.
- **IL**: see **Intermediate language** (2.2)
- **Image digest** (11.3): The hash of an image's content, which always identifies exactly the same image.
- **Image layer** (11.3): A content-addressed set of file changes; images are stacks of layers.
- **Image tag** (11.3): A movable, human-readable label for an image version, such as 1.4.2.
- **Immutable** (1.3): Unable to change after creation. C# strings are immutable.
- **Immutable collection** (3.2): A collection that can't be changed after creation. "Changing" it returns a new one.
- **Imperative programming** (2.1): Describing a computation as a sequence of steps that change state.
- **Imperative shell** (4.4): The thin outer layer that does I/O and carries out the core's decisions.
- **In-order traversal** (3.4): Visiting the left subtree, then the node, then the right subtree. Gives sorted order on a binary search tree.
- **Incident** (12.5): An unplanned disruption that affects users or threatens to.
- **Index** (3.2): The position of an element in an array or list, counting from zero.
- **Index seek** (7.3): Walking an index from its root straight to the matching rows.
- **Information hiding** (4.2): Splitting a program so that each module hides a design decision likely to change.
- **Infrastructure as code** (11.5): Defining infrastructure in versioned files that tools apply repeatably. Often shortened to IaC.
- **Inheritance** (4.1): A class taking over and extending the members of a base class.
- **Injection** (10.4): Untrusted data inserted into something an interpreter parses as code, such as SQL or a shell command.
- **Inlining** (1.4): Copying a called method's body into its caller, removing the call.
- **Inner exception** (2.8): The original exception stored inside a new one, preserving the underlying cause.
- **Inner join** (7.2): A join that keeps only rows with a match on both sides.
- **Input validation** (8.5): Checking that incoming data is acceptable before using it.
- **Insecure direct object reference** (10.2): Returning or changing a record by the id in a request without checking the caller may access it.
- **Insertion sort** (3.7): Sorting by sliding each item left into place among the already-sorted items.
- **Instruction** (1.4): One tiny operation the processor understands, such as load, add, compare or jump.
- **Instruction set architecture** (1.4): The set of instructions a processor family understands, such as x86-64 or ARM64.
- **Integration event** (12.3): A stable, versioned event published for other bounded contexts.
- **Integration test** (4.6): A test that checks pieces working together with real infrastructure such as a database.
- **Integrity** (10.1): Data and systems can't be changed without permission, and changes can be detected.
- **Interceptor** (9.6): A function that processes every HTTP request and response in an Angular app.
- **Interface** (4.2): A named contract listing members that implementing types must provide.
- **Interface segregation principle** (4.5): Clients shouldn't depend on interface members they don't use.
- **Interleaving** (5.1): The order in which the steps of several threads end up running relative to each other.
- **Intermediate language** (2.2): The bytecode .NET compilers produce. Also called IL, MSIL or CIL.
- **Internet layer** (6.1): The layer that moves packets between networks using IP addresses and routers.
- **Interpreter** (2.2): A program that carries out source code or bytecode directly while it runs.
- **Interrupt** (1.4): A signal from a device or timer that makes the processor pause and run a kernel handler.
- **Introsort** (3.7): Quicksort that switches to heap sort when recursion gets deep and to insertion sort for small ranges. Used by Array.Sort.
- **Invariant** (4.2): Something that is always true of an object between method calls.
- **Inversion of control** (4.3): Letting outside code decide which implementations a class works with, instead of the class itself.
- **Inverted index** (3.7): A map from each word to the documents containing it, used by search engines.
- **IP** (background; covered in 6.2): Internet Protocol: the protocol that addresses packets and routes them between networks.
- **IP address** (6.2): The address of a network interface on an IP network.
- **IPv4** (6.2): The original Internet Protocol, with 32-bit addresses written like 192.168.1.20.
- **IPv6** (6.2): The newer Internet Protocol, with 128-bit addresses written like 2001:db8::1.
- **ISO** (background): The International Organization for Standardization, which publishes standards such as ISO 8601 for writing dates and times.
- **Isolation** (7.4): Concurrent transactions don't see or disturb each other's unfinished work.
- **Isolation level** (7.4): How strictly a database isolates concurrent transactions, trading safety for speed.
- **ISP** (background; covered in 6.2): Internet service provider: a company that connects homes and businesses to the internet.
- **Issuer** (10.3): The authority that created a token, in its iss claim.
- **Iterator** (2.5): A method that uses yield return to hand out items one at a time, pausing between them.

### J

- **Jagged array** (3.2): An array of separate arrays, where each row can have its own length.
- **Java** (background): A statically typed language similar to C#, running on the Java Virtual Machine, widely used for business systems.
- **JavaScript** (9.2): The programming language of the web, also used on servers through Node.js.
- **JIT**: see **JIT compiler** (2.2)
- **JIT compiler** (2.2): A compiler that turns bytecode into machine code while the program runs, just before it is needed.
- **Jitter** (2.8): A random amount added to retry delays so that many clients don't retry at the same moment.
- **Join** (7.2): Combining rows from two tables by matching them on a condition.
- **Journaling** (1.6): Writing a note of a change before making it, so a file system can recover from a crash.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **JSX** (9.5): A syntax for writing markup inside JavaScript or TypeScript, compiled into function calls.
- **Junction table** (7.5): A table of foreign key pairs that records a many-to-many relationship.
- **JVM** (background): Java Virtual Machine: the runtime for Java and related languages, with a JIT compiler and garbage collector like .NET's.
- **JWKS** (10.3): JSON Web Key Set: the published public keys an issuer signs tokens with.
- **JWT** (10.3): JSON Web Token: signed, base64url-encoded claims in three dot-separated parts.

### K

- **Kernel** (1.7): The core of the operating system, running with full control of the hardware.
- **Kestrel** (8.3): ASP.NET Core's built-in web server.
- **Key exchange** (6.5): A method for two parties to agree on a shared secret key over an open network.
- **Key lookup** (7.3): Following a nonclustered index's pointer into the table to fetch columns the index doesn't hold.
- **Key pair** (6.5): A public key and its matching private key.
- **Key vault** (10.5): A service that stores keys and secrets, often in hardware security modules, and controls and logs their use.
- **Key-value store** (7.6): A store that saves and fetches values by key, very fast, with little querying beyond that.
- **Keyed service** (4.3): A service registered under a key, so several implementations of one interface can be requested by name.
- **Keyset pagination** (7.3): Paging by asking for rows after the last key seen, instead of skipping rows with OFFSET.
- **KISS** (4.5): Keep it simple: prefer the simplest design that meets the need.
- **Knapsack problem** (3.8): Choosing items with weights and values to get the most value within a capacity.
- **Knowledge cutoff** (13.1): The date after which a model's training data contains nothing.
- **Kubernetes** (11.3): The dominant container orchestrator, which reconciles declared desired state across a cluster.
- **Kubernetes Deployment** (11.3): A Kubernetes object declaring how many replicas of a pod to run, and managing rolling updates.
- **Kubernetes probe** (11.3): A readiness, liveness or start-up check that Kubernetes runs against each container.
- **Kubernetes Service** (11.3): A stable name and address for a set of pods, spreading traffic across the ready ones.

### L

- **Lambda expression** (2.5): A function without a name, written inline, such as `x => x * 2`.
- **Lamport clock** (12.2): A logical clock where each process increments a counter per event and jumps past counters it receives.
- **Large language model** (13.1): A neural network trained on huge amounts of text and code to predict the next token. Often shortened to LLM.
- **Large object heap** (1.5): The area for .NET objects of 85,000 bytes or more. Collected only in full collections.
- **Last-write-wins** (12.2): Resolving conflicting writes by keeping the one with the latest timestamp.
- **Latency** (6.1): How long one piece of data takes to travel from one end to the other.
- **Latency budget** (8.1): The total time a request may take, divided among the work and calls it makes.
- **Law of Demeter** (4.5): A method should talk only to its close collaborators, not reach through them to others.
- **Layered architecture** (8.1): Organizing code by technical role, such as API, application, domain and infrastructure layers.
- **Layout** (9.1): Calculating the size and position of every box on the page. Also called reflow.
- **Layout thrashing** (9.1): Alternating style changes and layout reads, forcing a new layout each time.
- **Lazy deletion** (3.5): Adding an item again with a new priority and skipping stale entries when they come out.
- **Lazy loading** (7.7): Loading related data automatically the first time a navigation property is used.
- **LDAP** (background): Lightweight Directory Access Protocol: a protocol for querying directories of users and groups, such as Active Directory.
- **Leader election** (12.2): Choosing one node to coordinate a cluster, with a majority vote.
- **Leaf** (3.4): A tree node with no children.
- **Leaky abstraction** (4.2): An abstraction whose hidden details still affect the code that uses it.
- **Lease** (12.2): A lock that expires after a set time unless renewed.
- **Least privilege** (10.1): Giving every user, service and process only the permissions it needs.
- **Level-order traversal** (3.4): Visiting a tree level by level from the root, using a queue. Also called breadth-first.
- **LF** (background; covered in 1.3): Line feed: the single-character line ending used by Linux and macOS.
- **Library** (background): Ready-made code you call from your own program, such as a JSON parser or a database driver.
- **libuv** (5.4): The C library that provides Node.js's event loop and its thread pool.
- **Lifetime** (2.6): How long a variable's storage or an object actually exists while the program runs.
- **LIFO** (3.2): Last in, first out. The order in which a stack gives items back.
- **Lifting state up** (9.4): Moving shared state to the nearest common parent of the components that need it.
- **Linear search** (3.7): Checking items one by one until a match is found. O(n).
- **Linear time** (3.1): O(n). The cost grows in proportion to the input.
- **Linearizability** (12.2): The strongest consistency model: the system behaves as a single copy, with every read seeing the latest write.
- **Link layer** (6.1): The layer that moves frames across one local network, such as Ethernet or Wi-Fi.
- **Linked list** (3.2): A list of separate nodes, each holding a value and a reference to the next.
- **Linker** (2.2): A tool that joins compiled pieces and libraries into one executable.
- **LINQ** (background; covered in 2.5): Language Integrated Query: C# methods such as Where, Select and Count that filter and transform sequences of items.
- **Linter** (9.2): A tool that checks code for bugs and bad patterns without running it.
- **Liskov substitution principle** (4.5): A subtype must work wherever its base type is expected, keeping all of its promises.
- **List key** (9.5): A stable, unique key prop that identifies an item in a list across renders.
- **Literal type** (9.3): A type with exactly one value, such as "delayed" or 42.
- **Little's law** (12.4): The average number of items in a system equals the arrival rate times the average time each spends there.
- **Livelock** (5.2): Threads that keep reacting to each other and retrying without any making progress.
- **LLM**: see **Large language model** (13.1)
- **Load balancer** (6.6): A reverse proxy that spreads requests across several servers and avoids unhealthy ones.
- **Load factor** (3.3): The number of entries divided by the number of buckets.
- **Load shedding** (12.5): Rejecting some work quickly under overload so the rest stays fast.
- **Load test** (12.4): Driving a whole system with realistic traffic to measure its capacity and behaviour under load.
- **Local function** (0.1): A method declared inside other code, next to where it is used.
- **Local state** (9.4): State that belongs to one component, such as whether a menu is open.
- **Locality** (1.5): Using memory that is close together and in order. The key to cache-friendly code.
- **Lock** (5.2): A mechanism that lets only one thread at a time hold it, used to protect shared data.
- **Lock granularity** (5.2): How much data one lock protects.
- **Lock ordering** (5.2): Always taking multiple locks in the same fixed order, which prevents deadlock.
- **Lock-free** (5.2): An algorithm that coordinates threads with atomic operations instead of locks.
- **Lockfile** (8.4): A file recording the exact version of every installed package, for reproducible installs.
- **Log level** (11.6): The importance of a log event, such as Debug, Information, Warning or Error.
- **Logarithmic time** (3.1): O(log n). Each step cuts the problem down by a fraction, as in binary search.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Logic gate** (1.1): A tiny circuit that combines input bits into one output bit, such as AND, OR or NOT.
- **Logical clock** (12.2): A counter that orders events by cause and effect instead of by physical time.
- **Long polling** (9.7): Holding a request open on the server until there is news, then immediately asking again.
- **Longest prefix match** (6.2): The rule that a router uses the most specific matching route.
- **Loopback address** (6.2): 127.0.0.1 or ::1, which always means this machine.
- **Lost update** (5.1): A change that disappears because another thread overwrote it with a value based on stale data.
- **Lower bound** (3.7): The first position in sorted data whose value is not less than a target.
- **LRU** (8.6): Least recently used: evicting whichever entry has gone longest without being read.

### M

- **MAC address** (6.1): The 48-bit hardware address of a network interface, used within a local network.
- **Machine code** (2.2): Instructions encoded as numbers, ready for a specific processor to run.
- **Machine epsilon** (1.2): The gap between 1.0 and the next double, about 2.2 × 10⁻¹⁶. A guide to how precise a double is.
- **Machine learning** (13.1): Building programs whose behaviour is learned from examples rather than written as rules.
- **Magic number** (1.6): Fixed bytes at the start of a file that identify its format.
- **Main thread** (9.1): The browser thread that runs a page's JavaScript, handles input and renders.
- **Man-in-the-middle attack** (6.5): An attacker secretly relaying, reading or changing traffic between two parties.
- **Managed identity** (10.5): An identity a cloud platform gives a service so it can get tokens without any stored secret.
- **Many-to-many relationship** (7.5): Rows on each side relate to many on the other, stored with a junction table.
- **Mapped type** (9.3): A type that transforms each property of another type.
- **MapReduce** (5.5): Processing data on many machines with an independent map step and a combining reduce step.
- **Mark** (2.7): The phase of collection that finds and marks every reachable object.
- **Mask** (1.1): A number whose 1 bits select which bits of another number to keep or change.
- **Max-heap** (3.5): A heap with the largest item at the root.
- **MCP**: see **Model Context Protocol** (13.1)
- **Memoization** (3.8): Remembering a function's results so repeated calls with the same arguments return instantly.
- **Memory bandwidth** (5.5): How fast main memory can deliver data, a limit on loops that do little work per byte.
- **Memory leak** (2.7): Memory that stays in use although the program will never need it again.
- **Memory ordering** (1.4): The rules for how much a processor may reorder memory reads and writes as seen by other threads.
- **Memory-mapped file** (1.6): A file made to appear as a range of memory, loaded page by page on demand.
- **Merge commit** (11.1): A commit with two parents that combines two lines of history.
- **Merge conflict** (11.1): When both sides of a merge changed the same lines and Git can't choose between them.
- **Merge sort** (3.7): Sorting by splitting in halves, sorting each, and merging. Stable and always O(n log n).
- **Message authentication code** (10.5): A tag computed from data and a secret key, proving the data came from a key holder unchanged. Often shortened to MAC.
- **Message body** (6.6): The data after the headers of a request or response.
- **Message broker** (8.7): The server that hosts queues and topics and delivers messages.
- **Message framing** (6.3): A way to mark where each message ends in a byte stream, such as a length prefix or a delimiter.
- **Message queue** (8.7): A durable list of messages that producers add to and consumers take from.
- **Metadata** (1.6): Data about a file, such as its size, timestamps and permissions.
- **Method** (0.1): A named block of code belonging to a type, which can take inputs and return an output.
- **Metric** (11.6): A number measured repeatedly over time and aggregated as it is recorded.
- **MFA**: see **Multi-factor authentication** (10.2)
- **Micro-ORM** (7.7): A lightweight library, such as Dapper, that runs your SQL and maps the rows to objects.
- **Microservices** (12.1): An architecture of independently deployed services, each owning its data and communicating over the network.
- **Microtask** (5.4): A small unit of work, mainly promise callbacks, run after each task before the next one.
- **Middleware** (8.3): A component in the request pipeline that handles a request, calls the next component and handles the response.
- **Min-heap** (3.5): A heap with the smallest item at the root.
- **Minimal API** (8.3): An endpoint defined by mapping a route and method directly to a handler function.
- **Mock** (4.6): A test double set up with expectations that it verifies itself.
- **Model Context Protocol** (13.1): An open standard for connecting AI assistants to tools and data sources. Often shortened to MCP.
- **Modular monolith** (12.1): A single deployable system divided into modules with public APIs, private internals and their own data.
- **Mojibake** (1.3): Garbled text caused by decoding bytes with the wrong encoding.
- **Monitor** (5.2): The lock built into every .NET object, used by the lock statement with ordinary objects.
- **Monitoring** (11.6): Watching a system for known problems, such as an error rate above a threshold.
- **Monolith** (12.1): A system built and deployed as one unit.
- **Monotonic clock** (1.7): A clock that only moves forward at a steady rate. Used for measuring durations.
- **Monotonic predicate** (3.7): A yes-or-no question whose answer switches once as a number grows and never switches back.
- **MSBuild** (11.2): The build engine that reads .NET project files and runs their build steps.
- **MTU** (6.1): Maximum transmission unit: the largest packet a link can carry, usually 1,500 bytes.
- **Multi-factor authentication** (10.2): Requiring two or more different kinds of credential. Often shortened to MFA.
- **Multi-stage build** (11.3): A Dockerfile that builds in one image and copies only the results into a smaller final image.
- **Multi-tenant** (7.5): One system serving many customer organizations whose data must be kept apart.
- **Mutable state** (4.4): Data that can change after it is created.
- **Mutation testing** (4.6): Checking test quality by making small changes to code and seeing whether tests catch them.
- **Mutex** (5.2): An operating system lock that can be named and shared between processes.
- **Mutual exclusion** (5.2): Guaranteeing that only one thread at a time runs a critical section.
- **Mutual TLS** (6.5): TLS where the client also proves its identity with a certificate. Often shortened to mTLS.
- **MVC** (background; covered in 8.3): Model-view-controller: a pattern separating data, presentation and request handling, used by ASP.NET MVC.
- **MVVM** (background): Model-view-viewmodel: a pattern for desktop and mobile apps where views bind to view models that hold their state.
- **MX record** (6.4): A DNS record naming a domain's mail servers.

### N

- **N+1 query problem** (7.7): One query for a list, then one more query per item for related data.
- **NaN** (1.2): Not a number. The result of undefined operations like 0.0 / 0.0. It is not equal to anything, including itself.
- **Narrowing** (9.3): The compiler working out a more specific type inside a branch from a check.
- **Narrowing conversion** (1.2): Converting a value to a smaller type. It can lose information, so it needs an explicit cast.
- **NAT** (6.2): Network address translation: rewriting addresses at a router so private machines can share a public address.
- **Native AOT** (2.2): Compiling a whole .NET app to one native executable at publish time, with no JIT.
- **Natural key** (7.5): A primary key made from a real-world identifier, such as an airport code.
- **Navigation property** (7.7): A property that refers to related entities, such as a flight's bookings.
- **Negative caching** (6.4): Caching the answer that a name doesn't exist.
- **NestJS** (8.4): A structured Node.js framework with modules, dependency injection and controllers.
- **Network encapsulation** (6.1): Each layer wrapping the data from the layer above in its own header.
- **Network interface card** (6.1): The hardware that connects a machine to a network. Often shortened to NIC.
- **Network partition** (12.2): A network failure that splits machines into groups that can't reach each other.
- **Network prefix** (6.2): The leading bits of an address that identify its network.
- **Neural network** (13.1): A large mathematical function made of layers of simple operations with adjustable numbers.
- **Next-token prediction** (13.1): Generating text by repeatedly predicting a likely next token and appending it.
- **Next.js** (9.5): The most widely used framework built on React, with server rendering and server components.
- **Nibble** (1.1): Four bits, half a byte. Exactly one hex digit.
- **NIC**: see **Network interface card** (6.1)
- **NIST** (background; covered in 10.2): The US National Institute of Standards and Technology, whose security and cryptography standards are widely followed.
- **No-tracking query** (7.7): A query whose results aren't recorded by the change tracker.
- **Node** (3.2): One element of a linked structure, holding a value and references to its neighbours.
- **Node.js** (8.4): A runtime that runs JavaScript outside the browser, combining the V8 engine with an event loop.
- **Nominal typing** (2.3): Types are compatible only if declared to be, by name. C# works this way.
- **Non-blocking I/O** (5.4): I/O calls that return immediately, with the OS reporting later when data is ready or the operation finished.
- **Non-null assertion** (9.3): A ! after an expression, asserting it isn't null or undefined, unchecked.
- **Non-repeatable read** (7.4): Reading the same row twice in one transaction and getting different values.
- **Nonce** (10.5): A value used only once with a given key, such as the 12-byte input to AES-GCM.
- **Nonclustered index** (7.3): A separate index holding its key columns and a pointer to each row.
- **Normal form** (7.5): One of a series of rules defining how normalized a table is.
- **Normalization** (1.3): Rewriting a string into one standard form, so that identical-looking text compares equal.
- **NoSQL** (7.6): The loose name for non-relational databases built for particular scale or data needs.
- **NP-hard** (3.8): A class of problems with no known algorithm that is fast on every input.
- **npm** (8.4): Node's package manager, and the registry of packages it installs from.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.
- **Null-coalescing operator** (0.1): `??`, which gives its left side unless that is null, and its right side otherwise.
- **Null-conditional operator** (0.1): `?.`, which reads a member only if the object isn't null, and gives null otherwise.
- **Nullable reference types** (2.3): The C# feature that tracks which references may be null and warns about unsafe uses.

### O

- **OAuth 2.0** (10.3): The standard for letting an application call an API on a user's behalf with limited permission.
- **OAuth client** (10.3): The application that requests tokens and calls APIs with them.
- **OAuth scope** (10.3): A named permission a client requests, such as bookings.read.
- **Object** (4.1): An instance of a class, holding state and offering methods.
- **Object storage** (1.6): Storage for whole objects over HTTP, such as S3. Not a file system.
- **Object-oriented programming** (4.1): Organizing a program as objects that bundle state with the methods that act on it.
- **Object-relational impedance mismatch** (7.7): The differences between objects in code and rows in tables that an ORM has to bridge.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Observable** (9.6): A lazy stream of values over time, from RxJS, which runs when subscribed.
- **Off by one** (2.5): A loop or index that runs one step too far or stops one step too soon.
- **OIDC**: see **OpenID Connect** (10.3)
- **OLAP** (7.1): Online analytical processing: large queries that scan and summarize a lot of data.
- **OLTP** (7.1): Online transaction processing: many small, concurrent reads and writes.
- **One-time password** (10.2): A short code valid once or for a short time, often from an authenticator app.
- **One-to-many relationship** (7.5): Each row on one side relates to many on the other, with a foreign key on the many side.
- **Open addressing** (3.3): Handling collisions by probing for another free slot in the same array.
- **Open redirect** (10.4): A redirect to any address given in a request, which attackers use for phishing.
- **Open-closed principle** (4.5): Software should allow new behaviour to be added without modifying working code.
- **OpenAPI** (8.2): The standard machine-readable format for describing an HTTP API.
- **OpenID Connect** (10.3): A layer on OAuth 2.0 that adds sign-in: ID tokens, standard user claims and discovery.
- **OpenTelemetry** (11.6): The vendor-neutral standard and libraries for producing and exporting logs, metrics and traces.
- **Operating system** (1.7): The software that shares hardware among programs and protects them from each other.
- **Operations research** (3.8): The field that applies optimization and solvers to planning and scheduling problems.
- **Optimal substructure** (3.8): The property that the best answer is built from best answers to subproblems.
- **Optimistic concurrency** (7.4): Detecting conflicting changes at save time with a version check, instead of locking.
- **Optimistic update** (9.7): Showing the expected result of a change immediately, and rolling it back if the server rejects it.
- **Options pattern** (4.3): Binding a configuration section to a class and injecting it, with validation.
- **Ordinal comparison** (1.3): Comparing strings by their raw code units. Fast and the same on every machine.
- **Origin** (9.1): The scheme, host and port of a URL, which together define a site for security purposes.
- **ORM** (7.7): Object-relational mapper: a library that maps classes to tables and generates SQL.
- **OS**: see **Operating system** (1.7)
- **Outer join** (7.2): A join that also keeps unmatched rows, filling the missing side with NULLs.
- **Output caching** (8.6): Storing whole HTTP responses on the server and replaying them for matching requests.
- **Output encoding** (10.4): Converting special characters for the context data is written into, so they display instead of being parsed.
- **Over-posting** (8.5): A client setting fields it shouldn't by adding them to a request bound onto a larger object. Also called mass assignment.
- **Overflow** (1.2): A result too large for its type. The extra top bits are dropped and the value wraps around.
- **Overlapping subproblems** (3.8): The same smaller problems appearing many times when a problem is broken down.
- **Overload** (2.5): One of several methods sharing a name but taking different parameters.
- **Oversubscription** (5.5): Running more CPU-bound threads than cores, causing wasteful context switching.
- **OWASP** (10.4): The Open Worldwide Application Security Project, a community that publishes free security guidance and tools.
- **OWASP Top 10** (10.4): OWASP's list of the most critical kinds of web application security risk.
- **Owner** (2.6): The code responsible for disposing a disposable object.

### P

- **PaaS** (11.5): Platform as a service: running code or data on a platform the provider manages.
- **Package directive** (0.1): A `#:package` line at the top of a single-file program that downloads a NuGet library before building.
- **Package source mapping** (11.2): Configuration that fixes which feed each package name may be restored from.
- **package.json** (8.4): A Node project's manifest: its name, dependencies with version ranges, and scripts.
- **Packet** (6.1): One small unit of data sent over a network, with a header and a payload.
- **Packet switching** (6.1): Sending data as independent pieces that share links, instead of reserving a path for each conversation.
- **Page cache** (1.6): The operating system's copy of recently used file data in spare RAM.
- **Page fault** (1.7): Touching a page that isn't in RAM, which makes the kernel load or create it.
- **Paint** (9.1): Turning laid-out boxes into pixels, in one or more layers.
- **Paradigm** (2.1): A style of programming, such as imperative, object-oriented, functional or declarative.
- **Parallel efficiency** (5.5): Speedup divided by the number of cores used.
- **Parallelism** (5.5): Running parts of a job at the same instant on several cores to finish sooner.
- **Parameter** (0.1): The name a method uses for one of its inputs.
- **Parameter binding** (8.3): Filling a handler's parameters from the route, query, headers, body or services.
- **Parameter sniffing** (7.3): SQL Server optimizing a cached plan for the parameter values of its first call.
- **Parameterized query** (7.2): A query whose values are sent separately from its SQL text, so they can never be read as SQL.
- **Parse, don't validate** (8.5): Converting input into types that can only hold valid values, instead of checking and passing on raw data.
- **Parser** (2.1): The part of a compiler that checks tokens against the grammar and builds a tree.
- **Partial failure** (12.2): Some parts of a distributed system failing while others keep running.
- **Partition** (3.7): Rearranging items so those smaller than the pivot come before it and larger ones after.
- **Partition key** (7.6): The value that decides which machine or partition a record lives on.
- **Partitioning** (5.5): Splitting work into chunks for different workers.
- **Pass by value** (2.4): Passing a copy of each argument into a method. C#'s default for every type.
- **Passkey** (10.2): A phishing-resistant credential: a key pair created on the user's device for one website.
- **Password hashing function** (10.5): A deliberately slow, salted hash designed for storing passwords, such as Argon2 or PBKDF2.
- **Path traversal** (10.4): Using ../ and similar tricks in a file name to read files outside the intended folder.
- **Pattern matching** (2.5): Testing a value against a shape and extracting parts of it in one step.
- **Payload** (6.1): The actual data a packet carries, after its header.
- **PCI DSS** (background; covered in 10.1): Payment Card Industry Data Security Standard: the security rules for any system that handles card payments.
- **Penetration test** (10.1): An authorized attack on a system by specialists, to find weaknesses before real attackers do.
- **Percentile** (8.1): The value below which a given share of measurements fall, such as the 99th percentile response time.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **Persistent connection** (6.6): An HTTP connection kept open for further requests instead of being closed after one.
- **Persistent data structure** (4.4): A data structure whose old versions remain intact and usable after changes.
- **Pessimistic locking** (7.4): Locking rows when reading them, so nobody else can change them until you finish.
- **Phantom read** (7.4): Running the same query twice in one transaction and getting a different set of rows.
- **Phishing** (10.2): Tricking people into giving credentials to a fake site or message that looks real.
- **Pinning** (2.7): Stopping the collector from moving an object, so native code can use its address.
- **Pipe** (1.7): A kernel buffer connecting one process's output to another's input.
- **Pivot** (3.7): The item quicksort partitions around.
- **PKCE** (10.3): Proof Key for Code Exchange: a secret and its hash that make a stolen authorization code useless.
- **Plaintext** (6.5): Data in its readable, unencrypted form.
- **Plan cache** (7.3): The database's store of compiled plans, reused for later runs of the same statement.
- **PLINQ** (5.5): Parallel LINQ, which runs LINQ queries across several cores with AsParallel.
- **Pod** (11.3): The smallest unit Kubernetes runs: one or more containers sharing a network address.
- **Pointer** (1.5): A stored memory address. C and C++ let you work with pointers directly.
- **Poison message** (8.7): A message that fails every time it is processed.
- **Policy** (10.2): In ASP.NET Core, a named set of requirements a caller must meet to use an endpoint.
- **Polling** (9.7): Asking the server for changes at a regular interval.
- **Polyfill** (9.2): Code that implements a newer built-in feature for environments that lack it.
- **Polyglot persistence** (7.6): Using several kinds of data store in one system, each for what it does best.
- **Polymorphism** (4.1): Code working with a general type while each object behaves according to its actual type.
- **Port** (6.3): A 16-bit number that identifies which program on a machine a packet is for.
- **Post-quantum cryptography** (10.5): Algorithms designed to resist attacks by future quantum computers, such as ML-KEM and ML-DSA.
- **Postcondition** (4.2): Something a method guarantees when it returns.
- **PostgreSQL** (background; covered in 7.2): A widely used free, open-source relational database.
- **Pre-signed URL** (9.7): A short-lived URL that grants permission to upload or download one object directly from storage.
- **Precondition** (4.2): Something that must be true when a method is called.
- **Preflight request** (9.7): An automatic OPTIONS request a browser sends to ask whether a cross-origin request is allowed.
- **Pretraining** (13.1): The first, largest stage of training a language model, on next-token prediction over broad data.
- **Primary constructor** (0.1): Constructor parameters written right after a class or struct name, usable throughout its body.
- **Primary key** (7.2): A column or set of columns that uniquely identifies each row.
- **Primitive value** (9.2): A simple value that is copied on assignment: a number, bigint, string, boolean, undefined, null or symbol.
- **Principle of least astonishment** (4.5): Code should behave the way its name and signature lead a reader to expect.
- **Priority queue** (3.5): A collection that always hands back the item with the best priority, the smallest value by convention.
- **Private address** (6.2): An address from the ranges reserved for internal networks, not routed on the public internet.
- **Private endpoint** (11.5): A private address in your virtual network for a managed service, keeping its traffic off the internet.
- **Private field** (9.2): A class field written with #, which the language makes inaccessible from outside the class.
- **Problem Details** (2.8): The standard JSON format for error responses from web APIs, defined in RFC 9457.
- **Procedural programming** (4.1): Organizing a program as procedures that operate on separate, plain data.
- **Process** (1.7): A running program with its own private memory and resources.
- **Producer-consumer** (5.2): A pattern where some threads add work to a queue and others take it off and process it.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Profile-guided optimization** (2.2): Optimizing code based on how it actually behaved when run. Dynamic PGO does this in the .NET JIT.
- **Profiling** (12.4): Recording where a running program spends its time or allocates memory.
- **Programming language** (2.1): A precise notation for describing what a computer should do, translated into machine instructions by tools.
- **Project file** (11.2): The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **Projection** (7.7): Selecting only the needed values into a new shape, with Select.
- **Promise** (5.4): The JavaScript object standing for a result that will arrive later.
- **Prompt injection** (13.1): Instructions hidden in content a model reads, which it may follow as if they came from the user.
- **Prop drilling** (9.4): Passing props through many components that don't use them to reach one that does.
- **Property** (0.1): A member that looks like a field from outside but is read and written through get and set.
- **Property binding** (9.6): Setting a DOM property from an expression in a template, written [property]="expression".
- **Property-based testing** (4.6): Checking a rule against many generated inputs instead of a few chosen examples.
- **Props** (9.4): A component's inputs, passed in by its parent.
- **Protocol** (6.1): An agreed set of rules for the format and meaning of messages between machines.
- **Protocol Buffers** (8.2): A compact binary format for structured data, defined by .proto schema files.
- **Protocol stack** (6.1): The set of protocols a machine runs, layered on top of each other.
- **Prototype** (9.2): The object another object inherits properties from, forming a lookup chain.
- **Pruning** (3.8): Abandoning a partial search path that can't lead to a valid or better answer.
- **Publish-subscribe** (12.3): Sending messages to a topic so every subscriber receives a copy, without the publisher knowing them.
- **Pull request** (11.1): A proposal to merge a branch, with a diff for review, discussion and automated checks.
- **Pure DI** (4.3): Wiring dependencies by hand with `new` in the composition root, without a container.
- **Pure function** (4.4): A function whose result depends only on its arguments and which has no side effects.
- **Python** (background): A popular dynamically typed programming language, used widely for scripting, data work and machine learning.

### Q

- **Quadratic time** (3.1): O(n²). The cost grows with the square of the input, typically from nested loops.
- **Query** (background; covered in 7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **Query key** (9.7): The key under which a server-state library caches one piece of fetched data.
- **Query language** (7.1): A language for asking a database for data, most often SQL.
- **Query optimizer** (7.3): The part of a database that chooses how to run each query.
- **Query plan** (7.3): The step-by-step strategy the optimizer chose for a query. Also called an execution plan.
- **Queue-based load leveling** (12.4): Putting a queue in front of a service so bursts of work are handled at a steady rate.
- **QUIC** (6.6): A transport protocol on UDP with independent streams and a combined connection and TLS handshake.
- **Quicksort** (3.7): Sorting by partitioning around a pivot and sorting each side. Fast in practice, O(n²) at worst.
- **Quorum** (12.2): The number of replicas that must take part in an operation, chosen so that reads and writes overlap.

### R

- **Race condition** (5.1): A bug where the result depends on the timing of threads.
- **Radix sort** (3.7): Sorting by one digit or byte at a time using stable counting sorts.
- **Raft** (12.2): A widely used consensus algorithm designed to be understandable.
- **RAG**: see **Retrieval-augmented generation** (13.1)
- **RAM** (1.5): Main memory. Fast compared with storage, slow compared with caches, and lost when the power goes off.
- **Range scan** (7.3): Seeking to the start of a range in an index and reading along it to the end.
- **Rate limiting** (8.2): Capping how many requests a client may make in a period.
- **RBAC**: see **Role-based access control** (10.2)
- **Reachable** (2.7): Able to be found by following references from a root. Reachable objects are kept.
- **React** (9.5): A JavaScript library for building interfaces from components, created at Facebook.
- **React Compiler** (9.5): A build-time tool that adds memoization to React components automatically.
- **React context** (9.5): A way to provide a value to a whole subtree of components without passing props.
- **React Server Components** (9.5): Components that run only on the server and send their rendered output, not their code.
- **Reactive effect** (9.4): Code that runs when the state it read changes, to act on the world outside the UI.
- **Reactive signal** (9.4): A value that records what reads it and notifies those readers when it changes.
- **Reactor pattern** (5.4): Waiting for the OS to report which connections are ready, then reacting to each.
- **Read committed** (7.4): The isolation level that shows only committed data. The default in SQL Server and PostgreSQL.
- **Read model** (12.3): A data structure shaped for particular queries, updated from the write side's changes.
- **Read replica** (7.6): A copy of a database that serves reads, taking load off the primary.
- **Read-modify-write** (5.1): Reading a value, computing a new one and writing it back. Not atomic unless made so.
- **Read-through cache** (8.6): A cache that loads missing values from the source itself, using a loader it was given.
- **Read-your-writes consistency** (7.6): The guarantee that users always see their own latest writes.
- **Reader-writer lock** (5.2): A lock that lets many readers in together, or one writer alone.
- **Readiness check** (8.1): A health check that says whether an instance can serve traffic right now.
- **ReadyToRun** (2.2): Pre-compiled machine code stored inside a .NET assembly to speed up start-up.
- **Rebase** (11.1): Replaying a branch's commits on top of another commit, creating new commits.
- **Reconciliation** (9.5): Comparing a new render with the previous one to find the DOM changes needed.
- **Record** (2.4): A C# type for holding data, with generated value equality, ToString and with support.
- **Record TTL** (6.4): How many seconds a DNS answer may be cached.
- **Recovery point objective** (12.5): The most data, measured in time, that the business can afford to lose. Often shortened to RPO.
- **Recovery time objective** (12.5): The longest a service may be down while it is restored. Often shortened to RTO.
- **Recursion** (3.8): A function solving a problem by calling itself on smaller versions of it.
- **Recursive case** (3.8): The case that breaks the problem down and calls the function on the smaller pieces.
- **Recursive descent** (2.1): A parsing technique with one method per grammar rule, where rules call each other.
- **Recursive resolver** (6.4): A server that finds answers on a client's behalf, walking the DNS tree and caching the results.
- **RED method** (11.6): Measuring rate, errors and duration for every service.
- **Red-black tree** (3.4): A self-balancing binary search tree that uses node colours to limit its height. Behind SortedSet and SortedDictionary.
- **Redirect URI** (10.3): The registered address the authorization server sends the browser back to.
- **Reduction** (5.5): Combining many partial results into one, such as a sum or a maximum.
- **Redundancy** (12.5): Having more than one of any component whose failure would matter.
- **ref parameter** (2.4): A parameter that is another name for the caller's variable, so assigning it changes the caller's variable.
- **Refactoring** (4.5): Changing the structure of code without changing its behaviour, in small tested steps.
- **Reference** (1.5): A pointer managed by the language. It is either null or refers to a real object.
- **Reference counting** (2.7): Keeping a count of references to each object and freeing it when the count reaches zero.
- **Reference cycle** (2.7): Objects that refer to each other, keeping each other's reference counts above zero.
- **Reference type** (1.5): A type that lives on the heap and is reached through references. In C#, a class.
- **Referential integrity** (7.2): The guarantee that every foreign key points at a row that really exists.
- **Referential transparency** (4.4): The property that a call can be replaced by its result without changing the program.
- **Reflected XSS** (10.4): Cross-site scripting where the script comes from the request, such as a link, and is echoed back.
- **Reflog** (11.1): Git's local record of where HEAD and each branch have pointed, used to recover lost commits.
- **Refresh token** (10.3): A long-lived token a client exchanges for new access tokens.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **Register** (1.4): A small, very fast storage slot inside the processor.
- **Regression test** (4.6): A test written to reproduce a bug, so it can't return unnoticed.
- **Rehashing** (3.3): Growing a hash table and reinserting every entry at its new position.
- **Reinforcement learning** (13.1): Training by rewarding outputs that score well, such as answers people prefer or code whose tests pass.
- **Relational database** (7.1): A database that stores data in tables of rows and columns and is queried with SQL.
- **Relational model** (7.2): Storing data as tables of rows and columns, connected by keys.
- **Relationship** (7.5): How entities relate, expressed in a schema with foreign keys.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Replication** (7.6): Keeping copies of the same data on several machines.
- **Replication lag** (7.6): The delay before a change on the primary appears on a replica.
- **Repository instructions file** (13.2): A file in a repository that gives AI assistants standing context, such as build steps and conventions.
- **Repository pattern** (7.7): Wrapping data access for each entity type behind a class with methods such as GetById.
- **Request pipeline** (8.3): The ordered chain of middleware every request passes through.
- **Resource** (8.2): A thing an API exposes at its own URL, such as a flight or a booking.
- **Resource group** (11.5): A container for Azure resources that share a lifecycle.
- **Resource server** (10.3): The API that accepts access tokens.
- **Resource-based authorization** (10.2): Checking the caller against the specific record being accessed.
- **REST** (8.2): An architectural style for APIs built around resources with URLs and the standard HTTP methods.
- **Rest parameter** (9.2): Three dots in a parameter list or pattern that gather the remaining items into an array or object.
- **Result type** (2.8): A return value that is either a success holding a result or a failure holding an error.
- **Rethrow** (2.8): Passing a caught exception on with `throw;`, keeping its original stack trace.
- **Retransmission** (6.3): Sending data again when its acknowledgement doesn't arrive in time.
- **Retrieval-augmented generation** (13.1): Finding relevant documents or code and adding them to the context before generating. Often shortened to RAG.
- **Retry amplification** (12.5): Retries multiplying the load on a failing service, worse when several layers retry.
- **Reverse proxy** (6.6): A server that receives requests on behalf of other servers and forwards them.
- **Revert** (11.1): A new commit that undoes an earlier commit, keeping history intact.
- **RFC** (background; covered in 6.6): Request for Comments: the numbered documents that define internet standards, such as RFC 9110 for HTTP.
- **Rich domain model** (4.1): Domain classes that enforce their own rules on the data they hold.
- **RID** (background; covered in 11.2): Runtime identifier: a name for an operating system and processor combination, such as linux-x64, used when building .NET apps.
- **Ring buffer** (3.2): A fixed array used in a circle, with front and back indexes that wrap around.
- **Role assignment** (11.5): Granting a role to an identity at a scope, such as a subscription, resource group or resource.
- **Role-based access control** (10.2): Granting permissions through roles assigned to users. Often shortened to RBAC.
- **Rollback** (7.4): Undoing all of a transaction's changes.
- **Rolling deployment** (11.4): Replacing instances a few at a time, so old and new versions briefly run together.
- **Rotation** (3.4): A small local rearrangement that rebalances a tree while keeping its order.
- **Round-trip time** (6.1): The time from sending a small packet to receiving the reply. Often shortened to RTT.
- **Route constraint** (8.3): A rule on a route segment, such as {id:int}, that must hold for the route to match.
- **Route group** (8.3): A set of endpoints sharing a route prefix and settings.
- **Route template** (8.3): A pattern such as /flights/{number} that matches request paths and captures values.
- **Router** (6.2): A device that connects networks and forwards packets between them.
- **Routing table** (6.2): A router's list of network prefixes and where to send packets for each.
- **Row** (7.2): One record in a table.
- **Row versioning** (7.4): Keeping old versions of changed rows so readers can see them without waiting. Also called MVCC.
- **RPO**: see **Recovery point objective** (12.5)
- **RTO**: see **Recovery time objective** (12.5)
- **RTT**: see **Round-trip time** (6.1)
- **Rule of three** (4.2): Waiting until a pattern appears three times before extracting a shared abstraction.
- **Rules of hooks** (9.5): Hooks must be called at the top level, in the same order on every render.
- **Run-to-completion** (5.4): Each callback finishing before the next one starts, with no interruption partway.
- **Runbook** (11.6): A short guide saying what an alert means and what to do about it.
- **Runner** (11.4): The machine or container that runs a pipeline's jobs.
- **Running median** (3.5): The median of a stream so far, maintained with two heaps as values arrive.
- **Runtime** (2.1): The software that loads and runs compiled code and manages memory. For .NET, the CLR.
- **RxJS** (9.6): The Reactive Extensions library for JavaScript, used by Angular for HTTP and event streams.

### S

- **SaaS** (11.5): Software as a service: finished software used over the internet.
- **Safe method** (6.6): A method that shouldn't change anything on the server, such as GET.
- **Saga** (12.3): A business transaction across services, made of local steps with compensating actions.
- **Saga choreography** (12.3): Coordinating a saga by having each service react to the previous step's event.
- **Saga orchestration** (12.3): Coordinating a saga with a central component that directs each step.
- **Salt** (10.5): A random value stored with each password hash so identical passwords hash differently.
- **Same-origin policy** (9.1): The browser rule that a page's scripts may only read data from their own origin.
- **Sargable** (7.3): Written so that an index can be used to find matching rows.
- **satisfies** (9.3): An operator that checks a value against a type while keeping the value's own inferred type.
- **Savepoint** (7.4): A named point inside a transaction that you can roll back to without ending the transaction.
- **SBOM**: see **Software bill of materials** (11.2)
- **Scalability** (12.4): How well a system keeps its performance as load grows, and what adding capacity costs.
- **Scheduled job** (8.7): Work that runs at set times rather than in response to a message.
- **Scheduler** (1.7): The part of the kernel that decides which thread runs on which core, and when.
- **Schema** (7.5): The structure of a database: its tables, columns, types, keys, constraints and indexes.
- **Schema migration** (7.5): A versioned script that changes a database from one schema version to the next.
- **Schema registry** (12.3): A service that stores event schemas and rejects incompatible changes.
- **Schema-on-read** (7.6): Applying structure when data is read rather than enforcing it when data is written.
- **Scope** (2.6): The region of source code where a name can be used.
- **Scoped service** (2.6): A service with one instance per scope, which in ASP.NET Core means per request.
- **SDK** (11.2): Software development kit: the tools needed to build for a platform. The .NET SDK includes the dotnet command, compilers and MSBuild.
- **Seam** (4.2): A place where one implementation can be swapped for another without changing the calling code.
- **Search domain** (6.4): A suffix a resolver tries adding to short names before looking them up as written.
- **Secret** (10.5): Any value that grants access, such as a password, API key, connection string or private key.
- **Secret scanning** (10.5): Automatically detecting secrets committed to repositories or present in build output.
- **Sector** (1.6): The fixed-size chunk a storage device reads and writes, usually 512 or 4,096 bytes.
- **Secure by default** (10.1): Making the safe choice the one that needs no action.
- **Security headers** (10.4): HTTP response headers that switch on browser protections, such as CSP and HSTS.
- **Security through obscurity** (10.1): Relying on attackers not knowing how a system works. Not enough on its own.
- **Seek** (1.6): Moving a hard disk's head to the right track. Takes milliseconds.
- **Selectivity** (7.3): The share of rows a condition matches. Indexes help most for very selective conditions.
- **Self-contained deployment** (11.2): A published app that includes the .NET runtime, so it needs nothing installed on the target.
- **Semantic validation** (8.5): Checking the meaning of input using the request itself, such as a date not being in the past.
- **Semantic versioning** (8.4): Numbering versions major.minor.patch, where only a new major version may break compatibility.
- **Semantics** (2.1): What valid code means when it runs.
- **Semaphore** (5.2): A lock that allows up to a fixed number of threads in at once.
- **Separation of concerns** (4.5): Keeping different aspects of a program, such as parsing, rules and storage, in different places.
- **Sequence number** (6.3): The position of a byte in a TCP stream, used to order data and detect gaps.
- **Serializable** (7.4): The isolation level whose results match running the transactions one at a time.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Server GC** (2.7): The .NET collector mode with one heap per core, tuned for throughput. The default for ASP.NET Core.
- **Server state** (9.4): Data owned by the backend of which the frontend holds a cached copy.
- **Server-sent events** (9.7): A one-way stream of text events from server to browser over one long HTTP response. Often shortened to SSE.
- **Server-side rendering** (9.1): Building a page's HTML on the server for each request. Often shortened to SSR.
- **Server-side request forgery** (10.4): Making a server fetch an address the attacker chooses, often an internal one. Often shortened to SSRF.
- **Server-state library** (9.7): A library that fetches, caches, deduplicates, refreshes and retries server data for the frontend.
- **Serverless** (11.5): Running code that the platform starts on demand, scales automatically and bills per use.
- **Service level agreement** (12.5): A contract promising a level of service, with penalties if it is missed. Often shortened to SLA.
- **Service level indicator** (12.5): A measurement of service quality that users care about, such as the share of fast, successful requests. Often shortened to SLI.
- **Service level objective** (12.5): A target for a service level indicator over a period, such as 99.9% over 30 days. Often shortened to SLO.
- **Service lifetime** (2.6): How long a dependency injection container keeps an instance: singleton, scoped or transient.
- **Service locator** (4.3): Asking a container for dependencies from inside a class instead of receiving them. Usually an anti-pattern.
- **Service mesh** (12.1): Proxies beside each service that add encryption, retries and telemetry to calls between services.
- **Service worker** (9.7): A script a browser runs in the background between a page and the network, able to cache and work offline.
- **Session** (10.2): The signed-in state that lets later requests be recognized as coming from the same user.
- **Session cookie** (10.2): A cookie that identifies a signed-in session.
- **Session fixation** (10.2): An attack that plants a known session id in a victim's browser before they sign in.
- **Shadowing** (2.6): Declaring a name in an inner scope that hides the same name in an outer scope.
- **Shallow copy** (2.4): A copy that shares the objects inside the original instead of copying them too.
- **Sharding** (7.6): Splitting data across several machines by a key, each holding a share.
- **Shared lock** (7.4): A lock for reading that several transactions can hold on the same row at once.
- **Shared responsibility model** (11.5): The split of security duties between cloud provider and customer.
- **Shared-nothing** (5.1): A design where threads don't share mutable data and communicate by passing messages or values.
- **Short-circuit evaluation** (2.5): Stopping evaluation of a condition as soon as its answer is known, as `&&` and `||` do.
- **Shortest path** (3.6): The path between two vertices with the smallest total edge weight.
- **Side effect** (4.4): Anything a function does besides return a result, such as changing data or doing I/O.
- **Sift down** (3.5): Swapping an item with its smaller child until the heap property holds again.
- **Sift up** (3.5): Swapping a new item with its parent until the heap property holds again.
- **Signal** (1.7): A notification from the OS to a process, such as SIGTERM asking it to stop.
- **Signal input** (9.6): A component input declared with input(), read as a signal inside the component.
- **SignalR** (9.7): The .NET library for real-time messaging, choosing between WebSockets, server-sent events and long polling.
- **SIGTERM** (background; covered in 1.7): The signal that asks a process to shut down cleanly, sent by Docker, Kubernetes and systemd when stopping it.
- **SIMD** (1.4): Single instruction, multiple data. One instruction that works on several values at once.
- **Single point of failure** (12.5): A component whose failure alone brings the system down.
- **Single responsibility principle** (4.5): A module should have one reason to change.
- **Single sign-on** (10.3): Signing in once to get access to many applications.
- **Single source of truth** (9.4): One owner for each piece of state, from which everything else is derived.
- **Single-flight** (8.6): Letting only one request load a missing key while others wait for its result.
- **Single-page application** (9.1): An app that loads once and then builds and updates its interface in the browser. Often shortened to SPA.
- **Singleton** (2.6): A service with one instance for the whole life of the app.
- **SLA**: see **Service level agreement** (12.5)
- **SLI**: see **Service level indicator** (12.5)
- **Sliding expiration** (8.6): Removing a cache entry after a period in which nothing read it.
- **SLO**: see **Service level objective** (12.5)
- **Slow start** (6.3): The phase where a new TCP connection starts small and doubles how much it sends each round trip.
- **SLSA** (background): Supply-chain Levels for Software Artifacts: a framework for recording and verifying how software was built.
- **Smoke test** (11.4): A quick check after deployment that the service started and answers.
- **Snapshot** (4.4): An unchanging copy of state as it was at one moment.
- **Snapshot isolation** (7.4): Each transaction reading a consistent snapshot of the database from when it started.
- **Snapshot testing** (4.6): Saving output once and failing when later output differs. Also called approval testing.
- **SNI** (6.5): Server Name Indication: the site name a client sends at the start of the handshake.
- **Socket** (6.3): The operating system's handle for one endpoint of network communication.
- **Soft delete** (7.5): Marking a row as deleted instead of removing it.
- **Software bill of materials** (11.2): A machine-readable list of every component and version in a build. Often shortened to SBOM.
- **SOLID** (4.5): Five object-oriented design principles: single responsibility, open-closed, Liskov substitution, interface segregation and dependency inversion.
- **Source generator** (2.2): A compiler plug-in that writes extra C# code during the build.
- **Source map** (2.2): A file linking positions in generated code back to the original source.
- **SPA**: see **Single-page application** (9.1)
- **Space complexity** (3.1): How the extra memory an algorithm needs grows with the size of its input.
- **Sparse graph** (3.6): A graph where each vertex connects to only a few others.
- **Spatial index** (3.4): A tree that organizes data by location, for queries such as "everything within 200 km".
- **Speedup** (5.5): How many times faster a job runs in parallel than sequentially.
- **Split brain** (12.2): Two parts of a cluster each acting as the leader, so their data diverges.
- **Split query** (7.7): Loading each included collection with its own SQL query.
- **Spread syntax** (9.2): Three dots that expand an object's or array's contents in place, used for copying and combining.
- **Spy** (4.6): A test double that records how it was called, for checking afterwards.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **SQL dialect** (7.2): One database's version of SQL, with its own extras and differences.
- **SQL injection** (7.2): Untrusted input changing the meaning of a SQL statement because it was pasted into the SQL text.
- **SQL Server** (background; covered in 7.2): Microsoft's relational database product.
- **SSD** (1.6): Solid-state drive. Stores data in flash memory chips, with no moving parts.
- **SSE**: see **Server-sent events** (9.7)
- **SSH** (background): Secure Shell: an encrypted protocol for signing in to and running commands on remote machines.
- **SSR**: see **Server-side rendering** (9.1)
- **SSRF**: see **Server-side request forgery** (10.4)
- **Stable hash** (3.3): A hash function defined to give the same result on every machine and every run.
- **Stable sort** (3.7): A sort that keeps items with equal keys in their original order.
- **Stack** (1.5): Per-thread memory holding one frame per active method call. Freed automatically on return.
- **Stack frame** (1.5): The block on the stack for one method call: parameters, locals and the return address.
- **Stack trace** (2.8): The chain of method calls recorded where an exception was thrown.
- **Stack unwinding** (2.8): Leaving methods one by one, running their finally blocks, while looking for a matching catch.
- **Staging area** (11.1): The set of changes that will go into the next commit. Also called the index.
- **Stale response** (9.7): A response to an earlier request that arrives after a newer one and would show outdated results.
- **Stale-while-revalidate** (8.6): Serving an expired entry briefly while it is refreshed in the background.
- **Standalone component** (9.6): An Angular component that lists its own dependencies in imports, without an NgModule.
- **Standard library** (2.1): The ready-made types that come with a language platform. In .NET, the base class library.
- **State store** (9.4): A single object holding shared application state, with defined ways to change it.
- **Stateless protocol** (6.6): A protocol where each request stands alone, with no required memory of earlier ones.
- **Stateless service** (8.1): A service whose instances keep no per-user state between requests, so any instance can serve any request.
- **Statement** (0.1): One instruction in a program, usually ending with a semicolon.
- **Static abstract member** (4.2): A static method or operator that an interface requires implementing types to provide.
- **Static analysis** (11.2): Examining code for bugs and bad patterns without running it, as analyzers do during a build.
- **Static field** (2.6): A field that belongs to a type rather than an instance. One copy exists for the whole process.
- **Static typing** (2.3): Checking types when code is compiled, before it runs.
- **Status code** (6.6): The three-digit number in a response saying how the request went.
- **Sticky session** (8.1): Routing each user's requests to the same instance because their state lives in its memory.
- **Stop-the-world pause** (2.7): A moment when the collector halts all program threads to do its work safely.
- **Stored XSS** (10.4): Cross-site scripting where the script is saved by the site and served to every viewer.
- **Strangler fig pattern** (12.1): Replacing a system gradually by routing features one by one from the old system to the new.
- **Strategy pattern** (4.3): Passing in the part of an algorithm that varies as an object or function.
- **Stream** (1.6): Reading or writing data a piece at a time instead of all at once.
- **Strict equality** (9.2): Comparing with === and !==, which never converts types first.
- **Strict mode** (9.2): A stricter set of JavaScript rules, on automatically in modules and classes.
- **STRIDE** (10.1): A checklist of threats: spoofing, tampering, repudiation, information disclosure, denial of service and elevation of privilege.
- **String interpolation** (0.1): Inserting expression values into a string marked with `$`, such as `$"{flight} is late"`.
- **Strong typing** (2.3): Refusing to silently mix unrelated types. The term is used loosely.
- **Strongly typed ID** (2.3): A small wrapper type for an identifier, so ids of different things can't be mixed up.
- **Structural sharing** (4.4): Letting a new version of a data structure reuse the unchanged parts of the old one.
- **Structural typing** (2.3): Types are compatible if their members match. TypeScript works this way.
- **Structured logging** (8.3): Logging with named fields for values, not just formatted text, so logs can be searched by field.
- **Structured programming** (2.5): Building programs from blocks with one entry and one exit, instead of arbitrary jumps.
- **Stub** (4.6): A test double that returns fixed answers.
- **Subnet mask** (6.2): A number with 1s for the network bits, such as 255.255.255.0 for /24.
- **Subquery** (7.2): A query nested inside another query.
- **Subtree** (3.4): A node together with everything below it.
- **Supply chain attack** (8.4): An attack that gets malicious code into your build through a dependency or build tool.
- **Surrogate key** (7.5): A generated primary key with no meaning outside the database.
- **Surrogate pair** (1.3): Two UTF-16 code units that together hold one code point above U+FFFF.
- **Switch expression** (2.5): An expression that picks a result using patterns, from top to bottom.
- **Symmetric encryption** (6.5): Encryption where the same key encrypts and decrypts, such as AES.
- **Sync over async** (5.3): Blocking on asynchronous code with .Result or .Wait(). Causes deadlocks and starvation.
- **Synchronization context** (5.3): An object that decides where continuations run, such as back on a UI thread.
- **Syntactic validation** (8.5): Checking the form of input, such as its type, length and format.
- **Syntax** (2.1): The rules for what counts as valid code.
- **Synthetic monitoring** (11.6): Scripted checks run from outside the system on a schedule, such as a test search every minute.
- **System call** (1.7): A request from a program to the kernel, such as opening a file.

### T

- **Table** (7.2): A set of rows describing one kind of thing, with a fixed list of typed columns.
- **Table scan** (7.3): Reading every row of a table to answer a query.
- **Tail call** (3.8): A call that is the last thing a function does, which some languages turn into a jump.
- **Tail latency** (8.1): The response time of the slowest requests, described by high percentiles.
- **Target framework** (11.2): The .NET version a project builds for, such as net10.0, which decides the APIs it may use.
- **Target-typed new** (0.1): Writing `new()` without a type name when the type is already known from context.
- **Task** (5.3): The .NET object representing an operation that will complete in the future, possibly with a result.
- **Task parallelism** (5.5): Running different operations at the same time.
- **Task queue** (5.4): The event loop's queue of ordinary callbacks from timers, I/O and user events.
- **TaskCompletionSource** (5.3): An object that creates a task you complete by hand, used to wrap callback-based APIs.
- **TCP** (6.3): Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **TCP keepalive** (6.3): Small probe packets on an idle connection that detect whether the other end is still there.
- **Technical debt** (4.5): The ongoing extra cost of changes caused by shortcuts or outdated design.
- **Tell, don't ask** (4.5): Tell an object what you want done instead of pulling out its data and deciding for it.
- **Temperature** (13.1): A setting that controls how much a model's choice of next token varies.
- **Template literal** (9.2): A string in backticks that can include ${expressions} and span lines.
- **Template method** (4.2): A base-class method that fixes an operation's outline and leaves some steps to derived classes.
- **Temporal coupling** (4.4): A hidden requirement that methods be called in a particular order.
- **Temporal table** (7.5): A table that automatically keeps every past version of its rows.
- **Terminal** (background; covered in 1.7): A text window where you type commands for the shell to run, such as dotnet run.
- **Terraform** (11.5): A widely used infrastructure-as-code tool that works across many cloud providers.
- **Test double** (4.6): Any stand-in object used in place of a real collaborator in a test.
- **Test pyramid** (4.6): The guideline to have many unit tests, fewer integration tests and few end-to-end tests.
- **Test-driven development** (4.6): Writing a failing test first, making it pass, then refactoring, in short cycles.
- **Testcontainers** (4.6): A library that runs real databases and services in Docker for the length of a test run.
- **This binding** (9.2): How JavaScript sets this, based on how a function is called rather than where it is defined.
- **Thread** (1.7): One path of execution inside a process. Threads share memory and each has its own stack.
- **Thread pool** (5.2): A managed set of reusable worker threads that run queued work items.
- **Thread pool starvation** (5.3): All pool threads blocked, so queued work and continuations can't run.
- **Thread-local storage** (2.6): A variable with a separate copy for each thread.
- **Threat modelling** (10.1): Finding and addressing what can go wrong with a system, in a structured way, usually at design time.
- **Three-valued logic** (7.2): SQL's logic of true, false and unknown, where any comparison with NULL is unknown.
- **Three-way handshake** (6.3): The SYN, SYN-ACK, ACK exchange that opens a TCP connection.
- **Throughput** (6.1): The rate a transfer actually achieves after overheads and sharing.
- **Tiered compilation** (2.2): Compiling a method quickly first, then again with full optimization once it proves to be hot.
- **Time complexity** (3.1): How the number of steps an algorithm takes grows with the size of its input.
- **Time to live** (6.2): A counter in each packet, reduced by every router, that stops packets circling forever. Often shortened to TTL.
- **Time-series database** (7.6): A store built for timestamped measurements that arrive continuously.
- **TIME_WAIT** (6.3): The state a closed connection's record stays in for a while, on the side that closed first.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Timing attack** (10.5): Working out a secret from how long operations take.
- **TLD**: see **Top-level domain** (6.4)
- **TLS** (6.5): Transport Layer Security: the protocol that encrypts and authenticates connections.
- **TLS termination** (6.5): Decrypting TLS at a load balancer or proxy in front of the real servers.
- **Token** (2.1): The smallest meaningful piece of source code, such as a name, a number or an operator.
- **Tokenizer** (13.1): The component that splits text into the tokens a model reads and writes.
- **Top-k** (3.5): Finding the k largest or smallest items, efficiently done with a size-k heap.
- **Top-level domain** (6.4): The last label of a domain name, such as .com, .aero or .uk. Often shortened to TLD.
- **Top-level statements** (0.1): Code written directly in a file without a Main method. The program starts at the first line.
- **Topic** (8.7): A channel where each message is delivered to every subscriber.
- **Topological sort** (3.6): An ordering of a DAG's vertices where every edge points forward.
- **Torn read** (5.1): Reading a value while it is being written, getting part of the old value and part of the new.
- **Trace** (11.6): The record of one request's path through a system, as a tree of spans.
- **Trace context** (11.6): The trace id and parent span id passed between services, in the W3C traceparent header.
- **Trace span** (11.6): One timed piece of work within a trace, with a name, duration, status, attributes and parent.
- **Tracing garbage collection** (2.7): Finding live objects by following references from the roots, and freeing the rest.
- **Transaction** (7.1): A group of database changes that happen all together or not at all.
- **Transactional outbox** (7.4): Writing outgoing messages to a table in the same transaction as the change, and publishing them afterwards.
- **Transformer** (13.1): The neural network architecture behind almost all language models, built around attention.
- **Transient failure** (2.8): A failure likely to go away if the operation is tried again, such as a timeout.
- **Transient service** (2.6): A service created new every time it is requested.
- **Transitive dependency** (11.2): A package included because one of your dependencies needs it, not because you referenced it.
- **Transpiler** (2.2): A compiler whose output is another high-level language, such as TypeScript to JavaScript.
- **Transport layer** (6.1): The layer that moves data between programs, adding guarantees such as ordering and reliability.
- **Tree** (3.4): Nodes connected with no loops, where every node except the root has exactly one parent.
- **Tree traversal** (3.4): Visiting every node of a tree once, in a defined order.
- **Trie** (3.4): A tree that stores strings one character per level, so keys with a common prefix share nodes.
- **Trimming** (2.2): Removing unused code from a published app to make it smaller.
- **Trunk-based development** (11.1): Integrating small changes into one main branch frequently, with short-lived branches.
- **Trust boundary** (8.5): A point where data passes from something you don't control into something you do.
- **Trust store** (6.5): The set of root certificates an operating system or browser trusts.
- **Truthy** (9.2): Any value that isn't falsy, including "0", [] and {}.
- **tsconfig.json** (9.3): The file that configures a TypeScript project's compiler settings.
- **TTL**: see **Time to live** (6.2)
- **Tuple** (2.5): A small group of values handled as one, such as `(int Min, int Max)`.
- **Twelve-factor app** (8.1): A set of practices for services that run well in the cloud, such as keeping configuration in the environment.
- **Two's complement** (1.2): The standard way to store negative integers. The top bit counts as a large negative value.
- **Type** (2.3): A set of possible values plus the operations allowed on them.
- **Type alias** (9.3): A name for a type, declared with type.
- **Type annotation** (9.3): An explicit type written after a colon, as in let delay: number.
- **Type assertion** (9.3): Telling the compiler to treat a value as a type, with as, without any run-time check.
- **Type constraint** (2.3): A rule limiting what a type parameter can be, such as `where T : IComparable`.
- **Type guard** (9.3): A function returning value is T, which narrows its argument where it returns true.
- **Type inference** (2.3): The compiler working out a type from context, as with `var`.
- **Type parameter** (2.3): The placeholder, such as `T`, filled in when a generic type is used.
- **Type stripping** (8.4): Running TypeScript by deleting its type annotations, without checking them.
- **Typed results** (8.3): Result types such as Ok<T> and NotFound that list every outcome in an endpoint's signature.
- **TypeScript** (9.3): JavaScript with a static type system added, checked at compile time and removed before running.

### U

- **UDP** (6.3): User Datagram Protocol: independent messages with ports and no delivery guarantees.
- **UI** (background): User interface: the screens and controls people use to work with software.
- **UI thread** (5.4): The single thread that runs a user interface's event loop and alone may touch its controls.
- **Unhandled exception** (2.8): An exception that no catch block handles. It usually ends the process or the request.
- **Unhandled rejection** (8.4): A rejected promise that no code handles. It crashes current versions of Node by default.
- **Unicode** (1.3): One character table for every writing system, with over a million slots.
- **Unidirectional data flow** (9.4): Data moving down the component tree as props, and changes moving up as events.
- **Union type** (9.3): A type that is one of several types, written with |, such as string | null.
- **Union-find** (3.6): A structure that tracks which items are connected and merges groups in nearly constant time.
- **Unique constraint** (7.5): A rule forbidding duplicate values in a column or combination of columns.
- **Unit of work** (7.7): An object that collects changes and writes them all together.
- **Unit test** (4.6): A fast, in-memory test of a small piece of behaviour in isolation.
- **Universal Scalability Law** (12.4): A model in which coordination costs make throughput level off, then fall, as nodes are added.
- **Unix time** (1.2): The number of seconds since 1 January 1970, 00:00 UTC.
- **unknown** (9.3): The type of a value you know nothing about, which must be narrowed before use.
- **Unsafe deserialization** (10.4): Deserializing untrusted data in a way that lets the data choose which types are created.
- **Unsigned integer** (1.2): A whole number with no sign, using every bit for magnitude. With n bits it holds 0 to 2ⁿ − 1.
- **Upcasting** (12.3): Converting an old version of an event into the current shape as it is read.
- **Update anomaly** (7.5): A duplicated fact changed in some places and not others, leaving the data contradictory.
- **URL** (6.6): Uniform resource locator: a scheme, host, optional port, path, query and fragment naming a resource.
- **URL state** (9.4): State kept in the address bar, such as filters and the current page.
- **USE method** (11.6): Measuring utilization, saturation and errors for every resource.
- **User enumeration** (10.1): Discovering which usernames exist from differences in a system's responses.
- **User mode** (1.7): The restricted processor mode ordinary programs run in.
- **Using declaration** (2.6): `using var x = ...;` which disposes x at the end of the enclosing block.
- **Using directive** (0.1): A line such as `using System.Linq;` that makes a namespace's types available by short name.
- **UTC** (background; covered in 1.2): Coordinated Universal Time: the world's reference clock, with no time zone offset or daylight saving.
- **UTF-8** (1.3): The dominant encoding. Uses 1 to 4 bytes per code point and matches ASCII for English text.
- **Utility type** (9.3): A built-in type that builds a new type from another, such as Partial or Omit.
- **Utilization** (12.4): The share of time a resource is busy.
- **UUID** (background; covered in 7.5): Universally unique identifier: the standard name for a GUID.

### V

- **V8** (8.4): Google's JavaScript engine, used by Chrome and Node.js.
- **Value object** (4.4): A small immutable type defined by its values, with no identity, that validates itself.
- **Value type** (1.5): A type that holds its data directly and is copied on assignment. In C#, a struct.
- **ValueTask** (5.3): A task-like type that avoids an allocation when the result is already available.
- **Variable** (2.4): A name for a piece of storage that holds a value.
- **Variance** (2.3): The rules for when one generic type can stand in for another, marked with `in` and `out`.
- **vCPU** (1.4): A virtual CPU sold by cloud providers, usually one hardware thread of a physical core.
- **Vector embedding** (13.1): A list of numbers representing the meaning of text, so that similar meanings are close together.
- **Version control** (11.1): Recording the history of files so past states can be recovered and changes combined and explained.
- **Vertex** (3.6): One of the things in a graph. Also called a node.
- **Vertical scaling** (7.6): Handling more load by moving to a bigger machine.
- **Vertical slice** (8.1): Organizing code by feature, keeping each feature's endpoint, logic and data access together.
- **Vibe coding** (13.2): Accepting AI-generated code without reading or understanding it.
- **Virtual DOM** (9.5): The tree of plain objects a render produces, describing what the DOM should look like.
- **Virtual machine** (1.7): A simulated computer with its own operating system, run by a hypervisor.
- **Virtual memory** (1.7): Each process's private view of memory, translated to physical memory by the processor and kernel.
- **Virtual method** (4.1): A method that derived classes can override, chosen at run time by the object's actual type.
- **Virtual network** (11.5): A private network within a cloud provider, divided into subnets with traffic rules.
- **Visibility** (5.1): Whether a write made by one thread can be seen by another.
- **Visibility timeout** (8.7): How long a received message stays hidden from other consumers before it is redelivered.
- **Vite** (9.5): A fast build tool and development server, the standard starting point for single-page React apps.
- **VM**: see **Virtual machine** (1.7)
- **Volume** (11.3): Storage managed by the container runtime that survives containers being replaced.
- **VPN** (background): Virtual private network: an encrypted tunnel over another network, often used to reach a private network remotely.

### W

- **W3C** (background): World Wide Web Consortium: the body that sets web standards, such as HTML, CSS and Trace Context.
- **WAL**: see **Write-ahead log** (7.1)
- **Weak reference** (2.7): A reference that doesn't keep its object alive.
- **WebApplicationFactory** (8.3): A test helper that runs a whole ASP.NET Core app in memory and provides an HttpClient to call it.
- **WebAssembly** (background): A compact instruction format that browsers can run at near-native speed, used to run languages like C# in the browser.
- **Webhook** (8.2): An HTTP request a server sends to a client-registered URL when an event happens.
- **WebSocket** (9.7): A two-way message channel between browser and server, upgraded from an HTTP connection.
- **Weighted graph** (3.6): A graph whose edges carry a number such as a distance or cost.
- **Weights** (13.1): The adjustable numbers inside a neural network, set by training.
- **Wide-column store** (7.6): A distributed store of huge tables whose rows are placed by partition key and sorted within partitions.
- **Widening conversion** (1.2): Converting a value to a larger type. Usually safe and implicit.
- **Window function** (7.2): A function that computes a value for each row from a related set of rows, without collapsing them.
- **With expression** (2.4): Creating a copy of a record with some properties changed.
- **Word** (1.1): The amount of data a processor handles in one step. 64 bits on current processors.
- **Work factor** (10.5): The adjustable cost, such as an iteration count, of a password hashing function.
- **Work stealing** (5.5): Idle workers taking queued work from busy workers' queues to balance load.
- **Worker thread** (5.4): An extra thread, with its own loop, used to run CPU-heavy work away from the main loop.
- **Working set** (8.6): The data actually in use over a period, which a cache must hold to be effective.
- **Workload identity federation** (11.4): Letting a pipeline get short-lived cloud access from its platform's signed tokens, with no stored secret.
- **Worst case** (3.1): The most expensive input of a given size.
- **Write amplification** (7.3): The extra work each change costs because every index must be updated too.
- **Write barrier** (2.7): A small piece of code run on reference writes so the collector can track old-to-young references.
- **Write skew** (7.4): Two transactions each changing different rows based on overlapping reads, together breaking a rule.
- **Write-ahead log** (7.1): A log where every change is recorded before the data itself is changed, used for crash recovery. Often shortened to WAL.
- **Write-behind cache** (8.6): A cache that accepts writes immediately and writes them to the source later.
- **Write-through cache** (8.6): A cache updated together with the source on every write.

### X

- **XAML** (background): An XML-based language for describing user interfaces in WPF, .NET MAUI and other Microsoft UI frameworks.
- **XML** (background): Extensible Markup Language, an older text format for structured data using tags like <flight>...</flight>.
- **XSS**: see **Cross-site scripting** (10.4)

### Y

- **YAGNI** (4.5): You aren't gonna need it: don't build for needs that don't exist yet.
- **YAML** (11.4): A text format for structured data that uses indentation, used for pipelines and many configuration files.

### Z

- **Zero trust** (10.1): Verifying every request explicitly, rather than trusting anything inside a network.
- **Zone** (6.4): The part of the DNS tree managed by one organization.
- **Zone.js** (9.6): A library that patches browser async APIs so Angular knows when to check for changes.

### Appendix

<p class="src">From <em>.NET Backend Deep Dive</em> · Appendix</p>

<a id="dn-244"></a>

#### F. Glossary

| Term | Meaning |
|---|---|
| **Idempotent** | Calling it many times has the same effect as calling it once |
| **At-least-once** | Messages may arrive more than once but are never lost |
| **Exactly-once** | Essentially doesn't exist in practice; achieved as at-least-once + idempotency |
| **Backpressure** | A mechanism that slows producers when consumers can't keep up |
| **Bulkhead** | Isolating resources so one failure can't sink the whole system |
| **Fail fast** | Return an error immediately instead of letting callers hang |
| **Graceful degradation** | Losing secondary features while core functionality still works |
| **Cold start** | First-run latency (runtime init, JIT, connection setup) |
| **Cache stampede** | Many requests missing the same key and hitting the database together |
| **Captive dependency** | A short-lived service trapped inside a long-lived one |
| **Cartesian explosion** | Joining multiple collections multiplies the row count |
| **SARGable** | A WHERE clause that can use an index |
| **Parameter sniffing** | A plan cached for the first parameters, wrong for later ones |
| **Thread pool starvation** | No free threads because they're blocked; requests queue while CPU idles |
| **Dual write** | Writing to two systems non-atomically (DB + broker) |
| **Fencing token** | A monotonic number tied to a lock, used to reject writes from a stale holder |
| **Expand and contract** | A multi-step migration strategy that avoids downtime |
| **Tail latency (p99)** | The slowest 1% of requests — what users actually feel |

---

*The emphasis throughout is on "why" and "when" rather than syntax — syntax you can look up, but the trade-offs you have to understand.*

### Glossary

<p class="src">From <em>Backend Engineering</em> · C# Types & Data Structures · Glossary</p>

<a id="be-b301"></a>

#### Glossary

- **IL** — the stack-based instruction set the C# compiler emits. Not machine code.
- **Assembly** — a `.dll`/`.exe` holding IL plus metadata describing every type inside it.
- **Metadata** — the self-description that makes reflection and decompilation possible.
- **CLR** — the runtime: loads assemblies, manages the heap, runs the GC, handles exceptions.
- **JIT** — compiles IL to machine code one method at a time, on first call. Why benchmarks need warmup.
- **Managed / unmanaged** — under CLR supervision, or native code outside it.
- **BCL** — the built-in library: `string`, `List<T>`, and everything you didn't write.
- **Devirtualization** — the JIT proving a virtual call has one possible target and calling it directly.
- **Inlining** — replacing a call with the method body. Blocked by virtual dispatch and large bodies.
- **Bounds check** — the runtime range test on every array access; eliminated when the JIT can prove safety.

<!-- -->

- **Value type** — holds its data directly; assignment copies the data. All structs and enums.
- **Reference type** — holds a reference to a heap object; assignment copies the reference.
- **Boxing** — allocating a heap box to hold a value type where a reference is required.
- **Object header** — 16 bytes on x64 (sync block + method table pointer) before any of your fields.
- **Method table** — the type's runtime descriptor; where virtual dispatch looks up the target.
- **Sync block** — per-object slot holding lock state and the identity hash code.
- **Padding / alignment** — bytes inserted so fields sit at natural offsets; why field order changes struct size.
- **Defensive copy** — a copy the compiler makes when calling a non-`readonly` member on a `readonly` struct or `in` parameter.
- **ref struct** — stack-only type. Cannot be a heap field, cannot cross `await` or `yield return`.
- **ref local / ref return** — an alias to existing storage rather than a copy. How you mutate a struct in place.
- **LOH** — Large Object Heap, ≥85,000 bytes, collected with gen 2 and not compacted by default.
- **Cache line** — 64 bytes the CPU fetches at once. Why contiguous beats scattered.
- **Escape analysis** — proving an object never leaves a method so it could live on the stack. .NET does not do this.

<!-- -->

- **Delegate** — a method pointer plus a target object. Immutable, and multicast.
- **Multicast** — one delegate holding several methods. Only the last return value survives; a throw stops the rest.
- **Closure** — a lambda capturing a variable from its enclosing scope.
- **Display class** — the hidden class the compiler generates to hold captured variables. One allocation per invocation.
- **Capture by reference** — a lambda sees the variable, not a snapshot; the cause of the `for`-loop capture bug.
- **static lambda** — compiler-enforced proof that nothing is captured, so nothing is allocated.
- **Expression tree** — code represented as data, walkable and translatable. How `IQueryable` becomes SQL.

<!-- -->

- **const vs static readonly** — compile-time, inlined into callers vs runtime, read from the field. `const` across assemblies is a versioning hazard.
- **new vs override** — hiding (the static type decides) vs overriding (the runtime type decides).
- **sealed** — no further derivation, so the JIT can devirtualize. A free optimization.
- **Explicit interface implementation** — a member reachable only through the interface. Boxes structs.
- **Finalizer** — last-resort cleanup; delays collection and runs at an unpredictable time. Prefer `SafeHandle`.
- **Covariant ( out )** — safe when the type only comes out. **Contravariant (`in`)** — safe when it only goes in. Both directions means invariant.

<!-- -->

- **Amortized O(1)** — total cost over n operations is O(n), so constant on average. Says nothing about the tail.
- **Load factor** — occupancy of a hash table. Chaining degrades gracefully; open addressing collapses past ~80%.
- **Chaining** — collisions form a chain (.NET's choice). **Open addressing** — collisions probe for another slot.
- **Tombstone** — a "deleted but keep probing" marker; why deletion is hard in open addressing.
- **Robin Hood hashing** — swapping entries so probe distances equalize, shortening the worst case.
- **Prime capacity** — mixes high hash bits into the bucket index, so a mediocre `GetHashCode` still spreads.
- **Red-black / AVL tree** — self-balancing BSTs. AVL is stricter: faster reads, more rotations.
- **Rotation** — the local restructuring that restores balance after an insert.
- **Binary heap** — complete tree in an array; children of `i` at `2i+1`, `2i+2`.
- **Trie** — prefix tree; lookup is O(key length), independent of item count.
- **Bloom filter** — probabilistic set: false positives possible, false negatives impossible.
- **Introsort** — quicksort, switching to heapsort when recursion runs deep and insertion sort when small. In-place, unstable.
- **Stable sort** — preserves the relative order of equal elements. `OrderBy` is; `List.Sort` isn't.
- **Structural sharing** — new versions of an immutable tree reuse untouched subtrees by reference.

<!-- -->

- **Deferred execution** — LINQ builds a pipeline; nothing runs until enumerated.
- **Multiple enumeration** — running an expensive pipeline more than once by accident.
- **Materialize** — force a pipeline to run and hold the results (`ToList`, `ToArray`).
- **IQueryable** — builds an expression tree translated by a provider (e.g. to SQL).
- **Iterator state machine** — the compiler transform behind `yield return`; the same mechanism as `async`.
- **Struct enumerator** — why `foreach` over a concrete `List<T>` allocates nothing.
- **Interning** — the runtime's table of unique strings. Literals are interned; interning user input leaks.
- **Code unit / code point / grapheme** — a UTF-16 `char`, a Unicode scalar (`Rune`), and what a human calls a character.
- **Frozen collection** — expensive to build, fastest to read; for lookup tables built once.

------------------------------------------------------------------------

### Glossary

<p class="src">From <em>Backend Engineering</em> · async / await · Glossary</p>

<a id="be-c392"></a>

#### Glossary

Ordered roughly from the machine upward.

- **Register** — a named slot inside the CPU core, ~16 of them, the only place arithmetic happens.
- **Instruction pointer** — register holding the address of the next instruction. "Where the code is."
- **Cache line** — 64-byte unit the CPU moves between RAM and cache. Locality is measured in these.
- **Page** — 4 KB unit of virtual memory mapping. Reserved (address claimed) vs committed (memory backed).
- **System call** — controlled trap from user mode into the kernel. ~100–500 ns each way.
- **Context switch** — saving one thread's registers and loading another's. 1–10 µs direct, more in lost cache.

<!-- -->

- **Stack** — per-thread LIFO memory for call frames. Fast, ordered, dies on return, thread-exclusive.
- **Stack frame** — one call's parameters, locals, and return address. Cannot be extracted from the middle.
- **Heap** — shared, unordered object memory. Survives the creating call; reachable from any thread.
- **Reference** — an 8-byte address pointing at a heap object. Lives wherever you put it.
- **Boxing** — copying a value type onto the heap to be referenced as `object`. One allocation.
- **Gen 0/1/2** — GC generations. Gen 0 collection is nearly free; gen 2 is the expensive one.
- **LOH** — Large Object Heap, ≥85,000 bytes, collected with gen 2.
- **Root** — something the GC treats as definitely alive: stack locals, statics, registers, handles.

<!-- -->

- **Thread** — one execution path: instruction pointer, registers, stack. Shares the heap with its siblings.
- **Blocked** — thread state where the scheduler skips it entirely. Zero CPU, full memory cost.
- **Thread pool** — reusable worker + I/O completion threads. Injects only ~1–2/sec above `MinThreads`.
- **Work stealing** — idle pool threads taking work from other threads' local queues.
- **Starvation** — no free pool threads while CPU is idle. The signature failure of sync-over-async.
- **IOCP / epoll / io_uring** — OS completion mechanisms. Where "waiting without a thread" actually lives.

<!-- -->

- **Task** — a handle to a future outcome. Not a thread, not a unit of work.
- **ValueTask** — allocation-avoiding Task substitute for usually-synchronous operations. Await exactly once.
- **Awaitable** — anything with `GetAwaiter()`. Structural, no interface required.
- **Awaiter** — the object with `IsCompleted`, `GetResult()`, `OnCompleted()`.
- **State machine** — compiler-generated class/struct holding hoisted locals plus a state bookmark.
- **MoveNext** — the resumable entry point. Called once per episode, by whoever completed the operation.
- **Builder** — `AsyncTaskMethodBuilder` etc. Owns the Task, boxes the state machine, sets result/exception.
- **AsyncStateMachineBox** — the heap object that is simultaneously the boxed state machine *and* the Task.
- **Continuation** — the callback that resumes a suspended state machine.
- **Inlining** — running a continuation on the completing thread instead of queuing it.
- **Elision** — dropping `async`/`await` from a pass-through method. Saves a state machine, loses a frame.
- **Fast path** — `IsCompleted` already true, so no suspension, no allocation, no thread hop.

<!-- -->

- **SynchronizationContext** — abstraction for "post this back to the right place." Controls *where you resume*. Absent in ASP.NET Core.
- **ExecutionContext** — ambient state (`AsyncLocal`, impersonation) that flows across awaits regardless of `ConfigureAwait`.
- **ConfigureAwait(false)** — skip context capture. Changes the resuming thread, never ambient flow.
- **AsyncLocal\<T\>** — per-logical-flow state. Copy-on-write, propagates downward only.
- **Sync-over-async** — `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`. Deadlocks under a context, starves without one.
- **Fake async** — sync work wrapped in `Task.Run` and presented as async. Hides thread cost from callers.
- **async void** — no Task to store an exception in, so it crashes the process. Event handlers only.
- **Backpressure** — a fast producer being slowed by a slow consumer. Bounded channels; the alternative is OOM.
- **Cooperative cancellation** — nothing is forcibly stopped; code must check the token.

------------------------------------------------------------------------

`await` compiles to *"if this isn't finished, copy my locals to a heap object, note where I am, register that object to be called back, and return"* — and every downstream oddity, from deadlocks to null `HttpContext` to vanishing stack traces to `ValueTask` corruption, is a consequence of who calls `MoveNext`, on which thread, and how many times.

------------------------------------------------------------------------

*Caveats: code here is illustrative and uncompiled. API surface reflects knowledge through May 2026 — verify .NET 9/10-era specifics against current docs. Guidance encodes judgment calls, not universal consensus; the Framework-specific sections in particular assume a codebase shaped like a typical ServiceStack/Akka.NET legacy platform and may not match yours.*

<div class="also">

APIs → C# types → Caching → all references →

</div>

### Glossary

<p class="src">From <em>Backend Engineering</em> · APIs · Glossary</p>

<a id="be-e557"></a>

#### Glossary

- **IP** — best-effort packet delivery between machines. No guarantees.
- **TCP** — reliable ordered byte stream built on IP. Handshake costs 1 RTT.
- **Port** — 16-bit number identifying a program on a machine.
- **Socket** — one connection endpoint: (src IP, src port, dst IP, dst port).
- **Ephemeral port** — temporary client-side port. ~28k available; `TIME_WAIT` holds them ~4 min after close.
- **DNS** — name → IP resolution. Cached by TTL, which is why IP changes propagate slowly.
- **TLS** — encryption, integrity, and server authentication. mTLS adds client authentication.
- **RTT** — round-trip time. The unit that actually matters for API performance.

<!-- -->

- **Idempotent** — N calls ≡ 1 call. The property that makes retries legal.
- **Safe** — no state change. `GET`, `HEAD`, `OPTIONS`.
- **Stateless** — each request carries everything needed; no server-side session affinity.
- **ETag** — version fingerprint enabling `304 Not Modified` and optimistic concurrency.
- **Preflight** — the `OPTIONS` request browsers send before non-simple cross-origin calls.

<!-- -->

- **REST** — architectural style; colloquially just "JSON over HTTP with nouns and verbs."
- **HATEOAS** — responses carry links to next actions. The REST constraint nobody implements.
- **RPC** — call a remote function. gRPC is the modern binary form.
- **GraphQL** — client specifies the response shape. Solves over-fetching, complicates caching.
- **Webhook** — the server calls you when something happens. Inverted polling.
- **SSE** — one-way server→client stream over plain HTTP.
- **WebSocket** — full-duplex persistent connection.

<!-- -->

- **Bearer token** — whoever holds it can use it. Requires TLS and short lifetimes.
- **JWT** — self-contained signed token. Verifiable without a lookup; **not revocable** before expiry.
- **Scope** — what a token is permitted to do.
- **BOLA / IDOR** — accessing another user's object because ownership wasn't checked. OWASP API \#1.
- **CORS** — browser rule restricting cross-origin reads. Not server-side security.
- **CSRF** — attack exploiting automatically-attached credentials. Cookies vulnerable; header tokens not.

<!-- -->

- **Idempotency key** — client-supplied ID making a `POST` safely retryable.
- **Exponential backoff** — increasing retry delays. **Jitter** randomizes them to prevent thundering herds.
- **Thundering herd** — synchronized retries killing a recovering service.
- **Circuit breaker** — stop calling a failing dependency so it can recover.
- **Retry budget** — cap retries as a fraction of total traffic.
- **N+1** — one list call followed by one call per item. The classic API performance bug.
- **Cursor pagination** — opaque "where I left off" marker. Stable and constant-time, unlike offset.
- **Problem Details** — RFC 9457 standard error body.
- **OpenAPI** — machine-readable API description; generates docs, clients, mocks, validation.

------------------------------------------------------------------------

### Glossary

<p class="src">From <em>Backend Engineering</em> · Caching · Glossary</p>

<a id="be-f624"></a>

#### Glossary

- **Cache** — a second copy of data placed somewhere cheaper to reach.
- **Hit / miss** — found in cache / not found.
- **Hit rate** — hits ÷ total lookups. Improvement scales with the *miss* rate.
- **Working set** — the data actually being accessed in a window. Cache size is judged against this.
- **Locality** — temporal (recently used will be used again) and spatial (nearby data will be used).
- **Zipf / power law** — the skew that makes caching work. Top 1% of keys, 30–50% of traffic.
- **Cache line** — 64-byte unit the CPU fetches. Why sequential beats scattered.
- **False sharing** — two threads writing different variables in one cache line, invalidating each other.

<!-- -->

- **TTL** — time to live; how long an entry is trusted.
- **Divergence window** — how long a copy may disagree with the source. The core quantity.
- **Jitter** — randomizing TTLs so entries don't expire together.
- **Absolute vs sliding expiry** — from creation vs from last access. Sliding does not bound staleness.
- **Eviction** — removal due to size pressure. **Expiry** — removal due to age.
- **LRU / LFU / W-TinyLFU / ARC** — eviction policies. W-TinyLFU is the modern default.
- **Belady's optimal** — evict what's used furthest in the future. The unreachable benchmark.

<!-- -->

- **Cache-aside** — app checks cache, loads on miss, populates. The dominant pattern.
- **Read-through / write-through / write-behind / write-around** — who writes to the cache and when.
- **Refresh-ahead** — refresh before expiry so users never see a miss.
- **Versioned key** — change the key instead of invalidating. Atomic, cross-layer, no purge to fail.
- **Dual write** — updating two systems without a shared transaction. Source of permanent staleness.
- **CDC** — change data capture; deriving invalidations from the DB log so they can't be missed.

<!-- -->

- **Stampede / dogpile** — many concurrent misses on one expired key hitting the source at once.
- **Single-flight / coalescing** — one caller computes, the rest await that result.
- **Penetration** — repeated misses for keys that don't exist. Fixed by negative caching or Bloom filters.
- **Avalanche** — mass simultaneous expiry, or the cache itself failing.
- **Hot key** — one key taking disproportionate traffic; sharding doesn't help.
- **Poisoning** — wrong data cached and served to everyone.
- **Fail open** — on cache error, go to the source. The right default, with a circuit breaker.

<!-- -->

- **Freshness vs validation** — "good until T" (no request) vs "still good?" (request, maybe 304).
- **no-cache vs no-store** — revalidate before use vs never store at all.
- **private vs public** — browser-only vs shareable. A security boundary.
- **s-maxage** — max-age for shared caches only.
- **ETag** — version fingerprint. Strong (byte-identical) or weak (`W/`, semantically equal).
- **Vary** — request headers that form part of the cache key.
- **stale-while-revalidate** — serve stale instantly, refresh behind. **stale-if-error** — serve stale when the origin fails.
- **Origin shield** — one parent node so N edge misses become one origin request.
- **Surrogate key** — a tag on a response enabling tag-based CDN purging.

<!-- -->

- **Content hashing** — filename contains a hash of contents, so changes produce new URLs. Versioned keys for assets.
- **bfcache** — frozen page state for instant back-navigation.
- **staleTime vs gcTime** — how long data is fresh vs how long unused data is retained.
- **Normalized cache** — entities stored by ID and shared across queries, vs a document cache storing whole responses.
- **Optimistic update** — apply the change in the UI before the server confirms; roll back on failure.

------------------------------------------------------------------------
