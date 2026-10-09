---
id: "7-1"
number: "7.1"
title: "Why databases exist"
part: 7
part_title: "Storing data"
words: 5277
minutes: 23
---

# 7.1 Why databases exist

> **Quick look.** Why databases exist: storing data safely while many users read and change it at once.
>
> - A database groups changes into all-or-nothing transactions, survives crashes and enforces rules on every change.
> - It answers queries without scanning everything, using indexes.
> - Underneath: fixed-size pages, a buffer pool in memory, and a write-ahead log written before any change.
> - Relational is the default; SQLite is embedded in your process, SQL Server and Postgres run as services.
> - Don't read, change and write back in the application; let the database do it in one statement.
> - Keep heavy reports off the live operational database.


### The big picture

Storage and files (1.6) showed that a file is just a named row of bytes, and that even saving one safely takes care. Now imagine an airline's bookings in files. Hundreds of agents change them at once. Some changes involve several records that must all happen or none: a seat taken from one flight and given to another. A crash can come at any moment. And people constantly ask questions like "which passengers on tomorrow's flights have a connection under an hour?".

Doing all of that correctly with plain files is extremely hard. A database is a program built to do exactly that: store data safely, let many users read and change it at once without corrupting it, enforce rules about what data is valid, and answer questions about it quickly. This chapter explains what problems databases solve, how they solve them underneath, and the main kinds you will meet. The rest of Part 7 goes into each piece in depth.

### Before you start

**Builds on:** 1.6 Storage and files, 5.1 Why concurrency is hard, 1.7 The operating system, 3.4 Trees, 1.4 The processor, 6.3 TCP and UDP.

**Ideas explained later:**

- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **SQL** (7.2): Structured Query Language: the standard declarative language of relational databases.
- **Relational model** (7.2): Storing data as tables of rows and columns, connected by keys.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **NuGet** (11.2): .NET's package system, with feeds such as nuget.org and private feeds.

**Tools and technologies named here:**

- **SQL Server** (7.2): Microsoft's relational database product.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.

**The code uses:** variables and var, methods and local functions, string interpolation, lambdas, LINQ, exceptions, using for cleanup, async and await, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Name four problems a database solves that plain files don't.**

> Many users changing data at once without losing updates; groups of changes that happen all together or not at all; surviving crashes without corruption; and answering questions quickly without reading everything. Databases also enforce rules about valid data.

**2. What is a write-ahead log?**

> A file where the database records each change, in order, before changing the data itself. After a crash, it replays or undoes changes from the log, so committed work survives and half-done work disappears.

**3. Why does a database server use so much memory?**

> It keeps recently used data pages in a memory cache, the buffer pool, so most reads never touch the disk. More memory means more of the database stays cached.

**4. What is the difference between SQLite and SQL Server as databases?**

> SQLite is an embedded database: a library inside your program, storing everything in one file. SQL Server is a client-server database: a separate service that many programs connect to over the network.

**5. What is the difference between OLTP and OLAP?**

> OLTP, online transaction processing, is many small reads and writes, such as bookings and payments. OLAP, online analytical processing, is fewer, large queries that scan and summarize huge amounts of data, such as monthly revenue by route.

**6. Why is UPDATE seats SET available = available - 1 WHERE available > 0 safer than reading the count and writing a new value?**

> The database performs the check and the change as one step on each row, so two bookings can't both take the last seat. Reading the count in your program and writing it back later leaves a gap for the check-then-act race.

### What goes wrong with plain files

Suppose an airline keeps each flight's available seat count in a file. Four problems appear almost at once.

**Many writers.** Two agents book the last seat at the same moment. Each program reads "1 seat left", each writes "0", and two passengers hold the same seat. That is the check-then-act race from Why concurrency is hard (5.1), across processes and machines this time.

**Changes that belong together.** Moving a passenger from one flight to another means freeing a seat on one and taking a seat on the other. If the program crashes between the two writes, a seat has vanished. The two changes need to happen together or not at all.

