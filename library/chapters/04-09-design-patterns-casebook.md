---
id: "4-9"
number: "4.9"
title: "Design patterns casebook"
part: 4
part_title: "Designing programs"
words: 24903
minutes: 108
---

# 4.9 Design patterns casebook

> **Quick look.** Eighteen design patterns applied to real production problems, each with before and after code, what it cost and when not to use it.
>
> - Factory over keyed DI services picks a notification channel per tenant without if/else at every call site.
> - Builders with sensible defaults stop test arrange blocks breaking on every new constructor parameter.
> - Adapter hides two payment gateways behind one port; the money and error mapping is the part to test.
> - Decorators add audit and timing to every command handler without editing one; Proxy makes an expensive renderer lazy and reusable.
> - Facade gives one checkout entry point so the API, admin tool and import cannot drift apart.
> - Strategy, Template Method and State replace growing switches and contradictory status flags.
> - Chain of Responsibility (DelegatingHandler), Command, domain events and a small mediator pipeline remove copy-pasted cross-cutting code.
> - Every case ends with the cost: several say a plain switch, a method or a nullable value would have been the better choice.


Chapter [4.7](04-07-design-patterns.md) explains what each design pattern is. This chapter shows 18 of them earning their place in production code. Each case starts from a real situation and the code that caused trouble, shows what went wrong and the refactor that fixed it, and ends with what the fix cost and when you should not reach for the pattern at all.

The code is C# on .NET 10 with ASP.NET Core and EF Core, but the shapes carry over to Java, TypeScript or Go almost unchanged. A pattern is worth its extra types only when the problem in the "Before" code is one you actually have. Several cases end by saying that a plain `switch`, a method or a nullable value would have been the better choice.

**How each case is laid out:** the situation, the code before, what goes wrong, the code after, why it works, the result, the costs and when not to use it, and a one-line lesson.

### Find the case for your problem

