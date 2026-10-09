---
id: "7-7"
number: "7.7"
title: "ORMs and EF Core"
part: 7
part_title: "Storing data"
words: 20578
minutes: 89
---

# 7.7 ORMs and EF Core

> **Quick look.** ORMs and EF Core: huge time savers and the most common source of .NET database slowness.
>
> - DbContext is a short-lived unit of work with a change tracker; use one per request, never share it across threads.
> - LINQ on a DbSet becomes SQL when enumerated; untranslatable expressions throw.
> - Project to DTOs with Select, beware N+1 loops and cartesian explosions from multiple Includes (use split queries).
> - AsNoTracking for reads; ExecuteUpdate and ExecuteDelete for bulk changes, but they bypass interceptors.
> - Interceptors stamp audit columns; global query filters implement soft delete (not applied to raw SQL).
> - FromSql with interpolation is safe; FromSqlRaw with interpolation is SQL injection.
> - Configure decimal precision and string lengths: the defaults truncate money and create nvarchar(max).
> - Use Dapper alongside EF for hot reads when you want to write the SQL yourself.


### The big picture

C# code works with objects: a flight with a list of bookings and a reference to its origin airport. Relational databases store tables of rows linked by keys. Moving data between the two by hand means writing SQL, reading columns out of result rows, and working out which rows changed and need updating. An ORM, an object-relational mapper, automates that. EF Core, Entity Framework Core, is the one most .NET applications use.

An ORM saves an enormous amount of code, and it is also the most common source of database performance problems in .NET. It makes the database feel like an in-memory collection, which it isn't: every query is still SQL on a remote server. This chapter explains what EF Core does underneath and how LINQ becomes SQL. It covers the patterns that go wrong, such as the N+1 query problem and loading far more than a screen needs, and the habits that keep EF Core fast and correct. The example prints every SQL command EF Core sends, so you can see exactly what your code causes.

### Before you start

**Builds on:** 2.4 Values, variables and references, 7.1 Why databases exist, 2.6 Scope and lifetime, 7.3 Indexes and query plans, 2.3 Types, 4.4 Immutability and pure functions.

**Ideas explained later:**

- **Performance** (12.4): How quickly a system does its work at a given load.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **API** (8.2): Application programming interface: the operations software offers to other software, and the rules for using them.
- **JSON** (8.2): JavaScript Object Notation: a text format of objects, arrays, strings, numbers, true, false and null.
- **Client** (8.1): A program that sends requests to a server.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Backend** (8.1): The part of a system that runs on the organization's servers, holding data and enforcing rules.

**Tools and technologies named here:**

**The code uses:** variables and var, methods and local functions, string interpolation, classes and properties, primary constructors, records, collection expressions, generic types, lambdas, LINQ, tuples, exceptions, using for cleanup, async and await, package directives. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the N+1 query problem?**

> Loading a list with one query, then running one more query for each item to fetch related data. 100 flights become 101 round trips. Fix it by fetching the related data in the same query, with a projection or `Include`.

**2. What is the difference between filtering an IQueryable and an IEnumerable?**

> A `Where` on `IQueryable` becomes part of the SQL, so the database filters. After `AsEnumerable()` or `ToList()`, a `Where` runs in memory on rows that were all downloaded first.

**3. What does AsNoTracking do, and when should you use it?**

> It tells EF Core not to record the loaded entities in its change tracker. Queries are faster and use less memory. Use it for read-only queries, where nothing will be saved.

**4. What is DbContext's intended lifetime, and why?**

> Short: one per unit of work, typically one per web request, registered as scoped. It isn't thread-safe, and its change tracker grows with everything it loads.

**5. What does SaveChanges do with several changes?**

> It works out every insert, update and delete from the change tracker, sends them in batches and wraps them all in one transaction. All succeed or none do.

**6. Why can Include with two collection navigations be slow?**

> One SQL query with two one-to-many joins repeats each row of one collection for each row of the other, a cartesian explosion. `AsSplitQuery()` loads each collection with its own query instead.

**7. How do you run a SQL query through EF Core without risking SQL injection?**

> Use `FromSql` or `ExecuteSql` with an interpolated string: EF Core turns each interpolated value into a parameter. Never build SQL with string concatenation and pass it to `FromSqlRaw`.

**8. How do you change many rows without loading them?**

> `ExecuteUpdateAsync` and `ExecuteDeleteAsync`, added in EF Core 7, translate straight to a single `UPDATE` or `DELETE` statement, bypassing the change tracker.

### Two different shapes

Objects and tables don't fit together neatly, a problem known as the **object-relational impedance mismatch**. Objects refer to each other through references; tables through foreign key values. An object can hold a list of other objects; a table can't hold a list, so the list becomes rows in another table with a foreign key back. Object identity is a reference, `ReferenceEquals` from Values, variables and references (2.4); row identity is a primary key. Objects support inheritance; tables don't. And objects live in memory, while rows live in a database on another machine, one round trip away.

### What an ORM does

An **ORM**, object-relational mapper, bridges the gap. It maps classes to tables and properties to columns. It generates SQL from queries written in code. It turns result rows back into objects, including their relationships. It notices which objects changed and writes those changes back. And it manages keys, transactions and schema migrations.

.NET has three levels to choose from. **EF Core** is Microsoft's full ORM, a rewrite of the older Entity Framework first released in 2016. The old Entity Framework 6 is still used by .NET Framework applications. A **micro-ORM**, such as Dapper, does much less: you write the SQL, and it maps result rows to objects quickly. Below both is plain ADO.NET with connections, commands and readers, as Why databases exist (7.1) showed. Many applications use EF Core for most work and Dapper or raw SQL for a few hot or complicated queries.

### DbContext

In EF Core, a **DbContext** is the gateway to the database. You derive a class from it, expose a `DbSet<T>` for each kind of entity, and describe the model, as in the example. A DbContext also plays three further roles.

It is a **unit of work**: it collects every change you make to its entities and writes them all together when you call `SaveChanges`.

It holds a **change tracker**, which records every entity loaded or added through it, along with the original values, so it can work out what changed.

It keeps an identity map: within one context, loading the same row twice gives you the same object, not two copies.

That makes a DbContext stateful, and not safe to share between threads. It is meant to be short-lived: create one for a unit of work, use it and dispose it. In ASP.NET Core it is registered as a scoped service, one per request, as Scope and lifetime (2.6) explained. `AddDbContextPool` reuses context instances between requests to save the small cost of creating them.

### Modelling

EF Core builds its model from your classes by conventions. A property named `Id`, or `FlightId` on `Flight`, becomes the primary key. A property that refers to another entity is a **navigation property**, such as `Flight.Origin` or `Flight.Bookings`. A property named after a navigation plus its key, such as `OriginCode` for `Origin`, becomes the foreign key. Where conventions aren't enough, you configure the model in `OnModelCreating` with the fluent API, as the example does for a flight's two airport references, or with attributes on the classes.

The model also declares indexes with `HasIndex`, as Indexes and query plans (7.3) recommended, as well as column types and lengths, and whether strings are Unicode. That last setting matters on SQL Server: a `string` property maps to `nvarchar` by default. If the real column is `varchar`, configure `IsUnicode(false)`, or parameters are sent as `nvarchar` and index seeks can turn into scans, as that chapter warned.

Other mapping features cover the rest of the mismatch. Value converters handle types such as enums and the strongly typed ids from Types (2.3). Complex types and owned types hold value objects, from Immutability and pure functions (4.4). There are also JSON columns, and several strategies for inheritance.

### Queries: LINQ to SQL

A LINQ query on a `DbSet` doesn't run in memory. `DbSet<T>` implements `IQueryable<T>`, so the lambdas you pass to `Where`, `Select` and `OrderBy` become expression trees, the data structures from Trees (3.4), not compiled code. When the query is enumerated, with `ToListAsync`, `FirstAsync` or `foreach`, EF Core walks the tree, translates it into SQL, sends it, and turns the rows into objects. As with other LINQ queries, building it does nothing; enumerating it runs it, and enumerating it again runs it again, as Control flow and functions (2.5) described.

Only what EF Core can translate can run in the database. Comparisons, arithmetic, many string and date methods, `Contains` on a list of values and navigation properties all translate. A call to your own C# method doesn't: EF Core can't turn arbitrary code into SQL. Since EF Core 3.0, an untranslatable expression in a query throws an exception instead of silently loading the whole table and filtering in memory, as early versions did. That silent version is called **client evaluation**. You can still ask for it deliberately, by calling `AsEnumerable()`, after which every operator runs in memory on rows already downloaded. Put every filter before that point.

To see what SQL a query produces, call `ToQueryString()` on it, or turn on logging with `LogTo`, as the example does. Reading the SQL is the first step with any slow EF Core query.

### Loading related data

A query for flights returns flights. Their navigation properties, such as `Bookings`, stay empty unless you ask for more. There are several ways to ask.

**Eager loading** fetches related data in the same query, with `Include`: `db.Flights.Include(f => f.Bookings)`. `ThenInclude` goes a level deeper. It is simple and clear.

A **projection** uses `Select` to shape exactly the result you need: `db.Flights.Select(f => new { f.Number, Booked = f.Bookings.Count })`. EF Core translates the whole thing, including the count, into one SQL query that returns only those columns. For read-only screens and APIs, projections are usually the best choice: they fetch only what is shown and involve no change tracking.

**Lazy loading** fetches a navigation property automatically the first time code touches it. It requires opting in, with proxy classes or an injected loader, and it makes the next problem very easy to create by accident.

### The N+1 query problem

The **N+1 query problem** is the most common ORM performance bug. Code loads a list with one query, then, in a loop, triggers one more query per item: to count each flight's bookings, or to touch each booking's passenger with lazy loading on. 100 flights mean 101 round trips. At half a millisecond each, that is 50 ms of pure waiting, and much more with a remote database or thousands of items. It hides well: the code looks like an ordinary loop, and it is fast in tests with five rows.

The fix is to fetch everything in one query: a projection that computes what the loop needed, or an `Include`. The example shows both the problem and the fix, with the SQL for each.

`Include` has its own trap. Including two collections, such as a flight's bookings and its crew, in one query joins both, so every booking row is repeated for every crew member, as The relational model and SQL (7.2) warned. This is called a **cartesian explosion**, and it can turn a few hundred rows into hundreds of thousands. A **split query**, `AsSplitQuery()`, loads each collection with its own SQL query instead.

### Tracking and no-tracking queries

By default, every entity a query returns is added to the change tracker, with a snapshot of its original values. That is what lets `SaveChanges` find changes, and it costs time and memory for every row.

A **no-tracking query**, `AsNoTracking()`, skips all of that. The objects are plain results, and changing them does nothing. Use it for every query whose results won't be saved: lists, reports and API responses. Projections into non-entity types are never tracked anyway.

### Saving changes

To save, change the objects and call `SaveChangesAsync`. `Add` marks a new entity for insertion. Changing a property on a tracked entity marks that property modified. `Remove` marks an entity for deletion. When you save, EF Core compares tracked entities with their snapshots, generates an `INSERT`, `UPDATE` or `DELETE` for each change, including only changed columns in updates, and sends them in batches inside one transaction. Keys generated by the database are read back into the objects.

For changes to many rows at once, loading them all to change them one by one is wasteful. `ExecuteUpdateAsync` and `ExecuteDeleteAsync`, added in EF Core 7, translate directly into a single `UPDATE` or `DELETE` statement and run it immediately. They bypass the change tracker, so entities already loaded in that context won't reflect the change.

### Concurrency and transactions

EF Core supports the optimistic concurrency from Transactions and isolation (7.4) through a **concurrency token**: a property whose original value is checked in the `WHERE` clause of every update and delete. On SQL Server, mark a `byte[]` property with `[Timestamp]`, or configure `IsRowVersion()`, and the database changes it on every update automatically. If the row changed since it was loaded, the update affects no rows and `SaveChangesAsync` throws `DbUpdateConcurrencyException`. The application then reloads, merges or asks the user.

`SaveChanges` is already one transaction. To group several `SaveChanges` calls, or EF Core work with raw SQL, use `Database.BeginTransactionAsync()`. For cloud databases, where brief connection failures are normal, an **execution strategy** retries whole operations automatically: `EnableRetryOnFailure()` in the SQL Server provider. Work that uses explicit transactions must then be wrapped in the strategy so the whole transaction is retried together.

### Raw SQL

Sometimes SQL is simply the better tool: a complex report, a query that uses database-specific features, or a hot path. EF Core lets you run SQL and still get entities back. `FromSql` takes an interpolated string, `db.Flights.FromSql($"SELECT * FROM Flights WHERE Number = {input}")`, and turns every interpolated value into a real parameter, so it is safe from SQL injection. `ExecuteSql` runs statements that return no rows. `Database.SqlQuery<T>` maps results to types that aren't entities.

`FromSqlRaw` and `ExecuteSqlRaw` take a plain string. They are for SQL assembled from trusted pieces, such as a table name chosen from a fixed list. Passing them a string built with user input brings back the injection hole from The relational model and SQL (7.2).

### Seeing it for real

This program uses EF Core with SQLite and prints every SQL command EF Core sends, so you can see the translation, the N+1 problem and its fix, change tracking, a concurrency conflict and a bulk update.

```csharp
#:package Microsoft.EntityFrameworkCore.Sqlite@10.0.0
// efcore.cs — run with: dotnet run efcore.cs
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

File.Delete("ef.db");
await using (var setup = new AirlineContext(logSql: false))
{
    await setup.Database.EnsureCreatedAsync();                   // for demos only; real apps use migrations
    var lhr = new Airport { Code = "LHR", City = "London" };
    var jfk = new Airport { Code = "JFK", City = "New York" };
    var sin = new Airport { Code = "SIN", City = "Singapore" };
    setup.Flights.AddRange(
        new Flight { Number = "BA117", Origin = lhr, Destination = jfk, Bookings = [new() { Passenger = "Ada", Fare = 450 }, new() { Passenger = "Grace", Fare = 520 }] },
        new Flight { Number = "BA11", Origin = lhr, Destination = sin, Bookings = [new() { Passenger = "Linus", Fare = 900 }] },
        new Flight { Number = "SQ321", Origin = sin, Destination = lhr });
    await setup.SaveChangesAsync();                              // every INSERT, in one transaction
}

await using var db = new AirlineContext(logSql: true);          // from here on, each SQL command is printed

Section("1. LINQ becomes SQL");
var fromLondon = await db.Flights.Where(f => f.OriginCode == "LHR").OrderBy(f => f.Number).Select(f => f.Number).ToListAsync();
Console.WriteLine(string.Join(", ", fromLondon));                // BA11, BA117

Section("2. The N+1 problem: one query for the flights, then one more per flight");
foreach (var f in await db.Flights.AsNoTracking().ToListAsync())
    Console.WriteLine($"{f.Number}: {await db.Bookings.CountAsync(b => b.FlightId == f.Id)} bookings");   // 4 queries in all

Section("   ...and the fix: one query with a projection");
foreach (var row in await db.Flights.Select(f => new { f.Number, Booked = f.Bookings.Count }).ToListAsync())
    Console.WriteLine($"{row.Number}: {row.Booked} bookings");  // BA117: 2, BA11: 1, SQ321: 0, from 1 query

Section("3. Change tracking: only the changed columns are updated");
var flight = await db.Flights.SingleAsync(f => f.Number == "BA117");     // tracked, with a snapshot of its values
await using var other = new AirlineContext(logSql: false);                // a second user loads the same row
var stale = await other.Flights.SingleAsync(f => f.Id == flight.Id);
flight.Number = "BA117A";
flight.Version++;
await db.SaveChangesAsync();   // UPDATE "Flights" SET "Number" = @p0, "Version" = @p1 WHERE "Id" = @p2 AND "Version" = @p3

Section("4. The second user's save conflicts");
stale.Number = "BA117B";
stale.Version++;
try { await other.SaveChangesAsync(); }
catch (DbUpdateConcurrencyException) { Console.WriteLine("conflict: the flight changed since it was loaded"); }

Section("5. A bulk update without loading anything");
int raised = await db.Bookings.Where(b => b.Fare < 500).ExecuteUpdateAsync(s => s.SetProperty(b => b.Fare, b => b.Fare + 25));
Console.WriteLine($"{raised} booking(s) repriced");              // 1: a single UPDATE statement

Section("6. Raw SQL, still parameterized");
string typed = "SQ321";
var found = await db.Flights.FromSql($"SELECT * FROM Flights WHERE Number = {typed}").AsNoTracking().ToListAsync();
Console.WriteLine($"found {found.Count}");                       // found 1; the SQL shows a parameter, not the text

Section("7. What can't be translated");
try { await db.Flights.Where(f => IsLongHaul(f.Number)).ToListAsync(); }
catch (InvalidOperationException ex) { Console.WriteLine($"could not be translated: {ex.Message.Contains("could not be translated")}"); }   // True

static bool IsLongHaul(string number) => number.Length > 4;    // ordinary C#: EF Core can't turn it into SQL
static void Section(string title) => Console.WriteLine($"\n== {title}");

sealed class AirlineContext(bool logSql) : DbContext
{
    public DbSet<Airport> Airports => Set<Airport>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=ef.db");
        if (logSql) options.LogTo(Console.WriteLine, [DbLoggerCategory.Database.Command.Name], LogLevel.Information);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Airport>().HasKey(a => a.Code);             // a natural key, configured explicitly
        model.Entity<Flight>().HasOne(f => f.Origin).WithMany().HasForeignKey(f => f.OriginCode).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Flight>().HasOne(f => f.Destination).WithMany().HasForeignKey(f => f.DestinationCode).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Flight>().HasIndex(f => f.Number).IsUnique();
        model.Entity<Flight>().Property(f => f.Version).IsConcurrencyToken();   // checked in every UPDATE's WHERE
    }
}

sealed class Airport
{
    public string Code { get; set; } = "";
    public string City { get; set; } = "";
}

sealed class Flight
{
    public int Id { get; set; }                                  // primary key by convention
    public string Number { get; set; } = "";
    public string OriginCode { get; set; } = "";                 // foreign key
    public Airport Origin { get; set; } = null!;                 // navigation property
    public string DestinationCode { get; set; } = "";
    public Airport Destination { get; set; } = null!;
    public List<Booking> Bookings { get; set; } = [];            // collection navigation: one flight, many bookings
    public int Version { get; set; }
}

sealed class Booking
{
    public int Id { get; set; }
    public int FlightId { get; set; }
    public string Passenger { get; set; } = "";
    public int Fare { get; set; }
}
```

