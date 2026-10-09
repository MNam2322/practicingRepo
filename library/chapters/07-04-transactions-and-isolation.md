---
id: "7-4"
number: "7.4"
title: "Transactions and isolation"
part: 7
part_title: "Storing data"
words: 10625
minutes: 46
---

# 7.4 Transactions and isolation

> **Quick look.** Transactions make changes all-or-nothing; isolation decides what concurrent work can see.
>
> - ACID: atomicity, consistency (declared constraints hold), isolation, durability.
> - Read committed, the usual default, still allows non-repeatable reads, phantoms and lost updates.
> - Snapshot and serializable prevent more anomalies at a higher cost; write skew is the subtle one.
> - Enable READ_COMMITTED_SNAPSHOT instead of sprinkling NOLOCK.
> - Check-then-act is a race: let a constraint or a single statement enforce the rule.
> - Default to optimistic concurrency with a version column; pessimistic locks only for hot rows, kept short.
> - Keep transactions short, retry deadlocks, and avoid distributed transactions; use an outbox to pair a change with a message.


### The big picture

A booking usually means several changes: take a seat, record the passenger, charge the fare, add loyalty points. If the system fails halfway, you want none of it, not a charged passenger without a seat. And while one booking is in progress, hundreds of others are happening at the same moment on the same flights. A transaction handles both problems: it makes a group of changes all-or-nothing, and it keeps concurrent groups from seeing or trampling each other's half-finished work.

This chapter explains what transactions promise, known as ACID, and the part that is subtle: isolation. Full isolation is expensive, so databases offer weaker levels that allow some anomalies in exchange for speed, and the defaults allow more than most developers assume. You will see a lost update happen in a real database, and how to prevent it. The chapter also covers locks and row versions, optimistic concurrency with version numbers, and how to keep a database change and a message to another system consistent.

### Before you start

**Builds on:** 7.1 Why databases exist, 1.6 Storage and files, 5.1 Why concurrency is hard, 5.2 Threads and locks, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Schema** (7.5): The structure of a database: its tables, columns, types, keys, constraints and indexes.

**The code uses:** variables and var, methods and local functions, string interpolation, null operators, tuples, using for cleanup, async and await, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What do the letters of ACID stand for?**

> Atomicity: all of a transaction's changes happen or none do. Consistency: it moves the database from one valid state to another. Isolation: concurrent transactions don't interfere. Durability: committed changes survive crashes.

**2. What is SQL Server's default isolation level, and what anomalies does it allow?**

> Read committed. You never see uncommitted data, but reading the same row twice can give different values, new matching rows can appear, and read-then-write sequences can lose updates.

**3. Two users read a seat count of 10, and each writes back 9. What is this called, and how do you prevent it?**

> A lost update. Prevent it by doing the change in one statement (`SET available = available - 1`), by locking the row when reading, or by optimistic concurrency, where the update only succeeds if the row's version hasn't changed.

**4. What is snapshot isolation?**

> Each transaction reads from a consistent snapshot of the database as it was when the transaction started, using stored row versions. Readers never block writers. It still allows an anomaly called write skew.

**5. What is optimistic concurrency?**

> Instead of locking, each row carries a version number. An update includes `WHERE version = @versionYouRead`. If someone else changed the row first, no rows are updated, and the application reloads and decides what to do.

**6. What should an application do when SQL Server reports error 1205?**

> Retry the transaction. Error 1205 means the transaction was chosen as a deadlock victim and rolled back, and a retry usually succeeds.

**7. Why is "save to the database, then publish a message" unreliable, and what fixes it?**

> A crash between the two steps loses the message, and publishing first risks a message for a change that never committed. The transactional outbox writes the message to a table in the same transaction, and a separate process publishes it afterwards.

### What a transaction promises

Why databases exist (7.1) introduced the **transaction**: a group of changes that happen together or not at all. A transaction starts, makes its changes, and then either commits them or rolls them back. The guarantees are summed up as **ACID**.

**Atomicity** means all or nothing. If any part fails, or the application asks for it, every change in the transaction is undone. A **commit** makes all the changes permanent at once. A **rollback** throws them all away.

**Consistency** means a transaction takes the database from one valid state to another. The database checks its constraints, such as keys, foreign keys and check rules, and refuses a commit that would break them. Rules that only live in application code are the application's responsibility.

**Isolation** means concurrent transactions don't interfere with each other. Ideally, each would behave as if it ran alone. This is the subtle part, covered in most of this chapter.

**Durability** means that once a commit is reported, it survives crashes and power cuts. The database guarantees it by flushing the write-ahead log to disk before replying, as Why databases exist (7.1) and Storage and files (1.6) explained.

Every single SQL statement is already a transaction on its own. That is called autocommit: an `UPDATE` that changes a thousand rows either changes all of them or none. You group several statements into one transaction explicitly, with `BEGIN TRANSACTION` and `COMMIT` in SQL, or `BeginTransaction` in .NET. EF Core's `SaveChanges` wraps all of its changes in a transaction automatically.

### The problem isolation solves

A database runs many transactions at the same time, on many threads, for the same reasons as Why concurrency is hard (5.1): waiting for disks and networks, and using many cores. Their reads and writes interleave. Perfect isolation would make every transaction behave as if it ran alone, one after another. That is expensive, because it means transactions often wait for each other. So databases offer a choice of **isolation levels**, which trade some safety for speed by allowing some of these anomalies.

A **dirty read** is reading data another transaction has written but not yet committed. If that transaction rolls back, you acted on data that never officially existed.

A **non-repeatable read** is reading the same row twice in one transaction and getting different values, because another transaction changed and committed it in between.

A **phantom read** is running the same query twice and getting a different set of rows, because another transaction inserted or deleted matching rows in between.

A lost update, from Why concurrency is hard (5.1), is two transactions each reading a value, computing a new one and writing it back, so one overwrites the other's change.

**Write skew** is subtler. Two transactions read overlapping data, each checks a rule, and each changes a different row. Each change is fine alone; together they break the rule. An airline requires at least one standby captain at each base. Two schedulers each see two on standby, and each releases a different one. Both commit, and nobody is left.

### Isolation levels

The SQL standard defines four levels, from weakest to strongest.

**Read uncommitted** allows dirty reads. In SQL Server it is what the `NOLOCK` hint does. It can also read rows twice or skip them while pages are being rearranged. It is almost never what you want.

**Read committed** prevents dirty reads: you only ever see committed data. Non-repeatable reads, phantoms and lost updates can still happen. It is the default in SQL Server, PostgreSQL and Oracle.

**Repeatable read** also guarantees that rows you have read won't change until you finish. Phantoms may still appear. It is MySQL's default.

**Serializable** guarantees the result is the same as if the transactions had run one at a time in some order. No anomalies at all, at the highest cost: more waiting, and more transactions aborted to be retried.

SQL Server and PostgreSQL add **snapshot isolation**: each transaction sees a consistent snapshot of the database as it was when the transaction started, whatever others commit meanwhile. Reads never wait for writers. If two snapshot transactions update the same row, the second to commit fails and must retry. Snapshot isolation prevents dirty reads, non-repeatable reads, phantoms and lost updates on the same row, but still allows write skew.

| Level | Dirty read | Non-repeatable read | Phantom | Write skew |
|---|---|---|---|---|
| Read uncommitted | possible | possible | possible | possible |
| Read committed | no | possible | possible | possible |
| Repeatable read | no | no | possible | possible |
| Snapshot | no | no | no | possible |
| Serializable | no | no | no | no |

Names don't always mean the same thing across databases. PostgreSQL's "repeatable read" is actually snapshot isolation, and its "serializable" detects dangerous patterns and aborts one transaction. Check the documentation of the database you use.

### Locks and row versions

Databases implement isolation in two ways, and most use both.

With locking, a transaction takes locks on the rows, or pages or tables, that it touches. A **shared lock** is taken for reading; many transactions can hold one on the same row. An **exclusive lock** is taken for writing; only one transaction can hold it, and nobody else can take any lock on that row meanwhile. Locks are held until the transaction ends. A transaction that needs a lock someone else holds waits. That waiting is **blocking**, and it is the most common cause of a database that is idle on CPU but slow for users. Two transactions each waiting for the other's lock is a deadlock, as in Threads and locks (5.2). The database detects it and aborts one of them. SQL Server reports that as error 1205, and the right response is to retry.

