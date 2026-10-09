---
id: "7-6"
number: "7.6"
title: "Non-relational stores"
part: 7
part_title: "Storing data"
words: 16218
minutes: 71
---

# 7.6 Non-relational stores

> **Quick look.** Non-relational stores: what each kind is good at, and what it gives up.
>
> - Key-value for fast lookups, documents for nested data shaped by access patterns, wide-column for huge write loads.
> - Graph databases follow relationships, time-series stores handle measurements, search engines do forgiving full-text queries.
> - Design documents around aggregates; unbounded arrays inside documents are a classic failure.
> - Redis is a data-structure server on a single thread: avoid big keys and hot keys; separate caches from sessions.
> - DynamoDB and Cosmos DB need access patterns designed first; hot partition keys throttle you.
> - Replicas lag and shards need an even key. Start relational: most "we need NoSQL" cases are really "we need an index".
> - Every extra store adds backups, restores, runbooks and consistency boundaries.


### The big picture

Relational databases are the right default for most business data, but not for every job. A cache that must answer in a fraction of a millisecond, billions of engine sensor readings a day, a search box that forgives typos, or a web-scale service spread across continents each strain what one relational server does well. Since the mid-2000s, a family of other stores has grown up for such jobs, loosely called NoSQL.

This chapter explains the main kinds: key-value stores, document databases, wide-column stores, graph databases, time-series databases and search engines. For each, it covers what they are good at and what they give up. It then covers the two techniques behind almost every large data system, relational or not: copying data to several machines, and splitting it across them, along with the consistency trade-off that comes with both. The point isn't to replace SQL Server, but to know when a second tool earns its place.

### Before you start

**Builds on:** 3.6 Graphs, 3.7 Searching and sorting, 3.3 Hash tables.

**Ideas explained later:**

- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Rate limiting** (8.2): Capping how many requests a client may make in a period.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Region** (11.5): A geographic area where a provider has its own data centres.
- **Availability** (10.1): A system works when legitimate users need it.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, lambdas, LINQ, null operators, tuples, using for cleanup, async and await, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a key-value store good for, and what can't it do well?**

> Very fast reads and writes of a value by its key: caching, sessions, counters, rate limits. It can't answer queries about the values themselves, such as "all sessions for users in Germany", without extra work.

**2. In a document database, when should related data be embedded in one document?**

> When it is always read together with its parent and belongs only to it, such as a flight's legs. Data that is shared, large or changes independently, such as passengers, is better referenced by id.

**3. What does schema-on-read mean?**

> The database doesn't enforce a structure when data is written; each reader interprets the fields it finds. Documents can vary, and code must handle missing or old-format fields.

**4. What is replication lag, and what problem does it cause?**

> The delay before a change on the primary reaches a replica. A user who writes and then reads from a replica may not see their own change, which feels like the change was lost.

**5. What is sharding, and what makes a good shard key?**

> Splitting data across several machines by a key, each machine holding a share. A good key spreads load evenly and keeps data that is queried together on the same shard.

**6. What does eventually consistent mean?**

> If writes stop, all copies of the data will end up the same, but for a while different copies can return different answers.

**7. Why shouldn't a search engine like Elasticsearch be the only copy of your data?**

> Search engines are built for fast, forgiving queries, and are usually filled from a source of truth. They typically lack full transactions and constraints, and indexing is often slightly delayed. Keep the authoritative data in a primary database and rebuild the search index from it.

### Why other stores exist

By the mid-2000s, companies like Google and Amazon had more data and traffic than any single database server could hold. Relational databases of the time scaled mainly by buying a bigger machine, called **vertical scaling**, and that has a ceiling and a steep price. Their answer was **horizontal scaling**: many ordinary machines, each holding part of the data. Google's Bigtable paper in 2006 and Amazon's Dynamo paper in 2007 described stores built for that from the start, and inspired a generation of databases.

To spread across machines easily, these stores gave things up: joins, multi-row transactions, a fixed schema, or immediate consistency between copies. The name **NoSQL** stuck, although many now speak SQL-like languages, and many relational databases have borrowed their ideas back.

Each kind below solves a specific problem well. None is simply "better" than a relational database.

### Key-value stores

A **key-value store** is a giant, fast dictionary: store a value under a key, get it back by the key. That is nearly all it does, and it does it very quickly, often from memory, in well under a millisecond.

Redis is the best-known example, and more than a plain dictionary. Its values can be strings, lists, sets, hashes and sorted sets, with atomic operations on each, such as incrementing a counter or adding to a leaderboard. Keys can expire after a set time. Common uses are caching, covered in Caching (8.6), user sessions, rate limiting, locks shared between servers and queues. Amazon DynamoDB and Azure Cosmos DB also work well as key-value stores at very large scale.

The limit is that you can only find things by their key. "All sessions that belong to users in Germany" needs a separate structure you maintain yourself. And an in-memory store may lose recent writes when a server fails, depending on how it is configured to save data.

### Document databases

A **document database** stores records as documents, usually JSON, each with its own nested structure: a flight document can contain its aircraft, its crew list and its legs, all in one record. MongoDB, Azure Cosmos DB and Couchbase are common examples.

Documents map naturally to objects in code, and one read fetches everything about an item. The database doesn't require every document to have the same fields. That is **schema-on-read**: structure is applied by the code that reads the data, not enforced when it is written. It makes early development fast, and it moves the job of handling missing, renamed and old-format fields into every reader.

Design is driven by **access patterns**: the specific questions the application will ask. Data read together should usually live together, so a flight's legs are embedded in the flight document. Data that is shared, large, or changes on its own, such as passenger profiles, is referenced by id instead, as with a foreign key, except that the database won't check the reference.

Document databases handle single-document changes atomically. Transactions across several documents are supported in some, with limits and extra cost. Joins are weak or missing. When the questions change after the documents are designed, the data often has to be reshaped.

### Wide-column stores

A **wide-column store** such as Apache Cassandra, ScyllaDB or Google Bigtable holds huge tables spread across many machines. Each row is found by a **partition key**, which decides the machine it lives on, and rows within a partition are kept sorted by further key columns. Reading one partition, such as "all position reports for this aircraft today, in time order", is very fast. Writes are extremely fast, because they are appended.

The price is that you design tables around queries, one table per query pattern, often duplicating data between them. There are no joins, and queries that don't start from a partition key are slow or impossible. These stores suit enormous, write-heavy, append-mostly data: messaging, event logs, telemetry.

### Graph databases

A **graph database** stores vertices and edges, from Graphs (3.6), as first-class data, and answers questions about connections: "which passengers share a phone number or card with this flagged booking, up to three steps away?". Neo4j is the best known, queried with a language called Cypher. Following relationships is fast however deep the path, while the same query in SQL needs repeated self-joins or recursive CTEs that grow slow and hard to write. Fraud detection, recommendations, network planning and access-rights systems are typical uses.

### Time-series databases

A **time-series database** is built for measurements that arrive continuously, each with a timestamp: server metrics, stock prices, sensor readings. Modern aircraft engines report hundreds of parameters, and health-monitoring systems analyse those streams to spot faults before they happen. Time-series stores such as InfluxDB, Prometheus and TimescaleDB, an extension to PostgreSQL, compress this data heavily, answer range and aggregation queries quickly, keep high-detail data only for a while, and summarize older data into averages.

### Search engines

Full-text search, "find flights and passengers matching 'heathrow smiht'", is poorly served by `LIKE` queries. A search engine such as Elasticsearch or OpenSearch builds an inverted index, from Searching and sorting (3.7), over the words in each document. It ranks results by relevance, handles typos and word forms, and filters by many fields at once. SQL Server and PostgreSQL have built-in full-text search that covers simpler needs.

A search engine is usually a secondary store: data is copied into it from the primary database, and the index is rebuilt from there if needed. Treating it as the only copy of important data is a common mistake.

### Relational databases with document features

The line between kinds has blurred. PostgreSQL's `jsonb` type stores JSON with indexes over its contents. SQL Server has JSON functions and, in recent versions, a native JSON type. SQLite has JSON functions built in. So a relational database can hold flexible documents alongside ordinary tables, with transactions and constraints still available. For many applications, that is enough to avoid adding a second database at all.

This program stores flight documents in SQLite and queries inside them.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// documents.cs — run with: dotnet run documents.cs
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

using var db = new SqliteConnection("Data Source=:memory:");
db.Open();
Run("CREATE TABLE flights (id INTEGER PRIMARY KEY, body TEXT NOT NULL)");   // one JSON document per row

object[] documents =
[
    new { number = "BA117", origin = "LHR", destination = "JFK", aircraft = new { type = "B777", registration = "G-VIIA" }, crew = new[] { "Ada", "Grace" }, meal = "vegetarian" },
    new { number = "QF1", origin = "SYD", destination = "LHR", aircraft = new { type = "A380", registration = "VH-OQA" }, crew = new[] { "Linus" }, stops = new[] { "SIN" } },
    new { number = "SQ321", origin = "SIN", destination = "LHR", aircraft = new { type = "A350", registration = "9V-SMA" }, crew = Array.Empty<string>() },
];
foreach (object doc in documents) Run("INSERT INTO flights (body) VALUES ($body)", JsonSerializer.Serialize(doc));

Show("SELECT json_extract(body, '$.number'), json_extract(body, '$.aircraft.type') FROM flights WHERE json_extract(body, '$.destination') = 'LHR'");
// QF1 | A380
// SQ321 | A350

Show("SELECT json_extract(body, '$.number'), json_extract(body, '$.stops[0]') FROM flights");
// BA117 | NULL   <- schema-on-read: documents differ, and a missing field is just NULL
// QF1 | SIN
// SQ321 | NULL

Show("SELECT json_extract(body, '$.number'), json_array_length(body, '$.crew') FROM flights");
// BA117 | 2    QF1 | 1    SQ321 | 0

Run("CREATE INDEX ix_flights_number ON flights (json_extract(body, '$.number'))");   // an index on a field inside the document
Show("EXPLAIN QUERY PLAN SELECT body FROM flights WHERE json_extract(body, '$.number') = 'QF1'", column: 3);
// SEARCH flights USING INDEX ix_flights_number (<expr>=?)

void Run(string sql, string? body = null)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    if (body is not null) cmd.Parameters.AddWithValue("$body", body);
    cmd.ExecuteNonQuery();
}

void Show(string sql, int? column = null)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    while (r.Read())
        Console.WriteLine(column is int c ? r.GetString(c)
            : string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i))));
}
```

### Copies: replication

Whatever the kind of store, two techniques let data grow beyond one machine. The first is **replication**: keeping copies of the same data on several machines. It serves three purposes. If one machine fails, another has the data. Reads can be spread across the copies. And a copy can sit in another region, close to users there.

The usual arrangement has one primary, which accepts writes, and one or more replicas, which receive a stream of its changes and serve reads. A replica used to take read traffic off the primary is a **read replica**. SQL Server Always On availability groups, PostgreSQL streaming replication, and the replicas of managed cloud databases all work this way.

Replication can be synchronous: the primary waits until a replica confirms each change before reporting the commit. No committed data is lost if the primary fails, but every write waits for the network. Or it can be asynchronous: the primary commits at once and sends changes afterwards. Writes are fast, but replicas lag behind, and a primary that fails may take its most recent commits with it.

**Replication lag** is the delay before a change reaches a replica: usually milliseconds, sometimes seconds under load. It produces a classic bug. A user updates their seat, the page reloads, the read goes to a replica, and the old seat is shown. The fix is **read-your-writes consistency**: after a user writes, send that user's reads to the primary for a while, or wait until the replica has caught up with that write.

### Splitting: sharding

The second technique is **sharding**: splitting data across several machines, each holding only its share. Writes and storage then scale with the number of machines, which replication alone can't do. Each shard is usually replicated as well.

The choice of shard key is the most important decision. The key decides where each record lives, usually through a stable hash, as in Hash tables (3.3), or through ranges of values. A good key spreads load evenly and keeps data that is queried together on the same shard. Booking data might be sharded by booking reference, spreading evenly. Sharding by departure date would send all of today's writes to one machine: a **hot partition**, where one shard gets most of the load while the others sit idle.

Queries that need data from every shard, such as "all bookings for passenger X" when shards are by booking reference, must ask every shard and combine the results. Moving data when shards are added is expensive, which is why consistent hashing, from Hash tables (3.3), is used to limit how much has to move.

```csharp
// replicas.cs — run with: dotnet run replicas.cs
using System.Collections.Concurrent;
using System.Linq;
using System.Text;

