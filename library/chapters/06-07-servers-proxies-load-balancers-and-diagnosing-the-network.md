---
id: "6-7"
number: "6.7"
title: "Servers, proxies, load balancers and diagnosing the network"
part: 6
part_title: "Networks"
words: 9141
minutes: 40
---

# 6.7 Servers, proxies, load balancers and diagnosing the network

> **Quick look.** What sits between clients and your code: web servers, reverse proxies, load balancers, and how to debug the network.
>
> - Kestrel usually sits behind nginx, IIS, Envoy, an ingress or a cloud load balancer that terminates TLS.
> - Trust X-Forwarded-For only from known proxies, or clients can forge their IP.
> - L4 balancers route connections, L7 route requests; HTTP/2 or gRPC needs L7 or one pod gets all traffic.
> - Avoid sticky sessions; keep session state in Redis.
> - Every outbound call needs a timeout shorter than its caller's; rate-limit to protect the origin.
> - Diagnose layer by layer: DNS, TCP connect, TLS, then HTTP.
> - Deny egress by default and block the cloud metadata address (169.254.169.254) from apps first.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Rate limits bypassed with a forged header

Per-IP rate limiting stops working: attackers send `X-Forwarded-For: 1.2.3.<random>`.

*Trust forwarded headers only from your proxy*

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear(); o.KnownProxies.Clear();
    o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.10.0.0"), 20));   // load balancer subnet
});
app.UseForwardedHeaders();   // before rate limiting, auth and logging
```

> **Lesson:** Forwarded headers are client input unless they come from a proxy you trust.

#### Example 2: gRPC traffic all landing on one pod

Five pods run the pricing gRPC service, but one is at 100% CPU and four are idle.

**What goes wrong.** HTTP/2 multiplexes every call on one long-lived connection, and an L4 balancer balances connections, not requests.

*Client-side balancing over all pods*

```csharp
// headless Kubernetes service: DNS returns every pod IP
var channel = GrpcChannel.ForAddress("dns:///pricing-headless.default.svc.cluster.local:5000", new GrpcChannelOptions
{
    Credentials = ChannelCredentials.Insecure,
    ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
});
// or put an L7 proxy / service mesh in front that balances per request
```

> **Lesson:** Long-lived multiplexed connections need request-level (L7) or client-side balancing.

### Networking

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Networking</p>

<p class="pd">The wire. Most production incidents that look like application bugs are really DNS, TLS, timeouts, connection pools or a proxy in the middle — so this is the layer worth genuinely understanding.</p>

<a id="be-a161"></a>

#### Kestrel, reverse proxies and YARP

Kestrel is the .NET web server and it is fast. In most deployments something sits in front of it: nginx, IIS, Envoy, an ingress controller, or a cloud load balancer, handling TLS termination, static files, compression, WAF rules and routing.

``` text
  internet -> [ load balancer / ingress ]      TLS terminates here
                     |
                     v
              [ reverse proxy ]                routing, rate limit, auth offload
                     |
             +-------+-------+
             v               v
        [ Kestrel pod ]  [ Kestrel pod ]       your app, plain HTTP inside the cluster
