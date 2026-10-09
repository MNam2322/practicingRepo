---
id: "12-6"
number: "12.6"
title: "Domain modeling and application architecture"
part: 12
part_title: "Systems and architecture"
words: 11914
minutes: 52
---

# 12.6 Domain modeling and application architecture

> **Quick look.** Modeling the business in code: aggregates, value objects, domain events, CQRS and multi-tenancy.
>
> - An aggregate is one consistency boundary: one root, one transaction, small, and referencing others by id.
> - Value objects and private setters make invariants impossible to break, which deletes defensive code.
> - Anemic models are fine for simple CRUD; put rich models where the complexity actually is.
> - Domain events decouple side effects; never send email or call APIs inside the transaction.
> - CQRS separates read and write models and needs neither event sourcing nor a second database.
> - Mediator pipelines give one place for cross-cutting concerns; specifications name reusable query predicates.
> - Multi-tenancy: enforce the tenant in one place and test that tenant A can never read tenant B's data.
> - Model lifecycles as explicit state machines.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: An order aggregate that protects its total

Several services add lines to orders, and totals occasionally don't match the lines.

*Change only through the root*

```csharp
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyList<OrderLine> Lines => _lines;
    public Money Total { get; private set; } = Money.Zero("VND");

    public void AddLine(Sku sku, int qty, Money unitPrice)
    {
        if (qty is < 1 or > 9) throw new DomainException("Quantity must be 1–9");
        _lines.Add(new OrderLine(sku, qty, unitPrice));
        Total = _lines.Aggregate(Money.Zero(Total.Currency), (sum, l) => sum + l.UnitPrice * l.Qty);
    }
}
// EF Core maps the private field: builder.Navigation(o => o.Lines).HasField("_lines");
```

> **Lesson:** An aggregate root is the only way in, so invariants like "total matches lines" can't be broken.

#### Example 2: Tenant isolation you can test

A multi-tenant SaaS relies on developers adding `WHERE TenantId = ...`.

*A global filter plus a test that proves it*

```csharp
modelBuilder.Entity<Booking>().HasQueryFilter(b => b.TenantId == _tenant.Id);

[Fact]
public async Task Tenant_A_cannot_read_tenant_B()
{
    await SeedAsync(tenant: "A", bookingId: IdA);
    await SeedAsync(tenant: "B", bookingId: IdB);
    var client = Factory.CreateClientFor(tenant: "A");
    var res = await client.GetAsync($"/bookings/{IdB}");
    Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
}
// raw SQL, Dapper and ExecuteUpdate skip the filter: use row-level security for regulated data
```

> **Lesson:** Enforce the tenant in one place, and test the isolation explicitly; a cross-tenant leak is a breach.

### Architecture: Layers, CQRS, Multi-tenancy

<p class="src">From <em>.NET Backend Deep Dive</em> · Architecture: Layers, CQRS, Multi-tenancy</p>

<a id="dn-132"></a>

#### CQRS — and when not to

Separating read and write models is genuinely valuable when their shapes diverge:

```csharp
// Write: full aggregate, invariants enforced, tracked by EF
public async Task Handle(PlaceOrder cmd) { ... }

// Read: flat projection straight to a DTO, no tracking, possibly a denormalized table
public async Task<OrderSummaryDto> Handle(GetOrderSummary q) =>
    await _db.Orders.Where(...).Select(o => new OrderSummaryDto(...)).FirstAsync();
```

CQRS does **not** require event sourcing, separate databases, or a message bus. Those are separate, much more expensive decisions. Adopting the full stack for a CRUD app buys eventual consistency and debugging pain in exchange for nothing.

The cheap, near-universal version: **write through the domain model, read through projections.** That alone captures most of the benefit.

<a id="dn-133"></a>

#### Multi-tenancy

| Model | Isolation | Cost | Noisy-neighbor risk |
|---|---|---|---|
| Database per tenant | Strongest | Highest; migrations across N databases | Low |
| Schema per tenant | Good | Medium | Medium |
| **Shared table + TenantId** | Weakest — one bug leaks data | Lowest | Highest |

For the shared model, never rely on developers remembering the filter:

```csharp
// Automatic tenant filter (same mechanism as soft delete, Part 2.1)
mb.Entity<Order>().HasQueryFilter(o => o.TenantId == _tenant.Id);

// And index it FIRST in every composite index
mb.Entity<Order>().HasIndex(o => new { o.TenantId, o.CreatedAt });
```

Test the isolation explicitly: a test that authenticates as tenant A and asserts it cannot read tenant B's row is worth more than any amount of code review.

Remember that query filters **don't apply to raw SQL or `ExecuteDelete`** — the same trap as soft delete, but here the consequence is a cross-tenant data leak.

### Validation & Domain Modeling

<p class="src">From <em>.NET Backend Deep Dive</em> · Validation & Domain Modeling</p>

<a id="dn-148"></a>

#### Aggregates

An **aggregate** is a consistency boundary: one root, one transaction, one set of invariants.

```csharp
public class Order              // the aggregate root
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines;   // no public mutation
    public OrderStatus Status { get; private set; }

    public void AddLine(ProductId product, int qty, Money price)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("Cannot modify a submitted order");
        if (_lines.Count >= 100)
            throw new DomainException("Line limit reached");
        _lines.Add(new OrderLine(product, qty, price));
    }
}
```

Three practical rules: **reference other aggregates by id, not by navigation**; **one aggregate per transaction** (cross-aggregate consistency is eventual, via events); and **keep aggregates small** — a root holding ten thousand children is a locking and loading disaster.

Note this is in direct tension with EF convenience (public setters, bidirectional navigations). Use backing fields and `private set`, and configure EF to use the field:

```csharp
mb.Entity<Order>().Metadata
  .FindNavigation(nameof(Order.Lines))!
  .SetPropertyAccessMode(PropertyAccessMode.Field);
```

---

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a64"></a>

#### CQRS and the mediator pattern

Command Query Responsibility Segregation splits writes (commands, which change state and return little) from reads (queries, which change nothing and are shaped for the screen). At its lightest it is just two sets of classes; at its heaviest it is two databases kept in sync by events.

``` csharp
public record PlaceOrder(Guid CustomerId, IReadOnlyList<Line> Lines) : IRequest<Guid>;

public class PlaceOrderHandler(IOrderRepository repo, IUnitOfWork uow)
    : IRequestHandler<PlaceOrder, Guid>
{
    public async Task<Guid> Handle(PlaceOrder cmd, CancellationToken ct)
    {
        var order = Order.Place(cmd.CustomerId, cmd.Lines);   // domain rules live here
        await repo.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        return order.Id;
    }
}

// the controller becomes one line
[HttpPost] public async Task<Guid> Post(PlaceOrder cmd) => await _mediator.Send(cmd);
```

The real win is the *pipeline*: validation, logging, transactions, retries and caching become behaviours applied to every handler instead of copy-pasted into each one. Note that MediatR moved to a commercial licence in 2025 — alternatives include Wolverine, Brighter, or a hand-rolled dispatcher (about 40 lines).

**CQRS earns its keep when**

- Read and write models genuinely differ
- Reads vastly outnumber writes and need their own scaling
- You want cross-cutting behaviours in one place

**Skip it when**

- The model is a table with a form on it
- The team is small and the domain is thin
- You would just be renaming `Service` to `Handler`

###### Worked example

The pipeline is the actual payoff, and it is easy to underestimate from a diagram. Here is what six behaviours give you, applied to every single handler in the application, written once:

``` csharp
public class LoggingBehaviour<TReq, TRes>(ILogger<LoggingBehaviour<TReq, TRes>> log)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        using var scope = log.BeginScope(new Dictionary<string, object>
        {
            ["RequestType"] = typeof(TReq).Name,
            ["RequestId"]   = Guid.NewGuid()
        });
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            log.LogInformation("{Request} handled in {Ms}ms", typeof(TReq).Name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "{Request} failed after {Ms}ms", typeof(TReq).Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

public class ValidationBehaviour<TReq, TRes>(IEnumerable<IValidator<TReq>> validators)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(req, ct))))
            .SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();
    }
}

public class TransactionBehaviour<TReq, TRes>(AppDb db)
    : IPipelineBehavior<TReq, TRes> where TReq : ICommand<TRes>     // commands only
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await next();   // already in one

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await next();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }
}
```

Registered in order, these give every command in the system: a correlated log scope, timing, validation before any work, a transaction that also covers the outbox insert, retry on transient database failures, and structured error logging. The handlers themselves contain nothing but business logic — no try/catch, no `SaveChanges`, no validation calls.

> **\[Note\]**
>
> **The generic constraint is doing real work.** `where TReq : ICommand<TRes>` means the transaction behaviour is not even instantiated for queries. That is compile-time policy selection, not a runtime `if`, and it is the reason the pattern stays cheap as behaviours accumulate.

###### A dispatcher in forty lines, if you would rather not take the dependency

``` csharp
public interface IRequest<out TRes>;
public interface IHandler<in TReq, TRes> where TReq : IRequest<TRes>
{
    Task<TRes> Handle(TReq request, CancellationToken ct);
}

public sealed class Dispatcher(IServiceProvider sp)
{
    public Task<TRes> Send<TRes>(IRequest<TRes> request, CancellationToken ct = default)
    {
        var handlerType = typeof(IHandler<,>).MakeGenericType(request.GetType(), typeof(TRes));
        dynamic handler = sp.GetRequiredService(handlerType);
        return handler.Handle((dynamic)request, ct);
    }
}

// registration by convention
services.Scan(s => s.FromAssemblyOf<Dispatcher>()
    .AddClasses(c => c.AssignableTo(typeof(IHandler<,>)))
    .AsImplementedInterfaces().WithScopedLifetime());
```