// 1. Asynchronous replication: changes reach the replica after a delay
var primary = new Store("primary");
var replica = new Store("replica");
primary.Changed += (key, value) => _ = Task.Run(async () => { await Task.Delay(200); replica.Apply(key, value); });

primary.Set("BA117:gate", "A10");
await Task.Delay(300);                                    // the first value reaches the replica
primary.Set("BA117:gate", "B12");                         // the gate changes
Console.WriteLine($"primary: {primary.Get("BA117:gate")}, replica: {replica.Get("BA117:gate")}");   // primary: B12, replica: A10
await Task.Delay(300);
Console.WriteLine($"a moment later the replica says {replica.Get("BA117:gate")}");                 // B12: eventually consistent

// 2. Sharding: a stable hash of each key picks one of four shards
Store[] shards = Enumerable.Range(0, 4).Select(i => new Store($"shard {i}")).ToArray();
for (int i = 0; i < 1000; i++) shards[ShardFor($"booking-{i}", shards.Length)].Set($"booking-{i}", "...");
Console.WriteLine(string.Join(", ", shards.Select(s => $"{s.Name}: {s.Count}")));   // shard 0: 249, shard 1: 251, shard 2: 251, shard 3: 249
Console.WriteLine($"booking-42 lives on shard {ShardFor("booking-42", 4)}");        // shard 1, on every machine and every run

static int ShardFor(string key, int shardCount)
{
    uint hash = 2166136261;                                // FNV-1a: simple, fast and stable, unlike string.GetHashCode
    foreach (byte b in Encoding.UTF8.GetBytes(key)) { hash ^= b; hash *= 16777619; }
    return (int)(hash % (uint)shardCount);
}

sealed class Store(string name)
{
    readonly ConcurrentDictionary<string, string> data = new();
    public string Name => name;
    public int Count => data.Count;
    public event Action<string, string>? Changed;
    public void Set(string key, string value) { data[key] = value; Changed?.Invoke(key, value); }
    public void Apply(string key, string value) => data[key] = value;   // a replica applying the primary's change
    public string? Get(string key) => data.TryGetValue(key, out var v) ? v : null;
}
```

### Consistency

Replication and sharding force a choice about consistency. With one database server, a read after a commit always sees the commit. With copies on several machines, keeping every copy identical at every instant means every write must wait for all of them, and a failed network link means refusing writes. Many distributed stores therefore offer **eventual consistency**: if writes stop, all copies converge to the same value, but for a while different copies can give different answers. The replica in the example is eventually consistent.

Many stores let you choose per operation. Cosmos DB offers five consistency levels, from strong to eventual. Cassandra lets each query say how many replicas must answer. The trade-off between consistency, availability and tolerance of network failures is the subject of the CAP theorem, covered in Distributed systems basics (12.2).

### Choosing

Using several kinds of store in one system, each for what it does best, is called **polyglot persistence**. A typical system might keep bookings in SQL Server, sessions and caches in Redis, search in Elasticsearch and engine telemetry in a time-series database.

Every extra store has a cost: another system to run, secure, back up, monitor and keep in step with the others, and another set of failure modes. A reasonable approach is to start with a relational database, use its JSON and full-text features where they fit, and add a specialized store only when a real, measured need appears that the relational database handles badly.

### In short

Non-relational stores trade features such as joins, transactions or fixed schemas for scale or speed in a specific job. Key-value stores give fast lookups by key, document databases store nested JSON designed around access patterns, and wide-column stores handle enormous write-heavy data by partition key. Graph databases follow relationships, time-series databases handle timestamped measurements, and search engines do forgiving full-text queries. Replication copies data for safety and read scale, with lag on asynchronous replicas; sharding splits data by a key, which must spread load evenly. Distributed copies are often only eventually consistent. Start relational, and add stores for measured needs.

### What goes wrong

**Choosing a NoSQL store for fashion.** A document database is picked for a highly relational domain, and joins and consistency get rebuilt badly in application code. Choose by access patterns and consistency needs.

**Documents that grow without limit.** Embedding every booking inside a passenger document makes documents huge and slow to update. Embed only bounded, owned data.

**Ignoring schema-on-read.** New code crashes on old documents that lack a field. Handle missing fields, version documents and migrate old ones deliberately.

**Reading your own write from a replica.** Users see their change disappear after saving. Route a user's reads to the primary after they write, or wait for the replica to catch up.

**A hot shard key.** All of today's traffic lands on one shard because the key is a date. Choose keys that spread writes, such as an id or a hash.

**The search index as the source of truth.** A corrupted or lagging index can't be rebuilt, because it was the only copy. Keep authoritative data in a primary store.

**Caches with no plan for failure.** An in-memory store restarts empty, and every request falls through to the database at once. Plan for a cold cache, as Caching (8.6) explains.

### Cost model

An in-memory key-value lookup costs about a network round trip: well under a millisecond in the same data centre. A document read by id is similar, plus disk time if it isn't cached. Queries that aren't served by the store's key or indexes can be orders of magnitude slower, or scan everything. Synchronous replication adds a round trip to the replica to every commit; asynchronous replication adds nothing to commits but leaves a window of lag. Cross-shard queries cost as many requests as there are shards. The largest cost is often operational: each store needs its own backups, monitoring and expertise.

### In other languages (optional)

**.NET.** StackExchange.Redis is the standard Redis client, and `Microsoft.Extensions.Caching.StackExchangeRedis` plugs it into ASP.NET Core's caching. The MongoDB and Cosmos DB SDKs support LINQ queries. EF Core has a provider for Cosmos DB, and can map JSON columns in SQL Server and PostgreSQL.

**JavaScript and TypeScript.** Node.js has first-class drivers for MongoDB, Redis and DynamoDB, and JSON documents map directly to JavaScript objects.

**Python.** redis-py, PyMongo and boto3 for DynamoDB are standard; data work often reads time-series and document data with pandas.

**Every language.** The data model, not the language, decides what is easy. A query pattern the store wasn't designed for is hard from any client.

### Key terms

- **Vertical scaling**: Handling more load by moving to a bigger machine.
- **Horizontal scaling**: Handling more load by adding more machines, each doing part of the work.
- **NoSQL**: The loose name for non-relational databases built for particular scale or data needs.
- **Key-value store**: A store that saves and fetches values by key, very fast, with little querying beyond that.
- **Document database**: A store that keeps records as nested JSON-like documents.
- **Schema-on-read**: Applying structure when data is read rather than enforcing it when data is written.
- **Access pattern**: A specific question an application asks of its data, used to drive data design.
- **Wide-column store**: A distributed store of huge tables whose rows are placed by partition key and sorted within partitions.
- **Partition key**: The value that decides which machine or partition a record lives on.
- **Graph database**: A store that keeps vertices and edges as data and queries paths between them.
- **Time-series database**: A store built for timestamped measurements that arrive continuously.
- **Replication**: Keeping copies of the same data on several machines.
- **Read replica**: A copy of a database that serves reads, taking load off the primary.
- **Replication lag**: The delay before a change on the primary appears on a replica.
- **Read-your-writes consistency**: The guarantee that users always see their own latest writes.
- **Sharding**: Splitting data across several machines by a key, each holding a share.
- **Hot partition**: A shard or partition that receives far more load than the others.
- **Eventual consistency**: The guarantee that copies converge once writes stop, though they may differ for a while.
- **Polyglot persistence**: Using several kinds of data store in one system, each for what it does best.

### Where this leads

Working with relational data from C# is in ORMs and EF Core (7.7). Using Redis and other caches safely is in Caching (8.6). Consistency, consensus and the CAP theorem are in Distributed systems basics (12.2), and scaling whole systems across machines is in Scaling and performance (12.4). Keeping stores in step with each other through events is in Messaging and events (12.3).

### Check yourself

1. Add a document to `documents.cs` with a different shape, such as `crew` stored as a comma-separated string. Show what `json_array_length` returns for it, and decide how readers should cope.
2. In `replicas.cs`, add a `ReadYourWrites` method that reads from the primary for one second after a key is written, and from the replica otherwise.
3. Change `ShardFor` to shard by the first letter of the key instead of a hash, and explain the new distribution.
4. Design the documents for a flight-tracking app that shows each aircraft's last 24 hours of positions. Decide what goes in one document and what is referenced.
5. List every data store a system you know uses. For each, note what it holds, whether it is the source of truth, and what happens if it is lost.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A tenant that throttled everyone in Cosmos DB

With partition key `/tenantId`, one large customer generates 80% of writes and requests start failing with 429.

*A key that spreads the load*

```csharp
// Before: PartitionKey = tenantId   (one hot partition)
// After: hierarchical key, so one tenant spreads across many physical partitions
var props = new ContainerProperties("events", partitionKeyPaths: ["/tenantId", "/day"]);
await db.CreateContainerIfNotExistsAsync(props);
await container.CreateItemAsync(evt, new PartitionKeyBuilder().Add(evt.TenantId).Add(evt.Day).Build());
```

> **Lesson:** In partitioned stores, design the key from the access pattern and the load distribution, not just the data model.

#### Example 2: Sessions evicted from a shared Redis

Users get logged out at peak. Redis is near its memory limit with `allkeys-lru`, so it evicts session keys along with cache keys.

*Separate by purpose*

```text
redis-cache     maxmemory-policy allkeys-lru     (evicting is fine)
redis-sessions  maxmemory-policy noeviction      (fail writes loudly instead of losing data)
                plus persistence and a replica
```

> **Lesson:** A cache and a store of record need different eviction rules. Don't mix them in one Redis.

### Advanced Redis

<p class="src">From <em>.NET Backend Deep Dive</em> · Advanced Redis</p>

<a id="dn-77"></a>

#### Configuring StackExchange.Redis correctly

```csharp
// The ConnectionMultiplexer MUST be a SINGLETON. Creating one per request is a disaster.
services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(new ConfigurationOptions
    {
        EndPoints = { "redis:6379" },
        AbortOnConnectFail = false,       // allow reconnecting if Redis starts after the app
        ConnectTimeout = 5000,
        SyncTimeout = 1000,
        ConnectRetry = 3,
        KeepAlive = 60
    }));
```

The multiplexer pipelines many commands over **one connection**. That's why you don't need a connection pool.

<a id="dn-78"></a>

#### Picking the right data structure

| Structure | Good for |
|---|---|
| String | Cached JSON objects, counters (`INCR`) |
| Hash | Multi-field objects where you update one field without rewriting the whole object |
| Set | Membership tests, permissions |
| Sorted Set | **Leaderboards, sliding-window rate limits, delayed queues** |
| List | Simple queues (`LPUSH`/`BRPOP`) |
| HyperLogLog | Approximate unique counts (unique visitors) in ~12KB |
| Stream | Event logs with consumer groups (a miniature Kafka) |
| Bitmap | Per-user boolean state (read/unread) |

```csharp
// Sliding-window rate limit with a sorted set - make it atomic with Lua
// ZREMRANGEBYSCORE key 0 (now - window)
// ZCARD key -> reject if >= limit
// ZADD key now uuid ; EXPIRE key window
```

<a id="dn-79"></a>

#### Eviction policy — get it wrong and you lose data

```
maxmemory 2gb
maxmemory-policy allkeys-lru
```

| Policy | Behavior |
|---|---|
| `noeviction` | Writes **fail** when full. For when Redis is a data store, not a cache |
| `allkeys-lru` | Evict least recently used — **the sensible default for a pure cache** |
| `allkeys-lfu` | By frequency; better when you have stable hot keys |
| `volatile-lru` | Only evicts keys with a TTL — dangerous if untagged keys fill memory |

**If Redis serves both cache and sessions/locks**, split it into two instances (or at least two databases) with different policies, or your sessions get evicted mid-flight.

<a id="dn-80"></a>

#### Big keys and hot keys

- **Big key**: one key holding a million-element list means `LRANGE` blocks all of Redis (it's single-threaded) for hundreds of milliseconds. Split it.
- **Hot key**: one key absorbing half your traffic overloads a single cluster node. Fix it with an L1 memory cache in front of Redis (that's exactly what **HybridCache** does), or shard the key into `key:0..9` and pick at random.
- Redis executes commands **single-threaded**: one slow command (`KEYS *`, `FLUSHALL`, a long Lua script) stalls everyone. **Never run `KEYS` in production** — use `SCAN`.

<a id="dn-81"></a>

#### A backplane for invalidating L1 caches

```csharp
// On update: publish an invalidation to every pod
await _sub.PublishAsync("cache:invalidate", key);

