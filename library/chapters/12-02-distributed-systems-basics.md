---
id: "12-2"
number: "12.2"
title: "Distributed systems basics"
part: 12
part_title: "Systems and architecture"
words: 7898
minutes: 34
---

# 12.2 Distributed systems basics

> **Quick look.** What changes when parts fail independently, messages get lost and clocks disagree.
>
> - Systems fail partially, and a slow part looks exactly like a dead one: a timeout tells you nothing about what happened.
> - Make every operation safe to retry (idempotent).
> - Quorums with W + R > N guarantee reading the latest write.
> - CAP: during a partition choose consistency or availability; otherwise the trade is latency (PACELC).
> - Read-your-writes and monotonic reads are the guarantees users notice.
> - Never order events across machines by wall-clock time; last-write-wins silently discards data.
> - Consensus needs majorities; distributed locks need fencing tokens. Use proven infrastructure, never homemade consensus.


### The big picture

Leslie Lamport, one of the founders of the field, once defined a distributed system as one "in which the failure of a computer you didn't even know existed can render your own computer unusable." Any system whose parts talk over a network qualifies: services calling services, a database with replicas, a cache, a message broker, a load balancer. Nearly every system in this guide is one. What makes them hard isn't the code in each part. It is that the parts can fail independently, messages can be lost or delayed, and no two machines agree exactly on the time. On a single machine, those things either can't happen or happen all at once.

This chapter covers the handful of ideas that explain most distributed systems behaviour: partial failure, and why a lost reply is so troublesome; replication and the range of consistency guarantees; quorums; and the CAP theorem, stated correctly. It explains why clocks can't be trusted to order events, and what logical clocks do instead. It then covers how machines agree on a leader or a value, why only a majority may decide, and how a paused process can corrupt data unless storage checks a fencing token. The example simulates five of these problems deterministically, so each can be seen happening, and being prevented.

### Before you start

**Builds on:** 6.1 Layers and packets, 2.8 Errors and exceptions, 8.2 API design, 8.7 Background work and queues, 7.6 Non-relational stores, 1.7 The operating system.

**Ideas explained later:**

- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **Failover** (12.5): Switching work from a failed component to a standby or replica.
- **Chaos engineering** (12.5): Deliberately injecting failures, in a controlled way, to test that a system copes.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. A request times out. What do you know about whether it was processed?**

> Nothing. The request may have been lost, it may have been processed and the reply lost, or it may still be running. That is why operations must be safe to retry: idempotent, or protected by an idempotency key.

**2. With three replicas, why does reading from two and writing to two guarantee reading the latest write?**

> Any two replicas out of three overlap in at least one. If W + R is greater than N, every read set includes at least one replica that took part in the last successful write.

**3. What does the CAP theorem actually say?**

> That when a network partition separates parts of a system, it must choose between consistency, refusing some requests, and availability, answering with possibly stale data. Without a partition, both are possible; the practical trade-off then is consistency against latency.

**4. Why is "last write wins" by timestamp dangerous across machines?**

> Machines' clocks differ, often by milliseconds and sometimes by much more. The write with the higher timestamp may really have happened first, so a later write is silently discarded.

**5. What does a Lamport clock capture?**

> The order of causally related events: if one event could have influenced another, it gets a smaller counter value. It doesn't need synchronized clocks, only that each message carries the sender's counter.

**6. Why must a leader election need a majority?**

> Two majorities of the same cluster always overlap, so two sides of a partition can't both have one. That prevents split brain, where two nodes each believe they are the leader.

**7. What is a fencing token?**

> A number that increases each time a lock or lease is granted. Storage remembers the highest token it has seen and rejects writes with older tokens, so a holder whose lease expired during a pause can't overwrite newer work.

### What makes distributed systems hard

A **distributed system** is a set of programs on different machines that cooperate over a network to behave as one system. The eight fallacies from Layers and packets (6.1) describe what goes wrong: the network is not reliable, latency is not zero, and so on. Their consequence is **partial failure**. On one machine, a crash stops everything at once. In a distributed system, some parts fail while others carry on, and the parts that carry on often can't tell what happened to the others.

The deepest form of this is that a slow machine and a dead one look the same from outside. A request with no reply might have been lost, might be waiting in a queue, might be running slowly, or might have finished with the reply lost on the way back. A timeout is only a guess that it has gone wrong. Every design decision in this chapter comes from that uncertainty.

### Lost replies and retries

The most common consequence is the lost reply. A client asks for a booking, the server makes it, and the reply never arrives. The client times out and retries, and the passenger has two bookings. The example's first simulation shows exactly that.

The defences come from earlier chapters. Make operations idempotent where possible, from Errors and exceptions (2.8). Otherwise, have clients send an idempotency key that the server remembers, from API design (8.2). And have message consumers ignore duplicates, from Background work and queues (8.7). Retries themselves need exponential backoff with jitter, so that a failing service isn't hammered by every client at once. In a distributed system, retries are unavoidable, so everything must tolerate them.

### Replicas and consistency

Data is copied to several machines, the replication from Non-relational stores (7.6), so that it survives failures and can be read from more places. The question is what readers see while copies are being updated. The answer is a system's **consistency model**, and there is a range of them.

The strongest is **linearizability**: the system behaves as if there were a single copy, and every read sees the latest completed write. It is what programmers intuitively expect, and it costs coordination, and so latency, on every operation. At the other end is eventual consistency, also from Non-relational stores (7.6): copies converge once writes stop, but reads in between may be stale. In between are models such as read-your-writes, where each user sees their own changes, and causal consistency, where anything that caused a change is seen before the change itself.

