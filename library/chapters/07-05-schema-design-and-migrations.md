---
id: "7-5"
number: "7.5"
title: "Schema design and migrations"
part: 7
part_title: "Storing data"
words: 16388
minutes: 71
---

# 7.5 Schema design and migrations

> **Quick look.** Designing a schema that stays correct, and changing it while the system keeps running.
>
> - A schema outlives the code: make invalid data impossible with keys and constraints.
> - Normalize so each fact lives once; denormalize deliberately when measurement shows reads need it.
> - Prefer surrogate keys, keep natural identifiers unique, avoid random GUIDs as clustered keys, and don't expose sequential ids.
> - Exact types for money, UTC for instants; constraints enforce rules for every writer.
> - Change live systems with expand and contract so old and new code both work; batch backfills.
> - Don't run Database.Migrate() at start-up with several instances; run reviewed migrations from the pipeline.
> - Soft delete, history (temporal tables) and multi-tenancy each have costs; a global query filter is not a security boundary.


### The big picture

Code gets rewritten; data stays. An airline's passenger records may outlive three generations of the applications that read them. The shape of that data, its tables, columns, keys and rules, is the schema, and mistakes in it are among the most expensive in software. A duplicated fact that falls out of step, a missing constraint that let bad rows in, or a key that turned out not to be unique: each has to be fixed in the data itself. That has to happen carefully, while the system keeps running.

This chapter covers designing a schema that stays correct: how to split data into tables, which keys to use, which data types fit money and time, and which rules to hand to the database. Then it covers the other half of the job: changing a schema that is already in production, with migrations, including the expand-and-contract technique that lets you change tables without taking the system down. It ends with the ways multi-tenant systems keep each customer's data apart.

### Before you start

**Builds on:** 2.3 Types, 7.2 The relational model and SQL, 1.2 Numbers inside the machine, 1.3 Text is numbers too.

**Ideas explained later:**

- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **EF Core** (7.7): Entity Framework Core, Microsoft's ORM for .NET.
- **Runner** (11.4): The machine or container that runs a pipeline's jobs.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

**The code uses:** variables and var, string interpolation, null operators, tuples, exceptions, using for cleanup, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What problem does normalization solve?**

> Duplicated facts falling out of step. If a passenger's email is stored in every booking, changing it means updating many rows, and missing one leaves the data contradicting itself. Normalization stores each fact once.

**2. How do you store a many-to-many relationship, such as crew members and flights?**

> With a junction table holding pairs of foreign keys, such as `crew_assignments (flight_id, crew_id)`, one row per crew member per flight.

**3. What is the difference between a natural key and a surrogate key?**

> A natural key is a real-world identifier, such as an airport code or a passport number. A surrogate key is a value the database generates with no meaning, such as an auto-increment number or a GUID. Natural keys can change or turn out not to be unique; surrogate keys never need to.

**4. Why can a random GUID primary key slow down SQL Server inserts?**

> The clustered index keeps rows in key order, and random values land all over the B-tree, splitting pages and scattering writes. Sequential keys, such as identity numbers or time-ordered GUIDs, append at the end.

**5. What is a schema migration?**

> A versioned script that changes the database from one schema version to the next. A migration tool applies pending ones in order and records which have run, so every environment ends up the same.

**6. How do you rename a column without downtime?**

> Expand and contract: add the new column, copy the data across, release code that writes both and reads the new one, then, once nothing uses the old column, drop it in a later release.

**7. What are the three common ways to separate tenants in a multi-tenant database?**

> A shared table with a tenant id column on every row, a separate schema per tenant in one database, or a separate database per tenant. They trade cost and simplicity against isolation.

### Why the schema matters

A **schema** is the structure of a database: its tables, columns, types, keys, constraints and indexes. Application code depends on it, reports depend on it, other systems depend on it, and the data in it lasts far longer than any one version of the code.

A good schema makes invalid data impossible to store and correct data easy to query. A poor one lets contradictions creep in, and every program that touches it has to defend against them. It is the database version of the strongly typed modelling in Types (2.3): push the rules into the structure, and whole categories of bug disappear.

### Entities and relationships

Schema design starts with the things the business talks about, each called an **entity**: airports, flights, aircraft, passengers, bookings, crew. Each usually becomes a table. Then come the **relationships** between them, which become keys.

A **one-to-many relationship** is the most common. An airport has many departing flights; each flight departs from one airport. The "many" side holds a foreign key to the "one" side: `flights.origin` refers to `airports.code`, as The relational model and SQL (7.2) showed.

A one-to-one relationship, such as a flight and its final load sheet, is usually a foreign key with a unique constraint, or simply extra columns in the same table.

A **many-to-many relationship** needs a table of its own. A flight has many crew members, and a crew member works many flights. Neither table can hold a single foreign key to the other. A **junction table**, here `crew_assignments (flight_id, crew_id)`, holds one row per pairing, with a foreign key to each side. It is also where facts about the pairing itself live, such as the crew member's role on that flight.

### Normalization

**Database normalization** is the discipline of storing each fact in exactly one place. Its rules are expressed as a series of **normal forms**, of which the first three cover nearly everything in practice.

First normal form: each column holds a single value. No comma-separated lists of seat numbers in one column, no `phone1`, `phone2`, `phone3` columns. Repeating values become rows in another table.

Second and third normal forms: every column describes the whole key of its table, and nothing else. A classic summary is that each column must depend on "the key, the whole key and nothing but the key". A bookings table shouldn't hold the passenger's email, which depends on the passenger, not the booking, or the flight's departure time, which depends on the flight.

Breaking these rules invites anomalies. The **update anomaly** is the most common: a fact stored in many rows gets changed in some and not others, and the database now contradicts itself. There are insert and delete anomalies too: you can't record an airport until it has a flight, or deleting the last flight deletes the only record of the airport.

### Denormalization

Normalization optimizes for correct writes. Sometimes reads need something different: a dashboard that would otherwise join eight tables on every refresh, or a search page that needs the airport name next to every flight. **Denormalization** deliberately duplicates data to make such reads cheaper.

It is a trade, not a free improvement. Every copy must be kept in step when the source changes, and that code is a new place for bugs. Prefer mechanisms the database maintains for you: computed columns, indexed or materialized views, and separate read models or reporting databases filled from the source of truth. Denormalize when measurements show the reads need it, not in advance.

### Keys

Every table needs a primary key. The first choice is between two kinds.

A **natural key** comes from the real world: an IATA airport code, an ISO country code, a passport number. It is meaningful and needs no lookup. The risk is that the real world changes. Codes get reassigned, people change names, "unique" identifiers turn out to have duplicates, and a key used as a foreign key in twenty tables is painful to change.

A **surrogate key** has no meaning at all. It is generated by the database or the application purely to identify the row. It never needs to change, because it describes nothing. Most tables use one, and keep natural identifiers as ordinary columns with a unique constraint.

Surrogate keys are usually one of two types. An auto-incrementing integer, `IDENTITY` in SQL Server, is compact, fast and sequential, but only the database can generate it and it reveals how many rows exist. A GUID can be generated anywhere, including before the row is saved, and reveals nothing. Random GUIDs, though, land at random places in a clustered index, splitting pages and slowing inserts on large tables. Time-ordered GUIDs avoid that: .NET 9 added `Guid.CreateVersion7()`, which produces version 7 UUIDs that sort by creation time.

Composite keys, made of several columns, suit junction tables: the pair `(flight_id, crew_id)` is naturally unique.

### Choosing data types

The right type makes wrong values impossible and saves space.

Money needs an exact type: `DECIMAL(19,4)` or similar in SQL Server, `NUMERIC` in PostgreSQL, never `FLOAT`, for the reasons in Numbers inside the machine (1.2). Store the currency alongside it.

Times need care. Store instants in UTC, as `datetime2` or `datetimeoffset` in SQL Server and `timestamptz` in PostgreSQL. Use a plain date type for things that really are dates, such as a date of birth or a flight's scheduled date, which don't shift with time zones.

Text needs a sensible maximum length, and a Unicode type if it can hold names or anything typed by people, as Text is numbers too (1.3) explained. Booleans are `bit` in SQL Server and `boolean` in PostgreSQL.

Enumerations, such as a flight status, can be stored as integers or as short strings. Integers are compact; strings are readable in queries and survive reordering in code. Either way, add a check constraint listing the allowed values.

JSON columns hold semi-structured data, such as a payload from another system. They are useful for data the database doesn't need to understand. For data you filter, join and constrain, use real columns.

### Constraints

Constraints are rules the database enforces on every change, from every program. They are the last line of defence, and the only one that covers everything: application code, scripts, other services and manual fixes.

`NOT NULL` requires a value. A **unique constraint** forbids duplicates in a column or combination of columns: one BA117 per day. A **check constraint** requires a condition to hold: a flight's origin differs from its destination, a fare isn't negative. A foreign key keeps references valid.

A foreign key also decides what happens when the row it points at is deleted. The default refuses the delete while referencing rows exist. A **cascade delete** deletes the referencing rows too: deleting a flight deletes its bookings. Cascades are convenient and dangerous; one mistaken delete can remove far more than intended. Use them for rows that truly belong to their parent, and refuse deletes elsewhere.

### Keeping history

Business data rarely just disappears, so schemas often keep history.

A **soft delete** marks a row as deleted, with a column such as `deleted_at`, instead of removing it. It keeps history and allows undelete. The cost is that every query must remember to exclude deleted rows, and unique constraints must allow for them. EF Core's global query filters, in ORMs and EF Core (7.7), help with the first.

Audit columns record who created or last changed each row, and when.

A **temporal table** keeps every past version of every row automatically. SQL Server's system-versioned temporal tables store old versions in a history table and let you query the data as it was at any moment, with `FOR SYSTEM_TIME AS OF`. That answers "what did this booking look like before the complaint?" without any application code.

### Migrations

Schemas change as the application changes. A **schema migration** is a versioned script that takes the database from one version of the schema to the next: create a table, add a column, add an index. Migrations live in source control next to the code that needs them.

A migration tool keeps a history table in the database listing which migrations have run. On deployment, it applies every pending migration, in order, and records each one. Every environment, from a developer's laptop to production, then ends up with exactly the same schema. EF Core has migrations built in; DbUp, Flyway and Liquibase are popular standalone tools.

A few habits keep migrations safe. Never edit a migration that has already run anywhere else; add a new one. Run each migration in a transaction where the database allows it. Test migrations against a copy of production-sized data, because a change that takes a second on a test database can lock a big table for an hour. And decide when migrations run: as a step in the deployment pipeline before the new code starts is the usual choice, rather than from application start-up, where several instances may race to apply them.

The program below is a small migration runner, the same idea as the real tools.

