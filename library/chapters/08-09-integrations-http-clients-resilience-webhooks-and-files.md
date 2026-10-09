---
id: "8-9"
number: "8.9"
title: "Integrations: HTTP clients, resilience, webhooks and files"
part: 8
part_title: "Building a backend"
words: 6564
minutes: 29
---

# 8.9 Integrations: HTTP clients, resilience, webhooks and files

> **Quick look.** Calling other systems reliably: HTTP clients, resilience, webhooks, uploads and imports.
>
> - new HttpClient per call exhausts sockets; one static client caches stale DNS. IHttpClientFactory with typed clients fixes both.
> - Use the standard resilience handler (retry, breaker, timeout); per-attempt timeouts must be well under the total.
> - Stream large responses with ResponseHeadersRead.
> - Sign webhooks with HMAC over the raw body plus a timestamp; verify in constant time; dedupe by event id.
> - Upload large files directly to object storage with presigned URLs; never trust the extension or content type.
> - Import big files by streaming into a staging table, then merging in one statement.
> - Inside your own system use a broker; webhooks are for crossing organizational boundaries. Use claim checks for large payloads.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Socket exhaustion from new HttpClient per call

Under load, outbound calls fail with "Only one usage of each socket address is normally permitted".

*Typed client from the factory*

```csharp
builder.Services.AddHttpClient<PartnerClient>(c =>
{
    c.BaseAddress = new Uri("https://api.partner.example/");
    c.Timeout = Timeout.InfiniteTimeSpan;          // the resilience pipeline owns timeouts
})
.AddStandardResilienceHandler();                    // retries with jitter, breaker, attempt and total timeouts

public sealed class PartnerClient(HttpClient http)
{
    public Task<Quote?> GetQuoteAsync(string route, CancellationToken ct) =>
        http.GetFromJsonAsync<Quote>($"quotes/{Uri.EscapeDataString(route)}", ct);
}
```

> **Lesson:** IHttpClientFactory pools connections and refreshes DNS. Add a resilience handler instead of hand-rolled retries.

#### Example 2: Receiving a payment webhook safely

Anyone can POST to `/webhooks/payments`, and the provider retries deliveries for 24 hours.

*Verify, store, then process*

```csharp
app.MapPost("/webhooks/payments", async (HttpRequest req, WebhookDb db, IConfiguration cfg) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var raw = ms.ToArray();

    var sig = Convert.FromHexString(req.Headers["X-Signature"].ToString());
    var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Webhooks:Secret"]!), raw);
    if (!CryptographicOperations.FixedTimeEquals(sig, expected)) return Results.Unauthorized();

    var eventId = req.Headers["X-Event-Id"].ToString();
    if (await db.Received.AnyAsync(r => r.EventId == eventId)) return Results.Ok();   // duplicate delivery
    db.Received.Add(new ReceivedEvent(eventId, raw));                                    // process later
    await db.SaveChangesAsync();
    return Results.Ok();
});
```

> **Lesson:** Verify the signature over the raw body in constant time, deduplicate by event id, and answer fast.

### HttpClient & Networking

<p class="src">From <em>.NET Backend Deep Dive</em> · HttpClient & Networking</p>

<a id="dn-119"></a>

#### The two classic failures

```csharp
// 1) A new HttpClient per call -> socket exhaustion.
//    Disposing doesn't release the socket immediately; it sits in TIME_WAIT ~4 minutes.
using var c = new HttpClient();     // WRONG under load

// 2) One static HttpClient forever -> never observes DNS changes.
//    Fine until a failover repoints the hostname and you keep hitting the dead IP.
static readonly HttpClient C = new();
```

`IHttpClientFactory` solves both: handlers are pooled and reused (no socket churn) and rotated on a schedule (DNS is re-resolved).

```csharp
builder.Services.AddHttpClient<IPaymentApi, PaymentApi>(c =>
{
    c.BaseAddress = new Uri("https://payment.internal");
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("order-api/1.4");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // DNS refresh interval
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    MaxConnectionsPerServer = 50,
    EnableMultipleHttp2Connections = true
})
.AddStandardResilienceHandler();
```

<a id="dn-120"></a>

#### Typed vs named vs basic clients

- **Typed** (`AddHttpClient<TInterface, TImpl>`) — the default choice; configuration and usage stay together and the dependency is explicit.
- **Named** — when one class needs several differently-configured clients.
- **`CreateClient()`** — avoid; it loses type safety and encourages scattered configuration.

> The *typed client itself* is registered transient. Don't hold state on it, and never make it a singleton — that reintroduces the DNS problem the factory just solved.

<a id="dn-121"></a>

#### Streaming large responses

```csharp
// Buffers the entire body into memory - fine for 2KB, fatal for 2GB
var json = await client.GetStringAsync(url);

// Streams - constant memory
using var res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
res.EnsureSuccessStatusCode();
await using var stream = await res.Content.ReadAsStreamAsync(ct);
await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<Item>(stream, ct))
    await ProcessAsync(item);
```

`HttpCompletionOption.ResponseHeadersRead` is the key: without it, `GetAsync` doesn't return until the whole body has been buffered.

<a id="dn-122"></a>

#### Timeouts and cancellation

`HttpClient.Timeout` covers the whole operation and surfaces as `TaskCanceledException`, which is easy to confuse with a caller cancelling. Distinguish them:

```csharp
catch (TaskCanceledException) when (!ct.IsCancellationRequested)
{
    // our timeout, not the caller's cancellation
}
```

Set the per-request timeout through the resilience pipeline rather than `HttpClient.Timeout` when you also retry, so each attempt gets its own budget (Part 12.1).

---

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a133"></a>

#### HttpClient and IHttpClientFactory

`HttpClient` has two classic failure modes: creating one per request exhausts sockets (they linger in `TIME_WAIT`), and holding one static forever caches DNS until it is wrong. `IHttpClientFactory` fixes both by pooling and rotating the underlying handlers.