**Crashes.** Storage and files (1.6) showed that a crash in the middle of a write can leave a file half old and half new. With thousands of records in many files, recovering to a consistent state after a power cut is a serious problem.

**Questions.** "Which flights tomorrow are more than 90% full?" means reading and checking every record, every time. With millions of records and hundreds of such questions a second, scanning files doesn't scale.

Every application that stores important data meets these problems. Solving them well once, in one carefully built piece of software, is far better than solving them badly in every application.

### What a database is

A **database** is an organized collection of data, stored so that it can be read, changed and queried reliably. The software that manages it is a **database management system** (DBMS). In everyday speech, "the database" means either or both.

A DBMS gives applications several guarantees. Many users can work at the same time without corrupting data or losing each other's changes. A group of changes can be made all-or-nothing, as a **transaction**. Committed changes survive crashes. Rules about valid data, called constraints, are enforced for every change, from every program. And questions are answered with a **query language**, most often SQL, which says what data you want and leaves the database to work out how to find it.

Transactions get a chapter of their own, Transactions and isolation (7.4), as does SQL in The relational model and SQL (7.2).

### Inside a database

Underneath, a database uses the same building blocks as the rest of this guide: files, pages, caches, trees and logs.

Data is stored in fixed-size blocks called **database pages**, 8 KB in SQL Server and PostgreSQL, 4 KB by default in SQLite. Pages match the way disks and the operating system move data, as Storage and files (1.6) and The operating system (1.7) described. A table's rows are packed into pages, and indexes are B-trees of pages, as Trees (3.4) explained.

Reading from disk is slow, so the database keeps recently used pages in memory, in a cache called the **buffer pool**. Most reads in a healthy system never touch the disk. That is why a database server uses all the memory you give it, and why adding memory is often the cheapest way to speed one up.

Changes are handled with a **write-ahead log** (WAL): a file where every change is appended, in order, before the data pages themselves are changed. SQL Server calls it the transaction log. Appending to a log is fast, because it is sequential. When a transaction commits, the database flushes the log to disk, the fsync from Storage and files (1.6), and only then reports success. The changed data pages can be written later, in the background, at a **checkpoint**.

After a crash, the database performs **crash recovery**. It reads the log, redoes committed changes whose pages hadn't been written yet, and undoes changes from transactions that never committed. The result is exactly the committed state, as if the crash had happened between transactions. It is the journaling idea from Storage and files (1.6), applied to every change a database makes.

### Kinds of database

The most common kind is the **relational database**, which stores data in tables of rows and columns and is queried with SQL. SQL Server, PostgreSQL, MySQL, Oracle and SQLite are relational. They have dominated since the 1980s, and they are the right default for most business data.

Relational databases come in two deployment styles.

A **client-server database** runs as a separate service, usually on its own machine. Applications connect to it over the network with a driver and a **connection string**, a short text naming the server, the database and how to log in, such as `Server=db1;Database=Airline;Integrated Security=true`. SQL Server and PostgreSQL work this way, and so do their managed cloud versions.

An **embedded database** is a library that runs inside your own program and stores its data in a local file. There is no server and no network. SQLite is by far the most widely used; it is inside every phone, every browser and countless desktop apps. It suits single-user apps, local caches, tests and tools. It allows many readers but only one writer at a time, so it isn't built for busy multi-user servers.

Other kinds of store make different trade-offs. Document databases, key-value stores, graph databases, search engines and time-series databases are covered in Non-relational stores (7.6).

### Two kinds of work

Database work falls into two broad shapes.

**OLTP**, online transaction processing, means many small operations: book a seat, record a payment, update a passenger's email. Each touches a few rows and must be fast and correct under heavy concurrency. Operational systems do this.

**OLAP**, online analytical processing, means fewer, much larger queries: total revenue by route and month over five years, or average delay by aircraft type. Each scans and summarizes millions or billions of rows. Reporting and analytics do this.

