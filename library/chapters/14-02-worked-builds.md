---
id: "14-2"
number: "14.2"
title: "Worked builds"
part: 14
part_title: "Practice: recipes, worked builds and real cases"
words: 6770
minutes: 29
---

# 14.2 Worked builds

> **Quick look.** Complete features built end to end, from requirement to tests.
>
> - Order placement: validation protects the contract; stock and SKU checks are domain rules that need data.
> - Idempotency as an endpoint filter: claim the key with a unique constraint, never SELECT-then-INSERT.
> - Domain rules, optimistic concurrency and the outbox in one transaction; on retry, clear the tracker and reload, with a cap.
> - The test that matters is the oversell test: real concurrency against a real database.
> - Webhooks: verify the signature (FixedTimeEquals) before parsing, store, then process. Uploads: presigned, check magic bytes, serve from a separate domain.
> - Rescuing a slow endpoint: measure first, compute in the database, add the index the plan asks for (online), cache last.
> - Outbox relay uses the outbox row id as the message id; workflows need compensations; scheduled jobs need idempotent work, not just a lock.
> - Tenant isolation beyond query filters (row-level security); rename a column safely in four expand-contract deploys.


### In practice: realistic examples

1 worked scenario of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: The oversell test

"Stock must not oversell" is the requirement that matters most, and it only fails under real concurrency.

*Fifty parallel buyers, one item*

```csharp
[Fact]
public async Task Last_item_is_sold_exactly_once()
{
    await SeedProductAsync(sku: "SEAT-12A", stock: 1);
    var client = Factory.CreateClient();
    var attempts = Enumerable.Range(0, 50).Select(i =>
        client.PostAsJsonAsync("/orders", new { sku = "SEAT-12A", qty = 1 },
            CancellationToken.None));
    var responses = await Task.WhenAll(attempts);

    Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    Assert.Equal(49, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    Assert.Equal(0, await StockOfAsync("SEAT-12A"));
}
```

> **Lesson:** Concurrency rules need a test with real parallel requests against a real database.

### A feature, end to end

<p class="src">From <em>Backend Engineering</em> · Worked Builds · A feature, end to end</p>

<p class="pd">One vertical slice built the way it would actually ship: requirement, endpoint, validation, domain rules, persistence, concurrency, tests. Every layer shown, nothing hand-waved.</p>

<a id="be-w1"></a>

#### The requirement, and the shape of the answer

“A customer can place an order. Stock must not oversell. If two requests arrive at once, exactly one wins. The client retries on timeout and must not be charged twice.” That single paragraph decides most of the design.

| Requirement                   | Mechanism                                                 |
|-------------------------------|-----------------------------------------------------------|
| Must not oversell             | Optimistic concurrency on stock rows, retried on conflict |
| Exactly one wins under a race | Row version / `xmin` check on update                      |
| Retry must not double-charge  | Idempotency key, claimed atomically                       |
| Client sees a useful failure  | `ProblemDetails` with a stable error code                 |
| Other services react          | Domain event via the outbox, in the same transaction      |

``` text
the slice, top to bottom

POST /api/v1/orders
  │  Idempotency-Key: 5f2c…            ← the client generates this once
  ▼
endpoint          bind, validate shape, authenticate, authorise
  ▼
idempotency       claim the key atomically → replay stored response if seen
  ▼
application       load aggregate, apply the rule, raise the event
  ▼
domain            Order.Place() enforces the invariant — not the service
  ▼
persistence       SaveChanges: order + stock + outbox row, ONE transaction
  ▼
outbox relay      publishes OrderPlaced after commit, at-least-once
  ▼
201 Created       + Location, + the stored response for later retries
```

<a id="be-w2"></a>

#### The endpoint: binding, validation and the error contract

``` csharp
// Program.cs — a versioned group, filters applied once for the whole group
var orders = app.NewVersionedApi("Orders")
    .MapGroup("/api/v{version:apiVersion}/orders")
    .HasApiVersion(1.0)
    .RequireAuthorization("orders:write")
    .AddEndpointFilter<IdempotencyFilter>()
    .WithTags("Orders");

orders.MapPost("/", PlaceOrder)
      .WithName("PlaceOrder")
      .Produces<OrderResponse>(StatusCodes.Status201Created)
      .ProducesValidationProblem()
      .ProducesProblem(StatusCodes.Status409Conflict)
      .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

static async Task<Results<Created<OrderResponse>, ValidationProblem, ProblemHttpResult>>
    PlaceOrder(PlaceOrderRequest request,
               IValidator<PlaceOrderRequest> validator,
               PlaceOrderHandler handler,
               ICurrentUser user,
               CancellationToken ct)
{
    var validation = await validator.ValidateAsync(request, ct);
    if (!validation.IsValid)
        return TypedResults.ValidationProblem(validation.ToDictionary());

    var result = await handler.HandleAsync(
        new PlaceOrderCommand(user.Id, user.TenantId, request.Items), ct);

    return result.Match<Results<Created<OrderResponse>, ValidationProblem, ProblemHttpResult>>(
        order => TypedResults.Created($"/api/v1/orders/{order.Id}", order),
        outOfStock => TypedResults.Problem(
            title: "Insufficient stock", statusCode: 422,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "INSUFFICIENT_STOCK",          // stable, never localised
                ["sku"]  = outOfStock.Sku,
                ["available"] = outOfStock.Available,
            }),
        conflict => TypedResults.Problem(
            title: "The order changed while you were placing it",
            statusCode: 409,
            extensions: new Dictionary<string, object?> { ["code"] = "CONCURRENT_UPDATE" }));
}

// the request contract — a record, so it is immutable and value-compared
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items);
public sealed record OrderLineRequest(string Sku, int Quantity);
```