// Each pod subscribes and clears its own L1
_sub.Subscribe("cache:invalidate", (_, key) => _memoryCache.Remove(key.ToString()));
```

This is the mechanism `HybridCache` uses underneath. Note that Redis pub/sub is **fire-and-forget** — a restarting pod misses the message, so L1 still needs a short TTL as a self-correcting fallback.

---

### Choosing a Datastore

<p class="src">From <em>.NET Backend Deep Dive</em> · Choosing a Datastore</p>

<a id="dn-178"></a>

#### Relational is the default, and that's fine

Start with Postgres or SQL Server unless you have a concrete reason not to. You get transactions, constraints, joins, ad-hoc queries, mature tooling, and a hiring pool. Most "we need NoSQL" cases are actually "we need an index."

**Postgres specifics worth knowing** if you're coming from SQL Server:

```sql
-- JSONB is a genuinely good document column, and indexable
CREATE INDEX idx_meta ON orders USING GIN (metadata jsonb_path_ops);

-- Partial and expression indexes
CREATE INDEX ON orders (customer_id) WHERE NOT is_deleted;

-- Upsert that actually works
INSERT INTO settings (key, value) VALUES (@k, @v)
ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;

-- Full-text search without another system
SELECT * FROM docs WHERE to_tsvector('english', body) @@ plainto_tsquery(@q);
```

Operational differences that catch people: **MVCC means `UPDATE` writes a new row version**, so update-heavy tables bloat and depend on autovacuum; `VACUUM`/bloat monitoring is a real duty. And transaction ID wraparound is a genuine outage class if autovacuum is disabled.

<a id="dn-179"></a>

#### When a document/key-value store earns its place

**DynamoDB and Cosmos DB are not "schemaless SQL."** They're systems where you design the access patterns first and the data model second.

```
Partition key = the unit of scale, throughput and atomicity.
You can query efficiently ONLY by partition key (plus sort key range).
Anything else is a scan, or a secondary index you must design and pay for.
```

- **Hot partitions** are the classic failure: `PK = "TENANT#acme"` where one tenant is 80% of traffic throttles regardless of provisioned capacity.
- **Single-table design** (multiple entity types sharing a table with composite keys) is the idiomatic approach and is genuinely hard to retrofit.
- **Cosmos RU/s** is a budget per second; a cross-partition query silently costs orders of magnitude more RUs. Log the RU charge per operation — it's the only way to find the expensive query.
- Transactions exist but only **within a partition**.

Use them when you have a known, narrow access pattern at very high scale (session store, event log, device telemetry, shopping cart). Avoid them when you'll need to ask new questions of the data later.

<a id="dn-180"></a>

#### The rest of the landscape

| Need | Tool | The catch |
|---|---|---|
| Relevance-ranked search, facets, fuzzy matching | Elasticsearch / OpenSearch | It's a **secondary** index — never the source of truth; you own the reindex pipeline and the lag |
| Time-series metrics | Prometheus, Timescale, InfluxDB | Cardinality limits (Part 28.2) apply here too |
| Graph traversal (fraud rings, social) | Neo4j, Gremlin | Only pays off if traversal depth is the actual query |
| Cache / ephemeral state | Redis | Not durable by default; don't make it your source of truth |
| Analytics over history | Columnar warehouse (Snowflake, BigQuery, ClickHouse) | Keep it out of the OLTP path — analytical queries wreck transactional databases |

**Polyglot persistence has a cost people underestimate**: every extra store is another backup, restore test, migration path, failure mode, on-call runbook, and consistency boundary. Two stores is a reasonable system; six is usually an accident.

<a id="dn-181"></a>

#### The decision questions

1. What are the **access patterns**? (Not "what is the data" — what queries will run?)
2. What is the **actual** scale? Do the arithmetic (Part 40.4).
3. Do you need **transactions across** entities?
4. Will the questions you ask of the data **change** over time? (If yes → relational.)
5. Who operates it at 3am?

---

### NoSQL & Polyglot Persistence

<p class="src">From <em>Backend Engineering</em> · C# & .NET · NoSQL & Polyglot Persistence</p>

<p class="pd">Document stores, key-value caches, wide-column, search and vector databases — what each one is actually good at, how it looks from C#, and how to decide which of them you genuinely need.</p>

<a id="be-a115"></a>

#### The NoSQL taxonomy and the trade being made

| Family      | Model                             | Examples                                | Query shape                   |
|-------------|-----------------------------------|-----------------------------------------|-------------------------------|
| Document    | Self-contained JSON per record    | RavenDB, MongoDB, Cosmos DB, DocumentDB | By ID, or by indexed field    |
| Key-value   | Opaque value behind a key         | Redis, DynamoDB, Valkey                 | By key, extremely fast        |
| Wide-column | Rows with dynamic column families | Cassandra, ScyllaDB, HBase              | By partition + clustering key |
| Graph       | Nodes and edges as first-class    | Neo4j, Cosmos Gremlin                   | Traversals and paths          |
| Search      | Inverted index over text          | Elasticsearch, OpenSearch, Meilisearch  | Relevance-ranked full text    |
| Time-series | Timestamped measurements          | TimescaleDB, InfluxDB, Prometheus       | Windows and aggregations      |
| Vector      | High-dimensional embeddings       | Qdrant, Pinecone, pgvector, Milvus      | Nearest-neighbour similarity  |

##### CAP, honestly

During a network partition you can be consistent or available, not both. Outside a partition the real trade is latency versus consistency, which is why PACELC is the more useful framing: *if Partition then A or C, else Latency or Consistency*. Every distributed store is a specific position on that dial, and most let you move it per operation.

> **\[Why it matters\]** The question to ask a vendor or a colleague is never “is it consistent?” but “what exactly am I guaranteed to see immediately after my own write, and what am I guaranteed to see after someone else's?” The answers differ per product and often per API call.

###### What is actually being traded, and CAP versus PACELC

``` text
  the honest framing: NoSQL stores give up something SPECIFIC in exchange
  for something SPECIFIC. Naming both is the whole decision.

  KEY-VALUE     give up: queries by anything but the key
                get:     O(1) access, trivial horizontal scale

  DOCUMENT      give up: joins, cross-document transactions (mostly),
                         schema enforcement
                get:     flexible shape, whole-aggregate reads

  WIDE-COLUMN   give up: ad-hoc queries entirely - you model per QUERY
                get:     enormous write throughput, linear scale

  GRAPH         give up: write throughput, easy sharding
                get:     traversals that are O(edges) rather than O(joins)

  SEARCH        give up: being a source of truth (it is a derived index)
                get:     relevance ranking, facets, fuzzy matching

  TIME-SERIES   give up: updates and general-purpose querying
                get:     compression, retention, time-bucketed aggregation

  the mistake is adopting one WITHOUT naming what you gave up - and then
  discovering it when you need the thing you traded away.
```

``` text
  CAP is the famous one, and PACELC is the useful one.

  CAP    during a network PARTITION, choose:
           Consistency  - refuse to answer rather than be wrong
           Availability - answer, possibly with stale data
         note: this only applies DURING a partition, which is rare.
         "CA" systems do not exist in a distributed setting.

  PACELC extends it to the normal case, which is what you live with daily:

           if PARTITION:  choose A or C
           ELSE:          choose L (latency) or C (consistency)

         PostgreSQL, single node    ...  EL/EC  (no partition to speak of)
         DynamoDB                   ...  PA/EL  (tunable to EC per read)
         Cassandra                  ...  PA/EL  (tunable via consistency level)
         Spanner / CockroachDB      ...  PC/EC  (pays latency for consistency)
         MongoDB                    ...  PA/EC  (majority reads/writes by default)

  the EL/EC half is the one you feel every day. Choosing "eventually
  consistent" is choosing lower latency in the NORMAL case, not just
  during an outage.
```

| Reason given for NoSQL                       | Honest assessment                                                                                         |
|----------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| “It scales better”                           | True at very large scale. Most systems never reach it, and PostgreSQL handles far more than people assume |
| “Our data is unstructured”                   | Usually means “varies per tenant” — a `jsonb` column solves it                                            |
| “No schema means faster development”         | The schema still exists; it moved into the application, where it is unenforced                            |
| “Joins are slow”                             | Joins on indexed keys are fast. Denormalising moves the join into your code                               |
| “We need sub-millisecond reads”              | Legitimate — that is a key-value cache, alongside a relational store                                      |
| “The access pattern is one key, huge volume” | Legitimate, and the strongest reason there is                                                             |

<a id="be-a116"></a>

#### Document databases and aggregate design

The central modelling decision is what goes in one document. A document is the unit of atomicity, the unit of retrieval, and usually the unit of contention — so it should map closely to a DDD aggregate.

``` text
// one document, loaded and saved atomically
{
  "id": "orders/4711",
  "customerId": "customers/88",
  "status": "Placed",
  "placedAt": "2026-07-27T09:14:00Z",
  "lines": [
    { "sku": "ABC-1", "qty": 2, "unitPrice": 19.99 },
    { "sku": "XYZ-9", "qty": 1, "unitPrice": 249.00 }
  ],
  "shipTo": { "line1": "...", "city": "Amsterdam", "country": "NL" }
}
```

**Embed when**

- The child has no life outside the parent
- They are always read together
- The collection is bounded — tens, not millions
- They change together in one transaction

**Reference when**

- The child is an aggregate in its own right
- It is shared by many parents
- The collection grows without limit
- It changes far more often than the parent

> **\[Trap\]**
>
> **Common trap.** The unbounded array is the classic document-store failure: an order with a growing `auditEvents` array eventually hits the document size limit, and every read of the order pulls megabytes. Events, comments and log-shaped children belong in their own documents.

###### Worked example

The same domain, modelled three ways, with the consequences of each. An order with lines, a customer, and an audit trail.

``` text
// A. everything embedded - one document
{ "id": "orders/4711", "customer": { "id": "c/88", "name": "Acme", "address": {...} },
  "lines": [ ...20 lines... ],
  "auditTrail": [ ...400 entries and growing... ] }

// reads:   one lookup, everything present.
// writes:  the whole document is rewritten every time ANY field changes.
// problem: the audit array grows without bound. At 400 entries the document is 200 KB,
//          so every status change rewrites 200 KB and every read transfers 200 KB.
//          Customer name is duplicated across every order and goes stale on rename.
```

``` text
// B. everything referenced - fully normalised
{ "id": "orders/4711", "customerId": "c/88", "lineIds": ["l/1","l/2", ...] }

// reads:   1 + 1 + 20 lookups, or an Include/join. You have rebuilt a relational
//          database on a store that is bad at joins.
// writes:  cheap and granular.
// problem: you gave up the one thing document stores are good at.
```

``` text
// C. the aggregate boundary, drawn deliberately
{ "id": "orders/4711",
  "customerId": "c/88",
  "customerSnapshot": { "name": "Acme B.V.", "vatNumber": "NL..." },   // point-in-time COPY
  "lines": [ ...20 lines... ],                                          // bounded, part of the order
  "status": "Shipped", "total": 1249.00 }

// separately:
{ "id": "orders/4711/audit/0187", "orderId": "orders/4711", "at": "...", "what": "..." }

// reads:   one lookup for everything the order screen needs.
// writes:  bounded document size; audit entries are independent appends.
// the customer snapshot is DELIBERATE denormalisation: an invoice must show the name
// and VAT number as they were at the time, not as they are today. That is a
// business rule, not a caching decision.
```

The question that resolves nearly every embed-or-reference argument: *does this data need to be consistent with the parent at the moment the parent is written?* Order lines, yes — they are part of the order's invariants. Audit entries, no. Customer details, it depends on whether the business wants the current value or the historical one, and asking that question usually reveals a requirement nobody had written down.

###### Document size and the limits you will actually hit

| Store     | Max document  | Practical ceiling | What happens past it                                  |
|-----------|---------------|-------------------|-------------------------------------------------------|
| MongoDB   | 16 MB         | ~100 KB           | Hard error on write                                   |
| Cosmos DB | 2 MB          | ~50 KB            | Hard error; RU cost scales with size long before that |
| RavenDB   | No hard limit | ~1 MB             | Indexing and replication degrade                      |
| DynamoDB  | 400 KB        | ~50 KB            | Hard error; also a write-capacity multiplier          |

“Practical ceiling” matters more than the hard limit. Cost and latency in these stores scale with document size on *every* read and write, so a 300 KB document that you read a thousand times an hour is expensive long before it is illegal. Design for the document you will have after three years of use, not the one in your test fixture.

``` text
// the bucket pattern: bound an unbounded child collection by time
{ "id": "readings/device-42/2026-07-27T09",     // one document per device per HOUR
  "deviceId": "device-42",
  "hour": "2026-07-27T09:00:00Z",
  "count": 3600,
  "values": [ 21.4, 21.5, ... ] }                // fixed maximum size, known in advance