With **row versioning**, a writer keeps the old version of each row it changes, and readers that need the older state read that version instead of waiting. This is also called MVCC, multi-version concurrency control. Readers don't block writers, and writers don't block readers. The cost is storage for old versions and background work to clean them up: tempdb in SQL Server, and a process called vacuum in PostgreSQL.

SQL Server's default read committed uses locks, so readers wait for writers. Turning on the database option `READ_COMMITTED_SNAPSHOT` makes read committed use row versions instead. It is on by default in Azure SQL Database, and many teams turn it on elsewhere, because it removes most reader-writer blocking. PostgreSQL uses row versions throughout.

### Seeing it in two sessions

Isolation is easiest to understand by running two connections side by side. With SQL Server, for example in a Docker container, open two query windows and run these steps in order.

```sql
-- Setup, once:
CREATE TABLE dbo.Seats (Flight varchar(10) PRIMARY KEY, Available int NOT NULL);
INSERT dbo.Seats VALUES ('BA117', 10);

-- Session 1:
BEGIN TRANSACTION;
UPDATE dbo.Seats SET Available = Available - 1 WHERE Flight = 'BA117';   -- takes an exclusive lock; not committed yet

-- Session 2:
SELECT Available FROM dbo.Seats WHERE Flight = 'BA117';
-- With locking read committed, this waits: it is blocked by session 1's lock.
-- With READ_COMMITTED_SNAPSHOT ON, it returns 10 at once: the last committed version.

-- Session 1:
COMMIT;   -- session 2, if it was waiting, now returns 9
```

Running `SELECT` with `WITH (NOLOCK)` in session 2 instead would return 9 before session 1 commits: a dirty read of a change that might still be rolled back.

### Lost updates in practice

Most application code runs at read committed, and the most common real bug is the lost update: read a value, change it in C#, write it back. The program below makes one happen in SQLite, then shows two ways to prevent it.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// transactions.cs — run with: dotnet run transactions.cs
using Microsoft.Data.Sqlite;

File.Delete("tx.db");
const string Db = "Data Source=tx.db";
Run("""
    CREATE TABLE seats (flight TEXT PRIMARY KEY, available INTEGER NOT NULL, version INTEGER NOT NULL);
    INSERT INTO seats VALUES ('BA117', 10, 1);
    CREATE TABLE bookings (id INTEGER PRIMARY KEY, flight TEXT NOT NULL, passenger TEXT NOT NULL);
    CREATE TABLE outbox (id INTEGER PRIMARY KEY, message TEXT NOT NULL, sent INTEGER NOT NULL DEFAULT 0);
    """);

// 1. Lost update: each booking reads the count, waits a moment, then writes back its own new value
await Task.WhenAll(Task.Run(BookByReadThenWrite), Task.Run(BookByReadThenWrite));
Console.WriteLine($"two bookings, read then write: {Available()} seats left");   // 9: one booking was lost

// 2. Fix one: let the database do the read and the write in one statement
Run("UPDATE seats SET available = 10");
await Task.WhenAll(Task.Run(BookAtomically), Task.Run(BookAtomically));
Console.WriteLine($"two bookings, one statement:   {Available()} seats left");   // 8

// 3. Fix two: optimistic concurrency with a version number
Run("UPDATE seats SET available = 10, version = 1");
var (seatsA, versionA) = Read();                           // two users load the same row...
var (seatsB, versionB) = Read();
Console.WriteLine(TrySave(seatsA - 1, versionA) ? "user A saved" : "user A: conflict");   // user A saved
Console.WriteLine(TrySave(seatsB - 1, versionB) ? "user B saved" : "user B: conflict");   // user B: conflict
(seatsB, versionB) = Read();                               // B reloads the current row and tries again
Console.WriteLine(TrySave(seatsB - 1, versionB) ? $"user B saved on retry, {Available()} left" : "conflict again");
// user B saved on retry, 8 left

// 4. The transactional outbox: the booking and its message commit together
using (var db = Open())
using (var tx = db.BeginTransaction())
{
    Exec(db, tx, "INSERT INTO bookings (flight, passenger) VALUES ('BA117', 'Ada')");
    Exec(db, tx, "INSERT INTO outbox (message) VALUES ('BookingCreated BA117 Ada')");
    tx.Commit();                                           // both rows, or neither
}
foreach (string message in PublishPending()) Console.WriteLine($"published: {message}");   // published: BookingCreated BA117 Ada

async Task BookByReadThenWrite()
{
    using var db = Open();
    long seats = Scalar(db, "SELECT available FROM seats WHERE flight = 'BA117'");   // read
    await Task.Delay(100);                                                             // the other booking reads too
    Exec(db, null, $"UPDATE seats SET available = {seats - 1} WHERE flight = 'BA117'");   // write back a stale value
}

void BookAtomically()
{
    using var db = Open();
    Exec(db, null, "UPDATE seats SET available = available - 1 WHERE flight = 'BA117' AND available > 0");
}

(long Seats, long Version) Read()
{
    using var db = Open();
    return (Scalar(db, "SELECT available FROM seats"), Scalar(db, "SELECT version FROM seats"));
}

bool TrySave(long newSeats, long versionRead)
{
    using var db = Open();
    using var cmd = db.CreateCommand();
    cmd.CommandText = "UPDATE seats SET available = $seats, version = version + 1 WHERE flight = 'BA117' AND version = $version";
    cmd.Parameters.AddWithValue("$seats", newSeats);
    cmd.Parameters.AddWithValue("$version", versionRead);
    return cmd.ExecuteNonQuery() == 1;                     // 0 rows changed means someone else got there first
}

List<string> PublishPending()                              // in real systems, a background worker does this
{
    using var db = Open();
    var sent = new List<string>();
    using (var cmd = db.CreateCommand())
    {
        cmd.CommandText = "SELECT message FROM outbox WHERE sent = 0 ORDER BY id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) sent.Add(reader.GetString(0));   // here it would go to a message broker
    }
    Exec(db, null, "UPDATE outbox SET sent = 1 WHERE sent = 0");
    return sent;
}

long Available() { using var db = Open(); return Scalar(db, "SELECT available FROM seats"); }

SqliteConnection Open() { var db = new SqliteConnection(Db); db.Open(); return db; }

void Run(string sql) { using var db = Open(); Exec(db, null, sql); }