Add behaviours by resolving `IEnumerable<IPipelineBehavior<,>>` and folding them around the handler call. That is another twenty lines. Given the licensing changes in the ecosystem, understanding that a mediator is genuinely this small is worth more than knowing one library's API.

<a id="be-a65"></a>

#### Domain-driven design, the useful parts

Ignore the taxonomy for a moment; four ideas do most of the work.

| Concept             | Meaning                               | In code                                      |
|---------------------|---------------------------------------|----------------------------------------------|
| Ubiquitous language | Code uses the words the business uses | `Shipment.Dispatch()`, not `UpdateStatus(3)` |
| Entity              | Identity persists through change      | Has an `Id`; equality by Id                  |
| Value object        | Defined entirely by its values        | `record Money`, immutable                    |
| Aggregate           | Consistency boundary, one root        | Save the whole root in one transaction       |
| Bounded context     | One model, one meaning, one team      | One service, one database                    |

``` csharp
public class Order            // aggregate root
{
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyList<OrderLine> Lines => _lines;   // no public mutation
    public OrderStatus Status { get; private set; }

    public void AddLine(Sku sku, int qty)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("Cannot modify a submitted order.");
        _lines.Add(new OrderLine(sku, qty));
    }
}
```

> **\[Why it matters\]** Bounded contexts are how you decide microservice boundaries later. Get this wrong and you build a distributed monolith where every feature needs three services deployed together.

###### The aggregate boundary is the transaction boundary

``` text
  the single most useful DDD idea, and the one most often skipped

    an AGGREGATE is a cluster of objects changed TOGETHER, atomically.
    the AGGREGATE ROOT is the only thing outside code may reference.

    rules
      1  one transaction modifies ONE aggregate
      2  references BETWEEN aggregates are by ID, never by object
      3  invariants that must always hold live INSIDE one aggregate
      4  anything crossing aggregates is eventually consistent

  why rule 1 matters operationally: the aggregate is your unit of locking
  and of contention. Too LARGE, and unrelated work serialises. Too SMALL,
  and it cannot enforce a rule that spans it.

  worked example
    "an order's total must equal the sum of its lines"
      -> lines live INSIDE the Order aggregate. One transaction.

    "a customer may not exceed their credit limit"
      -> Customer and Order are separate aggregates.
         accept eventual consistency and compensate, or enforce it in the
         DATABASE with a constraint. Do NOT load every order to check.
```

``` csharp
public class Order                                  // aggregate root
{
    private readonly List<OrderLine> _lines = [];    // owned; no outside reference
    public IReadOnlyList<OrderLine> Lines => _lines; // read-only outward
    public CustomerId CustomerId { get; }            // ANOTHER aggregate: by ID
    public Money Total { get; private set; }         // maintained, not settable

    public void AddLine(ProductId product, int qty, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("Cannot modify a submitted order");
        _lines.Add(new OrderLine(product, qty, unitPrice));
        Total = Money.Sum(_lines.Select(l => l.LineTotal));   // invariant, always
    }
}
// note what is absent: no Customer object, no repository, no DbContext.
// this can be unit-tested with no infrastructure whatsoever.
```

| Worth adopting                                      | Usually skip                                                                      |
|-----------------------------------------------------|-----------------------------------------------------------------------------------|
| Ubiquitous language — the business's words, in code | Full Event sourcing — see Terminology"\>event sourcing, unless you need the audit |
| Aggregate = transaction boundary                    | CQRS with separate read databases, until reads actually hurt                      |
| Value objects for `Money`, `Email`, `DateRange`     | A repository interface per entity, mechanically                                   |
| Domain events for cross-aggregate effects           | Layering ceremony with no domain logic inside it                                  |
| Bounded contexts as service boundaries              | Applying all of it to a CRUD admin screen                                         |

The honest scope: DDD earns its cost where the *rules* are complex. For a table-maintenance screen the rule is “save what was typed”, and domain modelling is pure overhead. Applying it selectively — rich modelling in the two or three genuinely complex areas, plain CRUD everywhere else — is a legitimate and underused choice.

<a id="be-a69"></a>

#### Multi-tenancy

| Model                    | Isolation    | Cost                                    | Fits                                         |
|--------------------------|--------------|-----------------------------------------|----------------------------------------------|
| Database per tenant      | Strongest    | Highest — N migrations, N backups       | Enterprise, regulated, few large tenants     |
| Schema per tenant        | Strong       | Medium; connection pooling gets awkward | Dozens of tenants                            |
| Shared tables + TenantId | Logical only | Lowest                                  | SaaS with many small tenants                 |
| Hybrid                   | Per tier     | Complex but pragmatic                   | Big customers isolated, the long tail shared |

``` csharp
// resolve the tenant once, per request
public sealed class TenantContext { public string TenantId { get; set; } = ""; }

app.Use(async (ctx, next) =>
{
    var tenant = ctx.User.FindFirst("tenant_id")?.Value
              ?? throw new UnauthorizedAccessException("No tenant claim.");
    ctx.RequestServices.GetRequiredService<TenantContext>().TenantId = tenant;
    await next(ctx);
});

// then make it impossible to forget
public class AppDb(DbContextOptions o, TenantContext tenant) : DbContext(o)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>().HasQueryFilter(x => x.TenantId == tenant.TenantId);
        b.Entity<Invoice>().HasQueryFilter(x => x.TenantId == tenant.TenantId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<ITenantOwned>()
                                       .Where(e => e.State == EntityState.Added))
            e.Entity.TenantId = tenant.TenantId;
        return base.SaveChangesAsync(ct);
    }
}
```

> **\[Trap\]**
>
> **Common trap.** Global query filters are bypassed by `IgnoreQueryFilters()`, raw SQL, `FromSqlRaw`, and `Find()` on a cached entity. Write a test that seeds two tenants and asserts every repository method returns only one — a cross-tenant leak is a reportable data breach, not a bug.

Other things that need tenant scoping and are routinely forgotten: cache keys, background jobs, file storage paths, search index documents, log properties, and rate-limit partitions.

###### Worked example

A cross-tenant leak, and the test that would have caught it. This is the single highest-consequence bug class in multi-tenant software, and it is almost always one missing filter.

``` csharp
// the global filter is in place and works for every LINQ query
b.Entity<Invoice>().HasQueryFilter(i => i.TenantId == _tenant.TenantId);

// ...and then, six months later, someone adds a reporting endpoint
var rows = await db.Database
    .SqlQuery<InvoiceRow>($"SELECT Id, Total, DueDate FROM Invoices WHERE DueDate < {cutoff}")
    .ToListAsync(ct);
// raw SQL BYPASSES the global filter entirely. Every tenant's invoices, returned to one tenant.
```

``` csharp
// the test that makes this impossible to ship unnoticed
public class TenantIsolationTests : IClassFixture<ApiFactory>
{
    [Theory]
    [MemberData(nameof(AllReadEndpoints))]      // enumerate every GET endpoint via the endpoint data source
    public async Task Endpoint_never_returns_another_tenants_data(string route)
    {
        await Seed.TenantAsync("tenant-a", records: 3);
        await Seed.TenantAsync("tenant-b", records: 3);

        var client = _factory.CreateClientFor("tenant-a");
        var body = await client.GetStringAsync(route);

        Assert.DoesNotContain("tenant-b", body);
        Assert.DoesNotContain(Seed.KnownIdFor("tenant-b"), body);
    }
}
```

Enumerating the endpoints from `EndpointDataSource` rather than listing them by hand is the important detail: a new endpoint is covered automatically the day it is added, which is exactly when nobody remembers to write the isolation test.

###### The list of things that also need tenant scoping

| Surface            | Leak looks like                                            | Fix                                                                         |
|--------------------|------------------------------------------------------------|-----------------------------------------------------------------------------|
| Cache keys         | Tenant B sees Tenant A's cached price list                 | `$"{tenant}:prices:{id}"` — never a bare key                                |
| Background jobs    | A nightly job runs with no tenant and processes everything | Enqueue one job per tenant, with the tenant in the payload                  |
| Blob storage paths | A predictable URL exposes another tenant's document        | Tenant-prefixed container or path, plus authorization on access             |
| Search index       | One index, no filter, all results                          | Tenant as a mandatory filter clause, or an index per tenant                 |
| Log properties     | Support cannot tell whose error it is                      | Tenant id in the log scope on every request                                 |
| Rate limits        | One noisy tenant throttles everyone                        | Partition the limiter by tenant                                             |
| Connection pools   | Database-per-tenant exhausts pool count                    | Pool per tenant with a bounded LRU, or a proxy                              |
| Migrations         | One tenant is on an old schema                             | A migration runner that iterates every tenant and reports per-tenant status |

``` csharp
// rate limiting, partitioned properly
builder.Services.AddRateLimiter(o => o.GlobalLimiter =
    PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var tenant = ctx.User.FindFirst("tenant_id")?.Value ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(tenant, _ => new()
        {
            TokenLimit = 1000, TokensPerPeriod = 1000,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0
        });
    }));
```

### Domain Patterns

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Domain Patterns</p>

<p class="pd">Patterns for modelling a business domain. Where the GoF catalogue is about mechanics, these are about meaning — which objects exist, what they own, and where the rules live.</p>

<a id="be-h759"></a>

#### Entity and Value Object

The most foundational distinction in domain modelling, and the one that decides how every other pattern here behaves. It is a question about **identity**.

``` text
  ENTITY            has an IDENTITY that persists through change

    a Customer whose name, address and email all change is STILL THAT
    CUSTOMER. Identity is not derived from the values; it is assigned.

      two entities with identical fields are DIFFERENT if their ids differ
      one entity with different fields over time is THE SAME entity

  VALUE OBJECT      is defined ENTIRELY by its values

    Money(10, EUR) is Money(10, EUR). There is no "which ten euros".
    changing a value produces a DIFFERENT value object, not a modified one.

      two value objects with identical fields ARE the same
      they are immutable, because "changing" one makes no sense

  the test: "if every field changed, would it still be the same thing?"
             yes -> entity.   no -> value object.
```