The two pull in different directions. OLTP wants rows stored together so one record is quick to read and change. OLAP wants each column stored together, so a query that sums one column reads only that column; that is the columnstore idea from The processor (1.4). So organizations often copy operational data into a separate **data warehouse** built for analysis, such as Azure Synapse, Snowflake, BigQuery or a warehouse inside SQL Server using columnstore indexes. Running heavy reports directly against the operational database can slow down the bookings.

### Seeing a database do its job

This program uses SQLite, so it runs anywhere with no server to install. It shows three of the guarantees from this chapter. The package directive downloads the SQLite driver from NuGet, as Reading the code in this guide (0.1) explained.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// why.cs — run with: dotnet run why.cs
using System.Linq;
using Microsoft.Data.Sqlite;

File.Delete("airline.db");
const string Connection = "Data Source=airline.db";   // a connection string: here, just the file name
Run("""
    CREATE TABLE seats (
        flight    TEXT PRIMARY KEY,
        available INTEGER NOT NULL CHECK (available >= 0)   -- a rule the database enforces for every change
    );
    INSERT INTO seats VALUES ('BA117', 10), ('QF1', 5);
    """);

// 1. Twenty bookings at once, each on its own connection, for ten seats
int[] changed = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
    Run("UPDATE seats SET available = available - 1 WHERE flight = 'BA117' AND available > 0"))));
Console.WriteLine($"booked {changed.Sum()} of 20, seats left {Scalar("SELECT available FROM seats WHERE flight = 'BA117'")}");
// booked 10 of 20, seats left 0: the check and the change happen together, row by row

// 2. The database refuses data that breaks its rules, whichever program sends it
try { Run("UPDATE seats SET available = -1 WHERE flight = 'QF1'"); }
catch (SqliteException ex) { Console.WriteLine($"refused: {ex.Message}"); }
// refused: SQLite Error 19: 'CHECK constraint failed: available >= 0'.

// 3. All or nothing: move 3 seats from QF1 to BA117, with a crash halfway
try { MoveSeats("QF1", "BA117", 3, crashHalfway: true); }
catch (InvalidOperationException ex) { Console.WriteLine(ex.Message); }
Console.WriteLine($"QF1 still has {Scalar("SELECT available FROM seats WHERE flight = 'QF1'")} seats");   // 5: the first change was undone

static void MoveSeats(string from, string to, int count, bool crashHalfway)
{
    using var db = new SqliteConnection(Connection);
    db.Open();
    using var tx = db.BeginTransaction();                  // start a transaction
    Execute(db, tx, $"UPDATE seats SET available = available - {count} WHERE flight = '{from}'");
    if (crashHalfway) throw new InvalidOperationException("the app failed before the second change");
    Execute(db, tx, $"UPDATE seats SET available = available + {count} WHERE flight = '{to}'");
    tx.Commit();                                           // only now do both changes become permanent
}                                                          // disposing an uncommitted transaction rolls it back

static int Run(string sql)
{
    using var db = new SqliteConnection(Connection);
    db.Open();
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    return cmd.ExecuteNonQuery();                          // how many rows changed
}

