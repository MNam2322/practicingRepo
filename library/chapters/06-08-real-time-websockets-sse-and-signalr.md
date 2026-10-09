---
id: "6-8"
number: "6.8"
title: "Real-time: WebSockets, SSE and SignalR"
part: 6
part_title: "Networks"
words: 2096
minutes: 9
---

# 6.8 Real-time: WebSockets, SSE and SignalR

> **Quick look.** Pushing data to clients with WebSockets, Server-Sent Events and SignalR.
>
> - Three options in rising capability and cost: polling, SSE, WebSockets (SignalR picks for you).
> - A WebSocket is a long-lived connection pinned to one server, which breaks the stateless model.
> - With more than one server you need a backplane (such as Redis), or messages reach only some clients.
> - Connections are a capacity resource; reconnection is normal, so resynchronize state after reconnect.
> - Realtime is an optimisation, never the source of truth: clients must be able to recover by querying.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Notifications that reached only some users

Gate-change notifications via SignalR work in testing but reach about a third of users in production with three pods.

*Add a backplane*

```csharp
builder.Services.AddSignalR().AddStackExchangeRedis(redisConnection, o =>
    o.Configuration.ChannelPrefix = RedisChannel.Literal("gates"));
// now Clients.Group("flight-VN123").SendAsync(...) reaches users connected to any pod
```

> **Lesson:** A WebSocket is pinned to one server. With several servers, broadcasts need a backplane.

#### Example 2: Missed updates after reconnecting

A phone loses signal in the terminal for 40 seconds and misses a gate change sent in that window.

*Refetch state on reconnect*

```typescript
const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/flights")
  .withAutomaticReconnect()
  .build();

conn.on("gateChanged", (g) => render(g));
conn.onreconnected(async () => {
  render(await fetch(`/api/flights/${flightId}`).then(r => r.json()));   // pushed events are an optimisation
});
await conn.start();
```

> **Lesson:** Realtime pushes can be missed. The client must be able to recover the current state by querying.

### Real-time: SignalR & WebSockets

<p class="src">From <em>.NET Backend Deep Dive</em> · Real-time: SignalR & WebSockets</p>

<a id="dn-152"></a>

#### Why scaling it is different

A WebSocket is a **long-lived, stateful connection pinned to one server**. That breaks the stateless assumption everything else in this document relies on.

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisCs);   // backplane: without this, multi-instance is broken
```

Without a backplane, `Clients.All.SendAsync(...)` reaches only the clients connected to *that* pod. The bug looks like "notifications work sometimes."

```csharp
public class OrderHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Group by user so you can target them from any instance
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Context.UserIdentifier}");
        await base.OnConnectedAsync();
    }
}

// Push from anywhere - a controller, a consumer, a background job
await _hub.Clients.Group($"user:{userId}").SendAsync("OrderUpdated", dto, ct);
```

<a id="dn-153"></a>

#### The operational realities

- **Connections are a resource.** Each one holds memory and a socket for its whole lifetime. Ten thousand idle connections is a capacity number you must plan for, not an afterthought.
- **Reconnection is normal, not exceptional** — deploys, scaling, mobile networks and load-balancer idle timeouts all drop connections. Use `WithAutomaticReconnect()` and, critically, **resynchronize state after reconnect**: messages sent while disconnected are gone. The client should fetch current state on reconnect rather than assuming the stream is complete.
- **Sticky sessions** are required when negotiation is enabled, or the initial handshake and the socket land on different pods.
- **Scale-out limits**: the Redis backplane broadcasts every message to every server. At very high fan-out that becomes the bottleneck — Azure SignalR Service or a dedicated pub-sub tier exists for this reason.
- **Authorize per hub method**, not just at connection time; connections outlive token lifetimes.
- **Backpressure**: a slow client can't be allowed to buffer unboundedly on the server. SignalR bounds this; raw WebSocket code must do it explicitly.

**When not to use it:** if the client can tolerate a few seconds of delay, polling or SSE is dramatically simpler to operate. Real-time is a genuine operational cost — take it only when the product needs it.

---

### Networking

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Networking</p>

<p class="pd">The wire. Most production incidents that look like application bugs are really DNS, TLS, timeouts, connection pools or a proxy in the middle — so this is the layer worth genuinely understanding.</p>

<a id="be-a163"></a>

#### WebSockets, SSE and SignalR

Three ways to push data to a client, in increasing order of capability and cost.

| Technique          | Direction                           | Good for                                     |
|--------------------|-------------------------------------|----------------------------------------------|
| Polling            | Client asks repeatedly              | Simple, works everywhere, wasteful           |
| Server-sent events | Server → client, over plain HTTP    | Notifications, live feeds, log tails         |
| WebSockets         | Full duplex, persistent TCP         | Chat, collaboration, trading, games          |
| SignalR            | Abstraction with automatic fallback | The .NET default; hubs, groups, reconnection |

``` csharp
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{TenantId}");
}