```csharp
#:package Microsoft.Data.Sqlite@10.0.0
// migrations.cs — run with: dotnet run migrations.cs   (run it twice)
using Microsoft.Data.Sqlite;

using var db = new SqliteConnection("Data Source=schema.db");
db.Open();
Run("PRAGMA foreign_keys = ON");
Run("CREATE TABLE IF NOT EXISTS schema_history (id TEXT PRIMARY KEY, applied_at TEXT NOT NULL)");

(string Id, string Sql)[] migrations =
[
    ("001_create_airports", """
        CREATE TABLE airports (
            code TEXT PRIMARY KEY CHECK (length(code) = 3),   -- a natural key, checked
            city TEXT NOT NULL
        );
        """),
    ("002_create_flights", """
        CREATE TABLE flights (
            id          INTEGER PRIMARY KEY,                  -- a surrogate key
            number      TEXT NOT NULL,
            departs_on  TEXT NOT NULL,
            origin      TEXT NOT NULL REFERENCES airports (code),
            destination TEXT NOT NULL REFERENCES airports (code),
            gate        TEXT,
            CHECK (origin <> destination),
            UNIQUE (number, departs_on)                       -- one BA117 per day
        );
        INSERT INTO airports VALUES ('LHR', 'London'), ('JFK', 'New York');
        INSERT INTO flights (number, departs_on, origin, destination, gate) VALUES ('BA117', '2026-09-28', 'LHR', 'JFK', 'B12');
        """),
    // Renaming gate to gate_code without downtime: expand, backfill, then contract in a later release
    ("003_expand_add_gate_code", "ALTER TABLE flights ADD COLUMN gate_code TEXT;"),
    ("004_backfill_gate_code", "UPDATE flights SET gate_code = gate WHERE gate_code IS NULL;"),
    ("005_contract_drop_gate", "ALTER TABLE flights DROP COLUMN gate;"),
];

foreach (var (id, sql) in migrations)
{
    if (Scalar("SELECT COUNT(*) FROM schema_history WHERE id = $id", id) > 0) continue;   // already applied
    using var tx = db.BeginTransaction();                  // each migration is all or nothing
    Run(sql, tx);
    Run("INSERT INTO schema_history VALUES ($id, datetime('now'))", tx, id);
    tx.Commit();
    Console.WriteLine($"applied {id}");
}
Console.WriteLine($"schema is at {Scalar("SELECT COUNT(*) FROM schema_history")} migrations");
// First run: applied 001 ... applied 005, schema is at 5 migrations. Second run: nothing applied.

// The constraints refuse bad data, whichever program sends it
foreach (string bad in new[]
{
    "INSERT INTO flights (number, departs_on, origin, destination) VALUES ('BA117', '2026-09-28', 'LHR', 'JFK')",
    "INSERT INTO flights (number, departs_on, origin, destination) VALUES ('BA999', '2026-09-28', 'LHR', 'LHR')",
    "INSERT INTO flights (number, departs_on, origin, destination) VALUES ('BA118', '2026-09-28', 'LHR', 'XXX')",
    "INSERT INTO airports VALUES ('LOND', 'London')",
    "DELETE FROM airports WHERE code = 'JFK'",
})
{
    try { Run(bad); Console.WriteLine("accepted?!"); }
    catch (SqliteException ex) { Console.WriteLine($"refused: {ex.Message}"); }
}
// refused: ... UNIQUE constraint failed: flights.number, flights.departs_on
// refused: ... CHECK constraint failed: origin <> destination
// refused: ... FOREIGN KEY constraint failed   (no airport XXX)
// refused: ... CHECK constraint failed: length(code) = 3
// refused: ... FOREIGN KEY constraint failed   (JFK still has flights)

void Run(string sql, SqliteTransaction? tx = null, string? id = null)
{
    using var cmd = db.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    if (id is not null) cmd.Parameters.AddWithValue("$id", id);
    cmd.ExecuteNonQuery();
}

long Scalar(string sql, string? id = null)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = sql;
    if (id is not null) cmd.Parameters.AddWithValue("$id", id);
    return (long)cmd.ExecuteScalar()!;
}
```

### Changing a live schema

In a system that must stay up, the old version of the code and the new one run side by side during a deployment, both against the same database. Every schema change must work with both.

That rules out changes that break the running code. Dropping or renaming a column that the old code reads breaks it the moment the migration runs. Adding a `NOT NULL` column without a default breaks the old code's inserts.

**Expand-contract**, also called parallel change, solves this in steps. First expand: add the new structure alongside the old, such as a new column that allows NULL. Then **backfill**: copy existing data into the new structure, in batches if the table is large, so no single transaction locks it for long. Release code that writes to both and reads the new one. Once no running code uses the old structure, contract: remove it, in a later release. Migrations 003 to 005 in the example follow this pattern for renaming a column. In a real system, each step goes out with a separate release.

Large tables add a second concern: some changes lock the whole table while they run. Adding an index to a big SQL Server table can block writes unless it is built online, with `WITH (ONLINE = ON)` where the edition supports it; PostgreSQL has `CREATE INDEX CONCURRENTLY`. Changing a column's type may rewrite every row. Test such changes on realistic data sizes before running them in production.

### Many customers in one system

Software sold to many organizations, such as a booking platform used by many airlines, is **multi-tenant**: one system serves many customers, each a tenant, whose data must never mix. There are three main designs.

A shared schema puts all tenants in the same tables, with a tenant id column on every row. It is the cheapest and easiest to run and to change, because there is one schema. The risk is a query that forgets its tenant filter and shows one customer another's data. EF Core's global query filters and SQL Server's row-level security make that filter automatic.

A schema per tenant gives each tenant its own set of tables inside one database. Isolation is stronger, and migrations must run once per tenant.

A database per tenant gives the strongest isolation, per-tenant backups and restores, and the ability to place large tenants on their own servers. It is the most expensive to run, and every change must be rolled out across many databases.

Many systems mix them: shared tables for most tenants, dedicated databases for the largest or most regulated.

### In short

A schema outlives the code, so design it to make invalid data impossible. Model entities as tables and relationships as foreign keys, with junction tables for many-to-many. Normalize so each fact lives in one place, and denormalize only when measurement shows reads need it. Prefer surrogate keys, keep natural identifiers unique, and avoid random GUIDs in clustered keys. Use exact types for money and UTC for instants, and let constraints enforce the rules. Change schemas through versioned migrations, and change live systems in expand-contract steps that work with old and new code at once.

### What goes wrong

**The same fact in many places.** A passenger's email is updated in one table and not another, and the system contradicts itself. Normalize, and denormalize only with a mechanism that keeps copies in step.

**Rules only in application code.** A script or another service inserts a booking with no flight. Put essential rules in constraints.

**Natural keys that change.** An airline code or product code used as a primary key and foreign key everywhere has to change, and it touches every table. Use a surrogate key and keep the natural one unique.

**Money and time in the wrong types.** `FLOAT` fares and local-time timestamps cause rounding errors and daylight-saving bugs forever after. Use `DECIMAL` and UTC.

**Breaking changes in one step.** A migration drops or renames a column while the old code is still running, and requests fail during the deployment. Use expand-contract.

**Migrations that lock big tables.** An index added in a migration blocks writes to a 200-million-row table for twenty minutes. Build indexes online and test on realistic data.

**Editing applied migrations.** Environments that ran the old version drift apart from those that ran the new one. Add a new migration instead.

**Tenant filters left to memory.** One missed `WHERE TenantId = ...` exposes another customer's data. Enforce tenant filtering automatically, with query filters or row-level security.

### Cost model

Each constraint costs a little on every write: a unique constraint is an index lookup, a foreign key a lookup in the referenced table's key. These checks take microseconds and prevent problems that cost days. Normalized schemas need joins to read, which indexes make cheap for OLTP work. Adding a nullable column is usually instant, while adding a column with a computed value or changing a type can rewrite the whole table, taking time proportional to its size. Backfilling in batches of a few thousand rows keeps each transaction short, at the cost of taking longer overall.

### In other languages (optional)

**EF Core.** The model in C#, with keys, relationships, indexes and constraints configured by conventions, attributes or the fluent API, generates migrations with `dotnet ef migrations add`. Migrations can also be exported as SQL scripts or bundles for the deployment pipeline, as ORMs and EF Core (7.7) shows.

**Node.js and TypeScript.** Prisma and Drizzle define schemas in code and generate migrations. Knex has a standalone migration runner.

**Python.** Django has built-in migrations; SQLAlchemy uses Alembic.

**Standalone tools.** Flyway, Liquibase and DbUp run plain SQL migrations from any language, which suits teams that want to write and review the SQL directly.

### Key terms

- **Schema**: The structure of a database: its tables, columns, types, keys, constraints and indexes.
- **Entity**: A kind of thing the business deals with, usually stored as a table.
- **Relationship**: How entities relate, expressed in a schema with foreign keys.
- **One-to-many relationship**: Each row on one side relates to many on the other, with a foreign key on the many side.
- **Many-to-many relationship**: Rows on each side relate to many on the other, stored with a junction table.
- **Junction table**: A table of foreign key pairs that records a many-to-many relationship.
- **Database normalization**: Structuring tables so that each fact is stored in exactly one place.
- **Normal form**: One of a series of rules defining how normalized a table is.
- **Update anomaly**: A duplicated fact changed in some places and not others, leaving the data contradictory.
- **Denormalization**: Deliberately duplicating data to make reads cheaper.
- **Natural key**: A primary key made from a real-world identifier, such as an airport code.
- **Surrogate key**: A generated primary key with no meaning outside the database.
- **Unique constraint**: A rule forbidding duplicate values in a column or combination of columns.
- **Check constraint**: A rule that each row must satisfy, such as a fare not being negative.
- **Cascade delete**: Deleting rows that refer to a deleted row, automatically.
- **Soft delete**: Marking a row as deleted instead of removing it.
- **Temporal table**: A table that automatically keeps every past version of its rows.
- **Schema migration**: A versioned script that changes a database from one schema version to the next.
- **Expand-contract**: Changing a live schema in steps: add the new structure, move data and code over, then remove the old.
- **Backfill**: Filling a new column or table with data derived from existing data.
- **Multi-tenant**: One system serving many customer organizations whose data must be kept apart.

### Where this leads

Stores that relax the relational rules are in Non-relational stores (7.6). Defining the schema in C# and generating migrations is in ORMs and EF Core (7.7). Running migrations in deployment pipelines is in CI/CD pipelines (11.4). API contracts, which evolve by the same expand-contract logic, are in API design (8.2).

### Check yourself

1. Run `migrations.cs` twice and confirm the second run applies nothing. Then add migration 006 that creates a `crew_assignments` junction table.
2. Design tables for bookings where one booking can hold several passengers on several flights. Identify every foreign key and unique constraint.
3. Take a denormalized spreadsheet of flights with repeated airport names and cities, and normalize it into tables by hand.
4. Plan the expand-contract steps to split a `passenger_name` column into `given_name` and `family_name` without downtime. Note what each release of the code does.
5. Look at the primary keys in a database you know. Note which are natural, which surrogate, and whether any GUID keys are random.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Renaming a column without downtime

`Orders.Notes` must become `CustomerNote` while several app versions run during a rolling deploy.

*Expand and contract*

```text
Deploy 1  add CustomerNote (nullable); app writes both Notes and CustomerNote
Deploy 2  backfill CustomerNote from Notes in batches; app reads CustomerNote
Deploy 3  app stops writing Notes
Deploy 4  drop Notes
Every step works with the version before it, so each can be rolled back.
```

> **Lesson:** A breaking schema change in one deploy breaks the old version still running. Expand, migrate, then contract.

#### Example 2: A backfill that locked the table

`UPDATE Orders SET Region = ...` over 20 million rows blocks all writers for 12 minutes.

*Batch it*

```sql
DECLARE @batch int = 5000, @rows int = 1;
WHILE @rows > 0
BEGIN
  UPDATE TOP (@batch) o
  SET    o.Region = c.Region
  FROM   Orders o JOIN Customers c ON c.Id = o.CustomerId
  WHERE  o.Region IS NULL;
  SET @rows = @@ROWCOUNT;
  WAITFOR DELAY '00:00:00.200';    -- give other work room
END
```

> **Lesson:** Big one-shot updates lock tables and bloat the log. Backfill in small, resumable batches.

### EF Core Internals: What People Miss

<p class="src">From <em>.NET Backend Deep Dive</em> · EF Core Internals: What People Miss</p>

<a id="dn-69"></a>

#### Zero-downtime migrations — expand and contract

Never make a breaking schema change while the old code is still running:

```
Deploy 1 (EXPAND):   add the new column as NULLABLE; code writes both old and new, reads old
Deploy 2 (MIGRATE):  backfill the new column in batches
Deploy 3 (SWITCH):   code reads the new column
Deploy 4 (CONTRACT): drop the old column, make the new one NOT NULL
```

Renaming a column in a single deploy means the one pod that hasn't updated yet crashes immediately.

### CI/CD & Delivery

<p class="src">From <em>.NET Backend Deep Dive</em> · CI/CD & Delivery</p>

<a id="dn-187"></a>

#### Database migrations in a pipeline

The riskiest part of most deploys, because it's the one step that isn't easily reversible.

```bash
# Generate a reviewable SQL script rather than running Migrate() at startup
dotnet ef migrations script --idempotent --output migrate.sql
```

- **Don't call `db.Database.Migrate()` on startup** in a multi-replica deployment: every pod races to apply the same migration. Run it as a separate pipeline step or an init job with a lock.
- **Migrations must be backward compatible** with the currently-running version, because rolling deploys run both (Part 14.7). This is non-negotiable, not a best practice.
- **Never auto-generate destructive changes** without review. A rename that EF models as drop-then-add silently deletes a column of production data.
- Rehearse the **rollback**: forward-only migrations plus a tested restore path is usually more honest than pretending every migration has a clean `Down`.

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a111"></a>

#### Schema design: normalisation and when to break it

Indexes and query plans tune a schema. This is the step before: deciding what the tables are. Most performance and correctness problems attributed to an ORM are really schema problems that the ORM faithfully expressed.

| Form | Rule                                             | Violation looks like                                        |
|------|--------------------------------------------------|-------------------------------------------------------------|
| 1NF  | No repeating groups; one value per cell          | `Tags` column holding `"a,b,c"`                             |
| 2NF  | No partial dependency on part of a composite key | `ProductName` in `OrderLines` keyed on (OrderId, ProductId) |
| 3NF  | No dependency on a non-key column                | `CustomerCity` alongside `CustomerId`                       |
| BCNF | Every determinant is a candidate key             | Rare in practice; the edge case 3NF misses                  |

