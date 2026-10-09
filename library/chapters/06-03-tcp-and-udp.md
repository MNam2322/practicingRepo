---
id: "6-3"
number: "6.3"
title: "TCP and UDP"
part: 6
part_title: "Networks"
words: 7253
minutes: 32
---

# 6.3 TCP and UDP

> **Quick look.** TCP gives a reliable ordered byte stream; UDP sends fast datagrams with no guarantees.
>
> - Ports identify programs; a connection is identified by both ends' addresses and ports.
> - TCP carries bytes, not messages, so every protocol on top needs framing; one read is not one message.
> - Each connection costs a handshake round trip and starts slowly, which is why connection pools exist.
> - "Connection refused" means nothing is listening; a timeout means something swallowed the packets.
> - UDP fits when late data is worthless (video, metrics, game state); QUIC builds HTTP/3 on it.
> - Round-trip time sets a floor: under 1 ms in a datacentre, 150–250 ms between continents.


### The big picture

IP gets packets from one machine to another, but only on a best-effort basis. Packets can be lost, duplicated, delayed or arrive in the wrong order, and IP doesn't say which program on the machine they are for. The transport layer fixes both problems.

It offers two very different services. UDP is like sending postcards: each message travels alone, quickly, with no promise it arrives. TCP is like a phone call: you set up a connection first, and then everything you say arrives, complete and in order, or you are told the line went dead. Almost everything you build, from database connections to web APIs, runs on TCP. This chapter explains how TCP turns unreliable packets into a reliable stream, what that costs, and the mistakes that come from misunderstanding it, such as assuming each send arrives as one neat message.

### Before you start

**Builds on:** 1.7 The operating system, 6.1 Layers and packets, 5.4 The event loop, 5.3 Async and await, 6.2 IP addresses and routing.

**Ideas explained later:**

- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Client** (8.1): A program that sends requests to a server.
- **Timeout** (12.5): A limit on how long to wait for an operation before giving up.
- **DNS** (6.4): Domain Name System: the distributed directory that turns names into addresses and other records.
- **QUIC** (6.6): A transport protocol on UDP with independent streams and a combined connection and TLS handshake.
- **HTTP/3** (6.6): HTTP carried over QUIC.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.

**Tools and technologies named here:**

- **SQL Server** (7.2): Microsoft's relational database product.
- **PostgreSQL** (7.2): A widely used free, open-source relational database.
- **Query** (7.2): A request for data, most often sent to a database in SQL. LINQ queries ask the same kind of question of data in memory.

**The code uses:** variables and var, methods and local functions, string interpolation, LINQ, tuples, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is a port?**

> A 16-bit number, 0 to 65535, that identifies which program on a machine a packet is for. A web server listens on port 443, SQL Server on 1433 and PostgreSQL on 5432.

**2. What is the three-way handshake, and what does it cost?**

> The exchange that opens a TCP connection: the client sends SYN, the server replies SYN-ACK, the client sends ACK. It costs one full round trip before any data can be sent.

**3. You send two messages on a TCP connection. Can the other side receive them in one read?**

> Yes. TCP delivers a stream of bytes with no message boundaries. Two writes can arrive in one read, and one write can arrive in several. The application must mark where each message ends, for example with a length prefix.

**4. What is the difference between "connection refused" and a connection timing out?**

> Refused means the machine answered and said nothing is listening on that port. A timeout means no answer came at all, usually because a firewall dropped the packets, the machine is down or the address is wrong.

**5. Why do new TCP connections start slowly?**

> Congestion control. TCP doesn't know how much the network can take, so it starts with a small amount of data in flight and increases it as acknowledgements come back. This is called slow start, and it is one reason reusing connections is faster.

**6. When is UDP a better choice than TCP?**

> When late data is useless and losing a little is acceptable, such as live voice, video and games, or when messages are tiny and a retry is cheap, such as DNS queries. QUIC, used by HTTP/3, also builds its own reliability on top of UDP.

**7. What is TIME_WAIT, and why does it matter?**

> After closing a connection, the side that closed first keeps its entry for up to a few minutes, so late packets from the old connection can't be confused with a new one. Code that opens and closes huge numbers of connections can run out of local ports this way.

### From machines to programs

An IP address gets a packet to a machine. But a machine runs many programs that use the network at once: a browser, a mail client, a database, a chat app. The transport layer adds a **port** number to tell them apart. A port is a 16-bit number, from 0 to 65535.

A server program claims a port and waits there for incoming traffic. That is called listening. Well-known services use standard ports: 80 for HTTP, 443 for HTTPS, 22 for SSH, 53 for DNS, 1433 for SQL Server, 5432 for PostgreSQL. A client program usually gets a temporary port picked by the operating system for each connection, called an **ephemeral port**, from a range of tens of thousands, as The operating system (1.7) mentioned.

An address and a port together form an **endpoint**, such as `10.0.0.5:5432`. A connection is identified by four values together: the client's address and port, and the server's address and port. That is how one server port can hold thousands of connections at once: each has a different client endpoint.

A **socket** is the operating system's handle for one endpoint of network communication. A program creates a socket, connects it or binds it to a port, and then reads and writes through it, much like a file. Every network library, including .NET's `HttpClient`, is built on sockets.

