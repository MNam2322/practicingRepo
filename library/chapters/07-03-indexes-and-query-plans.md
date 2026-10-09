---
id: "7-3"
number: "7.3"
title: "Indexes and query plans"
part: 7
part_title: "Storing data"
words: 18192
minutes: 79
---

# 7.3 Indexes and query plans

> **Quick look.** Indexes and query plans: the single most effective performance fix in most applications.
>
> - An index is a sorted B-tree copy of some columns with pointers to rows, so the database seeks instead of scanning.
> - Composite indexes follow the leftmost-prefix rule: put equality columns before range columns.
> - Covering indexes (INCLUDE columns) avoid key lookups.
> - Functions on a column or type mismatches stop an index being used.
> - Read actual plans: look for scans, key lookups, and estimated rows far from actual rows.
> - SQL Server caches a plan from the first parameter values (parameter sniffing), which can make one plan bad for others.
> - Databases read 8 KB pages; every index costs writes and memory, so add only what real queries need.
> - Index foreign keys; update statistics after big loads; avoid deep OFFSET paging.


### The big picture

A table with ten million rows can't be read from start to finish for every question. A database index works like the index at the back of a book: a sorted list of values with a pointer to where each one is, so the database can jump straight to the rows it needs. The right index turns a query that takes seconds into one that takes a millisecond. It is the single most effective performance fix in most applications.

This chapter explains what an index is underneath, the B-tree from Trees (3.4), and the difference between the kinds SQL Server offers. It covers why the order of columns in an index matters, which conditions can and can't use one, and what indexes cost. It then explains the query optimizer, the part of the database that decides how to run each query, and how to read the plans it produces. That includes parameter sniffing, a famous SQL Server behaviour that makes a query fast for one customer and slow for another.

### Before you start

**Builds on:** 3.4 Trees, 7.1 Why databases exist, 3.3 Hash tables, 3.7 Searching and sorting, 7.2 The relational model and SQL.

**Ideas explained later:**

- **Performance** (12.4): How quickly a system does its work at a given load.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Histogram** (11.6): A metric that counts measurements into value ranges, from which percentiles can be computed.
- **Cardinality** (11.6): The number of distinct values a metric label can take, which multiplies the time series stored.
- **Cache** (8.6): A store of copies of data kept close to where it is used, so repeated reads are faster.
- **ORM** (7.7): Object-relational mapper: a library that maps classes to tables and generates SQL.
- **Schema** (7.5): The structure of a database: its tables, columns, types, keys, constraints and indexes.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**The code uses:** variables and var, string interpolation, collection expressions, using for cleanup, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between a clustered and a nonclustered index in SQL Server?**

> A clustered index is the table itself, stored in key order; a table has at most one, usually on its primary key. A nonclustered index is a separate sorted structure holding its key columns and a pointer back to the row. A table can have many.

**2. An index is on (origin, departs). Which of these can seek on it: WHERE origin = 'LHR', WHERE departs > '2026-09-01', or both?**

> Only the first. An index is sorted by its first column, then the second within each value of the first, like a phone book sorted by surname then first name. You can look up a surname, but not a first name alone.

**3. Why can WHERE YEAR(departs) = 2026 be slow even with an index on departs?**

> The index is sorted by `departs`, not by `YEAR(departs)`, so the database must compute the function for every row. Write it as a range instead: `departs >= '2026-01-01' AND departs < '2027-01-01'`.

**4. What is a covering index?**

> An index that contains every column a query needs, so the database never has to visit the table itself. In SQL Server, extra columns can be added to an index's leaf level with `INCLUDE`.

**5. What is parameter sniffing?**

> SQL Server builds a query plan using the parameter values of the first call and caches it. If data is skewed, that plan can be terrible for later values: a plan built for a small airport runs very slowly for a huge one.

**6. Why doesn't every column get an index?**

> Each index must be updated on every insert, update and delete that touches its columns, and it takes storage. Too many indexes slow writes and waste memory. Index for the queries you actually run.

**7. Why does paging with OFFSET 100000 get slow, and what is the alternative?**

> The database still has to read and skip the first 100,000 rows. Keyset pagination remembers the last key seen and asks for rows after it, which an index can seek to directly.

### Without an index

Ask a database for flight BA117 in a table with no index on flight number, and it has only one option: read every row and check each one. That is a **table scan**. On a small table it is fine. On ten million rows, it reads every page of the table, many thousands of them, for every query. Most slow database queries are scans that didn't need to happen.

### What an index is

A **database index** is a separate structure that keeps the values of one or more columns in sorted order, each with a pointer back to its row. Almost always, it is a B+ tree, the wide, shallow tree from Trees (3.4), with each node filling one database page from Why databases exist (7.1).

Finding a value is a walk from the root to a leaf: three or four page reads even for millions of rows, most of them already in the buffer pool. That is an **index seek**. Because the leaves are sorted and linked, finding every value in a range is a seek to the start followed by walking along the leaves until the range ends. That is a **range scan**, and it is how `WHERE departs >= ... AND departs < ...` is answered efficiently.

An index is redundant data: the database could answer every query without it, just more slowly. It keeps each index in step with the table automatically, on every change.

### How SQL Server stores tables

SQL Server has two kinds of index, and the difference matters.

A **clustered index** is the table itself, stored as a B+ tree in the order of the index key. Its leaf pages hold the full rows. A table can have only one, because rows can only be stored in one order, and by default the primary key becomes the clustered index. A table without one is a **heap table**: rows stored in no particular order.

A **nonclustered index** is a separate B+ tree holding its own key columns plus a locator for each row: the clustered key, or a row address for a heap. A table can have many. Using one to fetch columns it doesn't contain means following the locator into the clustered index for each matching row. That extra step is a **key lookup**. A few are cheap. Thousands are expensive, and at some point the optimizer decides a scan is cheaper after all.

PostgreSQL always stores tables as heaps and every index is a separate structure. SQLite stores each table as a B-tree keyed by a hidden row id. The ideas carry across; only the names change.

### Covering indexes

If an index contains every column a query needs, the database never has to touch the table. That is a **covering index**, and it can make a frequent query dramatically cheaper. In SQL Server, you can add columns to just the leaf level of a nonclustered index with `INCLUDE`, so they are available for reading without being part of the sorted key:

```sql
CREATE INDEX IX_Flights_Origin_Departs
    ON dbo.Flights (Origin, Departs)
    INCLUDE (Number, DelayMinutes);
```

A query that filters on origin and departure time and returns the number and delay is then answered entirely from this index.

### Indexes on several columns

A **composite index** has several key columns. It is sorted by the first, then by the second within each value of the first, and so on, like a phone book sorted by surname and then by first name.

That makes column order decisive. An index on `(origin, departs)` answers "flights from LHR", and "flights from LHR in September", with a seek. It can't seek for "flights in September from any airport", just as a phone book can't find everyone called Ada without reading every page. This is often called the leftmost prefix rule: a composite index helps queries that constrain its first column, or its first few.

A good rule of thumb is to put columns tested for equality first, and the column tested with a range, such as a date, last. `(origin, departs)` serves `origin = 'LHR' AND departs BETWEEN ...` perfectly. `(departs, origin)` can only use the date range, then must check the origin row by row.

### Selectivity

An index helps most when a condition matches a small share of the rows. That share is its **selectivity**. "Flight number = BA117" matches a few rows out of millions: very selective, perfect for an index. "Cancelled = false" matches 98% of rows: an index on it alone is nearly useless, because reading almost every row through the index costs more than scanning the table.

The optimizer knows this, and will ignore an index when a condition matches too many rows. That is correct behaviour, not a bug. Low-selectivity columns become useful as the later columns of a composite index, or in a filtered index that covers only the rare rows, such as `WHERE Cancelled = 1`.

### Conditions that can use an index

A condition that can be answered by seeking an index is called **sargable**, from "search argument able". The rule underneath is simple: the index is sorted by the column's stored values, so the condition must compare the column itself, unchanged, with something.

Four patterns break this. A function on the column, such as `YEAR(departs) = 2026` or `LOWER(email) = @e`, forces the database to compute the function for every row. A leading wildcard, `LIKE '%117'`, can't use sorted order, while `LIKE 'BA%'` can. An implicit type conversion does the same damage invisibly. In SQL Server, comparing a `VARCHAR` column with an `NVARCHAR` parameter converts every row's value, and the index seek becomes a scan. .NET strings are sent as `NVARCHAR` by default, so declare parameter types to match the column. Finally, `OR` across different columns often prevents a single seek.

The fix is usually to rewrite the condition so the column stands alone: a date range instead of `YEAR`, a stored lower-case column or a case-insensitive collation instead of `LOWER`, parameters with the right type.

### Seeing it for real

This program fills a table with 200,000 flights in SQLite, then shows the plan and timing for the same queries before and after adding indexes. SQLite's `EXPLAIN QUERY PLAN` prints how it will run a query: `SCAN` means reading everything, `SEARCH` means seeking an index.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// indexes.cs — run with: dotnet run -c Release indexes.cs
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

using var db = new SqliteConnection("Data Source=:memory:");
db.Open();
Run("CREATE TABLE flights (id INTEGER PRIMARY KEY, number TEXT NOT NULL, origin TEXT NOT NULL, departs TEXT NOT NULL, delay INTEGER NOT NULL)");
string[] airports = ["LHR","JFK","SIN","DXB","SYD","CDG","FRA","AMS","HND","LAX","ORD","ATL","DFW","DEN","IST","MAD","BCN","MUC","ZRH","DOH"];
string[] airlines = ["BA", "QF", "SQ", "EK"];
using (var tx = db.BeginTransaction())                     // one transaction: one commit for all 200,000 rows
{
    using var insert = db.CreateCommand();
    insert.Transaction = tx;
    insert.CommandText = "INSERT INTO flights VALUES ($id, $number, $origin, $departs, $delay)";
    foreach (string p in new[] { "$id", "$number", "$origin", "$departs", "$delay" }) insert.Parameters.Add(new SqliteParameter(p, null));
    var start = new DateTime(2026, 1, 1);
    for (int i = 0; i < 200_000; i++)
    {
        insert.Parameters["$id"].Value = i + 1;
        insert.Parameters["$number"].Value = $"{airlines[i % 4]}{100 + (i / 4) % 900}";
        insert.Parameters["$origin"].Value = airports[i % 20];
        insert.Parameters["$departs"].Value = start.AddDays(i % 365).AddMinutes(i * 7 % 1440).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        insert.Parameters["$delay"].Value = i * 37 % 180;
        insert.ExecuteNonQuery();
    }
    tx.Commit();
}

string byNumber = "SELECT COUNT(*) FROM flights WHERE number = 'BA117'";
string byRoute = "SELECT number, delay FROM flights WHERE origin = 'LHR' AND departs >= '2026-09-01' AND departs < '2026-10-01'";

Explain(byNumber);   // SCAN flights, several ms: every row read to find 56
Run("CREATE INDEX ix_number ON flights (number)");
Explain(byNumber);   // SEARCH flights USING COVERING INDEX ix_number (number=?), microseconds

Explain(byRoute);    // SCAN flights: 822 rows found by reading 200,000
Run("CREATE INDEX ix_departs_origin ON flights (departs, origin)");
Explain(byRoute);    // SEARCH ... USING INDEX ix_departs_origin (departs>? AND departs<?): the range only, origin checked row by row
Run("CREATE INDEX ix_origin_departs ON flights (origin, departs)");
Explain(byRoute);    // SEARCH ... USING INDEX ix_origin_departs (origin=? AND departs>? AND departs<?): both columns used
Run("CREATE INDEX ix_covering ON flights (origin, departs, number, delay)");
Explain(byRoute);    // SEARCH ... USING COVERING INDEX ix_covering (...): the table itself is never read

Explain("SELECT COUNT(*) FROM flights WHERE substr(departs, 1, 7) = '2026-09'");
// SCAN flights USING COVERING INDEX ...: a function on the column means every entry is checked
Explain("SELECT COUNT(*) FROM flights WHERE departs >= '2026-09-01' AND departs < '2026-10-01'");
// SEARCH flights USING COVERING INDEX ix_departs_origin (departs>? AND departs<?): the same question, sargable

void Explain(string sql)
{
    using var plan = db.CreateCommand();
    plan.CommandText = "EXPLAIN QUERY PLAN " + sql;
    using var reader = plan.ExecuteReader();
    var steps = new List<string>();
    while (reader.Read()) steps.Add(reader.GetString(3));   // the fourth column describes each step
    using var query = db.CreateCommand();
    query.CommandText = sql;
    var sw = Stopwatch.StartNew();
    int rows = 0;
    for (int run = 0; run < 20; run++)
    {
        rows = 0;
        using var r = query.ExecuteReader();
        while (r.Read()) rows++;
    }
    Console.WriteLine($"{sw.Elapsed.TotalMilliseconds / 20,8:F3} ms  {rows,4} rows  {string.Join(" / ", steps)}");
}