Third normal form is the right default for a transactional schema: each fact lives in exactly one place, so it cannot be updated in one place and not another. The point of knowing the forms is not to recite them but to recognise that a duplicated fact is a future inconsistency.

##### The four denormalisations that are usually correct

| Pattern                       | Example                                      | Why it is not a violation                                                                                        |
|-------------------------------|----------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| Point-in-time snapshot        | `InvoiceLine.ProductName`, `UnitPriceAtSale` | It is a *different fact*: what the name and price *were*. Renaming the product must not alter a historic invoice |
| Materialised aggregate        | `Order.Total`, `Post.CommentCount`           | A cache with a defined maintenance rule; needed when recomputation is too slow                                   |
| Read model / projection       | A flattened table built from events          | Derived and rebuildable; the normalised source remains authoritative                                             |
| JSON column for variable data | `Order.Metadata`                             | There is no fixed set of columns to normalise into                                                               |

``` csharp
// the snapshot vs reference distinction, made explicit in the model
public class InvoiceLine
{
    public Guid ProductId { get; init; }          // reference: for reporting and drill-through
    public string ProductName { get; init; } = "";// snapshot: what it was called when issued
    public string ProductSku { get; init; } = "";
    public Money UnitPrice { get; init; }         // snapshot: never recalculated
    public int Quantity { get; init; }
}
// an issued invoice is an immutable legal document. Joining to Products to get the
// current name is not a normalisation improvement - it is a correctness bug.
```

###### Choosing column types deliberately

| Instead of                 | Prefer                                        | Reason                                                         |
|----------------------------|-----------------------------------------------|----------------------------------------------------------------|
| `nvarchar(max)` everywhere | A real length limit                           | Off-row storage, no index support, and no validation           |
| `varchar` for identifiers  | `uuid` / `uniqueidentifier`                   | Half the bytes, type-checked, no case-sensitivity surprises    |
| `datetime`                 | `datetimeoffset` / `timestamptz`              | An instant without an offset is ambiguous                      |
| `float` for money          | `decimal(19,4)`                               | Exactness                                                      |
| `bit` flags in one column  | Separate boolean columns                      | Indexable, self-documenting, queryable                         |
| Nullable everything        | `NOT NULL` with a default                     | Every nullable column is a state the application must handle   |
| An enum as `int` only      | `int` + a lookup table, or a check constraint | The database can then reject nonsense, and reports can read it |

``` csharp
// enums: the three options, and when each fits
// 1. int column, C# enum, nothing in the database   -> fastest, database is opaque
b.Entity<Order>().Property(o => o.Status).HasConversion<int>();

// 2. string column                                  -> readable in the database, wider
b.Entity<Order>().Property(o => o.Status).HasConversion<string>().HasMaxLength(32);

// 3. int column + lookup table + FK                 -> the database enforces validity,
//    and reporting tools can join to get the label
b.Entity<Order>().HasOne(o => o.StatusRef).WithMany().HasForeignKey(o => o.Status);
```

Option 3 is unfashionable and frequently right: it means a bad migration or a direct SQL edit cannot insert status 99, and the BI team does not need to hardcode your enum values. Option 1 is fine when the application is the only writer and nobody reports directly off the tables — which is a stronger assumption than it first sounds.

> **\[Trap\]**
>
> **Common trap.** A `NOT NULL` column with no default added to a large table can rewrite it under an exclusive lock. Add it nullable, backfill in batches, then add the constraint as a separate step — the expand-and-contract pattern applies to constraints exactly as it does to columns.

<a id="be-a112"></a>

#### Keys: natural, surrogate and composite

The key choice affects every foreign key, every index, every URL and every integration for the life of the system. It is the least reversible decision in a schema.

|              | Natural key                                 | Surrogate key                    |
|--------------|---------------------------------------------|----------------------------------|
| Example      | ISO country code, ISBN, VAT number          | An auto-increment or UUID column |
| Meaning      | Carries business meaning                    | None — deliberately              |
| Stability    | Changes when the business changes           | Never changes                    |
| Foreign keys | Wide, and cascade on change                 | Narrow, and immutable            |
| Verdict      | Only for genuinely immutable reference data | The default for everything else  |

The argument against natural keys is simple: business identifiers change. Email addresses are reassigned, VAT numbers are reissued, employee numbers are reused after a decade, and the ‘definitely unique’ product code turns out to be duplicated in one region. A surrogate primary key with a *unique constraint* on the natural key gives you both identity stability and the uniqueness rule.

##### Which surrogate: sequential integer, or GUID

|                         | `bigint` identity | `Guid` v4                         | `Guid` v7 / sequential          |
|-------------------------|-------------------|-----------------------------------|---------------------------------|
| Size                    | 8 bytes           | 16 bytes                          | 16 bytes                        |
| Generated by            | The database      | The client                        | The client                      |
| Insert locality         | Perfect — appends | **Terrible** — random page splits | Good — time-ordered             |
| Known before insert     | No                | Yes                               | Yes                             |
| Safe to expose in URLs  | No — enumerable   | Yes                               | Partially — leaks creation time |
| Merges across databases | Painful           | Trivial                           | Trivial                         |

``` csharp
// Guid.CreateVersion7() (.NET 9) is time-ordered, so it behaves like a sequential key
// in the index while remaining globally unique and client-generated.
public class Order
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}

// on SQL Server, a random GUID clustered key causes constant page splits and
// index fragmentation. If you must use v4, keep the clustered index on something
// sequential and make the GUID a non-clustered unique key:
b.Entity<Order>().HasKey(o => o.Id);
b.Entity<Order>().Property<long>("Sequence").ValueGeneratedOnAdd();
b.Entity<Order>().HasIndex("Sequence").IsClustered();
```

###### Composite keys, and the exposure problem

Composite primary keys are correct for pure join tables and are a liability almost everywhere else: every referencing table carries every column, every URL needs every part, and every query must remember the order.

``` csharp
// legitimate: a join table with no identity of its own
b.Entity<OrderTag>().HasKey(x => new { x.OrderId, x.TagId });

// becomes a liability the moment the relationship gains attributes and its own
// references - at that point give it a surrogate key and a unique constraint:
b.Entity<Enrolment>().HasKey(e => e.Id);
b.Entity<Enrolment>().HasIndex(e => new { e.StudentId, e.CourseId }).IsUnique();
```

##### Do not expose sequential keys

`/orders/1041` tells an attacker that `/orders/1040` exists, lets a competitor estimate your order volume from two signups a week apart, and turns any missing authorization check into a bulk data extraction rather than a single leak.

``` csharp
// options, in increasing order of effort
// 1. a Guid v7 primary key                     -> simplest; leaks a creation timestamp
// 2. a public opaque id alongside the internal key
public class Order
{
    public long Id { get; init; }                             // internal, sequential, narrow FK
    public string PublicId { get; init; } = Nanoid.Generate(size: 12);   // "V1StGXR8Z5jd"
}
b.Entity<Order>().HasIndex(o => o.PublicId).IsUnique();
// 3. Hashids / Sqids to encode the integer reversibly - obfuscation, NOT security
```

> **\[Note\]**
>
> **Neither replaces authorization.** An unguessable identifier reduces the blast radius of a missing check; it does not substitute for one. Every request must still verify that *this* caller may see *this* resource — which is the resource-based authorization covered in the security part.

<a id="be-a113"></a>

#### Modelling hierarchies and graphs in SQL

Categories with subcategories, org charts, threaded comments, bill-of-materials, folder trees. Relational databases have no native tree type, and the four standard approaches trade read cost against write cost quite differently.

``` text
                    Electronics (1)
                    /            \
              Audio (2)        Video (4)
              /                    \
       Headphones (3)            TVs (5)
```

| Approach                           | Read a subtree     | Move a subtree               | Depth limit   | Fits                                        |
|------------------------------------|--------------------|------------------------------|---------------|---------------------------------------------|
| **Adjacency list** (`ParentId`)    | Recursive CTE      | One row update               | None          | The default. Shallow trees, frequent writes |
| **Path enumeration** (`"/1/2/3/"`) | One `LIKE 'path%'` | Rewrite the subtree          | Column length | Read-heavy trees, breadcrumbs               |
| **Nested sets** (left/right)       | One range query    | Renumber much of the table   | None          | Almost never write, read constantly         |
| **Closure table**                  | One join           | Delete + insert closure rows | None          | Complex queries, arbitrary graphs           |

``` sql
-- adjacency list plus a recursive CTE: works everywhere, and is usually enough
WITH descendants AS (
    SELECT id, parent_id, name, 0 AS depth
    FROM   categories WHERE id = @rootId
    UNION ALL
    SELECT c.id, c.parent_id, c.name, d.depth + 1
    FROM   categories c
    JOIN   descendants d ON c.parent_id = d.id
    WHERE  d.depth < 20            -- ALWAYS bound the recursion
)
SELECT * FROM descendants ORDER BY depth, name;
```

``` sql
-- path enumeration: reads become trivial, at the cost of write complexity
CREATE TABLE categories (
    id    bigint PRIMARY KEY,
    name  text   NOT NULL,
    path  text   NOT NULL          -- '/1/2/3/'
);
CREATE INDEX ix_categories_path ON categories (path varchar_pattern_ops);

-- entire subtree, one index range scan, no recursion
SELECT * FROM categories WHERE path LIKE '/1/2/%';
-- all ancestors, no query at all - parse the path in the application
-- depth, for free:  length(path) - length(replace(path,'/','')) - 1
```

``` sql
-- closure table: every ancestor-descendant pair, materialised
CREATE TABLE category_closure (
    ancestor_id   bigint NOT NULL,
    descendant_id bigint NOT NULL,
    depth         int    NOT NULL,
    PRIMARY KEY (ancestor_id, descendant_id)
);
-- for node 3: (1,3,2) (2,3,1) (3,3,0)

-- "all products in Electronics or below" - one join, no recursion, index-friendly
SELECT p.* FROM products p
JOIN   category_closure cc ON cc.descendant_id = p.category_id
WHERE  cc.ancestor_id = 1;
```

###### Native options, and when to leave SQL entirely

| Option         | Available in         | Note                                                                                           |
|----------------|----------------------|------------------------------------------------------------------------------------------------|
| `ltree`        | PostgreSQL extension | Path type with GiST indexing and ancestor/descendant operators — excellent, and Npgsql maps it |
| `hierarchyid`  | SQL Server           | Compact, ordered, supports reparenting; awkward from EF Core                                   |
| Recursive CTE  | Everything modern    | The portable baseline                                                                          |
| Graph database | Neo4j and similar    | Once traversals are many-hop and variable-length, and the relationships themselves carry data  |

The threshold for a graph database is higher than vendors suggest. A recursive CTE handles a five-level category tree or an org chart comfortably. Reach for a graph store when the questions are genuinely graph-shaped — shortest paths, arbitrary-depth traversal, relationship properties, cycles — and when they are the primary workload rather than one report.

> **\[Trap\]**
>
> **Common trap.** Every recursive query needs a depth bound and every hierarchy needs a cycle guard. A single bad row making a node its own ancestor turns a category page into an infinite loop that consumes a worker until it times out. Enforce acyclicity on write, and bound the recursion on read — belt and braces.

<a id="be-a114"></a>

#### Temporal data, audit trails and soft deletes

Three related requirements that are usually implemented three inconsistent ways in the same codebase: knowing what a row *was*, knowing *who changed it*, and knowing that something is *gone but not removed*.

##### Choosing a history mechanism

| Mechanism                                         | Gives you                                                      | Cost                                                        |
|---------------------------------------------------|----------------------------------------------------------------|-------------------------------------------------------------|
| Nothing                                           | Current state only                                             | None — and no answer to “what did it say last Tuesday?”     |
| `ModifiedAt` / `ModifiedBy` columns               | Who last touched it                                            | Trivial; loses everything before the last change            |
| Audit table per entity                            | Full row history, queryable                                    | Doubles write volume; schema must be maintained in parallel |
| System-versioned temporal tables                  | Full history, maintained by the engine, queryable with `AS OF` | SQL Server / MariaDB / PostgreSQL extension; storage growth |
| Event sourcing — see Terminology"\>Event sourcing | Every intent, not just every state                             | A different architecture, not a feature                     |

``` sql
-- SQL Server system-versioned table: the engine maintains the history
CREATE TABLE Products (
    Id          uniqueidentifier PRIMARY KEY,
    Name        nvarchar(200) NOT NULL,
    Price       decimal(19,4) NOT NULL,
    ValidFrom   datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    ValidTo     datetime2 GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.ProductsHistory));

-- "what did the catalogue look like at the moment that invoice was issued?"
SELECT * FROM Products FOR SYSTEM_TIME AS OF '2026-03-14T09:00:00';
```