### UDP: postcards

**UDP**, the User Datagram Protocol, is the thin option. It adds an 8-byte header to each IP packet with the two port numbers, a length and a checksum, as the example in Layers and packets (6.1) built by hand. Each message is a separate **datagram**.

UDP makes no promises. A datagram may be lost, duplicated or delivered out of order, and nobody is told. There is no connection to set up, so the first datagram goes immediately. What UDP does guarantee is boundaries: each datagram arrives whole or not at all.

That suits traffic where speed matters more than completeness. In a voice call, a lost packet means a tiny glitch, while waiting to resend it would mean an unbearable delay. Games send position updates many times a second, so a lost one is replaced by the next. DNS lookups, covered in DNS (6.4), are one small question and one small answer, and the client simply asks again if no answer comes.

### TCP: a reliable stream

**TCP**, the Transmission Control Protocol, gives programs something much stronger: a reliable, ordered **byte stream** between two endpoints. What one side writes, the other side reads, every byte, in the same order, with no gaps and no duplicates. If that becomes impossible, both sides are told the connection has failed.

TCP builds this on top of unreliable packets with a few mechanisms.

Every byte in the stream has a **sequence number**, its position counted from the start. Each TCP segment's header says which bytes it carries.

The receiver sends back an **acknowledgement**, an ACK, saying how much of the stream it has received so far, without gaps.

If an acknowledgement doesn't come back in time, the sender assumes the data was lost and sends it again. That is **retransmission**. The waiting time adapts to the measured round-trip time.

The receiver uses the sequence numbers to put segments back in order, drop duplicates and hold data until any gap before it is filled. Only then does it hand bytes to the program.

### Opening a connection

A TCP connection starts with the **three-way handshake**. The client sends a segment with the SYN flag, for "synchronize", and its starting sequence number. The server replies with SYN-ACK: its own starting number, and an acknowledgement of the client's. The client sends ACK. Both sides now agree on where each stream starts, and data can flow.

That costs one full round trip before the first byte of real data. To a server 100 ms away, every new connection starts with 100 ms of nothing. TLS usually adds another round trip on top, as TLS (6.5) explains. This is the first reason to reuse connections.

The handshake also explains two different failures. If the server machine is reachable but nothing listens on the port, its operating system answers the SYN with a reset, RST, and the client immediately gets "connection refused". If there is no answer at all, because a firewall silently drops the packets, the machine is down or the address is wrong, the client retries the SYN a few times and eventually times out. That can take 20 seconds or more. "Refused" is quick and tells you something. "Timed out" is slow and means something between you and the server is swallowing packets.

### Streams, not messages

This is the most important thing to understand about TCP as a programmer: it carries bytes, not messages. It has no idea where one of your messages ends and the next begins.

Two small writes can arrive together in one read. One large write can arrive split across several reads. Both are normal. Code that assumes each `Read` returns exactly one complete message works in testing, where messages are small and the network is quiet, and fails in production.

So every protocol built on TCP defines its own **message framing**: a way to tell where each message ends. There are two common approaches. A length prefix writes the size of each message before it, so the reader knows exactly how many bytes to collect. A delimiter ends each message with a marker that can't appear inside it, such as a newline. HTTP uses both: headers end at a blank line, and the body length is given in a header.

The program below shows the problem and the fix. It runs a TCP server and client on the same machine.

```csharp
// tcp.cs — run with: dotnet run tcp.cs
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

// A server on this machine, on a free port the operating system picks
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
Console.WriteLine($"listening on 127.0.0.1:{port}");

var server = Task.Run(async () =>
{
    using TcpClient connection = await listener.AcceptTcpClientAsync();   // waits for a client to connect
    NetworkStream stream = connection.GetStream();

    await Task.Delay(200);                                  // let both of the client's first writes arrive
    var buffer = new byte[1024];
    int n = await stream.ReadAsync(buffer);                 // read whatever has arrived so far
    Console.WriteLine($"one read got {n} bytes: \"{Encoding.UTF8.GetString(buffer, 0, n)}\"");

    for (int i = 0; i < 3; i++)                             // now read framed messages properly
        Console.WriteLine($"message: \"{await ReadMessage(stream)}\"");
});

using (var client = new TcpClient())
{
    await client.ConnectAsync(IPAddress.Loopback, port);   // the three-way handshake happens here
    NetworkStream stream = client.GetStream();
    await stream.WriteAsync("BA117 boarding"u8.ToArray()); // two separate writes...
    await stream.WriteAsync("QF1 delayed"u8.ToArray());
    await Task.Delay(400);
    foreach (string text in new[] { "gate B12", "gate C3", "final call" })
        await WriteMessage(stream, text);
    await server;
}
// one read got 25 bytes: "BA117 boardingQF1 delayed"    ...arrived as one run of bytes
// message: "gate B12"
// message: "gate C3"
// message: "final call"

// Nothing listening any more: the other side refuses at once
listener.Stop();
try { using var late = new TcpClient(); await late.ConnectAsync(IPAddress.Loopback, port); }
catch (SocketException ex) { Console.WriteLine(ex.SocketErrorCode); }   // ConnectionRefused

// UDP keeps each datagram separate, but promises nothing else
using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var sender = new UdpClient();
var target = (IPEndPoint)receiver.Client.LocalEndPoint!;
await sender.SendAsync("BA117 boarding"u8.ToArray(), target);
await sender.SendAsync("QF1 delayed"u8.ToArray(), target);
for (int i = 0; i < 2; i++)
{
    UdpReceiveResult datagram = await receiver.ReceiveAsync();
    Console.WriteLine($"datagram: \"{Encoding.UTF8.GetString(datagram.Buffer)}\"");
}
// datagram: "BA117 boarding"
// datagram: "QF1 delayed"

static async Task WriteMessage(NetworkStream stream, string text)
{
    byte[] body = Encoding.UTF8.GetBytes(text);
    byte[] length = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(length, body.Length);   // a 4-byte length, in network byte order
    await stream.WriteAsync(length);
    await stream.WriteAsync(body);
}

static async Task<string> ReadMessage(NetworkStream stream)
{
    byte[] length = new byte[4];
    await stream.ReadExactlyAsync(length);                  // keeps reading until exactly 4 bytes have arrived
    byte[] body = new byte[BinaryPrimitives.ReadInt32BigEndian(length)];
    await stream.ReadExactlyAsync(body);
    return Encoding.UTF8.GetString(body);
}
```