void Run(string sql)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}
```

Notice the plan for the `substr` query. It says `SCAN ... USING COVERING INDEX`: the database found it cheaper to read every entry of a narrow index than every row of the table, but it still had to read all 200,000 of them.

### The query optimizer

SQL says what you want, not how to get it. The **query optimizer** is the part of the database that decides how. For each query, it considers many possible strategies: which index to use, in which order to join tables, which join algorithm, whether to sort or use an index's order. It estimates the cost of each, and picks the cheapest. The result is a **query plan**, also called an execution plan.

Its estimates depend on **database statistics**: summaries of the data in each column, including how many distinct values there are and a histogram of how values are spread. From these it produces a **cardinality estimate** for each step: how many rows it expects to flow through. When statistics are out of date, or the data is unusual, estimates go wrong, and a plan that looks cheap on paper reads millions of rows. Databases update statistics automatically as data changes, but big loads can outrun them.

The optimizer chooses between the join algorithms you met earlier in this guide. A nested loop join looks up matching rows in one table for each row of the other; it is excellent when one side is small and the other has an index. A hash join builds a hash table from one side, as in Hash tables (3.3), and probes it with the other; it suits large inputs with no useful index. A merge join walks two inputs that are already sorted on the join key, like the merge in Searching and sorting (3.7).

### Reading plans

Every database can show its plan. SQLite has `EXPLAIN QUERY PLAN`. PostgreSQL has `EXPLAIN`, and `EXPLAIN ANALYZE`, which runs the query and shows actual row counts and times beside the estimates. In SQL Server, use the actual execution plan in SQL Server Management Studio or the MSSQL extension for VS Code, together with `SET STATISTICS IO ON`, which reports how many pages each table read.

When a query is slow, look for a few things in its plan. A scan of a large table or index where you expected a seek. Many key lookups. A big gap between estimated and actual row counts, which points at statistics or at a condition the optimizer couldn't estimate. Sorts and hash operations that spill to disk because they ran out of memory. And the step with the largest share of the cost, which is where to start.

### Plan caching and parameter sniffing

Compiling a plan takes time, so databases cache plans and reuse them for later runs of the same statement text: the **plan cache**. Parameterized queries, from The relational model and SQL (7.2), are what make reuse possible, because the text stays the same while values change.

SQL Server adds a twist. When it compiles a plan for a parameterized query, it looks at the parameter values of that first call and optimizes for them. This is **parameter sniffing**. It usually helps. With skewed data, it can hurt badly. If the first call asks for flights from a small regional airport, SQL Server may pick a seek with key lookups, perfect for 12 rows. The cached plan is then reused for London Heathrow, where it performs hundreds of thousands of lookups. Symptoms are a query that is fast for some inputs and slow for others, or that suddenly slows down after a restart or a statistics update causes recompilation.

Remedies include `OPTION (RECOMPILE)` on the query, which compiles a fresh plan every time, and `OPTIMIZE FOR` hints. You can also force a good plan through Query Store, SQL Server's built-in history of queries and plans. And newer versions of SQL Server can cache several plans for different value ranges. The first step is always to recognize it.

### What indexes cost

Every index is maintained on every insert, every delete and every update that changes its columns. A table with eight indexes does nine B-tree updates per inserted row. That extra work per change is **write amplification**. Indexes also take storage, and room in the buffer pool that data could use.

So index deliberately. Index foreign key columns, which joins and deletes use constantly. Index the columns of your most frequent and most important queries. Remove indexes nothing uses; SQL Server's index usage statistics and PostgreSQL's `pg_stat_user_indexes` show which those are. Avoid overlapping ones, such as `(origin)` alongside `(origin, departs)`, where the second can do the first's job.

### Paging through results

Showing results a page at a time is usually written with `OFFSET` and a row limit: page 5,000 is `OFFSET 99980 ROWS FETCH NEXT 20 ROWS ONLY`. The database still has to find and skip the first 99,980 rows, so deep pages get slower and slower.

**Keyset pagination**, also called seek pagination, remembers where the last page ended and asks for the rows after it: `WHERE (departs, id) > (@lastDeparts, @lastId) ORDER BY departs, id`, written with an equivalent pair of conditions where the database doesn't support that row syntax. With an index on those columns, every page is a seek, however deep. The trade-off is that you can go to the next page, not jump straight to page 5,000. Most infinite-scroll lists and APIs work this way.

### In short

An index is a sorted B-tree copy of some columns with pointers to rows, so the database can seek instead of scanning. In SQL Server, the clustered index is the table in key order and nonclustered indexes point into it; covering indexes avoid key lookups. Composite indexes serve queries that constrain their leading columns, so put equality columns before range columns. Conditions must compare the bare column to use an index. The optimizer picks plans from statistics and caches them, and SQL Server's parameter sniffing can make one cached plan bad for other values. Indexes cost writes and memory, so add the ones your real queries need.

### What goes wrong

**Missing indexes on foreign keys.** Joins and deletes on the parent table scan the child table every time. Index foreign key columns.

**Functions on indexed columns.** `WHERE CONVERT(date, Departs) = @day` scans the whole table. Rewrite as a range on the bare column.

**Type mismatches.** An `NVARCHAR` parameter against a `VARCHAR` column turns seeks into scans in SQL Server. Declare parameter types to match, or configure the ORM's column types.

**Wrong column order.** An index on `(departs, origin)` does little for "all flights from LHR". Design composite indexes from the queries, equality columns first.

**Parameter sniffing.** A query is fast for most customers and times out for the largest one. Look at the cached plan for the slow case and use recompile hints or Query Store to fix it.

**Too many indexes.** Inserts slow down and the database grows, while half the indexes are never used. Check usage statistics and drop dead ones.

**Deep OFFSET paging.** Page 10,000 of a list takes seconds. Use keyset pagination for long lists and APIs.

**Stale statistics after a big load.** A nightly import leaves the optimizer with old estimates, and morning queries pick bad plans. Update statistics after large changes.

### Cost model

An index seek costs three or four page reads, usually from memory: microseconds. A key lookup costs roughly one more seek per row, so 10,000 lookups cost about as much as tens of thousands of page reads. A table scan costs every page of the table: for 10 million rows of 200 bytes, about 250,000 pages, around 2 GB. Each extra index adds one B-tree update per row written. Compiling a plan can take milliseconds for complex queries, which is why plans are cached. In practice, one well-chosen index often speeds a query up by a factor of a thousand.

### In other languages (optional)

**PostgreSQL.** Tables are heaps; `INCLUDE` columns and partial indexes, with a `WHERE` clause, are supported. `EXPLAIN (ANALYZE, BUFFERS)` shows actual rows and pages read. Besides B-trees, it offers GIN indexes for JSON and full-text search and GiST indexes for geographic data.

**SQLite.** `EXPLAIN QUERY PLAN` as in the example. Indexes on expressions let a function-based condition use an index.

**EF Core.** Indexes are declared in the model with `HasIndex`, including `IsUnique` and `IncludeProperties`, and created by migrations, as ORMs and EF Core (7.7) and Schema design and migrations (7.5) show.

**Document and search databases.** They have indexes too, with the same trade-off between faster reads and slower writes, as Non-relational stores (7.6) describes.

### Key terms

- **Table scan**: Reading every row of a table to answer a query.
- **Database index**: A separate sorted structure, usually a B+ tree, holding some columns with pointers back to rows.
- **Index seek**: Walking an index from its root straight to the matching rows.
- **Range scan**: Seeking to the start of a range in an index and reading along it to the end.
- **Clustered index**: In SQL Server, the table itself stored as a B+ tree in key order. One per table.
- **Heap table**: A table stored with no particular row order.
- **Nonclustered index**: A separate index holding its key columns and a pointer to each row.
- **Key lookup**: Following a nonclustered index's pointer into the table to fetch columns the index doesn't hold.
- **Covering index**: An index containing every column a query needs, so the table isn't read.
- **Composite index**: An index on several columns, sorted by the first, then the second, and so on.
- **Selectivity**: The share of rows a condition matches. Indexes help most for very selective conditions.
- **Sargable**: Written so that an index can be used to find matching rows.
- **Query optimizer**: The part of a database that chooses how to run each query.
- **Query plan**: The step-by-step strategy the optimizer chose for a query. Also called an execution plan.
- **Database statistics**: Summaries of the data in columns, used by the optimizer to estimate costs.
- **Cardinality estimate**: The optimizer's guess of how many rows a step of a plan will produce.
- **Plan cache**: The database's store of compiled plans, reused for later runs of the same statement.
- **Parameter sniffing**: SQL Server optimizing a cached plan for the parameter values of its first call.
- **Write amplification**: The extra work each change costs because every index must be updated too.
- **Keyset pagination**: Paging by asking for rows after the last key seen, instead of skipping rows with OFFSET.

### Where this leads

Indexes also take locks when data changes, which matters in Transactions and isolation (7.4). Declaring indexes and keys as part of the schema is in Schema design and migrations (7.5). Seeing the SQL your ORM sends, so you can check its plans, is in ORMs and EF Core (7.7). Finding slow queries in production is part of Observability (11.6), and paging in APIs comes back in API design (8.2).

### Check yourself

1. In `indexes.cs`, add `EXPLAIN QUERY PLAN` for `SELECT * FROM flights WHERE departs >= '2026-09-01'` with and without `ORDER BY departs`. Explain what changes.
2. Add a query for flights whose number ends in `117` using `LIKE '%117'`, and explain its plan.
3. Time 1,000 single-row inserts into the table with no extra indexes, then with all four. Explain the difference.
4. In SQL Server, run a slow query from a system you know with `SET STATISTICS IO ON` and the actual execution plan. Find the step with the most reads.
5. Rewrite a page-number query from a real app using keyset pagination, and decide what the API should return so clients can ask for the next page.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The order list that scanned 8 million rows

"My orders" takes 3 seconds for every customer. The plan shows a clustered index scan.

*An index shaped for the query*

```sql
-- query: WHERE CustomerId = @c ORDER BY CreatedAt DESC, select Id, Total, Status
CREATE INDEX IX_Orders_Customer_Created
ON Orders (CustomerId, CreatedAt DESC)
INCLUDE (Total, Status)               -- covering: no key lookups
WITH (ONLINE = ON);                   -- don't block writes while building
```

> **Lesson:** Put equality columns first, then the sort or range column, and include what the query returns.

#### Example 2: An index that stopped being used

After a refactor, a search by email is slow even though `IX_Users_Email` exists.

**What goes wrong.** The new code wraps the column in a function, so the index can't be seeked.

*Before / after*

```sql
-- Before: function on the column, full scan
WHERE LOWER(Email) = LOWER(@email)

-- After: store normalized values and compare the bare column
WHERE Email = @emailNormalized    -- with a case-insensitive collation or a normalized column
```

> **Lesson:** An index is usable only when the query compares the bare column to a value of the same type.

### SQL

<p class="src">From <em>.NET Backend Deep Dive</em> · SQL</p>

<a id="dn-16"></a>

#### Indexes — 90% of performance

```sql
-- Clustered index = the physical row order. One per table.
-- Non-clustered = a separate structure pointing back at the row.

-- Covering index: the query is answered from the index alone (INCLUDE)
CREATE NONCLUSTERED INDEX IX_Orders_Customer_Created
ON Orders (CustomerId, CreatedAt DESC)
INCLUDE (Total, Status)
WHERE IsDeleted = 0;                 -- filtered index for soft delete
```

**Column order in a composite index matters** (left-most prefix rule): `(CustomerId, CreatedAt)` serves `WHERE CustomerId = ?` and `WHERE CustomerId = ? AND CreatedAt > ?`, but **not** `WHERE CreatedAt > ?` on its own.

**SARGability** — how to destroy an index:

```sql
WHERE YEAR(CreatedAt) = 2026            -- function on the column -> scan
WHERE CreatedAt >= '2026-01-01' AND CreatedAt < '2027-01-01'   -- correct

WHERE CAST(CustomerId AS varchar) = @id -- implicit/explicit conversion
WHERE Name LIKE '%abc%'                 -- leading wildcard -> full scan
```

<a id="dn-17"></a>

#### Reading an execution plan

- **Table/Index Scan** on a large table → missing index.
- Many **Key Lookups** → add `INCLUDE` columns to make the index covering.
- **Estimated rows far from actual rows** → stale statistics (`UPDATE STATISTICS`) or parameter sniffing.
- **Sort / Hash Match** consuming memory → often avoidable with an index in the right order.

### Advanced SQL

<p class="src">From <em>.NET Backend Deep Dive</em> · Advanced SQL</p>

<a id="dn-72"></a>

#### Parameter sniffing — "it ran in 20ms yesterday, 20 seconds today"

SQL Server caches an execution plan based on the **parameter values from the first execution**. If that first run used a `@CustomerId` with three rows, it picks an index seek with nested loops. Run it later for a customer with two million rows and you keep the same plan — disaster.

```sql
-- Remedies, from light to heavy
OPTION (RECOMPILE)                       -- recompile every time; costs CPU, always correct
OPTION (OPTIMIZE FOR (@CustomerId = 12345))
OPTION (OPTIMIZE FOR UNKNOWN)            -- use average statistics
-- Or: split into two procedures for the two data shapes
-- Or: enable Query Store and force the good plan
```

Telltale sign: a query suddenly becomes slow after a SQL Server restart, a statistics update, or a deploy.

<a id="dn-73"></a>

#### Finding missing and unused indexes

```sql
-- Indexes SQL Server wishes you had
SELECT TOP 20
    ROUND(s.avg_total_user_cost * s.avg_user_impact * (s.user_seeks + s.user_scans), 0) AS Score,
    d.statement AS TableName,
    d.equality_columns, d.inequality_columns, d.included_columns
FROM sys.dm_db_missing_index_group_stats s
JOIN sys.dm_db_missing_index_groups g ON s.group_handle = g.index_group_handle
JOIN sys.dm_db_missing_index_details d ON g.index_handle = d.index_handle
ORDER BY Score DESC;

-- Indexes NEVER used (pure write overhead)
SELECT OBJECT_NAME(i.object_id) AS TableName, i.name,
       s.user_seeks, s.user_scans, s.user_lookups, s.user_updates
FROM sys.indexes i
LEFT JOIN sys.dm_db_index_usage_stats s
       ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE i.type_desc = 'NONCLUSTERED' AND OBJECTPROPERTY(i.object_id,'IsUserTable') = 1
  AND ISNULL(s.user_seeks,0) + ISNULL(s.user_scans,0) + ISNULL(s.user_lookups,0) = 0
ORDER BY s.user_updates DESC;