| Pattern | The problem it solved | Group |
|---|---|---|
| Factory | [Adding WhatsApp meant editing six notification call sites](#case-factory-keyed-senders) | Creational |
| Builder | [Adding one Order constructor parameter broke 140 tests](#case-builder-test-data) | Creational |
| Abstract Factory | [Download links 404'd because storage parts came from two editions](#case-abstract-factory-storage-family) | Creational |
| Adapter | [Two payment providers, two money formats, one checkout](#case-adapter-payment-gateways) | Structural |
| Decorator | [Audit logging copied into 60 handlers, and missing from 9](#case-decorator-command-audit) | Structural |
| Proxy | [Launching a browser for every invoice PDF](#case-proxy-pdf-renderer) | Structural |
| Facade | [Three copies of checkout, and the CSV import forgot tax](#case-facade-checkout) | Structural |
| Composite | [Promotion rules marketing could not express, or explain](#case-composite-discount-rules) | Structural |
| Flyweight | [3 million import rows and 2.4 GB of duplicate strings](#case-flyweight-import-strings) | Structural |
| Strategy | [One export switch, four formats, and an XLSX fix that broke CSV](#case-strategy-export-formats) | Behavioural |
| Template Method | [Five partner imports, five copies of the flow, two silent skips](#case-template-method-partner-imports) | Behavioural |
| State | [The renewal job charged 212 cancelled subscriptions](#case-state-subscription-billing) | Behavioural |
| Chain of Responsibility | [Token refresh and partner headers copied into 14 methods](#case-chain-delegating-handlers) | Behavioural |
| Command | [Bulk repricing timed out at 100 seconds and ran twice](#case-command-bulk-admin-actions) | Behavioural |
| Null Object | [31 null checks for customers without a loyalty account](#case-null-object-loyalty) | Behavioural |
| Visitor | [Saved searches broke when someone added an InList filter](#case-visitor-filter-tree) | Behavioural |
| Observer / domain events | [A Slack outage failed checkout for the biggest orders](#case-observer-domain-events) | Application |
| Mediator | [80 endpoints, 80 slightly different copies of the same plumbing](#case-mediator-pipeline-behaviours) | Application |

### Creational patterns

Deciding which object to create, and keeping construction out of business code.

<a id="case-factory-keyed-senders"></a>

#### Case 1 · Factory: Adding WhatsApp meant editing six notification call sites

*C# · ASP.NET Core · keyed DI*

**The situation.** A multi-tenant ordering and bookings platform sends customer notifications by email, SMS or WhatsApp, depending on each tenant's settings: about 1,900 tenants and 140,000 messages a day. The choice of sender was written inline at 6 call sites, from order shipped to subscription renewal notices, and each one built its sender with `new` from raw configuration. Adding WhatsApp took 9 working days and still missed one of them.

**Before**

*Before: one of six copies of the channel choice*

```csharp
public sealed class OrderShippedHandler(ITenantSettingsStore tenants, IConfiguration config)
{
    public async Task HandleAsync(OrderShipped e, CancellationToken ct)
    {
        var settings = await tenants.GetNotificationSettingsAsync(e.TenantId, ct);
        var text = $"Your order {e.OrderNumber} has shipped: {e.TrackingUrl}";

        if (settings.Channel == "sms")                                       // (1) raw string compare, 6 copies
        {
            var sms = new SmsGatewaySender(config["Sms:ApiKey"]!, config["Sms:From"]!);   // (2) built by hand
            await sms.SendAsync(e.CustomerPhone, text, ct);
        }
        else if (settings.Channel == "whatsapp")                             // (3) added to 5 of the 6 copies
        {
            var wa = new WhatsAppSender(new HttpClient(), config["WhatsApp:Token"]!);  // (4) new HttpClient per message
            await wa.SendTemplateAsync(e.CustomerPhone, "order_shipped", [e.OrderNumber], ct);
        }
        else
        {
            using var smtp = new SmtpSender(config["Smtp:Host"]!, int.Parse(config["Smtp:Port"]!),
                                            config["Smtp:User"]!, config["Smtp:Password"]!);
            await smtp.SendAsync(e.CustomerEmail, "Your order has shipped", text, ct);
        }
    }
}
```

**What goes wrong.** Each call site makes the same decision with its own copy of the rules. Four copies compare with `==` (1) and two with `OrdinalIgnoreCase`, so a tenant saved as "SMS" by the old admin UI got SMS for invoices and renewal notices and email for everything else. Senders are built by hand from configuration (2), so none of this code can be unit tested without real credentials, and (4) creates a new `HttpClient` per WhatsApp message, which exhausted sockets during a 40,000-message booking reminder run.

The WhatsApp change (3) had to edit every copy, and it found five. Payment-failed notices kept going out by email to WhatsApp tenants for three weeks, until a tenant asked why their dunning messages never arrived.

**Evidence**

*The WhatsApp commit, and the call site it missed*

```text
$ git show --stat --format='%h %s' a41f9c2
a41f9c2 Add WhatsApp as a tenant notification channel
 src/Orders/OrderShippedHandler.cs        | 19 ++++++++++++++++---
 src/Orders/OrderCancelledHandler.cs      | 18 +++++++++++++++---
 src/Billing/InvoiceIssuedHandler.cs      | 21 +++++++++++++++++----
 src/Bookings/BookingReminderJob.cs       | 23 +++++++++++++++++++----
 src/Subscriptions/RenewalNoticeJob.cs    | 17 +++++++++++++++--
 5 files changed, 82 insertions(+), 16 deletions(-)

$ git grep -l '"sms"' -- src/
src/Billing/InvoiceIssuedHandler.cs
src/Billing/PaymentFailedNotifier.cs          <- not in the commit
src/Bookings/BookingReminderJob.cs
src/Orders/OrderCancelledHandler.cs
src/Orders/OrderShippedHandler.cs
src/Subscriptions/RenewalNoticeJob.cs
```

**After**

*After: one interface, one key per channel*

```csharp
public enum NotificationChannel { Email, Sms, WhatsApp }

public interface INotificationSender
{
    Task SendAsync(Recipient to, NotificationMessage message, CancellationToken ct);
}

// Each sender renders its own template variant and gets its dependencies from DI
public sealed class SmsGatewaySender(IHttpClientFactory http, IOptions<SmsOptions> options)
    : INotificationSender { /* ... */ }

// Program.cs
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection("Sms"));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient("sms");
builder.Services.AddHttpClient("whatsapp");

builder.Services.AddKeyedScoped<INotificationSender, SmtpEmailSender>(NotificationChannel.Email);
builder.Services.AddKeyedScoped<INotificationSender, SmsGatewaySender>(NotificationChannel.Sms);
builder.Services.AddKeyedScoped<INotificationSender, WhatsAppSender>(NotificationChannel.WhatsApp);
builder.Services.AddScoped<NotificationSenderFactory>();

var app = builder.Build();

// Fail at startup, not on some tenant's first WhatsApp message
using (var scope = app.Services.CreateScope())
{
    foreach (var channel in Enum.GetValues<NotificationChannel>())
        scope.ServiceProvider.GetRequiredKeyedService<INotificationSender>(channel);
}
```

*After: the factory, a call site, and a fixed-channel consumer*

```csharp
public sealed class NotificationSenderFactory(IServiceProvider services, ITenantSettingsStore tenants)
{
    public async Task<INotificationSender> ForAsync(TenantId tenantId, NotificationKind kind, CancellationToken ct)
    {
        var settings = await tenants.GetNotificationSettingsAsync(tenantId, ct);   // cached for 5 minutes
        NotificationChannel channel = settings.ChannelFor(kind);                   // parsed to the enum on save
        return services.GetRequiredKeyedService<INotificationSender>(channel);
    }
}

public sealed class OrderShippedHandler(NotificationSenderFactory senders)
{
    public async Task HandleAsync(OrderShipped e, CancellationToken ct)
    {
        var sender = await senders.ForAsync(e.TenantId, NotificationKind.OrderShipped, ct);
        await sender.SendAsync(
            new Recipient(e.CustomerEmail, e.CustomerPhone),
            new NotificationMessage("order-shipped", new { e.OrderNumber, e.TrackingUrl }),
            ct);
    }
}

// Password reset is email-only by policy, so it asks for that sender directly
public sealed class PasswordResetNotifier(
    [FromKeyedServices(NotificationChannel.Email)] INotificationSender email)
{
    public Task SendAsync(string address, Uri resetLink, CancellationToken ct) =>
        email.SendAsync(new Recipient(address, Phone: null),
                        new NotificationMessage("password-reset", new { resetLink }), ct);
}
```

**Why it works.** The decision "which channel for this tenant and this kind of message" now lives in one method, and building each sender is the container's job. A call site asks for a sender and sends. It does not know that SMS exists. Adding a channel is one class, one registration and one enum value, and the startup loop refuses to boot if the registration is missing.

Keyed services do the lookup, so the factory has no `switch` to keep in step, and each sender gets its options and `IHttpClientFactory` like any other service. The factory is the only class that touches `IServiceProvider`, which keeps the service-locator style in one small, well-named place. Where the channel never varies, `[FromKeyedServices]` injects the right sender and no factory is involved.

**Result.** When push notifications to the mobile app were added in August, the change was one class, one registration line and one enum value, merged in a day, with no call site touched. The six handlers lost about 30 lines each and gained unit tests with fake senders, and the socket exhaustion stopped because WhatsApp now uses a pooled named client.

> **Costs, and when not to.** Keyed services move the mapping from code you can read into registrations you have to go and find, and a missing key is an `InvalidOperationException` at runtime, not a compile error. The startup check covers that only while someone keeps it. The factory also hides which channels a handler can end up using, which makes a single call site slightly harder to reason about.
>
> A plain `switch` is better when the set is small and stable and the senders are cheap to create: inject the three senders into the factory and return `channel switch { NotificationChannel.Email => email, NotificationChannel.Sms => sms, NotificationChannel.WhatsApp => whatsApp }`. Everything is on one screen, a missing case shows up in the diff, and if you leave out the `_` arm the compiler's CS8509 warning flags a new channel the switch does not handle. Prefer real code over a key lookup when the choice needs more than a key, such as falling back to email when the customer has no phone number. And if there is only one channel, inject the sender and skip the factory.

> **Lesson:** A factory earns its place when the same choice is made in many places and the set of products grows; for a small fixed set, one `switch` in one method is enough.

<a id="case-builder-test-data"></a>

#### Case 2 · Builder: Adding one Order constructor parameter broke 140 tests

*C# · xUnit*

**The situation.** The order service has about 300 unit and integration tests that need an `Order` aggregate in some state: draft, paid, shipped, part-refunded. Each test builds it by hand, so arrange blocks average 25 lines. In March the team added a `SalesChannel` parameter to the `Order` constructor for marketplace orders, and 140 tests stopped compiling. Two developers spent most of a day editing tests that did not care about the sales channel at all.

**Before**

*Before: a typical arrange block*

```csharp
[Fact]
public void Shipped_order_cannot_be_cancelled()
{
    var placedAt = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    var address = new Address("12 Mill Lane", null, "Leeds", "LS1 4AP", "GB");
    var order = new Order(                                   // (1) 140 tests call the constructor directly
        new OrderId(Guid.NewGuid()),
        new CustomerId(Guid.NewGuid()),
        address,
        Currency.GBP,
        SalesChannel.Web,                                    // (2) added in March: 140 call sites edited by hand
        placedAt);
    order.AddLine(new ProductId(1001), 2, new Money(12.50m, Currency.GBP));
    order.AddLine(new ProductId(1002), 1, new Money(30.00m, Currency.GBP));
    order.MarkPaid(new PaymentId("pay_8812"), placedAt.AddMinutes(5));   // (3) Ship() throws unless paid,
    order.Ship("DPD", "15501234567890", placedAt.AddDays(1));            //     so every test repeats the sequence

    var result = order.Cancel("customer changed mind");      // (4) the line this test is about

    Assert.False(result.IsSuccess);
    Assert.Equal(OrderStatus.Shipped, order.Status);
}
```

**What goes wrong.** The test checks one rule: a shipped order cannot be cancelled. Thirteen of its lines are about constructing an order, and only (4) is the behaviour under test. Because 140 tests call the constructor directly (1), any change to its signature (2) breaks all of them, whether or not the test cares. Line (3) shows a second coupling: tests have to know the legal sequence of state changes, so when `Ship` started requiring a paid order last year, 60 tests that had skipped `MarkPaid` failed at runtime.

The arrange blocks also hide what matters. A reader cannot tell whether `Currency.GBP` or the second line item is relevant to the assertion, so nobody dares to delete anything, and the blocks only grow.

**Evidence**

*Build after adding the parameter*

```text
$ dotnet build tests/Orders.Tests
  Orders.Tests failed with 140 error(s) (6.8s)
    tests/Orders.Tests/Cancellation/CancelOrderTests.cs(14,21): error CS7036: There is no argument given
      that corresponds to the required parameter 'placedAt' of 'Order.Order(OrderId, CustomerId, Address,
      Currency, SalesChannel, DateTimeOffset)'
    tests/Orders.Tests/Refunds/RefundLineTests.cs(22,21): error CS7036: There is no argument given
      that corresponds to the required parameter 'placedAt' of 'Order.Order(...)'
    ...
Build failed with 140 error(s) in 9.4s
```

**After**

*After: a builder with defaults that goes through the aggregate*

```csharp
public sealed class OrderBuilder
{
    private CustomerId _customer = new(Guid.Parse("8c0e6a4e-3f7b-4f2e-9d8a-1b2c3d4e5f60"));
    private Address _address = new("12 Mill Lane", null, "Leeds", "LS1 4AP", "GB");
    private Currency _currency = Currency.GBP;
    private SalesChannel _channel = SalesChannel.Web;
    private DateTimeOffset _placedAt = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private readonly List<(int ProductId, int Quantity, decimal UnitPrice)> _lines = [];
    private bool _paid, _shipped;

    public static OrderBuilder AnOrder() => new();

    public OrderBuilder For(CustomerId customer) { _customer = customer; return this; }
    public OrderBuilder In(Currency currency) { _currency = currency; return this; }
    public OrderBuilder Via(SalesChannel channel) { _channel = channel; return this; }
    public OrderBuilder ShippingTo(Address address) { _address = address; return this; }
    public OrderBuilder WithLine(int productId, int quantity = 1, decimal unitPrice = 10m)
    {
        _lines.Add((productId, quantity, unitPrice));
        return this;
    }
    public OrderBuilder WithLines(int count)
    {
        for (var i = 0; i < count; i++) WithLine(1001 + i);
        return this;
    }
    public OrderBuilder Paid() { _paid = true; return this; }
    public OrderBuilder Shipped() { _paid = true; _shipped = true; return this; }

    public Order Build()
    {
        // The only constructor call in the test project
        var order = new Order(OrderId.New(), _customer, _address, _currency, _channel, _placedAt);
        if (_lines.Count == 0) WithLine(1001);
        foreach (var (productId, quantity, unitPrice) in _lines)
            order.AddLine(new ProductId(productId), quantity, new Money(unitPrice, _currency));
        if (_paid) order.MarkPaid(new PaymentId("pay_test"), _placedAt.AddMinutes(5));
        if (_shipped) order.Ship("DPD", "15501234567890", _placedAt.AddDays(1));
        return order;
    }
}
```

*Usage*

```csharp
using static Orders.Tests.Builders.OrderBuilder;

public class OrderCancellationTests
{
    [Fact]
    public void Shipped_order_cannot_be_cancelled()
    {
        var order = AnOrder().WithLines(2).Shipped().Build();

        var result = order.Cancel("customer changed mind");

        Assert.False(result.IsSuccess);
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Refund_is_issued_in_the_order_currency()
    {
        // The currency matters to this assertion, so the test states it even though GBP is the default
        var order = AnOrder().In(Currency.EUR).WithLine(1001, quantity: 2, unitPrice: 12.50m).Paid().Build();

        var refund = order.RefundLine(new ProductId(1001), quantity: 1);

        Assert.Equal(new Money(12.50m, Currency.EUR), refund.Amount);
    }
}
```

*When a record and `with` are enough*

```csharp
// API tests post a request DTO. It is a record with no invariants, so one valid
// instance plus `with` covers every variation, and a builder would add nothing.
public sealed record PlaceOrderRequest(Guid CustomerId, string Currency, IReadOnlyList<LineRequest> Lines, string Postcode);

public class PlaceOrderEndpointTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly PlaceOrderRequest Valid =
        new(Guid.Parse("8c0e6a4e-3f7b-4f2e-9d8a-1b2c3d4e5f60"), "GBP", [new LineRequest(1001, 2)], "LS1 4AP");

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    public async Task Rejects_unknown_currency(string currency)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/orders", Valid with { Currency = currency });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

**Why it works.** The builder owns the one call to the constructor, so a signature change is one edit in one file. Its defaults describe a valid, boring order, and a test states only what it depends on, which makes the relevant data stand out. `Shipped()` goes through `MarkPaid` and `Ship`, the same public methods production code uses, so the builder cannot produce a state the domain would reject, and when the legal sequence changes it changes in one place.

The method names read like the premise of the test: `AnOrder().WithLines(2).Shipped()`. The arrange block becomes a sentence about the scenario instead of a recipe for constructing objects.

**Result.** Arrange blocks went from an average of 25 lines to 2, and the test project shrank by about 6,500 lines. In August `Order` gained a `TaxRegion` parameter: one line in `OrderBuilder` changed and no test was touched.

> **Costs, and when not to.** A builder is code you maintain, and defaults become a trap when a test silently depends on one. If an assertion relies on the currency being GBP, the test must say `.In(Currency.GBP)` even though that is the default; otherwise someone changes the default and unrelated tests fail with confusing messages. Keep defaults boring and stable, and resist adding a method for every field: a builder with 40 `WithX` methods is the constructor again with more typing. Never let a builder set private state through reflection to reach a state quickly, because those tests then pass against objects production can never create.
>
> Do not write a builder for types without invariants. A request DTO or other record needs one valid instance and `with` expressions, and a mutable options class needs an object initialiser. Builders pay off for aggregates whose construction involves a legal sequence of calls and that dozens of tests need.

> **Lesson:** Put the constructor call in one builder with boring defaults, and let each test state only the data its assertion depends on.

<a id="case-abstract-factory-storage-family"></a>

#### Case 3 · Abstract Factory: Download links 404'd because storage parts came from two editions

*C# · ASP.NET Core · Azure SDK*

**The situation.** An invoicing product ships in two editions from one codebase. The on-prem edition stores documents on a file share, signs download links with an HMAC that its own `/files` endpoint checks, and queues background jobs in a SQL table. The cloud edition uses Azure Blob Storage with SAS links and Azure Service Bus. In September, Hartwell Logistics moved from on-prem to cloud, and for three days every invoice download link in their tenant returned 404.

**Before**

*Before: each part chosen by its own setting*

```csharp
// Program.cs
builder.Services.AddSingleton<IBlobStore>(sp =>
    builder.Configuration["Storage:Provider"] == "AzureBlob"                  // (1)
        ? ActivatorUtilities.CreateInstance<AzureBlobStore>(sp)
        : ActivatorUtilities.CreateInstance<FileShareBlobStore>(sp));

builder.Services.AddSingleton<ISignedUrlGenerator>(sp =>
    builder.Configuration["Downloads:UrlSigner"] == "Sas"                     // (2) independent of (1)
        ? ActivatorUtilities.CreateInstance<BlobSasUrlGenerator>(sp)
        : ActivatorUtilities.CreateInstance<HmacFileUrlGenerator>(sp));

if (builder.Configuration["Jobs:Queue"] == "ServiceBus")                      // (3) and of (1) and (2)
    builder.Services.AddSingleton<IJobQueue, ServiceBusJobQueue>();
else
    builder.Services.AddScoped<IJobQueue, SqlTableJobQueue>();
```

*Before: Hartwell's production settings, built from the on-prem template*

```json
{
  "Storage":   { "Provider": "AzureBlob" },
  "Downloads": { "UrlSigner": "Hmac" },
  "Jobs":      { "Queue": "ServiceBus" }
}
```

**What goes wrong.** Each interface was chosen independently (1, 2, 3), so the code allowed combinations that cannot work. The operations team built Hartwell's cloud settings from the on-prem template and changed `Storage:Provider` and `Jobs:Queue`, but not `Downloads:UrlSigner`. Invoices were uploaded to Blob Storage, and the link generator produced HMAC links to `/files`, an endpoint that reads from a file share the cloud container does not have.

Nothing failed at startup or in the health check, because every registration resolved. The blob store and the URL generator share assumptions the type system never saw: the same account, the same container, the same path scheme. Those shared assumptions are what make them a family.

**Evidence**

*Access log and blob check, Hartwell tenant*

```text
/files/* requests, 11 to 14 Sep 2026
GET /files/invoices/2026/09/INV-48121.pdf?exp=1789380000&sig=9f1c07...  404  2 ms
GET /files/invoices/2026/09/INV-48122.pdf?exp=1789380060&sig=41ab9e...  404  1 ms
...
requests: 1,946    404: 1,946

$ az storage blob exists --account-name hartwelldocs --container-name documents \
    --name invoices/2026/09/INV-48121.pdf
{
  "exists": true
}
```

**After**

*After: the ports, and one switch that picks a family*

```csharp
public interface IBlobStore
{
    Task SaveAsync(string path, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
}

public interface ISignedUrlGenerator
{
    Uri ForDownload(string path, TimeSpan validFor);
}

public interface IJobQueue
{
    Task EnqueueAsync<TJob>(TJob job, CancellationToken ct) where TJob : notnull;
}

public enum Edition { OnPrem = 1, Cloud = 2 }

public static class StorageRegistration
{
    // The only switch, and the only storage setting operations can get wrong
    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration config)
    {
        var edition = Enum.Parse<Edition>(config.GetRequiredSection("Edition").Value!, ignoreCase: true);
        return edition switch
        {
            Edition.Cloud  => services.AddCloudStorage(config.GetRequiredSection("Cloud")),
            Edition.OnPrem => services.AddOnPremStorage(config.GetRequiredSection("OnPrem")),
            _ => throw new InvalidOperationException($"Unsupported edition '{edition}'."),
        };
    }
}

// Program.cs
builder.Services.AddStorage(builder.Configuration);
```

*After: one registration module per family*

```csharp
public static class CloudStorageModule
{
    public static IServiceCollection AddCloudStorage(this IServiceCollection services, IConfigurationSection cloud)
    {
        services.Configure<CloudStorageOptions>(cloud);
        services.AddSingleton(new BlobServiceClient(cloud["BlobConnectionString"]!));
        services.AddSingleton(new ServiceBusClient(cloud["ServiceBusConnectionString"]!));
        services.AddSingleton<IBlobStore, AzureBlobStore>();
        services.AddSingleton<ISignedUrlGenerator, BlobSasUrlGenerator>();
        services.AddSingleton<IJobQueue, ServiceBusJobQueue>();
        return services;
    }
}

internal sealed class BlobSasUrlGenerator(BlobServiceClient blobs, IOptions<CloudStorageOptions> options)
    : ISignedUrlGenerator
{
    // Signs for the same account and container that AzureBlobStore writes to
    public Uri ForDownload(string path, TimeSpan validFor) =>
        blobs.GetBlobContainerClient(options.Value.Container)
             .GetBlobClient(path)
             .GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(validFor));
}

public static class OnPremStorageModule
{
    public static IServiceCollection AddOnPremStorage(this IServiceCollection services, IConfigurationSection onPrem)
    {
        services.Configure<FileShareOptions>(onPrem.GetSection("FileShare"));
        services.AddSingleton<IBlobStore, FileShareBlobStore>();
        services.AddSingleton<ISignedUrlGenerator, HmacFileUrlGenerator>();   // links to /files, checked by FileDownloadEndpoint
        services.AddScoped<IJobQueue, SqlTableJobQueue>();                    // writes through AppDbContext, so scoped
        return services;
    }
}
```

*A test that each edition registers one whole family*

```csharp
public class StorageRegistrationTests
{
    [Theory]
    [InlineData("Cloud", typeof(AzureBlobStore), typeof(BlobSasUrlGenerator), typeof(ServiceBusJobQueue))]
    [InlineData("OnPrem", typeof(FileShareBlobStore), typeof(HmacFileUrlGenerator), typeof(SqlTableJobQueue))]
    public void Edition_registers_one_whole_family(string edition, Type blobs, Type urls, Type queue)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Edition"] = edition,
                ["Cloud:BlobConnectionString"] = "UseDevelopmentStorage=true",
                ["Cloud:ServiceBusConnectionString"] =
                    "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==",
                ["OnPrem:FileShare:RootPath"] = "/srv/share",
            })
            .Build();

        var services = new ServiceCollection().AddStorage(config);

        // Single() also fails if anything else registers one of these interfaces
        Assert.Equal(blobs, ImplementationOf<IBlobStore>(services));
        Assert.Equal(urls, ImplementationOf<ISignedUrlGenerator>(services));
        Assert.Equal(queue, ImplementationOf<IJobQueue>(services));
    }

    private static Type? ImplementationOf<T>(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(T)).ImplementationType;
}
```

**Why it works.** The family is now the unit of choice. A deployment picks an edition with one setting, and the module for that edition registers every member, so a cloud blob store with an on-prem link generator can no longer be configured. The knowledge that SAS links must be signed for the container the store writes to sits next to both registrations, where the next person adding a member will see it.

This is the Abstract Factory from 4.7 in the form .NET applications actually use: the DI container does the creating, and each registration module is a concrete factory that produces one consistent family. A hand-written `IStorageFactory` with `CreateBlobStore()`, `CreateUrlGenerator()` and `CreateQueue()` methods is only worth it when the family must be chosen at runtime, for example per tenant in a hybrid deployment.

**Result.** The three per-component settings were deleted and replaced by `Edition`, and the test runs in CI for both editions, so this mistake can no longer be expressed in configuration. All 1,946 failed downloads were for documents that existed in Blob Storage; once Hartwell's edition was set, the same links were regenerated as SAS links and worked without any data migration.

> **Costs, and when not to.** You lose mix and match. If a real customer needs on-prem files with a cloud queue, that is a third family with its own module and its own test, which is the honest cost of supporting it. Members of a family can also differ in lifetime: `SqlTableJobQueue` is scoped and `ServiceBusJobQueue` is a singleton, so consumers must work with the narrowest lifetime. A singleton that takes `IJobQueue` will work in the cloud and fail scope validation on-prem.
>
> Abstract Factory is the wrong choice when the parts vary independently and every combination is valid, such as an email provider and an SMS provider: separate settings are correct there. And do not write a factory class on top of the container just to have one. If the family is fixed at startup, the registration modules are the factory.

> **Lesson:** When implementations only work together, make the family the unit of choice: one setting, one registration module per family, one test per family.

### Structural patterns

Wrapping, combining and hiding objects so the rest of the code sees a simpler shape.

<a id="case-adapter-payment-gateways"></a>

#### Case 4 · Adapter: Two payment providers, two money formats, one checkout

*C# · HttpClient · xUnit*

**The situation.** A subscription retailer takes card payments through two providers. About 60% of volume still goes through a legacy acquirer gateway with an XML API: amounts as decimal strings, ISO 8583 response codes such as "00", "05" and "51", and an SDK that throws an exception on every decline. Newer tenants use a REST provider that takes amounts in minor units, supports idempotency keys and returns typed JSON errors. Checkout talked to both directly.

**Before**

*Before: provider details inside checkout*

```csharp
public async Task<IResult> PayAsync(Guid orderId, PayRequest req, CancellationToken ct)
{
    var order = await db.Orders.Include(o => o.Tenant).SingleAsync(o => o.Id == orderId, ct);
    if (order.Tenant.PaymentProvider == "legacy")
    {
        try
        {
            var resp = await legacyClient.AuthoriseAsync(merchantId,
                order.Total.ToString(),                          // (1) culture-sensitive: "12,50" on de-DE hosts
                order.Currency, req.CardToken);
            order.MarkAuthorised(resp.TransactionRef);
        }
        catch (LegacyDeclineException ex)                        // (2) an ordinary decline arrives as an exception
        {
            logger.LogError(ex, "Payment failed for {Order}", orderId);
            return Results.Problem("Payment failed, please try again");   // (3) "51" (no funds) is told to retry
        }
    }
    else
    {
        var resp = await restPay.PostAsJsonAsync("v2/authorisations", new
        {
            amount_minor = (long)(order.Total * 100),            // (4) wrong for JPY (0 decimals) and BHD (3)
            currency = order.Currency,
            source = req.CardToken,                              // (5) no idempotency key: retry after timeout charges twice
        }, ct);
        if (!resp.IsSuccessStatusCode)
            return Results.Problem("Payment failed");            // (6) a decline and an outage look the same
        order.MarkAuthorised((await resp.Content.ReadFromJsonAsync<RestPayAuthorised>(ct))!.Id);
    }
    await db.SaveChangesAsync(ct);
    return Results.Ok();
}
```

**What goes wrong.** Every provider quirk leaked into checkout. (1) formats the amount with the server's culture, so a host built with `de-DE` sent `12,50` and the gateway answered with response code 30, format error. (4) assumes two decimal places, so a 12,800 JPY order was sent as 1,280,000 minor units, and only the provider's risk limit stopped 23 of them. (5) sends no idempotency key, so a timeout followed by the client's retry produced duplicate authorisations.

Errors were worse. The legacy SDK throws on decline (2), and checkout logged that as a fault: 412 on-call pages in July were ordinary declines. Customers with insufficient funds were told to try again (3), and on the REST side declines and outages were indistinguishable (6), so support could not tell a customer whether to use another card or wait.

**Evidence**

*Gateway logs and pages, July*

```text
Legacy gateway request log, host checkout-03 (culture de-DE), 2026-07-08
  <Authorise><Amount>12,50</Amount><Currency>EUR</Currency>...    -> <ResponseCode>30</ResponseCode> (format error)
  58 requests that day, all from checkout-03

REST provider risk events, July
  rule amount_over_limit (JPY > 500,000): 23 blocked
  e.g. amount_minor=1280000 currency=JPY    (order total 12,800 JPY)

On-call pages from LegacyDeclineException, July: 412
  response code 05: 251    51: 140    54: 21
```

**After**

*After: one port, one result type, money that knows its exponent*

```csharp
public interface IPaymentGateway
{
    Task<AuthorisationResult> AuthoriseAsync(AuthorisationRequest request, CancellationToken ct);
}

public sealed record AuthorisationRequest(Guid PaymentId, Money Amount, string CardToken);

public abstract record AuthorisationResult
{
    public sealed record Approved(string ProviderReference) : AuthorisationResult;
    public sealed record Declined(DeclineReason Reason, string ProviderCode) : AuthorisationResult;
    public sealed record Failed(string ProviderCode, bool SafeToRetry) : AuthorisationResult;
}

public enum DeclineReason { InsufficientFunds, DoNotHonour, ExpiredCard, SuspectedFraud, Other }

public readonly record struct Money(decimal Amount, string Currency)
{
    // ISO 4217 exponents for the currencies we sell in; anything else is refused, not guessed
    public int Exponent => Currency switch
    {
        "JPY" or "KRW" => 0,
        "GBP" or "EUR" or "USD" or "CHF" or "SEK" => 2,
        "BHD" or "KWD" => 3,
        _ => throw new NotSupportedException($"No exponent configured for {Currency}"),
    };

    public long ToMinorUnits()
    {
        var scaled = Amount * (Exponent switch { 0 => 1m, 2 => 100m, _ => 1000m });
        return scaled == decimal.Truncate(scaled)
            ? (long)scaled
            : throw new InvalidOperationException($"{Amount} has more decimals than {Currency} allows");
    }

    public string ToMajorString() => Amount.ToString("F" + Exponent, CultureInfo.InvariantCulture);
}
```

*After: two adapters, each translating its own provider*

```csharp
public sealed class LegacyGatewayAdapter(LegacyGatewayClient client, IOptions<LegacyGatewayOptions> options)
    : IPaymentGateway
{
    public async Task<AuthorisationResult> AuthoriseAsync(AuthorisationRequest request, CancellationToken ct)
    {
        try
        {
            var response = await client.AuthoriseAsync(options.Value.MerchantId,
                request.Amount.ToMajorString(), request.Amount.Currency, request.CardToken);
            return LegacyCodes.Map(response.ResponseCode, response.TransactionRef);
        }
        catch (LegacyDeclineException ex) { return LegacyCodes.Map(ex.ResponseCode, null); }  // a decline is a result
        catch (TimeoutException) { return new AuthorisationResult.Failed("timeout", SafeToRetry: false); }  // may have succeeded
    }
}

public sealed class RestPayAdapter(HttpClient http) : IPaymentGateway
{
    public async Task<AuthorisationResult> AuthoriseAsync(AuthorisationRequest request, CancellationToken ct)
    {
        var body = new RestPayAuthorisation(request.Amount.ToMinorUnits(), request.Amount.Currency, request.CardToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v2/authorisations")
        {
            Content = JsonContent.Create(body, options: RestPayJson.Options),
        };
        message.Headers.Add("Idempotency-Key", request.PaymentId.ToString());   // a retry cannot charge twice
        try
        {
            using var response = await http.SendAsync(message, ct);
            if (response.IsSuccessStatusCode)
                return new AuthorisationResult.Approved(
                    (await response.Content.ReadFromJsonAsync<RestPayAuthorised>(RestPayJson.Options, ct))!.Id);
            var error = response.Content.Headers.ContentType?.MediaType == "application/json"
                ? await response.Content.ReadFromJsonAsync<RestPayError>(RestPayJson.Options, ct)
                : null;
            return RestPayErrors.Map((int)response.StatusCode, error?.Error.Code);
        }
        catch (HttpRequestException) { return new AuthorisationResult.Failed("network", SafeToRetry: true); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { return new AuthorisationResult.Failed("timeout", SafeToRetry: true); }
    }
}
```

*The mapping tables, and the tests that pin them*

```csharp
internal static class LegacyCodes
{
    public static AuthorisationResult Map(string code, string? reference) => code switch
    {
        "00" when reference is not null => new AuthorisationResult.Approved(reference),
        "05" => new AuthorisationResult.Declined(DeclineReason.DoNotHonour, code),
        "51" => new AuthorisationResult.Declined(DeclineReason.InsufficientFunds, code),
        "54" => new AuthorisationResult.Declined(DeclineReason.ExpiredCard, code),
        "91" or "96" => new AuthorisationResult.Failed(code, SafeToRetry: true),   // issuer or switch unavailable
        _ => new AuthorisationResult.Failed(code, SafeToRetry: false),
    };
}

internal static class RestPayErrors
{
    public static AuthorisationResult Map(int status, string? code) => (status, code ?? $"http_{status}") switch
    {
        (402, "insufficient_funds") => new AuthorisationResult.Declined(DeclineReason.InsufficientFunds, "insufficient_funds"),
        (402, "expired_card") => new AuthorisationResult.Declined(DeclineReason.ExpiredCard, "expired_card"),
        (402, var c) => new AuthorisationResult.Declined(DeclineReason.Other, c),
        (409 or 429 or >= 500, var c) => new AuthorisationResult.Failed(c, SafeToRetry: true),   // 409: same key in flight
        (_, var c) => new AuthorisationResult.Failed(c, SafeToRetry: false),
    };
}

public class PaymentMappingTests
{
    [Fact]
    public void Insufficient_funds_means_the_same_from_both_providers() => Assert.Equal(
        Assert.IsType<AuthorisationResult.Declined>(LegacyCodes.Map("51", null)).Reason,
        Assert.IsType<AuthorisationResult.Declined>(RestPayErrors.Map(402, "insufficient_funds")).Reason);

    [Theory]
    [InlineData("12.50", "EUR", 1250L, "12.50")]
    [InlineData("12800", "JPY", 12800L, "12800")]
    [InlineData("1.25", "BHD", 1250L, "1.250")]
    public void Money_uses_the_currency_exponent_and_invariant_culture(string amount, string currency, long minor, string major)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");   // the host culture that sent "12,50"
        var money = new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);
        Assert.Equal((minor, major), (money.ToMinorUnits(), money.ToMajorString()));
    }
}
```

**Why it works.** Each adapter translates one provider's dialect into the port's terms, and nothing provider-shaped gets past it. Money crosses the boundary as a `Money` value, and each adapter converts it the way its provider wants: invariant-culture major units with the right number of decimals for the legacy gateway, integer minor units for REST. A currency without a known exponent is refused, not guessed. Outcomes cross as a closed set of results: a decline is data with a reason, an outage is a failure with a flag that says whether a retry is safe, and only bugs remain exceptions.

That flag is where the two providers really differ. The REST provider honours idempotency keys, so retrying after a network error is safe. The legacy gateway has no idempotency, so a timeout there has an unknown outcome and must go to reconciliation, not to a retry. The adapter knows this, and checkout no longer needs to.

**Result.** Checkout dropped to one call and a `switch` over three result types. On-call pages from declines went from 412 a month to zero, the format errors and JPY overcharges stopped, and customers with no funds now see "declined: insufficient funds, try another card" instead of "try again". Moving a tenant between providers became a configuration change.

> **Costs, and when not to.** Adapters cost a mapping layer per provider, and the mapping is where the bugs live, so it needs tests like the ones above and a review whenever a provider adds codes. A common port also flattens: provider-specific features such as 3-D Secure challenges or partial authorisations either get a deliberate place in the port or are unavailable to checkout. Do not add a `RawResponse` escape hatch; callers will start parsing it and the adapter stops being the only place that knows the dialect. The exponent table is a similar trap if treated as universal: a few providers deviate from ISO 4217 for some currencies, and when that happens the conversion belongs in that adapter, not in `Money`.
>
> Do not introduce a port for a single provider you have no plan to replace and whose SDK already has decent types. Wrapping it adds a layer that mirrors the SDK one to one and translates nothing.

> **Lesson:** An adapter's job is translation: convert money and errors into your own types at the boundary, and pin that conversion with tests.

<a id="case-decorator-command-audit"></a>

#### Case 5 · Decorator: Audit logging copied into 60 handlers, and missing from 9

*C# · ASP.NET Core · Scrutor*

**The situation.** A billing back office runs 60 command handlers behind its admin API: refunds, credit notes, bank detail changes, plan changes. Regulation requires an audit row for every state-changing command, including failed attempts, and the operations team wants latency per command. Both were added by hand, one handler at a time, over two years. A compliance review in Q2 found the gaps.

**Before**

*Before: cross-cutting code inside each handler*

```csharp
public sealed class IssueRefundHandler(
    BillingDbContext db, IAuditLog audit, ICurrentUser user, ILogger<IssueRefundHandler> logger)
    : ICommandHandler<IssueRefund>
{
    public async Task HandleAsync(IssueRefund command, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();                        // (1) the same lines in 51 of 60 handlers
        try
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == command.InvoiceId, ct);
            invoice.Refund(command.Amount, command.Reason);
            await db.SaveChangesAsync(ct);

            await audit.WriteAsync(new AuditEntry
            {
                Command = "IssueRefund", UserId = user.Id,
                Subject = command.InvoiceId.ToString(), Outcome = AuditOutcome.Succeeded,
            }, ct);                                           // (2) only reached on success
        }
        finally
        {
            logger.LogInformation("IssueRefund took {Ms} ms", sw.ElapsedMilliseconds);   // (3) 7 formats across handlers
        }
    }
}
```

**What goes wrong.** The handler's real work is three lines; the rest is cross-cutting code repeated in every handler (1), and repetition by hand drifts. Nine handlers never got the audit call at all, including `ChangeBankDetails` and `ResetMfa`. In the others the audit write sits on the success path (2), so the 4,318 failed commands in the review period left no trail, which is the first thing an auditor asks about. Timing (3) was logged in seven different message templates, so no dashboard could aggregate it.

Every new handler starts as a copy of an old one, and whichever old one you copy decides whether the new one is audited.

**Evidence**

*Audit coverage check, Q1 2026*

```text
Command log joined to audit table, 1 Jan to 31 Mar 2026
  command types executed                      60
  command types with no audit rows             9   ChangeBankDetails, ResetMfa, MergeCustomers, ...
  failed executions                        4,318
  failed executions with an audit row          0
  distinct timing log message templates        7
```

**After**

*After: two open generic decorators*

```csharp
public sealed class AuditingHandler<TCommand>(
    ICommandHandler<TCommand> inner, IAuditLog audit, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<TCommand>
{
    public async Task HandleAsync(TCommand command, CancellationToken ct)
    {
        var entry = new AuditEntry
        {
            Command = typeof(TCommand).Name,
            UserId = user.Id,
            Subject = command is IHasAuditSubject s ? s.AuditSubject : null,   // commands opt in to naming a subject
            At = clock.GetUtcNow(),
        };
        try
        {
            await inner.HandleAsync(command, ct);
            entry.Outcome = AuditOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            entry.Outcome = ex is OperationCanceledException ? AuditOutcome.Cancelled : AuditOutcome.Failed;
            entry.Error = ex.GetType().Name;          // the type, not the message: messages can carry personal data
            throw;
        }
        finally
        {
            await audit.WriteAsync(entry, CancellationToken.None);   // own connection, so a rollback cannot remove it
        }
    }
}

public sealed class TimingHandler<TCommand>(ICommandHandler<TCommand> inner, CommandMetrics metrics)
    : ICommandHandler<TCommand>
{
    public async Task HandleAsync(TCommand command, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        try { await inner.HandleAsync(command, ct); }
        finally { metrics.Record(typeof(TCommand).Name, Stopwatch.GetElapsedTime(start)); }
    }
}
```

*Registration with Scrutor*

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CommandMetrics>();
builder.Services.AddScoped<IAuditLog, SqlAuditLog>();

builder.Services.Scan(scan => scan
    .FromAssemblyOf<IssueRefundHandler>()
    .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>))
                      .Where(t => !t.IsGenericTypeDefinition))     // keep the decorators themselves out
    .AsImplementedInterfaces()
    .WithScopedLifetime());

// Each Decorate wraps whatever is registered so far, so the LAST call is the OUTERMOST layer.
builder.Services.Decorate(typeof(ICommandHandler<>), typeof(TimingHandler<>));
builder.Services.Decorate(typeof(ICommandHandler<>), typeof(AuditingHandler<>));
// ICommandHandler<IssueRefund> now resolves to:
//   AuditingHandler<IssueRefund> -> TimingHandler<IssueRefund> -> IssueRefundHandler

public sealed class CommandMetrics(IMeterFactory meters)
{
    private readonly Histogram<double> _duration = meters.Create("Billing.Commands")
        .CreateHistogram<double>("billing.command.duration", unit: "s");

    public void Record(string command, TimeSpan elapsed) =>
        _duration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("command", command));
}
```

*The same thing without Scrutor*

```csharp
// One helper and one line per handler; the order is visible in code, read inside out
public static class CommandHandlerRegistration
{
    public static IServiceCollection AddCommandHandler<TCommand, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand>>(sp =>
        {
            ICommandHandler<TCommand> handler = sp.GetRequiredService<THandler>();
            handler = ActivatorUtilities.CreateInstance<TimingHandler<TCommand>>(sp, handler);     // inner
            handler = ActivatorUtilities.CreateInstance<AuditingHandler<TCommand>>(sp, handler);   // outer
            return handler;
        });
        return services;
    }
}

builder.Services.AddCommandHandler<IssueRefund, IssueRefundHandler>();
builder.Services.AddCommandHandler<ChangeBankDetails, ChangeBankDetailsHandler>();
// ... 58 more
```

**Why it works.** Each concern now exists once and is wrapped around every handler by the container; the handlers lost their audit and timing code entirely. The audit decorator records success, failure and cancellation in a `finally`, so the failure path is covered by construction instead of by discipline. It writes through its own connection: if it shared the handler's `DbContext` and transaction, a rolled-back command would roll back its own audit row.

Order matters, and it is decided in one place. Auditing is outermost so it sees the final outcome, including anything thrown by the layers inside it. Timing sits directly around the handler, so the histogram measures the handler and not the 4 to 12 ms audit insert. With Scrutor the rule is that the last `Decorate` call is the outermost layer; in the manual version the order reads inside out, top to bottom.

**Result.** All 60 command types now write audit rows, failures included: 1,140 failed attempts were recorded in the first month. About 600 lines of copied code came out of the handlers, and p95 latency per command is now one dashboard panel built from `billing.command.duration`.

> **Costs, and when not to.** Decorators hide behaviour. Reading `IssueRefundHandler` no longer tells you it is audited, and stack traces gain frames for every layer. Open generic decoration applies to every handler, including ones you did not mean: a high-frequency `RecordHeartbeat` command started writing 30,000 audit rows an hour until it got an opt-out (a marker interface the decorator checks). Calling the registration code twice, which some test hosts do, decorates twice and writes two audit rows per command.
>
> Deep stacks are the real danger. Validation, authorisation, transaction, retry, caching and logging decorators each look harmless, but the order between them changes behaviour: retry outside the transaction re-runs the whole unit, retry inside it reuses a broken one, and caching outside authorisation serves one user's result to another. Past two or three layers nobody can say what runs when. If you need more, use one explicit pipeline where the steps are listed in order, or use the framework's own visible mechanisms such as middleware and endpoint filters. And for a concern only five handlers need, call it explicitly from those five.

> **Lesson:** A decorator puts a cross-cutting concern in one place; keep the stack short and set its order deliberately, because the order is behaviour.

<a id="case-proxy-pdf-renderer"></a>

#### Case 6 · Proxy: Launching a browser for every invoice PDF

*C# · ASP.NET Core · Playwright*

**The situation.** The invoices API renders invoice PDFs from server-generated HTML with headless Chromium through Microsoft.Playwright. It runs as 3 pods, each with 4 vCPU and a 4 GiB memory limit. Customers download about 9,000 PDFs a day, and at month end a batch job requests 11,840 more for emailing. Every request launched its own browser.

**Before**

*Before: a browser per request*

```csharp
public sealed class ChromiumPdfRenderer : IPdfRenderer
{
    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        using var playwright = await Playwright.CreateAsync();            // (1) starts the Playwright driver process
        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });             // (2) about 1.8 s and 150 to 200 MB, every time
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html);
        return await page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
    }
}

// Program.cs
builder.Services.AddScoped<IPdfRenderer, ChromiumPdfRenderer>();          // (3) nothing limits how many run at once
```

**What goes wrong.** Rendering an invoice takes about 0.4 s. Starting the driver (1) and launching Chromium (2) takes about 1.8 s, so 80% of every request was start-up. Each launch also starts several Chromium processes. Nothing limited concurrency (3), so the month-end batch drove 34 simultaneous launches per pod, about 6 GB of demand against a 4 GiB limit, and the kernel killed the container. Kubernetes restarted it, in-flight requests failed, the batch retried them, and the next spike killed it again.

The callers (the download endpoint, the month-end batch and the dunning email job) only needed `IPdfRenderer.RenderAsync`. None of them cared how the browser was managed, which is why the fix could sit behind the same interface.

**Evidence**

*Latency and the month-end run, one pod*

```text
Normal day
  p50 latency            2.31 s   (driver + Chromium launch 1.82 s, render 0.41 s)

Month-end batch, 2026-08-31 18:00 to 18:47
  PDF requests            3,950
  peak concurrency           34 renders, 34 Chromium instances
  failed requests           212   (retried by the batch)

$ kubectl get pod invoices-api-7d9c8f-x2kq \
    -o jsonpath='{.status.containerStatuses[0].lastState.terminated.reason}'
OOMKilled
$ kubectl get pod invoices-api-7d9c8f-x2kq \
    -o jsonpath='{.status.containerStatuses[0].restartCount}'
6
```

**After**

*After: a virtual proxy behind the same interface*

```csharp
public sealed class LazyChromiumPdfRenderer(IOptions<PdfRendererOptions> options, ILogger<LazyChromiumPdfRenderer> logger)
    : IPdfRenderer, IAsyncDisposable
{
    private static readonly BrowserTypeLaunchOptions LaunchOptions =
        new() { Headless = true, Args = ["--disable-dev-shm-usage"] };   // containers default to a 64 MB /dev/shm

    private readonly PdfRendererOptions _options = options.Value;
    private readonly SemaphoreSlim _pageSlots = new(options.Value.MaxConcurrentPages);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IPlaywright? _playwright;
    private BrowserHandle? _current;

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        await _pageSlots.WaitAsync(ct);                    // extra requests queue here instead of launching
        BrowserHandle? handle = null;
        var failed = false;
        try
        {
            handle = await AcquireAsync(ct);               // the first call launches; later calls reuse
            var page = await handle.Browser.NewPageAsync();
            try
            {
                await page.SetContentAsync(html, new PageSetContentOptions { Timeout = 15_000 });
                return await page.PdfAsync(new PagePdfOptions { Format = "A4", PrintBackground = true });
            }
            finally { await page.CloseAsync(); }
        }
        catch (PlaywrightException ex)                     // includes Playwright's TimeoutException and "Target closed"
        {
            failed = true;
            logger.LogWarning(ex, "PDF render failed; retiring this Chromium instance");
            throw;
        }
        finally
        {
            if (handle is not null) await ReleaseAsync(handle, failed);
            _pageSlots.Release();
        }
    }
```

*Browser lifecycle: launch once, recycle when worn out or broken*

```csharp
    private sealed class BrowserHandle(IBrowser browser)
    {
        public IBrowser Browser { get; } = browser;
        public int Renders;          // these three are guarded by _lifecycle
        public int InFlight;
        public bool Retired;
    }

    private async Task<BrowserHandle> AcquireAsync(CancellationToken ct)
    {
        BrowserHandle? toClose = null;
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_current is { } c && (!c.Browser.IsConnected || c.Renders >= _options.RendersPerBrowser))
            {
                c.Retired = true;                          // whoever finishes its last page closes it
                toClose = c.InFlight == 0 ? c : null;
                _current = null;
            }
            _playwright ??= await Playwright.CreateAsync();
            _current ??= new BrowserHandle(await _playwright.Chromium.LaunchAsync(LaunchOptions));
            _current.Renders++;
            _current.InFlight++;
            return _current;
        }
        finally
        {
            _lifecycle.Release();
            if (toClose is not null) await CloseQuietlyAsync(toClose);
        }
    }
```

*Release, disposal and registration*

```csharp
    private async Task ReleaseAsync(BrowserHandle handle, bool failed)
    {
        bool close;
        await _lifecycle.WaitAsync();
        try
        {
            if (failed && _current == handle) _current = null;   // the next request launches a fresh browser
            handle.Retired |= failed;
            close = --handle.InFlight == 0 && handle.Retired;
        }
        finally { _lifecycle.Release(); }
        if (close) await CloseQuietlyAsync(handle);
    }

    private async Task CloseQuietlyAsync(BrowserHandle handle)
    {
        try { await handle.Browser.CloseAsync(); }
        catch (PlaywrightException ex) { logger.LogDebug(ex, "Chromium was already gone"); }
    }

    public async ValueTask DisposeAsync()                  // called by the host at shutdown
    {
        if (_current is not null) await CloseQuietlyAsync(_current);
        _playwright?.Dispose();
        _pageSlots.Dispose();
        _lifecycle.Dispose();
    }
}

public sealed class PdfRendererOptions
{
    public int MaxConcurrentPages { get; set; } = 8;       // about 2 per vCPU
    public int RendersPerBrowser { get; set; } = 500;      // recycle to cap slow memory growth
}

// Program.cs: one instance per process; the container disposes it asynchronously at shutdown
builder.Services.Configure<PdfRendererOptions>(builder.Configuration.GetSection("Pdf"));
builder.Services.AddSingleton<IPdfRenderer, LazyChromiumPdfRenderer>();
```

**Why it works.** The proxy implements the same interface as the real renderer, so the three callers did not change. What changed is when the expensive object exists: the first render launches Chromium, and every later render opens a page in that browser, which costs milliseconds instead of 1.8 s. The page semaphore turns 34 concurrent requests into 8 rendering pages and 26 waiting requests, which bounds memory however hard the batch pushes.

The proxy also owns the browser's whole life. A crashed or disconnected browser is retired and the next request launches a fresh one, and a healthy browser is replaced after 500 renders to cap the slow memory growth of long-lived Chromium instances. A retired browser is closed only when its last in-flight page finishes, so recycling never kills a render. As a singleton there is one browser per process, and because it implements `IAsyncDisposable` the host closes Chromium cleanly on shutdown.

**Result.** p50 latency for customer downloads fell from 2.31 s to 0.44 s; only the first request after a deploy or a recycle pays the launch. During the month-end batch peak pod memory went from an OOM kill at 4 GiB to 1.3 GiB, the next two month-end runs had no restarts, and the batch finished in 12 minutes instead of 47.

> **Costs, and when not to.** A long-lived browser is shared state. A render that hangs holds a page slot until its timeout, a crash fails every in-flight render on that instance, and the recycling logic is concurrency code that needs its own tests. The queue in front of the semaphore also hides overload: under a big enough spike requests wait instead of failing fast, so pair it with a request timeout or a limit on waiters. Operationally the image must contain Chromium (run `playwright.ps1 install --with-deps chromium` at build time) and `/dev/shm` must be sized or bypassed. A singleton that implements only `IAsyncDisposable` throws if the container is disposed synchronously, which test code does with `using var provider = services.BuildServiceProvider()`; use `await using`.
>
> Do not write this proxy if PDFs are rare, say a few a day: the launch cost does not matter and an idle browser holds memory for nothing. And if volume keeps growing, the next step is not a cleverer proxy but a separate rendering service, or a PDF library that does not need a browser.

> **Lesson:** A virtual proxy defers and shares an expensive resource behind the interface callers already use, and then it owns that resource's whole lifecycle: creation, concurrency, failure and disposal.

<a id="case-facade-checkout"></a>

#### Case 7 · Facade: Three copies of checkout, and the CSV import forgot tax

*C# · ASP.NET Core · EF Core 10*

**The situation.** A garden and homeware retailer places orders from three entry points: the public API behind the web shop and app, an admin tool where agents place orders on a customer's behalf, and a nightly CSV import for B2B trade customers. Placing an order takes seven steps across five services: reserve stock, price, calculate tax, authorise payment, save the order, write the outbox event and queue the confirmation email. Each entry point had its own copy of those steps.

**Before**

*Before: the B2B import's copy of checkout*

```csharp
foreach (var row in batch.Rows)
{
    var reservation = await stock.ReserveAsync(row.Lines, ct);                                   // 1
    var priced = await pricing.PriceAsync(row.CustomerId, row.Lines, ct);                        // 2
    // (1) no tax step: this copy came from the admin tool before tax moved out of
    //     pricing on 12 May. The API and the admin tool were updated; the import was not.
    var auth = await payments.AuthoriseOnAccountAsync(row.CustomerId, priced.Subtotal, ct);      // 4
    if (!auth.Approved)
    {
        failures.Add(row, auth.Reason);                                                         // (2) reservation never released
        continue;
    }
    var order = new Order(row.CustomerId, priced.Lines, priced.Subtotal, tax: 0m, auth.Reference);   // (3)
    db.Orders.Add(order);                                                                       // 5
    db.OutboxMessages.Add(OutboxMessage.For(new OrderPlaced(order.Id)));                        // 6
    await db.SaveChangesAsync(ct);
    // 7: no confirmation email. Deliberate for B2B, but written down nowhere.
}
```

**What goes wrong.** The three copies were written by different people at different times, and they drifted. When tax calculation moved out of the pricing service in May, the API and the admin tool were updated and the import was not (1), so every B2B order imported since then was saved with zero VAT (3). The import also never released stock when an on-account authorisation was refused (2), leaving phantom reservations that hid stock from the web shop until a nightly sweep. The admin tool had its own drift: it reserved stock after taking payment, so agents sometimes charged customers for items that then turned out to be unavailable.

Nothing in the code said these were the same operation, so a fix to one copy was a fix to one copy.

**Evidence**

*SQL Server: B2B orders since 12 May*

```sql
SELECT   o.Source,
         COUNT(*)        AS Orders,
         SUM(o.NetTotal) AS Net,
         SUM(o.TaxTotal) AS Tax
FROM     Sales.Orders AS o
WHERE    o.CustomerType = 'B2B'
  AND    o.PlacedAt >= '2026-05-12'
GROUP BY o.Source;
```

*Result*

```text
Source     Orders  Net        Tax
---------  ------  ---------  ---------
Api          4880  612310.40  122462.08
AdminTool     231   28904.00    5780.80
CsvImport    1312  243000.00       0.00
```

**After**

*After: one facade for placing an order*

```csharp
public sealed class CheckoutFacade(
    IStockService stock, IPricingService pricing, ITaxCalculator tax, IPaymentService payments,
    ShopDbContext db, TimeProvider clock)
{
    public async Task<PlaceOrderResult> PlaceOrderAsync(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var reservation = await stock.ReserveAsync(cmd.Lines, ct);                                   // 1
        if (!reservation.Succeeded) return new PlaceOrderResult.OutOfStock(reservation.MissingSkus);

        PaymentAuthorisation? authorised = null;
        try
        {
            var priced = await pricing.PriceAsync(cmd.CustomerId, cmd.Lines, ct);                  // 2
            var taxes = await tax.CalculateAsync(priced, cmd.ShippingAddress, ct);                 // 3
            var auth = await payments.AuthoriseAsync(
                cmd.Payment, priced.Subtotal + taxes.Total, cmd.IdempotencyKey, ct);              // 4
            if (!auth.Approved)
            {
                await stock.ReleaseAsync(reservation.Id, ct);
                return new PlaceOrderResult.PaymentRefused(auth.Reason);
            }
            authorised = auth;

            var order = Order.Place(cmd.CustomerId, priced, taxes, auth.Reference, cmd.PlacedBy, clock.GetUtcNow());
            db.Orders.Add(order);                                                                   // 5
            db.OutboxMessages.Add(OutboxMessage.For(new OrderPlaced(order.Id, order.Total)));        // 6
            if (cmd.SendConfirmation)
                db.OutboxMessages.Add(OutboxMessage.For(new SendOrderConfirmation(order.Id)));       // 7
            await db.SaveChangesAsync(ct);                                                          // 5 to 7 commit together
            return new PlaceOrderResult.Placed(order.Id, order.Number);
        }
        catch
        {
            if (authorised is not null) await payments.VoidAsync(authorised.Reference, CancellationToken.None);
            await stock.ReleaseAsync(reservation.Id, CancellationToken.None);
            throw;
        }
    }
}
```

*After: the command, and the three callers*

```csharp
public sealed record PlaceOrderCommand(
    CustomerId CustomerId,
    IReadOnlyList<OrderLineRequest> Lines,
    Address ShippingAddress,
    PaymentInstruction Payment,         // card token, or on-account for trade customers
    PlacedBy PlacedBy,                  // customer, agent or import batch: stored on the order
    string IdempotencyKey,
    bool SendConfirmation = true);

// Public API
app.MapPost("/orders", async (PlaceOrderRequest req, ClaimsPrincipal user, CheckoutFacade checkout, CancellationToken ct) =>
    (await checkout.PlaceOrderAsync(req.ToCommand(user.GetCustomerId()), ct)).ToHttpResult());

// Admin tool: place an order on a customer's behalf
var result = await checkout.PlaceOrderAsync(form.ToCommand(PlacedBy.Agent(agent.Id)), ct);

// B2B import: the differences are data on the command, not a different workflow
foreach (var row in batch.Rows)
{
    var placed = await checkout.PlaceOrderAsync(
        row.ToCommand(PlacedBy.Import(batch.Id), idempotencyKey: $"{batch.Id}:{row.LineNumber}", sendConfirmation: false),
        ct);
    if (placed is not PlaceOrderResult.Placed) failures.Add(row, placed);
}
```

**Why it works.** The facade gives the seven steps one home and one order: reserve, price, tax, authorise, then save the order and both outbox messages in a single transaction. Compensation is written once as well: a refused payment releases stock, and a failure after authorisation voids it. The callers now differ only in what they pass, such as who placed the order, how it is paid and whether to send a confirmation. Those are values on the command, not branches inside the workflow.

The subsystems are unchanged and still usable on their own: the basket still calls `IStockService` to hold stock, and the tax calculator still owns the tax rules. The facade holds no business rules of its own. It is a sequence, which is why it stays at about 40 lines.

**Result.** Three copies of about 110 lines each became one method and three short callers. B2B orders have carried VAT since the change, and finance issued corrective invoices for the 1,312 affected orders, £48,600 of VAT. Checkout integration tests now run once against the facade, instead of three partial suites that each tested a different copy.

> **Costs, and when not to.** The line between a facade and a god service is the thing to watch. A facade covers one workflow and delegates; a god service collects every workflow and the logic too. When someone proposes adding `CancelOrderAsync` and `RefundAsync` to `CheckoutFacade`, the answer is a separate facade for each, and when someone adds a calculation inside it ("apply the trade discount for B2B"), that belongs in the pricing service. Watch the command as well: caller-specific flags such as `SkipTax` or `SkipStockCheck` bring back the drift the facade removed, so a flag must describe a fact about the order, never a step to skip.
>
> A facade is not a distributed transaction. If the process dies between authorisation and save, the `catch` never runs, so you still need authorisation expiry or a reconciliation job. And do not create one for a workflow with a single caller: an endpoint handler that calls five services in order is already the facade. Extract it when the second caller appears.

> **Lesson:** When several entry points perform the same multi-step operation, give the steps one home and let callers differ by data, not by copies.

<a id="case-composite-discount-rules"></a>

#### Case 8 · Composite: Promotion rules marketing could not express, or explain

*C# · System.Text.Json*

**The situation.** A garden and homeware retailer's marketing team runs 20 to 30 promotions a quarter. Eligibility was stored as a row of nullable columns that were always ANDed together, so any campaign with an OR in it needed a developer, a migration and a release. Support received about 60 tickets a month asking why a code had not applied, and the code's only answer was `false`.

**Before**

*Before: eligibility as a row of optional columns*

```csharp
public sealed class PromotionEligibility                     // one row per promotion, mapped by EF Core
{
    public string? RequiredSegment { get; set; }
    public decimal? MinBasketTotal { get; set; }
    public string? RequiredCategory { get; set; }
    public bool TotalOrCategory { get; set; }                // (1) added for one spring campaign: OR, for these two only
    public bool OncePerCustomer { get; set; }
    // ... 9 more nullable columns, one per campaign that did not fit
}

public static bool IsEligible(PromotionEligibility e, PromotionContext ctx)
{
    if (e.RequiredSegment is not null && !ctx.Customer.Segments.Contains(e.RequiredSegment))
        return false;                                        // (2) every failure is the same "false"

    var totalOk = e.MinBasketTotal is null || ctx.Basket.Total >= e.MinBasketTotal;
    var categoryOk = e.RequiredCategory is null || ctx.Basket.Lines.Any(l => l.Category == e.RequiredCategory);
    if (e.TotalOrCategory ? !(totalOk || categoryOk) : !(totalOk && categoryOk))
        return false;                                        // (3) category null => categoryOk is true => OR passes everyone

    if (e.OncePerCustomer && ctx.UsedPromotionCodes.Contains(ctx.PromotionCode))
        return false;
    // ...
    return true;
}
```

**What goes wrong.** The model can only express AND, so each campaign with a different shape added a column and a special case (1). The special cases interact badly: "null means no restriction" is right for AND and wrong for OR. SPRING15 was cloned from the garden campaign, which had `TotalOrCategory` set; marketing cleared the category and kept the £100 minimum, and (3) let the code apply to every basket for six hours.

When a code did not apply, (2) gave support nothing to go on. Answering a ticket meant a developer reading the row and the code, about 35 minutes each.

**Evidence**

*SPRING15, 3 April*

```text
Promotion SPRING15 (15% off, intended for baskets >= 100.00 GBP), 2026-04-03 09:00 to 15:10
  baskets checked out              6,140
  code applied                     6,120   (20 refused: already used)
  baskets with total >= 100.00       880
  discount given               41,862.40 GBP
  discount intended           about 17,690 GBP   (the 880 qualifying baskets only)
```

**After**

*After: one interface, three composites*

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AllRule), "all")]
[JsonDerivedType(typeof(AnyRule), "any")]
[JsonDerivedType(typeof(NotRule), "not")]
[JsonDerivedType(typeof(InSegmentRule), "inSegment")]
[JsonDerivedType(typeof(BasketTotalAtLeastRule), "basketTotalAtLeast")]
[JsonDerivedType(typeof(ContainsCategoryRule), "containsCategory")]
[JsonDerivedType(typeof(PromotionUsedRule), "promotionUsed")]
public interface IRule
{
    RuleResult Explain(PromotionContext ctx);     // checkout reads .Passed; support reads the whole tree
}

public sealed record AllRule(IReadOnlyList<IRule> Rules) : IRule
{
    public RuleResult Explain(PromotionContext ctx)
    {
        var children = Rules.Select(r => r.Explain(ctx)).ToList();
        return new("ALL of", children.All(c => c.Passed), children);
    }
}

public sealed record AnyRule(IReadOnlyList<IRule> Rules) : IRule
{
    public RuleResult Explain(PromotionContext ctx)
    {
        var children = Rules.Select(r => r.Explain(ctx)).ToList();
        return new("ANY of", children.Any(c => c.Passed), children);
    }
}

public sealed record NotRule(IRule Rule) : IRule
{
    public RuleResult Explain(PromotionContext ctx)
    {
        var inner = Rule.Explain(ctx);
        return new("NOT", !inner.Passed, [inner]);
    }
}
```

*After: the leaves, and loading a rule*

```csharp
public sealed record RuleResult(string Description, bool Passed, IReadOnlyList<RuleResult> Children)
{
    public static RuleResult Leaf(string description, bool passed) => new(description, passed, []);
}

public sealed record InSegmentRule(string Segment) : IRule
{
    public RuleResult Explain(PromotionContext ctx) => RuleResult.Leaf(
        $"customer in segment '{Segment}' (has: {string.Join(", ", ctx.Customer.Segments)})",
        ctx.Customer.Segments.Contains(Segment));
}

public sealed record BasketTotalAtLeastRule(decimal Amount) : IRule
{
    public RuleResult Explain(PromotionContext ctx) => RuleResult.Leaf(
        $"basket total >= {Amount:0.00} (was {ctx.Basket.Total:0.00})", ctx.Basket.Total >= Amount);
}

public sealed record ContainsCategoryRule(string Category) : IRule
{
    public RuleResult Explain(PromotionContext ctx) => RuleResult.Leaf(
        $"basket contains category '{Category}'",
        ctx.Basket.Lines.Any(l => string.Equals(l.Category, Category, StringComparison.OrdinalIgnoreCase)));
}

public sealed record PromotionUsedRule : IRule
{
    public RuleResult Explain(PromotionContext ctx) => RuleResult.Leaf(
        $"customer has already used code {ctx.PromotionCode}", ctx.UsedPromotionCodes.Contains(ctx.PromotionCode));
}

public static class RuleJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,      // .NET 9+: "type" need not be the first property
    };

    public static IRule Parse(string json) =>
        JsonSerializer.Deserialize<IRule>(json, Options) ?? throw new JsonException("Rule is null.");
}
```

*The stored rule, and what support sees*

```text
{
  "type": "all",
  "rules": [
    { "type": "inSegment", "segment": "VIP" },
    { "type": "any", "rules": [
      { "type": "basketTotalAtLeast", "amount": 100 },
      { "type": "containsCategory", "category": "garden" }
    ] },
    { "type": "not", "rule": { "type": "promotionUsed" } }
  ]
}

Explain() for customer 48213, code SPRING-VIP, basket 86.40 GBP: NOT APPLIED
[fail] ALL of
  [pass] customer in segment 'VIP' (has: VIP, NEWSLETTER)
  [fail] ANY of
    [fail] basket total >= 100.00 (was 86.40)
    [fail] basket contains category 'garden'
  [pass] NOT
    [fail] customer has already used code SPRING-VIP
```

**Why it works.** Every node, leaf or composite, answers the same question through the same method, so a composite does not care whether its children are leaves or further composites, and the tree nests as deep as marketing needs. AND, OR and NOT are three small classes instead of flags spread across columns, and "null means no restriction" no longer exists: a condition that is not in the tree is not evaluated.

The JSON maps one to one onto the tree. `[JsonPolymorphic]` and `[JsonDerivedType]` tell System.Text.Json which record to create for each `type`, so loading a rule is one `Deserialize` call with no hand-written converter. `Explain` deliberately evaluates every branch instead of short-circuiting, and records the actual values, so support can see which condition failed and by how much.

**Result.** Marketing built 23 campaigns in Q3 in the admin UI with no release. The 14 eligibility columns were replaced by one `EligibilityRule` JSON column that is validated on save, and support now answers most "why did my code not work" tickets from the Explain view in under five minutes.

> **Costs, and when not to.** You have built a small interpreter, and it needs what interpreters need. Validate on save: reject an empty `all` (true for everyone) and an empty `any` (false for everyone), cap the depth, and evaluate the rule against a few sample baskets before publishing. Treat discriminators as a public contract, because renaming `basketTotalAtLeast` breaks every stored rule that uses it; add new types instead of renaming old ones. Unknown discriminators fail deserialisation, which you want at save time and not at checkout. Leaves must not do I/O: load what they need (segments, used codes) into `PromotionContext` once, or a deep tree becomes an N+1 at checkout.
>
> Composite is the wrong tool when promotions come in two or three fixed shapes. Typed columns are simpler, the database can constrain them, and "which promotions target VIP?" is one SQL query instead of a walk over JSON trees. Keep the leaves as named business conditions, too. The moment someone asks for a generic `{ field, operator, value }` leaf, you are building a query language without any of the tooling of a real one.

> **Lesson:** Composite lets a tree of rules be treated as one rule, which makes nested AND, OR and NOT configurable as data and lets the same tree explain its own answer.

<a id="case-flyweight-import-strings"></a>

#### Case 9 · Flyweight: 3 million import rows and 2.4 GB of duplicate strings

*C# · .NET 10*

**The situation.** A marketplace sync worker imports a nightly listings feed of 3 million rows from three marketplaces and keeps every parsed row in memory so it can de-duplicate listings across marketplaces before writing. It runs in a container with a 4 GiB limit. As the feed grew past 2.5 million rows, the import slowed from 20 to 52 minutes, and the heap reached 3.1 GB.

**Before**

*Before: every field of every row is its own string*

```csharp
public sealed class ListingRow
{
    public required string Sku { get; init; }
    public required string Country { get; init; }        // (1) 31 distinct values, 3 million instances
    public required string Currency { get; init; }
    public required string Status { get; init; }
    public required string Brand { get; init; }
    public required string CategoryPath { get; init; }   // (2) 4,812 distinct values of about 90 characters
    // ... 9 more low-cardinality columns: condition, fulfilment channel, tax code, colour, ...
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}

public async Task<List<ListingRow>> ParseAsync(string path, CancellationToken ct)
{
    var rows = new List<ListingRow>();
    await foreach (var line in File.ReadLinesAsync(path, ct))
    {
        var f = line.Split('\t');                        // (3) a new string for every field of every row
        rows.Add(new ListingRow
        {
            Sku = f[0],
            Country = f[3],
            Currency = f[4],
            Status = f[5],
            Brand = f[6].Trim(),
            CategoryPath = f[7],
            // ...
            Price = decimal.Parse(f[8], CultureInfo.InvariantCulture),
            Quantity = int.Parse(f[9], CultureInfo.InvariantCulture),
        });
    }
    return rows;
}
```

**What goes wrong.** `Split` (3) allocates a fresh string for every field, and because the rows are kept, those strings are kept. Country (1) has 31 distinct values and 3 million instances; category path (2) has 4,812 distinct values and 3 million copies at about 208 bytes each. Across the 14 low-cardinality columns the import holds 42 million strings that represent fewer than 26,000 distinct values.

The cost is more than memory. All of those strings survive to gen 2, so every full collection has to mark 45 million objects, and one import ran 58 gen 2 collections and spent 38% of its time in GC. At 3.1 GB against a 4 GiB limit, the worker was one larger feed away from an OOM kill.

**Evidence**

*dotnet-counters at the end of the 52-minute import*

```text
$ dotnet-counters monitor -n Listings.Worker --counters System.Runtime
Name                                                     Current Value
[System.Runtime]
    dotnet.gc.collections ({collection})
        gc.heap.generation
        ------------------
        gen0                                                     1,846
        gen1                                                       512
        gen2                                                        58
    dotnet.gc.heap.total_allocated (By)                     1.0412e+10
    dotnet.gc.last_collection.heap.size (By)
        gc.heap.generation
        ------------------
        gen0                                                12,582,912
        gen1                                                88,014,336
        gen2                                                2.8933e+09
        loh                                                180,355,120
        poh                                                     98,304
    dotnet.gc.pause.time (s)                                   1,185.6
    dotnet.process.memory.working_set (By)                  3.7021e+09
```

*Heap snapshot and duplicate strings*

```text
$ dotnet-gcdump collect -n Listings.Worker -o feed.gcdump
$ dotnet-gcdump report feed.gcdump
  3,072,413,904  GC Heap bytes
     48,006,733  GC Heap objects

   Object Bytes        Count  Type
  2,544,186,712   45,000,212  System.String  [System.Private.CoreLib.dll]
    480,000,000    3,000,000  Listings.ListingRow  [Listings.Worker.dll]
     33,554,456            1  Listings.ListingRow[] (Bytes > 10M)  [Listings.Worker.dll]
  ...

Duplicate strings by value (a .gcdump has no string contents, so these come
from a full dump of the same run, dotnet-dump collect, grouped with ClrMD)
      Count   Bytes each   Value
  2,904,001           32   "new"
  2,712,330           40   "active"
  2,140,118           32   "GBP"
  2,139,604           32   "GB"
     61,220          104   "Home & Garden > Garden > Watering > Hoses"
  ...
  42,000,000 strings in 14 columns, 25,904 distinct values: 2.40 GB held by duplicates
```

**After**

*After: a per-import string pool*

```csharp
// One canonical instance per distinct value.
// Not thread-safe: use one pool per import run, or one per parsing thread.
public sealed class StringPool
{
    private readonly HashSet<string> _strings = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _bySpan;

    public StringPool() => _bySpan = _strings.GetAlternateLookup<ReadOnlySpan<char>>();   // .NET 9+

    public int Count => _strings.Count;

    // Look up by span: a value seen before costs a hash and a compare, and no allocation
    public string Get(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return string.Empty;
        if (_bySpan.TryGetValue(value, out var existing)) return existing;

        var created = value.ToString();                  // first time this value has been seen
        _strings.Add(created);
        return created;
    }

    // Before .NET 9, or when you already hold a string: keep the canonical one, let the copy die in gen 0
    public string Get(string value)
    {
        if (_strings.TryGetValue(value, out var existing)) return existing;
        _strings.Add(value);
        return value;
    }
}
```

*After: parsing spans into pooled strings*

```csharp
public sealed class ListingFeedParser
{
    private readonly StringPool _pool = new();          // lives as long as this import, then is collected

    public async Task<List<ListingRow>> ParseAsync(string path, CancellationToken ct)
    {
        var rows = new List<ListingRow>(capacity: 3_200_000);
        await foreach (var line in File.ReadLinesAsync(path, ct))
            rows.Add(ParseLine(line));                   // the line itself dies young, in gen 0
        return rows;
    }

    private ListingRow ParseLine(string line)
    {
        ReadOnlySpan<char> s = line;
        Span<Range> f = stackalloc Range[24];
        if (s.Split(f, '\t') < 22) throw new FormatException($"Expected 22 columns, got: {line}");

        return new ListingRow
        {
            Sku          = s[f[0]].ToString(),           // unique per row: pooling would only add a lookup
            Country      = _pool.Get(s[f[3]]),
            Currency     = _pool.Get(s[f[4]]),
            Status       = _pool.Get(s[f[5]]),
            Brand        = _pool.Get(s[f[6]].Trim()),
            CategoryPath = _pool.Get(s[f[7]]),
            // ... the other nine low-cardinality columns the same way
            Price    = decimal.Parse(s[f[8]], NumberStyles.Number, CultureInfo.InvariantCulture),
            Quantity = int.Parse(s[f[9]], CultureInfo.InvariantCulture),
        };
    }
}
```

*Measured after, same feed*

```text
dotnet-counters at the end of the run
[System.Runtime]
    dotnet.gc.collections ({collection})
        gc.heap.generation
        ------------------
        gen0                                                       604
        gen1                                                       171
        gen2                                                         6
    dotnet.gc.last_collection.heap.size (By)
        gc.heap.generation
        ------------------
        gen0                                                 4,194,304
        gen1                                                21,495,808
        gen2                                               600,834,048
        loh                                                 35,651,584
        poh                                                     98,304
    dotnet.gc.pause.time (s)                                      45.6
    dotnet.process.memory.working_set (By)                  1.0183e+09

StringPool.Count at end of run                      25,904
Import duration                                     19 min   (was 52 min)
```

**Why it works.** This is Flyweight with the simplest possible flyweight, an immutable string. The pool keeps one canonical instance per distinct value and every row refers to it, so 42 million references now point at 25,904 strings instead of 42 million copies. Strings are immutable, so sharing them is safe without any further design. With `GetAlternateLookup<ReadOnlySpan<char>>()` the pool is queried with a slice of the line before any string exists, so a value seen before costs a hash and a comparison and allocates nothing.

`string.Intern` looks like the same idea and is the wrong tool. The intern pool is process-wide, and as its documentation warns, memory for interned strings is unlikely to be released until the runtime terminates, so every brand and category from every nightly import stays for the life of the worker. It also takes a `string`, so you have already allocated the copy you meant to avoid, and it shares a table with every literal in the process. A per-import pool has a lifetime you control: when the import ends, the parser, the pool and the rows become garbage together.

**Result.** The GC heap fell from 3.1 GB to about 660 MB, time in GC from 38% to 4%, gen 2 collections from 58 to 6, and the import from 52 minutes to 19. The pool held 25,904 strings at the end of the run.

> **Costs, and when not to.** The pool adds a hash lookup per field, which is cheap next to an allocation but not free, and it is not thread-safe: give each parsing thread its own pool, or use a `ConcurrentDictionary<string, string>` and accept the extra cost. Never pool high-cardinality columns such as SKUs or titles, because the pool grows to the size of the data and every lookup misses. Never make the pool static either, or you have rebuilt `string.Intern` with a leak you wrote yourself.
>
> Check two things before adding one. First, do you need all the rows at once? If they can be streamed to the database in batches of 10,000, the duplicates die in gen 0 and a pool buys almost nothing. Second, is the set of values closed? Status, currency and condition have a handful of known values, and an enum or a small value type such as a 3-character `CurrencyCode` struct is smaller than any shared string and catches bad data at parse time. The pool is for the open, low-cardinality text in between, such as brands and category paths.

> **Lesson:** When millions of retained objects repeat a few thousand immutable values, share one instance per value through a pool whose lifetime you own, not through `string.Intern`.

### Behavioural patterns

Varying an algorithm or a step, and moving logic between objects instead of growing a switch.

<a id="case-strategy-export-formats"></a>

#### Case 10 · Strategy: One export switch, four formats, and an XLSX fix that broke CSV

*C# · ASP.NET Core · EF Core 10*

**The situation.** A B2B wholesale portal exports orders as CSV, XLSX or JSON, plus a fixed-width file for one customer, Calder Retail, whose ERP accepts nothing else: about 2,600 exports a day, and 410,000 rows in Calder's nightly file. All four formats lived in a 300-line `switch (format)` in `ReportsController.Export`. In April an edit to the XLSX branch broke CSV quoting, and for 11 days every row with a comma in the customer name had one column too many.

**Before**

*Before: every format in one action*

```csharp
[HttpGet("orders/export")]
public async Task<IActionResult> Export(string format, DateOnly from, DateOnly to, CancellationToken ct)
{
    var rows = await db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenant.Id && o.PlacedOn >= from && o.PlacedOn <= to)
        .Select(o => new OrderReportRow(o.Number, o.Customer.Name, o.PlacedOn, o.Status, o.Total))
        .ToListAsync(ct);                                                // (1) all 410,000 rows in memory

    // (2) written for CSV, reused by the XLSX branch; quoting removed in April "for Excel"
    string Cell(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    var buffer = new MemoryStream();                                     // (3) and the whole file again
    switch (format.ToLowerInvariant())
    {
        case "csv":
            using (var writer = new StreamWriter(buffer, leaveOpen: true))
            {
                writer.WriteLine("Number,Customer,PlacedOn,Status,Total");
                foreach (var r in rows)
                    writer.WriteLine(string.Join(",", Cell(r.Number), Cell(r.Customer),
                        Cell(r.PlacedOn), Cell(r.Status), Cell(r.Total)));
            }
            return File(buffer.ToArray(), "text/csv", "orders.csv");

        case "xlsx":
            // ... 90 lines of ClosedXML, calling Cell() for every value
        case "json":
            // ... 40 lines
        case "fixed":
            if (tenant.Id != CalderTenantId) return Forbid();            // (4) a customer rule inside format code
            // ... 110 lines of padding, truncation and Latin-1 encoding
        default:
            return BadRequest($"Unknown format '{format}'");
    }
}
```

**What goes wrong.** The four branches share one method, so they share its helpers. `Cell` (2) was written for CSV, where quoting matters, and later reused by the XLSX branch, where it does not. Removing the quotes was right for XLSX and broke CSV, and the diff gave no hint: it touched "the XLSX helper". Customers' import tools rejected the shifted rows, and three customers reported missing orders before anyone opened a file.

The shape also costs memory. Every format loads all rows (1) and buffers the whole file (3) before sending a byte, so Calder's export peaked at 1.31 GB and the pod, limited to 1.5 GB, was killed twice in May. A rule about one customer (4) sits in the middle of format code.

**Evidence**

*A CSV export after the April change, and Calder's export*

```text
orders-20260422-20260422.csv
Number,Customer,PlacedOn,Status,Total
SO-118203,Brightwell Foods,04/22/2026,Shipped,312.00
SO-118204,Harper, Lowe & Sons,04/22/2026,Shipped,1240.50      <- 6 fields in a 5-column file

GET /reports/orders/export?format=fixed   (Calder Retail, 410,000 rows)
  rows materialised            410,000
  peak working set             1.31 GB      pod limit 1.5 GB, OOMKilled 2x in May
  time to first byte           48 s
```

**After**

*After: one class per format*

```csharp
public interface IReportExporter
{
    string Format { get; }                          // the ?format= value
    string ContentType { get; }
    string FileExtension { get; }
    bool IsAvailableTo(TenantId tenant) => true;    // default interface member

    Task WriteAsync(IAsyncEnumerable<OrderReportRow> rows, Stream output, CancellationToken ct);
}

public sealed class CsvReportExporter : IReportExporter
{
    private static readonly SearchValues<char> NeedsQuotes = SearchValues.Create(",\"\r\n");

    public string Format => "csv";
    public string ContentType => "text/csv; charset=utf-8";
    public string FileExtension => "csv";

    public async Task WriteAsync(IAsyncEnumerable<OrderReportRow> rows, Stream output, CancellationToken ct)
    {
        // await using: a plain Dispose would flush synchronously, which Kestrel rejects
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        await writer.WriteLineAsync("Number,Customer,PlacedOn,Status,Total");
        await foreach (var r in rows.WithCancellation(ct))
            await writer.WriteLineAsync(string.Join(',', Quote(r.Number), Quote(r.Customer),
                r.PlacedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.Status.ToString(),
                r.Total.ToString(CultureInfo.InvariantCulture)));
    }

    // RFC 4180: quote a field containing a comma, quote or line break, and double any embedded quotes
    private static string Quote(string value) =>
        value.AsSpan().ContainsAny(NeedsQuotes) ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

public sealed class FixedWidthReportExporter(IOptions<CalderExportOptions> calder) : IReportExporter
{
    public bool IsAvailableTo(TenantId tenant) => tenant == calder.Value.TenantId;
    // ... Format "fixed"; WriteAsync: Latin-1, CRLF, each field padded or cut to Calder's widths
}
```

*Selection, registration and the controller*

```csharp
public sealed class ReportExporters(IEnumerable<IReportExporter> exporters)
{
    // ToDictionary throws on a duplicate Format, so two exporters claiming "csv" fail when the registry
    // is first resolved, instead of one silently winning
    private readonly Dictionary<string, IReportExporter> _byFormat =
        exporters.ToDictionary(e => e.Format, StringComparer.OrdinalIgnoreCase);

    public IReportExporter? Find(string format, TenantId tenant) =>
        _byFormat.TryGetValue(format, out var e) && e.IsAvailableTo(tenant) ? e : null;
}

// Program.cs: exporters are stateless, so singletons
builder.Services.AddSingleton<IReportExporter, CsvReportExporter>();
builder.Services.AddSingleton<IReportExporter, XlsxReportExporter>();
builder.Services.AddSingleton<IReportExporter, JsonReportExporter>();
builder.Services.AddSingleton<IReportExporter, FixedWidthReportExporter>();
builder.Services.AddSingleton<ReportExporters>();

[HttpGet("orders/export")]
public async Task<IActionResult> Export(string format, DateOnly from, DateOnly to, CancellationToken ct)
{
    var exporter = exporters.Find(format, tenant.Id);
    if (exporter is null) return BadRequest($"Unknown format '{format}'");

    Response.ContentType = exporter.ContentType;
    Response.Headers.ContentDisposition =
        $"attachment; filename=\"orders-{from:yyyyMMdd}-{to:yyyyMMdd}.{exporter.FileExtension}\"";

    var rows = db.Orders.AsNoTracking()
        .Where(o => o.TenantId == tenant.Id && o.PlacedOn >= from && o.PlacedOn <= to)
        .OrderBy(o => o.Number)
        .Select(o => new OrderReportRow(o.Number, o.Customer.Name, o.PlacedOn, o.Status, o.Total))
        .AsAsyncEnumerable();                     // rows flow from the data reader to the response

    await exporter.WriteAsync(rows, Response.Body, ct);
    return new EmptyResult();
}
```

*Adding a format: one class, one line*

```csharp
// The analytics team's JSON Lines feed. No existing exporter, and not the controller, was edited.
public sealed class JsonLinesReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly byte[] NewLine = [(byte)'\n'];

    public string Format => "jsonl";
    public string ContentType => "application/x-ndjson";
    public string FileExtension => "jsonl";

    public async Task WriteAsync(IAsyncEnumerable<OrderReportRow> rows, Stream output, CancellationToken ct)
    {
        await foreach (var row in rows.WithCancellation(ct))
        {
            await JsonSerializer.SerializeAsync(output, row, Options, ct);
            await output.WriteAsync(NewLine, ct);
        }
    }
}

// Program.cs
builder.Services.AddSingleton<IReportExporter, JsonLinesReportExporter>();
```

**Why it works.** Each format is now a class that owns its rules: CSV owns RFC 4180 quoting, XLSX owns cell types, fixed-width owns Latin-1, CRLF and Calder's column widths. A change to one format is a diff to one file, and CSV quoting has unit tests that no XLSX change can reach. The controller no longer knows which formats exist: it looks one up, sets the headers it declares, and hands over a row stream and the response body. Calder's special status is a property of its exporter.

The registry builds a case-insensitive dictionary from `IEnumerable<IReportExporter>`. Keyed services (`AddKeyedSingleton<IReportExporter, CsvReportExporter>("csv")`) also work, but keys compare exactly, so `?format=CSV` would not resolve, and the availability check would still need a home. Because rows arrive through `AsAsyncEnumerable()`, each exporter writes while the data reader produces rows, and memory stays flat whatever the date range.

**Result.** In June the XLSX exporter gained a summary sheet; the diff touched one file and the CSV tests stayed green. The JSON Lines feed shipped in a day. Calder's export now peaks at 74 MB and sends its first byte after 0.3 s instead of 48 s. XLSX still builds the workbook in memory, because ClosedXML has no streaming writer, so it is capped at 200,000 rows.

> **Costs, and when not to.** Five small classes and an interface replace one method, and the column list now exists in every exporter. When finance asked for a currency column, it was added to three exporters and missed in JSON until a test compared headers across formats. A shared `OrderReportColumns` list that each exporter iterates fixes that, at the price of one more abstraction. Rules that must agree across formats, such as rounding, belong in the row model, not copied into each strategy.
>
> Strategy is the wrong choice for two formats that rarely change: a `switch` expression with two arms calling two private methods is shorter and just as safe, provided the branches share nothing. It also does not pay when variants differ only in data, such as a delimiter or a date format. That is one CSV exporter with options, not two classes.

> **Lesson:** When the branches of a switch change for different reasons, give each its own class, so an edit to one cannot reach the others.

<a id="case-template-method-partner-imports"></a>

#### Case 11 · Template Method: Five partner imports, five copies of the flow, two silent skips

*C# · EF Core 10*

**The situation.** A home-and-garden marketplace imports nightly stock and price files from five partners, 9,000 to 120,000 rows each. Five developers wrote the five imports over three years, and each reimplemented the same flow: open the file, parse rows, validate, upsert in batches of 1,000, and write an error report for the partner manager to send back. In July, Harbour Supplies asked why 2,140 of their SKUs showed as out of stock when their file said otherwise.

**Before**

*Before: one of the five imports*

```csharp
public sealed class HarbourStockImport(CatalogDbContext db)
{
    public async Task RunAsync(Stream file, CancellationToken ct)
    {
        using var reader = new StreamReader(file);
        var batch = new List<StockLevel>();
        var errors = new List<string>();
        await reader.ReadLineAsync(ct);                                   // header
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var f = line.Split(';');
            try
            {
                var quantity = int.Parse(f[2]);
                if (quantity < 0) continue;                               // (1) dropped, not reported
                batch.Add(new StockLevel { PartnerId = 3, Sku = f[0], Quantity = quantity,
                                           Price = decimal.Parse(f[3], CultureInfo.GetCultureInfo("en-GB")) });
            }
            catch (FormatException) { continue; }                         // (2) "1,200" is dropped the same way
            if (batch.Count == 1000)                                      // (3) batching: copy 4 of 5
            {
                await UpsertAsync(batch, ct);
                batch.Clear();
            }
        }
        await UpsertAsync(batch, ct);
        if (errors.Count > 0)                                             // (4) nothing ever adds to errors
            await File.WriteAllLinesAsync($"/imports/errors/harbour-{DateTime.Now:yyyyMMdd}.txt", errors, ct);
    }

    private async Task UpsertAsync(List<StockLevel> batch, CancellationToken ct) { /* 30 lines, also copied */ }
}
```

**What goes wrong.** In June, Harbour's system started writing quantities over 999 with a thousands separator: "1,200". `int.Parse` throws on that, and the `catch` at (2) drops the row without recording it, as (1) does for negative quantities. The error list (4) is never filled, so every report said zero errors. A row never upserted keeps its old stock level, which the site expires after 48 hours, so Harbour's best sellers went out of stock. Peak Wholesale's import had its own `catch { continue; }` and had been dropping rows priced at 0.00 for a year.

No single line is unusual. The flow was written five times, and five copies of a rule drift: two imports forgot that skipping a row must mean reporting it, and the batching code (3) existed in four slightly different versions.

**Evidence**

*Reconciliation, night of 14 July*

```text
partner            rows in file   upserted   in error report   unaccounted
Northgate               118,442    118,442                 0             0
Brightline               61,020     60,983                37             0
Harbour Supplies         41,906     39,766                 0         2,140
Kestrel                  22,310     22,151               159             0
Peak Wholesale            9,875      9,612                 0           263
```

**After**

*After: the skeleton, written once*

```csharp
public interface IPartnerRow { int LineNumber { get; } string Sku { get; } }

public abstract class PartnerImport<TRow>(CatalogDbContext db, IImportErrorReport errorReport)
    where TRow : IPartnerRow
{
    private const int BatchSize = 1_000;

    protected abstract int PartnerId { get; }
    protected abstract IAsyncEnumerable<TRow> ParseAsync(Stream file, CancellationToken ct);
    protected abstract IReadOnlyList<string> Validate(TRow row);          // empty list means valid
    protected abstract StockLevel MapToEntity(TRow row);
    protected virtual Task OnCompletedAsync(ImportSummary summary, CancellationToken ct) => Task.CompletedTask;

    // Not virtual: every partner runs the same steps in the same order, and none can skip one
    public async Task<ImportSummary> RunAsync(ImportRunId runId, Stream file, CancellationToken ct)
    {
        var errors = new List<ImportError>();
        var batch = new List<StockLevel>(BatchSize);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int read = 0, upserted = 0, rejected = 0;

        await foreach (var row in ParseAsync(file, ct))
        {
            read++;
            IReadOnlyList<string> problems = seen.Add(row.Sku) ? Validate(row) : ["SKU appears twice in the file"];
            if (problems.Count > 0)
            {
                rejected++;                                  // one row can have several problems
                errors.AddRange(problems.Select(p => new ImportError(row.LineNumber, row.Sku, p)));
                continue;                                    // the only way to skip a row, and it is recorded
            }
            batch.Add(MapToEntity(row));
            if (batch.Count == BatchSize) upserted += await UpsertAsync(batch, ct);
        }
        if (batch.Count > 0) upserted += await UpsertAsync(batch, ct);

        var summary = new ImportSummary(runId, PartnerId, read, upserted, rejected);
        await errorReport.WriteAsync(summary, errors, ct);  // every run, even with zero errors
        if (read != upserted + rejected)
            throw new ImportReconciliationException(summary);
        await OnCompletedAsync(summary, ct);
        return summary;
    }
```

*The batch upsert, also in the base class*

```csharp
    private async Task<int> UpsertAsync(List<StockLevel> batch, CancellationToken ct)
    {
        var skus = batch.Select(s => s.Sku).ToList();
        var existing = await db.StockLevels
            .Where(s => s.PartnerId == PartnerId && skus.Contains(s.Sku))
            .ToDictionaryAsync(s => s.Sku, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var incoming in batch)
        {
            if (existing.TryGetValue(incoming.Sku, out var current)) current.UpdateFrom(incoming);
            else db.StockLevels.Add(incoming);
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();                  // keep the context small across 120 batches

        var count = batch.Count;
        batch.Clear();
        return count;
    }
}
```

*A partner: only what differs*

```csharp
public sealed record HarbourRow(int LineNumber, string Sku, string Quantity, string Price) : IPartnerRow;

public sealed class HarbourStockImport(CatalogDbContext db, IImportErrorReport errorReport)
    : PartnerImport<HarbourRow>(db, errorReport)
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");
    private const NumberStyles Quantity = NumberStyles.Integer | NumberStyles.AllowThousands;

    protected override int PartnerId => 3;

    protected override async IAsyncEnumerable<HarbourRow> ParseAsync(
        Stream file, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(file, leaveOpen: true);
        var lineNumber = 1;
        await reader.ReadLineAsync(ct);                                   // header
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var f = line.Split(';');
            yield return new HarbourRow(++lineNumber, f.ElementAtOrDefault(0) ?? "",
                                        f.ElementAtOrDefault(2) ?? "", f.ElementAtOrDefault(3) ?? "");
        }
    }

    protected override IReadOnlyList<string> Validate(HarbourRow row)
    {
        var problems = new List<string>();
        if (row.Sku.Length is 0 or > 40) problems.Add("SKU is missing or longer than 40 characters");
        if (!int.TryParse(row.Quantity, Quantity, Uk, out var quantity) || quantity < 0)
            problems.Add($"Quantity '{row.Quantity}' is not a whole number of 0 or more");
        if (!decimal.TryParse(row.Price, NumberStyles.Number, Uk, out var price) || price <= 0)
            problems.Add($"Price '{row.Price}' is not a positive amount");
        return problems;
    }

    protected override StockLevel MapToEntity(HarbourRow row) => new()
    {
        PartnerId = PartnerId,
        Sku = row.Sku,
        Quantity = int.Parse(row.Quantity, Quantity, Uk),
        Price = decimal.Parse(row.Price, NumberStyles.Number, Uk),
    };
}
```

**Why it works.** The flow now exists once, in `RunAsync`, and each subclass supplies only what differs: how to read its format, what makes a row valid, and how a row becomes a `StockLevel`. `RunAsync` is not virtual, so no subclass can reorder or skip a step. The only way to reject a row is to return a problem from `Validate`, and the skeleton turns every problem into a line in the report, which is written on every run, clean or not.

The reconciliation check makes the rule enforceable: rows read must equal rows upserted plus rows rejected, or the run fails. Parsing into string-typed rows and converting in `MapToEntity` keeps format problems in `Validate`, where they are reported, and out of the parser, where a `catch` can swallow them.

**Result.** The five imports went from about 1,450 lines to 610, including the 120-line base class. The first night, the reports listed 263 Peak Wholesale rows priced at 0.00, which Peak exports for discontinued lines. Harbour's separator is now parsed with `NumberStyles.AllowThousands`, and unaccounted rows have been zero on every run since.

> **Costs, and when not to.** Inheritance couples all five imports to the base class: every protected member is an API, and a change to the skeleton changes five imports at once. The base class becomes a trap when hooks multiply. Kestrel sends deltas, so it needs `protected virtual bool IsFullSnapshot`; Brightline's rows are wide, so `BatchSize` becomes virtual. Once you add a flag that only one subclass sets, or override a hook to do nothing, the skeleton no longer describes the imports. Single inheritance also blocks sharing: Harbour and Peak both read semicolon CSV, and sharing that parser needs a middle class, which is how hierarchies get deep.
>
> Composition keeps the fixed flow without those limits: an `ImportPipeline<TRow>` that takes an `IRowSource<TRow>`, an `IRowValidator<TRow>` and an `IRowMapper<TRow>` as strategies, which can be mixed and tested alone, at the cost of more types and wiring. Template Method is the wrong choice for two imports, which can share a helper method, and for a partner whose flow really differs, such as a paged REST API: forcing it into a file-shaped skeleton is worse than a separate class.

> **Lesson:** Template Method fixes the order of steps in one place; when subclasses start needing flags to opt out of steps, move to composition.

<a id="case-state-subscription-billing"></a>

#### Case 12 · State: The renewal job charged 212 cancelled subscriptions

*C# · EF Core 10 · SQL Server*

**The situation.** A project-management SaaS bills 38,000 subscriptions through a renewal job that runs at 02:00. A subscription's state was a `Status` string plus two flags, `IsCancelled` and `IsSuspended`, written by three code paths: the cancellation endpoint, the dunning logic and the support tool. On 1 March the job charged 212 customers who had cancelled, £89 each on average, and support spent a week refunding them.

**Before**

*Before: three fields, three writers*

```csharp
public class Subscription
{
    public string Status { get; set; } = "Trial";        // (1) free text, written by three code paths
    public bool IsCancelled { get; set; }                 // (2) set by the cancel endpoint
    public bool IsSuspended { get; set; }                 // (3) set by dunning, cleared by support "sometimes"
    // ... CurrentPeriodEnd, CancelledAt, FailedPayments, PlanPrice
}

// RenewalJob: trials were converted by a separate job
var due = await db.Subscriptions
    .Where(s => s.Status != "Trial" && s.CurrentPeriodEnd <= now)          // (4) deny-list: everything else is charged
    .ToListAsync(ct);

foreach (var sub in due)
{
    var charge = await payments.ChargeAsync(sub.CustomerId, sub.PlanPrice, ct);
    if (charge.Succeeded) { sub.Status = "Active"; sub.CurrentPeriodEnd = sub.CurrentPeriodEnd.AddMonths(1); }
    else { sub.Status = "PastDue"; sub.IsSuspended = ++sub.FailedPayments >= 2; }   // (5) suspension as a side effect
}
await db.SaveChangesAsync(ct);

// CancelSubscriptionEndpoint
sub.IsCancelled = true;                                                     // (6) Status stays "Active"
sub.CancelledAt = clock.GetUtcNow();
```

**What goes wrong.** The cancellation endpoint (6) sets `IsCancelled` and leaves `Status` alone, because its author thought of `Status` as "the billing field". The renewal job (4) selects on `Status` with a deny-list: anything that is not a trial gets charged. Together they charged 212 cancelled customers. Even a correct `Status = "Cancelled"`, which the support tool writes, passes the same filter.

Three fields (1, 2, 3) describe one fact and three code paths write them, so they disagree. Dunning suspends by side effect inside a payment branch (5), and support clears `Status` without clearing `IsSuspended` (3). Every reader picks a different field to trust: the access check reads `IsSuspended`, so 9 subscriptions marked Suspended kept using the product for free.

**Evidence**

*Subscriptions grouped by Status, IsCancelled and IsSuspended, March*

```text
Status     IsCancelled IsSuspended Subscriptions
---------- ----------- ----------- -------------
Active     0           0                  31866
Active     0           1                     41   <- support cleared PastDue, not the flag
Active     1           0                    212   <- cancelled, but charged on 1 March
Cancelled  0           0                     17   <- written by the support tool, flags untouched
PastDue    0           0                    904
PastDue    1           1                     37   <- cancelled and suspended at once
Suspended  0           0                      9   <- access check reads IsSuspended: still active
Trial      0           0                   4914
```

**After**

*After: one status, one transition table*

```csharp
using static Billing.SubscriptionStatus;
using static Billing.BillingTrigger;

public enum SubscriptionStatus { Trial, Active, PastDue, Suspended, Cancelled }
public enum BillingTrigger { Renew, PaymentFailed, Cancel, Reactivate }

public static class SubscriptionTransitions
{
    // Every legal move. Anything not listed is illegal, including every move out of Cancelled.
    public static readonly IReadOnlyDictionary<(SubscriptionStatus From, BillingTrigger Trigger), SubscriptionStatus> Table =
        new Dictionary<(SubscriptionStatus, BillingTrigger), SubscriptionStatus>
        {
            [(Trial, Renew)] = Active,                // first charge at the end of the trial
            [(Trial, PaymentFailed)] = Suspended,     // no grace on a first charge
            [(Trial, Cancel)] = Cancelled,
            [(Active, Renew)] = Active,
            [(Active, PaymentFailed)] = PastDue,      // 7 days' grace, one retry
            [(Active, Cancel)] = Cancelled,
            [(PastDue, Renew)] = Active,
            [(PastDue, PaymentFailed)] = Suspended,   // the retry failed
            [(PastDue, Cancel)] = Cancelled,
            [(Suspended, Reactivate)] = Active,       // the open invoice was paid
            [(Suspended, Cancel)] = Cancelled,
        }.ToFrozenDictionary();

    public static bool TryNext(SubscriptionStatus from, BillingTrigger trigger, out SubscriptionStatus to) =>
        Table.TryGetValue((from, trigger), out to);

    public static List<SubscriptionStatus> StatesAllowing(BillingTrigger trigger) =>
        Table.Keys.Where(k => k.Trigger == trigger).Select(k => k.From).Distinct().ToList();
}

// SubscriptionConfiguration: stored as text for support's queries, constrained to the enum's names
var names = string.Join(", ", Enum.GetNames<SubscriptionStatus>().Select(n => $"'{n}'"));
b.ToTable("Subscriptions", t => t.HasCheckConstraint("CK_Subscriptions_Status", $"[Status] IN ({names})"));
b.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
```

*The aggregate and the renewal job*

```csharp
public sealed class Subscription
{
    public SubscriptionId Id { get; private set; }
    public SubscriptionStatus Status { get; private set; } = SubscriptionStatus.Trial;
    public DateTimeOffset CurrentPeriodEnd { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int FailedPayments { get; private set; }
    // ... CustomerId, PlanPrice

    public void Renew(DateTimeOffset end) { MoveOn(BillingTrigger.Renew); CurrentPeriodEnd = end; FailedPayments = 0; }
    public void PaymentFailed() { MoveOn(BillingTrigger.PaymentFailed); FailedPayments++; }
    public void Cancel(DateTimeOffset at) { MoveOn(BillingTrigger.Cancel); CancelledAt = at; }
    public void Reactivate(DateTimeOffset end) { MoveOn(BillingTrigger.Reactivate); CurrentPeriodEnd = end; }

    private void MoveOn(BillingTrigger trigger)
    {
        if (!SubscriptionTransitions.TryNext(Status, trigger, out var next))
            throw new InvalidSubscriptionTransitionException(Id, Status, trigger);
        Status = next;
    }
}

// RenewalJob: an allow-list taken from the table, so a new state is never charged by default
var renewable = SubscriptionTransitions.StatesAllowing(BillingTrigger.Renew);     // Trial, Active, PastDue
var due = await db.Subscriptions
    .Where(s => renewable.Contains(s.Status) && s.CurrentPeriodEnd <= now)
    .ToListAsync(ct);

foreach (var sub in due)
{
    var charge = await payments.ChargeAsync(sub.Id, sub.PlanPrice,
        idempotencyKey: $"renew-{sub.Id}-{sub.CurrentPeriodEnd:yyyyMMdd}", ct);   // one charge per period
    if (charge.Succeeded) sub.Renew(sub.CurrentPeriodEnd.AddMonths(1));
    else sub.PaymentFailed();
}
await db.SaveChangesAsync(ct);
```

*A test that walks the whole table*

```csharp
public class SubscriptionTransitionTests
{
    // Written out a second time on purpose: changing a billing rule means changing it here, in review
    private static readonly Dictionary<(SubscriptionStatus, BillingTrigger), SubscriptionStatus> Expected = new()
    {
        [(Trial, Renew)] = Active,     [(Trial, PaymentFailed)] = Suspended,   [(Trial, Cancel)] = Cancelled,
        [(Active, Renew)] = Active,    [(Active, PaymentFailed)] = PastDue,    [(Active, Cancel)] = Cancelled,
        [(PastDue, Renew)] = Active,   [(PastDue, PaymentFailed)] = Suspended, [(PastDue, Cancel)] = Cancelled,
        [(Suspended, Reactivate)] = Active,                                    [(Suspended, Cancel)] = Cancelled,
    };

    [Fact]
    public void Every_status_and_trigger_behaves_as_the_table_says()
    {
        foreach (var from in Enum.GetValues<SubscriptionStatus>())
        foreach (var trigger in Enum.GetValues<BillingTrigger>())
        {
            var sub = TestSubscriptions.In(from);
            if (Expected.TryGetValue((from, trigger), out var to))
            {
                Fire(sub, trigger);
                Assert.Equal(to, sub.Status);
            }
            else
            {
                Assert.Throws<InvalidSubscriptionTransitionException>(() => Fire(sub, trigger));
                Assert.Equal(from, sub.Status);              // an illegal move changes nothing
            }
        }
    }

    private static readonly DateTimeOffset May = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static void Fire(Subscription sub, BillingTrigger trigger)
    {
        if (trigger == Renew) sub.Renew(May);
        else if (trigger == PaymentFailed) sub.PaymentFailed();
        else if (trigger == Cancel) sub.Cancel(May);
        else sub.Reactivate(May);
    }
}
```

**Why it works.** `Status` is now the only state, and it changes only through four methods that consult one table. Anything not in the table throws, so renewing a cancelled subscription fails loudly where the mistake is made, instead of writing a new combination of flags. The two flags were dropped, so nothing is left to disagree with `Status`.

The renewal job's filter is built from the table, which makes it an allow-list: a state added later is never charged until someone adds a `Renew` row for it. The check constraint is generated from the enum, so the support tool's hand-written SQL cannot store "Canceled". The test writes the expected table out again and walks all 20 pairs of status and trigger, so a billing rule changes only deliberately, in two places, in review.

**Result.** The migration surfaced 316 rows whose flags disagreed with `Status`, and finance resolved them by hand in two days. Since April the job has charged no cancelled or suspended subscription. The table has rejected 23 illegal moves, most of them support trying to reactivate cancelled accounts, which now start a new subscription instead.

> **Costs, and when not to.** A transition table holds plain moves well and guards badly. When annual plans got 14 days' grace instead of 7, the rule went into the `PaymentFailed` method rather than the table, and each further guard spreads the rules a little more. Past a few guards, a class per state with its own four methods reads better, at the cost of five classes to keep consistent and no single table to review. Libraries such as Stateless add guards and nested states; for five states and four triggers a dictionary is enough. And the job must catch the exception per subscription, so one bad row does not stop the run.
>
> The pattern is the wrong choice for a two-valued flag like `IsArchived`, or for a status that is only displayed and never decides what the code may do. Nor does it replace concurrency control: a cancel that lands while the job is charging still needs a rowversion check on save.

> **Lesson:** Keep one status column, change it only through a transition table, and select work with an allow-list of states, never a deny-list.

<a id="case-chain-delegating-handlers"></a>

#### Case 13 · Chain of Responsibility: Token refresh and partner headers copied into 14 methods

*C# · HttpClient · IHttpClientFactory*

**The situation.** The shipping service books collections and prints labels through ParcelHub, a carrier aggregator, at about 170,000 calls a day. ParcelHub requires an OAuth bearer token that expires after 60 minutes, an `X-Correlation-Id` header their support team can search on, and an `Idempotency-Key` on every POST. Over 18 months these rules, and logging of status and duration, were copied into all 14 methods of `ParcelHubService`.

**Before**

*Before: one of 14 methods*

```csharp
public sealed class ParcelHubService(HttpClient http, ILogger<ParcelHubService> logger)
{
    private static string? _token;                                  // (1) no lock, no refresh margin
    private static DateTime _tokenExpires;

    public async Task<Shipment> CreateShipmentAsync(CreateShipment command, CancellationToken ct)
    {
        if (_token is null || _tokenExpires < DateTime.UtcNow)      //     14 copies of this check
            (_token, _tokenExpires) = await FetchTokenAsync(ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "v2/shipments") { Content = JsonContent.Create(command) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());    // (2) a new id for every call
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());     // (3) a new key for every attempt

        var sw = Stopwatch.StartNew();
        var response = await http.SendAsync(request, ct);
        logger.LogInformation("POST v2/shipments {Status} in {Ms} ms. Headers: {Headers}",
            response.StatusCode, sw.ElapsedMilliseconds, request.Headers);     // (4) Authorization included
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Shipment>(ct))!;
    }

    // ... 13 more methods with the same 12 lines, each a little different
}
```

**What goes wrong.** The token cache (1) has no lock and no margin, so at expiry every call in flight fetches a new token at once. ParcelHub allows 10 token requests a minute, the rest get 429, and their bookings fail: a burst of errors in the same minute of every hour. A fresh correlation id per call (2) gives the five calls made for one order five unrelated ids, so ParcelHub support cannot match them. The dispatcher retries `CreateShipmentAsync` on timeout, each attempt gets a new idempotency key (3), and ParcelHub sees separate bookings: 38 duplicate collections in June.

Three methods log `request.Headers` (4), which includes `Authorization`. Live tokens sat in a log store that keeps records for 90 days and that all of engineering can read.

**Evidence**

*ParcelHub calls, 1 to 7 June*

```text
calls                                              1,204,330
401 Unauthorized in minute hh:00 to hh:01              2,916
429 Too Many Requests from POST /oauth/token             611
log records containing "Bearer ey"                    48,207
duplicate collections (same order, two bookings)          38
```

**After**

*After: one handler per concern*

```csharp
public sealed class CorrelationIdHandler : DelegatingHandler
{
    public const string Header = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // The trace id is shared by every call made while handling one request or job
        if (!request.Headers.Contains(Header))
            request.Headers.Add(Header, Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"));
        return base.SendAsync(request, ct);
    }
}

public sealed class IdempotencyKeyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // A caller that knows a business key sets it; otherwise one key per logical request
        if (request.Method == HttpMethod.Post && !request.Headers.Contains("Idempotency-Key"))
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return base.SendAsync(request, ct);
    }
}

public sealed class PartnerLoggingHandler(ILogger<PartnerLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, ct);
        // An allow-list of fields: never headers, never bodies, and the path without its query string
        logger.LogInformation("ParcelHub {Method} {Path} returned {Status} in {ElapsedMs:F0} ms ({CorrelationId}, {Bytes} bytes)",
            request.Method.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            request.Headers.TryGetValues(CorrelationIdHandler.Header, out var ids) ? ids.First() : null,
            request.Content?.Headers.ContentLength);
        return response;
    }
}
```

*The token: a singleton cache behind a transient handler*

```csharp
public sealed class ParcelHubTokenCache(IHttpClientFactory factory, IOptions<ParcelHubOptions> options, TimeProvider clock)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private volatile CachedToken? _current;

    public async ValueTask<string> GetAsync(CancellationToken ct)
    {
        if (_current is { } t && clock.GetUtcNow() < t.ExpiresAt - RefreshMargin) return t.Value;

        await _refresh.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while this one waited: one refresh per expiry
            if (_current is { } fresh && clock.GetUtcNow() < fresh.ExpiresAt - RefreshMargin) return fresh.Value;

            var o = options.Value;
            using var response = await factory.CreateClient("parcelhub-auth").PostAsync("oauth/token",
                new FormUrlEncodedContent([new("grant_type", "client_credentials"),
                                           new("client_id", o.ClientId), new("client_secret", o.ClientSecret)]), ct);
            response.EnsureSuccessStatusCode();
            var token = (await response.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
            _current = new CachedToken(token.AccessToken, clock.GetUtcNow().AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
        finally { _refresh.Release(); }
    }

    public void Invalidate() => _current = null;

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken,
                                        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class BearerTokenHandler(ParcelHubTokenCache tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate();   // revoked early
        return response;
    }
}
```

*Registration: the order is the behaviour*

```csharp
builder.Services.Configure<ParcelHubOptions>(builder.Configuration.GetSection("ParcelHub"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ParcelHubTokenCache>();             // one token for the whole process
builder.Services.AddTransient<CorrelationIdHandler>();            // handlers are always transient
builder.Services.AddTransient<IdempotencyKeyHandler>();
builder.Services.AddTransient<PartnerLoggingHandler>();
builder.Services.AddTransient<BearerTokenHandler>();

// No handlers on the token client: with BearerTokenHandler it would ask itself for a token
builder.Services.AddHttpClient("parcelhub-auth", c => c.BaseAddress = new Uri("https://auth.parcelhub.example/"));

builder.Services.AddHttpClient<ParcelHubClient>(c =>
    {
        c.BaseAddress = new Uri("https://api.parcelhub.example/");
        c.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<CorrelationIdHandler>()    // 1 outermost: one id for the logical call
    .AddHttpMessageHandler<IdempotencyKeyHandler>()   // 2 before any retry: one key for every attempt
    .AddHttpMessageHandler<PartnerLoggingHandler>()   // 3 inside any retry: one line per attempt
    .AddHttpMessageHandler<BearerTokenHandler>();     // 4 innermost: each attempt gets a current token

// The 14 methods are back to their own work
public async Task<Shipment> CreateShipmentAsync(CreateShipment command, CancellationToken ct)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "v2/shipments") { Content = JsonContent.Create(command) };
    request.Headers.Add("Idempotency-Key", $"shipment-{command.ShipmentId}");   // survives caller retries too
    using var response = await http.SendAsync(request, ct);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Shipment>(ct))!;
}
```

**Why it works.** Each concern is now a `DelegatingHandler` that does its part to the request, calls `base.SendAsync`, and can inspect the response on the way back. `AddHttpMessageHandler` chains them in registration order, first one outermost, so a request passes 1 to 4 and the response returns 4 to 1. That order is behaviour. A retry handler (covered in another case) belongs between 2 and 3: the correlation id and idempotency key are then set once and carried by every attempt, the logger writes one line per attempt, and the token is fetched per attempt, so a retry after a backoff never sends an expired one. Order does not keep secrets out of logs, though. The handlers share one request object, so by the time the logger reads it after `base.SendAsync`, the bearer handler has already added the token; only the allow-list protects it.

Lifetimes matter as much as order. The factory builds a fresh chain every handler lifetime, two minutes by default, and sets each handler's `InnerHandler` once, so handlers must be transient: a reused instance throws "The 'InnerHandler' property must be null". Handlers resolve from a scope the factory owns, not the request's, so the correlation id comes from `Activity.Current`. The token outlives chains, so it lives in a singleton that refreshes two minutes early, once, behind a `SemaphoreSlim`.

**Result.** The 14 methods lost about 110 lines between them. The hourly bursts of 401 and 429 stopped: the process fetches one token every 58 minutes. There were no duplicate collections in July or August, and no log record has contained a bearer token since the change; the old records were purged.

> **Costs, and when not to.** The chain is invisible from the call site. `CreateShipmentAsync` no longer shows that a token is attached, and nothing enforces the order: move the logging handler outside the retry handler and a call that succeeded on its third attempt logs as one slow success. Keep a test that builds the client from the real container with a stub primary handler (`ConfigurePrimaryHttpMessageHandler`) and asserts the headers that reach it and the log output. Handlers see HTTP, not business operations, so mapping ParcelHub's error codes to domain exceptions belongs in the typed client.
>
> Handlers are also the wrong place to read bodies. `ReadAsStringAsync` on the request makes it readable twice by buffering it in memory, an extra copy of every 2 to 6 MB customs PDF that `UploadDocumentsAsync` sends. Reading a `StreamContent` over a non-seekable stream directly consumes it, and the real send then goes out empty or fails. Log `Content-Length`, not content. And for one call to an API with no auth and no shared headers, a chain is ceremony: set the header in the method.

> **Lesson:** Put per-request HTTP concerns in transient DelegatingHandlers and shared state in singletons, and choose their order deliberately: the first one registered runs outermost.

<a id="case-command-bulk-admin-actions"></a>

#### Case 14 · Command: Bulk repricing timed out at 100 seconds and ran twice

*C# · ASP.NET Core · EF Core 10 · BackgroundService*

**The situation.** The back office of a homeware retailer lets merchandisers and the fraud team run bulk actions, such as repricing a category by a percentage or cancelling every open order for a fraudulent account. Each ran inline, inside the HTTP request. In May a merchandiser repriced the 4,000 products in Garden by +5%. The request failed after 100 seconds, she clicked again, and afterwards nobody could say which products had changed, how many times, or who had asked.

**Before**

*Before: a bulk action inside the request*

```csharp
[HttpPost("admin/products/reprice")]
public async Task<IActionResult> Reprice(RepriceRequest req, CancellationToken ct)    // (1) the whole job, inline
{
    var products = await db.Products
        .Where(p => p.CategoryId == req.CategoryId)
        .OrderBy(p => p.Id)
        .ToListAsync(ct);

    foreach (var p in products)
    {
        p.Price = Math.Round(p.Price * (1 + req.Percent / 100m), 2);
        await db.SaveChangesAsync(ct);                       // (2) one commit per product
        await search.ReindexAsync(p.Id, ct);                 // (3) about 40 ms each: 4,000 x 40 ms = 160 s
    }

    logger.LogInformation("Repriced {Count} products", products.Count);    // (4) no user, no old prices
    return Ok();
}
```

**What goes wrong.** The action does 4,000 units of work inside one request (1). Reindexing (3) takes about 40 ms per product, so the loop needs 160 s, and the admin BFF's `HttpClient` gives up at its default timeout of 100 seconds. That aborts the request and cancels the token, and because each product was committed on its own (2), the first 2,310 kept their new price and the rest did not. The second click repriced from the top again.

Nothing recorded what had happened (4): the log line is written only on success, names no user and keeps no old prices, so the clean-up was a day of diffing against the nightly price snapshot. Cancelling a fraudulent account's 612 orders timed out the same way, after 380.

**Evidence**

*Category 212 (Garden) against the 12 May price snapshot*

```text
14:02:11  first click    request aborted at 100 s after 2,310 products
14:04:30  second click   request aborted at 100 s after 2,280 products
-------------------------------------------------------------------------
2,280 products at +10.25%      30 products at +5%      1,690 unchanged
log lines "Repriced {Count} products": 0      user recorded: nowhere
```

**After**

*After: commands as data, a job as a row*

```csharp
public interface IAdminCommand { }
public sealed record RepriceCategory(int CategoryId, decimal Percent, string Reason) : IAdminCommand;
public sealed record UndoReprice(Guid OriginalJobId, string Reason) : IAdminCommand;
public interface IAdminCommandHandler<in TCommand> where TCommand : IAdminCommand
{
    Task ExecuteAsync(TCommand command, JobContext job, CancellationToken ct);
}

public sealed class AdminJob
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string CommandType { get; init; }    // a stable registry name, never the CLR type name
    public required string Payload { get; init; }        // the command as JSON
    public required string RequestedBy { get; init; }
    public AdminJobStatus Status { get; set; }           // Queued, Running, Succeeded, Failed
    // ... RequestedAt, Total, Processed, Error
}

public sealed class AdminCommandRegistry
{
    private readonly Dictionary<Type, string> _names = new();
    private readonly Dictionary<string, Func<IServiceProvider, AdminJob, JobContext, CancellationToken, Task>> _run = new();

    public AdminCommandRegistry Add<TCommand, THandler>(string name)
        where TCommand : IAdminCommand where THandler : IAdminCommandHandler<TCommand>
    {
        _names.Add(typeof(TCommand), name);
        _run.Add(name, (sp, job, context, ct) => ActivatorUtilities.GetServiceOrCreateInstance<THandler>(sp)
            .ExecuteAsync(JsonSerializer.Deserialize<TCommand>(job.Payload)!, context, ct));
        return this;
    }
    public string NameOf(IAdminCommand command) => _names[command.GetType()];
    public Task RunAsync(IServiceProvider sp, AdminJob job, JobContext context, CancellationToken ct) =>
        _run[job.CommandType](sp, job, context, ct);
}

// Program.cs: these names are stored in the database, so they never change once used
builder.Services.AddSingleton(new AdminCommandRegistry()
    .Add<RepriceCategory, RepriceCategoryHandler>("reprice-category.v1")
    .Add<UndoReprice, UndoRepriceHandler>("undo-reprice.v1"));          // ... and cancel-customer-orders.v1
```

*The worker: claim, dispatch, record*

```csharp
// The endpoint calls queue.EnqueueAsync(command, user, ct), which inserts one row, and returns
// 202 Accepted with /admin/jobs/{id} to poll for progress.
public sealed class AdminJobWorker(IServiceScopeFactory scopes, AdminCommandRegistry registry,
    TimeProvider clock, ILogger<AdminJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            // Oldest first by RequestedAt, not Id: SQL Server does not order a v7 GUID by its timestamp
            var job = await db.AdminJobs.AsNoTracking().Where(j => j.Status == AdminJobStatus.Queued)
                .OrderBy(j => j.RequestedAt).FirstOrDefaultAsync(stoppingToken);
            if (job is null) continue;

            // Claim it: only one worker instance can move a job from Queued to Running
            var claimed = await db.AdminJobs.Where(j => j.Id == job.Id && j.Status == AdminJobStatus.Queued)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, AdminJobStatus.Running), stoppingToken);
            if (claimed == 0) continue;

            var (status, error) = (AdminJobStatus.Succeeded, (string?)null);
            try { await registry.RunAsync(scope.ServiceProvider, job, new JobContext(job.Id, db), stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Admin job {JobId} failed", job.Id);
                (status, error) = (AdminJobStatus.Failed, ex.Message);
            }
            await db.AdminJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status).SetProperty(j => j.Error, error), CancellationToken.None);
            db.AdminAudit.Add(AdminAuditEntry.For(job, status, clock.GetUtcNow()));   // append-only, kept 7 years
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
```

*A batched handler that keeps old prices, and its undo*

```csharp
public sealed class RepriceCategoryHandler(CatalogDbContext db, ISearchIndex search) : IAdminCommandHandler<RepriceCategory>
{
    public async Task ExecuteAsync(RepriceCategory command, JobContext job, CancellationToken ct)
    {
        await job.SetTotalAsync(await db.Products.CountAsync(p => p.CategoryId == command.CategoryId, ct), ct);
        for (var lastId = 0; ;)
        {
            var batch = await db.Products
                .Where(p => p.CategoryId == command.CategoryId && p.Id > lastId
                         && !db.PriceChanges.Any(c => c.JobId == job.Id && c.ProductId == p.Id))   // done before a restart
                .OrderBy(p => p.Id).Take(250).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var p in batch)
            {
                var newPrice = Math.Round(p.Price * (1 + command.Percent / 100m), 2, MidpointRounding.AwayFromZero);
                db.PriceChanges.Add(new PriceChange(job.Id, p.Id, OldPrice: p.Price, NewPrice: newPrice));
                p.Price = newPrice;
            }
            await db.SaveChangesAsync(ct);                     // 250 prices and their history, one transaction
            db.ChangeTracker.Clear();
            await search.ReindexAsync(batch.Select(p => p.Id).ToList(), ct);    // one bulk call per batch
            await job.AddProgressAsync(batch.Count, ct);       // UPDATE ... SET Processed = Processed + @n
            lastId = batch[^1].Id;
        }
    }
}

public sealed class UndoRepriceHandler(CatalogDbContext db, ISearchIndex search) : IAdminCommandHandler<UndoReprice>
{
    public async Task ExecuteAsync(UndoReprice command, JobContext job, CancellationToken ct)
    {
        var rows = await db.PriceChanges.Where(c => c.JobId == command.OriginalJobId)
            .Join(db.Products, c => c.ProductId, p => p.Id, (c, p) => new { c.OldPrice, c.NewPrice, Product = p })
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            if (row.Product.Price != row.NewPrice)            // changed by hand since: report it, don't overwrite it
            {
                job.Skip(row.Product.Id, $"price is now {row.Product.Price}, not {row.NewPrice}");
                continue;
            }
            db.PriceChanges.Add(new PriceChange(job.Id, row.Product.Id, OldPrice: row.NewPrice, NewPrice: row.OldPrice));
            row.Product.Price = row.OldPrice;
        }
        await db.SaveChangesAsync(ct);                        // a few thousand rows: one transaction, all or nothing
        await search.ReindexAsync(rows.Select(r => r.Product.Id).ToList(), ct);
    }
}
```

**Why it works.** A command turns a request into data: the type name says what to do, the JSON says with which arguments, and the row says who asked and when. As a row, an action can be queued, resumed, shown with progress and kept, and the request returns 202 at once, so the 100-second limit stops mattering. Rows store a stable name such as `reprice-category.v1`, not a CLR type name, so a rename cannot strand a queued job, and the registry is the complete list of types the worker will deserialise.

Each batch of 250 commits new prices and their `PriceChange` rows together, and the handler skips products that already have a history row for this job, so a requeued job carries on instead of applying +5% twice. The worker writes the audit row when a job ends, whatever the outcome. Undo is just another command: it restores only prices that still hold the value the job set.

**Result.** Repricing 4,000 products now takes 2 minutes 40 seconds in the worker, with a progress bar, and the request returns in about 30 ms; an identical command while one is queued or running gets a 409. In August an undo of a mistaken -15% restored 1,172 prices and listed the 8 that had been changed by hand since.

> **Costs, and when not to.** Commands as rows bring the problems of a queue: a worker to deploy and monitor, jobs left in Running after a crash to requeue at startup, and payloads to version. Renaming a property of `RepriceCategory` breaks queued jobs and old audit payloads, hence the `.v1` names and additive changes only. Undo works only for effects you own: cancelled orders were refunded through the payment provider and customers were emailed, so "undo cancel" is a new business process, not a reversal.
>
> Do not do this for an action that touches a few hundred rows and finishes in a second: run it in the request, in one transaction. And if you already run Hangfire or a message broker, send the command there rather than writing a jobs table.

> **Lesson:** Turn slow or risky bulk actions into stored commands: a row you can queue, resume and audit, and undo when you kept the old values.

<a id="case-null-object-loyalty"></a>

#### Case 15 · Null Object: 31 null checks for customers without a loyalty account

*C# · ASP.NET Core · EF Core 10*

**The situation.** A fashion retailer's web shop takes 38% of its orders from customers without a loyalty account: guests, and registered customers who never joined. For all of them `Customer.Loyalty` is null. After a run of NullReferenceExceptions in checkout, receipts and the account page, developers added a null check wherever it crashed, and there are now 31 of them in 17 files. In September finance found that two were wrong, and had been costing money for two months.

**Before**

*Before: three of the 31 checks*

```csharp
// PriceCalculator.cs, checkout
public Money Discounts(Basket basket, LoyaltyAccount? loyalty, Voucher? voucher)
{
    if (loyalty == null) return Money.Zero(basket.Currency);        // (1) also skips the voucher below
    var discount = loyalty.RedemptionValue(basket.PointsToRedeem, basket.Currency);
    if (voucher != null) discount += voucher.DiscountFor(basket);
    return discount;
}

// ShippingCalculator.cs
var tier = loyalty?.Tier ?? LoyaltyTier.Bronze;                    // (2) non-members charged as Bronze:
return DeliveryPolicy.Charge(basket, tier);                        //     free delivery from £40, not £60

// ReceiptBuilder.cs
if (customer?.Loyalty != null && customer.Loyalty.Balance > 0)     // (3) check 17 of 31
    receipt.AddLine($"Points balance: {customer.Loyalty.Balance:N0}");
```

**What goes wrong.** Each check is a separate decision about what a missing account means, made by whoever hit the crash. In `Discounts` (1), no account was taken to mean no discounts, and the early return also skips the voucher, which has nothing to do with loyalty: 1,920 non-members paid full price with a valid voucher. In `ShippingCalculator` (2), someone picked Bronze as the default tier, which gives non-members Bronze's free delivery from £40 instead of £60. Each line looks reasonable on its own, and both passed review.

The other 29 checks are right, for now. Every new feature that touches loyalty starts with a NullReferenceException in testing, or in production, followed by another check: the receipt line (3) became check 17 after 2,600 receipt emails failed over one weekend.

**Evidence**

*The checks, and finance's review*

```text
$ git grep -iE "loyalty( [!=]= null|[?][.])" -- '*.cs' | wc -l
31
$ git grep -liE "loyalty( [!=]= null|[?][.])" -- '*.cs' | wc -l
17

Orders by customers without a loyalty account, 1 July to 31 August
  valid voucher entered but not applied       1,920 orders     £14,300 in discounts not given
  free delivery given between £40 and £60     6,410 orders     £25,300 in delivery not charged
```

**After**

*After: an account that earns nothing*

```csharp
public interface ILoyaltyAccount
{
    LoyaltyTier Tier { get; }
    int Balance { get; }
    int PointsFor(Money spend);
    Money RedemptionValue(int points, Currency currency);    // capped at the balance
    void Earn(int points, OrderId order);
}

// The EF entity, unchanged apart from implementing the interface
public sealed class LoyaltyAccount : ILoyaltyAccount { /* ... */ }

// An account that can never earn or spend. It has no state, so one instance is shared.
public sealed class NoLoyaltyAccount : ILoyaltyAccount
{
    public static readonly NoLoyaltyAccount Instance = new();
    private NoLoyaltyAccount() { }

    public LoyaltyTier Tier => LoyaltyTier.None;              // no benefits: free delivery from £60, like a guest
    public int Balance => 0;
    public int PointsFor(Money spend) => 0;
    public Money RedemptionValue(int points, Currency currency) => Money.Zero(currency);
    public void Earn(int points, OrderId order) { }           // nowhere to put them, and PointsFor said 0
}
```

*Where it is used, and where it is not*

```csharp
public sealed class LoyaltyAccounts(ShopDbContext db)
{
    // Where absence matters (join banner, support tool): nullable, so the compiler makes callers check
    public Task<LoyaltyAccount?> FindAsync(CustomerId customer, CancellationToken ct) =>
        db.LoyaltyAccounts.SingleOrDefaultAsync(a => a.CustomerId == customer, ct);

    // Where absence is neutral (pricing, shipping, receipts): never null
    public async Task<ILoyaltyAccount> ForCheckoutAsync(Customer? customer, CancellationToken ct)
    {
        if (customer?.LoyaltyJoinedAt is null)
            return NoLoyaltyAccount.Instance;                         // a guest, or never joined: expected
        return await FindAsync(customer.Id, ct)
            ?? throw new InvalidOperationException(                   // joined but no row: a bug, so say so
                $"Customer {customer.Id} joined loyalty on {customer.LoyaltyJoinedAt:d} but has no account.");
    }
}

// Checkout: no branch on membership, so no early return can skip the voucher
public Money Discounts(Basket basket, ILoyaltyAccount loyalty, Voucher? voucher) =>
    loyalty.RedemptionValue(basket.PointsToRedeem, basket.Currency)
    + (voucher?.DiscountFor(basket) ?? Money.Zero(basket.Currency));

public Money Shipping(Basket basket, ILoyaltyAccount loyalty) => DeliveryPolicy.Charge(basket, loyalty.Tier);

// Receipt: whether to print a line is a presentation choice, not a null check
var earned = loyalty.PointsFor(order.Total);
if (earned > 0) receipt.AddLine($"Points earned: {earned:N0}");

// Account page: absence is the point here, so it uses the nullable API
var account = await accounts.FindAsync(customer.Id, ct);
return account is null ? View("JoinLoyalty") : View("Loyalty", LoyaltyViewModel.From(account));
```

**Why it works.** The decision about what a customer without an account gets is now made once, in `NoLoyaltyAccount`: no tier benefits, a zero balance, no points earned, nothing to redeem. Callers just call methods. Pricing has no membership branch, so there is no early return to skip the voucher, and shipping reads `Tier`, which is `None` for non-members by construction rather than by each caller's choice of default.

The resolver substitutes the null object only where absence is expected: a guest, or a customer who never joined. A customer who joined but has no account row is a data problem, and it throws instead of quietly earning nothing. Code where absence changes the outcome, such as the account page's join banner, calls `FindAsync`, which returns `LoyaltyAccount?`; with nullable warnings treated as errors, the build fails on a dereference without a check.

**Result.** The 31 checks came out, and the two wrong ones were fixed by deletion. Checkout pricing lost 9 tests, because members and non-members now run the same code. In its first month the resolver's exception fired for 14 customers whose sign-up in 2024 had failed between creating the customer and creating the account. They had been earning nothing for up to two years, and support credited them.

> **Costs, and when not to.** A Null Object is a decision about what absence means, made once, and where that decision is wrong for some caller it is wrong silently. An `Earn` that ignores points is right in checkout, where `PointsFor` has already returned 0, and wrong in the support tool, where an agent adding 500 goodwill points would see success while nothing happened; the support tool therefore uses `FindAsync` and shows "no loyalty account". The same goes for data that should exist: returning `NoLoyaltyAccount` for a member with a missing row would have hidden the 14 broken sign-ups for good. Use it only where "no account" and "an empty account" really behave the same.
>
> When absence is meaningful, because you show a banner, refuse an action or report a data problem, keep it visible: return `LoyaltyAccount?` with `<WarningsAsErrors>nullable</WarningsAsErrors>` in the project file, or return an explicit result with cases such as `NotJoined`, `Found` and `Missing`. And do not build a null object for a type with two call sites: one `is null` check is clearer than a class.

> **Lesson:** Use a Null Object where absence and emptiness behave the same, and keep absence visible, through nullable types or an explicit result, wherever it changes the outcome.

<a id="case-visitor-filter-tree"></a>

#### Case 16 · Visitor: Saved searches broke when someone added an InList filter

*C# · EF Core 10 · System.Linq.Expressions*

**The situation.** Operations staff in an order-management app have saved about 3,100 searches such as "Status is Shipped and total between 100 and 500". Each is a tree of filter nodes stored as JSON and used three ways: evaluated in memory against each new order for notifications, translated to an EF Core query for the search page, and rendered as text. Each use had its own type checks, and when `InList` arrived in March, only two of the three learned about it.

**Before**

*Before: the same type checks in three files*

```csharp
// SavedSearchQuery.cs: the search page
static IQueryable<Order> Apply(IQueryable<Order> query, FilterNode node)
{
    if (node is AndFilter a) return a.Children.Aggregate(query, Apply);      // (1) chained Where: no way to do Or
    if (node is EqualsFilter { Field: "Status" } s) return query.Where(o => o.Status == (OrderStatus)s.Value);
    if (node is EqualsFilter { Field: "Country" } c) return query.Where(o => o.Country == (string)c.Value);
    if (node is BetweenFilter { Field: "Total" } b)
        return query.Where(o => o.Total >= (decimal)b.From && o.Total < (decimal)b.To);   // (2) < here, <= below
    throw new NotSupportedException($"Saved search node {node.GetType().Name} is not supported");   // (3)
}

// NotificationMatcher.cs: the same checks again, with Between inclusive at both ends
// FilterText.cs: and again, for the UI
static string Describe(FilterNode node) => node switch
{
    AndFilter a => string.Join(" and ", a.Children.Select(Describe)),
    EqualsFilter e => $"{e.Field} is {e.Value}",
    BetweenFilter b => $"{b.Field} between {b.From} and {b.To}",
    InListFilter l => $"{l.Field} is one of {string.Join(", ", l.Values)}",
    _ => "",                                                               // (4) Or and Not render as nothing
};
```

**What goes wrong.** Three files make the same type checks, and nothing connects them. The query builds And by chaining `Where` (1), which cannot express Or, so it throws (3) for every search containing Or, Not or InList: 418 of them failed on the search page. The text renderer falls through to an empty string (4), so "Status is Shipped or Status is Returned" displays as nothing. Adding a node type compiled cleanly and failed at runtime in whichever file was forgotten.

The copies also disagree on meaning. The matcher's Between is inclusive at both ends and the query uses `<` at the top (2), so an order totalling exactly 500.00 triggers a notification and is missing from the results it links to.

**Evidence**

*Saved searches on the search page, 2 April*

```text
saved searches                              3,112
ran without error                           2,694
NotSupportedException: Or or Not              276
NotSupportedException: InList                 142

"Total between 100 and 500", order SO-77120, total 500.00
  notification sent      yes    matcher uses <=
  shown in results       no     query uses <
```

**After**

*After: the classic Visitor*

```csharp
public enum OrderField { Status, Country, Total, PlacedAt }

public abstract class FilterNode
{
    public abstract T Accept<T>(IFilterVisitor<T> visitor);
}

public sealed class AndFilter(IReadOnlyList<FilterNode> children) : FilterNode
{
    public IReadOnlyList<FilterNode> Children { get; } = children;
    public override T Accept<T>(IFilterVisitor<T> visitor) => visitor.VisitAnd(this);
}
// OrFilter(Children), NotFilter(Inner), EqualsFilter(Field, Value), BetweenFilter(Field, From, To) and
// InListFilter(Field, Values) have the same shape; values get the field's CLR type when the JSON is read

public interface IFilterVisitor<out T>
{
    T VisitAnd(AndFilter node);
    T VisitOr(OrFilter node);
    T VisitNot(NotFilter node);
    T VisitEquals(EqualsFilter node);
    T VisitBetween(BetweenFilter node);
    T VisitInList(InListFilter node);   // a new node type adds a method here, and every visitor stops compiling
}

// The smallest visitor, the text renderer: "status is Shipped and total between 100 and 500"
public sealed class FilterDescriber : IFilterVisitor<string>
{
    public string VisitAnd(AndFilter n) => string.Join(" and ", n.Children.Select(Wrap));
    public string VisitBetween(BetweenFilter n) => $"{Label(n.Field)} between {n.From} and {n.To}";
    // ... one line for each of the other four node types

    private string Wrap(FilterNode child) => child is AndFilter or OrFilter ? $"({child.Accept(this)})" : child.Accept(this);
    private static string Label(OrderField field) => field.ToString().ToLowerInvariant();
}
```

*The EF Core visitor: an expression tree EF can translate*

```csharp
public sealed class OrderPredicateBuilder : IFilterVisitor<Expression>
{
    private readonly ParameterExpression _order = Expression.Parameter(typeof(Order), "o");   // one, shared by every node

    public static Expression<Func<Order, bool>> Build(FilterNode root)
    {
        var builder = new OrderPredicateBuilder();
        return Expression.Lambda<Func<Order, bool>>(root.Accept(builder), builder._order);
    }

    // Saved searches are validated on save to have at least one child, so Aggregate never sees an empty list
    public Expression VisitAnd(AndFilter n) => n.Children.Select(c => c.Accept(this)).Aggregate(Expression.AndAlso);
    public Expression VisitOr(OrFilter n) => n.Children.Select(c => c.Accept(this)).Aggregate(Expression.OrElse);
    public Expression VisitNot(NotFilter n) => Expression.Not(n.Inner.Accept(this));

    public Expression VisitEquals(EqualsFilter n)
    {
        var property = Property(n.Field);
        return Expression.Equal(property, Value(n.Value, property.Type));
    }
    public Expression VisitBetween(BetweenFilter n)     // inclusive at both ends, like the matcher and the text
    {
        var property = Property(n.Field);
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(property, Value(n.From, property.Type)),
            Expression.LessThanOrEqual(property, Value(n.To, property.Type)));
    }
    public Expression VisitInList(InListFilter n)
    {
        var property = Property(n.Field);
        var array = Array.CreateInstance(property.Type, n.Values.Count);      // string[], OrderStatus[], ...
        for (var i = 0; i < n.Values.Count; i++) array.SetValue(n.Values[i], i);
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [property.Type],
            Value(array, array.GetType()), property);
    }

    private MemberExpression Property(OrderField field) => Expression.Property(_order, field.ToString());

    // A member access on a constant is the shape of a captured variable, so EF sends a parameter, not a literal
    private static MemberExpression Value(object value, Type type) =>
        Expression.Property(Expression.Constant(Activator.CreateInstance(typeof(Box<>).MakeGenericType(type), value)), "Value");

    public sealed record Box<T>(T Value);
}
// Usage: db.Orders.Where(OrderPredicateBuilder.Build(search.Root))
```

*The alternative: sealed records and a switch expression*

```csharp
// The same tree without Accept: sealed records, and each operation as one switch
public abstract record Filter;
public sealed record AndFilter(IReadOnlyList<Filter> Children) : Filter;
public sealed record BetweenFilter(OrderField Field, IComparable From, IComparable To) : Filter;
// ... OrFilter, NotFilter, EqualsFilter and InListFilter likewise

public static class FilterMatcher           // the notification matcher; order.Get returns the boxed value
{
    public static bool Matches(this Filter filter, OrderSnapshot order) => filter switch
    {
        AndFilter f => f.Children.All(c => c.Matches(order)),
        OrFilter f => f.Children.Any(c => c.Matches(order)),
        NotFilter f => !f.Inner.Matches(order),
        EqualsFilter f => object.Equals(order.Get(f.Field), f.Value),
        BetweenFilter f => f.From.CompareTo(order.Get(f.Field)) <= 0 && f.To.CompareTo(order.Get(f.Field)) >= 0,
        InListFilter f => f.Values.Contains(order.Get(f.Field)),
        // C# cannot know the hierarchy is closed: a seventh record compiles and lands here at runtime
        _ => throw new UnreachableException($"No rule for {filter.GetType().Name}"),
    };
}
```

**Why it works.** Each operation is now one class that must handle every node type, and each node has one `Accept` that calls the matching `Visit`. The compiler connects the two: when `ContainsText` was added in September, `IFilterVisitor<T>` gained a method, and the build listed the three visitors that needed it.

`OrderPredicateBuilder` builds the same kind of tree as a lambda such as `o => o.Status == s && o.Total >= a && o.Total <= b`: one `ParameterExpression` shared by every node, `Expression.Property` for fields, `AndAlso`, `OrElse` and `Not` for logic, and `Enumerable.Contains` over a typed array for InList. With no compiled delegate or custom method inside, EF translates all of it to SQL. Values are wrapped in a member access, the shape of a captured variable, so EF sends parameters rather than literals and searches of the same shape share a plan.

**Result.** All 3,112 saved searches now run, including the 418 with Or, Not or InList. A test runs every stored shape through the matcher and through EF against the same 500 orders in a SQL Server test container. It caught the Between bound and one more disagreement: SQL Server's collation ignores case in Country, and the matcher did not.

> **Costs, and when not to.** Both versions keep an operation in one place, so adding an operation is cheap either way: a new visitor or a new switch. They differ when a node type is added. The Visitor turns a forgotten operation into a compile error, exactly the bug this team had; the switch compiles, because C# cannot know the hierarchy is closed, and the new node hits `_ => throw` at runtime. So prefer the Visitor while node types are still being added, and sealed records with switch expressions once the node set is stable and operations are what grows, with one test that runs every operation over each node type. Virtual methods on the nodes make the opposite trade: a new node is one class, but each new operation edits every node and pulls EF and UI code into domain types.
>
> The Visitor is heavy: six methods per operation and an `Accept` on every node. Neither version is worth it for a search form with fixed criteria: five optional fields are five `if` statements adding `Where` clauses, not a tree.

> **Lesson:** Use a Visitor while node types are still being added and every operation must handle all of them; once the node set is stable, sealed records and switch expressions do the job with less ceremony.

### Application patterns

Patterns that organise a whole use case or request pipeline rather than one class.

<a id="case-observer-domain-events"></a>

#### Case 17 · Observer / domain events: A Slack outage failed checkout for the biggest orders

*C# · ASP.NET Core · EF Core 10*

**The situation.** `OrderService.PlaceAsync` in an online furniture shop started at 40 lines. Over two years it gained a confirmation email, loyalty points, an analytics event, a webhook to the third-party warehouse and a Slack alert for orders over £2,000, each one another edit to the most important method in the shop. During a 47-minute Slack outage in February, 31 orders over £2,000 were saved and paid for, and every one of those customers saw an error page.

**Before**

*Before: every reaction inside the use case*

```csharp
public async Task<OrderId> PlaceAsync(PlaceOrder cmd, CancellationToken ct)
{
    var order = Order.Create(cmd.CustomerId, cmd.Lines, cmd.DeliveryAddress, clock.GetUtcNow());
    await payments.CaptureAsync(order.Id, order.Total, cmd.PaymentToken, ct);
    db.Orders.Add(order);
    await db.SaveChangesAsync(ct);                                       // the order is committed here

    await email.SendOrderConfirmationAsync(order, ct);                   // (1) sent before the points exist
    var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
    customer.LoyaltyPoints += LoyaltyRules.PointsFor(order.Total);
    await db.SaveChangesAsync(ct);                                       // (2) a second transaction
    await analytics.TrackAsync("order_placed", new { order.Id, order.Total }, ct);
    await warehouse.NotifyAsync(order, ct);                              // (3) up to 3 s, on every checkout
    if (order.Total > 2_000m)
        await slack.PostAsync("#sales", $"Big order {order.Number}: £{order.Total:N0}", ct);   // (4) threw a 503

    return order.Id;
}
```

**What goes wrong.** Everything after the first `SaveChangesAsync` runs once the order is committed and the card captured, yet any failure there reaches the customer as a failed checkout. When Slack returned 503 (4), the 31 largest orders of the afternoon got a 500, and 9 of those customers tried again and were charged twice. The loyalty update (2) is a second transaction, so when it deadlocks the order exists without its points: 140 support tickets last year. The email (1) can promise points that never arrive, and the warehouse webhook (3) adds up to 3 seconds to every checkout.

The method also knows five other teams' concerns: each new reaction is a merge conflict in checkout, and a bug in analytics code can fail a sale.

**Evidence**

*Incident review, 11 February*

```text
14:06 to 14:53, Slack API returning 503
  checkouts                                    1,204
  checkouts over £2,000                           31   all HTTP 500, after payment capture
  of those, orders saved and captured             31
  customers who retried                            9   charged twice, refunded by hand

Normal day
  p95 checkout latency                         2.8 s   of which the warehouse webhook 1.9 s
```

**After**

*After: the aggregate raises a fact*

```csharp
public interface IDomainEvent { }
public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, Money Total, DateTimeOffset PlacedAt) : IDomainEvent;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];
    protected void Raise(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public IReadOnlyList<IDomainEvent> TakeEvents()
    {
        var taken = _events.ToList();
        _events.Clear();
        return taken;
    }
}

public sealed class Order : AggregateRoot
{
    public static Order Place(CustomerId customer, IReadOnlyList<OrderLine> lines, Address deliverTo, DateTimeOffset now)
    {
        var order = new Order(OrderId.New(), customer, lines, deliverTo, now);
        order.Raise(new OrderPlaced(order.Id, customer, order.Total, now));
        return order;
    }
    // ...
}

// OrderService.PlaceAsync, back to the use case
var order = Order.Place(cmd.CustomerId, cmd.Lines, cmd.DeliveryAddress, clock.GetUtcNow());
await payments.CaptureAsync(order.Id, order.Total, cmd.PaymentToken, ct);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);              // the OrderPlaced handlers run inside this call
return order.Id;
```

*Dispatch inside SaveChangesAsync, before the commit*

```csharp
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options, DomainEventDispatcher events)
    : DbContext(options)
{
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        // Before commit. Handlers change tracked entities or add outbox rows, and base.SaveChangesAsync
        // saves all of it in one transaction, so it commits or rolls back together with the order.
        for (var pass = 1; ; pass++)
        {
            var pending = ChangeTracker.Entries<AggregateRoot>().SelectMany(e => e.Entity.TakeEvents()).ToList();
            if (pending.Count == 0) break;
            if (pass > 5) throw new InvalidOperationException("Domain events still being raised after 5 passes.");
            foreach (var domainEvent in pending)
                await events.DispatchAsync(domainEvent, ct);      // handlers may raise further events
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    // The synchronous path would skip the handlers, so it is closed
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync.");
    // ...
}

public sealed class DomainEventDispatcher(IServiceProvider services)
{
    private static readonly MethodInfo DispatchTypedMethod = typeof(DomainEventDispatcher)
        .GetMethod(nameof(DispatchTyped), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct) =>
        (Task)DispatchTypedMethod.MakeGenericMethod(domainEvent.GetType()).Invoke(this, [domainEvent, ct])!;

    private async Task DispatchTyped<TEvent>(TEvent domainEvent, CancellationToken ct) where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, ct);
    }
}
```

*Handlers: in the transaction, or after commit via the outbox*

```csharp
// In the transaction: the receipt promises these points, so they commit with the order or not at all
public sealed class AwardLoyaltyPoints(ShopDbContext db) : IDomainEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        var account = await db.LoyaltyAccounts.SingleOrDefaultAsync(a => a.CustomerId == e.CustomerId, ct);
        account?.Earn(LoyaltyRules.PointsFor(e.Total), e.OrderId);       // a tracked change, saved with the order
    }
}