`ReadExactlyAsync`, added in .NET 7, loops until it has the requested number of bytes. A plain `ReadAsync` may return fewer, and code that ignores its return value reads garbage.

### Flow control

A fast sender could overwhelm a slow receiver. TCP prevents that with **flow control**. In every acknowledgement, the receiver announces how much free buffer space it has, called its window. The sender never has more unacknowledged data in flight than that window allows. When the receiving program stops reading, the window shrinks to zero and the sender waits.

This is TCP's version of the backpressure described in The event loop (5.4). It flows all the way back: a slow database consumer makes TCP buffers fill, which makes the sending program's writes wait.

### Congestion control

A second limit protects the network in between. If every sender sent as fast as its receiver allowed, routers would be overwhelmed, drop packets, and every sender would retransmit, making things worse. In 1986 this happened for real, and parts of the early internet slowed to a thousandth of their capacity.

TCP's **congestion control** makes each sender feel out how much the network can carry. A new connection starts with only a small amount of data in flight, about ten packets, and roughly doubles it with each round trip while everything is acknowledged. This phase is called **slow start**. When packets start getting lost, the sender takes that as a sign of congestion and cuts back.

The consequence for software is that a fresh connection doesn't reach full speed straight away. Transferring a small file over a new connection to a distant server is dominated by round trips, not bandwidth. Warm, reused connections are faster.

### Closing a connection

A connection closes when each side sends a segment with the FIN flag, for "finish", and the other acknowledges it. Either side can also abort at once with RST.

The side that closes first then keeps a record of the connection in a state called **TIME_WAIT**, for up to a few minutes. That way, stray packets still in the network from the old connection can't be mistaken for part of a new one using the same ports. On a busy client that opens and closes huge numbers of short connections, those records pile up, and it can run out of ephemeral ports. That is the socket exhaustion from The operating system (1.7), caused by creating a new `HttpClient` for every request.

A connection can also die silently: a machine loses power, or a NAT router or firewall between the two drops its record of the connection after a period of silence. Neither end finds out until it next tries to send. **TCP keepalive** sends small probe packets on idle connections to detect this, and to keep middleboxes from forgetting the connection. Its operating system default is about two hours of silence before the first probe, far too long for most applications, so libraries often set their own shorter timeouts and heartbeats.

### Head-of-line blocking

TCP's ordering guarantee has a cost. If one segment is lost, every later byte waits at the receiver until it is resent, even data that has already arrived. Nothing behind the gap can be handed to the program. That is **head-of-line blocking**.

For one stream, that is exactly right. But when many independent requests share one TCP connection, as in HTTP/2, one lost packet stalls all of them. That is why HTTP/3 moved to QUIC, a new transport protocol built on UDP, which keeps independent streams separate. HTTP (6.6) covers it.

### Connections in .NET

In .NET, `Socket` is the low-level type, `TcpListener` and `TcpClient` wrap it for TCP, and `UdpClient` wraps it for UDP. `NetworkStream` lets you read and write a TCP connection like any other stream. Use the async methods everywhere, for the reasons in Async and await (5.3): a server holding thousands of connections can't afford a thread per connection.

Most code never touches sockets directly. `HttpClient`, database drivers and message clients manage them for you, and they all keep a **connection pool**: a set of open connections kept ready and reused for later requests, which skips the handshake, the TLS setup and slow start. That is why the rules are: create one `HttpClient`, or use `IHttpClientFactory`, and reuse it; and open a database connection late, close it early, and let the pool keep it warm.

### In short

Ports identify programs on a machine, and a connection is identified by both ends' addresses and ports. UDP sends independent datagrams fast, with no delivery guarantees. TCP builds a reliable, ordered byte stream from unreliable packets using sequence numbers, acknowledgements and retransmission. It carries bytes, not messages, so every protocol on top needs framing. A connection costs a handshake round trip and starts slowly, which is why connection pools reuse them. Refused means nothing is listening; a timeout means something swallowed the packets.