static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql)
{
    using var cmd = db.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static long Scalar(SqliteConnection db, string sql)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    return (long)cmd.ExecuteScalar()!;
}
```

SQLite itself allows only one writer at a time, which makes it serializable for writes. The lost update still happens, because the read and the write are two separate statements, each its own transaction. The same code loses updates in SQL Server at read committed, and for the same reason.

### Optimistic and pessimistic concurrency

There are two general strategies for keeping concurrent edits from clobbering each other.

**Pessimistic locking** assumes conflicts are likely and prevents them up front: lock the row when you read it, so nobody else can change it until you are done. In SQL Server, `SELECT ... WITH (UPDLOCK, ROWLOCK)` inside a transaction takes an update lock on the rows read. In PostgreSQL, the equivalent is `SELECT ... FOR UPDATE`. It is simple and reliable for short operations. It must never be held while waiting for a user, a network call or anything slow, because every other writer waits too.

**Optimistic concurrency** assumes conflicts are rare and detects them at the end. Each row carries a version. An update includes `WHERE version = @theVersionIRead` and increments the version. If someone else changed the row first, the update changes zero rows, and the application knows there was a conflict. It can reload and retry, merge the changes, or ask the user. SQL Server has a `rowversion` column type that changes automatically on every update, and EF Core supports concurrency tokens that add the check to every update for you, as ORMs and EF Core (7.7) shows.

Optimistic concurrency suits the common web pattern: load a record, show it to a user, let them edit it for a minute, save. No lock is held while the user thinks.

### Keeping transactions healthy

A few rules keep transactions from becoming a bottleneck.

Keep them short. Locks and row versions are held until the transaction ends. A transaction that includes a slow query, an HTTP call to another service, or waiting for user input makes everyone else wait. Do the slow work first, then open the transaction, make the changes and commit.

Touch rows in a consistent order, so two transactions don't take the same locks in opposite orders, for the lock-ordering reason in Threads and locks (5.2).

Be ready to retry. Deadlock victims, snapshot update conflicts and serialization failures are normal events under load. Wrap the transaction in a retry with backoff, as Errors and exceptions (2.8) described, and make sure the whole transaction can safely run again.

Use a **savepoint** when you need to undo only part of a transaction: `SAVE TRANSACTION name` in SQL Server, `SAVEPOINT name` in PostgreSQL and SQLite. Rolling back to it undoes everything since, while keeping the transaction open.

### Transactions across systems

A database transaction covers one database. Real work often touches two systems: save a booking, then send a "booking created" message to a queue that the email and loyalty services read.

Doing them one after the other is unreliable either way round. Save first and then crash before publishing, and the message is lost. Publish first and the save fails, and other services act on a booking that doesn't exist.

A **distributed transaction** coordinates a commit across several systems with a protocol called two-phase commit. It exists, for example through .NET's `TransactionScope` with the Microsoft Distributed Transaction Coordinator, but it is slow, fragile and not supported by most cloud services and message brokers. Modern systems avoid it.

The common alternative is the **transactional outbox**. In the same database transaction as the business change, insert the message into an outbox table. Either both commit or neither does. A separate background process reads unsent messages from the outbox, publishes them to the broker and marks them sent. If it crashes after publishing but before marking, it publishes the message again, so consumers must handle duplicates safely: the idempotency idea from Errors and exceptions (2.8). The last part of the example is a minimal outbox. Messaging and events (12.3) comes back to it.

### In short

A transaction's changes commit together or roll back together, and ACID names its guarantees: atomicity, consistency, isolation and durability. Isolation levels trade safety for speed. Read committed, the usual default, stops dirty reads but still allows non-repeatable reads, phantoms and lost updates; snapshot and serializable prevent more at a higher cost. Databases isolate with locks, which make transactions wait, and row versions, which let readers see older copies. Prevent lost updates with single-statement changes, row locks or optimistic version checks. Keep transactions short, retry deadlocks, and use an outbox to pair a database change with a message.

### What goes wrong

**Read, compute, write back.** Two users edit the same record and one change silently disappears. Update in one statement, lock the row, or use a version check.

**Assuming serializable behaviour.** Code written as if transactions ran one at a time breaks under read committed with write skew and phantoms. Know the isolation level, and use constraints or stronger levels for rules that span rows.

**`NOLOCK` everywhere.** Queries read uncommitted, duplicated or missing rows. Turn on read committed snapshot instead of hinting every query.

**Long transactions.** A transaction left open during an HTTP call or a report blocks hundreds of other requests. Do slow work outside the transaction.

**No retry on deadlocks.** Error 1205 reaches users as a failure, although a retry would have succeeded. Retry deadlock victims and serialization failures automatically.

**Save, then publish.** A crash between the two leaves systems disagreeing. Use a transactional outbox, and make consumers idempotent.

**Distributed transactions by accident.** Opening two different connections inside a `TransactionScope` can promote it to a distributed transaction, which fails in most cloud environments. Use one connection, or the outbox.

### Cost model

A commit costs at least one flush of the log to disk: from a fraction of a millisecond to several, depending on the storage. Batching many changes into one transaction pays that once instead of many times. Taking an uncontended lock costs microseconds; waiting for a contended one costs however long the other transaction runs, which is why transaction length matters more than anything else here. Row versioning adds storage and cleanup work for old versions. Serializable and snapshot levels cause more retries under contention, each of which repeats the whole transaction.

### In other languages (optional)

**EF Core.** `SaveChanges` runs in a transaction. `Database.BeginTransaction` groups several calls. Concurrency tokens, such as a `[Timestamp]` property mapped to `rowversion`, throw `DbUpdateConcurrencyException` on conflicts. Execution strategies can retry transient failures, including deadlocks.

**PostgreSQL.** Uses row versions throughout. Serializable isolation uses a technique called serializable snapshot isolation, which aborts one transaction when it detects a dangerous pattern, with error code 40001, and the application should retry.

**JavaScript, TypeScript and Python.** Drivers and ORMs expose the same `BEGIN`, `COMMIT` and `ROLLBACK`, often through a callback or context manager that commits on success and rolls back on an exception.

**Document and key-value stores.** Many offer atomic operations on one document or key, and some offer multi-document transactions with limits, as Non-relational stores (7.6) describes.

### Key terms

- **ACID**: Atomicity, consistency, isolation and durability: the guarantees of a database transaction.
- **Atomicity**: All of a transaction's changes happen, or none of them do.
- **Consistency**: A transaction moves the database from one valid state to another, respecting its constraints.
- **Isolation**: Concurrent transactions don't see or disturb each other's unfinished work.
- **Commit**: Making all of a transaction's changes permanent at once.
- **Rollback**: Undoing all of a transaction's changes.
- **Isolation level**: How strictly a database isolates concurrent transactions, trading safety for speed.
- **Dirty read**: Reading another transaction's changes before they are committed.
- **Non-repeatable read**: Reading the same row twice in one transaction and getting different values.
- **Phantom read**: Running the same query twice in one transaction and getting a different set of rows.
- **Write skew**: Two transactions each changing different rows based on overlapping reads, together breaking a rule.
- **Read committed**: The isolation level that shows only committed data. The default in SQL Server and PostgreSQL.
- **Serializable**: The isolation level whose results match running the transactions one at a time.
- **Snapshot isolation**: Each transaction reading a consistent snapshot of the database from when it started.
- **Shared lock**: A lock for reading that several transactions can hold on the same row at once.
- **Exclusive lock**: A lock for writing that only one transaction can hold, keeping all others out.
- **Blocking**: A transaction waiting for a lock held by another.
- **Row versioning**: Keeping old versions of changed rows so readers can see them without waiting. Also called MVCC.
- **Pessimistic locking**: Locking rows when reading them, so nobody else can change them until you finish.
- **Optimistic concurrency**: Detecting conflicting changes at save time with a version check, instead of locking.
- **Savepoint**: A named point inside a transaction that you can roll back to without ending the transaction.
- **Distributed transaction**: A transaction that commits across several systems at once, using two-phase commit.
- **Transactional outbox**: Writing outgoing messages to a table in the same transaction as the change, and publishing them afterwards.

### Where this leads

Constraints that enforce consistency are designed in Schema design and migrations (7.5). What non-relational stores offer instead of full transactions is in Non-relational stores (7.6). Concurrency tokens and transactions in EF Core are in ORMs and EF Core (7.7). The outbox, consumers and duplicate messages come back in Messaging and events (12.3), and consistency across machines in Distributed systems basics (12.2).

### Check yourself

1. Change `BookByReadThenWrite` in `transactions.cs` to run five bookings at once. Predict the final count, then run it.
2. Wrap the read and the write in `BookByReadThenWrite` in one SQLite transaction started with `BEGIN IMMEDIATE`, and explain what happens now.
3. Run the two-session SQL Server script with `READ_COMMITTED_SNAPSHOT` off and then on. Note which step waits in each case.
4. Write the standby captain scenario as two SQL transactions and show write skew at snapshot isolation. Then prevent it with a constraint or a lock.
5. Find a place in an application you know that saves data and then publishes a message or sends an email. Decide what happens if the process crashes between the two.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Two agents overwriting each other's edits

Two support agents edit the same booking. The second save silently discards the first agent's changes.

*Optimistic concurrency in EF Core*

```csharp
public class Booking { public Guid Id { get; set; } [Timestamp] public byte[] RowVersion { get; set; } = []; }