Many replicated stores let you choose per operation with a **quorum**. With N replicas, a write waits for W of them to confirm, and a read asks R of them and takes the newest answer. If W + R is greater than N, every set of replicas read overlaps every set written, so a read always includes at least one up-to-date copy. With three replicas, writing to two and reading from two achieves that; writing to one and reading from one is faster, and can return stale data, as the example's second simulation shows. Cassandra and similar stores expose these settings directly.

### The CAP theorem

The **CAP theorem**, conjectured by Eric Brewer in 2000 and proved in 2002, is often summarized as "consistency, availability, partition tolerance: pick two". That summary is misleading. Networks do partition: a **network partition** is when some machines can't reach others, even though all are running. So partition tolerance isn't optional. The theorem really says that during a partition, a system must choose. It can stay consistent by refusing requests on the side that can't coordinate, giving up availability. Or it can stay available by answering on both sides, giving up consistency, because the two sides can't see each other's writes.

Partitions are rare. Most of the time, the real trade-off is between consistency and latency, since coordinating copies takes round trips. The PACELC formulation captures both: if there is a Partition, choose Availability or Consistency; Else, choose Latency or Consistency. A bank balance and a seat inventory lean towards consistency. A "recently viewed flights" list and a like counter lean towards availability and speed.

Businesses often make this trade-off consciously, long before software does. Airlines overbook flights: they accept that the booking system may sell more seats than exist, because some passengers don't turn up, and they compensate the few who are affected. Choosing where inconsistency is acceptable, and how to repair it when it matters, is often better than paying for strict consistency everywhere.

### Time and order

On one machine, events have an order. Across machines, deciding which of two events happened first is surprisingly hard. Each machine's clock drifts, and synchronization with NTP, the Network Time Protocol, keeps clocks only roughly aligned, typically within a few milliseconds. The difference between two machines' clocks is **clock skew**. Clocks can also jump, forwards or backwards, when they are corrected. That is why durations should always be measured with a monotonic clock, from The operating system (1.7), never by subtracting wall-clock times.

This matters most when timestamps decide what to keep. **Last-write-wins** resolves conflicting writes by keeping the one with the latest timestamp. With skewed clocks, the "latest" timestamp can belong to the earlier write, and the real latest write is silently discarded, as the example's third simulation shows. Last-write-wins is acceptable only where losing an update doesn't matter.

A **logical clock** orders events without trusting physical time. In a **Lamport clock**, from a 1978 paper by Lamport, each process keeps a counter. It increments the counter for each event, sends it with each message, and on receiving a message sets its own counter past the one received. If one event could have caused another, it always has a smaller value. Vector clocks extend the idea to tell whether two events were concurrent, neither having seen the other, which is exactly the case where a conflict must be resolved. Some systems use very accurate clocks instead: Google's Spanner database uses atomic clocks and GPS to bound clock uncertainty, and waits out that uncertainty before committing.

### Agreeing on things

Some decisions need every machine to agree: which node is the leader that accepts writes, which configuration is current, whether a transaction committed. Reaching agreement despite machines failing and messages being lost is called **consensus**. It is famously hard to get right. The Paxos algorithm solved it in theory, and the **Raft** algorithm, published in 2014, was designed to be understandable and is now widely used.

Both depend on majorities. **Leader election** chooses one node to coordinate the others, and a candidate becomes leader only when a majority of the cluster votes for it. Two majorities of the same cluster always share at least one member, so two sides of a partition can never both elect a leader. The side without a majority waits. Without that rule, a partition can produce **split brain**: two nodes each believing they are the leader, both accepting writes, and the data diverging. It is why clusters have an odd number of members, three or five, and why the example's fifth simulation lets only the three-node side elect.

Don't implement consensus yourself. Use systems built on it: etcd, which stores Kubernetes' state; ZooKeeper and Consul; databases with built-in failover; and cloud services' managed coordination.

### Locks, leases and pauses

Distributed locks, such as the one from Background work and queues (8.7) that stops a scheduled job running twice, are usually leases. A **lease** is a lock that expires after a set time unless renewed, so a crashed holder doesn't hold it forever.

Leases have a subtle failure. A process takes the lease and then pauses: a long garbage collection, from Memory management and garbage collection (2.7), a virtual machine being migrated, or a slow disk. While it is paused, the lease expires and another process takes it and does the work. Then the first process wakes up, still believing it holds the lease, and writes its stale results over the new ones. Checking the lease before writing doesn't help, because the pause can happen between the check and the write.

The fix is a **fencing token**: a number that increases every time the lease is granted. Every write carries the holder's token. Storage remembers the highest token it has seen, and rejects writes with older ones. The example's fourth simulation shows the woken process's write being refused. The same idea, attaching a version and letting storage refuse stale ones, is the optimistic concurrency from Transactions and isolation (7.4).

### Seeing it for real

This script simulates five of the problems in this chapter, deterministically, so each outcome is the same on every run.