```

Once you are behind a proxy, the request's remote IP is the proxy's. Without `ForwardedHeaders`, rate limiting, geo rules and audit logs all see one address, and `Request.Scheme` says http so your redirect URIs break.

``` csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { new IPNetwork(IPAddress.Parse("10.0.0.0"), 8) }   // trust only your proxy
});
```

##### YARP

YARP is a reverse proxy you configure in .NET, which makes it a natural gateway or strangler-fig front door: route `/orders/**` to the new service and everything else to the legacy app, then move routes over one at a time.

``` json
{
  "ReverseProxy": {
    "Routes": {
      "orders": { "ClusterId": "new",    "Match": { "Path": "/api/orders/{**rest}" } },
      "legacy": { "ClusterId": "legacy", "Match": { "Path": "{**catch-all}" } }
    },
    "Clusters": {
      "new":    { "Destinations": { "d1": { "Address": "http://orders-svc:8080/" } } },
      "legacy": { "Destinations": { "d1": { "Address": "http://legacy-iis/" } } }
    }
  }
}
```

###### Kestrel's limits, and what the proxy is really for

``` csharp
builder.WebHost.ConfigureKestrel(k =>
{
    // the limits that protect you from clients, and their defaults
    k.Limits.MaxConcurrentConnections = null;          // unlimited by default
    k.Limits.MaxRequestBodySize = 30_000_000;          // ~28 MB default
    k.Limits.MaxRequestHeadersTotalSize = 32_768;
    k.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);

    // the SLOWLORIS defence - a minimum data rate, so a client that
    // dribbles bytes cannot hold a connection open indefinitely
    k.Limits.MinRequestBodyDataRate =
        new MinDataRate(bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
    k.Limits.MinResponseDataRate =
        new MinDataRate(bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
});
```

``` text
  WHY A REVERSE PROXY, when Kestrel is fast and can face the internet?

  Kestrel CAN be edge-facing, and Microsoft supports it. The proxy earns
  its place for reasons that are mostly NOT performance:

    BUFFERING       a client on a 3G connection takes 3 seconds to send a
                    request. nginx reads it fully, then hands it to Kestrel,
                    which handles it in 20ms and moves on. Without the proxy,
                    a Kestrel connection is held for the full 3 seconds.

    TLS management  one place for certificates, not one per instance

    ROUTING         several applications on one host/port

    SHIELDING       malformed requests, floods and scanners never reach
                    your application code

    GRACEFUL DRAIN  remove an instance from rotation before stopping it

  what the proxy does NOT buy you: raw throughput. Kestrel is faster than
  nginx at serving a .NET response. The proxy is about protection and
  operations, not speed.
```

``` csharp
// the configuration people forget, and it breaks auth and redirects
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost,
    // WITHOUT these, .NET trusts the headers from ANYONE, so a client can
    // forge its own IP - defeating rate limiting, IP allow-lists and audit.
    KnownProxies = { IPAddress.Parse("10.0.1.10") },
    // or, in a container where the proxy IP is unknown:
    // KnownNetworks = { new IPNetwork(IPAddress.Parse("10.0.0.0"), 8) }
});
// must run EARLY - before UseAuthentication and before anything reading
// Request.Scheme, or HTTPS redirects will loop.
```

> **\[Trap\]**
>
> **Common trap.** Leaving `ForwardedHeaders` unconfigured is the default state, and it produces two distinct bugs: every client appears to come from the proxy's IP (so per-IP rate limiting throttles everyone as one), and `Request.Scheme` is `http` (so generated absolute URLs and HTTPS redirects are wrong, sometimes producing a redirect loop). Configure it, and configure `KnownProxies` — trusting the header from anywhere is worse than not trusting it at all.

<a id="be-a162"></a>

#### Load balancing, service discovery and health

| Approach         | How it works                         | Notes                                                |
|------------------|--------------------------------------|------------------------------------------------------|
| DNS round robin  | Multiple A records                   | Cached by clients; no health awareness               |
| L4 load balancer | Balances TCP connections             | Cheap; wrong for HTTP/2 and gRPC                     |
| L7 load balancer | Balances individual HTTP requests    | Correct for HTTP/2, can route on path and header     |
| Client-side      | Client knows all instances and picks | Lowest latency; needs a discovery source             |
| Service mesh     | Sidecar proxy handles everything     | mTLS, retries and tracing for free; operational cost |

Load-balancing algorithms in ascending order of usefulness: round robin, least connections, power-of-two-choices, and latency-weighted. Sticky sessions are a smell — they defeat balancing and make rolling deploys visible to users. Keep session state in Redis instead.

##### Health checks the balancer can use

``` csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(cs, name: "sql", tags: ["ready"])
    .AddRedis(redisCs, name: "redis", tags: ["ready"])
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

app.MapHealthChecks("/health/live",  new() { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

> **\[Trap\]**
>
> **Common trap.** Liveness must not check dependencies. If your liveness probe fails because the database is down, Kubernetes restarts every pod in a loop while the database recovers, turning a degraded system into a dead one. Liveness = ‘is this process wedged?’. Readiness = ‘can I serve traffic right now?’.

###### Health endpoints that are useful rather than harmful

``` csharp
builder.Services.AddHealthChecks()
    // LIVENESS: is the process alive? Check NOTHING external.
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])

    // READINESS: should this instance receive traffic?
    .AddNpgSql(cs, name: "db", tags: ["ready"])
    .AddRedis(redisCs, name: "cache", tags: ["ready"]);

app.MapHealthChecks("/health/live",  new() { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

``` text
  WHY THE SPLIT MATTERS - the outage it prevents

  suppose ONE endpoint checks the database, and it is wired to LIVENESS.

    the database has a slow minute.
    every instance's liveness check fails.
    the orchestrator RESTARTS every instance, simultaneously.
    they all reconnect at once, hammering a database that was
      already struggling.
    startup is slower than steady state, so the outage lengthens.
    -> a brief database blip became a total, self-inflicted outage.

  the rule
    LIVENESS   "is this process wedged?"   -> if no, RESTART
               depends on NOTHING external. Return 200 unconditionally
               unless you can detect a genuine internal deadlock.

    READINESS  "can I serve right now?"    -> if no, REMOVE FROM ROTATION
               may check dependencies. Does NOT cause a restart.
```

``` csharp
// and CACHE the readiness result. 50 instances x every 5 seconds x
// a real database query = 600 queries a minute of pure overhead -
// which is worst exactly when the database is already struggling.
builder.Services.AddHealthChecks()
    .AddNpgSql(cs, name: "db", tags: ["ready"])
    .AddCheck<CachedReadiness>("cached", tags: ["ready"]);

// or use the built-in publisher and serve a cached snapshot
builder.Services.Configure<HealthCheckPublisherOptions>(o =>
{
    o.Delay = TimeSpan.FromSeconds(5);
    o.Period = TimeSpan.FromSeconds(15);      // evaluate every 15s, not per probe
});
```

| Discovery mechanism                     | How                                               | Fits                                           |
|-----------------------------------------|---------------------------------------------------|------------------------------------------------|
| Static configuration                    | Hostnames in config                               | Few, stable services                           |
| DNS                                     | A name resolving to healthy instances             | The default; Kubernetes Services work this way |
| Load balancer VIP                       | One stable address in front of a pool             | Simple and effective                           |
| `Microsoft.Extensions.ServiceDiscovery` | Resolves `https+http://orders` from config or DNS | The .NET-native option; what Aspire wires up   |
| Consul, Eureka                          | Register and query                                | Richer health and metadata                     |
| Service mesh                            | A sidecar handles it                              | Large fleets; adds mTLS and policy             |

<a id="be-a164"></a>

#### Diagnosing network problems

A repeatable order of operations beats guessing. Work up the stack, and prove each layer before moving on.

1.  **Resolve.** `dig +short api.internal` or `nslookup`. Is the answer what you expect, and how long is the TTL?
2.  **Reach.** `nc -vz host 443` or `Test-NetConnection host -Port 443`. Refused vs timeout tells you firewall vs nothing-listening.
3.  **Negotiate.** `openssl s_client -connect host:443 -servername host`. Read the chain and the expiry dates.
4.  **Request.** `curl -v --http2 https://host/health`. Check status, headers and which proxy answered.
5.  **Observe.** `ss -tan state time-wait | wc -l`, `netstat -ano`, or `dotnet-counters` for connection and thread-pool counters.
6.  **Capture.** Only now reach for tcpdump or Wireshark, with a tight filter.

``` text
dotnet-counters monitor -n MyApi --counters System.Net.Http,System.Runtime
#  http requests-queued, connections established
#  threadpool queue length, gen 2 collections
```

> **\[Note\]**
>
> **The three usual suspects.** In roughly this order: a timeout somewhere is longer than the layer above it; a connection pool is exhausted because something is not being disposed; or DNS is returning a stale address. Check those before you suspect the network itself.

###### Worked example

A full triage of a real-shaped incident: “the orders API is timing out calling billing, but only from the Kubernetes cluster, and only sometimes.”

``` bash
# 1. is it DNS, and what does it resolve to FROM INSIDE THE POD?
kubectl exec -it orders-api-7d9 -- sh
> nslookup billing-api
  billing-api.prod.svc.cluster.local -> 10.96.14.22        # cluster IP: correct
> nslookup billing.example.com
  -> 203.0.113.9                                            # PUBLIC ip - suspicious.
    # the service is being reached over the internet instead of in-cluster.

# 2. can we reach it at all, and how fast is the connect?
> time nc -vz 10.96.14.22 8080
  Connection to 10.96.14.22 8080 port [tcp/*] succeeded!
  real 0m0.004s                                             # fine

> time nc -vz billing.example.com 443
  real 0m5.021s ... timed out                               # NOT fine

# 3. what does the request actually do?
> curl -v -o /dev/null -w '%{time_namelookup} %{time_connect} %{time_appconnect} %{time_total}\n' \
       https://billing.example.com/health
  0.004  0.031  2.940  3.180
  #                ^^^ TLS handshake is 2.9 seconds. That is the problem.

# 4. why is the handshake slow?
> openssl s_client -connect billing.example.com:443 -servername billing.example.com
  ... verify error: unable to get local issuer certificate
  # the pod is trying to fetch the intermediate via AIA over an egress path that is
  # blocked, waiting for a timeout, then falling back.
```

1.  **Root cause:** the service was configured with a public hostname instead of the in-cluster service name, so traffic left the cluster, hit the egress firewall, and paid a TLS handshake that stalled on an AIA fetch.
2.  **Immediate fix:** point the client at `http://billing-api:8080` — in-cluster, no TLS needed inside the mesh boundary.
3.  **Prevent recurrence:** service discovery instead of hardcoded hostnames, plus an egress network policy that would have failed loudly rather than hanging.
4.  **Detect it next time:** a histogram of `time_appconnect` per upstream, so a slow handshake is a graph rather than an archaeology exercise.

The general lesson is the ordering: resolve, connect, negotiate, request. Each step has a distinct symptom and a distinct fix, and `curl`'s timing breakdown separates all four in one command. Reaching for tcpdump before doing this is almost always premature.

###### A diagnostic command reference worth keeping

``` bash
# --- DNS
dig +short api.internal            ; dig +trace api.internal      # what and from where
resolvectl query api.internal                                     # systemd resolver view

# --- reachability
nc -vz host 443                    ; Test-NetConnection host -Port 443
traceroute -T -p 443 host          ; tracert host                 # where it stops

# --- TLS
openssl s_client -connect host:443 -servername host -showcerts

# --- HTTP with full timing
curl -v --http2 -o /dev/null \
  -w 'dns=%{time_namelookup} conn=%{time_connect} tls=%{time_appconnect} ttfb=%{time_starttransfer} total=%{time_total}\n' \
  https://host/path

# --- sockets on this machine
ss -tanp                           # what is connected, and which process owns it
ss -s                              # summary counts by state
ss -tan state time-wait | wc -l    # port exhaustion check

# --- from inside the .NET process
dotnet-counters monitor -n MyApi --counters System.Net.Http,System.Net.Sockets,System.Runtime
dotnet-trace collect -p PID --providers Microsoft-System-Net-Http
```

If a pod has no shell — which is the point of a chiseled image — use an ephemeral debug container: `kubectl debug -it pod/orders-api --image=nicolaka/netshoot --target=api`. It joins the same network namespace, so you are diagnosing the real network path, not a sidecar's.

### How Networks Work

<p class="src">From <em>Backend Engineering</em> · Backend Platform · How Networks Work</p>

<p class="pd">What actually happens between your code calling a URL and bytes arriving. Addressing, routing, DNS and TCP — the layer most backend developers use daily and have never been taught.</p>

<a id="be-g639"></a>

#### Diagnosing a network problem

A method, in order. Each step eliminates a layer, so you stop as soon as one fails rather than guessing.

1.  **Does the name resolve?** `dig host`. If not, it is DNS — nothing else matters yet.
2.  **Is the host reachable?** `ping` — noting that ICMP is often blocked, so failure here is not conclusive.
3.  **Where does the path break?** `traceroute` or `mtr`.
4.  **Is the port open?** `nc -zv host port`. Refused vs timeout tells you firewall or service.
5.  **Is something listening locally?** `ss -ltnp` on the server — and check *which address* it bound to.
6.  **Does the protocol work?** `curl -v` for the full request and response, including TLS.
7.  **What is actually on the wire?** `tcpdump` — the final authority when everything else disagrees.

``` bash
# the toolkit
dig api.example.com +short
ping -c4 api.example.com
mtr -rw api.example.com                  # traceroute + per-hop loss, continuously
nc -zv api.example.com 443               # is the port open?
curl -v https://api.example.com/health   # full exchange, headers, TLS details
curl -w '@curl-format.txt' -o /dev/null -s https://api.example.com/
      # dns: %{time_namelookup}  connect: %{time_connect}
      # tls: %{time_appconnect}  ttfb: %{time_starttransfer}  total: %{time_total}
      # -> tells you WHICH PHASE is slow, which is usually the whole diagnosis

ss -ltnp                                 # listening sockets + owning process
ss -tanp | grep :5432                    # connections to a specific port

sudo tcpdump -i any -nn port 5432 -c 100          # capture, no name resolution
sudo tcpdump -i any -nn host 10.0.4.37 -w cap.pcap  # to a file, for Wireshark
```

| Observation                         | Almost certainly                                                            |
|-------------------------------------|-----------------------------------------------------------------------------|
| Timeout, no response at all         | Firewall or security group dropping silently                                |
| Connection refused, immediately     | Reached the host; nothing listening on that port                            |
| Works from one host, not another    | Security group, network ACL, or route table                                 |
| Works by IP, fails by name          | DNS                                                                         |
| First request slow, rest fast       | DNS or TLS handshake on a cold connection                                   |
| Intermittent failures under load    | Connection limits, port exhaustion, or NAT gateway limits                   |
| Service bound to `127.0.0.1`        | Reachable only from the same host — bind `0.0.0.0`                          |
| Works locally, fails in a container | Container network namespace — `localhost` means the container, not the host |

###### A decision tree that terminates, and the tools for each branch

``` text
  work OUTWARD from your process. Each step eliminates a layer.

  1  IS IT DNS?
       getent hosts api.example.com          -> no answer? DNS. Stop.
       dig api.example.com                   -> compare with expected

  2  CAN I OPEN A TCP CONNECTION?
       nc -vz api.example.com 443            (or: timeout 3 bash -c
                                              '</dev/tcp/host/443')
       CONNECTION REFUSED -> the packet ARRIVED and nothing listened.
                             the network path is FINE. Wrong port,
                             service down, or bound to 127.0.0.1.
       TIMED OUT          -> the packet VANISHED. Firewall, security
                             group, NACL, or routing. Different team.
       -> these two are the most important distinction in the whole
          exercise, and people conflate them constantly.

  3  DOES TLS COMPLETE?
       openssl s_client -connect host:443 -servername host </dev/null
       look at: the chain, "Verify return code", and the SNI name

  4  DOES HTTP ANSWER?
       curl -v -w '%{time_connect} %{time_appconnect} %{time_starttransfer}'
       a 5xx here means the path is fine and the APPLICATION failed

  5  WHERE DOES IT STOP?
       mtr -rwzbc 100 api.example.com        <- better than traceroute:
                                                continuous, shows LOSS PER HOP
       note: loss at an INTERMEDIATE hop only is usually ICMP
       de-prioritisation, not real loss. Loss that persists to the
       FINAL hop is real.
```

``` bash
# see the packets, when nothing else explains it
sudo tcpdump -i any -nn 'host 10.0.1.20 and port 5432' -c 50
# SYN with no SYN-ACK          -> nothing listening, or dropped
# SYN, SYN-ACK, RST            -> something reset it. A proxy? A firewall?
# retransmissions of the same seq -> packet loss
# FIN from the far side quickly -> the server closed it deliberately

# who is actually listening, locally
ss -tlnp | grep 5432
# 0.0.0.0:5432   listening on ALL interfaces - reachable
# 127.0.0.1:5432 LOOPBACK ONLY - unreachable from anywhere else.
#                this single line explains an enormous number of
#                "connection refused" incidents.

# and per-socket state counts
ss -s
ss -tan state time-wait | wc -l      # thousands = short-lived connections
ss -tan state close-wait | wc -l     # growing = YOUR code is not closing
```

| Observation                         | Means                                                    | Owner                                |
|-------------------------------------|----------------------------------------------------------|--------------------------------------|
| `ECONNREFUSED`                      | Reached the host, nothing listening                      | The service owner                    |
| Timeout on connect                  | Packet dropped silently                                  | Network / firewall team              |
| `ECONNRESET` mid-stream             | Something actively closed it                             | A proxy, an idle timeout, or the app |
| TLS `unable to get local issuer`    | Missing intermediate certificate                         | The certificate owner                |
| Works in a browser, fails from code | **The browser fetched the missing intermediate for you** | Fix the chain                        |
| Slow only on the first request      | DNS or TLS handshake, not the app                        | Enable connection reuse              |
| `CLOSE_WAIT` growing                | **Your code is not disposing sockets**                   | You                                  |

### Servers, Proxies & Balancing

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Servers, Proxies & Balancing</p>

<p class="pd">The machinery between the internet and your process: what a web server does, how proxies and load balancers behave, and how to protect an origin from traffic it cannot handle.</p>

<a id="be-g646"></a>

#### What a web server actually does

“Web server” covers two different jobs that are frequently combined: serving files, and passing requests to an application. Understanding the concurrency model matters because it determines how the thing fails under load.

| Server      | Concurrency model                                               | Typical role                                      |
|-------------|-----------------------------------------------------------------|---------------------------------------------------|
| **nginx**   | Event-driven, few worker processes                              | Reverse proxy, TLS termination, static files      |
| **Apache**  | Process or thread per connection (prefork/worker), or event MPM | Legacy hosting, `.htaccess` ecosystems            |
| **HAProxy** | Event-driven                                                    | Pure load balancing, L4 and L7                    |
| **Envoy**   | Event-driven, dynamically configured                            | Service mesh data plane, modern ingress           |
| **Caddy**   | Event-driven, automatic HTTPS                                   | Simple deployments                                |
| **IIS**     | Thread pool, application pools                                  | Windows and .NET Framework                        |
| **Kestrel** | Event-driven (async I/O)                                        | .NET's own server; usually behind a reverse proxy |

``` text
  why the model matters under load

  THREAD/PROCESS PER CONNECTION
    1,000 slow clients = 1,000 threads = ~1 GB of stacks + context switching
    memory is the limit; the "slowloris" attack exploits exactly this

  EVENT-DRIVEN
    1,000 slow clients = a few threads and 1,000 file descriptors
    file descriptors and memory per connection are the limit, and both are cheap
    -> this is why nginx sits in front of application servers:
       it absorbs slow clients so your app never sees them
```

``` csharp
# the reverse proxy's real job is BUFFERING
#
#   slow mobile client  <--- 3 seconds --->  nginx  <-- 20ms -->  your app
#
# nginx reads the whole request before forwarding it, and buffers the whole
# response before trickling it out. Your application handles the request in
# 20ms and moves on, instead of holding a worker for 3 seconds.

proxy_buffering on;                      # default, and usually what you want
proxy_request_buffering on;
# turn buffering OFF only for streaming responses - SSE, long polling,
# large downloads - where you want bytes to flow through immediately.
```

<a id="be-g647"></a>

#### Reverse proxies and what they change

``` text
  FORWARD proxy      client -> [proxy] -> internet
                     acts on behalf of the CLIENT. Egress filtering,
                     corporate web filtering, hiding client identity.

  REVERSE proxy      internet -> [proxy] -> your servers
                     acts on behalf of the SERVER. Clients do not know
                     it exists, or how many servers are behind it.
```

| A reverse proxy gives you | Detail                                             |
|---------------------------|----------------------------------------------------|
| TLS termination           | One place to manage certificates                   |
| Load balancing            | Across several application instances               |
| Buffering                 | Absorbs slow clients                               |
| Compression               | gzip/brotli once, at the edge                      |
| Caching                   | Serve repeat responses without touching the origin |
| Rate limiting             | Before requests consume application resources      |
| Header manipulation       | Security headers, forwarded client information     |
| Path routing              | `/api` to one service, `/` to another              |
| Graceful upstream removal | Drain a server for deployment                      |

``` csharp
server {
    listen 443 ssl http2;
    server_name api.example.com;

    ssl_certificate     /etc/ssl/live/api.example.com/fullchain.pem;   # leaf + intermediates
    ssl_certificate_key /etc/ssl/live/api.example.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;

    location / {
        proxy_pass http://app_upstream;

        # WITHOUT these, your application sees nginx's IP as the client,
        # thinks every request is HTTP, and generates wrong redirect URLs.
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host  $host;

        proxy_connect_timeout 5s;
        proxy_read_timeout   60s;      # must EXCEED your slowest legitimate request
        proxy_next_upstream error timeout http_502 http_503;
    }
}

upstream app_upstream {
    server 10.0.10.11:8080 max_fails=3 fail_timeout=30s;
    server 10.0.10.12:8080 max_fails=3 fail_timeout=30s;
    keepalive 32;                      # reuse upstream connections - big win
}
```

> **\[Trap\]**
>
> **Common trap.** Your application must be configured to *trust* forwarded headers, and to trust them only from known proxies. Trusting `X-Forwarded-For` from anywhere lets a client forge its own IP address — defeating rate limiting, IP allow-lists and audit logs. In ASP.NET Core that is `ForwardedHeadersOptions` with `KnownProxies` populated; every framework has an equivalent, and leaving it unconfigured is the default state.

###### Where a request's time goes through a proxy, and the buffering decision

``` csharp
# nginx: the settings that actually change behaviour, annotated
upstream app {
    server 10.0.1.10:8080 max_fails=3 fail_timeout=30s;
    server 10.0.1.11:8080 max_fails=3 fail_timeout=30s;
    keepalive 32;                    # REUSE upstream connections.
                                     # without this, nginx opens a NEW TCP
                                     # connection per request to your app -
                                     # and you will see TIME_WAIT climb.
}

server {
    location / {
        proxy_pass http://app;
        proxy_http_version 1.1;      # REQUIRED for keepalive to work
        proxy_set_header Connection "";   # and so is clearing this header

        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        proxy_connect_timeout 3s;    # time to REACH the app
        proxy_send_timeout   30s;    # time to send the request
        proxy_read_timeout   30s;    # time between bytes of the RESPONSE
                                     # not total time - a slow stream is fine
    }
}
```

``` text
  BUFFERING - the setting that decides whether the proxy helps at all

  proxy_buffering on   (default)

    client (slow 3G)          nginx                 app
      |--- 3 seconds --------->|
      |                        |--- 20 ms ---------->|
      |                        |<-------------------|   app is FREE after 20 ms
      |<--- 3 seconds ---------|

    nginx absorbs the slow client. The app handles one request in 20 ms
    and moves on. THIS is the main reason to run a proxy - not speed.

  proxy_buffering off

    the app's connection is held for the full 3 seconds.
    with 500 slow clients, 500 app connections are occupied doing nothing.

  BUT you must turn it OFF for:
    server-sent events        or nothing streams; the client sees nothing
    long-polling              same
    large downloads           or nginx buffers to disk first
    gRPC streaming

  the symptom of getting this wrong is very recognisable: "my SSE
  endpoint works locally and sends nothing in production".
```

| Symptom through a proxy                     | Cause                                                |
|---------------------------------------------|------------------------------------------------------|
| `502 Bad Gateway`                           | The app refused the connection or died mid-response  |
| `504 Gateway Timeout`                       | The app exceeded `proxy_read_timeout`                |
| `413 Payload Too Large`                     | `client_max_body_size` — default is 1 MB             |
| Every client appears to have the proxy's IP | `ForwardedHeaders` not configured in the app         |
| Infinite HTTPS redirect loop                | The app sees `http`; `X-Forwarded-Proto` not trusted |
| `TIME_WAIT` climbing on the proxy           | No upstream keepalive                                |
| SSE or streaming produces nothing           | `proxy_buffering` is on                              |

<a id="be-g648"></a>

#### Load balancing: L4, L7 and algorithms

**Layer 4 (transport)**

- Routes on IP and port only
- Never reads the payload — works for any TCP protocol
- Very fast, very low overhead
- Cannot route by path, header or cookie
- TLS passes through untouched

**Layer 7 (application)**

- Parses HTTP: path, headers, method, cookies
- Routing by path, host, header; content rewriting
- Terminates TLS, so it can inspect and re-encrypt
- Higher cost per request
- Retries, circuit breaking, per-route policy

| Algorithm                | Behaviour                                            | Suits                                                   |
|--------------------------|------------------------------------------------------|---------------------------------------------------------|
| **Round robin**          | Each server in turn                                  | Uniform servers and uniform requests                    |
| **Weighted round robin** | Proportional to capacity                             | Mixed instance sizes                                    |
| **Least connections**    | Fewest active connections wins                       | Variable request durations — usually the best default   |
| **Least response time**  | Fastest observed server                              | Heterogeneous performance                               |
| **IP hash**              | Same client → same server                            | Crude session affinity                                  |
| **Consistent hashing**   | Same key → same server, stable when the pool changes | Cache affinity — adding a server moves only 1/N of keys |
| **Random two choices**   | Pick two at random, choose the less loaded           | Near-optimal with almost no coordination                |

``` text
  HEALTH CHECKS - the part that decides whether balancing works at all

  PASSIVE     observe real traffic; after N failures, eject the server
              free, and it takes N real user failures to notice

  ACTIVE      poll /health every few seconds
              detects failure before users do

  and the distinction that matters:

  LIVENESS    "is the process alive?"        -> if not, RESTART it
  READINESS   "should it receive traffic?"   -> if not, REMOVE from the pool
                                                 but do NOT restart

  conflating them is a classic outage: a readiness check that fails because
  a dependency is slow gets wired to liveness, so every instance restarts,
  simultaneously, while the dependency is already struggling.
```

``` text
# a health endpoint that is actually useful
GET /health/live    -> 200 if the process is running. Check NOTHING else.
GET /health/ready   -> 200 only if it can serve: DB reachable, cache reachable,
                       migrations applied, warm-up complete

# the trap: a readiness check that queries the database on every poll,
# from 50 instances, every 5 seconds, is 600 queries a minute of pure overhead -
# and when the database is struggling, your health checks make it worse.
# Cache the result for a few seconds.
```

###### Why least-connections beats round-robin, and how health checks cascade

``` text
  ROUND ROBIN fails when requests are NOT UNIFORM, which is most systems.

    3 backends, requests taking 10 ms or 2,000 ms at random.

    round robin      each backend gets 1/3 of REQUESTS.
                     one unlucky backend gets three slow ones in a row,
                     queues, and its latency climbs - while the load
                     balancer keeps sending it its share regardless.

    least connections  each new request goes to the backend with the
                     FEWEST IN-FLIGHT requests.
                     a backend handling a slow request naturally
                     receives less work. Self-correcting.

  and the one that matters most at scale:

    LEAST TIME / PEAK EWMA    weights by observed LATENCY, not count.
                     a backend that is degraded but still accepting
                     connections gets drained automatically - which
                     least-connections does not do, because a slow
                     backend and a busy backend look identical by count.

  the general rule: round robin is correct only when every request
  costs the same and every backend is identical. Neither is usually true.
```

|                          | L4 (transport)                         | L7 (application)                     |
|--------------------------|----------------------------------------|--------------------------------------|
| Sees                     | IP and port                            | URL, headers, method, body           |
| Can route by             | Nothing above TCP                      | Path, host, header, cookie           |
| TLS                      | Passes through                         | Usually terminates it                |
| Overhead                 | Very low                               | Higher — it parses                   |
| Retries a failed request | **No**                                 | **Yes** — it understands the request |
| Health check             | Can it connect?                        | Does the endpoint return 200?        |
| Fits                     | Databases, raw TCP, extreme throughput | HTTP APIs — the usual choice         |

``` text
  THE HEALTH-CHECK CASCADE, and it is self-inflicted every time

    health check hits /health, which queries the DATABASE.

    t=0   the database has a slow minute
    t=5s  every backend's health check times out
    t=10s the load balancer marks ALL backends unhealthy
    t=10s ...and now has nowhere to send traffic. 100% failure.
          note the database RECOVERED at t=30s, but the backends are
          still out of rotation until they pass again.

  the fixes, and you want all three
    1  liveness checks NOTHING external. Readiness may.
    2  the load balancer needs a MINIMUM HEALTHY threshold - if all
       backends fail, keep sending traffic to them anyway. Degraded
       service beats none, and the alternative helps nobody.
    3  CACHE the health result. 50 backends x every 5 seconds x a real
       query = 600 queries/minute of pure overhead, worst exactly when
       the database is already struggling.
```

<a id="be-g649"></a>

#### Session affinity, and why to avoid it

“Sticky sessions” pin a client to one server. It solves a real problem and creates several, and the better answer is almost always to remove the need for it.

| Approach                     | How                                              | Cost                                                          |
|------------------------------|--------------------------------------------------|---------------------------------------------------------------|
| **Cookie-based affinity**    | The balancer sets a cookie naming the backend    | Uneven load; state lost when that server dies                 |
| **IP hash**                  | Hash the client IP                               | Breaks behind corporate NAT — thousands of users share one IP |
| **Shared session store**     | Sessions in Redis or a database                  | A network hop per request; a new dependency                   |
| **Stateless tokens**         | All state in a signed token the client holds     | Token size; revocation is harder                              |
| **No server session at all** | The client holds state; the server holds nothing | The cleanest, where the domain allows                         |

``` text
  why affinity hurts

  deploy         every server restart drops its sessions -> users logged out
  scaling out    new servers get no traffic from existing users
  failure        one server dying logs out exactly its share of users
  load           a "hot" server stays hot; the balancer cannot rebalance
  autoscaling    scale-in terminates instances holding live sessions

  the fix is not a better affinity mechanism. It is making the application
  stateless so any request can be served by any instance.
```

> **\[Note\]**
>
> **The design rule.** Anything a server holds in memory between requests is state you must then replicate, route around, or lose. Push it outward — to a signed token the client carries, or to a shared store that every instance can reach. Then affinity, sticky deploys and drain-before-scale-in all stop being problems you have to solve.

<a id="be-g650"></a>

#### Rate limiting and protecting an origin

| Algorithm             | Behaviour                                       | Note                                               |
|-----------------------|-------------------------------------------------|----------------------------------------------------|
| **Fixed window**      | N per calendar minute                           | Simple; allows a 2N burst across a window boundary |
| **Sliding window**    | N over the trailing period                      | Smoother; more state                               |
| **Token bucket**      | Tokens refill at a rate; each request costs one | Allows controlled bursts. The usual best choice    |
| **Leaky bucket**      | Requests drain at a fixed rate                  | Enforces a smooth output rate                      |
| **Concurrency limit** | N in flight at once                             | Protects a resource better than a rate does        |

``` csharp
# nginx: two independent limits, because they defend different things
limit_req_zone  $binary_remote_addr zone=perip:10m  rate=10r/s;
limit_conn_zone $binary_remote_addr zone=conns:10m;

location /api/ {
    limit_req  zone=perip burst=20 nodelay;   # rate, with a burst allowance
    limit_conn conns 10;                      # concurrent connections per IP
    limit_req_status 429;
}

# and tell the client what happened, in a machine-readable way
add_header Retry-After 30 always;
# RateLimit-Limit / RateLimit-Remaining / RateLimit-Reset
```

| Protection              | Defends against                           | Where           |
|-------------------------|-------------------------------------------|-----------------|
| Rate limit per client   | Abusive individual clients                | Edge or gateway |
| Global concurrency cap  | Overload from legitimate traffic          | Application     |
| Request size limit      | Memory exhaustion from large bodies       | Proxy           |
| Timeouts at every layer | Slow clients and slow upstreams           | Everywhere      |
| Connection limits       | Slowloris and socket exhaustion           | Proxy           |
| CDN caching             | Repeat traffic reaching the origin at all | Edge            |
| Queue with shedding     | Unbounded queue growth under load         | Application     |
| WAF                     | Known attack patterns                     | Edge            |

``` text
  LOAD SHEDDING beats queueing, and this is counter-intuitive

  under overload, a growing queue means every request waits longer,
  and eventually EVERY request times out - so you serve nobody while
  doing maximum work.

  shedding: reject early, cheaply, with 429 or 503.
    -> the requests you DO accept still succeed
    -> the client can retry with backoff
    -> throughput of USEFUL work stays high

  the counter-intuitive part: deliberately failing some requests fast
  produces better overall availability than trying to serve them all.
```

> **\[Note\]**
>
> **Timeouts are the foundation.** Every outbound call needs a timeout shorter than the caller's timeout, all the way down the chain. Without that, a slow dependency propagates upward: threads pile up waiting, the pool exhausts, and a service that is merely *slow* at the bottom becomes a service that is *down* at the top. Set them explicitly — almost every client library defaults to infinite.

###### Choosing an algorithm, and what to key on

``` text
  FOUR ALGORITHMS, and the difference is what happens at a boundary

  FIXED WINDOW      "100 per minute"
      simple, and it allows a 2x BURST across the boundary:
      100 at 11:59:59 and 100 at 12:00:00 = 200 in one second.

  SLIDING WINDOW    counts the last 60 seconds continuously
      no boundary burst. Costs more state.

  TOKEN BUCKET      tokens refill at a steady rate; each request
      takes one. A full bucket allows a burst up to its size, then
      the sustained rate applies.
      -> the best fit for most APIs: bursty clients are normal, and
         this permits a burst without permitting sustained abuse.

  LEAKY BUCKET      requests queue and drain at a fixed rate
      smooths output completely. Adds latency instead of rejecting.
      -> for protecting a downstream that cannot absorb bursts at all.
```

``` csharp
// ASP.NET Core rate limiting - and the KEY is the important decision
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("per-tenant", ctx => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: ctx.User.FindFirst("tenant_id")?.Value ?? "anonymous",
        factory: _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,                       // burst size
            TokensPerPeriod = 20,                   // sustained rate
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,                         // reject, do not queue
        }));

    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        ctx.HttpContext.Response.Headers.RetryAfter = "1";   // TELL THE CLIENT
        await ctx.HttpContext.Response.WriteAsync("Rate limit exceeded", ct);
    };
});
```

| Key on              | Protects against          | Weakness                                             |
|---------------------|---------------------------|------------------------------------------------------|
| IP address          | Naive scrapers            | **Breaks behind NAT** — a whole office shares one IP |
| API key or tenant   | A noisy customer          | Requires authentication first                        |
| User id             | One user's runaway client | Same                                                 |
| Endpoint + key      | One expensive endpoint    | More state                                           |
| **Cost, not count** | Expensive queries         | Requires estimating cost up front                    |
| Global              | Total overload            | Punishes everyone for one abuser                     |

Two details that decide whether rate limiting helps or annoys. **Return the headers** — `RateLimit-Limit`, `RateLimit-Remaining`, `Retry-After` — because a client that cannot see its budget cannot back off correctly, and will simply retry immediately. And **rate limiting is not DDoS protection**: by the time a flood reaches your application, you have already paid to receive and parse it. Volumetric attacks are absorbed at the edge, by a CDN or scrubbing provider.

### Networking for Backend Systems

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Networking for Backend Systems</p>

<p class="pd">Networking as it applies once you have more than one service: discovery, segmentation, private connectivity, and the latency consequences of where things sit.</p>

<a id="be-g675"></a>

#### Service discovery

How does service A find service B when instances come and go? Four answers, in increasing sophistication.

| Mechanism                | How                                   | Fits                                           |
|--------------------------|---------------------------------------|------------------------------------------------|
| **Static configuration** | Hostnames in config                   | Few, stable services                           |
| **DNS**                  | A name resolving to healthy instances | The default. Kubernetes Services work this way |
| **Load balancer VIP**    | One stable address in front of a pool | Simple and effective                           |
| **Registry**             | Services register; clients query      | Consul, Eureka — richer health and metadata    |
| **Service mesh**         | A sidecar proxy handles it entirely   | Large fleets; adds mTLS and policy             |

``` text
# Kubernetes: DNS-based, and the naming convention is worth memorising
orders                                  # same namespace
orders.production                       # explicit namespace
orders.production.svc.cluster.local     # fully qualified

# a normal Service gives you one stable virtual IP, load-balanced by kube-proxy.
# a HEADLESS service (clusterIP: None) returns every pod IP instead - which is
# what StatefulSets use, so each database replica is individually addressable:
postgres-0.postgres.production.svc.cluster.local
```

> **\[Trap\]**
>
> **Common trap.** Clients that resolve DNS once and cache the result forever will not follow instances changing. This affects connection pools, HTTP clients with keep-alive, and some runtimes by default. If discovery depends on DNS, verify that your clients actually re-resolve — and prefer a proxy or mesh where the re-resolution is not your application's responsibility.

###### How a name becomes an address, in each model

``` text
  the same question - "where is the orders service?" - answered four ways

  STATIC CONFIG        orders.internal:8080 in a config file
      + trivial, no infrastructure
      - every change is a deploy; no health awareness

  DNS                  orders.svc.cluster.local -> A records
      + universal; every language and library already speaks it
      - CLIENT-SIDE CACHING is the problem. A pooled connection never
        re-resolves, so a scaled-down instance keeps receiving traffic.
      - no health awareness beyond record removal, which is delayed
        by TTL AND by client caching

  LOAD BALANCER VIP    one stable address in front of a pool
      + health checking is the LB's job; clients need nothing
      + this is what Kubernetes Services actually are (a virtual IP
        plus iptables/IPVS rules on every node)
      - an extra hop

  REGISTRY             Consul, Eureka; instances register and heartbeat
      + rich health, metadata, weighting
      - a registry to operate, and it becomes critical infrastructure
```

``` csharp
// .NET's own abstraction, which resolves differently per environment
builder.Services.AddServiceDiscovery();
builder.Services.AddHttpClient<IBillingClient, BillingClient>(c =>
    c.BaseAddress = new Uri("https+http://billing-api"));
//                          ^^^^^^^^^^^^ prefer https, fall back to http
//                                       ^^^^^^^^^^^ the SERVICE NAME

// locally under Aspire  -> reads services__billing-api__https__0 env vars
// in Kubernetes         -> resolves billing-api via cluster DNS
// with static config    -> reads the Services: section
// the application code is identical in all three.
```

| Failure                               | Cause                                              |
|---------------------------------------|----------------------------------------------------|
| Traffic to a terminated instance      | DNS TTL plus **client-side connection pooling**    |
| Uneven load across instances          | The client resolved once and pinned one address    |
| Works in one namespace, not another   | Short name resolution depends on the search domain |
| Slow first request only               | Resolution plus TLS on a cold connection           |
| A scaled-up instance receives nothing | Existing pooled connections are never re-resolved  |

The recurring theme: **service discovery usually fails on the client side, not the server side**. The registry is correct, DNS is correct, and the client is holding a connection opened before the change. Bound connection lifetime is the fix in every language — in .NET, `PooledConnectionLifetime` or `IHttpClientFactory`'s handler rotation.

<a id="be-g676"></a>

#### Network segmentation and firewalls

``` text
  defence in depth - each layer assumes the one outside it failed

  internet
    |
  WAF / DDoS protection          filters known attack patterns
    |
  load balancer                  the ONLY thing in a public subnet
    |  security group: 443 from 0.0.0.0/0
    |
  app tier (private subnet)
    |  security group: 8080 FROM THE LOAD BALANCER'S GROUP ONLY
    |
  data tier (private subnet)
       security group: 5432 FROM THE APP TIER'S GROUP ONLY
       no route to an internet gateway at all

  the pattern that matters: rules reference OTHER SECURITY GROUPS,
  not IP ranges. Instances scale and change addresses; group membership
  does not. An IP-based rule set rots; a group-based one does not.
```

| Control                           | Operates at      | Stateful                                | Note                                                             |
|-----------------------------------|------------------|-----------------------------------------|------------------------------------------------------------------|
| Security group                    | Instance / ENI   | **Yes** — replies allowed automatically | Allow rules only. Your primary control                           |
| Network ACL                       | Subnet           | No — you must allow both directions     | Allow and deny. Use sparingly; easy to misconfigure              |
| Host firewall (`nftables`, `ufw`) | The machine      | Yes                                     | Defence in depth on self-managed hosts                           |
| Kubernetes NetworkPolicy          | Pod              | Depends on CNI                          | Default is *all pods can reach all pods* — restrict deliberately |
| Service mesh policy               | Service identity | —                                       | Identity-based rather than address-based                         |

``` text
# Kubernetes default-deny, then allow what is needed. Without this,
# any compromised pod can reach every service in the cluster.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: default-deny, namespace: production }
spec:
  podSelector: {}
  policyTypes: [Ingress, Egress]
---
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: api-to-db, namespace: production }
spec:
  podSelector: { matchLabels: { app: postgres } }
  ingress:
  - from: [{ podSelector: { matchLabels: { app: api } } }]
    ports: [{ protocol: TCP, port: 5432 }]
```

- **Egress matters as much as ingress.** Restricting outbound traffic is what limits data exfiltration and stops a compromised host reaching a command-and-control server.
- **Deny by default**, then allow specific paths. An allow-by-default posture cannot be audited.
- **Reference identities, not addresses**, wherever the platform supports it.
- **Document why each rule exists.** Undocumented rules are never removed, because nobody dares.
- **Log denied traffic.** It tells you both about attacks and about the legitimate path you forgot to allow.

<a id="be-g677"></a>

#### Private connectivity and egress

| Mechanism                         | Connects                    | Note                                                    |
|-----------------------------------|-----------------------------|---------------------------------------------------------|
| **VPC peering**                   | Two networks, directly      | Non-transitive; CIDRs must not overlap                  |
| **Transit gateway** / hub         | Many networks               | Scales past the peering mesh problem                    |
| **Private Link** / endpoint       | Your network to a *service* | Traffic never traverses the internet or the NAT gateway |
| **VPN**                           | On-premises to cloud        | Over the internet, encrypted. Cheap, variable latency   |
| **Direct Connect** / ExpressRoute | On-premises to cloud        | A dedicated circuit. Predictable, expensive             |

``` text
  why Private Link matters beyond security

  WITHOUT an endpoint:
    private instance -> NAT gateway -> internet -> S3
      metered NAT data processing charges on every byte
      traffic leaves the provider network
      requires the NAT gateway to be available

  WITH a VPC endpoint:
    private instance -> endpoint -> S3
      no NAT charges
      never leaves the provider backbone
      works even with no internet route at all

  for a workload pulling container images or writing backups to object
  storage, this is frequently the largest single line on the network bill.
```

``` text
# find out what your egress actually is before optimising it
# - container image pulls on every deploy, across every instance
# - package updates
# - outbound API calls
# - cross-AZ traffic between your OWN services (charged in most clouds!)
# - log and metric shipping
#
# the cross-AZ one surprises people: a chatty service whose instances are
# spread across three availability zones pays inter-AZ transfer on most
# internal calls. Zone-aware routing, where available, removes it.
```

###### Controlling what leaves, and why it is the control people skip

``` text
  INGRESS is what everybody restricts. EGRESS is what almost nobody does -
  and egress is what an attacker needs after a compromise.

    with unrestricted egress, a compromised container can:
      exfiltrate your database, to anywhere
      fetch a second-stage payload
      join a botnet or mine cryptocurrency
      reach the CLOUD METADATA ENDPOINT and steal the instance's
        credentials -> which are often far more privileged than the app

  the metadata endpoint deserves specific attention:

    http://169.254.169.254/           AWS, Azure, GCP - link-local
      returns temporary credentials for the instance's role.
      an SSRF bug in your application becomes full credential theft.

    defences, and use all of them
      IMDSv2 / required token         AWS - makes SSRF much harder
      block 169.254.169.254 at the pod/container network level
      never grant the NODE role permissions the WORKLOAD needs -
        use IRSA / workload identity so pods get their own identity
```

``` text
# a default-deny egress policy in Kubernetes, allowing only what is needed
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: orders-egress }
spec:
  podSelector: { matchLabels: { app: orders } }
  policyTypes: [Egress]
  egress:
    - to: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: kube-system } } }]
      ports: [{ protocol: UDP, port: 53 }]        # DNS. Forgetting this
                                                   # breaks EVERYTHING, and
                                                   # the symptom is confusing.
    - to: [{ podSelector: { matchLabels: { app: postgres } } }]
      ports: [{ protocol: TCP, port: 5432 }]
    - to: [{ ipBlock: { cidr: 0.0.0.0/0, except: [169.254.169.254/32,
                                                  10.0.0.0/8] } }]
      ports: [{ protocol: TCP, port: 443 }]        # outbound HTTPS only,
                                                   # never to metadata or
                                                   # to internal ranges
```

| Egress control                     | Stops                                            |
|------------------------------------|--------------------------------------------------|
| Default-deny network policy        | Lateral movement and exfiltration                |
| Blocking the metadata IP           | SSRF → credential theft                          |
| An egress proxy with an allow-list | Unapproved destinations, with an audit log       |
| VPC / private endpoints            | Traffic to managed services leaving the backbone |
| No NAT gateway at all              | Any internet access from that subnet             |
| DNS filtering                      | Known-bad domains, and DNS tunnelling            |

> **\[Note\]**
>
> **The pragmatic first step.** Full default-deny egress is a project. Blocking `169.254.169.254` from application workloads is one rule, takes minutes, and removes the single highest-value escalation path from an SSRF bug. Do that first, then work toward allow-listing.

<a id="be-g678"></a>

#### Latency, regions and placement

``` text
  round-trip times, to reason with

    same host (loopback)          ~0.05 ms
    same rack / AZ                ~0.2-0.5 ms
    cross-AZ, same region         ~0.5-2 ms
    cross-region, same continent  ~10-40 ms
    intercontinental              ~80-250 ms

  the consequence for design:

    a request making 10 SEQUENTIAL calls
      all same-AZ            ~5 ms of network
      app in EU, DB in US    ~1,000 ms of network

  and the rule that follows:
    KEEP COMPUTE NEXT TO ITS DATA. Moving the application to the edge
    while the database stays in one region makes things WORSE, because
    every query now crosses an ocean.
```

| Pattern                         | Works when                                  | Fails when                                                                    |
|---------------------------------|---------------------------------------------|-------------------------------------------------------------------------------|
| Single region                   | Most systems, honestly                      | Regulatory residency, or a global user base with strict latency needs         |
| Multi-region read replicas      | Read-heavy, tolerant of staleness           | Writes must be local too                                                      |
| Active-active                   | Data partitions cleanly by region           | One dataset written from everywhere — conflict resolution is now your problem |
| Edge compute + central database | Static content, or work needing no database | Anything database-backed — latency gets worse                                 |
| Edge compute + edge data        | Read-mostly, replicated data                | Strong consistency requirements                                               |

> **\[Note\]**
>
> **Start in one region.** Multi-region is frequently proposed for availability and delivers complexity instead. Multiple availability zones within one region already protect against the failure mode that actually occurs — a datacentre problem — at a small fraction of the cost. Go multi-region when you have a stated requirement: data residency law, a genuine RTO that a region failure would breach, or users on another continent whose latency you have measured and cannot accept.

###### Where the time actually goes, measured

``` bash
# stop guessing which phase is slow. curl reports every one of them.
cat > curl-format.txt <<'EOF'
     dns:  %{time_namelookup}s
 connect:  %{time_connect}s
     tls:  %{time_appconnect}s
    ttfb:  %{time_starttransfer}s
   total:  %{time_total}s
EOF
curl -w '@curl-format.txt' -o /dev/null -s https://api.example.com/orders

#   dns high        -> resolver, or a cold cache
#   connect - dns   -> TCP handshake: network RTT
#   tls - connect   -> handshake: an extra RTT, plus certificate work
#   ttfb - tls      -> YOUR APPLICATION. This is the only part you control
#                      by optimising code.
#   total - ttfb    -> response transfer: payload size and bandwidth
#
# the frequent surprise: ttfb-tls is 15ms and total is 900ms, because the
# response is 4 MB. No amount of query tuning fixes that; pagination does.
```

| Latency budget item    | Typical          | Reduce by                             |
|------------------------|------------------|---------------------------------------|
| DNS                    | 0–50 ms          | Caching; longer TTLs; a warm resolver |
| TCP handshake          | 1 RTT            | Connection reuse                      |
| TLS handshake          | 1 RTT (TLS 1.3)  | Session resumption; 0-RTT where safe  |
| Application processing | Yours            | The rest of these guides              |
| Response transfer      | Size / bandwidth | Compression, pagination, fewer fields |
| Client rendering       | Yours            | The frontend guide                    |

Two useful rules of thumb. First, a request crossing a region boundary pays that RTT for *every* round trip, so ten sequential calls cost ten times the distance — batch or parallelise them. Second, compression is nearly always worth it above about 1 KB: the CPU cost is small and the transfer saving is large, particularly for JSON, which compresses roughly 8–10×.

### Remote Access & Operations

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Remote Access & Operations</p>

<p class="pd">Reaching and operating machines you cannot walk up to. SSH properly, bastion patterns, and running work across many hosts without inventing a fragile script.</p>

<a id="be-g661"></a>

#### Bastions, VPNs and the shape of access

``` text
  three access models, oldest to newest

  1  BASTION / JUMP HOST
       internet -> bastion (public, hardened, audited) -> private hosts
       one auditable entry point; the bastion itself is a target
       and a single point of failure

  2  VPN
       laptop -> VPN -> you are "inside" the network
       simple mental model; the weakness is that being inside grants
       broad network reach - one compromised laptop sees everything

  3  ZERO TRUST / IDENTITY-AWARE PROXY
       every connection authenticated and authorised individually,
       per user AND per resource, regardless of network position
       -> AWS SSM Session Manager, GCP IAP, Cloudflare Access,
          Teleport, Tailscale
       -> no inbound ports at all; the host dials OUT to the control plane
```

| Model                | Inbound ports                         | Audit                        | Revocation                         |
|----------------------|---------------------------------------|------------------------------|------------------------------------|
| Bastion + keys       | 22 open to the world or an allow-list | Per-host logs                | Edit files on every host           |
| Bastion + SSH CA     | 22, restricted                        | Central issuance log         | Stop issuing — certificates expire |
| VPN                  | VPN port                              | Connection-level             | Central, immediate                 |
| Identity-aware proxy | **None**                              | **Per-session, per-command** | Central, immediate                 |

``` bash
# the modern default in AWS: no bastion, no SSH port, no keys
aws ssm start-session --target i-0abc123
aws ssm start-session --target i-0abc123 \
  --document-name AWS-StartPortForwardingSessionToRemoteHost \
  --parameters '{"host":["mydb.rds.amazonaws.com"],"portNumber":["5432"],"localPortNumber":["15432"]}'
# access is IAM-controlled, every session is logged to CloudTrail,
# and the instance has no inbound rules at all.

# Azure: az ssh vm / Bastion.  GCP: gcloud compute ssh with IAP.
```

- **Nobody should have standing production access.** Request it, get it for an hour, have it expire automatically.
- **Every session should be attributable to a person**, not to a shared `appuser` account — use per-person identities that `sudo` where needed.
- **Record what was done**, not merely that a session occurred. Session recording is standard in the identity-aware tools.
- **Break-glass access must exist**, be documented, be tested, and be loudly alerted on when used.
- **Prefer no access at all.** If dashboards, logs and traces answer the question, nobody needs to log in — and that is the real goal.

### APIs & Integration

<p class="src">From <em>Backend Engineering</em> · C# & .NET · APIs & Integration</p>

<p class="pd">Getting your service to talk to other services and other services to talk to yours — over HTTP, over gRPC, over a message broker, and over the awkward legacy protocols nobody chose.</p>

<a id="be-a152"></a>

#### API gateways compared

YARP is the .NET-native option and the natural choice for a strangler-fig facade. It is not the only one, and the trade-offs differ enough to matter.

| Gateway                    | Nature                                  | Strengths                                                                                       | Weaknesses                                                   |
|----------------------------|-----------------------------------------|-------------------------------------------------------------------------------------------------|--------------------------------------------------------------|
| **YARP**                   | A library you host in ASP.NET Core      | Full C# extensibility; config from any provider; ideal for legacy routing and gradual migration | You operate it; no managed control plane or portal           |
| **Ocelot**                 | .NET library, config-driven             | Simple, mature, small                                                                           | Less actively developed; fewer features than YARP            |
| **Azure API Management**   | Managed product                         | Developer portal, subscriptions, quotas, policy engine, monetisation                            | Cost; policy XML is a language of its own; slower to iterate |
| **Kong / Tyk**             | Standalone, plugin-based                | Rich plugin ecosystem, multi-cloud, high performance                                            | Another platform to operate; plugins often in Lua or Go      |
| **Envoy / Istio ingress**  | Proxy, config as CRDs                   | Extremely capable L7 routing, mTLS, observability                                               | Steep learning curve; xDS configuration is not friendly      |
| **Kubernetes Gateway API** | Standard CRDs, multiple implementations | Vendor-neutral, the successor to Ingress, per-route RBAC                                        | Implementation-dependent feature coverage                    |
| **Cloud L7 LB**            | Front Door, ALB, Cloud Load Balancing   | Global anycast, WAF, DDoS protection, TLS at the edge                                           | Coarse routing; not a full gateway                           |

``` csharp
// YARP's real advantage: the routing decision can be arbitrary C#
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        // propagate identity to the legacy app in the form it understands
        ctx.AddRequestTransform(async t =>
        {
            var user = t.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
                t.ProxyRequest.Headers.Add("X-Legacy-User", user.FindFirst("legacy_id")!.Value);
            await ValueTask.CompletedTask;
        });

        // and rewrite a legacy response shape into the new contract
        ctx.AddResponseTransform(async t =>
        {
            if (t.ProxyResponse?.Content.Headers.ContentType?.MediaType != "application/json") return;
            var body = await t.ProxyResponse.Content.ReadAsStringAsync();
            var rewritten = LegacyResponseTranslator.ToModern(body);
            t.SuppressResponseBody = true;
            await t.HttpContext.Response.WriteAsync(rewritten);
        });
    });
```

``` csharp
// dynamic destinations: route by cohort, tenant, or a feature flag, with no redeploy
public sealed class CanaryConfigProvider(IFeatureManager features) : IProxyConfigProvider
{
    // returning a new IProxyConfig triggers a hot reload of routes and clusters
    public IProxyConfig GetConfig() => new Config(BuildRoutes(), BuildClusters());
}
```

> **\[Note\]**
>
> **Layer them, do not choose one.** The common production shape is a cloud L7 load balancer at the edge for TLS, WAF and DDoS; an in-cluster gateway (YARP, Envoy, Gateway API) for routing and per-route policy; and nothing in either that contains business logic. Trying to make one component do all three is what turns a gateway into the shared bottleneck every team must change.

###### What belongs in the gateway, and what does not

| Gateway                              | Model                        | Fits                                                          |
|--------------------------------------|------------------------------|---------------------------------------------------------------|
| **YARP**                             | A .NET library you host      | Reverse proxy in .NET; strangler-fig migrations; full control |
| **nginx**                            | Config-driven proxy          | Simple routing, TLS, static content                           |
| **Envoy**                            | Dynamically configured proxy | Service mesh data plane, modern ingress                       |
| **Kong** / **Tyk**                   | Full API management          | Plugins, key management, developer portal                     |
| **Azure APIM** / **AWS API Gateway** | Managed API management       | Cloud estates; policy without infrastructure                  |
| **Cloud load balancer**              | L7 routing                   | Often enough. Do not add a gateway you do not need            |

``` text
  WHAT BELONGS WHERE - the decision that keeps a gateway maintainable

  IN THE GATEWAY                  IN THE SERVICE
    TLS termination                 business authorisation
    routing by host and path        domain validation
    rate limiting (coarse)          rate limiting (per-tenant, per-feature)
    authentication (token           anything needing domain data
      validation, not authz)        response shaping for one client
    request size limits             retries to ITS dependencies
    IP allow/deny lists
    CORS
    coarse caching
    observability injection

  the failure mode: business logic creeping into gateway policy.
  it starts as one transformation, and ends as an undebuggable,
  untestable, unversioned second application written in YAML that
  nobody owns and no test covers.
```

``` text
// YARP as a strangler-fig proxy - the highest-value use for a .NET team
{
  "ReverseProxy": {
    "Routes": {
      "orders-new": {
        "ClusterId": "new",
        "Match": { "Path": "/orders/{**rest}" },
        "Transforms": [
          { "RequestHeader": "X-Forwarded-Prefix", "Set": "/orders" },
          { "RequestHeadersCopy": "true" }
        ]
      },
      "everything-else": {
        "ClusterId": "legacy",
        "Match": { "Path": "{**catch-all}" },
        "Order": 100
      }
    },
    "Clusters": {
      "new":    { "Destinations": { "d1": { "Address": "http://orders-api:8080/" } },
                  "HealthCheck": { "Active": { "Enabled": true, "Path": "/health/ready" } } },
      "legacy": { "Destinations": { "d1": { "Address": "http://legacy-iis/" } } }
    }
  }
}
// moving a route is a CONFIGURATION change, reversible in seconds.
// that reversibility is what makes each migration increment genuinely safe.
```

> **\[Trap\]**
>
> **Common trap.** A gateway is a single point of failure and a single point of deployment coupling. Every team now waits on one config repository, and one bad route rule affects everything. Keep gateway configuration in version control with review, deploy it through a pipeline like any other artefact, test routes in a staging environment, and resist the temptation to let it become the place where cross-team problems are solved.
