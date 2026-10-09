---
id: "7-2"
number: "7.2"
title: "The relational model and SQL"
part: 7
part_title: "Storing data"
words: 15102
minutes: 66
---

# 7.2 The relational model and SQL

> **Quick look.** The relational model and the parts of SQL that trip up experienced developers.
>
> - Tables of typed columns, a primary key per row and foreign keys between tables, so each fact lives in one place.
> - Logical order is FROM, WHERE, GROUP BY, HAVING, SELECT, ORDER BY, which explains where aliases work.
> - Inner joins keep matches, left joins keep every left row; one-to-many joins multiply rows.
> - A condition on the right table in WHERE silently turns a left join into an inner join.
> - NULL means unknown: use IS NULL, COALESCE and NOT EXISTS.
> - Window functions handle rankings, running totals and top-N per group.
> - Think in sets, not loops; do upserts in one statement; always pass values as parameters.


### The big picture

In 1970, Edgar Codd, a researcher at IBM, proposed storing data as simple tables and asking questions about it in a language that describes the answer you want, not the steps to find it. That idea, the relational model, still runs most of the world's business data, including nearly every airline's bookings, schedules and loyalty accounts. SQL is its language.

You have probably written plenty of SQL already. This chapter is about the parts that trip up even experienced developers. It covers how tables and keys fit together, the order in which a query is really evaluated, what joins do to row counts, the odd logic of NULL, and window functions, which answer questions that are painful any other way. It also covers the one rule that must never be broken: values go in as parameters, never pasted into the SQL text.

### Before you start

**Builds on:** 2.1 What a programming language is, 3.4 Trees, 7.1 Why databases exist, 1.3 Text is numbers too.

**Ideas explained later:**

- **Schema** (7.5): The structure of a database: its tables, columns, types, keys, constraints and indexes.
- **Integrity** (10.1): Data and systems can't be changed without permission, and changes can be detected.
- **Query optimizer** (7.3): The part of a database that chooses how to run each query.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.

**Tools and technologies named here:**

**The code uses:** variables and var, string interpolation, lambdas, LINQ, tuples, using for cleanup, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. In what order are the clauses of a SELECT statement logically evaluated?**

> FROM and joins, then WHERE, then GROUP BY, then HAVING, then SELECT, then ORDER BY, then the row limit. That is why a column alias defined in SELECT can be used in ORDER BY but not in WHERE.

**2. Why does WHERE gate = NULL return no rows, even when some gates are NULL?**

> NULL means "unknown", and comparing anything with unknown gives unknown, not true. WHERE keeps only rows where the condition is true. Use `IS NULL` instead.

**3. What is the difference between an inner join and a left outer join?**

> An inner join keeps only rows that have a match on both sides. A left outer join keeps every row from the left table, with NULLs in the right table's columns where there is no match.

**4. What does HAVING do that WHERE can't?**

> It filters groups after aggregation, so it can test aggregate values such as `COUNT(*) > 1`. WHERE filters individual rows before grouping.

**5. How do you get the single most expensive booking on each flight?**

> Use a window function: number the bookings within each flight by fare, with `ROW_NUMBER() OVER (PARTITION BY flight_id ORDER BY fare DESC)`, then keep the rows numbered 1.

**6. Why can NOT IN (subquery) return nothing at all?**

> If the subquery returns any NULL, then `x NOT IN (..., NULL)` is unknown for every x, so no rows pass. `NOT EXISTS` doesn't have this problem.

**7. What is SQL injection, and what prevents it?**

> Pasting user input into SQL text lets the input change the statement itself, such as `x' OR '1'='1` making a condition always true. Passing values as parameters prevents it, because parameters are never treated as SQL.

### Tables, rows and columns

The **relational model** stores data as tables. A **table** holds one kind of thing, such as flights. Each **row** is one of those things: one flight. Each **column** is one fact about them, with a fixed type: its number, its origin, its departure time. Codd's mathematical name for a table was a relation, which is where "relational" comes from.

Two details of the model matter in practice. Rows have no built-in order: a table is a set, and a query without `ORDER BY` may return rows in any order, even differently from one run to the next. And every fact lives in exactly one place. A flight's destination city isn't copied into each flight row; the flight stores the airport code, and the city lives once in the airports table.

### Keys

A **primary key** is a column, or set of columns, whose value identifies each row uniquely and never changes. The database refuses two rows with the same key. It might be a natural identifier, such as an airport's IATA code `LHR`, or a number the database generates, such as a flight row's `id`. Schema design and migrations (7.5) discusses which to choose.

A **foreign key** is a column that refers to the primary key of another table. `flights.origin` refers to `airports.code`. With a foreign key constraint, the database refuses a flight whose origin isn't a real airport, and refuses to delete an airport that flights still use. That guarantee is called **referential integrity**. Foreign keys are how the relational model connects tables without copying data.

### SQL

**SQL**, Structured Query Language, is the standard language of relational databases, created at IBM in the 1970s and standardized since 1986. It is declarative, as What a programming language is (2.1) explained: you describe the result, and the database's query optimizer decides how to produce it, as Indexes and query plans (7.3) shows.

SQL has two main parts. Data definition statements, such as `CREATE TABLE` and `ALTER TABLE`, define structure. Data manipulation statements, `SELECT`, `INSERT`, `UPDATE` and `DELETE`, read and change data. Every database speaks its own **SQL dialect**, a version of the standard with its own extras and differences, covered at the end of this chapter.

### Reading data

A `SELECT` statement names the columns you want, the table to read and optional conditions:

```sql
SELECT number, destination, departs
FROM flights
WHERE origin = 'LHR'
ORDER BY departs;
```

The clauses are written in one order and evaluated in another. Logically, the database first takes the rows from `FROM`, including any joins. Then `WHERE` filters rows. Then `GROUP BY` forms groups and `HAVING` filters groups. Then `SELECT` computes the output columns. Then `ORDER BY` sorts, and finally any row limit is applied.

Knowing this order explains many puzzling errors. A column alias created in `SELECT` doesn't exist yet in `WHERE`, so it can't be used there, but it can be used in `ORDER BY`. An aggregate like `COUNT(*)` can't appear in `WHERE`, because groups haven't been formed yet; it belongs in `HAVING`. The optimizer may run things in a different physical order, but the result is always as if this logical order were followed.

### Joins

A **join** combines rows from two tables, matching them on a condition, usually a foreign key.

An **inner join**, written `JOIN` or `INNER JOIN`, keeps only rows with a match on both sides. Flights joined to airports on destination gives each flight with its destination's city; a flight with no matching airport would disappear.

An **outer join** keeps unmatched rows too. A `LEFT JOIN` keeps every row from the left table, and fills the right table's columns with NULL where nothing matches. Joining flights to bookings with a `LEFT JOIN` includes flights with no bookings at all. `RIGHT JOIN` is the mirror image, and `FULL JOIN` keeps unmatched rows from both sides. A `CROSS JOIN` pairs every row with every other row, which is occasionally useful and usually a mistake.

Joins change row counts, and this causes real bugs. Joining a flight to its three bookings gives three rows for that flight. Join those rows to a second one-to-many table, such as the flight's crew, and every booking is repeated for every crew member. A `SUM` of fares over that result counts each fare several times. When totals look too big, check whether a join multiplied rows.

### Grouping and aggregates

An **aggregate function** turns many rows into one value: `COUNT`, `SUM`, `AVG`, `MIN` and `MAX`. `GROUP BY` splits rows into groups, one per distinct value of the grouping columns, and aggregates are computed per group. Every column in the `SELECT` list must either be grouped or be inside an aggregate.

`HAVING` filters groups after they are formed, so it can test aggregates. "Passengers with more than one booking" is `GROUP BY passenger HAVING COUNT(*) > 1`. `WHERE` can't do that, because it runs before grouping.

### NULL

NULL means a value is missing or unknown: a flight with no gate assigned yet. It is not zero and not an empty string, and it behaves differently from every other value.

Any comparison with NULL gives neither true nor false, but unknown. `gate = NULL` is unknown, even when the gate is NULL. `gate <> 'B12'` is unknown for flights with no gate, so they are excluded too. SQL uses this **three-valued logic** everywhere: `WHERE` keeps only rows where the condition is true, so unknown behaves like false.

The tools for NULL are `IS NULL` and `IS NOT NULL`, and `COALESCE(a, b)`, which gives the first non-NULL value. Aggregates ignore NULLs: `COUNT(*)` counts rows, while `COUNT(gate)` counts rows whose gate isn't NULL. And `SUM` over no rows gives NULL, not zero, which is why the revenue query below wraps it in `COALESCE`.

The worst trap is `NOT IN` with a subquery. If the subquery returns even one NULL, `x NOT IN (...)` is unknown for every row, and the query quietly returns nothing. Use `NOT EXISTS` instead.

### Subqueries, CTEs and window functions

A **subquery** is a query inside another query: in `WHERE`, as with `EXISTS`, or in `FROM`, as a temporary table. `EXISTS (SELECT 1 FROM bookings WHERE ...)` is true if at least one matching row exists, and the database can stop looking as soon as it finds one.

A **common table expression** (CTE), introduced with `WITH`, names a subquery at the top of a statement so the main query can use it like a table. CTEs make long queries readable, step by step. A recursive CTE refers to itself, which is how SQL walks the tree-shaped data from Trees (3.4), such as a category hierarchy.

A **window function** computes a value for each row from a set of related rows, its window, without collapsing them the way `GROUP BY` does. `ROW_NUMBER() OVER (PARTITION BY flight_id ORDER BY fare DESC)` numbers the bookings within each flight, most expensive first. `SUM(fare) OVER (ORDER BY booked_at)` gives a running total. `LAG(departs) OVER (ORDER BY departs)` gives the previous row's value, for gaps between flights. Top-N per group, running totals, rankings and comparisons with the previous row are all simple with window functions and painful without them.

### Seeing it for real

This program builds a small airline database in SQLite and runs each kind of query from this chapter.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// sql.cs — run with: dotnet run sql.cs
using System.Linq;
using Microsoft.Data.Sqlite;

using var db = new SqliteConnection("Data Source=:memory:");   // a throwaway database in memory
db.Open();
Run("""
    PRAGMA foreign_keys = ON;   -- SQLite only enforces foreign keys when asked
    CREATE TABLE airports (code TEXT PRIMARY KEY, city TEXT NOT NULL, country TEXT NOT NULL);
    CREATE TABLE flights (
        id          INTEGER PRIMARY KEY,
        number      TEXT NOT NULL,
        origin      TEXT NOT NULL REFERENCES airports (code),
        destination TEXT NOT NULL REFERENCES airports (code),
        departs     TEXT NOT NULL,
        gate        TEXT                                     -- NULL until a gate is assigned
    );
    CREATE TABLE bookings (
        id        INTEGER PRIMARY KEY,
        flight_id INTEGER NOT NULL REFERENCES flights (id),
        passenger TEXT NOT NULL,
        fare      INTEGER NOT NULL                           -- whole pounds; SQL Server would use DECIMAL(10,2)
    );
    INSERT INTO airports VALUES ('LHR','London','UK'), ('JFK','New York','US'), ('SIN','Singapore','SG'),
                                ('DXB','Dubai','AE'), ('SYD','Sydney','AU');
    INSERT INTO flights VALUES (1,'BA117','LHR','JFK','2026-09-28 08:25','B12'), (2,'BA11','LHR','SIN','2026-09-28 21:00',NULL),
                               (3,'EK1','DXB','LHR','2026-09-28 07:45','A3'),  (4,'QF1','SYD','SIN','2026-09-28 16:00',NULL),
                               (5,'SQ321','SIN','LHR','2026-09-29 23:55','C7');
    INSERT INTO bookings VALUES (1,1,'Ada',450), (2,1,'Grace',520), (3,2,'Linus',900),
                                (4,3,'Ada',610), (5,1,'Alan',480), (6,5,'Grace',700);
    """);

Show("SELECT number, destination, departs FROM flights WHERE origin = 'LHR' ORDER BY departs");
// BA117 | JFK | 2026-09-28 08:25
// BA11 | SIN | 2026-09-28 21:00

Show("SELECT f.number, a.city FROM flights AS f JOIN airports AS a ON a.code = f.destination WHERE f.origin = 'LHR' ORDER BY f.number");
// BA11 | Singapore
// BA117 | New York

Show("""
    SELECT f.number, COUNT(b.id) AS booked, COALESCE(SUM(b.fare), 0) AS revenue
    FROM flights AS f LEFT JOIN bookings AS b ON b.flight_id = f.id
    GROUP BY f.id, f.number
    ORDER BY revenue DESC
    """);