// Anything external goes through the outbox (covered in its own case): a row now, delivery after commit
public sealed class PublishOrderPlaced(ShopDbContext db) : IDomainEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced e, CancellationToken ct)
    {
        db.OutboxMessages.Add(OutboxMessage.From(new OrderPlacedV1(
            e.OrderId.Value, e.CustomerId.Value, e.Total.Amount, e.Total.Currency.Code, e.PlacedAt)));
        return Task.CompletedTask;
    }
}

// After commit, run by the outbox relay with its own retries. A Slack outage delays an alert, nothing more.
public sealed class BigOrderSlackAlert(ISlackClient slack) : IIntegrationEventHandler<OrderPlacedV1>
{
    public Task HandleAsync(OrderPlacedV1 e, CancellationToken ct) =>
        e.Total <= 2_000m
            ? Task.CompletedTask
            : slack.PostAsync("#sales", $"Big order {e.OrderId}: £{e.Total:N0}", ct);
}
// Also subscribed to OrderPlacedV1: SendOrderConfirmation, TrackOrderPlaced, NotifyWarehouse

// Program.cs
builder.Services.AddScoped<DomainEventDispatcher>();
builder.Services.AddScoped<IDomainEventHandler<OrderPlaced>, AwardLoyaltyPoints>();
builder.Services.AddScoped<IDomainEventHandler<OrderPlaced>, PublishOrderPlaced>();
builder.Services.AddScoped<IIntegrationEventHandler<OrderPlacedV1>, BigOrderSlackAlert>();
```

**Why it works.** `PlaceAsync` is back to its own job: create the order, take the payment, save. The aggregate records a fact, `OrderPlaced`, and does not know who listens. A new reaction is a new handler and one registration line, and checkout's code and tests do not change.

The important decision is when each handler runs. Handlers dispatched inside `SaveChangesAsync` run before the commit, in the same transaction, so they are for changes that must be true together with the order: the points the receipt promises, a stock reservation, the outbox row itself. If one fails, nothing commits. Anything that talks to another system runs after the commit, from the outbox: an email must never go out for an order that rolled back, nor be able to roll one back. Calling those handlers in-process right after `base.SaveChangesAsync` looks simpler, but a crash between the commit and the call loses the email for good; that gap is what the outbox closes.

**Result.** `PlaceAsync` went from 64 lines and seven dependencies to 12 lines and three. The p95 checkout latency fell from 2.8 s to 0.9 s once the warehouse webhook left the request. In the next Slack outage, in May, sales alerts arrived 22 minutes late and checkout was unaffected. The gift-card notification added in June was one handler and one registration.

> **Costs, and when not to.** You can no longer see what placing an order does by reading `PlaceAsync`; the answer is spread across handlers found by type. Keep a test that resolves `IEnumerable<IDomainEventHandler<OrderPlaced>>` and asserts the list. Handlers run in registration order, and none should depend on it: two handlers that must run in sequence are one handler. Everything after the commit is eventually consistent, so the confirmation page cannot say "email sent". Every in-transaction handler lengthens the transaction and can fail checkout, so keep that list short and free of network calls.
>
> Do not turn steps of the use case into events. Capturing payment is not a reaction to `OrderPlaced`, and making it one would hide the most important line in checkout. Events pay off when several independent reactions exist and more are coming; with one side effect that will not grow, write the outbox row in `PlaceAsync` and skip the dispatcher.

> **Lesson:** Raise a domain event for side effects: run the handlers that must succeed with the order before the commit, and send anything external after it through the outbox.

<a id="case-mediator-pipeline-behaviours"></a>

#### Case 18 · Mediator: 80 endpoints, 80 slightly different copies of the same plumbing

*C# · Minimal APIs · FluentValidation*

**The situation.** A B2B invoicing API has 80 minimal API endpoints, and each repeats the same plumbing: validate the body, log the call, open a transaction, and turn exceptions into an error response. Every copy is a little different. In August an audit by the team's largest API consumer found three different shapes for validation errors, 11 endpoints that returned 500 for a duplicate invoice number, and 6 write endpoints with no transaction at all.

**Before**

*Before: one of 80 endpoints*

```csharp
invoices.MapPost("/{id:guid}/credit-notes", async (Guid id, IssueCreditNoteBody body,
    IValidator<IssueCreditNoteBody> validator, BillingDbContext db, ILogger<Program> logger, CancellationToken ct) =>
{
    var validation = await validator.ValidateAsync(body, ct);
    if (!validation.IsValid)
        return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));    // (1) a bare string array
    logger.LogInformation("Issuing credit note for {InvoiceId}", id);               // (2) worded differently everywhere

    await using var tx = await db.Database.BeginTransactionAsync(ct);                // (3) in 74 of 80 endpoints
    try
    {
        var invoice = await db.Invoices.Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Results.NotFound();
        var note = invoice.IssueCreditNote(body.Amount, body.Reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(new { note.Id, note.Number });
    }
    catch (DomainRuleException ex)
    {
        return Results.Problem(ex.Message, statusCode: 422);                         // (4) 400 or 409 elsewhere
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
    {
        return Results.Conflict();                                                    // (5) in 69 of 80 endpoints
    }
});
```

**What goes wrong.** Each endpoint is plumbing around four lines of real work, and copied plumbing drifts. Validation errors come back as a bare string array here (1), as `ValidationProblem` elsewhere and as a custom `{ "error": ... }` object in a few, so clients need three parsers. The log message (2) is worded differently everywhere, so no single query finds all credit notes. Six endpoints never got the transaction (3), and one of them, `POST /payments/allocate`, writes to three tables. A broken business rule (4) is a 422 here and a 400 or 409 elsewhere, and only 69 endpoints catch the unique-key violation (5), so the other 11 return 500 for a client error.

**Evidence**

*Three validation errors, three shapes*

```text
POST /invoices/9b1e.../credit-notes        {"amount": -5}
400  ["'Amount' must be greater than '0'."]