``` csharp
// validation lives beside the contract, not scattered through the handler
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("An order needs at least one line.")
            .Must(i => i.Count <= 50).WithMessage("At most 50 lines per order.")
            .Must(i => i.Select(l => l.Sku).Distinct().Count() == i.Count)
                .WithMessage("Duplicate SKUs — combine them into one line.");

        RuleForEach(x => x.Items).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku).NotEmpty().MaximumLength(32)
                .Matches("^[A-Z0-9-]+$").WithMessage("SKU format is invalid.");
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 100);
        });
    }
}
// registered once: builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

> **\[Trap\]**
>
> **Common trap.** Validation here protects the *contract* — shape, ranges, format. It does not know whether the SKU exists or whether stock is available; those are domain rules that need data, and putting them in the validator means a database call inside validation and a rule that a second entry point can bypass.

<a id="be-w3"></a>

#### Idempotency as an endpoint filter

``` csharp
public sealed class IdempotencyFilter(AppDb db, ILogger<IdempotencyFilter> log)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx,
                                                EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return TypedResults.Problem("Idempotency-Key header is required",
                                        statusCode: 400);
        }

        var key = raw.ToString();
        var fingerprint = await HashBodyAsync(http.Request);   // detect key reuse

        // atomic claim: the unique index does the work, not a read-then-write
        var record = new IdempotencyRecord
        {
            Key = key, Fingerprint = fingerprint,
            TenantId = http.User.TenantId(), CreatedAt = DateTime.UtcNow
        };
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException e) when (e.IsUniqueViolation())
        {
            db.ChangeTracker.Clear();
            var existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstAsync(r => r.Key == key, http.RequestAborted);

            if (existing.Fingerprint != fingerprint)
                return TypedResults.Problem(
                    "This Idempotency-Key was used with a different payload",
                    statusCode: 422);

            if (existing.CompletedAt is null)          // the first call is still running
                return TypedResults.Problem("Request in progress, retry shortly",
                                            statusCode: 409);

            log.LogInformation("Replaying stored response for {Key}", key);
            http.Response.StatusCode = existing.StatusCode;
            return Results.Content(existing.Response!, "application/json");
        }

        var result = await next(ctx);                   // run the real handler

        // store the response BEFORE returning it, or a crash loses the record
        record.StatusCode = http.Response.StatusCode is >= 200 and < 300 ? 201 : 400;
        record.Response   = JsonSerializer.Serialize(result);
        record.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(http.RequestAborted);
        return result;
    }
}

// the table that makes it work
public class IdempotencyRecordConfig : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasKey(r => r.Key);
        b.HasIndex(r => new { r.TenantId, r.Key }).IsUnique();
        b.HasIndex(r => r.CreatedAt);        // for the 24h cleanup job
        b.Property(r => r.Response).HasColumnType("jsonb");
    }
}
```

> **\[Trap\]**
>
> **Common trap.** Do not implement the claim as “SELECT, then INSERT if missing”. Two retries arriving a millisecond apart both see nothing and both proceed. Let the unique constraint arbitrate and catch the violation — the database is the only thing that can decide this atomically.

<a id="be-w4"></a>

#### Domain rules, concurrency and the outbox in one transaction

``` csharp
// ---------- domain: the invariant lives in the aggregate ----------
public sealed class Order : Entity
{
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyList<OrderLine> Lines => _lines;
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public decimal Total { get; private set; }

    public static Order Place(Guid tenantId, Guid customerId,
                              IEnumerable<(Product Product, int Qty)> items)
    {
        var order = new Order { TenantId = tenantId, CustomerId = customerId };
        foreach (var (product, qty) in items)
        {
            if (qty < 1) throw new DomainException("Quantity must be positive.");
            product.Reserve(qty);                       // ← the rule, in Product
            order._lines.Add(new OrderLine(product.Id, product.Price, qty));
        }
        if (order._lines.Count == 0) throw new DomainException("Empty order.");

        order.Total  = order._lines.Sum(l => l.UnitPrice * l.Quantity);
        order.Status = OrderStatus.Placed;
        order.Raise(new OrderPlaced(order.Id, order.TenantId, order.Total));
        return order;
    }
}

public sealed class Product : Entity
{
    public int Available { get; private set; }
    public uint Version { get; private set; }          // ← concurrency token

    public void Reserve(int qty)
    {
        if (qty > Available)
            throw new InsufficientStockException(Sku, Available);
        Available -= qty;
    }
}

// EF mapping: let the database detect the race
b.Entity<Product>().Property(p => p.Version).IsRowVersion();      // SQL Server
// b.Entity<Product>().UseXminAsConcurrencyToken();               // Postgres
```

``` csharp
// ---------- application: orchestrate, retry the race, never swallow it ----------
public sealed class PlaceOrderHandler(AppDb db, IDbContextFactory<AppDb> _,
                                      ILogger<PlaceOrderHandler> log)
{
    public async Task<OneOf<OrderResponse, OutOfStock, ConcurrencyConflict>>
        HandleAsync(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var skus = cmd.Items.Select(i => i.Sku).ToArray();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var products = await db.Products
                    .Where(p => p.TenantId == cmd.TenantId && skus.Contains(p.Sku))
                    .ToDictionaryAsync(p => p.Sku, ct);

                var missing = skus.Except(products.Keys).ToArray();
                if (missing.Length > 0) return new OutOfStock(missing[0], 0);

                var order = Order.Place(cmd.TenantId, cmd.CustomerId,
                    cmd.Items.Select(i => (products[i.Sku], i.Quantity)));

                db.Orders.Add(order);
                // the outbox row is written by an interceptor from order.DomainEvents,
                // inside THIS SaveChanges — so the event cannot be lost or duplicated
                await db.SaveChangesAsync(ct);
                return OrderResponse.From(order);
            }
            catch (InsufficientStockException e)
            {
                return new OutOfStock(e.Sku, e.Available);     // expected: not an error
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                log.LogInformation("Stock changed under us, retry {Attempt}", attempt);
                db.ChangeTracker.Clear();                       // reload and try again
                await Task.Delay(Random.Shared.Next(20, 80) * attempt, ct);
            }
        }
        return new ConcurrencyConflict();
    }
}
```

> **\[Trap\]**
>
> **Common trap.** The retry must reload the entities — `ChangeTracker.Clear()` then query again. Retrying with the stale instances re-sends the same original values and fails forever. And cap the attempts: an unbounded retry on a hot product turns a race into a livelock that pins a CPU.

<a id="be-w5"></a>

#### Testing the slice: what to test at which level

| Level                        | What it proves                                    | Cost                 |
|------------------------------|---------------------------------------------------|----------------------|
| Domain unit test             | The invariant holds — no oversell, no empty order | Milliseconds, no I/O |
| Handler test with fakes      | Orchestration and the retry path                  | Milliseconds         |
| Integration (Testcontainers) | The SQL, the constraint, the outbox row           | Seconds              |
| Contract/API test            | Status codes, error codes, headers                | Seconds              |

``` csharp
// 1. domain — the rule itself, no infrastructure at all
[Fact]
public void Placing_more_than_available_is_rejected()
{
    var product = ProductBuilder.WithStock(3);
    var act = () => Order.Place(TenantId, CustomerId, [(product, 5)]);
    act.Should().Throw<InsufficientStockException>()
       .Which.Available.Should().Be(3);
    product.Available.Should().Be(3);        // and nothing was reserved
}