``` csharp
builder.Services.AddHttpClient<IBillingApi, BillingApi>(c =>
{
    c.BaseAddress = new Uri(cfg["Billing:BaseUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.Add("User-Agent", "legacy-svc/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer  = 50
})
.AddStandardResilienceHandler();       // Microsoft.Extensions.Http.Resilience
```

Typed clients are the preferred shape: one class per upstream service, the HTTP details hidden inside, the interface injectable and mockable. Add a `DelegatingHandler` to attach auth tokens or propagate correlation IDs on every call without touching each method.

> **\[Trap\]**
>
> **Common trap.** Timeouts must be set explicitly at every layer. The default `HttpClient` timeout is 100 seconds, which under load means requests pile up until the Thread pool — see Terminology"\>thread pool and connection pool are exhausted — the classic cascading failure.

###### The two opposite bugs, and why the factory exists

``` text
  BUG 1: a new HttpClient per request

    using var client = new HttpClient();     // <- inside a request handler

    each one creates a new HttpMessageHandler -> a new connection pool.
    Dispose() does NOT close the sockets immediately: they sit in
    TIME_WAIT for ~60 seconds and the port is unusable.

    at a few hundred requests per second you exhaust the ephemeral port
    range and get:
      SocketException: Only one usage of each socket address is normally
      permitted   /   "cannot assign requested address"

    intermittent, load-dependent, and it looks like the REMOTE service
    is failing.

  BUG 2: one static HttpClient forever

    static readonly HttpClient Client = new();

    fixes the sockets. Introduces a subtler failure: the handler caches
    DNS resolution FOREVER. A failover, a blue/green cutover, or a scaled
    service that changes IP is never noticed. Requests keep going to an
    address that no longer serves.

  IHttpClientFactory solves both: it POOLS handlers (so sockets are reused)
  and ROTATES them on a timer (so DNS is re-resolved).
```

``` csharp
// typed client - the form to reach for
builder.Services.AddHttpClient<IBillingClient, BillingClient>(c =>
{
    c.BaseAddress = new Uri(cfg["Billing:Url"]!);
    c.Timeout = TimeSpan.FromSeconds(10);       // ALWAYS set this. Default is 100s.
    c.DefaultRequestHeaders.Add("User-Agent", "acme-orders/1.0");
})
.AddStandardResilienceHandler(o =>              // Polly, built in (.NET 8+)
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.BackoffType = DelayBackoffType.Exponential;
    o.Retry.UseJitter = true;                   // avoid synchronised retry waves
    o.CircuitBreaker.FailureRatio = 0.5;
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);   // PER ATTEMPT
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);  // ALL attempts
});

// note the two timeouts. Per-attempt x retries must not exceed the total,
// or the total fires mid-retry and the pattern is incoherent.
```

| Trap                                      | Detail                                                                                                         |
|-------------------------------------------|----------------------------------------------------------------------------------------------------------------|
| Handler lifetime vs typed client lifetime | The *handler* is pooled (2 min default). The typed client is transient. Do not hold state on the client        |
| `SocketsHttpHandler` directly             | Valid alternative: one static client with `PooledConnectionLifetime` set. Solves both bugs without the factory |
| Retrying a non-idempotent request         | A retried `POST` can charge twice. Retry only with an idempotency key                                          |
| No `CancellationToken`                    | A cancelled request keeps consuming the remote service                                                         |
| Reading `Content` as a string first       | Large responses allocate the whole body. Use `ReadFromJsonAsync` or a stream                                   |
| `EnsureSuccessStatusCode()`               | Throws away the body — and the body usually contains the error detail                                          |

``` csharp
// if you prefer no factory, this is the equivalent and it is perfectly fine
static readonly HttpClient Client = new(new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),   // <- rotates for DNS
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    MaxConnectionsPerServer = 50,
});
```

### Factories, Lifetimes & Object Creation

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Factories, Lifetimes & Object Creation</p>

<p class="pd">Almost every awkward question in a .NET codebase — ‘how do I get a DbContext in a singleton?’, ‘why is my HttpClient exhausting sockets?’, ‘how do I pick an implementation at runtime?’ — has the same answer: a factory. Here is the whole family.</p>

<a id="be-a86"></a>

#### IHttpClientFactory, in detail

The factory does not pool `HttpClient` objects — those are cheap. It pools the expensive part underneath: the `HttpMessageHandler` chain that owns the connection pool.

``` text
  CreateClient("billing")
      -> new HttpClient(handler, disposeHandler: false)      <- cheap, per call
             |
             v
      LifetimeTrackingHandler  (from a cache keyed by name)
             |
      your DelegatingHandlers  (auth token, correlation id, logging)
             |
      PrimaryHandler = SocketsHttpHandler                    <- owns the sockets

  after HandlerLifetime (default 2 min) the handler entry is retired:
    - new clients get a fresh handler (so DNS is re-resolved)
    - the old one stays alive until its in-flight requests finish, then is disposed
```

``` csharp
builder.Services
    .AddHttpClient<IBillingApi, BillingApi>(c => c.BaseAddress = new(cfg["Billing:Url"]!))
    .SetHandlerLifetime(TimeSpan.FromMinutes(2))
    .AddHttpMessageHandler<AuthTokenHandler>()
    .AddStandardResilienceHandler();

public sealed class AuthTokenHandler(ITokenCache tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.Authorization = new("Bearer", await tokens.GetAsync(ct));
        return await base.SendAsync(req, ct);
    }
}
```

| Style     | Registration                                            | Best for                                       |
|-----------|---------------------------------------------------------|------------------------------------------------|
| Basic     | `AddHttpClient()` + `IHttpClientFactory.CreateClient()` | Ad-hoc calls                                   |
| Named     | `AddHttpClient("billing", ...)`                         | Several configs, resolved by string            |
| Typed     | `AddHttpClient<IBillingApi, BillingApi>()`              | The default choice — testable and discoverable |
| Generated | Refit / Kiota over a typed client                       | Large upstream APIs with an OpenAPI doc        |