``` csharp
// entity: identity, mutable state, behaviour that enforces its own rules
public class Customer
{
    public CustomerId Id { get; }                      // identity, never changes
    public string Name { get; private set; }           // may change
    public EmailAddress Email { get; private set; }    // a VALUE OBJECT

    public void ChangeEmail(EmailAddress newEmail)
    {
        if (newEmail == Email) return;
        Email = newEmail;
        Raise(new CustomerEmailChanged(Id, newEmail));
    }
}

// value object: immutable, self-validating, compared by value
public readonly record struct EmailAddress
{
    public string Value { get; }
    public EmailAddress(string value)
    {
        if (!IsValid(value)) throw new ArgumentException($"Invalid email: {value}");
        Value = value.Trim().ToLowerInvariant();       // NORMALISED on construction
    }
}
// once constructed, an EmailAddress is VALID. Nothing downstream re-checks.
```

| Model as a value object   | Rather than                                     |
|---------------------------|-------------------------------------------------|
| `Money(Amount, Currency)` | `decimal` — which loses the currency            |
| `EmailAddress`            | `string` — validated at ten call sites          |
| `DateRange(From, To)`     | Two loose dates that can be the wrong way round |
| `Quantity`                | `int` — which can be negative                   |
| `CustomerId`              | `Guid` — interchangeable with every other id    |
| `Percentage`              | `decimal` — is 0.2 or 20 meant?                 |

> **\[Note\]**
>
> **The highest-return refactor in domain code.** Replacing primitive parameters with value objects — often called fixing *primitive obsession* — moves validation to construction, makes `Transfer(customerId, orderId)` impossible to call with the arguments swapped, and makes the type signature carry meaning. It is a small change with a large effect on how many bugs are possible at all.

###### Persisting value objects, and the strongly-typed id trade

``` csharp
// EF Core: a value object becomes columns on the owner's table
modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress, a =>
{
    a.Property(p => p.Street).HasColumnName("shipping_street");
    a.Property(p => p.City).HasColumnName("shipping_city");
});
// one table, no join. The value object has no identity, so it needs no key.

// a single-value wrapper uses a CONVERSION instead
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

modelBuilder.Entity<Order>()
    .Property(o => o.Id)
    .HasConversion(id => id.Value, v => new OrderId(v));

// or globally, so you declare it once
configurationBuilder.Properties<OrderId>()
    .HaveConversion<OrderIdConverter>();
```

``` text
  STRONGLY-TYPED IDs - what you gain and what you pay

  GAIN
    Transfer(CustomerId from, OrderId to)   <- cannot be called with the
                                               arguments swapped. The
                                               compiler refuses.
    a repository cannot be handed the wrong kind of Guid
    the type documents what the value IS

  PAY
    a converter per id type (or one global convention)
    JSON serialisation needs a converter too, or ids serialise as
      { "value": "..." } instead of a bare string
    model binding in ASP.NET Core needs a TypeConverter, or route
      parameters will not bind
    LINQ queries mostly work, but some translations need help
    third-party libraries that expect Guid need unwrapping

  THE HONEST ASSESSMENT
    worth it for the three or four ids that flow through everything and
    get confused. Applying it to all forty entity types in a CRUD
    application is ceremony - the bug it prevents does not occur where
    ids never travel together.
```

| Value object shape                 | EF Core mapping                              |
|------------------------------------|----------------------------------------------|
| Several properties (`Address`)     | `OwnsOne` — columns on the owner             |
| A single wrapped value (`OrderId`) | `HasConversion`                              |
| A collection of value objects      | `OwnsMany` — a separate table, or `ToJson()` |
| Complex and variable               | `OwnsOne(...).ToJson()` — one `jsonb` column |
| Needs querying and indexing        | Real columns, not JSON                       |

<a id="be-h760"></a>

#### Aggregate and Aggregate Root

**Intent:** a cluster of entities and value objects treated as one unit for changes, with a single entry point. The most useful idea in domain-driven design, and the one most often skipped.

``` text
  the rules, and each one earns its place

  1  ONE TRANSACTION MODIFIES ONE AGGREGATE
       the aggregate is your unit of locking and of contention.

  2  REFERENCES BETWEEN AGGREGATES ARE BY ID, NEVER BY OBJECT
       Order holds a CustomerId, not a Customer. Otherwise loading an
       order drags in half the database.

  3  INVARIANTS THAT MUST ALWAYS HOLD LIVE INSIDE ONE AGGREGATE
       "the order total equals the sum of its lines" - so lines are
       INSIDE Order, and only Order may modify them.

  4  ANYTHING CROSSING AGGREGATES IS EVENTUALLY CONSISTENT
       and if that is unacceptable, the boundary is in the wrong place.

  sizing it is the whole skill:
    TOO LARGE  -> unrelated work serialises on one lock
    TOO SMALL  -> a rule spanning two aggregates cannot be enforced
```

``` csharp
public class Order : Entity                             // AGGREGATE ROOT
{
    private readonly List<OrderLine> _lines = [];        // owned, encapsulated
    public IReadOnlyList<OrderLine> Lines => _lines;     // read-only outward
    public CustomerId CustomerId { get; }                // ANOTHER aggregate: by ID
    public Money Total { get; private set; }             // maintained, not settable

    public void AddLine(ProductId product, Quantity qty, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("Cannot modify a submitted order");

        _lines.Add(new OrderLine(product, qty, unitPrice));
        Total = Money.Sum(_lines.Select(l => l.LineTotal));   // INVARIANT, always
    }
}
// note what is absent: no Customer object, no repository, no DbContext.
// this class can be unit-tested with no infrastructure whatsoever, and
// there is no way to add a line without the total being recalculated.
```

``` text
  SIZING, worked

    "an order's total must equal the sum of its lines"
      -> one invariant, one aggregate. Lines live inside Order.

    "a customer may not exceed their credit limit"
      -> Customer and Order are SEPARATE aggregates.
         you cannot enforce this in one transaction without loading
         every order the customer has.

         the options, in order of preference:
           1  a DATABASE CONSTRAINT or an atomic conditional update
           2  accept eventual consistency, and compensate
           3  merge the aggregates - and accept the contention

         what you must NOT do is load all orders into memory to check,
         which is the naive answer and does not scale past a few hundred.
```

###### Sizing one, worked through

``` text
  a real decision: does an Order own its Payments?

  ARGUMENT FOR (one aggregate)
    "an order's paid amount must never exceed its total"
    -> that is an INVARIANT SPANNING BOTH. Inside one aggregate it is
       enforceable in one transaction.

  ARGUMENT AGAINST (two aggregates)
    a subscription order accumulates payments for YEARS.
    loading the order to add one payment loads every historic payment.
    two concurrent payments contend on the SAME aggregate lock.

  THE RESOLUTION - and this is the general shape:
    ask whether the invariant must be IMMEDIATE or can be CHECKED.

      immediate  -> one aggregate, and accept the contention
      checkable  -> two aggregates, and enforce it with a DATABASE
                    CONSTRAINT or an atomic conditional update:

        UPDATE orders SET paid = paid + :amt
        WHERE id = :id AND paid + :amt <= total;
        -- 0 rows affected = would exceed. No aggregate loaded at all.

  the database can enforce cross-aggregate invariants that the domain
  model cannot - and using it for that is not a failure of the model.
```

| Symptom                                        | The aggregate is          | Fix                                         |
|------------------------------------------------|---------------------------|---------------------------------------------|
| Loading it fetches hundreds of rows            | Too large                 | Split; reference by id                      |
| Concurrent updates constantly conflict         | Too large                 | Split along the contention line             |
| A rule needs two aggregates in one transaction | Too small                 | Merge, or enforce in the database           |
| A collection inside it grows without bound     | Too large                 | That collection is its own aggregate        |
| You load it only to read one field             | Too large for that use    | Project instead — do not load the aggregate |
| It has one entity and no invariants            | Possibly not an aggregate | It may just be a table                      |

A useful sizing heuristic: **an aggregate should be loadable and savable in one transaction without you worrying about its size**. If you find yourself adding pagination *inside* an aggregate, or selectively loading parts of it, the boundary is wrong — that collection is a separate aggregate referenced by id.

<a id="be-h761"></a>

#### Repository

**Intent:** mediate between the domain and data mapping, presenting a collection-like interface for accessing aggregates. It is also the pattern with the most genuine disagreement about whether it is worth having on top of an ORM.

``` csharp
// the domain defines what it NEEDS, in its own vocabulary
namespace Acme.Domain;
public interface IOrderRepository
{
    Task<Order?> ByIdAsync(OrderId id, CancellationToken ct);
    Task<IReadOnlyList<Order>> OpenForCustomerAsync(CustomerId id, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
}
// NOTE what is absent: no IQueryable, no DbSet, no Include, no SaveChanges.
// if the interface mentions the ORM's concepts, nothing was abstracted -
// it was renamed, and you now have indirection without benefit.
```

**The case FOR a repository over EF Core**

- The domain compiles without the ORM assembly
- Query intent is named: `OpenForCustomerAsync`, not a LINQ expression at the call site
- Query logic is in one place, and testable
- One aggregate per repository enforces the boundary
- Swapping to Dapper for a hot path is contained

**The case AGAINST**

- `DbSet<T>` *is* a repository, and `DbContext` *is* a unit of work
- A generic `IRepository<T>` exposing `IQueryable` abstracts nothing
- It blocks EF features: projections, split queries, `ExecuteUpdate`
- “We might swap the ORM” almost never happens
- It is often a layer that only forwards