static void Execute(SqliteConnection db, SqliteTransaction tx, string sql)
{
    using var cmd = db.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

static long Scalar(string sql)
{
    using var db = new SqliteConnection(Connection);
    db.Open();
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    return (long)cmd.ExecuteScalar()!;
}
```

The SQL here builds statements by joining strings, to keep the example short. Real code must pass values as parameters instead, for the reasons in The relational model and SQL (7.2).

### Talking to a database from code

An application talks to a database through a driver: a library that speaks the database's own protocol over TCP, as TCP and UDP (6.3) described. In .NET, drivers implement a common set of types called ADO.NET: a connection, a command with SQL text and parameters, and a reader for results. `Microsoft.Data.SqlClient` is the driver for SQL Server, `Npgsql` for PostgreSQL and `Microsoft.Data.Sqlite` for SQLite.

Opening a network connection to a database costs a TCP handshake, usually a TLS handshake and a login, often several milliseconds. So drivers keep a connection pool, as TCP and UDP (6.3) explained. `Open` usually borrows an existing connection, and `Dispose` returns it. The rule is to open a connection as late as possible, close it as early as possible, and let the pool do the rest.

Most application code goes one level higher, to libraries that map rows to objects, such as EF Core and Dapper. They are the subject of ORMs and EF Core (7.7). They still run on these same drivers, connections and pools underneath.

### Where databases run

A database can run on your own servers, in a virtual machine or container, or as a managed cloud service such as Azure SQL Database, Amazon RDS or Azure Database for PostgreSQL. Managed services handle backups, patching, failover and much of the tuning, and charge for size and performance level. For most teams they are the sensible default. Running a database yourself means you own the backups, the upgrades and the 3 a.m. recovery.

Whichever you choose, the backup advice from Storage and files (1.6) applies with extra force: take backups, keep them separate from the database, and practise restoring them. Databases also support point-in-time restore, replaying the log up to a chosen moment, which rescues you from a mistaken `DELETE` as well as from lost disks.

### In short

A database stores data safely and lets many users read and change it at once. It groups changes into all-or-nothing transactions, survives crashes, enforces rules on every change and answers queries without scanning everything. Underneath, it keeps data in fixed-size pages, caches them in a buffer pool, and records every change in a write-ahead log before applying it, which is how it recovers from crashes. Relational databases are the default; embedded ones like SQLite live inside your program, client-server ones like SQL Server run as a service. Keep heavy analysis away from the operational database.

### What goes wrong

**Read, change, write back in the application.** Reading a value, computing a new one in C# and writing it back loses updates when two users do it at once. Let the database do the check and the change in one statement, or use the concurrency techniques in Transactions and isolation (7.4).

**Rules enforced only in application code.** Another app, a script or a manual fix writes invalid data straight into the database. Put essential rules in the database too, as constraints.

**Leaking connections.** Connections that are opened and never disposed drain the pool, and requests start failing with timeouts while the database itself is idle. Use `using` on every connection and command.

**Heavy reports on the live database.** A month-end report scans millions of rows and slows every booking. Run analytics on a replica or a data warehouse.

**SQLite for a busy multi-user server.** One writer at a time becomes a bottleneck under concurrent load. Use a client-server database for shared, write-heavy workloads.

**Untested backups.** A database that has backups but has never been restored has an unknown recovery time, or none. Restore regularly into a test environment.

### Cost model

A query answered from pages in the buffer pool takes microseconds to a few milliseconds inside the database. Reading a page from an SSD adds tens to hundreds of microseconds. A commit costs at least one log flush, from well under a millisecond to several milliseconds depending on the storage, so thousands of tiny transactions are much slower than a few batched ones. Opening a new network connection costs several milliseconds; borrowing one from the pool costs microseconds. A round trip to a database server in the same data centre costs about half a millisecond, which is why hundreds of small queries per request add up.

### In other languages (optional)

**JavaScript and TypeScript.** Node.js has drivers for every database, such as `pg` for PostgreSQL and `mssql` for SQL Server, and ORMs such as Prisma and Drizzle. Browsers have their own embedded database, IndexedDB.

**Python.** The `sqlite3` module is built in, and the DB-API standard plays the role of ADO.NET. SQLAlchemy is the most common toolkit and ORM.

**Java.** JDBC is the standard driver interface, with Hibernate and JPA on top.

**Every language.** The database is shared across all of them. A PostgreSQL database behaves the same whether a C#, Python or Go program sends the query, which is why rules that must always hold belong in the database.

### Key terms

- **Database**: An organized collection of data stored so it can be read, changed and queried reliably.
- **Database management system**: The software that manages a database, such as SQL Server or PostgreSQL. Often shortened to DBMS.
- **Transaction**: A group of database changes that happen all together or not at all.
- **Query language**: A language for asking a database for data, most often SQL.
- **Database page**: A fixed-size block, typically 4 or 8 KB, in which a database stores rows and indexes.
- **Buffer pool**: The database's memory cache of recently used pages.
- **Write-ahead log**: A log where every change is recorded before the data itself is changed, used for crash recovery. Often shortened to WAL.
- **Checkpoint**: The point at which changed pages in memory are written to the data files.
- **Crash recovery**: Replaying and undoing logged changes after a crash so only committed work remains.
- **Relational database**: A database that stores data in tables of rows and columns and is queried with SQL.
- **Client-server database**: A database that runs as a separate service that programs connect to over the network.
- **Connection string**: Text telling a driver which server and database to connect to and how to log in.
- **Embedded database**: A database that runs as a library inside the program, storing data in a local file.
- **OLTP**: Online transaction processing: many small, concurrent reads and writes.
- **OLAP**: Online analytical processing: large queries that scan and summarize a lot of data.
- **Data warehouse**: A database designed for analysis, usually loaded with copies of operational data.

### Where this leads

The language of relational databases is in The relational model and SQL (7.2). How databases answer queries quickly is in Indexes and query plans (7.3). What transactions guarantee, and at what cost, is in Transactions and isolation (7.4). Designing tables is in Schema design and migrations (7.5), the alternatives to relational databases are in Non-relational stores (7.6), and working with databases from C# objects is in ORMs and EF Core (7.7).

### Check yourself

1. Run `why.cs`, then change `crashHalfway` to `false` and check that both flights change together.
2. Replace the single `UPDATE` in the booking loop with a `SELECT` of the seat count followed by a separate `UPDATE ... SET available = @newValue`. Run it and count the bookings.
3. Open `airline.db` with the `sqlite3` command-line tool or a database viewer, and list the tables and rows.
4. Find the connection strings in an application you know. Identify the server, the database, the login method and any pool settings.
5. For a system you know, sort its main database operations into OLTP and OLAP. Decide whether any of the heavy ones should move elsewhere.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Loyalty points updated in the application, then lost

Two simultaneous flights complete for the same member. Each request reads 1,000 points, adds 500 and writes 1,500. The member should have 2,000.

*Let the database do the arithmetic*

```sql
UPDATE LoyaltyAccounts
SET    Points = Points + @earned, UpdatedAt = SYSUTCDATETIME()
WHERE  MemberId = @memberId;
```

> **Lesson:** Read, change, write back in the application loses updates. One UPDATE statement is atomic.

#### Example 2: A weekly report that slowed check-in

Every Monday at 08:00 a finance report scans two years of bookings on the primary database and check-in latency triples.

*Route analytics elsewhere*

```csharp
builder.Services.AddDbContext<BookingDb>(o => o.UseSqlServer(cfg.GetConnectionString("Primary")));
builder.Services.AddDbContext<ReportingDb>(o => o.UseSqlServer(cfg.GetConnectionString("ReadReplica"))
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
// ReadReplica: ApplicationIntent=ReadOnly on an availability-group secondary
```

> **Lesson:** Keep heavy reporting off the database that serves live transactions.

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a93"></a>

#### What a database is, from the application's side

This part goes quickly into connection pooling, change tracking and query plans. All of it assumes a picture of what actually happens when your code talks to a database. Here is that picture.

##### It is another program, on another machine, that you talk to over a network

``` text
  your application                          the database server
  ---------------                           -------------------
       |                                            |
       |  1. open a CONNECTION (a TCP socket)       |
       |------------------------------------------->|
       |     authenticate                           |
       |                                            |
       |  2. send a COMMAND: text, plus parameters  |
       |     "SELECT * FROM orders WHERE id = @p"   |
       |------------------------------------------->|
       |                                     3. parse, plan, execute
       |                                        read pages from disk or cache
       |  4. rows stream back                       |
       |<-------------------------------------------|
       |  5. you read them, one at a time           |
       |                                            |

  three consequences that explain almost everything in this part:

    it is a NETWORK CALL      every query costs a round trip. 50 queries
                              in a loop is 50 round trips - which is why
                              the N+1 problem matters so much.
    it is ANOTHER PROCESS     with its own memory, CPU and limits. It can
                              be the bottleneck while your app is idle.
    the CONNECTION IS SCARCE  it costs memory on the server, and there is
                              a hard cap on how many exist.
```

##### The four things you send

| Concept         | Is                                         | Why it matters                                                                                                           |
|-----------------|--------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|
| **Connection**  | An authenticated TCP session to the server | Expensive to open. Pooled and reused, never created per query                                                            |
| **Command**     | A statement plus its parameters            | Parameters are sent *separately* from the text — which is why parameterised queries prevent SQL injection *structurally* |
| **Reader**      | A forward-only cursor over the results     | Rows stream back. Holding a reader open holds the connection                                                             |
| **Transaction** | A boundary around several commands         | See below — this is the one most often assumed                                                                           |

##### A transaction, precisely

``` text
  a TRANSACTION groups statements so that they succeed or fail TOGETHER.

    BEGIN
      UPDATE accounts SET balance = balance - 100 WHERE id = 1;
      UPDATE accounts SET balance = balance + 100 WHERE id = 2;
    COMMIT

  without it, a crash between the two statements destroys 100 units of
  money. With it, the database guarantees:

    ATOMIC       both, or neither. There is no half-done state.
    CONSISTENT   declared constraints still hold when it finishes
    ISOLATED     other sessions do not see your half-finished work
    DURABLE      once COMMIT returns, a power cut cannot undo it

  and the part people miss: EVERY statement runs in a transaction.
  if you do not start one, the database wraps each statement in its own.
  "no transaction" really means "one transaction per statement".

  ROLLBACK discards everything since BEGIN, as though it never happened.
```

``` csharp
// the same thing in ADO.NET, which is what EF Core does underneath
await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);                    // rents from the POOL

await using var tx = await conn.BeginTransactionAsync(ct);

await using var cmd = conn.CreateCommand();
cmd.Transaction = tx;
cmd.CommandText = "UPDATE accounts SET balance = balance - @amt WHERE id = @id";
cmd.Parameters.AddWithValue("amt", 100);     // a PARAMETER, not string concatenation
cmd.Parameters.AddWithValue("id", 1);
await cmd.ExecuteNonQueryAsync(ct);

await tx.CommitAsync(ct);                    // now it is durable
// conn disposed -> RETURNED to the pool. The socket stays open.
```

##### Where an ORM fits

``` text
  what EF Core does is translate, and track:

    db.Orders.Where(o => o.Total > 100).ToListAsync()
        |
        |  translate the expression tree to SQL
        v
    SELECT ... FROM orders WHERE total > 100
        |
        |  execute over a POOLED CONNECTION
        v
    rows
        |
        |  MATERIALISE into Order objects
        |  and REMEMBER what they looked like  <- the change tracker
        v
    List<Order>

    later: order.Total = 200;  await db.SaveChangesAsync();
        |
        |  compare current values to remembered values
        v
    UPDATE orders SET total = 200 WHERE id = 4471    (in a transaction)

  everything in this part is either about that TRANSLATION step producing
  bad SQL, or about that TRACKING step costing more than you expected.
```

> **\[Note\]**
>
> **The mental model to carry forward.** Your ORM is a convenience over *text sent to another process over a socket*. When a query is slow, the question is what SQL was actually sent and what the server did with it — not what the C# looked like. That is why the first move in every performance section here is “turn on SQL logging and read what it generated”.

###### Watching the round trips, and the N+1 as it appears in SQL

``` csharp
// turn on SQL logging in development. Always. This is the single most
// effective thing you can do for database performance.
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(cs)
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging());     // shows PARAMETER VALUES. Dev only.