> **\[Trap\]**
>
> **Common trap.** A typed client is registered as **transient**. Injecting one into a singleton captures a handler that will never rotate, which defeats the entire point — you are back to stale DNS. Inject `IHttpClientFactory` into the singleton instead.

###### What this looks like when it breaks

The two failure modes, with their exact signatures, because they look nothing alike.

``` csharp
// FAILURE 1: socket exhaustion from new HttpClient per call
public async Task<string> Get(string url)
{
    using var client = new HttpClient();          // looks correct: it is IDisposable!
    return await client.GetStringAsync(url);
}
```

``` text
  symptom:  SocketException "Only one usage of each socket address is normally permitted"
            or: intermittent connection timeouts under load, fine when idle
  cause:    Dispose closes the connection, which enters TIME_WAIT for ~2 x MSL (up to 240 s
            on Windows). At 200 requests/second you accumulate tens of thousands of
            TIME_WAIT sockets and exhaust the ~16k default ephemeral port range.
  confirm:  netstat -ano | find "TIME_WAIT" /c        (Windows)
            ss -tan state time-wait | wc -l           (Linux)
```

``` text
// FAILURE 2: a static HttpClient that never sees DNS changes
private static readonly HttpClient Shared = new();   // the "fix" everyone applied next
```

``` text
  symptom:  after a failover or blue-green swap, this ONE service keeps calling the old
            IP address and gets connection refused, while every other service is fine.
            A restart fixes it, which makes it look like a memory bug.
  cause:    connections are kept alive indefinitely; the DNS TTL is never re-evaluated
            because no new connection is ever opened.
  fix:      PooledConnectionLifetime, or IHttpClientFactory, which does it for you.
```

``` csharp
// the version that has neither problem
builder.Services.AddHttpClient<IBillingApi, BillingApi>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime      = TimeSpan.FromMinutes(2),   // rotate: fixes DNS
        PooledConnectionIdleTimeout   = TimeSpan.FromSeconds(90),
        MaxConnectionsPerServer       = 100,                        // per-host concurrency cap
        EnableMultipleHttp2Connections = true
    });
```

###### Handler lifetime, and the subtlety people miss

`SetHandlerLifetime` and `PooledConnectionLifetime` solve the same problem at different layers, and you generally only need one. If you configure a primary `SocketsHttpHandler` with a `PooledConnectionLifetime`, set the handler lifetime to `Timeout.InfiniteTimeSpan` — otherwise you are rotating handlers *and* connections, which discards warm connections for no benefit.

``` csharp
builder.Services.AddHttpClient<IBillingApi, BillingApi>()
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)      // let the socket handler manage rotation
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
```

| Setting                       | Governs                                           | Typical value                               |
|-------------------------------|---------------------------------------------------|---------------------------------------------|
| `HandlerLifetime`             | How long a handler chain is reused by the factory | 2 min, or infinite if using the below       |
| `PooledConnectionLifetime`    | Max age of a TCP connection before rotation       | 2–5 min                                     |
| `PooledConnectionIdleTimeout` | Close connections unused for this long            | 1–2 min; under the LB idle timeout          |
| `MaxConnectionsPerServer`     | Concurrency ceiling to one host                   | Sized to the upstream's capacity, not yours |
| `HttpClient.Timeout`          | Total per-request deadline                        | Always set it; the 100 s default is a trap  |

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a144"></a>

#### Webhooks: sending and receiving

##### Sending them

1.  Sign every payload with an HMAC over the raw body plus a timestamp, and document how to verify it.
2.  Include the timestamp in the signed content so old payloads cannot be replayed.
3.  Send a stable event ID so the receiver can deduplicate.
4.  Retry with exponential backoff over hours, not minutes, and stop after a bounded number of attempts.
5.  Give consumers a dashboard: recent deliveries, response codes, and a manual replay button.

``` csharp
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var signed = $"{timestamp}.{payloadJson}";
var sig = Convert.ToHexString(
    HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();

req.Headers.Add("X-Signature", $"t={timestamp},v1={sig}");
req.Headers.Add("X-Event-Id", eventId.ToString());
```

##### Receiving them

``` csharp
app.MapPost("/webhooks/stripe", async (HttpRequest req, IProcessor p, CancellationToken ct) =>
{
    req.EnableBuffering();
    using var reader = new StreamReader(req.Body, leaveOpen: true);
    var raw = await reader.ReadToEndAsync(ct);     // verify the RAW body, never the re-serialized model
    req.Body.Position = 0;

    if (!Verify(raw, req.Headers["X-Signature"]!))
        return Results.Unauthorized();

    await p.EnqueueAsync(raw, ct);                 // queue it, do not process inline
    return Results.Accepted();                     // respond fast; providers time out at a few seconds
});
```

> **\[Trap\]**
>
> **Common trap.** Processing a webhook synchronously is how you lose events: your handler takes four seconds, the provider times out at three, retries, and now you have processed it twice and reported a failure. Verify, enqueue, return 202, and make the consumer idempotent on the event ID.

###### Both sides done properly

``` text
  RECEIVING a webhook - the four things, in order

  1  VERIFY THE SIGNATURE, before parsing.
       the payload is untrusted input from the public internet.
       verify against the RAW BYTES - not the deserialised object,
       because serialisation is not byte-stable.

  2  CHECK THE TIMESTAMP.
       reject anything older than ~5 minutes, or a captured request
       can be replayed indefinitely.

  3  RESPOND FAST - 2xx immediately.
       most senders time out in 5-10 seconds and then RETRY.
       do the work asynchronously: enqueue, then return 200.
       processing inline is how one slow handler causes a flood
       of duplicate deliveries.

  4  BE IDEMPOTENT.
       you WILL receive duplicates. Deduplicate on the sender's
       event id, in the same transaction as the work.
```