// the client sends back the RowVersion it loaded (e.g. in an If-Match header)
db.Entry(booking).Property(b => b.RowVersion).OriginalValue = Convert.FromBase64String(ifMatch);
try { await db.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException)
{
    return Results.Conflict(new { error = "changed_by_someone_else" });   // reload and show the current state
}
```

> **Lesson:** Without a version check, the last writer wins silently. The client's version must reach the UPDATE.

#### Example 2: A deadlock every month-end

A closing job updates Orders then Invoices; the billing job updates Invoices then Orders. SQL Server kills one with error 1205.

*Same order everywhere, short transactions, retry*

```csharp
var strategy = db.Database.CreateExecutionStrategy();   // EnableRetryOnFailure retries deadlocks (1205)
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await UpdateOrdersAsync(ct);      // always Orders first ...
    await UpdateInvoicesAsync(ct);    // ... then Invoices, in every job
    await tx.CommitAsync(ct);
});
```

> **Lesson:** Access tables in one consistent order, keep transactions short, and retry deadlock victims.

### SQL

<p class="src">From <em>.NET Backend Deep Dive</em> · SQL</p>

<a id="dn-18"></a>

#### Isolation levels and locking

| Level | Dirty read | Repeatable read | Phantom | Notes |
|---|---|---|---|---|
| Read Uncommitted (`NOLOCK`) | Yes | — | — | Reads uncommitted data; can double-read or skip rows |
| Read Committed | No | No | Yes | SQL Server default |
| Repeatable Read | No | Yes | Yes | Holds shared locks |
| Serializable | No | Yes | No | Range locks; deadlock-prone |
| Snapshot (RCSI) | No | Yes | — | Version store; readers don't block writers |

> Practical advice: enable **READ_COMMITTED_SNAPSHOT** on OLTP databases so readers don't block writers, instead of sprinkling `WITH (NOLOCK)` everywhere.

<a id="dn-19"></a>

#### Deadlocks and concurrency

```sql
-- Deadlock avoidance: always access tables in the SAME ORDER in every transaction.
-- Keep transactions SHORT. Never call an HTTP API inside a transaction.
```

Optimistic concurrency in EF:

```csharp
public class Order
{
    [Timestamp] public byte[] RowVersion { get; set; } = default!;
}
// SaveChanges adds WHERE RowVersion = @old; if zero rows are affected
// -> DbUpdateConcurrencyException -> reload and retry, or tell the user
```

### Consistency & Race Conditions

<p class="src">From <em>.NET Backend Deep Dive</em> · Consistency & Race Conditions</p>

<a id="dn-174"></a>

#### Check-then-act is a bug

```csharp
// RACE: two concurrent requests both pass the check, both insert
if (!await db.Users.AnyAsync(u => u.Email == email))
    db.Users.Add(new User { Email = email });
await db.SaveChangesAsync();
```

Any read followed by a conditional write is a race unless the database enforces the rule. **The constraint is the correctness mechanism; the check is only a nicety for a better error message.**

```csharp
// Let the database decide, then translate the violation
try
{
    db.Users.Add(new User { Email = email });
    await db.SaveChangesAsync();
}
catch (DbUpdateException e) when (e.IsUniqueViolation())
{
    return Conflict("Email already registered");
}
```

The same shape appears everywhere: inventory checks ("2 left" → both customers buy), balance checks ("sufficient funds" → double spend), and slot booking. The fix is always to push the decision into a single atomic operation:

```sql
-- Atomic decrement that cannot go negative
UPDATE Inventory SET Qty = Qty - @n
WHERE ProductId = @id AND Qty >= @n;
-- @@ROWCOUNT = 0 means insufficient stock. No read involved.
```

<a id="dn-175"></a>

#### Optimistic vs pessimistic

| | Optimistic (`RowVersion`) | Pessimistic (`UPDLOCK`, `SELECT FOR UPDATE`) |
|---|---|---|
| Cost | None until a conflict occurs | A lock held for the whole transaction |
| Best when | Conflicts are rare | Conflicts are common, or retry is expensive |
| Failure mode | `DbUpdateConcurrencyException` → reload and retry, or 409 | Blocking, and deadlocks if lock order varies |

Default to optimistic. Reach for pessimistic only for genuinely contended rows (a shared counter, a seat map), and then keep the transaction extremely short.

<a id="dn-176"></a>

#### Idempotent writes

```sql
-- Upsert. Note MERGE has documented concurrency issues on SQL Server;
-- prefer an explicit UPDATE-then-INSERT with HOLDLOCK, or a unique index + catch.
BEGIN TRAN
UPDATE Settings WITH (UPDLOCK, HOLDLOCK)
  SET Value = @v WHERE [Key] = @k;
IF @@ROWCOUNT = 0
  INSERT INTO Settings ([Key], Value) VALUES (@k, @v);
COMMIT
```

Postgres makes this pleasant: `INSERT ... ON CONFLICT (key) DO UPDATE SET ...`.

<a id="dn-177"></a>

#### Eventual consistency in practice

Once you introduce an outbox, a cache, a read replica, or a search index, your reads can lag your writes. The three failures users actually notice:

- **Read-your-writes** — a user saves and immediately sees the old value. Fix by reading from the primary for that user for a short window, or by returning the updated resource from the write and having the client render it.
- **Monotonic reads** — a refresh shows *older* data than the previous load, because two replicas were at different points. Fix with session stickiness to a replica.
- **Causal ordering** — "comment appears before the post it belongs to." Fix by propagating a version/sequence and holding the dependent read.

**CAP as it actually applies:** partitions happen whether you plan for them or not, so the real decision is what to do *during* one — refuse writes (consistency) or accept them and reconcile (availability). Most business systems quietly choose availability and then discover they never defined the reconciliation rules.

The practical guidance: **be explicit about which reads are allowed to be stale, and for how long.** A product listing can lag 60 seconds; an account balance after a transfer cannot.

---

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a100"></a>

#### Transactions and isolation levels

A transaction gives you atomicity across statements. The isolation level decides how much other people's in-flight work you can see, trading correctness against concurrency.

| Level            | Prevents                    | Still possible                               |
|------------------|-----------------------------|----------------------------------------------|
| Read Uncommitted | Nothing                     | Dirty reads — avoid                          |
| Read Committed   | Dirty reads                 | Non-repeatable reads, phantoms               |
| Repeatable Read  | \+ non-repeatable reads     | Phantom rows                                 |
| Serializable     | Everything                  | Blocking and deadlocks                       |
| Snapshot / RCSI  | Readers never block writers | Update conflicts; needs tempdb/version store |

``` csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
try
{
    await db.SaveChangesAsync(ct);
    await _outbox.EnqueueAsync(evt, ct);     // same transaction = atomic
    await tx.CommitAsync(ct);
}
catch { await tx.RollbackAsync(ct); throw; }
```

> **\[Trap\]**
>
> **Common trap.** Never hold a transaction open across an HTTP call to another service. You are holding database locks for the duration of someone else's latency and outage. Use the outbox pattern instead.

###### Worked example

The anomalies are abstract until you see the interleaving. Two concurrent transfers against the same account:

``` text
  LOST UPDATE  (read committed, read-modify-write in application code)

  T1: SELECT balance FROM acct WHERE id=1     -> 100
  T2: SELECT balance FROM acct WHERE id=1     -> 100
  T1: UPDATE acct SET balance = 100 - 30      -> 70
  T2: UPDATE acct SET balance = 100 - 50      -> 50      <- T1's withdrawal vanished

  fixes:  UPDATE acct SET balance = balance - 30   (atomic, in the database)
          or a rowversion concurrency token
          or SELECT ... FOR UPDATE / UPDLOCK
```

``` text
  PHANTOM READ  (repeatable read is not enough)

  T1: SELECT SUM(amount) FROM lines WHERE order_id=7   -> 100     (checking a credit limit)
  T2: INSERT INTO lines (order_id, amount) VALUES (7, 500); COMMIT
  T1: SELECT SUM(amount) FROM lines WHERE order_id=7   -> 600     <- a new row appeared
  T1: proceeds having validated against 100

  fixes:  serializable isolation (range locks)
          or make the aggregate a column on the parent, updated in the same transaction
          or a check constraint the database enforces
```

``` csharp
// the atomic version - no read-modify-write in application code at all
var affected = await db.Accounts
    .Where(a => a.Id == id && a.Balance >= amount)
    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount), ct);

if (affected == 0)
    return Result.Fail("Insufficient funds or account not found.");