```typescript
// dist.ts: run with: node dist.ts   (five classic distributed systems problems, simulated deterministically)

// 1. A lost reply: the caller can't tell "it failed" from "it worked and the answer was lost"
{
  const bookings: string[] = [];
  const seen = new Map<string, string>();
  const server = (passenger: string, key?: string) => {
    if (key && seen.has(key)) return seen.get(key)!;            // a repeat: return the first answer
    const id = `B${bookings.push(passenger)}`;
    if (key) seen.set(key, id);
    return id;
  };
  const callLosingFirstReply = (send: () => string) => { send(); /* the reply is lost: timeout */ return send(); };   // retry once
  callLosingFirstReply(() => server("Ada"));
  console.log(`1. retry without a key: ${bookings.length} bookings for Ada`);           // 2
  bookings.length = 0;
  callLosingFirstReply(() => server("Ada", "key-7c1e"));
  console.log(`   retry with an idempotency key: ${bookings.length} booking`);           // 1
}

// 2. Quorums: N replicas, a write waits for W of them, a read asks R of them
{
  type Copy = { value: string; version: number };
  const replicas: Copy[] = [{ value: "A10", version: 1 }, { value: "A10", version: 1 }, { value: "A10", version: 1 }];
  const reachable = [true, true, false];                          // replica 3 is cut off by a network partition
  const write = (value: string, version: number, W: number) => {
    let acks = 0;
    replicas.forEach((r, i) => { if (reachable[i] && acks < W) { r.value = value; r.version = version; acks++; } });
    return acks >= W;
  };
  const read = (R: number, order: number[]) => {                 // ask R replicas, in the given order, and keep the newest
    const answers = order.filter(i => reachable[i]).slice(0, R).map(i => replicas[i]!);
    return answers.length < R ? "no quorum" : answers.sort((a, b) => b.version - a.version)[0]!.value;
  };
  write("B12", 2, 1);                                             // W=1: acknowledged by replica 1 alone
  reachable[0] = false; reachable[2] = true;                      // the partition moves: replica 1 now cut off, 3 back
  console.log(`2. W=1, R=1 (W+R <= N): read gets ${read(1, [2, 1, 0])}`);              // A10: a stale value
  reachable.fill(true); replicas.forEach(r => { r.value = "A10"; r.version = 1; });
  reachable[2] = false;
  write("B12", 2, 2);                                             // W=2: replicas 1 and 2
  reachable[0] = false; reachable[2] = true;
  console.log(`   W=2, R=2 (W+R > N): read gets ${read(2, [2, 1, 0])}`);               // B12: the sets must overlap
}

// 3. Clocks disagree: "last write wins" by wall-clock time can keep the older write
{
  const clockA = 10_000, clockB = 10_000 - 300;                  // B's clock runs 300 ms behind A's
  const writes = [{ node: "A", gate: "B12", at: clockA }, { node: "B", gate: "C3", at: clockB + 100 }];   // B writes 100 ms later, really
  const winner = writes.sort((x, y) => y.at - x.at)[0]!;
  console.log(`3. last write wins by timestamp keeps gate ${winner.gate}, but C3 was written later`);
  let lamportA = 0, lamportB = 0;                                 // logical clocks: count events, and jump past what you've seen
  const sendA = ++lamportA;                                       // A writes B12 and tells B about it
  lamportB = Math.max(lamportB, sendA) + 1;                       // B receives, then writes C3
  console.log(`   Lamport clocks: B12 at ${sendA}, C3 at ${lamportB}, so C3 is known to come after B12`);
}

// 4. A paused lock holder: the lease expires, someone else takes over, and the storage must refuse the old holder
{
  let highestToken = 0;
  const storage = (token: number, data: string) => {             // accept writes only from the newest lock holder
    if (token < highestToken) return `rejected ${data} (token ${token} is older than ${highestToken})`;
    highestToken = token;
    return `stored ${data}`;
  };
  const tokenA = 33;                                              // A takes the lock... and pauses for a long garbage collection
  const tokenB = 34;                                              // the lease expires; B takes the lock and writes
  console.log(`4. ${storage(tokenB, "B's roster")}; then A wakes: ${storage(tokenA, "A's roster")}`);
}

// 5. Leader election: only a side with a majority may choose a leader, so two leaders can't exist at once
{
  const cluster = 5;
  for (const [side, nodes] of [["left", 3], ["right", 2]] as const)
    console.log(`5. ${side} side of the partition, ${nodes} of ${cluster}: ${nodes > cluster / 2 ? "elects a leader" : "can't elect, waits"}`);
}
```

Its output:

```text
1. retry without a key: 2 bookings for Ada
   retry with an idempotency key: 1 booking
2. W=1, R=1 (W+R <= N): read gets A10
   W=2, R=2 (W+R > N): read gets B12
3. last write wins by timestamp keeps gate B12, but C3 was written later
   Lamport clocks: B12 at 1, C3 at 2, so C3 is known to come after B12
4. stored B's roster; then A wakes: rejected A's roster (token 33 is older than 34)
5. left side of the partition, 3 of 5: elects a leader
5. right side of the partition, 2 of 5: can't elect, waits
```

Each line pairs a failure with its defence: an idempotency key, overlapping quorums, a logical clock, a fencing token, and a majority rule. None of them needs clever code. They need the designer to have expected the failure.

### Designing for it

A few habits keep distributed systems manageable.

Coordinate only where the business needs it. Strict consistency is expensive, so apply it to the few things that require it, such as seat inventory and payments, and let the rest be eventually consistent. Prefer asynchronous communication, from Messaging and events (12.3), so a slow or failed part delays work instead of blocking it. Keep order requirements per key, such as per booking, rather than global, since partitions and ordered keys scale while a global order doesn't, as Non-relational stores (7.6) described.

Assume every call can time out, every message can arrive twice or late, and every clock can be wrong. Build on proven infrastructure, databases, brokers and coordination services, for the hard parts, rather than inventing protocols. And test failure deliberately. Tools that inject faults, and the chaos engineering from Reliability (12.5), find the assumptions that only break when a network cable is pulled. The Jepsen project has found consistency bugs in many well-known databases this way.

### In short

Distributed systems fail partially: parts fail independently, and a slow part looks exactly like a dead one, so a timeout tells you nothing about what happened. Make every operation safe to retry. Replicas trade consistency against latency and availability, from linearizable to eventual; quorums with W + R greater than N guarantee reading the latest write. During a network partition, CAP forces a choice between consistency and availability; otherwise, the trade-off is latency. Clocks skew and jump, so never order events across machines by timestamp; logical clocks capture causality. Agreement needs consensus with majorities, which prevents split brain. Leases expire during pauses, so storage must check fencing tokens. Coordinate only where needed, and use proven infrastructure.

### What goes wrong