``` csharp
[HttpPost("/webhooks/stripe")]
public async Task<IResult> Receive(CancellationToken ct)
{
    // raw bytes, before any model binding
    using var reader = new StreamReader(Request.Body);
    var payload = await reader.ReadToEndAsync(ct);

    var signature = Request.Headers["Stripe-Signature"].ToString();
    if (!Verify(payload, signature, _secret)) return Results.Unauthorized();

    var evt = JsonSerializer.Deserialize<WebhookEvent>(payload)!;

    // deduplicate + enqueue, in ONE transaction. Then return.
    var inserted = await db.Database.ExecuteSqlAsync(
        $"INSERT INTO webhook_events (id, payload) VALUES ({evt.Id}, {payload}::jsonb) ON CONFLICT DO NOTHING", ct);

    if (inserted > 0) await queue.EnqueueAsync(new ProcessWebhook(evt.Id), ct);
    return Results.Ok();                    // fast, always
}

// constant-time comparison - a naive == leaks the signature via timing
static bool Verify(string payload, string sig, byte[] secret)
{
    using var h = new HMACSHA256(secret);
    var expected = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
    return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(sig));
}
```

| Sending a webhook               | Requirement                                                           |
|---------------------------------|-----------------------------------------------------------------------|
| Signature                       | HMAC over the raw body, with a per-subscriber secret, in a header     |
| Timestamp in the signed payload | So receivers can reject replays                                       |
| Retries                         | Exponential backoff with jitter, over hours — then give up and notify |
| Timeout                         | A few seconds. A slow receiver must not hold your sender              |
| Event id                        | Stable across retries, so receivers can deduplicate                   |
| Delivery log                    | Every attempt, its status, and a manual replay button                 |
| **SSRF protection**             | **Validate the destination URL** — see below                          |
| Ordering                        | Do not promise it. Include a sequence number and let receivers cope   |

> **\[Trap\]**
>
> **Common trap.** Outbound webhooks are a server-side request forgery vector: a customer registers `http://169.254.169.254/latest/meta-data/` or an internal address, and your server obligingly fetches it and reports the response back to them. Validate the destination — require HTTPS, resolve the hostname and reject private, loopback and link-local ranges, re-check after redirects, and ideally send from an egress-restricted worker that cannot reach anything internal.

<a id="be-a145"></a>

#### Large payloads, uploads and streaming

The default request pipeline buffers. That is fine for a 4 KB JSON body and ruinous for a 2 GB file — which is exactly how a service that worked all year gets OOM-killed the day someone uploads a video.

``` csharp
// stream a large upload straight to storage, never through memory
[HttpPost("documents")]
[DisableRequestSizeLimit]
[RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
public async Task<IResult> Upload(CancellationToken ct)
{
    var boundary = MultipartRequestHelper.GetBoundary(Request.ContentType!);
    var reader = new MultipartReader(boundary, Request.Body);

    while (await reader.ReadNextSectionAsync(ct) is { } section)
    {
        var file = section.AsFileSection();
        if (file is null) continue;
        await _blobs.UploadAsync(file.FileName, file.FileStream!, ct);   // pipe, no buffer
    }
    return Results.Accepted();
}

// stream a large response as it is produced
public async IAsyncEnumerable<Row> Export([EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var r in _db.Rows.AsAsyncEnumerable().WithCancellation(ct))
        yield return r;
}
```

Better still, avoid proxying bytes through your API at all: issue a pre-signed URL (SAS token, S3 presigned PUT) and let the client talk to blob storage directly. Your service handles authorization and metadata; the storage service handles the bytes.

| Payload problem     | Fix                                                                                   |
|---------------------|---------------------------------------------------------------------------------------|
| Large upload        | Stream to storage, or pre-signed direct upload                                        |
| Large export        | `IAsyncEnumerable` with streaming JSON, or a background job producing a download link |
| Slow client         | `MinResponseDataRate`; do not hold a DB connection while writing                      |
| Huge JSON in memory | `DeserializeAsync` over the stream; never `ReadAsStringAsync`                         |

###### Not buffering, and getting uploads off your server entirely

``` csharp
// the default binds the WHOLE file into memory or a temp file first
[HttpPost("upload")]
public async Task<IResult> Upload(IFormFile file)     // buffered
{ ... }
// a 2 GB upload from 10 concurrent users = 20 GB of buffering.

// streaming: read the multipart sections as they arrive
[HttpPost("upload")]
[DisableFormValueModelBinding]
[RequestSizeLimit(2L * 1024 * 1024 * 1024)]
public async Task<IResult> Upload(CancellationToken ct)
{
    var boundary = MediaTypeHeaderValue.Parse(Request.ContentType)
        .Boundary.Value!.Trim('"');
    var reader = new MultipartReader(boundary, Request.Body);

    while (await reader.ReadNextSectionAsync(ct) is { } section)
    {
        var cd = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
        if (!cd.IsFileDisposition()) continue;

        // straight from the request stream to storage. Never fully in memory.
        await blob.UploadAsync(section.Body, ct);
    }
    return Results.Ok();
}
```

``` text
  BETTER: DO NOT UPLOAD THROUGH YOUR SERVER AT ALL

  browser                    your API                object storage
     |  1. POST /uploads         |                         |
     |-------------------------->|                         |
     |  2. { presigned PUT url } |                         |
     |<--------------------------|                         |
     |  3. PUT the file directly, using that URL           |
     |---------------------------------------------------->|
     |  4. POST /uploads/{id}/complete                     |
     |-------------------------->|  5. verify, record      |

  your server never touches the bytes.
    - no bandwidth cost, no memory, no request-timeout problem
    - the upload survives your deployment restarting
    - resumable and multipart come free from the storage service
    - your API stays small and fast

  the same pattern in reverse for downloads: return a presigned GET URL
  rather than proxying a 2 GB file through your application.
```