// 2. integration — a real Postgres, so constraints and SQL are real
public class OrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Concurrent_orders_do_not_oversell()
    {
        await factory.SeedAsync(new Product("SKU-1", stock: 1));
        var client = factory.CreateClient();

        // fire ten at once at the last unit
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            client.PostAsJsonAsync("/api/v1/orders",
                new { items = new[] { new { sku = "SKU-1", quantity = 1 } } },
                Headers("Idempotency-Key", Guid.NewGuid().ToString()))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => (int)r.StatusCode == 422).Should().Be(9);

        var stock = await factory.GetStockAsync("SKU-1");
        stock.Should().Be(0);                 // never negative — the actual point
    }

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_same_order()
    {
        var key = Guid.NewGuid().ToString();
        var body = new { items = new[] { new { sku = "SKU-2", quantity = 1 } } };

        var first  = await client.PostAsJsonAsync("/api/v1/orders", body, Key(key));
        var second = await client.PostAsJsonAsync("/api/v1/orders", body, Key(key));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync())
            .Should().Be(await first.Content.ReadAsStringAsync());
        (await factory.CountOrdersAsync()).Should().Be(1);   // exactly one row
    }
}
```

``` csharp
// the factory: a real database per test run, disposed automatically
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDb>()
                   .Database.MigrateAsync();      // migrations are tested too
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<AppDb>>();
            s.AddDbContext<AppDb>(o => o.UseNpgsql(_db.GetConnectionString()));
            s.AddAuthentication("Test")
             .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        });

    public new async Task DisposeAsync() => await _db.DisposeAsync();
}
```

> **\[Why it matters\]** The oversell test is the one that matters, and it is the one most suites do not have: it needs real concurrency against a real database, because the bug it catches lives in the interaction between the constraint, the transaction and the retry — exactly the part mocks cannot model.

### Integrations that survive the real world

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Integrations that survive the real world</p>

<p class="pd">Calling other people’s systems, receiving their callbacks, and moving files — with the failure handling that separates a demo from production.</p>

<a id="be-w6"></a>

#### A resilient outbound call, configured properly

``` csharp
// registration: typed client + resilience + telemetry, once
builder.Services.AddHttpClient<PricingClient>(c =>
{
    c.BaseAddress = new Uri(config["Pricing:BaseUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(10);            // the OVERALL ceiling
    c.DefaultRequestHeaders.UserAgent.ParseAdd("orders-api/2.4");
})
.AddResilienceHandler("pricing", b =>
{
    b.AddTimeout(TimeSpan.FromSeconds(2));           // per attempt

    b.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,                            // never synchronise retries
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result is { StatusCode: HttpStatusCode.RequestTimeout
                                              or HttpStatusCode.TooManyRequests
                                              or >= HttpStatusCode.InternalServerError }
            || args.Outcome.Exception is HttpRequestException or TimeoutRejectedException),
        OnRetry = args =>                            // honour Retry-After if given
        {
            var after = args.Outcome.Result?.Headers.RetryAfter?.Delta;
            if (after is not null) args.RetryDelay = after.Value;
            return ValueTask.CompletedTask;
        }
    });

    b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5, SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 10, BreakDuration = TimeSpan.FromSeconds(15),
    });
})
.AddStandardHedgingHandler();                        // optional: parallel attempt
```

``` csharp
public sealed class PricingClient(HttpClient http, HybridCache cache,
                                  ILogger<PricingClient> log)
{
    // the call itself is boring — that is the point. Policy lives in config.
    public async Task<decimal?> GetPriceAsync(string sku, CancellationToken ct)
    {
        try
        {
            return await cache.GetOrCreateAsync($"price:{sku}",
                async token =>
                {
                    using var res = await http.GetAsync($"/prices/{sku}", token);
                    if (res.StatusCode == HttpStatusCode.NotFound) return (decimal?)null;
                    res.EnsureSuccessStatusCode();
                    var dto = await res.Content
                        .ReadFromJsonAsync<PriceDto>(cancellationToken: token);
                    return dto?.Amount;
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
                cancellationToken: ct);
        }
        catch (BrokenCircuitException)
        {
            // degrade deliberately: a stale price beats a failed checkout
            log.LogWarning("Pricing circuit open — falling back to last known price");
            return await _fallback.GetLastKnownAsync(sku, ct);
        }
    }
}
```

> **\[Trap\]**
>
> **Common trap.** The client `Timeout` and the per-attempt timeout are different things, and getting them backwards makes retries impossible: if the overall timeout is 2 s and each attempt is allowed 2 s, the first attempt consumes the whole budget. Overall must exceed (attempts × per-attempt) plus backoff, or the resilience pipeline is decorative.

<a id="be-w7"></a>

#### Receiving a webhook: verify, store, then work

``` csharp
app.MapPost("/webhooks/payments", async (HttpRequest req, WebhookInbox inbox,
        IOptions<PaymentWebhookOptions> opt, ILogger<Program> log,
        CancellationToken ct) =>
{
    // 1. read the RAW body — signatures are over bytes, not over your DTO
    req.EnableBuffering();
    using var reader = new StreamReader(req.Body, leaveOpen: true);
    var payload = await reader.ReadToEndAsync(ct);
    req.Body.Position = 0;

    // 2. verify the signature in constant time, and check the timestamp
    if (!req.Headers.TryGetValue("X-Signature", out var sig) ||
        !req.Headers.TryGetValue("X-Timestamp", out var ts))
        return Results.Unauthorized();

    if (!long.TryParse(ts, out var unix) ||
        Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > 300)
        return Results.Unauthorized();                       // replay window: 5 min

    var expected = Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(opt.Value.Secret),
        Encoding.UTF8.GetBytes($"{unix}.{payload}")));

    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sig.ToString())))
    {
        log.LogWarning("Rejected webhook: bad signature");   // never log the payload
        return Results.Unauthorized();
    }

    // 3. dedupe on the provider's event id, then ACK FAST
    var evt = JsonSerializer.Deserialize<PaymentEvent>(payload)!;
    var isNew = await inbox.TryRecordAsync(evt.Id, payload, ct);
    if (!isNew) return Results.Ok();                          // already have it

    // 4. do the work LATER — the provider is waiting, and will retry on timeout
    await inbox.EnqueueAsync(evt.Id, ct);
    return Results.Ok();
});
```

``` text
why "ACK fast, work later" is not optional