``` csharp
// EF Core maps it, including the temporal query operators
b.Entity<Product>().ToTable("Products", t => t.IsTemporal());

var asOf = await db.Products
    .TemporalAsOf(invoice.IssuedAt)
    .SingleOrDefaultAsync(p => p.Id == id, ct);

var changes = await db.Products
    .TemporalAll()
    .Where(p => p.Id == id)
    .OrderBy(p => EF.Property<DateTime>(p, "ValidFrom"))
    .ToListAsync(ct);
```

###### Valid time vs system time, and why you may need both

``` text
  SYSTEM time  ("when did the database believe this?")     -> temporal tables
  VALID time   ("when was this true in the real world?")   -> your own columns

  a price rise announced on the 1st, effective on the 15th, corrected on the 3rd:

    system time  says the row changed on the 1st and again on the 3rd
    valid time   says the price is X until the 14th and Y from the 15th

  you need BOTH to answer "on the 20th, what did we think the price was on the 16th?"
  - which is exactly the question an auditor or a dispute resolution asks.
```

``` csharp
// bitemporal: model valid time explicitly, and let the engine handle system time
public class PriceListEntry
{
    public Guid ProductId { get; init; }
    public Money Price { get; init; }
    public DateOnly ValidFrom { get; init; }        // business effective dates
    public DateOnly? ValidTo { get; init; }         // null = still in force
}

// the current price is a query, not a column
var price = await db.PriceList
    .Where(p => p.ProductId == id
             && p.ValidFrom <= today
             && (p.ValidTo == null || p.ValidTo >= today))
    .OrderByDescending(p => p.ValidFrom)
    .Select(p => p.Price)
    .FirstAsync(ct);
```

Model valid time whenever the business speaks about effective dates — prices, tax rates, contracts, employment, insurance cover, addresses. It is far cheaper to build in at the start than to retrofit, because retrofitting means inventing history for existing rows.

###### Soft delete, and its four sharp edges

``` csharp
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

// applied globally so no query can forget
foreach (var type in modelBuilder.Model.GetEntityTypes()
             .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
{
    var param = Expression.Parameter(type.ClrType);
    var body = Expression.Equal(
        Expression.Property(param, nameof(ISoftDeletable.DeletedAt)),
        Expression.Constant(null, typeof(DateTimeOffset?)));
    modelBuilder.Entity(type.ClrType)
        .HasQueryFilter(Expression.Lambda(body, param));
}
```

| Sharp edge            | Symptom                                                                    | Handling                                                                           |
|-----------------------|----------------------------------------------------------------------------|------------------------------------------------------------------------------------|
| Unique constraints    | A user cannot re-register an email that belongs to a deleted account       | Filtered unique index: `WHERE deleted_at IS NULL`                                  |
| Foreign keys          | A live order references a deleted product; the database sees nothing wrong | Decide per relationship whether soft delete cascades, and enforce it in the domain |
| Aggregates and counts | Reports include deleted rows because raw SQL bypasses the filter           | Views that apply the filter; never let reporting hit base tables                   |
| It is not deletion    | A GDPR erasure request is not satisfied by setting a flag                  | A separate hard-delete or anonymisation path, tested                               |

The last point is worth stating plainly, because it is a compliance failure rather than a bug: soft delete exists so that *your application* hides a record, usually so an accidental deletion can be undone and referential history survives. It does nothing for a legal erasure obligation. Those need a separate operation that hard-deletes or anonymises the row, propagates to replicas, search indexes, caches, backups and analytics copies, and is exercised regularly enough that you know it works.

> **\[Note\]**
>
> **A defensible retention design.** Soft delete with a defined retention window; a scheduled job that hard-deletes or anonymises after it expires; an audit record of the erasure itself (which is permitted, and usually required); and a documented list of every downstream copy the job must reach. Write that list before you need it — discovering the fourth copy during a regulator's deadline is not the way.

### Schema Design & Modelling

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Schema Design & Modelling</p>

<p class="pd">Deciding what the tables are. This is the layer where mistakes are most expensive, because every query, index and application above it inherits the consequences.</p>

<a id="be-d401"></a>

#### Conceptual, logical and physical models

Three models, produced in order, each answering a different question. Skipping straight to `CREATE TABLE` is why so many schemas encode a screen layout rather than a business.

| Model      | Question                                  | Contains                                    | Audience                |
|------------|-------------------------------------------|---------------------------------------------|-------------------------|
| Conceptual | What things exist and how do they relate? | Entities and relationships, no attributes   | Domain experts          |
| Logical    | What are the facts about each thing?      | Attributes, keys, cardinality, normalised   | Analysts and developers |
| Physical   | How is it stored and accessed?            | Types, indexes, partitions, denormalisation | Developers and DBAs     |

``` text
  crow's foot notation, which you will meet in every ER diagram

    ---||----      exactly one
    ---o|----      zero or one
    ---|<----      one or many
    ---o<----      zero or many

  CUSTOMER ||-----o< ORDER ||-----|< ORDER_LINE >|-----|| PRODUCT

  read it aloud: "a customer has zero or many orders;
                  an order has one or many lines;
                  a line refers to exactly one product."

  the sentence is the specification. If a domain expert disagrees with the
  sentence, the model is wrong - and that conversation is far cheaper now
  than after 200 tables depend on it.
```

The most valuable output of the conceptual stage is not the diagram. It is the list of nouns the business actually uses, and the discovery that two departments mean different things by the same word — which usually means two entities, not one.

<a id="be-d402"></a>

#### Normalisation, form by form

Normalisation is one idea applied repeatedly: *every fact should be stored exactly once*. A fact stored twice will eventually be updated once.

##### The anomalies it prevents

``` text
  an unnormalised orders table

  order_id | customer_name | customer_city | product   | qty | unit_price
  ---------|---------------|---------------|-----------|-----|------------
  1001     | Acme B.V.     | Amsterdam     | Widget    | 2   | 19.99
  1001     | Acme B.V.     | Amsterdam     | Gadget    | 1   | 249.00
  1002     | Acme B.V.     | Amsterdam     | Widget    | 5   | 19.99

  UPDATE anomaly    Acme moves to Utrecht -> 3 rows to change; miss one and the
                    database now holds two contradictory facts, both "true".
  INSERT anomaly    a new customer with no orders cannot be recorded at all.
  DELETE anomaly    deleting order 1002 may erase the only record of a product.
```

| Form          | Requires                                                        | Fix                                                 |
|---------------|-----------------------------------------------------------------|-----------------------------------------------------|
| **1NF**       | Atomic values; no repeating groups or arrays-as-strings         | Split `"a,b,c"` into rows                           |
| **2NF**       | 1NF + no non-key attribute depends on part of a composite key   | Move partially-dependent columns to their own table |
| **3NF**       | 2NF + no non-key attribute depends on another non-key attribute | Move transitively-dependent columns out             |
| **BCNF**      | 3NF + every determinant is a candidate key                      | Rare edge case; decompose the offending dependency  |
| **4NF / 5NF** | No multi-valued or join dependencies                            | Almost never encountered in practice                |

The informal statement that covers 1NF through BCNF: *every non-key attribute must depend on the key, the whole key, and nothing but the key.* If you can state that and mean it, you can normalise without memorising the numbered forms.

``` sql
-- 3NF: each fact in exactly one place
CREATE TABLE customers  (id bigint PRIMARY KEY, name text NOT NULL, city text NOT NULL);
CREATE TABLE products   (id bigint PRIMARY KEY, name text NOT NULL, list_price numeric(19,4));
CREATE TABLE orders     (id bigint PRIMARY KEY,
                         customer_id bigint NOT NULL REFERENCES customers(id),
                         placed_at timestamptz NOT NULL);
CREATE TABLE order_lines(order_id bigint NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
                         product_id bigint NOT NULL REFERENCES products(id),
                         qty int NOT NULL CHECK (qty > 0),
                         unit_price numeric(19,4) NOT NULL,   -- see the next section
                         PRIMARY KEY (order_id, product_id));
```

> **\[Note\]**
>
> **Normalise first, then decide.** It is far easier to denormalise a normalised model deliberately than to untangle a denormalised one. Start at 3NF as the default and step back from it with a stated reason.

<a id="be-d403"></a>

#### Denormalisation, deliberately

Four denormalisations are so common and so correct that calling them violations is a misunderstanding. What makes them legitimate is that each stores a *different fact*, or accepts a defined maintenance cost for a measured gain.

| Pattern                     | Example                                              | Justification                                                              |
|-----------------------------|------------------------------------------------------|----------------------------------------------------------------------------|
| **Point-in-time snapshot**  | `order_lines.unit_price`, `invoice.customer_name`    | It is a different fact: what the price *was*, not what it *is*             |
| **Materialised aggregate**  | `orders.total`, `posts.comment_count`                | A cache with a defined refresh rule; needed when recomputation is too slow |
| **Read model / projection** | A flat reporting table built from normalised sources | Derived and rebuildable; the source remains authoritative                  |
| **Semi-structured column**  | `orders.metadata jsonb`                              | There is no fixed attribute set to normalise into                          |

The snapshot case is the one people get wrong most often, and it is a correctness issue rather than a performance one. An issued invoice is a legal document. Joining to `products` to display the current name means renaming a product silently rewrites history.

###### Maintaining a materialised aggregate

``` sql
-- three ways to keep orders.total in step with order_lines, worst to best

-- 1. application recalculates and writes it
--    breaks the moment anything else writes to order_lines: a migration,
--    a support fix, a second service.

-- 2. a trigger
CREATE FUNCTION refresh_order_total() RETURNS trigger AS $$
BEGIN
    UPDATE orders o SET total = (SELECT COALESCE(SUM(qty * unit_price), 0)
                                 FROM order_lines WHERE order_id = o.id)
    WHERE o.id = COALESCE(NEW.order_id, OLD.order_id);
    RETURN NULL;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_order_total
AFTER INSERT OR UPDATE OR DELETE ON order_lines
FOR EACH ROW EXECUTE FUNCTION refresh_order_total();
--    correct for every writer, but invisible, and a hot spot under concurrency:
--    every line insert updates the same parent row, serialising them.

-- 3. a generated / computed column, where the engine maintains it
ALTER TABLE order_lines ADD line_total numeric(19,4)
    GENERATED ALWAYS AS (qty * unit_price) STORED;
--    no trigger, cannot drift. Only works for same-row expressions -
--    cross-row aggregates still need option 2 or a materialised view.
```

| Maintenance strategy         | Consistency                         | Cost                                  |
|------------------------------|-------------------------------------|---------------------------------------|
| Compute on read              | Always correct                      | Slow if the child set is large        |
| Generated column (same row)  | Always correct, engine-enforced     | Storage; recomputed on write          |
| Trigger                      | Correct for all writers             | Hidden logic; parent-row contention   |
| Materialised view, refreshed | Stale by the refresh interval       | Cheap reads; needs a refresh schedule |
| Application-maintained       | Correct only if nothing else writes | Fragile — assume something else will  |

> **\[Trap\]**
>
> **Common trap.** Once a value is both stored and derivable, it will eventually disagree with its source — after a partial refund, a bad migration, or a rounding change. Decide which is authoritative and write a reconciliation check that reports drift. For an issued invoice the stored value is authoritative and recomputing it is the bug; for a comment count the source is authoritative and the cache should be repaired.

<a id="be-d404"></a>

#### Keys: primary, natural, surrogate, composite

The key choice propagates into every foreign key, index, URL and integration, and is the least reversible decision in a schema.

|                  | Natural key                                 | Surrogate key                   |
|------------------|---------------------------------------------|---------------------------------|
| Example          | ISO code, ISBN, VAT number                  | An identity column or UUID      |
| Business meaning | Yes                                         | None, deliberately              |
| Stability        | Changes when the business changes           | Never                           |
| Foreign keys     | Wide; a change cascades everywhere          | Narrow and immutable            |
| Verdict          | Only for genuinely immutable reference data | The default for everything else |

Business identifiers change: email addresses are reassigned, VAT numbers reissued, employee numbers reused, and the ‘definitely unique’ product code turns out to be duplicated in one region. A surrogate primary key plus a *unique constraint* on the natural key gives you stable identity and the uniqueness rule, with no conflict between them.

##### Which surrogate

|                     | `bigint` identity            | UUID v4                       | UUID v7 / sequential       |
|---------------------|------------------------------|-------------------------------|----------------------------|
| Width               | 8 bytes                      | 16 bytes                      | 16 bytes                   |
| Generated by        | The database                 | The client                    | The client                 |
| Insert locality     | Perfect — appends to the end | **Poor** — random page splits | Good — time-ordered        |
| Known before insert | No                           | Yes                           | Yes                        |
| Safe in URLs        | No — enumerable              | Yes                           | Leaks a creation timestamp |
| Multi-source merges | Painful                      | Trivial                       | Trivial                    |