**Treating a timeout as failure.** The operation succeeded and the retry does it again. Make operations idempotent or keyed.

**Retrying without backoff.** Every client retries at once and keeps the struggling service down. Use exponential backoff with jitter and a limit.

**Reading from any replica after a write.** Users don't see their own changes. Use read-your-writes routing or quorum reads.

**Ordering by wall-clock timestamps.** Last-write-wins silently drops newer updates when clocks are skewed. Use versions, logical clocks or a single authority.

**Even-sized clusters and no quorum rule.** A partition splits a cluster in half and both halves carry on, or neither can. Use odd sizes and majority rules.

**Locks without fencing.** A paused holder wakes and overwrites a newer holder's work. Carry fencing tokens and have storage check them.

**Homemade consensus.** A custom leader-election scheme fails in exactly the rare cases that matter. Use etcd, ZooKeeper, a database's failover or a managed service.

**Strict consistency everywhere.** Every operation pays for coordination it doesn't need. Decide per piece of data what the business can tolerate.

### Cost model

Coordination costs round trips. A quorum write in one region waits for the second-fastest of three replicas, typically a millisecond or two. Across regions it waits for tens to hundreds of milliseconds, which is why strongly consistent global systems are slow to write. Consensus needs a majority to reply for each decision, so a five-node cluster tolerates two failures at the cost of waiting for three nodes. Eventually consistent reads cost one round trip to the nearest copy. Idempotency costs a lookup per request. Every retry adds load exactly when a system is struggling, which is why backoff and limits matter more than the retry itself.

### In other languages (optional)

**Every stack.** These problems belong to networks, not languages: a .NET service, a Node.js service and a Go service face the same lost replies and skewed clocks.

**Libraries and platforms.** .NET's Orleans framework and Akka for the JVM and .NET build distributed applications from actors with location transparency, and handle much of the coordination. Dapr offers building blocks such as state stores, pub/sub and distributed locks as a sidecar for any language.

**Databases.** Azure Cosmos DB exposes five consistency levels from strong to eventual; Cassandra exposes per-query quorums; CockroachDB and Spanner offer serializable transactions across regions using consensus.

### Key terms

- **Distributed system**: Programs on several machines cooperating over a network to behave as one system.
- **Partial failure**: Some parts of a distributed system failing while others keep running.
- **Consistency model**: The guarantees a replicated system gives about what reads can see.
- **Linearizability**: The strongest consistency model: the system behaves as a single copy, with every read seeing the latest write.
- **Quorum**: The number of replicas that must take part in an operation, chosen so that reads and writes overlap.
- **Network partition**: A network failure that splits machines into groups that can't reach each other.
- **CAP theorem**: During a network partition, a system must choose between consistency and availability.
- **Clock skew**: The difference between two machines' clocks at the same moment.
- **Last-write-wins**: Resolving conflicting writes by keeping the one with the latest timestamp.
- **Logical clock**: A counter that orders events by cause and effect instead of by physical time.
- **Lamport clock**: A logical clock where each process increments a counter per event and jumps past counters it receives.
- **Consensus**: Getting several machines to agree on a value despite failures and lost messages.
- **Raft**: A widely used consensus algorithm designed to be understandable.
- **Leader election**: Choosing one node to coordinate a cluster, with a majority vote.
- **Split brain**: Two parts of a cluster each acting as the leader, so their data diverges.
- **Lease**: A lock that expires after a set time unless renewed.
- **Fencing token**: An increasing number issued with each lease, used by storage to reject stale holders' writes.

### Where this leads

Connecting services with events and keeping their data consistent without distributed transactions is in Messaging and events (12.3). Timeouts, retries, circuit breakers and testing failure on purpose are in Reliability (12.5). The replication and partitioning that make data distributed are in Non-relational stores (7.6).

### Check yourself

1. In `dist.ts`, change the second quorum case to W=3, R=1, and explain the result, including what happens to writes while one replica is cut off.
2. Add a third node, C, to the Lamport clock example, which receives B's write and then writes itself, and print all three counters.
3. Remove the fencing check from `storage` and describe exactly what the paused holder would do to the roster.
4. For a system you know, list three pieces of data and decide, for each, whether it needs strong consistency or can be eventually consistent, and why.
5. Find where a system you work on uses timestamps to decide which update wins, and decide whether skewed clocks could lose data there.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A distributed lock that two workers both held

A Redis lock with a 30-second expiry protects a ledger job. A 45-second GC pause lets the lock expire; a second worker takes it and both write.

*Fencing tokens*

```sql
-- each lock grant returns an increasing token; the ledger rejects stale holders
UPDATE Ledgers
SET    Balance = @newBalance, FenceToken = @token
WHERE  Id = @ledgerId AND FenceToken < @token;   -- an older token updates 0 rows
-- @@ROWCOUNT = 0 -> this worker lost the lock while paused: abort
```

> **Lesson:** Leases expire during pauses. The resource itself must reject writes from holders that lost the lock.

#### Example 2: Saved, but the page still shows the old value

A user updates their phone number and the profile page, read from a replica, shows the old one.

*Read your own writes*

```csharp
// after a write, remember it briefly for this user
http.Response.Cookies.Append("rw", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
    new CookieOptions { HttpOnly = true, MaxAge = TimeSpan.FromSeconds(10) });

// on reads: recent writer -> primary, otherwise replica
var db = http.Request.Cookies.ContainsKey("rw") ? primaryDb : replicaDb;
```

> **Lesson:** Replicas lag. Users must at least see their own changes: route their reads to the primary briefly or return the saved state.

### Resilience & Distributed Systems Patterns

<p class="src">From <em>.NET Backend Deep Dive</em> · Resilience & Distributed Systems Patterns</p>

<a id="dn-50"></a>

#### Distributed locks — correct is harder than it looks