POST /customers                            {"email": ""}
400  {"title":"One or more validation errors occurred.","status":400,
      "errors":{"Email":["'Email' must not be empty."]}}

POST /invoices/9b1e.../payments            {"amount": 0}
422  {"error":"Amount must be positive"}
```

**After**

*After: a dispatcher in about 40 lines*

```csharp
public interface IRequest<TResponse> { }
public interface ICommand<TResponse> : IRequest<TResponse> { }        // writes: these get a transaction
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}

public sealed class Sender(IServiceProvider services) : ISender
{
    private static readonly ConcurrentDictionary<Type, object> Pipelines = new();   // one per request type

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) =>
        ((Pipeline<TResponse>)Pipelines.GetOrAdd(request.GetType(),
            t => Activator.CreateInstance(typeof(Pipeline<,>).MakeGenericType(t, typeof(TResponse)))!))
        .Run(request, services, ct);

    private abstract class Pipeline<TResponse>
    {
        public abstract Task<TResponse> Run(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct);
    }
    private sealed class Pipeline<TRequest, TResponse> : Pipeline<TResponse> where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Run(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct)
        {
            var typed = (TRequest)request;
            var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(typed, ct);
            // The first behaviour registered is the outermost, so wrap from the last one inwards
            foreach (var behavior in sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var inner = next;
                next = () => behavior.HandleAsync(typed, inner, ct);
            }
            return next();
        }
    }
}
```

*Three behaviours, and their order*

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        try { return await next(); }
        finally { logger.LogInformation("{Request} took {ElapsedMs:F0} ms", typeof(TRequest).Name,
                                        Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
    }
}

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)           // one at a time: async rules may share the DbContext
            failures.AddRange((await validator.ValidateAsync(request, ct)).Errors);
        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();
    }
}

public sealed class TransactionBehavior<TRequest, TResponse>(BillingDbContext db)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICommand<TResponse>) return await next();       // queries run without one
        // With EnableRetryOnFailure, a user transaction must run inside the execution strategy
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var response = await next();
            await tx.CommitAsync(ct);
            return response;
        });
    }
}
```