``` sql
-- if you must use random UUIDs on SQL Server, do NOT cluster on them:
-- random inserts across the whole B-tree cause constant page splits and fragmentation.
CREATE TABLE orders (
    id        uniqueidentifier NOT NULL DEFAULT NEWID(),
    seq       bigint IDENTITY(1,1) NOT NULL,
    ...
    CONSTRAINT pk_orders PRIMARY KEY NONCLUSTERED (id),
    INDEX ix_orders_seq CLUSTERED (seq)          -- physical order is sequential
);
-- PostgreSQL heap tables have no clustering key, so the problem is milder,
-- but index bloat on a random-UUID primary key index is still real.
```

###### Composite keys, and not exposing sequential identifiers

``` sql
-- composite key: correct for a pure join table with no identity of its own
CREATE TABLE order_tags (
    order_id bigint NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    tag_id   bigint NOT NULL REFERENCES tags(id),
    PRIMARY KEY (order_id, tag_id)
);

-- the moment the relationship gains attributes and its own references,
-- give it a surrogate key and keep the uniqueness as a constraint
CREATE TABLE enrolments (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    student_id bigint NOT NULL REFERENCES students(id),
    course_id  bigint NOT NULL REFERENCES courses(id),
    enrolled_at timestamptz NOT NULL,
    grade      text,
    UNIQUE (student_id, course_id)
);
```

`/orders/1041` tells a caller that `/orders/1040` exists, lets a competitor estimate your volume from two observations a week apart, and turns any missing authorization check into bulk extraction rather than a single leak.

``` sql
-- an internal narrow key plus an external opaque one
CREATE TABLE orders (
    id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,  -- narrow FKs, sequential
    public_id text NOT NULL,                                    -- 'V1StGXR8Z5jd'
    ...
    UNIQUE (public_id)
);
```

> **\[Note\]**
>
> **This is not authorization.** An unguessable identifier reduces the blast radius of a missing check. It does not replace one. Every request must still verify that this caller may see this row.

<a id="be-d405"></a>

#### Relationship patterns

Six shapes cover nearly every relationship you will model, and two of them have a standard trap.

| Shape            | Implementation                              | Note                                           |
|------------------|---------------------------------------------|------------------------------------------------|
| One to many      | Foreign key on the *many* side              | The default; nothing subtle                    |
| Many to many     | A junction table with two foreign keys      | Give it a surrogate key once it has attributes |
| One to one       | Shared primary key, or a unique foreign key | Ask whether it should just be one table        |
| Self-referencing | A foreign key to the same table             | Hierarchies — see the next section             |
| Optional         | Nullable foreign key                        | Legitimate; better than a magic 'none' row     |
| Polymorphic      | See below                                   | The one that has no clean answer               |

##### One to one: usually a smell, sometimes right

- **Right:** splitting rarely-accessed wide columns (a large document, a blob) off a hot narrow table.
- **Right:** subtype tables, where each subtype has genuinely different attributes.
- **Right:** separating data with different security or retention requirements.
- **Wrong:** splitting because the table “has too many columns”. Column count is not a cost; every query now needs a join.

##### Polymorphic associations: the four options

``` sql
-- a comment can attach to a post, a photo, or a product.

-- 1. "shotgun" nullable FKs: honest, checkable, gets unwieldy past ~5 targets
CREATE TABLE comments (
    id bigint PRIMARY KEY,
    post_id    bigint REFERENCES posts(id),
    photo_id   bigint REFERENCES photos(id),
    product_id bigint REFERENCES products(id),
    body text NOT NULL,
    CHECK (num_nonnulls(post_id, photo_id, product_id) = 1)   -- exactly one target
);

-- 2. exclusive-arc via a type discriminator: NO referential integrity possible
CREATE TABLE comments (
    id bigint PRIMARY KEY,
    target_type text NOT NULL CHECK (target_type IN ('post','photo','product')),
    target_id   bigint NOT NULL,     -- cannot be a foreign key: it points at 3 tables
    body text NOT NULL
);
-- convenient, and the database can no longer prevent orphans. Common in ORMs.

-- 3. a shared supertype table: full integrity, one extra join
CREATE TABLE commentables (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                           kind text NOT NULL);
CREATE TABLE posts    (id bigint PRIMARY KEY REFERENCES commentables(id), ...);
CREATE TABLE photos   (id bigint PRIMARY KEY REFERENCES commentables(id), ...);
CREATE TABLE comments (id bigint PRIMARY KEY,
                       commentable_id bigint NOT NULL REFERENCES commentables(id), ...);

-- 4. a junction table per target: most tables, most integrity, no ambiguity
CREATE TABLE post_comments  (comment_id bigint, post_id  bigint, PRIMARY KEY (comment_id));
CREATE TABLE photo_comments (comment_id bigint, photo_id bigint, PRIMARY KEY (comment_id));
```

| Option              | Integrity | Query ease     | Adding a target |
|---------------------|-----------|----------------|-----------------|
| Nullable FKs        | Full      | Good           | Adds a column   |
| Type + id           | **None**  | Easiest        | Free            |
| Supertype table     | Full      | One extra join | Adds a table    |
| Junction per target | Full      | Verbose        | Adds a table    |

> **\[Note\]**
>
> **Recommendation.** Up to about four targets, nullable foreign keys with a `CHECK` for exactly-one. Beyond that, the supertype table. Reach for the type-plus-id form only when the target set is genuinely open-ended, and accept that you have given up referential integrity — then compensate with a scheduled orphan-detection job.

<a id="be-d406"></a>

#### Hierarchies and graphs

Categories, org charts, threaded comments, bills of materials, folder trees. There is no native tree type in standard SQL, and the four approaches trade read cost against write cost quite differently.

``` text
                    Electronics (1)
                    /            \
              Audio (2)        Video (4)
                /                  \
       Headphones (3)            TVs (5)
```

| Approach                           | Read a subtree     | Move a subtree                   | Fits                                        |
|------------------------------------|--------------------|----------------------------------|---------------------------------------------|
| **Adjacency list** (`parent_id`)   | Recursive CTE      | One row update                   | The default. Shallow trees, frequent writes |
| **Path enumeration** (`'/1/2/3/'`) | One `LIKE 'path%'` | Rewrite the subtree              | Read-heavy, breadcrumbs                     |
| **Nested sets** (left/right)       | One range query    | Renumber much of the table       | Read constantly, write almost never         |
| **Closure table**                  | One join           | Delete and reinsert closure rows | Complex queries, arbitrary DAGs             |

``` sql
-- adjacency list + recursive CTE: portable, and usually sufficient
WITH RECURSIVE subtree AS (
    SELECT id, parent_id, name, 0 AS depth, ARRAY[id] AS path
    FROM   categories WHERE id = :root
  UNION ALL
    SELECT c.id, c.parent_id, c.name, s.depth + 1, s.path || c.id
    FROM   categories c
    JOIN   subtree s ON c.parent_id = s.id
    WHERE  s.depth < 20                    -- ALWAYS bound the recursion
      AND  NOT c.id = ANY(s.path)          -- and guard against cycles
)
SELECT * FROM subtree ORDER BY path;
```

``` sql
-- closure table: every ancestor/descendant pair, materialised
CREATE TABLE category_closure (
    ancestor_id   bigint NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    descendant_id bigint NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    depth         int NOT NULL,
    PRIMARY KEY (ancestor_id, descendant_id)
);
-- node 3 has rows: (1,3,2) (2,3,1) (3,3,0)   -- including itself at depth 0

-- "every product in Electronics or below" - one join, no recursion, index-friendly
SELECT p.* FROM products p
JOIN   category_closure cc ON cc.descendant_id = p.category_id
WHERE  cc.ancestor_id = 1;

-- inserting a node: copy the parent's ancestors, plus self
INSERT INTO category_closure (ancestor_id, descendant_id, depth)
SELECT ancestor_id, :new_id, depth + 1 FROM category_closure WHERE descendant_id = :parent_id
UNION ALL SELECT :new_id, :new_id, 0;
```

###### Native support, and when to leave SQL

| Feature                 | Engine               | Note                                                              |
|-------------------------|----------------------|-------------------------------------------------------------------|
| `ltree`                 | PostgreSQL extension | A path type with GiST indexing and ancestor operators — excellent |
| `hierarchyid`           | SQL Server           | Compact, ordered, supports reparenting                            |
| `CONNECT BY`            | Oracle               | Predates recursive CTEs; same purpose                             |
| Recursive CTE           | All modern engines   | The portable baseline                                             |
| `SQL/PGQ` graph queries | Emerging (SQL:2023)  | Pattern matching over graphs, in standard SQL                     |

The threshold for a dedicated graph database is higher than commonly claimed. A recursive CTE handles a five-level category tree or an org chart comfortably. Move to a graph store when traversals are variable-depth and many-hop, relationships themselves carry attributes and are queried, and that workload is the primary one — not one report.

> **\[Trap\]**
>
> **Common trap.** Every recursive query needs a depth bound *and* a cycle guard. One bad row making a node its own ancestor turns a page load into an infinite loop that consumes a connection until it times out. Enforce acyclicity on write and bound the recursion on read.

<a id="be-d407"></a>

#### Temporal data and history

Two independent time dimensions, routinely conflated, and the confusion produces reports that nobody can reconcile.

``` text
  SYSTEM time    "when did the database believe this?"       -> row versioning
  VALID time     "when was this true in the real world?"     -> your own columns

  a price rise announced on the 1st, effective on the 15th, corrected on the 3rd

    system time  the row changed on the 1st, and again on the 3rd
    valid time   the price is X until the 14th, Y from the 15th

  you need BOTH to answer: "on the 20th, what did we believe the price
  was on the 16th?" - which is precisely what an audit or a dispute asks.
```

| Mechanism                        | Gives                                            | Cost                                        |
|----------------------------------|--------------------------------------------------|---------------------------------------------|
| `modified_at` / `modified_by`    | Who touched it last                              | Trivial; loses all prior history            |
| Audit table per entity           | Full row history                                 | Doubles writes; parallel schema to maintain |
| System-versioned temporal tables | Full history, engine-maintained, `AS OF` queries | Storage growth; engine support varies       |
| Valid-time columns               | Business effective dating                        | Every query must filter on the period       |
| Event sourcing                   | Every intent, not just every state               | A different architecture entirely           |

``` sql
-- system-versioned table: the engine maintains the history table
CREATE TABLE products (
    id        uuid PRIMARY KEY,
    name      varchar(200) NOT NULL,
    price     decimal(19,4) NOT NULL,
    valid_from datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    valid_to   datetime2 GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
    PERIOD FOR SYSTEM_TIME (valid_from, valid_to)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.products_history));

-- what did the catalogue look like when that invoice was issued?
SELECT * FROM products FOR SYSTEM_TIME AS OF '2026-03-14T09:00:00';
```

``` sql
-- valid time, modelled explicitly: the current price is a QUERY, not a column
CREATE TABLE price_list (
    product_id bigint NOT NULL REFERENCES products(id),
    price      numeric(19,4) NOT NULL,
    valid_from date NOT NULL,
    valid_to   date,                                    -- null = still in force
    PRIMARY KEY (product_id, valid_from),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
);

-- and prevent overlapping periods at the database level
ALTER TABLE price_list ADD EXCLUDE USING gist (
    product_id WITH =,
    daterange(valid_from, valid_to, '[)') WITH &&
);

SELECT price FROM price_list
WHERE product_id = :id
  AND valid_from <= :on_date
  AND (valid_to IS NULL OR valid_to > :on_date);
```

> **\[Note\]**
>
> **Model valid time whenever the business says “effective from”.** Prices, tax rates, contracts, employment, insurance cover, addresses, org structure. It is far cheaper to build in than to retrofit, because retrofitting means inventing history for rows that never recorded it.

<a id="be-d408"></a>

#### Soft deletes and retention

“Deleted but recoverable” is a reasonable requirement with four sharp edges that catch every team that implements it casually.

``` sql
ALTER TABLE customers ADD deleted_at timestamptz;
ALTER TABLE customers ADD deleted_by text;
```

| Sharp edge         | Symptom                                                                       | Handling                                                                |
|--------------------|-------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| Unique constraints | A user cannot re-register an email belonging to a deleted account             | Partial unique index: `WHERE deleted_at IS NULL`                        |
| Foreign keys       | A live order references a deleted product and the database sees nothing wrong | Decide per relationship whether deletion propagates, and enforce it     |
| Forgotten filters  | Reports include deleted rows because raw SQL bypasses the application         | Expose views that apply the filter; never let reporting hit base tables |
| It is not deletion | A legal erasure request is not satisfied by a flag                            | A separate hard-delete or anonymisation path, tested                    |