``` text
  the position that survives both arguments

    WRITES  -> go through a repository.
               loading an aggregate, mutating it, saving it. The
               repository enforces the aggregate boundary, and the
               domain stays free of the ORM.

    READS   -> query directly, and PROJECT.
               a list screen needs 6 columns from 4 tables. Loading
               aggregates to build it is wasteful and the repository
               interface will grow a method per screen.

               var rows = await db.Orders
                   .Where(o => o.TenantId == t)
                   .Select(o => new OrderRow(o.Id, o.Total, o.Customer.Name))
                   .ToListAsync(ct);

  this is CQRS in its mildest and most useful form: different models for
  reading and writing, in one database, with no extra infrastructure.
```

> **\[Trap\]**
>
> **Common trap.** The generic repository — `IRepository<T>` with `GetAll()`, `Find(predicate)` and an `IQueryable` escape hatch — is the version worth avoiding. It abstracts nothing (the caller still writes LINQ against the ORM's model), blocks the ORM's features, and gives every aggregate the same interface regardless of what it actually needs. If you use repositories, make them specific.

###### The read/write split, in full

``` csharp
// WRITE SIDE - through the aggregate. The repository enforces the boundary.
public interface IOrderRepository
{
    Task<Order?> ByIdAsync(OrderId id, CancellationToken ct);   // loads the aggregate
    void Add(Order order);
    void Remove(Order order);
}

internal sealed class EfOrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Order?> ByIdAsync(OrderId id, CancellationToken ct) =>
        db.Orders
          .Include(o => o.Lines)          // the WHOLE aggregate, always
          .FirstOrDefaultAsync(o => o.Id == id, ct);
    public void Add(Order o) => db.Orders.Add(o);
}
// note there is no ListAsync, no FindByStatus, no paging. Those are READS,
// and putting them here is what makes repositories grow a method per screen.
```

``` csharp
// READ SIDE - project directly. No aggregate, no repository, no mapping.
public sealed class OrderListQuery(AppDbContext db)
{
    public async Task<IReadOnlyList<OrderRow>> HandleAsync(
        Guid tenantId, OrderStatus status, int page, CancellationToken ct) =>
        await db.Orders
            .AsNoTracking()                                   // no change tracking
            .Where(o => o.TenantId == tenantId && o.Status == status)
            .OrderByDescending(o => o.PlacedAt)
            .Skip(page * 50).Take(50)
            .Select(o => new OrderRow(                        // SQL selects 4 columns
                o.Id, o.PlacedAt, o.Total, o.Customer.Name))
            .ToListAsync(ct);
}
```

``` text
  WHY THE SPLIT RESOLVES THE REPOSITORY ARGUMENT

    the case AGAINST repositories is really a case against using them
    for READS:
      "it blocks projections"          - true, for reads
      "it blocks ExecuteUpdate"        - true, for bulk writes
      "DbSet is already a repository"  - true, for querying
      "it grows a method per screen"   - true, for reads

    the case FOR repositories is really a case for using them for WRITES:
      the aggregate boundary is enforced
      the domain compiles without the ORM
      loading is consistent - you always get the whole aggregate

    so: repository for writes, direct projection for reads.
    both objections disappear, and this is CQRS at level 2 - which
    needs no extra infrastructure at all.
```

| Operation                      | Goes through                           |
|--------------------------------|----------------------------------------|
| Load an aggregate to change it | Repository                             |
| Save an aggregate              | Repository + unit of work              |
| A list or grid screen          | Direct projection                      |
| A report                       | Direct projection, or a read model     |
| A bulk status update           | `ExecuteUpdate` — no aggregates loaded |
| An existence check             | Direct `AnyAsync`                      |

<a id="be-h762"></a>

#### Unit of Work and Specification

##### Unit of Work

**Intent:** track everything affected by a business transaction, and coordinate writing the changes and resolving concurrency. In .NET this is almost always already present, which is the main thing to know about it.

``` csharp
// DbContext IS a unit of work. It tracks changes and commits them together.
_order.AddLine(product, qty, price);       // tracked
_customer.RecordActivity();                // tracked
await db.SaveChangesAsync(ct);             // ONE transaction, both changes

// so wrapping it in this is usually pure ceremony:
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct); }
public class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

// the ONE defensible reason to have the interface: keeping the
// application layer free of the ORM type, so a handler's signature does
// not mention EF Core. That is a real benefit and a thin one - decide
// deliberately rather than by habit.
```

##### Specification

**Intent:** encapsulate a business rule as an object that can be combined, reused, and used both to *test* an instance and to *query* for matches.

``` csharp
public sealed class OverdueSpecification : Specification<Invoice>
{
    private readonly DateOnly _today;
    public OverdueSpecification(DateOnly today) => _today = today;

    public override Expression<Func<Invoice, bool>> ToExpression() =>
        i => i.DueDate < _today && i.Status != InvoiceStatus.Paid;
}

// the value: ONE definition, used in BOTH directions
if (spec.IsSatisfiedBy(invoice)) { ... }                    // in memory
var overdue = await db.Invoices.Where(spec.ToExpression()).ToListAsync(ct);  // in SQL

// and they compose
var chaseable = new OverdueSpecification(today)
    .And(new AmountAbove(Money.Euro(50)))
    .And(new NotOnPaymentPlan());
```

| Specification earns its place when                 | A method is enough when        |
|----------------------------------------------------|--------------------------------|
| The same rule is needed in memory *and* in a query | Only one of the two            |
| Rules are combined in varying ways                 | One fixed combination          |
| The rule is a named business concept               | It is an implementation detail |
| Rules are configured or composed at runtime        | They are fixed at compile time |

> **\[Trap\]**
>
> **Common trap.** Specifications must remain translatable if they are used in queries. The moment one calls a method EF Core cannot convert, it compiles cleanly and throws at runtime — and only against a real database, so a test using an in-memory list will pass. Keep them to expressions over mapped properties, and test them against a real provider.

###### Specifications that compose, and stay translatable

``` csharp
// a base that supports composition and stays EF-translatable
public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public bool IsSatisfiedBy(T candidate) => ToExpression().Compile()(candidate);

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);
    public Specification<T> Or(Specification<T> other)  => new OrSpecification<T>(this, other);
    public Specification<T> Not()                        => new NotSpecification<T>(this);
}

// the composition must rebuild the expression TREE, not just combine
// delegates - otherwise EF cannot translate the result to SQL.
internal sealed class AndSpecification<T>(Specification<T> left, Specification<T> right)
    : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var l = left.ToExpression();
        var r = right.ToExpression();
        var param = Expression.Parameter(typeof(T));

        // rebind BOTH lambdas onto ONE parameter, or the tree is invalid
        var body = Expression.AndAlso(
            new ReplaceParameterVisitor(l.Parameters[0], param).Visit(l.Body)!,
            new ReplaceParameterVisitor(r.Parameters[0], param).Visit(r.Body)!);

        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}

internal sealed class ReplaceParameterVisitor(ParameterExpression from,
                                              ParameterExpression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);
}
```

``` csharp
// real specifications, from a credit-control process
public sealed class OverdueSpecification(DateOnly today) : Specification<Invoice>
{
    public override Expression<Func<Invoice, bool>> ToExpression() =>
        i => i.DueDate < today && i.Status != InvoiceStatus.Paid;
}

public sealed class AmountAtLeast(decimal amount) : Specification<Invoice>
{
    public override Expression<Func<Invoice, bool>> ToExpression() =>
        i => i.Total >= amount;
}

public sealed class NotOnPaymentPlan : Specification<Invoice>
{
    public override Expression<Func<Invoice, bool>> ToExpression() =>
        i => i.PaymentPlanId == null;
}

// composed, once, and used in BOTH directions
var chaseable = new OverdueSpecification(today)
    .And(new AmountAtLeast(50m))
    .And(new NotOnPaymentPlan());

// 1. as SQL - the whole point of keeping it an expression tree
var toChase = await db.Invoices.Where(chaseable.ToExpression()).ToListAsync(ct);

// 2. in memory - the SAME rule, on an object you already have
if (chaseable.IsSatisfiedBy(invoice)) await _dunning.SendAsync(invoice, ct);
```

``` csharp
// the test that stops a specification silently becoming untranslatable
[Fact]
public async Task Every_specification_translates_to_sql()
{
    // runs against a REAL provider - an in-memory list would pass
    // even for specifications EF cannot translate
    await using var db = _fixture.CreateContext();

    foreach (var spec in AllSpecifications())
    {
        var ex = await Record.ExceptionAsync(() =>
            db.Invoices.Where(spec.ToExpression()).Take(1).ToListAsync());

        Assert.Null(ex);   // InvalidOperationException here means the spec
                           // calls a method EF cannot convert - and that
                           // only fails against a real database
    }
}
```

> **\[Trap\]**
>
> **Common trap.** The moment a specification calls a C# method — `i.Customer.IsActive()`, a helper, anything not a mapped property or a supported operator — it compiles, passes any test using a `List<T>`, and throws at runtime against a real database. Keep them to expressions over mapped properties, and run the translation test above.

<a id="be-h763"></a>

#### Domain Events, and rich versus anaemic models

**Domain event intent:** record that something meaningful happened in the domain, so other parts of the system can react without the originator knowing they exist.

``` csharp
public abstract class Entity
{
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

public class Order : Entity
{
    public void Place()
    {
        if (_lines.Count == 0) throw new DomainException("An order needs lines");
        Status = OrderStatus.Placed;
        Raise(new OrderPlaced(Id, CustomerId, Total));   // RECORDED, not dispatched
    }
}
```

``` text
  WHEN TO DISPATCH - and this detail decides whether it works

    INSIDE the transaction, before commit
      + handlers can modify the same transaction
      - a handler failure ROLLS BACK the business operation
      - side effects (emails, HTTP calls) happen before the data is durable
      -> only for handlers that modify data in the same aggregate boundary

    AFTER commit  <- the usual correct choice
      + the business operation succeeded regardless of handler failure
      + side effects follow durable state
      - a handler failure leaves the system inconsistent until retried
      -> pair with an OUTBOX if the reaction must not be lost

    the rule: in-process reactions that share the transaction -> before.
              anything with an external side effect -> after, via outbox.
```

##### Rich versus anaemic domain models

**Rich model**

- Behaviour lives with the data it governs
- Invalid states are unreachable — no public setters
- Rules are enforced regardless of caller
- Unit-testable with no infrastructure
- Costs: harder to map, and unfamiliar to some teams

**Anaemic model**

- Data classes plus service classes
- Rules live in services, and can be bypassed
- Maps trivially to an ORM and to DTOs
- Familiar, and productive for simple CRUD
- Costs: the same rule appears in several services, and they drift

The honest position: **anaemic is the right default for genuinely CRUD-shaped work** — a table maintenance screen has no invariants to protect, and wrapping it in a rich model is pure overhead. Rich models earn their cost where the *rules* are complex and must hold no matter who writes. Applying either uniformly across a whole system is the mistake; most systems want rich models in two or three areas and plain CRUD everywhere else.

###### Dispatching after commit, implemented

``` csharp
// collect on the entity; dispatch in the infrastructure, AFTER commit.
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    // 1. capture BEFORE saving - SaveChanges can change tracked state
    var entities = ChangeTracker.Entries<Entity>()
        .Where(e => e.Entity.DomainEvents.Count > 0)
        .Select(e => e.Entity)
        .ToList();

    var events = entities.SelectMany(e => e.DomainEvents).ToList();

    // 2. clear BEFORE dispatching, or a handler that saves again
    //    re-dispatches the same events - an infinite loop that is
    //    surprisingly easy to write
    foreach (var e in entities) e.ClearEvents();

    // 3. commit
    var result = await base.SaveChangesAsync(ct);

    // 4. dispatch. The business operation is already durable.
    foreach (var e in events) await _dispatcher.PublishAsync(e, ct);

    return result;
}
```

``` text
  THE GAP THIS LEAVES, and how to close it

    step 3 commits. Step 4 dispatches.
    the process dies between them.

    -> the order is placed, and NOBODY WAS TOLD. Silent inconsistency.

  for in-process reactions that are not critical (a cache invalidation,
  a metric) this is acceptable.

  for anything that MUST happen (an email, a downstream service, an
  integration event) it is not - and the fix is the OUTBOX:

    step 3 becomes:
      BEGIN
        save the entities
        INSERT the events into an outbox table      <- same transaction
      COMMIT

    a relay then publishes from the outbox, at-least-once.

  so the rule is:
    in-process, non-critical  -> dispatch after commit, as above
    must not be lost          -> write to the outbox IN the transaction

  and note the two can coexist: dispatch in-process handlers after
  commit, AND write integration events to the outbox.
```

| Handler does                             | Dispatch                                          |
|------------------------------------------|---------------------------------------------------|
| Modifies the same aggregate              | **Before** commit — it must be in the transaction |
| Invalidates a cache                      | After commit — loss is tolerable                  |
| Writes a read model in the same database | In the transaction                                |
| Sends an email or calls a service        | **Outbox** — must survive a crash                 |
| Publishes an integration event           | **Outbox**                                        |
| Records a metric                         | After commit                                      |

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · Design Patterns · Application Architecture</p>

<p class="pd">Patterns at the scale of a whole application: how the layers are arranged, which direction dependencies point, and whether reads and writes share a model.</p>

<a id="be-h767"></a>

#### CQRS

**Command Query Responsibility Segregation:** separate the model used to *change* data from the model used to *read* it. The most misunderstood pattern in this guide, because people assume it requires two databases. It does not.

``` text
  CQRS IS A SPECTRUM, and most of the value is at the cheap end

  LEVEL 1  separate CLASSES, one database, one model
             CreateOrderHandler / GetOrderQueryHandler
             -> the whole benefit of clarity, essentially no cost.
                MOST TEAMS SHOULD STOP HERE.

  LEVEL 2  separate MODELS, one database
             writes go through aggregates and repositories
             reads project directly to DTOs, no aggregates loaded
             -> removes the "repository grows a method per screen" problem

  LEVEL 3  separate READ STORE, same database
             materialised views or summary tables, refreshed

  LEVEL 4  separate read DATABASE, updated by events
             -> eventual consistency is now VISIBLE TO USERS.
                "I saved it and the list still shows the old value."
                needs a real justification: read/write ratios in the
                hundreds, or genuinely different storage needs.

  the mistake is hearing "CQRS" and implementing level 4.
```

``` csharp
// level 2 - the sweet spot, and it needs no infrastructure

// WRITE: through the aggregate. Rules enforced, invariants held.
public async Task<Result<Guid>> Handle(PlaceOrder cmd, CancellationToken ct)
{
    var order = Order.Create(new CustomerId(cmd.CustomerId));
    foreach (var l in cmd.Lines) order.AddLine(l.ProductId, l.Qty, l.Price);
    _repository.Add(order);
    await _uow.SaveChangesAsync(ct);
    return order.Id;
}

// READ: project straight to what the screen needs. No aggregate loaded,
// no mapping, and the SQL selects three columns instead of forty.
public async Task<IReadOnlyList<OrderRow>> Handle(ListOrders q, CancellationToken ct) =>
    await _db.Orders
        .Where(o => o.TenantId == q.TenantId && o.Status == q.Status)
        .OrderByDescending(o => o.PlacedAt)
        .Select(o => new OrderRow(o.Id, o.PlacedAt, o.Total, o.Customer.Name))
        .Take(50)
        .ToListAsync(ct);
```

| CQRS gives you                             | At level                   |
|--------------------------------------------|----------------------------|
| Named, single-purpose handlers             | 1                          |
| Reads that do not distort the domain model | 2                          |
| Queries that select only what is needed    | 2                          |
| Independent scaling of reads               | 3–4                        |
| A read store shaped for the screen         | 4                          |
| **Eventual consistency, visible to users** | **4 — and this is a cost** |

###### Level 2 in full, and the moment level 4 becomes necessary

``` csharp
// the write model: rich, invariant-protecting, loaded whole
public async Task<Result<Guid>> Handle(PlaceOrder cmd, CancellationToken ct)
{
    var customer = await _customers.ByIdAsync(new CustomerId(cmd.CustomerId), ct);
    if (customer is null) return Error.NotFound("Customer", cmd.CustomerId);

    var order = Order.Create(customer.Id);
    foreach (var l in cmd.Lines) order.AddLine(l.ProductId, l.Qty, l.Price);
    // every invariant enforced inside AddLine. Nothing outside can bypass it.

    _orders.Add(order);
    await _uow.SaveChangesAsync(ct);
    return order.Id;
}

// the read model: flat, shaped for one screen, no domain objects involved
public sealed record OrderRow(Guid Id, DateTimeOffset PlacedAt, decimal Total,
                              string CustomerName, int LineCount);

public async Task<IReadOnlyList<OrderRow>> Handle(ListOrders q, CancellationToken ct) =>
    await _db.Orders.AsNoTracking()
        .Where(o => o.TenantId == q.TenantId)
        .Select(o => new OrderRow(o.Id, o.PlacedAt, o.Total,
                                  o.Customer.Name, o.Lines.Count))
        .ToListAsync(ct);
// SAME DATABASE. SAME TABLES. Different models. No infrastructure added,
// and no consistency problem, because there is only one copy of the data.
```

``` text
  WHEN LEVEL 4 (a separate read database) BECOMES NECESSARY

    it is a capacity decision, not a design preference. The signals:

      reads outnumber writes by 100:1 or more, AND the read load is
        measurably affecting write latency
      the read shape requires joins so expensive that no index helps
      reads and writes need genuinely different storage
        (writes relational, reads a search index or a graph)
      reads must be served from a different REGION than writes

    what you take on, and it is visible to users:

      "I saved it and the list still shows the old value"
      -> eventual consistency stops being a technical detail and
         becomes a product decision requiring UX work: optimistic
         updates, a "processing" state, or a read-your-writes path.

    which is why levels 1-2 are where most of the value is, and level 4
    should follow a MEASUREMENT rather than an architecture diagram.
```

A practical middle step frequently missed: a **materialised view or summary table in the same database**, refreshed on a schedule. It gives you level 3's read performance with one storage system, one backup, and a staleness window you control explicitly — and it is a configuration change rather than an architecture.

<a id="be-h768"></a>

#### Event Sourcing

**Intent:** store the sequence of *events* that occurred, rather than the current state. Current state is derived by replaying them. Powerful, genuinely different, and frequently adopted for reasons that do not justify it.

``` text
  STATE-BASED                      EVENT-SOURCED

  orders table                     events stream, append-only
    id     | status  | total         1  OrderCreated(id, customer)
    4471   | Shipped | 250           2  LineAdded(sku, 2, 50)
                                     3  LineAdded(sku2, 1, 150)
  an UPDATE overwrites.              4  OrderPlaced()
  the previous state is GONE.        5  OrderShipped(trackingNo)

                                   current state = fold(events)
                                   and EVERY past state is still available

  WHAT IT BUYS
    a complete audit trail, by construction rather than by discipline
    "what did this look like on the 3rd?" - answerable
    new projections over HISTORICAL data ("we now want to know X" -
      and you can compute it for the past, not just from today)
    temporal debugging: replay to reproduce a bug exactly

  WHAT IT COSTS
    queries need PROJECTIONS - you cannot SELECT WHERE status = 'Shipped'
    SCHEMA EVOLUTION of events, forever. An event written in 2019 must
      still be readable in 2029, so events are immutable and versioned.
    snapshots, once streams grow long
    eventual consistency between the stream and its projections
    a much smaller pool of engineers who have done it
    GDPR erasure vs an append-only log - a genuine, hard conflict
```

``` csharp
public class Order
{
    private readonly List<IDomainEvent> _uncommitted = [];

    // state is REBUILT from events
    public static Order Replay(IEnumerable<IDomainEvent> history)
    {
        var order = new Order();
        foreach (var e in history) order.Apply(e);     // no validation on replay
        return order;
    }

    // a command VALIDATES, then RAISES. Apply only mutates.
    public void AddLine(ProductId p, Quantity q, Money price)
    {
        if (Status != OrderStatus.Draft) throw new DomainException("...");
        RaiseAndApply(new LineAdded(p, q, price));
    }

    private void Apply(IDomainEvent e)                 // NEVER throws, NEVER validates
    {
        switch (e)
        {
            case LineAdded l: _lines.Add(new(l.Product, l.Qty, l.Price)); break;
            case OrderPlaced: Status = OrderStatus.Placed; break;
        }
    }
}
// the separation matters: Apply must handle every event ever written,
// including ones whose validation rules have since changed. Validating
// in Apply means old streams stop replaying when a rule changes.
```

> **\[Note\]**
>
> **The honest threshold.** Adopt event sourcing when **the history is the product** — ledgers, regulated audit, trading, anything where “what did we know and when” is a business question. Do not adopt it because you want an audit log; a temporal table or an audit table gives you that for a fraction of the cost. And note that CQRS and event sourcing are *separate* ideas that pair well — you can have either without the other.

###### Event versioning, which is the part that decides whether it works

``` text
  the constraint nobody mentions until year two

    EVENTS ARE IMMUTABLE AND PERMANENT.

    an event written in 2019 must still be readable in 2029, by code
    that has changed completely. You cannot migrate them - rewriting
    history is precisely what event sourcing exists to prevent.

    so every schema change is an ADDITION, forever.
```

``` csharp
// V1, written three years ago. Thousands of these exist in the stream.
public sealed record OrderPlaced(Guid OrderId, decimal Total);

// the requirement: Total must now carry a currency.

// WRONG - this breaks every historic event
public sealed record OrderPlaced(Guid OrderId, Money Total);

// RIGHT, option A: a new version, and an UPCASTER
public sealed record OrderPlacedV2(Guid OrderId, Money Total);

public static OrderPlacedV2 Upcast(OrderPlaced v1) =>
    new(v1.OrderId, new Money(v1.Total, Currency.EUR));
//                                       ^^^^^^^^^^^^ an ASSUMPTION about
// history, and it must be documented. Everything before 2022 was in euros -
// if that is not true, the data is now silently wrong.

// RIGHT, option B: additive only. Add a nullable field, default on read.
public sealed record OrderPlaced(Guid OrderId, decimal Total,
                                 string? Currency = null);
// reading an old event gives Currency = null; the domain interprets that
// as the historic default. Fewer types, and the null spreads outward.
```

| Change to an event          | Safe?                       | Approach                                                        |
|-----------------------------|-----------------------------|-----------------------------------------------------------------|
| Add an optional field       | **Yes**                     | Default on read                                                 |
| Add a required field        | No                          | New version + upcaster                                          |
| Rename a field              | No                          | New version, or a serialiser alias                              |
| Change a type               | No                          | New version + upcaster                                          |
| Remove a field              | No                          | Keep reading it; stop writing it                                |
| Change what a field *means* | **No, and it is invisible** | New version. Same shape, different semantics, is the worst case |
| Split one event into two    | No                          | Upcast one to a sequence                                        |

The GDPR conflict is worth stating plainly because it has no clean solution: an append-only log versus a legal right to erasure. The usual answer is **crypto-shredding** — encrypt each subject's personal fields with a per-subject key, and destroy the key on an erasure request. The events remain, structurally intact and permanently unreadable. Decide this before adopting event sourcing for anything holding personal data, not after.

<a id="be-h769"></a>

#### Mediator and pipeline behaviours

**Intent:** route requests to handlers through a central dispatcher, so senders and handlers do not reference each other. In practice its main value is not the decoupling — it is that **one dispatcher gives you one place to put cross-cutting concerns**.

``` csharp
// the pipeline: Decorator + Chain of Responsibility, applied generically
public sealed class ValidationBehaviour<TReq, TRes>(IEnumerable<IValidator<TReq>> validators)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq request, RequestHandlerDelegate<TRes> next,
                                   CancellationToken ct)
    {
        var failures = validators.Select(v => v.Validate(request))
                                 .SelectMany(r => r.Errors).ToList();
        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();                          // pass to the next link
    }
}

// registration order IS execution order
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehaviour<,>));
// Logging -> Validation -> Transaction -> Handler
// note transaction INSIDE validation: do not open a transaction for a
// request that is about to be rejected.
```

**Worth it when**

- Cross-cutting concerns apply to every operation
- Many handlers with a uniform shape
- You want commands as objects, for the reasons in the Command section
- Vertical slices, where each slice is a handler

**Not worth it when**

- A controller calling a service is clear enough
- Few operations
- The indirection hides more than it saves
- The team spends longer finding handlers than reading them

> **\[Trap\]**
>
> **Common trap.** A mediator makes navigation harder: “go to definition” on `Send(command)` lands in the library, not the handler. That is a real daily cost, traded for one place to put cross-cutting concerns. It is a reasonable trade at scale and pure overhead for a small service — and note that MediatR's move to a commercial licence made this a dependency decision as well as a design one.

###### Behaviour ordering, and the transaction placement that matters

``` csharp
// registration order IS execution order, outermost first
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ExceptionBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehaviour<,>));
// Logging -> Exception -> Authorization -> Validation -> Transaction -> Handler
```

``` text
  WHY THAT ORDER, position by position - each one has a reason

    LOGGING        outermost. Must observe everything, including
                   requests rejected by authorisation. Put it inside
                   and your metrics silently exclude the failures.

    EXCEPTION      just inside logging, so the log records the
                   exception before it is translated to a response.

    AUTHORIZATION  before validation. Do not tell an unauthorised
                   caller which fields were invalid - that is
                   information disclosure.

    VALIDATION     before the transaction. Never open a transaction
                   for a request that is about to be rejected: it
                   consumes a connection and holds it for nothing.

    TRANSACTION    innermost, wrapping only the handler. The narrower
                   the transaction, the less it holds locks.

  the two orderings that are actual bugs, not preferences:

    transaction OUTSIDE validation
      -> every invalid request opens and rolls back a transaction.
         under load this exhausts the connection pool.

    validation BEFORE authorisation
      -> "field X is invalid" leaks the shape of a resource to
         someone not permitted to see it.
```

``` csharp
public sealed class TransactionBehaviour<TReq, TRes>(AppDbContext db)
    : IPipelineBehavior<TReq, TRes> where TReq : notnull
{
    public async Task<TRes> Handle(TReq request, RequestHandlerDelegate<TRes> next,
                                   CancellationToken ct)
    {
        // QUERIES must not open a transaction. Only commands.
        if (request is not ICommand) return await next();

        // already inside one? Do not nest - just continue.
        if (db.Database.CurrentTransaction is not null) return await next();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var response = await next();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return response;
        });
        // note the execution strategy wrapper: with EnableRetryOnFailure,
        // EF refuses a user-initiated transaction unless the WHOLE block
        // is retryable - which means the handler must be idempotent.
    }
}
```

### Domain modeling and transaction boundaries

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Domain modeling and transaction boundaries</p>

<a id="be-m253"></a>

#### Aggregates: the transaction boundary

An **aggregate** is a cluster of objects treated as one unit for changes: one root, one identity, one consistency boundary. It is not an abstract modelling nicety — it is the answer to "what does a single transaction cover?", which is a data question with direct performance consequences.

``` text
Order (aggregate ROOT)          ← the only thing outside code may reference
 ├── OrderLine  ×N              ← reachable only through Order
 ├── ShippingAddress (value)    ← no identity of its own
 └── CustomerId                 ← an ID to ANOTHER aggregate, never a reference
```

Three rules, each of which is really a data-access rule:

- **Reference other aggregates by id, never by navigation.** An `Order.Customer` navigation invites loading the customer graph on every order operation, and invites a transaction that spans both ([Case: the order list that went from 80ms to 4s](14-03-worked-cases-from-production.md#be-m294)).
- **One transaction changes one aggregate.** If two must change together atomically, either the boundary is wrong, or you need eventual consistency between them ([Transactions across services: sagas and the outbox](12-01-monoliths-modules-and-services.md#be-m250)).
- **Invariants inside the boundary are enforced synchronously; everything across boundaries is eventually consistent.**

``` text
// The boundary decides your locking and your failure modes:
//   "order total must equal the sum of its lines"     -> inside Order. Enforce now.
//   "customer credit limit must not be exceeded"      -> spans Customer and all
//                                                        their Orders. Either make
//                                                        that one aggregate (and
//                                                        accept the contention) or
//                                                        check it asynchronously
//                                                        and compensate.
```

> **\[Trap\]**
>
> **Large aggregates are a contention problem, not a design preference.** If `Customer` contains every order, then two concurrent orders for one customer contend on the same root — and your optimistic concurrency token rejects one of them ([Optimistic concurrency](07-04-transactions-and-isolation.md#be-d449)). Aggregates should be as small as the invariants allow. "Everything that logically belongs together" is the wrong criterion; "everything that must be consistent at the instant of a write" is the right one.

<a id="be-m254"></a>

#### Entities, value objects, and invariants

|            | Entity                                         | Value object                                                                                |
|------------|------------------------------------------------|---------------------------------------------------------------------------------------------|
| Identity   | an id; two with the same id are the same thing | none — equal if all fields are equal                                                        |
| Mutability | changes over time, keeps its id                | immutable; replace, do not modify                                                           |
| Examples   | `Order`, `Customer`                            | `Money`, `Address`, `DateRange`, `Sku`                                                      |
| In C#      | `class` with an id                             | `readonly record struct` ([records, readonly struct, ref struct](02-03-types.md#be-m54))                  |
| In EF      | an entity type                                 | a complex type or owned type ([Shadow properties, backing fields, and splitting](07-07-orms-and-ef-core.md#be-m176)) |

``` csharp
// An invariant is a rule that is ALWAYS true. The way to guarantee it is to
// make the invalid state unconstructable, not to check for it later.
public sealed class Order {
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyList<OrderLine> Lines => _lines;
    public Money Total { get; private set; }
    public OrderStatus Status { get; private set; }

    private Order() { }                             // for EF materialisation

    public static Order Place(CustomerId customer, IEnumerable<OrderLine> lines) {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("An order needs at least one line");
        if (list.Count > 500) throw new DomainException("At most 500 lines");
        return new Order { CustomerId = customer, Status = OrderStatus.Open,
                           _lines = { }, Total = Sum(list) };
    }

    public void AddLine(OrderLine line) {
        if (Status != OrderStatus.Open) throw new DomainException("Order is not open");
        _lines.Add(line);
        Total += line.LineTotal;                    // the invariant holds after
    }                                               // every operation, by construction
}
```

> **\[Why it matters\]**
>
> **The payoff is deletion, again.** If `Total` can only change through `AddLine`, no code anywhere can put it out of sync — so every defensive recalculation, every "just in case" validation, and every reconciliation job for that particular bug disappears. Same argument as a `NOT NULL` column ([Constraints: what the database guarantees](07-02-the-relational-model-and-sql.md#be-m188)) and a parsed `Sku` ([Value objects and parsing at the boundary](08-05-validation-and-errors-at-the-boundary.md#be-m266)): push the guarantee to the edge and the interior simplifies.

> **\[Trap\]**
>
> **Do not enforce a cross-aggregate invariant inside a setter.** A check that requires querying the database ("is this SKU still in stock?") inside a domain method turns every property assignment into I/O, and it is a lie anyway — the answer can change between the check and the commit. Those belong in an application service with an explicit transaction, or in a database constraint ([Constraints: what the database guarantees](07-02-the-relational-model-and-sql.md#be-m188)).

<a id="be-m255"></a>

#### Rich and anemic models, honestly

An *anemic* model is data classes with public setters plus service classes holding all behaviour. It is widely called an anti-pattern and is widely the right choice.

| Use a rich model when                           | Anemic is fine when                                     |
|-------------------------------------------------|---------------------------------------------------------|
| invariants are non-trivial and must never break | the entity is a data record with no rules               |
| the same rules apply from several entry points  | rules are workflow-specific, not entity-specific        |
| state transitions are constrained (a lifecycle) | every field is independently editable                   |
| the domain is where the complexity actually is  | the complexity is in queries, reporting, or integration |

``` text
// A CRUD screen over a lookup table does not need an aggregate root, a
// repository, a domain event, and a specification. It needs a DTO and a save.
// Applying tactical DDD uniformly costs more than it returns on the 80% of
// a system that is genuinely CRUD.

// The useful test: can you name a rule that must never be violated?
//   "A closed order cannot be modified"          -> rich model, enforce it
//   "Name is required, max 200 characters"       -> validation at the edge
```

> **\[Why it matters\]**
>
> **Complexity is not uniformly distributed, so your modelling should not be either.** A realistic system has two or three genuinely rule-heavy areas — pricing, order lifecycle, entitlements — surrounded by a large amount of forms-over-data. Invest the modelling effort where the rules are, and keep the rest boring. A codebase where everything is an aggregate is as hard to work in as one where nothing is.

> **\[Trap\]**
>
> **A rich model with public setters is neither.** If `order.Status = Closed` compiles from anywhere, the methods on `Order` are suggestions and the invariants are not enforced. Private setters and a private parameterless constructor for EF are what make the difference — and EF supports both without compromise ([Shadow properties, backing fields, and splitting](07-07-orms-and-ef-core.md#be-m176)).

<a id="be-m256"></a>

#### Domain events

A domain event records that something meaningful happened, so side effects can be decoupled from the operation that caused them.

``` csharp
public sealed class Order {
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyList<IDomainEvent> Events => _events;
    public void ClearEvents() => _events.Clear();

    public void Confirm(TimeProvider time) {
        if (Status != OrderStatus.Open) throw new DomainException("Not open");
        Status = OrderStatus.Confirmed;
        _events.Add(new OrderConfirmed(Id, CustomerId, Total, time.GetUtcNow()));
    }                                    // recorded, NOT dispatched - the domain
}                                        // does not know what happens next
```

``` csharp
// Dispatch at the transaction boundary, in a SaveChanges interceptor:
public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData e, int result, CancellationToken ct = default) {
    var roots = e.Context!.ChangeTracker.Entries<IHasDomainEvents>()
        .Select(x => x.Entity).Where(x => x.Events.Count > 0).ToList();
    foreach (var root in roots) {
        foreach (var ev in root.Events) await _mediator.PublishAsync(ev, ct);
        root.ClearEvents();
    }
    return result;
}
```

**Before or after the commit is a real decision**, and the two are not interchangeable:

|                 | Dispatch BEFORE commit     | Dispatch AFTER commit                                                                    |
|-----------------|----------------------------|------------------------------------------------------------------------------------------|
| Handlers run in | the same transaction       | their own                                                                                |
| Handler failure | rolls the whole thing back | leaves the change committed                                                              |
| Handler sees    | uncommitted state          | committed state                                                                          |
| Right for       | in-aggregate consistency   | notifications, integration ([Asynchronous communication: events and commands](12-01-monoliths-modules-and-services.md#be-m249)) |

> **\[Trap\]**
>
> **Do not send an email, call an API, or publish to a broker from a domain event handler running inside the transaction.** It holds locks across a network call ([Case: the deadlock at month-end close](14-03-worked-cases-from-production.md#be-m303)), and if the transaction later rolls back you have already sent the email. Write an outbox row inside the transaction and let a dispatcher publish it afterwards ([Transactions across services: sagas and the outbox](12-01-monoliths-modules-and-services.md#be-m250)).

<a id="be-m257"></a>

#### Repositories, one per aggregate

If you use repositories at all ([Where data access code belongs](07-07-orms-and-ef-core.md#be-m260)), the useful shape is **one per aggregate root**, with methods that return whole aggregates — not a generic `IRepository<T>` over every entity.

``` csharp
public interface IOrderRepository {
    Task<Order?> FindAsync(OrderId id, CancellationToken ct);       // whole aggregate
    Task<Order?> FindWithLinesAsync(OrderId id, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
    // No IQueryable. No GetAll. No arbitrary predicates.
    // If a caller needs a filtered list of projections, that is a QUERY,
    // not a repository operation - see below.
}

public sealed class OrderRepository(AppDb db) : IOrderRepository {
    public Task<Order?> FindWithLinesAsync(OrderId id, CancellationToken ct) =>
        db.Orders.Include(o => o.Lines)          // tracked - we intend to modify
                 .SingleOrDefaultAsync(o => o.Id == id, ct);
    public void Add(Order order) => db.Orders.Add(order);
}
```

**Reads and writes want different things**, and forcing both through one abstraction is what makes repositories painful:

``` csharp
WRITE path   repository → whole tracked aggregate → domain method → SaveChanges
READ  path   query class → projection → DTO, untracked, no aggregate at all
             (§157)

// A grid showing 50 orders with customer names does NOT need 50 Order
// aggregates with their lines loaded. It needs one projected query.
```

> **\[Tip\]**
>
> **The unit of work is already there.** `DbContext` is the unit of work and `SaveChanges` is the commit, so repositories should *not* each expose a `Save` — that makes a transaction spanning two aggregates impossible to express. Let the application service own the `SaveChanges` call ([Application services and use cases](#be-m258)).

<a id="be-m258"></a>

#### Application services and use cases

The layer between the transport and the domain. Its job is orchestration, and it should read like a description of the use case.

``` csharp
public sealed class ConfirmOrderHandler(
        IOrderRepository orders, IStockService stock, AppDb db, TimeProvider time) {

    public async Task<Result<OrderDto>> HandleAsync(ConfirmOrder cmd, CancellationToken ct) {
        // 1. load
        var order = await orders.FindWithLinesAsync(cmd.OrderId, ct);
        if (order is null) return Result.NotFound($"Order {cmd.OrderId}");

        // 2. check anything that crosses the aggregate boundary, explicitly
        if (!await stock.IsAvailableAsync(order.Lines, ct))
            return Result.Conflict("Insufficient stock");

        // 3. let the DOMAIN decide - invariants and state transitions live there
        order.Confirm(time);

        // 4. commit once, at this level - one transaction, one aggregate
        await db.SaveChangesAsync(ct);

        // 5. map out
        return Result.Ok(OrderDto.From(order));
    }
}
```

Notice what is *not* here: no HTTP, no status codes, no SQL, no business rules. The handler can be called from an endpoint, a background job, a message consumer, or a test, unchanged — which is the whole reason to have the layer.

``` csharp
// Returning a result rather than throwing keeps expected outcomes off the
// exception path (§44), and lets the transport map them:
return result.Status switch {
    ResultStatus.Ok       => Results.Ok(result.Value),
    ResultStatus.NotFound => Results.NotFound(),
    ResultStatus.Conflict => Results.Problem(result.Error, statusCode: 409),
    _                     => Results.Problem()
};                                                    // (§242)
```

> **\[Trap\]**
>
> **Transaction scope belongs here, not lower.** If a repository or a domain method calls `SaveChanges`, no caller can compose two operations atomically, and every use case that needs to becomes a workaround. One `SaveChanges` per use case, at this level, is the rule that keeps transaction boundaries visible and controllable ([Transactions and isolation levels](07-04-transactions-and-isolation.md#be-a100)).

<a id="be-m259"></a>

#### Mapping a domain model to EF Core

The common objection to rich domain models is that the ORM forces public setters and parameterless constructors. It does not — EF Core supports encapsulation fully, provided you configure it.

``` csharp
modelBuilder.Entity<Order>(e => {
    // Private parameterless ctor is found automatically; EF sets fields directly.
    e.HasKey(o => o.Id);

    // Strongly typed id, stored as the primitive (§266)
    e.Property(o => o.Id).HasConversion(id => id.Value, v => new OrderId(v));

    // Encapsulated collection: EF writes the FIELD, your API stays read-only
    e.Metadata.FindNavigation(nameof(Order.Lines))!
     .SetPropertyAccessMode(PropertyAccessMode.Field);

    // Value object, inline, no identity of its own
    e.ComplexProperty(o => o.Total, b => {
        b.Property(m => m.Amount).HasColumnName("Total").HasPrecision(18, 4);
        b.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsUnicode(false);
    });

    // Private setters are fine - EF sets the backing field
    e.Property(o => o.Status).HasConversion<int>();

    // Audit columns the domain never sees (§176)
    e.Property<DateTimeOffset>("ModifiedUtc");
    e.Property<byte[]>("RowVersion").IsRowVersion();
});
```

| Domain concept       | EF mechanism                                                                |
|----------------------|-----------------------------------------------------------------------------|
| Private constructor  | found automatically                                                         |
| Private setter       | supported directly                                                          |
| Read-only collection | backing field + `PropertyAccessMode.Field`                                  |
| Value object         | `ComplexProperty`, or `OwnsOne` on older versions                           |
| Strongly typed id    | `HasConversion`, ideally configured globally                                |
| Audit metadata       | shadow property + an interceptor ([Interceptors and diagnostics](07-07-orms-and-ef-core.md#be-m177)) |
| Domain events        | a non-mapped field, dispatched on save ([Domain events](#be-m256))          |

> **\[Trap\]**
>
> **Where EF genuinely does constrain you:** an aggregate must be loadable as a graph, so an aggregate with thousands of children is expensive to load and save whole — which is a real argument for smaller aggregates ([Aggregates: the transaction boundary](#be-m253)), not for abandoning the model. And a `HasConversion`'d property loses some translation fidelity in SQL, so check that filtering and ordering on it still produce the SQL you expect ([Type mapping: CLR to SQL](07-07-orms-and-ef-core.md#be-m170)).

> **\[Why it matters\]**
>
> **The persistence-ignorance trade.** A fully persistence-ignorant domain with a hand-written mapping layer is achievable and occasionally worth it — for a genuinely complex core domain that must outlive its storage. For everything else, configuring EF to respect encapsulation gets you 90% of the benefit for a fraction of the code, and the mapping stays in one file you can read.

### Design Patterns for .NET Backends

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Design Patterns for .NET Backends</p>

<p class="pd">The patterns that actually recur in .NET backend code, in the form they take here rather than in the 1994 book — plus the anti-patterns that produce most of the code you will be asked to rescue.</p>

<a id="be-a75"></a>

#### The specification pattern

A specification is a named, reusable, composable predicate. It solves the problem that query logic otherwise duplicates itself across handlers, and that `Where(o => o.Status == Active && o.DeletedAt == null && o.TenantId == t)` appears in nineteen places with one of them subtly wrong.

``` csharp
public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> ToExpression();

    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);
    public Specification<T> Or(Specification<T> other)  => new OrSpecification<T>(this, other);
    public Specification<T> Not()                       => new NotSpecification<T>(this);

    public bool IsSatisfiedBy(T candidate) => ToExpression().Compile()(candidate);   // in-memory
    public static implicit operator Expression<Func<T, bool>>(Specification<T> s) => s.ToExpression();
}

public sealed class ActiveOrders : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        o => o.Status != OrderStatus.Cancelled && o.DeletedAt == null;
}

public sealed class PlacedAfter(DateTimeOffset when) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => o => o.PlacedAt >= when;
}

public sealed class OverValue(decimal amount) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() => o => o.Total > amount;
}
```

``` csharp
// compose at the call site, and the whole thing still translates to SQL
var spec = new ActiveOrders()
    .And(new PlacedAfter(lastMonth))
    .And(new OverValue(1000m));

var orders = await db.Orders.Where(spec).ToListAsync(ct);
// -> WHERE Status <> 3 AND DeletedAt IS NULL AND PlacedAt >= @p0 AND Total > @p1

// and the SAME object validates an in-memory instance, with no duplication
if (!spec.IsSatisfiedBy(candidateOrder))
    return Result.Fail("This order does not qualify for the promotion.");
```

###### Making composition actually translate

The naive `AndSpecification` fails at runtime: you cannot simply `&&` two expression bodies, because each has its own parameter instance and EF cannot translate the result. The composition must rewrite the parameter.

``` csharp
public sealed class AndSpecification<T>(Specification<T> left, Specification<T> right) : Specification<T>
{
    public override Expression<Func<T, bool>> ToExpression()
    {
        var l = left.ToExpression();
        var r = right.ToExpression();

        var param = Expression.Parameter(typeof(T), "x");
        var body = Expression.AndAlso(
            new ReplaceParameterVisitor(l.Parameters[0], param).Visit(l.Body)!,
            new ReplaceParameterVisitor(r.Parameters[0], param).Visit(r.Body)!);

        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}

private sealed class ReplaceParameterVisitor(ParameterExpression from, ParameterExpression to)
    : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == from ? to : base.VisitParameter(node);
}
```

In practice, use `LinqKit`'s `PredicateBuilder` or the `Ardalis.Specification` package rather than maintaining visitors. The value of writing it once is understanding why composed expressions need rewriting — it is the same mechanism EF uses to build every query you have ever written.

| Use a specification when                                   | Skip it when                 |
|------------------------------------------------------------|------------------------------|
| The same predicate appears in three or more places         | The query is used once       |
| Business rules must be evaluated both in SQL and in memory | Reads are simple projections |
| Rules combine in ways the caller decides at runtime        | The combination is fixed     |
| Rules need their own unit tests                            | The rule is one comparison   |

<a id="be-a77"></a>

#### State machines for aggregate lifecycles

Most backend domains are state machines wearing a status column. Modelling the transitions explicitly turns “which status can go to which” from tribal knowledge scattered across if-statements into a table you can read, test, and show to a domain expert.

``` text
                +----------+
                |  Draft   |
                +----+-----+
                     | submit
                +----v-----+  cancel   +-----------+
                |  Placed  +----------->  Cancelled |
                +----+-----+           +-----------+
                     | pay                   ^
                +----v-----+  cancel          |
                |   Paid   +------------------+
                +----+-----+
                     | ship
                +----v-----+
                | Shipped  |     no transitions out - terminal
                +----------+

  the diagram IS the specification. Every transition not drawn is illegal.
```

``` csharp
public enum OrderStatus { Draft, Placed, Paid, Shipped, Cancelled }
public enum OrderTrigger { Submit, Pay, Ship, Cancel }

public sealed class Order
{
    private static readonly Dictionary<(OrderStatus, OrderTrigger), OrderStatus> Transitions = new()
    {
        [(OrderStatus.Draft,  OrderTrigger.Submit)] = OrderStatus.Placed,
        [(OrderStatus.Placed, OrderTrigger.Pay)]    = OrderStatus.Paid,
        [(OrderStatus.Placed, OrderTrigger.Cancel)] = OrderStatus.Cancelled,
        [(OrderStatus.Paid,   OrderTrigger.Ship)]   = OrderStatus.Shipped,
        [(OrderStatus.Paid,   OrderTrigger.Cancel)] = OrderStatus.Cancelled,
    };

    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    public bool CanFire(OrderTrigger t) => Transitions.ContainsKey((Status, t));

    private void Fire(OrderTrigger t)
    {
        if (!Transitions.TryGetValue((Status, t), out var next))
            throw new DomainException("order.invalid_transition",
                $"Cannot {t} an order that is {Status}.");
        Status = next;
    }

    public void Submit() { GuardHasLines(); Fire(OrderTrigger.Submit);
                           Raise(new OrderPlaced(Id)); }
    public void Ship(string carrier) { Fire(OrderTrigger.Ship);
                                       Raise(new OrderShipped(Id, carrier)); }
}
```

###### What an explicit machine buys you beyond correctness

- **The UI writes itself.** `CanFire` drives which buttons are enabled, so the front end and the domain can never disagree about what is allowed.
- **Exhaustive tests are generated, not written.** Iterate every (state, trigger) pair and assert that exactly the ones in the table succeed — 25 test cases from five lines of test code.
- **New states are cheap and safe.** Adding `Refunded` means adding rows; the compiler and the table tell you what is now reachable.
- **The table is reviewable by non-developers.** A domain expert can read a transition list and spot the missing rule in a way they cannot read a nested conditional.
- **Audit and projection fall out of it.** Every transition is an event, so history is a natural by-product.

``` csharp
[Fact]
public void Only_the_declared_transitions_are_permitted()
{
    foreach (var state in Enum.GetValues<OrderStatus>())
    foreach (var trigger in Enum.GetValues<OrderTrigger>())
    {
        var order = OrderBuilder.InState(state);
        var expected = Order.IsDeclared(state, trigger);

        var actual = Record.Exception(() => order.Fire(trigger)) is null;

        Assert.Equal(expected, actual);
    }
}
```

For anything past a handful of states — approval chains, claims processing, subscription lifecycles — consider `Stateless`, a small library that adds guards, entry and exit actions, substates and DOT-graph export. The exported diagram in your documentation staying automatically in sync with the code is worth the dependency on its own.