*A use case, its endpoint, and one place for errors*

```csharp
public sealed record IssueCreditNote(Guid InvoiceId, decimal Amount, string Reason) : ICommand<CreditNoteIssued>;
// In the same file: IssueCreditNoteValidator, and IssueCreditNoteHandler with the four lines of real work.
// It throws NotFoundException or DomainRuleException and knows nothing about HTTP.

invoices.MapPost("/{id:guid}/credit-notes", (Guid id, IssueCreditNoteBody body, ISender sender, CancellationToken ct) =>
    sender.Send(new IssueCreditNote(id, body.Amount, body.Reason), ct));

// One place that turns exceptions into ProblemDetails, for every endpoint
public sealed class ProblemDetailsMapper(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails? details = exception switch
        {
            ValidationException v => new HttpValidationProblemDetails(v.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())) { Status = 400 },
            NotFoundException => new ProblemDetails { Status = 404, Title = "Not found" },
            DomainRuleException rule => new ProblemDetails { Status = 422, Title = "Business rule violated", Detail = rule.Message },
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }
                => new ProblemDetails { Status = 409, Title = "Duplicate" },
            _ => null,                                    // anything else: the default 500
        };
        if (details is null) return false;
        http.Response.StatusCode = details.Status!.Value;
        return await problems.TryWriteAsync(new() { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}

// Program.cs: registration order is pipeline order
builder.Services.AddScoped<ISender, Sender>();
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));       // outermost
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));   // innermost
builder.Services.AddValidatorsFromAssemblyContaining<IssueCreditNoteValidator>();
builder.Services.AddProblemDetails().AddExceptionHandler<ProblemDetailsMapper>();
// ... and after builder.Build(): app.UseExceptionHandler(), or the mapper never runs
```