// then run one page load and count the statements. The N+1 is obvious:
var orders = await db.Orders.ToListAsync(ct);          // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);                 // 1 query EACH

// SELECT * FROM orders                                  <- 1
// SELECT * FROM customers WHERE id = 1                  <- 2
// SELECT * FROM customers WHERE id = 2                  <- 3
// ... 50 more
// 51 round trips. At 2 ms each on a local machine that is 100 ms and
// nobody notices. At 20 ms across a network it is ONE SECOND.
```

``` text
  WHY THIS IS THE MOST COMMON DATABASE PROBLEM

    it is INVISIBLE in the C#. The line `o.Customer.Name` looks like a
    property read. It is a network round trip.

    it SCALES WITH DATA, not with code. Works perfectly with 10 test
    rows; falls over at 5,000 production rows.

    it is FASTER ON A DEVELOPER MACHINE, where the database is local
    and the round trip is 0.2 ms instead of 20 ms.

  the three fixes, and choosing between them

    Include        one query with a JOIN
                   -> good for one-to-one; duplicates parent rows badly
                      on one-to-many ("cartesian explosion")

    AsSplitQuery   one query per navigation, not per ROW
                   -> 51 queries becomes 2

    Select         project ONLY what you need
                   -> best. One query, three columns, no entities.

      db.Orders.Select(o => new Row(o.Id, o.Total, o.Customer.Name))