// one statement, no race, and the guard is part of the same atomic operation
```

###### Deadlocks: why they happen and how to stop them

``` text
  T1: UPDATE orders   SET ... WHERE id=1      (holds lock on orders/1)
  T2: UPDATE invoices SET ... WHERE id=9      (holds lock on invoices/9)
  T1: UPDATE invoices SET ... WHERE id=9      (waits for T2)
  T2: UPDATE orders   SET ... WHERE id=1      (waits for T1)   -> DEADLOCK

  the engine picks a victim, rolls it back, and returns error 1205 (SQL Server)
  or 40P01 (PostgreSQL) to that caller.
```

1.  **Always acquire locks in the same order** across the whole codebase — e.g. always orders before invoices. This single convention eliminates most deadlocks.
2.  **Keep transactions short.** No HTTP calls, no file I/O, no user interaction inside one. Do the slow work first, then open the transaction.
3.  **Touch fewer rows.** A missing index causes a scan, a scan takes range locks, and range locks collide with everything.
4.  **Retry the victim.** Deadlocks are transient by definition; `EnableRetryOnFailure` includes deadlock error codes.
5.  **Consider snapshot isolation / RCSI** so readers stop blocking writers entirely — often the single most effective change.

``` csharp
// retryable unit of work that survives deadlocks and transient failures
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    await DoWorkAsync(ct);            // must be idempotent - it may run more than once
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```

<a id="be-a102"></a>

#### Concurrency control: optimistic and pessimistic

Two users open the same record and both press save. Without a strategy, the second write silently destroys the first — the lost update problem.

``` csharp
public class Product
{
    public int Id { get; set; }
    public decimal Price { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];   // SQL Server rowversion
}

// EF adds "WHERE Id = @id AND RowVersion = @original" to the UPDATE
try { await db.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException ex)
{
    var entry   = ex.Entries.Single();
    var current = await entry.GetDatabaseValuesAsync(ct);      // someone else won
    // choose: reload and retry, merge, or return 409 to the user
}
```

**Optimistic (default)**

- No locks held; scales well
- Detects conflict at write time
- Caller must handle the retry
- Right for web apps and APIs

**Pessimistic**

- `SELECT ... FOR UPDATE` / `UPDLOCK`
- Blocks others until you commit
- Risk of deadlock and timeouts
- Right for short, contended, must-not-fail work

In distributed systems you will also need a distributed lock — typically Redis `SET key value NX PX 30000` with a unique token, released only by the owner, and with the understanding that it is a best-effort mutex, not a correctness guarantee.

###### Making it work in EF Core, and what happens on conflict

``` csharp
// 1. declare the concurrency token
public class Product
{
    public long Id { get; set; }
    public decimal Price { get; set; }

    [Timestamp]                      // SQL Server: rowversion, maintained by the DB
    public byte[]? RowVersion { get; set; }
}

// PostgreSQL: use the system xmin column - no schema change needed
modelBuilder.Entity<Product>().UseXminAsConcurrencyToken();

// or a plain int you maintain, which works everywhere
modelBuilder.Entity<Product>().Property(p => p.Version).IsConcurrencyToken();
```

``` text
  what EF Core actually generates

  WITHOUT a concurrency token
    UPDATE products SET price = 99 WHERE id = 42;
      -> 1 row affected. Whoever writes last wins. The other update is GONE,
         silently. This is the LOST UPDATE anomaly.

  WITH a concurrency token
    UPDATE products SET price = 99, row_version = <new>
    WHERE id = 42 AND row_version = <what I read>;
      -> 0 rows affected  =  someone else changed it since I read.
         EF Core sees 0 and throws DbUpdateConcurrencyException.

  the check and the write are ONE statement, so there is no window
  between them. That atomicity is the entire mechanism.
```

``` csharp
// handling the conflict - you must DECIDE, and the right answer is domain-specific
try
{
    await db.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException ex)
{
    foreach (var entry in ex.Entries)
    {
        var current = entry.CurrentValues;                    // what I want to write
        var database = await entry.GetDatabaseValuesAsync(ct); // what is there now

        if (database is null) throw new ConflictException("Deleted by another user");

        // three strategies:
        // (a) CLIENT WINS - overwrite, keeping my values
        entry.OriginalValues.SetValues(database);

        // (b) STORE WINS - discard my change, reload
        // entry.CurrentValues.SetValues(database);

        // (c) MERGE - field by field. The only honest option for a user-facing
        //     form, and it means showing the user both versions.
    }
    await db.SaveChangesAsync(ct);   // retry once; do NOT loop indefinitely
}
```

| Situation                           | Approach                                                    |
|-------------------------------------|-------------------------------------------------------------|
| A user edits a form over minutes    | Optimistic — never hold a lock across think-time            |
| Decrementing stock at checkout      | **Atomic conditional update** — no read-modify-write at all |
| Allocating a unique sequence        | A database sequence, or pessimistic lock                    |
| A short, highly contended counter   | Atomic increment via `ExecuteUpdate`, or a sharded counter  |
| Background workers on distinct rows | `FOR UPDATE SKIP LOCKED` via raw SQL                        |

``` csharp
// the best option where it applies: skip the read entirely.
// EF Core 7+ ExecuteUpdate translates to ONE atomic UPDATE, no tracking.
var affected = await db.Products
    .Where(p => p.Id == id && p.Stock >= qty)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - qty), ct);

if (affected == 0) return Error.InsufficientStock();
// no concurrency token needed, no exception handling, no retry.
// the guard is part of the same statement, so two callers cannot both win.
```

### Transactions & Concurrency

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Transactions & Concurrency</p>

<p class="pd">What happens when many sessions touch the same data at once. ACID, the anomalies, isolation levels, locking and MVCC — the area where bugs are hardest to reproduce and most expensive to get wrong.</p>

<a id="be-d443"></a>

#### ACID, precisely

| Property        | Guarantees                                                   | Mechanism                                  | What breaks without it             |
|-----------------|--------------------------------------------------------------|--------------------------------------------|------------------------------------|
| **Atomicity**   | All of a transaction or none of it                           | Undo log / rollback segments               | Money debited and never credited   |
| **Consistency** | Constraints hold at commit                                   | Constraint checking inside the transaction | Orphan rows, negative stock        |
| **Isolation**   | Concurrent transactions do not see each other's partial work | Locking or MVCC                            | Reads of half-finished state       |
| **Durability**  | A commit survives a crash                                    | Write-ahead log + fsync                    | Confirmed orders vanish on restart |

The *C* is the one most often misdescribed. It does not mean “the data makes business sense” — it means declared constraints are not violated. A transaction that satisfies every constraint and still records nonsense is perfectly ACID. Business correctness comes from the constraints you declare and the logic you write.

``` sql
BEGIN;
  UPDATE accounts SET balance = balance - 100 WHERE id = 1;
  UPDATE accounts SET balance = balance + 100 WHERE id = 2;
COMMIT;
-- atomicity: a crash between the two statements leaves NEITHER applied.
-- consistency: a CHECK (balance >= 0) is enforced, so an overdraft is rejected.
-- isolation: no other session sees the money existing in neither account.
-- durability: after COMMIT returns, a power cut cannot undo it.
```

##### Isolation is the only one you tune

Atomicity and durability are essentially binary — you have them or you have a broken database. Isolation is a dial, because full isolation costs concurrency. Every isolation level below serialisable is a deliberate trade of correctness for throughput, and knowing exactly what you have traded away is the point of the next few sections.

<a id="be-d444"></a>

#### The anomalies, as interleavings

Isolation levels are defined by which anomalies they prevent. The definitions are meaningless until you see the interleaving, so here is each one.

``` text
  DIRTY READ  (prevented at read committed and above)

  T1: UPDATE accounts SET balance = 500 WHERE id = 1
  T2: SELECT balance FROM accounts WHERE id = 1     -> 500     <- uncommitted!
  T1: ROLLBACK
      T2 acted on a value that never existed.
```

``` text
  NON-REPEATABLE READ  (prevented at repeatable read and above)

  T1: SELECT balance FROM accounts WHERE id = 1     -> 100
  T2: UPDATE accounts SET balance = 500 WHERE id = 1; COMMIT
  T1: SELECT balance FROM accounts WHERE id = 1     -> 500     <- changed mid-transaction
      T1's two reads disagree, so any calculation spanning them is inconsistent.