``` csharp
// generating a presigned PUT
var sas = blobClient.GenerateSasUri(new BlobSasBuilder
{
    BlobContainerName = "uploads",
    BlobName = $"{tenantId}/{Guid.CreateVersion7()}",
    Resource = "b",
    ExpiresOn = timeProvider.GetUtcNow().AddMinutes(15),   // SHORT
}.SetPermissions(BlobSasPermissions.Write));               // WRITE ONLY

// and on completion, VERIFY before trusting it:
//   size within limits, content type as declared, virus scan if required,
//   and the caller actually owns that upload id.
// a presigned URL is a capability - scope it narrowly and expire it fast.
```

| Streaming a response   | How                                                                     |
|------------------------|-------------------------------------------------------------------------|
| Large JSON collection  | `IAsyncEnumerable<T>` from the action — ASP.NET Core streams it         |
| A file from storage    | A presigned URL, or `Results.Stream` without buffering                  |
| Server-sent events     | `text/event-stream`; disable response buffering                         |
| A generated report     | Write to the response stream as you build it, not into a `MemoryStream` |
| Anything through nginx | Set `proxy_buffering off` or the proxy re-buffers it for you            |

<a id="be-a149"></a>

#### The claim check pattern

Message brokers are optimised for small messages. RabbitMQ slows sharply past a few hundred kilobytes, Azure Service Bus caps at 256 KB on standard tiers, Kafka's default limit is 1 MB, and a broker holding gigabytes of payload is a broker that cannot be replicated or replayed cheaply. The claim check pattern stores the payload elsewhere and sends a reference.

``` text
  producer                                          consumer

  1. upload payload to blob storage
     -> documents/2026/07/9f2a.pdf  (14 MB)
  2. publish a SMALL message
     { blobUri, sizeBytes, sha256, contentType, expiresAt }   (400 bytes)
                                     ------------->
                                                    3. read the message
                                                    4. fetch the blob
                                                    5. verify the hash
                                                    6. process
  7. lifecycle policy deletes the blob after N days
```

``` csharp
public sealed class ClaimCheckPublisher(BlobContainerClient blobs, IBus bus, TimeProvider time)
{
    private const int InlineThreshold = 64 * 1024;      // small payloads skip the round trip

    public async Task PublishAsync(DocumentGenerated evt, Stream payload, CancellationToken ct)
    {
        if (payload.Length <= InlineThreshold)
        {
            await bus.PublishAsync(evt with { InlineContent = await ReadAllAsync(payload, ct) }, ct);
            return;
        }

        var name = $"{time.GetUtcNow():yyyy/MM}/{evt.DocumentId}.bin";
        var blob = blobs.GetBlobClient(name);

        var hash = await ComputeSha256Async(payload, ct);
        payload.Position = 0;
        await blob.UploadAsync(payload, new BlobUploadOptions
        {
            HttpHeaders = new() { ContentType = evt.ContentType },
            Metadata = { ["documentId"] = evt.DocumentId.ToString() }
        }, ct);

        await bus.PublishAsync(evt with
        {
            ClaimCheck = new ClaimCheck(blob.Uri, payload.Length, hash, ContentType: evt.ContentType)
        }, ct);
    }
}
```

| Concern       | Handling                                                                                                       |
|---------------|----------------------------------------------------------------------------------------------------------------|
| Authorization | The consumer must be authorized to read the blob — workload identity, not a URL that is itself the credential  |
| Integrity     | Send a hash and verify it. A blob overwritten between publish and consume is otherwise undetectable            |
| Lifecycle     | A storage lifecycle rule, with a retention longer than your maximum message retry window                       |
| Orphans       | Publish fails after upload → an orphaned blob. A sweeper job, or upload inside the outbox transaction's shadow |
| Replay        | Replaying a six-month-old event needs the blob to still exist — align retention with replay policy             |
| Ordering      | Unchanged; the reference is just a small message                                                               |

> **\[Trap\]**
>
> **Common trap.** Do not send a pre-signed URL as the claim check unless the token's lifetime exceeds your maximum dead-letter-and-replay window. A message that sits in a dead-letter queue for two days and is then replayed will find an expired token, and the failure looks like a permissions bug rather than an expiry. Send the blob path and let the consumer authenticate with its own identity.

###### When the message is too big for the message bus

``` text
  the problem

    brokers have message size limits, and they are small:
      RabbitMQ           practical limit ~128 KB (technically larger, don't)
      Azure Service Bus  256 KB standard / 100 MB premium
      Kafka              1 MB default (message.max.bytes)
      SQS                256 KB

    and even under the limit, large messages are bad citizens:
      they consume broker memory and disk
      they slow every consumer on the same queue
      they make retries and DLQ inspection painful

  the pattern

    producer  ->  write the payload to BLOB STORAGE
              ->  publish a small message containing only the KEY

    { "eventId": "...", "type": "DocumentUploaded",
      "payloadUri": "s3://bucket/2026/08/abc123.json",
      "payloadHash": "sha256:...", "sizeBytes": 8412233 }

    consumer  ->  read the message
              ->  fetch the payload by key
              ->  process
```

``` csharp
public async Task PublishAsync(LargeEvent evt, CancellationToken ct)
{
    var key = $"events/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy/MM/dd}/{evt.Id}.json";

    await using var ms = new MemoryStream();
    await JsonSerializer.SerializeAsync(ms, evt.Payload, cancellationToken: ct);
    ms.Position = 0;
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(ms, ct));
    ms.Position = 0;

    await blobs.UploadAsync(key, ms, ct);        // payload first, ALWAYS

    await bus.PublishAsync(new ClaimCheck(evt.Id, key, hash, ms.Length), ct);
    // then the message. This order matters: a consumer must never receive
    // a claim check pointing at a blob that does not exist yet.
}
```

| Operational concern | Handling                                                                                                      |
|---------------------|---------------------------------------------------------------------------------------------------------------|
| Orphaned blobs      | The message failed to publish after the upload. A lifecycle rule deleting unreferenced blobs after N days     |
| Retention mismatch  | The blob expires before the message is consumed. Blob retention must **exceed** queue retention plus DLQ time |
| Access control      | The consumer needs read access to the bucket — a permission the message alone does not grant                  |
| Integrity           | Include a hash; verify on read. A truncated upload is otherwise silent                                        |
| Replay              | Replaying an old message needs the blob still present — this is what breaks Kafka replay                      |
| Cost                | Two operations and storage per event, instead of one message                                                  |