-- The most expensive queries
SELECT TOP 20
    qs.total_worker_time/qs.execution_count AS AvgCPU,
    qs.total_elapsed_time/qs.execution_count AS AvgDuration,
    qs.execution_count,
    SUBSTRING(t.text, qs.statement_start_offset/2+1, 200) AS QueryText
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
ORDER BY AvgCPU DESC;
```

> Don't blindly create every index the DMV suggests — each one slows down INSERT/UPDATE/DELETE and costs storage. Consolidate overlapping suggestions into a single composite index.

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a103"></a>

#### Indexes and how the optimiser uses them

An index is a sorted structure with pointers back to the rows. That one sentence explains almost every rule about them: sort order matters, prefix columns matter, and every index makes writes slower.

| Index type                | Structure                                   | Good for                                        |
|---------------------------|---------------------------------------------|-------------------------------------------------|
| Clustered (SQL Server)    | The table itself, sorted by the key         | Range scans on the key; one per table           |
| Non-clustered / secondary | Key columns + a pointer to the row          | Lookups on other columns                        |
| Covering                  | Includes every column the query needs       | Eliminates the lookup entirely                  |
| Composite                 | Multiple columns, leftmost-prefix rule      | Queries filtering on the same column order      |
| Filtered / partial        | Only rows matching a predicate              | `WHERE IsDeleted = 0` on a mostly-deleted table |
| Unique                    | Enforces uniqueness and helps the optimiser | Natural keys                                    |

``` csharp
// EF Core model configuration
b.Entity<Order>(e =>
{
    e.HasIndex(o => new { o.TenantId, o.PlacedAt })          // composite: tenant first
     .IncludeProperties(o => new { o.Status, o.Total })       // covering
     .HasDatabaseName("IX_Orders_Tenant_PlacedAt");

    e.HasIndex(o => o.ExternalRef).IsUnique()
     .HasFilter("[ExternalRef] IS NOT NULL");                 // filtered unique
});
```

##### The leftmost-prefix rule

An index on `(TenantId, PlacedAt)` serves `WHERE TenantId = @t` and `WHERE TenantId = @t AND PlacedAt > @d`, but not `WHERE PlacedAt > @d` alone. Column order in a composite index is a design decision, not an implementation detail: put the equality predicates first and the range predicate last.

> **\[Trap\]**
>
> **Common trap.** Wrapping a column in a function — `WHERE YEAR(PlacedAt) = 2026` or `WHERE LOWER(Email) = @e` — makes the predicate non-sargable and the index unusable. Rewrite as a range (`>= '2026-01-01' AND < '2027-01-01'`) or add a computed/persisted column with its own index.

###### Worked example

A concrete tuning session. The query is a tenant's recent orders, and it takes 2.1 seconds on a table with 40 million rows.

``` sql
SELECT TOP 50 Id, Status, Total, PlacedAt
FROM   Orders
WHERE  TenantId = @t AND Status = 'Open' AND PlacedAt >= @from
ORDER  BY PlacedAt DESC;
```

``` text
  step 1  no useful index
          plan: Clustered Index Scan (40,000,000 rows) -> Filter -> Sort -> Top
          2,100 ms, 380,000 logical reads

  step 2  CREATE INDEX IX_1 ON Orders (TenantId)
          plan: Index Seek (120,000 rows for this tenant) -> Key Lookup x120,000 -> Filter -> Sort
          410 ms. Better, but the key lookups dominate: one per row, to fetch Status/Total.

  step 3  CREATE INDEX IX_2 ON Orders (TenantId, Status, PlacedAt DESC)
          plan: Index Seek -> Key Lookup x900 -> Top
          the ORDER BY is now free: the index is already in that order, so NO SORT.
          22 ms, 2,900 reads

  step 4  ... INCLUDE (Total)          -- the only remaining non-key column
          plan: Index Seek -> Top      -- covering: zero key lookups
          3 ms, 158 reads
```

Two things to take from this. The column order in step 3 — equality predicates first (`TenantId`, `Status`), range predicate and sort column last (`PlacedAt`) — is what eliminates the sort, which was most of the remaining cost. And `INCLUDE` is what removes the key lookups: included columns are stored in the index leaf without being part of the sort key, so they make the index wider but not deeper.

###### The cost side, which people forget

| Every additional index costs | Detail                                                                                   |
|------------------------------|------------------------------------------------------------------------------------------|
| Write amplification          | Each INSERT/UPDATE/DELETE must maintain it — ten indexes means ten writes per row change |
| Storage                      | A wide covering index on a large table can approach the size of the table                |
| Memory                       | Index pages compete with data pages for the buffer pool                                  |
| Maintenance                  | Rebuilds, statistics updates, and fragmentation management                               |
| Plan instability             | More choices means more chances the optimiser picks badly                                |

``` sql
-- SQL Server: what is actually being used, and what is dead weight
SELECT OBJECT_NAME(s.object_id) AS table_name, i.name AS index_name,
       s.user_seeks, s.user_scans, s.user_lookups, s.user_updates
FROM   sys.dm_db_index_usage_stats s
JOIN   sys.indexes i ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE  s.database_id = DB_ID()
ORDER  BY s.user_updates DESC;      -- high updates, zero seeks = delete this index

-- PostgreSQL
SELECT relname, indexrelname, idx_scan, idx_tup_read
FROM   pg_stat_user_indexes ORDER BY idx_scan ASC;
```

Run this quarterly. Most mature systems carry several indexes created for a query that was rewritten years ago, each silently taxing every write. Removing an unused index on a hot table is one of the few optimisations that is both free and immediately measurable.

<a id="be-a104"></a>

#### Reading a query plan

The plan is the optimiser telling you exactly what it decided to do. Learning to read one is the single highest-return database skill for an application developer.

``` text
  what you want to see              what should worry you
  --------------------------------------------------------------------------
  Index Seek                        Table Scan / Clustered Index Scan on a big table
  Nested Loops on small inputs      Key Lookup executed thousands of times
  Hash Match on big joins           Sort operators you did not ask for
  estimated ~= actual rows          estimated 1 row, actual 900,000  (stale stats)
  no warnings                       implicit conversion / missing index warnings
                                    spills to tempdb  (memory grant too small)
```

``` sql
-- SQL Server
SET STATISTICS IO, TIME ON;
SELECT ... ;                       -- then read the actual execution plan

-- PostgreSQL
EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) SELECT ... ;
```

##### Parameter sniffing

The engine caches a plan built for the first parameter value it saw. If that value was atypical — one tenant with ten rows, when most have ten million — every subsequent execution uses a plan tuned for the wrong shape. Symptoms: a query that is fast for months and then suddenly slow with no code change. Mitigations include `OPTIMIZE FOR UNKNOWN`, `RECOMPILE` on genuinely skewed queries, and keeping statistics fresh.

> **\[Trap\]**
>
> **Common trap.** EF Core parameterises everything, which is good for security and plan reuse but means an `IN` clause with a varying number of items generates a new plan per count. EF 8+ translates these to a single parameterised construct on supported providers — check what your version emits.

###### Getting from a slow EF Core call to the plan that explains it

``` csharp
// step 1: see the SQL. In development, always.
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(cs)
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging()      // shows PARAMETER VALUES. Dev only.
    .EnableDetailedErrors());

// step 2: get the SQL for one query without executing it
var sql = db.Orders.Where(o => o.TenantId == t).ToQueryString();

// step 3: take that SQL to the database and explain it
// EXPLAIN (ANALYZE, BUFFERS) <paste the SQL with real parameter values>;
```

``` text
  reading the result - the ONE thing that matters

    Nested Loop  (cost=0.85..412.30 rows=12 width=48)
                 (actual time=0.031..8241.882 rows=1284001 loops=1)
                                            ^^^^^^^^^^^^^^^
    estimated 12 rows. Got 1,284,001.

  that gap IS the bug. The optimiser chose a nested loop because 12
  iterations is nothing; at 1.28 million it is a disaster. The ALGORITHM
  was reasonable for the ESTIMATE. The estimate was wrong.

  so the method is always:
    1  find the LOWEST node where estimated and actual diverge sharply
    2  ask why that node is misestimated
         stale statistics? correlated columns? a function on a column?
         a parameter against a skewed distribution?
    3  fix the ESTIMATE first - ANALYZE, extended statistics, a rewrite
    4  only then reach for an index, and only then for a hint
```

| Plan feature                          | Means                                | EF Core cause                                                     |
|---------------------------------------|--------------------------------------|-------------------------------------------------------------------|
| `Seq Scan` on a large table           | Reading everything                   | A predicate the translator could not push down                    |
| Large `Rows Removed by Filter`        | Reading rows to discard them         | Filtering after loading, or a missing index                       |
| `loops=` in the thousands             | A nested loop over a big outer input | Often an N+1 that EF Core did not batch                           |
| `Sort ... Disk`                       | The sort exceeded memory             | `OrderBy` on an unindexed column over many rows                   |
| A `CONVERT_IMPLICIT` in the predicate | Type mismatch, index unusable        | A `string` parameter mapped `nvarchar` against a `varchar` column |
| Many identical plans                  | N+1                                  | Lazy loading, or a `Select` touching a navigation                 |

``` csharp
// the implicit-conversion trap, EF Core edition. This is invisible in C#.
public class Customer { public string Code { get; set; } }   // varchar(20) in the DB

db.Customers.Where(c => c.Code == code);
// EF Core parameterises `code` as nvarchar by default -> SQL Server converts
// the COLUMN, not the parameter -> index scan over every row.

// fix: tell EF Core the store type
modelBuilder.Entity<Customer>().Property(c => c.Code)
    .HasColumnType("varchar(20)");
// the plan goes from a scan of 40 million rows to a seek. Nothing in the
// C# changed, and nothing in the C# hinted at the problem.
```

> **\[Note\]**
>
> **Do this once per quarter, not once per incident.** Turn on SQL logging in development, exercise your five busiest endpoints, and read every statement EF Core produced. You will reliably find one N+1, one query selecting forty columns to use three, and one predicate that could not be translated. Twenty minutes, and it consistently finds more than a profiler pointed at production.

### Storage Internals

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Storage Internals</p>

<p class="pd">What the engine is actually doing with bytes on disk. Pages, B-trees, the buffer pool and the write-ahead log explain nearly every performance characteristic above them.</p>

<a id="be-d419"></a>

#### Pages: the unit of everything

Databases do not read rows. They read **pages** — fixed-size blocks, typically 8 KB — and everything about performance follows from that. Reading one row costs a page read. Reading twenty rows that happen to share a page also costs one page read. That single fact explains clustering, covering indexes, and why row size matters.

``` text
  an 8 KB heap page

  +--------------------------------------------------------------+
  | page header   ~24-96 B : page id, LSN, checksum, free space   |
  +--------------------------------------------------------------+
  | slot array (line pointers), grows DOWNWARD ->                 |
  | [ptr1][ptr2][ptr3][ptr4]...                                   |
  +--------------------------------------------------------------+
  |                                                               |
  |                    FREE SPACE                                 |
  |                                                               |
  +--------------------------------------------------------------+
  |          ...row4  row3  row2  row1   <- grows UPWARD          |
  +--------------------------------------------------------------+

  the indirection through the slot array is what lets a row move within
  a page (after an update) without invalidating any index pointing at it.
```

| Engine         | Page size    | Configurable           |
|----------------|--------------|------------------------|
| PostgreSQL     | 8 KB         | Compile-time only      |
| SQL Server     | 8 KB         | No                     |
| MySQL / InnoDB | 16 KB        | Yes, at initialisation |
| Oracle         | 8 KB default | Per tablespace         |

##### Why row width matters more than people expect

``` text
  8 KB page, ~8060 usable bytes

  row width 80 bytes    ->  ~100 rows per page  ->  1M rows = 10,000 pages =  80 MB
  row width 400 bytes   ->   ~20 rows per page  ->  1M rows = 50,000 pages = 400 MB
  row width 2000 bytes  ->    ~4 rows per page  ->  1M rows = 250,000 pages = 2 GB

  a full scan of the third table reads 25x more pages than the first.
  the CPU work is identical; the I/O is not.

  and the buffer pool holds 25x fewer of those rows in the same memory,
  so the cache hit rate collapses too.
```

This is the mechanical reason behind advice that otherwise sounds like fussiness: choose narrower types, avoid `SELECT *`, move large rarely-read columns to a side table, and prefer `int` to `bigint` when the range allows.

<a id="be-d420"></a>

#### Row storage, overflow and TOAST

A row must fit on a page. When it does not, the engine stores part of it elsewhere and leaves a pointer — which means a query touching that column pays an extra read.

| Engine     | Mechanism                                   | Trigger                             |
|------------|---------------------------------------------|-------------------------------------|
| PostgreSQL | TOAST — compress, then move to a side table | Row exceeds ~2 KB                   |
| SQL Server | Row-overflow and LOB pages                  | Row exceeds 8060 bytes; `MAX` types |
| InnoDB     | Overflow pages                              | Depends on row format               |

``` sql
-- PostgreSQL: control TOAST behaviour per column
ALTER TABLE documents ALTER COLUMN body SET STORAGE EXTERNAL;  -- store out of line, no compression
ALTER TABLE documents ALTER COLUMN body SET STORAGE MAIN;      -- compress, keep inline if possible

-- inspect what is happening
SELECT pg_size_pretty(pg_relation_size('documents'))       AS main_table,
       pg_size_pretty(pg_total_relation_size('documents')) AS including_toast_and_indexes;
```

##### The practical consequence: split wide tables

``` sql
-- one wide table: every query pays for the blob's page pointer chasing
CREATE TABLE documents (id bigint PRIMARY KEY, title text, author_id bigint,
                        created_at timestamptz, body text);   -- body averages 400 KB

-- split: the hot table is narrow and fully cacheable
CREATE TABLE documents      (id bigint PRIMARY KEY, title text, author_id bigint,
                             created_at timestamptz);
CREATE TABLE document_bodies(document_id bigint PRIMARY KEY REFERENCES documents(id),
                             body text);
-- listing 50 documents now reads a handful of narrow pages instead of
-- touching the TOAST table 50 times.
```

> **\[Note\]**
>
> **A useful rule.** If a column is large and is read in fewer than a fifth of the queries that touch the table, it belongs in a side table with a one-to-one relationship. This is one of the few times splitting a table on column count is genuinely right.

<a id="be-d421"></a>

#### Heaps, clustered indexes and row locators

How the table itself is physically arranged is an engine-level choice with large consequences, and the two major engines differ.

**Heap (PostgreSQL; SQL Server without a clustered index)**

- Rows in no particular order, appended where there is space
- Row locator is a physical address (`ctid` / RID)
- Inserts are cheap — no position to maintain
- Every index lookup needs a second read to fetch the row

**Clustered / index-organised (SQL Server default; InnoDB always)**

- The table *is* a B-tree, ordered by the clustering key
- Row locator is the clustering key itself
- Range scans on the key are sequential and fast
- Inserts in the middle cause page splits

``` text
  SQL Server / InnoDB: a secondary index stores the CLUSTERING KEY, not an address

    secondary index on (email)          clustered index on (id)
      'a@x.com' -> id 4471        --->    id 4471 -> the full row
      'b@y.com' -> id 992                 id 4472 -> the full row

    so every secondary lookup is TWO tree descents.
    and a wide clustering key makes EVERY secondary index wider.
    -> this is the strongest argument for a narrow clustering key.

  PostgreSQL: an index entry points at a physical tuple location (ctid)

    index on (email) -> ctid (page 42, slot 3) -> the row, one read
    but the row MOVES on update, so old index entries must be maintained,
    which is the origin of HOT updates and of index bloat.