**Why it works.** Each endpoint is now one line that turns HTTP into a request and sends it. The behaviours wrap every handler in the same order: logging outermost, so its timing includes validation; validation next, so an invalid request never opens a transaction; the transaction innermost, around the handler alone and only for `ICommand` requests. Exceptions become ProblemDetails in one `IExceptionHandler`. The Service Bus consumer that imports invoices from customers' ERPs sends the same requests and gets the same steps, which endpoint filters could not give it.

This is the core of what MediatR does. From version 13, released in July 2025, MediatR is dual-licensed under the Reciprocal Public License 1.5 and a commercial licence, with a free community edition for companies under $5 million in annual revenue; earlier versions keep their Apache 2.0 licence. For most teams a hand-rolled dispatcher of about 40 lines, like the one above, is enough: send, one handler per request, and behaviours.

**Result.** All 80 endpoints now return the same error shapes: 400 with field errors, and 404, 409 and 422 as ProblemDetails. The six write endpoints without a transaction got one by becoming commands, and the 11 duplicate-number 500s became 409s. The endpoint files shrank from 2,900 lines to 640, and the plumbing is about 190 lines including its tests.

> **Costs, and when not to.** The indirection is real. `sender.Send(new IssueCreditNote(...))` does not say which code runs: go to definition lands on `ISender.Send`, not on the handler, and every stack trace gains a frame per behaviour. Keep the request, validator and handler in one file with matching names, and add a startup test that resolves a handler for every request type, because a missing registration fails only at runtime. Behaviours apply to everything, including what you did not mean: in the first week, report queries opened transactions, hence the `ICommand` check.
>
> When the endpoints are the only callers, ASP.NET Core already has the pipeline: endpoint filters on a route group, `IExceptionHandler` with `AddProblemDetails`, and since .NET 10 built-in DataAnnotations validation through `builder.Services.AddValidation()`. A dozen endpoints with one service per area do not need a mediator: inject the service and call it.

> **Lesson:** A mediator with pipeline behaviours earns its indirection when many handlers, including non-HTTP callers, need the same steps in the same order; otherwise use the framework's filters and call a service.