provider sends event ──► your endpoint
                            │
       if you process inline (charge, email, update ledger):
         • slow work → provider times out → provider RETRIES
         • you now process the same event twice, concurrently
         • a 500 from any downstream turns into a retry storm

       if you verify + store + return 200 in ~10ms:
         • the provider is satisfied exactly once
         • your worker retries on YOUR schedule, with YOUR backoff
         • a downstream outage delays work instead of duplicating it
```

> **\[Trap\]**
>
> **Common trap.** Compare signatures with `FixedTimeEquals`, never `==`. String comparison exits at the first differing byte, and the timing difference is measurable over a network — enough to forge a signature byte by byte. Also verify *before* deserialising: parsing untrusted JSON is attack surface you do not need to expose.

<a id="be-w8"></a>

#### File upload: presigned, verified, and recorded

``` csharp
// STEP 1 — the client asks for permission; the server decides everything
app.MapPost("/api/v1/uploads/presign", async (PresignRequest req,
        IStorage storage, ICurrentUser user, AppDb db, CancellationToken ct) =>
{
    if (req.SizeBytes > 25 * 1024 * 1024)
        return Results.Problem("File too large (max 25 MB)", statusCode: 413);
    if (!AllowedTypes.Contains(req.ContentType))
        return Results.Problem($"Unsupported type {req.ContentType}", statusCode: 415);

    // the SERVER picks the key — never a client-supplied path
    var key = $"tenant-{user.TenantId}/uploads/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}";

    var pending = new UploadRecord
    {
        Id = Guid.NewGuid(), Key = key, TenantId = user.TenantId,
        OwnerId = user.Id, DeclaredType = req.ContentType,
        DeclaredSize = req.SizeBytes, Status = UploadStatus.Pending,
        ExpiresAt = DateTime.UtcNow.AddMinutes(15),
    };
    db.Uploads.Add(pending);
    await db.SaveChangesAsync(ct);

    var url = storage.CreateUploadUrl(key, req.ContentType, TimeSpan.FromMinutes(10));
    return Results.Ok(new { uploadId = pending.Id, url, key });
})
.RequireAuthorization();

// STEP 2 — the browser PUTs the bytes straight to storage. Your API sees none of it.