```

| Consequence            | Clustered                     | Heap                            |
|------------------------|-------------------------------|---------------------------------|
| Range scan on the key  | Excellent — sequential        | Requires an index plus lookups  |
| Secondary index lookup | Two tree descents             | One descent plus a direct fetch |
| Random-key inserts     | Page splits and fragmentation | Appends; no split               |
| Updating the key       | Physically moves the row      | N/A                             |
| Wide key               | Bloats every secondary index  | No effect                       |

The practical rules follow directly. On SQL Server and InnoDB: cluster on something narrow, ever-increasing and immutable — which is exactly why a random GUID clustering key is a well-known mistake and a sequential identity or UUIDv7 is not. On PostgreSQL there is no clustering key, so the equivalent concern is physical *correlation*, which you influence with `CLUSTER` or with insertion order and which the planner tracks as a statistic.

<a id="be-d422"></a>

#### B+ trees: the workhorse

Almost every index you will ever create is a B+ tree. Understanding its shape explains index depth, why lookups cost so few reads, and why some operations are cheap and others are not.

``` text
                       [ root: 50 | 100 ]                    <- 1 page, always cached
                      /       |          \
        [ 10 | 30 ]      [ 60 | 80 ]      [ 120 | 150 ]      <- internal pages
        /    |    \        ...                ...
  [1..9] [10..29] [30..49]  ...                              <- LEAF pages
     |       |        |
     +-------+--------+---------------------------------->   leaves are LINKED
                                                              (range scans are sequential)

  KEY PROPERTIES
    - all data is in the leaves; internal pages hold only separator keys
    - the tree is perfectly BALANCED: every leaf is at the same depth
    - depth grows logarithmically, and the fanout is huge
```

##### Why lookups cost so little

``` text
  8 KB page, 16-byte key + 8-byte pointer -> ~340 entries per internal page

  depth 1        340 leaves
  depth 2    ~115,000
  depth 3  ~39,000,000
  depth 4  ~13,000,000,000

  a 40-million-row table has an index depth of about 4.
  the root and most internal pages are permanently in the buffer pool,
  so a point lookup is typically ONE physical read, sometimes zero.

  this is why "add an index" changes a query from seconds to milliseconds:
  it is not a small constant factor, it is O(n) -> O(log n) with a base of ~340.
```

| Operation                  | Cost                            | Why                                              |
|----------------------------|---------------------------------|--------------------------------------------------|
| Point lookup               | O(log n), ~1 physical read      | Descend the tree                                 |
| Range scan                 | O(log n + k)                    | Descend once, then follow the leaf chain         |
| Ordered retrieval          | Free                            | Leaves are already in key order — no sort needed |
| `MIN` / `MAX`              | O(log n)                        | Leftmost or rightmost leaf                       |
| Insert                     | O(log n), plus a possible split | May cascade upward                               |
| Prefix match `LIKE 'abc%'` | O(log n + k)                    | A range scan                                     |
| Suffix match `LIKE '%abc'` | **O(n)**                        | No usable prefix — the index cannot help         |

###### Splits, fill factor and fragmentation

``` text
  inserting into a FULL leaf page

    before   [ 10 | 20 | 30 | 40 ]   <- full
    insert 25
    after    [ 10 | 20 | 25 ]  <->  [ 30 | 40 ]      the page SPLITS in two
                                                     both now ~50% full
                                                     a new separator goes to the parent
                                                     which may split in turn

  sequential keys        always insert at the rightmost leaf -> no splits,
                         pages ~100% full, compact index
  random keys (UUIDv4)   split all over the tree -> pages ~50-70% full,
                         index up to 2x larger, poor cache locality
```

``` sql
-- fill factor: leave room in leaf pages so future inserts do not split
CREATE INDEX ix_orders_customer ON orders (customer_id) WITH (FILLFACTOR = 80);
-- 80 = leave 20% free. Good for randomly-inserted keys.
-- 100 (the default for many engines) is right for append-only, read-mostly indexes.

-- measure fragmentation before acting on it
SELECT * FROM pgstatindex('ix_orders_customer');            -- PostgreSQL, pgstattuple
SELECT avg_fragmentation_in_percent, page_count             -- SQL Server
FROM   sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED')
WHERE  avg_fragmentation_in_percent > 10;
```

Fragmentation matters much less on SSDs than the folklore suggests — random reads are cheap now. What still matters is *page density*: a half-full index occupies twice the pages, so it consumes twice the buffer pool and takes twice as long to scan. Rebuild for density, not for the fragmentation percentage.

<a id="be-d423"></a>

#### LSM trees and write-optimised storage

The main alternative to a B-tree. Where a B-tree updates pages in place, a log-structured merge tree only ever appends, and reconciles later. It is the storage engine behind RocksDB, Cassandra, and increasingly behind write-heavy relational deployments.

``` text
  writes                                    reads

  memtable (in memory, sorted)              check memtable
      | flush when full                     then L0 files, newest first
      v                                     then L1, L2, ...   (a read may touch many)
  L0  [sst][sst][sst]                       bloom filters skip files that
      | compaction merges + discards        certainly do not contain the key
      v   overwritten and deleted keys
  L1  [ sst ][ sst ]
      |
  L2  [    sst    ]      larger, fewer, non-overlapping
```

**B-tree**

- Update in place; read-optimised
- Read amplification ~1 (one tree descent)
- Write amplification from page splits and full-page writes
- Space: pages are partly empty
- Predictable latency

**LSM tree**

- Append-only; write-optimised
- Read amplification: may check several levels
- Write amplification from compaction, but sequential I/O
- Space: better compression, plus obsolete data until compacted
- Latency spikes during compaction

| Workload                                      | Prefer                                                       |
|-----------------------------------------------|--------------------------------------------------------------|
| Read-heavy, point lookups, low latency        | B-tree                                                       |
| Write-heavy ingest, time-series, logs         | LSM                                                          |
| Mixed OLTP                                    | B-tree — which is why every major relational engine uses one |
| Very large datasets where compression matters | LSM                                                          |

You will meet LSM in the relational world through MyRocks, TiDB, CockroachDB and YugabyteDB, and through PostgreSQL extensions. The reason to know it is that the operational profile is genuinely different: compaction is a background process that consumes I/O and can cause latency spikes, and disk usage temporarily exceeds the logical data size.

<a id="be-d424"></a>

#### Row stores and column stores

The same data, laid out two ways, for two entirely different access patterns.

``` text
  ROW STORE                        COLUMN STORE

  page 1: [1,Ann,NL,100]           page 1: [1,2,3,4,5,...]           (id)
          [2,Bob,BE,250]           page 2: [Ann,Bob,Cid,...]         (name)
          [3,Cid,NL, 75]           page 3: [NL,BE,NL,...]            (country)
                                   page 4: [100,250,75,...]          (amount)

  "give me order 2"                "sum(amount) for 500M rows"
  -> 1 page read                   -> read ONLY the amount column
                                   -> compresses ~10x (similar values adjacent)
                                   -> vectorised: process 1000 values per instruction
  the whole row is together        the whole COLUMN is together
```

|                           | Row store                            | Column store                              |
|---------------------------|--------------------------------------|-------------------------------------------|
| Optimised for             | OLTP: point reads, single-row writes | OLAP: scans, aggregates over few columns  |
| A single-row insert       | One page write                       | Touches every column segment — expensive  |
| `SELECT *` for one row    | Fast                                 | Slow — reassembles from every column      |
| `SUM` over a billion rows | Reads every byte of every row        | Reads one column, compressed              |
| Compression               | Modest                               | Excellent — run-length, dictionary, delta |

``` sql
-- SQL Server: add a columnstore index for analytics on the same table
CREATE NONCLUSTERED COLUMNSTORE INDEX cci_orders
    ON orders (placed_at, country, product_id, total);
-- OLTP queries keep using the rowstore B-trees; the optimiser picks the
-- columnstore for large aggregations. One table, both access patterns.

-- PostgreSQL: columnar via extensions (Citus columnar, Hydra), or push
-- analytics to a separate warehouse and keep OLTP purely row-based.
```

> **\[Note\]**
>
> **The architectural point.** This is the mechanical reason OLTP and OLAP separate. Attempting both on one row-store table means large aggregations scan the transactional working set out of the buffer pool, and every OLTP query slows down. The classic answer is a replica or a warehouse; a hybrid columnstore index is the modern middle ground when the data volume is moderate.

<a id="be-d425"></a>

#### The buffer pool

The database keeps recently-used pages in memory. Almost all read performance is really a question about the hit rate of this cache, and almost all memory tuning is about its size.

``` text
  a read request

  1. is the page in the buffer pool?
       yes -> LOGICAL read.  ~100 ns.  no I/O at all.
       no  -> PHYSICAL read. ~100 us on SSD, ~10 ms on spinning disk.
              evict a victim page (clock-sweep / LRU-K),
              writing it out first if it is DIRTY.

  a write request
  1. modify the page IN MEMORY, mark it dirty
  2. write the change to the WAL and flush THAT to disk       <- durability point
  3. the dirty data page is written later, at a checkpoint    <- lazily, in bulk

  which is why writes are fast: only the sequential log is fsynced on commit,
  not the scattered data pages.
```

``` sql
-- PostgreSQL
shared_buffers = 8GB          -- ~25% of RAM; the OS page cache holds the rest
effective_cache_size = 24GB   -- a HINT to the planner about total cache, not an allocation
work_mem = 64MB               -- PER SORT/HASH OPERATION, per connection - multiply carefully
maintenance_work_mem = 2GB    -- for VACUUM and index builds

-- SQL Server: 'max server memory' - leave several GB for the OS

-- measure the hit ratio rather than guessing
SELECT sum(heap_blks_hit) * 100.0 / NULLIF(sum(heap_blks_hit + heap_blks_read), 0)
FROM pg_statio_user_tables;                      -- want > 99% for an OLTP workload
```

> **\[Trap\]**
>
> **Common trap.** `work_mem` is allocated per sort or hash node, per connection — not globally. A 64 MB setting with 200 connections each running a query with three hash joins can attempt to allocate 38 GB. Size it against your realistic concurrency, and raise it per session for known-heavy reporting queries instead of globally.

###### Reading cache behaviour from the numbers

| Symptom                                      | Likely cause                                                        | Action                                                          |
|----------------------------------------------|---------------------------------------------------------------------|-----------------------------------------------------------------|
| Hit ratio below ~95% on OLTP                 | Working set exceeds the pool                                        | More memory, or narrow the rows and indexes                     |
| Ratio drops at a fixed time daily            | A report or batch job scanning large tables and evicting everything | Move it to a replica; or check for a ring-buffer scan strategy  |
| First query after restart is slow, then fine | Cold cache                                                          | Normal; pre-warm if cold starts matter                          |
| Sorts spilling to disk                       | `work_mem` too small for the plan                                   | Raise it for that session, or add an index that avoids the sort |
| Steady I/O with a small dataset              | Sequential scans caused by missing indexes                          | Fix the plans, not the memory                                   |

Large sequential scans are usually protected against by a *ring buffer* strategy: the engine confines a big scan to a small portion of the pool so it cannot evict the entire working set. It helps, and it does not fully prevent a nightly analytics job from degrading morning OLTP latency — which is the real argument for physically separating the two workloads.

<a id="be-d426"></a>

#### The write-ahead log and durability

The central trick that makes databases both fast and crash-safe: **write the intention to a sequential log and flush that, before modifying anything else.** Data pages can then be written lazily, in bulk, in any order.

``` text
  COMMIT sequence

  1. modify the page in the buffer pool (fast, in memory)
  2. append a log record describing the change to the WAL buffer
  3. on COMMIT: fsync the WAL up to this transaction's LSN     <- the ONLY sync required
  4. tell the client "committed"
  5. ...minutes later, a checkpoint writes the dirty data pages out

  WAL rule (write-ahead logging):
     a data page may NEVER reach disk before the log record describing its change.

  CRASH RECOVERY
    REDO   replay every log record after the last checkpoint
             -> committed changes that had not reached the data files are reapplied
    UNDO   roll back transactions that were in flight at the crash
```

The performance consequence is large: one sequential fsync per commit, rather than a scattered random write per modified page. This is also why WAL storage should be fast and, ideally, on its own device — commit latency is bounded by how quickly it can be flushed.

``` text
-- PostgreSQL: the durability/performance dial
synchronous_commit = on        -- default: wait for the WAL fsync. Do not change lightly.
synchronous_commit = off       -- commit returns BEFORE the fsync.
                               -- Faster. On a crash you lose up to wal_writer_delay
                               -- of COMMITTED transactions. The database stays CONSISTENT,
                               -- it just forgets recent commits. Sometimes acceptable
                               -- for logging or bulk load; never for money.

wal_compression = on
max_wal_size = 4GB             -- larger = fewer checkpoints, longer recovery
checkpoint_timeout = 15min
checkpoint_completion_target = 0.9   -- spread checkpoint I/O to avoid write storms
```

###### Checkpoints, group commit and what the log is also for

A **checkpoint** flushes all dirty pages and records a point from which recovery can start. It trades steady-state I/O against recovery time: frequent checkpoints mean constant background writing and fast recovery; infrequent ones mean bursty I/O and a longer restart.

``` sql
-- watch for checkpoints being forced by volume rather than by time - a sign
-- max_wal_size is too small and you are paying for write storms
SELECT checkpoints_timed, checkpoints_req, buffers_checkpoint, buffers_backend
FROM   pg_stat_bgwriter;
-- checkpoints_req climbing relative to checkpoints_timed = raise max_wal_size
```

**Group commit** is the other reason throughput holds up: concurrent commits are batched into a single fsync. Fifty sessions committing simultaneously cost roughly one flush, not fifty — which is why commit throughput scales far better with concurrency than a naive model predicts.

| The WAL also enables      | How                                                              |
|---------------------------|------------------------------------------------------------------|
| Point-in-time recovery    | Restore a base backup, then replay the log to any chosen instant |
| Physical replication      | Ship WAL records to a standby and replay them there              |
| Logical replication / CDC | Decode the log into row-level change events                      |
| Crash recovery            | Its original purpose                                             |

> **\[Trap\]**
>
> **Common trap.** On PostgreSQL, an unused or stalled replication slot prevents WAL from being recycled. The log directory grows without bound until the disk fills and the database stops. Monitor `pg_replication_slots` for slots with a growing lag and drop the ones nobody is consuming — this is one of the most common ways a healthy PostgreSQL instance goes down.

### Indexing

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Indexing</p>

<p class="pd">The highest-leverage tuning tool there is, and the one most often applied by guesswork. What indexes are, how the optimiser chooses to use them, and a method for designing them deliberately.</p>

<a id="be-d427"></a>

#### What an index is, and what it costs

An index is a second, sorted copy of some columns, with a pointer back to the row. That is the whole idea, and every property follows from it: sorted means fast lookup and free ordering; second copy means storage and write cost.

``` text
  table (heap, unordered)              index on (email)

  page 1  [Bob   bob@y.com  ]          leaf: ann@x.com  -> (page 2, slot 1)
          [Dan   dan@z.com  ]                bob@y.com  -> (page 1, slot 1)
  page 2  [Ann   ann@x.com  ]                cid@w.com  -> (page 2, slot 2)
          [Cid   cid@w.com  ]                dan@z.com  -> (page 1, slot 2)

  finding 'cid@w.com':
    without the index   scan every page      O(n)
    with the index      descend the B-tree   O(log n), then one fetch