```

``` text
  PHANTOM READ  (prevented at serializable; also by repeatable read under MVCC)

  T1: SELECT count(*) FROM orders WHERE customer_id = 7   -> 3
  T2: INSERT INTO orders (customer_id) VALUES (7); COMMIT
  T1: SELECT count(*) FROM orders WHERE customer_id = 7   -> 4     <- a NEW ROW appeared
      T1 validated a credit limit against 3 orders and there are now 4.
```

``` text
  LOST UPDATE  (NOT prevented by any isolation level below repeatable read;
                and not by read committed at all)

  T1: SELECT balance FROM accounts WHERE id = 1   -> 100
  T2: SELECT balance FROM accounts WHERE id = 1   -> 100
  T1: UPDATE accounts SET balance = 100 - 30      -> 70   COMMIT
  T2: UPDATE accounts SET balance = 100 - 50      -> 50   COMMIT
      T1's withdrawal is GONE. No error was raised anywhere.

  fixes:  UPDATE accounts SET balance = balance - 30   (atomic, in the database)
          or a version/rowversion concurrency token
          or SELECT ... FOR UPDATE to lock the row while you decide
```

``` text
  WRITE SKEW  (prevented ONLY at serializable)

  rule: at least one doctor must always be on call.
  currently: Alice and Bob are both on call.

  T1: SELECT count(*) FROM oncall WHERE on_call    -> 2, so it is safe for Alice to leave
  T2: SELECT count(*) FROM oncall WHERE on_call    -> 2, so it is safe for Bob to leave
  T1: UPDATE oncall SET on_call = false WHERE name = 'Alice'; COMMIT
  T2: UPDATE oncall SET on_call = false WHERE name = 'Bob';   COMMIT

  nobody is on call. Neither transaction wrote a row the other read,
  so snapshot isolation permits it. This is the anomaly that catches people out
  who believe "snapshot isolation" means "serializable".
```

> **\[Note\]**
>
> **Write skew is the important one.** It is the anomaly most likely to exist in a real system and least likely to be noticed, because it needs no lock conflict and produces no error. Any invariant of the form “*at least / at most N rows satisfying P*” is vulnerable, and the answers are serialisable isolation, an explicit lock on a shared row, or a database constraint that makes the state impossible.

<a id="be-d445"></a>

#### Isolation levels

| Level              | Dirty read | Non-repeatable | Phantom    | Lost update | Write skew   |
|--------------------|------------|----------------|------------|-------------|--------------|
| Read uncommitted   | Possible   | Possible       | Possible   | Possible    | Possible     |
| **Read committed** | No         | Possible       | Possible   | Possible    | Possible     |
| Repeatable read    | No         | No             | Possible\* | No          | Possible     |
| Snapshot           | No         | No             | No         | No\*        | **Possible** |
| **Serializable**   | No         | No             | No         | No          | No           |

\*Asterisks matter. The SQL standard defines levels by anomalies, but engines implement them differently, so the same level name behaves differently across products. PostgreSQL's repeatable read is snapshot isolation and does prevent phantoms; SQL Server's is lock-based and does not. This is a genuine portability hazard.

| Engine         | Default level            | Notes                                                                                                        |
|----------------|--------------------------|--------------------------------------------------------------------------------------------------------------|
| PostgreSQL     | Read committed           | MVCC throughout; repeatable read = snapshot; serialisable = SSI, which detects conflicts rather than locking |
| SQL Server     | Read committed (locking) | Optional `READ_COMMITTED_SNAPSHOT` makes it MVCC-like; `SNAPSHOT` is separate                                |
| Oracle         | Read committed           | MVCC; “serialisable” is really snapshot isolation                                                            |
| MySQL / InnoDB | Repeatable read          | Uses gap locks to prevent phantoms — a common source of unexpected deadlocks                                 |

``` sql
BEGIN ISOLATION LEVEL SERIALIZABLE;                       -- PostgreSQL
SET TRANSACTION ISOLATION LEVEL SNAPSHOT; BEGIN TRAN;     -- SQL Server
ALTER DATABASE db SET READ_COMMITTED_SNAPSHOT ON;         -- SQL Server: readers stop blocking
```

###### Choosing a level deliberately

| Requirement                                       | Level                             | Reasoning                                                    |
|---------------------------------------------------|-----------------------------------|--------------------------------------------------------------|
| Ordinary CRUD                                     | Read committed                    | The default; combine with optimistic concurrency for updates |
| A report that must be internally consistent       | Repeatable read / snapshot        | One stable snapshot for every query in the transaction       |
| An invariant across rows (“at least one on call”) | Serializable                      | The only level that prevents write skew                      |
| Financial ledger entries                          | Serializable, or explicit locking | Correctness dominates throughput                             |
| Analytics on a replica                            | Read committed or snapshot        | Staleness is already accepted                                |
| Anything                                          | Never read uncommitted            | Dirty reads buy little and can produce impossible values     |

PostgreSQL's serialisable is worth understanding because it is not lock-based. Serialisable Snapshot Isolation tracks read/write dependencies and *aborts* a transaction that would create a cycle, with error `40001`. That means it barely blocks — but your application must be prepared to retry, and a transaction that is not retryable cannot use it.

``` sql
-- serialisable requires a retry loop. This is not optional.
for attempt in 1..3:
    try:
        BEGIN ISOLATION LEVEL SERIALIZABLE;
        ... the whole unit of work ...
        COMMIT;
        break
    except SerializationFailure:      -- SQLSTATE 40001
        ROLLBACK; sleep(random_jitter); continue
-- and the work inside must be idempotent, because it may run more than once.
```

<a id="be-d446"></a>

#### Locking: modes, granularity, escalation

A lock is a reservation on a resource, held until the transaction ends. Two things define a locking system: which modes conflict, and at what granularity locks are taken.

``` text
  compatibility matrix (simplified)

              held: S      X      U      IS     IX
  requested S       OK     no     OK     OK     no
            X       no     no     no     no     no
            U       OK     no     no     OK     no

  S   shared        readers; many at once
  X   exclusive     writers; one, and no readers
  U   update        "I intend to upgrade to X" - prevents conversion deadlocks
  IS/IX  intent     placed on the TABLE to signal row-level locks below,
                    so a table-level request can conflict without scanning every row
```

| Granularity | Concurrency | Overhead                   | Used when                         |
|-------------|-------------|----------------------------|-----------------------------------|
| Row         | Highest     | One lock structure per row | Normal OLTP                       |
| Page        | Medium      | Lower                      | Some engines, some operations     |
| Table       | Lowest      | Minimal                    | DDL, or after escalation          |
| Key range   | Medium      | Higher                     | Serialisable, to prevent phantoms |

##### Lock escalation

Locks cost memory. SQL Server escalates to a table lock at roughly 5,000 row locks on one object, trading concurrency for memory — which is why a large `UPDATE` can suddenly block every other session on that table. PostgreSQL does not escalate; it stores row-lock information in the tuple header itself, so there is no per-row memory cost and no escalation threshold.

``` sql
-- explicit locking, for when you must read-then-write safely
SELECT balance FROM accounts WHERE id = 1 FOR UPDATE;        -- X lock; others wait
SELECT ... FOR NO KEY UPDATE;      -- weaker: allows referencing FKs
SELECT ... FOR SHARE;              -- S lock: nobody may modify while I read
SELECT ... FOR UPDATE NOWAIT;      -- fail immediately rather than wait
SELECT ... FOR UPDATE SKIP LOCKED; -- skip locked rows - the queue-worker pattern

-- SQL Server
SELECT ... FROM accounts WITH (UPDLOCK, HOLDLOCK) WHERE id = 1;
```

###### SKIP LOCKED: the correct way to build a queue on a table

``` sql
-- N workers polling the same table without conflicting or duplicating work
WITH claimed AS (
    SELECT id FROM jobs
    WHERE  status = 'pending'
    ORDER  BY priority DESC, created_at
    LIMIT  10
    FOR UPDATE SKIP LOCKED         -- <- the whole trick
)
UPDATE jobs j SET status = 'running', claimed_at = now(), claimed_by = :worker
FROM   claimed c WHERE j.id = c.id
RETURNING j.*;