### What goes wrong

**Assuming one read is one message.** Messages get merged or split under load, and the parser breaks. Frame messages explicitly and read until each is complete.

**Ignoring the count from `Read`.** A read returns how many bytes it actually got, possibly fewer than asked for. Code that assumes a full buffer processes garbage. Use `ReadExactlyAsync` or check the count.

**A new connection per request.** Every request pays a handshake, often a TLS handshake and slow start, and closed connections pile up in TIME_WAIT until ports run out. Reuse `HttpClient` and rely on connection pools.

**No timeouts.** A connection to a silently dead server waits a very long time. Set connect and read timeouts, and pass cancellation tokens.

**Idle connections that died silently.** A pooled database connection unused for an hour has been dropped by a firewall, and the next query fails. Configure pools to test or retire idle connections, and use keepalives.

**Reading "connection refused" as a network problem.** The network is fine; nothing is listening on that port. Check that the service is running and bound to the right address, as IP addresses and routing (6.2) discussed.

**Using UDP and forgetting it is lossy.** A UDP-based feature works in the office and loses messages on mobile networks. Handle loss, reordering and duplicates, or use TCP.

### Cost model

Opening a TCP connection costs one round trip; adding TLS 1.3 costs one more. To a server 50 ms away, a new HTTPS connection costs about 100 ms before the request is even sent, while a pooled connection costs nothing extra. Slow start means the first round trips of a new connection carry only about 14 KB each, doubling after that. Each open connection costs a few kilobytes of kernel memory for buffers and state, so a server can hold tens of thousands. TCP and IP headers add 40 bytes to each packet.

### In other languages (optional)

**JavaScript and TypeScript.** Node.js has `net.createServer` and `net.connect` for TCP and `dgram` for UDP, event-based and non-blocking. Browsers can't open raw TCP or UDP connections; WebSockets give a message-framed, TCP-based channel instead, and WebTransport one based on QUIC.

**Python.** The `socket` module, with `asyncio` streams for non-blocking servers. The same framing rules apply: `recv` can return part of a message.

**Go.** `net.Listen` and `net.Dial` return connections that goroutines read with blocking calls, which the runtime turns into non-blocking I/O underneath.

**Databases.** Every database driver speaks its own framed protocol over TCP and keeps a connection pool, which is why pool size and idle timeouts appear in connection strings.

### Key terms

- **Port**: A 16-bit number that identifies which program on a machine a packet is for.
- **Ephemeral port**: A temporary port the operating system gives a client for one connection.
- **Endpoint**: An IP address and port together, such as 10.0.0.5:5432.
- **Socket**: The operating system's handle for one endpoint of network communication.
- **UDP**: User Datagram Protocol: independent messages with ports and no delivery guarantees.
- **Datagram**: One self-contained UDP message.
- **TCP**: Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **Byte stream**: A continuous sequence of bytes with no built-in message boundaries.
- **Sequence number**: The position of a byte in a TCP stream, used to order data and detect gaps.
- **Acknowledgement**: A message telling the sender how much of the stream has arrived. Often shortened to ACK.
- **Retransmission**: Sending data again when its acknowledgement doesn't arrive in time.
- **Three-way handshake**: The SYN, SYN-ACK, ACK exchange that opens a TCP connection.
- **Message framing**: A way to mark where each message ends in a byte stream, such as a length prefix or a delimiter.
- **Flow control**: The receiver limiting how much unacknowledged data the sender may have in flight.
- **Congestion control**: The sender limiting its rate to what the network in between can carry.
- **Slow start**: The phase where a new TCP connection starts small and doubles how much it sends each round trip.
- **TIME_WAIT**: The state a closed connection's record stays in for a while, on the side that closed first.
- **TCP keepalive**: Small probe packets on an idle connection that detect whether the other end is still there.
- **Head-of-line blocking**: Later data waiting behind one lost piece, because delivery must stay in order.
- **Connection pool**: A set of open connections kept ready and reused, skipping setup costs.

### Where this leads

The first thing most connections need is an address from a name, in DNS (6.4). Encrypting what TCP carries is in TLS (6.5). The protocol most connections carry, and its answer to head-of-line blocking, is in HTTP (6.6). Connection pooling for databases returns in ORMs and EF Core (7.7), and timeouts and retries across services in Reliability (12.5).

### Check yourself

1. Remove the `Task.Delay(200)` from the server in `tcp.cs` and run it several times. Explain why the first read now gets different amounts.
2. Change the framing in `tcp.cs` from a length prefix to newline-delimited messages. Decide what should happen if a message itself contains a newline.
3. Try to connect to a port on your machine that nothing listens on, then to an address like `10.255.255.1`, which usually doesn't answer. Time both failures.
4. Write a UDP sender that sends 10,000 numbered datagrams as fast as it can, and a receiver that counts how many arrive and whether any are out of order.
5. In a real app, find where `HttpClient` instances are created. Check whether they are reused, and whether they have timeouts.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Messages split in half over TCP

A custom socket protocol occasionally fails to parse messages under load.

**What goes wrong.** TCP is a byte stream: one Read can return half a message or two messages.

*Length-prefixed framing*