```csharp
// Acquire: random token + NX + PX, atomically
var token = Guid.NewGuid().ToString();
bool acquired = await db.StringSetAsync(
    $"lock:{resource}", token,
    TimeSpan.FromSeconds(30),
    When.NotExists);
```

```lua
-- Release MUST use Lua: delete only if the token is still yours.
-- A plain DEL can delete someone else's lock after yours expired.
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
else
    return 0
end
```

**The fundamental problem you can't eliminate:** the lock expires while you're still working (a long GC pause, a slow network). Now two processes both believe they hold it.

**The real answer:** use a **fencing token** — issue a monotonically increasing number with each lock grant, and have the downstream resource reject writes carrying an older token. Or, more simply: **make the operation idempotent** and treat the lock as an optimization (reducing duplicate work) rather than a correctness guarantee.

> For .NET job scheduling, consider SQL Server's `sp_getapplock` (it rides your existing transaction and releases itself) or Kubernetes Lease-based leader election — simpler and more correct than hand-rolling Redlock.

### Distributed Systems

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Distributed Systems</p>

<p class="pd">What changes the moment there is more than one machine. Partial failure, consistency models, consensus, clocks, and why idempotency is the property that makes any of it survivable.</p>

<a id="be-g679"></a>

#### What changes when there is more than one machine

A single process either works or crashes. A distributed system has a third state that has no local analogue, and nearly every hard problem in this part comes from it.

``` text
  THE THIRD STATE: PARTIAL FAILURE

    you send a request. No response arrives. What happened?

      the request never arrived                   -> the work did NOT happen
      it arrived, the work happened, the reply
        was lost                                  -> the work DID happen
      it arrived, the work is still running       -> the work MIGHT happen
      the server is up but unreachable from you   -> unknowable from here

    YOU CANNOT TELL THESE APART. There is no timeout value that
    distinguishes "slow" from "dead" - that is not a tooling limitation,
    it is a property of asynchronous networks.

    so every remote call has THREE outcomes, not two:
      success, failure, and DON'T KNOW.

    and "don't know" is the one your code must handle, because it is the
    one that decides whether retrying is safe.
```

| The fallacies of distributed computing | What it actually means for you                   |
|----------------------------------------|--------------------------------------------------|
| The network is reliable                | Every call needs a timeout and a retry policy    |
| Latency is zero                        | Chatty protocols cost round trips; batch them    |
| Bandwidth is infinite                  | Payload size is a real constraint                |
| The network is secure                  | Encrypt in transit; authenticate every hop       |
| Topology does not change               | Instances come and go; discover, do not hardcode |
| There is one administrator             | Dependencies are upgraded without telling you    |
| Transport cost is zero                 | Serialisation and egress cost money and CPU      |
| The network is homogeneous             | Different MTUs, proxies, TLS versions, timeouts  |

> **\[Note\]**
>
> **The design consequence.** Because you cannot distinguish a lost request from a lost reply, **retrying is the only recovery available and it is only safe if the operation is idempotent**. That single sentence drives the outbox pattern, idempotency keys, at-least-once delivery, and the reason exactly-once delivery is not achievable. Everything else in this part elaborates it.

<a id="be-g680"></a>

#### Consistency models: CAP, and the more useful PACELC

``` text
  CAP - famous, and narrower than people think

    during a network PARTITION you must choose:
      CONSISTENCY   refuse to answer rather than answer wrongly
      AVAILABILITY  answer, possibly with stale data

    note the precondition: DURING A PARTITION. Partitions are rare.
    and "CA" systems do not exist in a distributed setting - if the
    network can partition, you are choosing between C and A when it does.

  PACELC - extends it to the normal case, which is what you live with daily

    if PARTITION:  choose Availability or Consistency
    ELSE:          choose Latency or Consistency

    PostgreSQL, single node    EL/EC   no partition to speak of
    DynamoDB                   PA/EL   tunable to EC per read
    Cassandra                  PA/EL   tunable by consistency level
    Spanner, CockroachDB       PC/EC   pays latency for consistency
    MongoDB                    PA/EC   majority reads/writes by default

    the ELSE half is the one you feel every day: choosing "eventually
    consistent" is choosing LOWER LATENCY IN THE NORMAL CASE, not just
    different behaviour during an outage.
```

| Model                | Guarantees                                                    | Cost                                    |
|----------------------|---------------------------------------------------------------|-----------------------------------------|
| **Linearizable**     | Every read sees the latest write, globally, as if one machine | A coordination round trip per operation |
| **Sequential**       | All nodes see operations in the same order                    | Cheaper; a read may be stale            |
| **Causal**           | If A caused B, everyone sees A before B                       | Practical middle ground                 |
| **Read-your-writes** | You see your own changes                                      | Session stickiness, or a read cursor    |
| **Monotonic reads**  | You never see time go backwards                               | Pin a session to one replica            |
| **Eventual**         | Replicas converge, given no new writes                        | Cheapest, weakest — and often fine      |

The two session guarantees in the middle are the ones that matter most in practice, because their absence is what users *notice*. Read-your-writes missing means someone saves a form and sees the old value. Monotonic reads missing means refreshing a page shows newer data, then older data — which reads as a bug even when every individual response was valid.

> **\[Note\]**
>
> **Ask what the user must never see.** “Eventually consistent” is an acceptable answer for a follower count, a search index or a dashboard. It is not acceptable for a balance the user just changed, or for a uniqueness check guarding an insert. Classify reads by that question rather than choosing one consistency level for the whole system.

<a id="be-g681"></a>

#### Consensus, quorums and clocks