// push from anywhere in the app
await _hub.Clients.Group($"tenant-{id}").SendAsync("OrderUpdated", dto, ct);
```

Scaling out breaks the naive model: a client connected to pod A cannot be reached from pod B. Add a backplane — Redis, or Azure SignalR Service — so a message published on any pod reaches every connection. Also budget memory: each connection is a live socket plus buffers, so tens of thousands per pod is the realistic ceiling.

###### Choosing a transport, and scaling out

|                         | SSE                       | WebSocket         | SignalR                       | Long polling     |
|-------------------------|---------------------------|-------------------|-------------------------------|------------------|
| Direction               | **Server → client only**  | Bidirectional     | Bidirectional                 | Server → client  |
| Reconnects itself       | **Yes, automatically**    | No                | Yes                           | N/A              |
| Resume after a drop     | **Yes — `Last-Event-ID`** | You implement it  | Partial                       | You implement it |
| Protocol                | Plain HTTP                | Upgrade from HTTP | Negotiates the best available | Plain HTTP       |
| Through awkward proxies | Usually fine              | Sometimes blocked | Falls back automatically      | Always works     |
| Client code             | ~5 lines                  | Moderate          | A library                     | Moderate         |

The default is smaller than people assume. If updates only flow *from* the server — notifications, a live count, a progress feed, streaming tokens from a model — **SSE is almost always the right answer**. It is plain HTTP, it reconnects and resumes on its own, it survives proxies that block WebSocket upgrades, and it needs no library on either side.

``` csharp
// SSE from ASP.NET Core - the whole server side
app.MapGet("/notifications", async (HttpContext ctx, INotificationFeed feed,
                                    CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache, no-transform";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";     // stop nginx buffering

    var since = ctx.Request.Headers["Last-Event-ID"].ToString();

    await foreach (var n in feed.StreamAsync(since, ct))
    {
        await ctx.Response.WriteAsync(
            $"id: {n.Id}\nevent: notification\ndata: {JsonSerializer.Serialize(n)}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);           // FLUSH or nothing sends
    }
});
// .NET 10 adds TypedResults.ServerSentEvents, which handles the framing.
```

``` text
  SCALING SIGNALR OUT - the problem and the fix

  instance A holds Alice's connection.
  instance B holds Bob's connection.
  Alice sends a message to the group. Instance A broadcasts...
  ...only to the connections IT holds. Bob never receives it.

  the fix is a BACKPLANE - every instance publishes to a shared bus
  and subscribes to it:

      Redis backplane        .AddStackExchangeRedis(cs)
      Azure SignalR Service  .AddAzureSignalR()   <- offloads connections
                                                     entirely; your instances
                                                     stop holding sockets
  and the other requirements
    STICKY SESSIONS      required unless you use WebSockets exclusively,
                         because the negotiate handshake must reach the
                         same instance
    DEPLOY = MASS DROP   every rolling deploy disconnects every client.
                         Backoff WITH JITTER is what prevents the stampede.
    CONNECTION MEMORY    each connection holds buffers; tens of thousands
                         per instance is achievable and needs measuring
```

``` csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisCs, o => o.Configuration.ChannelPrefix = "app");

// authorise on the HUB, not just the endpoint - and re-check on reconnect
[Authorize]
public class OrdersHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User!.FindFirst("tenant_id")!.Value;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenant}");
        await base.OnConnectedAsync();
    }
    // then broadcast to the GROUP, never to All - or you leak across tenants
}
```

### HTTP & the Web Stack

<p class="src">From <em>Backend Engineering</em> · Backend Platform · HTTP & the Web Stack</p>

<p class="pd">HTTP and TLS at the level you need to debug them: what the headers actually control, how caching is negotiated, and what a certificate chain really is.</p>

<a id="be-g642"></a>

#### WebSockets, SSE and realtime transports

HTTP is request/response: the client asks, the server answers, the exchange ends. Realtime features — live dashboards, chat, progress bars, collaborative editing — need the server to speak first, and there are three ways to arrange that, each with different infrastructure consequences.

| Transport                | Direction              | Cost                                 | Use for                                   |
|--------------------------|------------------------|--------------------------------------|-------------------------------------------|
| Polling / long polling   | Client asks repeatedly | Wasteful, but works through anything | Fallback of last resort                   |
| Server-Sent Events (SSE) | Server → client only   | Plain HTTP, auto-reconnect built in  | Feeds, notifications, progress, log tails |
| WebSocket                | Full duplex            | Upgraded connection, held open       | Chat, collaboration, trading, games       |

``` text
the upgrade, on the wire:

GET /hub HTTP/1.1
Upgrade: websocket                  ← client asks to change protocol
Connection: Upgrade
Sec-WebSocket-Key: dGhlIHNhbXBsZ...

HTTP/1.1 101 Switching Protocols    ← after this, it is no longer HTTP:
Upgrade: websocket                     frames flow both ways until close
Connection: Upgrade

SSE is simpler — it never stops being HTTP:

GET /events            →   HTTP/1.1 200 OK
                           Content-Type: text/event-stream
                           Cache-Control: no-cache

                           data: {"progress":42}\n\n     ← one message
                           data: {"progress":87}\n\n     ← another, same response
```

The infrastructure consequences are where teams get caught. A held-open connection is a held resource at every hop: the load balancer, the reverse proxy, and the server all count it, and defaults are tuned for short requests.

``` csharp
# nginx must be told to allow the upgrade — without these two headers
# the handshake returns 200 with an HTML body and the client just fails
location /hub {
    proxy_pass         http://app;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade    $http_upgrade;
    proxy_set_header   Connection "upgrade";
    proxy_read_timeout 3600s;        # default 60s kills idle sockets
    proxy_buffering    off;          # required for SSE, or nothing streams
}
```

###### Scaling out breaks the obvious mental model

With one server, “send this user a message” means finding their socket in a dictionary. With three servers, the user’s socket is on exactly one of them and the request may arrive at either of the other two. That is why every realtime framework grows a backplane — Redis pub/sub, a broker, or a managed service — which broadcasts to all instances so whichever one holds the socket delivers it. Plan for the backplane before the second replica, not after.

> **\[Trap\]**
>
> **Common trap.** Idle timeouts kill connections silently. Load balancers commonly close idle sockets at 60–350 seconds and the client sees a generic disconnect. Send application-level pings (or configure the framework’s keep-alive) below the shortest timeout in the path, and make the client reconnect with backoff — a thundering herd of reconnects after a deploy is a self-inflicted outage.

> **\[Why it matters\]** Realtime changes your capacity model. Request/response capacity is measured in requests per second and each one releases its resources; a WebSocket tier is measured in *concurrent connections*, each holding memory and a file descriptor for hours. Sizing, autoscaling signals and deployment strategy all differ — a rolling restart that is invisible for a REST API disconnects every realtime client at once.

### Work that happens later

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Work that happens later</p>

<p class="pd">The outbox relay, a multi-step workflow with compensation, live updates over SignalR, and a scheduled job that stays correct when three instances run it at once.</p>

<a id="be-w20"></a>

#### Live updates: SignalR with a backplane and auth

``` csharp
// ---------- hub: authorised, tenant-scoped, thin ----------
[Authorize]
public sealed class OrdersHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User!.FindFirstValue("tenant_id")!;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenant}");
        await base.OnConnectedAsync();
    }
    // no business logic here — the hub is transport, not a service
}

builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisCs, o =>                 // ← the backplane:
        o.Configuration.ChannelPrefix = RedisChannel.Literal("orders"));
        // without it, a message published on instance A never reaches a
        // client whose socket lives on instance B

app.MapHub<OrdersHub>("/hubs/orders").RequireAuthorization();

// ---------- pushing from the domain, not from the endpoint ----------
public sealed class OrderStatusNotifier(IHubContext<OrdersHub> hub)
    : IConsumer<OrderConfirmed>
{
    public Task Consume(ConsumeContext<OrderConfirmed> ctx) =>
        hub.Clients.Group($"tenant:{ctx.Message.TenantId}")
           .SendAsync("orderConfirmed", new
           {
               ctx.Message.OrderId, ctx.Message.ConfirmedAt
           }, ctx.CancellationToken);
}
```

``` csharp
// client: reconnection is the part that gets skipped
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/orders", { accessTokenFactory: () => auth.getToken() })
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .configureLogging(signalR.LogLevel.Warning)
  .build();

connection.on("orderConfirmed", (e) => store.markConfirmed(e.orderId));

connection.onreconnected(async () => {
  // the gap matters: fetch what was missed while disconnected
  await store.refreshSince(store.lastEventAt);
});

await connection.start();
```

> **\[Trap\]**
>
> **Common trap.** Realtime is an *optimisation*, never the source of truth. A client that missed a message while disconnected must be able to recover by querying — which is why `onreconnected` refetches. Systems that treat pushed events as the only delivery mechanism silently show stale data to anyone whose train went through a tunnel.