// BA117 | 3 | 1450   ...   QF1 | 0 | 0: kept by the LEFT JOIN, with no bookings

Show("SELECT passenger, COUNT(*) AS trips FROM bookings GROUP BY passenger HAVING COUNT(*) > 1 ORDER BY passenger");
// Ada | 2
// Grace | 2

Show("SELECT number FROM flights AS f WHERE NOT EXISTS (SELECT 1 FROM bookings AS b WHERE b.flight_id = f.id)");
// QF1

Show("""
    WITH ranked AS (
        SELECT f.number, b.passenger, b.fare,
               ROW_NUMBER() OVER (PARTITION BY b.flight_id ORDER BY b.fare DESC) AS place
        FROM bookings AS b JOIN flights AS f ON f.id = b.flight_id
    )
    SELECT number, passenger, fare FROM ranked WHERE place = 1 ORDER BY number
    """);
// BA11 | Linus | 900    BA117 | Grace | 520    EK1 | Ada | 610    SQ321 | Grace | 700

Show("SELECT COUNT(*) FROM flights WHERE gate = NULL");      // 0: comparing with NULL is never true
Show("SELECT COUNT(*) FROM flights WHERE gate IS NULL");     // 2
Show("SELECT COUNT(*) FROM flights WHERE gate <> 'B12'");    // 2, not 4: the two NULL gates are "unknown"

string typed = "x' OR '1'='1";                              // what an attacker types into a name box
Show($"SELECT COUNT(*) FROM bookings WHERE passenger = '{typed}'");                          // 6: the input rewrote the query
Show("SELECT COUNT(*) FROM bookings WHERE passenger = $name", ("$name", typed));             // 0: the input stayed a value

void Run(string sql)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
}

void Show(string sql, params (string Name, object Value)[] parameters)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);   // sent separately from the SQL
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
        Console.WriteLine(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i))));
    Console.WriteLine();
}
```

### Changing data

`INSERT` adds rows. `UPDATE` changes rows that match a `WHERE` condition. `DELETE` removes them. An `UPDATE` or `DELETE` without `WHERE` changes every row in the table, and it is one of the most common disasters in database work. Many teams run such statements inside a transaction, check the row count, and only then commit.

"Insert, or update if it already exists" is called an upsert. Each dialect spells it differently: `INSERT ... ON CONFLICT ... DO UPDATE` in PostgreSQL and SQLite, and `MERGE` in SQL Server, which needs care, because under concurrent use it can still hit duplicate-key errors unless you take the right locks. Most databases can also return the rows a change affected: `RETURNING` in PostgreSQL and SQLite, `OUTPUT` in SQL Server. That returns a generated id in the same round trip.

### Think in sets

SQL works best on whole sets of rows at once. `UPDATE bookings SET fare = fare * 1.1 WHERE flight_id = 7` changes a thousand rows in one statement, one round trip and one pass the database can optimize. Doing the same with a loop in C# that reads each row and updates it separately costs a thousand round trips, as Why databases exist (7.1) described. The same goes for cursors, which step through rows one at a time inside the database. When you catch yourself writing a loop around SQL statements, look for a single statement that does it.

### Parameters and SQL injection

The last two lines of the example show the most important rule in this chapter. Building SQL by pasting in user input lets that input change the statement itself. The name `x' OR '1'='1` closes the quoted string and adds a condition that is always true, so the query returns every booking. With a different input, the same hole could delete tables or read passwords. This is **SQL injection**, and it has been one of the most damaging security holes on the web for over two decades, as Common web vulnerabilities (10.4) explains.

The cure is a **parameterized query**. The SQL text contains a placeholder, `$name` in SQLite or `@name` in SQL Server, and the value is sent separately. The database never reads a parameter as SQL, whatever it contains. Parameters also help performance: the database can reuse the plan for the same statement text with different values, as Indexes and query plans (7.3) explains. Never escape values by hand instead; use parameters every time, and let ORMs such as EF Core do it for you.

### Dialects

The SQL standard covers the core, and every database adds its own extras and differences. Some you will meet constantly:

| Task | SQL Server | PostgreSQL | SQLite |
|---|---|---|---|
| First 10 rows | `SELECT TOP 10 ...` | `... LIMIT 10` | `... LIMIT 10` |
| Join strings | `+` or `CONCAT` | `\|\|` or `CONCAT` | `\|\|` |
| Quote an identifier | `[order]` | `"order"` | `"order"` |
| Current time | `SYSUTCDATETIME()` | `now()` | `datetime('now')` |
| Parameter marker | `@name` | `@name` (in .NET drivers) or `$1` | `$name`, `@name` or `?` |
| Auto-numbered key | `IDENTITY` | `GENERATED ... AS IDENTITY` | `INTEGER PRIMARY KEY` |
| Upsert | `MERGE` | `ON CONFLICT` | `ON CONFLICT` |

The standard form `OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY` works in both SQL Server and PostgreSQL. Case sensitivity of text comparisons depends on the column's collation, as Text is numbers too (1.3) described.

### In short

The relational model stores data in tables of typed columns, with a primary key per row and foreign keys between tables, so each fact lives in one place. SQL describes the result you want. A query is evaluated FROM, WHERE, GROUP BY, HAVING, SELECT, ORDER BY, which explains where aliases and aggregates may appear. Inner joins keep matches, left joins keep every left row, and one-to-many joins multiply rows. NULL means unknown, so comparisons with it are never true: use `IS NULL`, `COALESCE` and `NOT EXISTS`. Window functions handle rankings and running totals. Work in sets, and always pass values as parameters.

### What goes wrong

**SQL built from strings.** Pasted-in user input rewrites the query and opens the whole database. Use parameters everywhere, including in scripts and admin tools.

**Missing WHERE.** An `UPDATE` or `DELETE` without a condition changes every row. Run risky statements in a transaction, check the row count, then commit.

**Relying on row order.** A query without `ORDER BY` returns rows in whatever order is convenient, which changes when data, indexes or versions change. Always sort when order matters, with a unique tie-breaker for paging.

**NULL surprises.** `= NULL`, `<> value` and `NOT IN` with NULLs silently drop rows. Use `IS NULL`, `COALESCE` and `NOT EXISTS`.

**Joins that multiply rows.** Totals double or triple after joining two one-to-many tables. Aggregate each child table separately, then join the results.

**Row-by-row loops.** A C# loop issuing one statement per row turns a one-second job into an hour of round trips. Write one set-based statement.

**Assuming one dialect.** SQL copied between SQL Server and PostgreSQL fails on `TOP`, quoting or date functions. Know which dialect you are writing.

### Cost model

A query's cost is mostly the rows the database must read to answer it, not the rows it returns. Finding one row through an index costs a few page reads; scanning a table of ten million rows costs thousands. Joins cost roughly the sizes of their inputs, in ways the next chapter explains. Every statement also costs a round trip from the application, about half a millisecond in a data centre, so one statement touching 1,000 rows is far cheaper than 1,000 statements touching one row each. Parameterized statements let the database reuse compiled plans, saving compilation time on every call.

### In other languages (optional)

**C# with LINQ.** EF Core translates LINQ into SQL: `Where` becomes `WHERE`, `Select` shapes the column list, `GroupBy` becomes `GROUP BY` and `Join` or navigation properties become joins, as ORMs and EF Core (7.7) shows.

**JavaScript, TypeScript and Python.** Every driver supports parameters: `$1` placeholders in Node's `pg`, and `%s` or `?` in Python drivers. Query builders such as Knex and SQLAlchemy Core build SQL safely from code.

**Spreadsheets and dataframes.** pandas in Python and similar libraries offer the same operations, filtering, joining, grouping and window calculations, on data in memory, with different names.

### Key terms

- **Relational model**: Storing data as tables of rows and columns, connected by keys.
- **Table**: A set of rows describing one kind of thing, with a fixed list of typed columns.
- **Row**: One record in a table.
- **Column**: One named, typed fact stored for every row of a table.
- **Primary key**: A column or set of columns that uniquely identifies each row.
- **Foreign key**: A column that refers to the primary key of another table.
- **Referential integrity**: The guarantee that every foreign key points at a row that really exists.
- **SQL**: Structured Query Language: the standard declarative language of relational databases.
- **SQL dialect**: One database's version of SQL, with its own extras and differences.
- **Join**: Combining rows from two tables by matching them on a condition.
- **Inner join**: A join that keeps only rows with a match on both sides.
- **Outer join**: A join that also keeps unmatched rows, filling the missing side with NULLs.
- **Aggregate function**: A function that turns many rows into one value, such as COUNT, SUM or MAX.
- **Three-valued logic**: SQL's logic of true, false and unknown, where any comparison with NULL is unknown.
- **Subquery**: A query nested inside another query.
- **Common table expression**: A named subquery introduced with WITH at the start of a statement. Often shortened to CTE.
- **Window function**: A function that computes a value for each row from a related set of rows, without collapsing them.
- **SQL injection**: Untrusted input changing the meaning of a SQL statement because it was pasted into the SQL text.
- **Parameterized query**: A query whose values are sent separately from its SQL text, so they can never be read as SQL.

### Where this leads

How the database finds rows quickly, and how to read its plans, is in Indexes and query plans (7.3). What happens when several statements must succeed together is in Transactions and isolation (7.4). Designing tables and keys is in Schema design and migrations (7.5). Writing queries as LINQ is in ORMs and EF Core (7.7), and injection attacks in general are in Common web vulnerabilities (10.4).

### Check yourself

1. Add a query to `sql.cs` listing every airport with the number of flights departing from it, including airports with none.
2. Write a query that shows, for each passenger, their bookings in fare order with a running total of what they have spent, using `SUM(...) OVER`.
3. Add a NULL `passenger` row to a copy of the bookings table and show that `NOT IN` returns nothing where `NOT EXISTS` still works.
4. Rewrite `SELECT TOP 5 * FROM flights ORDER BY departs` for PostgreSQL and SQLite, and then with the standard `OFFSET ... FETCH` form.
5. Search a codebase you know for SQL built with string interpolation or `+`. Rewrite one example with parameters.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Revenue counted three times

Revenue by country is 30% too high, only for countries where customers pay in instalments.

**What goes wrong.** Joining orders to payments multiplies each order by its number of payments before SUM.

*Before*

```sql
SELECT c.Country, SUM(o.Total) AS Revenue
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
JOIN Payments p ON p.OrderId = o.Id          -- 3 payments = order counted 3 times
GROUP BY c.Country;
```

*After*

```sql
SELECT c.Country, SUM(o.Total) AS Revenue
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
WHERE EXISTS (SELECT 1 FROM Payments p WHERE p.OrderId = o.Id)   -- filter, don't multiply
GROUP BY c.Country;
```

> **Lesson:** One-to-many joins multiply rows. Aggregate at the right grain, or filter with EXISTS instead of joining.

#### Example 2: Latest booking per customer

Support needs each customer's most recent booking. The old query used a correlated subquery per row.

*Window function*

```sql
WITH ranked AS (
  SELECT b.*, ROW_NUMBER() OVER (PARTITION BY b.CustomerId ORDER BY b.CreatedAt DESC, b.Id DESC) AS rn
  FROM Bookings b
)
SELECT CustomerId, Id, CreatedAt, Total
FROM ranked
WHERE rn = 1;
```

> **Lesson:** Top-N per group is a window function: rank within each partition, then filter.

### Advanced SQL

<p class="src">From <em>.NET Backend Deep Dive</em> · Advanced SQL</p>

<a id="dn-71"></a>

#### Window functions — replacing self-joins and cursors

```sql
-- Rank each customer's orders
SELECT CustomerId, OrderId, Total,
       ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY Total DESC) AS Rn,
       RANK()       OVER (PARTITION BY CustomerId ORDER BY Total DESC) AS Rnk,
       SUM(Total)   OVER (PARTITION BY CustomerId) AS CustomerTotal,
       Total * 100.0 / SUM(Total) OVER (PARTITION BY CustomerId) AS PctOfCustomer
FROM Orders;

-- Each customer's LATEST order (replaces a MAX subquery)
WITH Ranked AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY CreatedAt DESC) rn
    FROM Orders
)
SELECT * FROM Ranked WHERE rn = 1;

-- Compare to the previous row (growth calculation)
SELECT Month, Revenue,
       LAG(Revenue) OVER (ORDER BY Month) AS PrevRevenue,
       Revenue - LAG(Revenue) OVER (ORDER BY Month) AS Delta
FROM MonthlyRevenue;