```csharp
static async Task<byte[]> ReadMessageAsync(Stream s, CancellationToken ct)
{
    var header = new byte[4];
    await s.ReadExactlyAsync(header, ct);                              // loops until 4 bytes arrive
    int length = BinaryPrimitives.ReadInt32BigEndian(header);
    if (length is < 0 or > 1_048_576) throw new InvalidDataException("Bad frame length");
    var body = new byte[length];
    await s.ReadExactlyAsync(body, ct);
    return body;
}
```

> **Lesson:** Every protocol on TCP needs framing. Never assume one read is one message.

#### Example 2: "Refused" versus "timed out"

A deploy fails health checks. Some logs say refused, others say timed out, and the team argues about the cause.

*Read the error precisely*

```bash
nc -vz orders-db 5432
# Connection refused  -> the host answered: nothing listens on that port (wrong port, app down)
# timed out           -> packets vanished: firewall, security group, routing, or host down
# Name or service not known -> DNS, not the network
```

> **Lesson:** Refused, timed out and unresolved point to different layers. Read the exact error before guessing.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · APIs · Ground Floor</p>

<a id="be-e491"></a>

#### Ports and sockets

An IP address identifies a *machine*. But a machine runs many programs. A **port** — a 16-bit number, 0–65535 — identifies which program.

Conventions: 80 = HTTP, 443 = HTTPS, 22 = SSH, 5432 = PostgreSQL, 6379 = Redis.

A **socket** is one endpoint of a connection, identified by the four-tuple:

``` text
(source IP, source port, destination IP, destination port)
```

That tuple must be unique. Your browser opening three connections to the same server gets three different *source* ports. This is exactly why creating a new `HttpClient` per request causes socket exhaustion — each connection burns a source port, and closed ports sit in `TIME_WAIT` for up to four minutes before they can be reused. There are only ~28,000 usable ephemeral ports by default.

<a id="be-e493"></a>

#### The TCP handshake, and why connections are expensive

Opening a TCP connection takes a round trip:

``` text
Client  ── SYN ──────────>  Server
Client  <───── SYN-ACK ───  Server
Client  ── ACK ──────────>  Server        (1 RTT)
```

Add TLS and you pay more:

``` text
TLS 1.2:  2 more round trips
TLS 1.3:  1 more round trip
```

So over a 50 ms link, an HTTPS connection costs ~100–150 ms before your request begins. That's why **connection reuse is the single highest-leverage performance decision** in API clients. Keep-alive, connection pooling, HTTP/2 multiplexing all exist to amortize this.

Practical: `HttpClient` reused across requests reuses connections. Created per request, it doesn't — and you pay the handshake every time, on top of exhausting ports.

### Networking

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Networking</p>

<p class="pd">The wire. Most production incidents that look like application bugs are really DNS, TLS, timeouts, connection pools or a proxy in the middle — so this is the layer worth genuinely understanding.</p>

<a id="be-a157"></a>

#### TCP, UDP and sockets in .NET

TCP: connection-oriented, ordered, retransmitted, congestion-controlled. UDP: fire and forget, no ordering, no delivery guarantee, no head-of-line blocking. Use TCP for almost everything; UDP for DNS, metrics (StatsD), video, game state, and QUIC underneath HTTP/3.

``` csharp
using var listener = new TcpListener(IPAddress.Any, 5000);
listener.Start();

while (!ct.IsCancellationRequested)
{
    var client = await listener.AcceptTcpClientAsync(ct);
    _ = HandleAsync(client, ct);                     // do not await; keep accepting
}

static async Task HandleAsync(TcpClient client, CancellationToken ct)
{
    using (client)
    {
        client.NoDelay = true;                        // disable Nagle for small messages
        await using var stream = client.GetStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                await stream.WriteAsync(buffer.AsMemory(0, n), ct);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
```

| Socket state         | Meaning                   | When it is a problem                        |
|----------------------|---------------------------|---------------------------------------------|
| `ESTABLISHED`        | Live connection           | Thousands to one host = pool misconfigured  |
| `TIME_WAIT`          | Closed, waiting ~2×MSL    | Port exhaustion from new-client-per-request |
| `CLOSE_WAIT`         | Peer closed, you have not | You are leaking sockets — missing Dispose   |
| `SYN_SENT` piling up | Handshake not completing  | Firewall dropping, or backlog full          |

> **\[Trap\]**
>
> **Common trap.** A read that returns 0 bytes means the peer closed cleanly. A stream read is not message-framed — TCP gives you a byte stream, so you must define your own framing (length prefix or delimiter) or you will get half a message.

###### What this looks like when it breaks

Port exhaustion, from first symptom to root cause, because the error message points at the wrong thing.

``` text
  symptom:   SocketException: "An attempt was made to access a socket in a way
             forbidden by its access permissions" (Windows, error 10013)
             or   "Cannot assign requested address" (Linux, EADDRNOTAVAIL)
             appears only under load; the app is fine after a restart for ~20 minutes

  what people check first:   firewall rules, permissions, antivirus.  All fine.

  actual cause:  outbound connections are being created faster than TIME_WAIT expires.

  arithmetic:    default ephemeral port range   ~16,000 ports (Windows) / 28,000 (Linux)
                 TIME_WAIT duration              ~120 s (Windows) / 60 s (Linux)
                 sustainable new-connection rate = ports / time_wait
                                                 = 16,000 / 120  ~= 133 per second

  the service was doing 400 outbound requests per second, each on a fresh connection.
```