``` text
  THE QUORUM ARITHMETIC that underpins every HA system

    N replicas. W = how many must acknowledge a write.
                R = how many are read from.

    if W + R > N, a read is GUARANTEED to see the latest write,
    because the read set and the write set must overlap.

      N=3, W=2, R=2  ->  4 > 3  strongly consistent, survives 1 failure
      N=3, W=1, R=1  ->  2 < 3  fast, EVENTUALLY consistent
      N=3, W=3, R=1  ->  fast reads, and ANY failure blocks writes

    and the reason clusters have an ODD number of nodes: a majority of 3
    is 2, so it tolerates 1 failure. A majority of 4 is 3 - so 4 nodes
    tolerate exactly the same single failure as 3, at higher cost.

    TWO NODES CANNOT FORM A MAJORITY. This is why a two-node cluster
    cannot safely fail over automatically: it cannot distinguish a dead
    peer from a partition, and both nodes reach the same conclusion.
```

| Mechanism             | Does                                                         | You meet it as                            |
|-----------------------|--------------------------------------------------------------|-------------------------------------------|
| **Raft** / **Paxos**  | Elects a leader and replicates a log with majority agreement | etcd, Consul, CockroachDB, Kafka's KRaft  |
| **Quorum read/write** | Overlapping read and write sets                              | Cassandra, DynamoDB consistency levels    |
| **Leader election**   | Exactly one node holds a role                                | “Only one instance runs this job”         |
| **Fencing**           | Ensures a demoted leader cannot still write                  | The step whose absence causes split brain |
| **Lease**             | A time-bounded claim that expires on its own                 | How presence and locks should be modelled |

``` text
  CLOCKS - why "just use a timestamp" fails

    WALL CLOCK (DateTime.UtcNow)
      jumps. NTP corrects it; it can go BACKWARDS. Skew between machines
      is routinely tens of milliseconds and occasionally minutes.
      -> NEVER use it to order events across machines
      -> NEVER use it to measure elapsed time (use Stopwatch)

    LOGICAL CLOCK (Lamport)
      a counter per node, incremented on send and merged on receive.
      gives a consistent ORDER, with no relation to real time.

    VECTOR CLOCK
      a counter per node, per node. Can detect CONCURRENT updates -
      "these two writes did not know about each other" - which is what
      conflict resolution needs.

    HYBRID LOGICAL CLOCK (HLC)
      physical time plus a logical counter. Close to wall-clock, and
      monotonic. What CockroachDB and YugabyteDB use.

  the practical rules
    order events with a sequence number or an HLC, not a timestamp
    measure durations with a monotonic clock
    treat clock skew as a monitored metric - it breaks TLS, JWT
      validation, TOTP and log correlation long before it breaks ordering
```

> **\[Trap\]**
>
> **Common trap.** Last-write-wins conflict resolution using wall-clock timestamps silently discards data. Two nodes accept concurrent writes, the one with the later clock reading wins, and the other update is gone with no error and no record — and which node “wins” depends on NTP skew rather than on what actually happened last. If you must use last-write-wins, be explicit that it is lossy; if you cannot lose writes, you need a vector clock, a CRDT, or server-side arbitration.

###### Reading a real cluster's quorum state , and split brain

``` text
# etcd - what a healthy cluster looks like, and what to check
etcdctl endpoint status --cluster -w table
# +------------------+------------------+---------+-----------+
# |     ENDPOINT     |        ID        | IS LEADER| RAFT TERM |
# +------------------+------------------+---------+-----------+
# | http://10.0.1.10 | 8e9e05c52164694d |    true |        14 |
# | http://10.0.1.11 | 91bc3c398fb3c146 |   false |        14 |
# | http://10.0.1.12 | fd422379fda50e48 |   false |        14 |
#
# READ THESE THREE THINGS:
#   exactly ONE leader          two leaders = split brain, right now
#   the SAME raft term          differing terms = a node is partitioned
#   RAFT TERM climbing steadily = repeated elections. Usually a network
#                                 or disk-latency problem, not a bug.

etcdctl endpoint health --cluster    # quorum present?
```

``` text
  WHAT ACTUALLY HAPPENS DURING A PARTITION - 5 nodes, split 3 / 2

    +--- majority side (3) ---+        +--- minority side (2) ---+
    | has quorum              |        | NO quorum               |
    | elects a leader         |        | cannot elect a leader   |
    | ACCEPTS WRITES          |        | REFUSES writes          |
    | continues normally      |        | may serve STALE reads   |
    +-------------------------+        +-------------------------+

    this is correct behaviour, and it is CP: the minority chooses
    unavailability over inconsistency.

  SPLIT BRAIN is what happens when the minority does NOT refuse:

    both sides accept writes. They diverge. When the partition heals,
    there is no correct merge - some writes must be discarded, and the
    system cannot tell you which ones mattered.

    this is why the FENCING step exists: a demoted leader must be
    prevented from writing even if it still believes it is leader.
    without fencing, a leader that was partitioned and is now paused
    (GC, VM migration) can wake up and write with stale authority.

  and why 2-node clusters cannot fail over safely: neither side can
  form a majority, so both reach the same conclusion, whichever it is.
```

| Nodes | Majority | Failures tolerated | Verdict                                                    |
|-------|----------|--------------------|------------------------------------------------------------|
| 1     | 1        | 0                  | No HA                                                      |
| 2     | 2        | **0**              | **Worse than 1** — twice the failure surface, no tolerance |
| 3     | 2        | 1                  | The usual minimum                                          |
| 4     | 3        | **1**              | Same tolerance as 3, higher cost and latency               |
| 5     | 3        | 2                  | For clusters that must survive a node *during* maintenance |

The 4-node row is the one worth remembering: adding a node to a 3-node cluster buys you *nothing* in fault tolerance and costs you a slower write path, because every commit now needs three acknowledgements instead of two. Odd numbers only.

<a id="be-g682"></a>

#### Idempotency, retries and exactly-once