// 3,600 individual documents become one, reads are one lookup per hour,
// and the document can never grow past a known bound.
```

<a id="be-a117"></a>

#### RavenDB in depth

RavenDB is unusual among document stores in offering ACID transactions across documents in a single session, plus a unit-of-work session model that will feel immediately familiar coming from EF Core.

``` csharp
using var session = store.OpenAsyncSession();

var order = await session.LoadAsync<Order>("orders/4711");     // by ID: always consistent
order.Status = OrderStatus.Shipped;

// change tracking, exactly like a DbContext
await session.SaveChangesAsync(ct);      // one ACID transaction

// includes avoid a second round trip
var o = await session.Include<Order>(x => x.CustomerId)
                     .LoadAsync<Order>("orders/4711");
var customer = await session.LoadAsync<Customer>(o.CustomerId);   // no network call
```

##### Indexes are the thing to understand

Queries in RavenDB always run against an index, never the documents themselves. If you query on a field with no index, one is created automatically — and then updated in the background. That background update is the source of the behaviour that surprises everyone.

``` text
  session.Store(newOrder); session.SaveChanges();     <- document is committed, ACID
        |
        |  indexing runs asynchronously
        v
  session.Query<Order>().Where(...)                   <- may NOT see it yet: STALE

  three correct responses:
    1. Load by ID instead of querying           (always consistent)
    2. WaitForNonStaleResults on this one query (blocks; use narrowly)
    3. Design the flow to tolerate the lag      (usually the right answer)
```

``` csharp
// static index: defined in code, versioned, deployed with the app
public class Orders_ByCustomerAndDate : AbstractIndexCreationTask<Order>
{
    public Orders_ByCustomerAndDate()
    {
        Map = orders => from o in orders
                        select new { o.CustomerId, o.PlacedAt, o.Status };
        Index(x => x.Status, FieldIndexing.Exact);
    }
}

// map-reduce runs continuously; aggregates are pre-computed, so reads are instant
public class Sales_ByMonth : AbstractIndexCreationTask<Order, Sales_ByMonth.Result>
{
    public class Result { public int Year, Month; public decimal Total; }
    public Sales_ByMonth()
    {
        Map = orders => from o in orders
                        select new Result { Year = o.PlacedAt.Year,
                                            Month = o.PlacedAt.Month,
                                            Total = o.Lines.Sum(l => l.UnitPrice * l.Qty) };
        Reduce = results => from r in results
                            group r by new { r.Year, r.Month } into g
                            select new Result { Year = g.Key.Year, Month = g.Key.Month,
                                                Total = g.Sum(x => x.Total) };
    }
}
```

###### What this looks like when it breaks

The staleness bug, in the exact form it usually appears — as a flaky test that only fails in CI.

``` csharp
[Fact]
public async Task Creating_an_order_makes_it_appear_in_the_list()
{
    using var session = _store.OpenAsyncSession();

    await session.StoreAsync(new Order { CustomerId = "customers/1" });
    await session.SaveChangesAsync();                    // committed, ACID, definitely on disk

    var orders = await session.Query<Order>()
        .Where(o => o.CustomerId == "customers/1")
        .ToListAsync();

    Assert.Single(orders);      // passes locally on a fast machine
}                               // fails ~1 in 20 on a loaded CI agent
```

Nothing is wrong with the write. The document is committed. The *index* has not caught up, and the query reads the index. On a fast idle laptop indexing completes in a millisecond and the assertion passes; on a busy CI agent with four other test suites running it takes longer than the query.

``` csharp
// in tests: wait explicitly, and make it the default for the test store
var store = new DocumentStore { ... };
store.OnBeforeQuery += (_, args) => args.QueryCustomization.WaitForNonStaleResults();

// in application code: choose deliberately, per query
var orders = await session.Query<Order>()
    .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(5)))   // blocks; use sparingly
    .Where(o => o.CustomerId == id)
    .ToListAsync();

// usually better: do not query at all on the read-your-own-write path
var justCreated = await session.LoadAsync<Order>(newId);   // by ID: always consistent
```

> **\[Note\]**
>
> **The design rule.** Treat any query result as ‘true as of a moment ago’. Anywhere that is not acceptable — a uniqueness check, a redirect straight to a just-created item, a balance used for a decision — must use a document ID load, a compare-exchange value, or a cluster-wide transaction. Sprinkling `WaitForNonStaleResults` through application code hides the problem and serialises your throughput on the indexing thread.

###### Other RavenDB behaviours worth knowing before they surprise you

| Behaviour                 | Detail                                                                                                                                     |
|---------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| Max requests per session  | 30 by default — a deliberate N+1 guard. Hitting it means you should use `Include` or a batch load, not raise the limit                     |
| `Query` default page size | Capped (128 by default in older versions); always page explicitly rather than relying on the default                                       |
| Auto-indexes              | Created on demand and merged over time; fine in development, but define static indexes for production so they are versioned and reviewable |
| Document IDs              | Semantic and hierarchical (`orders/4711-A`); the prefix is meaningful and is used by collection queries                                    |
| Compare-exchange          | Cluster-wide atomic values — the correct tool for unique-email-style constraints                                                           |
| Revisions                 | Built-in document history; enable per collection for an audit trail with no extra code                                                     |
| Subscriptions             | A durable, resumable server-side stream of matching documents — an excellent outbox/projection mechanism                                   |

``` csharp
// uniqueness, done correctly - a query cannot do this safely because of staleness
var result = await store.Operations.SendAsync(
    new PutCompareExchangeValueOperation<string>($"emails/{email}", userId, index: 0));

if (!result.Successful)
    return Result.Fail("That email address is already registered.");
```

<a id="be-a118"></a>

#### MongoDB with .NET

``` csharp
var client   = new MongoClient(cs);              // thread-safe, register as SINGLETON
var database = client.GetDatabase("catalog");
var products = database.GetCollection<Product>("products");

// LINQ provider translates to the aggregation pipeline
var results = await products.AsQueryable()
    .Where(p => p.CategoryId == categoryId && p.Price < 100)
    .OrderBy(p => p.Price)
    .Take(20)
    .ToListAsync(ct);

// atomic partial update - no read-modify-write race
await products.UpdateOneAsync(
    Builders<Product>.Filter.Eq(p => p.Id, id),
    Builders<Product>.Update.Inc(p => p.StockLevel, -qty)
                            .Set(p => p.UpdatedAt, DateTime.UtcNow),
    cancellationToken: ct);

// EF Core provider also exists if you want the familiar API
builder.Services.AddDbContext<CatalogDb>(o => o.UseMongoDB(cs, "catalog"));
```

| Concern         | Guidance                                                                     |
|-----------------|------------------------------------------------------------------------------|
| Client lifetime | `MongoClient` owns the connection pool — singleton, never per request        |
| Write concern   | `w:majority` for anything you cannot lose                                    |
| Read preference | `primary` for read-your-writes; secondary only for tolerant reads            |
| Transactions    | Supported on replica sets, but they are slower — model to avoid needing them |
| Indexes         | Create them explicitly at startup; `explain()` to verify they are used       |
| Change streams  | A built-in CDC feed — a clean way to publish events without an outbox poller |

> **\[Trap\]**
>
> **Common trap.** Mongo will happily perform a full collection scan on an unindexed query and simply be slow. There is no error, no warning, and it works fine on your 500-document dev database. Enable the profiler for slow operations in every environment.

###### Modelling for a document store, and the transaction limits

``` csharp
// the modelling decision that determines everything: EMBED or REFERENCE

// EMBED when the child is owned, bounded, and read with the parent
public class Order
{
    [BsonId] public ObjectId Id { get; set; }
    public Guid TenantId { get; set; }
    public List<OrderLine> Lines { get; set; } = [];   // bounded: ~1-50
    public Address ShippingAddress { get; set; } = new();
}
// one read returns the whole aggregate. No join. This is the point.

// REFERENCE when the child is unbounded, shared, or queried independently
public class Post
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId AuthorId { get; set; }             // shared across posts
    // NOT: public List<Comment> Comments  -- unbounded. See below.
}
```

``` text
  THE 16 MB DOCUMENT LIMIT, and why it matters more than the number

    a document must be under 16 MB. That sounds generous.

    the real constraint is that an UNBOUNDED embedded array is a bug
    regardless of size:
      - the WHOLE document is read and rewritten on every update
      - a post with 40,000 comments rewrites 40,000 comments to add one
      - indexes on the array grow with it
      - the document grows in place until it must be MOVED

    the rule: embed only what is BOUNDED and OWNED.
      order lines          bounded (~50)      -> embed
      comments on a post   unbounded          -> separate collection
      the last 10 comments bounded, denormalised -> embed AS A CACHE,
                                                    with the full set elsewhere
```

``` csharp
// transactions exist, and they are more constrained than in a relational store
using var session = await client.StartSessionAsync(cancellationToken: ct);
session.StartTransaction();
try
{
    await orders.InsertOneAsync(session, order, cancellationToken: ct);
    await inventory.UpdateOneAsync(session,
        Builders<Item>.Filter.Eq(i => i.Sku, sku),
        Builders<Item>.Update.Inc(i => i.Stock, -qty),
        cancellationToken: ct);
    await session.CommitTransactionAsync(ct);
}
catch { await session.AbortTransactionAsync(ct); throw; }

// the constraints
//   requires a replica set - a standalone mongod cannot do transactions
//   a default 60-second limit
//   performance cost is real; they were added, not designed in
//   -> if you find yourself using them constantly, your document
//      boundaries are wrong. Well-modelled documents rarely need them,
//      because the aggregate IS the transaction boundary.
```

| .NET consideration                    | Detail                                                                                    |
|---------------------------------------|-------------------------------------------------------------------------------------------|
| `MongoClient` is thread-safe          | Register it as a **singleton**. It holds the connection pool                              |
| `IMongoDatabase` / `IMongoCollection` | Cheap; scoped or singleton both fine                                                      |
| LINQ support                          | Good, and incomplete — unsupported expressions throw at runtime, not compile time         |
| Conventions                           | Register a camelCase convention pack once, or field names will be PascalCase in the store |
| `ObjectId` vs `Guid`                  | `ObjectId` is 12 bytes and time-ordered; a random `Guid` hurts index locality             |
| EF Core provider                      | Exists, and is young. The native driver is the mature path                                |
| Index creation                        | Not automatic. Create them explicitly at startup or in a migration                        |

> **\[Trap\]**
>
> **Common trap.** MongoDB will happily perform a full collection scan and give you the right answer, slowly, with no warning. There is no equivalent of a missing-index warning at the point of query. Enable the profiler in non-production, check `explain()` for `COLLSCAN` stages, and treat any query without an index as an incident waiting for the collection to grow.

<a id="be-a119"></a>

#### Cosmos DB: partition keys and request units

Cosmos is the store where an architectural mistake becomes a monthly bill. Two concepts govern everything: the partition key and request units.

##### Partition key

It determines physical distribution. A good key spreads writes evenly and appears in almost every query, so reads stay single-partition. A bad key creates a hot partition — one physical partition takes all the traffic while the rest idle, and you hit its throughput ceiling regardless of how much you provision.

| Key choice                     | Verdict                                                           |
|--------------------------------|-------------------------------------------------------------------|
| `/tenantId`                    | Good if tenants are similarly sized; bad with one giant tenant    |
| `/id`                          | Perfectly even, but every query that is not a point read fans out |
| `/date`                        | Bad — today's partition takes 100% of writes                      |
| `/tenantId-yyyyMM` (synthetic) | Often the right answer for time-series-shaped data                |

``` csharp
// point read: cheapest possible operation, ~1 RU
var response = await container.ReadItemAsync<Order>(
    id: "4711", partitionKey: new PartitionKey(tenantId), cancellationToken: ct);

Console.WriteLine(response.RequestCharge);      // ALWAYS measure this

// cross-partition query: fans out, cost scales with partition count
var q = container.GetItemQueryIterator<Order>(
    new QueryDefinition("SELECT * FROM c WHERE c.status = @s").WithParameter("@s", "Placed"));