``` text
# confirm it in seconds
ss -tan state time-wait | wc -l                    # Linux
netstat -ano | findstr TIME_WAIT | find /c /v ""   # Windows

# and see WHO the connections are to
ss -tan | awk '{print $5}' | cut -d: -f1 | sort | uniq -c | sort -rn | head
```

The fix is never to widen the port range or shorten TIME_WAIT — those are workarounds that trade one problem for another. The fix is connection reuse: `IHttpClientFactory` for HTTP, a shared singleton client for Redis and message brokers, and connection pooling for databases. TIME_WAIT exists to prevent packets from an old connection being delivered to a new one on the same port pair; shortening it invites data corruption under packet reordering.

###### Message framing, which TCP does not do for you

A TCP `Read` returns whatever bytes have arrived. It does not respect your message boundaries, because TCP has no concept of them. Sending three 100-byte messages may arrive as one 300-byte read, or as reads of 40, 210 and 50 bytes.

``` csharp
// length-prefixed framing, the standard solution
public static async Task WriteMessageAsync(Stream s, ReadOnlyMemory<byte> body, CancellationToken ct)
{
    var header = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
    await s.WriteAsync(header, ct);
    await s.WriteAsync(body, ct);
    await s.FlushAsync(ct);
}

public static async Task<byte[]> ReadMessageAsync(Stream s, CancellationToken ct)
{
    var header = new byte[4];
    await s.ReadExactlyAsync(header, ct);              // .NET 7+: loops until full or EOF
    var length = BinaryPrimitives.ReadInt32BigEndian(header);

    if (length is < 0 or > MaxMessageSize)             // ALWAYS bound this
        throw new InvalidDataException($"Frame length {length} out of range.");

    var body = new byte[length];
    await s.ReadExactlyAsync(body, ct);
    return body;
}
```

> **\[Trap\]**
>
> **Common trap.** Trusting the length prefix without an upper bound is a denial-of-service vulnerability: an attacker sends a header claiming two gigabytes and your server allocates it. Every custom protocol needs a maximum frame size, checked before allocation. This is also why `ReadAsync` returning fewer bytes than requested is normal and `ReadExactly` exists.

### How machines talk

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · How machines talk</p>

<a id="be-m14"></a>

#### TCP: reliability built on unreliability

TCP takes IP's "maybe it arrives" and turns it into an ordered, reliable byte stream. It does this by numbering every byte, acknowledging what arrived, and re-sending what did not.

``` text
Opening a connection - the three-way handshake:

  client ──── SYN, seq=x ─────────────►  server
  client ◄─── SYN-ACK, seq=y, ack=x+1 ─  server
  client ──── ACK, ack=y+1 ───────────►  server
  now established.

// That is ONE FULL ROUND TRIP before a single byte of your data moves.
// Same datacentre: ~0.5 ms. Cross-region: 50-150 ms. Every new connection.
// This is the entire justification for connection pooling
// (§262).
```

Once open, three mechanisms run continuously:

- **Retransmission.** Unacknowledged data is re-sent after a timeout derived from measured round-trip time. A lost packet therefore costs at least one RTT, often more.
- **Flow control.** The receiver advertises a window — how much it can currently buffer — so a fast sender cannot overwhelm a slow reader.
- **Congestion control.** The sender starts cautiously (*slow start*) and increases its rate until packets drop, then backs off. This is why a brand-new connection is slower than a warm one even after the handshake: it has not yet grown its window.

``` text
Closing is FOUR messages, and leaves state behind:

  FIN ──►   ◄── ACK   ◄── FIN   ACK ──►
  the side that closed first enters TIME_WAIT for ~60 seconds,
  holding its local port so a delayed duplicate cannot be misread.

// A service opening and closing many short connections accumulates thousands
// of TIME_WAIT entries and can exhaust its ephemeral ports
// (§19).
```