This is the section that makes distributed systems survivable. Given that you cannot tell a lost request from a lost reply, retrying is your only recovery — and idempotency is what makes retrying safe.

| Operation                             | Idempotent? | Make it so by                                            |
|---------------------------------------|-------------|----------------------------------------------------------|
| `GET /orders/4471`                    | Yes         | —                                                        |
| `PUT /orders/4471` with the full body | Yes         | —                                                        |
| `DELETE /orders/4471`                 | Yes         | — deleting twice leaves it deleted                       |
| `POST /orders`                        | **No**      | A client-supplied idempotency key                        |
| “Add 100 to the balance”              | **No**      | “Set the balance to X”, or a deduplicated transaction id |
| “Send an email”                       | **No**      | A dedup table on a message id                            |
| “Set status to Shipped”               | Yes         | — state assignment is naturally idempotent               |

``` sql
-- the idempotency key, server side. The check and the work MUST be in one
-- transaction, or you have moved the race rather than removed it.
BEGIN;
  INSERT INTO idempotency_keys (key, request_hash, created_at)
  VALUES (:key, :hash, now())
  ON CONFLICT (key) DO NOTHING;

  -- 0 rows inserted = we have seen this key before.
  -- return the STORED RESPONSE, do not redo the work.
  -- and compare request_hash: same key with a DIFFERENT body is a
  -- client bug, and should be a 422 rather than a silent replay.

  INSERT INTO orders (...) VALUES (...);
  UPDATE idempotency_keys SET response = :body, status = 201 WHERE key = :key;
COMMIT;
```

``` text
  WHY "EXACTLY-ONCE DELIVERY" IS NOT ACHIEVABLE

    sender transmits. No acknowledgement arrives.
      retransmit?  -> risk delivering twice
      give up?     -> risk delivering zero times
    there is no third option, and no amount of protocol removes the choice.

  WHAT IS ACHIEVABLE

    at-least-once DELIVERY  +  idempotent PROCESSING
      = exactly-once EFFECT

    the guarantee moves from the network - where it cannot exist - to the
    consumer, where it can, because the consumer has durable state and
    can remember what it has already done.

  so when a vendor advertises exactly-once, they mean either
    transactional writes WITHIN their own system, or
    deduplication over a BOUNDED window.
  design your consumers to be idempotent regardless of the promise.
```

| Retry rule                              | Why                                                                           |
|-----------------------------------------|-------------------------------------------------------------------------------|
| Only retry idempotent operations        | Otherwise you duplicate side effects                                          |
| Exponential backoff                     | A struggling service must not be hammered                                     |
| **With jitter**                         | Without it, every client that failed together retries together                |
| A bounded attempt count                 | Infinite retries are a self-inflicted denial of service                       |
| Do not retry 4xx                        | The request is wrong; it will be wrong again                                  |
| Retry budgets, not just per-call limits | Cap retries as a fraction of total traffic, or a partial outage becomes total |
| Give up and dead-letter                 | Some failures are permanent. Surface them to a human                          |

> **\[Note\]**
>
> **The one property to design for.** If you make every write path idempotent — a key on every command, a dedup table on every consumer, state assignment rather than increment wherever possible — then almost every other distributed-systems problem becomes recoverable by retrying. Skip it, and every timeout becomes a manual investigation into whether the work happened.

### Integration & Distributed

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Integration & Distributed</p>

<p class="pd">Patterns for systems made of parts that can fail independently. These postdate the GoF catalogue entirely, and they are where most modern design difficulty actually lives.</p>

<a id="be-h770"></a>

#### What changes when the call can fail

Every pattern in this part exists because of one fact: **a remote call has three outcomes, not two**.

``` text
  in-process call            remote call

    it returns                 it returns
    it throws                  it throws
                               ...nothing comes back, and you cannot
                                  tell WHICH of these happened:

                                    the request never arrived
                                      -> the work did NOT happen
                                    it arrived, the work ran, the reply
                                      was lost
                                      -> the work DID happen
                                    it is still running
                                      -> the work MIGHT happen

  no timeout value distinguishes "slow" from "dead". That is not a
  tooling gap; it is a property of asynchronous networks.

  so retrying is the ONLY recovery available - and retrying is only safe
  if the operation is IDEMPOTENT. That single sentence generates the
  outbox, the idempotency key, at-least-once delivery, and the reason
  exactly-once delivery is not achievable.
```

| Pattern                   | Answers                                          |
|---------------------------|--------------------------------------------------|
| **Retry** + **backoff**   | The failure may be transient                     |
| **Timeout**               | How long before I stop waiting?                  |
| **Circuit breaker**       | It keeps failing — stop trying for a while       |
| **Bulkhead**              | One dependency must not consume all my capacity  |
| **Idempotent receiver**   | This may arrive twice                            |
| **Outbox**                | The message must not be lost if publishing fails |
| **Saga**                  | Several steps, no distributed transaction        |
| **Anti-corruption layer** | Their model must not leak into mine              |
| **Strangler fig**         | Replace it without stopping it                   |

###### Timeouts that compose, and the budget that must shrink

``` text
  TIMEOUTS MUST DECREASE AS YOU GO DOWN THE STACK, and almost nobody
  configures them that way.

    client        -> 30 s
      gateway     -> 25 s
        api       -> 20 s
          service -> 10 s
            db    ->  5 s

  if an inner timeout EXCEEDS an outer one, the outer gives up first
  and the inner work continues - consuming a connection, a thread and
  database resources for a response nobody will read.

  at scale this is a capacity leak: under load, abandoned work
  accumulates faster than it completes, and the system spends its
  capacity on results that are already discarded.

  and the retry interaction, which is where budgets are blown:

    3 attempts x 10 s per-attempt timeout = 30 s
    ...against a caller with a 20 s budget.

    the caller gives up during attempt 2. Attempt 3 runs anyway.

  -> configure BOTH a per-attempt timeout AND a total budget, and make
     attempts x per-attempt <= total.
```