-- Running total
SELECT OrderDate, Amount,
       SUM(Amount) OVER (ORDER BY OrderDate
                         ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RunningTotal
FROM Payments;
```

EF Core doesn't translate window functions — use raw SQL:

```csharp
var rows = await db.Database.SqlQuery<TopOrderDto>(
    $"SELECT ... ROW_NUMBER() OVER (...) ...").ToListAsync();
```

<a id="dn-74"></a>

#### Temp tables vs table variables

| | `#temp` | `@table` |
|---|---|---|
| Statistics | **Yes** → the optimizer picks a sane plan | No (estimates one row) |
| Indexes | Can be added, even after creation | Only declared inline |
| Use when | > ~100 rows, joined against | Few rows, simple work |
| Transactions | Rolled back | **Not** rolled back |

Common mistake: a `@table` with 500k rows. SQL estimates one row, chooses nested loops, and the query runs for hours.

<a id="dn-75"></a>

#### Batched deletes and updates that don't lock the table

```sql
-- Bad: deleting 10 million rows at once causes lock escalation and floods the log
-- Good: batch it
WHILE 1 = 1
BEGIN
    DELETE TOP (5000) FROM Orders
    WHERE CreatedAt < DATEADD(year, -3, GETDATE());

    IF @@ROWCOUNT < 5000 BREAK;
    WAITFOR DELAY '00:00:00.100';    -- yield to other traffic
END
```

<a id="dn-76"></a>

#### Assorted things worth knowing

```sql
-- EXISTS beats COUNT(*) when you only need "any?"
IF EXISTS (SELECT 1 FROM Orders WHERE CustomerId = @id) ...

-- UNION ALL beats UNION (UNION must sort to deduplicate)

-- Avoid SELECT *: breaks covering indexes, wastes bandwidth, breaks when columns are added

-- NOT IN with a NULL-containing subquery returns nothing unexpectedly. Use NOT EXISTS.

-- Match data types across join keys (nvarchar vs varchar -> implicit convert -> scan)

-- Query Store: enable it to catch performance regressions over time
ALTER DATABASE MyDb SET QUERY_STORE = ON;
```

---

### Foundations

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Foundations</p>

<p class="pd">The model underneath SQL. Most durable database skill comes from understanding relations, sets and three-valued logic — the syntax is the easy part, and it is the part that changes.</p>

<a id="be-d394"></a>

#### The relational model

Codd's 1970 model is the reason relational databases outlived their competitors. Its central idea is that data should be stored as *relations* — sets of tuples — with access defined by logic rather than by navigating pointers. You state *what* you want; the system decides *how*.

| Formal term                | SQL term        | Note                                                         |
|----------------------------|-----------------|--------------------------------------------------------------|
| Relation                   | Table           | A *set* of tuples — no order, no duplicates                  |
| Tuple                      | Row             | An unordered set of attribute/value pairs                    |
| Attribute                  | Column          | Has a name and a domain (type)                               |
| Domain                     | Data type       | The set of values an attribute may take                      |
| Cardinality                | Row count       |                                                              |
| Degree                     | Column count    |                                                              |
| Relation variable (relvar) | The named table | The table is the variable; the relation is its current value |

##### Where SQL deviates from the model, and why it matters

| Relational model says                       | SQL actually does                                       | Consequence                                                                 |
|---------------------------------------------|---------------------------------------------------------|-----------------------------------------------------------------------------|
| A relation is a set — no duplicate tuples   | Tables are *bags*; duplicates are allowed without a key | You must declare keys, or you get duplicates                                |
| No ordering of tuples                       | Same — but people assume insertion order                | Without `ORDER BY`, row order is undefined and may change between runs      |
| No ordering of attributes                   | Columns have a position                                 | `SELECT *` and `INSERT` without a column list break when the schema changes |
| Every attribute has a value from its domain | `NULL` exists                                           | Three-valued logic, and a whole class of surprises                          |

> **\[Note\]**
>
> **The practical takeaway.** Two habits follow directly: always declare a key, and never rely on physical row order. A query that returns rows in a pleasing order today is doing so by accident of the current plan, and will change when an index is added or the optimiser picks a parallel scan.

<a id="be-d395"></a>

#### Relational algebra, and how SQL maps to it

The optimiser thinks in algebra, not in SQL. Knowing the six primitive operations makes execution plans readable, and explains why two differently-written queries can produce the identical plan.

| Operation         | Symbol | SQL               | Produces                             |
|-------------------|--------|-------------------|--------------------------------------|
| Selection         | σ      | `WHERE`           | A subset of rows                     |
| Projection        | π      | `SELECT col, col` | A subset of columns                  |
| Cartesian product | ×      | `CROSS JOIN`      | Every combination                    |
| Union             | ∪      | `UNION`           | Rows in either                       |
| Difference        | −      | `EXCEPT`          | Rows in the first but not the second |
| Rename            | ρ      | `AS`              | The same relation under a new name   |

Everything else is derived. A join is a product followed by a selection. An intersection is two differences. A semi-join is a projection over a join. This is not academic trivia — it is precisely why the optimiser can rewrite your query.

``` text
  the single most important rewrite: PREDICATE PUSHDOWN

  what you wrote                        what the optimiser does

  sigma(total > 100)                    join
        |                                 /        \
      join                       sigma(total>100)  customers
      /    \                            |
  orders  customers                   orders

  filter AFTER joining 10M rows   ->   filter BEFORE, join only the 40k that survive

  the results are provably identical, because selection distributes over join.
  that provability is what makes a declarative language optimisable at all.
```

> **\[Why it matters\]** This is the answer to “why is my hand-tuned query no faster?” and to “why did the same query get slow after a data change?”. You are not writing an execution strategy; you are writing a logical specification that the optimiser is free to reorganise however it can prove is equivalent.

<a id="be-d396"></a>

#### SQL and its logical processing order

SQL is written in one order and evaluated in another. Nearly every “why can't I use my alias there?” question is answered by this list.

``` text
  written                     evaluated (logical order)

  SELECT   ...                1.  FROM        + JOIN     -> build the working set
  FROM     ...                2.  WHERE                  -> filter rows
  WHERE    ...                3.  GROUP BY               -> collapse into groups
  GROUP BY ...                4.  HAVING                 -> filter groups
  HAVING   ...                5.  SELECT      + aliases  -> compute output columns
  ORDER BY ...                6.  DISTINCT
  LIMIT    ...                7.  ORDER BY               -> aliases ARE visible here
                              8.  LIMIT / OFFSET / FETCH
```

``` sql
-- fails: the alias does not exist yet at step 2
SELECT total * 1.21 AS gross FROM orders WHERE gross > 100;      -- ERROR

-- works: ORDER BY is step 7, after SELECT
SELECT total * 1.21 AS gross FROM orders ORDER BY gross DESC;    -- OK

-- fails: WHERE cannot see aggregates, which are computed at step 3
SELECT customer_id, SUM(total) FROM orders
WHERE SUM(total) > 1000 GROUP BY customer_id;                    -- ERROR

-- works: HAVING filters groups, at step 4
SELECT customer_id, SUM(total) FROM orders
GROUP BY customer_id HAVING SUM(total) > 1000;                   -- OK
```

> **\[Trap\]**
>
> **Common trap.** This is the *logical* order, not the physical one. The engine will absolutely evaluate a `WHERE` predicate during the scan, before the join, if it can prove that is equivalent. Logical order defines *meaning*; the plan defines *execution*. Confusing the two leads people to believe they can control execution by reordering clauses. They cannot.

<a id="be-d397"></a>

#### Thinking in sets, not in loops

The single largest performance difference between a competent SQL author and a beginner is not syntax. It is that the beginner writes procedural code in a set-based language.

``` sql
-- procedural thinking, expressed in SQL: a cursor over 200,000 rows
DECLARE cur CURSOR FOR SELECT id, total FROM orders WHERE status = 'Pending';
OPEN cur; FETCH NEXT FROM cur INTO @id, @total;
WHILE @@FETCH_STATUS = 0
BEGIN
    UPDATE orders SET status = 'Expired' WHERE id = @id;   -- 200,000 statements
    FETCH NEXT FROM cur INTO @id, @total;
END
-- ~4 minutes, 200,000 round trips through the executor, 200,000 log records

-- set-based: one statement, one pass, one transaction
UPDATE orders SET status = 'Expired'
WHERE  status = 'Pending' AND placed_at < @cutoff;
-- ~600 ms
```

##### The mental shift

| Procedural instinct                  | Set-based equivalent                      |
|--------------------------------------|-------------------------------------------|
| Loop over rows and update each       | One `UPDATE ... WHERE`                    |
| If exists, update; else insert       | `MERGE`, or `INSERT ... ON CONFLICT`      |
| Loop to build a running total        | `SUM() OVER (ORDER BY ...)`               |
| Loop to find the latest per customer | `ROW_NUMBER() OVER (PARTITION BY ...)`    |
| Nested loops to compare two tables   | `FULL OUTER JOIN` with `IS DISTINCT FROM` |
| Recursion in application code        | Recursive CTE                             |

The reason the set-based form is faster is not that the loop is inefficient C. It is that a single statement gives the optimiser the whole problem at once, so it can choose one scan, one sort, one write pass, and batch the logging. A loop gives it 200,000 unrelated problems and forbids every cross-row optimisation.

> **\[Note\]**
>
> **The exception worth knowing.** Very large single statements can be worse: one `DELETE` of 50 million rows holds locks and generates log for hours. The answer is *batched* set operations — a loop of large set-based statements — not a loop of row-based ones.

<a id="be-d398"></a>

#### NULL and three-valued logic

`NULL` is not a value. It is a marker meaning “no value here”, and it turns boolean logic into three-valued logic: *true*, *false*, *unknown*. Every surprise follows from that.

| Expression       | Result  | Because                                      |
|------------------|---------|----------------------------------------------|
| `NULL = NULL`    | unknown | Two unknowns are not known to be equal       |
| `NULL <> NULL`   | unknown | Nor known to differ                          |
| `NULL + 1`       | `NULL`  | Unknown arithmetic is unknown                |
| `'a' || NULL`    | `NULL`  | Concatenation propagates it (Oracle differs) |
| `TRUE OR NULL`   | true    | Already true regardless                      |
| `FALSE AND NULL` | false   | Already false regardless                     |
| `TRUE AND NULL`  | unknown |                                              |
| `NOT NULL`       | unknown | The negation of unknown is unknown           |

The rule that ties it together: **a row is returned only if the `WHERE` clause evaluates to true.** Unknown is not true, so it is excluded — which is why both a condition and its apparent negation can exclude the same row.

``` sql
-- 100 rows: 60 status='A', 30 status='B', 10 status IS NULL
SELECT count(*) FROM t WHERE status =  'A';    -- 60
SELECT count(*) FROM t WHERE status <> 'A';    -- 30, NOT 40. The NULLs vanish.
SELECT count(*) FROM t WHERE status <> 'A' OR status IS NULL;   -- 40

-- NOT IN with a NULL in the list returns NOTHING, ever
SELECT * FROM orders WHERE customer_id NOT IN (SELECT id FROM banned);
-- if banned.id contains a single NULL, the result is empty.
-- because  x NOT IN (1, NULL)  ==  x<>1 AND x<>NULL  ==  x<>1 AND unknown  ==  unknown

-- NOT EXISTS is null-safe, and usually optimises better too
SELECT * FROM orders o
WHERE NOT EXISTS (SELECT 1 FROM banned b WHERE b.id = o.customer_id);
```

###### Null-safe comparison, and aggregate behaviour

``` csharp
-- null-safe equality, per dialect
a IS NOT DISTINCT FROM b     -- ANSI / PostgreSQL: TRUE when both are NULL
a <=> b                      -- MySQL
EXISTS (SELECT a INTERSECT SELECT b)   -- SQL Server idiom, works but obscure
COALESCE(a,'~') = COALESCE(b,'~')      -- portable hack; needs a value that cannot occur

-- aggregates IGNORE nulls, except COUNT(*)
SELECT count(*),        -- 100  : counts rows
       count(status),   --  90  : counts non-null values
       avg(amount)      -- divides by the count of NON-NULL amounts, not by 100
FROM t;
-- so AVG over a column with nulls is not sum/rowcount, which is a classic reporting bug.

-- UNIQUE constraints treat nulls as distinct (in most engines), so a "unique" column
-- can hold many nulls. Use a filtered unique index if you need one-null-only semantics.
```

| Design question                               | Guidance                                                                                           |
|-----------------------------------------------|----------------------------------------------------------------------------------------------------|
| Should this column be nullable?               | Default to `NOT NULL`. Every nullable column is an extra state the application must handle         |
| Use a sentinel instead? (0, '', '1900-01-01') | No. Sentinels lie to aggregates and to `MIN`/`MAX`, and everyone forgets which sentinel means what |
| Nullable foreign key                          | Legitimate — it means an optional relationship. Prefer it to a magic 'unknown' row                 |
| Many nullable columns on one table            | Often a sign that one table is modelling several entity types — consider splitting                 |

There is a real philosophical objection to NULL — Codd himself proposed distinguishing ‘missing but applicable’ from ‘missing and inapplicable’ — and Date argued it should not exist. You still have to work with it. The practical position: minimise nullable columns, know the three-valued rules cold, and reach for `NOT EXISTS` over `NOT IN` as a reflex.

<a id="be-d399"></a>

#### Data types, and why the choice compounds

A type is not just a validation rule. It determines storage size, comparison semantics, index efficiency, and what the optimiser can prove — and it is the hardest thing to change later.

| Category    | Choose                                          | Avoid                       | Reason                                            |
|-------------|-------------------------------------------------|-----------------------------|---------------------------------------------------|
| Integers    | `int`, `bigint`                                 | Oversized defaults          | Narrower keys mean more rows per index page       |
| Money       | `decimal(19,4)`, or minor units                 | `float`, `real`             | Binary floats cannot represent 0.1 exactly        |
| Text        | `varchar(n)` / `text`                           | `char(n)` for variable data | `char` pads with spaces, which affects comparison |
| Timestamps  | `timestamptz` / `datetimeoffset`                | `datetime`                  | An instant without an offset is ambiguous         |
| Dates only  | `date`                                          | A timestamp at midnight     | Avoids timezone shifting a birthday by a day      |
| Booleans    | `boolean` / `bit`                               | `char(1)` 'Y'/'N'           | Self-documenting and one byte or less             |
| Identifiers | `uuid`                                          | `varchar(36)`               | 16 bytes vs 36, and no case or format ambiguity   |
| Enumerated  | Small int + lookup table, or a check constraint | Free text                   | The database can then reject invalid values       |

###### Fixed vs variable length, and what it costs

``` sql
-- these are NOT equivalent
CREATE TABLE a (code char(10));      -- always 10 bytes, space-padded
CREATE TABLE b (code varchar(10));   -- length prefix + actual bytes

INSERT INTO a VALUES ('AB');   -- stored as 'AB        '
SELECT * FROM a WHERE code = 'AB';   -- matches, because comparison ignores trailing spaces
SELECT length(code) FROM a;          -- 10, which surprises people

-- and in PostgreSQL, varchar(n) vs text is a length CHECK, not a storage difference:
-- both use the same variable-length representation. varchar(n) buys you validation only.
```

| Decision                     | Effect on storage                    | Effect on performance                                          |
|------------------------------|--------------------------------------|----------------------------------------------------------------|
| Narrower integer keys        | Directly smaller indexes             | More entries per page → shallower tree → fewer reads           |
| `NOT NULL`                   | May avoid a null-bitmap bit          | Lets the optimiser skip null handling and use some index paths |
| Wide `varchar` actually used | Rows exceed a page → off-row storage | An extra read per row when the column is touched               |
| Column order in the table    | Alignment padding (PostgreSQL)       | Ordering wide-to-narrow can measurably shrink a table          |
| An over-precise `decimal`    | More bytes per value                 | Slower arithmetic than integer types                           |

> **\[Note\]**
>
> **Widening is easy; narrowing is not.** Growing `varchar(50)` to `varchar(100)` is usually a metadata-only change. Shrinking it, changing `int` to `bigint`, or changing a type at all typically rewrites the whole table under a lock. Choose with the ten-year view, and prefer the smallest type that certainly fits.

<a id="be-d400"></a>

#### Constraints: the integrity the application cannot enforce

Application-level validation protects against your own code. Database constraints protect against every other writer: a second service, a migration script, a support engineer with a SQL console, and your own code with a bug. They are the only guarantees that actually hold.

| Constraint    | Guarantees                          | Note                                                       |
|---------------|-------------------------------------|------------------------------------------------------------|
| `NOT NULL`    | A value is present                  | The cheapest and most under-used                           |
| `PRIMARY KEY` | Unique and not null                 | One per table; usually also the clustering key             |
| `UNIQUE`      | No duplicates                       | Nulls are typically treated as distinct                    |
| `FOREIGN KEY` | The referenced row exists           | Also documents the model, and informs the optimiser        |
| `CHECK`       | An arbitrary row-level predicate    | Cheap, and enforced for every writer                       |
| `EXCLUDE`     | No two rows conflict on an operator | PostgreSQL — the correct tool for non-overlapping bookings |
| `DEFAULT`     | A value when none is supplied       | Not a constraint, but part of the same contract            |

``` sql
CREATE TABLE bookings (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    room_id     bigint NOT NULL REFERENCES rooms(id) ON DELETE RESTRICT,
    guest_id    bigint NOT NULL REFERENCES guests(id),
    period      tstzrange NOT NULL,
    price       numeric(19,4) NOT NULL CHECK (price >= 0),
    status      text NOT NULL DEFAULT 'held'
                CHECK (status IN ('held','confirmed','cancelled')),

    -- the constraint an application cannot reliably enforce under concurrency:
    -- no two active bookings for the same room may overlap in time
    EXCLUDE USING gist (room_id WITH =, period WITH &&)
        WHERE (status <> 'cancelled')
);
```

That last constraint is worth dwelling on. Checking “does an overlapping booking exist?” in application code is a read followed by a write, and two concurrent sessions can both read ‘no’ and both write. Only the database can make that atomic. Any rule of the form *“no two rows may…”* belongs in a constraint, not in a service.

###### Foreign keys: the recurring argument

**The case against**

- A cost on every insert and update
- Awkward for bulk loads and for sharding
- Deletion order becomes constrained
- ‘The application guarantees it anyway’

**The case for**

- The application does not, in fact, guarantee it
- Orphan rows are silent and accumulate for years
- The optimiser uses them to eliminate joins
- They document the model better than any wiki

``` sql
-- what the optimiser can do with a trusted foreign key: eliminate the join entirely
SELECT o.id, o.total
FROM   orders o
JOIN   customers c ON c.id = o.customer_id;     -- no columns from c are selected

-- with a NOT NULL foreign key, the join cannot add or remove rows,
-- so the optimiser removes it. Without the constraint it must perform the join.
```

| Referential action       | On delete of the parent | Use when                                      |
|--------------------------|-------------------------|-----------------------------------------------|
| `RESTRICT` / `NO ACTION` | Refuse                  | The default, and the right default            |
| `CASCADE`                | Delete the children     | Genuine ownership — order lines with an order |
| `SET NULL`               | Null the reference      | An optional relationship                      |
| `SET DEFAULT`            | Point at a default row  | Rare; needs the default row to exist          |

> **\[Trap\]**
>
> **Common trap.** `ON DELETE CASCADE` across several levels can delete far more than intended from a single statement, with no confirmation and no easy undo. It is correct for tightly-owned children and dangerous everywhere else. If you would be alarmed to see the row count deleted, do not cascade — make the application delete explicitly, so the intent is visible.

### Querying

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Querying</p>

<p class="pd">Writing SQL that says what you mean. Joins, aggregation, subqueries, CTEs and window functions, plus the handful of query patterns that solve most real reporting problems.</p>

<a id="be-d410"></a>

#### Joins, precisely

A join is a Cartesian product filtered by a predicate. Everything else — inner, outer, semi, anti — is a variation on which unmatched rows survive.

``` text
  A          B              INNER      LEFT       RIGHT      FULL
  --         --             -----      ----       -----      ----
  1  x       1  p           1 x p      1 x p      1 x p      1 x p
  2  y       3  q           3 z q      2 y -      3 z q      2 y -
  3  z       4  r                      3 z q      - - r      3 z q
                                                             - - r

  INNER  matched rows only
  LEFT   all of A, plus matches from B (nulls where none)
  RIGHT  all of B, plus matches from A
  FULL   all of both
  CROSS  every combination, no predicate
```

| Join                      | Returns                                     | Typical use                                             |
|---------------------------|---------------------------------------------|---------------------------------------------------------|
| `INNER`                   | Matched pairs                               | The default                                             |
| `LEFT OUTER`              | All left rows                               | “Customers and their orders, including those with none” |
| `FULL OUTER`              | All rows from both                          | Reconciling two sources                                 |
| `CROSS`                   | Every combination                           | Generating grids, date spines                           |
| `LATERAL` / `CROSS APPLY` | A correlated subquery per row               | Top-N per group, table functions                        |
| Semi (`EXISTS`)           | Left rows that have a match, no duplication | “Customers who ordered”                                 |
| Anti (`NOT EXISTS`)       | Left rows with no match                     | “Customers who never ordered”                           |

``` sql
-- the classic mistake: a filter on the outer table turns LEFT into INNER
SELECT c.name, o.id
FROM   customers c
LEFT   JOIN orders o ON o.customer_id = c.id
WHERE  o.status = 'Open';          -- customers with no orders have o.status = NULL,
                                   -- NULL = 'Open' is unknown, so they are excluded.

-- put the condition in the JOIN when it should limit the MATCH,
-- and in WHERE when it should limit the RESULT
SELECT c.name, o.id
FROM   customers c
LEFT   JOIN orders o ON o.customer_id = c.id AND o.status = 'Open';
```

###### Semi-joins, anti-joins and duplicate explosion

``` sql
-- these look equivalent and are not
-- 1. JOIN: duplicates the customer once per matching order
SELECT c.* FROM customers c JOIN orders o ON o.customer_id = c.id WHERE o.total > 100;

-- 2. SEMI-JOIN: each customer at most once, and the engine can stop at the first match
SELECT c.* FROM customers c
WHERE EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = c.id AND o.total > 100);

-- 3. DISTINCT over the join: same result as 2, but it materialises and dedupes
--    the whole exploded set first. Usually slower.
SELECT DISTINCT c.* FROM customers c JOIN orders o ON ... WHERE o.total > 100;
```

A `DISTINCT` that exists only to undo duplication caused by a join is a signal that you wanted a semi-join. The optimiser can often rewrite it, and often cannot — and the `EXISTS` form also states the intent, which the next reader will thank you for.

``` sql
-- LATERAL / CROSS APPLY: a subquery that can reference the outer row
SELECT c.id, c.name, recent.id AS last_order, recent.placed_at
FROM   customers c
LEFT   JOIN LATERAL (                      -- SQL Server: OUTER APPLY
    SELECT o.id, o.placed_at
    FROM   orders o
    WHERE  o.customer_id = c.id            -- <- correlation, impossible in a plain join
    ORDER  BY o.placed_at DESC
    LIMIT  1
) recent ON true;
-- the top-N-per-group problem, solved directly. With an index on
-- (customer_id, placed_at DESC) this is one seek per customer.
```

> **\[Trap\]**
>
> **Common trap.** A join predicate that is accidentally omitted produces a Cartesian product, not an error. Ten thousand rows joined to ten thousand rows is a hundred million, and the query does not fail — it runs for an hour and fills tempdb. Comma-separated `FROM a, b WHERE ...` syntax makes this easy to do by accident, which is one reason explicit `JOIN` syntax is preferred.

<a id="be-d411"></a>

#### Aggregation, GROUP BY and HAVING

Aggregation collapses a set of rows into one row per group. The rule that follows: every column in `SELECT` must either be in `GROUP BY` or be inside an aggregate, because anything else has no single value for the group.

``` sql
SELECT   c.country,
         count(*)                                   AS orders,
         count(DISTINCT o.customer_id)              AS customers,
         sum(o.total)                               AS revenue,
         avg(o.total)                               AS avg_order,
         percentile_cont(0.5) WITHIN GROUP (ORDER BY o.total) AS median_order,
         min(o.placed_at)                           AS first_order
FROM     orders o
JOIN     customers c ON c.id = o.customer_id
WHERE    o.placed_at >= :from            -- filters ROWS, before grouping
GROUP BY c.country
HAVING   sum(o.total) > 10000            -- filters GROUPS, after
ORDER BY revenue DESC;
```

| Function                    | Note                                                                            |
|-----------------------------|---------------------------------------------------------------------------------|
| `count(*)`                  | Counts rows, including those with nulls                                         |
| `count(col)`                | Counts non-null values — frequently the source of a reporting bug               |
| `count(DISTINCT col)`       | Expensive: usually requires a sort or hash of all values                        |
| `sum` / `avg`               | Ignore nulls; `avg` divides by the non-null count                               |
| `string_agg` / `STRING_AGG` | Concatenate a group; specify `ORDER BY` inside it                               |
| `array_agg` / `json_agg`    | Collapse a child set into one column — avoids a second round trip               |
| `filter` clause             | `count(*) FILTER (WHERE status='Open')` — conditional aggregates without `CASE` |

``` sql
-- conditional aggregation: one pass, many measures. Far better than N subqueries.
SELECT country,
       count(*)                                            AS total,
       count(*) FILTER (WHERE status = 'Open')             AS open,       -- PostgreSQL
       sum(CASE WHEN status = 'Open' THEN 1 ELSE 0 END)    AS open_ansi,  -- portable
       sum(total) FILTER (WHERE placed_at >= :from)        AS recent_revenue
FROM   orders GROUP BY country;
```

###### GROUPING SETS, ROLLUP and CUBE

``` sql
-- subtotals and a grand total in ONE pass over the data
SELECT   COALESCE(country, 'ALL') AS country,
         COALESCE(status,  'ALL') AS status,
         sum(total) AS revenue,
         GROUPING(country) AS is_country_total
FROM     orders
GROUP BY ROLLUP (country, status);

-- produces:  (NL, Open) (NL, Shipped) (NL, ALL) (BE, Open) ... (ALL, ALL)

GROUP BY GROUPING SETS ((country), (status), ())   -- exactly the combinations you want
GROUP BY CUBE (country, status)                    -- every combination
```

The alternative — three queries with `UNION ALL` — scans the table three times. `ROLLUP` scans once and computes all levels together, which on a large fact table is the difference between a report that runs in seconds and one that runs in minutes.

> **\[Trap\]**
>
> **Common trap.** `GROUP BY` on a column with high cardinality and no supporting index forces a sort or a hash of the entire input. On large tables the aggregation itself, not the scan, becomes the dominant cost — watch for a `Sort` or `Hash Aggregate` spilling to disk in the plan.

<a id="be-d412"></a>

#### Subqueries, derived tables and CTEs

Four ways to nest a query, with real differences in readability and sometimes in plan.

| Form            | Shape                               | Note                                                 |
|-----------------|-------------------------------------|------------------------------------------------------|
| Scalar subquery | `SELECT (SELECT max(x) FROM t) ...` | Must return one row, one column                      |
| `IN` / `EXISTS` | In `WHERE`                          | `EXISTS` is null-safe; `IN` is not                   |
| Derived table   | `FROM (SELECT ...) x`               | Inline; scoped to one use                            |
| CTE             | `WITH x AS (SELECT ...)`            | Named, reusable within the statement, reads top-down |

``` sql
-- correlated subquery: conceptually runs per outer row
SELECT c.name,
       (SELECT count(*) FROM orders o WHERE o.customer_id = c.id) AS order_count
FROM   customers c;
-- the optimiser usually rewrites this into a join or a hash aggregate.
-- "usually" is doing work: check the plan on a large table.

-- the same thing as an explicit aggregate join, which is easier to reason about
SELECT c.name, COALESCE(oc.n, 0) AS order_count
FROM   customers c
LEFT   JOIN (SELECT customer_id, count(*) AS n FROM orders GROUP BY customer_id) oc
       ON oc.customer_id = c.id;
```

``` sql
-- CTEs make a multi-step calculation readable, and each step testable
WITH monthly AS (
    SELECT date_trunc('month', placed_at) AS month, customer_id, sum(total) AS revenue
    FROM   orders
    WHERE  placed_at >= :from
    GROUP  BY 1, 2
),
ranked AS (
    SELECT *, rank() OVER (PARTITION BY month ORDER BY revenue DESC) AS rk
    FROM   monthly
)
SELECT month, customer_id, revenue
FROM   ranked
WHERE  rk <= 10
ORDER  BY month DESC, rk;
```

###### The optimisation-fence question

Whether a CTE is inlined into the surrounding query or materialised separately used to be a major engine-specific gotcha, and it still matters.

| Engine           | Behaviour                                                                                         |
|------------------|---------------------------------------------------------------------------------------------------|
| PostgreSQL \< 12 | CTEs were always an **optimisation fence** — materialised, predicates never pushed in             |
| PostgreSQL 12+   | Inlined when referenced once and not recursive; override with `MATERIALIZED` / `NOT MATERIALIZED` |
| SQL Server       | Always inlined — a CTE referenced three times is executed three times                             |
| Oracle, MySQL 8+ | Cost-based; may inline or materialise                                                             |

``` sql
-- PostgreSQL: force materialisation when the CTE is expensive and reused
WITH expensive AS MATERIALIZED (
    SELECT ... -- a heavy aggregation
)
SELECT * FROM expensive a JOIN expensive b ON ...;   -- computed once

-- SQL Server: a CTE referenced twice runs twice. If that is expensive,
-- materialise it yourself:
SELECT ... INTO #expensive FROM ...;
CREATE INDEX ix ON #expensive (key);
```

> **\[Note\]**
>
> **Practical guidance.** Use CTEs freely for readability — a five-step CTE chain is enormously easier to follow than five levels of nested subquery, and easier to debug because you can select from each step in turn. Just check the plan when a CTE is referenced more than once or wraps something expensive.

<a id="be-d413"></a>

#### Recursive CTEs

The only looping construct in standard SQL. A recursive CTE has an anchor member, a recursive member that references the CTE itself, and a termination condition — which you must supply, because the engine will not.

``` sql
WITH RECURSIVE org AS (
    -- anchor: the starting rows
    SELECT id, manager_id, name, 1 AS level, name::text AS chain
    FROM   employees WHERE manager_id IS NULL

    UNION ALL          -- UNION ALL, not UNION: UNION would deduplicate every iteration

    -- recursive member: references 'org'
    SELECT e.id, e.manager_id, e.name, o.level + 1, o.chain || ' > ' || e.name
    FROM   employees e
    JOIN   org o ON e.manager_id = o.id
    WHERE  o.level < 20                          -- termination guard
)
SELECT level, chain, name FROM org ORDER BY chain;
```

##### The other classic use: generating a series

``` sql
-- a date spine, so that days with no data still appear in a report
WITH RECURSIVE days AS (
    SELECT date '2026-01-01' AS d
    UNION ALL
    SELECT d + 1 FROM days WHERE d < date '2026-12-31'
)
SELECT d, COALESCE(count(o.id), 0) AS orders
FROM   days LEFT JOIN orders o ON o.placed_at::date = d
GROUP  BY d ORDER BY d;

-- PostgreSQL has a shortcut, and it is faster:
SELECT d::date FROM generate_series('2026-01-01'::date, '2026-12-31', '1 day') d;
```

###### Termination, cycles and cost

``` sql
-- SQL Server bounds recursion at 100 iterations by default; raise or remove it
SELECT ... OPTION (MAXRECURSION 1000);     -- 0 = unlimited, which is a loaded gun

-- PostgreSQL has NO default limit. An unguarded cycle runs until the disk fills.
-- guard explicitly by tracking the path:
WITH RECURSIVE walk AS (
    SELECT id, ARRAY[id] AS path, false AS cycle FROM nodes WHERE id = :start
  UNION ALL
    SELECT n.id, w.path || n.id, n.id = ANY(w.path)
    FROM   nodes n JOIN edges e ON e.to_id = n.id
    JOIN   walk w ON e.from_id = w.id
    WHERE  NOT w.cycle
)
SELECT * FROM walk WHERE NOT cycle;

-- PostgreSQL 14+ has native syntax for this
WITH RECURSIVE walk AS (...) CYCLE id SET is_cycle USING path
```

- **Always bound the depth**, even when the data ‘cannot’ contain a cycle. One bad row is all it takes.
- **Index the recursion column.** A recursive CTE over an unindexed `parent_id` performs a full scan *per level*.
- **Estimate the fan-out.** Recursion over a graph with high branching can produce vastly more rows than the table contains.
- **Recursive CTEs are not parallelised** in most engines, so they are inherently single-threaded.
- **For read-heavy hierarchies**, a closure table or a materialised path usually beats recursing on every query.

<a id="be-d414"></a>

#### Window functions

The single most valuable SQL feature that many developers never learn. A window function computes across a set of rows related to the current row — without collapsing them, which is what distinguishes it from an aggregate.

``` text
  GROUP BY                              OVER ()

  collapses rows into one per group     keeps every row, adds a computed column

  country | revenue                     order_id | country | total | country_total | rank
  --------|--------                     ---------|---------|-------|---------------|-----
  NL      | 5000                        1001     | NL      | 2000  | 5000          | 1
  BE      | 3000                        1002     | NL      | 3000  | 5000          | 1
                                        1003     | BE      | 3000  | 3000          | 1
```

``` sql
SELECT
  order_id, country, total,

  -- aggregate over a partition, without collapsing
  sum(total)   OVER (PARTITION BY country)                          AS country_total,
  total / sum(total) OVER (PARTITION BY country)                    AS pct_of_country,

  -- ranking
  row_number() OVER (PARTITION BY country ORDER BY total DESC)      AS rn,   -- 1,2,3,4
  rank()       OVER (PARTITION BY country ORDER BY total DESC)      AS rk,   -- 1,2,2,4
  dense_rank() OVER (PARTITION BY country ORDER BY total DESC)      AS drk,  -- 1,2,2,3
  ntile(4)     OVER (ORDER BY total)                                AS quartile,

  -- navigation
  lag(total)   OVER (PARTITION BY customer_id ORDER BY placed_at)   AS prev_order,
  lead(placed_at) OVER (PARTITION BY customer_id ORDER BY placed_at) AS next_order_at,
  first_value(total) OVER (PARTITION BY customer_id ORDER BY placed_at) AS first_order,

  -- running totals, via an explicit frame
  sum(total) OVER (PARTITION BY customer_id ORDER BY placed_at
                   ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)  AS running_total,

  -- a 7-row moving average
  avg(total) OVER (ORDER BY placed_at ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS ma7
FROM orders;
```

###### Frames: the part that trips everyone

``` text
  the frame defines WHICH rows the function sees, relative to the current row

    ROWS   BETWEEN 2 PRECEDING AND CURRENT ROW     physical rows: exactly 3
    RANGE  BETWEEN 2 PRECEDING AND CURRENT ROW     logical: all PEER rows too

  with ties in the ORDER BY column these differ, and the default is RANGE:

    default frame when ORDER BY is present:
      RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
      -> a running total INCLUDES every row tied with the current one,
         so tied rows all show the SAME running total. Rarely what is wanted.

    default frame with NO ORDER BY:
      the whole partition
```

``` text
-- these give different answers when two orders share a timestamp
sum(total) OVER (ORDER BY placed_at)                                          -- RANGE
sum(total) OVER (ORDER BY placed_at ROWS UNBOUNDED PRECEDING)                 -- ROWS

-- so: be explicit about the frame whenever you use ORDER BY with an aggregate.
-- ranking functions (row_number, rank) ignore the frame entirely.
```

| Need                        | Function                                                                                    |
|-----------------------------|---------------------------------------------------------------------------------------------|
| Exactly one row per group   | `row_number()` — ties broken arbitrarily but deterministically if you add a tiebreak column |
| All tied rows at the top    | `rank()`                                                                                    |
| Ranks with no gaps          | `dense_rank()`                                                                              |
| Compare to the previous row | `lag()`                                                                                     |
| Percent of total            | `x / sum(x) OVER (PARTITION BY ...)`                                                        |
| Buckets                     | `ntile(n)`                                                                                  |
| Deduplicate                 | `row_number()` in a CTE, then `WHERE rn = 1`                                                |

Window functions are evaluated after `WHERE`, `GROUP BY` and `HAVING`, at step 5. That is why you cannot filter on one directly and must wrap it in a CTE or derived table first — a rule that follows directly from the logical processing order.

<a id="be-d415"></a>

#### Query patterns worth memorising

Six shapes that between them solve a large share of real reporting and data-quality work.

##### 1. Top-N per group

``` sql
-- the most-asked SQL question there is
WITH ranked AS (
  SELECT o.*, row_number() OVER (PARTITION BY customer_id ORDER BY placed_at DESC) rn
  FROM orders o
)
SELECT * FROM ranked WHERE rn <= 3;

-- for N=1 with a supporting index, LATERAL is usually faster:
-- it seeks once per customer instead of ranking every row.
SELECT c.id, o.*
FROM   customers c
CROSS  JOIN LATERAL (SELECT * FROM orders o WHERE o.customer_id = c.id
                     ORDER BY placed_at DESC LIMIT 1) o;

-- PostgreSQL has a shortcut for N=1
SELECT DISTINCT ON (customer_id) * FROM orders ORDER BY customer_id, placed_at DESC;
```

##### 2. Deduplication

``` sql
DELETE FROM t WHERE ctid IN (                          -- PostgreSQL
  SELECT ctid FROM (
    SELECT ctid, row_number() OVER (PARTITION BY email ORDER BY created_at) rn
    FROM t) x WHERE rn > 1);

-- prevent it recurring, which is the actual fix
CREATE UNIQUE INDEX ux_t_email ON t (lower(email));
```

##### 3. Gaps and islands

``` sql
-- find consecutive runs: the trick is that (row_number - value) is CONSTANT within a run
WITH grouped AS (
  SELECT user_id, activity_date,
         activity_date - (row_number() OVER (PARTITION BY user_id
                                             ORDER BY activity_date))::int AS grp
  FROM   daily_activity
)
SELECT user_id, min(activity_date) AS streak_start, max(activity_date) AS streak_end,
       count(*) AS streak_days
FROM   grouped GROUP BY user_id, grp HAVING count(*) >= 7;
```

##### 4. Pivot

``` sql
-- portable: conditional aggregation. Works everywhere, and the columns are fixed.
SELECT customer_id,
       sum(total) FILTER (WHERE quarter = 1) AS q1,
       sum(total) FILTER (WHERE quarter = 2) AS q2,
       sum(total) FILTER (WHERE quarter = 3) AS q3,
       sum(total) FILTER (WHERE quarter = 4) AS q4
FROM   quarterly GROUP BY customer_id;
-- a truly dynamic pivot needs dynamic SQL. Usually the presentation layer's job.
```

##### 5. Reconciling two sources

``` sql
SELECT COALESCE(a.id, b.id) AS id,
       CASE WHEN b.id IS NULL THEN 'missing in B'
            WHEN a.id IS NULL THEN 'missing in A'
            ELSE 'differs' END AS issue,
       a.amount AS a_amount, b.amount AS b_amount
FROM   source_a a
FULL   OUTER JOIN source_b b ON b.id = a.id
WHERE  a.id IS NULL OR b.id IS NULL
   OR  a.amount IS DISTINCT FROM b.amount;      -- null-safe inequality
```

##### 6. Keyset pagination

``` sql
-- OFFSET degrades linearly: page 500 must read and discard 24,950 rows
SELECT * FROM orders ORDER BY placed_at DESC OFFSET 24950 LIMIT 50;   -- slow, and unstable

-- keyset: constant time, and stable when rows are inserted between pages
SELECT * FROM orders
WHERE  (placed_at, id) < (:last_placed_at, :last_id)     -- row-value comparison
ORDER  BY placed_at DESC, id DESC
LIMIT  50;
-- the tiebreak on id is essential: without it, two rows sharing a timestamp
-- straddle a page boundary and one is silently skipped forever.
```

> **\[Note\]**
>
> **Why memorise these.** Each replaces a procedural loop or a multi-pass export that people otherwise write in application code. Recognising that a request is “top-N per group” or “gaps and islands” turns an afternoon of fiddly code into six lines of SQL that the database can optimise.

<a id="be-d416"></a>

#### Set operations and data comparison

| Operator           | Returns                      | Duplicates                        | Null handling              |
|--------------------|------------------------------|-----------------------------------|----------------------------|
| `UNION`            | Rows in either               | Removed — requires a sort or hash | Nulls are treated as equal |
| `UNION ALL`        | Rows in either               | Kept — cheaper, use it by default | —                          |
| `INTERSECT`        | Rows in both                 | Removed                           | Nulls match                |
| `EXCEPT` / `MINUS` | In the first, not the second | Removed                           | Nulls match                |

Note the asymmetry with `=`: set operators compare rows using *not distinct from* semantics, so two rows that are null in the same column are considered equal. That makes `EXCEPT` the easiest correct way to diff two result sets.

``` csharp
-- "are these two queries identical?" - a one-line regression test for a refactor
(SELECT * FROM old_query EXCEPT SELECT * FROM new_query)
UNION ALL
(SELECT * FROM new_query EXCEPT SELECT * FROM old_query);
-- empty result = identical, including null placement and duplicates-as-sets.

-- always prefer UNION ALL unless you actually need deduplication:
-- UNION on 10 million rows sorts or hashes all of them for nothing.
```

> **\[Trap\]**
>
> **Common trap.** `UNION` where `UNION ALL` would do is one of the most common silent performance mistakes in reporting SQL. If the branches are disjoint by construction — different date ranges, different statuses — deduplication cannot remove anything, and you are paying for a full sort to prove it.

<a id="be-d417"></a>

#### Upsert: ON CONFLICT and MERGE

“Insert it, or update it if it already exists” looks trivial and is one of the most reliable sources of race conditions in application code. Doing it in two statements is wrong under concurrency; doing it in one is correct.

``` sql
-- WRONG. Two sessions can both see "not exists" and both insert.
IF NOT EXISTS (SELECT 1 FROM inventory WHERE sku = :sku)
    INSERT INTO inventory (sku, qty) VALUES (:sku, :qty);
ELSE
    UPDATE inventory SET qty = qty + :qty WHERE sku = :sku;
-- outcome: a unique violation, or a lost update. Both happen in production.
```

``` sql
-- PostgreSQL / SQLite: ON CONFLICT. Atomic, and it needs a unique constraint to target.
INSERT INTO inventory (sku, qty, updated_at)
VALUES (:sku, :qty, now())
ON CONFLICT (sku) DO UPDATE
SET qty        = inventory.qty + EXCLUDED.qty,   -- EXCLUDED = the row we tried to insert
    updated_at = now()
WHERE inventory.qty + EXCLUDED.qty >= 0          -- optional guard; no update if it fails
RETURNING id, qty;                               -- RETURNING works either way

-- ignore duplicates instead of updating
INSERT INTO tags (name) VALUES (:name) ON CONFLICT (name) DO NOTHING;

-- MySQL
INSERT INTO inventory (sku, qty) VALUES (:sku, :qty)
ON DUPLICATE KEY UPDATE qty = qty + VALUES(qty);
```

``` csharp
-- ANSI MERGE: PostgreSQL 15+, SQL Server, Oracle. More expressive, more verbose.
MERGE INTO inventory AS t
USING (VALUES (:sku, :qty)) AS s (sku, qty)
   ON t.sku = s.sku
WHEN MATCHED AND t.qty + s.qty = 0 THEN DELETE
WHEN MATCHED                       THEN UPDATE SET qty = t.qty + s.qty, updated_at = now()
WHEN NOT MATCHED                   THEN INSERT (sku, qty, updated_at)
                                        VALUES (s.sku, s.qty, now());
```

|                               | `ON CONFLICT`                       | `MERGE`                                 |
|-------------------------------|-------------------------------------|-----------------------------------------|
| Needs a unique index          | Yes — that is what it detects       | No — it joins on a condition            |
| Can delete                    | No                                  | Yes                                     |
| Multiple conditional branches | No                                  | Yes                                     |
| Concurrency-safe              | Yes, by construction                | **Depends** — see below                 |
| Bulk source                   | `INSERT ... SELECT ... ON CONFLICT` | Natural — join to a table               |
| Verdict                       | The default for single-row upsert   | Bulk merges, and when you need `DELETE` |

###### Why MERGE is not automatically safe

`ON CONFLICT` is safe because it is implemented as an index-level conflict detection: the engine attempts the insert, hits the unique index, and takes the update path with the row already locked. `MERGE` is a join, and a join can be evaluated before another session commits.

``` text
  MERGE under concurrency, at READ COMMITTED

  T1: MERGE ... ON t.sku = 'ABC'      -> no match, so it takes the INSERT branch
  T2: MERGE ... ON t.sku = 'ABC'      -> no match either (T1 has not committed)
  T1: INSERT 'ABC'                       COMMIT
  T2: INSERT 'ABC'                    -> UNIQUE VIOLATION

  MERGE does not serialise the "did it exist?" check for rows that do not yet exist.

  fixes, in order of preference
    1. use ON CONFLICT instead for single-row upserts
    2. keep the unique constraint and RETRY on violation - correct, and simple
    3. SERIALIZABLE isolation, with a retry loop
    4. take an explicit lock on a parent row first
```

``` sql
-- the bulk pattern that IS safe and fast: stage, then one set-based upsert
CREATE TEMP TABLE staging (sku text, qty int) ON COMMIT DROP;
COPY staging FROM STDIN CSV;                       -- fast bulk load
ANALYZE staging;                                   -- give the planner statistics

INSERT INTO inventory (sku, qty, updated_at)
SELECT sku, sum(qty), now()
FROM   staging
GROUP  BY sku                                      -- CRUCIAL: dedupe the source first
ON CONFLICT (sku) DO UPDATE
SET qty = inventory.qty + EXCLUDED.qty, updated_at = now();
```

> **\[Trap\]**
>
> **Common trap.** `ON CONFLICT` cannot update the same row twice in one statement — if the source contains two rows with the same key, it raises *“ON CONFLICT DO UPDATE command cannot affect row a second time”*. Always aggregate or deduplicate the source before the upsert. This is the single most common failure when moving from single-row to bulk upserts.

<a id="be-d418"></a>

#### JSON and semi-structured data in SQL

Every major engine now stores, indexes and queries JSON, which removes most of the historical reason to add a document database alongside a relational one. The skill is knowing which parts of your data belong in columns and which in a document.

| Engine     | Type                                               | Indexing                     | Notes                                                                          |
|------------|----------------------------------------------------|------------------------------|--------------------------------------------------------------------------------|
| PostgreSQL | `jsonb`                                            | GIN, plus expression B-trees | Binary, deduplicated keys, rich operators — the strongest implementation       |
| PostgreSQL | `json`                                             | None useful                  | Stores text verbatim; preserves key order and duplicates. Rarely what you want |
| SQL Server | `nvarchar` + JSON functions; native `json` in 2025 | Computed column + index      | `OPENJSON`, `JSON_VALUE`, `JSON_QUERY`                                         |
| MySQL      | `JSON`                                             | Multi-valued indexes         | Binary storage; good function coverage                                         |
| Oracle     | `JSON`                                             | Search index                 | Full SQL/JSON support                                                          |

``` sql
CREATE TABLE orders (
    id       bigint PRIMARY KEY,
    tenant_id uuid NOT NULL,          -- queried, joined, constrained  -> a COLUMN
    status   text NOT NULL,           -- filtered, indexed             -> a COLUMN
    total    numeric(19,4) NOT NULL,  -- summed, compared              -> a COLUMN
    metadata jsonb NOT NULL DEFAULT '{}'   -- per-tenant, open-ended   -> JSON
);

-- containment index: good for @> and key existence
CREATE INDEX ix_orders_meta ON orders USING gin (metadata jsonb_path_ops);

-- expression index: for one hot path, far smaller and supports ranges/sorting
CREATE INDEX ix_orders_channel ON orders ((metadata->>'channel'));
CREATE INDEX ix_orders_score   ON orders (((metadata->>'score')::numeric));
```

``` sql
-- the operators worth knowing
SELECT metadata->'shipping'                      -- -> returns jsonb
     , metadata->>'channel'                      -- ->> returns text
     , metadata#>'{shipping,address,city}'       -- #> path, returns jsonb
     , metadata#>>'{shipping,address,city}'      -- #>> path, returns text
FROM orders;

WHERE metadata @> '{"channel":"web"}'            -- containment           (GIN)
WHERE metadata ? 'promoCode'                     -- key exists            (GIN)
WHERE metadata ?| array['a','b']                 -- any of these keys
WHERE metadata @? '$.lines[*] ? (@.qty > 10)'    -- SQL/JSON path exists  (GIN)
WHERE jsonb_path_exists(metadata, '$.tags[*] ? (@ == "priority")')

-- flatten an array into rows, then treat it relationally
SELECT o.id, line->>'sku' AS sku, (line->>'qty')::int AS qty
FROM   orders o
CROSS  JOIN LATERAL jsonb_array_elements(o.metadata->'lines') AS line;

-- and build JSON on the way out, so one round trip returns a whole aggregate
SELECT jsonb_build_object(
         'id', o.id, 'total', o.total,
         'lines', COALESCE(jsonb_agg(jsonb_build_object('sku', l.sku, 'qty', l.qty))
                           FILTER (WHERE l.id IS NOT NULL), '[]')
       )
FROM   orders o LEFT JOIN order_lines l ON l.order_id = o.id
WHERE  o.id = :id GROUP BY o.id, o.total;
```

###### Column or JSON: the decision, and its costs

| Put it in a column when                         | Put it in JSON when                                       |
|-------------------------------------------------|-----------------------------------------------------------|
| You filter, sort, join or aggregate on it       | It is read as part of the whole record                    |
| It needs a constraint or foreign key            | The shape varies per tenant, per category, or per version |
| Every row has it                                | It is sparse — present on a minority of rows              |
| Reporting tools must see it                     | It is an immutable snapshot of an external payload        |
| The optimiser needs to estimate its selectivity | You genuinely do not know the fields in advance           |

The cost people underestimate is **estimation**. The planner has no per-key statistics inside a JSON document, so it falls back to a fixed guess for any JSON predicate. A query filtering on `metadata->>'status'` may be estimated at 0.5% when the reality is 60%, producing exactly the nested-loop disaster described in the optimisation part. An expression index helps, because PostgreSQL collects statistics on expression indexes.

``` sql
-- give the planner statistics for a hot JSON path
CREATE INDEX ix_orders_channel ON orders ((metadata->>'channel'));
ANALYZE orders;
-- now the planner has a histogram for that expression, not a hardcoded guess.
```

| Other cost             | Detail                                                                                  |
|------------------------|-----------------------------------------------------------------------------------------|
| Storage                | Keys are repeated in every row; `jsonb` deduplicates within a document, not across rows |
| TOAST                  | Documents over ~2 KB move off-row, adding a read whenever the column is touched         |
| No constraints         | Nothing stops `{"qty": "banana"}` unless you add a `CHECK`                              |
| Whole-document rewrite | Updating one key rewrites the entire document, and the whole row version                |
| GIN write cost         | GIN indexes are expensive to maintain; consider `fastupdate` tuning                     |

``` sql
-- you can still enforce a contract on JSON, and usually should
ALTER TABLE orders ADD CONSTRAINT ck_meta_shape CHECK (
    jsonb_typeof(metadata) = 'object'
    AND (NOT metadata ? 'score' OR jsonb_typeof(metadata->'score') = 'number')
    AND (NOT metadata ? 'channel' OR metadata->>'channel' IN ('web','phone','partner'))
);
-- PostgreSQL 17+ also has JSON_TABLE and SQL/JSON constructors from the standard.
```

> **\[Note\]**
>
> **The practical position.** A hybrid table — real columns for the queryable core, one `jsonb` column for the variable remainder — is very often a better answer than adding a second database. You keep transactions, joins, constraints and one operational surface, and you get the schema flexibility that motivated the document store in the first place.

### SQL you should write by hand

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · SQL you should write by hand</p>

<a id="be-m180"></a>

#### Logical query processing order

SQL is written in one order and evaluated in another. Almost every "why can't I do that?" question has its answer here.

``` text
-- Written:            SELECT ... FROM ... WHERE ... GROUP BY ... HAVING ... ORDER BY
-- Logically evaluated:
--   1. FROM / JOIN      build the working set
--   2. WHERE            filter rows
--   3. GROUP BY         collapse into groups
--   4. HAVING           filter groups
--   5. SELECT           compute output columns, assign aliases
--   6. DISTINCT
--   7. ORDER BY         sort the result
--   8. OFFSET / FETCH   slice it
```

Consequences that follow directly:

- A `SELECT` alias cannot be used in `WHERE` or `GROUP BY` — it does not exist yet. It *can* be used in `ORDER BY`, which runs after.
- `WHERE` filters rows, `HAVING` filters groups. Putting a non-aggregate condition in `HAVING` works but filters later, over more rows.
- You cannot aggregate in `WHERE` — the groups do not exist yet.
- `ORDER BY` is the only place a query's row order is defined. A subquery's `ORDER BY` is meaningless (and illegal in most engines without `TOP`/`LIMIT`).

``` sql
SELECT CustomerId, SUM(Total) AS Revenue
FROM   Orders
WHERE  CreatedAt >= '2026-01-01'     -- rows, before grouping
GROUP  BY CustomerId
HAVING SUM(Total) > 10000            -- groups; cannot say "Revenue > 10000" here
ORDER  BY Revenue DESC;              -- alias IS allowed here
```

> **\[Why it matters\]**
>
> **This is logical, not physical.** The optimiser is free to reorder anything as long as the result is identical — it routinely pushes predicates down into joins and evaluates them first. The processing order tells you what the query *means*; the execution plan tells you what actually happened.

<a id="be-m181"></a>

#### Join types and what they really mean

| Join              | Returns                                           | LINQ shape                                  |
|-------------------|---------------------------------------------------|---------------------------------------------|
| `INNER JOIN`      | only matched pairs                                | `Join`                                      |
| `LEFT JOIN`       | all left rows, nulls where unmatched              | `GroupJoin` + `DefaultIfEmpty`              |
| `RIGHT JOIN`      | mirror of left — just flip the tables instead     | —                                           |
| `FULL OUTER JOIN` | everything from both, nulls both ways             | no direct LINQ form                         |
| `CROSS JOIN`      | cartesian product                                 | `SelectMany` over an uncorrelated source    |
| `CROSS APPLY`     | per-row correlated subquery, inner-join semantics | `SelectMany` correlated                     |
| `OUTER APPLY`     | same, left-join semantics                         | `SelectMany(..., DefaultIfEmpty())`         |
| semi-join         | left rows that *have* a match, no duplication     | `Where(x => x.Items.Any())` → `EXISTS`      |
| anti-join         | left rows with *no* match                         | `Where(x => !x.Items.Any())` → `NOT EXISTS` |

> **\[Trap\]** **The classic left-join bug.** Putting a condition on the right table in `WHERE` instead of `ON` silently converts a left join into an inner join, because `NULL <> 'x'` is unknown and the row is dropped.
>
> ``` text
> -- Behaves as an INNER JOIN:
> FROM Orders o LEFT JOIN Payments p ON p.OrderId = o.Id
> WHERE p.Status = 'Settled'
>
> -- Actually a left join:
> FROM Orders o LEFT JOIN Payments p ON p.OrderId = o.Id AND p.Status = 'Settled'
> ```

``` sql
-- APPLY is the tool for "top N per group" - no window function needed
SELECT c.Id, r.*
FROM Customers c
CROSS APPLY (SELECT TOP 3 * FROM Orders o
             WHERE o.CustomerId = c.Id ORDER BY o.Total DESC) r;
```

> **\[Tip\]**
>
> **Duplication check.** If a join multiplies your row count unexpectedly, the join key is not unique on the other side. That is also what turns a `SUM` into a wrong number — the most common silent reporting bug there is. Verify uniqueness before summing across a join, or aggregate the many-side in a subquery first.

Worked example: [Case: the report whose totals were wrong](14-03-worked-cases-from-production.md#be-m295)

<a id="be-m182"></a>

#### Set operations: UNION, INTERSECT, EXCEPT

``` sql
SELECT Id FROM A UNION     SELECT Id FROM B;   -- distinct union: sorts/hashes to dedupe
SELECT Id FROM A UNION ALL SELECT Id FROM B;   -- concatenation: no dedupe, much cheaper
SELECT Id FROM A INTERSECT SELECT Id FROM B;   -- in both, distinct
SELECT Id FROM A EXCEPT    SELECT Id FROM B;   -- in A not in B, distinct
```

`UNION` without `ALL` is a deduplication operation. It costs a sort or a hash over the combined set, and it is frequently used where `UNION ALL` would be correct and free.

Rules: all branches need the same column count and compatible types; column names come from the first branch; only one `ORDER BY` is allowed, at the very end, applying to the whole result.

``` text
-- LINQ equivalents (all use a HashSet internally in memory,
-- and translate to the SQL forms above under EF):
a.Concat(b);      // UNION ALL
a.Union(b);       // UNION
a.Intersect(b);   // INTERSECT
a.Except(b);      // EXCEPT
```

> **\[Trap\]**
>
> **Set operators compare whole rows, and they treat NULLs as equal** — unlike `=`. So `EXCEPT` and `INTERSECT` give you the null-safe comparison that a join does not, which is occasionally exactly what you want for reconciling two tables. It also means adding a column to a `UNION` branch can change which rows deduplicate.

> **\[Tip\]**
>
> When comparing two tables for differences, `(A EXCEPT B) UNION ALL (B EXCEPT A)` gives you both directions in one pass and handles nulls correctly, which a full outer join with `IS NULL` checks does not do without extra care.

<a id="be-m187"></a>

#### NULL handling in SQL

`NULL` means "unknown", and any comparison with it yields `UNKNOWN`. `WHERE` keeps only `TRUE`, so unknown rows are dropped; `CHECK` constraints reject only `FALSE`, so unknown rows pass.

``` text
NULL = NULL        -- UNKNOWN (not true!)
NULL <> 'x'        -- UNKNOWN
x IS NULL          -- the only correct null test
COALESCE(a, b, c)  -- first non-null; standard, variadic
ISNULL(a, b)       -- SQL Server only, two args, uses a's type (can truncate!)
NULLIF(a, b)       -- NULL when a = b; handy for turning '' into NULL
a IS NOT DISTINCT FROM b   -- null-safe equality (Postgres); SQL Server: use EXCEPT
                           -- or (a = b OR (a IS NULL AND b IS NULL))
```

| Construct                     | NULL behaviour                                              |
|-------------------------------|-------------------------------------------------------------|
| `WHERE`                       | drops unknown rows                                          |
| `JOIN ... ON`                 | null never matches null                                     |
| `GROUP BY`                    | all nulls form *one* group                                  |
| `ORDER BY`                    | nulls sort first in SQL Server, last in Postgres by default |
| `UNIQUE` constraint           | SQL Server allows one null; Postgres allows many            |
| `DISTINCT`, `UNION`, `EXCEPT` | treat nulls as equal                                        |
| aggregates                    | skip nulls                                                  |
| `CHECK`                       | passes on unknown                                           |

> **\[Trap\]**
>
> **Every row of that table is a different rule.** Nulls being equal for `GROUP BY` and `DISTINCT` but unequal for `=` and `JOIN` is not an inconsistency you can reason your way around — it has to be memorised. The practical defence is to make columns `NOT NULL` with a sensible default wherever the domain allows it. Every nullable column is a permanent source of three-valued-logic bugs, and it makes indexes on that column less effective.

<a id="be-m188"></a>

#### Constraints: what the database guarantees

``` sql
CREATE TABLE Orders (
  Id          bigint IDENTITY PRIMARY KEY,
  PublicId    uniqueidentifier NOT NULL DEFAULT NEWID() UNIQUE,
  CustomerId  int NOT NULL REFERENCES Customers(Id) ON DELETE NO ACTION,
  Status      varchar(20) NOT NULL CHECK (Status IN ('Draft','Open','Closed')),
  Total       decimal(18,4) NOT NULL CHECK (Total >= 0),
  CreatedAt   datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
  RowVersion  rowversion
);
```

| Constraint    | Guarantees            | Also gives you                                 |
|---------------|-----------------------|------------------------------------------------|
| `PRIMARY KEY` | unique + not null     | a clustered index by default in SQL Server     |
| `UNIQUE`      | no duplicates         | a unique index the optimiser can exploit       |
| `FOREIGN KEY` | referential integrity | join-cardinality knowledge for the optimiser   |
| `CHECK`       | a domain rule         | predicate knowledge that can eliminate work    |
| `NOT NULL`    | presence              | simpler, faster plans; smaller nullable-bitmap |
| `DEFAULT`     | a value when omitted  | —                                              |

> **\[Why it matters\]**
>
> **Constraints are performance features, not just correctness features.** A trusted foreign key lets the optimiser eliminate a join it can prove is redundant. A `CHECK` constraint lets it skip partitions. A `NOT NULL` column removes null-handling branches from the plan. Validating only in application code gives up all of that *and* does not survive a bulk import, a hotfix script, or a second application writing to the same database.

> **\[Trap\]**
>
> **Untrusted constraints.** After a bulk insert with `WITH (CHECK_CONSTRAINTS)` disabled, or after re-enabling with `WITH NOCHECK`, SQL Server marks the constraint *untrusted* and stops using it for optimisation — silently. Check `sys.foreign_keys.is_not_trusted` and `sys.check_constraints.is_not_trusted`; it is a common finding on databases that have been through a data migration.

<a id="be-m189"></a>

#### Normalization and deliberate denormalization

The practical target is third normal form, reachable with one rule per step:

- **1NF** — one value per cell. No comma-separated lists in a column, no `Phone1`/`Phone2`/`Phone3`.
- **2NF** — every non-key column depends on the *whole* key. Only bites with composite keys: if `OrderLines(OrderId, LineNo)` stores `OrderDate`, that depends on `OrderId` alone and belongs in `Orders`.
- **3NF** — no non-key column depends on another non-key column. Storing `CustomerName` on `Orders` is a 3NF violation: it depends on `CustomerId`, not on the order.

The payoff is that every fact lives in exactly one place, so there is no way for two copies to disagree and no update anomaly.

**Denormalize deliberately**, for reasons you can state:

| Pattern                                          | Legitimate reason                                | Cost you accept                   |
|--------------------------------------------------|--------------------------------------------------|-----------------------------------|
| Cached aggregate (`Orders.LineCount`)            | avoids a per-read aggregation                    | must be maintained on every write |
| Point-in-time copy (`OrderLine.UnitPriceAtSale`) | **not denormalization** — it is a different fact | none; this is correct modelling   |
| Read model / materialised view                   | reporting queries would cripple OLTP             | staleness, rebuild cost           |
| JSON column for sparse attributes                | schema churn on optional data                    | no constraints, weak indexing     |

> **\[Tip\]**
>
> **The distinction that matters most.** Copying a value because a join is inconvenient is a bug waiting to happen. Copying a value because it must be *frozen at a point in time* — the price at the moment of sale, the address at the moment of shipment — is correct design, and normalising it away is the actual mistake. Ask: "if the source changes, should this change too?" If no, it is a separate fact.

Goes further: [Decomposition: contexts and data ownership](12-01-monoliths-modules-and-services.md#be-m247)

<a id="be-m190"></a>

#### Column types, sizing, and collation

Column choices affect row size, which affects pages read, which is the currency of database performance ([Reading a query plan without being a DBA](07-03-indexes-and-query-plans.md#be-m172)).

- **Pick the narrowest type that fits the domain**, with room to grow. `int` over `bigint` when 2.1 billion is genuinely enough; `date` over `datetime2` when there is no time.
- **`varchar` vs `nvarchar`.** `nvarchar` is 2 bytes per character on SQL Server. For codes, SKUs, and identifiers that are ASCII by definition, `varchar` halves the storage and the index size. For anything user-entered — and in a Vietnamese-language application, that is most text — you need Unicode.
- **Always size `varchar`.** `varchar(max)` is stored off-row past 8000 bytes, cannot be an index key, and disables some optimisations. Set a real limit.
- **`decimal(p, s)` deliberately.** Precision 1–9 costs 5 bytes, 10–19 costs 9, 20–28 costs 13. The default `decimal(18,2)` silently truncates unit prices, tax rates, and FX rates.
- **Fixed vs variable.** `char(n)` pads; `varchar(n)` adds 2 bytes of length overhead. Below about 5 characters, fixed wins.

``` text
-- Collation decides comparison and sorting semantics, per column if you want:
Name nvarchar(200) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL
-- CI/CS = case in/sensitive, AI/AS = accent in/sensitive
-- Vietnamese text: use a Vietnamese_100_* or a UTF-8 enabled collation
--                  so that diacritics sort and compare correctly.
```

> **\[Trap\]**
>
> **Collation mismatches are a silent performance killer.** Joining or comparing two columns with different collations forces an implicit conversion, which makes the predicate non-sargable and turns a seek into a scan. It can also throw outright. Set collation at the database level and only override it with a documented reason — and check it after restoring a database onto a differently-configured server.

<a id="be-m191"></a>

#### Set-based thinking, temp tables, and TVPs

The mental shift that makes SQL fast: describe the whole transformation as one statement over a set, rather than a loop over rows. A cursor doing 100,000 single-row updates can be minutes; the equivalent `UPDATE ... FROM` is often under a second.

``` sql
-- Row-at-a-time (avoid)
DECLARE cur CURSOR FOR SELECT Id FROM Orders WHERE ...
-- Set-based
UPDATE o SET o.Status = 'Stale'
FROM Orders o JOIN Batches b ON b.Id = o.BatchId
WHERE b.ClosedAt < @cutoff;
```

|                   | `#temp` table           | `@table` variable                     | TVP                             |
|-------------------|-------------------------|---------------------------------------|---------------------------------|
| Statistics        | **yes**                 | no (estimates 1 row until recompiled) | no                              |
| Indexes           | any, added after        | only inline, at declaration           | predefined in the type          |
| Transaction scope | participates, logged    | survives rollback                     | —                               |
| Best for          | large intermediate sets | small sets (\< ~100 rows)             | passing a set *in* from the app |

``` csharp
-- TVP: send a whole set from C# as one parameter instead of 2,000 parameters
CREATE TYPE dbo.IdList AS TABLE (Id bigint PRIMARY KEY);

var table = new DataTable(); table.Columns.Add("Id", typeof(long));
foreach (var id in ids) table.Rows.Add(id);
var p = cmd.Parameters.AddWithValue("@ids", table);
p.SqlDbType = SqlDbType.Structured; p.TypeName = "dbo.IdList";
```

> **\[Tip\]**
>
> A TVP, or EF Core 8's `OPENJSON` translation of `Contains`, is the right answer to "I need to filter by 5,000 ids" — it sends one parameter, produces one reusable plan, and gives the optimiser a real set with a real index to join against. Chunking into batches of 500 is the portable fallback ([IN lists, Contains, and parameter blowup](07-07-orms-and-ef-core.md#be-m164)).

Worked example: [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)

Recipe: [Recipe: import a large CSV into SQL Server](08-09-integrations-http-clients-resilience-webhooks-and-files.md#be-m284)

<a id="be-m193"></a>

#### Upsert: MERGE and the alternatives

``` sql
-- The naive version has a race: two connections can both see "not exists"
IF NOT EXISTS (SELECT 1 FROM T WHERE K = @k)
    INSERT INTO T (K, V) VALUES (@k, @v);
ELSE
    UPDATE T SET V = @v WHERE K = @k;
```

Correct approaches, by engine:

``` sql
-- SQL Server: single statement, with an explicit lock hint to serialise the check
UPDATE T WITH (UPDLOCK, SERIALIZABLE) SET V = @v WHERE K = @k;
IF @@ROWCOUNT = 0 INSERT INTO T (K, V) VALUES (@k, @v);

-- PostgreSQL: the clean one
INSERT INTO T (K, V) VALUES (@k, @v)
ON CONFLICT (K) DO UPDATE SET V = EXCLUDED.V;

-- MERGE: standard SQL, and the natural fit for set-based upserts
MERGE INTO T AS t
USING (SELECT @k AS K, @v AS V) AS s ON t.K = s.K
WHEN MATCHED THEN UPDATE SET V = s.V
WHEN NOT MATCHED THEN INSERT (K, V) VALUES (s.K, s.V);
```

> **\[Trap\]**
>
> **`MERGE` on SQL Server has a long history of bugs and concurrency surprises** — it is not automatically atomic against concurrent inserts unless the target key is protected by a unique index and the statement holds the right locks (`HOLDLOCK` on the target). For single-row upserts, prefer the update-then-insert pattern above. `MERGE` earns its place for genuinely set-based synchronisation of one table from another.

> **\[Tip\]**
>
> **The pragmatic pattern:** attempt the insert, catch the unique-constraint violation (SQL Server error 2627/2601), and fall back to update. It is correct under any concurrency, needs no hints, and the exception path only runs on an actual collision — which, for a genuine upsert workload, is the uncommon case. EF Core has no built-in upsert; this or a raw statement is how you do it.

Recipe: [Recipe: an idempotent command handler](14-01-recipes-code-you-will-actually-write.md#be-m286)

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a106"></a>

#### Bulk operations and set-based thinking

Loading ten thousand entities to change one field on each is the most common performance mistake in EF Core code. The database is a set-processing engine; use it as one.

``` csharp
// slow: 10,000 entities materialised, tracked, then 10,000 UPDATE statements
foreach (var o in await db.Orders.Where(o => o.Status == Pending).ToListAsync(ct))
    o.Status = Expired;
await db.SaveChangesAsync(ct);

// fast: one statement, nothing loaded  (EF Core 7+)
await db.Orders.Where(o => o.Status == Pending && o.PlacedAt < cutoff)
    .ExecuteUpdateAsync(s => s
        .SetProperty(o => o.Status,      Expired)
        .SetProperty(o => o.ExpiredAt,   DateTimeOffset.UtcNow), ct);

await db.AuditRows.Where(a => a.CreatedAt < retention).ExecuteDeleteAsync(ct);
```

| Task                      | Tool                                             | Rough throughput               |
|---------------------------|--------------------------------------------------|--------------------------------|
| Insert a few hundred rows | `AddRange` + `SaveChanges`                       | Fine                           |
| Insert 100k+ rows         | `SqlBulkCopy` / Npgsql `COPY`                    | Orders of magnitude faster     |
| Update by predicate       | `ExecuteUpdateAsync`                             | One round trip                 |
| Upsert                    | `MERGE`, `ON CONFLICT`, or EFCore.BulkExtensions | One round trip                 |
| Delete by predicate       | `ExecuteDeleteAsync`, batched                    | Batch it or you lock the table |

``` csharp
// Npgsql binary COPY - the fastest way to get rows into Postgres
await using var writer = await conn.BeginBinaryImportAsync(
    "COPY readings (device_id, taken_at, value) FROM STDIN (FORMAT BINARY)", ct);

foreach (var r in readings)
{
    await writer.StartRowAsync(ct);
    await writer.WriteAsync(r.DeviceId, NpgsqlDbType.Uuid, ct);
    await writer.WriteAsync(r.TakenAt,  NpgsqlDbType.TimestampTz, ct);
    await writer.WriteAsync(r.Value,    NpgsqlDbType.Double, ct);
}
await writer.CompleteAsync(ct);
```

> **\[Trap\]**
>
> **Common trap.** `ExecuteUpdate` and `ExecuteDelete` bypass the change tracker entirely. Entities already loaded in the context keep their old values, and no domain events fire. Use them for maintenance and batch work, not for operations with business rules attached.

###### The four ways to write many rows, measured

``` csharp
// 1. NAIVE: SaveChanges per row.  ~200,000 round trips for 200k rows.
foreach (var row in rows) { db.Add(row); await db.SaveChangesAsync(); }
// minutes to hours. Never do this.

// 2. ONE SaveChanges. EF Core batches into multi-row statements
//    (default MaxBatchSize 42 on SQL Server, 1000 on Npgsql).
foreach (var row in rows) db.Add(row);
await db.SaveChangesAsync();
// better, and the change tracker now holds 200,000 entities: memory grows,
// and DetectChanges is O(n) per call. Acceptable to ~10k rows.

// 3. ExecuteUpdate / ExecuteDelete (EF Core 7+): ONE statement, no tracking
await db.Orders.Where(o => o.PlacedAt < cutoff)
    .ExecuteDeleteAsync(ct);
await db.Orders.Where(o => o.Status == "Pending")
    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "Expired"), ct);
// seconds. No entities loaded at all.

// 4. PROVIDER BULK COPY: for genuinely large loads
using var writer = conn.BeginBinaryImport(
    "COPY orders (id, total, placed_at) FROM STDIN (FORMAT BINARY)");
foreach (var r in rows)
{
    await writer.StartRowAsync(ct);
    await writer.WriteAsync(r.Id, NpgsqlDbType.Bigint, ct);
    await writer.WriteAsync(r.Total, NpgsqlDbType.Numeric, ct);
    await writer.WriteAsync(r.PlacedAt, NpgsqlDbType.TimestampTz, ct);
}
await writer.CompleteAsync(ct);
// millions of rows per minute. SqlBulkCopy is the SQL Server equivalent.
```

``` text
  rough shape of the difference, 200,000 rows

    per-row SaveChanges       ~25 minutes
    one SaveChanges           ~90 seconds
    ExecuteUpdate/Delete      ~3 seconds      (for update/delete)
    COPY / SqlBulkCopy        ~4 seconds      (for insert)

  the numbers vary enormously by schema, indexes and hardware -
  the ORDERS OF MAGNITUDE do not.
```

| Caveat of `ExecuteUpdate`/`ExecuteDelete` | Consequence                                                                        |
|-------------------------------------------|------------------------------------------------------------------------------------|
| Bypasses the change tracker               | Tracked entities in memory become **stale** — do not mix in one unit of work       |
| Does not fire `SaveChanges` interceptors  | Audit columns and soft-delete filters you rely on will not apply                   |
| No domain events raised                   | Anything hooked to `SaveChanges` is skipped                                        |
| Executes immediately                      | It is **not** part of the `DbContext`'s transaction unless you open one explicitly |
| No cascade behaviour from EF              | The database's own `ON DELETE` rules still apply                                   |

> **\[Trap\]**
>
> **Common trap.** A single `ExecuteDelete` over 50 million rows is one enormous transaction: hours of locking, a log that cannot be recycled, and a rollback as slow as the delete. Batch it — delete 10,000 at a time in a loop with a commit between — or, if the purge is routine, partition the table by date so it becomes `DROP TABLE` instead.