-- without SKIP LOCKED every worker queues behind the first one on the same rows,
-- and the "queue" processes strictly serially no matter how many workers you run.
-- with it, each worker takes a different batch, lock-free in practice.

-- pair it with a partial index so the poll stays cheap forever
CREATE INDEX ix_jobs_pending ON jobs (priority DESC, created_at)
    WHERE status = 'pending';
```

This pattern is worth knowing because it removes the most common reason teams introduce a message broker. For moderate throughput — thousands of jobs per minute — a table with `SKIP LOCKED` and a partial index is transactional with your data, needs no extra infrastructure, and gives you the outbox pattern for free.

> **\[Trap\]**
>
> **Common trap.** Do not use `SKIP LOCKED` for anything where ordering must be strict. It deliberately hands out rows out of order when earlier ones are locked. It is a work-distribution mechanism, not a FIFO queue.

<a id="be-d447"></a>

#### MVCC: how readers stop blocking writers

Multi-version concurrency control is the single most important idea in modern database concurrency. Instead of locking a row to read it, keep multiple versions and let each transaction see the version that was current when it started.

``` text
  each row version carries transaction ids

    xmin = the transaction that CREATED this version
    xmax = the transaction that DELETED or SUPERSEDED it (0 if still live)

  UPDATE does not overwrite. It writes a NEW version and marks the old one dead:

    before   (xmin=100, xmax=0)    balance = 100
    T205 updates ->
             (xmin=100, xmax=205)  balance = 100     <- dead to new transactions
             (xmin=205, xmax=0)    balance =  70     <- the live version

  a SNAPSHOT is "which transaction ids were committed when I began".
  each reader walks the version chain and picks the version visible to its snapshot.

  -> readers never block writers. Writers never block readers.
     Only writer-writer conflicts on the same row require waiting.
```

| Consequence                              | Detail                                                                                 |
|------------------------------------------|----------------------------------------------------------------------------------------|
| Readers never block                      | The main benefit — a long report does not stall the write workload                     |
| Old versions accumulate                  | They must be cleaned up: `VACUUM`, or a rollback segment                               |
| A table can be larger than its live data | Bloat, if cleanup falls behind                                                         |
| `count(*)` is not free                   | Visibility must be checked per row; there is no single stored count                    |
| Long transactions are expensive          | They hold back cleanup of every version newer than their snapshot                      |
| Write skew becomes possible              | Two transactions can each read a consistent snapshot and write conflicting conclusions |

``` sql
-- PostgreSQL: bloat and the oldest transaction holding cleanup back
SELECT relname,
       n_live_tup, n_dead_tup,
       round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 1) AS dead_pct,
       last_autovacuum
FROM   pg_stat_user_tables ORDER BY n_dead_tup DESC LIMIT 20;

-- the single most useful query for MVCC health
SELECT pid, now() - xact_start AS duration, state, left(query, 80)
FROM   pg_stat_activity
WHERE  xact_start IS NOT NULL AND now() - xact_start > interval '5 minutes'
ORDER  BY xact_start;
```

###### Why long transactions are so damaging

``` text
  a reporting session opens a transaction at 09:00 and holds it until 17:00

  09:00  snapshot taken. Everything committed before now is visible.
  09:00-17:00
         every UPDATE and DELETE anywhere in the database creates a dead version
         that MIGHT still be needed by that snapshot.
         VACUUM cannot remove ANY of them.

  results
    tables and indexes bloat, sometimes several times their live size
    index-only scans stop working (the visibility map cannot advance)
    query plans degrade as statistics stop reflecting reality
    on PostgreSQL, transaction id wraparound protection may eventually
      force an emergency shutdown

  and none of this is caused by the transaction doing anything expensive.
  merely being OPEN is enough. An idle-in-transaction connection is the worst case:
  it does nothing at all and blocks all cleanup.
```

``` text
-- defend against it
idle_in_transaction_session_timeout = '5min'      -- kill idle-in-transaction sessions
statement_timeout = '30s'                         -- per statement, set per role or session
```

- **Open the transaction as late as possible** and commit as early as possible.
- **Never hold a transaction across a network call** to another service, or across user interaction.
- **Never leave a transaction open while the application does CPU work** — read, commit, then compute.
- **Autocommit for reads** unless the read genuinely needs a consistent multi-statement snapshot.
- **Monitor `idle in transaction` as a first-class metric.** It is one of the highest-signal database health indicators there is.

<a id="be-d448"></a>

#### Deadlocks

Two transactions each holding a lock the other needs. Neither can proceed, so the engine detects the cycle, chooses a victim, and aborts it.

``` text
  T1: UPDATE orders   SET ... WHERE id = 1     holds X on orders/1
  T2: UPDATE invoices SET ... WHERE id = 9     holds X on invoices/9
  T1: UPDATE invoices SET ... WHERE id = 9     waits for T2
  T2: UPDATE orders   SET ... WHERE id = 1     waits for T1   -> CYCLE

  the deadlock detector finds the cycle (after ~1s in PostgreSQL, immediately
  in SQL Server) and aborts the transaction that is cheapest to roll back.

  victim gets:   SQLSTATE 40P01 (PostgreSQL) / error 1205 (SQL Server)
  the other one proceeds normally.
```

1.  **Acquire locks in a consistent order** across the entire codebase — always `orders` before `invoices`, always ascending by id. This one convention eliminates most deadlocks.
2.  **Keep transactions short.** Fewer locks, held for less time, means a smaller window for a cycle.
3.  **Touch fewer rows.** A missing index causes a scan; a scan takes many more locks; more locks means more conflict.
4.  **Use a single statement where possible.** One `UPDATE ... WHERE` takes its locks in the engine's chosen order, consistently.
5.  **Consider snapshot isolation / RCSI** so readers stop taking shared locks at all — often the single most effective change.
6.  **Always retry the victim.** Deadlocks are transient by definition, so the application must handle 40P01/1205 with a retry.

``` sql
-- diagnose: PostgreSQL logs the full cycle if you ask it to
log_lock_waits = on
deadlock_timeout = '1s'

-- who is blocking whom, right now
SELECT blocked.pid AS blocked_pid, blocked.query AS blocked_query,
       blocking.pid AS blocking_pid, blocking.query AS blocking_query,
       now() - blocked.query_start AS blocked_for
FROM   pg_stat_activity blocked
JOIN   pg_stat_activity blocking ON blocking.pid = ANY(pg_blocking_pids(blocked.pid))
WHERE  blocked.wait_event_type = 'Lock';

-- SQL Server: the deadlock graph is in the system_health extended event session
```

> **\[Trap\]**
>
> **Common trap.** A deadlock is not a bug in the database and the fix is rarely a hint. It is a lock-ordering problem in your code, and the retry is a requirement rather than a workaround. Note also that MySQL's default repeatable read uses gap locks, which produce deadlocks between statements that appear to touch different rows entirely — if you see inexplicable deadlocks on InnoDB, gap locking is the first thing to investigate.

<a id="be-d449"></a>

#### Optimistic and pessimistic concurrency

Two strategies for the read-modify-write cycle, which no isolation level below repeatable read protects.

**Optimistic**

- Read, compute, then check nothing changed at write time
- A version column or timestamp in the `WHERE` clause
- No locks held while the user or the code thinks
- The caller must handle the conflict and retry
- Right for web applications and APIs

**Pessimistic**

- Lock the row on read, hold it until commit
- `SELECT ... FOR UPDATE`
- Others wait; no conflict can occur
- Risk of blocking, timeouts and deadlocks
- Right for short, contended, must-not-fail work

``` csharp
-- OPTIMISTIC: the version check is part of the UPDATE, so it is atomic
UPDATE products
SET    price = :new_price, version = version + 1
WHERE  id = :id AND version = :version_i_read;
-- 0 rows affected => someone else won. Re-read and decide: retry, merge, or 409.

-- PESSIMISTIC: hold the row from read to commit
BEGIN;
  SELECT price FROM products WHERE id = :id FOR UPDATE;   -- others block here
  -- ... compute ...
  UPDATE products SET price = :new_price WHERE id = :id;
COMMIT;