``` csharp
builder.Services.AddHttpClient<IBillingClient, BillingClient>(c =>
{
    c.Timeout = Timeout.InfiniteTimeSpan;      // let the pipeline own timing
})
.AddStandardResilienceHandler(o =>
{
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);  // the BUDGET
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);        // per try
    o.Retry.MaxRetryAttempts = 2;                              // 3 x 3s = 9s < 10s
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.Retry.UseJitter = true;
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
});

// and propagate the CALLER's remaining budget, so an inner call cannot
// outlive the request that started it
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(remainingBudget);
await _billing.ChargeAsync(cmd, cts.Token);
```

| Timeout mistake                   | Consequence                                                       |
|-----------------------------------|-------------------------------------------------------------------|
| No timeout at all                 | `HttpClient` defaults to **100 seconds** — threads held that long |
| Inner longer than outer           | Abandoned work consuming capacity                                 |
| Retries not counted in the budget | The total exceeds what the caller will wait                       |
| The same timeout everywhere       | No layer can distinguish slow from dead                           |
| Token not propagated              | Cancellation does not reach the database or the socket            |
| Retrying a non-idempotent call    | Duplicate side effects                                            |

<a id="be-h772"></a>

#### Idempotent receiver

**Intent:** make processing a message twice have the same effect as processing it once. This is the property that makes at-least-once delivery survivable, and it is the single most valuable thing to design for in a distributed system.

``` sql
-- the check and the work must be in ONE transaction, or you have moved
-- the race rather than removed it
BEGIN;
  INSERT INTO processed_messages (message_id, processed_at)
  VALUES (:message_id, now())
  ON CONFLICT (message_id) DO NOTHING;

  -- 0 rows inserted = already handled. ROLLBACK, acknowledge, done.

  UPDATE orders SET status = 'Shipped' WHERE id = :order_id;
COMMIT;
```

| Operation                 | Idempotent? | Make it so by                               |
|---------------------------|-------------|---------------------------------------------|
| `SET status = 'Shipped'`  | **Yes**     | — assignment is naturally idempotent        |
| `balance = balance + 100` | **No**      | A deduplicated transaction id               |
| `DELETE` by id            | Yes         | —                                           |
| `INSERT`                  | No          | A natural key with `ON CONFLICT DO NOTHING` |
| Send an email             | No          | A dedup table keyed on the message id       |
| Call a payment provider   | No          | Their idempotency key header                |

``` text
  EXACTLY-ONCE, honestly

    exactly-once DELIVERY is not achievable across a network.
      the sender either retransmits (risking two) or gives up
      (risking zero). There is no third option.

    exactly-once PROCESSING is achievable:

      at-least-once delivery  +  idempotent processing
        = the same observable effect

    the guarantee moves from the network - where it cannot exist - to
    the consumer, which has durable state and can remember.

    when a vendor advertises exactly-once, they mean transactional
    writes within their own system, or deduplication over a bounded
    window. Design consumers to be idempotent regardless.
```

###### Storing the response, and the retention window

``` sql
-- the full idempotency-key table. Storing the RESPONSE is the part
-- usually omitted, and it is what makes a retry return the same answer
-- rather than a conflict.
CREATE TABLE idempotency_keys (
    key           text PRIMARY KEY,
    request_hash  text NOT NULL,          -- detect key reuse with a DIFFERENT body
    status        text NOT NULL,          -- 'in_progress' | 'completed'
    response_code int,
    response_body jsonb,
    created_at    timestamptz NOT NULL DEFAULT now(),
    completed_at  timestamptz
);
CREATE INDEX ON idempotency_keys (created_at);
```

``` text
  THE THREE CASES, and the middle one is the one people miss

  1  NEW KEY
       INSERT ... ON CONFLICT DO NOTHING succeeds.
       do the work. Store the response. Return it.

  2  KEY EXISTS, status = 'completed'
       return the STORED RESPONSE, with the stored status code.
       do NOT redo the work, and do NOT return 409 - the client is
       retrying because it never saw the first response, and it needs
       the answer, not an error.

  3  KEY EXISTS, status = 'in_progress'      <- THE MISSED CASE
       the first request is STILL RUNNING. A concurrent duplicate.
       -> return 409 Conflict with Retry-After, or block briefly.
       -> and you need a TIMEOUT: if 'in_progress' is older than N
          minutes, the original request died. Treat it as new, or the
          key is poisoned forever.

  AND THE HASH CHECK
    same key, DIFFERENT request body = a client bug. Return 422, not
    the cached response - silently returning the first result for a
    different request is a genuinely dangerous failure.
```

``` csharp
public async Task<IResult> HandleAsync(CreateOrder cmd, string key, CancellationToken ct)
{
    var hash = Hash(cmd);

    var existing = await _db.IdempotencyKeys
        .FirstOrDefaultAsync(k => k.Key == key, ct);

    if (existing is not null)
    {
        if (existing.RequestHash != hash)
            return Results.UnprocessableEntity("Key reused with a different body");

        if (existing.Status == "completed")
            return Results.Json(existing.ResponseBody,
                                statusCode: existing.ResponseCode);

        // in progress - and stale?
        if (_clock.GetUtcNow() - existing.CreatedAt > TimeSpan.FromMinutes(5))
            existing.Status = "abandoned";       // allow a fresh attempt
        else
            return Results.Conflict("Request in progress");
    }
    ...
}
```

On retention: the table must be pruned, and the window must **exceed the longest possible client retry interval plus any dead-letter reprocessing time**. Too short and a legitimate late retry is treated as new, duplicating the effect — which is precisely the failure the key existed to prevent. Thirty days is a common and defensible default.