```

| Round-trip cost         | Typical    | So                                           |
|-------------------------|------------|----------------------------------------------|
| Same machine            | ~0.2 ms    | 50 queries = 10 ms. Invisible in development |
| Same datacentre         | ~1–2 ms    | 50 queries = 100 ms. Noticeable              |
| Cross availability zone | ~2–5 ms    | 50 queries = 250 ms. A complaint             |
| Cross region            | ~50–150 ms | 50 queries = **7 seconds**. An outage        |

The pattern generalises beyond databases: **any per-item remote call in a loop is the same bug**. An HTTP call per row, a cache lookup per row, a message published per row. The fix is always the same shape — batch it, or fetch what you need in one call — and the diagnosis is always the same: count the round trips for one user action.

### Foundations

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Foundations</p>

<p class="pd">The model underneath SQL. Most durable database skill comes from understanding relations, sets and three-valued logic — the syntax is the easy part, and it is the part that changes.</p>

<a id="be-d393"></a>

#### What a database management system actually does

A database is not a file format with a query language bolted on. A DBMS is a system that makes four hard guarantees simultaneously, and almost everything else about its design follows from having to keep all four at once.

| Guarantee                   | Means                                                                               | Mechanism                                           |
|-----------------------------|-------------------------------------------------------------------------------------|-----------------------------------------------------|
| **Durability**              | A committed change survives a crash mid-write                                       | Write-ahead log, fsync, checkpoints                 |
| **Consistency of the data** | Constraints are never violated, even under concurrency                              | Constraint checking inside the transaction boundary |
| **Concurrency**             | Thousands of sessions read and write without seeing each other's half-finished work | Locking, MVCC, isolation levels                     |
| **Efficient access**        | Find 10 rows among a billion without reading a billion                              | Indexes, statistics, a cost-based optimiser         |

``` text
  the layers, top to bottom

    client protocol / connection handling
      parser              SQL text -> syntax tree
      binder              names -> real tables, columns, types
      optimiser           logical plan -> physical plan, chosen by estimated cost
      executor            pulls rows through a tree of physical operators
        access methods    heap scans, B-tree seeks
          buffer pool     in-memory cache of disk pages
            storage       pages on disk, plus the write-ahead log
      transaction manager   locks, MVCC snapshots, commit/rollback
      recovery manager      replay the log after a crash
```

Almost every question in this guide is really a question about one of those layers, and about how a decision at one layer — a data type, an index, an isolation level — propagates through the others.

> **\[Why it matters\]** You can be productive with SQL knowing none of this, right up until something is slow or something is wrong. Every diagnostic skill worth having comes from knowing which layer to look at.