```

| Index gives you                         | Index costs you                                                          |
|-----------------------------------------|--------------------------------------------------------------------------|
| Point lookups in ~1 read                | Storage — often 10–30% of the table per index                            |
| Range scans without a full scan         | A write on every `INSERT`/`DELETE`, and on `UPDATE` of an indexed column |
| Free ordering (`ORDER BY` with no sort) | Buffer pool space competing with data pages                              |
| Cheap `MIN`/`MAX`                       | Maintenance: statistics, bloat, rebuilds                                 |
| Uniqueness enforcement                  | More plan choices, so more chances to choose badly                       |
| Join efficiency                         | Longer index-build time on restore and migration                         |

The write cost is the one that is consistently underestimated. Ten indexes on a table mean an insert performs eleven writes, each potentially causing a page split, each generating log. A table with many indexes and a high insert rate can spend the large majority of its write time on index maintenance.

> **\[Why it matters\]** This is why “just add an index” is not free advice, and why quarterly review of unused indexes is one of the few tuning activities that is both zero-risk and immediately measurable.

<a id="be-d428"></a>

#### Composite indexes and the leftmost prefix rule

A composite index is sorted by its first column, then within that by its second, and so on — like a phone book sorted by surname then forename. That ordering determines exactly which queries it can serve.

``` text
  index on (tenant_id, status, placed_at)

  sorted as:  (1,'Open',   Jan)  (1,'Open',   Feb)  (1,'Shipped',Jan)
              (2,'Open',   Jan)  (2,'Shipped',Mar)  ...

  WHERE tenant_id = 1                                    SEEK
  WHERE tenant_id = 1 AND status = 'Open'                SEEK
  WHERE tenant_id = 1 AND status = 'Open' AND placed_at > X   SEEK - full use
  WHERE tenant_id = 1 AND placed_at > X                  seek on tenant, then FILTER
                                                         (status is skipped, so the
                                                          placed_at ordering is useless)
  WHERE status = 'Open'                                  NO SEEK - wrong leading column
  WHERE placed_at > X                                    NO SEEK
```

##### Column order: the rule

1.  **Equality predicates first**, in any order among themselves.
2.  **Then the range predicate** — and only one range column can be used efficiently.
3.  **Then the `ORDER BY` columns**, if the sort can be satisfied by the same ordering.
4.  **Then `INCLUDE`** the remaining selected columns, to make it covering.

``` sql
-- for this query
SELECT id, total, status
FROM   orders
WHERE  tenant_id = :t AND status = 'Open' AND placed_at >= :from
ORDER  BY placed_at DESC
LIMIT  50;

-- equality: tenant_id, status.  range + order: placed_at.  select: id, total.
CREATE INDEX ix_orders_open ON orders (tenant_id, status, placed_at DESC)
    INCLUDE (total);                              -- SQL Server / PostgreSQL 11+
-- result: one seek, rows already in the right order (no sort), no heap fetch.
```

###### Why only one range column works

``` text
  index on (a, b), query: WHERE a > 5 AND b > 100

  sorted:  (5,50) (5,200) (6,10) (6,300) (7,20) ...
                          ^^^^^^ a>5 starts here

  from that point the index is sorted by a, THEN b - so b values are
  scattered. The engine must read every entry with a>5 and filter on b.

  the b predicate is a "residual", not a seek predicate.
  reverse the order to (b, a) and the same problem appears for a.

  -> put the MOST SELECTIVE range column last, and accept the filter cost,
     or use a multi-dimensional index type (GiST, or a BRIN for correlated data).
```

| Question                             | Answer                                                                                                                           |
|--------------------------------------|----------------------------------------------------------------------------------------------------------------------------------|
| Do I need one index per column?      | No — usually the opposite. One well-ordered composite beats three single-column indexes                                          |
| Will the engine combine two indexes? | It can (bitmap index scan / index intersection) and it is usually slower than one composite                                      |
| Most selective column first?         | A common rule of thumb, and it is **wrong**. Equality columns come first regardless of selectivity, because they enable the seek |
| Does `(a,b)` make `(a)` redundant?   | Yes — drop the narrower one, unless the width difference genuinely matters                                                       |
| Does `(a,b)` help `WHERE b = ?`      | Only via a full index scan, which is rarely worth it                                                                             |

<a id="be-d429"></a>

#### Covering indexes

If the index contains every column a query needs, the engine never touches the table. That eliminates one read per row, which on a query returning thousands of rows is the difference between fast and unusable.

``` text
  non-covering                              covering

  seek index on (customer_id)               seek index on (customer_id) INCLUDE (total, status)
    -> 5,000 matching entries                 -> 5,000 entries, all data present
    -> 5,000 KEY LOOKUPS into the table       -> DONE
       (each a separate random read)
                                            ~40 page reads instead of ~5,000
```

``` sql
-- INCLUDE columns live only in the LEAF pages: they are payload, not sort key.
-- so they do not widen the internal pages, and do not affect ordering.
CREATE INDEX ix_orders_cust ON orders (customer_id)          -- key: seekable, sorted
    INCLUDE (total, status, placed_at);                      -- payload: retrievable only

-- vs putting them in the key, which widens every level of the tree
CREATE INDEX ix_bad ON orders (customer_id, total, status, placed_at);
```

| Put a column in | When                                         |
|-----------------|----------------------------------------------|
| The **key**     | You filter on it, join on it, or order by it |
| **`INCLUDE`**   | You only select it                           |
| **Neither**     | It is rarely selected, or it is wide         |

> **\[Trap\]**
>
> **Common trap.** Covering everything is a trap of its own. An index that includes six wide columns approaches the size of the table, doubles the write cost, and evicts real data from the buffer pool. Cover the columns needed by a *specific hot query*, not every column the table happens to have. If you find yourself including most of the table, what you actually want is a different clustering key or a summary table.

<a id="be-d430"></a>

#### Partial and expression indexes

Two features that dramatically reduce index size and are consistently under-used.

##### Partial (filtered) indexes

``` sql
-- a table of 200 million orders, of which 40,000 are open at any time.
-- a full index on status is 200M entries; this one is 40,000.
CREATE INDEX ix_orders_open ON orders (tenant_id, placed_at)
    WHERE status = 'Open';                        -- PostgreSQL
CREATE INDEX ix_orders_open ON orders (tenant_id, placed_at)
    WHERE status = 'Open';                        -- SQL Server: "filtered index"

-- the optimiser uses it only when it can PROVE the query implies the predicate:
SELECT ... WHERE status = 'Open' AND tenant_id = :t;         -- uses it
SELECT ... WHERE status = :s     AND tenant_id = :t;         -- does NOT (parameter)
```

| Excellent for                      | Example                                                     |
|------------------------------------|-------------------------------------------------------------|
| A small hot subset of a huge table | `WHERE status = 'Pending'`                                  |
| Soft-delete filtering              | `WHERE deleted_at IS NULL`                                  |
| Uniqueness among active rows only  | A unique partial index — lets a deleted email be reused     |
| Avoiding indexing nulls            | `WHERE external_ref IS NOT NULL`                            |
| Outbox and queue tables            | `WHERE processed_at IS NULL` — the index stays tiny forever |

##### Expression (computed) indexes

``` sql
-- an index on a function result, so the function becomes sargable
CREATE INDEX ix_customers_email_lower ON customers (lower(email));
SELECT * FROM customers WHERE lower(email) = lower(:input);   -- SEEK

-- without it, lower(email) = ... cannot use an index on email at all.

-- SQL Server: a persisted computed column plus an index on it
ALTER TABLE customers ADD email_lower AS lower(email) PERSISTED;
CREATE INDEX ix_customers_email_lower ON customers (email_lower);

-- indexing inside JSON
CREATE INDEX ix_orders_channel ON orders ((metadata->>'channel'));
```

> **\[Note\]**
>
> **The pairing that solves most ‘why is this not using my index’ questions.** An index is used only when the query's expression matches the index's expression *exactly*. `lower(email)` in the index and `UPPER(email)` in the query do not match. Neither does an index on `email` with a query on `lower(email)`.

<a id="be-d431"></a>

#### Specialised index types

The B-tree handles equality and ranges over ordered types. Everything else has a purpose-built structure, and using the wrong one is why some queries cannot be made fast with a normal index.

| Type          | Structure                          | Good for                                                    | Cannot do                            |
|---------------|------------------------------------|-------------------------------------------------------------|--------------------------------------|
| **B-tree**    | Balanced tree                      | Equality, ranges, ordering, prefix `LIKE`                   | Contains-search, multi-value columns |
| **Hash**      | Hash table                         | Equality only, slightly faster and smaller                  | Ranges, ordering                     |
| **GIN**       | Inverted index                     | Arrays, JSONB containment, full-text                        | Ranges; writes are slow              |
| **GiST**      | Generalised search tree            | Geometry, ranges, nearest-neighbour, exclusion constraints  | Exact equality is slower than B-tree |
| **BRIN**      | Min/max per block range            | Huge tables where the column correlates with physical order | Anything unsorted                    |
| **Bitmap**    | Bit vector per value               | Low-cardinality columns in warehouses                       | High concurrency writes              |
| **Full-text** | Inverted, with linguistic analysis | Ranked text search, stemming, stop words                    | Exact substring                      |
| **Spatial**   | R-tree / quadtree                  | Geographic containment, distance                            | Non-spatial predicates               |

``` sql
-- BRIN: the highest value-per-byte index that exists, for the right data
-- a 500 GB append-only events table, physically ordered by time
CREATE INDEX ix_events_time_brin ON events USING brin (occurred_at)
    WITH (pages_per_range = 128);
-- index size: a few MEGABYTES, versus tens of gigabytes for a B-tree.
-- it stores only the min and max of occurred_at per 128-page block,
-- so a time-range query skips every block that cannot contain a match.
-- requires physical correlation - it is useless on a randomly-ordered column.

-- GIN for containment and arrays
CREATE INDEX ix_orders_meta ON orders USING gin (metadata jsonb_path_ops);
SELECT * FROM orders WHERE metadata @> '{"channel":"web"}';

CREATE INDEX ix_articles_tags ON articles USING gin (tags);   -- text[]
SELECT * FROM articles WHERE tags @> ARRAY['postgres'];

-- GiST for exclusion: no two bookings may overlap for the same room
ALTER TABLE bookings ADD EXCLUDE USING gist (room_id WITH =, period WITH &&);
```

###### Full-text search inside the database

``` sql
-- PostgreSQL: a stored tsvector plus a GIN index
ALTER TABLE articles ADD search_vector tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('english', coalesce(title, '')),  'A') ||
        setweight(to_tsvector('english', coalesce(body,  '')),  'B')
    ) STORED;
CREATE INDEX ix_articles_fts ON articles USING gin (search_vector);

SELECT id, title, ts_rank(search_vector, q) AS rank
FROM   articles, websearch_to_tsquery('english', :input) q
WHERE  search_vector @@ q
ORDER  BY rank DESC LIMIT 20;

-- fuzzy matching for typos, via trigrams
CREATE EXTENSION pg_trgm;
CREATE INDEX ix_products_name_trgm ON products USING gin (name gin_trgm_ops);
SELECT * FROM products WHERE name % 'labtop';        -- similarity match
SELECT * FROM products WHERE name ILIKE '%labtop%';  -- and this now uses the index too
```

**Database full-text is enough when**

- The corpus is moderate (millions, not billions)
- You want one system, transactionally consistent
- Ranking needs are simple
- Operational simplicity matters more than relevance tuning

**Use a dedicated search engine when**

- Relevance tuning is a product feature
- You need facets, synonyms, aggregations, autocomplete
- The corpus is very large or updates constantly
- Search traffic would otherwise compete with OLTP

The `pg_trgm` point deserves emphasis, because it solves the single most common “this cannot be indexed” problem: a trigram index makes `LIKE '%substring%'` fast. That predicate is normally a guaranteed full scan.

<a id="be-d432"></a>

#### Index maintenance and statistics

Indexes degrade. Two distinct things degrade — the physical structure, and the statistics the optimiser relies on — and confusing them leads to rebuilding indexes when the real problem is stale statistics.

| Problem                  | Symptom                                         | Fix                                       | Frequency                              |
|--------------------------|-------------------------------------------------|-------------------------------------------|----------------------------------------|
| Stale statistics         | Estimated rows wildly wrong; suddenly bad plans | `ANALYZE` / `UPDATE STATISTICS`           | Automatic, but verify after bulk loads |
| Bloat / low page density | Index far larger than the data justifies        | `REINDEX CONCURRENTLY` / `REBUILD ONLINE` | Rarely; when density drops below ~60%  |
| Fragmentation            | Logical order ≠ physical order                  | Reorganise, or ignore on SSD              | Much less important than folklore says |
| Dead tuples              | Table grows though rows are deleted             | `VACUUM` (PostgreSQL)                     | Autovacuum; tune it, do not disable it |
| Unused indexes           | Write cost with no read benefit                 | Drop them                                 | Review quarterly                       |

``` sql
-- find indexes nobody uses: pure cost, zero benefit
SELECT s.relname AS table, s.indexrelname AS index,
       s.idx_scan AS times_used,
       pg_size_pretty(pg_relation_size(s.indexrelid)) AS size
FROM   pg_stat_user_indexes s
JOIN   pg_index i ON i.indexrelid = s.indexrelid
WHERE  s.idx_scan = 0 AND NOT i.indisunique
ORDER  BY pg_relation_size(s.indexrelid) DESC;