> **\[Note\]**
>
> **Consider whether the event should be smaller instead.** A claim check is the right answer for genuinely large payloads — a document, an image, a bulk export. It is the wrong answer for an event that grew large because it carries an entire aggregate. If your `OrderPlaced` event is 800 KB, the fix is to publish the order *id* and let consumers fetch what they need. Events should carry what happened, not a snapshot of everything.

<a id="be-a142"></a>

#### Legacy integration: SOAP, WCF, files and EDI

Most real integration work is not with a shiny REST API. It is with a partner's SOAP endpoint, an SFTP drop folder, or a fixed-width file from a mainframe. .NET is unusually good at this, and it is a marketable skill.

| Legacy thing              | Modern .NET answer                                         |
|---------------------------|------------------------------------------------------------|
| WCF service host          | CoreWCF — server-side WCF on modern .NET                   |
| WCF client / `svcutil`    | `dotnet-svcutil` generates a client from WSDL              |
| ASMX web service          | Consume via generated client; do not build new ones        |
| `System.Web` dependencies | No modern equivalent — the main porting blocker            |
| MSMQ                      | RabbitMQ, Azure Service Bus, or Kafka                      |
| Fixed-width / CSV drops   | `CsvHelper`, `Span`-based parsers, an SFTP client library  |
| EDIFACT / X12             | A commercial library or a broker product; do not hand-roll |

##### The anti-corruption layer

Do not let a partner's schema leak into your domain. Put a translation layer at the boundary that turns their `CUST_REC_TYP_3` into your `Customer`. When they change their format in 2029, exactly one class changes.

``` csharp
// SOAP client on modern .NET: dotnet-svcutil generates this from WSDL
var binding = new BasicHttpBinding(BasicHttpSecurityMode.Transport);
var endpoint = new EndpointAddress("https://partner.example/svc.asmx");
var client = new PartnerServiceClient(binding, endpoint);
client.ClientCredentials.UserName.UserName = config["Partner:User"];

var response = await client.SubmitBookingAsync(request);   // async by default

// Hosting SOAP yourself, when a partner cannot change: CoreWCF
builder.Services.AddServiceModelServices();
app.UseServiceModel(b => b.AddService<BookingService>()
    .AddServiceEndpoint<BookingService, IBookingService>(
        new BasicHttpBinding(), "/BookingService.svc"));
```

``` csharp
// Fixed-width mainframe file: slice, never Substring in a loop
public static Record ParseLine(ReadOnlySpan<char> line) => new(
    Id:     int.Parse(line[0..8]),
    Name:   line[8..38].TrimEnd().ToString(),
    // implied 2 decimals: "0000019995" means 199.95
    Amount: decimal.Parse(line[38..48]) / 100m,
    Date:   DateOnly.ParseExact(line[48..56], "yyyyMMdd"));

// The rules that make file integration survive contact with reality:
// 1. read from a landing folder, move to /processing, then /done or /failed
// 2. treat the filename as the idempotency key — partners resend
// 3. never delete the original; you will be asked what arrived, and when
// 4. encoding is not UTF-8 (often Shift-JIS or an EBCDIC-derived codepage) —
//    Encoding.GetEncoding(932) after RegisterProvider, and assert it
```

> **\[Why it matters\]** This work is unglamorous and consistently in demand. Teams that can connect a modern .NET service to a partner’s SOAP endpoint, a nightly SFTP drop and an EDI feed — without rewriting either side — are the ones enterprises pay for, because the alternative is asking a bank or a logistics provider to change their system, and they will not.

### Recipes: code you will actually write

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Recipes: code you will actually write</p>

<a id="be-m287"></a>

#### Recipe: a resilient outbound API client

``` csharp
builder.Services.AddHttpClient<IStockApi, StockApi>(c => {
        c.BaseAddress = new Uri(cfg["Stock:BaseUrl"]!);
        c.Timeout = TimeSpan.FromSeconds(10);              // total, including retries
        c.DefaultRequestHeaders.Add("User-Agent", "the legacy system-orders/1.0");
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(2))           // re-resolve DNS (§16)
    .AddStandardResilienceHandler(o => {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);   // per attempt
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.UseJitter = true;                             // essential (§263)
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });
```

``` csharp
public sealed class StockApi(HttpClient http, ILogger<StockApi> log) : IStockApi {

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Cached instance - constructing it per call rebuilds the metadata cache
    // (§208).

    public async Task<IReadOnlyDictionary<string, int>> GetStockAsync(
            IReadOnlyCollection<string> skus, CancellationToken ct) {

        if (skus.Count == 0) return ReadOnlyDictionary<string, int>.Empty;

        // ONE call for all SKUs. Never a call per item (§308).
        using var res = await http.PostAsJsonAsync("stock/query",
            new { skus }, Json, ct);

        if (res.StatusCode is HttpStatusCode.TooManyRequests) {
            var retryAfter = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            log.LogWarning("Stock API throttled, retry after {Delay}", retryAfter);
            throw new TransientException("Throttled", retryAfter);
        }

        res.EnsureSuccessStatusCode();

        // Deserialize straight from the stream - no intermediate string
        // (§83).
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<StockResponse>(stream, Json, ct)
                      ?? throw new InvalidDataException("Empty stock response");

        return payload.Items.ToDictionary(i => i.Sku, i => i.Available, StringComparer.Ordinal);
    }
}
```

``` csharp
// The decision you must make explicitly: what happens when it is down?
public async Task<IReadOnlyDictionary<string, int>> GetStockOrEmptyAsync(
        IReadOnlyCollection<string> skus, CancellationToken ct) {
    try { return await GetStockAsync(skus, ct); }
    catch (BrokenCircuitException) {
        log.LogWarning("Stock circuit open - rendering without availability");
        return ReadOnlyDictionary<string, int>.Empty;      // degrade, do not fail
    }
}
// For a product page, showing no availability badge is better than a 500.
// For checkout, failing is correct. Decide per call site (§248).
```

