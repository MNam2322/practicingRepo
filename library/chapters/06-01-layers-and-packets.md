---
id: "6-1"
number: "6.1"
title: "Layers and packets"
part: 6
part_title: "Networks"
words: 5216
minutes: 23
---

# 6.1 Layers and packets

> **Quick look.** How data travels between machines as packets in layers of envelopes.
>
> - Data moves as packets, each with a header and a payload.
> - Four layers: link (local network), internet (routing), transport (program to program), application (HTTP).
> - Each layer wraps the data from the layer above in its own header.
> - Latency and bandwidth are separate limits; round trips usually matter more than bandwidth.
> - TCP turns packet loss into latency rather than errors; connection reuse is the biggest client-side lever.


### The big picture

Until now, everything in this guide happened inside one machine. Most real software talks to other machines: a browser to a web server, a service to its database, an app to a payment provider. This part of the guide follows data as it leaves one computer and arrives at another.

Networks work much like the postal system. Data is cut into small pieces called packets. Each packet travels in its own envelope, with an address on the front, and passes through several sorting offices on the way. This chapter explains packets, the layers of envelopes they travel in, and the two numbers that describe any network connection: how long a packet takes to arrive, and how much data can flow at once. Those two numbers explain more slow software than any other fact about networks.

### Before you start

**Builds on:** 4.1 Procedural, object-oriented and functional styles, 1.1 Bits and bytes, 2.8 Errors and exceptions.

**Ideas explained later:**

- **Browser** (9.1): A program that fetches web documents and turns them into interactive pages.
- **Server** (8.1): A program, or the machine running it, that waits for requests and answers them.
- **Database** (7.1): An organized collection of data stored so it can be read, changed and queried reliably.
- **TCP** (6.3): Transmission Control Protocol: a reliable, ordered byte stream between two endpoints.
- **UDP** (6.3): User Datagram Protocol: independent messages with ports and no delivery guarantees.
- **HTTP** (6.6): Hypertext Transfer Protocol: the request-and-response protocol of the web.
- **DNS** (6.4): Domain Name System: the distributed directory that turns names into addresses and other records.
- **TLS** (6.5): Transport Layer Security: the protocol that encrypts and authenticates connections.

**Tools and technologies named here:**

- **IP** (6.2): Internet Protocol: the protocol that addresses packets and routes them between networks.

**The code uses:** variables and var, methods and local functions, string interpolation, lambdas, LINQ, switch expressions and patterns, exceptions, using for cleanup, async and await. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why do networks send data as many small packets instead of one continuous stream?**

> So that many conversations can share the same cables, so that each piece can take whatever route is free, and so that a lost piece can be sent again on its own instead of restarting everything.

**2. What does encapsulation mean in networking?**

> Each layer wraps the data it receives from the layer above in its own header, like putting a letter in an envelope, then that envelope in a mailbag. The receiving side unwraps the layers in reverse order.

**3. Name the four layers of the internet model, from the bottom.**

> Link (the local network, such as Ethernet or Wi-Fi), internet (IP, moving packets between networks), transport (TCP and UDP, between programs) and application (HTTP, DNS and the rest).

**4. What is the difference between latency and bandwidth?**

> Latency is how long one piece of data takes to arrive. Bandwidth is how much data can arrive per second. A long, wide pipe has high latency and high bandwidth. More bandwidth doesn't reduce latency.

**5. Why can no network make a request from London to New York take less than about 55 ms?**

> Light in optical fibre travels at about 200,000 km per second. The distance is about 5,500 km, so a round trip of 11,000 km takes at least 55 ms, before any processing at all.

**6. A link is rated at 100 Mbps. Roughly how many megabytes per second can it move?**

> About 12.5. Network speeds are quoted in bits per second, and a byte is 8 bits. Protocol overhead reduces the real figure a little further.

### Sharing the wires

The earliest long-distance networks were telephone lines. A call reserved a complete path of wires from one phone to the other for as long as it lasted. That approach is called circuit switching. It is simple and predictable, and it wastes capacity: the reserved wires sit idle through every pause in the conversation.

Computer networks took a different approach, starting with the ARPANET in 1969, the ancestor of the internet. Data is cut into small pieces, and each piece travels on its own, sharing the cables with everyone else's pieces. This is **packet switching**. A cable is never reserved for one conversation. Pieces from thousands of conversations take turns on it, so it stays busy.