-- SQL Server: high writes, no reads
SELECT OBJECT_NAME(s.object_id) AS table_name, i.name,
       s.user_seeks + s.user_scans + s.user_lookups AS reads, s.user_updates AS writes
FROM   sys.dm_db_index_usage_stats s
JOIN   sys.indexes i ON i.object_id = s.object_id AND i.index_id = s.index_id
WHERE  s.database_id = DB_ID() AND s.user_seeks + s.user_scans + s.user_lookups = 0
ORDER  BY s.user_updates DESC;
```

``` sql
-- rebuild WITHOUT taking an exclusive lock
REINDEX INDEX CONCURRENTLY ix_orders_customer;              -- PostgreSQL 12+
ALTER INDEX ix_orders_customer ON orders REBUILD WITH (ONLINE = ON);   -- SQL Server Ent.
CREATE INDEX CONCURRENTLY ...;   -- and always use CONCURRENTLY to create on a live table
```

> **\[Trap\]**
>
> **Common trap.** `CREATE INDEX` without `CONCURRENTLY` takes a lock that blocks writes for the entire build — minutes to hours on a large table. Because it queues ahead of subsequent readers in PostgreSQL, one careless index build during business hours can stall the whole application. The concurrent variant is slower and does not block; always use it in production.

<a id="be-d433"></a>

#### When indexes are not used

An index exists, the column is in the `WHERE` clause, and the plan shows a full scan anyway. There are seven common reasons, and they are all diagnosable.

| Reason                   | Example                                          | Fix                                          |
|--------------------------|--------------------------------------------------|----------------------------------------------|
| **Not sargable**         | `WHERE year(placed_at) = 2026`                   | Rewrite as a range, or index the expression  |
| **Implicit conversion**  | `WHERE varchar_col = 123`                        | Match the parameter type to the column type  |
| **Leading wildcard**     | `LIKE '%abc'`                                    | Trigram index, or full-text                  |
| **Low selectivity**      | Predicate matches 40% of rows                    | Correct behaviour — a scan really is cheaper |
| **Wrong leading column** | Index `(a,b)`, query on `b`                      | Reorder, or add an index                     |
| **Stale statistics**     | Optimiser estimates 5 rows, reality is 5 million | `ANALYZE`                                    |
| **`OR` across columns**  | `WHERE a = 1 OR b = 2`                           | `UNION` of two seeks, or a bitmap scan       |

``` text
-- SARGABILITY: the predicate must isolate the column on one side, unmodified

-- not sargable                          sargable
WHERE year(placed_at) = 2026        ->   WHERE placed_at >= '2026-01-01'
                                           AND placed_at <  '2027-01-01'
WHERE total * 1.21 > 100            ->   WHERE total > 100 / 1.21
WHERE lower(email) = :e             ->   index on lower(email), or store it normalised
WHERE coalesce(status,'X') = 'X'    ->   WHERE status IS NULL OR status = 'X'
WHERE id + 0 = :id                  ->   WHERE id = :id
WHERE substring(code,1,3) = 'ABC'   ->   WHERE code LIKE 'ABC%'
WHERE datediff(day, d, now()) < 7   ->   WHERE d > now() - interval '7 days'
```

###### The implicit conversion trap, in full

``` sql
-- a column typed nvarchar, compared against a varchar parameter
SELECT * FROM users WHERE username = 'alice';      -- SQL Server: 'alice' is varchar

-- data type precedence makes nvarchar higher, so SQL Server converts the COLUMN,
-- not the literal:
--    WHERE CONVERT(nvarchar, username) = N'alice'
-- the column is now wrapped in a function -> the index cannot be seeked ->
-- an index SCAN over every row, on a table with a perfectly good index.

-- the plan shows a CONVERT_IMPLICIT in the predicate, and often a warning.
-- fix: parameterise with the matching type. In an ORM, check the mapped type -
-- a string parameter defaulting to nvarchar against a varchar column causes the
-- same problem in the opposite direction.
```

This is worth internalising because it is invisible in the SQL text, silent, and produces a full scan on the busiest table in the system. It is one of the highest-frequency causes of a query that is fast in a test harness and slow from the application, since the harness and the ORM parameterise differently.

<a id="be-d434"></a>

#### A method for designing indexes

Indexes designed by intuition accumulate; indexes designed by method stay small. A workable process:

1.  **Start from the workload, not the schema.** Collect the actual top queries by total time — `pg_stat_statements`, Query Store, or the slow log. Optimise what runs, not what looks slow.
2.  **For each query, write the access pattern down:** equality columns, range column, sort columns, selected columns.
3.  **Design one composite index per pattern** using the ordering rule: equality, then range, then sort, then `INCLUDE`.
4.  **Consolidate.** An index that is a prefix of another is redundant. Several similar patterns often collapse into one index with a well-chosen order.
5.  **Verify with the plan** on production-sized data. A plan on 1,000 rows tells you nothing — everything fits in cache and a scan always wins.
6.  **Measure the write cost** before and after on the insert path.
7.  **Review quarterly** and drop what is unused.

``` sql
-- the workload query that should drive all of this
SELECT calls, total_exec_time, mean_exec_time, rows,
       100.0 * shared_blks_hit / NULLIF(shared_blks_hit + shared_blks_read, 0) AS hit_pct,
       left(query, 120) AS query
FROM   pg_stat_statements
ORDER  BY total_exec_time DESC
LIMIT  25;
-- note: order by TOTAL time, not mean. A 20 ms query run 2 million times an hour
-- costs far more than a 4-second report run twice a day.
```

| Heuristic                                       | Reasoning                                                                                |
|-------------------------------------------------|------------------------------------------------------------------------------------------|
| Index every foreign key                         | Not automatic in most engines; without it, joins scan, and parent deletes scan the child |
| Index columns used in `ORDER BY` + `LIMIT`      | Turns a full sort into a short scan                                                      |
| Aim for under ~5 indexes on a write-heavy table | Not a law, but a prompt to justify the sixth                                             |
| Prefer one composite to several singles         | The optimiser combines indexes poorly compared to seeking one                            |
| Partial-index the hot subset                    | Often reduces the index by orders of magnitude                                           |
| Do not index low-cardinality columns alone      | A boolean index rarely helps; as a partial-index predicate it often does                 |

> **\[Note\]**
>
> **The single most common real-world win.** Not an exotic index type — it is adding the missing composite index for a query that currently seeks on one column and then filters and sorts thousands of rows. Look for plans where the estimated rows at the seek are far larger than the rows returned; the gap is work the index could have eliminated.

### Query Processing & Optimisation

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Query Processing & Optimisation</p>

<p class="pd">How a SQL string becomes a plan, and how to read that plan. This is the layer where most real performance problems are diagnosed, and the vocabulary is worth learning properly.</p>

<a id="be-d435"></a>

#### The lifecycle of a query

``` text
  SQL text
    |
  1 PARSE       syntax check -> a parse tree.  Syntax errors surface here.
    |
  2 BIND        resolve names to real objects, check types and permissions.
    |           "column does not exist" comes from here.
  3 REWRITE     expand views, flatten subqueries, fold constants,
    |           push predicates down, eliminate provably-unnecessary joins.
  4 OPTIMISE    enumerate candidate PHYSICAL plans;
    |           estimate the cost of each using STATISTICS; pick the cheapest.
  5 EXECUTE     pull rows through the chosen operator tree.
    |
  results
```

Step 4 is the interesting one, and the crucial point is that it is **cost-based and estimate-driven**. The optimiser does not know how many rows a predicate will match; it *guesses*, from statistics. Almost every catastrophically bad plan is a bad guess rather than a bad algorithm.

| Stage        | Can it be the problem? | How you would know                                  |
|--------------|------------------------|-----------------------------------------------------|
| Parse        | Rarely                 | A syntax error                                      |
| Bind         | Rarely                 | A name or permission error                          |
| Rewrite      | Occasionally           | A view or subquery blocking predicate pushdown      |
| **Optimise** | **Very often**         | Estimated rows differ wildly from actual rows       |
| Execute      | Sometimes              | The plan is reasonable and the data really is large |

> **\[Why it matters\]** This is why the first question about a slow query is never “how do I make it faster?” but “does the optimiser know how much data it is dealing with?” A plan built for 5 rows and executed against 5 million will choose nested loops and run for an hour.

<a id="be-d436"></a>

#### Statistics and cardinality estimation

The optimiser's entire model of your data is a set of statistics: how many rows, how values are distributed, how many are distinct, and how correlated physical order is with logical order.

| Statistic          | Used for                                           |
|--------------------|----------------------------------------------------|
| Row count          | Baseline cost of any scan                          |
| Histogram          | Selectivity of a range predicate                   |
| Distinct values    | Selectivity of equality, and join estimates        |
| Most common values | Skewed distributions                               |
| Null fraction      | Selectivity of `IS NULL`                           |
| Correlation        | Whether an index scan will be sequential or random |

``` sql
-- see what the optimiser believes
SELECT attname, n_distinct, null_frac, correlation, most_common_vals, most_common_freqs
FROM   pg_stats WHERE tablename = 'orders';

ANALYZE orders;                                              -- refresh
ALTER TABLE orders ALTER COLUMN status SET STATISTICS 500;    -- more histogram detail
```

##### Where estimation goes wrong

| Situation                            | Estimate                               | Reality                                                          |
|--------------------------------------|----------------------------------------|------------------------------------------------------------------|
| Correlated columns                   | Assumes independence: 0.1 × 0.1 = 0.01 | City and postcode are not independent; the true value may be 0.1 |
| A skewed column with a parameter     | The average frequency                  | One value holds 90% of the rows                                  |
| A function on a column               | A hardcoded guess (often 0.5% or 33%)  | Anything                                                         |
| Data loaded since the last `ANALYZE` | The old row count, sometimes zero      | Millions                                                         |
| A temp table used immediately        | No statistics at all                   | Anything                                                         |

``` sql
-- multi-column statistics: teach the optimiser about correlation
CREATE STATISTICS st_orders_geo (dependencies, ndistinct)
    ON city, postcode, country FROM orders;
ANALYZE orders;
-- without it, WHERE city='Amsterdam' AND postcode='1011AB' is estimated as
-- selectivity(city) x selectivity(postcode), which can be off by 1000x.

CREATE STATISTICS st_orders_geo ON orders (city, postcode);   -- SQL Server equivalent
```

###### Diagnosing an estimation failure

``` sql
EXPLAIN (ANALYZE, BUFFERS) SELECT ... ;

  Nested Loop  (cost=0.85..412.30 rows=12 width=48)
               (actual time=0.031..8241.882 rows=1284001 loops=1)
                                            ^^^^^^^^^^^^^^^
  estimated 12 rows, got 1,284,001. That is the whole bug.

  the optimiser chose a nested loop because 12 iterations is nothing.
  at 1.28 million iterations it is a disaster. The ALGORITHM was reasonable
  given the estimate; the ESTIMATE was wrong.
```

1.  Find the operator where estimated and actual rows first diverge sharply. Errors propagate upward, so the *lowest* divergence is the cause.
2.  Ask why that node is misestimated: stale statistics, correlated predicates, a function, or a parameter against a skewed column.
3.  Fix the estimate — `ANALYZE`, extended statistics, or a rewrite the optimiser can reason about.
4.  Only if the estimate cannot be fixed, consider forcing the plan — and record why.

A useful threshold: a tenfold estimation error is worth investigating, a hundredfold error will eventually cause an incident, and a thousandfold error *is* the incident.

<a id="be-d437"></a>

#### Physical access operators

| Operator                    | Does                                                 | Cost profile                                               |
|-----------------------------|------------------------------------------------------|------------------------------------------------------------|
| **Seq Scan** / Table Scan   | Reads every page                                     | Cheap per page, expensive in total                         |
| **Index Scan** / Seek       | Descends the tree, then fetches rows                 | One random read per row — great for few, terrible for many |
| **Index Only Scan**         | Reads the index alone                                | The best case: no table access at all                      |
| **Bitmap Heap Scan**        | Collects addresses, sorts them, reads pages in order | The middle ground: random reads become sequential          |
| **Key Lookup** / Heap Fetch | The second read after a non-covering seek            | What covering indexes eliminate                            |

``` text
  when a SCAN beats a SEEK - and it often does

  1M-row table, 100 rows per page = 10,000 pages

  predicate matches 10 rows
    seek:  ~4 index reads + 10 random row reads = ~14 reads       WINS
    scan:  10,000 sequential reads

  predicate matches 200,000 rows (20%)
    seek:  ~4 + 200,000 random reads = 200,004 reads
    scan:  10,000 sequential reads                                WINS by 20x

  crossover is usually between 1% and 15% of the table.

  -> a full scan in a plan is NOT automatically a bug. It is a bug when the
     predicate is selective and the optimiser thought it was not.
```

The bitmap heap scan is the optimiser's answer to the middle of that range: gather all matching row addresses, sort them into physical order, then read each page once in sequence. It converts 50,000 random reads into a few thousand sequential ones.

<a id="be-d438"></a>

#### Join algorithms

``` text
  NESTED LOOPS                for each row of OUTER, probe INNER
    cost ~ |outer| x probe      GREAT when outer is tiny and inner is indexed
                               CATASTROPHIC when outer is large - the classic bad plan

  HASH JOIN                   build a hash table on the smaller input, probe with the larger
    cost ~ |build| + |probe|   GREAT for large unsorted inputs; EQUALITY only
                               needs memory; spills to disk if the build side is too big

  MERGE JOIN                  both inputs sorted on the key, walked together
    cost ~ |a| + |b| + sorts   GREAT when both are already sorted (index order)
                               supports inequality joins
```

| Algorithm    | Best when                                   | Warning sign in a plan                 |
|--------------|---------------------------------------------|----------------------------------------|
| Nested loops | Outer input under ~1000 rows, inner indexed | A large `loops=` with high actual rows |
| Hash join    | Both inputs large, equality predicate       | `Batches > 1`, or a disk spill         |
| Merge join   | Both inputs already sorted on the key       | An explicit sort feeding it            |

``` sql
-- nested loops gone wrong
--   Nested Loop  (rows=8) (actual rows=940000 loops=1)
--     -> Seq Scan on orders  (rows=8) (actual rows=940000)
--     -> Index Scan on customers  (loops=940000)     <- 940,000 tree descents

ANALYZE orders;                          -- usually the entire fix