Read the logged SQL beside each section. Notice that the N+1 loop sends four commands and the projection one, that the update touches only `Number` and `Version` and checks the old `Version`, and that the raw SQL query shows a parameter where the input went.

### Micro-ORMs

Dapper, originally written for Stack Overflow, takes the opposite approach to EF Core. You write the SQL yourself, and Dapper runs it and maps the rows to objects, very quickly:

```csharp
// With the Dapper package, on any open ADO.NET connection:
var late = await connection.QueryAsync<FlightRow>(
    "SELECT Number, DelayMinutes FROM Flights WHERE DelayMinutes > @min ORDER BY DelayMinutes DESC",
    new { min = 60 });                                           // the anonymous object's properties become parameters

record FlightRow(string Number, int DelayMinutes);
```

There is no change tracking, no LINQ translation and no model to configure: what you write is exactly what runs. That makes it ideal for reports, hot paths and complex SQL, and it pairs well with EF Core in the same application.

### Migrations with EF Core

EF Core generates the schema migrations from Schema design and migrations (7.5) by comparing your model with a snapshot of the last one. `dotnet ef migrations add AddGateCode` writes a migration class with `Up` and `Down` methods. `dotnet ef database update` applies pending migrations to a database. `dotnet ef migrations script --idempotent` produces a SQL script that can be reviewed and run safely against any version. A migration bundle, made with `dotnet ef migrations bundle`, is a small executable that applies them, suited to deployment pipelines.

Always read the generated migration before applying it. EF Core sometimes chooses to drop and recreate a column where you meant to rename it, and its changes are as blocking as any other `ALTER TABLE`. Split breaking changes into expand-contract steps yourself. Avoid calling `Database.Migrate()` at application start-up when several instances start at once; run migrations as a deployment step instead. `EnsureCreated`, used in the example, creates a schema without migrations at all, and is only for tests and demos.

### Patterns and habits

A few habits keep EF Core fast and predictable.

Project what you need. For reads, `Select` into a small result type rather than loading whole entities and their graphs.

Use `AsNoTracking` for anything you won't save.

Watch the SQL. Log commands in development, look for N+1 patterns and cartesian explosions, and check plans for slow queries as Indexes and query plans (7.3) described.

Page with keyset pagination for long lists, and never return an entire table from an API.

Keep contexts short-lived and scoped. Never share one across threads or requests.

Use **global query filters** for rules that apply to every query of an entity type, such as hiding soft-deleted rows or restricting rows to the current tenant, from Schema design and migrations (7.5). Remember that `IgnoreQueryFilters()` switches them off.

A common question is whether to wrap EF Core in a **repository pattern**: a class per entity type, such as `IFlightRepository`, with methods like `GetById` and `Add`. A DbContext already is a unit of work, and each `DbSet` already is a repository, so a generic repository over EF Core often only hides useful features such as `Include` and projections. Specific query methods for real needs, or a thin data-access layer shaped around your use cases, usually serve better. For tests, run against a real database, as Testing (4.6) recommended, rather than mocking `DbSet`.

### In short

An ORM maps objects to tables, turns queries into SQL, tracks changes and saves them. EF Core's DbContext is a short-lived unit of work with a change tracker; use one per request. LINQ on a `DbSet` is translated to SQL when enumerated, and only translatable expressions can run in the database. Fetch related data in one query with projections or `Include`, beware N+1 loops and cartesian explosions, and use `AsNoTracking` for reads. `SaveChanges` sends only changed columns in one transaction; `ExecuteUpdate` and `ExecuteDelete` change rows in bulk. Use concurrency tokens, parameterized raw SQL, and reviewed migrations run from the pipeline.

### What goes wrong

**N+1 queries.** A page makes hundreds of small queries, one per row in a list. Log the SQL, and replace the loop with a projection or `Include`.

**Filtering in memory.** A `Where` after `ToList()` or `AsEnumerable()` downloads the whole table first. Keep filters on the `IQueryable`.

**Loading entities to display them.** Whole entity graphs are loaded and tracked to show three columns. Project to what the screen needs, with `AsNoTracking` or `Select`.

**Cartesian explosions.** Including two collections multiplies rows. Use `AsSplitQuery()` or separate queries.

**A long-lived or shared DbContext.** A singleton context grows without limit, serves stale data and throws when two requests use it at once. Keep it scoped.

**Unicode mismatches.** `string` properties mapped to `varchar` columns without `IsUnicode(false)` send `nvarchar` parameters, turning seeks into scans. Configure column types to match.

**Raw SQL built from strings.** `FromSqlRaw($"... {input} ...")` with user input is SQL injection. Use `FromSql` or real parameters.

**Unreviewed migrations.** A generated migration drops a column with data in it, or locks a huge table. Read every migration, and script it for review.

**Ignoring concurrency.** Two users edit the same record and the last save silently wins. Add a concurrency token and handle `DbUpdateConcurrencyException`.

### Cost model

The cost of an EF Core query is mostly the cost of its SQL and round trips: a well-indexed query returning a few rows takes about a millisecond including the network. On top of that, EF Core adds translation, which is cached after the first run, and materialization, typically microseconds per row. Change tracking adds memory and time per entity, noticeable at thousands of rows, which `AsNoTracking` removes. Each N+1 round trip costs a full network trip, so the pattern costs far more than anything EF Core does itself. Compiled queries and DbContext pooling shave the remaining overhead for very hot paths.

### In other languages (optional)

**JavaScript and TypeScript.** Prisma generates a typed client from a schema file and has its own migrations. Drizzle and Kysely are closer to SQL, with full type checking. TypeORM follows a design similar to EF Core.

**Python.** SQLAlchemy offers both a SQL toolkit and an ORM with sessions, which play the DbContext's role; Django has its own ORM. The N+1 problem appears in both, with `joinedload` and `select_related` as fixes.

**Java.** Hibernate, under the JPA standard, is the closest equivalent to EF Core, with the same lazy-loading traps.

**Everywhere.** Every ORM makes the database feel local, and every one needs you to watch the SQL it sends.

### Key terms

- **Object-relational impedance mismatch**: The differences between objects in code and rows in tables that an ORM has to bridge.
- **ORM**: Object-relational mapper: a library that maps classes to tables and generates SQL.
- **EF Core**: Entity Framework Core, Microsoft's ORM for .NET.
- **Micro-ORM**: A lightweight library, such as Dapper, that runs your SQL and maps the rows to objects.
- **DbContext**: EF Core's gateway to the database: a unit of work with a change tracker.
- **Unit of work**: An object that collects changes and writes them all together.
- **Change tracker**: The part of a DbContext that records loaded entities and their original values to detect changes.
- **Navigation property**: A property that refers to related entities, such as a flight's bookings.
- **Eager loading**: Loading related data in the same query, with Include.
- **Projection**: Selecting only the needed values into a new shape, with Select.
- **Lazy loading**: Loading related data automatically the first time a navigation property is used.
- **N+1 query problem**: One query for a list, then one more query per item for related data.
- **Cartesian explosion**: Rows multiplying when one query joins several one-to-many collections.
- **Split query**: Loading each included collection with its own SQL query.
- **No-tracking query**: A query whose results aren't recorded by the change tracker.
- **Client evaluation**: Running parts of a query in memory in .NET instead of in the database.
- **Concurrency token**: A property checked in every update and delete, so conflicting changes are detected.
- **Execution strategy**: EF Core's mechanism for retrying operations after transient database failures.
- **Global query filter**: A condition EF Core adds automatically to every query of an entity type.
- **Repository pattern**: Wrapping data access for each entity type behind a class with methods such as GetById.

### Where this leads

This ends Part 7. Part 8 builds backends on top of it, starting with What a backend does (8.1). Using a DbContext inside an ASP.NET Core app is in A modern .NET backend (8.3). Validating data before it reaches the database is in Validation and errors at the boundary (8.5), and caching query results is in Caching (8.6). Finding slow queries in production is part of Observability (11.6).

### Check yourself

1. In `efcore.cs`, rewrite section 2's loop with `Include(f => f.Bookings)` and compare the SQL with the projection's.
2. Call `ToQueryString()` on the projection query and print it, then add `.OrderByDescending(r => r.Booked)` and see how the SQL changes.
3. Change section 4 to catch the conflict, reload the flight with `other.Entry(stale).ReloadAsync()`, apply the change again and save successfully.
4. Add a `Crew` collection to `Flight`, include both crew and bookings, and look at the row count of the SQL result. Then add `AsSplitQuery()`.
5. In an application you know, turn on EF Core command logging for one page or API call and count the SQL commands it sends.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Two Includes that returned 300,000 rows

Loading 100 orders with their lines and payments is slow, and SQL returns 300,000 rows.

**What goes wrong.** Two collection Includes at the same level produce a cartesian product: lines × payments per order.

*Project, or split the query*

```csharp
var orders = await db.Orders
    .Where(o => o.CustomerId == id)
    .Select(o => new OrderDto(
        o.Id, o.Total,
        o.Lines.Select(l => new LineDto(l.Sku, l.Qty)).ToList(),
        o.Payments.Select(p => new PaymentDto(p.Amount, p.Method)).ToList()))
    .AsSplitQuery()                     // one query per collection instead of a cross product
    .ToListAsync(ct);
```

> **Lesson:** Project to what the screen needs, and split queries when loading several collections.

#### Example 2: A soft delete that a bulk update bypassed

Soft delete is implemented in a SaveChanges interceptor. A cleanup job using `ExecuteDelete` hard-deleted rows.

*Do bulk soft deletes explicitly*

```csharp
// ExecuteUpdate/ExecuteDelete skip the change tracker and every SaveChanges interceptor
await db.Bookings
    .Where(b => b.Status == BookingStatus.Expired && b.CreatedAt < cutoff)
    .ExecuteUpdateAsync(s => s
        .SetProperty(b => b.IsDeleted, true)
        .SetProperty(b => b.DeletedAt, DateTime.UtcNow), ct);
```

> **Lesson:** Bulk operations bypass interceptors. Repeat the rule explicitly in bulk code, or keep audited entities off bulk APIs.

### EF Core

<p class="src">From <em>.NET Backend Deep Dive</em> · EF Core</p>

<a id="dn-6"></a>

#### Interceptors: auto-populating audit columns on every table

##### The problem
Every table has `CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted`. If each service sets them by hand, someone will forget.

##### The solution: `SaveChangesInterceptor`

```csharp
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string?  CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string?  UpdatedBy { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public AuditInterceptor(ICurrentUser user, TimeProvider clock)
        => (_user, _clock) = (user, clock);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    // Override the sync version too, or SaveChanges() silently skips auditing
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? ctx)
    {
        if (ctx is null) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        var who = _user.Id ?? "system";

        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditable a)
            {
                if (entry.State == EntityState.Added)
                {
                    a.CreatedAt = now; a.CreatedBy = who;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAt = now; a.UpdatedBy = who;
                    // prevent overwriting creation metadata
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                }
            }

            // SOFT DELETE: turn a DELETE into an UPDATE
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable s)
            {
                entry.State = EntityState.Modified;   // <- the key move
                s.IsDeleted = true;
                s.DeletedAt = now;
                s.DeletedBy = who;
            }
        }
    }
}
```

Registration:

```csharp
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
    opt.UseSqlServer(cs)
       .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
```

> The interceptor needs `ICurrentUser` (scoped), so it must be resolved through `sp` in the `(sp, opt)` overload. Registering it as `new AuditInterceptor()` means it can never see the user.

##### Global query filters — so you never write `Where(x => !x.IsDeleted)` again

```csharp
protected override void OnModelCreating(ModelBuilder mb)
{
    foreach (var et in mb.Model.GetEntityTypes())
    {
        if (!typeof(ISoftDeletable).IsAssignableFrom(et.ClrType)) continue;

        var param = Expression.Parameter(et.ClrType, "e");
        var body  = Expression.Equal(
            Expression.Property(param, nameof(ISoftDeletable.IsDeleted)),
            Expression.Constant(false));

        mb.Entity(et.ClrType).HasQueryFilter(Expression.Lambda(body, param));
    }

    // Filtered index - critical for soft-delete performance
    mb.Entity<Order>()
      .HasIndex(o => o.CustomerId)
      .HasFilter("[IsDeleted] = 0");
}
```

Bypass the filter when you need to (admin screens, reports):

```csharp
var all = await db.Orders.IgnoreQueryFilters().ToListAsync();
```

##### Soft-delete traps you must know

| Trap | Consequence | Fix |
|---|---|---|
| Unique index on `Email` | Soft-delete then re-create with the same email violates the constraint | Use a **filtered unique index**: `WHERE IsDeleted = 0` |
| Required navigation to a soft-deleted row | EF throws, or you get an unexpected null | Cascade the soft delete to children, or make the nav optional |
| `Include` children that are filtered out | Wrong counts (order has 5 items, shows 3) | The filter applies to `Include` too; consider cascading |
| Table grows forever | Queries degrade over time | Archive/purge job after N days |
| Raw SQL / Dapper / views | No query filter → deleted data leaks out | Add `IsDeleted = 0` manually, or expose a pre-filtered VIEW |

##### Other interceptors worth having

```csharp
// 1) Log slow queries
public class SlowQueryInterceptor : DbCommandInterceptor
{
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand cmd, CommandExecutedEventData e, DbDataReader result,
        CancellationToken ct = default)
    {
        if (e.Duration.TotalMilliseconds > 500)
            Log.Warning("Slow query {Ms}ms {Sql}", e.Duration.TotalMilliseconds, cmd.CommandText);
        return base.ReaderExecutedAsync(cmd, e, result, ct);
    }
}

// 2) Multi-tenancy: set SESSION_CONTEXT when the connection opens
public class TenantConnectionInterceptor : DbConnectionInterceptor { /* ... */ }

// 3) Outbox: collect domain events after a successful SaveChanges
public class OutboxInterceptor : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    { /* publish events */ return base.SavedChangesAsync(e, result, ct); }
}
```

##### Interview answer
> "I use a `SaveChangesInterceptor` that walks `ChangeTracker.Entries()`: anything implementing `IAuditable` gets created/updated stamps, and anything implementing `ISoftDeletable` in the `Deleted` state is flipped to `Modified` with `IsDeleted = true`. That pairs with global query filters applied dynamically in `OnModelCreating`. The gotchas are filtered indexes, filtered unique indexes, and the fact that query filters don't apply to raw SQL."

---

<a id="dn-7"></a>