### Packets

A **packet** is one small unit of data sent over a network, typically up to about 1,500 bytes. It has two parts.

The **header** comes first. It is a small block of fixed-format information for the network: where the packet is going, where it came from, how long it is and a few other details. The **payload** follows. It is the actual data being carried, such as part of a web page.

Packets bring several advantages. Many conversations can share the same links. Each packet can take whichever route is working, so a broken cable doesn't stop the whole transfer. And if one packet is lost, only that packet has to be sent again.

They also bring problems that every networked program lives with. Packets can arrive late, out of order, twice or not at all. Something has to put them back together, which is the job of TCP, covered in TCP and UDP (6.3).

### Protocols

For two machines to understand each other, they must agree exactly on the format of every message and on what to do with it. That agreement is a **protocol**: a written set of rules for communicating. Protocols say which bytes go where in a header, what each value means, and how each side must respond.

Internet protocols are published as numbered documents called RFCs, short for "request for comments". IP, TCP, DNS, TLS and HTTP each have their own. Because they are public, any computer, from a phone to a satellite, can take part.

### Layers

Networking is split into layers, each with one job, each using the layer below it and serving the layer above. The internet is usually described with four.

The **link layer** moves data across one local network: the cable from your laptop to a switch, or the radio link from your phone to a Wi-Fi access point. Ethernet and Wi-Fi are link-layer protocols. Each network card has a hardware address called a **MAC address**, 48 bits usually written in hex, such as `3c:22:fb:4e:a1:07`. A unit of data at this layer is called a **frame**.

The **internet layer** moves packets between networks, from any machine to any other machine in the world, hop by hop through routers. The Internet Protocol, IP, works at this layer, and its addresses are covered in IP addresses and routing (6.2). IP makes no promises. A packet might arrive or might not. This is called best-effort delivery.

The **transport layer** moves data between programs rather than machines, and can add guarantees. TCP gives a reliable, ordered stream of bytes. UDP gives fast, unguaranteed single messages. Both are covered in TCP and UDP (6.3).

The **application layer** is where programs speak their own protocols on top: HTTP for the web, DNS for looking up names, SMTP for email and many more.

You may also meet the seven-layer OSI model, a reference model from the 1980s that splits some of these further. People still say "layer 4" for transport and "layer 7" for application, borrowing its numbers. For understanding the internet, the four layers are enough.

### Envelopes inside envelopes

Each layer adds its own header to whatever the layer above hands it. This wrapping is called **network encapsulation**, or just encapsulation when the meaning is clear. The same word means something different in object-oriented programming, as Procedural, object-oriented and functional styles (4.1) described; here it only means wrapping.

Follow a request for a web page as it leaves your machine. The browser writes an HTTP request, which is plain text. TCP puts that text into a segment with a 20-byte TCP header saying which program it's for and which part of the stream it is. IP puts the segment into a packet with a 20-byte IP header holding the source and destination addresses. Ethernet puts the packet into a frame with a 14-byte header holding hardware addresses, and a 4-byte check value at the end. The frame goes onto the wire as electrical signals, light or radio waves.

At the other end, each layer reads its own header, acts on it, removes it and passes the rest up. Routers in between only look as far as the IP header. They don't need to understand TCP or HTTP to deliver the packet.

The set of protocols a machine runs, stacked this way, is its **protocol stack**. Layering lets each part change independently. Wi-Fi replaced cables without changing TCP. HTTP/3 replaced TCP without changing HTML.

### The largest packet

Every link has a maximum packet size, the **MTU**, short for maximum transmission unit. On Ethernet and most of the internet, it is 1,500 bytes for the IP packet. After the IP and TCP headers take 40 bytes, 1,460 bytes are left for data in each packet.

A packet bigger than a link's MTU must be split into smaller ones, or dropped. Tunnels and VPNs add headers of their own, which lowers the effective MTU inside them. When a path contains a link with a smaller MTU and the signals that should report it are blocked, small messages work and large ones silently vanish. It is a classic cause of connections that hang only for big requests.

### Latency and bandwidth

Two numbers describe the performance of any network path.