``` sql
-- the partial unique index that makes re-registration work
CREATE UNIQUE INDEX ux_customers_email_active
    ON customers (lower(email)) WHERE deleted_at IS NULL;

-- and a view so that "customers" means "live customers" to every consumer
CREATE VIEW active_customers AS SELECT * FROM customers WHERE deleted_at IS NULL;
```

The last row of the table is a compliance point rather than a bug. Soft delete exists so *your application* hides a record and an accident can be undone. A statutory erasure obligation requires an operation that hard-deletes or anonymises, and that propagates to replicas, search indexes, caches, analytics copies and backups. Write the list of downstream copies before you need it.

> **\[Note\]**
>
> **A defensible design.** Soft delete with a defined retention window; a scheduled job that anonymises or hard-deletes when it expires; an audit record of the erasure itself (permitted, and usually required); and a documented list of every place a copy lives.

<a id="be-d409"></a>

#### Schema anti-patterns

| Anti-pattern               | Looks like                              | Why it hurts                                                   | Instead                                              |
|----------------------------|-----------------------------------------|----------------------------------------------------------------|------------------------------------------------------|
| **EAV**                    | `(entity_id, attribute, value)` as text | No types, no constraints, every query is a self-join pivot     | Real columns; JSON for the genuinely variable part   |
| **One true lookup table**  | One `codes` table for every enum        | No foreign keys are meaningful; every join needs a type filter | A lookup table per domain                            |
| **Comma-separated values** | `tags = 'a,b,c'`                        | Violates 1NF; unindexable; `LIKE '%b%'` matches wrongly        | A junction table, or a native array with a GIN index |
| **Metadata tribbles**      | `sales_2024`, `sales_2025`              | Every query needs dynamic SQL; DDL on every new period         | Partitioning                                         |
| **Wide sparse table**      | 120 columns, most null on most rows     | One table modelling several entity types                       | Split into subtype tables                            |
| **No natural ordering**    | Relying on insertion order              | Row order is undefined                                         | An explicit sequence or timestamp column             |
| **Booleans as free text**  | `'Y'/'N'/'y'/'yes'/''`                  | Five representations of two states                             | `boolean NOT NULL`                                   |
| **Nullable everything**    | Every column nullable ‘for flexibility’ | Every state must be handled everywhere                         | `NOT NULL` with defaults                             |

###### EAV: why it is tempting and what it costs

``` sql
-- EAV: infinitely flexible, and it gives up everything the database is for
CREATE TABLE attributes (entity_id bigint, name text, value text);

-- "products under 100 euro, in red, in stock" becomes:
SELECT p.entity_id
FROM   attributes p
JOIN   attributes c ON c.entity_id = p.entity_id AND c.name = 'colour'
JOIN   attributes s ON s.entity_id = p.entity_id AND s.name = 'stock'
WHERE  p.name = 'price' AND CAST(p.value AS numeric) < 100      -- unindexable cast
  AND  c.value = 'red'
  AND  CAST(s.value AS int) > 0;
-- one self-join per attribute, no type safety, no constraints,
-- and the optimiser cannot estimate cardinality on any of it.
```

``` sql
-- the modern answer: fixed columns for what you query, JSON for what varies
CREATE TABLE products (
    id       bigint PRIMARY KEY,
    sku      text NOT NULL UNIQUE,
    price    numeric(19,4) NOT NULL,      -- queried, sorted, constrained -> a column
    stock    int NOT NULL DEFAULT 0,
    attrs    jsonb NOT NULL DEFAULT '{}'  -- per-category, open-ended -> JSON
);
CREATE INDEX ix_products_attrs ON products USING gin (attrs);

SELECT * FROM products
WHERE price < 100 AND stock > 0 AND attrs @> '{"colour":"red"}';
-- index on price, index on the JSON containment, no self-joins, real types.
```

The test for column-versus-JSON: *do you filter, sort, join or constrain on it?* If yes, it is a column. If it is read as part of the whole record and varies per tenant or per category, it is JSON. EAV is what you build when you refuse to make that decision.

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a98"></a>

#### Migrations and schema change

EF migrations turn model diffs into versioned C# up/down scripts. They are excellent for development and need discipline for production.

``` bash
dotnet ef migrations add AddOrderCancelledAt
dotnet ef migrations script 20260701_Prev 20260727_AddOrderCancelledAt -o deploy.sql
dotnet ef database update            # dev only
```

1.  Generate an idempotent SQL script in CI — never let the app call `Migrate()` on startup in production, because N replicas will race.
2.  Review the script like code. EF happily emits a column drop that loses data.
3.  Apply it as a separate, gated pipeline stage before the new app version rolls out.
4.  Make every change backwards compatible for one release so old and new pods can run side by side.

##### Expand and contract

Rename `Name` to `FullName` without downtime: **expand** (add `FullName`, write to both, read from old) → **migrate** (backfill, switch reads) → **contract** (stop writing old, drop the column in a later release). Three deploys, zero outage.

###### Worked example

Migrations that are safe on a large table are a different skill from migrations that work on a laptop. The difference is locking.

``` sql
-- looks harmless. On a 200-million-row table in PostgreSQL:
ALTER TABLE orders ADD COLUMN region text NOT NULL DEFAULT 'EU';
-- PG 11+: fast (default stored in the catalogue). PG 10 and earlier: full table rewrite.
-- SQL Server: fast for a constant default since 2012 Enterprise; a rewrite otherwise.

-- unambiguously dangerous everywhere:
ALTER TABLE orders ALTER COLUMN status TYPE varchar(50);   -- full rewrite, ACCESS EXCLUSIVE lock
CREATE INDEX ix_orders_region ON orders (region);          -- blocks writes for the duration
ALTER TABLE orders ADD CONSTRAINT fk_x FOREIGN KEY ...;    -- full validation scan under lock
```

``` sql
-- the safe forms
CREATE INDEX CONCURRENTLY ix_orders_region ON orders (region);   -- PostgreSQL, no write lock
CREATE INDEX ix_orders_region ON orders (region) WITH (ONLINE = ON);  -- SQL Server Enterprise

-- add the constraint unvalidated, then validate without a heavy lock
ALTER TABLE orders ADD CONSTRAINT fk_x FOREIGN KEY (customer_id)
      REFERENCES customers (id) NOT VALID;
ALTER TABLE orders VALIDATE CONSTRAINT fk_x;                      -- SHARE UPDATE EXCLUSIVE only

-- always bound the lock wait, so a migration fails fast instead of queueing behind
-- a long transaction and blocking every subsequent query
SET lock_timeout = '5s';
SET statement_timeout = '10min';
```

``` csharp
// backfills must be batched, or they hold one enormous transaction
public async Task BackfillAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var affected = await _db.Database.ExecuteSqlAsync($@"
            UPDATE orders SET customer_notes = notes
            WHERE id IN (SELECT id FROM orders
                         WHERE customer_notes IS NULL AND notes IS NOT NULL
                         LIMIT 5000)", ct);

        if (affected == 0) break;
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);   // let replicas catch up
    }
}
```

The `lock_timeout` line is the one that turns a two-hour outage into a failed pipeline step. Without it, a DDL statement that cannot acquire its lock waits — and because it is waiting for an ACCESS EXCLUSIVE lock, every subsequent query on that table queues behind it. One long-running report plus one unbounded ALTER equals a total outage on a table that was working fine.

###### Migrations in a pipeline, with rollback

1.  **Generate an idempotent script in CI:** `dotnet ef migrations script --idempotent --output migrate.sql`. It is safe to run twice.
2.  **Diff it in the pull request.** A human reads the SQL. This catches the accidental column drop that the model diff produced.
3.  **Run it as a pipeline stage**, before the application rollout, from a single runner — never from application startup with N replicas racing.
4.  **Take a backup or a snapshot immediately before**, and record the restore command in the deployment log.
5.  **Do not write down-migrations for production.** They are almost never exercised and rarely correct. Roll *forward* with a new migration, and use expand/contract so the previous app version still works against the new schema.
6.  **Verify.** A post-migration smoke test that queries the changed tables, so a broken deploy is caught by the pipeline rather than by users.

``` bash
# pipeline stage
- name: Apply migrations
  run: |
    dotnet ef migrations script --idempotent --no-build \
      --project src/Orders.Infrastructure --startup-project src/Orders.Api \
      --output migrate.sql
    psql "$CONN" -v ON_ERROR_STOP=1 -c "SET lock_timeout='5s';" -f migrate.sql
```

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a207"></a>

#### Database DevOps beyond EF migrations

EF Core migrations are excellent for a single application that owns its schema. They are a poor fit when a DBA team owns the schema, when several applications share a database, when stored procedures and views are first-class, or when the deployment must be a reviewed SQL artifact. Those situations are common in enterprise .NET, and there is a whole tooling category for them.

| Tool               | Model                                                                | Fits when                                                             |
|--------------------|----------------------------------------------------------------------|-----------------------------------------------------------------------|
| EF Core migrations | Model-first; C# migrations generated from the model diff             | One app owns the schema; the model is the source of truth             |
| DbUp               | Ordered SQL scripts, tracked in a journal table                      | You want plain SQL, minimal magic, full control                       |
| Flyway / Liquibase | Versioned + repeatable migrations, checksums                         | Polyglot shops; strong conventions; enterprise standard               |
| SSDT / DACPAC      | State-based — declare the desired schema, the tool computes the diff | SQL Server shops with a DBA team; schema in source control as objects |
| Grate / RoundhousE | Script folders by phase (before, up, functions, after)               | Heavy use of procedures, views and functions                          |

**Migration-based (EF, DbUp, Flyway)**

- Explicit, ordered, reviewable steps
- You control exactly how each change happens
- Data migrations are natural
- Drift is invisible — nothing verifies the end state

**State-based (DACPAC, SSDT)**

- The desired schema is the source of truth
- Drift is detected and corrected automatically
- Excellent for procedures, views and permissions
- The generated diff can be destructive — must be reviewed

``` csharp
// DbUp: a migration runner as a tiny console app, run as a pipeline stage or init container
var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(),
        s => s.StartsWith("Migrations.Scripts."))
    .WithTransactionPerScript()
    .WithExecutionTimeout(TimeSpan.FromMinutes(30))
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;                                 // fail the pipeline stage
}
Console.WriteLine("Schema up to date.");
return 0;
```

###### A migration pipeline that is safe regardless of the tool

1.  **Migrations are a separate artifact** from the application — a container image or a script bundle, versioned alongside it.
2.  **Generate and review the SQL** in the pull request. For state-based tools this is essential: publish a diff report and fail the build if it contains a drop.
3.  **Run against a restored copy of production** in CI. A migration that works on an empty schema and locks a 200-million-row table is the classic escape.
4.  **Measure the runtime.** If a migration takes eleven minutes in CI against production-sized data, that is eleven minutes of your deployment window, and it needs to be known in advance.
5.  **Apply once, from one place**, before the application rollout, with a lock timeout so it fails fast rather than blocking every query on the table.
6.  **Never roll back schema; roll forward.** Combined with expand/contract, the previous application version keeps working, which is what makes application rollback safe.
7.  **Verify.** A post-migration check that the expected objects exist and a smoke query returns, so a partial failure is caught by the pipeline.

``` text
# Kubernetes: migrations as a Job that must complete before the rollout
apiVersion: batch/v1
kind: Job
metadata: { name: orders-migrate-1-14-2 }
spec:
  backoffLimit: 0                 # do NOT retry blindly - a half-applied migration needs eyes
  template:
    spec:
      restartPolicy: Never
      containers:
        - name: migrate
          image: registry.internal/orders-migrator:1.14.2
          env:
            - name: ConnectionStrings__Default
              valueFrom: { secretKeyRef: { name: orders-db, key: connstring } }
```

> **\[Trap\]**
>
> **Common trap.** Running migrations from application startup with N replicas is the single most common way to corrupt a schema. Even with a lock, you get N pods blocked on boot, failing readiness probes, and a deployment that appears to hang. Migrations are a deployment step, not an application concern.

### Specialised & Advanced

<p class="src">From <em>Backend Engineering</em> · Relational Databases · Specialised & Advanced</p>

<p class="pd">Capabilities beyond ordinary CRUD, plus the engineering practice around databases: procedural code, search, spatial and time-series data, change data capture, migrations, and a method for tuning.</p>

<a id="be-d479"></a>

#### Database DevOps: migrations and testing