```

Every response carries a `RequestCharge`. Log it, put it on a dashboard, and alert on regressions — it is the only reliable way to catch the query that quietly became forty times more expensive. A 429 means you exceeded provisioned RU/s; the SDK retries automatically, which is why the symptom shows up as latency rather than errors.

> **\[Note\]**
>
> **Consistency levels.** Strong, bounded staleness, session, consistent prefix, eventual. **Session** is the default and usually correct: your own client reads its own writes, at a fraction of the cost of strong. Reusing the same `CosmosClient` singleton is what keeps the session token flowing.

###### Worked example

A hot-partition incident, and the fix. A multi-tenant SaaS uses `/tenantId` as the partition key. It works for two years, then one customer signs up who is forty times larger than any other.

``` text
  physical partition 1   tenants a..f     8,000 RU/s used of 10,000     fine
  physical partition 2   tenants g..m     6,400 RU/s used               fine
  physical partition 3   TENANT-BIGCO     10,000 RU/s -> 429 THROTTLED  <- everything for
                                                                            this customer
                                                                            is now slow
  container is provisioned at 100,000 RU/s and using 40,000.
  Adding more RU/s does NOT help: a single logical partition key cannot exceed
  the throughput of the one physical partition it lives on (and has a 20 GB storage cap).
```

``` csharp
// the fix: a synthetic key that spreads one tenant across many logical partitions
public static string PartitionKeyFor(Order o) =>
    $"{o.TenantId}|{o.PlacedAt:yyyy-MM}";       // tenant + month

// point reads still work perfectly, because you know the month from the id
await container.ReadItemAsync<Order>(id, new PartitionKey($"{tenantId}|2026-07"), ct);

// queries within a month stay single-partition
var q = new QueryDefinition("SELECT * FROM c WHERE c.status = @s")
    .WithParameter("@s", "Open");
container.GetItemQueryIterator<Order>(q, requestOptions: new QueryRequestOptions
{
    PartitionKey = new PartitionKey($"{tenantId}|2026-07")     // ALWAYS set this if you can
});

// cross-month queries fan out over N partitions - acceptable, and now bounded by N,
// not by one partition's throughput ceiling.
```

Cosmos also supports hierarchical partition keys (up to three levels), which express `/tenantId` then `/yearMonth` natively and let the service split subpartitions automatically. That is the modern answer to this problem and avoids string-concatenated synthetic keys, but the underlying reasoning is identical.

###### Making RU cost visible before it becomes a bill

``` csharp
// log the charge on every operation, with enough context to attribute it
public async Task<T?> ReadAsync<T>(string id, string pk, CancellationToken ct)
{
    try
    {
        var r = await _container.ReadItemAsync<T>(id, new PartitionKey(pk), cancellationToken: ct);
        _ruMeter.Record(r.RequestCharge,
            new KeyValuePair<string, object?>("operation", "read"),
            new KeyValuePair<string, object?>("type", typeof(T).Name));
        return r.Resource;
    }
    catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound) { return default; }
}
```

| Operation                           | Typical RU    | Note                                                    |
|-------------------------------------|---------------|---------------------------------------------------------|
| Point read, 1 KB document           | 1             | The cheapest thing Cosmos does — design for it          |
| Point read, 100 KB document         | ~10           | Cost scales with size                                   |
| Write, 1 KB document                | ~5            | More with many indexed properties                       |
| Single-partition query, 100 results | ~10–30        | Depends on index usage                                  |
| Cross-partition query               | N × the above | Scales with physical partition count, not result count  |
| Query with no matching index        | Very high     | It scans; exclude unused paths from the indexing policy |

Two habits keep the bill sane: tune the indexing policy to exclude paths you never filter on (indexing every path is the default and roughly doubles write cost on wide documents), and put a dashboard panel on RU-per-operation-type so a regression shows up as a graph rather than an invoice.

<a id="be-a120"></a>

#### Redis beyond the cache

Redis is treated as a cache because that is the first use, but it is a data-structure server, and the other structures solve problems people otherwise build badly in a database.

| Structure   | Command family      | Use                                                         |
|-------------|---------------------|-------------------------------------------------------------|
| String      | `GET/SET/INCR`      | Cache, counters, feature flags                              |
| Hash        | `HGET/HSET`         | Session objects, partial updates without re-serializing     |
| Sorted set  | `ZADD/ZRANGE`       | Leaderboards, rate limits, priority queues, sliding windows |
| List        | `LPUSH/BRPOP`       | Simple work queues                                          |
| Stream      | `XADD/XREADGROUP`   | Durable log with consumer groups — a lightweight Kafka      |
| Set         | `SADD/SINTER`       | Tags, unique visitors, deduplication                        |
| Pub/sub     | `PUBLISH/SUBSCRIBE` | SignalR backplane, cache invalidation fan-out               |
| HyperLogLog | `PFADD/PFCOUNT`     | Approximate cardinality in 12 KB                            |

``` csharp
// distributed lock, done correctly: unique token, atomic release via Lua
var token = Guid.NewGuid().ToString();
var got = await db.StringSetAsync(key, token, TimeSpan.FromSeconds(30), When.NotExists);
if (!got) return;
try { await DoWorkAsync(ct); }
finally
{
    const string release = @"
        if redis.call('get', KEYS[1]) == ARGV[1]
        then return redis.call('del', KEYS[1]) else return 0 end";
    await db.ScriptEvaluateAsync(release, [key], [token]);
}
```

> **\[Trap\]**
>
> **Common trap.** `KEYS *` is O(n) and blocks the single-threaded server. On a production instance it is an outage. Use `SCAN`, or better, keep an index set so you never need to enumerate. The same applies to large `DEL` operations — use `UNLINK`.

Also remember Redis is not durable by default. It is the right place for data you can rebuild and the wrong place for the only copy of anything.

###### Worked example

A sliding-window rate limiter in a sorted set. This is the canonical example of Redis doing something a relational database would do badly.

``` text
-- executed atomically as a Lua script: no race between the steps
local key    = KEYS[1]
local now    = tonumber(ARGV[1])      -- unix ms
local window = tonumber(ARGV[2])      -- ms
local limit  = tonumber(ARGV[3])
local id     = ARGV[4]                -- a unique member per request

redis.call('ZREMRANGEBYSCORE', key, 0, now - window)   -- drop entries outside the window
local count = redis.call('ZCARD', key)
if count >= limit then
  return 0
end
redis.call('ZADD', key, now, id)
redis.call('PEXPIRE', key, window)
return 1
```

``` csharp
private static readonly string Script = File.ReadAllText("sliding_window.lua");

public async Task<bool> TryAcquireAsync(string tenant, CancellationToken ct)
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var result = (int)await _db.ScriptEvaluateAsync(Script,
        [ (RedisKey)$"rl:{tenant}" ],
        [ now, 60_000, 1000, Guid.NewGuid().ToString("N") ]);
    return result == 1;
}
```

One round trip, atomic, self-expiring, and accurate to the millisecond across every instance of your service. The equivalent in a relational database is a table with an index on timestamp, a delete, a count and an insert inside a transaction — four statements, lock contention on a hot row, and vacuum pressure from the constant churn.

###### Operational facts that change how you use it

- **It is single-threaded for command execution.** One slow command blocks every client. This is why `KEYS`, large `ZRANGE`, and unbounded `SMEMBERS` are dangerous, and why Lua scripts must be short.
- **Persistence is a choice, and both options lose data.** RDB snapshots lose everything since the last snapshot; AOF with `everysec` loses up to a second. Treat Redis as rebuildable state.
- **Eviction policy matters.** The default `noeviction` returns errors on write when memory is full. For a pure cache you want `allkeys-lru`; for mixed use, `volatile-lru` so only keys with a TTL are evicted.
- **Pipelining is free throughput.** `StackExchange.Redis` multiplexes automatically, so firing many commands without awaiting each one in turn is dramatically faster than a sequential loop.
- **`ConnectionMultiplexer` is a singleton.** It is expensive, thread-safe, and designed to be shared for the process lifetime. Creating one per operation is the Redis equivalent of the HttpClient bug.

``` csharp
// sequential: 100 round trips
foreach (var id in ids) results.Add(await db.StringGetAsync($"p:{id}"));

// pipelined: 1 round trip's worth of latency
var tasks = ids.Select(id => db.StringGetAsync($"p:{id}")).ToArray();
var values = await Task.WhenAll(tasks);

// or explicitly, for a batch that must go together
var batch = db.CreateBatch();
var t1 = batch.StringSetAsync("a", 1);
var t2 = batch.StringIncrementAsync("counter");
batch.Execute();
await Task.WhenAll(t1, t2);
```

<a id="be-a121"></a>

#### Search and text: Elasticsearch and friends

A relational `LIKE '%term%'` cannot use an index, has no notion of relevance, and does not handle typos, stemming or synonyms. A search engine inverts the problem: it indexes terms to documents, with analysers that normalise language.

``` csharp
var response = await client.SearchAsync<ProductDoc>(s => s
    .Index("products")
    .Query(q => q.Bool(b => b
        .Must(m => m.MultiMatch(mm => mm
            .Fields(f => f.Field(p => p.Name, boost: 3).Field(p => p.Description))
            .Query(term).Fuzziness(new Fuzziness(1))))
        .Filter(f => f.Term(t => t.Field(p => p.InStock).Value(true)))))
    .Aggregations(a => a.Terms("by_category", t => t.Field(p => p.Category)))
    .Size(20), ct);
```

The distinction between `must` and `filter` is worth knowing: `must` contributes to the relevance score, `filter` is a yes/no predicate that is cached and cheap. Put every hard constraint in the filter context.

##### Keeping the index in sync

1.  The database stays the source of truth. The search index is a derived projection, always.
2.  Publish domain events on write and have an indexer consume them — or use CDC / change streams.
3.  Index in bulk, not per document; a bulk request of a few thousand is far cheaper than a few thousand requests.
4.  Build a full reindex job on day one, because you will change the mapping and you will need it.
5.  Alias the index (`products` → `products_v7`) so a reindex is an atomic alias swap with no downtime.

###### Search as a derived read model, never a source of truth

``` text
  the architecture that works

    PostgreSQL (source of truth)
         |
      CDC / outbox
         |
         v
    Elasticsearch (derived index)  <---- queries for search only
         |
    results are IDs + highlights
         |
    hydrate from PostgreSQL if you need authoritative fields

  the rules that follow
    the index is REBUILDABLE from the source, always. Test that you can.
    NEVER write to the index as the only copy of anything.
    accept it is eventually consistent - a document may be seconds stale.
    reindexing is a routine operation, not an emergency: you WILL need to
      change the mapping, and mappings are mostly immutable.
```

``` csharp
// index and search with the modern .NET client
var client = new ElasticsearchClient(settings);   // register as a SINGLETON

// mapping matters more than anything else. Get it wrong and you reindex.
await client.Indices.CreateAsync("products-v3", c => c
    .Mappings(m => m.Properties<Product>(p => p
        .Text(t => t.Name, cfg => cfg.Analyzer("english")
            .Fields(f => f.Keyword("raw")))     // text for SEARCH, keyword for
                                                 // sorting/aggregating/exact match
        .Keyword(k => k.Sku)                    // never analysed - exact only
        .Doubl(d => d.Price)
        .Date(d => d.CreatedAt))), ct);

// the version suffix + alias pattern - this is how you reindex with no downtime
await client.Indices.PutAliasAsync("products-v3", "products", ct);
// application queries the ALIAS. Build v4, then atomically move the alias.
```

| Field type         | Analysed?                                | Use for                                                       |
|--------------------|------------------------------------------|---------------------------------------------------------------|
| `text`             | **Yes** — tokenised, stemmed, lowercased | Full-text search                                              |
| `keyword`          | No — stored verbatim                     | Exact match, sorting, aggregations, IDs                       |
| Both (multi-field) | Both                                     | The usual answer: search on `name`, sort on `name.raw`        |
| `date`, `double`   | No                                       | Ranges and maths                                              |
| `nested`           | Depends                                  | Arrays of objects where field relationships must be preserved |

**PostgreSQL full-text is enough when**

- The corpus is millions, not billions
- You want one system, transactionally consistent
- Ranking needs are simple
- Operational simplicity beats relevance tuning
- `pg_trgm` covers your fuzzy matching

**Use a search engine when**

- Relevance tuning is a product feature
- You need facets, synonyms, autocomplete, aggregations
- The corpus is very large or updates constantly
- Search traffic would compete with OLTP
- Multi-language analysis matters

> **\[Trap\]**
>
> **Common trap.** The classic operational failure is a mapping change requiring a full reindex, discovered at the moment you need it. Most Elasticsearch mappings cannot be altered in place — you create a new index and reindex. Build the versioned-index-plus-alias pattern from day one, and rehearse a full rebuild while the data is still small enough that it takes minutes.

<a id="be-a122"></a>

#### Vector databases and embeddings

An embedding model turns text, images or audio into a vector of a few hundred to a few thousand floats where geometric closeness means semantic similarity. A vector database indexes those for approximate nearest-neighbour search. This is the storage layer behind retrieval-augmented generation and most modern semantic search.

``` csharp
// Microsoft.Extensions.VectorData - a provider-agnostic abstraction
public class DocChunk
{
    [VectorStoreKey]                       public Guid Id { get; set; }
    [VectorStoreData(IsIndexed = true)]    public string TenantId { get; set; } = "";
    [VectorStoreData]                      public string Text { get; set; } = "";
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}