**Latency** is how long it takes one piece of data to get from one end to the other. It is usually measured as the **round-trip time** (RTT): from sending a small packet to receiving the reply. Within one machine it is microseconds. Within a data centre it is a fraction of a millisecond. Across an ocean it is 70 to 150 milliseconds.

Latency has a hard floor: the speed of light. In optical fibre, light travels at about 200,000 km per second, two-thirds of its speed in a vacuum. London to New York is about 5,500 km, so one way takes at least 28 ms, and a round trip at least 55 ms. Real paths are longer than a straight line, and every router adds a little delay.

**Bandwidth** is how much data a path can carry per second. It is quoted in bits per second: a 100 Mbps line moves 100 million bits per second, which is 12.5 million bytes. Mixing up bits and bytes is an easy factor-of-8 mistake. The rate a transfer actually achieves, after overheads and sharing, is its **throughput**.

A useful picture is a water pipe. Latency is the pipe's length: how long a drop takes to travel through it. Bandwidth is its width: how much flows through per second. A wider pipe moves more water but doesn't make any single drop arrive sooner.

That matters for software, because many operations are not limited by bandwidth at all. A request that needs five round trips to a server 100 ms away takes at least half a second, however fast the connection is. Reducing the number of round trips is often the most effective network optimization there is.

```csharp
// packets.cs — run with: dotnet run packets.cs
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

// 1. Encapsulation by hand: wrap a message in a UDP header, then an IPv4 header
byte[] message = "BA117 boarding"u8.ToArray();                          // 14 bytes of application data
byte[] udp = new byte[8 + message.Length];                               // UDP adds an 8-byte header
BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(0), 50000);             // source port
BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(2), 9000);              // destination port
BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(4), (ushort)udp.Length);   // length of header plus data
message.CopyTo(udp, 8);                                                  // bytes 6-7, the checksum, stay 0: "not used"

byte[] ip = new byte[20 + udp.Length];                                   // IPv4 adds a 20-byte header
ip[0] = 0x45;                                                            // version 4; header is 5 × 4 = 20 bytes
BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(2), (ushort)ip.Length);  // total length
ip[8] = 64;                                                              // time to live: at most 64 router hops
ip[9] = 17;                                                              // protocol 17 means "the payload is UDP"
IPAddress.Parse("192.168.1.20").GetAddressBytes().CopyTo(ip, 12);        // source address
IPAddress.Parse("192.168.1.30").GetAddressBytes().CopyTo(ip, 16);        // destination address
BinaryPrimitives.WriteUInt16BigEndian(ip.AsSpan(10), Checksum(ip.AsSpan(0, 20)));
udp.CopyTo(ip, 20);

Console.WriteLine($"data {message.Length} bytes, UDP datagram {udp.Length}, IP packet {ip.Length}");   // data 14 bytes, UDP datagram 22, IP packet 42
Console.WriteLine(Convert.ToHexString(ip.AsSpan(0, 20)));                // 4500002A000000004011F740C0A80114C0A8011E
Console.WriteLine(Checksum(ip.AsSpan(0, 20)) == 0);                      // True: a correct header checks out to zero

// 2. Round-trip time to this machine and to a server on the internet
using var ping = new Ping();
foreach (string host in new[] { "127.0.0.1", "8.8.8.8" })
{
    try
    {
        PingReply reply = await ping.SendPingAsync(host, 2000);
        Console.WriteLine($"{host}: {reply.Status}, {reply.RoundtripTime} ms");
    }
    catch (PingException ex) { Console.WriteLine($"{host}: {ex.Message}"); }
}
// 127.0.0.1: Success, 0 ms   (it never leaves the machine)
// 8.8.8.8: Success, typically 5 to 50 ms, depending on where you are

// 3. The largest packet each network interface carries
foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
{
    try { Console.WriteLine($"{nic.Name}: {nic.NetworkInterfaceType}, MTU {nic.GetIPProperties().GetIPv4Properties().Mtu}"); }
    catch (NetworkInformationException) { Console.WriteLine($"{nic.Name}: no IPv4"); }
}

static ushort Checksum(ReadOnlySpan<byte> header)
{
    uint sum = 0;
    for (int i = 0; i < header.Length; i += 2)
        sum += BinaryPrimitives.ReadUInt16BigEndian(header[i..]);        // add the header up as 16-bit numbers
    while (sum > 0xFFFF) sum = (sum & 0xFFFF) + (sum >> 16);            // fold any overflow back into the low 16 bits
    return (ushort)~sum;                                                 // flip every bit of the result
}
```