Schema change is code change and deserves the same discipline: version controlled, reviewed, tested, and applied by a pipeline rather than by a person with a console.

| Tool               | Model                                            | Fits                                 |
|--------------------|--------------------------------------------------|--------------------------------------|
| Flyway / Liquibase | Versioned SQL scripts with checksums             | Polyglot shops; strong conventions   |
| DbUp / Grate       | Ordered scripts in a journal table               | Plain SQL, minimal magic             |
| SSDT / DACPAC      | State-based — declare the schema, the tool diffs | SQL Server with a DBA team           |
| ORM migrations     | Generated from a model diff                      | One application owns the schema      |
| Atlas / Sqitch     | Declarative or dependency-based                  | Modern alternatives worth evaluating |

1.  **Every change is a script in version control.** No exceptions, including hotfixes.
2.  **Generate and review the SQL** in the pull request — especially for state-based tools, where the diff may contain a drop.
3.  **Test against a restored copy of production.** A migration that works on an empty schema and locks a 200-million-row table is the classic escape.
4.  **Measure the runtime.** Eleven minutes in CI is eleven minutes of your deployment window.
5.  **Apply once, from one place**, before the application rollout, with a `lock_timeout` so it fails fast rather than blocking every query.
6.  **Roll forward, not back.** Combined with expand-and-contract, the previous application version keeps working, which is what makes application rollback safe.

``` sql
-- always bound the lock wait. Without this, a DDL statement that cannot acquire
-- its lock QUEUES - and every subsequent query on that table queues behind it.
-- One long report plus one unbounded ALTER equals a full outage.
SET lock_timeout = '5s';
SET statement_timeout = '10min';

-- and use the non-blocking forms
CREATE INDEX CONCURRENTLY ...;
ALTER TABLE t ADD CONSTRAINT fk FOREIGN KEY (...) REFERENCES ... NOT VALID;
ALTER TABLE t VALIDATE CONSTRAINT fk;      -- a much weaker lock
```

###### Expand and contract, as three releases

``` sql
-- goal: rename orders.notes to orders.customer_notes with zero downtime,
-- while three application replicas are rolling.

-- RELEASE 1 (expand)  add, never remove
ALTER TABLE orders ADD COLUMN customer_notes text;
-- app v1.1 writes BOTH columns, reads the OLD one.
-- v1.0 (writes notes only) and v1.1 coexist safely, because v1.1 reads notes.

-- RELEASE 2 (migrate)  backfill in batches, then switch reads
UPDATE orders SET customer_notes = notes
WHERE  id IN (SELECT id FROM orders WHERE customer_notes IS NULL LIMIT 5000);
-- repeat until zero rows. app v1.2 writes both, reads the NEW one.
-- v1.1 and v1.2 coexist safely, because both columns are kept in sync.

-- RELEASE 3 (contract)  stop writing the old column, then drop it
-- app v1.3 uses customer_notes only. After the rollout completes:
ALTER TABLE orders DROP COLUMN notes;
```

``` text
  the invariant that makes it safe

    at every instant, for any two app versions that can coexist:
      every column a running version READS is written by every running version
      every column a running version WRITES exists in the schema

  combining all three releases into one violates this during the rollout window -
  which is precisely the 90 seconds when both versions are live.
```

| Change                               | Safe in one release?                                   |
|--------------------------------------|--------------------------------------------------------|
| Add a nullable column                | Yes                                                    |
| Add `NOT NULL` with a default        | Usually — check the lock behaviour on large tables     |
| Drop a column                        | **No** — old code still selects it                     |
| Rename anything                      | **No** — it is a drop plus an add                      |
| Widen a column                       | Yes                                                    |
| Narrow a column, or add a constraint | **No** — old code can write violating data mid-rollout |
| Add an index                         | Yes, with `CONCURRENTLY` / `ONLINE`                    |

### Modeling patterns that recur

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Modeling patterns that recur</p>

<a id="be-m211"></a>

#### Soft delete: costs and alternatives

Marking rows deleted instead of removing them is the most common modeling decision made without thinking, and it has a long tail of consequences.

``` csharp
modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
// Every LINQ query now gets "AND IsDeleted = 0" appended automatically.
```

| Consequence               | Detail                                                                                           |
|---------------------------|--------------------------------------------------------------------------------------------------|
| Unique constraints break  | a deleted row still occupies the unique value — needs a *filtered* unique index                  |
| Foreign keys lie          | the database will happily reference a soft-deleted parent                                        |
| Every query must remember | raw SQL, reports, and other applications bypass the filter entirely                              |
| Indexes grow              | deleted rows still occupy pages and still get scanned                                            |
| Aggregates silently wrong | any `COUNT` or `SUM` that forgets the predicate                                                  |
| Filters do not compose    | EF applies a filter per entity; an `Include` of a filtered child needs `IgnoreQueryFilters` care |

``` sql
-- The filtered unique index that makes soft delete workable on SQL Server:
CREATE UNIQUE INDEX UX_Customers_Email
  ON Customers (TenantId, Email) WHERE IsDeleted = 0;

-- And index the flag into your hot indexes, or every query pays for a lookup:
CREATE INDEX IX_Orders_Tenant_Created
  ON Orders (TenantId, CreatedAt DESC) WHERE IsDeleted = 0;   -- filtered index
```

**Ask what you actually need**, because "soft delete" usually conflates three different requirements:

- *Undo* — a short window to restore a mistake. A `DeletedAt` timestamp plus a purge job after 30 days.
- *Audit* — proof of what existed. That is history, not deletion; use temporal tables or an audit trail ([History: temporal tables and bitemporality](#be-m213)).
- *Referential safety* — other rows point at it. That is what foreign keys and a status column are for; the row is not deleted, it is *inactive*, which is a domain state with a domain name.

> **\[Tip\]**
>
> **The cleanest alternative is an archive table.** Move the row out on delete, inside the same transaction. Live tables stay small and fast with no filters anywhere; recovery is a documented insert; and nothing silently miscounts. It costs one extra table per entity that needs it — and only the entities that need it, rather than a flag on everything by convention.

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

<a id="be-m212"></a>

#### Multi-tenancy: three strategies

|                        | Shared schema + `TenantId` | Schema per tenant | Database per tenant |
|------------------------|----------------------------|-------------------|---------------------|
| Isolation              | logical only               | moderate          | strong              |
| Cost per tenant        | near zero                  | low               | a database each     |
| Migrations             | one                        | N schemas         | N databases         |
| Noisy neighbour        | shared everything          | shared server     | isolatable          |
| Per-tenant restore     | hard                       | awkward           | trivial             |
| Cross-tenant analytics | one query                  | union of N        | a pipeline          |
| Scales to              | thousands                  | hundreds          | tens to hundreds    |

``` csharp
// Shared schema: TenantId must lead every index, not trail it.
CREATE INDEX IX_Orders_Tenant_Created ON Orders (TenantId, CreatedAt DESC);
// A tenant filter that is not the index's first column cannot seek to that
// tenant's rows (§171).

modelBuilder.Entity<Order>().HasQueryFilter(o => o.TenantId == _tenant.Current);
```

``` sql
-- Defence in depth: row-level security enforces it in the engine, so raw SQL,
-- a reporting tool, and a forgotten query filter all stay scoped.
CREATE FUNCTION dbo.fn_TenantPredicate(@TenantId int) RETURNS TABLE
WITH SCHEMABINDING AS
  RETURN SELECT 1 AS ok WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS int);

CREATE SECURITY POLICY dbo.TenantFilter
  ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Orders,
  ADD BLOCK PREDICATE  dbo.fn_TenantPredicate(TenantId) ON dbo.Orders AFTER INSERT
  WITH (STATE = ON);
```

> **\[Trap\]**
>
> **A global query filter is not a security boundary.** It is applied by EF, so it is absent from raw SQL, from `ExecuteUpdate`, from `IgnoreQueryFilters()`, from anything Dapper runs, and from every other process touching the database. Treat it as ergonomics; put the actual guarantee in row-level security or in an explicit predicate you can see, and assert the tenant on the way out ([Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)).

> **\[Tip\]**
>
> **The usual pragmatic answer** is shared schema with a tenant-leading index everywhere, plus the ability to *move* a large or sensitive tenant into its own database later. Designing for that move — no cross-tenant foreign keys, tenant id on every table, connection string resolved per request — costs little up front and preserves the option ([Read replicas and read/write splitting](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m264)).

Worked example: [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304)

<a id="be-m213"></a>

#### History: temporal tables and bitemporality

SQL Server's system-versioned temporal tables record every version of every row with no application code, and they are dramatically cheaper than a hand-written audit trail ([Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)).

``` sql
CREATE TABLE Orders (
  Id bigint PRIMARY KEY,
  Total decimal(18,4) NOT NULL,
  ValidFrom datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
  ValidTo   datetime2 GENERATED ALWAYS AS ROW END   HIDDEN NOT NULL,
  PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.OrdersHistory));

SELECT * FROM Orders FOR SYSTEM_TIME AS OF '2026-03-01T10:00:00';
SELECT * FROM Orders FOR SYSTEM_TIME BETWEEN @a AND @b WHERE Id = @id;
```

``` csharp
// EF Core maps this directly:
modelBuilder.Entity<Order>().ToTable("Orders", t => t.IsTemporal());
var asOf = await db.Orders.TemporalAsOf(when).SingleAsync(o => o.Id == id, ct);
var all  = await db.Orders.TemporalAll().Where(o => o.Id == id).ToListAsync(ct);
```

**Bitemporal** modelling separates two independent timelines, and the distinction matters more often than people expect:

| Timeline                           | Answers                                   | Can be corrected?              |
|------------------------------------|-------------------------------------------|--------------------------------|
| **Transaction time** (system time) | "what did the database say on that date?" | no — append-only by definition |
| **Valid time** (business time)     | "what was true in the real world then?"   | yes — backdated corrections    |

``` sql
-- A price that took effect on 1 March but was entered on 15 March:
CREATE TABLE PriceHistory (
  Sku varchar(50) NOT NULL,
  Price decimal(18,4) NOT NULL,
  ValidFrom date NOT NULL, ValidTo date NULL,     -- business time, half-open
  RecordedAt datetime2 NOT NULL                   -- transaction time
);
-- "What price did we CHARGE on 5 March?"      -> valid time
-- "What price did we BELIEVE on 5 March?"     -> transaction time
-- Those differ, and an audit will ask for both.
```

> **\[Trap\]**
>
> **History tables grow without bound.** A temporal table on a hot row updated every minute produces half a million history rows a year per row. Partition the history table by time and switch out old partitions, or set a `HISTORY_RETENTION_PERIOD`. Also note the history table is written on every update — including updates that change nothing, so avoid no-op writes ([Bulk operations and batching](07-07-orms-and-ef-core.md#be-m166)).

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

<a id="be-m214"></a>

#### Append-only and event sourcing

Instead of storing current state and overwriting it, store the ordered facts that produced it and derive state by folding over them.

``` csharp
public abstract record OrderEvent(long OrderId, DateTimeOffset At);
public sealed record OrderPlaced(long OrderId, DateTimeOffset At, int CustomerId) : OrderEvent(OrderId, At);
public sealed record LineAdded(long OrderId, DateTimeOffset At, string Sku, int Qty, decimal Price) : OrderEvent(OrderId, At);
public sealed record OrderShipped(long OrderId, DateTimeOffset At) : OrderEvent(OrderId, At);

// State is a left fold - the same shape as Aggregate (§147).
static OrderState Replay(IEnumerable<OrderEvent> events) =>
    events.Aggregate(OrderState.Empty, (state, e) => e switch {
        OrderPlaced p  => state with { CustomerId = p.CustomerId, Status = "Open" },
        LineAdded l    => state with { Lines = state.Lines.Add(new(l.Sku, l.Qty, l.Price)) },
        OrderShipped   => state with { Status = "Shipped" },
        _              => state
    });
```

``` sql
-- The store: one table, append-only, with a per-stream version for concurrency.
CREATE TABLE Events (
  Position   bigint IDENTITY PRIMARY KEY,      -- global order
  StreamId   varchar(100) NOT NULL,
  Version    int NOT NULL,                     -- per-stream order
  Type       varchar(100) NOT NULL,
  Data       nvarchar(max) NOT NULL,
  RecordedAt datetime2 NOT NULL,
  CONSTRAINT UX_Stream_Version UNIQUE (StreamId, Version)   -- optimistic concurrency
);
-- An append with the expected version fails on a duplicate key if someone
-- else wrote first. That single unique constraint IS the concurrency control.
```

**Snapshots** bound replay cost: persist the folded state every N events and replay only the tail. **Projections** build read models — ordinary tables, shaped for querying — by subscribing to the stream ([Consistency across stores](07-06-non-relational-stores.md#be-m210)).

> **\[Why it matters\]**
>
> **What it buys, and what it costs.** You get a complete, tamper-evident history, temporal queries for free, the ability to build a new read model retroactively over past events, and natural integration with messaging. You pay with eventual consistency between the stream and every projection, event schema versioning that lasts forever ([Versioning data contracts](#be-m223)), no ad-hoc querying of the stream itself, and a much steeper learning curve for everyone who joins.

> **\[Trap\]**
>
> **Do not event-source the whole system.** The pattern earns its cost where history *is* the domain — ledgers, order lifecycles, inventory movement, approval workflows, anything audited. For a settings table or a product catalogue it is pure overhead. Mixing both in one system is normal and correct; a codebase where everything is an event stream usually indicates the pattern was adopted for its own sake.

<a id="be-m215"></a>

#### Analytical modeling: grain, facts, dimensions

Normalised OLTP schemas are optimised for correct writes; analytical queries want wide scans and aggregation. A star schema is the standard answer, and it starts with one sentence.

**Declare the grain first:** "one row per order line per day". Everything else follows — every measure must be true at that grain, and every dimension must apply to it. Most broken warehouses are broken because the grain was never stated.

``` sql
-- Fact: numeric measures + foreign keys to dimensions. Long and narrow.
CREATE TABLE FactSales (
  DateKey     int    NOT NULL,     -- 20260315
  ProductKey  int    NOT NULL,
  CustomerKey int    NOT NULL,
  Quantity    int    NOT NULL,     -- additive
  NetAmount   decimal(18,4) NOT NULL,   -- additive
  UnitPrice   decimal(18,4) NOT NULL    -- NON-additive: never SUM this
);
CREATE CLUSTERED COLUMNSTORE INDEX CCI_FactSales ON FactSales;

-- Dimension: descriptive attributes, denormalised on purpose. Short and wide.
CREATE TABLE DimProduct (
  ProductKey int PRIMARY KEY,      -- surrogate
  Sku varchar(50), Name nvarchar(200), Category nvarchar(100), Brand nvarchar(100),
  ValidFrom date, ValidTo date, IsCurrent bit    -- slowly changing dimension, type 2
);
```

| Measure type  | Can you SUM it?        | Example                       |
|---------------|------------------------|-------------------------------|
| Additive      | across every dimension | quantity, revenue             |
| Semi-additive | across some, not time  | stock level, account balance  |
| Non-additive  | never                  | unit price, ratio, percentage |

> **\[Tip\]**
>
> **Slowly changing dimensions, type 2,** are how a warehouse answers "what was this product's category *at the time of the sale*". Instead of updating the row, close the old version (`ValidTo`) and insert a new one with a new surrogate key; the fact row keeps pointing at the version that was current when it happened. This is the same "frozen at a point in time" reasoning as storing the unit price at sale ([Normalization and deliberate denormalization](07-02-the-relational-model-and-sql.md#be-m189)), applied to descriptive attributes.

> **\[Trap\]**
>
> **Do not run analytics on the OLTP database.** A columnstore scan over a fact table competes for exactly the buffer pool and CPU your transactional workload needs, and analytical queries hold their reads for a long time. Separate physically — a replica, a warehouse, or at minimum a snapshot-isolated read path ([Read replicas and read/write splitting](07-08-running-databases-operations-scaling-and-the-modern-landscap.md#be-m264)). Almost every "the database gets slow every morning" report has a report behind it ([Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)).

Worked example: [Case: the dashboard that melts the database at 9am](14-03-worked-cases-from-production.md#be-m297)

### Evolving, testing, securing data

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Evolving, testing, securing data</p>

<a id="be-m220"></a>

#### Schema evolution: expand and contract

The moment you have more than one instance, a deploy runs old and new code against the same schema simultaneously. Any migration that is not compatible with *both* versions causes errors during the rollout window — and blocks rollback entirely.

The discipline is **expand / migrate / contract**, spread across three deploys:

| Phase           | Schema                                              | Code                                   |
|-----------------|-----------------------------------------------------|----------------------------------------|
| **1. Expand**   | add the new nullable column / new table / new index | unchanged, or writes both              |
| **2. Migrate**  | backfill the new shape                              | writes both, reads new with a fallback |
| **3. Contract** | drop the old column, add `NOT NULL`                 | reads and writes new only              |

``` sql
-- Renaming Orders.Total to Orders.GrossTotal, safely:

-- Deploy 1: expand. Additive only, old code is unaffected.
ALTER TABLE Orders ADD GrossTotal decimal(18,4) NULL;

-- Deploy 2: dual-write in application code, then backfill in batches.
UPDATE TOP (5000) Orders SET GrossTotal = Total
WHERE GrossTotal IS NULL;                 -- run in a loop until 0 rows

-- Deploy 3: contract, only once no running instance reads Total.
ALTER TABLE Orders ALTER COLUMN GrossTotal decimal(18,4) NOT NULL;
ALTER TABLE Orders DROP COLUMN Total;
```

> **\[Trap\]**
>
> **Operations that lock a large table** and will take an outage if run naively: adding a `NOT NULL` column with a default on older SQL Server versions, changing a column's type or width, adding a foreign key with `CHECK`, and building a non-online index. Check your engine's rules for the specific operation — SQL Server Enterprise supports `WITH (ONLINE = ON)` for index builds, and PostgreSQL has `CREATE INDEX CONCURRENTLY`. On a small table none of this matters; on a 50-million-row table all of it does.

> **\[Tip\]**
>
> **Every expand step is independently revertible.** That is the real payoff, not elegance. If deploy 2 misbehaves you roll code back to deploy 1 and the schema is still valid. A single "rename the column" migration gives you no such option once one write has landed.

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

Goes further: [The migration path, in order](12-01-monoliths-modules-and-services.md#be-m252)

Applied in: [Deployment strategies and rollback](11-04-ci-cd-pipelines.md#be-a220)

<a id="be-m221"></a>

#### Backfills without downtime

A one-shot `UPDATE` over ten million rows takes a table-level lock, grows the transaction log by the size of the change, blocks every writer, and cannot be interrupted without rolling all of it back. Batch it instead.

``` sql
-- Batched, restartable, and bounded in lock count.
DECLARE @batch int = 5000, @rows int = 1;
WHILE @rows > 0
BEGIN
    UPDATE TOP (@batch) Orders WITH (ROWLOCK)
    SET GrossTotal = Total
    WHERE GrossTotal IS NULL;             -- the predicate makes it resumable

    SET @rows = @@ROWCOUNT;
    WAITFOR DELAY '00:00:00.200';         -- let other work through
END
```

``` csharp
// The same in C#, with progress, cancellation, and a fresh context per batch.
public async Task BackfillAsync(CancellationToken ct) {
    long lastId = 0; int total = 0;
    while (!ct.IsCancellationRequested) {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Keyset-driven, so cost per batch is constant however far in we are.
        var ids = await db.Orders.AsNoTracking()
            .Where(o => o.Id > lastId && o.GrossTotal == null)
            .OrderBy(o => o.Id).Take(5000)
            .Select(o => o.Id).ToListAsync(ct);
        if (ids.Count == 0) break;

        await db.Orders.Where(o => ids.Contains(o.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.GrossTotal, o => o.Total), ct);

        lastId = ids[^1];
        total += ids.Count;
        _logger.LogInformation("Backfilled {Total} rows, up to id {Id}", total, lastId);
        await Task.Delay(200, ct);
    }
}
```

**Four properties every backfill needs:** it must be *restartable* (the predicate identifies remaining work), *idempotent* (running it twice is harmless), *observable* (progress is logged, so you know whether it will finish this century), and *throttled* (a delay between batches so production traffic still gets through).

> **\[Trap\]**
>
> **Keyset, not offset.** A backfill driven by `Skip(n).Take(batch)` degrades quadratically — each batch scans past everything already done ([Pagination: OFFSET vs keyset](07-07-orms-and-ef-core.md#be-m161)). Worse, as rows stop matching the filter the offsets shift and you silently skip rows. Drive from the last processed key, always.

> **\[Tip\]**
>
> **Batch size is a lock-escalation decision.** SQL Server escalates row locks to a table lock past roughly 5,000 locks on one object, so batches under that threshold keep other writers alive ([Locking, blocking, and deadlocks](07-04-transactions-and-isolation.md#be-m192)). Start at 1,000–5,000, watch blocking, adjust.

Worked example: [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)

<a id="be-m222"></a>

#### EF migrations in practice

``` bash
dotnet ef migrations add AddGrossTotal
dotnet ef migrations script 20260101_Prev 20260201_AddGrossTotal --idempotent -o up.sql
dotnet ef migrations remove          # only before it has been applied anywhere
```

> **\[Trap\]**
>
> **Do not call `Database.Migrate()` at application startup in production.** With multiple instances, several race to apply the same migration; the losers fail or, worse, partially apply. It also means your application's database user needs DDL rights permanently. Generate an idempotent script and run it as a distinct deployment step, once, with a privileged account.

**Always read the generated migration before committing it.** EF infers intent from a model diff and gets renames wrong by default:

``` csharp
// EF sees a dropped column and a new column. It generates:
migrationBuilder.DropColumn("Total", "Orders");        // DATA LOSS
migrationBuilder.AddColumn<decimal>("GrossTotal", "Orders");

// What you want:
migrationBuilder.RenameColumn(name: "Total", table: "Orders", newName: "GrossTotal");
```

``` sql
// Data migrations belong in the migration, not in a script someone forgets to run:
protected override void Up(MigrationBuilder migrationBuilder) {
    migrationBuilder.AddColumn<string>("CountryCode", "Customers", maxLength: 2, nullable: true);
    migrationBuilder.Sql(@"
        UPDATE Customers SET CountryCode = 'VN' WHERE Country = 'Vietnam';
        UPDATE Customers SET CountryCode = 'SG' WHERE Country = 'Singapore';");
}
```

> **\[Tip\]**
>
> **Two checks worth automating in CI.** First, assert that no model changes are pending — `dotnet ef migrations has-pending-model-changes` catches the "someone edited the entity and forgot the migration" case. Second, apply every migration to a throwaway database from empty and assert it succeeds; that catches migrations that only work against *your* local database's accumulated state.

<a id="be-m223"></a>

#### Versioning data contracts

Anything serialized outlives the code that wrote it: cache entries, queue messages, event stores, JSON columns, API payloads. During a rolling deploy, old and new versions read each other's data. Treat every serialized shape as a public contract.

| Change                        | Safe?   | Why                                      |
|-------------------------------|---------|------------------------------------------|
| add an optional field         | yes     | old readers ignore it                    |
| add a required field          | no      | old writers omit it                      |
| remove a field                | no      | old readers may require it               |
| rename a field                | no      | a remove plus an add                     |
| widen a type (`int` → `long`) | careful | old readers overflow on large values     |
| narrow a type                 | no      | silent truncation or a parse failure     |
| add an enum member            | careful | old readers must tolerate unknown values |
| change the meaning of a field | never   | undetectable; add a new field instead    |

``` csharp
// Deprecate rather than remove: keep the old field readable for one release cycle.
public sealed record OrderSnapshotV2 {
    public required long Id { get; init; }
    public required decimal GrossTotal { get; init; }

    [Obsolete("Use GrossTotal. Removed after 2026-09."), JsonPropertyName("total")]
    public decimal? Total { get => null; init => _legacyTotal = value; }
    private decimal? _legacyTotal;

    // Anything the writer sent that we do not model - round-trip it untouched,
    // so a v1 reader/writer sandwich does not silently drop v2 fields.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

``` csharp
// Version the envelope, not just the payload. Then a reader can dispatch.
public sealed record Envelope(string Type, int Version, JsonElement Payload);

var order = envelope switch {
    { Type: "order", Version: 1 } => Upgrade(Deserialize<OrderV1>(envelope.Payload)),
    { Type: "order", Version: 2 } => Deserialize<OrderV2>(envelope.Payload),
    _ => throw new NotSupportedException($"{envelope.Type} v{envelope.Version}")
};
```

> **\[Tip\]**
>
> **For caches, do not version the payload at all — version the key** ([Cache key design and value format](07-06-non-relational-stores.md#be-m207)). Bumping `v3` to `v4` makes every old entry unreachable, so the format change needs no compatibility code and no migration. That is only available because cache entries are disposable; a queue or an event store has no such luxury, which is why those need real versioning.

Goes deeper: [Schema registries and compatibility](08-08-serialization-and-data-formats.md#be-m94)

Goes further: [Versioning an API](08-02-api-design.md#be-m241)