> **\[Trap\]**
>
> **The per-attempt timeout must be well under the total.** With a 10-second client timeout and a 10-second attempt timeout, the retry policy never gets to run — the whole request is cancelled during the first attempt. Attempt timeout × (retries + 1) plus backoff should fit inside the total, and the total should fit inside your caller's budget ([Cancellation and timeouts](05-03-async-and-await.md#be-m137)).

<a id="be-m290"></a>

#### Recipe: a validated, deduplicated file upload

``` csharp
app.MapPost("/orders/{id:long}/attachments",
    async (long id, IFormFile file, ITenant tenant, IBlobStore blobs,
           AppDb db, CancellationToken ct) => {

    // 1. Cheap checks first, before reading a byte.
    if (file.Length == 0) return Results.BadRequest("Empty file");
    if (file.Length > 20 * 1024 * 1024) return Results.BadRequest("Max 20 MB");
    if (!Allowed.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
        return Results.BadRequest("Unsupported file type");

    // 2. Hash while streaming to a temp file - one pass, constant memory.
    //    The hash is the content id, so identical files are stored once
    //    (§76).
    var temp = Path.GetTempFileName();
    string contentId;
    try {
        await using (var source = file.OpenReadStream())
        await using (var dest = new FileStream(temp, FileMode.Create, FileAccess.Write,
                                               FileShare.None, 64 * 1024,
                                               FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var sha = SHA256.Create())
        await using (var hashing = new CryptoStream(dest, sha, CryptoStreamMode.Write)) {
            await source.CopyToAsync(hashing, ct);
            await hashing.FlushFinalBlockAsync(ct);
            contentId = Convert.ToHexStringLower(sha.Hash!);
        }

        // 3. Verify the CONTENT, not the extension. A .pdf that is not a PDF
        //    is the oldest upload attack there is.
        await using (var check = File.OpenRead(temp)) {
            Memory<byte> magic = new byte[8];
            var read = await check.ReadAsync(magic, ct);
            if (!IsAllowedMagic(magic[..read].Span))
                return Results.BadRequest("File content does not match its extension");
        }

        // 4. Store only if new. Two users uploading the same PDF store one copy.
        if (!await blobs.ExistsAsync(contentId, ct)) {
            await using var upload = File.OpenRead(temp);
            await blobs.PutAsync(contentId, upload, ct);
        }
    }
    finally {
        File.Delete(temp);                            // ALWAYS, including on failure
    }

    // 5. Record the reference. Note the sanitised display name - never trust
    //    a client-supplied path (§227).
    var attachment = new Attachment {
        TenantId = tenant.Id, OrderId = id, BlobId = contentId,
        FileName = Path.GetFileName(file.FileName),   // strips any directory component
        SizeBytes = file.Length,
        ContentType = file.ContentType
    };
    db.Attachments.Add(attachment);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/attachments/{attachment.Id}", new { attachment.Id, contentId });
})
.DisableAntiforgery()
.RequireAuthorization("orders:write");
```