// STEP 3 — the client tells you it finished; you VERIFY rather than believe
app.MapPost("/api/v1/uploads/{id:guid}/complete", async (Guid id,
        IStorage storage, AppDb db, IBus bus, ICurrentUser user,
        CancellationToken ct) =>
{
    var upload = await db.Uploads.FirstOrDefaultAsync(
        u => u.Id == id && u.OwnerId == user.Id, ct);
    if (upload is null) return Results.NotFound();
    if (upload.Status is not UploadStatus.Pending) return Results.Conflict();

    var meta = await storage.HeadAsync(upload.Key, ct);      // does it exist?
    if (meta is null)
        return Results.Problem("No object was uploaded", statusCode: 422);

    if (meta.ContentLength > upload.DeclaredSize * 1.05)     // and match the claim
        return Results.Problem("Uploaded file does not match the declared size",
                               statusCode: 422);

    if (!await storage.SniffContentTypeMatchesAsync(upload.Key, upload.DeclaredType, ct))
        return Results.Problem("File content does not match its declared type",
                               statusCode: 415);             // magic-byte check

    upload.Status = UploadStatus.Uploaded;
    upload.ActualSize = meta.ContentLength;
    upload.CompletedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);

    await bus.Publish(new FileUploaded(upload.Id, upload.Key), ct);   // scan, thumbnail…
    return Results.Ok(new { upload.Id, upload.Key });
});
```

``` csharp
// the cleanup nobody writes until storage costs spike
public class OrphanedUploadCleaner(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var storage = scope.ServiceProvider.GetRequiredService<IStorage>();

            var stale = await db.Uploads
                .Where(u => u.Status == UploadStatus.Pending &&
                            u.ExpiresAt < DateTime.UtcNow)
                .Take(500).ToListAsync(stopping);

            foreach (var u in stale)
            {
                await storage.DeleteAsync(u.Key, stopping);   // may not exist: fine
                u.Status = UploadStatus.Abandoned;
            }
            await db.SaveChangesAsync(stopping);
        }
    }
}
```

> **\[Trap\]**
>
> **Common trap.** Trusting `Content-Type` is how a “profile picture” becomes stored HTML that runs script on your domain. Check the magic bytes, serve user files from a *separate* domain (or with `Content-Disposition: attachment` and a strict CSP), and never render them inline on the origin that holds your session cookie.

<a id="be-w9"></a>

#### Bulk import: stream it, stage it, merge it

``` csharp
// 100 MB CSV, 2 million rows, must not exhaust memory or lock the live table
public sealed class ProductImporter(NpgsqlDataSource dataSource,
                                    ILogger<ProductImporter> log)
{
    public async Task<ImportResult> ImportAsync(Stream csv, Guid tenantId,
                                                CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. a staging table scoped to this transaction — no locks on the real one
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TEMP TABLE staging_products
                (sku text, name text, price numeric, stock int)
                ON COMMIT DROP;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. stream rows straight into it — constant memory, binary protocol
        var rows = 0; var errors = new List<string>();
        await using (var writer = await conn.BeginBinaryImportAsync(
            "COPY staging_products (sku, name, price, stock) FROM STDIN (FORMAT BINARY)", ct))
        {
            using var reader = new StreamReader(csv);
            using var parser = new CsvReader(reader, CultureInfo.InvariantCulture);
            await foreach (var row in parser.GetRecordsAsync<ProductRow>(ct))
            {
                if (!row.IsValid(out var why)) { errors.Add($"{row.Sku}: {why}"); continue; }
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(row.Sku, ct);
                await writer.WriteAsync(row.Name, ct);
                await writer.WriteAsync(row.Price, ct);
                await writer.WriteAsync(row.Stock, ct);
                if (++rows % 100_000 == 0) log.LogInformation("Staged {Rows} rows", rows);
            }
            await writer.CompleteAsync(ct);
        }

        // 3. ONE set-based merge — the live table is touched once, briefly
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO products (tenant_id, sku, name, price, stock, updated_at)
                SELECT @tenant, s.sku, s.name, s.price, s.stock, now()
                FROM staging_products s
                ON CONFLICT (tenant_id, sku) DO UPDATE
                SET name = EXCLUDED.name, price = EXCLUDED.price,
                    stock = EXCLUDED.stock, updated_at = now()
                WHERE products.price IS DISTINCT FROM EXCLUDED.price
                   OR products.stock IS DISTINCT FROM EXCLUDED.stock;
                """;                      // ← WHERE avoids rewriting unchanged rows
            cmd.Parameters.AddWithValue("tenant", tenantId);
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return new ImportResult(rows, affected, errors);
        }
    }
}
```

| Approach                       | 2M rows     | Memory          | Live-table impact    |
|--------------------------------|-------------|-----------------|----------------------|
| EF `Add` + SaveChanges per row | Hours       | Grows unbounded | Constant locking     |
| EF batched (1,000/batch)       | ~20–40 min  | Bounded         | Frequent short locks |
| **COPY into staging + merge**  | **1–3 min** | **Constant**    | **One brief write**  |

> **\[Trap\]**
>
> **Common trap.** Never hold the import in a single long transaction against the live table — it blocks writers, bloats the WAL and cannot be resumed. Staging plus a final merge keeps the exclusive window to seconds. And always report the rows you *rejected*: a silent import that skips 40,000 malformed rows is worse than a failure.

### Rescuing a slow endpoint

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Rescuing a slow endpoint</p>

<p class="pd">A real optimisation, start to finish: an endpoint at 4.2 s p95 taken to 90 ms, with the measurement at every step. Each fix is shown with what it changed and what it did not.</p>

<a id="be-w10"></a>

#### The situation, and refusing to guess

A dashboard endpoint is timing out for the largest customer. The instinct is to add a cache. Do not: caching a wrong query hides the problem and adds an invalidation bug. Measure first.

``` csharp
// the endpoint as found
app.MapGet("/api/v1/dashboard", async (AppDb db, ICurrentUser user, CancellationToken ct) =>
{
    var orders = await db.Orders
        .Include(o => o.Lines).ThenInclude(l => l.Product)
        .Include(o => o.Customer)
        .Where(o => o.TenantId == user.TenantId)
        .ToListAsync(ct);                                    // ← everything. ever.

    return new DashboardDto
    {
        TotalRevenue  = orders.Sum(o => o.Total),
        OrderCount    = orders.Count,
        TopCustomers  = orders.GroupBy(o => o.Customer.Name)
                              .OrderByDescending(g => g.Sum(o => o.Total))
                              .Take(5)
                              .Select(g => new { Name = g.Key, Total = g.Sum(o => o.Total) }),
        RecentOrders  = orders.OrderByDescending(o => o.PlacedAt).Take(20)
                              .Select(OrderSummary.From),
    };
});
```

``` text
measurement, before touching anything

p95 latency          4,200 ms
rows materialised    412,000 orders × ~6 lines  → 2.4M row objects
SQL statements       1  (a cartesian join across 3 tables)
bytes from database  ~180 MB per request
allocations          ~600 MB per request  → gen-2 collections under load
CPU                  mostly GC and object materialisation, not SQL

the trace makes it obvious: one span, 4.1s, "SELECT … FROM orders
LEFT JOIN order_lines LEFT JOIN products LEFT JOIN customers"

so the problem is NOT the database being slow. It is being asked for
180 MB to compute five numbers.
```

<a id="be-w11"></a>

#### Fix 1 — compute in the database, not in memory

``` csharp
// the same five numbers, computed where the data already lives
var since = DateTime.UtcNow.AddDays(-30);

var totals = await db.Orders
    .Where(o => o.TenantId == user.TenantId && o.PlacedAt >= since)
    .GroupBy(_ => 1)
    .Select(g => new { Revenue = g.Sum(o => o.Total), Count = g.Count() })
    .SingleOrDefaultAsync(ct) ?? new { Revenue = 0m, Count = 0 };

var topCustomers = await db.Orders
    .Where(o => o.TenantId == user.TenantId && o.PlacedAt >= since)
    .GroupBy(o => new { o.CustomerId, o.Customer.Name })
    .Select(g => new TopCustomerDto(g.Key.Name, g.Sum(o => o.Total)))
    .OrderByDescending(x => x.Total)
    .Take(5)
    .ToListAsync(ct);                       // → ORDER BY … LIMIT 5, in SQL

var recent = await db.Orders
    .Where(o => o.TenantId == user.TenantId)
    .OrderByDescending(o => o.PlacedAt)
    .Take(20)
    .Select(o => new OrderSummaryDto(o.Id, o.PlacedAt, o.Total, o.Customer.Name))
    .ToListAsync(ct);                       // 20 rows, 4 columns
```

|                     | Before   | After fix 1 |
|---------------------|----------|-------------|
| p95                 | 4,200 ms | **780 ms**  |
| Rows returned       | 2.4M     | 26          |
| Bytes from DB       | ~180 MB  | ~8 KB       |
| Allocations/request | ~600 MB  | ~40 KB      |
| SQL statements      | 1        | 3           |

Three round trips replaced one, and it is **five times faster** — because the cost was never the round trip, it was the volume. This is the fix that matters most and the one people skip on the way to caching.

> **\[Trap\]**
>
> **Common trap.** A scope change is hiding in there: the original summed *all* orders, the rewrite sums the last 30 days. That is a product decision, not an optimisation. Confirm it — silently changing what a number means is worse than a slow endpoint.

<a id="be-w12"></a>

#### Fix 2 — the index the plan asked for

``` sql
-- ask the database what it is doing, before adding anything
EXPLAIN (ANALYZE, BUFFERS)
SELECT customer_id, sum(total) FROM orders
WHERE tenant_id = '…' AND placed_at >= now() - interval '30 days'
GROUP BY customer_id ORDER BY 2 DESC LIMIT 5;

-- Seq Scan on orders  (cost=0..48210 rows=412000 width=24)
--   Filter: (tenant_id = '…' AND placed_at >= …)
--   Rows Removed by Filter: 1932000        ← reading 2.3M rows to keep 412k
--   Buffers: shared read=41203             ← 320 MB off disk, every request

CREATE INDEX CONCURRENTLY ix_orders_tenant_placed
    ON orders (tenant_id, placed_at DESC)
    INCLUDE (total, customer_id);           -- covering: no heap fetch at all

-- Index Only Scan using ix_orders_tenant_placed  (rows=412000)
--   Heap Fetches: 0
--   Buffers: shared hit=2841               ← 22 MB, from cache
```

|              | Before   | Fix 1  | Fix 2      |
|--------------|----------|--------|------------|
| p95          | 4,200 ms | 780 ms | **140 ms** |
| Buffers read | 41,203   | 41,203 | 2,841      |

> **\[Trap\]**
>
> **Common trap.** `CREATE INDEX` takes an exclusive lock and blocks writes for the whole build — on a large table that is an outage. Use `CONCURRENTLY` (Postgres) or `ONLINE = ON` (SQL Server), and check afterwards that it is actually *valid* — a concurrent build can fail and leave an invalid index behind that the planner ignores.

<a id="be-w13"></a>

#### Fix 3 — cache last, with invalidation designed in

Only now is caching the right move: the query is correct, indexed and cheap, and what remains is that a hundred users ask for the same numbers every few seconds.

``` csharp
public sealed class DashboardService(AppDb db, HybridCache cache, ICurrentUser user)
{
    public ValueTask<DashboardDto> GetAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(
            key: $"dashboard:v2:{user.TenantId}",       // ← version in the key,
            factory: async token => await BuildAsync(token),  //   tenant in the key
            options: new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(2),          // L2 (Redis)
                LocalCacheExpiration = TimeSpan.FromSeconds(20) // L1 (per instance)
            },
            tags: [$"tenant:{user.TenantId}", "dashboard"],
            cancellationToken: ct);
}

// invalidation: the write path owns it, triggered by the domain event
public sealed class DashboardInvalidator(HybridCache cache) : IConsumer<OrderPlaced>
{
    public Task Consume(ConsumeContext<OrderPlaced> ctx) =>
        cache.RemoveByTagAsync($"tenant:{ctx.Message.TenantId}")
             .AsTask();
}
```

|                      | Before    | Fix 1    | Fix 2  | Fix 3           |
|----------------------|-----------|----------|--------|-----------------|
| p95                  | 4,200 ms  | 780 ms   | 140 ms | **90 ms**       |
| p99                  | 11,000 ms | 1,900 ms | 310 ms | **150 ms**      |
| DB queries / 100 req | 100       | 300      | 300    | **~4**          |
| Cost of a stale read | —         | —        | —      | up to 2 min old |

Note the honest bookkeeping: the last fix bought 50 ms of latency and a 98% reduction in database load, at the price of numbers that can be two minutes stale. For a dashboard that is an easy trade. For an account balance it would not be — and that is a product conversation, not an engineering one.

> **\[Why it matters\]** The order matters more than the individual fixes. Caching first would have delivered “fast” while still transferring 180 MB on every miss, still allocating 600 MB, and now with stale data on top. Measure, reduce the work, index the work that remains, and only then avoid repeating it.

### Work that happens later

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Work that happens later</p>

<p class="pd">The outbox relay, a multi-step workflow with compensation, live updates over SignalR, and a scheduled job that stays correct when three instances run it at once.</p>

<a id="be-w18"></a>

#### The outbox relay: publishing without losing or duplicating

``` csharp
// ---------- write side: the interceptor that stages events ----------
public sealed class OutboxInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
    {
        var db = e.Context!;
        var events = db.ChangeTracker.Entries<Entity>()
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();

        foreach (var ev in events)
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id          = Guid.CreateVersion7(),      // time-ordered: index-friendly
                Type        = ev.GetType().FullName!,
                Payload     = JsonSerializer.Serialize(ev, ev.GetType()),
                OccurredAt  = clock.GetUtcNow().UtcDateTime,
                TenantId    = (ev as ITenantEvent)?.TenantId,
            });
        }
        foreach (var x in db.ChangeTracker.Entries<Entity>()) x.Entity.ClearEvents();
        return base.SavingChangesAsync(e, r, ct);
    }
}
// the order row and the outbox row commit together, or neither does.
```

``` csharp
// ---------- relay: the worker that publishes them, exactly once *enough* ----------
public sealed class OutboxRelay(IServiceScopeFactory scopes, IBus bus,
                                ILogger<OutboxRelay> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            try { await PumpAsync(stopping); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Outbox pump failed"); }
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();

        // SKIP LOCKED: three replicas can pump concurrently without colliding
        var batch = await db.Outbox
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND (retry_after IS NULL OR retry_after < now())
                ORDER BY occurred_at
                LIMIT 100
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var msg in batch)
        {
            try
            {
                var type = Type.GetType(msg.Type)
                    ?? throw new InvalidOperationException($"Unknown type {msg.Type}");
                var ev = JsonSerializer.Deserialize(msg.Payload, type)!;

                await bus.Publish(ev, c => c.MessageId = msg.Id, ct);  // ← stable id
                msg.ProcessedAt = DateTime.UtcNow;                     //   = dedupe key
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                msg.LastError = ex.Message[..Math.Min(500, ex.Message.Length)];
                msg.RetryAfter = DateTime.UtcNow.AddSeconds(Math.Pow(2, msg.Attempts));

                if (msg.Attempts >= 10)
                {
                    msg.DeadLetteredAt = DateTime.UtcNow;   // stop retrying; alert on this
                    log.LogError(ex, "Outbox message {Id} dead-lettered after {N}",
                                 msg.Id, msg.Attempts);
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
```

> **\[Trap\]**
>
> **Common trap.** Publishing with a fresh `MessageId` each attempt destroys the whole guarantee: a crash between publish and mark-processed republishes with a new id, and consumers cannot tell it is the same event. Use the outbox row’s id as the message id — that is what makes the consumer’s inbox dedupe work.

> **\[Why it matters\]** This is the mechanism that lets a service change its own state and tell the world atomically, without distributed transactions. Every “the order exists but no email was sent” incident is a system that publishes outside the transaction.

<a id="be-w19"></a>

#### A multi-step workflow with compensation

``` text
place order → reserve stock → charge card → confirm
                      │                 │
              (rejected)          (failed)
                      ▼                 ▼
                cancel order    release stock + cancel order

rules that make it survivable:
  • every step is idempotent and carries the saga id
  • the IRREVERSIBLE step goes last (send the email after confirmation)
  • every waiting state has a TIMEOUT — otherwise a lost reply hangs forever
  • the saga state is queryable: "which orders are stuck in AwaitingPayment?"
```

``` csharp
public sealed class OrderSaga : MassTransitStateMachine<OrderSagaState>
{
    public State AwaitingStock { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public Schedule<OrderSagaState, PaymentTimeout> PaymentTimeout { get; private set; } = null!;

    public OrderSaga()
    {
        InstanceState(x => x.CurrentState);
        Event(() => OrderPlaced, e => e.CorrelateById(m => m.Message.OrderId));
        Schedule(() => PaymentTimeout, x => x.PaymentTimeoutToken,
                 s => s.Delay = TimeSpan.FromMinutes(10));

        Initially(
            When(OrderPlaced)
                .Then(c => { c.Saga.OrderId = c.Message.OrderId;
                             c.Saga.StartedAt = DateTime.UtcNow; })
                .Send(c => new ReserveStock(c.Saga.OrderId, c.Message.Items))
                .TransitionTo(AwaitingStock));

        During(AwaitingStock,
            When(StockReserved)
                .Send(c => new ChargeCard(c.Saga.OrderId, c.Saga.Total))
                .Schedule(PaymentTimeout, c => new PaymentTimeout(c.Saga.OrderId))
                .TransitionTo(AwaitingPayment),
            When(StockRejected)
                .Send(c => new CancelOrder(c.Saga.OrderId, "out of stock"))
                .Finalize());

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .Unschedule(PaymentTimeout)
                .Publish(c => new OrderConfirmed(c.Saga.OrderId))   // email happens HERE
                .Finalize(),
            When(PaymentFailed)
                .Send(c => new ReleaseStock(c.Saga.OrderId))        // compensate
                .Send(c => new CancelOrder(c.Saga.OrderId, "payment failed"))
                .Finalize(),
            When(PaymentTimeout.Received)                           // the forgotten path
                .Send(c => new ReleaseStock(c.Saga.OrderId))
                .Send(c => new CancelOrder(c.Saga.OrderId, "payment timed out"))
                .Finalize());

        SetCompletedWhenFinalized();      // remove finished sagas, keep the table small
    }
}

// the operational query you WILL need at 3am
// SELECT current_state, count(*) FROM order_saga_state
// WHERE started_at < now() - interval '30 minutes' GROUP BY 1;
```

> **\[Trap\]**
>
> **Common trap.** Compensation is not rollback: the stock is released and the order cancelled, but the customer saw “order placed” and may have had a pending charge on their statement. Model those intermediate states in the domain (`Pending → Confirmed → Cancelled`) and show them — hiding eventual consistency is what produces “I was charged but have no order” tickets.

<a id="be-w21"></a>

#### A scheduled job that three instances can run safely

``` csharp
// the problem: replicas × timers = the job runs N times
// the fix: a lease in the database, held by whoever wins the race
public sealed class NightlyInvoiceJob(IServiceScopeFactory scopes,
                                      ILogger<NightlyInvoiceJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            var now = DateTime.UtcNow;
            if (now.Hour != 2 || now.Minute != 0) continue;      // 02:00 UTC

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();

            // Postgres advisory lock: one instance gets true, the others false.
            // Released automatically when the connection closes — including on crash.
            var gotLock = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(918273645)")
                .SingleAsync(stopping);

            if (!gotLock) { log.LogDebug("Another instance is running invoices"); continue; }

            // idempotence as well as exclusion: a lock can be lost, a run can repeat
            var runKey = $"invoices:{now:yyyy-MM-dd}";
            if (await db.JobRuns.AnyAsync(r => r.Key == runKey, stopping))
            {
                log.LogInformation("Invoices already generated for {Date}", now.Date);
                continue;
            }

            var count = await GenerateInvoicesAsync(db, stopping);
            db.JobRuns.Add(new JobRun { Key = runKey, CompletedAt = DateTime.UtcNow,
                                        ItemCount = count });
            await db.SaveChangesAsync(stopping);
            log.LogInformation("Generated {Count} invoices for {Date}", count, now.Date);
        }
    }
}
```

| Mechanism                   | Exclusion     | Survives crash          | Notes                                    |
|-----------------------------|---------------|-------------------------|------------------------------------------|
| Postgres advisory lock      | Yes           | Yes (connection-scoped) | Free, no extra table                     |
| Redis `SET NX PX`           | Yes           | Yes (TTL)               | Needs careful TTL vs job length          |
| Row lock on a leases table  | Yes           | Only with a heartbeat   | Portable, visible, easy to debug         |
| Hangfire / Quartz clustered | Yes           | Yes                     | Brings its own storage and dashboard     |
| Kubernetes CronJob          | Yes (one pod) | Yes                     | Best when the work is a separate process |

> **\[Trap\]**
>
> **Common trap.** A lock is not idempotence. The instance holding it can die mid-run and the next tick starts over — so the work itself must tolerate a partial previous attempt. The `JobRuns` row plus per-item guards is what makes a repeat harmless, and without it a nightly invoice job eventually bills someone twice.

### Access, tenancy and changing the schema

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Access, tenancy and changing the schema</p>

<p class="pd">Who is allowed to see what, how tenant isolation is enforced rather than remembered, and how to change a table under live traffic without downtime.</p>

<a id="be-w23"></a>

#### Tenant isolation you cannot forget

``` csharp
// 1. resolve once, per request, from the token — never from a query string
public sealed class TenantContext { public Guid TenantId { get; internal set; } }

app.Use(async (ctx, next) =>
{
    var claim = ctx.User.FindFirst("tenant_id")?.Value;
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        if (!Guid.TryParse(claim, out var tenantId))
        {
            ctx.Response.StatusCode = 401; return;      // authenticated but no tenant
        }
        ctx.RequestServices.GetRequiredService<TenantContext>().TenantId = tenantId;
    }
    await next(ctx);
});

// 2. enforce in the data layer, for every entity, automatically
public sealed class AppDb(DbContextOptions<AppDb> o, TenantContext tenant) : DbContext(o)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes()
                 .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(p, nameof(ITenantOwned.TenantId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenant))),
                p);
            b.Entity(et.ClrType).HasQueryFilter(filter);
            b.Entity(et.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
        }
    }
    public Guid CurrentTenant => tenant.TenantId;

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<ITenantOwned>()
                          .Where(e => e.State == EntityState.Added))
            e.Entity.TenantId = tenant.TenantId;        // stamp on insert
        foreach (var e in ChangeTracker.Entries<ITenantOwned>()
                          .Where(e => e.State is EntityState.Modified))
            if (e.Entity.TenantId != tenant.TenantId)   // and refuse cross-tenant writes
                throw new CrossTenantWriteException(e.Entity.GetType().Name);
        return base.SaveChangesAsync(ct);
    }
}
```

``` csharp
// 3. the test that proves it — worth more than the code above
[Fact]
public async Task Tenant_a_cannot_see_tenant_b_data()
{
    await Seed(tenantA, new Order { Id = orderA });
    await Seed(tenantB, new Order { Id = orderB });

    var clientA = factory.CreateClientFor(tenantA);
    (await clientA.GetAsync($"/api/v1/orders/{orderB}")).StatusCode
        .Should().Be(HttpStatusCode.NotFound);          // not 403 — no existence leak

    var list = await clientA.GetFromJsonAsync<OrderDto[]>("/api/v1/orders");
    list!.Should().OnlyContain(o => o.Id == orderA);
}

// 4. and the escape hatches, audited
public async Task<List<Order>> AdminCrossTenantQuery(string reason)
{
    _audit.Record("cross-tenant-query", _user.Id, reason);   // never silent
    return await _db.Orders.IgnoreQueryFilters().ToListAsync();
}
```

> **\[Trap\]**
>
> **Common trap.** Query filters do not cover raw SQL, Dapper, `ExecuteUpdate`, `ExecuteDelete` or anything that runs outside the DbContext — and they are silently absent in a `BackgroundService` where no request set the tenant. For genuinely regulated data, add database-level row-level security so the enforcement survives your application code entirely.

<a id="be-w24"></a>

#### Zero-downtime schema change: expand and contract

Renaming `Orders.Notes` to `Orders.CustomerNote` with a single migration takes the site down: old pods write the old column, the new column does not exist for them, and a rollback loses data. The safe path is four deploys.

``` text
expand → migrate → switch → contract

deploy 1  EXPAND    add customer_note (nullable). Old code ignores it.
                    ✔ old and new schema both work with old code

deploy 2  DUAL-WRITE new code writes BOTH columns, reads notes
                    ✔ safe to roll back — old code still functional

  (job)   BACKFILL  copy notes → customer_note in batches, throttled
                    ✔ no long transaction, resumable, watch replica lag

deploy 3  SWITCH    new code reads customer_note, still writes both
                    ✔ verify: SELECT count(*) WHERE customer_note IS NULL
                              AND notes IS NOT NULL   → must be 0

deploy 4  CONTRACT  stop writing notes; drop the column in a later migration
                    ✔ only after every instance is on deploy 3
```

``` csharp
// deploy 1 — additive only, no rewrite, no lock
public partial class AddCustomerNote : Migration
{
    protected override void Up(MigrationBuilder mb) =>
        mb.AddColumn<string>("customer_note", "orders", nullable: true);
        // ⚠ adding a NOT NULL column WITH a default rewrites the whole table
        //   on older engines. Nullable now, constraint at contract time.
}

// deploy 2 — the entity writes both, reads the old one
public string? Notes
{
    get => _notes;
    set { _notes = value; CustomerNote = value; }   // dual write
}

// the backfill: batched, throttled, resumable
public async Task BackfillAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var affected = await db.Database.ExecuteSqlAsync($"""
            UPDATE orders SET customer_note = notes
            WHERE id IN (
                SELECT id FROM orders
                WHERE customer_note IS NULL AND notes IS NOT NULL
                ORDER BY id LIMIT 5000)
            """, ct);

        log.LogInformation("Backfilled {Rows}", affected);
        if (affected == 0) break;
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);   // let replicas keep up
    }
}
```

| Change                    | Safe in one deploy?                      | If not, the path                                |
|---------------------------|------------------------------------------|-------------------------------------------------|
| Add a nullable column     | Yes                                      | —                                               |
| Add NOT NULL with default | Engine-dependent — can rewrite the table | Add nullable, backfill, then add the constraint |
| Rename a column           | **No**                                   | Expand / dual-write / switch / contract         |
| Drop a column             | **No**                                   | Stop reading, deploy, then drop later           |
| Add an index              | Only with CONCURRENTLY / ONLINE          | Build concurrently, verify validity             |
| Change a column type      | **No**                                   | New column + backfill + switch                  |
| Add a foreign key         | Locks both tables                        | Add NOT VALID, then VALIDATE separately         |

> **\[Why it matters\]** Every step here exists so that *two versions of your code run against one database at the same time* — which is exactly what a rolling deploy guarantees, and what a blue-green cutover does not remove. Migrations that assume a single code version are the most common cause of deployment-time outages.