> **\[Trap\]**
>
> **Head-of-line blocking.** Because TCP guarantees order, one lost packet stalls *everything* behind it on that connection until it is retransmitted — even data that already arrived intact. On a multiplexed connection carrying many logical requests, one dropped packet delays all of them. That is a real limitation of HTTP/2 and the specific problem HTTP/3 was designed to fix ([HTTP/1.1, HTTP/2, and HTTP/3](06-06-http.md#be-a159)).

<a id="be-m15"></a>

#### UDP, and when unreliable is right

UDP adds almost nothing to IP: source port, destination port, length, checksum. No handshake, no ordering, no retransmission, no congestion control. A datagram is sent and may or may not arrive.

|                        | TCP                        | UDP                     |
|------------------------|----------------------------|-------------------------|
| Setup cost             | 1 RTT handshake            | none — send immediately |
| Ordering               | guaranteed                 | none                    |
| Loss handling          | retransmits                | your problem            |
| Head-of-line blocking  | yes                        | no                      |
| Per-message boundaries | none — it is a byte stream | preserved               |

**Unreliable is the right choice when late data is worthless.** In a voice call, a packet from 400 ms ago has no value — retransmitting it makes the call worse, not better. Same for live video, game state, and high-frequency metrics: send the next update rather than repair the last one.

``` text
Uses you meet in practice:
  DNS          one small question, one small answer - a handshake would double it
  QUIC/HTTP-3  reimplements reliability ON TOP of UDP, per-stream, so one
               lost packet does not stall unrelated streams
  Metrics      StatsD-style fire-and-forget: losing a data point is acceptable,
               blocking the application to deliver it is not
  Video, VoIP  latency matters more than completeness
```

> **\[Why it matters\]**
>
> **QUIC is the interesting case.** It is not "UDP because reliability is unwanted" — it wants reliability, but wants it *per stream* rather than per connection. TCP cannot offer that because it is implemented in the kernel and defines a single ordered stream. Building on UDP put the reliability logic in user space, where it could be redesigned. That is a good example of choosing a weaker foundation deliberately in order to build a different guarantee on top ([HTTP/1.1, HTTP/2, and HTTP/3](06-06-http.md#be-a159)).

<a id="be-m19"></a>

#### Sockets, ports, and connection state

A connection is identified by a **five-tuple**: protocol, source IP, source port, destination IP, destination port. Any of the five differing makes it a different connection.

``` text
  TCP  10.0.3.17:52814  ──►  10.0.9.4:1433     one connection to SQL Server
  TCP  10.0.3.17:52815  ──►  10.0.9.4:1433     a second, distinct connection
                └── the EPHEMERAL port, chosen by the OS

// The ephemeral range is roughly 28,000 ports (49152-65535 by default).
// That is the hard ceiling on simultaneous outbound connections
// from one machine to one destination.
```

**Ephemeral port exhaustion** is the failure this produces, and it is a favourite because it looks like the remote system is broken. Sockets in `TIME_WAIT` still hold their port for around 60 seconds after closing, so a service opening 1,000 short-lived connections per second accumulates ~60,000 held ports and then cannot connect at all.

``` text
# The diagnosis is one command:
netstat -an | grep TIME_WAIT | wc -l      # thousands = you have found it
ss -s                                      # summary by socket state

# Almost always the cause is one of:
#   new HttpClient() per request        -> use IHttpClientFactory
#   connections not pooled              -> enable pooling (§262)
#   a client library creating a channel per call
```

> **\[Trap\]**
>
> **A "connection" in a pool is not free while idle.** Each holds a socket, a file descriptor ([Files, handles, and the page cache](01-06-storage-and-files.md#be-m9)), kernel send and receive buffers, and on the server side a matching set. A pool of 100 connections across 20 service instances is 2,000 sockets on the database, each with its own memory. Database connection limits exist for this reason, and sizing pools per instance without considering the total is how you exhaust the server instead of the client.

> **\[Tip\]**
>
> **Listening sockets are different.** A server binds *one* port and accepts many connections on it — the five-tuple differs by client address and port, so there is no per-connection port cost on the accepting side. Outbound connections are where the scarcity lives, which is why the problem hits API clients and application servers rather than the services they call.

### How Networks Work

<p class="src">From <em>Backend Engineering</em> · Backend Platform · How Networks Work</p>

<p class="pd">What actually happens between your code calling a URL and bytes arriving. Addressing, routing, DNS and TCP — the layer most backend developers use daily and have never been taught.</p>

<a id="be-g637"></a>

#### TCP: the handshake, and what makes it slow

``` text
  connection establishment - three round trips before any data with TLS

  client                                server
    |------------- SYN ------------------>|
    |<---------- SYN-ACK -----------------|      1 RTT: TCP established
    |------------- ACK ------------------>|
    |------- TLS ClientHello ------------>|
    |<------ ServerHello, cert -----------|      1 RTT: TLS 1.3
    |------- Finished + REQUEST --------->|
    |<---------- response ----------------|      1 RTT: the actual request

  at 100ms RTT that is ~300ms before a single byte of your response.
  which is why connection REUSE (keep-alive, pooling) is the single
  biggest latency win available to a client.

  connection teardown
    FIN / ACK / FIN / ACK, and the initiator waits in TIME_WAIT (~60s)
    -> a server that closes many connections accumulates TIME_WAIT sockets
```

| Concept               | What it does                           | Why you care                                                                                  |
|-----------------------|----------------------------------------|-----------------------------------------------------------------------------------------------|
| **Sequence numbers**  | Order and deduplicate bytes            | TCP is a reliable ordered stream; you never see reordering                                    |
| **ACK**               | Confirms receipt                       | Unacknowledged data is retransmitted                                                          |
| **Receive window**    | “I can accept N more bytes”            | Flow control — a slow reader slows the sender. This is backpressure, in the protocol          |
| **Congestion window** | Sender's guess at network capacity     | Starts small and grows — “slow start”. New connections are slow for the first few round trips |
| **Nagle's algorithm** | Batches small writes                   | Adds latency to small messages; `TCP_NODELAY` disables it                                     |
| **Keep-alive**        | Periodic probes on an idle connection  | Detects a peer that vanished without closing                                                  |
| **TIME_WAIT**         | Post-close wait to catch stray packets | Accumulates on whichever side closes first                                                    |

``` text
# connection states, and counting them
ss -tan                                   # all TCP sockets and their state
ss -tan state established | wc -l
ss -tan state time-wait | wc -l           # thousands = you are closing a lot
ss -ltnp                                  # what is LISTENING, and which process

# the states worth recognising
#   LISTEN       waiting for connections
#   ESTABLISHED  active
#   SYN_SENT     we sent SYN, no reply -> firewall, or host down
#   TIME_WAIT    we closed, waiting out stray packets
#   CLOSE_WAIT   THE PEER closed and OUR APPLICATION HAS NOT.
#                a growing CLOSE_WAIT count is an application bug:
#                you are leaking sockets by not closing them.
```

> **\[Note\]**
>
> **The one number to internalise.** Round-trip time sets a floor no amount of server optimisation can cross. Same datacentre is under a millisecond; across a continent is 30–70 ms; intercontinental is 150–250 ms. A chatty protocol making ten sequential calls across the Atlantic costs two seconds in network alone. This is why batching, connection reuse and moving compute closer to data matter far more than micro-optimising the handler.

###### Port exhaustion, TIME_WAIT and connection reuse

``` text
  a source port is consumed per OUTBOUND connection, per destination.

    ephemeral range (Linux default)  32768-60999  = ~28,000 ports
    a connection is identified by the 4-tuple:
      (src IP, src port, dst IP, dst port)

  so the limit is ~28,000 concurrent connections TO ONE destination
  (ip:port), per source IP - not 28,000 total.

  but TIME_WAIT holds the port for ~60 seconds AFTER close.
  a service opening 500 short-lived connections per second to one
  upstream accumulates 30,000 sockets in TIME_WAIT and exhausts the range.

  symptom:  "cannot assign requested address" (EADDRNOTAVAIL)
            intermittent, under load only, and it looks like the
            upstream is failing when the problem is entirely local.
```

``` csharp
# diagnose
ss -tan state time-wait | wc -l
sysctl net.ipv4.ip_local_port_range        # widen if genuinely needed
cat /proc/sys/net/ipv4/tcp_tw_reuse        # 2 = reuse for outbound, safe

# the real fix is almost never kernel tuning - it is CONNECTION REUSE:
#   HTTP keep-alive, with a properly sized client pool
#   HTTP/2 multiplexing (one connection, many streams)
#   a database connection pool instead of connect-per-query
#
# .NET specifically: a new HttpClient per request is the classic version
# of this bug. Use IHttpClientFactory or a single long-lived instance;
# and set PooledConnectionLifetime so it still follows DNS changes.
```

| Setting                     | Effect                                               | Caution                                                                     |
|-----------------------------|------------------------------------------------------|-----------------------------------------------------------------------------|
| `tcp_tw_reuse=2`            | Reuse TIME_WAIT sockets for new outbound connections | Safe. This is the useful one                                                |
| `tcp_tw_recycle`            | —                                                    | **Removed from Linux** — it broke NAT clients badly. Old advice, do not use |
| Widen `ip_local_port_range` | More ephemeral ports                                 | Buys headroom; does not fix churn                                           |
| `somaxconn` / backlog       | Larger accept queue                                  | Raise if you see connection drops at accept                                 |
| Connection pooling          | Eliminates the churn                                 | **The actual fix**                                                          |

<a id="be-g638"></a>

#### UDP, QUIC and when TCP is the wrong choice

**TCP**

- Reliable, ordered, connection-oriented
- Retransmits lost segments
- Flow and congestion control built in
- Head-of-line blocking: one lost packet stalls everything behind it
- Handshake before any data

**UDP**

- Fire and forget: no ordering, no delivery guarantee
- No handshake — send immediately
- No congestion control unless you build it
- No head-of-line blocking — each datagram is independent
- The foundation for QUIC, DNS, and most real-time media

**QUIC** is the interesting development: it runs over UDP and reimplements reliability, ordering and congestion control in userspace, with TLS built in rather than layered on. HTTP/3 is HTTP over QUIC.

``` text
  why QUIC exists

  HTTP/2 over TCP        one TCP connection, many multiplexed streams
                         ONE lost packet stalls EVERY stream, because TCP
                         must deliver bytes in order -> head-of-line blocking
                         at the transport layer.

  HTTP/3 over QUIC       streams are independent AT THE TRANSPORT LAYER.
                         a lost packet stalls only its own stream.
                         plus: 1-RTT handshake (0-RTT on resumption),
                         and CONNECTION MIGRATION - a phone switching from
                         wifi to cellular keeps the same connection,
                         because the connection id is not tied to the IP.
```

| Use                                    | Protocol                                                         |
|----------------------------------------|------------------------------------------------------------------|
| Ordinary request/response APIs         | TCP — HTTP/1.1 or HTTP/2                                         |
| High-latency or lossy networks, mobile | QUIC / HTTP/3                                                    |
| DNS queries                            | UDP, falling back to TCP for large responses                     |
| Real-time voice and video              | UDP — a late packet is worthless, so retransmission is pointless |
| Metrics and logs at high volume        | UDP (statsd) where loss is acceptable                            |
| Anything where loss is unacceptable    | TCP, or QUIC                                                     |