SET LOCAL enable_nestloop = off;         -- PostgreSQL, session scope, blunt
SELECT ... OPTION (HASH JOIN);           -- SQL Server hint
```

###### Join order matters more than the algorithm

``` text
  orders (10M), customers (500k), countries (200)
  predicate: countries.region = 'EMEA' -> 40 countries

  BAD order                          GOOD order
    orders JOIN customers              countries filtered to 40
      -> 10M intermediate rows           JOIN customers -> 90k
      JOIN countries -> 1.8M             JOIN orders    -> 1.8M
    peak intermediate: 10M             peak intermediate: 90k

  same result, same algorithms, ~50x the work.

  the optimiser searches this space, and the search is EXPONENTIAL in the
  number of tables - which is why 15-table joins often get heuristic plans.
```

``` sql
SHOW join_collapse_limit;      -- PostgreSQL default 8
SHOW geqo_threshold;           -- above 12 relations a genetic algorithm takes over

-- the pragmatic fix for a very wide join: give the optimiser two small problems
CREATE TEMP TABLE t AS SELECT ... ;    -- the selective part, materialised
ANALYZE t;                             -- CRUCIAL - otherwise it has no statistics
SELECT ... FROM t JOIN ... ;
```

Splitting a very wide query into a materialised intermediate plus a second query is a legitimate and often dramatic optimisation, precisely because it replaces one intractable search with two tractable ones.

<a id="be-d439"></a>

#### Reading an execution plan

A plan is a tree, executed leaves-first. Read it bottom-up and look for four things, in order.

``` sql
EXPLAIN (ANALYZE, BUFFERS, VERBOSE) SELECT ...;      -- PostgreSQL
SET STATISTICS IO, TIME ON;                          -- SQL Server, then the actual plan
EXPLAIN ANALYZE ...;                                 -- MySQL 8+
```

``` yaml
Limit  (cost=1044.21..1044.28 rows=25) (actual time=94.2..94.3 rows=25 loops=1)
  ->  Sort  (cost=1044.21..1071.55 rows=10936) (actual time=94.2..94.2 rows=25)
        Sort Key: o.placed_at DESC
        Sort Method: top-N heapsort  Memory: 28kB
        ->  Hash Join  (cost=88.20..812.44 rows=10936) (actual time=1.9..87.1 rows=11204)
              Hash Cond: (o.customer_id = c.id)
              ->  Seq Scan on orders o  (rows=11000) (actual rows=11204)
                    Filter: (placed_at >= '2026-07-01')
                    Rows Removed by Filter: 188796           <- read 200k, kept 11k
              ->  Hash  (rows=2000)
                    Buckets: 2048  Batches: 1  Memory Usage: 112kB
                    ->  Seq Scan on customers c  (rows=2000)
Planning Time: 0.4 ms
Execution Time: 94.6 ms
```

1.  **Compare estimated to actual rows at every node.** The lowest sharp divergence is the root cause; everything above it is a consequence.
2.  **Find where the time goes.** Actual times include children, so subtract to get each node's own cost.
3.  **Look for wasted work.** `Rows Removed by Filter: 188796` to keep 11,204 means an index could have avoided 94% of the reads.
4.  **Look for spills and warnings.** Disk sort methods, hash `Batches > 1`, implicit conversions, missing-index warnings.

| Plan feature                              | Means                                | Consider                                      |
|-------------------------------------------|--------------------------------------|-----------------------------------------------|
| Large `Rows Removed by Filter`            | Reading rows only to discard them    | An index covering the predicate               |
| `Sort Method: external merge Disk`        | The sort exceeded `work_mem`         | More memory, or an index that avoids the sort |
| `Batches: 4`                              | The hash table spilled               | More memory, or a smaller build side          |
| `loops=` in the thousands                 | Nested loop over a large outer input | Check the outer estimate                      |
| High `Heap Fetches` on an index-only scan | Stale visibility map                 | `VACUUM`                                      |
| Parallel workers launched \< planned      | Worker limit reached                 | Tune the parallel settings                    |

> **\[Note\]**
>
> **Estimated vs actual is the whole game.** If you adopt one habit: run `EXPLAIN ANALYZE` and read the two row counts side by side at every node. Nearly every dramatic query problem announces itself as a large gap between them.

<a id="be-d440"></a>

#### Plan caching and parameter sniffing

Compiling a plan is expensive, so engines cache them. That is what makes parameterised queries fast, and it causes one of the strangest symptoms in databases: a query that has been fast for months becomes slow with no code or data change.

``` text
  parameter sniffing

  first execution:  @status = 'Archived'   (12 rows of 40 million)
    -> a plan for 12 rows: index seek + nested loops. CACHED.

  every later execution reuses it:
      @status = 'Active'                   (38 million rows)
    -> nested loops over 38 million rows. Hours instead of seconds.

  the plan is CORRECT - just optimal for the wrong value.

  the tell: fast or slow depending on the argument, and a cache flush
  "fixes" it until the next unlucky first call.
```

| Mitigation                  | Effect                                 | Cost                                                      |
|-----------------------------|----------------------------------------|-----------------------------------------------------------|
| `OPTION (RECOMPILE)`        | Compile per execution with real values | Compilation each time — fine for infrequent heavy queries |
| `OPTIMIZE FOR (@p = value)` | Always plan for a chosen typical value | You must know it                                          |
| `OPTIMIZE FOR UNKNOWN`      | Use average distribution               | Mediocre for everyone, but stable                         |
| Branch in application code  | Each pattern gets its own cached plan  | Duplication — and it works well                           |
| Filtered indexes per case   | Each subset gets its own access path   | More indexes                                              |
| `plan_cache_mode`           | Force custom or generic plans          | PostgreSQL, per session                                   |

``` sql
-- PostgreSQL: after 5 executions it considers a GENERIC plan with unknown parameters
SET plan_cache_mode = force_custom_plan;     -- always replan with real values
SET plan_cache_mode = force_generic_plan;

-- SQL Server: a huge min/max elapsed spread on one query is the sniffing signature
SELECT qs.execution_count,
       qs.total_elapsed_time / qs.execution_count AS avg_us,
       qs.min_elapsed_time, qs.max_elapsed_time,
       SUBSTRING(st.text, 1, 150) AS query
FROM   sys.dm_exec_query_stats qs
CROSS  APPLY sys.dm_exec_sql_text(qs.sql_handle) st
ORDER  BY qs.total_elapsed_time DESC;
```

> **\[Trap\]**
>
> **Common trap.** Concatenating literals instead of parameterising avoids sniffing by never reusing a plan, and replaces it with two worse problems: SQL injection, and a plan cache holding millions of single-use plans, so compilation dominates CPU. Parameterise always; address sniffing directly if it occurs.

<a id="be-d441"></a>

#### Query rewrites that change everything

Logically equivalent queries can produce wildly different plans, because the optimiser's rewrite ability has limits. These are the rewrites worth knowing by heart.

``` sql
-- 1. NOT IN -> NOT EXISTS.  Null-safe, and usually an anti-join rather than a scan.
WHERE id NOT IN (SELECT customer_id FROM banned)
WHERE NOT EXISTS (SELECT 1 FROM banned b WHERE b.customer_id = o.id)

-- 2. OR across columns -> UNION of two seekable branches
WHERE email = :e OR phone = :p
SELECT ... WHERE email = :e  UNION  SELECT ... WHERE phone = :p

-- 3. OR on one column -> IN
WHERE status = 'A' OR status = 'B'   ->   WHERE status IN ('A','B')

-- 4. DISTINCT that only undoes join duplication -> EXISTS
SELECT DISTINCT c.* FROM customers c JOIN orders o ON ...
SELECT c.* FROM customers c WHERE EXISTS (SELECT 1 FROM orders o WHERE ...)

-- 5. correlated scalar subquery -> a single aggregate join
SELECT c.*, (SELECT count(*) FROM orders WHERE customer_id = c.id) FROM customers c
SELECT c.*, COALESCE(o.n,0) FROM customers c
LEFT JOIN (SELECT customer_id, count(*) n FROM orders GROUP BY 1) o ON o.customer_id = c.id

-- 6. count(*) for existence -> EXISTS, which stops at the first match
IF (SELECT count(*) FROM orders WHERE customer_id = :c) > 0
IF EXISTS (SELECT 1 FROM orders WHERE customer_id = :c)

-- 7. a function on the column -> a range on the raw column
WHERE date_trunc('month', placed_at) = '2026-07-01'
WHERE placed_at >= '2026-07-01' AND placed_at < '2026-08-01'
```

###### Rewrites for very large operations

``` sql
-- one statement: hours of locking, enormous log, and a rollback as slow as the delete
DELETE FROM audit_events WHERE occurred_at < '2024-01-01';

-- batched: short transactions, recycled log, interruptible
DO $$
DECLARE n int;
BEGIN
  LOOP
    DELETE FROM audit_events
    WHERE  id IN (SELECT id FROM audit_events
                  WHERE occurred_at < '2024-01-01' LIMIT 10000);
    GET DIAGNOSTICS n = ROW_COUNT;
    EXIT WHEN n = 0;
    COMMIT;
    PERFORM pg_sleep(0.05);          -- let replicas and autovacuum keep up
  END LOOP;
END $$;

-- and the structural answer: PARTITION by time, and the whole job becomes
--   DROP TABLE audit_events_2023_12;      -- milliseconds
```

| Large operation               | Better form                                       |
|-------------------------------|---------------------------------------------------|
| Delete most of a table        | Copy survivors to a new table, swap, drop the old |
| Update every row              | Batches, or a new column plus a backfill          |
| Add `NOT NULL` with a default | Add nullable, backfill in batches, then constrain |
| Recurring purge by date       | Partition, then drop partitions                   |
| Rebuild an index live         | `CONCURRENTLY` / `ONLINE`                         |

<a id="be-d442"></a>

#### Hints, and when they are legitimate

| Engine     | Mechanism                                                       |
|------------|-----------------------------------------------------------------|
| SQL Server | `OPTION (...)`, table hints, plan guides, Query Store forcing   |
| Oracle     | `/*+ hint */` comments — extensive                              |
| MySQL      | Index and optimiser hints                                       |
| PostgreSQL | **None by design.** Session `enable_*` flags, or `pg_hint_plan` |

PostgreSQL's refusal is a deliberate position: a hint encodes today's data distribution into the application, and it will still be there in three years when it has become wrong. The counter-argument is that sometimes you need the incident to stop tonight.

1.  **Fix the estimate first.** `ANALYZE`, extended statistics, or a rewrite the optimiser can reason about. This resolves most cases.
2.  **Then fix the access path.** The right index removes the choice rather than overriding it.
3.  **Then restructure** — a materialised intermediate with its own statistics.
4.  **Only then hint**, with a comment stating why, what the bad plan was, and what would make it removable.
5.  **Set a review date.** An unreviewed hint becomes a permanent constraint nobody understands.

``` sql
SELECT ... FROM orders o JOIN customers c ON c.id = o.customer_id
OPTION (HASH JOIN);
-- HINT 2026-07-28 (ticket OPS-4412): status and tenant_id are correlated;
-- multi-column statistics did not help. Optimiser estimated 8 rows, actual 940k,
-- and chose nested loops. Remove if the estimate improves. Verify with
-- EXPLAIN ANALYZE before removing. Review 2027-01.
```

> **\[Note\]**
>
> **The honest summary.** A hint is technical debt with a specific interest rate: correct until the data changes shape, and nobody notices when it stops being correct. Treat every hint as temporary with a ticket attached.

### LINQ meets SQL

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · LINQ meets SQL</p>

<a id="be-m171"></a>

#### Indexes for LINQ writers

An index is a B-tree sorted by its key columns. That single fact explains almost everything about when one gets used.

- **Leftmost prefix rule.** An index on `(A, B, C)` serves filters on `A`, on `A+B`, and on `A+B+C` — but not on `B` alone. Order your key columns most-selective-and-most-always-filtered first.
- **Equality before range.** Put equality-filtered columns before range-filtered ones: `(Status, CreatedAt)` beats `(CreatedAt, Status)` for `WHERE Status = x AND CreatedAt > y`.
- **Covering.** If the index contains every column the query needs, the engine never touches the table. Use `INCLUDE` for the extra columns — they add width without adding to the sort key.
- **Sargability.** A column wrapped in a function or implicitly converted cannot be seeked. See [Filtering, parameterization, and the plan cache](07-07-orms-and-ef-core.md#be-m160).

``` csharp
modelBuilder.Entity<Order>()
    .HasIndex(o => new { o.CustomerId, o.CreatedAt })
    .IncludeProperties(o => new { o.Total, o.Status })
    .HasFilter("[IsDeleted] = 0")            // filtered index
    .HasDatabaseName("IX_Orders_Customer_Created");
```

**Costs.** Every index slows every INSERT, UPDATE of its columns, and DELETE, and consumes storage and buffer-pool memory. Six indexes on a hot write table is a throughput problem. Drop unused ones — `sys.dm_db_index_usage_stats` tells you which have zero seeks.

> **\[Tip\]**
>
> **Map queries to indexes deliberately.** For each query shape your application runs, write down the columns in its `WHERE`, its `ORDER BY`, and its `SELECT`. That is the index. Doing this for your ten most frequent queries usually finds two or three missing indexes and several unused ones.

Goes deeper: [B-tree anatomy: levels, splits, and fill factor](#be-m196)

<a id="be-m172"></a>

#### Reading a query plan without being a DBA

Get the actual plan (SSMS Ctrl+M, Azure Data Studio, or `SET STATISTICS XML ON`) and read it right to left, top to bottom. Four things to look for, in order:

1.  **Scans where you expected seeks.** "Clustered Index Scan" or "Table Scan" on a large table with a selective filter means no usable index, or a non-sargable predicate.
2.  **A large gap between estimated and actual rows.** Hover any operator. An estimate of 1 against an actual of 500,000 means stale statistics or parameter sniffing, and everything downstream (join type, memory grant) was chosen wrong.
3.  **Key Lookups.** The index found the rows but had to go back to the table for extra columns. Many lookups → add those columns as `INCLUDE`.
4.  **Warnings.** Yellow triangles flag implicit conversions (killing sargability), spills to tempdb (memory grant too small), and missing-index suggestions — treat the last as a hint, not an instruction; the optimiser's suggestions are often too wide.

``` csharp
SET STATISTICS IO, TIME ON;
-- 'logical reads' is the metric that matters: pages touched.
-- Compare it before and after a change. Duration varies with cache state; reads don't.
```

> **\[Tip\]**
>
> **Find the queries worth looking at first.** Query Store (SQL Server 2016+) ranks by total duration, CPU, and logical reads. Optimising the query with the highest *total* cost — frequency × cost — beats optimising the slowest single query, which may run once a night.

> **\[Why it matters\]**
>
> **The connection back to LINQ.** Every fix in this part shows up in the plan: a projection removes a Key Lookup, a sargable date range turns a scan into a seek, `AsSplitQuery` removes a giant Nested Loops fan-out, keyset pagination removes a Top-N sort over millions of rows. The plan is the feedback loop that tells you whether your LINQ change did what you thought.

Goes deeper: [Join algorithms: loops, hash, and merge](#be-m199)

### Inside the storage engine

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Inside the storage engine</p>

<a id="be-m194"></a>

#### Pages, rows, and why everything is 8 KB

Databases do not read rows. They read **pages** — fixed-size blocks, 8 KB in both SQL Server and PostgreSQL — and everything about performance follows from that unit.

``` text
  8192-byte page
 ┌────────────────────────────────────────────────┐
 │ header (96 bytes): page id, type, free space,  │
 │                    row count, LSN               │
 ├────────────────────────────────────────────────┤
 │ row 1                                           │
 │ row 2                                           │  ~8060 bytes usable
 │ row 3                                           │
 │ ...                                             │
 │              free space                         │
 ├────────────────────────────────────────────────┤
 │ slot array (2 bytes/row, grows upward)          │
 └────────────────────────────────────────────────┘