> **\[Trap\]**
>
> **Four upload traps, all common.** Reading into a `byte[]` first (a 20 MB array is on the LOH, and ten concurrent uploads is 200 MB — [The GC in one page](02-07-memory-management-and-garbage-collection.md#be-b239)). Trusting the extension or the `Content-Type` header, both client-supplied. Using the client's filename as a path, which is directory traversal. And forgetting the `finally`, which leaks temp files until the disk fills.

> **\[Tip\]**
>
> **Content addressing changes deletion.** Because several rows may reference one blob, deleting an attachment must not delete the blob — reference-count it or sweep unreferenced blobs on a schedule, conservatively enough that an in-progress upload is never collected.

<a id="be-m284"></a>

#### Recipe: import a large CSV into SQL Server

Stream the file, validate per row, bulk-copy into a staging table, then merge in one statement. Constant memory regardless of file size.

``` csharp
public sealed class OrderImporter(
        IDbContextFactory<AppDb> factory, string connectionString, ILogger<OrderImporter> log) {

    public async Task<ImportResult> ImportAsync(Stream csv, int tenantId, CancellationToken ct) {
        var staging = $"#Import_{Guid.NewGuid():N}";
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        // 1. Staging table, session-scoped, no indexes (they'd slow the load).
        await using (var cmd = conn.CreateCommand()) {
            cmd.CommandText = $"""
                CREATE TABLE {staging} (
                    ExternalRef varchar(50)   NOT NULL,
                    Sku         varchar(50)   NOT NULL,
                    Qty         int           NOT NULL,
                    UnitPrice   decimal(18,4) NOT NULL,
                    CreatedAt   datetime2     NOT NULL)
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Stream rows into it. SqlBulkCopy pulls from an IDataReader, so
        //    nothing is ever fully in memory (§299).
        var errors = new List<string>();
        var reader = new CsvDataReader(ParseAsync(csv, errors, ct));

        using (var bulk = new SqlBulkCopy(conn) {
                   DestinationTableName = staging,
                   BatchSize = 5_000,
                   BulkCopyTimeout = 0,
                   EnableStreaming = true
               }) {
            foreach (var col in CsvDataReader.Columns)          // explicit mapping:
                bulk.ColumnMappings.Add(col, col);              // never rely on ordinal
            await bulk.WriteToServerAsync(reader, ct);
        }

        // 3. One set-based merge. All the work happens server-side
        //    (§191, §193).
        await using var merge = conn.CreateCommand();
        merge.CommandText = $"""
            MERGE INTO Orders WITH (HOLDLOCK) AS t
            USING (SELECT ExternalRef, SUM(Qty * UnitPrice) AS Total, MIN(CreatedAt) AS CreatedAt
                   FROM {staging} GROUP BY ExternalRef) AS s
              ON t.ExternalRef = s.ExternalRef AND t.TenantId = @tenantId
            WHEN MATCHED AND t.Total <> s.Total THEN
                UPDATE SET t.Total = s.Total
            WHEN NOT MATCHED THEN
                INSERT (TenantId, ExternalRef, Total, CreatedAt, Status)
                VALUES (@tenantId, s.ExternalRef, s.Total, s.CreatedAt, 1);
            """;
        merge.Parameters.AddWithValue("@tenantId", tenantId);
        var affected = await merge.ExecuteNonQueryAsync(ct);

        log.LogInformation("Imported {Rows} rows, {Errors} rejected", affected, errors.Count);
        return new ImportResult(affected, errors);
    }

    private static async IAsyncEnumerable<ImportRow> ParseAsync(
            Stream csv, List<string> errors,
            [EnumeratorCancellation] CancellationToken ct = default) {
        using var reader = new StreamReader(csv, new UTF8Encoding(false), bufferSize: 64 * 1024);
        using var parser = new CsvReader(reader,
            new CsvConfiguration(CultureInfo.InvariantCulture) { DetectDelimiter = true });

        await foreach (var raw in parser.GetRecordsAsync<RawRow>(ct)) {
            if (!Validate(raw, out var row, out var error)) {
                if (errors.Count < 1000) errors.Add($"Line {parser.Parser.Row}: {error}");
                continue;                                   // reject the row, not the file
            }
            yield return row;
        }
    }
}
```

> **\[Tip\]**
>
> **Why staging plus merge rather than EF.** `SaveChanges` batches statements but still round-trips per batch and tracks every entity ([Case: the nightly import that took four hours](14-03-worked-cases-from-production.md#be-m300)). `SqlBulkCopy` writes with minimal logging and no tracking; the merge then does the insert-or-update decision in one pass with the database's own join algorithms ([Join algorithms: loops, hash, and merge](07-03-indexes-and-query-plans.md#be-m199)). On 200,000 rows this is minutes versus seconds.

> **\[Trap\]**
>
> **Collect errors, do not throw on the first bad row.** A 200,000-row import that fails at row 3 because of one bad date is unusable operationally. Reject rows, cap the error list so a fully-malformed file cannot exhaust memory, and return a report the operator can act on.

### Events and Webhooks

<p class="src">From <em>Backend Engineering</em> · APIs · Events and Webhooks</p>

<p class="pd">Request/response covers &quot;I need something now.&quot; Events cover &quot;tell me when something happens.&quot; The delivery guarantees are where this gets interesting.</p>

<a id="be-e532"></a>

#### Why not polling

`GET /orders?since=...` every 30 seconds: latency up to 30 seconds, and 99% of calls return nothing. At a thousand clients that's a permanent load floor for almost no information.

Webhooks invert it — the server calls you. Cost: you need a publicly reachable HTTPS endpoint, and you inherit all of Fact 2 from the provider's side.

<a id="be-e533"></a>

#### Delivery semantics

**At-most-once** — send, don't retry. Simple, loses events. Almost never acceptable.

**At-least-once** — retry until acknowledged. **This is what essentially every webhook system provides**, and it means duplicates are guaranteed, not hypothetical.

**Exactly-once** — not achievable over an unreliable network (Fact 2 again). What people call exactly-once is at-least-once delivery plus idempotent processing. That combination is achievable and is what you should build.

So: **every webhook consumer must be idempotent.** Providers send an event ID; store processed IDs and skip repeats.

``` csharp
if (await _seen.ExistsAsync(evt.Id)) return Ok();   // already handled
await ProcessAsync(evt);
await _seen.AddAsync(evt.Id, ttl: TimeSpan.FromDays(7));
```

**Ordering is not guaranteed.** Retries and parallel delivery mean `order.updated` can arrive before `order.created`. Include a sequence number or timestamp and handle out-of-order arrival — usually by ignoring an event older than what you've already applied.

<a id="be-e534"></a>

#### Verifying webhooks

Your endpoint is public, so anyone can post to it. Signature verification is mandatory:

``` yaml
X-Signature: t=1755432000,v1=5257a869e7ecebeda32affa62cdca3fa...
```

The provider HMACs `timestamp + "." + body` with a shared secret. You recompute and compare.

Four things people get wrong:

1.  **Sign the raw body**, before any deserialization. Re-serializing changes bytes and breaks the signature.
2.  **Constant-time comparison.** `==` on strings leaks timing information. Use `CryptographicOperations.FixedTimeEquals`.
3.  **Check the timestamp.** Without it, a captured request replays forever. Reject anything older than ~5 minutes.
4.  **Return 2xx fast.** Acknowledge, then process asynchronously. Most providers time out at 5–10 seconds and will retry — so slow processing creates duplicates.

<a id="be-e535"></a>

#### Providing webhooks

- **Retry with backoff**, and cap it. Typical: ~8 attempts over 24 hours.
- **Dead-letter** what never succeeds, and expose it so consumers can replay.
- **Auto-disable** endpoints failing for days, with notification — otherwise you DDoS a dead URL forever.
- **Include the event ID, type, timestamp, and version** in every payload.
- **Send a reference, not just a snapshot**, or make the payload self-contained and versioned. Consumers processing out of order need a way to fetch current state.
- **SSRF risk:** you're making HTTP calls to URLs users supply. Block private IP ranges, `localhost`, link-local (`169.254.169.254` — the cloud metadata endpoint), and validate after DNS resolution, not before. This is a serious and frequently exploited hole.

<a id="be-e536"></a>

#### When to use a broker instead

Webhooks are HTTP calls with retries. A message broker (RabbitMQ, Kafka, Azure Service Bus, SQS) gives you durable queues, consumer groups, replay, and backpressure. Inside your own system, use the broker. Webhooks are for crossing an organizational boundary where you can't ask the other side to connect to your queue.

------------------------------------------------------------------------