var embedding = await embeddingGenerator.GenerateVectorAsync(question, ct);

var hits = collection.SearchAsync(embedding, top: 5,
    new() { Filter = c => c.TenantId == tenantId }, ct);
```

| Option                     | Shape                              | Fits                                                             |
|----------------------------|------------------------------------|------------------------------------------------------------------|
| `pgvector`                 | Extension on PostgreSQL            | You already run Postgres and the corpus is moderate — start here |
| Qdrant                     | Purpose-built, self-hostable, Rust | Serious scale with filtering; good Aspire integration            |
| Azure AI Search            | Managed hybrid keyword + vector    | Azure shops wanting one service for both                         |
| Redis vector sets          | Extension of an existing Redis     | Small corpora already cached in Redis                            |
| Elasticsearch dense_vector | Alongside full text                | Hybrid search where keywords still matter                        |

> **\[Note\]**
>
> **Hybrid beats pure vector.** In most real applications the best results come from combining keyword search with vector similarity and re-ranking the merged list. Pure semantic search is surprisingly bad at exact identifiers, product codes and names — precisely the things users search for most.

> **\[Trap\]**
>
> **Common trap.** The embedding model is part of your schema. Change models and every stored vector is meaningless, because they live in a different space. Version the collection by model name and reindex on a switch.

###### Filtered search, and the operational realities

``` sql
// pgvector: the default starting point, and it keeps vectors transactionally
// consistent with the rows they describe.
CREATE TABLE doc_chunks (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    tenant_id   uuid NOT NULL,                 -- a real column: SECURITY filter
    content     text NOT NULL,
    embedding   vector(1536) NOT NULL
);
CREATE INDEX ON doc_chunks USING hnsw (embedding vector_cosine_ops);
CREATE INDEX ON doc_chunks (tenant_id);
```

``` text
  THE FILTERED-SEARCH PROBLEM, which catches everyone

  an approximate nearest-neighbour index searches the GRAPH, and the
  filter is applied AFTER.

  ask for the 10 nearest, filter to one tenant, and you may get ZERO rows -
  because all 10 global nearest belonged to other tenants.

  three strategies
    1  OVER-FETCH then filter    LIMIT 200, filter in an outer query
                                 simple; recall degrades badly for
                                 selective filters
    2  PARTITION / partial index one index per tenant
                                 excellent recall; impractical past a
                                 few hundred tenants
    3  ITERATIVE SCAN            the index keeps producing candidates
                                 until enough pass the filter
                                 SET hnsw.iterative_scan = strict_order;
                                 -> the correct answer in most cases
```

``` csharp
// .NET side - Microsoft.Extensions.AI gives you a provider-neutral abstraction
IEmbeddingGenerator<string, Embedding<float>> embedder = /* OpenAI, Azure, local */;

var embedding = await embedder.GenerateEmbeddingVectorAsync(question, ct);

// then the query, with the tenant filter as a SECURITY control
var results = await db.Database
    .SqlQuery<ChunkResult>($@"
        SELECT id, content, embedding <=> {embedding.ToArray()} AS distance
        FROM   doc_chunks
        WHERE  tenant_id = {tenantId}
        ORDER  BY embedding <=> {embedding.ToArray()}
        LIMIT  10")
    .ToListAsync(ct);
```

| Operational fact                           | Consequence                                                                                       |
|--------------------------------------------|---------------------------------------------------------------------------------------------------|
| The embedding model is part of your schema | Change models and every stored vector is meaningless. Version the collection by model and reindex |
| 1536 dimensions is ~6 KB per row           | A million chunks is ~6 GB before indexing. Consider `halfvec`                                     |
| HNSW build is slow                         | Raise `maintenance_work_mem`; expect hours on large tables                                        |
| The index should fit in memory             | If it spills, latency rises by an order of magnitude                                              |
| Recall is not correctness                  | ANN is approximate by definition — measure recall against exact search on a sample                |
| Chunking dominates quality                 | More than the index or the model. Overlap chunks; keep them semantically whole                    |

> **\[Trap\]**
>
> **Common trap.** The tenant filter in a vector query is a **security control**, not an optimisation. Vector search returns the *nearest* chunks, so without a filter the nearest chunk to a question may belong to another customer — and in a retrieval-augmented pipeline that text is then quoted verbatim in an answer. Enforce it, ideally with row-level security, and write a test that proves two tenants cannot see each other's content.

<a id="be-a123"></a>

#### Event sourcing as a persistence strategy

Instead of storing current state, store the sequence of events that produced it, and rebuild state by replaying them. The event log is the source of truth and is append-only.

``` text
  state-oriented                    event-sourced

  orders                            order-4711 stream
   4711 | Shipped | 268.98            1  OrderPlaced      { lines, customer }
                                      2  LineAdded        { sku, qty }
  history: gone                       3  PaymentReceived  { amount }
                                      4  OrderShipped     { carrier, trackingId }

                                    current state = fold(events)
                                    read models   = projections over the same stream
```

``` csharp
// rebuild the aggregate by folding the stream
public static Order Rehydrate(IEnumerable<IDomainEvent> events) =>
    events.Aggregate(Order.Empty, (state, e) => state.Apply(e));

// a snapshot every N events keeps rehydration bounded
```

**Worth it when**

- Audit and ‘how did we get here’ are business requirements
- Temporal queries matter — state as of any past date
- Multiple read models over the same facts
- Finance, insurance, healthcare, compliance

**Not worth it when**

- CRUD with an audit table would do
- The team has not done it before and the deadline is real
- You need ad-hoc reporting on current state
- Nobody will ever replay anything

Tooling in .NET: Marten (event store plus document store on PostgreSQL, and an excellent default), EventStoreDB (purpose-built), or a hand-rolled append-only table with concurrency — see Terminology"\>optimistic concurrency on stream version. Be aware of the two hard parts — schema evolution of old events, and GDPR deletion from an immutable log (usually solved with crypto-shredding: encrypt personal data per subject and destroy the key).

###### Worked example

A minimal event-sourced aggregate with optimistic concurrency, so the moving parts are concrete rather than conceptual.

``` csharp
public abstract record OrderEvent(Guid OrderId, int Version);
public record OrderPlaced(Guid OrderId, int Version, Guid CustomerId) : OrderEvent(OrderId, Version);
public record LineAdded(Guid OrderId, int Version, string Sku, int Qty, decimal Price)
    : OrderEvent(OrderId, Version);
public record OrderShipped(Guid OrderId, int Version, string Carrier) : OrderEvent(OrderId, Version);

public sealed class Order
{
    private readonly List<OrderEvent> _uncommitted = [];
    public Guid Id { get; private set; }
    public int Version { get; private set; } = -1;
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }

    // COMMANDS validate, then raise. They never mutate state directly.
    public void AddLine(string sku, int qty, decimal price)
    {
        if (Status != OrderStatus.Placed)
            throw new DomainException("order.not_modifiable", "Only a placed order can be changed.");
        Raise(new LineAdded(Id, Version + 1, sku, qty, price));
    }

    // APPLY mutates, and must NEVER validate or throw - it replays history that
    // already happened, including events written by an older version of the rules.
    private void Apply(OrderEvent e)
    {
        switch (e)
        {
            case OrderPlaced p: Id = p.OrderId; Status = OrderStatus.Placed; break;
            case LineAdded l:   Total += l.Price * l.Qty; break;
            case OrderShipped:  Status = OrderStatus.Shipped; break;
        }
        Version = e.Version;
    }

    private void Raise(OrderEvent e) { Apply(e); _uncommitted.Add(e); }

    public static Order Rehydrate(IEnumerable<OrderEvent> history)
    {
        var o = new Order();
        foreach (var e in history) o.Apply(e);
        return o;
    }

    public IReadOnlyList<OrderEvent> DequeueUncommitted()
    {
        var events = _uncommitted.ToList(); _uncommitted.Clear(); return events;
    }
}
```

``` sql
-- the store: a unique constraint IS the concurrency control
CREATE TABLE events (
    stream_id   uuid    NOT NULL,
    version     int     NOT NULL,
    type        text    NOT NULL,
    data        jsonb   NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (stream_id, version)      -- two concurrent writers: one INSERT fails
);
```

The separation between command methods (validate, then raise) and `Apply` (mutate only) is the rule that makes event sourcing work long term. `Apply` runs against events written years ago under rules that no longer exist; if it validates, a rule change makes old aggregates unloadable. This is the single most common way teams break their own event store.

###### The two genuinely hard problems

##### 1. Schema evolution

``` csharp
// you cannot change an event that has already been written. You can only add
// new versions and upcast old ones on the way in.
public class LineAddedUpcaster : IUpcaster
{
    public object Upcast(string type, int schemaVersion, JsonElement data) =>
        (type, schemaVersion) switch
        {
            // v1 had no currency; every historical event was implicitly EUR
            ("LineAdded", 1) => new LineAddedV2(
                data.GetProperty("orderId").GetGuid(),
                data.GetProperty("version").GetInt32(),
                data.GetProperty("sku").GetString()!,
                data.GetProperty("qty").GetInt32(),
                data.GetProperty("price").GetDecimal(),
                Currency: "EUR"),
            _ => Deserialize(type, data)
        };
}
```

##### 2. Deleting personal data from an immutable log

GDPR erasure and an append-only store are in direct conflict. The standard resolution is crypto-shredding: encrypt each subject's personal data with a per-subject key, store the keys separately, and delete the key on an erasure request. The events remain, structurally intact, and the personal fields become permanently unreadable.

``` csharp
public record CustomerRegistered(Guid CustomerId, int Version,
                                 byte[] EncryptedName,        // per-subject key
                                 byte[] EncryptedEmail,
                                 string Country);             // not personal, left in clear

// erasure:
await _keyStore.DeleteAsync($"subject:{customerId}");
// every event referencing that subject now decrypts to nothing.
// projections rebuilt after this point simply show "[redacted]".
```

- **Snapshot long streams.** Rehydrating 40,000 events on every load is not viable; snapshot every few hundred and replay from there.
- **Projections must be rebuildable.** Treat every read model as disposable and keep the rebuild job working — you will change a projection's shape, and that is the whole point of having the log.
- **Version your projections too.** Rebuild into `orders_view_v4`, then swap the alias, so a rebuild never takes the read path down.
- **Do not event-source everything.** One or two aggregates where history is a business requirement, with ordinary tables everywhere else, is the configuration that actually survives contact with a team.

<a id="be-a124"></a>

#### Choosing a store, and how many

| If the shape is                                | Reach for                     | Before that, check                                   |
|------------------------------------------------|-------------------------------|------------------------------------------------------|
| Transactional business data with relationships | PostgreSQL / SQL Server       | This is the default; deviate with a reason           |
| Self-contained aggregates, flexible schema     | RavenDB, Mongo, Cosmos        | Could a `jsonb` column do it?                        |
| Ephemeral, hot, key-addressed                  | Redis                         | Is an in-memory cache enough?                        |
| Relevance-ranked text                          | Elasticsearch / OpenSearch    | Postgres full-text may be sufficient                 |
| Semantic similarity                            | pgvector, then Qdrant         | Do not start with a dedicated vector DB              |
| Massive append-only writes                     | Cassandra, Kafka, TimescaleDB | Partitioned Postgres goes further than people expect |
| Deep relationship traversal                    | Neo4j                         | Recursive CTEs handle a lot                          |

Polyglot persistence is a real and correct pattern, and it is also how teams end up operating five databases with one person who understands each. Every additional store costs a backup strategy, a monitoring story, a failover plan, a migration path, an on-call runbook, and an upgrade treadmill.

> **\[Note\]**
>
> **The rule of thumb.** Add a second store when the first one is demonstrably the wrong tool for a specific, measured problem — not because the new one is a better fit in theory. “PostgreSQL until it hurts, then add exactly one thing” is an unglamorous strategy that has aged extremely well.

###### The cost of each additional store, made explicit

``` text
  every store you add costs, permanently:

    operational      backups, restore testing, monitoring, patching,
                     upgrades, capacity, an on-call runbook
    consistency      data now lives in two places. They WILL diverge.
                     you need a reconciliation story, or you have a
                     silent-corruption story.
    expertise        someone must know its failure modes, not just its API
    cost             licences, instances, egress between them
    cognitive        every new developer learns one more thing
    testing          integration tests need it running

  so the question is never "would this store be better for this access
  pattern?" - it usually would. The question is:

      "is this access pattern's improvement worth a permanent
       increase in operational surface?"

  frequently the answer is no, and a jsonb column, a GIN index or a
  materialised view gets you 80% for zero additional surface.