The header bytes use big-endian order, network byte order from Bits and bytes (1.1). The checksum lets each router detect a header damaged in transit: adding up a correct header, including its checksum, always gives zero after the final flip.

### The hardware

A few kinds of device carry packets.

A **network interface card**, or NIC, connects a machine to a network. A laptop typically has a Wi-Fi one and perhaps an Ethernet one. A switch connects machines within one local network and forwards frames by MAC address. A router connects different networks and forwards packets by IP address. A home "router" is usually a router, a switch, a Wi-Fi access point and a firewall in one box.

Wi-Fi is a shared radio channel: devices take turns, and interference from neighbours and walls causes retransmissions. That makes Wi-Fi latency uneven in ways a cable isn't. Test anything latency-sensitive on a cable too.

### What the network doesn't promise

In 1994, Peter Deutsch at Sun Microsystems listed the false assumptions that programmers new to networks make. They became known as the **fallacies of distributed computing**. The network is reliable. Latency is zero. Bandwidth is infinite. The network is secure. The layout of the network doesn't change. There is one administrator. Moving data costs nothing. All networks are the same.

Every one of these fails in real systems, usually on a busy day. Each later chapter of this part shows how one of them is handled.

### In short

Networks move data as packets, each with a header for the network and a payload of real data. Protocols are the agreed rules both sides follow. Four layers split the work: link for the local network, internet for routing between networks, transport for program-to-program delivery, and application for protocols like HTTP. Each layer wraps the data from the layer above in its own header. Latency, set partly by the speed of light, and bandwidth are separate limits, and round trips usually matter more than bandwidth.

### What goes wrong

**Chatty protocols over long distances.** A page that makes 20 sequential calls to a server 80 ms away waits at least 1.6 seconds. Combine calls, run independent ones in parallel, or move the work closer to the data.

**Testing only on the same machine.** Code tested against `localhost` sees round trips of microseconds and never loses a packet. It behaves very differently across a real network. Test with realistic latency; tools can add artificial delay.

**Bits versus bytes.** A "1 Gbps" link moves at most about 125 MB per second, less after overhead. Capacity plans in the wrong unit are off by a factor of 8.

**Hidden MTU problems.** Behind a VPN or tunnel, small requests work while large uploads hang. Check the MTU along the path, and make sure the network doesn't block the messages that report size problems.

**Treating the network as reliable.** Code without timeouts hangs forever when a packet is lost at the wrong moment. Every network call needs a timeout, as Errors and exceptions (2.8) described.

### Cost model

| Path | Typical round-trip time |
|---|---:|
| Same machine | 10–50 µs |
| Same data centre | 0.2–0.5 ms |
| Same city or cloud region | 1–5 ms |
| Across a continent | 30–80 ms |
| Across an ocean | 70–150 ms |
| Geostationary satellite | about 600 ms |
| Low-orbit satellite | 25–60 ms |

A 1 Gbps link puts one 1,500-byte packet on the wire in 12 µs, so on a fast link the time is usually spent waiting for distance, not pushing bytes. Headers cost about 40 bytes per packet for TCP over IPv4, under 3% at full size and much more for tiny messages.

### In other languages (optional)

**JavaScript in the browser.** Pages can't open raw network connections; they can only use HTTP, WebSockets and WebRTC, which the browser controls for security. Node.js offers raw TCP through `net` and UDP through `dgram`.

**Python.** The `socket` module wraps the operating system's networking directly, and the Scapy library can build and send packets header by header.

**Every language.** A packet capture tool such as Wireshark, which records and decodes the packets passing through a network interface, works the same whatever language produced the traffic, and is the best way to see this chapter's layers for real.

### Key terms