```

**Row width decides how many rows fit per page, and that decides how many pages a query touches.** This is the whole reason narrow columns matter ([Column types, sizing, and collation](07-02-the-relational-model-and-sql.md#be-m190)):

| Row width | Rows per page | Pages for 10M rows | Bytes read for a full scan |
|-----------|---------------|--------------------|----------------------------|
| 40 bytes  | ~200          | 50,000             | 410 MB                     |
| 200 bytes | ~40           | 250,000            | 2.0 GB                     |
| 800 bytes | ~10           | 1,000,000          | 8.2 GB                     |

Adding one `nvarchar(200)` column you never query can quintuple the cost of every scan of that table — which is why `SELECT *` is expensive even when you discard the columns, and why projecting is the highest-leverage EF habit ([Projection: Select before materialize](07-07-orms-and-ef-core.md#be-m157)).

> **\[Trap\]**
>
> **Rows cannot span pages** (in SQL Server: 8,060 bytes of in-row data). Exceed it and variable-length columns are pushed off-row into `ROW_OVERFLOW` or LOB pages, and every read of that column becomes an extra page fetch through a 24-byte pointer. An unconfigured `nvarchar(max)` is stored off-row above 8,000 bytes by default — so a "just add a JSON blob column" change can silently double the I/O of unrelated queries on the same table ([JSON columns in a relational database](07-06-non-relational-stores.md#be-m209)).

> **\[Tip\]**
>
> **`SET STATISTICS IO ON` reports logical reads — pages touched.** That is the currency: it is deterministic, independent of cache state and machine speed, and directly comparable before and after a change. Duration is noise by comparison ([Reading a query plan without being a DBA](#be-m172)).

<a id="be-m196"></a>

#### B-tree anatomy: levels, splits, and fill factor

An index is a B+tree: internal pages hold key ranges and page pointers, leaf pages hold the data (clustered) or the key plus a row locator (nonclustered), and leaves are doubly linked so a range scan walks sideways without returning to the root.

``` text
                    ┌──────── root ────────┐          level 3
                    │ <5000 │ <9000 │ rest  │
                    └───┬───────┬───────┬───┘
              ┌─────────┘       │       └─────────┐
        ┌─────▼─────┐     ┌─────▼─────┐     ┌─────▼─────┐   level 2
        │ ...ranges │     │ ...ranges │     │ ...ranges │
        └─────┬─────┘     └─────┬─────┘     └─────┬─────┘
        ┌─────▼─────┐◄───►┌─────▼─────┐◄───►┌─────▼─────┐   leaf (level 1)
        │ rows      │     │ rows      │     │ rows      │
        └───────────┘     └───────────┘     └───────────┘
                          ^ leaves are linked: range scans go sideways
```

**Trees are shallow.** With ~8 KB pages and a narrow key, one internal page addresses hundreds of children, so depth grows logarithmically at a very high base: a 100-million-row table is typically 4 levels. A point lookup is *four page reads*, and the top levels are almost always cached — which is why an index seek is effectively O(1) in practice.

**Page splits** are the cost of inserting into the middle. When a full page must accept a new row, the engine allocates a new page, moves about half the rows across, and fixes the links — expensive in CPU, in log records, and in leaving both pages half empty.

``` sql
-- Fill factor reserves free space on every leaf page at rebuild time,
-- so inserts have somewhere to go before a split is needed.
ALTER INDEX IX_Orders_Customer ON Orders REBUILD WITH (FILLFACTOR = 80);

-- Guidance:
--   100 (default)  ever-increasing key; inserts always append; no splits
--   90-95          mostly append, occasional middle inserts
--   70-80          random key (a v4 Guid) or heavy middle insertion
-- Lower fill factor = fewer splits, but MORE pages, so every scan reads more.
-- It is a trade, not a free win.
```

> **\[Why it matters\]**
>
> **This is the mechanism behind the Guid warning.** A random clustered key inserts into a random page every time, so page splits are the normal case rather than the exception, fragmentation climbs continuously, and the working set becomes the whole index instead of its hot tail. A time-ordered key (identity, `UUIDv7`) always appends into the last page, and splits essentially disappear ([Guid: layout, versions, and index behaviour](02-03-types.md#be-m53), [Generating identifiers](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78)).

<a id="be-m197"></a>

#### Nonclustered indexes, lookups, and the tipping point

A nonclustered index leaf holds the key columns, any `INCLUDE`d columns, and a row locator. If your query needs a column that is in neither, the engine must fetch the full row — a **key lookup** (clustered table) or **RID lookup** (heap), once per qualifying row.

``` sql
CREATE INDEX IX_Orders_Status ON Orders (Status);

SELECT Id, Status, Total FROM Orders WHERE Status = 1;
--   Index Seek on IX_Orders_Status   -> finds the rows
--   + Key Lookup on the clustered index -> fetches Total, ONE PER ROW
--   + Nested Loops joining them

-- Covering the query removes the lookup entirely:
CREATE INDEX IX_Orders_Status ON Orders (Status) INCLUDE (Total);
```

Key columns and included columns differ in an important way: **key columns are sorted and seekable; included columns are only payload.** Put a column in the key if you filter, join, or order by it; include it if you merely return it. Includes are also not subject to the 900/1700-byte index key size limit.

> **\[Why it matters\]**
>
> **The tipping point.** Each lookup is a random read; a scan is sequential. Past roughly **1–2% of the table**, the optimiser correctly decides that scanning everything is cheaper than doing that many lookups — and your carefully built index goes unused. This is not a bug, and hinting the index usually makes things worse. The fix is to make the index cover the query so no lookup is needed, at which point the index stays attractive at any selectivity ([Indexes for LINQ writers](#be-m171)).

> **\[Trap\]**
>
> **Covering has a cost you pay on every write.** An index with five included columns is a second copy of those columns: it doubles their storage and must be maintained on every insert, delete, and update that touches them. "Just add an include" is a real trade. Check `sys.dm_db_index_usage_stats` for indexes with many updates and zero seeks — those are pure cost.

<a id="be-m199"></a>

#### Join algorithms: loops, hash, and merge

`JOIN` is a logical operation; the engine picks one of three physical implementations, and knowing which explains most surprising plans.

|                  | Nested loops                       | Hash join                                                     | Merge join                         |
|------------------|------------------------------------|---------------------------------------------------------------|------------------------------------|
| How              | for each outer row, seek the inner | build a hash table on the smaller side, probe with the larger | walk both inputs in order, in step |
| Cost             | O(n × lookup)                      | O(n + m)                                                      | O(n + m) *if already sorted*       |
| Needs            | an index on the inner join key     | a memory grant                                                | both inputs sorted on the key      |
| Best when        | outer is small, inner is indexed   | both large, no useful index                                   | both large and already ordered     |
| Fails badly when | outer is large — millions of seeks | the grant is too small → **spills to tempdb**                 | a sort must be added first         |

``` text
-- Nested loops with a large outer is the classic "why is this plan terrible".
-- It is usually a cardinality misestimate: the optimiser thought the outer
-- side had 10 rows, so loops looked cheap. It had 2 million.
-- Fix the estimate, not the join hint.

-- Hash join spilling shows as a yellow warning triangle on the operator.
-- Causes: underestimated input size, or too many concurrent memory grants.

-- Merge join with an explicit Sort above it means the optimiser wanted merge
-- but had to pay for ordering. An index in that order removes the sort.
```

Two behaviours worth knowing about modern engines: **adaptive joins** (SQL Server 2017+) defer the loops-versus-hash decision until the build input has actually been read, which defuses exactly the misestimate case above; and **batch mode** processes ~900 rows per operator call rather than one, which is why columnstore joins are dramatically faster ([Columnstore and compression](#be-m201)).

> **\[Why it matters\]**
>
> **The connection back to LINQ.** `Enumerable.Join` is always a hash join — it buffers the inner sequence into a lookup and probes it ([Join, GroupJoin, GroupBy in memory](03-10-collections-and-linq-in-net.md#be-m143)). It has no index to seek and no ordering to exploit, so it has no other option. That is a good reason to let the database do joins when it can: it has three algorithms and statistics to choose between them, and you have one.

<a id="be-m200"></a>

#### Seeks, scans, and residual predicates

"Index Seek" in a plan does not mean the query is efficient. A seek narrows to a range using the leading key columns; anything else is applied afterwards, as a **residual predicate**, to every row the seek returned.

``` sql
CREATE INDEX IX ON Orders (TenantId, Status, CreatedAt);

WHERE TenantId = 5 AND Status = 1 AND CreatedAt >= @from
--   Seek Predicate: TenantId = 5 AND Status = 1 AND CreatedAt >= @from
--   -> all three used to position. Efficient.

WHERE TenantId = 5 AND CreatedAt >= @from
--   Seek Predicate: TenantId = 5          <- only the leading column
--   Predicate:      CreatedAt >= @from    <- RESIDUAL, checked per row
--   -> reads every row for tenant 5, then filters. Still called a "Seek".
```

The gap between the two shows up as *rows read* far exceeding *rows returned* — a column exposed in the plan since SQL Server 2016 and the fastest way to spot this. Equality columns must precede range columns in the key for exactly this reason ([Indexes for LINQ writers](#be-m171)).

| Plan operator              | Means                                   | Concerning when                                                                        |
|----------------------------|-----------------------------------------|----------------------------------------------------------------------------------------|
| Index Seek                 | positioned via the key                  | rows read » rows returned                                                              |
| Index Scan                 | read the whole index                    | a selective filter exists                                                              |
| Table/Clustered Index Scan | read the whole table                    | almost always, on a large table                                                        |
| Key/RID Lookup             | fetching columns not in the index       | executed many times ([Nonclustered indexes, lookups, and the tipping point](#be-m197)) |
| Sort                       | ordering was not available              | it spills, or an index could supply the order                                          |
| Filter                     | a predicate applied late                | it could have been pushed into a seek                                                  |
| Spool                      | the engine materialised an intermediate | usually a sign of a rewritable query                                                   |

> **\[Trap\]**
>
> **A scan is not automatically wrong.** Reading a 500-row lookup table is one page; seeking it is also one page. And past the tipping point, a scan genuinely is the cheaper plan ([Nonclustered indexes, lookups, and the tipping point](#be-m197)). Judge by *pages read relative to rows returned*, not by the operator name — which is why `SET STATISTICS IO ON` is more useful than staring at the plan shape.

Worked example: [Case: the search that degraded as data grew](14-03-worked-cases-from-production.md#be-m313)

<a id="be-m201"></a>

#### Columnstore and compression

Row storage keeps a whole row together, which is right for "fetch this order". Column storage keeps a whole column together, which is right for "sum this column across 200 million rows".

``` text
ROW STORE       page: [id|date|sku|qty|price][id|date|sku|qty|price]...
                SUM(price) must read every byte of every row.

COLUMN STORE    segment: [price][price][price][price]...  (~1M rows per rowgroup)
                SUM(price) reads only the price segments.
                Values are similar, so they compress 5-15x.
                And segment metadata holds min/max, so whole rowgroups
                are skipped when they cannot match the filter.
```

``` sql
CREATE CLUSTERED COLUMNSTORE INDEX CCI_FactSales ON FactSales;
-- Also available as a nonclustered columnstore alongside a rowstore table,
-- which gives you analytical queries on an OLTP table without a second copy.
```

Three mechanisms multiply together here: **only the referenced columns are read**, **compression** (dictionary encoding and run-length, since a column's values repeat), and **batch mode** execution (~900 rows per operator call instead of one). Aggregations over large fact tables commonly improve by one to two orders of magnitude.

| Compression option    | Effect                               | Use for                      |
|-----------------------|--------------------------------------|------------------------------|
| `ROW`                 | stores values in their smallest form | almost always a win; low CPU |
| `PAGE`                | \+ prefix and dictionary per page    | read-heavy tables, wide rows |
| `COLUMNSTORE`         | columnar + RLE                       | analytical fact tables       |
| `COLUMNSTORE_ARCHIVE` | maximum ratio, slower                | cold partitions              |

> **\[Trap\]**
>
> **Columnstore is hostile to single-row work.** Point lookups, small updates, and deletes are slow: small inserts land in a rowstore *delta store* until a rowgroup fills, and deletes are logical, marked in a delete bitmap until reorganised. It is built for bulk load and scan. Do not put one on a transactional table and expect OLTP performance ([Analytical modeling: grain, facts, dimensions](07-05-schema-design-and-migrations.md#be-m215)).

> **\[Tip\]**
>
> **Page compression is underused.** On a read-heavy table it typically cuts storage by 50–70%, which cuts pages, which cuts I/O and buffer-pool pressure — often a net CPU *win* despite the decompression cost, because the reads it avoids are more expensive than the CPU it adds. Test it on your largest table; it is a one-line rebuild ([Pages, rows, and why everything is 8 KB](#be-m194)).