```

| Need                         | Reach for               | Before that, try                                     |
|------------------------------|-------------------------|------------------------------------------------------|
| Sub-millisecond cached reads | Redis                   | The database's own buffer cache; a materialised view |
| Flexible per-tenant fields   | A document store        | **`jsonb` column**                                   |
| Full-text search             | Elasticsearch           | **`tsvector` + GIN**, or `pg_trgm`                   |
| Semantic search              | A vector database       | **`pgvector`**                                       |
| Time-series at volume        | InfluxDB, Timescale     | **Partitioning + BRIN**                              |
| Graph traversal              | Neo4j                   | A recursive CTE, or a closure table                  |
| Analytics                    | A warehouse, ClickHouse | **A read replica**, then materialised views          |
| Queues                       | RabbitMQ, Kafka         | **A table with `SKIP LOCKED`**                       |

The middle column is where teams start and the right column is where they should. Each of those alternatives keeps you in one system, transactionally consistent, with one backup and one thing to operate. Move to the dedicated store when you have *measured* a specific limit — not because the dedicated store is obviously more appropriate in principle.

> **\[Note\]**
>
> **A defensible default architecture.** PostgreSQL as the source of truth, plus Redis for caching and ephemeral state. That is two stores, both boring, both well understood, and it covers a startling proportion of systems — including ones with search, JSON documents, vectors, geospatial data and queues, all of which PostgreSQL handles adequately. Add a third store when you can name the measurement that forced it.

### Beyond the relational store

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Beyond the relational store</p>

<a id="be-m202"></a>

#### Choosing a store

| Store                      | Unit     | Strong at                                                        | Weak at                                                         |
|----------------------------|----------|------------------------------------------------------------------|-----------------------------------------------------------------|
| Relational                 | row      | constraints, joins, ad-hoc queries, transactions across entities | schema churn, deeply nested data, horizontal scale              |
| Document (RavenDB, Mongo)  | document | loading a whole aggregate in one read, evolving shape            | cross-document joins, ad-hoc aggregation, referential integrity |
| Key-value (Redis)          | key      | sub-millisecond reads, counters, queues, TTL                     | querying by anything but the key; durability guarantees         |
| Column-family / analytical | column   | scans and aggregation over billions of rows                      | single-row updates, transactions                                |
| Search (Elastic, Lucene)   | document | full-text relevance, faceting                                    | being the system of record                                      |

> **\[Why it matters\]**
>
> **The question that decides it** is not "relational or document" but *what is the transactional boundary?* If a business operation must atomically change several entities that are queried independently, a relational store makes that trivial and a document store makes it hard. If every operation touches exactly one aggregate that is always loaded whole, a document store removes the object-relational impedance entirely and is genuinely simpler.

> **\[Tip\]**
>
> **Polyglot is normal and fine** — a relational system of record, a document store for aggregates that change shape, Redis for hot reads and coordination, and a search index for text. What is *not* fine is having two stores that both claim to be authoritative for the same fact. Pick one owner per fact and treat everything else as a derived projection you can rebuild.

<a id="be-m203"></a>

#### Modeling documents

The whole design question is **where the document boundary falls**. A document should be an aggregate: the unit that is loaded, changed, and saved together, and the unit within which consistency is guaranteed.

``` text
// Embed: read together, changed together, bounded in size
{ "Id": "orders/1-A", "CustomerId": "customers/9-A", "Status": "Open",
  "Lines": [ { "Sku": "A1", "Qty": 2, "UnitPrice": 19.99 } ],
  "ShipTo": { "Street": "...", "City": "..." } }

// Reference: shared, independently queried, or unbounded
"CustomerId": "customers/9-A"
```

| Embed when                                     | Reference when                       |
|------------------------------------------------|--------------------------------------|
| the child has no life of its own               | the entity is shared by many parents |
| it is always read with the parent              | it is queried on its own             |
| the collection is bounded (tens, not millions) | the collection grows without bound   |
| it must change atomically with the parent      | it changes on a different cadence    |

A middle option that document databases make cheap: **denormalized reference** — store the id *and* the one or two fields you always display, accepting that the copy can go stale.

``` text
"Customer": { "Id": "customers/9-A", "Name": "Acme Ltd" }
// Renders a list without a second read. Refresh on the rare rename,
// or accept staleness where a stale display name is harmless.
```

> **\[Trap\]**
>
> **The unbounded-array failure.** Embedding a collection that grows forever — audit entries, messages, event history — produces a document that grows until reads and writes both become expensive, and every small update rewrites the whole thing. Cap embedded collections by design, and make growth a separate document per item or per time bucket.

<a id="be-m204"></a>

#### RavenDB: session, unit of work, request budget

`IDocumentStore` is an expensive, thread-safe singleton — create one per application. `IDocumentSession` is cheap, single-threaded, and short-lived: one per request or per unit of work, exactly like a `DbContext` ([Tracking, no-tracking, and the identity map](07-07-orms-and-ef-core.md#be-m158)).

``` csharp
using var session = store.OpenAsyncSession();
var order = await session.LoadAsync<Order>("orders/1-A");   // by id: no index, consistent
order.Status = "Closed";                                     // tracked
await session.SaveChangesAsync();                            // ONE batched request
```

The session tracks loaded documents and sends only what changed, as a single atomic batch. Everything in one `SaveChanges` succeeds or fails together.

**The request budget is the thing to internalise.** A session caps the number of server round trips (30 by default) and throws when exceeded. This is a deliberate design feature: it turns an N+1 ([N+1 and how to see it](07-07-orms-and-ef-core.md#be-m156)) into a loud failure at development time rather than a silent latency problem in production.

``` csharp
// Batch loads instead of looping:
var customers = await session.LoadAsync<Customer>(ids);        // one request

// Fetch related documents alongside the query - still one request:
var orders = await session.Query<Order>()
    .Include(o => o.CustomerId)
    .Where(o => o.Status == "Open")
    .ToListAsync();
var c = await session.LoadAsync<Customer>(orders[0].CustomerId); // no server call

// Batch several unrelated queries into one round trip:
var lazyA = session.Query<Order>().Where(...).LazilyAsync();
var lazyB = session.Query<Customer>().Where(...).LazilyAsync();
await session.Advanced.Eagerly.ExecuteAllPendingLazyOperationsAsync();
```

> **\[Trap\]**
>
> **Do not raise `MaxNumberOfRequestsPerSession` to make an error go away.** It is the smoke alarm, not the fire. Hitting it means a loop is issuing per-item queries; the fix is `Include`, a batched `LoadAsync`, or a projection. For genuinely large result sets, use `session.Advanced.StreamAsync`, which does not track documents and does not count against the budget.

<a id="be-m205"></a>

#### RavenDB: indexes and eventual consistency

Every RavenDB query goes through an index. If none matches, the server creates an **auto-index** on the fly and reuses it. This makes development frictionless and hides a cost: an auto-index proliferation of near-identical indexes, each one maintained on every write.

``` csharp
// Auto-index gets created for this shape:
await session.Query<Order>().Where(o => o.Status == "Open").ToListAsync();

// A static index you control, versioned with your code:
public class Orders_ByCustomerAndStatus : AbstractIndexCreationTask<Order> {
    public Orders_ByCustomerAndStatus() {
        Map = orders => from o in orders
                        select new { o.CustomerId, o.Status, o.CreatedAt };
        Index(x => x.Status, FieldIndexing.Exact);
    }
}
await IndexCreation.CreateIndexesAsync(typeof(Program).Assembly, store);

var q = session.Query<Order, Orders_ByCustomerAndStatus>().Where(o => o.Status == "Open");
```

**Indexes are updated asynchronously.** A document you just saved may not appear in a query result for a few milliseconds — the index is "stale". Loading by id is always immediately consistent; querying is not.

``` csharp
// In tests, wait deliberately:
.Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(15)))
// In production code this is an anti-pattern: it converts a scalable
// asynchronous design into a synchronous wait under load.
```

> **\[Tip\]**
>
> **Design around staleness rather than against it.** After a write, use the document you already have in hand rather than re-querying for it. Return the saved entity from the command, and let list views tolerate a few milliseconds of lag — which users cannot perceive anyway.

> **\[Why it matters\]**
>
> **Map/reduce indexes** are the document-store answer to `GROUP BY` ([Aggregation: GROUP BY, HAVING, GROUPING SETS](07-02-the-relational-model-and-sql.md#be-d411)): the aggregation is computed incrementally as documents change, so the query reads a pre-aggregated result rather than scanning. That is a genuinely different performance profile from a relational `GROUP BY`, and it is why reporting over a document store can be fast if you plan the indexes and slow if you do not.

<a id="be-m206"></a>

#### Redis: the data structures

Redis is not just a string cache. Choosing the right structure is the difference between one round trip and hundreds.

| Type          | Key operations                             | Use for                                                            |
|---------------|--------------------------------------------|--------------------------------------------------------------------|
| `STRING`      | `GET`/`SET`/`INCR` — O(1)                  | cached blobs, counters, flags, locks                               |
| `HASH`        | `HGET`/`HSET`/`HINCRBY` — O(1)             | objects where you update one field                                 |
| `LIST`        | `LPUSH`/`BRPOP` — O(1) at ends             | simple queues, recent-items feeds                                  |
| `SET`         | `SADD`/`SISMEMBER` — O(1); `SINTER` — O(n) | membership, tags, unique visitors                                  |
| `ZSET`        | `ZADD`/`ZRANGE` — O(log n)                 | leaderboards, priority queues, time-ordered indexes, rate limiting |
| `STREAM`      | `XADD`/`XREADGROUP`                        | durable event log with consumer groups                             |
| `HYPERLOGLOG` | `PFADD`/`PFCOUNT` — O(1), 12KB fixed       | approximate distinct counts at any scale                           |
| `BITMAP`      | `SETBIT`/`BITCOUNT`                        | daily-active flags, dense boolean sets                             |

``` text
// Sorted sets are the most under-used. A sliding-window rate limiter:
ZREMRANGEBYSCORE key 0 (now - window)     -- drop old entries
ZCARD key                                  -- how many in the window
ZADD key now requestId                     -- record this one
EXPIRE key window
```

> **\[Trap\]**
>
> **Redis executes commands one at a time on a single thread.** One O(n) command over a large key blocks every other client. `KEYS *` is the classic outage (use `SCAN`, which is cursor-based). So are `SMEMBERS` on a million-member set, `HGETALL` on a huge hash, and `DEL` of a very large key (use `UNLINK`, which frees asynchronously). Watch for *big keys* and *hot keys* — both concentrate load onto one shard and one command.

> **\[Tip\]**
>
> **Round trips dominate.** Ten commands issued sequentially cost ten network latencies; the same ten pipelined cost one. In StackExchange.Redis the multiplexer pipelines automatically when you fire tasks without awaiting each one — `var t1 = db.StringGetAsync(a); var t2 = db.StringGetAsync(b); await Task.WhenAll(t1, t2);`. Use a Lua script when you need several operations to be atomic *and* in one round trip.

Worked example: [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)

<a id="be-m207"></a>

#### Cache key design and value format

``` text
// A key should be readable, namespaced, versioned, and tenant-scoped.
{app}:{version}:{tenant}:{entity}:{id}:{variant}
"the legacy system:v3:t42:order:1001:summary"
```

- **Namespace** so several applications can share an instance without collisions.
- **Version the schema** in the key. When the cached shape changes, bump the version — every old entry becomes unreachable and expires naturally. This removes the entire class of "deserialization failed after deploy" incidents, and it makes rollback safe.
- **Scope by tenant** explicitly. A cache key that forgets the tenant is a cross-tenant data leak, and it is one of the most damaging bug classes there is.
- **Never put a raw `GetHashCode()` in a key** — string hashing is randomised per process, so two servers compute different keys for the same input ([Hash codes: what makes a good one](03-03-hash-tables.md#be-m101)).

``` csharp
// Deriving a key from a complex query, deterministically:
static string KeyFor(Query q) {
    var json = JsonSerializer.Serialize(q, CanonicalOptions);   // stable property order
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
    return $"the legacy system:v3:t{q.TenantId}:report:{hash}";
}
```

**Value format.** JSON is debuggable and interoperable; MessagePack or protobuf are two to five times smaller and faster. For a distributed cache, size matters twice over — network transfer and Redis memory — so a compact format is often worth the loss of readability. Whatever you choose, make it tolerant of unknown fields so a rolling deploy with two versions running does not fail.

> **\[Trap\]**
>
> **Do not cache `DbContext`-tracked entities.** They carry lazy-loading proxies and navigation references that either fail to serialize or drag half the object graph in with them. Cache DTOs — flat, versioned, and serializable by design.

Worked example: [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)

Applied in: [Deployment strategies and rollback](11-04-ci-cd-pipelines.md#be-a220)

Recipe: [Recipe: a cached read with invalidation](08-06-caching.md#be-m288)

<a id="be-m209"></a>

#### JSON columns in a relational database

A JSON column is a pragmatic escape hatch for genuinely variable data — user preferences, integration payloads, sparse per-tenant attributes — and a mistake for anything you filter or join on regularly.

``` sql
-- SQL Server
SELECT JSON_VALUE(Payload, '$.customer.name') AS Name FROM Events
WHERE JSON_VALUE(Payload, '$.type') = 'OrderPlaced';