- **Packet switching**: Sending data as independent pieces that share links, instead of reserving a path for each conversation.
- **Packet**: One small unit of data sent over a network, with a header and a payload.
- **Header**: Fixed-format information at the front of a packet or message, used by the network or protocol.
- **Payload**: The actual data a packet carries, after its header.
- **Protocol**: An agreed set of rules for the format and meaning of messages between machines.
- **Link layer**: The layer that moves frames across one local network, such as Ethernet or Wi-Fi.
- **MAC address**: The 48-bit hardware address of a network interface, used within a local network.
- **Frame**: A unit of data at the link layer.
- **Internet layer**: The layer that moves packets between networks using IP addresses and routers.
- **Transport layer**: The layer that moves data between programs, adding guarantees such as ordering and reliability.
- **Application layer**: The layer where programs speak their own protocols, such as HTTP and DNS.
- **Network encapsulation**: Each layer wrapping the data from the layer above in its own header.
- **Protocol stack**: The set of protocols a machine runs, layered on top of each other.
- **MTU**: Maximum transmission unit: the largest packet a link can carry, usually 1,500 bytes.
- **Latency**: How long one piece of data takes to travel from one end to the other.
- **Round-trip time**: The time from sending a small packet to receiving the reply. Often shortened to RTT.
- **Bandwidth**: How much data a path can carry per second, usually quoted in bits per second.
- **Throughput**: The rate a transfer actually achieves after overheads and sharing.
- **Network interface card**: The hardware that connects a machine to a network. Often shortened to NIC.
- **Fallacies of distributed computing**: Eight false assumptions about networks, such as "the network is reliable" and "latency is zero".

### Where this leads

How packets find their way between networks is in IP addresses and routing (6.2). Reliable streams built from unreliable packets are in TCP and UDP (6.3). Turning names into addresses is in DNS (6.4). Keeping packets private on the way is in TLS (6.5), and the protocol most software speaks on top of all this is in HTTP (6.6).

### Check yourself

1. Change the message in `packets.cs` to 30 bytes and predict the three lengths and whether the checksum still checks out, before running it.
2. Change one byte of the address in the finished header and run `Checksum` on it again. Explain why it no longer gives zero.
3. Ping a server on another continent. Work out from the distance how much of the round-trip time the speed of light alone explains.
4. A file of 500 MB is sent over a 200 Mbps link. Estimate the transfer time, then list what would make it slower in practice.
5. Install Wireshark, capture while you load a web page, and find one packet's Ethernet, IP and TCP headers.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A chatty API across the ocean

A Singapore app calls a US API ten times per screen. Each call is fast on the server but the screen takes 2.5 seconds.

**What goes wrong.** Round-trip time is about 230 ms, and ten sequential round trips cost 2.3 seconds regardless of bandwidth.

*One request shaped for the screen*

```csharp
// Before: GET /flights/{id}, GET /seats/{id}, GET /meals/{id}, ... (10 calls)
app.MapGet("/screens/booking/{id}", async (Guid id, ScreenQueries q, CancellationToken ct) =>
    Results.Ok(await q.BookingScreenAsync(id, ct)));   // the server fans out internally, close to the data
```

> **Lesson:** Latency, not bandwidth, usually dominates. Reduce round trips over long distances.

#### Example 2: Fast on localhost, slow everywhere else

Load tests on the developer's machine pass, but staging is 20× slower.

*Add realistic latency to local tests*

```bash
# Linux: add 80 ms delay to the loopback interface for a test run
sudo tc qdisc add dev lo root netem delay 80ms
dotnet test --filter Category=Latency
sudo tc qdisc del dev lo root
```

> **Lesson:** Localhost hides the network. Test with realistic latency before trusting performance numbers.

### Ground Floor

<p class="src">From <em>Backend Engineering</em> · APIs · Ground Floor</p>

<a id="be-e490"></a>

#### What a network actually is

Underneath everything, a network is a way of turning bits into a physical signal, sending it somewhere, and turning it back into bits — voltage on copper, light in fibre, radio in the air. That is genuinely all the wire does.

Everything above it exists to solve problems that raw signalling does not:

| Problem                                                    | Solved by                                                                    |
|------------------------------------------------------------|------------------------------------------------------------------------------|
| Which machine on this cable is this for?                   | MAC addresses and frames — the link layer                                    |
| How do I reach a machine on a *different* network?         | IP addresses and routing ([IP: addressing and routing](06-02-ip-addresses-and-routing.md#be-m13))             |
| What if it arrives corrupted, out of order, or not at all? | TCP ([TCP: reliability built on unreliability](06-03-tcp-and-udp.md#be-m14))                     |
| Which program on that machine?                             | port numbers ([Sockets, ports, and connection state](06-03-tcp-and-udp.md#be-m19))               |
| What is the address for `db.internal`?                     | DNS ([DNS: the lookup you never see](06-04-dns.md#be-m16))                               |
| How do I know nobody read or altered it?                   | TLS ([TLS: what the handshake establishes](06-05-tls.md#be-m17))                         |
| What do the bytes *mean*?                                  | HTTP, the SQL wire protocol, gRPC ([HTTP/1.1, HTTP/2, and HTTP/3](06-06-http.md#be-a159)) |

Data travels in discrete chunks — **packets** — not as a stream. Each carries a payload plus enough addressing to be delivered independently, and each layer wraps the one above it:

``` text
┌─ Ethernet frame ──────────────────────────────────────────────┐
│ dst MAC │ src MAC │                                            │
│         ┌─ IP packet ─────────────────────────────────────┐   │
│         │ src IP │ dst IP │ TTL │                          │   │
│         │        ┌─ TCP segment ───────────────────────┐   │   │
│         │        │ src port │ dst port │ seq │ ack │    │   │   │
│         │        │          ┌─ TLS record ──────────┐  │   │   │
│         │        │          │  encrypted:           │  │   │   │
│         │        │          │  ┌─ HTTP request ──┐  │  │   │   │
│         │        │          │  │ GET /orders ... │  │  │   │   │
│         │        │          │  └─────────────────┘  │  │   │   │
│         │        │          └───────────────────────┘  │   │   │
│         │        └─────────────────────────────────────┘   │   │
│         └────────────────────────────────────────────────  ┘   │
└───────────────────────────────────────────────────────────────┘
 ~1500 bytes total on a typical link. Anything bigger is split up.
```

> **\[Why it matters\]**
>
> **Headers are overhead you pay per packet.** Roughly 40–60 bytes of IP and TCP headers ride on every packet regardless of payload. Sending 100 bytes of useful data costs about 160 bytes on the wire; sending 1,400 costs about 1,460. This is one reason chatty protocols lose so badly to batched ones — and why [`Rules of thumb, ranked`](12-04-scaling-and-performance.md#be-m276) ranks removing round trips first.

### Networking

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Networking</p>

<p class="pd">The wire. Most production incidents that look like application bugs are really DNS, TLS, timeouts, connection pools or a proxy in the middle — so this is the layer worth genuinely understanding.</p>

<a id="be-a156"></a>

#### The network stack, as far as it concerns you

``` text
  layer            what lives there              where you meet it in .NET
  -----------------------------------------------------------------------------
  application      HTTP, gRPC, AMQP, SMTP        HttpClient, Kestrel, brokers
  security         TLS 1.2 / 1.3                 certificates, SslStream, mTLS
  transport        TCP (reliable), UDP (not)     Socket, ports, connection pools
  network          IP, routing, NAT              IP addresses, CIDR, subnets
  link             Ethernet, wifi                MTU, the thing you never touch
```

Two facts explain most behaviour. First, TCP guarantees ordered, reliable delivery by retransmitting — so packet loss shows up as *latency*, not errors. Second, every connection costs a handshake, so connection reuse is the single biggest lever on client performance.

##### Latency budget

| Hop                    | Rough cost                      |
|------------------------|---------------------------------|
| Same host (loopback)   | \< 0.1 ms                       |
| Same datacentre / AZ   | 0.2–1 ms                        |
| Cross-AZ in a region   | 1–3 ms                          |
| Cross-region (EU ↔ US) | 70–120 ms                       |
| TLS 1.2 full handshake | 2 round trips on top of TCP's 1 |

> **\[Why it matters\]** A chatty service making 50 sequential cross-region calls costs 5 seconds in pure network time before a single line of your code runs. Batching and parallelism are not micro-optimisations at that scale.

###### The errors, mapped to layers, and what each tells you

``` text
  the mapping worth memorising, because it tells you WHO TO ASK

  "Name or service not known"          DNS. Nothing was attempted.
  SocketException: ConnectionRefused   the packet ARRIVED, nothing listening.
                                       -> network path is FINE. Service down,
                                          wrong port, or bound to 127.0.0.1
  SocketException: TimedOut            the packet VANISHED.
                                       -> firewall, security group, NSG,
                                          silently dropping. Ask the network team.
  AuthenticationException (TLS)        TCP connected; the handshake failed.
                                       -> certificate, chain, SNI, or protocol
  TaskCanceledException on HttpClient  YOUR timeout fired. Not the remote's fault.
  HttpRequestException 502/503/504     a PROXY is telling you about upstream.
                                       -> the failure is behind the proxy

  refused vs timed out is the single most useful distinction in the list:
  one means "reached and rejected", the other means "never arrived".
  Different teams, different fix, and people conflate them constantly.
```

``` csharp
// distinguishing them in code, because the handling differs
catch (HttpRequestException ex) when (ex.InnerException is SocketException se)
{
    var transient = se.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => true,   // service restarting - retry
        SocketError.TimedOut          => true,   // maybe transient - retry, carefully
        SocketError.HostNotFound      => false,  // DNS wrong - do NOT retry
        SocketError.AddressNotAvailable => false,// PORT EXHAUSTION - your bug
        _ => false
    };
    if (!transient) throw;   // retrying a configuration error just wastes time
}
```

``` text
# the .NET-specific diagnostics
dotnet-counters monitor -p <pid> System.Net.Http System.Net.Sockets
#   requests-started / requests-failed / current-requests
#   connections-established         <- climbing without bound = handler churn
#   bytes-sent / bytes-received

# and the connection state from the OS, which is authoritative
ss -tan state established | grep :5432 | wc -l
ss -tan state time-wait | wc -l          # thousands = short-lived connections
ss -tan state close-wait | wc -l         # growing = YOU are not closing sockets
```

`CLOSE_WAIT` deserves a note because it is unambiguous: it means the *peer* closed the connection and your application has not. A growing `CLOSE_WAIT` count is always a leak in your code — a stream, a client or a response not disposed — and it is one of the very few network symptoms with a single possible cause.

### How Networks Work

<p class="src">From <em>Backend Engineering</em> · Backend Platform · How Networks Work</p>

<p class="pd">What actually happens between your code calling a URL and bytes arriving. Addressing, routing, DNS and TCP — the layer most backend developers use daily and have never been taught.</p>

<a id="be-g633"></a>

#### The layers, as they actually exist

The seven-layer OSI model is taught in every course and describes almost nothing you will meet. The four-layer TCP/IP model is what the internet actually runs on, and the mapping is worth knowing because error messages come from specific layers.

``` text
  what you send                         what wraps it

  APPLICATION   your JSON               HTTP headers + body
     |                                  "GET /orders HTTP/1.1 ..."
  TRANSPORT     TCP segment             + source port, dest port, seq, ack
     |                                  ports identify the PROCESS
  INTERNET      IP packet               + source IP, dest IP, TTL
     |                                  IPs identify the HOST
  LINK          Ethernet frame          + source MAC, dest MAC
                                        MACs identify the NIC on this segment

  each layer adds a header and treats everything above it as opaque payload.
  this is why a load balancer can route on IP+port (L4) without ever
  parsing your HTTP, and why one that DOES parse HTTP is called L7.
```

| Symptom                     | Layer             | Meaning                                                       |
|-----------------------------|-------------------|---------------------------------------------------------------|
| `No route to host`          | Internet (L3)     | Routing table has nowhere to send it                          |
| `Connection refused`        | Transport (L4)    | Host reached, nothing listening on that port — you got an RST |
| `Connection timed out`      | Transport (L4)    | No response at all — usually a firewall silently dropping     |
| `Name or service not known` | Application       | DNS resolution failed before any connection was attempted     |
| `SSL handshake failed`      | Between L4 and L7 | TCP connected; TLS negotiation failed                         |
| `502 Bad Gateway`           | Application (L7)  | A proxy reached you but your upstream failed                  |
| `504 Gateway Timeout`       | Application (L7)  | A proxy waited for your upstream and gave up                  |

> **\[Why it matters\]** The distinction between *refused* and *timed out* is the single most useful diagnostic in networking. Refused means the packet arrived and something actively said no — the network path works, the service is not listening. Timed out means the packet vanished — almost always a firewall, a security group, or the wrong address entirely. These two errors point at completely different teams.