#### Performance & Query Optimization

##### 2.2.1 N+1 — the classic

**Symptom:** one query fetches a list, then each element fires another query.

```csharp
var orders = await db.Orders.ToListAsync();          // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);              // N queries (lazy loading)
// 1 + 1000 = 1001 round-trips. At ~1ms each that's a full second of pure waiting.
```

Even with lazy loading disabled you can still write N+1:

```csharp
foreach (var o in orders)
    o.ItemCount = await db.OrderItems.CountAsync(i => i.OrderId == o.Id);   // N queries
```

**How to detect it:**

- Enable EF logging for `Microsoft.EntityFrameworkCore.Database.Command` at `Information`.
- Look at OTel traces: 200 near-identical `db.query` spans in one request means N+1.
- Simplest of all: count SQL commands in a test (see Part 17).

**How to fix it:**

```csharp
// 1) Eager loading
var orders = await db.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items).ThenInclude(i => i.Product)
    .ToListAsync();

// 2) Projection - BEST for read models
var dtos = await db.Orders
    .Where(o => o.CreatedAt >= from)
    .Select(o => new OrderDto(
        o.Id,
        o.Customer.Name,                       // EF emits the JOIN
        o.Items.Count,                         // EF emits a COUNT subquery
        o.Items.Sum(i => i.Price * i.Qty)))    // aggregation runs in the database
    .AsNoTracking()
    .ToListAsync();

// 3) Manual batching when you need it
var orderIds = orders.Select(o => o.Id).ToList();
var items = await db.OrderItems
    .Where(i => orderIds.Contains(i.OrderId))
    .ToListAsync();                            // 1 query instead of N
var lookup = items.ToLookup(i => i.OrderId);
```

##### 2.2.2 Cartesian explosion — the trap on the other side of `Include`

Fixing N+1 by including several collections creates a different problem:

```csharp
db.Orders
  .Include(o => o.Items)      // 50 rows
  .Include(o => o.Payments)   // 10 rows
  .Include(o => o.Shipments); // 5 rows
// SQL returns 50 x 10 x 5 = 2,500 rows for ONE order,
// each repeating every Order column. Bandwidth explodes.
```

**Fix: `AsSplitQuery()`** — EF issues separate SELECTs and stitches the results in memory:

```csharp
db.Orders
  .Include(o => o.Items)
  .Include(o => o.Payments)
  .AsSplitQuery()             // 3 lean SELECTs, no row multiplication
  .ToListAsync();
```

Or set it as the default:

```csharp
opt.UseSqlServer(cs, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
```

**The trade-off:** split queries mean more round-trips and are **not atomic** (data can change between the SELECTs). If you need consistency, wrap them in a transaction with an appropriate isolation level, or keep a single query.

| Situation | Choose |
|---|---|
| One collection, few rows | Single query (plain `Include`) |
| Multiple or large collections | `AsSplitQuery()` |
| Read-only display | `Select` projection (no `Include` at all) |

##### 2.2.3 Splitting one big query into several and joining in the application layer

When is this actually the right move? When a 6–7 table JOIN makes the optimizer pick a bad plan, or when the data comes from **different sources** (DB + Redis + an API).

```csharp
// Step 1: run independent queries in parallel
// (NOTE: each needs its own DbContext - see 3.1)
await using var ctx1 = await _factory.CreateDbContextAsync();
await using var ctx2 = await _factory.CreateDbContextAsync();

var ordersTask   = ctx1.Orders.Where(o => o.CustomerId == id)
                              .AsNoTracking().ToListAsync();
var paymentsTask = ctx2.Payments.Where(p => p.CustomerId == id)
                              .AsNoTracking().ToListAsync();
var productsTask = _productApi.GetAllAsync();       // a different service

await Task.WhenAll(ordersTask, paymentsTask, productsTask);

var orders   = await ordersTask;
var payments = await paymentsTask;
var products = await productsTask;

// Step 2: join in memory with LINQ - O(n+m) thanks to hash lookups
var paymentsByOrder = payments.ToLookup(p => p.OrderId);
var productById     = products.ToDictionary(p => p.Id);

var result = orders.Select(o => new OrderView
{
    Id       = o.Id,
    Total    = o.Total,
    Paid     = paymentsByOrder[o.Id].Sum(p => p.Amount),
    Products = o.Items.Select(i => productById[i.ProductId].Name).ToList()
}).ToList();
```

**Golden rules for in-memory joins:**

- Always build a `Dictionary` or `ToLookup`. Nesting `list.Where(...)` inside a `Select` gives you O(n×m) — you've moved the cost from the database to the CPU and it's still slow.
- Only viable for **bounded** data (a few thousand rows). Never pull 500k rows into the app to join them.
- Filtering and paging must still happen in the database.

**When not to split:** a 2–3 table join over well-indexed columns is far faster in the database than in your app. Don't "optimize" on instinct — read the execution plan first.

##### 2.2.4 EF Core optimization checklist (in priority order)

```csharp
// 1. AsNoTracking for read-only -> no snapshots, ~30-50% fewer allocations
await db.Orders.AsNoTracking().ToListAsync();

// 2. Projection: select only the columns you need. Don't SELECT * then map.
.Select(o => new { o.Id, o.Total })

// 3. Keyset paging instead of OFFSET (OFFSET 100000 must scan and discard 100k rows)
.Where(o => o.Id > lastSeenId).OrderBy(o => o.Id).Take(50)

// 4. Push filters and aggregates into the database. WRONG:
var bad  = (await db.Orders.ToListAsync()).Where(o => o.Total > 100);   // loads everything
var good = await db.Orders.Where(o => o.Total > 100).ToListAsync();

// 5. Bulk update/delete (EF Core 7+) - no entities loaded into memory
await db.Orders.Where(o => o.Status == "Draft" && o.CreatedAt < cutoff)
               .ExecuteDeleteAsync();
await db.Orders.Where(o => o.Status == "Pending")
               .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "Expired"));

// 6. Compiled queries on hot paths
private static readonly Func<AppDbContext, int, Task<Order?>> GetById =
    EF.CompileAsyncQuery((AppDbContext db, int id) =>
        db.Orders.FirstOrDefault(o => o.Id == id));

// 7. Avoid 'Contains' with huge lists (SQL Server caps around 2100 parameters)
//    -> use a TVP, a temp table, or batches of 500-1000

// 8. Stream large result sets instead of materializing them
await foreach (var o in db.Orders.AsNoTracking().AsAsyncEnumerable())
    await ProcessAsync(o);

// 9. Index every column in WHERE/JOIN/ORDER BY. Use covering indexes with INCLUDE.
// 10. See the SQL you actually generate:
var sql = query.ToQueryString();
```

##### Interview answer (query optimization)
> "Measure first: EF command logging plus OTel spans to find N+1 and slow queries. Fix N+1 with projections or Include; if you include multiple collections, add AsSplitQuery to avoid a cartesian explosion. Read paths get AsNoTracking plus a DTO projection. Filters and aggregates belong in the database. Page with keysets, not OFFSET. Bulk work uses ExecuteUpdate/ExecuteDelete. Indexes and execution plans come last, once the query shape is right."

---

### Dependency Injection & Concurrency

<p class="src">From <em>.NET Backend Deep Dive</em> · Dependency Injection & Concurrency</p>

<a id="dn-8"></a>

#### DbContext + Task.WhenAll = crash

##### Why it breaks
`DbContext` is **not thread-safe** and allows only **one operation at a time**.

```csharp
public class ReportService
{
    private readonly AppDbContext _db;   // scoped - one instance per request

    public async Task<Report> BuildAsync()
    {
        // BOOM: InvalidOperationException
        // "A second operation was started on this context instance
        //  before a previous operation completed."
        var a = _db.Orders.CountAsync();
        var b = _db.Users.CountAsync();
        var c = _db.Products.CountAsync();
        await Task.WhenAll(a, b, c);
        ...
    }
}
```

Even when you get lucky and no exception is thrown, the `ChangeTracker` state is corrupted — a silent bug, which is worse.

##### Option 1 (recommended): `IDbContextFactory`

```csharp
// Program.cs
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseSqlServer(cs));
// To have both (scoped DbContext for controllers + factory for parallel work):
builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseSqlServer(cs),
    lifetime: ServiceLifetime.Scoped);
```

```csharp
public class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    public ReportService(IDbContextFactory<AppDbContext> f) => _factory = f;

    public async Task<Report> BuildAsync(CancellationToken ct)
    {
        // One DbContext and one connection per task -> genuinely parallel
        var ordersTask   = CountAsync(db => db.Orders.CountAsync(ct));
        var usersTask    = CountAsync(db => db.Users.CountAsync(ct));
        var productsTask = CountAsync(db => db.Products.CountAsync(ct));

        await Task.WhenAll(ordersTask, usersTask, productsTask);

        return new Report(await ordersTask, await usersTask, await productsTask);

        async Task<int> CountAsync(Func<AppDbContext, Task<int>> q)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            return await q(db);
        }
    }
}
```

##### Option 2: create your own scope (when you need the whole service graph)

```csharp
public class ReportService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public async Task<Report> BuildAsync()
    {
        var tasks = new[] { "orders", "users", "products" }
            .Select(async name =>
            {
                using var scope = _scopeFactory.CreateScope();   // isolated scope
                var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var repo = scope.ServiceProvider.GetRequiredService<IRepo>();
                return await repo.CountAsync(name);
            });

        var results = await Task.WhenAll(tasks);
        return new Report(results);
    }
}
```

##### Option 3: run them sequentially (often the correct answer)

Three queries at 20ms each: 60ms sequential, 20ms parallel. You save 40ms but consume three pooled connections. With 200 concurrent requests × 3 connections = 600 connections → **pool exhaustion**, which is far worse than the 40ms.

> Parallelizing database calls only pays off when each query is **slow** (>100ms) and **traffic is low**. On a high-traffic API, sequential is safer.

##### Comparison

| Approach | Use when | Risk |
|---|---|---|
| `IDbContextFactory` | Parallel queries, Blazor Server, background jobs | Connection pool pressure |
| `IServiceScopeFactory` | You need the whole DI graph, background services | Forgetting `Dispose` → leaks |
| Sequential | Default, high-traffic APIs | Latency adds up |
| Singleton `DbContext` | **Never** | Memory leak + race conditions |

##### Interview answer
> "DbContext isn't thread-safe, so you can't share one instance across `Task.WhenAll`. I inject `IDbContextFactory<T>` and create a context per task, or `IServiceScopeFactory` when I need the whole graph. But the first question is whether parallelism is even worth it — with fast queries and high traffic, sequential execution avoids exhausting the connection pool."

---

### EF Core Internals: What People Miss

<p class="src">From <em>.NET Backend Deep Dive</em> · EF Core Internals: What People Miss</p>

<a id="dn-62"></a>

#### `ExecuteUpdate`/`ExecuteDelete` bypass interceptors

This is the most dangerous trap here, and it **breaks the soft delete from Part 2**:

```csharp
await db.Orders.Where(o => o.Status == "Draft").ExecuteDeleteAsync();
// -> Emits a REAL DELETE.
// -> Does NOT go through SaveChangesInterceptor -> soft delete does not happen.
// -> Does NOT set audit columns.
// -> Does NOT update the ChangeTracker (tracked entities become ghosts).
```