-- BEST WHERE POSSIBLE: avoid the read-modify-write entirely
UPDATE accounts SET balance = balance - :amount
WHERE  id = :id AND balance >= :amount;
-- atomic, no version needed, and the guard is part of the same statement.
-- 0 rows affected = insufficient funds. This is the form to reach for first.
```

| Situation                                | Approach                                              |
|------------------------------------------|-------------------------------------------------------|
| A user edits a form over several minutes | Optimistic — never hold a lock across user think-time |
| Decrementing stock at checkout           | Atomic conditional update                             |
| Allocating a sequence or a unique code   | Pessimistic, or a database sequence                   |
| A short, highly contended counter        | Atomic increment, or a sharded counter                |
| A multi-row invariant                    | Serialisable, or a lock on a designated parent row    |
| Batch processing distinct rows           | `FOR UPDATE SKIP LOCKED`                              |

> **\[Note\]**
>
> **Distributed locks are a different problem.** A lock in Redis or a lease table coordinates *processes*, not database rows, and it is best-effort: clock skew, pauses and network partitions can allow two holders. Never rely on a distributed lock for a correctness invariant that the database itself could enforce with a constraint or an atomic statement.

<a id="be-d450"></a>

#### Distributed transactions, and why to avoid them

Two-phase commit gives you atomicity across separate systems, at a cost that makes it the wrong answer in almost every modern architecture.

``` text
  TWO-PHASE COMMIT

  coordinator -> all participants:  PREPARE
    each participant: write the changes durably, hold ALL LOCKS, reply "ready"
  coordinator -> all participants:  COMMIT
    each participant: make it visible, release locks

  the failure mode that matters:
    the coordinator dies between PREPARE and COMMIT.
    every participant sits holding locks, unable to decide, INDEFINITELY.
    -> the transaction is "in doubt". A human must resolve it.
    -> meanwhile those locks block everything that touches those rows.
```

| Problem                                         | Consequence                                                          |
|-------------------------------------------------|----------------------------------------------------------------------|
| Blocking protocol                               | One slow participant holds locks in all of them                      |
| Coordinator is a single point of failure        | Its failure leaves in-doubt transactions                             |
| Latency is the sum of all participants          | Plus two extra round trips                                           |
| Availability is the product of all participants | Three systems at 99.9% give 99.7%                                    |
| Poor support                                    | Many modern databases and message brokers do not implement XA at all |

The alternatives, in order of preference: put the things that must be atomic in the *same* database and use one local transaction; or accept eventual consistency with the outbox pattern and idempotent consumers; or use a saga with explicit compensating actions.

``` sql
-- the outbox pattern: ONE local transaction, and the message is not lost
BEGIN;
  UPDATE orders SET status = 'Placed' WHERE id = :id;
  INSERT INTO outbox (id, type, payload, occurred_at)
  VALUES (gen_random_uuid(), 'OrderPlaced', :payload, now());
COMMIT;
-- a separate relay polls the outbox and publishes. If it crashes after publishing
-- and before marking the row, the message is delivered twice - so consumers must be
-- idempotent. That is the trade: at-least-once delivery instead of a distributed
-- transaction, and it is almost always the right one.

CREATE INDEX ix_outbox_pending ON outbox (occurred_at) WHERE published_at IS NULL;
```

> **\[Note\]**
>
> **The design signal.** A requirement for a distributed transaction is usually evidence that two things which must be consistent have been placed in different systems. Before building 2PC or a saga, seriously consider moving them back together. Consistency requirements are a boundary-finding tool, not just a technical constraint to satisfy.

### SQL you should write by hand

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · SQL you should write by hand</p>

<a id="be-m192"></a>

#### Locking, blocking, and deadlocks

**Blocking** is one transaction waiting for another's lock — normal, and only a problem when it lasts. **Deadlock** is a cycle of waits that can never resolve, so the engine kills one transaction (SQL Server error 1205) and rolls it back.

``` text
-- The classic cycle: two transactions touching the same rows in opposite order
-- T1: UPDATE Accounts WHERE Id = 1 ... then UPDATE Accounts WHERE Id = 2
-- T2: UPDATE Accounts WHERE Id = 2 ... then UPDATE Accounts WHERE Id = 1
```

**Prevention, in order of effectiveness:**

1.  **Consistent ordering.** Always touch resources in the same order — sort ids before updating them. This alone eliminates most application-level deadlocks.
2.  **Shorter transactions.** Never hold a transaction across a network call, a user interaction, or a message publish. Do the reads first, the writes last, commit immediately.
3.  **Better indexes.** A scan takes far more locks than a seek. Many deadlocks disappear when the missing index is added.
4.  **`READ_COMMITTED_SNAPSHOT`** at the database level, so readers use row versions and never block writers. This is the single highest-impact setting on a read-heavy OLTP database ([Transactions and isolation levels](#be-a100)).
5.  **Retry.** Deadlocks are transient by definition. Catch 1205, back off with jitter, retry — and make sure the unit of work is idempotent.

> **\[Trap\]**
>
> **Lock escalation.** Past roughly 5,000 locks on one table, SQL Server escalates row locks to a table lock, and unrelated work stops. This is why bulk updates should be batched — `DELETE TOP (2000) ... ` in a loop with a commit between batches keeps the lock count under the threshold and keeps the log from growing without bound.

> **\[Tip\]**
>
> Deadlock graphs are captured by default in the `system_health` extended events session. You do not need to reproduce the deadlock to diagnose it — query `system_health` for the XML graph, which names both statements and the resources they contended for.

Worked example: [Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303)

### Recipes: code you will actually write

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Recipes: code you will actually write</p>

<a id="be-m292"></a>

#### Recipe: a concurrent-safe update from an API

``` csharp
public sealed record UpdateOrderRequest {
    public required decimal Total { get; init; }
    public required OrderStatus Status { get; init; }
    public required string RowVersion { get; init; }     // base64, from the GET
}

app.MapPut("/orders/{id:long}", async (long id, UpdateOrderRequest req, ITenant tenant,
                                       AppDb db, CancellationToken ct) => {

    // 1. Load the tracked entity. NEVER db.Update(dto) (§174).
    var order = await db.Orders.FirstOrDefaultAsync(
        o => o.Id == id && o.TenantId == tenant.Id, ct);
    if (order is null) return Results.NotFound();

    // 2. Tell EF what the client BELIEVED the version was. This is what makes
    //    the UPDATE's WHERE clause detect a concurrent change
    //    (§169).
    db.Entry(order).Property(o => o.RowVersion).OriginalValue =
        Convert.FromBase64String(req.RowVersion);

    // 3. Apply only the fields the contract allows to change. Anything not
    //    listed here cannot be modified by a client (§239).
    order.ChangeTotal(req.Total);                        // domain method, invariants inside
    order.TransitionTo(req.Status);                      // throws if illegal

    try {
        await db.SaveChangesAsync(ct);                   // UPDATE ... WHERE RowVersion = @orig
    }
    catch (DbUpdateConcurrencyException ex) {
        var entry = ex.Entries.Single();
        var current = await entry.GetDatabaseValuesAsync(ct);

        if (current is null)
            return Results.Problem("The order was deleted", statusCode: 409);

        // Return the CURRENT state so the client can show a real diff, rather
        // than a bare "conflict" the user cannot act on.
        return Results.Json(new {
            error = "The order was modified by someone else",
            current = OrderDto.From((Order)current.ToObject())
        }, statusCode: 409);
    }
    catch (DomainException dex) {
        return Results.Problem(dex.Message, statusCode: 422);   // (§242)
    }

    return Results.Ok(OrderDto.From(order));
})
.RequireAuthorization("orders:write");
```

> **\[Trap\]**
>
> **Without step 2, the concurrency token does nothing.** EF compares against the value it loaded a moment ago, which always matches — so the last writer silently wins and the token creates a false sense of safety. The client's version has to enter the comparison explicitly, which means the `GET` must return it and the `PUT` must send it back.

> **\[Tip\]**
>
> **Returning the current state on a 409 is the difference between a usable API and an annoying one.** "Conflict" forces the client to re-fetch and the user to redo their work; "conflict, and here is what it looks like now" lets the UI show a diff and offer a merge. It costs one extra round trip on a path that is already rare.