-- Postgres jsonb is far more capable: real operators and GIN indexes
SELECT payload ->> 'name' FROM events WHERE payload @> '{"type":"OrderPlaced"}';
CREATE INDEX ON events USING GIN (payload jsonb_path_ops);
```

``` csharp
// EF Core 8+ maps owned types and collections into a JSON column:
modelBuilder.Entity<Order>().OwnsOne(o => o.ShipTo, b => b.ToJson());
modelBuilder.Entity<Order>().OwnsMany(o => o.Tags, b => b.ToJson());
// and can translate filters into the document:
db.Orders.Where(o => o.ShipTo.City == "Ha Noi");
```

> **\[Trap\]**
>
> **What you give up.** No `NOT NULL`, no `CHECK`, no foreign keys, no column statistics — so the optimiser estimates blindly. Filtering on a JSON path is a computed expression, which means a scan unless you add a computed column with an index over it. And schema evolution moves from a migration you can review into application code that must tolerate every historical shape forever.

> **\[Tip\]** **The workable middle ground:** promote the two or three fields you actually query into real, indexed columns, and keep the rest in JSON. On SQL Server, a persisted computed column over `JSON_VALUE` plus an index on it gives you a seekable path without denormalising by hand.
>
> ``` sql
> ALTER TABLE Events ADD EventType AS JSON_VALUE(Payload,'$.type') PERSISTED;
> CREATE INDEX IX_Events_Type ON Events(EventType);
> ```

<a id="be-m210"></a>

#### Consistency across stores

The moment a single operation writes to two systems — database and cache, database and message broker, two databases — you no longer have a transaction, and you must choose a failure mode.

``` csharp
// Broken: the write succeeds, the process dies, the message is never sent.
await db.SaveChangesAsync();
await bus.PublishAsync(new OrderPlaced(id));   // may never run
```

**Patterns, in increasing order of guarantee:**

1.  **Best effort with retry.** Publish after commit, retry on failure, accept rare loss. Fine for a cache invalidation, unacceptable for a payment event.
2.  **Transactional outbox.** Write the message into an `Outbox` table *in the same transaction* as the business change. A background dispatcher reads unsent rows, publishes, and marks them sent. The message becomes as durable as the data.
3.  **Change data capture.** Tail the database log and derive events from committed changes. No application code in the write path at all.

**Cache coherence** has the same shape. Ordering matters more than it looks:

``` csharp
// Write-through: update the store, then INVALIDATE the cache (do not update it).
await db.SaveChangesAsync();
await cache.RemoveAsync(key);
// Removing is safe under concurrency; writing the new value is not - a slower
// concurrent read can overwrite your fresh value with a stale one it fetched earlier.
```

> **\[Why it matters\]**
>
> **Design for at-least-once, everywhere.** Every retry-based system delivers duplicates eventually. That makes *idempotency* the property to build in rather than exactly-once delivery, which is not achievable across a network. Give each message a stable id, record processed ids, and make handlers safe to run twice — then all of the above patterns become simple.

Worked example: [Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303)

Goes further: [Transactions across services: sagas and the outbox](12-01-monoliths-modules-and-services.md#be-m250)

Applied in: [Domain events](12-06-domain-modeling-and-application-architecture.md#be-m256)

### Modeling patterns that recur

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Modeling patterns that recur</p>

<a id="be-m216"></a>

#### Full-text search

`LIKE '%term%'` cannot use an index — the leading wildcard means every row must be examined ([Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160)). It also has no notion of word boundaries, stemming, or relevance. Full-text indexes solve all three by inverting the data: term → list of documents.

``` sql
-- SQL Server
CREATE FULLTEXT INDEX ON Products (Name, Description)
  KEY INDEX PK_Products WITH CHANGE_TRACKING AUTO;

SELECT p.*, ft.RANK
FROM Products p
JOIN FREETEXTTABLE(Products, (Name, Description), @query) ft ON ft.[KEY] = p.Id
ORDER BY ft.RANK DESC;

-- CONTAINS is precise; FREETEXT applies stemming and thesaurus expansion.
WHERE CONTAINS(Description, 'NEAR((wireless, keyboard), 5)')
```

``` sql
-- PostgreSQL: a stored, indexed tsvector is the idiomatic shape.
ALTER TABLE products ADD search tsvector
  GENERATED ALWAYS AS (to_tsvector('simple', coalesce(name,'') || ' ' || coalesce(description,''))) STORED;
CREATE INDEX ON products USING GIN (search);

SELECT *, ts_rank(search, q) AS rank
FROM products, websearch_to_tsquery('simple', $1) q
WHERE search @@ q ORDER BY rank DESC;
```

| Option                      | Good for                                      | Limits                                                                   |
|-----------------------------|-----------------------------------------------|--------------------------------------------------------------------------|
| `LIKE 'prefix%'`            | autocomplete on a short field — *is* sargable | prefix only                                                              |
| Computed search key + index | accent/case-folded exact matching             | no relevance, no stemming ([Normalization and searchable text](01-03-text-is-numbers-too.md#be-m86)) |
| Trigram index (`pg_trgm`)   | fuzzy and substring search                    | Postgres only, index is large                                            |
| Database full-text          | ranked word search, no new infrastructure     | weaker relevance tuning, per-engine syntax                               |
| Dedicated engine            | facets, synonyms, tuned relevance, scale      | a second system to sync and operate                                      |

> **\[Trap\]**
>
> **Language configuration is not a detail.** The stemmer and stop-word list are chosen per index and per query, and a mismatch silently returns nothing. Vietnamese in particular is not well served by most built-in analysers — word segmentation matters and diacritics carry meaning, so a folded search key alongside the full-text index usually gives better results than either alone.

> **\[Tip\]**
>
> **Reach for a dedicated engine only when you need what it uniquely offers** — relevance tuning, facets, synonyms, typo tolerance at scale. It is a second store, so it needs a sync pipeline, a reindex path, and an answer to "what if it is stale or down" ([Consistency across stores](#be-m210)). Database full-text has none of that and is enough for a great many products.

Worked example: [Case: the search that degraded as data grew](14-03-worked-cases-from-production.md#be-m313)

<a id="be-m217"></a>

#### Geospatial data

``` csharp
// NetTopologySuite is the .NET spatial type library, wired into EF providers.
options.UseSqlServer(cs, o => o.UseNetTopologySuite());

public class Store {
    public int Id { get; set; }
    public Point Location { get; set; } = null!;    // SRID 4326 = WGS 84 lat/lng
}

var origin = new Point(105.8342, 21.0278) { SRID = 4326 };   // NOTE: X = LONGITUDE
var nearby = await db.Stores
    .Where(s => s.Location.Distance(origin) < 5000)           // metres, on geography
    .OrderBy(s => s.Location.Distance(origin))
    .Take(20).ToListAsync(ct);
```

|                              | `geography`              | `geometry`                               |
|------------------------------|--------------------------|------------------------------------------|
| Model                        | a sphere (round earth)   | a flat plane                             |
| Distance units               | metres                   | whatever the coordinates are             |
| Accurate over long distances | yes                      | no                                       |
| Speed                        | slower                   | faster                                   |
| Use for                      | lat/lng, anything global | projected local coordinates, floor plans |

``` sql
-- A spatial index is essential; without one every query is a full scan.
CREATE SPATIAL INDEX SX_Stores_Location ON Stores(Location)
  USING GEOGRAPHY_AUTO_GRID;

-- The optimiser only uses it when the predicate is in a recognised form -
-- typically Distance(...) < @d or STIntersects(...) = 1. A computed distance
-- in the SELECT with a filter on the alias will not qualify.
```

> **\[Trap\]**
>
> **Coordinate order is the classic bug.** `Point(x, y)` means `Point(longitude, latitude)` — the reverse of how humans say and write coordinates, and the reverse of GeoJSON's own display convention in many tools. Swapping them puts Hanoi in the Indian Ocean, and it is a silent error: the query runs and returns nothing. Wrap construction in a named factory (`FromLatLng`) so the order is stated once.

> **\[Tip\]**
>
> **Bounding box first, distance second.** Even with a spatial index, filtering by a cheap rectangle before computing exact distances is faster for "nearest N" queries at scale. For very high volume, geohashing or H3 cells turn proximity into a string-prefix or integer match — which any ordinary B-tree index handles ([Indexes for LINQ writers](07-03-indexes-and-query-plans.md#be-m171)).

### Caching

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Caching</p>

<p class="pd">Caching as a discipline rather than a header. Where each layer belongs, the four patterns, why invalidation is the hard part, and operating the cache you chose.</p>

<a id="be-g654"></a>

#### Operating Redis as infrastructure

Redis is the default distributed cache, and it is also a database, a queue and a lock service depending on how it is used. The operational decisions differ sharply between those uses.

| Decision           | As a cache                           | As a store                                         |
|--------------------|--------------------------------------|----------------------------------------------------|
| Persistence        | **Off**, or RDB snapshots only       | AOF with `everysec` fsync                          |
| `maxmemory-policy` | `allkeys-lru` — evict freely         | `noeviction` — refuse writes rather than lose data |
| Losing everything  | Acceptable — it rebuilds from source | **Unacceptable** — needs replication and backups   |
| High availability  | Nice to have                         | Required — Sentinel or Cluster                     |
| Sizing             | Working set + headroom               | All data + headroom                                |

``` text
# the settings that matter for a cache
maxmemory 4gb
maxmemory-policy allkeys-lru       # evict the least recently used when full
                                   # DEFAULT is noeviction, which makes Redis
                                   # start REFUSING WRITES when full - a very
                                   # surprising way for a cache to fail
save ""                            # no snapshots; it is a cache
appendonly no
timeout 300
tcp-keepalive 60

# and the metrics to watch
#   used_memory vs maxmemory       how close to eviction
#   evicted_keys                   rising = the cache is too small
#   keyspace_hits / keyspace_misses   the hit ratio. Below ~80% and the
#                                     cache may be costing more than it saves
#   blocked_clients                 something is doing a blocking operation
#   instantaneous_ops_per_sec
```

``` text
  THE SINGLE-THREADED FACT that explains most Redis incidents

    Redis executes commands ONE AT A TIME, on one thread.
    that is why it is fast and predictable - no locking, no contention.

    it also means ONE SLOW COMMAND BLOCKS EVERYTHING.

      KEYS *              O(n) over every key. NEVER in production.
                          use SCAN, which is cursor-based and incremental.
      FLUSHALL            blocks until done
      a 500 MB value      serialising it blocks the server
      SMEMBERS on a huge set, LRANGE 0 -1, HGETALL on a big hash
      a Lua script with a loop

    symptom: latency spikes across EVERY client at once, with low CPU
             on the client side. Look for slow commands:

               redis-cli SLOWLOG GET 25
               redis-cli --latency-history
```

> **\[Trap\]**
>
> **Common trap.** Do not use Redis as a distributed lock for anything requiring correctness. Clock skew, GC pauses and network partitions can all allow two holders of the same lock, and the standard algorithms are contested precisely on this point. It is fine for “probably only one worker runs this job”; it is not fine for “only one process may decrement this stock level”. That belongs in a database constraint or an atomic conditional update.