Global query filters **do** still apply (they're part of the query), but interceptors don't.

```csharp
// Bulk soft delete must be explicit:
await db.Orders
    .Where(o => o.Status == "Draft")
    .ExecuteUpdateAsync(s => s
        .SetProperty(o => o.IsDeleted, true)
        .SetProperty(o => o.DeletedAt, DateTime.UtcNow)
        .SetProperty(o => o.DeletedBy, _user.Id));
```

Consider writing a `SoftDeleteAsync()` extension and **banning** raw `ExecuteDeleteAsync` via an analyzer or code review rule.

<a id="dn-64"></a>

#### How the change tracker works

Every tracked entity gets a **snapshot** of its values at load time. `SaveChanges` diffs current values against that snapshot to determine which columns changed.

Consequences:

- Tracking **1,000 entities** means 1,000 snapshots — double the memory, and `DetectChanges` is **O(n × properties)**.
- `SaveChanges` runs `DetectChanges` over **every** tracked entity, including ones you never touched.

```csharp
// Bulk inserts: disable auto-detection
db.ChangeTracker.AutoDetectChangesEnabled = false;
foreach (var item in items) db.Add(item);
db.ChangeTracker.DetectChanges();
await db.SaveChangesAsync();
db.ChangeTracker.AutoDetectChangesEnabled = true;

// Or better: EFCore.BulkExtensions -> BulkInsertAsync (SqlBulkCopy)
```

```csharp
// AsNoTracking does NOT deduplicate. The same Customer across 10 orders
// produces 10 distinct Customer objects.
db.Orders.Include(o => o.Customer).AsNoTracking();

// Need deduplication without tracking:
db.Orders.Include(o => o.Customer).AsNoTrackingWithIdentityResolution();
```

<a id="dn-65"></a>

#### DbContext pooling

```csharp
builder.Services.AddDbContextPool<AppDbContext>(opt => opt.UseSqlServer(cs), poolSize: 128);
```

Reuses `DbContext` instances (resetting the change tracker between uses), cutting allocations roughly 30% on high-throughput apps.

**Condition:** your `DbContext` must **not** hold request-specific state captured in its constructor (e.g. injecting `ICurrentUser` into a field), because the instance is reused across requests. If you need that, use `IDbContextFactory` or read the user via `IHttpContextAccessor` at point of use.

<a id="dn-66"></a>

#### Connection resiliency vs manual transactions

```csharp
opt.UseSqlServer(cs, o => o.EnableRetryOnFailure(
    maxRetryCount: 5,
    maxRetryDelay: TimeSpan.FromSeconds(10),
    errorNumbersToAdd: null));
```

**Trap:** enabling retries and then calling `BeginTransaction()` manually throws — EF can't safely retry a transaction you control. Wrap it in an execution strategy:

```csharp
var strategy = db.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    // ... several operations
    await db.SaveChangesAsync();
    await tx.CommitAsync();
});
```

<a id="dn-67"></a>

#### Inheritance: TPH / TPT / TPC

| | Storage | Pro | Con |
|---|---|---|---|
| **TPH** (default) | One table plus a `Discriminator` column | Fastest, no joins | Many nullable columns |
| **TPT** | Base table plus child tables | Normalized | A join on every query → slower |
| **TPC** (EF7+) | One complete table per concrete type | Very fast for a single type | Polymorphic queries need UNION |

Default to **TPH** unless you have a concrete reason not to.

<a id="dn-68"></a>

#### Value converters, owned types, JSON columns

```csharp
// Value object -> a column
mb.Entity<Order>().Property(o => o.Currency)
  .HasConversion(v => v.Code, v => Currency.FromCode(v));

// Owned type -> flattened columns in the same table
mb.Entity<Customer>().OwnsOne(c => c.Address);   // Address_Street, Address_City...

// JSON column (EF8+) - store an object graph in one nvarchar(max), still queryable
mb.Entity<Order>().OwnsMany(o => o.Metadata, b => b.ToJson());
```

**Value converter warning:** converted columns often **lose index usability** for certain comparisons, and operations like `StartsWith` may not translate to SQL at all.

<a id="dn-70"></a>

#### Useful APIs people overlook

```csharp
db.ChangeTracker.Clear();                        // fast reset instead of a new context
db.Entry(order).State = EntityState.Detached;
db.Entry(order).Property(o => o.Total).IsModified = true;   // update a single column
await db.Entry(order).Collection(o => o.Items).LoadAsync(); // explicit loading
db.Attach(entity).State = EntityState.Modified;             // update without a SELECT first
var sql = query.ToQueryString();                            // see the real SQL
db.Database.SetCommandTimeout(120);
```

---

### Relational Data & EF Core

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Relational Data & EF Core</p>

<p class="pd">Where the state lives. Relational and document stores, the ORM that hides them, and the concurrency and caching decisions that decide whether the system survives real traffic.</p>

<a id="be-a94"></a>

#### ADO.NET and connection pooling

Everything — EF Core, Dapper, your ORM of choice — sits on ADO.NET: `DbConnection`, `DbCommand`, `DbDataReader`. Knowing the raw layer is what lets you debug the layers above it.

``` csharp
await using var conn = new SqlConnection(connectionString);
await conn.OpenAsync(ct);

await using var cmd = new SqlCommand(
    "SELECT Id, Total FROM Orders WHERE CustomerId = @cid", conn);
cmd.Parameters.Add("@cid", SqlDbType.UniqueIdentifier).Value = customerId;

await using var r = await cmd.ExecuteReaderAsync(ct);
while (await r.ReadAsync(ct))
    yield return new Order(r.GetGuid(0), r.GetDecimal(1));
```

`OpenAsync` rarely opens a real socket. The driver keeps a pool keyed on the exact connection string, so opening late and disposing early returns the connection for reuse. Holding one open for the lifetime of a service is how you exhaust the pool and get `Timeout expired... from the pool` under load.

> **\[Trap\]**
>
> **Common trap.** String-concatenating user input into SQL is SQL injection, full stop. Parameters are not just safer, they also let the database cache the query plan.

###### What the pool actually does, and how it fails

``` text
  a "connection" in .NET is TWO things

    DbConnection      a lightweight managed object. Cheap to create.
    the POOLED physical connection    a real TCP socket, authenticated,
                                      with server-side session state.

  new SqlConnection(cs)     -> allocates the managed object. Nothing happens.
  conn.Open()               -> takes a PHYSICAL connection from the pool,
                               or opens a new one if none is free
  conn.Close() / Dispose()  -> RETURNS it to the pool. Does NOT close the socket.

  so the `using` block you write does not open and close a TCP connection.
  It rents and returns one. Which is why:
    - opening a connection late and closing early is correct and cheap
    - holding one open "to save time" is actively harmful
    - a leaked connection is a permanently lost pool slot
```

``` csharp
// the pool is keyed on the EXACT connection string, character for character.
"Server=db;Database=app;User Id=svc;Password=x;Max Pool Size=100"
"Server=db;Database=app;User Id=svc;Password=x;Max Pool Size=100;"  // <- TRAILING ;
// -> TWO separate pools, each up to 100 connections. 200 total.
// building connection strings dynamically per tenant is the usual way
// teams accidentally create hundreds of pools.

// the settings that matter
Max Pool Size=100          // default 100. This is PER POOL, per process.
Min Pool Size=5            // keep some warm; avoids cold-start latency
Connection Lifetime=1800   // recycle, so a failover/DNS change is picked up
Connect Timeout=5          // TIME TO GET A CONNECTION FROM THE POOL,
                           // not the query timeout. Default 15s is too long.
Command Timeout=30         // the query timeout - separate setting
```

``` text
  POOL EXHAUSTION - what the error actually means

    "Timeout expired. The timeout period elapsed prior to obtaining a
     connection from the pool. This may have occurred because all pooled
     connections were in use and max pool size was reached."

  this is NOT a database problem. The database may be idle.
  it means: your application asked for connection 101 while 100 were checked out.

  the four causes, in order of frequency
    1  a LEAK: a connection or DbContext not disposed. The classic is an
       exception path that skips Dispose, or a DbContext resolved from the
       root provider so it is never scoped-disposed.
    2  slow queries: each holds its connection longer, so concurrency
       requirements rise. One missing index can exhaust the pool.
    3  a long transaction holding a connection across a network call.
    4  genuinely too small a pool for the load.

  diagnose by counting from BOTH sides:
    app side  -> the ADO.NET counters below
    db side   -> SELECT count(*), state FROM pg_stat_activity GROUP BY state;
                 many 'idle in transaction' = cause 3.
```

``` text
# watch the pool live - this answers "leak or load?" in about a minute
dotnet-counters monitor --process-id <pid> Microsoft.Data.SqlClient.EventSource
#   active-hard-connections     physical sockets open
#   number-of-pooled-connections
#   number-of-active-connections    checked OUT right now
#   number-of-free-connections
#
# a leak looks like: active climbs monotonically and never falls, even
# when traffic drops. Load looks like: active tracks request rate.

# Npgsql exposes the same via its own EventSource / OpenTelemetry metrics.
```

> **\[Trap\]**
>
> **Common trap.** `MultipleActiveResultSets=true` on SQL Server lets one connection carry several open readers, which sounds convenient and is a common source of subtle problems: it changes how the driver batches, interacts badly with some transaction patterns, and hides the fact that you are iterating a reader while issuing more queries. If EF Core is telling you to enable MARS, the real fix is usually to materialise the first query with `ToListAsync()` before starting the second.

<a id="be-a95"></a>

#### EF Core: DbContext, change tracking and the unit of work

A `DbContext` is a unit of work plus an identity map. It is not thread-safe, it is cheap to create, and it should live for exactly one logical operation — hence `AddDbContext` registering it as scoped.

``` csharp
builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder b)
        => b.ApplyConfigurationsFromAssembly(typeof(AppDb).Assembly);
}

// one SaveChanges = one transaction over every tracked change
var order = await db.Orders.FindAsync([id], ct);
order.Cancel();
await db.SaveChangesAsync(ct);
```

Change tracking means EF snapshots each loaded entity and diffs it on save. That is convenient for writes and pure overhead for reads, which is why read-only queries should say so.

| Call                   | Effect                                                  |
|------------------------|---------------------------------------------------------|
| `AsNoTracking()`       | Skip the snapshot; faster reads, entities not updatable |
| `AsSplitQuery()`       | Multiple SELECTs instead of one cartesian join          |
| `Include()`            | Eager-load a navigation in the same round trip          |
| `ExecuteUpdateAsync()` | Set-based UPDATE with no entities loaded (EF 7+)        |

###### How it actually works

Change tracking is a snapshot diff. When an entity is loaded with tracking on, EF stores a copy of every scalar property in the `ChangeTracker`. On `SaveChanges`, it compares current values to that snapshot and generates statements only for what differs.

``` text
  db.Orders.First(...)         -> entity constructed
                               -> EntityEntry created, state = Unchanged
                               -> ORIGINAL VALUES snapshot taken (a full copy of every column)

  order.Status = Shipped       -> nothing happens yet; EF has not noticed

  db.SaveChangesAsync()        -> DetectChanges(): walk EVERY tracked entity,
                                  compare EVERY property against its snapshot   <- O(entities x properties)
                               -> state becomes Modified, only Status is dirty
                               -> UPDATE Orders SET Status = @p WHERE Id = @id AND RowVersion = @v
                               -> all statements batched, wrapped in one transaction
```

The cost hides in `DetectChanges`. It runs on every `SaveChanges`, and also on most queries and on `Local` access. Tracking 50,000 entities in one context turns each of those into a walk over 50,000 objects × every property. A loop that saves after each item becomes quadratic, and an import that takes two seconds for 1,000 rows takes ten minutes for 50,000.

``` csharp
// quadratic without meaning to be
foreach (var row in rows)            // 50,000 rows
{
    db.Items.Add(Map(row));
    await db.SaveChangesAsync(ct);   // DetectChanges walks everything added so far
}

// linear
foreach (var chunk in rows.Chunk(1000))
{
    db.Items.AddRange(chunk.Select(Map));
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();        // drop the snapshots; the context stays usable
}
```

###### Tracking behaviour, chosen deliberately

| Mode                                   | Snapshot taken | Identity map                           | Use for                                                   |
|----------------------------------------|----------------|----------------------------------------|-----------------------------------------------------------|
| Tracking (default)                     | Yes            | Yes — same row returns the same object | Writes                                                    |
| `AsNoTracking()`                       | No             | No — duplicates possible               | Reads, exports, projections                               |
| `AsNoTrackingWithIdentityResolution()` | No             | Yes                                    | Reads with shared navigations, avoiding duplicate objects |
| `QueryTrackingBehavior` on the context | Depends        | Depends                                | Read-only contexts — set it once, globally                |

``` csharp
// a read-only context makes no-tracking the default and removes a whole class of mistake
public class ReadDb(DbContextOptions<ReadDb> o) : DbContext(o)
{
    public ReadDb(...) { ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking; }
    public override int SaveChanges() => throw new InvalidOperationException("Read-only context.");
}
```

Measured on a query returning 10,000 entities with 15 properties each, no-tracking is typically 20–40% faster and allocates roughly half the memory, purely from skipping the snapshot. Projecting to a DTO with `Select` is faster still, because the unneeded columns never leave the database.

###### Interceptors and auditing without touching handlers

``` csharp
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var now = _time.GetUtcNow();
    var user = _current.UserId;

    foreach (var e in ChangeTracker.Entries<IAuditable>())
    {
        if (e.State == EntityState.Added)
        {
            e.Entity.CreatedAt = now; e.Entity.CreatedBy = user;
        }
        else if (e.State == EntityState.Modified)
        {
            e.Property(x => x.CreatedAt).IsModified = false;   // never let it be overwritten
            e.Entity.ModifiedAt = now; e.Entity.ModifiedBy = user;
        }
    }

    // collect domain events BEFORE saving, dispatch AFTER, so handlers see committed state
    var events = ChangeTracker.Entries<IHasDomainEvents>()
        .SelectMany(e => e.Entity.DequeueEvents()).ToList();

    var result = await base.SaveChangesAsync(ct);

    foreach (var evt in events) await _dispatcher.Publish(evt, ct);
    return result;
}
```

> **\[Trap\]**
>
> **Common trap.** Dispatching domain events *before* `base.SaveChangesAsync` means handlers see uncommitted state and may act on a transaction that then rolls back. Dispatching after means the handler's own writes are in a separate transaction. If both must be atomic, write to an outbox table inside the same `SaveChanges` and publish from a relay — there is no third option.

<a id="be-a96"></a>

#### The EF Core query pitfalls that bite in production

EF Core is fast when you know what SQL it emits and slow when you do not. Three failure modes cover most real incidents.

##### 1. The N+1 select

``` csharp
// 1 query for orders, then 1 per order for lines = 101 round trips
foreach (var o in await db.Orders.ToListAsync(ct))
    total += o.Lines.Sum(l => l.Price);

// fixed: one round trip
var orders = await db.Orders.Include(o => o.Lines).ToListAsync(ct);
// better: never load the entities at all
var total = await db.OrderLines.Where(l => l.Order.Date >= from).SumAsync(l => l.Price, ct);
```

##### 2. Client evaluation

If EF cannot translate an expression it throws in modern versions — but a `.ToList()` or `.AsEnumerable()` earlier in the chain makes everything after it run in memory. Watch for a filter that should have removed a million rows running after the fetch.

##### 3. The cartesian explosion

Two `Include`s on collection navigations produce a join with rows equal to the product of both sets. Ten lines and ten payments become a hundred rows of mostly duplicated data. `AsSplitQuery()` is the fix.

> **\[Note\]**
>
> **See the SQL.** Turn on `EnableSensitiveDataLogging()` in Development plus `LogTo(Console.WriteLine, LogLevel.Information)`, or attach MiniProfiler. If you have never read the SQL your ORM emits, you do not know what your app does.

###### Worked example

The same page of data, four ways, with the SQL each one actually emits. This is worth reading closely because the C# looks nearly identical in each case.

``` csharp
// A. loads whole entities, tracks them, fetches every column
var orders = await db.Orders
    .Include(o => o.Lines).Include(o => o.Customer)
    .Where(o => o.PlacedAt >= from)
    .OrderByDescending(o => o.PlacedAt).Take(20)
    .ToListAsync(ct);
```

``` sql
-- one query with two joins. Take(20) applies to the JOINED rows, so you may get
-- fewer than 20 orders, or duplicated order columns 8x for an order with 8 lines.
SELECT o.*, c.*, l.*
FROM (SELECT TOP 20 * FROM Orders WHERE PlacedAt >= @from ORDER BY PlacedAt DESC) o
LEFT JOIN Customers c ON ...
LEFT JOIN OrderLines l ON ...
```

``` csharp
// B. split query: no cartesian duplication
var orders = await db.Orders
    .Include(o => o.Lines).Include(o => o.Customer)
    .Where(o => o.PlacedAt >= from)
    .OrderByDescending(o => o.PlacedAt).Take(20)
    .AsSplitQuery().AsNoTracking()
    .ToListAsync(ct);
// -> 3 round trips, far less data transferred, no duplicate rows.
//    Trade-off: not one atomic snapshot unless wrapped in a transaction.

// C. projection: only the columns the screen needs, no entities at all
var page = await db.Orders
    .Where(o => o.PlacedAt >= from)
    .OrderByDescending(o => o.PlacedAt).Take(20)
    .Select(o => new OrderListItem(
        o.Id, o.Customer.Name, o.PlacedAt,
        o.Lines.Count,                          // -> a correlated subquery, not a join
        o.Lines.Sum(l => l.UnitPrice * l.Qty)))
    .ToListAsync(ct);
// -> ONE query, ~6 columns instead of ~40, no tracking, no duplicate rows.
//    This is almost always the right answer for a list view.

// D. Dapper, when the shape is awkward or the tuning must be exact
var page2 = await conn.QueryAsync<OrderListItem>(SqlText, new { from });
```

| Approach                  | Round trips | Rows over the wire     | Use for                                 |
|---------------------------|-------------|------------------------|-----------------------------------------|
| A — Include, single query | 1           | 20 × lines × 1         | Small graphs you will modify            |
| B — Include, split        | 3           | 20 + lines + customers | Larger graphs you will modify           |
| C — Projection            | 1           | 20                     | Any read-only list or detail view       |
| D — Dapper                | 1           | 20                     | Reports, hand-tuned SQL, awkward shapes |

###### Making the SQL visible in every environment

``` csharp
builder.Services.AddDbContext<AppDb>((sp, o) =>
{
    o.UseNpgsql(cs);
    if (env.IsDevelopment())
        o.EnableSensitiveDataLogging()      // shows parameter VALUES - never in production
         .EnableDetailedErrors()
         .LogTo(Console.WriteLine, LogLevel.Information);
});

// in every environment: warn on the mistakes that matter
o.ConfigureWarnings(w =>
{
    w.Throw(RelationalEventId.MultipleCollectionIncludeWarning);   // cartesian explosion
    w.Throw(CoreEventId.NavigationBaseIncludeIgnored);
    w.Log(RelationalEventId.CommandExecuting);
});
```

Turning the multiple-collection-include warning into an exception is one of the highest-value settings in EF Core. The cartesian explosion is invisible in development with ten rows and catastrophic in production with ten thousand; this makes it a build-time-ish failure instead.

For continuous visibility, the EF Core OpenTelemetry instrumentation emits a span per command with the SQL text and duration. Set a threshold alert on command duration and you will find the missing index before a user does.

<a id="be-a97"></a>

#### EF Core: the performance toolkit

Once the obvious mistakes are gone — the N+1s, the missing indexes, the accidental client evaluation — there is a second layer of tools most teams never open. None of them is a default; each answers a specific measured problem.

| Symptom                                  | Tool                                   | Cost                                       |
|------------------------------------------|----------------------------------------|--------------------------------------------|
| Same query, thousands of times a second  | Compiled query                         | Static field, fixed shape                  |
| One Include explodes the row count       | `AsSplitQuery()`                       | Multiple round trips, no cartesian blow-up |
| Read-only list, duplicated child objects | `AsNoTrackingWithIdentityResolution()` | Identity map without change tracking       |
| Hot reference data re-read constantly    | Second-level cache                     | Invalidation becomes your problem          |
| Thousands of inserts                     | Bulk copy / batching                   | Bypasses the tracker and its events        |

``` csharp
// 1. Compiled query — skip expression-tree translation on the hottest path
private static readonly Func<AppDb, Guid, CancellationToken, Task<Order?>> _byId =
    EF.CompileAsyncQuery((AppDb db, Guid id, CancellationToken ct) =>
        db.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id));

public Task<Order?> GetAsync(Guid id, CancellationToken ct) => _byId(db, id, ct);
// worth it for the two or three hottest queries. Everywhere else it is
// noise: the shape is fixed, so no conditional filters, no dynamic sorting.

// 2. Split query — one JOIN returning parents x children x grandchildren
//    is a cartesian product; splitting trades round trips for row count
var orders = await db.Orders
    .Include(o => o.Lines).Include(o => o.Shipments)
    .AsSplitQuery()                       // 3 queries instead of 1 fat one
    .ToListAsync(ct);
// ⚠ split queries are NOT in one snapshot unless you wrap them in a
//   transaction — concurrent writes can produce an inconsistent graph.

// 3. Identity resolution without tracking: same child object referenced
//    twice becomes one instance, but nothing is tracked for updates
var report = await db.Orders
    .AsNoTrackingWithIdentityResolution()
    .Include(o => o.Customer)             // shared customers → one instance
    .ToListAsync(ct);
```

``` csharp
// 4. Batching is already on — but it has a ceiling worth knowing
db.AddRange(entities);                    // EF batches INSERTs per SaveChanges
await db.SaveChangesAsync(ct);            // default max batch size ~42 (SQL Server)
// beyond a few thousand rows, the tracker itself becomes the bottleneck:
optionsBuilder.UseSqlServer(cs, o => o.MaxBatchSize(100));

// for tens of thousands: bypass EF entirely
using var bulk = new SqlBulkCopy(connection)
{ DestinationTableName = "Orders", BatchSize = 5_000 };
await bulk.WriteToServerAsync(reader, ct);       // seconds, not minutes
// Postgres equivalent: the binary COPY protocol via Npgsql BeginBinaryImport

// 5. Connection resiliency — retries for transient cloud failures
optionsBuilder.UseSqlServer(cs, o => o.EnableRetryOnFailure(
    maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10),
    errorNumbersToAdd: null));
```

> **\[Trap\]**
>
> **Common trap.** Enabling retry-on-failure makes user-initiated transactions throw: EF cannot retry a block it did not open. Wrap explicit transactions in the execution strategy — `db.Database.CreateExecutionStrategy().ExecuteAsync(...)` — or the first transient error in production surfaces as an unhandled exception rather than a retry.

> **\[Trap\]**
>
> **Second-level cache.** Packages exist that cache query results across requests, and they are genuinely useful for reference data. They are also a distributed-cache invalidation problem wearing an ORM costume: any write path that bypasses EF — raw SQL, another service, a DBA — leaves stale rows served indefinitely. Scope it to data you own and can invalidate deliberately.

> **\[Why it matters\]** Each of these is a specific answer to a specific measurement, and applying them speculatively makes code worse: compiled queries freeze query shape, split queries weaken consistency, bulk copy skips your domain events. Reach for them when the profiler names the problem — and record in the code *why*, because the next reader will otherwise assume it is cargo cult.

<a id="be-a99"></a>

#### Dapper and when to drop the ORM

Dapper is a thin mapper over ADO.NET: you write the SQL, it fills the objects. No change tracking, no query translation, no surprises. It is the right tool for complex reports, bulk reads, and any query where you were going to fight the ORM anyway.

``` csharp
const string sql = @"
    SELECT o.Id, o.Total, c.Name AS CustomerName
    FROM   Orders o
    JOIN   Customers c ON c.Id = o.CustomerId
    WHERE  o.PlacedAt >= @from
    ORDER  BY o.PlacedAt DESC
    OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";

var rows = await conn.QueryAsync<OrderRow>(sql, new { from, skip, take });
```

**EF Core**

- Writes, aggregates, domain models
- Migrations and model-first schema
- Change tracking and relationship fixup

**Dapper**

- Reads, reports, hand-tuned SQL
- Predictable performance, no translation layer
- You own the SQL, including the injection risk

Mixing both in one service is normal and healthy: EF for the write side, Dapper for the read side. That is CQRS with almost no ceremony.

###### Worked example

Where Dapper genuinely wins: a report query with a multi-level join, aggregation and a shape that no ORM will project cleanly.

``` csharp
private const string Sql = @"
WITH monthly AS (
    SELECT  c.id            AS customer_id,
            c.name          AS customer_name,
            date_trunc('month', o.placed_at) AS month,
            SUM(l.unit_price * l.qty)        AS revenue,
            COUNT(DISTINCT o.id)             AS order_count
    FROM    orders o
    JOIN    customers   c ON c.id = o.customer_id
    JOIN    order_lines l ON l.order_id = o.id
    WHERE   o.placed_at >= @from AND o.tenant_id = @tenantId
    GROUP BY c.id, c.name, date_trunc('month', o.placed_at)
)
SELECT  m.*,
        RANK() OVER (PARTITION BY m.month ORDER BY m.revenue DESC) AS rank_in_month,
        m.revenue - LAG(m.revenue) OVER (PARTITION BY m.customer_id ORDER BY m.month) AS delta
FROM    monthly m
ORDER BY m.month DESC, m.rank_in_month;";

var rows = await conn.QueryAsync<MonthlyRevenueRow>(Sql, new { from, tenantId });
```

Window functions, CTEs and `date_trunc` are either untranslatable or awkward in LINQ, and the result is a flat projection with no entity graph. Fighting the ORM here produces worse SQL and unreadable C#; Dapper produces exactly the query you intended in fifteen lines.

``` csharp
// multi-mapping: splitting one row into an object graph
var lookup = new Dictionary<Guid, Order>();
await conn.QueryAsync<Order, OrderLine, Order>(
    @"SELECT o.*, l.* FROM orders o JOIN order_lines l ON l.order_id = o.id WHERE o.id = @id",
    (order, line) =>
    {
        if (!lookup.TryGetValue(order.Id, out var existing))
            lookup[order.Id] = existing = order;
        existing.Lines.Add(line);
        return existing;
    },
    new { id }, splitOn: "id");
```

> **\[Trap\]**
>
> **Common trap.** Dapper does not parameterise for you if you build the string yourself. `$"WHERE status = '{status}'"` is SQL injection with extra steps, and it also defeats plan caching. Use anonymous-object parameters for every value, always — including for values you believe are safe, because the next person will copy the pattern.

###### Making raw SQL maintainable

- **Keep SQL in `.sql` files** as embedded resources, not in string constants. You get syntax highlighting, formatting, and a diff that a DBA can review.
- **Name the parameters after the C# properties** so an anonymous object binds without a mapping table.
- **One query, one record type.** Do not reuse a DTO across three queries that each populate a different subset — the nulls will lie to you.
- **Test them against a real database** in a container. A typo in a column name is a runtime failure with no compiler to catch it, which is the price of dropping the ORM.
- **Version them with the schema.** A migration that renames a column must update every raw query in the same commit, and only a test will tell you if it did.

``` csharp
// embedded resource + a tiny loader keeps call sites clean
public static class SqlResources
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Get(string name) => Cache.GetOrAdd(name, n =>
    {
        using var s = typeof(SqlResources).Assembly
            .GetManifestResourceStream($"Orders.Infrastructure.Sql.{n}.sql")
            ?? throw new InvalidOperationException($"SQL resource '{n}' not found.");
        return new StreamReader(s).ReadToEnd();
    });
}

var rows = await conn.QueryAsync<MonthlyRevenueRow>(
    SqlResources.Get("MonthlyRevenue"), new { from, tenantId });
```

### Factories, Lifetimes & Object Creation

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Factories, Lifetimes & Object Creation</p>

<p class="pd">Almost every awkward question in a .NET codebase — ‘how do I get a DbContext in a singleton?’, ‘why is my HttpClient exhausting sockets?’, ‘how do I pick an implementation at runtime?’ — has the same answer: a factory. Here is the whole family.</p>

<a id="be-a87"></a>

#### IDbContextFactory and DbContext pooling

Scoped injection is right for a web request, but wrong when one operation needs several contexts at once, or when there is no scope at all — Blazor components, parallel work, or a singleton cache warmer.

``` csharp
builder.Services.AddDbContextFactory<AppDb>(o => o.UseNpgsql(cs));

public class Warmer(IDbContextFactory<AppDb> factory)
{
    public async Task RunAsync(CancellationToken ct)
    {
        // parallel work: each task needs its OWN context - DbContext is not thread-safe
        await Parallel.ForEachAsync(tenantIds, ct, async (id, token) =>
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await Load(db, id, token);
        });
    }
}
```

##### Pooling

`AddDbContextPool` keeps a pool of context instances and resets their state between uses instead of constructing a new object graph each time. It measurably helps high-throughput APIs, and it comes with rules: no mutable state in your context class, no constructor logic that depends on per-request data, and injected scoped services must be handled via the pooling-aware overload.

| API                         | Lifetime                                | Use for                                   |
|-----------------------------|-----------------------------------------|-------------------------------------------|
| `AddDbContext`              | Scoped                                  | Normal web requests                       |
| `AddDbContextPool`          | Scoped, instance pooled                 | High-throughput APIs with a plain context |
| `AddDbContextFactory`       | Singleton factory, you own the instance | Blazor, parallel work, background jobs    |
| `AddPooledDbContextFactory` | Both                                    | The above, at high throughput             |

###### IDesignTimeDbContextFactory, and why migrations stop working

``` text
  THE PROBLEM

    dotnet ef migrations add AddOrderIndex

    the EF tooling needs a DbContext INSTANCE at DESIGN TIME. It gets
    one by, in order:

      1  looking for an IDesignTimeDbContextFactory<T>
      2  otherwise, BUILDING YOUR HOST - running Program.cs up to the
         point where the service provider exists

    step 2 is why it breaks. Building your host may:
      connect to Key Vault              -> no credentials on a build agent
      require environment variables     -> not set in a shell
      start an Aspire AppHost           -> not running
      run a startup health check        -> the database is not there yet
      need a connection string that only exists in the deployed config

    the error is unhelpfully generic:
      "Unable to create a DbContext of type ''."
      "Unable to create an object of type 'AppDbContext'. For the
       different patterns supported at design time, see ..."
```

``` csharp
// the fix: give the tooling its own path, bypassing the host entirely.
// this class is used ONLY by the EF tools. It never runs in production.
public sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("DESIGN_TIME_CONNECTION")
                 ?? "Host=localhost;Database=app_design;Username=postgres;Password=dev";
        // the database does NOT need to exist and does NOT need to be
        // reachable to SCAFFOLD a migration - EF only needs the model.
        // It DOES need to be reachable for `database update`.

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs, b => b.MigrationsAssembly("Acme.Migrations"))
            .Build_();
        return new AppDbContext(options);
    }
}
```

| Situation                                    | Approach                                                            |
|----------------------------------------------|---------------------------------------------------------------------|
| Migrations in a separate project             | `MigrationsAssembly(...)`, plus `--project` and `--startup-project` |
| `DbContext` has non-DI constructor arguments | `IDesignTimeDbContextFactory` supplies them                         |
| The host needs secrets to build              | `IDesignTimeDbContextFactory`, so the host is never built           |
| Using Aspire                                 | Same — the tooling must not need the AppHost running                |
| Several `DbContext` types                    | `--context AppDbContext`, and one factory each                      |
| CI generates an idempotent script            | `dotnet ef migrations script --idempotent -o migrate.sql`           |

> **\[Note\]**
>
> **Keep design-time and runtime configuration separate on purpose.** The design-time factory should use a local, throwaway connection string and nothing else — no Key Vault, no environment-specific configuration. That way `migrations add` works on any developer machine and on any build agent, offline, with no credentials. Migrations are then *generated* as an artefact and *applied* by the pipeline as a separate gated step.

### LINQ meets SQL

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · LINQ meets SQL</p>

<a id="be-m155"></a>

#### The translation boundary

EF Core takes your expression tree, walks it, and emits SQL for everything it recognises. Since EF Core 3.0, anything it cannot translate is a **hard error** rather than a silent fall back to client evaluation — a deliberate breaking change that has saved enormous amounts of production CPU.

``` csharp
// Translates:
db.Orders.Where(o => o.Total > 100 && o.Status == OrderStatus.Open)
db.Orders.Where(o => o.Customer.Country == "VN")            // navigation -> JOIN
db.Orders.Where(o => o.Lines.Any(l => l.Sku == sku))        // -> EXISTS
db.Orders.Where(o => ids.Contains(o.Id))                     // -> IN
db.Orders.Where(o => o.Name.StartsWith("A"))                 // -> LIKE 'A%'
db.Orders.Where(o => o.CreatedAt >= from && o.CreatedAt < to)
db.Orders.Select(o => new OrderDto { Id = o.Id, Name = o.Customer.Name })

// Does NOT translate (throws):
db.Orders.Where(o => MyBusinessRule(o))
db.Orders.Where(o => o.Tags.Split(',').Length > 2)
db.Orders.Where(o => o.Total.ToString("C") == "...")
db.Orders.OrderBy(o => SomeLocalDictionary[o.Key])
```

The last stage of a query is the only place client evaluation is allowed: the final `Select` projection can call your code, because it runs on rows already fetched.

``` csharp
db.Orders.Select(o => new { o.Id, Label = Format(o) })   // ALSO throws in EF Core 3+
db.Orders.Select(o => new { o.Id, o.Total })             // fetch
         .AsEnumerable()
         .Select(x => new { x.Id, Label = Format(x) })   // then format in memory
```

> **\[Tip\]**
>
> **Make the boundary visible.** Log generated SQL in development: `options.LogTo(Console.WriteLine, LogLevel.Information).EnableSensitiveDataLogging()`. Reading the SQL your LINQ produces, once, for each query shape in your app, catches more problems than any amount of reasoning about it.

Goes further: [Query tuning: a practical checklist](#be-m179)

<a id="be-m156"></a>

#### N+1 and how to see it

One query to fetch a list, then one more per item to fetch a related entity. Correct results, catastrophic latency — 500 rows becomes 501 round trips, and network latency dominates everything.

``` csharp
var orders = db.Orders.ToList();                  // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);           // N lazy-load queries
```

**Fixes, in order of preference:**

``` csharp
// 1. Project only what you need - one query, no entity graph
db.Orders.Select(o => new { o.Id, CustomerName = o.Customer.Name }).ToList();

// 2. Eager load the navigation
db.Orders.Include(o => o.Customer).ToList();

// 3. Load the related set separately and join in memory (good for many-to-one fan-in)
var custs = db.Customers.Where(c => ids.Contains(c.Id)).ToDictionary(c => c.Id);
```

> **\[Tip\]**
>
> **Detect it, don't hunt it.**
>
> - Disable lazy loading entirely (don't install the proxies package). Then N+1 by navigation becomes a null reference or an empty collection at development time instead of a silent perf bug.
> - `options.ConfigureWarnings(w => w.Throw(CoreEventId.NavigationBaseIncludeIgnored))`.
> - MiniProfiler, or an EF interceptor that counts commands per request and logs a warning above a threshold. This is a 30-line class and it pays for itself immediately.

> **\[Trap\]**
>
> **Hidden N+1.** It also appears without lazy loading: a `foreach` that calls a repository method per item, a `Select` that awaits a per-item lookup, or an `AutoMapper` projection configured to resolve a navigation with a callback. The signature is always the same — a query inside a loop.

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)

First principles: [Where the time actually goes](12-04-scaling-and-performance.md#be-g705)

<a id="be-m157"></a>

#### Projection: Select before materialize

The single highest-leverage habit in EF work: **project to a DTO in the query**, don't fetch entities and map afterwards.

``` csharp
// Fetches every column of every table, tracks every entity
var orders = await db.Orders.Include(o => o.Customer).ToListAsync();
return orders.Select(o => new OrderDto(o.Id, o.Total, o.Customer.Name)).ToList();

// Fetches exactly three columns, no tracking, no entity instances
return await db.Orders
    .Select(o => new OrderDto(o.Id, o.Total, o.Customer.Name))
    .ToListAsync();
```

What you save: unfetched columns (often including large `nvarchar(max)` blobs), change-tracker snapshots, identity-map entries, and the entity objects themselves. On a wide table this is routinely a 5–10x reduction in bytes transferred.

``` csharp
// Projection also composes into related collections, generating one query:
db.Orders.Select(o => new {
    o.Id,
    Lines = o.Lines.Select(l => new { l.Sku, l.Qty }).ToList(),
    LineCount = o.Lines.Count()
});
```

> **\[Tip\]** **Share projections.** Define the mapping once as a static expression so every query uses the same shape and it still translates:
>
> ``` csharp
> public static readonly Expression<Func<Order, OrderDto>> ToDto =
>     o => new OrderDto(o.Id, o.Total, o.Customer.Name);
>
> db.Orders.Where(...).Select(Order.ToDto).ToListAsync();
> ```
>
> AutoMapper's `ProjectTo<OrderDto>()` does the same thing by building the expression from your profile — that is the only AutoMapper feature worth using with EF.

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)

<a id="be-m158"></a>

#### Tracking, no-tracking, and the identity map

By default EF *tracks* every entity it materialises: it stores a snapshot of the original values so `SaveChanges` can compute an UPDATE, and it keeps an identity map so the same key always returns the same object instance.

``` text
db.Orders.AsNoTracking().ToListAsync();          // read-only: no snapshot, no identity map
db.Orders.AsNoTrackingWithIdentityResolution();  // no snapshot, but dedupes references
options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);  // context default
```

Costs of tracking: a snapshot of every property (roughly doubling memory for the result set), a dictionary insert per entity, and a fixup pass to wire up navigations. On a 10,000-row read-only query this is a large, entirely wasted cost.

> **\[Tip\]**
>
> **Rule.** Every query that feeds a read path — an API GET, a report, a grid — should be `AsNoTracking` or, better, a projection (projections to non-entity types are automatically untracked). Reserve tracking for the entities you actually intend to modify, and load those by key.

> **\[Trap\]**
>
> **Trap.** A long-lived `DbContext` accumulates tracked entities forever — memory grows and `SaveChanges` gets slower because it walks every tracked entry. `DbContext` is designed to be short-lived: one per request, or one per unit of work. In a background loop, create a new scope per batch. This is a common failure mode in worker services and in older frameworks that hold a context in a singleton.

``` csharp
// In a long-running loop:
foreach (var batch in ids.Chunk(500)) {
    using var scope = factory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    // ... work on this batch, SaveChanges, dispose
}
```

Worked example: [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)

Goes further: [The change tracker and disconnected graphs](#be-m174)

<a id="be-m159"></a>

#### Include, split queries, and cartesian explosion

`Include` generates a JOIN. Two *collection* includes at the same level generate a cross product.

``` csharp
db.Orders.Include(o => o.Lines)      // 1 order x 10 lines
         .Include(o => o.Payments);  // x 5 payments = 50 rows for ONE order
// Every order column is duplicated 50 times over the wire.
```

``` csharp
// Split queries: one SELECT per collection, correlated by key
db.Orders.Include(o => o.Lines).Include(o => o.Payments).AsSplitQuery();
// or globally: options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
```

|                  | Single query (JOIN)                      | Split query                                   |
|------------------|------------------------------------------|-----------------------------------------------|
| Round trips      | 1                                        | 1 + one per collection                        |
| Data duplication | multiplicative                           | none                                          |
| Consistency      | one snapshot                             | separate queries — can see interleaved writes |
| Best for         | reference navigations, small collections | multiple or large collections                 |

``` csharp
// Filtered includes (EF Core 5+) - load only what you need
db.Orders.Include(o => o.Lines.Where(l => l.Qty > 0).OrderBy(l => l.Sku).Take(10));

// ThenInclude for depth
db.Orders.Include(o => o.Lines).ThenInclude(l => l.Product);
```

> **\[Tip\]**
>
> **Usually, don't `Include` at all.** A projection ([Projection: Select before materialize](#be-m157)) gives you the same data with none of the duplication and none of the tracking, and EF generates the appropriate joins or subqueries itself. Reach for `Include` when you need real tracked entities to modify.

> **\[Trap\]**
>
> **Split-query consistency.** Between the parent query and the child query another transaction can commit. If that matters, wrap both in an explicit transaction with an appropriate isolation level — or use snapshot isolation.

<a id="be-m160"></a>

#### Filtering, parameterization, and the plan cache

EF parameterises captured variables and inlines constants. This difference decides whether SQL Server can reuse a cached execution plan.

``` csharp
var status = "Open";
db.Orders.Where(o => o.Status == status);   // WHERE Status = @__status_0   (reusable plan)
db.Orders.Where(o => o.Status == "Open");   // WHERE Status = N'Open'       (constant)
```

Constants are not automatically bad — for a low-cardinality filter, a plan specialised to that literal can be better. But a query that inlines a high-cardinality value floods the plan cache with single-use plans.

``` csharp
// Force parameterisation or constants explicitly (EF Core 8+):
db.Orders.Where(o => o.Status == EF.Parameter(status));
db.Orders.Where(o => o.Status == EF.Constant(status));
```

**Parameter sniffing** is the other side of this. SQL Server compiles a plan for the first parameter value it sees and reuses it. If your data is skewed — one customer has 5 million rows, everyone else has 50 — the plan chosen for one is disastrous for the other. Symptoms: a query that is fast for months, then suddenly slow after a statistics update or a restart.

> **\[Tip\]** **Also:** avoid wrapping a column in a function in a `Where`. `Where(o => o.CreatedAt.Date == today)` becomes `CAST(CreatedAt AS date) = @p`, which is **non-sargable** — the index on `CreatedAt` cannot be seeked, only scanned. Write a range instead:
>
> ``` csharp
> .Where(o => o.CreatedAt >= today && o.CreatedAt < today.AddDays(1))
> ```
>
> Same for `o.Name.ToLower() == x` (use a case-insensitive collation or `EF.Functions.Like`), and `o.Name.Contains(x)` (leading wildcard — cannot seek; needs full-text search at scale).

Worked example: [Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)

<a id="be-m161"></a>

#### Pagination: OFFSET vs keyset

``` csharp
// Offset pagination - what Skip/Take generates
db.Orders.OrderBy(o => o.Id).Skip(page * size).Take(size);
// -> ORDER BY Id OFFSET @p ROWS FETCH NEXT @s ROWS ONLY
```

The server must scan and discard every skipped row. Page 1 is instant; page 10,000 reads a million rows to return twenty. Offset pagination also *skips and duplicates* rows when the underlying data changes between pages.

``` csharp
// Keyset (seek) pagination - constant cost at any depth
db.Orders.Where(o => o.Id > lastSeenId).OrderBy(o => o.Id).Take(size);

// Composite key, using EF Core 7+ tuple translation:
db.Orders.Where(o => ValueTuple.Create(o.CreatedAt, o.Id)
                   > ValueTuple.Create(lastDate, lastId))
         .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
         .Take(size);
```

|                        | Offset                      | Keyset                  |
|------------------------|-----------------------------|-------------------------|
| Cost at page N         | O(N × size)                 | O(size)                 |
| Jump to arbitrary page | yes                         | no — next/previous only |
| Total page count       | needs a separate `COUNT(*)` | usually omitted         |
| Stable under writes    | no                          | yes                     |

> **\[Trap\]**
>
> **Non-deterministic sort.** Any pagination whose `ORDER BY` is not unique can repeat or skip rows between pages, because SQL is free to break ties differently each time. Always append the primary key: `.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)`. This is [Comparison: IComparable, IComparer, stable ordering](02-03-types.md#be-m57)'s rule, and it is the most common production pagination bug.

> **\[Tip\]**
>
> Skip the total count when you can — `COUNT(*)` over a filtered set is often more expensive than the page itself. "Load more" or "next page" UI needs only `Take(size + 1)` to know whether another page exists.

Worked example: [Case: page 500 takes eight seconds](14-03-worked-cases-from-production.md#be-m296)

Goes further: [Pagination, filtering, and sorting contracts](08-02-api-design.md#be-a130)

<a id="be-m162"></a>

#### Aggregates and GroupBy translation

``` csharp
// Translates cleanly to GROUP BY:
db.Orders.GroupBy(o => o.CustomerId)
         .Select(g => new { CustomerId = g.Key, Total = g.Sum(o => o.Total), N = g.Count() });

// Composite key:
db.Orders.GroupBy(o => new { o.CustomerId, o.Status }).Select(g => new { g.Key, ... });

// HAVING:
.GroupBy(o => o.CustomerId).Where(g => g.Sum(o => o.Total) > 1000).Select(...)
```

> **\[Trap\]** **The big one:** selecting the *elements* of a group does not translate to SQL, because SQL `GROUP BY` produces aggregates, not row sets. EF Core 3+ throws; before that it silently pulled everything client-side.
>
> ``` csharp
> // Does NOT translate:
> db.Orders.GroupBy(o => o.CustomerId).Select(g => new { g.Key, Orders = g.ToList() });
> ```
>
> Fix by fetching the rows you need and grouping in memory, which is honest about what is happening:
>
> ``` csharp
> var rows = await db.Orders.Where(filter)
>     .Select(o => new { o.CustomerId, o.Id, o.Total }).ToListAsync();
> var grouped = rows.GroupBy(r => r.CustomerId);
> ```
>
> …or with a window function (EF Core 8+ exposes some via `EF.Functions`), or raw SQL.

``` csharp
// Aggregates ignore NULLs, and SUM over an empty set returns NULL, not 0.
// EF maps that to default(decimal) for a non-nullable projection, which is usually
// what you want - but be explicit when it matters:
.Select(g => new { Total = (decimal?)g.Sum(o => o.Total) ?? 0m })
```

<a id="be-m163"></a>

#### Any vs Count vs Exists in SQL

``` csharp
await db.Orders.AnyAsync(o => o.CustomerId == id);
// SELECT CASE WHEN EXISTS (SELECT 1 FROM Orders WHERE CustomerId = @id)
//             THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END

await db.Orders.CountAsync(o => o.CustomerId == id) > 0;
// SELECT COUNT(*) FROM Orders WHERE CustomerId = @id
```

`EXISTS` can stop at the first matching row; `COUNT(*)` must read all of them. On a customer with a million orders that is the difference between an index seek touching one page and a scan of the whole range.

``` csharp
// Correlated existence - translates to EXISTS, does not fetch the collection:
db.Customers.Where(c => c.Orders.Any(o => o.Status == "Open"));

// The negation translates to NOT EXISTS:
db.Customers.Where(c => !c.Orders.Any());
```

> **\[Tip\]**
>
> **Also useful:** `LongCountAsync` when a count may exceed `int.MaxValue`, and remember that `Count()` on a tracked, already-loaded navigation collection is an in-memory count — no query at all. Whether `customer.Orders.Count` hits the database depends entirely on whether that navigation is loaded, which is a good reason to avoid lazy loading so the answer is never ambiguous.

<a id="be-m164"></a>

#### IN lists, Contains, and parameter blowup

``` csharp
var ids = new[] { 1, 2, 3 };
db.Orders.Where(o => ids.Contains(o.Id));   // -> WHERE Id IN (@p0, @p1, @p2)
```

Two limits bite as the list grows:

- SQL Server allows a maximum of **2,100 parameters** per command. Exceed it and the command fails outright.
- Every distinct list length produces a different SQL string, so every length gets its own compiled plan. A thousand different lengths means a thousand cached plans.

**EF Core 8+** changed this: `Contains` over a parameterised collection now translates to `OPENJSON` on SQL Server, sending one JSON parameter regardless of length. That solves both problems at once.

``` csharp
// EF Core 8+ on SQL Server:
// WHERE Id IN (SELECT [value] FROM OPENJSON(@ids))
// Force the old behaviour if a plan regression appears:
db.Orders.Where(o => EF.Constant(ids).Contains(o.Id));
```

``` csharp
// On older EF or other providers - batch:
var results = new List<Order>();
foreach (var chunk in ids.Chunk(500))
    results.AddRange(await db.Orders.Where(o => chunk.Contains(o.Id)).ToListAsync());

// Or, for very large sets, use a temp table / TVP and join to it.
```

> **\[Trap\]**
>
> **Trap.** `Contains` over a list of *strings* can defeat an index if the parameter type does not match the column type — the classic being an `nvarchar` parameter against a `varchar` column, which forces an implicit conversion and a scan. Check `IsUnicode(false)` in your entity configuration when columns are non-Unicode.

<a id="be-m166"></a>

#### Bulk operations and batching

The classic EF pattern loads entities, mutates them, and saves — one round trip to read plus one statement per row to write.

``` csharp
// Old way: SELECT all matching rows, materialise, track, then N UPDATEs
var stale = db.Sessions.Where(s => s.LastSeen < cutoff).ToList();
foreach (var s in stale) s.IsActive = false;
await db.SaveChangesAsync();

// EF Core 7+: one statement, no materialisation, no tracking
await db.Sessions.Where(s => s.LastSeen < cutoff)
    .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsActive, false));

await db.Sessions.Where(s => s.LastSeen < cutoff).ExecuteDeleteAsync();
```

> **\[Trap\]**
>
> **These bypass the change tracker entirely.** No `SaveChanges` interceptors, no audit fields set by `SaveChanges` overrides, no domain events, no optimistic concurrency check, and already-tracked entities in the context become stale. They also execute immediately, outside any implicit transaction — wrap them in an explicit one if they must be atomic with other work.

**Inserts.** `SaveChanges` batches multiple inserts into a single command (up to a provider limit, ~42 statements for SQL Server by default). For thousands of rows, use `SqlBulkCopy` or the `EFCore.BulkExtensions` package — the difference is minutes versus seconds.

``` csharp
db.ChangeTracker.AutoDetectChangesEnabled = false;  // before a big AddRange loop
db.Orders.AddRange(batch);
await db.SaveChangesAsync();
db.ChangeTracker.AutoDetectChangesEnabled = true;
// AddRange already suppresses DetectChanges per item, but the flag matters
// if you are setting properties on many tracked entities.
```

Worked example: [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)

Goes further: [Interceptors and diagnostics](#be-m177)

<a id="be-m167"></a>

#### Raw SQL, FromSql, and stored procedures

``` csharp
// Interpolated - parameterised automatically, SAFE
var orders = db.Orders
    .FromSql($"SELECT * FROM Orders WHERE CustomerId = {id}")
    .Where(o => o.Total > 100)      // composable! becomes a subquery
    .ToList();

// Raw with explicit parameters
db.Orders.FromSqlRaw("SELECT * FROM Orders WHERE CustomerId = {0}", id);

// Non-entity scalar / arbitrary shape (EF Core 8+)
var totals = await db.Database.SqlQuery<decimal>(
    $"SELECT SUM(Total) FROM Orders WHERE Year = {year}").ToListAsync();

// Non-query
await db.Database.ExecuteSqlAsync($"EXEC sp_Rebuild @Id = {id}");
```

> **\[Trap\]**
>
> **SQL injection.** `FromSql` with an interpolated string is safe — EF converts each hole into a parameter. `FromSqlRaw($"... {id}")` is *not*, because the interpolation happens before EF sees it. The two look almost identical in a diff. Ban `FromSqlRaw` with string concatenation or interpolation in code review; there is no legitimate use.

**Composability rules.** The raw SQL must return all the columns the entity type expects, cannot use `ORDER BY` if you compose on top of it (it becomes a subquery), and for stored procedures composition is not supported at all — call `.AsEnumerable()` or `.ToList()` first.

> **\[Tip\]**
>
> **When to reach for raw SQL:** window functions, recursive CTEs, `MERGE`, hierarchical queries, hints, or a query EF generates badly. Do not treat it as failure — a handful of hand-written queries for the hot 5% of your workload plus EF everywhere else is a good architecture. Keep them in one place, near the entity config, so they are findable when the schema changes.

<a id="be-m170"></a>

#### Type mapping: CLR to SQL

| C#               | SQL Server         | PostgreSQL         | Note                                                       |
|------------------|--------------------|--------------------|------------------------------------------------------------|
| `int`            | `int`              | `integer`          |                                                            |
| `long`           | `bigint`           | `bigint`           |                                                            |
| `decimal`        | `decimal(18,2)`    | `numeric`          | **set precision explicitly**                               |
| `double`         | `float`            | `double precision` | never for money                                            |
| `bool`           | `bit`              | `boolean`          |                                                            |
| `string`         | `nvarchar(max)`    | `text`             | **set MaxLength**                                          |
| `DateTime`       | `datetime2`        | `timestamp`        |                                                            |
| `DateTimeOffset` | `datetimeoffset`   | `timestamptz`      | preferred                                                  |
| `DateOnly`       | `date`             | `date`             | EF Core 8+                                                 |
| `Guid`           | `uniqueidentifier` | `uuid`             | see [Guid: layout, versions, and index behaviour](02-03-types.md#be-m53) |
| `byte[]`         | `varbinary(max)`   | `bytea`            |                                                            |
| `enum`           | `int`              | `integer`          | store the int, not the name                                |

> **\[Trap\]**
>
> **The two defaults that cost real money:** an unconfigured `decimal` becomes `decimal(18,2)` and silently truncates anything needing more scale (unit prices, FX rates, tax rates); an unconfigured `string` becomes `nvarchar(max)`, which cannot be indexed, cannot be an included column efficiently, and is stored off-row. Configure both on every entity.

``` csharp
modelBuilder.Entity<Product>(e => {
    e.Property(p => p.Price).HasPrecision(18, 4);
    e.Property(p => p.Sku).HasMaxLength(50).IsUnicode(false).IsRequired();
});

// Value converters for anything else:
e.Property(p => p.Status).HasConversion<string>();          // enum as text
e.Property(p => p.Tags).HasConversion(
    v => string.Join(',', v), v => v.Split(',', StringSplitOptions.None).ToList());
```

> **\[Tip\]**
>
> **Converted properties lose translatability.** A `HasConversion`'d column cannot generally be filtered or ordered on in SQL in a way that respects the CLR semantics — EF may translate against the *stored* representation, which for an enum-as-string means alphabetical ordering, not the enum's numeric order.

Worked example: [Case: the audit trail that killed write throughput](14-03-worked-cases-from-production.md#be-m305)

Goes further: [Shadow properties, backing fields, and splitting](#be-m176)

### EF Core in depth

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · EF Core in depth</p>

<a id="be-m173"></a>

#### Inheritance mapping: TPH, TPT, TPC

A class hierarchy has to become tables somehow, and the three strategies have very different query costs.

``` csharp
abstract class Payment { public long Id; public decimal Amount; }
class CardPayment     : Payment { public string Last4 = ""; }
class TransferPayment : Payment { public string Iban = ""; }
```

|                     | TPH (default)             | TPT                     | TPC                     |
|---------------------|---------------------------|-------------------------|-------------------------|
| Tables              | one, with a discriminator | base + one per subtype  | one per concrete type   |
| Query one subtype   | filter on discriminator   | JOIN per level          | single table            |
| Query the base type | one scan                  | LEFT JOIN every subtype | `UNION ALL`             |
| Subtype columns     | must be nullable          | can be `NOT NULL`       | can be `NOT NULL`       |
| Adding a subtype    | widens the shared table   | a new table             | a new table             |
| Keys                | shared identity           | shared identity         | needs a shared sequence |

``` text
modelBuilder.Entity<Payment>()
    .HasDiscriminator<string>("PaymentType")          // name it; don't take the default
    .HasValue<CardPayment>("card")                    // and pin the VALUES - the default
    .HasValue<TransferPayment>("transfer");           // is the CLR type name, so a
                                                       // rename becomes a data migration

modelBuilder.Entity<Payment>().UseTpcMappingStrategy();   // or .UseTptMappingStrategy()
modelBuilder.HasSequence<long>("PaymentIds");             // TPC needs one shared sequence
```

> **\[Tip\]**
>
> **TPH is right far more often than its reputation suggests.** One table, one index, no joins — and the "wasted" nullable columns cost almost nothing in a modern engine, since NULLs are stored in a bitmap rather than as empty space. Choose TPT only when subtypes are genuinely disjoint and rarely queried polymorphically, and TPC when subtypes share almost nothing and are always queried concretely ([Pages, rows, and why everything is 8 KB](07-03-indexes-and-query-plans.md#be-m194)).

> **\[Trap\]**
>
> **TPT is the one that surprises people in production.** Querying the base type generates a `LEFT JOIN` to every subtype table, so adding the fifth payment type silently adds a fifth join to every existing query. Costs grow with the *number of subtypes*, not with the rows you asked for.

<a id="be-m174"></a>

#### The change tracker and disconnected graphs

Every tracked entity has a state, and `SaveChanges` is just a translation of those states into statements.

``` csharp
Detached  → not tracked at all
Unchanged → tracked, matches the snapshot   → no statement
Added     → INSERT
Modified  → UPDATE, of the changed properties only
Deleted   → DELETE

db.Entry(order).State;                          // read or set it directly
db.Entry(order).Property(o => o.Total).IsModified = true;
db.Entry(order).OriginalValues["Total"];        // what was loaded
db.ChangeTracker.Entries<Order>().Count();      // how much are we carrying?
```

**The disconnected case** — an entity that came from an API request, not from the database — is where most EF write bugs live, because `Attach` and `Update` mean different things.

``` text
db.Attach(order);   // whole graph -> Unchanged (or Added if the key is unset)
db.Update(order);   // whole graph -> Modified: UPDATE EVERY COLUMN, every entity
                    // - including columns the client never sent, which is how
                    //   a partial update silently blanks fields
                    // - and it defeats concurrency tokens, because the original
                    //   values are whatever the client claimed (§169)
```

``` csharp
// The safe pattern for an update from an API: load, then apply.
var order = await db.Orders.FindAsync([dto.Id], ct)
            ?? throw new NotFoundException();
db.Entry(order).Property(o => o.RowVersion).OriginalValue = dto.RowVersion;  // concurrency
order.Total  = dto.Total;
order.Status = dto.Status;
await db.SaveChangesAsync(ct);      // UPDATE only the columns that changed
```

``` csharp
// For a graph whose entities may be new or existing, decide per node:
db.ChangeTracker.TrackGraph(order, node => {
    var e = node.Entry;
    e.State = e.IsKeySet ? EntityState.Modified : EntityState.Added;
});
```

> **\[Trap\]**
>
> **`DetectChanges` is O(entities × properties)** and runs on `SaveChanges`, on most queries, and on `Entries()`. With 50 tracked entities it is invisible; with 50,000 it dominates the whole operation, and it gets worse as the loop progresses — the exact degradation profile in [Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300). Bound what you track: `AsNoTracking` for reads, a fresh context per batch for writes.

Recipe: [Recipe: a concurrent-safe update from an API](07-04-transactions-and-isolation.md#be-m292)

<a id="be-m175"></a>

#### Compiled queries, compiled models, and pooling

Three distinct caches sit between your LINQ and the database, and each can be pre-warmed.

| Cost                                          | Paid                   | Removed by                           |
|-----------------------------------------------|------------------------|--------------------------------------|
| Building the EF model from your configuration | once, at first use     | a compiled model                     |
| Translating an expression tree to SQL         | once per query *shape* | the query cache, or a compiled query |
| Hashing the tree to find it in the cache      | **every call**         | a compiled query                     |
| Constructing a `DbContext`                    | every request          | context pooling                      |

``` csharp
// Compiled query: skips the cache lookup entirely. Worth it on the hottest
// few queries in a high-throughput service - typically 10-30% off a small query.
private static readonly Func<AppDb, int, CancellationToken, Task<Order?>> GetById =
    EF.CompileAsyncQuery((AppDb db, int id, CancellationToken ct) =>
        db.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id));

var order = await GetById(db, id, ct);
```

``` bash
# Compiled model: for a large model (hundreds of entity types), first-use model
# building can be seconds. This moves it to build time.
dotnet ef dbcontext optimize --output-dir Generated --namespace MyApp.Generated

options.UseModel(MyAppDbContextModel.Instance);
```

``` csharp
// Context pooling: reuse the instance, reset the state. Removes allocation and
// initialisation per request.
builder.Services.AddDbContextPool<AppDb>(o => o.UseSqlServer(cs), poolSize: 128);
```

> **\[Trap\]**
>
> **Pooled contexts are reset, not recreated.** Any state you put in a field on your `DbContext` subclass — a tenant id, a current user, a cached lookup — survives into the next request that gets that instance. That is a cross-request data leak with the same shape as [Case: the cache that served the wrong tenant](14-03-worked-cases-from-production.md#be-m304). If your context needs per-request state, inject it via `AddDbContextPool<TContext, TFactory>` or do not pool.

> **\[Tip\]**
>
> **Compiled queries cannot be composed.** The whole point is that the tree is fixed, so a dynamic filter builder ([Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)) cannot use one. Reserve them for the handful of fixed, extremely hot queries — a lookup by primary key on a hot path — and measure, because on a query that takes 2 ms in the database, saving 40 µs of translation is noise.

<a id="be-m176"></a>

#### Shadow properties, backing fields, and splitting

The model does not have to mirror your classes. Several mapping tools let the database shape and the domain shape differ deliberately.

``` csharp
// SHADOW PROPERTY - a column with no CLR property. Good for audit columns
// that no domain code should touch.
modelBuilder.Entity<Order>().Property<DateTimeOffset>("LastModifiedUtc");
db.Entry(order).Property("LastModifiedUtc").CurrentValue = now;
db.Orders.OrderBy(o => EF.Property<DateTimeOffset>(o, "LastModifiedUtc"));
```

``` csharp
// BACKING FIELD - EF reads and writes the field, bypassing your property logic.
// This is what lets an aggregate expose a read-only collection.
public class Order {
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public void AddLine(OrderLine l) { /* invariants enforced here */ _lines.Add(l); }
}
modelBuilder.Entity<Order>()
    .Metadata.FindNavigation(nameof(Order.Lines))!
    .SetPropertyAccessMode(PropertyAccessMode.Field);
```

``` csharp
// TABLE SPLITTING - two entity types, one table. Lets you load the cheap part
// without the expensive part (§194).
modelBuilder.Entity<Order>().HasOne(o => o.Detail).WithOne()
    .HasForeignKey<OrderDetail>(d => d.Id);
modelBuilder.Entity<OrderDetail>().ToTable("Orders");     // same table

// ENTITY SPLITTING (EF 7+) - one entity across two tables, the reverse.
modelBuilder.Entity<Order>()
    .SplitToTable("OrderDetails", t => t.Property(o => o.LargeNotes));
```

``` csharp
// COMPLEX TYPES (EF 8+) - value objects with no identity, no tracking of
// their own, mapped inline. What owned types should have been.
modelBuilder.Entity<Order>().ComplexProperty(o => o.ShipTo);

// PRIMITIVE COLLECTIONS (EF 8+) - List<string> mapped to a JSON column,
// and Contains over it TRANSLATES (§209).
public List<string> Tags { get; set; } = [];
db.Orders.Where(o => o.Tags.Contains("urgent"));
```

> **\[Why it matters\]**
>
> **Why this matters for design.** These tools are what let a domain model stay clean without a hand-written data-mapping layer. Encapsulated collections, value objects, and audit columns invisible to the domain are all achievable in the mapping rather than by compromising the class — which is the difference between EF as an ORM and EF as an active-record framework you fight ([Value objects and parsing at the boundary](08-05-validation-and-errors-at-the-boundary.md#be-m266)).

Applied in: [Mapping a domain model to EF Core](12-06-domain-modeling-and-application-architecture.md#be-m259)

<a id="be-m177"></a>

#### Interceptors and diagnostics

``` csharp
// See the SQL for any query, without executing it - the fastest debugging tool
// EF has, and the basis for the regression gates in §226.
var sql = db.Orders.Where(o => o.TenantId == 1).Select(OrderRow.Projection).ToQueryString();
```

``` csharp
// Tag queries so they are findable in Query Store and in the DBA's inbox.
var rows = await db.Orders
    .TagWith("OrderList: tenant grid, page load")
    .Where(...).ToListAsync(ct);
// -> the SQL is emitted with that text as a leading comment.
```

``` csharp
// Command interceptor: metrics, slow-query logging, hints, retries.
public sealed class SlowQueryInterceptor(ILogger log) : DbCommandInterceptor {
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand cmd, CommandExecutedEventData e, DbDataReader r,
            CancellationToken ct = default) {
        if (e.Duration > TimeSpan.FromMilliseconds(500))
            log.LogWarning("Slow query {Ms}ms: {Sql}", e.Duration.TotalMilliseconds,
                           cmd.CommandText);
        return ValueTask.FromResult(r);
    }
}

// SaveChanges interceptor: audit stamps, domain events, outbox rows.
public sealed class AuditInterceptor(TimeProvider time) : SaveChangesInterceptor {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default) {
        foreach (var entry in e.Context!.ChangeTracker.Entries<IAuditable>())
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.ModifiedUtc = time.GetUtcNow();
        return ValueTask.FromResult(r);
    }
}

options.AddInterceptors(new SlowQueryInterceptor(log), new AuditInterceptor(time));
```

> **\[Trap\]**
>
> **Interceptors do not see `ExecuteUpdate`/`ExecuteDelete` as entity changes** — those bypass the change tracker entirely, so audit stamps, domain events, and outbox rows silently stop happening for them ([Bulk operations and batching](#be-m166)). Either stamp explicitly in the bulk call, or keep audited entities off the bulk path. This is the most common way an audit trail quietly develops holes.

> **\[Tip\]**
>
> **Development-only diagnostics.** `EnableDetailedErrors()` reports which property failed to materialise; `EnableSensitiveDataLogging()` puts parameter *values* in the log. The first is cheap and worth having in staging; the second must never reach production ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)).

<a id="be-m178"></a>

#### Keys, sequences, and value generation

``` csharp
e.Property(o => o.Id).ValueGeneratedOnAdd();          // database assigns on INSERT
e.Property(o => o.Id).ValueGeneratedNever();          // you assign it
e.Property(o => o.Version).ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

e.Property(o => o.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
e.Property(o => o.Total).HasComputedColumnSql("[Qty] * [Price]", stored: true);
```

| Strategy                     | Round trip before you know the id? | Batching  | Gaps              |
|------------------------------|------------------------------------|-----------|-------------------|
| `IDENTITY`                   | yes — the INSERT must run          | good      | on rollback       |
| Sequence                     | yes, or a separate fetch           | good      | on rollback       |
| Hi/Lo                        | no — a block is reserved           | excellent | large, on restart |
| Client-generated (`Guid v7`) | no                                 | excellent | none              |

``` csharp
modelBuilder.HasSequence<long>("OrderIds").StartsAt(1).IncrementsBy(50);
e.Property(o => o.Id).UseHiLo("OrderIds");
// EF now takes 50 ids at a time and hands them out in memory. Inserting 10,000
// orders costs 200 sequence fetches instead of 10,000 - and every id is known
// before SaveChanges, so the whole graph can be built client-side first.
```

> **\[Why it matters\]**
>
> **Client-side ids simplify more than performance.** Knowing an entity's identity before it is persisted lets you build a full object graph, emit domain events, write outbox rows, and return the id to the caller without a round trip — all in one transaction. That is why `Guid.CreateVersion7()` is now an attractive default: it gives client assignment without the index fragmentation that made v4 Guids a bad key ([Generating identifiers](03-09-specialised-structures-and-data-that-does-not-fit.md#be-m78), [B-tree anatomy: levels, splits, and fill factor](07-03-indexes-and-query-plans.md#be-m196)).

> **\[Trap\]**
>
> **Gaps are normal and are not a bug.** Identity and sequence values are consumed outside the transaction so that concurrent inserts do not serialise; a rollback does not return the number. Any requirement for a gapless sequence — invoice numbers in many jurisdictions — needs a separate, deliberately serialised counter table, and you should expect it to be a contention point.

<a id="be-m179"></a>

#### Query tuning: a practical checklist

In the order that finds problems fastest, with the fix beside it.

| Check                                            | Fix                                                                                     |
|--------------------------------------------------|-----------------------------------------------------------------------------------------|
| How many commands per request?                   | projection or `Include` — [Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)    |
| Is anything tracked that is never saved?         | `AsNoTracking`, or project to a DTO                                                     |
| Are all columns fetched?                         | `Select` the ones you need — [Pages, rows, and why everything is 8 KB](07-03-indexes-and-query-plans.md#be-m194)        |
| Two collection `Include`s?                       | `AsSplitQuery()` — [Include, split queries, and cartesian explosion](#be-m159)          |
| Is the result set unbounded?                     | `Take`, and keyset paging — [Case: page 500 takes eight seconds](14-03-worked-cases-from-production.md#be-m296)              |
| Is a column wrapped in a function?               | a half-open range — [Filtering, parameterization, and the plan cache](#be-m160)         |
| Does `ToQueryString()` look like what you meant? | read it — every new query shape, once                                                   |
| Does the plan seek or scan?                      | index, or covering index — [Seeks, scans, and residual predicates](07-03-indexes-and-query-plans.md#be-m200)            |
| Estimated vs actual rows?                        | statistics, or a sargable predicate — [Statistics and cardinality estimation](07-03-indexes-and-query-plans.md#be-d436) |
| Updating rows you loaded only to update?         | `ExecuteUpdate` — [Bulk operations and batching](#be-m166)                              |

``` csharp
// Two settings worth applying globally, once, in most applications:
options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
// -> reads are untracked by default; opt IN with .AsTracking() where you write.
//    Removes an entire class of accidental cost.

options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
// -> only if multi-collection Includes are common; note the consistency
//    trade-off (§159).
```

> **\[Why it matters\]**
>
> **Where the time actually is.** On a typical business query, EF's own overhead — translation, materialisation, tracking — is a small fraction; the database and the network dominate ([Where the time actually goes](12-04-scaling-and-performance.md#be-g705)). So compiled queries and pooling are worth single-digit percentages, while removing one round trip or one full scan is worth orders of magnitude. Work the top of this table before the bottom, and never start at `EF.CompileQuery`.

### The data layer as an architecture

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · The data layer as an architecture</p>

<a id="be-m260"></a>

#### Where data access code belongs

Before adding a repository, note what `DbContext` already is: `DbSet<T>` is a repository and `SaveChanges` is a unit of work. Wrapping them in `IRepository<T>` re-implements a pattern you already have, usually worse.

``` csharp
// The common anti-pattern: a generic repository that leaks IQueryable anyway.
public interface IRepository<T> {
    IQueryable<T> Query();              // callers can do anything - no abstraction gained
    Task<T?> GetByIdAsync(int id);
    Task AddAsync(T entity);
    Task SaveAsync();                    // a unit of work per repository - now transactions
}                                        // across two repositories are impossible
```

Three positions that actually work, chosen by what the codebase needs:

| Approach                                   | Good when                                  | Cost                                                  |
|--------------------------------------------|--------------------------------------------|-------------------------------------------------------|
| **DbContext directly** in a handler        | small to mid app; EF is a settled decision | query logic scattered; hard to unit-test in isolation |
| **Named query methods** on a focused class | **most cases**                             | a class per aggregate                                 |
| **Specification objects**                  | many reusable filter combinations          | indirection; specs must stay translatable             |

``` csharp
// Named methods: intention-revealing, individually testable, no leaked IQueryable.
public sealed class OrderQueries(AppDb db) {
    public Task<List<OrderRow>> RecentForTenant(int tenantId, int take, CancellationToken ct) =>
        db.Orders.AsNoTracking()
          .Where(o => o.TenantId == tenantId)
          .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
          .Take(take).Select(OrderRow.Projection).ToListAsync(ct);

    public Task<Order?> ForUpdate(long id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);
}
```

``` csharp
// Specification: a composable, translatable predicate + shaping, held as data.
public sealed record OrdersOpenSince(int TenantId, DateTimeOffset Since) : ISpec<Order> {
    public Expression<Func<Order, bool>> Criteria =>
        o => o.TenantId == TenantId && o.Status == OrderStatus.Open && o.CreatedAt >= Since;
}
// It must remain an Expression, not a Func - a Func forces client evaluation
// (§140).
```

> **\[Tip\]**
>
> **The distinction that matters more than the pattern:** separate the *read* path from the *write* path. Reads want projections, no tracking, and freedom to hand-write SQL. Writes want tracked aggregates, invariants, and transactions. Forcing both through one abstraction is what makes repositories painful; giving them different shapes is most of what CQRS actually means in practice ([Projection: Select before materialize](#be-m157)).

Worked example: [Case: the search filter that stopped using indexes](14-03-worked-cases-from-production.md#be-m298)

Goes further: [The modular monolith](12-01-monoliths-modules-and-services.md#be-a68)

Applied in: [Repositories, one per aggregate](12-06-domain-modeling-and-application-architecture.md#be-m257)

<a id="be-m261"></a>

#### Dapper, EF, and raw ADO.NET

|                                | EF Core                   | Dapper     | ADO.NET    |
|--------------------------------|---------------------------|------------|------------|
| Writes an entity graph         | yes, with change tracking | manual     | manual     |
| Composable queries             | LINQ, translated          | string SQL | string SQL |
| Control over the SQL           | partial                   | total      | total      |
| Materialisation overhead       | low, plus tracking if on  | very low   | none       |
| Migrations, model, conventions | yes                       | no         | no         |

``` csharp
// Dapper: hand-written SQL, direct materialisation, no model.
var rows = await conn.QueryAsync<OrderRow>(@"
    SELECT o.Id, c.Name AS Customer, o.Total, o.CreatedAt
    FROM Orders o JOIN Customers c ON c.Id = o.CustomerId
    WHERE o.TenantId = @tenantId AND o.CreatedAt >= @since
    ORDER BY o.CreatedAt DESC, o.Id DESC
    OFFSET 0 ROWS FETCH NEXT @take ROWS ONLY",
    new { tenantId, since, take });

// Several result sets in one round trip - something EF cannot express:
using var multi = await conn.QueryMultipleAsync(
    "SELECT * FROM Orders WHERE Id = @id; SELECT * FROM OrderLines WHERE OrderId = @id;",
    new { id });
var order = await multi.ReadSingleAsync<Order>();
var lines = (await multi.ReadAsync<OrderLine>()).ToList();
```

> **\[Tip\]**
>
> **They are not mutually exclusive, and mixing them is normal.** Both can share one `DbConnection` and one transaction: `db.Database.GetDbConnection()` and `GetDbTransaction()`. A common, healthy shape is EF for the write model and migrations, Dapper for the handful of read queries where you want the SQL exactly right — reports, dashboards, anything with a window function or a recursive CTE ([Raw SQL, FromSql, and stored procedures](#be-m167)).

> **\[Trap\]**
>
> **Dapper gives up EF's protections along with its overhead.** No change tracking means no automatic concurrency token check; no model means no `HasMaxLength` or `HasPrecision` guarding your parameters; and every query is a string, so a careless interpolation is an injection ([Handling sensitive data](10-06-privacy-compliance-and-the-supply-chain.md#be-m227)). Dapper parameterises anonymous-object properties correctly — the danger is exclusively in the SQL you concatenate yourself.

Worked example: [Case: the report whose totals were wrong](14-03-worked-cases-from-production.md#be-m295)

### EF Core in production

<p class="src">From <em>Backend Engineering</em> · Practical Answers · EF Core in production</p>

<p class="pd">Interceptors that stamp every table, soft delete that cannot be forgotten, and the query problems that show up only under real data volumes.</p>

<a id="be-q5"></a>

#### Interceptors: one rule applied to every table

An interceptor is EF’s middleware. Instead of remembering to set `CreatedAt` in forty services, you write it once and it applies to every entity that implements the marker interface — and it cannot be forgotten, because it runs inside `SaveChanges`.

``` csharp
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string?  CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string?  UpdatedBy { get; set; }
}

public sealed class AuditInterceptor(ICurrentUser user, TimeProvider clock)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData e, InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        Apply(e.Context);
        return base.SavingChangesAsync(e, result, ct);
    }

    // sync path too — otherwise SaveChanges() silently skips the stamping
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData e, InterceptionResult<int> result)
    {
        Apply(e.Context);
        return base.SavingChanges(e, result);
    }

    private void Apply(DbContext? db)
    {
        if (db is null) return;
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var entry in db.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = user.Id;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = user.Id;
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;                        // ↑ never let an update rewrite creation
            }
        }
    }
}

// registration — note the interceptor is SCOPED because ICurrentUser is
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<AppDb>((sp, o) => o
    .UseNpgsql(cs)
    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));
```

> **\[Trap\]**
>
> **Common trap.** Interceptors run on `SaveChanges` only. `ExecuteUpdateAsync`, `ExecuteDeleteAsync`, raw SQL and Dapper all bypass them entirely — so a bulk update leaves `UpdatedAt` stale and a bulk delete hard-deletes rows your soft-delete filter was supposed to protect. Decide deliberately which paths are allowed to bypass, and enforce it in review.

<a id="be-q6"></a>

#### Soft delete: IsDeleted that cannot be forgotten

The requirement is “deleting should hide the row, not destroy it”. Doing that with discipline alone fails: one `Remove()` anywhere and the row is gone, one forgotten `WHERE` and deleted rows appear in a report. Two mechanisms make it structural — an interceptor for the write side, a global query filter for the read side.

``` csharp
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

// ---------- WRITE side: turn DELETE into UPDATE ----------
public sealed class SoftDeleteInterceptor(ICurrentUser user, TimeProvider clock)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
    {
        foreach (var entry in e.Context!.ChangeTracker.Entries<ISoftDelete>()
                               .Where(x => x.State == EntityState.Deleted))
        {
            entry.State = EntityState.Modified;      // ← the whole trick
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = clock.GetUtcNow().UtcDateTime;
            entry.Entity.DeletedBy = user.Id;

            // cascade by hand: EF would have deleted children
            foreach (var nav in entry.Navigations.OfType<CollectionEntry>())
                foreach (var child in nav.CurrentValue?.OfType<ISoftDelete>() ?? [])
                { child.IsDeleted = true; child.DeletedAt = clock.GetUtcNow().UtcDateTime; }
        }
        return base.SavingChangesAsync(e, r, ct);
    }
}

// ---------- READ side: every query, automatically ----------
protected override void OnModelCreating(ModelBuilder b)
{
    foreach (var type in b.Model.GetEntityTypes()
             .Where(t => typeof(ISoftDelete).IsAssignableFrom(t.ClrType)))
    {
        var p = Expression.Parameter(type.ClrType, "e");
        var body = Expression.Equal(
            Expression.Property(p, nameof(ISoftDelete.IsDeleted)),
            Expression.Constant(false));
        b.Entity(type.ClrType).HasQueryFilter(Expression.Lambda(body, p));
    }
    // one loop → every current AND future soft-deletable table is covered
}
```

``` csharp
// reading deleted rows deliberately (admin, audit, restore)
var all = await db.Orders.IgnoreQueryFilters()
                        .Where(o => o.IsDeleted)
                        .ToListAsync(ct);

// index the filter column, or every query pays for it
b.Entity<Order>().HasIndex(o => new { o.TenantId, o.IsDeleted, o.PlacedAt });

// unique constraints must account for soft-deleted rows, or you can never
// re-use an email/SKU after deleting one — filtered index solves it:
b.Entity<User>().HasIndex(u => u.Email).IsUnique()
                .HasFilter("\"IsDeleted\" = false");
```

> **\[Trap\]**
>
> **Common trap.** Query filters do *not* apply through `Include` in the way people expect until you filter the child entity too, and a required relationship to a soft-deleted parent makes the child disappear from queries entirely — EF cannot materialise it. Also remember `ExecuteDelete` ignores all of this and really deletes. Decide whether soft delete is a domain concept (a `Cancelled` status) rather than an infrastructure trick — often it should be.

<a id="be-q7"></a>

#### N+1: the query problem you will meet most

``` csharp
// the setup — an innocent-looking loop
var orders = await db.Orders.Where(o => o.TenantId == id).ToListAsync(ct);  // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);   // + 1 query EACH — lazy loading
// 500 orders → 501 round trips → 4 seconds of pure network latency

// fix 1: Include — one query with a join
var withCustomers = await db.Orders
    .Where(o => o.TenantId == id)
    .Include(o => o.Customer)
    .ToListAsync(ct);

// fix 2: projection — better, because it also stops loading columns you
// never use, and it cannot lazy-load by accident
var summaries = await db.Orders
    .Where(o => o.TenantId == id)
    .Select(o => new OrderSummary(o.Id, o.PlacedAt, o.Customer.Name,
                                  o.Lines.Count, o.Lines.Sum(l => l.Subtotal)))
    .ToListAsync(ct);

// fix 3: when Includes multiply rows (cartesian explosion), split the query
var deep = await db.Orders
    .Include(o => o.Lines).Include(o => o.Shipments)
    .AsSplitQuery()                    // 3 small queries beat 1 huge product
    .ToListAsync(ct);
```

``` text
how to SEE it, rather than guess

1. log the SQL in development
   builder.Services.AddDbContext(o => o
       .UseNpgsql(cs)
       .LogTo(Console.WriteLine, LogLevel.Information)
       .EnableSensitiveDataLogging());     // dev only — logs parameter values

2. count queries in a test — the honest regression guard
   var count = 0;
   using var _ = new QueryCounterInterceptor(() => count++);
   await sut.GetDashboardAsync();
   count.Should().BeLessThan(5);

3. in production: the trace view. One request with 200 identical DB spans
   is an N+1, and it is visible in one glance.
```

> **\[Trap\]**
>
> **Common trap.** Disable lazy loading. It is the mechanism that turns a property access into a hidden database call, and it makes N+1 invisible at the call site. Without it the same code throws a null reference or returns an empty collection — loud, local, and fixed in a minute rather than found in a trace six months later.

<a id="be-q8"></a>

#### Splitting one big query into several, and joining in LINQ

Sometimes the right answer is the opposite of a join: fetch two or three narrow result sets separately and stitch them together in memory. This is a genuine optimisation — and a genuine foot-gun if applied without measuring.

| Prefer one joined query when                       | Prefer several + in-memory join when                              |
|----------------------------------------------------|-------------------------------------------------------------------|
| The join is selective and indexed                  | One `Include` multiplies rows (1 order × 50 lines × 20 shipments) |
| You need database-side paging on the joined result | The sides come from **different databases or services**           |
| The result set is small                            | One side is small and highly reusable (lookup/reference data)     |
| Consistency across the sets matters                | The extra round trips cost less than the duplicated bytes         |

``` csharp
// two narrow queries, then an in-memory join — note both are indexed lookups
var orders = await db.Orders
    .Where(o => o.TenantId == tenantId && o.PlacedAt >= from)
    .Select(o => new { o.Id, o.CustomerId, o.PlacedAt, o.Total })
    .ToListAsync(ct);

var customerIds = orders.Select(o => o.CustomerId).Distinct().ToArray();

var customers = await db.Customers
    .Where(c => customerIds.Contains(c.Id))       // → WHERE id = ANY(@ids)
    .Select(c => new { c.Id, c.Name, c.Country })
    .ToListAsync(ct);

var byId = customers.ToDictionary(c => c.Id);     // O(1) lookups

var report = orders
    .Select(o => new OrderRow(o.Id, o.PlacedAt, o.Total,
                              byId.TryGetValue(o.CustomerId, out var c)
                                  ? c.Name : "(unknown)"))
    .ToList();

// the LINQ join operator does the same thing if you prefer it explicit
var joined = from o in orders
             join c in customers on o.CustomerId equals c.Id into g
             from c in g.DefaultIfEmpty()          // ← left join
             select new OrderRow(o.Id, o.PlacedAt, o.Total, c?.Name ?? "(unknown)");
```

> **\[Trap\]**
>
> **Common trap.** `Contains` on a large array becomes a huge `IN (...)` list — parameter limits (2,100 on SQL Server) and plan-cache pollution follow. Above a few hundred ids, batch them, or send a table-valued parameter / `unnest` array instead. And remember: two queries are two snapshots. If the data can change between them, wrap them in a transaction or accept the inconsistency knowingly.

### DI, scopes and DbContext concurrency

<p class="src">From <em>Backend Engineering</em> · Practical Answers · DI, scopes and DbContext concurrency</p>

<p class="pd">The questions that come up the moment an application does more than one thing at a time: parallel work with a scoped DbContext, factories, custom scopes, and scoped services inside a singleton.</p>

<a id="be-q9"></a>

#### Task.WhenAll with one DbContext: why it throws, and the fix

This is the single most common .NET concurrency bug. `DbContext` is **not thread-safe** and allows exactly one operation at a time. Three parallel queries on the same instance produce: `A second operation was started on this context instance before a previous operation completed`.

``` csharp
// ✗ broken — one scoped DbContext, three concurrent operations
public async Task<Dashboard> GetAsync(CancellationToken ct)
{
    var orders    = _db.Orders.CountAsync(ct);          // started
    var customers = _db.Customers.CountAsync(ct);       // BOOM
    var revenue   = _db.Orders.SumAsync(o => o.Total, ct);
    await Task.WhenAll(orders, customers, revenue);
    return new Dashboard(orders.Result, customers.Result, revenue.Result);
}
```

``` csharp
// ✓ fix 1 — sequential. Boring, correct, and usually fast enough:
//   three indexed aggregates on one connection often beat three
//   connections' worth of setup.
var orders    = await _db.Orders.CountAsync(ct);
var customers = await _db.Customers.CountAsync(ct);
var revenue   = await _db.Orders.SumAsync(o => o.Total, ct);

// ✓ fix 2 — one query instead of three (best when they share a filter)
var stats = await _db.Orders
    .Where(o => o.TenantId == tenantId)
    .GroupBy(_ => 1)
    .Select(g => new { Count = g.Count(), Revenue = g.Sum(o => o.Total) })
    .SingleAsync(ct);

// ✓ fix 3 — genuinely parallel: a CONTEXT PER TASK, via the factory
public class DashboardService(IDbContextFactory<AppDb> factory)
{
    public async Task<Dashboard> GetAsync(CancellationToken ct)
    {
        var t1 = CountOrders(ct);
        var t2 = CountCustomers(ct);
        var t3 = SumRevenue(ct);
        await Task.WhenAll(t1, t2, t3);
        return new Dashboard(t1.Result, t2.Result, t3.Result);
    }

    private async Task<int> CountOrders(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Orders.CountAsync(ct);       // its own context AND
    }                                                 // its own connection
}
```

> **\[Trap\]**
>
> **Common trap.** Parallel database work is not free: each context takes a connection from the pool. Fan out to ten queries per request across fifty concurrent requests and you have asked for 500 connections from a pool that is probably capped at 100 — requests then queue on connection acquisition and the API gets *slower*. Parallelise deliberately, with a bounded degree, and measure.
